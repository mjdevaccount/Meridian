# Meridian -- Real-Time Options Risk & PnL Engine

A production-grade, event-driven options risk and PnL engine demonstrating low-latency stream processing with Rx.NET, Apache Kafka, Redis, and SQL Server. Built to showcase the architecture patterns used in quantitative trading systems.

## Architecture

```
┌─────────────────┐     ┌──────────┐     ┌──────────────────┐     ┌──────────┐
│  Market Data    │────>│  Kafka   │────>│  Pricing Engine  │────>│  Kafka   │──┐
│  Simulator      │     │          │     │  (Rx Pipeline)   │     │          │  │
│  (GBM + OU Vol) │     │          │     │  Black-Scholes   │     │          │  │
└─────────────────┘     └──────────┘     │  Binomial Tree   │     └──────────┘  │
                                         └──────────────────┘                    │
                                                                                 │
┌─────────────────┐     ┌──────────┐     ┌──────────────────┐                    │
│   Dashboard     │<───>│  Redis   │<────│  Risk Service    │<───────────────────┘
│   (Blazor +     │     │  (Hot    │     │  PnL Calculator  │
│    SignalR)     │     │   State) │     │  Risk Aggregator │
└─────────────────┘     └──────────┘     │  Threshold Mon.  │
                                         └────────┬─────────┘
┌─────────────────┐                               │
│  Scenarios API  │     ┌──────────┐              │
│  (Python/FastAPI)│<───│ SQL Server│<─────────────┘
│  Vol Shock, PnL │     │  (Cold   │
│  Attribution    │     │   Store) │
└─────────────────┘     └──────────┘
```

## Quick Start

```bash
git clone https://github.com/mjdevaccount/Meridian.git
cd Meridian
docker-compose up --build
```

Open:
- **Dashboard:** http://localhost:8080
- **Grafana:** http://localhost:3000
- **Seq Logs:** http://localhost:8081
- **Scenarios API:** http://localhost:8000/docs
- **Prometheus:** http://localhost:9090

## Tech Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Stream Processing | Rx.NET (System.Reactive) | Real-time pipeline composition |
| Messaging | Apache Kafka | Inter-service event streaming |
| Hot State | Redis | Live position Greeks, PnL cache |
| Cold Storage | SQL Server | Trade history, PnL snapshots, audit |
| Pricing | Black-Scholes, Binomial Tree | Option pricing with full Greeks |
| Market Simulation | GBM + Ornstein-Uhlenbeck | Realistic price paths + vol surfaces |
| Dashboard | Blazor Server + SignalR | Real-time visualization |
| Scenarios | Python FastAPI | Cross-language scenario analysis |
| Observability | Serilog -> Seq, Prometheus -> Grafana | Structured logging + metrics |
| Testing | xUnit, 66 tests | Pricer accuracy, pipeline behavior |

## Rx Pipeline Patterns

The pricing pipeline demonstrates these key Rx operators:

| Operator | Usage |
|----------|-------|
| `GroupBy` | Partition ticks by symbol |
| `CombineLatest` | Merge spot + vol for each underlier |
| `Scan` | Portfolio accumulation over time |
| `Buffer` | 50ms micro-batching for efficiency |
| `DistinctUntilChanged` | Skip repricing when price unchanged |
| `Publish().RefCount()` | Multicast to multiple subscribers |
| `ObserveOn(TaskPoolScheduler)` | Async dispatch to thread pool |
| `Merge` | Combine per-position streams |
| `Window` | Rolling latency measurement |
| `Throttle` | Rate-limit output streams |

## Key Design Decisions

### Why Rx for stream composition
Every data flow is an Observable pipeline. No polling, no callbacks, no manual threading. Rx provides backpressure handling, error recovery, and composable stream transformations -- the same patterns used in production HFT systems.

### Why Kafka over raw sockets
Kafka provides durable, ordered, replayable message delivery between services. Consumer groups enable independent scaling of pricing and risk services. Topic partitioning by symbol enables parallel processing.

### Why micro-batching for PnL aggregation
The 50ms `Buffer` window reduces Scan operations by ~95% while keeping latency well under 100ms. This is the standard approach in production risk systems to balance throughput and latency.

### Interface-based pricing models
`IPricingModel` allows swapping Black-Scholes for Binomial Tree (or SABR, neural net, etc.) without changing the pipeline. Demonstrated with two working implementations.

## Performance

| Metric | Value |
|--------|-------|
| p50 latency | ~29 ms |
| p95 latency | ~53 ms |
| p99 latency | ~57 ms |
| Min latency | ~1.4 ms |
| Tick-to-price | < 1 ms |
| Throughput | 5,000 ticks/sec (5 symbols x 1,000/sec) |
| Portfolio | 12 positions, 5 underliers |

See [docs/LATENCY_BENCHMARKS.md](docs/LATENCY_BENCHMARKS.md) for detailed analysis.

## Testing

```bash
dotnet test Meridian.sln
```

66 tests covering:
- Black-Scholes pricing accuracy (ATM, ITM, OTM, put-call parity)
- Greeks accuracy via finite differences (all 5 Greeks)
- Rx pipeline behavior (snapshot production, multi-position, filtering)
- Binomial Tree convergence to Black-Scholes
- American vs European exercise validation

## Project Structure

```
Meridian/
├── src/
│   ├── Meridian.Common/          # Shared models, interfaces, config
│   ├── Meridian.MarketData/      # GBM simulator, vol surfaces, Kafka publisher
│   ├── Meridian.Pricing/         # Black-Scholes, Binomial Tree, Rx pipeline
│   ├── Meridian.Risk/            # PnL calculator, risk aggregator, alerts
│   ├── Meridian.Dashboard/       # Blazor Server + SignalR dashboard
│   └── Meridian.Scenarios/       # Python FastAPI scenario analysis
├── tests/
│   └── Meridian.Pricing.Tests/   # 66 xUnit tests
├── docker-compose.yml            # Full stack orchestration
├── prometheus.yml                # Metrics scraping config
└── grafana/                      # Pre-provisioned dashboards
```

## Services

| Service | Port | Description |
|---------|------|-------------|
| Dashboard | 8080 | Blazor real-time portfolio view |
| Scenarios API | 8000 | Python FastAPI scenario analysis |
| Seq | 8081 | Structured log viewer |
| Grafana | 3000 | Metrics dashboards |
| Prometheus | 9090 | Metrics collection |
| Kafka | 9092 | Message broker |
| Redis | 6379 | Hot state cache |
| SQL Server | 1433 | Cold storage |
