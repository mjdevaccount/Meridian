using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.Pricing.Models;

/// <summary>
/// Cox-Ross-Rubinstein (CRR) binomial tree pricer.
/// Supports both European and American exercise styles, demonstrating the
/// extensibility of the IPricingModel interface beyond Black-Scholes.
/// This is an accuracy-oriented implementation (default 200 steps);
/// performance is secondary to correctness.
/// </summary>
public class BinomialTreePricer : IPricingModel
{
    private readonly int _steps;

    public BinomialTreePricer(int steps = 200)
    {
        _steps = steps;
    }

    public string ModelName => "Binomial Tree (CRR)";

    public PricingResult Price(Option option, MarketSnapshot market)
    {
        double S = (double)market.SpotPrice;
        double K = (double)option.Strike;
        double r = (double)market.RiskFreeRate;
        double sigma = (double)market.ImpliedVol;
        double T = (option.Expiry - market.Timestamp).TotalDays / 365.25;

        bool isCall = option.Type == OptionType.Call;
        bool isAmerican = option.Style == ExerciseStyle.American;

        // Edge case: expired option
        if (T <= 0.0)
        {
            decimal intrinsic = isCall
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
            decimal intrinsic = isCall
                ? Math.Max(market.SpotPrice - discountedStrike, 0m)
                : Math.Max(discountedStrike - market.SpotPrice, 0m);

            return new PricingResult(
                TheoreticalPrice: intrinsic,
                IntrinsicValue: intrinsic,
                TimeValue: 0m,
                ImpliedVol: market.ImpliedVol);
        }

        double price = BuildTree(S, K, r, sigma, T, isCall, isAmerican, _steps);
        price = Math.Max(price, 0.0);

        decimal theoDecimal = Math.Round((decimal)price, 6);

        decimal intrinsicValue = isCall
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

        bool isCall = option.Type == OptionType.Call;
        bool isAmerican = option.Style == ExerciseStyle.American;

        // Edge case: expired or near-expired option
        if (T <= 1e-10)
        {
            var expDelta = isCall
                ? (market.SpotPrice > option.Strike ? 1m : 0m)
                : (market.SpotPrice < option.Strike ? -1m : 0m);
            return new GreeksResult(expDelta, 0m, 0m, 0m, 0m);
        }

        // Edge case: zero vol
        if (sigma <= 1e-10)
        {
            var zeroVolDelta = isCall
                ? (market.SpotPrice > option.Strike ? 1m : 0m)
                : (market.SpotPrice < option.Strike ? -1m : 0m);
            return new GreeksResult(zeroVolDelta, 0m, 0m, 0m, 0m);
        }

        // Mid price
        double pMid = BuildTree(S, K, r, sigma, T, isCall, isAmerican, _steps);

        // Delta & Gamma: bump S by +/- 0.5%
        double dS = S * 0.005;
        double pUp = BuildTree(S + dS, K, r, sigma, T, isCall, isAmerican, _steps);
        double pDown = BuildTree(S - dS, K, r, sigma, T, isCall, isAmerican, _steps);

        double delta = (pUp - pDown) / (2.0 * dS);
        double gamma = (pUp - 2.0 * pMid + pDown) / (dS * dS);

        // Vega: bump vol by +/- 0.5%, report per 1% vol move
        double dSigma = 0.005;
        double pVolUp = BuildTree(S, K, r, sigma + dSigma, T, isCall, isAmerican, _steps);
        double pVolDown = BuildTree(S, K, r, sigma - dSigma, T, isCall, isAmerican, _steps);
        double vega = (pVolUp - pVolDown) / (2.0 * dSigma) / 100.0;

        // Theta: reprice with T - 1/365, compute daily decay
        double T1 = T - 1.0 / 365.0;
        double pTheta;
        if (T1 <= 0.0)
        {
            // If less than a day to expiry, use intrinsic as the shifted price
            pTheta = isCall
                ? Math.Max(S - K, 0.0)
                : Math.Max(K - S, 0.0);
        }
        else
        {
            pTheta = BuildTree(S, K, r, sigma, T1, isCall, isAmerican, _steps);
        }
        double theta = pTheta - pMid; // daily theta (negative for long options)

        // Rho: bump r by +/- 0.1%, report per 1% rate move
        double dR = 0.001;
        double pRUp = BuildTree(S, K, r + dR, sigma, T, isCall, isAmerican, _steps);
        double pRDown = BuildTree(S, K, r - dR, sigma, T, isCall, isAmerican, _steps);
        double rho = (pRUp - pRDown) / (2.0 * dR) / 100.0;

        return new GreeksResult(
            Delta: Math.Round((decimal)delta, 6),
            Gamma: Math.Round((decimal)gamma, 6),
            Vega: Math.Round((decimal)vega, 6),
            Theta: Math.Round((decimal)theta, 6),
            Rho: Math.Round((decimal)rho, 6));
    }

    /// <summary>
    /// Builds a CRR binomial tree and returns the option price.
    /// Uses a single array (backward induction) to keep memory usage at O(N).
    /// </summary>
    private static double BuildTree(
        double S, double K, double r, double sigma, double T,
        bool isCall, bool isAmerican, int steps)
    {
        double dt = T / steps;
        double u = Math.Exp(sigma * Math.Sqrt(dt));
        double d = 1.0 / u;
        double discount = Math.Exp(-r * dt);
        double p = (Math.Exp(r * dt) - d) / (u - d);
        double q = 1.0 - p;

        // Initialize terminal payoffs
        var prices = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double spot = S * Math.Pow(u, steps - i) * Math.Pow(d, i);
            prices[i] = isCall
                ? Math.Max(spot - K, 0.0)
                : Math.Max(K - spot, 0.0);
        }

        // Backward induction
        for (int step = steps - 1; step >= 0; step--)
        {
            for (int i = 0; i <= step; i++)
            {
                // Continuation value (discounted expected value under risk-neutral measure)
                prices[i] = discount * (p * prices[i] + q * prices[i + 1]);

                // American exercise: compare with immediate exercise value
                if (isAmerican)
                {
                    double spot = S * Math.Pow(u, step - i) * Math.Pow(d, i);
                    double exerciseValue = isCall
                        ? Math.Max(spot - K, 0.0)
                        : Math.Max(K - spot, 0.0);
                    prices[i] = Math.Max(prices[i], exerciseValue);
                }
            }
        }

        return prices[0];
    }
}
