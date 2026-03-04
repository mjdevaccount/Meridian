namespace Meridian.Common.Models;

public record RiskAlert(
    AlertType Type,
    string Description,
    decimal Threshold,
    decimal CurrentValue,
    AlertSeverity Severity,
    DateTime Timestamp);
