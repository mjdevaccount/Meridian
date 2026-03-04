using System.Reactive.Linq;
using System.Text.Json;
using Confluent.Kafka;
using Meridian.Common.Configuration;
using Meridian.Common.Models;
using Meridian.Pricing.Models;
using Meridian.Pricing.Streams;
using Meridian.Risk.Aggregation;
using Meridian.Risk.State;
using Microsoft.Extensions.Configuration;

namespace Meridian.Risk;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Meridian Risk Service - Phase 2");
        Console.WriteLine("===============================");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var riskLimits = new RiskLimitsConfig();
        configuration.GetSection("RiskLimits").Bind(riskLimits);

        var kafkaBootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP")
            ?? configuration.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092";
        var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
            ?? configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
        var sqlConnection = Environment.GetEnvironmentVariable("SQL_CONNECTION")
            ?? configuration.GetValue<string>("SqlServer:ConnectionString") ?? "";

        Console.WriteLine($"Kafka: {kafkaBootstrap}");
        Console.WriteLine($"Redis: {redisConnection}");
        Console.WriteLine($"SQL: {(string.IsNullOrEmpty(sqlConnection) ? "disabled" : "enabled")}");

        // Initialize state stores
        RedisStateStore? redis = null;
        SqlPositionRepository? sql = null;

        try
        {
            redis = new RedisStateStore(redisConnection);
            Console.WriteLine("Redis connected.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Redis unavailable: {ex.Message}. Continuing without Redis.");
        }

        try
        {
            if (!string.IsNullOrEmpty(sqlConnection))
            {
                sql = new SqlPositionRepository(sqlConnection);
                await sql.InitializeSchemaAsync();
                Console.WriteLine("SQL Server connected. Schema initialized.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SQL Server unavailable: {ex.Message}. Continuing without SQL.");
        }

        // Build sample portfolio (same as Pricing)
        var now = DateTime.UtcNow;
        var positions = BuildSamplePortfolio(now);

        // Save positions to SQL
        if (sql != null)
        {
            foreach (var pos in positions)
            {
                try { await sql.SavePositionAsync(pos); } catch { }
            }
        }

        // Set up Kafka consumer for market ticks
        var ticksTopic = configuration.GetValue<string>("Kafka:TicksTopic") ?? "market.ticks";
        var volSurfaceTopic = configuration.GetValue<string>("Kafka:VolSurfaceTopic") ?? "market.volsurface";
        var tickStream = new MarketDataStream(kafkaBootstrap, ticksTopic, volSurfaceTopic, "meridian-risk");

        // Build pricing pipeline
        var pricer = new BlackScholesPricer();
        var pipeline = new PricingPipeline();
        var portfolioStream = pipeline.BuildPipeline(
            tickStream.GetAllTicksStream(),
            tickStream.GetVolSurfaceStream(),
            positions,
            pricer,
            0.05m);

        // Set up risk components
        var pnlCalc = new PnlCalculator();
        var riskAgg = new RiskAggregator();
        var thresholdMonitor = new ThresholdMonitor(riskLimits);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Subscribe to PnL updates -> write to Redis
        var pnlSub = pnlCalc.ComputePnl(portfolioStream)
            .Sample(TimeSpan.FromSeconds(1))
            .Subscribe(async pnlUpdates =>
            {
                foreach (var pnl in pnlUpdates)
                {
                    try { if (redis != null) await redis.UpdatePnlAsync(pnl.PositionId, pnl); } catch { }
                }
            });

        // Subscribe to portfolio -> write aggregate to Redis
        var aggSub = portfolioStream
            .Sample(TimeSpan.FromSeconds(1))
            .Subscribe(async snapshot =>
            {
                try { if (redis != null) await redis.UpdateAggregateAsync(snapshot); } catch { }

                // Write Greeks to Redis
                foreach (var kvp in snapshot.Positions)
                {
                    try
                    {
                        if (redis != null)
                            await redis.UpdatePositionGreeksAsync(kvp.Key, kvp.Value.Greeks);
                    }
                    catch { }
                }
            });

        // Subscribe to portfolio -> write snapshots to SQL every 10 seconds
        var sqlSnapshotSub = pnlCalc.ComputePnl(portfolioStream)
            .Sample(TimeSpan.FromSeconds(10))
            .Subscribe(async pnlUpdates =>
            {
                if (sql == null) return;
                foreach (var pnl in pnlUpdates)
                {
                    try { await sql.SavePnlSnapshotAsync(pnl); } catch { }
                }
            });

        // Subscribe to risk alerts
        var alertSub = thresholdMonitor.MonitorThresholds(portfolioStream)
            .Subscribe(async alert =>
            {
                var color = alert.Severity == AlertSeverity.Critical ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.ForegroundColor = color;
                Console.WriteLine($"[ALERT] [{alert.Severity}] {alert.Type}: {alert.Description}");
                Console.ResetColor();

                try { if (sql != null) await sql.SaveRiskAlertAsync(alert); } catch { }
            });

        // Subscribe to risk metrics for console output
        var metricsSub = riskAgg.AggregateRisk(portfolioStream)
            .Sample(TimeSpan.FromSeconds(3))
            .Subscribe(metrics =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[RISK] Positions={metrics.PositionCount} " +
                    $"PnL={metrics.TotalPnl:F2} " +
                    $"Delta={metrics.AggregateGreeks.Delta:F2} " +
                    $"Gamma={metrics.AggregateGreeks.Gamma:F4} " +
                    $"Vega={metrics.AggregateGreeks.Vega:F2}");
                Console.ResetColor();
            });

        Console.WriteLine();
        Console.WriteLine("Risk service running. Waiting for market data...");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException) { }

        pnlSub.Dispose();
        aggSub.Dispose();
        sqlSnapshotSub.Dispose();
        alertSub.Dispose();
        metricsSub.Dispose();
        tickStream.Dispose();
        pipeline.Dispose();
        redis?.Dispose();
        sql?.Dispose();

        Console.WriteLine("Risk service stopped.");
    }

    private static List<Position> BuildSamplePortfolio(DateTime now)
    {
        // Same portfolio as Pricing service
        var symbols = new[] {
            ("SIM_A", 100m), ("SIM_B", 250m), ("SIM_C", 50m),
            ("SIM_D", 175m), ("SIM_E", 80m)
        };
        var positions = new List<Position>();
        int id = 1;

        foreach (var (sym, price) in symbols)
        {
            positions.Add(new Position($"POS_{id++}",
                new Option($"{sym}_C_ATM", sym, OptionType.Call, price, now.AddDays(30), ExerciseStyle.European),
                10, price * 0.05m, now));
            positions.Add(new Position($"POS_{id++}",
                new Option($"{sym}_P_OTM", sym, OptionType.Put, price * 0.9m, now.AddDays(90), ExerciseStyle.European),
                -5, price * 0.03m, now));
        }

        return positions;
    }
}
