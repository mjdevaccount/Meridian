using System.Reactive.Linq;
using Meridian.Common.Models;

namespace Meridian.Pricing.Streams;

/// <summary>
/// Aggregates portfolio-level Greeks from the PortfolioSnapshot stream.
/// Since PortfolioSnapshot already computes AggregateGreeks internally,
/// this class provides a convenient projection to GreeksSnapshot with
/// optional throttling for rate-limited output to dashboards or displays.
/// </summary>
public class GreeksAggregator
{
    private readonly TimeSpan _throttleInterval;

    /// <summary>
    /// Creates a GreeksAggregator with an optional throttle interval.
    /// </summary>
    /// <param name="throttleInterval">
    /// Minimum interval between emitted snapshots.
    /// Defaults to 100ms to avoid overwhelming downstream consumers.
    /// </param>
    public GreeksAggregator(TimeSpan? throttleInterval = null)
    {
        _throttleInterval = throttleInterval ?? TimeSpan.FromMilliseconds(100);
    }

    /// <summary>
    /// Projects a stream of PortfolioSnapshots into a stream of aggregate GreeksSnapshots.
    /// Applies Throttle to rate-limit output for dashboard/display consumers.
    /// </summary>
    /// <param name="portfolio">The upstream portfolio snapshot stream.</param>
    /// <returns>A throttled stream of portfolio-level Greeks snapshots.</returns>
    public IObservable<GreeksSnapshot> AggregateGreeks(IObservable<PortfolioSnapshot> portfolio)
    {
        return portfolio
            .Throttle(_throttleInterval)
            .Select(snapshot => new GreeksSnapshot(
                PositionId: "PORTFOLIO",
                Delta: snapshot.AggregateGreeks.Delta,
                Gamma: snapshot.AggregateGreeks.Gamma,
                Vega: snapshot.AggregateGreeks.Vega,
                Theta: snapshot.AggregateGreeks.Theta,
                Rho: snapshot.AggregateGreeks.Rho,
                Timestamp: snapshot.Timestamp))
            .DistinctUntilChanged(gs => (gs.Delta, gs.Gamma, gs.Vega, gs.Theta, gs.Rho));
    }

    /// <summary>
    /// Projects per-position Greeks from the portfolio stream.
    /// Useful for position-level risk monitoring.
    /// </summary>
    /// <param name="portfolio">The upstream portfolio snapshot stream.</param>
    /// <returns>A stream of per-position Greeks snapshots, one per position per update.</returns>
    public IObservable<GreeksSnapshot> PerPositionGreeks(IObservable<PortfolioSnapshot> portfolio)
    {
        return portfolio
            .Throttle(_throttleInterval)
            .SelectMany(snapshot =>
                snapshot.Positions.Values.Select(ps => new GreeksSnapshot(
                    PositionId: ps.Position.PositionId,
                    Delta: ps.Greeks.Delta * ps.Position.Quantity,
                    Gamma: ps.Greeks.Gamma * ps.Position.Quantity,
                    Vega: ps.Greeks.Vega * ps.Position.Quantity,
                    Theta: ps.Greeks.Theta * ps.Position.Quantity,
                    Rho: ps.Greeks.Rho * ps.Position.Quantity,
                    Timestamp: ps.LastUpdated)));
    }
}
