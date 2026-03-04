namespace Meridian.Common.Configuration;

public class MeridianConfig
{
    public MarketDataConfig MarketData { get; set; } = new();
    public KafkaConfig Kafka { get; set; } = new();
    public RiskLimitsConfig RiskLimits { get; set; } = new();
}

public class MarketDataConfig
{
    public List<SymbolConfig> Symbols { get; set; } = new();
    public int TicksPerSecond { get; set; } = 1000;
    public int VolSurfaceUpdatesPerSecond { get; set; } = 5;
}

public class SymbolConfig
{
    public string Symbol { get; set; } = "";
    public decimal InitialPrice { get; set; }
    public decimal Drift { get; set; }
    public decimal Volatility { get; set; }
}

public class KafkaConfig
{
    public string BootstrapServers { get; set; } = "kafka:9092";
    public string TicksTopic { get; set; } = "market.ticks";
    public string VolSurfaceTopic { get; set; } = "market.volsurface";
}

public class RiskLimitsConfig
{
    public decimal MaxPortfolioDelta { get; set; } = 500;
    public decimal MaxPortfolioGamma { get; set; } = 100;
    public decimal MaxPortfolioVega { get; set; } = 10000;
    public decimal MaxPositionDelta { get; set; } = 100;
    public decimal MaxDrawdownPercent { get; set; } = 5.0m;
    public decimal MaxSingleNameConcentration { get; set; } = 0.30m;
    public int AlertCooldownSeconds { get; set; } = 30;
}
