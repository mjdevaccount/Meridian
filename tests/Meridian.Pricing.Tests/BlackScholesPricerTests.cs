using Meridian.Common.Models;
using Meridian.Pricing.Models;
using Xunit;

namespace Meridian.Pricing.Tests;

public class BlackScholesPricerTests
{
    private readonly BlackScholesPricer _pricer = new();

    private static Option CreateOption(
        OptionType type,
        decimal strike,
        DateTime expiry,
        string symbol = "TEST_CALL",
        string underlier = "TEST") =>
        new(symbol, underlier, type, strike, expiry, ExerciseStyle.European);

    private static MarketSnapshot CreateMarket(
        decimal spotPrice,
        decimal impliedVol,
        decimal riskFreeRate,
        DateTime? timestamp = null) =>
        new("TEST", spotPrice, impliedVol, riskFreeRate, timestamp ?? DateTime.UtcNow);

    private static DateTime ExpiryFromYears(double years) =>
        DateTime.UtcNow.AddDays(years * 365);

    // ---------------------------------------------------------------
    // (a) ATM Call Price: S=100, K=100, r=0.05, vol=0.20, T=1yr
    //     Known BS call price ~ 10.4506
    // ---------------------------------------------------------------
    [Fact]
    public void AtmCallPrice()
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Call, 100m, expiry);
        var market = CreateMarket(100m, 0.20m, 0.05m);

        var result = _pricer.Price(option, market);

        Assert.InRange((double)result.TheoreticalPrice, 10.4506 - 0.01, 10.4506 + 0.01);
    }

    // ---------------------------------------------------------------
    // (b) ATM Put Price: same params
    //     P = C - S + K*e^(-rT) ~ 5.5735
    // ---------------------------------------------------------------
    [Fact]
    public void AtmPutPrice()
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Put, 100m, expiry, symbol: "TEST_PUT");
        var market = CreateMarket(100m, 0.20m, 0.05m);

        var result = _pricer.Price(option, market);

        Assert.InRange((double)result.TheoreticalPrice, 5.5735 - 0.01, 5.5735 + 0.01);
    }

    // ---------------------------------------------------------------
    // (c) Put-Call Parity: C - P = S - K*e^(-rT)
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(100, 100, 0.05, 0.20, 1.0)]
    [InlineData(110, 100, 0.03, 0.30, 0.5)]
    [InlineData(90, 120, 0.08, 0.25, 2.0)]
    public void PutCallParity(double spot, double strike, double rate, double vol, double years)
    {
        var expiry = ExpiryFromYears(years);
        var callOption = CreateOption(OptionType.Call, (decimal)strike, expiry);
        var putOption = CreateOption(OptionType.Put, (decimal)strike, expiry, symbol: "TEST_PUT");
        var market = CreateMarket((decimal)spot, (decimal)vol, (decimal)rate);

        var callPrice = (double)_pricer.Price(callOption, market).TheoreticalPrice;
        var putPrice = (double)_pricer.Price(putOption, market).TheoreticalPrice;

        var expectedDiff = spot - strike * Math.Exp(-rate * years);
        var actualDiff = callPrice - putPrice;

        Assert.InRange(actualDiff, expectedDiff - 0.05, expectedDiff + 0.05);
    }

    // ---------------------------------------------------------------
    // (d) Deep ITM Call: S=150, K=100 -> delta near 1.0, price near intrinsic
    // ---------------------------------------------------------------
    [Fact]
    public void DeepItmCall()
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Call, 100m, expiry);
        var market = CreateMarket(150m, 0.20m, 0.05m);

        var price = _pricer.Price(option, market);
        var greeks = _pricer.ComputeGreeks(option, market);

        // Price should be at least intrinsic value (S - K = 50)
        Assert.True(price.TheoreticalPrice >= 50m);
        // Delta should be near 1.0
        Assert.InRange((double)greeks.Delta, 0.95, 1.0);
    }

    // ---------------------------------------------------------------
    // (e) Deep OTM Call: S=50, K=100 -> delta near 0, price near 0
    // ---------------------------------------------------------------
    [Fact]
    public void DeepOtmCall()
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Call, 100m, expiry);
        var market = CreateMarket(50m, 0.20m, 0.05m);

        var price = _pricer.Price(option, market);
        var greeks = _pricer.ComputeGreeks(option, market);

        // Price should be near 0
        Assert.InRange((double)price.TheoreticalPrice, 0.0, 0.5);
        // Delta should be near 0
        Assert.InRange((double)greeks.Delta, 0.0, 0.05);
    }

    // ---------------------------------------------------------------
    // (f) Expired Option: T=0 -> call = max(S-K,0), put = max(K-S,0)
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(110, 100, 10)]   // ITM call
    [InlineData(90, 100, 0)]     // OTM call
    [InlineData(100, 100, 0)]    // ATM call
    public void ExpiredCallOption(double spot, double strike, double expectedPrice)
    {
        var expiry = DateTime.UtcNow; // expired now
        var option = CreateOption(OptionType.Call, (decimal)strike, expiry);
        var market = CreateMarket((decimal)spot, 0.20m, 0.05m);

        var result = _pricer.Price(option, market);

        Assert.InRange((double)result.TheoreticalPrice,
            expectedPrice - 0.01, expectedPrice + 0.01);
    }

    [Theory]
    [InlineData(90, 100, 10)]    // ITM put
    [InlineData(110, 100, 0)]    // OTM put
    [InlineData(100, 100, 0)]    // ATM put
    public void ExpiredPutOption(double spot, double strike, double expectedPrice)
    {
        var expiry = DateTime.UtcNow; // expired now
        var option = CreateOption(OptionType.Put, (decimal)strike, expiry, symbol: "TEST_PUT");
        var market = CreateMarket((decimal)spot, 0.20m, 0.05m);

        var result = _pricer.Price(option, market);

        Assert.InRange((double)result.TheoreticalPrice,
            expectedPrice - 0.01, expectedPrice + 0.01);
    }

    // ---------------------------------------------------------------
    // (g) Higher vol -> higher option price
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call)]
    [InlineData(OptionType.Put)]
    public void HighVolIncreasesPrice(OptionType type)
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(type, 100m, expiry,
            symbol: type == OptionType.Call ? "TEST_CALL" : "TEST_PUT");

        var marketLowVol = CreateMarket(100m, 0.15m, 0.05m);
        var marketHighVol = CreateMarket(100m, 0.30m, 0.05m);

        var priceLow = _pricer.Price(option, marketLowVol).TheoreticalPrice;
        var priceHigh = _pricer.Price(option, marketHighVol).TheoreticalPrice;

        Assert.True(priceHigh > priceLow,
            $"Higher vol price ({priceHigh}) should exceed lower vol price ({priceLow})");
    }

    // ---------------------------------------------------------------
    // (h) Call delta: 0 <= delta <= 1
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    public void CallDeltaBetweenZeroAndOne(double spot)
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Call, 100m, expiry);
        var market = CreateMarket((decimal)spot, 0.20m, 0.05m);

        var greeks = _pricer.ComputeGreeks(option, market);

        Assert.InRange((double)greeks.Delta, 0.0, 1.0);
    }

    // ---------------------------------------------------------------
    // (i) Put delta: -1 <= delta <= 0
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    public void PutDeltaBetweenMinusOneAndZero(double spot)
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Put, 100m, expiry, symbol: "TEST_PUT");
        var market = CreateMarket((decimal)spot, 0.20m, 0.05m);

        var greeks = _pricer.ComputeGreeks(option, market);

        Assert.InRange((double)greeks.Delta, -1.0, 0.0);
    }

    // ---------------------------------------------------------------
    // (j) Gamma always positive for non-expired options
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call, 80)]
    [InlineData(OptionType.Call, 100)]
    [InlineData(OptionType.Call, 120)]
    [InlineData(OptionType.Put, 80)]
    [InlineData(OptionType.Put, 100)]
    [InlineData(OptionType.Put, 120)]
    public void GammaAlwaysPositive(OptionType type, double spot)
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(type, 100m, expiry,
            symbol: type == OptionType.Call ? "TEST_CALL" : "TEST_PUT");
        var market = CreateMarket((decimal)spot, 0.20m, 0.05m);

        var greeks = _pricer.ComputeGreeks(option, market);

        Assert.True(greeks.Gamma > 0m,
            $"Gamma should be positive, got {greeks.Gamma}");
    }

    // ---------------------------------------------------------------
    // (k) Vega always positive for non-expired options
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call, 80)]
    [InlineData(OptionType.Call, 100)]
    [InlineData(OptionType.Call, 120)]
    [InlineData(OptionType.Put, 80)]
    [InlineData(OptionType.Put, 100)]
    [InlineData(OptionType.Put, 120)]
    public void VegaAlwaysPositive(OptionType type, double spot)
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(type, 100m, expiry,
            symbol: type == OptionType.Call ? "TEST_CALL" : "TEST_PUT");
        var market = CreateMarket((decimal)spot, 0.20m, 0.05m);

        var greeks = _pricer.ComputeGreeks(option, market);

        Assert.True(greeks.Vega > 0m,
            $"Vega should be positive, got {greeks.Vega}");
    }
}
