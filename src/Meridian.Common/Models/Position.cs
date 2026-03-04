namespace Meridian.Common.Models;

public record Position(
    string PositionId,
    Option Instrument,
    int Quantity,
    decimal EntryPrice,
    DateTime EntryTime);
