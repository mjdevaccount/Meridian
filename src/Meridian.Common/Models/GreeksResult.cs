namespace Meridian.Common.Models;

public record GreeksResult(
    decimal Delta,
    decimal Gamma,
    decimal Vega,
    decimal Theta,
    decimal Rho)
{
    public static GreeksResult Zero => new(0, 0, 0, 0, 0);

    public static GreeksResult operator +(GreeksResult a, GreeksResult b) =>
        new(a.Delta + b.Delta, a.Gamma + b.Gamma, a.Vega + b.Vega, a.Theta + b.Theta, a.Rho + b.Rho);

    public GreeksResult Scale(int quantity) =>
        new(Delta * quantity, Gamma * quantity, Vega * quantity, Theta * quantity, Rho * quantity);
}
