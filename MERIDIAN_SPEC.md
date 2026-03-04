# Meridian — Real-Time Options Risk & PnL Engine

## Project Overview

Meridian is a real-time streaming analytics platform that simulates market data, prices an options portfolio, computes Greeks and PnL, aggregates risk metrics, and pushes alerts when configurable thresholds are breached. It demonstrates production-grade, low-latency, event-driven architecture using C# with Reactive Extensions (Rx) — the same stack used by DRW's UP Analytics Front Office team.

**Target audience:** Technical interviewers at HFT/prop trading firms evaluating distributed systems design, Rx fluency, and trading domain knowledge.

---

## Solution Structure

```
Meridian/
├── Meridian.sln
├── docker-compose.yml
├── .github/
│   └── workflows/
│       └── ci.yml                    # GitHub Actions CI/CD
├── src/
│   ├── Meridian.Common/              # Shared models, interfaces, contracts
│   │   ├── Models/
│   │   │   ├── MarketTick.cs         # Tick data: Symbol, Price, Timestamp, Volume
│   │   │   ├── Option.cs             # OptionType, Strike, Expiry, Underlier
│   │   │   ├── Position.cs           # Instrument, Quantity, EntryPrice
│   │   │   ├── GreeksSnapshot.cs     # Delta, Gamma, Vega, Theta, Rho per position
│   │   │   ├── PnlUpdate.cs         # Realized, Unrealized, Total PnL per position
│   │   │   ├── RiskAlert.cs          # AlertType, Threshold, CurrentValue, Timestamp
│   │   │   └── VolSurfacePoint.cs    # Strike, Expiry, ImpliedVol
│   │   ├── Interfaces/
│   │   │   ├── IPricingModel.cs      # Price(Option, MarketData) → PricingResult
│   │   │   ├── IMarketDataSource.cs  # IObservable<MarketTick> GetTickStream(symbol)
│   │   │   └── IRiskEvaluator.cs     # Evaluate(Portfolio) → RiskMetrics
│   │   ├── Configuration/
│   │   │   └── MeridianConfig.cs     # Strongly-typed config POCO
│   │   └── Meridian.Common.csproj
│   │
│   ├── Meridian.MarketData/          # Market data simulator service
│   │   ├── Simulation/
│   │   │   ├── GeometricBrownianMotion.cs   # GBM price path generator
│   │   │   ├── VolSurfaceGenerator.cs       # Simulated vol surface with smile
│   │   │   └── EventInjector.cs             # Vol spikes, price gaps, regime changes
│   │   ├── Publishing/
│   │   │   └── KafkaTickPublisher.cs        # Publish ticks to Kafka topics
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Dockerfile
│   │   └── Meridian.MarketData.csproj
│   │
│   ├── Meridian.Pricing/            # Rx-based pricing engine
│   │   ├── Models/
│   │   │   ├── BlackScholesPricer.cs        # BS model implementing IPricingModel
│   │   │   └── BinomialTreePricer.cs        # Alternative model (extensibility demo)
│   │   ├── Streams/
│   │   │   ├── MarketDataStream.cs          # Kafka consumer → IObservable<MarketTick>
│   │   │   ├── PricingPipeline.cs           # Core Rx pipeline: tick → price → greeks
│   │   │   └── GreeksAggregator.cs          # Position-level → portfolio-level greeks
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Dockerfile
│   │   └── Meridian.Pricing.csproj
│   │
│   ├── Meridian.Risk/               # PnL & risk aggregation service
│   │   ├── Aggregation/
│   │   │   ├── PnlCalculator.cs             # Streaming PnL: mark-to-market, realized
│   │   │   ├── RiskAggregator.cs            # Portfolio-level risk metrics
│   │   │   └── ThresholdMonitor.cs          # Configurable limits → RiskAlert stream
│   │   ├── State/
│   │   │   ├── RedisStateStore.cs           # Hot state: live positions, current greeks
│   │   │   └── SqlPositionRepository.cs     # Cold storage: trade history, audit trail
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Dockerfile
│   │   └── Meridian.Risk.csproj
│   │
│   ├── Meridian.Analytics/          # Python scenario analysis service
│   │   ├── app/
│   │   │   ├── main.py                      # FastAPI entry point
│   │   │   ├── scenarios.py                 # What-if: vol shock, rate shift, spot move
│   │   │   ├── pnl_attribution.py           # Historical PnL decomposition
│   │   │   └── models.py                    # Pydantic request/response models
│   │   ├── requirements.txt
│   │   ├── Dockerfile
│   │   └── README.md
│   │
│   └── Meridian.Dashboard/          # Real-time visualization
│       ├── (Blazor Server or React app)
│       ├── Pages/
│       │   ├── Portfolio.razor              # Live positions, PnL, Greeks table
│       │   ├── RiskMonitor.razor            # Risk limits status, alert feed
│       │   └── Analytics.razor              # Scenario analysis UI
│       ├── Hubs/
│       │   └── PricingHub.cs                # SignalR hub for WebSocket push
│       ├── Program.cs
│       ├── Dockerfile
│       └── Meridian.Dashboard.csproj
│
├── tests/
│   ├── Meridian.Common.Tests/
│   │   └── Models/                          # Model validation, serialization tests
│   ├── Meridian.Pricing.Tests/
│   │   ├── BlackScholesPricerTests.cs       # Verify against known BS values
│   │   ├── PricingPipelineTests.cs          # Rx pipeline unit tests with TestScheduler
│   │   └── GreeksAccuracyTests.cs           # Greeks vs finite-difference approximation
│   ├── Meridian.Risk.Tests/
│   │   ├── PnlCalculatorTests.cs
│   │   └── ThresholdMonitorTests.cs
│   ├── Meridian.Integration.Tests/
│   │   ├── EndToEndPipelineTests.cs         # Tick in → alert out, full path
│   │   └── LatencyBenchmarkTests.cs         # p50/p95/p99 tick-to-PnL latency
│   └── Meridian.Analytics.Tests/
│       └── test_scenarios.py                # pytest for Python service
│
└── docs/
    ├── architecture.md                      # System architecture with diagrams
    ├── rx-patterns.md                       # Rx design decisions and patterns used
    └── latency-profile.md                   # Benchmark results and analysis
```

---

## Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Core Services | C# / .NET 8 | All streaming services |
| Reactive Streams | System.Reactive (Rx.NET) | Observable pipelines, async composition |
| Message Bus | Apache Kafka (Confluent.Kafka) | Inter-service communication |
| Hot State Cache | Redis (StackExchange.Redis) | Live positions, current greeks |
| Persistence | SQL Server (Microsoft.Data.SqlClient) | Trade history, audit, position snapshots |
| Analytics Service | Python 3.12 / FastAPI | Scenario analysis, PnL attribution |
| Dashboard | Blazor Server + SignalR | Real-time WebSocket UI |
| Containerization | Docker + docker-compose | Full stack orchestration |
| CI/CD | GitHub Actions | Build, test, publish |
| Logging | Serilog + Seq | Structured logging with correlation IDs |
| Metrics | Prometheus + Grafana | Latency histograms, throughput counters |
| Testing | xUnit + Moq + TestScheduler | Unit, integration, and latency benchmarks |

### NuGet Packages (Key)

```xml
<!-- Meridian.Common -->
<PackageReference Include="System.Reactive" Version="6.*" />

<!-- Meridian.MarketData -->
<PackageReference Include="Confluent.Kafka" Version="2.*" />
<PackageReference Include="MathNet.Numerics" Version="5.*" />

<!-- Meridian.Pricing -->
<PackageReference Include="System.Reactive" Version="6.*" />
<PackageReference Include="Confluent.Kafka" Version="2.*" />
<PackageReference Include="StackExchange.Redis" Version="2.*" />

<!-- Meridian.Risk -->
<PackageReference Include="System.Reactive" Version="6.*" />
<PackageReference Include="StackExchange.Redis" Version="2.*" />
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />

<!-- All services -->
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Seq" Version="7.*" />
<PackageReference Include="prometheus-net" Version="8.*" />

<!-- Tests -->
<PackageReference Include="Microsoft.Reactive.Testing" Version="6.*" />
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="Moq" Version="4.*" />
<PackageReference Include="BenchmarkDotNet" Version="0.14.*" />
```

---

## Service Specifications

### 1. Meridian.Common — Shared Contracts

**Purpose:** Single source of truth for all models, interfaces, and configuration shared across services. No business logic here — just contracts.

#### Key Models

```csharp
public record MarketTick(
    string Symbol,
    decimal Price,
    decimal? ImpliedVol,
    DateTime Timestamp,
    long SequenceNumber);

public record Option(
    string Symbol,
    string Underlier,
    OptionType Type,        // Call, Put
    decimal Strike,
    DateTime Expiry,
    ExerciseStyle Style);   // European, American

public record Position(
    string PositionId,
    Option Instrument,
    int Quantity,            // +long, -short
    decimal EntryPrice,
    DateTime EntryTime);

public record GreeksSnapshot(
    string PositionId,
    decimal Delta,
    decimal Gamma,
    decimal Vega,
    decimal Theta,
    decimal Rho,
    DateTime Timestamp);

public record PnlUpdate(
    string PositionId,
    decimal MarkToMarket,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal TotalPnl,
    DateTime Timestamp);

public record RiskAlert(
    AlertType Type,          // DeltaBreached, VegaBreached, PnlDrawdown, etc.
    string Description,
    decimal Threshold,
    decimal CurrentValue,
    AlertSeverity Severity,  // Warning, Critical
    DateTime Timestamp);
```

#### Key Interfaces

```csharp
public interface IPricingModel
{
    string ModelName { get; }
    PricingResult Price(Option option, MarketSnapshot market);
    GreeksResult ComputeGreeks(Option option, MarketSnapshot market);
}

public interface IMarketDataSource
{
    IObservable<MarketTick> GetTickStream(string symbol);
    IObservable<MarketTick> GetAllTicksStream();
}

public interface IRiskEvaluator
{
    IObservable<RiskAlert> MonitorRisk(IObservable<PortfolioSnapshot> portfolio);
}
```

---

### 2. Meridian.MarketData — Simulator Service

**Purpose:** Generate realistic, high-frequency simulated market data ticks and publish to Kafka. This replaces a real market data feed.

#### Geometric Brownian Motion

```
dS = μSdt + σSdW

Where:
  S  = current price
  μ  = drift (annualized, e.g. 0.05)
  σ  = volatility (annualized, e.g. 0.20)
  dt = time step
  dW = Wiener process increment ~ N(0, √dt)
```

- Use MathNet.Numerics for random number generation
- Configurable tick rate: 100–10,000 ticks/second per symbol
- Support multiple underliers (e.g., 5–10 simulated equities)

#### Vol Surface Simulation

- Generate a basic vol smile per underlier
- ATM vol follows a mean-reverting process (Ornstein-Uhlenbeck)
- Wings steepen on large spot moves (skew dynamics)
- Publish vol surface updates at lower frequency (1–10 per second)

#### Event Injection

```csharp
public class EventInjector
{
    // Inject sudden vol spikes (earnings, macro events)
    public void InjectVolSpike(string symbol, decimal magnitude, TimeSpan duration);

    // Inject price gaps (overnight moves)
    public void InjectPriceGap(string symbol, decimal gapPercent);

    // Inject regime change (shift drift and vol levels)
    public void InjectRegimeChange(string symbol, decimal newDrift, decimal newVol);
}
```

#### Kafka Topics

| Topic | Key | Value | Frequency |
|-------|-----|-------|-----------|
| `market.ticks` | Symbol | MarketTick (JSON) | 100-10K/sec per symbol |
| `market.volsurface` | Symbol | VolSurface (JSON) | 1-10/sec per symbol |
| `market.events` | Symbol | MarketEvent (JSON) | On-demand |

#### Configuration (appsettings.json)

```json
{
  "MarketData": {
    "Symbols": [
      { "Symbol": "SIM_A", "InitialPrice": 100.0, "Drift": 0.05, "Volatility": 0.20 },
      { "Symbol": "SIM_B", "InitialPrice": 250.0, "Drift": 0.03, "Volatility": 0.30 },
      { "Symbol": "SIM_C", "InitialPrice": 50.0, "Drift": 0.08, "Volatility": 0.25 },
      { "Symbol": "SIM_D", "InitialPrice": 175.0, "Drift": 0.04, "Volatility": 0.18 },
      { "Symbol": "SIM_E", "InitialPrice": 80.0, "Drift": 0.06, "Volatility": 0.35 }
    ],
    "TicksPerSecond": 1000,
    "VolSurfaceUpdatesPerSecond": 5
  },
  "Kafka": {
    "BootstrapServers": "kafka:9092",
    "TicksTopic": "market.ticks",
    "VolSurfaceTopic": "market.volsurface"
  }
}
```

---

### 3. Meridian.Pricing — Rx Pricing Engine

**Purpose:** The core of the system. Consumes market data streams via Rx Observables, prices the portfolio in real-time, and computes streaming Greeks. This is the "money shot" service that demonstrates Rx fluency.

#### Rx Pipeline Architecture

This is the critical section. The pipeline should demonstrate sophisticated Rx composition:

```csharp
// CONCEPTUAL PIPELINE — demonstrates the Rx composition pattern
// Actual implementation should have proper error handling, logging, metrics

public class PricingPipeline : IDisposable
{
    private readonly CompositeDisposable _subscriptions = new();

    public IObservable<PortfolioUpdate> BuildPipeline(
        IObservable<MarketTick> ticks,
        IObservable<VolSurfaceUpdate> volUpdates,
        IEnumerable<Position> positions,
        IPricingModel model)
    {
        // 1. Combine latest spot + vol for each underlier
        var marketSnapshots = ticks
            .GroupBy(t => t.Symbol)
            .SelectMany(group =>
                group.CombineLatest(
                    volUpdates.Where(v => v.Symbol == group.Key),
                    (tick, vol) => new MarketSnapshot(tick, vol)));

        // 2. For each position, reprice on every relevant market update
        var positionUpdates = positions.Select(pos =>
            marketSnapshots
                .Where(snap => snap.Symbol == pos.Instrument.Underlier)
                .Select(snap => new
                {
                    Position = pos,
                    Pricing = model.Price(pos.Instrument, snap),
                    Greeks = model.ComputeGreeks(pos.Instrument, snap),
                    Timestamp = snap.Timestamp
                })
                .DistinctUntilChanged(x => x.Pricing.TheoreticalPrice));

        // 3. Merge all position streams, aggregate to portfolio level
        var portfolioStream = positionUpdates
            .Merge()
            .Buffer(TimeSpan.FromMilliseconds(50))  // Micro-batch for efficiency
            .Where(batch => batch.Count > 0)
            .Scan(PortfolioSnapshot.Empty, (portfolio, updates) =>
                portfolio.ApplyUpdates(updates))
            .Publish()
            .RefCount();

        return portfolioStream;
    }
}
```

#### Rx Patterns to Demonstrate

These specific Rx operators and patterns should be visible in the codebase:

| Pattern | Where Used | Why |
|---------|-----------|-----|
| `GroupBy` | Group ticks by symbol | Partition streams per instrument |
| `CombineLatest` | Merge spot + vol | Price with latest of both |
| `Scan` | Portfolio accumulation | Stateful aggregation over time |
| `Buffer(TimeSpan)` | Micro-batching | Reduce downstream pressure |
| `DistinctUntilChanged` | Price dedup | Only reprice on meaningful changes |
| `Publish().RefCount()` | Shared subscription | Multicast to PnL + Risk + Dashboard |
| `ObserveOn(TaskPoolScheduler)` | Async dispatch | Non-blocking downstream processing |
| `Retry` / `Catch` | Error handling | Resilient stream recovery |
| `Throttle` | Dashboard updates | Rate-limit UI pushes |
| `Window` | Latency measurement | Rolling performance windows |
| `Merge` | Combine position streams | Single portfolio-level stream |
| `WithLatestFrom` | Risk limit checks | Latest threshold config per update |

#### Black-Scholes Implementation

```csharp
public class BlackScholesPricer : IPricingModel
{
    public string ModelName => "Black-Scholes";

    public PricingResult Price(Option option, MarketSnapshot market)
    {
        // Standard BS formula
        // d1 = [ln(S/K) + (r + σ²/2)T] / (σ√T)
        // d2 = d1 - σ√T
        // Call = S·N(d1) - K·e^(-rT)·N(d2)
        // Put  = K·e^(-rT)·N(-d2) - S·N(-d1)
    }

    public GreeksResult ComputeGreeks(Option option, MarketSnapshot market)
    {
        // Analytical Greeks from BS
        // Delta = N(d1) for calls, N(d1)-1 for puts
        // Gamma = φ(d1) / (S·σ·√T)
        // Vega  = S·φ(d1)·√T
        // Theta = -(S·φ(d1)·σ)/(2√T) - rKe^(-rT)N(d2) [call]
        // Rho   = KTe^(-rT)N(d2) [call]
    }
}
```

Use MathNet.Numerics for `Normal.CDF` and `Normal.PDF`.

#### Binomial Tree (Extensibility Demo)

Implement `BinomialTreePricer : IPricingModel` as a second model option. Doesn't need to be fast — it's there to prove the interface-based architecture supports model pluggability. Can handle American exercise style as a bonus.

---

### 4. Meridian.Risk — PnL & Risk Aggregation

**Purpose:** Subscribe to portfolio updates from the pricing engine, compute PnL, aggregate risk, and fire alerts when thresholds breach.

#### PnL Calculation

```csharp
public class PnlCalculator
{
    // Mark-to-market: (CurrentPrice - EntryPrice) × Quantity
    // Unrealized: MTM for open positions
    // Realized: Closed positions locked in
    // Total: Unrealized + Realized

    public IObservable<PnlUpdate> ComputePnl(
        IObservable<PortfolioSnapshot> portfolio)
    {
        return portfolio
            .Select(snap => snap.Positions.Select(pos =>
                new PnlUpdate(
                    pos.PositionId,
                    MarkToMarket: (pos.CurrentPrice - pos.EntryPrice) * pos.Quantity,
                    ...)))
            .SelectMany(updates => updates);
    }
}
```

#### Risk Threshold Configuration

```json
{
  "RiskLimits": {
    "MaxPortfolioDelta": 500,
    "MaxPortfolioGamma": 100,
    "MaxPortfolioVega": 10000,
    "MaxPositionDelta": 100,
    "MaxDrawdownPercent": 5.0,
    "MaxSingleNameConcentration": 0.30,
    "AlertCooldownSeconds": 30
  }
}
```

#### State Management

- **Redis (hot):** Current positions, live greeks, latest PnL per position. Key schema: `position:{id}:greeks`, `portfolio:aggregate`, `pnl:{id}:latest`
- **SQL Server (cold):** Trade blotter, position snapshots (every N seconds), risk alert history, PnL time series for the analytics service

#### SQL Schema (Core Tables)

```sql
CREATE TABLE Positions (
    PositionId      VARCHAR(50)   PRIMARY KEY,
    Symbol          VARCHAR(20)   NOT NULL,
    Underlier       VARCHAR(20)   NOT NULL,
    OptionType      VARCHAR(4)    NOT NULL,  -- CALL/PUT
    Strike          DECIMAL(18,6) NOT NULL,
    Expiry          DATE          NOT NULL,
    Quantity        INT           NOT NULL,
    EntryPrice      DECIMAL(18,6) NOT NULL,
    EntryTime       DATETIME2     NOT NULL,
    IsOpen          BIT           NOT NULL DEFAULT 1
);

CREATE TABLE PnlSnapshots (
    Id              BIGINT IDENTITY PRIMARY KEY,
    PositionId      VARCHAR(50)   NOT NULL,
    MarkToMarket    DECIMAL(18,6),
    UnrealizedPnl   DECIMAL(18,6),
    TotalPnl        DECIMAL(18,6),
    SnapshotTime    DATETIME2     NOT NULL,
    INDEX IX_PnlSnapshots_Time (SnapshotTime)
);

CREATE TABLE GreeksSnapshots (
    Id              BIGINT IDENTITY PRIMARY KEY,
    PositionId      VARCHAR(50)   NOT NULL,
    Delta           DECIMAL(18,8),
    Gamma           DECIMAL(18,8),
    Vega            DECIMAL(18,8),
    Theta           DECIMAL(18,8),
    Rho             DECIMAL(18,8),
    SnapshotTime    DATETIME2     NOT NULL,
    INDEX IX_GreeksSnapshots_Time (SnapshotTime)
);

CREATE TABLE RiskAlerts (
    Id              BIGINT IDENTITY PRIMARY KEY,
    AlertType       VARCHAR(50)   NOT NULL,
    Severity        VARCHAR(20)   NOT NULL,
    Threshold       DECIMAL(18,6),
    CurrentValue    DECIMAL(18,6),
    Description     NVARCHAR(500),
    AlertTime       DATETIME2     NOT NULL,
    AcknowledgedAt  DATETIME2     NULL
);

CREATE TABLE TradeBlotter (
    TradeId         BIGINT IDENTITY PRIMARY KEY,
    PositionId      VARCHAR(50)   NOT NULL,
    Side            VARCHAR(4)    NOT NULL,  -- BUY/SELL
    Quantity        INT           NOT NULL,
    Price           DECIMAL(18,6) NOT NULL,
    TradeTime       DATETIME2     NOT NULL
);
```

---

### 5. Meridian.Analytics — Python Scenario Service

**Purpose:** Expose HTTP endpoints for scenario analysis and historical PnL attribution. Shows cross-language fluency per the JD requirement.

#### Endpoints

```
GET  /health
POST /scenarios/vol-shock     { "symbol": "SIM_A", "shockPercent": 20.0 }
POST /scenarios/spot-move     { "symbol": "SIM_A", "movePercent": -5.0 }
POST /scenarios/rate-shift    { "shiftBps": 25 }
POST /scenarios/combined      { "volShock": 10, "spotMove": -3, "rateShift": 10 }
GET  /pnl/attribution/{positionId}?from=...&to=...
GET  /pnl/summary?from=...&to=...
```

#### Tech

- FastAPI with Pydantic models
- NumPy/SciPy for pricing math
- Reads historical data from SQL Server (pyodbc or SQLAlchemy)
- Returns JSON with scenario P&L impact, Greeks shift, portfolio impact

---

### 6. Meridian.Dashboard — Real-Time Visualization

**Purpose:** Visual proof the system works. Not the main attraction but makes it demo-able.

#### Recommended: Blazor Server

Rationale: Keeps entire project in C#/.NET ecosystem. SignalR is built in. Matches your stack.

#### Pages

| Page | Content |
|------|---------|
| Portfolio | Live positions table: symbol, qty, entry, current, PnL, greeks. Color-coded green/red. Auto-updates via SignalR. |
| Risk Monitor | Portfolio-level greeks gauges (delta, gamma, vega). Risk limit bars showing current vs. max. Live alert feed. |
| Analytics | Form to run scenarios. Results table/chart showing P&L impact. |
| Latency | Live p50/p95/p99 latency chart. Throughput counter. System health. |

#### SignalR Hub

```csharp
public class PricingHub : Hub
{
    // Push portfolio updates to connected clients
    public async Task SendPortfolioUpdate(PortfolioSnapshot snapshot)
        => await Clients.All.SendAsync("PortfolioUpdate", snapshot);

    public async Task SendRiskAlert(RiskAlert alert)
        => await Clients.All.SendAsync("RiskAlert", alert);

    public async Task SendLatencyMetrics(LatencyMetrics metrics)
        => await Clients.All.SendAsync("LatencyUpdate", metrics);
}
```

---

## Cross-Cutting Concerns

### Latency Instrumentation

This is a key differentiator. Bake latency measurement into the pipeline from day one.

```csharp
public class LatencyTracker
{
    private readonly Histogram _tickToPriceLatency;
    private readonly Histogram _tickToPnlLatency;
    private readonly Histogram _tickToAlertLatency;

    // Stamp SequenceNumber + HighResolutionTimestamp at ingestion
    // Measure at each pipeline stage
    // Report p50, p95, p99 via Prometheus histograms

    // Target: sub-millisecond tick-to-price, <5ms tick-to-PnL
}
```

Expose via Prometheus `/metrics` endpoint. Grafana dashboard in `docker-compose`.

### Structured Logging

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.WithProperty("Service", "Meridian.Pricing")
    .Enrich.WithCorrelationId()          // Trace a tick through the pipeline
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq("http://seq:5341")
    .CreateLogger();
```

Every log entry includes: `ServiceName`, `CorrelationId` (tied to tick SequenceNumber), `Timestamp`, `Level`, structured properties.

### Health Checks

Each service exposes `/health` with:
- Kafka connectivity
- Redis connectivity (where applicable)
- SQL Server connectivity (where applicable)
- Pipeline throughput (ticks/sec processed in last 10s)

---

## Docker Compose

```yaml
version: '3.8'

services:
  zookeeper:
    image: confluentinc/cp-zookeeper:7.5.0
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181

  kafka:
    image: confluentinc/cp-kafka:7.5.0
    depends_on: [zookeeper]
    ports: ["9092:9092"]
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Meridian_Dev_123!"
    ports: ["1433:1433"]

  seq:
    image: datalust/seq:latest
    environment:
      ACCEPT_EULA: "Y"
    ports: ["5341:5341", "8081:80"]

  prometheus:
    image: prom/prometheus:latest
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    ports: ["9090:9090"]

  grafana:
    image: grafana/grafana:latest
    ports: ["3000:3000"]
    volumes:
      - ./grafana/dashboards:/var/lib/grafana/dashboards

  marketdata:
    build: ./src/Meridian.MarketData
    depends_on: [kafka]

  pricing:
    build: ./src/Meridian.Pricing
    depends_on: [kafka, redis]

  risk:
    build: ./src/Meridian.Risk
    depends_on: [kafka, redis, sqlserver]

  analytics:
    build: ./src/Meridian.Analytics
    depends_on: [sqlserver]
    ports: ["8082:8000"]

  dashboard:
    build: ./src/Meridian.Dashboard
    depends_on: [pricing, risk]
    ports: ["8080:8080"]
```

---

## Testing Strategy

### Unit Tests (xUnit + Moq)

| Test Class | What It Validates |
|-----------|------------------|
| `BlackScholesPricerTests` | BS prices match known analytical values (use published option pricing tables). Test put-call parity. Edge cases: deep ITM/OTM, near-expiry. |
| `PricingPipelineTests` | Rx pipeline correctness using `TestScheduler`. Verify ticks flow through → prices → greeks. Test backpressure, error recovery. |
| `GreeksAccuracyTests` | Analytical greeks vs. finite-difference numerical approximation. Should agree within tolerance. |
| `PnlCalculatorTests` | PnL math: MTM, unrealized, realized. Portfolio aggregation. |
| `ThresholdMonitorTests` | Alerts fire when limits breached. Cooldown prevents spam. |
| `GbmSimulatorTests` | Statistical properties: drift, variance over many paths should match expected. |

### Integration Tests

| Test | Scope |
|------|-------|
| `EndToEndPipelineTests` | Docker compose up → inject ticks → verify PnL updates arrive. Full pipeline. |
| `KafkaRoundtripTests` | Publish tick → consume tick. Verify serialization. |
| `RedisStateTests` | Write/read positions and greeks. Verify consistency. |

### Latency Benchmarks (BenchmarkDotNet)

```csharp
[MemoryDiagnoser]
public class PricingBenchmarks
{
    [Benchmark]
    public PricingResult BlackScholesPrice() => _pricer.Price(_option, _market);

    [Benchmark]
    public GreeksResult BlackScholesGreeks() => _pricer.ComputeGreeks(_option, _market);
}
```

Capture and document: single-price latency, throughput (prices/sec), pipeline end-to-end p50/p95/p99.

---

## CI/CD — GitHub Actions

```yaml
name: Meridian CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test --no-build --verbosity normal
        # Unit tests only — integration tests require docker-compose

  integration:
    runs-on: ubuntu-latest
    needs: build-and-test
    steps:
      - uses: actions/checkout@v4
      - run: docker-compose up -d
      - run: dotnet test tests/Meridian.Integration.Tests --filter Category=Integration
      - run: docker-compose down
```

---

## Build Phases

### Phase 1 — Core Pipeline (Week 1)
- [ ] Solution structure, Common project with models and interfaces
- [ ] GBM market data simulator (console output first, no Kafka yet)
- [ ] BlackScholes pricer with full Greeks
- [ ] Core Rx pipeline: simulated ticks → pricing → greeks → console output
- [ ] Unit tests for pricer and pipeline (TestScheduler)
- [ ] Dockerfiles for each service

**Milestone:** Run `dotnet run` on Pricing project, see streaming prices and Greeks in console.

### Phase 2 — Infrastructure Integration (Week 2)
- [ ] Kafka integration: MarketData publishes, Pricing consumes
- [ ] Redis state store for live positions and greeks
- [ ] SQL Server schema + position repository
- [ ] PnL calculator with streaming computation
- [ ] Risk threshold monitor with alert generation
- [ ] docker-compose bringing up full infrastructure stack
- [ ] Integration tests

**Milestone:** `docker-compose up` runs full pipeline end-to-end. Ticks flow through Kafka, PnL and greeks land in Redis, alerts fire on threshold breach.

### Phase 3 — Analytics & Observability (Week 2-3)
- [ ] Python FastAPI scenario service
- [ ] PnL attribution endpoint
- [ ] Serilog + Seq structured logging across all services
- [ ] Prometheus metrics + Grafana latency dashboard
- [ ] Health check endpoints
- [ ] Latency benchmarks documented
- [ ] BinomialTree pricer (extensibility demo)

**Milestone:** Full observability. Can see logs in Seq, metrics in Grafana. Python scenarios work.

### Phase 4 — Dashboard & Polish (Week 3)
- [ ] Blazor dashboard with SignalR
- [ ] Portfolio page, Risk Monitor page, Analytics page
- [ ] Event injection UI (trigger vol spikes, gaps)
- [ ] Architecture docs with diagrams
- [ ] Rx patterns documentation
- [ ] README with quickstart, screenshots, architecture overview
- [ ] GitHub Actions CI pipeline
- [ ] Clean up, code review pass

**Milestone:** Clone → `docker-compose up` → open browser → see live streaming portfolio with risk monitoring. README tells the whole story.

---

## README Structure (for GitHub)

```markdown
# Meridian — Real-Time Options Risk & PnL Engine

[One-line description]

## Architecture
[Diagram showing services, Kafka, Redis, SQL, data flow]

## Quick Start
docker-compose up
Open http://localhost:8080

## Tech Stack
[Table mapping to JD requirements]

## Key Design Decisions
- Why Rx for stream composition
- Why Kafka over raw sockets
- Why micro-batching for PnL aggregation
- Interface-based pricing models

## Performance
[Latency benchmarks: p50/p95/p99]
[Throughput: ticks/sec, prices/sec]

## Testing
dotnet test

## Project Structure
[Abbreviated tree]
```

---

## Design Principles

1. **Rx-first composition** — Every data flow is an Observable pipeline. No polling, no callbacks, no manual threading. This is the primary demonstration of the project.

2. **Interface-based extensibility** — `IPricingModel` means swapping Black-Scholes for SABR or a neural net pricer is a config change, not a rewrite.

3. **Operational maturity** — Structured logging, health checks, latency metrics, and alerting are not afterthoughts. They're built in from Phase 1.

4. **Test-driven confidence** — Pricer accuracy validated against known values. Rx pipelines tested with `TestScheduler`. Integration tests prove end-to-end correctness.

5. **Clean separation** — Each service owns its concern. Common contracts prevent coupling. Kafka provides loose coupling between services.

6. **Production patterns in a portfolio project** — This isn't a toy. It uses the same patterns (backpressure, error recovery, state management, observability) you'd use in a real trading system. The scale is smaller, but the architecture is real.
