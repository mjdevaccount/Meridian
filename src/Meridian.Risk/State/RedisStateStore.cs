using System.Text.Json;
using Meridian.Common.Models;
using StackExchange.Redis;

namespace Meridian.Risk.State;

public class RedisStateStore : IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisStateStore(string connectionString)
    {
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    public async Task UpdatePositionGreeksAsync(string positionId, GreeksResult greeks)
    {
        var json = JsonSerializer.Serialize(greeks);
        await _db.StringSetAsync($"position:{positionId}:greeks", json, TimeSpan.FromMinutes(5));
    }

    public async Task UpdatePnlAsync(string positionId, PnlUpdate pnl)
    {
        var json = JsonSerializer.Serialize(pnl);
        await _db.StringSetAsync($"pnl:{positionId}:latest", json, TimeSpan.FromMinutes(5));
    }

    public async Task UpdateAggregateAsync(PortfolioSnapshot snapshot)
    {
        var summary = new
        {
            snapshot.AggregateGreeks,
            snapshot.TotalUnrealizedPnl,
            PositionCount = snapshot.Positions.Count,
            snapshot.Timestamp
        };
        var json = JsonSerializer.Serialize(summary);
        await _db.StringSetAsync("portfolio:aggregate", json, TimeSpan.FromMinutes(5));
    }

    public async Task<GreeksResult?> GetPositionGreeksAsync(string positionId)
    {
        var json = await _db.StringGetAsync($"position:{positionId}:greeks");
        return json.HasValue ? JsonSerializer.Deserialize<GreeksResult>(json!) : null;
    }

    public async Task<PnlUpdate?> GetPnlAsync(string positionId)
    {
        var json = await _db.StringGetAsync($"pnl:{positionId}:latest");
        return json.HasValue ? JsonSerializer.Deserialize<PnlUpdate>(json!) : null;
    }

    public void Dispose()
    {
        _redis.Dispose();
    }
}
