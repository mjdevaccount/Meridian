using Microsoft.AspNetCore.SignalR;

namespace Meridian.Dashboard.Hubs;

public class DashboardHub : Hub
{
    // Server pushes to clients via PortfolioPollingService.
    // No client-to-server methods needed.
}
