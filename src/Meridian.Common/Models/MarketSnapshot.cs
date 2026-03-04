namespace Meridian.Common.Models;

public record MarketSnapshot(
    string Symbol,
    decimal SpotPrice,
    decimal ImpliedVol,
    decimal RiskFreeRate,
    DateTime Timestamp);
