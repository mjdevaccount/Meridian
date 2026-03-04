namespace Meridian.Common.Models;

public record MarketTick(
    string Symbol,
    decimal Price,
    decimal? ImpliedVol,
    DateTime Timestamp,
    long SequenceNumber);
