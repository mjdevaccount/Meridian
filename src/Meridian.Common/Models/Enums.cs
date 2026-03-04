namespace Meridian.Common.Models;

public enum OptionType
{
    Call,
    Put
}

public enum ExerciseStyle
{
    European,
    American
}

public enum AlertType
{
    DeltaBreached,
    GammaBreached,
    VegaBreached,
    PnlDrawdown,
    ConcentrationBreached
}

public enum AlertSeverity
{
    Warning,
    Critical
}
