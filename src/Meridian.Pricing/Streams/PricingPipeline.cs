using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.Pricing.Streams;

/// <summary>
/// The core Rx pipeline that transforms raw market data ticks and vol surface updates
/// into a continuous stream of portfolio snapshots with real-time PnL and Greeks.
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

    public PricingPipeline(int maxLatencySamples = 10_000)
    {
        _maxLatencySamples = maxLatencySamples;
    }

    /// <summary>
    /// Builds the full reactive pricing pipeline.
    /// </summary>
    /// <param name="ticks">Raw market tick stream (spot prices).</param>
    /// <param name="volUpdates">Vol surface updates stream.</param>
    /// <param name="positions">Portfolio positions to price.</param>
    /// <param name="model">Pricing model (e.g., Black-Scholes).</param>
    /// <param name="riskFreeRate">Risk-free rate for discounting.</param>
    /// <returns>A hot observable of PortfolioSnapshot updated in real-time.</returns>
    public IObservable<PortfolioSnapshot> BuildPipeline(
        IObservable<MarketTick> ticks,
        IObservable<VolSurfaceUpdate> volUpdates,
        IEnumerable<Position> positions,
        IPricingModel model,
        decimal riskFreeRate = 0.05m)
    {
        var positionList = positions.ToList();

        // Collect the distinct underliers we care about
        var underliers = positionList
            .Select(p => p.Instrument.Underlier)
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------
        // Step 1: GroupBy - partition ticks by symbol
        // ---------------------------------------------------------------
        var ticksBySymbol = ticks
            .Where(t => underliers.Contains(t.Symbol))
            .GroupBy(t => t.Symbol)
            .Publish()
            .RefCount();

        // ---------------------------------------------------------------
        // Step 2: Build per-symbol MarketSnapshot using CombineLatest
        //         Merge spot ticks with vol surface updates for that symbol
        // ---------------------------------------------------------------
        var volBySymbol = volUpdates
            .GroupBy(v => v.Symbol);

        var marketSnapshots = ticksBySymbol
            .SelectMany(tickGroup =>
            {
                var symbol = tickGroup.Key;

                // Get the vol updates for this same symbol
                var volForSymbol = volUpdates
                    .Where(v => string.Equals(v.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.AtmVol)
                    .StartWith(0.20m); // default vol until first vol update arrives

                // CombineLatest: every time either spot or vol changes, produce a new snapshot
                return tickGroup.CombineLatest(volForSymbol, (tick, vol) =>
                {
                    // Track tick timestamp for latency measurement
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
        // Step 3-5: For each position, filter snapshots, price, compute Greeks
        //           DistinctUntilChanged on theoretical price
        // ---------------------------------------------------------------
        var positionStreams = positionList.Select(position =>
        {
            return marketSnapshots
                // Filter to only the underlier for this position
                .Where(ms => string.Equals(ms.Symbol, position.Instrument.Underlier, StringComparison.OrdinalIgnoreCase))
                // Price and compute Greeks
                .Select(ms =>
                {
                    var pricing = model.Price(position.Instrument, ms);
                    var greeks = model.ComputeGreeks(position.Instrument, ms);
                    return new PositionUpdate(position, pricing, greeks, ms.Timestamp);
                })
                // DistinctUntilChanged: skip repricing when theoretical price hasn't changed
                .DistinctUntilChanged(pu => pu.Pricing.TheoreticalPrice);
        });

        // ---------------------------------------------------------------
        // Step 6: Merge all position update streams
        // ---------------------------------------------------------------
        var allUpdates = positionStreams.Merge();

        // ---------------------------------------------------------------
        // Step 7-10: Buffer into 50ms micro-batches, filter empty,
        //            Scan to accumulate portfolio, Publish().RefCount()
        // ---------------------------------------------------------------
        var portfolio = allUpdates
            // ObserveOn: dispatch to task pool for async processing
            .ObserveOn(TaskPoolScheduler.Default)
            // Buffer: micro-batch at 50ms for efficiency
            .Buffer(TimeSpan.FromMilliseconds(50))
            // Filter: skip empty batches (no updates in this window)
            .Where(batch => batch.Count > 0)
            // Scan: accumulate into PortfolioSnapshot over time
            .Scan(PortfolioSnapshot.Empty, (snapshot, batch) =>
            {
                var result = snapshot.ApplyUpdates(batch);

                // Record latency for each update in this batch
                RecordLatency(batch);

                return result;
            })
            // Publish().RefCount(): multicast to multiple subscribers
            .Publish()
            .RefCount();

        // ---------------------------------------------------------------
        // Latency tracking via Window: rolling 5-second windows
        // ---------------------------------------------------------------
        var latencySubscription = portfolio
            .Window(TimeSpan.FromSeconds(5))
            .SelectMany(window => window.Count())
            .Subscribe(_ =>
            {
                // Window boundary reached; latency stats are accumulated in _latencySamples
                // Consumers can call GetLatencyStats() to retrieve p50/p95/p99
            });

        _subscriptions.Add(latencySubscription);

        return portfolio;
    }

    /// <summary>
    /// Records tick-to-portfolio latency for a batch of position updates.
    /// </summary>
    private void RecordLatency(IList<PositionUpdate> batch)
    {
        var now = DateTime.UtcNow;
        foreach (var update in batch)
        {
            double latencyMs = (now - update.Timestamp).TotalMilliseconds;
            _latencySamples.Enqueue(latencyMs);

            // Keep the queue bounded
            while (_latencySamples.Count > _maxLatencySamples)
            {
                _latencySamples.TryDequeue(out _);
            }
        }
    }

    /// <summary>
    /// Returns latency statistics (p50, p95, p99) from recent samples.
    /// </summary>
    public LatencyStats GetLatencyStats()
    {
        var samples = _latencySamples.ToArray();
        if (samples.Length == 0)
        {
            return new LatencyStats(0, 0, 0, 0, 0);
        }

        Array.Sort(samples);
        int count = samples.Length;

        double p50 = samples[(int)(count * 0.50)];
        double p95 = samples[Math.Min((int)(count * 0.95), count - 1)];
        double p99 = samples[Math.Min((int)(count * 0.99), count - 1)];
        double min = samples[0];
        double max = samples[count - 1];

        return new LatencyStats(p50, p95, p99, min, max);
    }

    /// <summary>
    /// Clears all latency samples.
    /// </summary>
    public void ResetLatencyStats()
    {
        while (_latencySamples.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _tickTimestamps.Clear();
    }
}

/// <summary>
/// Latency percentile statistics (all values in milliseconds).
/// </summary>
public record LatencyStats(
    double P50,
    double P95,
    double P99,
    double Min,
    double Max);
