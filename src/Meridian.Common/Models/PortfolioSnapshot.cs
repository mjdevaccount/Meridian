using System.Collections.Immutable;

namespace Meridian.Common.Models;

public class PortfolioSnapshot
{
    public ImmutableDictionary<string, PositionState> Positions { get; init; }
        = ImmutableDictionary<string, PositionState>.Empty;
    public GreeksResult AggregateGreeks { get; init; } = GreeksResult.Zero;
    public decimal TotalUnrealizedPnl { get; init; }
    public DateTime Timestamp { get; init; }

    public static PortfolioSnapshot Empty => new();

    public PortfolioSnapshot ApplyUpdates(IList<PositionUpdate> updates)
    {
        var newPositions = Positions;
        foreach (var update in updates)
        {
            var mtm = (update.Pricing.TheoreticalPrice - update.Position.EntryPrice) * update.Position.Quantity;
            var state = new PositionState(
                update.Position,
                update.Pricing.TheoreticalPrice,
                update.Greeks,
                mtm,
                mtm, // unrealized = mtm for open positions
                update.Timestamp);
            newPositions = newPositions.SetItem(update.Position.PositionId, state);
        }

        var aggGreeks = GreeksResult.Zero;
        decimal totalPnl = 0;
        foreach (var kvp in newPositions)
        {
            aggGreeks = aggGreeks + kvp.Value.Greeks.Scale(kvp.Value.Position.Quantity);
            totalPnl += kvp.Value.UnrealizedPnl;
        }

        return new PortfolioSnapshot
        {
            Positions = newPositions,
            AggregateGreeks = aggGreeks,
            TotalUnrealizedPnl = totalPnl,
            Timestamp = updates.Count > 0 ? updates[^1].Timestamp : Timestamp
        };
    }
}

public record PositionState(
    Position Position,
    decimal CurrentPrice,
    GreeksResult Greeks,
    decimal MarkToMarket,
    decimal UnrealizedPnl,
    DateTime LastUpdated);
