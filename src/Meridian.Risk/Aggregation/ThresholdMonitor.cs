using System.Reactive.Linq;
using Meridian.Common.Configuration;
using Meridian.Common.Models;

namespace Meridian.Risk.Aggregation;

public class ThresholdMonitor
{
    private readonly RiskLimitsConfig _limits;
    private readonly Dictionary<AlertType, DateTime> _lastAlertTime = new();

    public ThresholdMonitor(RiskLimitsConfig limits)
    {
        _limits = limits;
    }

    public IObservable<RiskAlert> MonitorThresholds(IObservable<PortfolioSnapshot> portfolio)
    {
        return portfolio.SelectMany(snap =>
        {
            var alerts = new List<RiskAlert>();
            var greeks = snap.AggregateGreeks;
            var now = snap.Timestamp;

            CheckThreshold(alerts, AlertType.DeltaBreached, Math.Abs(greeks.Delta),
                _limits.MaxPortfolioDelta, now, $"Portfolio delta {greeks.Delta:F2} exceeds limit {_limits.MaxPortfolioDelta}");

            CheckThreshold(alerts, AlertType.GammaBreached, Math.Abs(greeks.Gamma),
                _limits.MaxPortfolioGamma, now, $"Portfolio gamma {greeks.Gamma:F2} exceeds limit {_limits.MaxPortfolioGamma}");

            CheckThreshold(alerts, AlertType.VegaBreached, Math.Abs(greeks.Vega),
                _limits.MaxPortfolioVega, now, $"Portfolio vega {greeks.Vega:F2} exceeds limit {_limits.MaxPortfolioVega}");

            // PnL drawdown check
            if (snap.TotalUnrealizedPnl < 0)
            {
                // Simple drawdown: treat negative PnL as potential drawdown
                var drawdownPercent = Math.Abs(snap.TotalUnrealizedPnl); // simplified
                CheckThreshold(alerts, AlertType.PnlDrawdown, drawdownPercent,
                    _limits.MaxDrawdownPercent * 100, // scale
                    now, $"PnL drawdown: {snap.TotalUnrealizedPnl:F2}");
            }

            // Concentration check
            foreach (var kvp in snap.Positions.GroupBy(p => p.Value.Position.Instrument.Underlier))
            {
                var totalAbsDelta = snap.Positions.Values.Sum(p => Math.Abs(p.Greeks.Delta * p.Position.Quantity));
                if (totalAbsDelta > 0)
                {
                    var nameAbsDelta = kvp.Sum(p => Math.Abs(p.Value.Greeks.Delta * p.Value.Position.Quantity));
                    var concentration = nameAbsDelta / totalAbsDelta;
                    if (concentration > _limits.MaxSingleNameConcentration)
                    {
                        CheckThreshold(alerts, AlertType.ConcentrationBreached, concentration,
                            _limits.MaxSingleNameConcentration, now,
                            $"Concentration in {kvp.Key}: {concentration:P1} exceeds {_limits.MaxSingleNameConcentration:P1}");
                    }
                }
            }

            return alerts;
        });
    }

    private void CheckThreshold(List<RiskAlert> alerts, AlertType type, decimal currentValue,
        decimal threshold, DateTime now, string description)
    {
        if (currentValue <= threshold) return;

        // Cooldown check
        if (_lastAlertTime.TryGetValue(type, out var lastTime) &&
            (now - lastTime).TotalSeconds < _limits.AlertCooldownSeconds)
            return;

        var severity = currentValue > threshold * 1.5m ? AlertSeverity.Critical : AlertSeverity.Warning;

        alerts.Add(new RiskAlert(type, description, threshold, currentValue, severity, now));
        _lastAlertTime[type] = now;
    }
}
