using Microsoft.Extensions.Options;
using Npgsql;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ElektrometerService
{
    /// <summary>
    /// Background service that polls the electrometer API and stores data into PostgreSQL
    /// via fn_t_data_fv_ex_ins_upd, then logs the result code for specific sensors and writes to file.
    /// </summary>
    public sealed class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly HttpClient _httpClient;
        private readonly ElectrometerSettings _settings;
        private readonly string _connectionString;
        private readonly string _logFilePath;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public Worker(
            ILogger<Worker> logger,
            IOptions<ElectrometerSettings> settings,
            IOptions<DatabaseOptions> dbOptions,
            IOptions<DataFvExSettings> dataSettings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _connectionString = dbOptions?.Value?.ConnectionString
                ?? throw new ArgumentNullException(nameof(dbOptions));
            _logFilePath = dataSettings?.Value?.LogFilePath
                ?? throw new ArgumentNullException(nameof(dataSettings));

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15)
            };
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_settings.BaseUrl),
                Timeout = TimeSpan.FromSeconds(100)
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Electrometer Service started at {Time}", DateTimeOffset.UtcNow);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessCycleAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during fetch-insert cycle.");
                }

                await Task.Delay(TimeSpan.FromMinutes(_settings.FetchIntervalMinutes), stoppingToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task ProcessCycleAsync(CancellationToken cancellationToken)
        {
            var token = await AcquireTokenAsync(cancellationToken).ConfigureAwait(false);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            var liveApi = await FetchAsync<ApiResponse<LiveData>>(
                $"api/report/energyStorage/getLastPowerData?sysSn={_settings.SysSn}&stationId={_settings.StationId}",
                cancellationToken).ConfigureAwait(false);

            var dayApi = await FetchAsync<ApiResponse<DailyData>>(
                $"api/report/power/staticsByDay?sysSn={_settings.SysSn}&date={today}",
                cancellationToken).ConfigureAwait(false);

            var statsApi = await FetchAsync<ApiResponse<StatsData>>(
                $"api/report/energy/getEnergyStatistics?sysSn={_settings.SysSn}&stationId=&beginDate={today}&endDate={today}",
                cancellationToken).ConfigureAwait(false);

            var output = new ElectrometerOutput
            {
                CentralId = _settings.CentralId,
                Timestamp = DateTime.Now, //DateTime.UtcNow
                Generation = (liveApi?.Data?.Ppv ?? 0) / 1000.0,
                Consumption = (liveApi?.Data?.Pload ?? 0) / 1000.0,
                BatterySOC = liveApi?.Data?.Soc ?? 0,
                GridConsumption = (liveApi?.Data?.Pgrid ?? 0) / 1000.0,
                BatteryPower = (liveApi?.Data?.Pbat ?? 0) / 1000.0,
                HasChargingPile = liveApi?.Data?.HasChargingPile ?? false,

                EpvToday = dayApi?.Data?.EpvToday ?? 0,
                EfeedIn = dayApi?.Data?.EfeedIn ?? 0,
                EhomeLoad = dayApi?.Data?.EhomeLoad ?? 0,
                Echarge = dayApi?.Data?.Echarge ?? 0,
                Ebat = dayApi?.Data?.Ebat ?? 0,
                EgridCharge = dayApi?.Data?.EgridCharge ?? 0,
                Einput = dayApi?.Data?.Einput ?? 0,
                EloadRaw = dayApi?.Data?.EloadRaw ?? 0,
                EchargingPile = dayApi?.Data?.EchargingPile ?? 0,
                Ediesel = dayApi?.Data?.Ediesel ?? 0,

                EselfConsumption = statsApi?.Data?.EselfConsumption ?? 0,
                EselfSufficiency = statsApi?.Data?.EselfSufficiency ?? 0,
                Edischarge = statsApi?.Data?.Edischarge ?? 0,
                HasGenerator = statsApi?.Data?.HasGenerator ?? false
            };

            await InsertAsync(output, cancellationToken).ConfigureAwait(false);
        }

        private async Task<T> FetchAsync<T>(string requestUri, CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result ?? throw new JsonException($"Deserialization returned null for {requestUri}");
        }

        private async Task<string> AcquireTokenAsync(CancellationToken cancellationToken)
        {
            var payload = new { username = _settings.Username, password = _settings.Password };
            using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_settings.LoginUrl, content, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var auth = await JsonSerializer.DeserializeAsync<ApiResponse<AuthData>>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return auth?.Data?.Token ?? throw new InvalidOperationException("Authentication token is missing in response.");
        }

        private async Task InsertAsync(ElectrometerOutput data, CancellationToken cancellationToken)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            var ts = DateTime.SpecifyKind(data.Timestamp, DateTimeKind.Unspecified);
            const string sql = @"
SELECT edison.fn_t_data_fv_ex_ins_upd(
    $1::bigint,
    $2::timestamp without time zone,
    $3::numeric,
    $4::numeric,
    $5::numeric,
    $6::numeric,
    $7::numeric,
    $8::numeric,
    $9::numeric,
    $10::numeric,
    $11::numeric,
    $12::numeric,
    $13::numeric,
    $14::numeric,
    $15::numeric,
    $16::numeric,
    $17::numeric,
    $18::numeric,
    $19::numeric,
    $20::numeric,
    $21::boolean,
    $22::boolean
);";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.Add(new NpgsqlParameter { Value = (long)_settings.SensorId });
            cmd.Parameters.Add(new NpgsqlParameter { Value = ts });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.Generation });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.Consumption });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.BatterySOC });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.GridConsumption });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.BatteryPower });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EpvToday });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EfeedIn });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EhomeLoad });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.Echarge });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.Ebat });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EgridCharge });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.Einput });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EloadRaw });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EchargingPile });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.Ediesel });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EselfConsumption });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.EselfSufficiency });
            cmd.Parameters.Add(new NpgsqlParameter { Value = (decimal)data.Edischarge });
            cmd.Parameters.Add(new NpgsqlParameter { Value = data.HasGenerator });
            cmd.Parameters.Add(new NpgsqlParameter { Value = data.HasChargingPile });

            var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var returnCode = raw is short s ? s : Convert.ToInt16(raw);

            if (returnCode == 1 || returnCode == 2 || returnCode == -1)
            {
                _logger.LogInformation(
                    "fn_t_data_fv_ex_ins_upd returned {ReturnCode} for sensor {SensorId}",
                    returnCode, _settings.SensorId);
                try
                {
                    await File.AppendAllTextAsync(_logFilePath,
                        $"{DateTime.UtcNow:O} - fn_t_data_fv_ex_ins_upd returned {returnCode} for sensor {_settings.SensorId}{Environment.NewLine}",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write return code to log file");
                }
            }
        }
    }
}
