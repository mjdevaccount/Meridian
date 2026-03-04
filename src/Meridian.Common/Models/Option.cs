namespace Meridian.Common.Models;

public record Option(
    string Symbol,
    string Underlier,
    OptionType Type,
    decimal Strike,
    DateTime Expiry,
    ExerciseStyle Style);
