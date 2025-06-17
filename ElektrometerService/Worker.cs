using Microsoft.Extensions.Options;
using Npgsql;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ElektrometerService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private readonly HttpClient httpClient;
        private readonly ElectrometerSettings settings;
        private readonly OutputSettings outputSettings;
        private readonly string dbConnectionString;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private DateTime tokenExpiration = DateTime.UtcNow;
        private string token = string.Empty;
        private Timer? timer;

        public Worker(
            ILogger<Worker> logger,
            IOptions<ElectrometerSettings> config,
            IOptions<OutputSettings> outputConfig,
            IOptions<DatabaseOptions> dbOptions)
        {
            this.logger = logger;
            settings = config.Value;
            outputSettings = outputConfig.Value;
            dbConnectionString = dbOptions.Value.ConnectionString;

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15)
            };
            httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(settings.BaseUrl)
            };
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Electrometer Service Started.");
            timer = new Timer(async _ => await FetchDataAsync(),
                              null,
                              TimeSpan.Zero,
                              TimeSpan.FromMinutes(settings.FetchIntervalMinutes));
            return Task.CompletedTask;
        }

        private async Task FetchDataAsync()
        {
            if (!await semaphore.WaitAsync(0))
            {
                logger.LogWarning("Previous execution still running. Skipping.");
                return;
            }

            try
            {
                if (DateTime.UtcNow >= tokenExpiration)
                {
                    logger.LogInformation("Token expired. Logging in...");
                    await LoginAsync();
                }
                await FetchElectrometerDataAsync();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task FetchElectrometerDataAsync()
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // 1) Live power data
            var liveUrl =
                $"api/report/energyStorage/getLastPowerData?sysSn={settings.SysSn}&stationId={settings.StationId}";
            logger.LogInformation("Calling Live URL: " + new Uri(httpClient.BaseAddress!, liveUrl));
            using var liveResp = await httpClient.GetAsync(liveUrl);
            liveResp.EnsureSuccessStatusCode();
            var liveApi = await JsonSerializer.DeserializeAsync<ApiResponse<LiveData>>(
                await liveResp.Content.ReadAsStreamAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            // 2) Daily energy totals
            var dayUrl =
                $"api/report/power/staticsByDay?sysSn={settings.SysSn}&date={today}";
            logger.LogInformation("Calling Day URL: " + new Uri(httpClient.BaseAddress!, dayUrl));
            using var dayResp = await httpClient.GetAsync(dayUrl);
            dayResp.EnsureSuccessStatusCode();
            var dayApi = await JsonSerializer.DeserializeAsync<ApiResponse<DailyData>>(
                await dayResp.Content.ReadAsStreamAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            // 3) Self-consumption & generator stats
            var statsUrl =
                $"api/report/energy/getEnergyStatistics?sysSn={settings.SysSn}&stationId=&beginDate={today}&endDate={today}";
            logger.LogInformation("Calling Stats URL: " + new Uri(httpClient.BaseAddress!, statsUrl));
            using var statsResp = await httpClient.GetAsync(statsUrl);
            statsResp.EnsureSuccessStatusCode();
            var statsApi = await JsonSerializer.DeserializeAsync<ApiResponse<StatsData>>(
                await statsResp.Content.ReadAsStreamAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            var outData = new ElectrometerOutput
            {
                CentralId = settings.CentralId,
                Timestamp = DateTime.UtcNow,
                Generation = liveApi.Data.Ppv / 1000.0,
                Consumption = liveApi.Data.Pload / 1000.0,
                BatterySOC = liveApi.Data.Soc,
                GridConsumption = liveApi.Data.Pgrid / 1000.0,
                BatteryPower = liveApi.Data.Pbat / 1000.0,
                HasChargingPile = liveApi.Data.HasChargingPile,
                EpvToday = dayApi.Data.EpvToday,
                EfeedIn = dayApi.Data.EfeedIn,
                EhomeLoad = dayApi.Data.EhomeLoad,
                Echarge = dayApi.Data.Echarge,
                Ebat = dayApi.Data.Ebat,
                EgridCharge = dayApi.Data.EgridCharge,
                Einput = dayApi.Data.Einput,
                EloadRaw = dayApi.Data.EloadRaw,
                EchargingPile = dayApi.Data.EchargingPile,
                Ediesel = dayApi.Data.Ediesel,
                EselfConsumption = statsApi.Data.EselfConsumption,
                EselfSufficiency = statsApi.Data.EselfSufficiency,
                Edischarge = statsApi.Data.Edischarge,
                HasGenerator = statsApi.Data.HasGenerator
            };

            // Dump to JSON
            var fileName = $"{DateTime.Now:yyyy-MM-dd}_kveto.json";
            var filePath = Path.Combine(outputSettings.JsonFilePath, fileName);
            var json = JsonSerializer.Serialize(outData, new JsonSerializerOptions { WriteIndented = true });
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine);
            logger.LogInformation($"Wrote data snapshot to {filePath}");

            await InsertToDatabaseAsync(outData);
        }

        private async Task InsertToDatabaseAsync(ElectrometerOutput data)
        {
            await using var conn = new NpgsqlConnection(dbConnectionString);
            await conn.OpenAsync();

            // use hard-coded SensorId from settings
            var snimacId = settings.SensorId;

            // force the timestamp into UTC
            var utcTimestamp = DateTime.SpecifyKind(data.Timestamp, DateTimeKind.Utc);

            // define the 2019-01-01 epoch in UTC
            var epochUtc = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // compute whole hours between them
            int timestampZaokruhl = (int)((utcTimestamp - epochUtc).TotalHours);

            const string insertSql = @"
INSERT INTO edison.t_data_fv_ex (
    xdfe_sni_id,
    dfe_cas,
    dfe_cas_zaokruhl,
    dfe_generation_kw,
    dfe_consumption_kw,
    dfe_battery_soc_percent,
    dfe_grid_consumption_kw,
    dfe_battery_power_kw,
    dfe_epv_today_kwh,
    dfe_efeed_in_kwh,
    dfe_ehome_load_kwh,
    dfe_echarge_kwh,
    dfe_ebat_kwh,
    dfe_egrid_charge_kwh,
    dfe_einput_kwh,
    dfe_eload_raw_kwh,
    dfe_echarging_pile_kwh,
    dfe_ediesel_kwh,
    dfe_eself_consumption_kwh,
    dfe_eself_sufficiency_percent,
    dfe_edischarge_kwh,
    dfe_has_generator,
    dfe_has_charging_pile
) VALUES (
    @snimacid,
    @timestamp,
    @timestampzaokruhl,
    @generation_kw,
    @consumption_kw,
    @battery_soc_percent,
    @grid_consumption_kw,
    @battery_power_kw,
    @epv_today_kwh,
    @efeed_in_kwh,
    @ehome_load_kwh,
    @echarge_kwh,
    @ebat_kwh,
    @egrid_charge_kwh,
    @einput_kwh,
    @eload_raw_kwh,
    @echarging_pile_kwh,
    @ediesel_kwh,
    @eself_consumption_kwh,
    @eself_sufficiency_percent,
    @edischarge_kwh,
    @has_generator,
    @has_charging_pile
)ON CONFLICT (xdfe_sni_id, dfe_cas_zaokruhl) DO NOTHING;;";


            await using var cmdIns = new NpgsqlCommand(insertSql, conn);
            cmdIns.Parameters.AddWithValue("snimacid", snimacId);
            cmdIns.Parameters.AddWithValue("timestamp", data.Timestamp);
            cmdIns.Parameters.AddWithValue("timestampzaokruhl", timestampZaokruhl);

            // Live power (kW)
            cmdIns.Parameters.AddWithValue("generation_kw", data.Generation);
            cmdIns.Parameters.AddWithValue("consumption_kw", data.Consumption);
            cmdIns.Parameters.AddWithValue("battery_soc_percent", data.BatterySOC);
            cmdIns.Parameters.AddWithValue("grid_consumption_kw", data.GridConsumption);
            cmdIns.Parameters.AddWithValue("battery_power_kw", data.BatteryPower);

            // Daily totals (kWh)
            cmdIns.Parameters.AddWithValue("epv_today_kwh", data.EpvToday);
            cmdIns.Parameters.AddWithValue("efeed_in_kwh", data.EfeedIn);
            cmdIns.Parameters.AddWithValue("ehome_load_kwh", data.EhomeLoad);
            cmdIns.Parameters.AddWithValue("echarge_kwh", data.Echarge);
            cmdIns.Parameters.AddWithValue("ebat_kwh", data.Ebat);
            cmdIns.Parameters.AddWithValue("egrid_charge_kwh", data.EgridCharge);
            cmdIns.Parameters.AddWithValue("einput_kwh", data.Einput);
            cmdIns.Parameters.AddWithValue("eload_raw_kwh", data.EloadRaw);
            cmdIns.Parameters.AddWithValue("echarging_pile_kwh", data.EchargingPile);
            cmdIns.Parameters.AddWithValue("ediesel_kwh", data.Ediesel);

            // Self-consumption & stats
            cmdIns.Parameters.AddWithValue("eself_consumption_kwh", data.EselfConsumption);
            cmdIns.Parameters.AddWithValue("eself_sufficiency_percent", data.EselfSufficiency);
            cmdIns.Parameters.AddWithValue("edischarge_kwh", data.Edischarge);
            cmdIns.Parameters.AddWithValue("has_generator", data.HasGenerator);
            cmdIns.Parameters.AddWithValue("has_charging_pile", data.HasChargingPile ?? (object)DBNull.Value);

            await cmdIns.ExecuteNonQueryAsync();
            logger.LogInformation($"Inserted snapshot into t_data_fv_ex under sensor {snimacId}.");
        }


        private async Task LoginAsync()
        {
            try
            {
                var loginPayload = new
                {
                    username = settings.Username,
                    password = settings.Password
                };

                using var content = new StringContent(
                    JsonSerializer.Serialize(loginPayload),
                    Encoding.UTF8,
                    "application/json"
                );
                using var response = await httpClient.PostAsync(settings.LoginUrl, content);
                await using var responseStream = await response.Content.ReadAsStreamAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError($"Login failed: {response.StatusCode}");
                    return;
                }

                var authResponse = await JsonSerializer.DeserializeAsync<ApiResponse<AuthData>>(
                    responseStream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
);

                if (authResponse?.Data?.Token != null)
                {
                    token = authResponse.Data.Token;
                    tokenExpiration = DateTime.UtcNow.AddMinutes(60);
                    logger.LogInformation("Login successful. Token acquired.");
                }
                else
                {
                    logger.LogError("Login response did not contain a token.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error logging in: {ex.Message}");
            }
        }
    }
}
