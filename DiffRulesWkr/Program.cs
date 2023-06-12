using NcidWorkerSpc;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting; 

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<NcidWorker>();
        services.AddHttpClient();        
    })
    .Build();

host.Run();
