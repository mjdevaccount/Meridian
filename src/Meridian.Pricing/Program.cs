using System.Reactive.Disposables;
using System.Reactive.Linq;
using Meridian.Common.Configuration;
using Meridian.Common.Health;
using Meridian.Common.Messaging;
using Meridian.Common.Models;
using Meridian.MarketData.Simulation;
using Meridian.Pricing.Models;
using Meridian.Pricing.Streams;
using Microsoft.Extensions.Configuration;
using Prometheus;
using Serilog;

namespace Meridian.Pricing;

/// <summary>
/// Phase 2 console application: pricing pipeline that can consume market data
/// from Kafka (when KAFKA_BOOTSTRAP env var is set) or from the in-process simulator.
/// </summary>
public class Program
{
    // Prometheus metrics
    private static readonly Histogram PricingLatency = Metrics.CreateHistogram(
        "meridian_pricing_latency_ms", "Tick-to-price latency in milliseconds",
        new HistogramConfiguration
        {
            Buckets = new double[] { 1, 5, 10, 25, 50, 100, 250, 500 }
        });
    private static readonly Gauge PortfolioPnl = Metrics.CreateGauge(
        "meridian_portfolio_pnl", "Current portfolio PnL");
    private static readonly Gauge PortfolioDelta = Metrics.CreateGauge(
        "meridian_portfolio_delta", "Current portfolio delta");
    private static readonly Counter SnapshotsTotal = Metrics.CreateCounter(
        "meridian_snapshots_total", "Total portfolio snapshots produced");

    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.WithProperty("Service", "Meridian.Pricing")
            .WriteTo.Console()
            .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
            .CreateLogger();

        Log.Information("Meridian Pricing Engine - Phase 2 starting");

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== Meridian Pricing Engine - Phase 2 ===");
        Console.WriteLine();

        // Start Prometheus metrics server on port 9101
        var metricServer = new MetricServer(port: 9101);
        try
        {
            metricServer.Start();
            Log.Information("Prometheus metrics server started on port {Port}", 9101);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start Prometheus metrics server on port {Port}", 9101);
        }

        // -----------------------------------------------------------------
        // Load configuration
        // -----------------------------------------------------------------
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var marketDataConfig = new MarketDataConfig();
        configuration.GetSection("MarketData").Bind(marketDataConfig);

        var riskFreeRate = configuration.GetValue<decimal>("Pricing:RiskFreeRate", 0.05m);
        var microBatchMs = configuration.GetValue<int>("Pricing:MicroBatchIntervalMs", 50);

        var sqlConnection = Environment.GetEnvironmentVariable("SQL_CONNECTION")
            ?? configuration.GetValue<string>("SqlServer:ConnectionString") ?? "";

        // -----------------------------------------------------------------
        // Determine data source: Kafka or in-process simulator
        // -----------------------------------------------------------------
        IObservable<MarketTick> allTicks;
        IObservable<VolSurfaceUpdate> allVolUpdates;
        IDisposable? dataSource = null;

        var kafkaBootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP")
            ?? configuration.GetValue<string>("Kafka:BootstrapServers");

        if (!string.IsNullOrEmpty(kafkaBootstrap) && kafkaBootstrap != "localhost:9092")
        {
            // Kafka mode: consume from Kafka topics
            Log.Information("Using Kafka data source at {KafkaBootstrap}", kafkaBootstrap);
            Console.WriteLine($"Using Kafka at {kafkaBootstrap}");
            var ticksTopic = configuration.GetValue<string>("Kafka:TicksTopic") ?? "market.ticks";
            var volSurfaceTopic = configuration.GetValue<string>("Kafka:VolSurfaceTopic") ?? "market.volsurface";

            var stream = new MarketDataStream(kafkaBootstrap, ticksTopic, volSurfaceTopic);
            allTicks = stream.GetAllTicksStream();
            allVolUpdates = stream.GetVolSurfaceStream();
            dataSource = stream;

            Console.WriteLine($"  Ticks topic: {ticksTopic}");
            Console.WriteLine($"  Vol surface topic: {volSurfaceTopic}");
        }
        else
        {
            // In-process simulator mode
            Log.Information("Using in-process simulator data source");
            Console.WriteLine("Using in-process simulator");

            var gbmSimulators = new Dictionary<string, GeometricBrownianMotion>();
            var volGenerators = new Dictionary<string, VolSurfaceGenerator>();

            foreach (var sym in marketDataConfig.Symbols)
            {
                var gbm = new GeometricBrownianMotion(sym.Symbol, sym.InitialPrice, sym.Drift, sym.Volatility);
                gbmSimulators[sym.Symbol] = gbm;

                var volGen = new VolSurfaceGenerator(sym.Symbol, (double)sym.Volatility);
                volGenerators[sym.Symbol] = volGen;
            }

            Console.WriteLine($"Configured {gbmSimulators.Count} underliers: {string.Join(", ", gbmSimulators.Keys)}");
            Console.WriteLine($"Tick rate: {marketDataConfig.TicksPerSecond}/sec | Vol updates: {marketDataConfig.VolSurfaceUpdatesPerSecond}/sec");

            var tickStreams = gbmSimulators.Values
                .Select(gbm => gbm.GenerateTickStream(marketDataConfig.TicksPerSecond))
                .ToArray();
            allTicks = tickStreams.Merge();

            var volStreams = gbmSimulators.Select(kvp =>
            {
                var symbol = kvp.Key;
                var gbm = kvp.Value;
                var volGen = volGenerators[symbol];

                var spotTicks = gbm.GenerateTickStream(marketDataConfig.TicksPerSecond);
                return volGen.GenerateVolSurfaceStream(spotTicks, marketDataConfig.VolSurfaceUpdatesPerSecond);
            }).ToArray();
            allVolUpdates = volStreams.Merge();
        }

        Log.Information("Risk-free rate: {RiskFreeRate}, Micro-batch interval: {MicroBatchMs}ms", riskFreeRate, microBatchMs);
        Console.WriteLine($"Risk-free rate: {riskFreeRate:P2} | Micro-batch: {microBatchMs}ms");
        Console.WriteLine();

        // -----------------------------------------------------------------
        // Build sample portfolio: ~12 option positions across 5 underliers
        // Mix of calls/puts, long/short, various strikes and expiries
        // -----------------------------------------------------------------
        // Load positions from SQL or seed with sample portfolio
        var now = DateTime.UtcNow;
        List<Position> positions;

        if (!string.IsNullOrEmpty(sqlConnection))
        {
            try
            {
                positions = await LoadPositionsFromSql(sqlConnection);
                if (positions.Count == 0)
                {
                    Log.Information("No positions in database, using sample portfolio");
                    positions = BuildSamplePortfolio(marketDataConfig.Symbols, now);
                }
                else
                {
                    Log.Information("Loaded {Count} positions from SQL", positions.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SQL unavailable, using sample portfolio");
                positions = BuildSamplePortfolio(marketDataConfig.Symbols, now);
            }
        }
        else
        {
            positions = BuildSamplePortfolio(marketDataConfig.Symbols, now);
        }

        Console.WriteLine($"Portfolio: {positions.Count} positions");
        Console.WriteLine(new string('-', 90));
        Console.WriteLine($"{"ID",-12} {"Underlier",-8} {"Type",-5} {"Strike",8} {"Expiry",-12} {"Qty",5} {"Entry$",8}");
        Console.WriteLine(new string('-', 90));
        foreach (var pos in positions)
        {
            Console.WriteLine($"{pos.PositionId,-12} {pos.Instrument.Underlier,-8} {pos.Instrument.Type,-5} " +
                              $"{pos.Instrument.Strike,8:F2} {pos.Instrument.Expiry:yyyy-MM-dd}   {pos.Quantity,5} {pos.EntryPrice,8:F2}");
        }
        Console.WriteLine(new string('-', 90));
        Console.WriteLine();

        // -----------------------------------------------------------------
        // Create pricer and pipeline
        // -----------------------------------------------------------------
        var pricer = new BlackScholesPricer();
        using var pipeline = new PricingPipeline();
        var aggregator = new GreeksAggregator(TimeSpan.FromMilliseconds(500));

        var portfolioStream = pipeline.BuildPipeline(
            allTicks,
            allVolUpdates,
            positions,
            pricer,
            riskFreeRate);

        // -----------------------------------------------------------------
        // Subscribe: Portfolio-level summary (every update)
        // -----------------------------------------------------------------
        var subscriptions = new CompositeDisposable();
        var snapshotCount = 0L;

        var portfolioSub = portfolioStream
            .Throttle(TimeSpan.FromMilliseconds(250))
            .Subscribe(
                snapshot =>
                {
                    Interlocked.Increment(ref snapshotCount);
                    SnapshotsTotal.Inc();
                    PortfolioPnl.Set((double)snapshot.TotalUnrealizedPnl);
                    PortfolioDelta.Set((double)snapshot.AggregateGreeks.Delta);
                    PrintPortfolioSummary(snapshot);
                },
                ex =>
                {
                    Log.Error(ex, "Pipeline error");
                    PrintError($"Pipeline error: {ex.Message}");
                },
                () => Console.WriteLine("Pipeline completed."));
        subscriptions.Add(portfolioSub);

        // -----------------------------------------------------------------
        // Subscribe: Position-level detail (every ~2 seconds, throttled)
        // -----------------------------------------------------------------
        var detailSub = portfolioStream
            .Throttle(TimeSpan.FromSeconds(2))
            .Subscribe(
                snapshot => PrintPositionDetail(snapshot),
                ex =>
                {
                    Log.Error(ex, "Detail stream error");
                    PrintError($"Detail stream error: {ex.Message}");
                });
        subscriptions.Add(detailSub);

        // -----------------------------------------------------------------
        // Subscribe: Aggregate Greeks via GreeksAggregator
        // -----------------------------------------------------------------
        var greeksSub = aggregator.AggregateGreeks(portfolioStream)
            .Throttle(TimeSpan.FromSeconds(3))
            .Subscribe(
                gs =>
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  [Greeks] Delta={gs.Delta,8:F2}  Gamma={gs.Gamma,8:F4}  " +
                                      $"Vega={gs.Vega,8:F2}  Theta={gs.Theta,8:F2}  Rho={gs.Rho,8:F2}");
                    Console.ResetColor();
                },
                ex =>
                {
                    Log.Error(ex, "Greeks stream error");
                    PrintError($"Greeks stream error: {ex.Message}");
                });
        subscriptions.Add(greeksSub);

        // -----------------------------------------------------------------
        // Latency stats: print every 5 seconds
        // -----------------------------------------------------------------
        var latencySub = Observable.Interval(TimeSpan.FromSeconds(5))
            .Subscribe(_ =>
            {
                var stats = pipeline.GetLatencyStats();
                var count = Interlocked.Read(ref snapshotCount);
                // Record latency percentiles in Prometheus histogram
                PricingLatency.Observe(stats.P50);
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"  [Latency] p50={stats.P50:F1}ms  p95={stats.P95:F1}ms  " +
                                  $"p99={stats.P99:F1}ms  min={stats.Min:F1}ms  max={stats.Max:F1}ms  " +
                                  $"snapshots={count}");
                Console.ResetColor();
            });
        subscriptions.Add(latencySub);

        // Portfolio command consumer for dynamic position management
        PortfolioCommandConsumer? commandConsumer = null;
        IDisposable? commandSub = null;
        if (!string.IsNullOrEmpty(kafkaBootstrap) && kafkaBootstrap != "localhost:9092")
        {
            try
            {
                commandConsumer = new PortfolioCommandConsumer(kafkaBootstrap, "portfolio.commands", "meridian-pricing-cmds");
                commandSub = commandConsumer.Commands.Subscribe(cmd =>
                {
                    switch (cmd.Type)
                    {
                        case CommandType.Add:
                            Log.Information("Adding position {PositionId} for {Underlier}", cmd.Position.PositionId, cmd.Position.Instrument.Underlier);
                            pipeline.AddPosition(cmd.Position);
                            break;
                        case CommandType.Remove:
                            Log.Information("Removing position {PositionId}", cmd.Position.PositionId);
                            pipeline.RemovePosition(cmd.Position.PositionId);
                            break;
                        case CommandType.Update:
                            pipeline.RemovePosition(cmd.Position.PositionId);
                            pipeline.AddPosition(cmd.Position);
                            break;
                    }
                });
                Log.Information("Portfolio command consumer started for Pricing");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to start portfolio command consumer");
            }
        }

        // -----------------------------------------------------------------
        // Wait for Ctrl+C
        // -----------------------------------------------------------------
        // Start health check server on port 8091
        HealthCheckServer? healthServer = null;
        try
        {
            healthServer = new HealthCheckServer(8091, async () =>
            {
                var checks = new Dictionary<string, object>
                {
                    ["service"] = "Meridian.Pricing",
                    ["status"] = "healthy",
                    ["pipeline"] = "running",
                    ["snapshots"] = Interlocked.Read(ref snapshotCount),
                    ["timestamp"] = DateTime.UtcNow.ToString("o")
                };
                return await Task.FromResult(checks);
            });
            healthServer.Start();
            Log.Information("Health check server started on port {Port}", 8091);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start health check server on port {Port}", 8091);
        }

        Log.Information("Pricing pipeline started with {PositionCount} positions", positions.Count);
        Console.WriteLine("Pipeline running. Press Ctrl+C to stop.");
        Console.WriteLine();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine();
            Console.WriteLine("Shutting down...");
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C
        }

        // -----------------------------------------------------------------
        // Clean up
        // -----------------------------------------------------------------
        subscriptions.Dispose();
        commandSub?.Dispose();
        commandConsumer?.Dispose();
        pipeline.Dispose();
        dataSource?.Dispose();
        healthServer?.Dispose();

        try { metricServer.Stop(); }
        catch (Exception ex) { Log.Warning(ex, "Error stopping Prometheus metrics server"); }

        Log.Information("Pricing engine stopped");
        Log.CloseAndFlush();
        Console.WriteLine();
        Console.WriteLine("=== Meridian Pricing Engine stopped. ===");
    }

    private static async Task<List<Position>> LoadPositionsFromSql(string connectionString)
    {
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT * FROM Positions WHERE IsOpen = 1", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        var positions = new List<Position>();
        while (await reader.ReadAsync())
        {
            positions.Add(new Position(
                reader.GetString(reader.GetOrdinal("PositionId")),
                new Option(
                    reader.GetString(reader.GetOrdinal("Symbol")),
                    reader.GetString(reader.GetOrdinal("Underlier")),
                    Enum.Parse<OptionType>(reader.GetString(reader.GetOrdinal("OptionType")), true),
                    reader.GetDecimal(reader.GetOrdinal("Strike")),
                    reader.GetDateTime(reader.GetOrdinal("Expiry")),
                    ExerciseStyle.European),
                reader.GetInt32(reader.GetOrdinal("Quantity")),
                reader.GetDecimal(reader.GetOrdinal("EntryPrice")),
                reader.GetDateTime(reader.GetOrdinal("EntryTime"))));
        }
        return positions;
    }

    /// <summary>
    /// Builds a sample portfolio of ~12 option positions across the configured underliers.
    /// </summary>
    private static List<Position> BuildSamplePortfolio(List<SymbolConfig> symbols, DateTime now)
    {
        var positions = new List<Position>();
        int posId = 1;

        foreach (var sym in symbols)
        {
            decimal spot = sym.InitialPrice;

            // Position 1: Long ATM Call, 1-month expiry
            positions.Add(new Position(
                PositionId: $"POS-{posId++:D3}",
                Instrument: new Option(
                    Symbol: $"{sym.Symbol}-C-{spot:F0}-1M",
                    Underlier: sym.Symbol,
                    Type: OptionType.Call,
                    Strike: Math.Round(spot, 0),
                    Expiry: now.AddMonths(1),
                    Style: ExerciseStyle.European),
                Quantity: 10,
                EntryPrice: Math.Round(spot * 0.03m, 2),
                EntryTime: now.AddDays(-5)));

            // Position 2: Short OTM Put, 3-month expiry
            positions.Add(new Position(
                PositionId: $"POS-{posId++:D3}",
                Instrument: new Option(
                    Symbol: $"{sym.Symbol}-P-{spot * 0.90m:F0}-3M",
                    Underlier: sym.Symbol,
                    Type: OptionType.Put,
                    Strike: Math.Round(spot * 0.90m, 0),
                    Expiry: now.AddMonths(3),
                    Style: ExerciseStyle.European),
                Quantity: -5,
                EntryPrice: Math.Round(spot * 0.015m, 2),
                EntryTime: now.AddDays(-10)));
        }

        // Add a few extra positions for variety

        // Long deep ITM call on SIM_A, 6-month expiry
        positions.Add(new Position(
            PositionId: $"POS-{posId++:D3}",
            Instrument: new Option(
                Symbol: "SIM_A-C-80-6M",
                Underlier: "SIM_A",
                Type: OptionType.Call,
                Strike: 80m,
                Expiry: now.AddMonths(6),
                Style: ExerciseStyle.European),
            Quantity: 5,
            EntryPrice: 22.50m,
            EntryTime: now.AddDays(-20)));

        // Long OTM put on SIM_B, 1-month expiry (tail hedge)
        positions.Add(new Position(
            PositionId: $"POS-{posId++:D3}",
            Instrument: new Option(
                Symbol: "SIM_B-P-200-1M",
                Underlier: "SIM_B",
                Type: OptionType.Put,
                Strike: 200m,
                Expiry: now.AddMonths(1),
                Style: ExerciseStyle.European),
            Quantity: 20,
            EntryPrice: 0.50m,
            EntryTime: now.AddDays(-3)));

        return positions;
    }

    /// <summary>
    /// Prints a compact portfolio-level summary line with colored PnL.
    /// </summary>
    private static void PrintPortfolioSummary(PortfolioSnapshot snapshot)
    {
        var pnl = snapshot.TotalUnrealizedPnl;
        var greeks = snapshot.AggregateGreeks;

        Console.ForegroundColor = pnl >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
        string pnlSign = pnl >= 0 ? "+" : "";
        Console.Write($"  PnL: {pnlSign}{pnl,10:F2}");
        Console.ResetColor();

        Console.Write($"  | Delta={greeks.Delta,8:F2}  Gamma={greeks.Gamma,8:F4}  " +
                      $"Vega={greeks.Vega,8:F2}  | {snapshot.Positions.Count} positions  " +
                      $"@ {snapshot.Timestamp:HH:mm:ss.fff}");
        Console.WriteLine();
    }

    /// <summary>
    /// Prints detailed per-position breakdown.
    /// </summary>
    private static void PrintPositionDetail(PortfolioSnapshot snapshot)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine();
        Console.WriteLine($"  {"Position",-12} {"Type",-5} {"Strike",8} {"Qty",5} {"Price",8} {"MtM",10} {"Delta",8} {"Gamma",8} {"Vega",8} {"Theta",8}");
        Console.WriteLine($"  {new string('-', 95)}");

        foreach (var kvp in snapshot.Positions.OrderBy(p => p.Key))
        {
            var ps = kvp.Value;
            var scaledGreeks = ps.Greeks.Scale(ps.Position.Quantity);

            Console.ForegroundColor = ps.UnrealizedPnl >= 0 ? ConsoleColor.DarkGreen : ConsoleColor.DarkRed;

            Console.Write($"  {ps.Position.PositionId,-12} ");
            Console.Write($"{ps.Position.Instrument.Type,-5} ");
            Console.Write($"{ps.Position.Instrument.Strike,8:F2} ");
            Console.Write($"{ps.Position.Quantity,5} ");
            Console.Write($"{ps.CurrentPrice,8:F4} ");

            string mtmSign = ps.UnrealizedPnl >= 0 ? "+" : "";
            Console.Write($"{mtmSign}{ps.UnrealizedPnl,10:F2} ");
            Console.Write($"{scaledGreeks.Delta,8:F3} ");
            Console.Write($"{scaledGreeks.Gamma,8:F4} ");
            Console.Write($"{scaledGreeks.Vega,8:F3} ");
            Console.Write($"{scaledGreeks.Theta,8:F3}");
            Console.WriteLine();
        }

        Console.ResetColor();
        Console.WriteLine();
    }

    /// <summary>
    /// Prints an error message in red.
    /// </summary>
    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"  [ERROR] {message}");
        Console.ResetColor();
    }
}
