namespace Meridian.Common.Models;

public enum CommandType
{
    Add,
    Remove,
    Update
}

public record PositionCommand(
    CommandType Type,
    Position Position,
    DateTime Timestamp,
    string? CorrelationId = null);
