using MathNet.Numerics.Distributions;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.Pricing.Models;

/// <summary>
/// Full analytical Black-Scholes pricing model for European options.
/// Computes theoretical price, intrinsic/time value, and all first-order Greeks
/// (Delta, Gamma, Vega, Theta, Rho) using closed-form solutions.
/// </summary>
public class BlackScholesPricer : IPricingModel
{
    public string ModelName => "Black-Scholes";

    public PricingResult Price(Option option, MarketSnapshot market)
    {
        double S = (double)market.SpotPrice;
        double K = (double)option.Strike;
        double r = (double)market.RiskFreeRate;
        double sigma = (double)market.ImpliedVol;
        double T = (option.Expiry - market.Timestamp).TotalDays / 365.25;

        // Edge case: expired option
        if (T <= 0.0)
        {
            decimal intrinsic = option.Type == OptionType.Call
                ? Math.Max(market.SpotPrice - option.Strike, 0m)
                : Math.Max(option.Strike - market.SpotPrice, 0m);

            return new PricingResult(
                TheoreticalPrice: intrinsic,
                IntrinsicValue: intrinsic,
                TimeValue: 0m,
                ImpliedVol: market.ImpliedVol);
        }

        // Edge case: zero or near-zero volatility
        if (sigma <= 1e-10)
        {
            decimal discountedStrike = (decimal)(K * Math.Exp(-r * T));
            decimal intrinsic = option.Type == OptionType.Call
                ? Math.Max(market.SpotPrice - discountedStrike, 0m)
                : Math.Max(discountedStrike - market.SpotPrice, 0m);

            return new PricingResult(
                TheoreticalPrice: intrinsic,
                IntrinsicValue: intrinsic,
                TimeValue: 0m,
                ImpliedVol: market.ImpliedVol);
        }

        double sqrtT = Math.Sqrt(T);
        double d1 = (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * sqrtT);
        double d2 = d1 - sigma * sqrtT;

        double nd1 = Normal.CDF(0, 1, d1);
        double nd2 = Normal.CDF(0, 1, d2);
        double nNegD1 = Normal.CDF(0, 1, -d1);
        double nNegD2 = Normal.CDF(0, 1, -d2);

        double theoreticalPrice;
        if (option.Type == OptionType.Call)
        {
            // Call = S * N(d1) - K * e^(-rT) * N(d2)
            theoreticalPrice = S * nd1 - K * Math.Exp(-r * T) * nd2;
        }
        else
        {
            // Put = K * e^(-rT) * N(-d2) - S * N(-d1)
            theoreticalPrice = K * Math.Exp(-r * T) * nNegD2 - S * nNegD1;
        }

        // Ensure price is non-negative (numerical edge cases)
        theoreticalPrice = Math.Max(theoreticalPrice, 0.0);

        decimal theoDecimal = Math.Round((decimal)theoreticalPrice, 6);

        decimal intrinsicValue = option.Type == OptionType.Call
            ? Math.Max(market.SpotPrice - option.Strike, 0m)
            : Math.Max(option.Strike - market.SpotPrice, 0m);

        decimal timeValue = Math.Max(theoDecimal - intrinsicValue, 0m);

        return new PricingResult(
            TheoreticalPrice: theoDecimal,
            IntrinsicValue: intrinsicValue,
            TimeValue: timeValue,
            ImpliedVol: market.ImpliedVol);
    }

    public GreeksResult ComputeGreeks(Option option, MarketSnapshot market)
    {
        double S = (double)market.SpotPrice;
        double K = (double)option.Strike;
        double r = (double)market.RiskFreeRate;
        double sigma = (double)market.ImpliedVol;
        double T = (option.Expiry - market.Timestamp).TotalDays / 365.25;

        // Edge case: expired or near-expired option
        if (T <= 1e-10)
        {
            var expDelta = option.Type == OptionType.Call
                ? (market.SpotPrice > option.Strike ? 1m : 0m)
                : (market.SpotPrice < option.Strike ? -1m : 0m);
            return new GreeksResult(expDelta, 0m, 0m, 0m, 0m);
        }

        // Edge case: zero vol
        if (sigma <= 1e-10)
        {
            var zeroVolDelta = option.Type == OptionType.Call
                ? (market.SpotPrice > option.Strike ? 1m : 0m)
                : (market.SpotPrice < option.Strike ? -1m : 0m);
            return new GreeksResult(zeroVolDelta, 0m, 0m, 0m, 0m);
        }

        double sqrtT = Math.Sqrt(T);
        double d1 = (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * sqrtT);
        double d2 = d1 - sigma * sqrtT;

        double nd1 = Normal.CDF(0, 1, d1);
        double nd2 = Normal.CDF(0, 1, d2);
        double nNegD1 = Normal.CDF(0, 1, -d1);
        double nNegD2 = Normal.CDF(0, 1, -d2);
        double phiD1 = Normal.PDF(0, 1, d1);

        double expNegRT = Math.Exp(-r * T);

        // Delta
        double delta = option.Type == OptionType.Call
            ? nd1
            : nd1 - 1.0;

        // Gamma: phi(d1) / (S * sigma * sqrt(T))
        double gamma = phiD1 / (S * sigma * sqrtT);

        // Vega: S * phi(d1) * sqrt(T) / 100 (per 1% vol move)
        double vega = S * phiD1 * sqrtT / 100.0;

        // Theta (daily):
        //   Call: -(S * phi(d1) * sigma) / (2 * sqrt(T)) - r * K * e^(-rT) * N(d2)
        //   Put:  -(S * phi(d1) * sigma) / (2 * sqrt(T)) + r * K * e^(-rT) * N(-d2)
        //   Divide by 365 for daily theta
        double thetaAnnual;
        if (option.Type == OptionType.Call)
        {
            thetaAnnual = -(S * phiD1 * sigma) / (2.0 * sqrtT) - r * K * expNegRT * nd2;
        }
        else
        {
            thetaAnnual = -(S * phiD1 * sigma) / (2.0 * sqrtT) + r * K * expNegRT * nNegD2;
        }
        double thetaDaily = thetaAnnual / 365.0;

        // Rho: K * T * e^(-rT) * N(d2) / 100 for calls; -K * T * e^(-rT) * N(-d2) / 100 for puts
        double rho = option.Type == OptionType.Call
            ? K * T * expNegRT * nd2 / 100.0
            : -K * T * expNegRT * nNegD2 / 100.0;

        return new GreeksResult(
            Delta: Math.Round((decimal)delta, 6),
            Gamma: Math.Round((decimal)gamma, 6),
            Vega: Math.Round((decimal)vega, 6),
            Theta: Math.Round((decimal)thetaDaily, 6),
            Rho: Math.Round((decimal)rho, 6));
    }
}
