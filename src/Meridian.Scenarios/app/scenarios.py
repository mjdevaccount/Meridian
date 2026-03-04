"""
Scenario engine for the Meridian project.

Provides a hardcoded sample portfolio matching the C# Pricing and Risk services,
and functions to run vol shocks, spot moves, rate shifts, and combined scenarios.
Each scenario re-prices positions under stressed parameters and reports per-position
PnL impact along with Greek changes.
"""

from datetime import datetime, timedelta, timezone
from typing import Optional

from app.models import (
    CombinedScenarioRequest,
    PortfolioScenarioResult,
    ScenarioResult,
)
from app.pricing import bs_greeks, bs_price

# ---------------------------------------------------------------------------
# Base market data -- mirrors the C# service configuration
# ---------------------------------------------------------------------------
BASE_SPOTS: dict[str, float] = {
    "SIM_A": 100.0,
    "SIM_B": 250.0,
    "SIM_C": 50.0,
    "SIM_D": 175.0,
    "SIM_E": 80.0,
}

BASE_VOLS: dict[str, float] = {
    "SIM_A": 0.20,
    "SIM_B": 0.30,
    "SIM_C": 0.25,
    "SIM_D": 0.18,
    "SIM_E": 0.35,
}

BASE_RATE: float = 0.05  # 5% risk-free rate


# ---------------------------------------------------------------------------
# Position descriptor used internally by the scenario engine
# ---------------------------------------------------------------------------
class PositionDef:
    """Lightweight position definition for scenario analysis."""

    def __init__(
        self,
        position_id: str,
        symbol: str,
        underlier: str,
        option_type: str,
        strike: float,
        expiry_days: int,
        quantity: int,
        entry_price: float,
    ):
        self.position_id = position_id
        self.symbol = symbol
        self.underlier = underlier
        self.option_type = option_type  # "call" or "put"
        self.strike = strike
        self.expiry_days = expiry_days  # days until expiry
        self.quantity = quantity
        self.entry_price = entry_price


def get_sample_portfolio() -> list[PositionDef]:
    """
    Build a sample portfolio of 12 option positions across 5 underliers,
    matching the structure used in the C# Pricing and Risk services.

    For each symbol:
      - Long 10x ATM call, ~30-day expiry
      - Short 5x OTM put (90% strike), ~90-day expiry

    Plus two extra positions:
      - Long 5x deep-ITM call on SIM_A, strike 80, ~180-day expiry
      - Long 20x OTM put on SIM_B, strike 200, ~30-day expiry (tail hedge)
    """
    positions: list[PositionDef] = []
    pos_id = 1

    for sym, spot in BASE_SPOTS.items():
        # Position 1: Long ATM Call, 1-month expiry
        positions.append(PositionDef(
            position_id=f"POS-{pos_id:03d}",
            symbol=f"{sym}-C-{spot:.0f}-1M",
            underlier=sym,
            option_type="call",
            strike=round(spot),
            expiry_days=30,
            quantity=10,
            entry_price=round(spot * 0.03, 2),
        ))
        pos_id += 1

        # Position 2: Short OTM Put, 3-month expiry
        otm_strike = round(spot * 0.90)
        positions.append(PositionDef(
            position_id=f"POS-{pos_id:03d}",
            symbol=f"{sym}-P-{otm_strike:.0f}-3M",
            underlier=sym,
            option_type="put",
            strike=otm_strike,
            expiry_days=90,
            quantity=-5,
            entry_price=round(spot * 0.015, 2),
        ))
        pos_id += 1

    # Extra position: Long deep ITM call on SIM_A, 6-month expiry
    positions.append(PositionDef(
        position_id=f"POS-{pos_id:03d}",
        symbol="SIM_A-C-80-6M",
        underlier="SIM_A",
        option_type="call",
        strike=80.0,
        expiry_days=180,
        quantity=5,
        entry_price=22.50,
    ))
    pos_id += 1

    # Extra position: Long OTM put on SIM_B, 1-month expiry (tail hedge)
    positions.append(PositionDef(
        position_id=f"POS-{pos_id:03d}",
        symbol="SIM_B-P-200-1M",
        underlier="SIM_B",
        option_type="put",
        strike=200.0,
        expiry_days=30,
        quantity=20,
        entry_price=0.50,
    ))

    return positions


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------
def _time_to_expiry(expiry_days: int) -> float:
    """Convert days to expiry into fractional years."""
    return expiry_days / 365.25


def _price_position(
    pos: PositionDef,
    spot: float,
    vol: float,
    rate: float,
) -> tuple[float, dict]:
    """Price a single position and return (price, greeks_dict)."""
    T = _time_to_expiry(pos.expiry_days)
    price = bs_price(spot, pos.strike, T, rate, vol, pos.option_type)
    greeks = bs_greeks(spot, pos.strike, T, rate, vol, pos.option_type)
    return price, greeks


def _run_scenario(
    description: str,
    spot_overrides: Optional[dict[str, float]] = None,
    vol_overrides: Optional[dict[str, float]] = None,
    rate_override: Optional[float] = None,
) -> PortfolioScenarioResult:
    """
    Generic scenario runner. Computes base and scenario prices for every
    position in the portfolio and returns per-position results with PnL impact.
    """
    portfolio = get_sample_portfolio()
    results: list[ScenarioResult] = []
    total_pnl = 0.0

    for pos in portfolio:
        underlier = pos.underlier
        base_spot = BASE_SPOTS[underlier]
        base_vol = BASE_VOLS[underlier]
        base_rate = BASE_RATE

        # Scenario parameters
        scen_spot = spot_overrides.get(underlier, base_spot) if spot_overrides else base_spot
        scen_vol = vol_overrides.get(underlier, base_vol) if vol_overrides else base_vol
        scen_rate = rate_override if rate_override is not None else base_rate

        # Base pricing
        base_price, base_greeks = _price_position(pos, base_spot, base_vol, base_rate)

        # Scenario pricing
        scen_price, scen_greeks = _price_position(pos, scen_spot, scen_vol, scen_rate)

        # PnL impact = (scenario_price - base_price) * quantity
        pnl_impact = (scen_price - base_price) * pos.quantity

        total_pnl += pnl_impact

        results.append(ScenarioResult(
            position_id=pos.position_id,
            symbol=pos.underlier,
            option_type=pos.option_type,
            strike=pos.strike,
            quantity=pos.quantity,
            base_price=round(base_price, 6),
            scenario_price=round(scen_price, 6),
            pnl_impact=round(pnl_impact, 2),
            delta_change=round(scen_greeks["delta"] - base_greeks["delta"], 6),
            gamma_change=round(scen_greeks["gamma"] - base_greeks["gamma"], 6),
            vega_change=round(scen_greeks["vega"] - base_greeks["vega"], 6),
        ))

    return PortfolioScenarioResult(
        scenario_description=description,
        total_pnl_impact=round(total_pnl, 2),
        positions=results,
    )


# ---------------------------------------------------------------------------
# Public scenario functions
# ---------------------------------------------------------------------------
def run_vol_shock(symbol: str, shock_percent: float) -> PortfolioScenarioResult:
    """
    Apply a volatility shock to a single symbol (or all symbols if symbol is "*").

    Args:
        symbol:        Underlier symbol (e.g. "SIM_A") or "*" for all.
        shock_percent: Percentage change in vol (e.g. 20.0 means +20% relative bump).
    """
    vol_overrides: dict[str, float] = {}

    if symbol == "*":
        for sym, base_vol in BASE_VOLS.items():
            vol_overrides[sym] = base_vol * (1.0 + shock_percent / 100.0)
        desc = f"Vol shock: {shock_percent:+.1f}% across all symbols"
    else:
        if symbol not in BASE_VOLS:
            # Apply to matching symbol; if not found, return empty result
            return PortfolioScenarioResult(
                scenario_description=f"Unknown symbol: {symbol}",
                total_pnl_impact=0.0,
                positions=[],
            )
        vol_overrides[symbol] = BASE_VOLS[symbol] * (1.0 + shock_percent / 100.0)
        desc = f"Vol shock: {shock_percent:+.1f}% on {symbol}"

    return _run_scenario(desc, vol_overrides=vol_overrides)


def run_spot_move(symbol: str, move_percent: float) -> PortfolioScenarioResult:
    """
    Apply a spot price move to a single symbol (or all symbols if symbol is "*").

    Args:
        symbol:       Underlier symbol (e.g. "SIM_B") or "*" for all.
        move_percent: Percentage move in spot (e.g. -5.0 means -5%).
    """
    spot_overrides: dict[str, float] = {}

    if symbol == "*":
        for sym, base_spot in BASE_SPOTS.items():
            spot_overrides[sym] = base_spot * (1.0 + move_percent / 100.0)
        desc = f"Spot move: {move_percent:+.1f}% across all symbols"
    else:
        if symbol not in BASE_SPOTS:
            return PortfolioScenarioResult(
                scenario_description=f"Unknown symbol: {symbol}",
                total_pnl_impact=0.0,
                positions=[],
            )
        spot_overrides[symbol] = BASE_SPOTS[symbol] * (1.0 + move_percent / 100.0)
        desc = f"Spot move: {move_percent:+.1f}% on {symbol}"

    return _run_scenario(desc, spot_overrides=spot_overrides)


def run_rate_shift(shift_bps: float) -> PortfolioScenarioResult:
    """
    Apply a parallel interest rate shift across all positions.

    Args:
        shift_bps: Shift in basis points (e.g. 25 means +25bps = +0.25%).
    """
    new_rate = BASE_RATE + shift_bps / 10000.0
    desc = f"Rate shift: {shift_bps:+.0f}bps (new rate: {new_rate:.4f})"
    return _run_scenario(desc, rate_override=new_rate)


def run_combined(request: CombinedScenarioRequest) -> PortfolioScenarioResult:
    """
    Apply a combined scenario with optional vol shock, spot move, and rate shift.
    If a symbol is specified, vol and spot shocks apply only to that symbol;
    otherwise they apply to all symbols.
    """
    vol_overrides: dict[str, float] = {}
    spot_overrides: dict[str, float] = {}
    rate_override: Optional[float] = None

    desc_parts: list[str] = []
    target = request.symbol if request.symbol else "*"

    # Vol shock
    if request.vol_shock is not None:
        if target == "*":
            for sym, base_vol in BASE_VOLS.items():
                vol_overrides[sym] = base_vol * (1.0 + request.vol_shock / 100.0)
        elif target in BASE_VOLS:
            vol_overrides[target] = BASE_VOLS[target] * (1.0 + request.vol_shock / 100.0)
        desc_parts.append(f"vol {request.vol_shock:+.1f}%")

    # Spot move
    if request.spot_move is not None:
        if target == "*":
            for sym, base_spot in BASE_SPOTS.items():
                spot_overrides[sym] = base_spot * (1.0 + request.spot_move / 100.0)
        elif target in BASE_SPOTS:
            spot_overrides[target] = BASE_SPOTS[target] * (1.0 + request.spot_move / 100.0)
        desc_parts.append(f"spot {request.spot_move:+.1f}%")

    # Rate shift
    if request.rate_shift is not None:
        rate_override = BASE_RATE + request.rate_shift / 10000.0
        desc_parts.append(f"rate {request.rate_shift:+.0f}bps")

    target_desc = f" on {target}" if target != "*" else " across all"
    description = f"Combined scenario: {', '.join(desc_parts)}{target_desc}"

    return _run_scenario(
        description,
        spot_overrides=spot_overrides if spot_overrides else None,
        vol_overrides=vol_overrides if vol_overrides else None,
        rate_override=rate_override,
    )
