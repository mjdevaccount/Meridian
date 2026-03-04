namespace Meridian.Dashboard.Models;

public class CreatePositionRequest
{
    public string Underlier { get; set; } = "";
    public string OptionType { get; set; } = "Call";
    public decimal Strike { get; set; }
    public DateTime Expiry { get; set; } = DateTime.UtcNow.AddMonths(1);
    public string ExerciseStyle { get; set; } = "European";
    public int Quantity { get; set; } = 1;
    public decimal EntryPrice { get; set; }
}
