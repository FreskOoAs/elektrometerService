namespace ElektrometerService
{
    public class ElectrometerSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string LoginUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SysSn { get; set; } = string.Empty;
        public string StationId { get; set; } = string.Empty;
        public string CentralId { get; set; } = "123456";
        public int FetchIntervalMinutes { get; set; } = 10;

        public long ApartmentId { get; set; }
        public long SensorId { get; set; }
    }

    public class OutputSettings
    {
        public string JsonFilePath { get; set; } = string.Empty;
    }
}
