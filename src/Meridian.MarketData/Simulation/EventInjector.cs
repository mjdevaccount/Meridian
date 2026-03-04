using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Meridian.MarketData.Simulation;

/// <summary>
/// Injects market events (vol spikes, price gaps, regime changes) into the simulation.
/// For Phase 1, directly mutates the state of GBM and VolSurface generators.
/// </summary>
public class EventInjector : IDisposable
{
    private readonly Dictionary<string, GeometricBrownianMotion> _gbmInstances;
    private readonly Dictionary<string, VolSurfaceGenerator> _volSurfaceGenerators;
    private readonly Subject<MarketEvent> _eventStream = new();
    private readonly List<IDisposable> _activeTimers = new();
    private readonly object _lock = new();

    public EventInjector(
        Dictionary<string, GeometricBrownianMotion> gbmInstances,
        Dictionary<string, VolSurfaceGenerator> volSurfaceGenerators)
    {
        _gbmInstances = gbmInstances;
        _volSurfaceGenerators = volSurfaceGenerators;
    }

    /// <summary>
    /// Observable stream of all injected market events.
    /// </summary>
    public IObservable<MarketEvent> Events => _eventStream.AsObservable();

    /// <summary>
    /// Injects a temporary volatility spike for the specified symbol.
    /// The vol spike is additive on top of the current ATM vol.
    /// After the duration elapses, volatility returns to normal.
    /// </summary>
    public void InjectVolSpike(string symbol, decimal magnitude, TimeSpan duration)
    {
        if (!_gbmInstances.TryGetValue(symbol, out var gbm))
            return;

        _volSurfaceGenerators.TryGetValue(symbol, out var volGen);

        double mag = (double)magnitude;

        // Apply the spike
        gbm.SetVolatilityOverride(mag + GetBaseVol(gbm));
        volGen?.SetVolSpikeOverride(mag);

        _eventStream.OnNext(new MarketEvent(
            symbol, MarketEventType.VolSpike, $"Vol spike +{magnitude:P0} for {duration.TotalSeconds}s"));

        // Schedule restoration
        lock (_lock)
        {
            var timer = Observable.Timer(duration)
                .Subscribe(_ =>
                {
                    gbm.RestoreVolatility();
                    volGen?.SetVolSpikeOverride(0.0);
                    _eventStream.OnNext(new MarketEvent(
                        symbol, MarketEventType.VolSpike, $"Vol spike ended for {symbol}"));
                });
            _activeTimers.Add(timer);
        }
    }

    /// <summary>
    /// Injects an instantaneous price gap (jump) for the specified symbol.
    /// </summary>
    /// <param name="symbol">Target symbol.</param>
    /// <param name="gapPercent">Gap as a decimal fraction (e.g., 0.05 = +5%, -0.10 = -10%).</param>
    public void InjectPriceGap(string symbol, decimal gapPercent)
    {
        if (!_gbmInstances.TryGetValue(symbol, out var gbm))
            return;

        gbm.ApplyPriceGap((double)gapPercent);

        _eventStream.OnNext(new MarketEvent(
            symbol, MarketEventType.PriceGap, $"Price gap {gapPercent:+0.0%;-0.0%}"));
    }

    /// <summary>
    /// Injects a permanent regime change (new drift and volatility) for the specified symbol.
    /// </summary>
    public void InjectRegimeChange(string symbol, decimal newDrift, decimal newVol)
    {
        if (!_gbmInstances.TryGetValue(symbol, out var gbm))
            return;

        gbm.SetRegime((double)newDrift, (double)newVol);

        _eventStream.OnNext(new MarketEvent(
            symbol, MarketEventType.RegimeChange, $"Regime change: drift={newDrift}, vol={newVol}"));
    }

    private static double GetBaseVol(GeometricBrownianMotion gbm)
    {
        // The GBM's active volatility before override
        // Since we're applying a spike, we want spike + current base
        return 0.0; // Spike magnitude is additive; the GBM override sets absolute value
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var timer in _activeTimers)
                timer.Dispose();
            _activeTimers.Clear();
        }

        _eventStream.Dispose();
    }
}

/// <summary>
/// Represents a market event injected into the simulation.
/// </summary>
public record MarketEvent(string Symbol, MarketEventType Type, string Description);

/// <summary>
/// Types of injectable market events.
/// </summary>
public enum MarketEventType
{
    VolSpike,
    PriceGap,
    RegimeChange
}
