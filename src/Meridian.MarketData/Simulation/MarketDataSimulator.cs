using System.Reactive.Linq;
using System.Reactive.Subjects;
using Meridian.Common.Configuration;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.MarketData.Simulation;

public class MarketDataSimulator : IMarketDataSource, IDisposable
{
    private readonly MarketDataConfig _config;
    private readonly Dictionary<string, GeometricBrownianMotion> _gbmInstances = new();
    private readonly Dictionary<string, VolSurfaceGenerator> _volSurfaceGenerators = new();
    private readonly Dictionary<string, IObservable<MarketTick>> _tickStreams = new();
    private readonly List<IDisposable> _streamSubscriptions = new();
    private readonly EventInjector _eventInjector;
    private readonly Subject<MarketTick> _allTicksSubject = new();
    private readonly Subject<VolSurfaceUpdate> _volSurfaceSubject = new();
    private readonly object _lock = new();

    public MarketDataSimulator(MarketDataConfig config)
    {
        _config = config;

        foreach (var symbolConfig in config.Symbols)
        {
            AddSymbolInternal(symbolConfig.Symbol, symbolConfig.InitialPrice,
                symbolConfig.Drift, symbolConfig.Volatility);
        }

        _eventInjector = new EventInjector(_gbmInstances, _volSurfaceGenerators);
    }

    public void AddSymbol(string symbol, decimal initialPrice, decimal drift = 0.05m, decimal volatility = 0.25m)
    {
        lock (_lock)
        {
            if (_gbmInstances.ContainsKey(symbol)) return;
            AddSymbolInternal(symbol, initialPrice, drift, volatility);
        }
    }

    private void AddSymbolInternal(string symbol, decimal initialPrice, decimal drift, decimal volatility)
    {
        var gbm = new GeometricBrownianMotion(symbol, initialPrice, drift, volatility);
        _gbmInstances[symbol] = gbm;

        var volGen = new VolSurfaceGenerator(symbol,
            initialAtmVol: (double)volatility,
            theta: (double)volatility);
        _volSurfaceGenerators[symbol] = volGen;

        var tickStream = gbm.GenerateTickStream(_config.TicksPerSecond)
            .Publish()
            .RefCount();
        _tickStreams[symbol] = tickStream;

        // Feed into the shared subjects
        var tickSub = tickStream.Subscribe(tick => _allTicksSubject.OnNext(tick));
        _streamSubscriptions.Add(tickSub);

        var volStream = volGen.GenerateVolSurfaceStream(tickStream, _config.VolSurfaceUpdatesPerSecond);
        var volSub = volStream.Subscribe(update => _volSurfaceSubject.OnNext(update));
        _streamSubscriptions.Add(volSub);
    }

    public IObservable<MarketTick> GetTickStream(string symbol)
    {
        lock (_lock)
        {
            if (_tickStreams.TryGetValue(symbol, out var stream))
                return stream;
        }
        return Observable.Empty<MarketTick>();
    }

    public IObservable<MarketTick> GetAllTicksStream() => _allTicksSubject.AsObservable();

    public IObservable<VolSurfaceUpdate> GetVolSurfaceStream() => _volSurfaceSubject.AsObservable();

    public Task<bool> SubscribeToSymbolAsync(string symbol)
    {
        lock (_lock)
        {
            if (_gbmInstances.ContainsKey(symbol)) return Task.FromResult(true);
            // For simulated mode, create a GBM with default params
            AddSymbolInternal(symbol, 100m, 0.05m, 0.25m);
        }
        return Task.FromResult(true);
    }

    public EventInjector EventInjector => _eventInjector;

    public IReadOnlyList<string> Symbols
    {
        get
        {
            lock (_lock) { return _gbmInstances.Keys.ToList().AsReadOnly(); }
        }
    }

    public void Dispose()
    {
        _eventInjector.Dispose();
        foreach (var sub in _streamSubscriptions) sub.Dispose();
        _allTicksSubject.Dispose();
        _volSurfaceSubject.Dispose();
    }
}
