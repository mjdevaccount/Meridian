using System.Reactive.Linq;
using Meridian.Common.Models;

namespace Meridian.Risk.Aggregation;

public class PnlCalculator
{
    public IObservable<IReadOnlyList<PnlUpdate>> ComputePnl(IObservable<PortfolioSnapshot> portfolio)
    {
        return portfolio
            .Select(snap => snap.Positions.Values.Select(pos =>
                new PnlUpdate(
                    PositionId: pos.Position.PositionId,
                    MarkToMarket: pos.MarkToMarket,
                    UnrealizedPnl: pos.UnrealizedPnl,
                    RealizedPnl: 0m,
                    TotalPnl: pos.UnrealizedPnl,
                    Timestamp: snap.Timestamp))
                .ToList() as IReadOnlyList<PnlUpdate>);
    }

    public IObservable<decimal> ComputePortfolioPnl(IObservable<PortfolioSnapshot> portfolio)
    {
        return portfolio.Select(snap => snap.TotalUnrealizedPnl);
    }
}
