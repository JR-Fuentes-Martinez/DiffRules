using DiffRulesWk;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<WKGFS>();
        services.AddHostedService<WKCopernicus>();
    })
    .Build();

host.Run();
