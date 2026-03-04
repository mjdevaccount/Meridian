using Microsoft.AspNetCore.SignalR;
using Meridian.Dashboard.Hubs;

namespace Meridian.Dashboard.Services;

public class PortfolioPollingService : BackgroundService
{
    private readonly RedisDataService _redis;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<PortfolioPollingService> _logger;

    public PortfolioPollingService(
        RedisDataService redis,
        IHubContext<DashboardHub> hub,
        ILogger<PortfolioPollingService> logger)
    {
        _redis = redis;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Portfolio polling service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var aggregate = await _redis.GetAggregateAsync();
                var positions = await _redis.GetAllPositionsAsync();

                if (aggregate != null)
                {
                    await _hub.Clients.All.SendAsync("PortfolioUpdate", aggregate, stoppingToken);
                    await _hub.Clients.All.SendAsync("PositionsUpdate", positions, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling Redis for portfolio data");
            }

            await Task.Delay(500, stoppingToken);
        }
    }
}
