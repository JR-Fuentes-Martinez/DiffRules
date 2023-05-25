using DiffRulesLib;

namespace DiffRulesWk;

public class WKGFS : BackgroundService
{
    private readonly ILogger<WKGFS> _logger;

    public WKGFS(ILogger<WKGFS> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(1000, stoppingToken);
        }
    }
}
