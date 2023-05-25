using DiffRulesLib;

namespace DiffRulesWk;

public class WKCopernicus : BackgroundService
{
    private readonly ILogger<WKCopernicus> _logger;

    public WKCopernicus(ILogger<WKCopernicus> logger)
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
