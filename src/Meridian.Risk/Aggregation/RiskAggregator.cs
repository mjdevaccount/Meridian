using System.Reactive.Linq;
using Meridian.Common.Models;

namespace Meridian.Risk.Aggregation;

public record RiskMetrics(
    GreeksResult AggregateGreeks,
    decimal TotalPnl,
    int PositionCount,
    Dictionary<string, decimal> ConcentrationByUnderlier,
    DateTime Timestamp);

public class RiskAggregator
{
    public IObservable<RiskMetrics> AggregateRisk(IObservable<PortfolioSnapshot> portfolio)
    {
        return portfolio.Select(snap =>
        {
            var concentration = new Dictionary<string, decimal>();
            decimal totalAbsDelta = 0;

            foreach (var kvp in snap.Positions)
            {
                var pos = kvp.Value;
                var underlier = pos.Position.Instrument.Underlier;
                var absDelta = Math.Abs(pos.Greeks.Delta * pos.Position.Quantity);
                totalAbsDelta += absDelta;

                if (concentration.ContainsKey(underlier))
                    concentration[underlier] += absDelta;
                else
                    concentration[underlier] = absDelta;
            }

            // Normalize to percentage
            if (totalAbsDelta > 0)
            {
                foreach (var key in concentration.Keys.ToList())
                    concentration[key] /= totalAbsDelta;
            }

            return new RiskMetrics(
                snap.AggregateGreeks,
                snap.TotalUnrealizedPnl,
                snap.Positions.Count,
                concentration,
                snap.Timestamp);
        });
    }
}
