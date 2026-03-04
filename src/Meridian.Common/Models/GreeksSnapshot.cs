namespace Meridian.Common.Models;

public record GreeksSnapshot(
    string PositionId,
    decimal Delta,
    decimal Gamma,
    decimal Vega,
    decimal Theta,
    decimal Rho,
    DateTime Timestamp);
