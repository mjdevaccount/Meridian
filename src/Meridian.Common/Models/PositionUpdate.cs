namespace Meridian.Common.Models;

public record PositionUpdate(
    Position Position,
    PricingResult Pricing,
    GreeksResult Greeks,
    DateTime Timestamp);
