using System.Text.Json;
using StackExchange.Redis;

namespace Meridian.Dashboard.Services;

public class PortfolioAggregate
{
    public AggregateGreeks AggregateGreeks { get; set; } = new();
    public decimal TotalUnrealizedPnl { get; set; }
    public int PositionCount { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AggregateGreeks
{
    public decimal Delta { get; set; }
    public decimal Gamma { get; set; }
    public decimal Vega { get; set; }
    public decimal Theta { get; set; }
    public decimal Rho { get; set; }
}

public class PositionData
{
    public string PositionId { get; set; } = "";
    public decimal Delta { get; set; }
    public decimal Gamma { get; set; }
    public decimal Vega { get; set; }
    public decimal Theta { get; set; }
    public decimal Rho { get; set; }
    public decimal Pnl { get; set; }
}

public class RedisDataService
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisDataService(string connectionString)
    {
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    public async Task<PortfolioAggregate?> GetAggregateAsync()
    {
        var value = await _db.StringGetAsync("portfolio:aggregate");
        if (value.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<PortfolioAggregate>(value!, JsonOptions);
    }

    public async Task<List<PositionData>> GetAllPositionsAsync()
    {
        var positions = new List<PositionData>();
        var server = _redis.GetServer(_redis.GetEndPoints().First());

        // Scan for position greek keys
        var greekKeys = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: "position:*:greeks"))
        {
            greekKeys.Add(key);
        }

        foreach (var key in greekKeys)
        {
            var keyStr = key.ToString();
            // Extract position ID from key pattern "position:{id}:greeks"
            var parts = keyStr.Split(':');
            if (parts.Length < 3) continue;
            var positionId = parts[1];

            var greeksJson = await _db.StringGetAsync(key);
            var pnlJson = await _db.StringGetAsync($"pnl:{positionId}:latest");

            if (greeksJson.IsNullOrEmpty) continue;

            var greeks = JsonSerializer.Deserialize<PositionGreeksDto>(greeksJson!, JsonOptions);
            decimal pnl = 0;
            if (!pnlJson.IsNullOrEmpty)
            {
                var pnlData = JsonSerializer.Deserialize<PnlDto>(pnlJson!, JsonOptions);
                pnl = pnlData?.Pnl ?? 0;
            }

            if (greeks != null)
            {
                positions.Add(new PositionData
                {
                    PositionId = positionId,
                    Delta = greeks.Delta,
                    Gamma = greeks.Gamma,
                    Vega = greeks.Vega,
                    Theta = greeks.Theta,
                    Rho = greeks.Rho,
                    Pnl = pnl
                });
            }
        }

        return positions.OrderBy(p => p.PositionId).ToList();
    }

    // Internal DTOs for deserialization
    private class PositionGreeksDto
    {
        public decimal Delta { get; set; }
        public decimal Gamma { get; set; }
        public decimal Vega { get; set; }
        public decimal Theta { get; set; }
        public decimal Rho { get; set; }
    }

    private class PnlDto
    {
        public decimal Pnl { get; set; }
    }
}
