using Meridian.Common.Models;
using Meridian.Pricing.Models;
using Xunit;

namespace Meridian.Pricing.Tests;

public class GreeksAccuracyTests
{
    private readonly BlackScholesPricer _pricer = new();

    // Base parameters: S=100, K=100, r=0.05, vol=0.20, T=1yr
    private const decimal BaseSpot = 100m;
    private const decimal BaseStrike = 100m;
    private const decimal BaseRate = 0.05m;
    private const decimal BaseVol = 0.20m;
    private const double BaseYears = 1.0;

    private static DateTime ExpiryFromYears(double years) =>
        DateTime.UtcNow.AddDays(years * 365);

    private static Option CreateOption(OptionType type, decimal strike, DateTime expiry) =>
        new(type == OptionType.Call ? "TEST_CALL" : "TEST_PUT",
            "TEST", type, strike, expiry, ExerciseStyle.European);

    private static MarketSnapshot CreateMarket(
        decimal spotPrice, decimal impliedVol, decimal riskFreeRate) =>
        new("TEST", spotPrice, impliedVol, riskFreeRate, DateTime.UtcNow);

    private decimal GetPrice(OptionType type, decimal spot, decimal strike,
        decimal vol, decimal rate, double years)
    {
        var expiry = ExpiryFromYears(years);
        var option = CreateOption(type, strike, expiry);
        var market = CreateMarket(spot, vol, rate);
        return _pricer.Price(option, market).TheoreticalPrice;
    }

    // ---------------------------------------------------------------
    // (a) Delta ~ [C(S+eps) - C(S-eps)] / (2*eps)
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call)]
    [InlineData(OptionType.Put)]
    public void DeltaMatchesFiniteDifference(OptionType type)
    {
        var eps = BaseSpot * 0.001m;
        var priceUp = GetPrice(type, BaseSpot + eps, BaseStrike, BaseVol, BaseRate, BaseYears);
        var priceDown = GetPrice(type, BaseSpot - eps, BaseStrike, BaseVol, BaseRate, BaseYears);
        var finiteDiffDelta = (priceUp - priceDown) / (2m * eps);

        var expiry = ExpiryFromYears(BaseYears);
        var option = CreateOption(type, BaseStrike, expiry);
        var market = CreateMarket(BaseSpot, BaseVol, BaseRate);
        var analyticalDelta = _pricer.ComputeGreeks(option, market).Delta;

        Assert.InRange((double)Math.Abs(analyticalDelta - finiteDiffDelta), 0.0, 0.01);
    }

    // ---------------------------------------------------------------
    // (b) Gamma ~ [C(S+eps) - 2*C(S) + C(S-eps)] / eps^2
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call)]
    [InlineData(OptionType.Put)]
    public void GammaMatchesFiniteDifference(OptionType type)
    {
        var eps = BaseSpot * 0.001m;
        var priceUp = GetPrice(type, BaseSpot + eps, BaseStrike, BaseVol, BaseRate, BaseYears);
        var priceMid = GetPrice(type, BaseSpot, BaseStrike, BaseVol, BaseRate, BaseYears);
        var priceDown = GetPrice(type, BaseSpot - eps, BaseStrike, BaseVol, BaseRate, BaseYears);
        var finiteDiffGamma = (priceUp - 2m * priceMid + priceDown) / (eps * eps);

        var expiry = ExpiryFromYears(BaseYears);
        var option = CreateOption(type, BaseStrike, expiry);
        var market = CreateMarket(BaseSpot, BaseVol, BaseRate);
        var analyticalGamma = _pricer.ComputeGreeks(option, market).Gamma;

        Assert.InRange((double)Math.Abs(analyticalGamma - finiteDiffGamma), 0.0, 0.01);
    }

    // ---------------------------------------------------------------
    // (c) Vega ~ [C(vol+eps) - C(vol-eps)] / (2*eps) / 100
    //     (scaled per 1% vol move)
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call)]
    [InlineData(OptionType.Put)]
    public void VegaMatchesFiniteDifference(OptionType type)
    {
        var eps = 0.001m;
        var priceUp = GetPrice(type, BaseSpot, BaseStrike, BaseVol + eps, BaseRate, BaseYears);
        var priceDown = GetPrice(type, BaseSpot, BaseStrike, BaseVol - eps, BaseRate, BaseYears);
        var finiteDiffVega = (priceUp - priceDown) / (2m * eps) / 100m;

        var expiry = ExpiryFromYears(BaseYears);
        var option = CreateOption(type, BaseStrike, expiry);
        var market = CreateMarket(BaseSpot, BaseVol, BaseRate);
        var analyticalVega = _pricer.ComputeGreeks(option, market).Vega;

        Assert.InRange((double)Math.Abs(analyticalVega - finiteDiffVega), 0.0, 0.01);
    }

    // ---------------------------------------------------------------
    // (d) Theta ~ [C(T-eps) - C(T)] / eps / 365
    //     (daily theta, eps = 1/365)
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call)]
    [InlineData(OptionType.Put)]
    public void ThetaMatchesFiniteDifference(OptionType type)
    {
        var epsYears = 1.0 / 365.0;
        var priceShortened = GetPrice(type, BaseSpot, BaseStrike, BaseVol, BaseRate,
            BaseYears - epsYears);
        var priceBase = GetPrice(type, BaseSpot, BaseStrike, BaseVol, BaseRate, BaseYears);
        var finiteDiffTheta = ((double)(priceShortened - priceBase)) / epsYears / 365.0;

        var expiry = ExpiryFromYears(BaseYears);
        var option = CreateOption(type, BaseStrike, expiry);
        var market = CreateMarket(BaseSpot, BaseVol, BaseRate);
        var analyticalTheta = (double)_pricer.ComputeGreeks(option, market).Theta;

        Assert.InRange(Math.Abs(analyticalTheta - finiteDiffTheta), 0.0, 0.01);
    }

    // ---------------------------------------------------------------
    // (e) Rho ~ [C(r+eps) - C(r-eps)] / (2*eps) / 100
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(OptionType.Call)]
    [InlineData(OptionType.Put)]
    public void RhoMatchesFiniteDifference(OptionType type)
    {
        var eps = 0.0001m;
        var priceUp = GetPrice(type, BaseSpot, BaseStrike, BaseVol, BaseRate + eps, BaseYears);
        var priceDown = GetPrice(type, BaseSpot, BaseStrike, BaseVol, BaseRate - eps, BaseYears);
        var finiteDiffRho = (priceUp - priceDown) / (2m * eps) / 100m;

        var expiry = ExpiryFromYears(BaseYears);
        var option = CreateOption(type, BaseStrike, expiry);
        var market = CreateMarket(BaseSpot, BaseVol, BaseRate);
        var analyticalRho = _pricer.ComputeGreeks(option, market).Rho;

        Assert.InRange((double)Math.Abs(analyticalRho - finiteDiffRho), 0.0, 0.01);
    }
}
