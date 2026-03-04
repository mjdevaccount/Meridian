using System.Reactive.Linq;
using Meridian.Common.Configuration;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.MarketData.Simulation;

/// <summary>
/// Orchestrates multiple GBM price generators and vol surface generators.
/// Implements IMarketDataSource to provide unified tick and vol surface streams.
/// </summary>
public class MarketDataSimulator : IMarketDataSource, IDisposable
{
    private readonly MarketDataConfig _config;
    private readonly Dictionary<string, GeometricBrownianMotion> _gbmInstances = new();
    private readonly Dictionary<string, VolSurfaceGenerator> _volSurfaceGenerators = new();
    private readonly Dictionary<string, IObservable<MarketTick>> _tickStreams = new();
    private readonly EventInjector _eventInjector;
    private readonly IObservable<MarketTick> _allTicksStream;
    private readonly IObservable<VolSurfaceUpdate> _volSurfaceStream;

    public MarketDataSimulator(MarketDataConfig config)
    {
        _config = config;

        // Create a GBM instance and vol surface generator for each configured symbol
        foreach (var symbolConfig in config.Symbols)
        {
            var gbm = new GeometricBrownianMotion(
                symbolConfig.Symbol,
                symbolConfig.InitialPrice,
                symbolConfig.Drift,
                symbolConfig.Volatility);

            _gbmInstances[symbolConfig.Symbol] = gbm;

            var volGen = new VolSurfaceGenerator(
                symbolConfig.Symbol,
                initialAtmVol: (double)symbolConfig.Volatility,
                theta: (double)symbolConfig.Volatility);

            _volSurfaceGenerators[symbolConfig.Symbol] = volGen;

            // Create and cache the tick stream (shared via Publish + RefCount)
            var tickStream = gbm.GenerateTickStream(config.TicksPerSecond)
                .Publish()
                .RefCount();

            _tickStreams[symbolConfig.Symbol] = tickStream;
        }

        // Create the event injector
        _eventInjector = new EventInjector(_gbmInstances, _volSurfaceGenerators);

        // Merge all tick streams
        _allTicksStream = _tickStreams.Values.Merge();

        // Create vol surface streams for each symbol, merging them
        var volStreams = new List<IObservable<VolSurfaceUpdate>>();
        foreach (var symbolConfig in config.Symbols)
        {
            var volStream = _volSurfaceGenerators[symbolConfig.Symbol]
                .GenerateVolSurfaceStream(
                    _tickStreams[symbolConfig.Symbol],
                    config.VolSurfaceUpdatesPerSecond);
            volStreams.Add(volStream);
        }

        _volSurfaceStream = volStreams.Merge();
    }

    /// <summary>
    /// Gets the tick stream for a specific symbol.
    /// </summary>
    public IObservable<MarketTick> GetTickStream(string symbol)
    {
        if (_tickStreams.TryGetValue(symbol, out var stream))
            return stream;

        return Observable.Empty<MarketTick>();
    }

    /// <summary>
    /// Gets the merged tick stream for all symbols.
    /// </summary>
    public IObservable<MarketTick> GetAllTicksStream()
    {
        return _allTicksStream;
    }

    /// <summary>
    /// Gets the merged vol surface update stream for all symbols.
    /// </summary>
    public IObservable<VolSurfaceUpdate> GetVolSurfaceStream()
    {
        return _volSurfaceStream;
    }

    /// <summary>
    /// Provides access to the event injector for injecting market events.
    /// </summary>
    public EventInjector EventInjector => _eventInjector;

    /// <summary>
    /// Gets the current list of configured symbols.
    /// </summary>
    public IReadOnlyList<string> Symbols => _config.Symbols.Select(s => s.Symbol).ToList().AsReadOnly();

    public void Dispose()
    {
        _eventInjector.Dispose();
    }
}
