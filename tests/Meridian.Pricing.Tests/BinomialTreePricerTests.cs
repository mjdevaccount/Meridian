using Meridian.Common.Models;
using Meridian.Pricing.Models;
using Xunit;

namespace Meridian.Pricing.Tests;

public class BinomialTreePricerTests
{
    private readonly BinomialTreePricer _pricer = new(steps: 200);
    private readonly BlackScholesPricer _bsPricer = new();

    private static Option CreateOption(
        OptionType type,
        decimal strike,
        DateTime expiry,
        ExerciseStyle style = ExerciseStyle.European,
        string symbol = "TEST",
        string underlier = "TEST") =>
        new(symbol, underlier, type, strike, expiry, style);

    private static MarketSnapshot CreateMarket(
        decimal spotPrice,
        decimal impliedVol,
        decimal riskFreeRate,
        DateTime? timestamp = null) =>
        new("TEST", spotPrice, impliedVol, riskFreeRate, timestamp ?? DateTime.UtcNow);

    private static DateTime ExpiryFromYears(double years) =>
        DateTime.UtcNow.AddDays(years * 365.25);

    // ---------------------------------------------------------------
    // 1. ATM European call converges to Black-Scholes (within 0.5%)
    // ---------------------------------------------------------------
    [Fact]
    public void EuropeanAtmCall_ConvergesToBlackScholes()
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Call, 100m, expiry);
        var market = CreateMarket(100m, 0.20m, 0.05m);

        var btPrice = (double)_pricer.Price(option, market).TheoreticalPrice;
        var bsPrice = (double)_bsPricer.Price(option, market).TheoreticalPrice;

        double relativeError = Math.Abs(btPrice - bsPrice) / bsPrice;
        Assert.True(relativeError < 0.005,
            $"BT call price {btPrice:F4} should be within 0.5% of BS price {bsPrice:F4} " +
            $"(relative error: {relativeError:P4})");
    }

    // ---------------------------------------------------------------
    // 2. ATM European put converges to Black-Scholes (within 0.5%)
    // ---------------------------------------------------------------
    [Fact]
    public void EuropeanAtmPut_ConvergesToBlackScholes()
    {
        var expiry = ExpiryFromYears(1.0);
        var option = CreateOption(OptionType.Put, 100m, expiry);
        var market = CreateMarket(100m, 0.20m, 0.05m);

        var btPrice = (double)_pricer.Price(option, market).TheoreticalPrice;
        var bsPrice = (double)_bsPricer.Price(option, market).TheoreticalPrice;

        double relativeError = Math.Abs(btPrice - bsPrice) / bsPrice;
        Assert.True(relativeError < 0.005,
            $"BT put price {btPrice:F4} should be within 0.5% of BS price {bsPrice:F4} " +
            $"(relative error: {relativeError:P4})");
    }

    // ---------------------------------------------------------------
    // 3. Put-call parity holds for European options
    //    C - P = S - K * e^(-rT)
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(100, 100, 0.05, 0.20, 1.0)]
    [InlineData(110, 100, 0.03, 0.30, 0.5)]
    [InlineData(90, 120, 0.08, 0.25, 2.0)]
    public void EuropeanPutCallParity(double spot, double strike, double rate, double vol, double years)
    {
        var expiry = ExpiryFromYears(years);
        var callOption = CreateOption(OptionType.Call, (decimal)strike, expiry);
        var putOption = CreateOption(OptionType.Put, (decimal)strike, expiry);
        var market = CreateMarket((decimal)spot, (decimal)vol, (decimal)rate);

        var callPrice = (double)_pricer.Price(callOption, market).TheoreticalPrice;
        var putPrice = (double)_pricer.Price(putOption, market).TheoreticalPrice;

        double expectedDiff = spot - strike * Math.Exp(-rate * years);
        double actualDiff = callPrice - putPrice;

        Assert.InRange(actualDiff, expectedDiff - 0.15, expectedDiff + 0.15);
    }

    // ---------------------------------------------------------------
    // 4. American call on non-dividend stock equals European call
    //    (early exercise is never optimal for calls without dividends)
    // ---------------------------------------------------------------
    [Fact]
    public void AmericanCall_EqualsEuropeanCall_NoDividend()
    {
        var expiry = ExpiryFromYears(1.0);
        var eurOption = CreateOption(OptionType.Call, 100m, expiry, ExerciseStyle.European);
        var amOption = CreateOption(OptionType.Call, 100m, expiry, ExerciseStyle.American);
        var market = CreateMarket(100m, 0.20m, 0.05m);

        var eurPrice = (double)_pricer.Price(eurOption, market).TheoreticalPrice;
        var amPrice = (double)_pricer.Price(amOption, market).TheoreticalPrice;

        // Should be essentially equal (within numerical tolerance)
        Assert.InRange(amPrice, eurPrice - 0.01, eurPrice + 0.01);
    }

    // ---------------------------------------------------------------
    // 5. American put >= European put
    //    (early exercise can be optimal for puts)
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(100, 100, 0.05, 0.20, 1.0)]
    [InlineData(80, 100, 0.05, 0.30, 1.0)]
    [InlineData(90, 100, 0.08, 0.25, 2.0)]
    public void AmericanPut_GreaterThanOrEqualToEuropeanPut(
        double spot, double strike, double rate, double vol, double years)
    {
        var expiry = ExpiryFromYears(years);
        var eurOption = CreateOption(OptionType.Put, (decimal)strike, expiry, ExerciseStyle.European);
        var amOption = CreateOption(OptionType.Put, (decimal)strike, expiry, ExerciseStyle.American);
        var market = CreateMarket((decimal)spot, (decimal)vol, (decimal)rate);

        var eurPrice = (double)_pricer.Price(eurOption, market).TheoreticalPrice;
        var amPrice = (double)_pricer.Price(amOption, market).TheoreticalPrice;

        Assert.True(amPrice >= eurPrice - 1e-6,
            $"American put price {amPrice:F4} should be >= European put price {eurPrice:F4}");
    }

    // ---------------------------------------------------------------
    // 6. Expired option returns intrinsic value
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call, 110, 100, 10)]   // ITM call
    [InlineData(OptionType.Call, 90, 100, 0)]     // OTM call
    [InlineData(OptionType.Call, 100, 100, 0)]    // ATM call
    [InlineData(OptionType.Put, 90, 100, 10)]     // ITM put
    [InlineData(OptionType.Put, 110, 100, 0)]     // OTM put
    [InlineData(OptionType.Put, 100, 100, 0)]     // ATM put
    public void ExpiredOption_ReturnsIntrinsicValue(
        OptionType type, double spot, double strike, double expectedPrice)
    {
        var expiry = DateTime.UtcNow; // expired now
        var option = CreateOption(type, (decimal)strike, expiry, ExerciseStyle.American);
        var market = CreateMarket((decimal)spot, 0.20m, 0.05m);

        var result = _pricer.Price(option, market);

        Assert.InRange((double)result.TheoreticalPrice,
            expectedPrice - 0.01, expectedPrice + 0.01);
        Assert.Equal(0m, result.TimeValue);
    }

    // ---------------------------------------------------------------
    // 7. Deep ITM American put has positive early exercise premium
    //    over European put
    // ---------------------------------------------------------------
    [Fact]
    public void DeepItmAmericanPut_HasEarlyExercisePremium()
    {
        // Deep ITM put: S=60, K=100 with long expiry and high rates
        // to maximize early exercise incentive
        var expiry = ExpiryFromYears(2.0);
        var eurOption = CreateOption(OptionType.Put, 100m, expiry, ExerciseStyle.European);
        var amOption = CreateOption(OptionType.Put, 100m, expiry, ExerciseStyle.American);
        var market = CreateMarket(60m, 0.20m, 0.10m);

        var eurPrice = (double)_pricer.Price(eurOption, market).TheoreticalPrice;
        var amPrice = (double)_pricer.Price(amOption, market).TheoreticalPrice;

        double earlyExercisePremium = amPrice - eurPrice;

        Assert.True(earlyExercisePremium > 0.01,
            $"Early exercise premium {earlyExercisePremium:F4} should be meaningfully positive " +
            $"(American={amPrice:F4}, European={eurPrice:F4})");
    }

    // ---------------------------------------------------------------
    // Additional: Greeks are computed without errors
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(ExerciseStyle.European)]
    [InlineData(ExerciseStyle.American)]
    public void Greeks_ReturnReasonableValues(ExerciseStyle style)
    {
        var expiry = ExpiryFromYears(1.0);
        var callOption = CreateOption(OptionType.Call, 100m, expiry, style);
        var putOption = CreateOption(OptionType.Put, 100m, expiry, style);
        var market = CreateMarket(100m, 0.20m, 0.05m);

        var callGreeks = _pricer.ComputeGreeks(callOption, market);
        var putGreeks = _pricer.ComputeGreeks(putOption, market);

        // Call delta between 0 and 1
        Assert.InRange((double)callGreeks.Delta, 0.0, 1.0);
        // Put delta between -1 and 0
        Assert.InRange((double)putGreeks.Delta, -1.0, 0.0);
        // Gamma positive for both
        Assert.True(callGreeks.Gamma > 0m, $"Call gamma should be positive, got {callGreeks.Gamma}");
        Assert.True(putGreeks.Gamma > 0m, $"Put gamma should be positive, got {putGreeks.Gamma}");
        // Vega positive for both
        Assert.True(callGreeks.Vega > 0m, $"Call vega should be positive, got {callGreeks.Vega}");
        Assert.True(putGreeks.Vega > 0m, $"Put vega should be positive, got {putGreeks.Vega}");
        // Theta negative (time decay for long options)
        Assert.True(callGreeks.Theta < 0m, $"Call theta should be negative, got {callGreeks.Theta}");
        Assert.True(putGreeks.Theta < 0m, $"Put theta should be negative, got {putGreeks.Theta}");
    }

    // ---------------------------------------------------------------
    // ModelName property
    // ---------------------------------------------------------------
    [Fact]
    public void ModelName_ReturnsBinomialTree()
    {
        Assert.Equal("Binomial Tree (CRR)", _pricer.ModelName);
    }
}
