namespace Meridian.Common.Models;

public record PnlUpdate(
    string PositionId,
    decimal MarkToMarket,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal TotalPnl,
    DateTime Timestamp);
