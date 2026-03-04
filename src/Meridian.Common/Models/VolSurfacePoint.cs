namespace Meridian.Common.Models;

public record VolSurfacePoint(
    decimal Strike,
    DateTime Expiry,
    decimal ImpliedVol);
