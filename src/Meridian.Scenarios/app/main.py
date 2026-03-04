"""
Meridian Scenario Analysis Service.

A FastAPI application that provides scenario analysis capabilities for the
Meridian options trading platform. Supports vol shocks, spot moves, rate shifts,
combined scenarios, and PnL attribution queries.
"""

import logging

from dotenv import load_dotenv
from fastapi import FastAPI

from app.models import (
    CombinedScenarioRequest,
    PortfolioScenarioResult,
    RateShiftRequest,
    SpotMoveRequest,
    VolShockRequest,
)
from app.pnl_attribution import get_pnl_attribution, get_pnl_summary
from app.scenarios import run_combined, run_rate_shift, run_spot_move, run_vol_shock

# Load environment variables from .env file if present
load_dotenv()

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)

app = FastAPI(
    title="Meridian Scenario Analysis",
    version="1.0.0",
    description=(
        "Scenario analysis and PnL attribution service for the Meridian "
        "options trading platform. Supports vol shocks, spot moves, rate "
        "shifts, combined stress tests, and historical PnL decomposition."
    ),
)


# ---------------------------------------------------------------------------
# Health check
# ---------------------------------------------------------------------------
@app.get("/health")
async def health():
    """Service health check endpoint."""
    return {"status": "healthy", "service": "meridian-scenarios"}


# ---------------------------------------------------------------------------
# Scenario endpoints
# ---------------------------------------------------------------------------
@app.post("/scenarios/vol-shock", response_model=PortfolioScenarioResult)
async def vol_shock(request: VolShockRequest):
    """
    Apply a volatility shock to a single symbol's positions.

    The shock_percent is a relative bump to the base implied volatility.
    For example, shock_percent=20.0 means the vol increases by 20% of its
    base value (e.g., 0.20 -> 0.24).
    """
    return run_vol_shock(request.symbol, request.shock_percent)


@app.post("/scenarios/spot-move", response_model=PortfolioScenarioResult)
async def spot_move(request: SpotMoveRequest):
    """
    Apply a spot price move to a single symbol's positions.

    The move_percent is a percentage move in the underlying spot price.
    For example, move_percent=-5.0 means the spot drops by 5%.
    """
    return run_spot_move(request.symbol, request.move_percent)


@app.post("/scenarios/rate-shift", response_model=PortfolioScenarioResult)
async def rate_shift(request: RateShiftRequest):
    """
    Apply a parallel interest rate shift across all positions.

    The shift_bps is in basis points. For example, shift_bps=25 means
    the risk-free rate increases by 25bps (0.25%).
    """
    return run_rate_shift(request.shift_bps)


@app.post("/scenarios/combined", response_model=PortfolioScenarioResult)
async def combined(request: CombinedScenarioRequest):
    """
    Apply a combined scenario with optional vol shock, spot move, and rate shift.

    If a symbol is specified, vol and spot shocks apply only to that symbol;
    otherwise they apply to all symbols. Rate shift always applies globally.
    """
    return run_combined(request)


# ---------------------------------------------------------------------------
# PnL attribution endpoints
# ---------------------------------------------------------------------------
@app.get("/pnl/attribution/{position_id}")
async def pnl_attribution(
    position_id: str,
    from_time: str = None,
    to_time: str = None,
):
    """
    Get PnL attribution breakdown for a single position.

    Decomposes PnL into delta, gamma, vega, theta, and unexplained components.
    When SQL Server is unavailable, returns mock data with a note.
    """
    return get_pnl_attribution(position_id, from_time, to_time)


@app.get("/pnl/summary")
async def pnl_summary(
    from_time: str = None,
    to_time: str = None,
):
    """
    Get aggregate PnL attribution summary across all positions.

    Returns per-position attribution breakdown and portfolio-level totals.
    When SQL Server is unavailable, returns mock data with a note.
    """
    return get_pnl_summary(from_time, to_time)
