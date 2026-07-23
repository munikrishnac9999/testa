using WpfCSharp.Models;
namespace WpfCSharp.Services;
public class IndicatorService
{
    public decimal Ema(decimal previous, decimal price, int period) => previous == 0 ? price : Math.Round(price * (2m / (period + 1)) + previous * (1 - 2m / (period + 1)), 2);
    public decimal Rsi(IReadOnlyList<CandleSnapshot> candles)
    {
        if (candles.Count < 15) return 50;
        var last = candles.TakeLast(14).ToList();
        decimal gain = 0, loss = 0;
        for (int i = 1; i < last.Count; i++) { var d = last[i].Close - last[i-1].Close; if (d >= 0) gain += d; else loss += Math.Abs(d); }
        if (loss == 0) return 100;
        var rs = gain / loss;
        return Math.Round(100 - (100 / (1 + rs)), 2);
    }
    public decimal Atr(IReadOnlyList<CandleSnapshot> candles)
    {
        if (candles.Count < 2) return 0;
        var last = candles.TakeLast(14).ToList();
        return Math.Round(last.Average(x => x.High - x.Low), 2);
    }
    public decimal Adx(IReadOnlyList<CandleSnapshot> candles)
    {
        if (candles.Count < 4) return 10;
        var range = candles.TakeLast(14).Average(x => x.High - x.Low);
        var move = Math.Abs(candles[^1].Close - candles[Math.Max(0, candles.Count - Math.Min(14, candles.Count))].Close);
        return Math.Round(Math.Clamp(range == 0 ? 10 : (move / range) * 10m, 5m, 55m), 2);
    }
}
