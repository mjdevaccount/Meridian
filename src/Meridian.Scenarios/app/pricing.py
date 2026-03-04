"""
Black-Scholes pricing and Greeks computation using scipy.stats.norm.
Mirrors the analytical formulas from the C# BlackScholesPricer in Meridian.Pricing.
"""

import numpy as np
from scipy.stats import norm


def bs_price(S: float, K: float, T: float, r: float, sigma: float,
             option_type: str = "call") -> float:
    """
    Compute the Black-Scholes theoretical price for a European option.

    Parameters:
        S:           Spot price of the underlying
        K:           Strike price
        T:           Time to expiry in years
        r:           Risk-free interest rate (annualized, e.g. 0.05 for 5%)
        sigma:       Implied volatility (annualized, e.g. 0.20 for 20%)
        option_type: "call" or "put"

    Returns:
        Theoretical option price.
    """
    if T <= 0:
        if option_type == "call":
            return max(S - K, 0.0)
        return max(K - S, 0.0)

    if sigma <= 1e-10:
        discounted_strike = K * np.exp(-r * T)
        if option_type == "call":
            return max(S - discounted_strike, 0.0)
        return max(discounted_strike - S, 0.0)

    sqrt_T = np.sqrt(T)
    d1 = (np.log(S / K) + (r + 0.5 * sigma ** 2) * T) / (sigma * sqrt_T)
    d2 = d1 - sigma * sqrt_T

    if option_type == "call":
        price = S * norm.cdf(d1) - K * np.exp(-r * T) * norm.cdf(d2)
    else:
        price = K * np.exp(-r * T) * norm.cdf(-d2) - S * norm.cdf(-d1)

    return max(price, 0.0)


def bs_greeks(S: float, K: float, T: float, r: float, sigma: float,
              option_type: str = "call") -> dict:
    """
    Compute Black-Scholes Greeks for a European option using analytical formulas.

    Parameters:
        S:           Spot price of the underlying
        K:           Strike price
        T:           Time to expiry in years
        r:           Risk-free interest rate (annualized)
        sigma:       Implied volatility (annualized)
        option_type: "call" or "put"

    Returns:
        Dictionary with keys: delta, gamma, vega, theta, rho
        - delta:  dV/dS
        - gamma:  d^2V/dS^2
        - vega:   dV/dsigma per 1% vol move
        - theta:  daily time decay (dV/dt / 365)
        - rho:    dV/dr per 1% rate move
    """
    # Edge case: expired or near-expired option
    if T <= 1e-10:
        if option_type == "call":
            delta = 1.0 if S > K else 0.0
        else:
            delta = -1.0 if S < K else 0.0
        return {"delta": delta, "gamma": 0.0, "vega": 0.0, "theta": 0.0, "rho": 0.0}

    # Edge case: zero volatility
    if sigma <= 1e-10:
        if option_type == "call":
            delta = 1.0 if S > K else 0.0
        else:
            delta = -1.0 if S < K else 0.0
        return {"delta": delta, "gamma": 0.0, "vega": 0.0, "theta": 0.0, "rho": 0.0}

    sqrt_T = np.sqrt(T)
    d1 = (np.log(S / K) + (r + 0.5 * sigma ** 2) * T) / (sigma * sqrt_T)
    d2 = d1 - sigma * sqrt_T

    nd1 = norm.cdf(d1)
    nd2 = norm.cdf(d2)
    n_neg_d1 = norm.cdf(-d1)
    n_neg_d2 = norm.cdf(-d2)
    phi_d1 = norm.pdf(d1)

    exp_neg_rT = np.exp(-r * T)

    # Delta
    if option_type == "call":
        delta = nd1
    else:
        delta = nd1 - 1.0

    # Gamma: phi(d1) / (S * sigma * sqrt(T))
    gamma = phi_d1 / (S * sigma * sqrt_T)

    # Vega: S * phi(d1) * sqrt(T) / 100 (per 1% vol move)
    vega = S * phi_d1 * sqrt_T / 100.0

    # Theta (daily):
    #   Call: -(S * phi(d1) * sigma) / (2 * sqrt(T)) - r * K * e^(-rT) * N(d2)
    #   Put:  -(S * phi(d1) * sigma) / (2 * sqrt(T)) + r * K * e^(-rT) * N(-d2)
    #   Divide by 365 for daily theta
    if option_type == "call":
        theta_annual = (-(S * phi_d1 * sigma) / (2.0 * sqrt_T)
                        - r * K * exp_neg_rT * nd2)
    else:
        theta_annual = (-(S * phi_d1 * sigma) / (2.0 * sqrt_T)
                        + r * K * exp_neg_rT * n_neg_d2)
    theta_daily = theta_annual / 365.0

    # Rho: per 1% rate move
    #   Call: K * T * e^(-rT) * N(d2) / 100
    #   Put: -K * T * e^(-rT) * N(-d2) / 100
    if option_type == "call":
        rho = K * T * exp_neg_rT * nd2 / 100.0
    else:
        rho = -K * T * exp_neg_rT * n_neg_d2 / 100.0

    return {
        "delta": round(delta, 6),
        "gamma": round(gamma, 6),
        "vega": round(vega, 6),
        "theta": round(theta_daily, 6),
        "rho": round(rho, 6),
    }
