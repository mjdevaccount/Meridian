"""
Pydantic models for the Meridian Scenario Analysis service.
Defines request/response schemas for scenario shocks and PnL attribution.
"""

from pydantic import BaseModel
from typing import Optional


class VolShockRequest(BaseModel):
    symbol: str
    shock_percent: float  # e.g., 20.0 means +20% vol


class SpotMoveRequest(BaseModel):
    symbol: str
    move_percent: float  # e.g., -5.0 means -5% spot


class RateShiftRequest(BaseModel):
    shift_bps: float  # e.g., 25 means +25bps


class CombinedScenarioRequest(BaseModel):
    vol_shock: Optional[float] = None
    spot_move: Optional[float] = None
    rate_shift: Optional[float] = None
    symbol: Optional[str] = None  # if None, apply to all


class ScenarioResult(BaseModel):
    position_id: str
    symbol: str
    option_type: str
    strike: float
    quantity: int
    base_price: float
    scenario_price: float
    pnl_impact: float
    delta_change: float
    gamma_change: float
    vega_change: float


class PortfolioScenarioResult(BaseModel):
    scenario_description: str
    total_pnl_impact: float
    positions: list[ScenarioResult]


class PnlAttribution(BaseModel):
    position_id: str
    delta_pnl: float
    gamma_pnl: float
    vega_pnl: float
    theta_pnl: float
    unexplained_pnl: float
    total_pnl: float


class PnlSummary(BaseModel):
    from_time: str
    to_time: str
    total_pnl: float
    attributions: list[PnlAttribution]
