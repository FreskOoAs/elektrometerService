using System.Text.Json.Serialization;

namespace ElektrometerService
{
    /// <summary>
    /// Generic wrapper for API responses with varying data shapes.
    /// </summary>
    /// <typeparam name="T">Type of the Data payload.</typeparam>
    public class ApiResponse<T>
    {
        public int Code { get; set; }
        public string Msg { get; set; } = string.Empty;
        public T Data { get; set; } = default!;
    }

    /// <summary>
    /// Instantaneous/live power data from getLastPowerData endpoint.
    /// </summary>
    public class LiveData
    {
        public double Ppv { get; set; }
        public double Pload { get; set; }
        public double Soc { get; set; }
        public double Pgrid { get; set; }
        public double Pbat { get; set; }
        public double Pev { get; set; }
        public bool? HasChargingPile { get; set; }
    }

    /// <summary>
    /// Daily energy totals (and per-interval arrays) from staticsByDay endpoint.
    /// </summary>
    public class DailyData
    {
        [JsonPropertyName("epvtoday")]
        public double EpvToday { get; set; }

        [JsonPropertyName("efeedIn")]
        public double EfeedIn { get; set; }

        [JsonPropertyName("ehomeload")]
        public double EhomeLoad { get; set; }

        [JsonPropertyName("echarge")]
        public double Echarge { get; set; }

        [JsonPropertyName("ebat")]
        public double Ebat { get; set; }

        [JsonPropertyName("egridCharge")]
        public double EgridCharge { get; set; }

        [JsonPropertyName("einput")]
        public double Einput { get; set; }

        [JsonPropertyName("eloadRaw")]
        public double EloadRaw { get; set; }

        [JsonPropertyName("echargingpile")]
        public double EchargingPile { get; set; }

        [JsonPropertyName("ediesel")]
        public double Ediesel { get; set; }

        [JsonPropertyName("cbat")]
        public double[] Cbat { get; set; } = Array.Empty<double>();

        [JsonPropertyName("homePower")]
        public double[] HomePower { get; set; } = Array.Empty<double>();

        [JsonPropertyName("feedIn")]
        public double[] FeedIn { get; set; } = Array.Empty<double>();

        [JsonPropertyName("gridCharge")]
        public double[] GridCharge { get; set; } = Array.Empty<double>();

        [JsonPropertyName("ppv")]
        public double[] PpvArray { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// Self-consumption and generator stats from getEnergyStatistics endpoint.
    /// </summary>
    public class StatsData
    {
        [JsonPropertyName("epvT")]
        public double EpvT { get; set; }             // daily PV gen

        [JsonPropertyName("eout")]
        public double Eout { get; set; }             // daily feed-in

        [JsonPropertyName("echarge")]
        public double Echarge { get; set; }          // battery charged

        [JsonPropertyName("epv2load")]
        public double Epv2Load { get; set; }         // PV → load

        [JsonPropertyName("epvcharge")]
        public double EpvCharge { get; set; }        // PV → battery

        [JsonPropertyName("eeff")]
        public double Eeff { get; set; }             // efficiency

        [JsonPropertyName("eload")]
        public double Eload { get; set; }            // home consumption

        [JsonPropertyName("eselfConsumption")]
        public double EselfConsumption { get; set; }

        [JsonPropertyName("eselfSufficiency")]
        public double EselfSufficiency { get; set; }

        [JsonPropertyName("einput")]
        public double Einput { get; set; }           // grid → load

        [JsonPropertyName("ebat")]
        public double Ebat { get; set; }             // battery throughput

        [JsonPropertyName("eloadPercentage")]
        public double EloadPercentage { get; set; }

        [JsonPropertyName("soc")]
        public double Soc { get; set; }              // state of charge

        [JsonPropertyName("hasGenerator")]
        public int HasGeneratorFlag { get; set; }    // 0 or 1

        [JsonIgnore]
        public bool HasGenerator => HasGeneratorFlag == 1;

        [JsonPropertyName("hasChargingPile")]
        public bool HasChargingPile { get; set; }

        [JsonPropertyName("eloadRaw")]
        public double EloadRaw { get; set; }

        [JsonPropertyName("epv2loadRaw")]
        public double? Epv2LoadRaw { get; set; }

        [JsonPropertyName("egriddischarge")]
        public double EgridDischarge { get; set; }

        [JsonPropertyName("edischarge")]
        public double Edischarge { get; set; }

        [JsonPropertyName("batLoad")]
        public double BatLoad { get; set; }

        [JsonPropertyName("echargingPile")]
        public double EchargingPile { get; set; }

        [JsonPropertyName("egridCharge")]
        public double EgridCharge { get; set; }

        [JsonPropertyName("echargingPileRaw")]
        public double EchargingPileRaw { get; set; }

        [JsonPropertyName("ehomeLoad")]
        public double EhomeLoad { get; set; }

        [JsonPropertyName("egrid2Load")]
        public double Egrid2Load { get; set; }

        [JsonPropertyName("ediesel")]
        public double Ediesel { get; set; }
    }
}
public class AuthData
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}