namespace WpfCSharp.Models;
public class CandleSnapshot
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public decimal Vwap { get; set; }
    public decimal Ema20 { get; set; }
    public decimal Ema50 { get; set; }
    public decimal Rsi { get; set; }
    public decimal Atr { get; set; }
    public decimal Adx { get; set; }
    public decimal Cvd { get; set; }
    public decimal Ema12 { get; set; }
    public decimal Ema26 { get; set; }
    public decimal Macd { get; set; }
    public decimal MacdSignal { get; set; }
    public decimal MacdHistogram { get; set; }

    // UI-only normalized properties for the WPF candlestick chart.
    public double X { get; set; }
    public double WickTop { get; set; }
    public double WickHeight { get; set; }
    public double BodyTop { get; set; }
    public double BodyHeight { get; set; }
    public double VolumeTop { get; set; }
    public double VolumeHeight { get; set; }
    public string CandleBrush { get; set; } = "#22C55E";
}
