using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.Pricing.Streams;

/// <summary>
/// The core Rx pipeline that transforms raw market data ticks and vol surface updates
/// into a continuous stream of portfolio snapshots with real-time PnL and Greeks.
///
/// Supports dynamic position add/remove via AddPosition() and RemovePosition() methods.
///
/// Demonstrates key Rx patterns:
///   GroupBy       - partition ticks by symbol
///   CombineLatest - merge spot + vol for each underlier
///   Scan          - portfolio accumulation over time
///   Buffer        - micro-batch at 50ms for efficiency
///   DistinctUntilChanged - skip repricing when price hasn't changed
///   Publish().RefCount()  - multicast to multiple subscribers
///   ObserveOn     - async dispatch to task pool
///   Merge         - combine per-position streams
///   Window        - rolling latency measurement windows
/// </summary>
public class PricingPipeline : IDisposable
{
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ConcurrentDictionary<long, DateTime> _tickTimestamps = new();
    private readonly ConcurrentQueue<double> _latencySamples = new();
    private readonly int _maxLatencySamples;

    // Dynamic position management
    private readonly ConcurrentDictionary<string, IDisposable> _positionSubscriptions = new();
    private readonly Subject<PositionUpdate> _dynamicUpdates = new();
    private ImmutableList<Position> _currentPositions = ImmutableList<Position>.Empty;
    private IObservable<MarketSnapshot>? _marketSnapshots;
    private IPricingModel? _model;
    private readonly object _positionLock = new();

    public PricingPipeline(int maxLatencySamples = 10_000)
    {
        _maxLatencySamples = maxLatencySamples;
    }

    /// <summary>
    /// Builds the full reactive pricing pipeline.
    /// </summary>
    public IObservable<PortfolioSnapshot> BuildPipeline(
        IObservable<MarketTick> ticks,
        IObservable<VolSurfaceUpdate> volUpdates,
        IEnumerable<Position> positions,
        IPricingModel model,
        decimal riskFreeRate = 0.05m)
    {
        _model = model;
        var positionList = positions.ToList();
        _currentPositions = positionList.ToImmutableList();

        // ---------------------------------------------------------------
        // Step 1: GroupBy - partition ticks by symbol (no underlier filter
        // so new underliers can be added dynamically)
        // ---------------------------------------------------------------
        var ticksBySymbol = ticks
            .GroupBy(t => t.Symbol)
            .Publish()
            .RefCount();

        // ---------------------------------------------------------------
        // Step 2: Build per-symbol MarketSnapshot using CombineLatest
        // ---------------------------------------------------------------
        _marketSnapshots = ticksBySymbol
            .SelectMany(tickGroup =>
            {
                var symbol = tickGroup.Key;
                var volForSymbol = volUpdates
                    .Where(v => string.Equals(v.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.AtmVol)
                    .StartWith(0.20m);

                return tickGroup.CombineLatest(volForSymbol, (tick, vol) =>
                {
                    _tickTimestamps[tick.SequenceNumber] = tick.Timestamp;
                    return new MarketSnapshot(
                        Symbol: tick.Symbol,
                        SpotPrice: tick.Price,
                        ImpliedVol: tick.ImpliedVol ?? vol,
                        RiskFreeRate: riskFreeRate,
                        Timestamp: tick.Timestamp);
                });
            })
            .Publish()
            .RefCount();

        // ---------------------------------------------------------------
        // Step 3-5: Subscribe each initial position
        // ---------------------------------------------------------------
        foreach (var position in positionList)
        {
            SubscribePosition(position);
        }

        // ---------------------------------------------------------------
        // Step 6-10: Buffer, Scan, Publish
        // ---------------------------------------------------------------
        var portfolio = _dynamicUpdates
            .ObserveOn(TaskPoolScheduler.Default)
            .Buffer(TimeSpan.FromMilliseconds(50))
            .Where(batch => batch.Count > 0)
            .Scan(PortfolioSnapshot.Empty, (snapshot, batch) =>
            {
                var result = snapshot.ApplyUpdates(batch);

                // Remove any positions that were removed
                lock (_positionLock)
                {
                    var activeIds = _currentPositions.Select(p => p.PositionId).ToHashSet();
                    foreach (var posId in result.Positions.Keys)
                    {
                        if (!activeIds.Contains(posId))
                        {
                            result = result.RemovePosition(posId);
                        }
                    }
                }

                RecordLatency(batch);
                return result;
            })
            .Publish()
            .RefCount();

        // Latency tracking
        var latencySubscription = portfolio
            .Window(TimeSpan.FromSeconds(5))
            .SelectMany(window => window.Count())
            .Subscribe(_ => { });

        _subscriptions.Add(latencySubscription);

        return portfolio;
    }

    /// <summary>
    /// Dynamically adds a position to the live pipeline.
    /// </summary>
    public void AddPosition(Position position)
    {
        lock (_positionLock)
        {
            if (_currentPositions.Any(p => p.PositionId == position.PositionId)) return;
            _currentPositions = _currentPositions.Add(position);
        }
        SubscribePosition(position);
    }

    /// <summary>
    /// Dynamically removes a position from the live pipeline.
    /// </summary>
    public void RemovePosition(string positionId)
    {
        lock (_positionLock)
        {
            _currentPositions = _currentPositions.RemoveAll(p => p.PositionId == positionId);
        }
        if (_positionSubscriptions.TryRemove(positionId, out var sub))
        {
            sub.Dispose();
        }
    }

    /// <summary>
    /// Gets the current list of active positions.
    /// </summary>
    public IReadOnlyList<Position> CurrentPositions
    {
        get { lock (_positionLock) { return _currentPositions; } }
    }

    private void SubscribePosition(Position position)
    {
        if (_marketSnapshots == null || _model == null)
            throw new InvalidOperationException("Pipeline must be built before adding positions");

        var sub = _marketSnapshots
            .Where(ms => string.Equals(ms.Symbol, position.Instrument.Underlier, StringComparison.OrdinalIgnoreCase))
            .Select(ms =>
            {
                var pricing = _model.Price(position.Instrument, ms);
                var greeks = _model.ComputeGreeks(position.Instrument, ms);
                return new PositionUpdate(position, pricing, greeks, ms.Timestamp);
            })
            .DistinctUntilChanged(pu => pu.Pricing.TheoreticalPrice)
            .Subscribe(update => _dynamicUpdates.OnNext(update));

        _positionSubscriptions[position.PositionId] = sub;
    }

    private void RecordLatency(IList<PositionUpdate> batch)
    {
        var now = DateTime.UtcNow;
        foreach (var update in batch)
        {
            double latencyMs = (now - update.Timestamp).TotalMilliseconds;
            _latencySamples.Enqueue(latencyMs);
            while (_latencySamples.Count > _maxLatencySamples)
            {
                _latencySamples.TryDequeue(out _);
            }
        }
    }

    public LatencyStats GetLatencyStats()
    {
        var samples = _latencySamples.ToArray();
        if (samples.Length == 0)
            return new LatencyStats(0, 0, 0, 0, 0);

        Array.Sort(samples);
        int count = samples.Length;
        double p50 = samples[(int)(count * 0.50)];
        double p95 = samples[Math.Min((int)(count * 0.95), count - 1)];
        double p99 = samples[Math.Min((int)(count * 0.99), count - 1)];
        return new LatencyStats(p50, p95, p99, samples[0], samples[count - 1]);
    }

    public void ResetLatencyStats()
    {
        while (_latencySamples.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (var sub in _positionSubscriptions.Values) sub.Dispose();
        _positionSubscriptions.Clear();
        _dynamicUpdates.Dispose();
        _tickTimestamps.Clear();
    }
}

public record LatencyStats(
    double P50,
    double P95,
    double P99,
    double Min,
    double Max);
