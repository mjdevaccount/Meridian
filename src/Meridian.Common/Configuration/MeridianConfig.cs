namespace Meridian.Common.Configuration;

public class MeridianConfig
{
    public MarketDataConfig MarketData { get; set; } = new();
    public KafkaConfig Kafka { get; set; } = new();
    public RiskLimitsConfig RiskLimits { get; set; } = new();
}

public class MarketDataConfig
{
    public MarketDataMode Mode { get; set; } = MarketDataMode.Simulated;
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
    public string PortfolioCommandsTopic { get; set; } = "portfolio.commands";
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

public enum MarketDataMode { Simulated, Live }

public class IbkrConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4002;
    public int ClientId { get; set; } = 1;
    public List<IbkrSymbolConfig> Symbols { get; set; } = new();
    public int ReconnectDelaySeconds { get; set; } = 5;
    public int MaxReconnectAttempts { get; set; } = 10;
}

public class IbkrSymbolConfig
{
    public string Symbol { get; set; } = "";
    public string SecType { get; set; } = "STK";
    public string Exchange { get; set; } = "SMART";
    public string Currency { get; set; } = "USD";
    public string MeridianSymbol { get; set; } = "";
}
