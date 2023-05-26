using DiffRulesWk;
using Microsoft.Extensions.Configuration.UserSecrets;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<WKCopernicus>();
    })
    .Build();

host.Run();
