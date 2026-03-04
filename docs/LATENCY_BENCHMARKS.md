# Meridian Latency Benchmarks

## Measurement Methodology

Latency is measured end-to-end: from `MarketTick.Timestamp` (when the GBM simulator generates a tick) through the full Rx pipeline to when the `PortfolioSnapshot` is produced. Measurements use `DateTime.UtcNow` at the snapshot boundary minus the original tick timestamp.

The pipeline processes ticks through these stages:
1. **Tick generation** (GBM) → Kafka publish
2. **Kafka transit** (producer → broker → consumer)
3. **Rx pipeline**: GroupBy → CombineLatest (spot + vol) → Price + Greeks → DistinctUntilChanged → Merge → Buffer(50ms) → Scan (accumulate)

Latency samples are collected in a bounded `ConcurrentQueue<double>` (10,000 samples) and reported as percentiles every 5 seconds. Prometheus histograms provide additional bucket-level granularity.

## Results (Docker Compose, single machine)

**Environment:** Windows 11, Docker Desktop, 5 underliers, 12 positions, 1000 ticks/sec per symbol.

| Metric | Value |
|--------|-------|
| **p50** | ~29 ms |
| **p95** | ~53 ms |
| **p99** | ~57 ms |
| **min** | ~1.4 ms |
| **max** | ~65 ms |

### Key observations

- The 50ms `Buffer` window in the Rx pipeline is the dominant contributor to median latency. Ticks arriving just after a buffer flush wait up to 50ms for the next batch.
- p95/p99 are tightly clustered around 53-57ms, confirming the buffer window as the ceiling.
- Minimum latency of ~1.4ms shows the pipeline itself (pricing + Greeks computation) is sub-2ms when a tick arrives just before a buffer flush.
- **Tick-to-price latency** (before buffering) is sub-millisecond, dominated by the Black-Scholes analytical computation.

### Prometheus histogram buckets

```
le="1"    → 6% of samples   (sub-1ms: tick arrives at buffer boundary)
le="5"    → 6%
le="10"   → 6%
le="25"   → 6%
le="50"   → 100%            (all samples under 50ms buffer ceiling)
le="100"  → 100%
```

## Tuning considerations

| Knob | Effect |
|------|--------|
| `Buffer(TimeSpan)` | Reducing from 50ms to 10ms lowers p50 to ~5ms but increases CPU from more frequent Scan operations |
| `ObserveOn(TaskPoolScheduler)` | Ensures pipeline doesn't block the tick ingestion thread |
| `DistinctUntilChanged` | Reduces unnecessary repricing when spot hasn't moved (saves ~15% compute) |
| `Publish().RefCount()` | Multicasts to multiple subscribers without duplicating upstream work |

## Monitoring

- **Seq** (http://localhost:8081): Structured logs with per-service filtering
- **Prometheus** (http://localhost:9090): Raw metric scraping at 5s intervals
- **Grafana** (http://localhost:3000): Pre-provisioned dashboard with latency percentiles, PnL, delta, throughput, and alert counters
