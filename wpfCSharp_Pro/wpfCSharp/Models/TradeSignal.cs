namespace WpfCSharp.Models;
public enum SignalAction { Hold, BuyCE, BuyPE, Exit }
public class TradeSignal
{
    public SignalAction Action { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal Strike { get; set; }
    public decimal Entry { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
}
