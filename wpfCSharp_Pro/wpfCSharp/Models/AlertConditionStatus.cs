namespace WpfCSharp.Models;
public class AlertConditionStatus
{
    public string Timeframe { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Status { get; set; } = "NO";
    public string Direction { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string StatusBrush { get; set; } = "#64748B";
    public string DirectionBrush { get; set; } = "#AAB8D5";
}
