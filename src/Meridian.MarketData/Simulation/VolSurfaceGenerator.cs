using System.Reactive.Linq;
using MathNet.Numerics.Distributions;
using Meridian.Common.Models;

namespace Meridian.MarketData.Simulation;

/// <summary>
/// Generates a simulated volatility surface with smile/skew dynamics.
/// ATM vol follows an Ornstein-Uhlenbeck mean-reverting process:
///   d_sigma = kappa * (theta - sigma) * dt + xi * dW
/// </summary>
public class VolSurfaceGenerator
{
    private readonly string _symbol;
    private readonly double _kappa;    // mean reversion speed
    private readonly double _theta;    // long-run mean ATM vol
    private readonly double _xi;       // vol of vol
    private readonly double _skew;     // skew coefficient for ln(K/S)
    private readonly double _smile;    // smile coefficient for (ln(K/S))^2
    private readonly Normal _normalDist;
    private readonly object _lock = new();

    // Strike levels as percentage of spot
    private static readonly double[] StrikeLevels = { 0.80, 0.90, 0.95, 1.00, 1.05, 1.10, 1.20 };

    // Expiry offsets in days
    private static readonly int[] ExpiryDays = { 7, 30, 60, 90, 180, 365 };

    private double _currentAtmVol;

    // External vol spike overlay (reads are inside _lock)
    private double _volSpikeOverride;

    public VolSurfaceGenerator(
        string symbol,
        double initialAtmVol,
        double kappa = 5.0,
        double theta = 0.20,
        double xi = 0.3,
        double skew = -0.10,
        double smile = 0.05)
    {
        _symbol = symbol;
        _currentAtmVol = initialAtmVol;
        _kappa = kappa;
        _theta = theta;
        _xi = xi;
        _skew = skew;
        _smile = smile;

        _normalDist = new Normal(0.0, 1.0, new MathNet.Numerics.Random.MersenneTwister(
            unchecked((int)DateTime.UtcNow.Ticks) ^ (symbol.GetHashCode() * 31)));
    }

    /// <summary>
    /// Generates a stream of vol surface updates, driven by spot price ticks.
    /// </summary>
    /// <param name="spotTicks">Upstream spot price observable.</param>
    /// <param name="updatesPerSecond">Rate at which to sample and emit vol surface updates.</param>
    public IObservable<VolSurfaceUpdate> GenerateVolSurfaceStream(
        IObservable<MarketTick> spotTicks,
        int updatesPerSecond)
    {
        // dt for the OU process at the update rate
        double dt = 1.0 / ((double)updatesPerSecond * 252.0 * 6.5 * 3600.0);
        double sqrtDt = Math.Sqrt(dt);

        return spotTicks
            .Sample(TimeSpan.FromMilliseconds(1000.0 / updatesPerSecond))
            .Select(tick =>
            {
                lock (_lock)
                {
                    // Ornstein-Uhlenbeck step for ATM vol
                    double z = _normalDist.Sample();
                    double dSigma = _kappa * (_theta - _currentAtmVol) * dt + _xi * sqrtDt * z;
                    _currentAtmVol += dSigma;

                    // Floor ATM vol to prevent negative values
                    _currentAtmVol = Math.Max(_currentAtmVol, 0.01);

                    // Apply vol spike override if active
                    double effectiveAtmVol = _currentAtmVol;
                    double spikeOverride = _volSpikeOverride;
                    if (spikeOverride > 0.0)
                    {
                        effectiveAtmVol += spikeOverride;
                    }

                    double spot = (double)tick.Price;
                    var now = DateTime.UtcNow;

                    // Generate surface points across strikes and expiries
                    var points = new List<VolSurfacePoint>();

                    foreach (double expiryDays in ExpiryDays)
                    {
                        DateTime expiry = now.AddDays(expiryDays);

                        // Term structure: slight increase for longer-dated
                        double termFactor = 1.0 + 0.02 * Math.Log(expiryDays / 30.0);

                        foreach (double strikeLevel in StrikeLevels)
                        {
                            double strike = spot * strikeLevel;
                            double moneyness = Math.Log(strikeLevel); // ln(K/S)

                            // Vol smile formula: vol(K) = atmVol + skew * ln(K/S) + smile * (ln(K/S))^2
                            double vol = effectiveAtmVol * termFactor
                                         + _skew * moneyness
                                         + _smile * moneyness * moneyness;

                            // Floor vol
                            vol = Math.Max(vol, 0.01);

                            points.Add(new VolSurfacePoint(
                                Strike: Math.Round((decimal)strike, 2),
                                Expiry: expiry,
                                ImpliedVol: Math.Round((decimal)vol, 6)));
                        }
                    }

                    return new VolSurfaceUpdate(
                        Symbol: _symbol,
                        Points: points.AsReadOnly(),
                        AtmVol: Math.Round((decimal)effectiveAtmVol, 6),
                        Timestamp: now);
                }
            });
    }

    /// <summary>
    /// Adds a vol spike magnitude that will be overlaid on top of ATM vol.
    /// Set to 0 to clear.
    /// </summary>
    public void SetVolSpikeOverride(double magnitude)
    {
        _volSpikeOverride = magnitude;
    }

    public string Symbol => _symbol;
}
