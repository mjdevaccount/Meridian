namespace Meridian.Common.Models;

public record VolSurfaceUpdate(
    string Symbol,
    IReadOnlyList<VolSurfacePoint> Points,
    decimal AtmVol,
    DateTime Timestamp);
