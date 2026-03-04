using System.Reactive.Linq;
using Microsoft.Extensions.Configuration;
using Meridian.Common.Configuration;
using Meridian.Common.Health;
using Meridian.MarketData.Publishing;
using Meridian.MarketData.Simulation;
using Prometheus;
using Serilog;

namespace Meridian.MarketData;

public class Program
{
    // Prometheus metrics
    private static readonly Counter TicksPublished = Metrics.CreateCounter(
        "meridian_ticks_published_total", "Total ticks published", new CounterConfiguration
        {
            LabelNames = new[] { "symbol" }
        });
    private static readonly Gauge TickRate = Metrics.CreateGauge(
        "meridian_tick_rate", "Current ticks per second");

    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.WithProperty("Service", "Meridian.MarketData")
            .WriteTo.Console()
            .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
            .CreateLogger();

        Log.Information("Meridian Market Data Simulator - Phase 2 starting");

        Console.WriteLine("Meridian Market Data Simulator - Phase 2");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // Start Prometheus metrics server on port 9100
        var metricServer = new MetricServer(port: 9100);
        try
        {
            metricServer.Start();
            Log.Information("Prometheus metrics server started on port {Port}", 9100);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start Prometheus metrics server on port {Port}", 9100);
        }

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var config = new MarketDataConfig();
        configuration.GetSection("MarketData").Bind(config);

        Log.Information("Configured symbols: {Symbols}", string.Join(", ", config.Symbols.Select(s => s.Symbol)));
        Log.Information("Ticks per second: {TicksPerSecond}, Vol surface updates per second: {VolSurfaceUpdatesPerSecond}",
            config.TicksPerSecond, config.VolSurfaceUpdatesPerSecond);
        Console.WriteLine($"Configured symbols: {string.Join(", ", config.Symbols.Select(s => s.Symbol))}");
        Console.WriteLine($"Ticks per second: {config.TicksPerSecond}");
        Console.WriteLine($"Vol surface updates per second: {config.VolSurfaceUpdatesPerSecond}");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        // Track previous prices for coloring
        var previousPrices = new Dictionary<string, decimal>();
        foreach (var sym in config.Symbols)
        {
            previousPrices[sym.Symbol] = sym.InitialPrice;
        }

        using var simulator = new MarketDataSimulator(config);

        // Try to set up Kafka publisher
        KafkaTickPublisher? kafkaPublisher = null;
        try
        {
            var kafkaBootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP")
                ?? configuration.GetValue<string>("Kafka:BootstrapServers");
            var ticksTopic = configuration.GetValue<string>("Kafka:TicksTopic") ?? "market.ticks";
            var volSurfaceTopic = configuration.GetValue<string>("Kafka:VolSurfaceTopic") ?? "market.volsurface";

            if (!string.IsNullOrEmpty(kafkaBootstrap))
            {
                kafkaPublisher = new KafkaTickPublisher(kafkaBootstrap, ticksTopic, volSurfaceTopic);
                Log.Information("Kafka publisher connected: {KafkaBootstrap}", kafkaBootstrap);
                Log.Information("Kafka ticks topic: {TicksTopic}, vol surface topic: {VolSurfaceTopic}", ticksTopic, volSurfaceTopic);
                Console.WriteLine($"Kafka publisher connected: {kafkaBootstrap}");
                Console.WriteLine($"  Ticks topic: {ticksTopic}");
                Console.WriteLine($"  Vol surface topic: {volSurfaceTopic}");
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Kafka publisher failed to initialize");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARNING: Kafka publisher failed to initialize: {ex.Message}");
            Console.WriteLine("Continuing with console-only output.");
            Console.ResetColor();
            Console.WriteLine();
            kafkaPublisher = null;
        }

        // Start health check server on port 8090
        HealthCheckServer? healthServer = null;
        try
        {
            healthServer = new HealthCheckServer(8090, async () =>
            {
                var checks = new Dictionary<string, object>
                {
                    ["service"] = "Meridian.MarketData",
                    ["status"] = "healthy",
                    ["kafka"] = kafkaPublisher != null ? "connected" : "not configured",
                    ["timestamp"] = DateTime.UtcNow.ToString("o")
                };
                return await Task.FromResult(checks);
            });
            healthServer.Start();
            Log.Information("Health check server started on port {Port}", 8090);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start health check server on port {Port}", 8090);
        }

        // Tick rate tracking
        var tickCount = 0L;
        var tickRateTimer = new System.Timers.Timer(1000);
        tickRateTimer.Elapsed += (_, _) =>
        {
            var count = Interlocked.Exchange(ref tickCount, 0);
            TickRate.Set(count);
        };
        tickRateTimer.Start();

        // Set up cancellation on Ctrl+C
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.ResetColor();
            Console.WriteLine();
            Log.Information("Shutdown requested");
            Console.WriteLine("Shutting down...");
        };

        // Publish all ticks to Kafka (unthrottled) if publisher is available
        IDisposable? kafkaTickSubscription = null;
        if (kafkaPublisher != null)
        {
            var publisher = kafkaPublisher; // capture for lambda
            kafkaTickSubscription = simulator.GetAllTicksStream()
                .Subscribe(tick =>
                {
                    if (cts.Token.IsCancellationRequested) return;
                    Interlocked.Increment(ref tickCount);
                    try
                    {
                        publisher.PublishTick(tick);
                        TicksPublished.WithLabels(tick.Symbol).Inc();
                    }
                    catch (Exception ex) { Log.Error(ex, "Kafka tick publish error for {Symbol}", tick.Symbol); }
                });
        }

        // Subscribe to the merged tick stream with colored output
        // Throttle console output to avoid overwhelming the terminal
        var tickSubscription = simulator.GetAllTicksStream()
            .Sample(TimeSpan.FromMilliseconds(100)) // sample per symbol isn't possible here; just throttle overall
            .Subscribe(tick =>
            {
                if (cts.Token.IsCancellationRequested) return;

                decimal prevPrice;
                lock (previousPrices)
                {
                    previousPrices.TryGetValue(tick.Symbol, out prevPrice);
                    previousPrices[tick.Symbol] = tick.Price;
                }

                // Color: green for up, red for down, gray for unchanged
                if (tick.Price > prevPrice)
                    Console.ForegroundColor = ConsoleColor.Green;
                else if (tick.Price < prevPrice)
                    Console.ForegroundColor = ConsoleColor.Red;
                else
                    Console.ForegroundColor = ConsoleColor.Gray;

                Console.WriteLine(
                    $"[{tick.Timestamp:HH:mm:ss.fff}] {tick.Symbol,-6} " +
                    $"Price: {tick.Price,12:F4}  " +
                    $"Seq: {tick.SequenceNumber,8}  " +
                    $"Chg: {(tick.Price - prevPrice),8:F4}");

                Console.ResetColor();
            });

        // Publish vol surface updates to Kafka (unthrottled) if publisher is available
        IDisposable? kafkaVolSubscription = null;
        if (kafkaPublisher != null)
        {
            var publisher = kafkaPublisher; // capture for lambda
            kafkaVolSubscription = simulator.GetVolSurfaceStream()
                .Subscribe(update =>
                {
                    if (cts.Token.IsCancellationRequested) return;
                    try { publisher.PublishVolSurface(update); }
                    catch (Exception ex) { Log.Error(ex, "Kafka vol surface publish error for {Symbol}", update.Symbol); }
                });
        }

        // Subscribe to vol surface updates (less frequent)
        var volSubscription = simulator.GetVolSurfaceStream()
            .Subscribe(update =>
            {
                if (cts.Token.IsCancellationRequested) return;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(
                    $"[{update.Timestamp:HH:mm:ss.fff}] {update.Symbol,-6} " +
                    $"VolSurface: ATM={update.AtmVol:P2}  " +
                    $"Points={update.Points.Count}");
                Console.ResetColor();
            });

        // Wait until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C
        }

        kafkaTickSubscription?.Dispose();
        kafkaVolSubscription?.Dispose();
        tickSubscription.Dispose();
        volSubscription.Dispose();
        kafkaPublisher?.Dispose();
        tickRateTimer.Stop();
        tickRateTimer.Dispose();
        healthServer?.Dispose();

        try { metricServer.Stop(); }
        catch (Exception ex) { Log.Warning(ex, "Error stopping Prometheus metrics server"); }

        Log.Information("Simulator stopped");
        Log.CloseAndFlush();
        Console.WriteLine("Simulator stopped.");
    }
}
