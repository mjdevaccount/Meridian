using System.Reactive.Linq;
using MathNet.Numerics.Distributions;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.MarketData.Simulation;

/// <summary>
/// Generates price paths using the Geometric Brownian Motion model.
/// dS = mu * S * dt + sigma * S * dW
/// </summary>
public class GeometricBrownianMotion : IMarketDataSource
{
    private readonly string _symbol;
    private readonly double _drift;       // annualized
    private readonly double _volatility;  // annualized
    private readonly Normal _normalDist;
    private readonly object _lock = new();

    private double _currentPrice;
    private long _sequenceNumber;

    // Mutable state that can be modified by EventInjector (reads are inside _lock)
    private double _activeDrift;
    private double _activeVolatility;
    private double _pendingGapPercent;

    public GeometricBrownianMotion(string symbol, decimal initialPrice, decimal drift, decimal volatility)
    {
        _symbol = symbol;
        _currentPrice = (double)initialPrice;
        _drift = (double)drift;
        _volatility = (double)volatility;
        _activeDrift = _drift;
        _activeVolatility = _volatility;

        // Thread-safe random number generation using MathNet
        _normalDist = new Normal(0.0, 1.0, new MathNet.Numerics.Random.MersenneTwister(
            unchecked((int)DateTime.UtcNow.Ticks) ^ symbol.GetHashCode()));
    }

    /// <summary>
    /// Generates an observable stream of MarketTick values using GBM.
    /// </summary>
    /// <param name="ticksPerSecond">Number of ticks to generate per wall-clock second.</param>
    public IObservable<MarketTick> GenerateTickStream(int ticksPerSecond)
    {
        // dt represents one tick's fraction of a trading year:
        // 252 trading days * 6.5 hours/day * 3600 seconds/hour = total trading seconds/year
        double dt = 1.0 / ((double)ticksPerSecond * 252.0 * 6.5 * 3600.0);
        double sqrtDt = Math.Sqrt(dt);

        return Observable.Interval(TimeSpan.FromMilliseconds(1000.0 / ticksPerSecond))
            .Select(_ =>
            {
                lock (_lock)
                {
                    // Apply any pending price gap
                    double gap = Interlocked.Exchange(ref _pendingGapPercent, 0.0);
                    if (gap != 0.0)
                    {
                        _currentPrice *= (1.0 + gap);
                    }

                    double mu = _activeDrift;
                    double sigma = _activeVolatility;

                    // GBM step: S(t+dt) = S(t) * exp((mu - 0.5*sigma^2)*dt + sigma*sqrt(dt)*Z)
                    double z = _normalDist.Sample();
                    double exponent = (mu - 0.5 * sigma * sigma) * dt + sigma * sqrtDt * z;
                    _currentPrice *= Math.Exp(exponent);

                    // Ensure price stays positive
                    _currentPrice = Math.Max(_currentPrice, 0.0001);

                    long seq = Interlocked.Increment(ref _sequenceNumber);

                    return new MarketTick(
                        Symbol: _symbol,
                        Price: Math.Round((decimal)_currentPrice, 4),
                        ImpliedVol: null,
                        Timestamp: DateTime.UtcNow,
                        SequenceNumber: seq);
                }
            });
    }

    /// <summary>
    /// Applies a price gap as a percentage (e.g., 0.05 = +5%).
    /// The gap will be applied on the next tick.
    /// </summary>
    public void ApplyPriceGap(double gapPercent)
    {
        Interlocked.Exchange(ref _pendingGapPercent, gapPercent);
    }

    /// <summary>
    /// Changes the drift and volatility parameters (regime change).
    /// </summary>
    public void SetRegime(double newDrift, double newVolatility)
    {
        _activeDrift = newDrift;
        _activeVolatility = newVolatility;
    }

    /// <summary>
    /// Temporarily overrides volatility for a vol spike.
    /// </summary>
    public void SetVolatilityOverride(double newVol)
    {
        _activeVolatility = newVol;
    }

    /// <summary>
    /// Restores volatility to the base level.
    /// </summary>
    public void RestoreVolatility()
    {
        _activeVolatility = _volatility;
    }

    public string Symbol => _symbol;
    public decimal CurrentPrice => (decimal)_currentPrice;

    // IMarketDataSource implementation
    public IObservable<MarketTick> GetTickStream(string symbol)
    {
        if (!string.Equals(symbol, _symbol, StringComparison.OrdinalIgnoreCase))
            return Observable.Empty<MarketTick>();

        return GenerateTickStream(1000); // default rate
    }

    public IObservable<MarketTick> GetAllTicksStream()
    {
        return GenerateTickStream(1000); // single symbol source
    }
}
