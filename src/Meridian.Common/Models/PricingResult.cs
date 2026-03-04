namespace Meridian.Common.Models;

public record PricingResult(
    decimal TheoreticalPrice,
    decimal IntrinsicValue,
    decimal TimeValue,
    decimal ImpliedVol);
