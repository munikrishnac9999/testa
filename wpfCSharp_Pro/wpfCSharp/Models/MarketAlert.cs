namespace WpfCSharp.Models;
public enum AlertSeverity { Info, Bullish, Bearish, Warning }
public class MarketAlert
{
    public DateTime Time { get; set; } = DateTime.Now;
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Brush => Severity == AlertSeverity.Bullish ? "#22C55E" : Severity == AlertSeverity.Bearish ? "#EF4444" : Severity == AlertSeverity.Warning ? "#FBBF24" : "#AAB8D5";
    public override string ToString() => $"{Time:HH:mm:ss} | {Severity} | {Title} | {Message}";
}
