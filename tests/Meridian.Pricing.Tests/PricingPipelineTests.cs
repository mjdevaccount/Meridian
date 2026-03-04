using System.Reactive.Linq;
using System.Reactive.Subjects;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;
using Meridian.Pricing.Streams;
using Moq;
using Xunit;

namespace Meridian.Pricing.Tests;

public class PricingPipelineTests
{
    private static readonly DateTime BaseTime = new(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Option CreateOption(
        string symbol, string underlier, OptionType type, decimal strike) =>
        new(symbol, underlier, type, strike,
            BaseTime.AddDays(365), ExerciseStyle.European);

    private static Position CreatePosition(
        string positionId, Option instrument, int quantity = 1,
        decimal entryPrice = 5.0m) =>
        new(positionId, instrument, quantity, entryPrice, BaseTime);

    private static MarketTick CreateTick(string symbol, decimal price, long seq) =>
        new(symbol, price, null, DateTime.UtcNow, seq);

    private static VolSurfaceUpdate CreateVolUpdate(string symbol, decimal atmVol) =>
        new(symbol,
            new List<VolSurfacePoint> { new(100m, BaseTime.AddDays(365), atmVol) },
            atmVol,
            DateTime.UtcNow);

    private static Mock<IPricingModel> CreateMockPricer()
    {
        var mock = new Mock<IPricingModel>();
        mock.Setup(m => m.ModelName).Returns("MockBS");
        mock.Setup(m => m.Price(It.IsAny<Option>(), It.IsAny<MarketSnapshot>()))
            .Returns((Option opt, MarketSnapshot mkt) =>
                new PricingResult(
                    mkt.SpotPrice * 0.10m,
                    Math.Max(0, opt.Type == OptionType.Call
                        ? mkt.SpotPrice - opt.Strike
                        : opt.Strike - mkt.SpotPrice),
                    mkt.SpotPrice * 0.05m,
                    mkt.ImpliedVol));
        mock.Setup(m => m.ComputeGreeks(It.IsAny<Option>(), It.IsAny<MarketSnapshot>()))
            .Returns(new GreeksResult(0.50m, 0.03m, 0.20m, -0.05m, 0.10m));
        return mock;
    }

    private static bool WaitForResults<T>(List<T> results, int minCount, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (results.Count >= minCount) return true;
            Thread.Sleep(10);
        }
        return results.Count >= minCount;
    }

    [Fact]
    public void PipelineProducesPortfolioSnapshots()
    {
        using var pipeline = new PricingPipeline();
        var mockPricer = CreateMockPricer();

        var callOption = CreateOption("AAPL_C100", "AAPL", OptionType.Call, 100m);
        var position = CreatePosition("POS1", callOption);

        var tickSubject = new Subject<MarketTick>();
        var volSubject = new Subject<VolSurfaceUpdate>();

        var results = new List<PortfolioSnapshot>();
        pipeline.BuildPipeline(
                tickSubject, volSubject, new[] { position }, mockPricer.Object, 0.05m)
            .Subscribe(snapshot => results.Add(snapshot));

        volSubject.OnNext(CreateVolUpdate("AAPL", 0.20m));
        tickSubject.OnNext(CreateTick("AAPL", 105m, 1));
        tickSubject.OnNext(CreateTick("AAPL", 106m, 2));

        Assert.True(WaitForResults(results, 1),
            "Pipeline should produce at least one PortfolioSnapshot");
        Assert.All(results, snapshot =>
        {
            Assert.NotEmpty(snapshot.Positions);
            Assert.True(snapshot.Positions.ContainsKey("POS1"));
        });
    }

    [Fact]
    public void PipelineAggregatesMultiplePositions()
    {
        using var pipeline = new PricingPipeline();
        var mockPricer = CreateMockPricer();

        var callOption = CreateOption("AAPL_C100", "AAPL", OptionType.Call, 100m);
        var putOption = CreateOption("AAPL_P95", "AAPL", OptionType.Put, 95m);
        var pos1 = CreatePosition("POS1", callOption, quantity: 10);
        var pos2 = CreatePosition("POS2", putOption, quantity: 5, entryPrice: 3.0m);

        var tickSubject = new Subject<MarketTick>();
        var volSubject = new Subject<VolSurfaceUpdate>();

        var results = new List<PortfolioSnapshot>();
        pipeline.BuildPipeline(
                tickSubject, volSubject, new[] { pos1, pos2 }, mockPricer.Object, 0.05m)
            .Subscribe(snapshot => results.Add(snapshot));

        volSubject.OnNext(CreateVolUpdate("AAPL", 0.20m));
        tickSubject.OnNext(CreateTick("AAPL", 105m, 1));

        Assert.True(WaitForResults(results, 1),
            "Pipeline should produce at least one PortfolioSnapshot");
        var lastSnapshot = results[^1];
        Assert.True(lastSnapshot.Positions.ContainsKey("POS1"),
            "Snapshot should contain POS1");
        Assert.True(lastSnapshot.Positions.ContainsKey("POS2"),
            "Snapshot should contain POS2");
    }

    [Fact]
    public void PipelineHandlesMultipleUnderliers()
    {
        using var pipeline = new PricingPipeline();
        var mockPricer = CreateMockPricer();

        var aaplCall = CreateOption("AAPL_C100", "AAPL", OptionType.Call, 100m);
        var msftCall = CreateOption("MSFT_C300", "MSFT", OptionType.Call, 300m);
        var pos1 = CreatePosition("POS_AAPL", aaplCall);
        var pos2 = CreatePosition("POS_MSFT", msftCall, entryPrice: 15.0m);

        var tickSubject = new Subject<MarketTick>();
        var volSubject = new Subject<VolSurfaceUpdate>();

        var results = new List<PortfolioSnapshot>();
        pipeline.BuildPipeline(
                tickSubject, volSubject, new[] { pos1, pos2 }, mockPricer.Object, 0.05m)
            .Subscribe(snapshot => results.Add(snapshot));

        volSubject.OnNext(CreateVolUpdate("AAPL", 0.20m));
        volSubject.OnNext(CreateVolUpdate("MSFT", 0.25m));
        tickSubject.OnNext(CreateTick("AAPL", 105m, 1));
        tickSubject.OnNext(CreateTick("MSFT", 310m, 2));

        Assert.True(WaitForResults(results, 1, 3000),
            "Pipeline should produce at least one PortfolioSnapshot");

        // The last snapshot should have accumulated both positions via Scan
        var lastSnapshot = results[^1];
        Assert.True(lastSnapshot.Positions.ContainsKey("POS_AAPL"),
            "Snapshot should contain AAPL position");
        Assert.True(lastSnapshot.Positions.ContainsKey("POS_MSFT"),
            "Snapshot should contain MSFT position");
    }

    [Fact]
    public void EmptyBatchesAreFiltered()
    {
        using var pipeline = new PricingPipeline();
        var mockPricer = CreateMockPricer();

        var callOption = CreateOption("AAPL_C100", "AAPL", OptionType.Call, 100m);
        var position = CreatePosition("POS1", callOption);

        var tickSubject = new Subject<MarketTick>();
        var volSubject = new Subject<VolSurfaceUpdate>();

        var results = new List<PortfolioSnapshot>();
        pipeline.BuildPipeline(
                tickSubject, volSubject, new[] { position }, mockPricer.Object, 0.05m)
            .Subscribe(snapshot => results.Add(snapshot));

        volSubject.OnNext(CreateVolUpdate("AAPL", 0.20m));

        // Send same price twice, then a different price
        tickSubject.OnNext(CreateTick("AAPL", 105m, 1));
        tickSubject.OnNext(CreateTick("AAPL", 105m, 2)); // same price -> filtered by DistinctUntilChanged
        tickSubject.OnNext(CreateTick("AAPL", 106m, 3)); // different price

        WaitForResults(results, 1);

        // DistinctUntilChanged filters the duplicate, so we should get at most 2 snapshots
        Assert.True(results.Count <= 2,
            $"Expected at most 2 snapshots (duplicate price filtered), but got {results.Count}");
    }
}
