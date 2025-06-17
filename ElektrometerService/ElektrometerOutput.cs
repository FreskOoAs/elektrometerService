namespace ElektrometerService
{
    /// <summary>
    /// Output model representing both instantaneous and daily-energy statistics,
    /// serialized to JSON and stored in database.
    /// </summary>
    public class ElectrometerOutput
    {
        // --- Identifiers & timestamps ---
        public string CentralId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        // --- Instantaneous power fields (converted to kW) ---
        public double Generation { get; set; }       // kW (instant PV)
        public double Consumption { get; set; }      // kW (instant home load)
        public double BatterySOC { get; set; }       // %  (state of charge)
        public double GridConsumption { get; set; }  // kW (instant grid)
        public double BatteryPower { get; set; }     // kW (instant battery)

        // --- Daily energy totals (kWh) ---
        public double EpvToday { get; set; }         // PV generated today
        public double EfeedIn { get; set; }          // exported to grid
        public double EhomeLoad { get; set; }        // home consumption
        public double Echarge { get; set; }          // charged into battery
        public double Ebat { get; set; }             // battery throughput
        public double EgridCharge { get; set; }      // imported from grid
        public double Einput { get; set; }           // alias/grid import
        public double EloadRaw { get; set; }         // raw home load
        public double EchargingPile { get; set; }    // charging pile usage
        public double Ediesel { get; set; }          // diesel PV generation

        // --- Self-consumption & sufficiency ---
        public double EselfConsumption { get; set; } // kWh self-consumed
        public double EselfSufficiency { get; set; } // % self-sufficiency

        // --- Battery discharge and generator flag ---
        public double Edischarge { get; set; }       // kWh discharged
        public bool HasGenerator { get; set; }       // diesel generator ran?

        // --- Flags ---
        public bool? HasChargingPile { get; set; }   // EV charger present
    }
}
