using ElektrometerService;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.Configure<ElectrometerSettings>(
            hostContext.Configuration.GetSection("ElectrometerSettings"));
        services.Configure<OutputSettings>(
            hostContext.Configuration.GetSection("OutputSettings"));
        services.Configure<DatabaseOptions>(
            hostContext.Configuration.GetSection("Database"));

        services.AddHostedService<Worker>();
    });

await hostBuilder.Build().RunAsync();
