using WpfCSharp.Models;
namespace WpfCSharp.Services;
public class PriceActionAlertService
{
    public List<MarketAlert> Evaluate(IReadOnlyList<CandleSnapshot> candles, decimal openingRangeHigh, decimal openingRangeLow, OptionChainRow? atmRow, IReadOnlyList<long> ceOiTrend, IReadOnlyList<long> peOiTrend)
    {
        var alerts = new List<MarketAlert>();
        if (candles.Count == 0 || atmRow == null) return alerts;
        var latest = candles[^1];
        if (openingRangeHigh > 0 && latest.Close > openingRangeHigh) alerts.Add(Alert(AlertSeverity.Bullish, "Opening High Break", $"Close {latest.Close:N2} is above opening high {openingRangeHigh:N2}"));
        if (openingRangeLow > 0 && latest.Close < openingRangeLow) alerts.Add(Alert(AlertSeverity.Bearish, "Opening Low Breakdown", $"Close {latest.Close:N2} is below opening low {openingRangeLow:N2}"));
        if (candles.Count >= 3 && candles.TakeLast(3).All(x => x.Close > x.Vwap)) alerts.Add(Alert(AlertSeverity.Bullish, "VWAP Hold", "Last 3 candles closed above VWAP"));
        if (candles.Count >= 3 && candles.TakeLast(3).All(x => x.Close < x.Vwap)) alerts.Add(Alert(AlertSeverity.Bearish, "VWAP Rejection", "Last 3 candles closed below VWAP"));
        var avgVolume = candles.TakeLast(Math.Min(20, candles.Count)).Average(x => (decimal)x.Volume);
        if ((decimal)latest.Volume > avgVolume * 1.5m)
            alerts.Add(Alert(AlertSeverity.Bullish, "Volume Spike", $"Current volume {latest.Volume:N0} is {((decimal)latest.Volume / Math.Max(1m, avgVolume)):N2}x average"));
        if (IsContinuouslyFalling(ceOiTrend)) alerts.Add(Alert(AlertSeverity.Bullish, "CE OI Unwinding", $"ATM CE OI falling. Latest: {atmRow.CE.OpenInterest:N0}"));
        if (IsContinuouslyRising(ceOiTrend)) alerts.Add(Alert(AlertSeverity.Bearish, "Fresh CE Writing", $"ATM CE OI rising. Latest: {atmRow.CE.OpenInterest:N0}"));
        if (IsContinuouslyFalling(peOiTrend)) alerts.Add(Alert(AlertSeverity.Bearish, "PE OI Unwinding", $"ATM PE OI falling. Latest: {atmRow.PE.OpenInterest:N0}"));
        if (IsContinuouslyRising(peOiTrend)) alerts.Add(Alert(AlertSeverity.Bullish, "Fresh PE Writing", $"ATM PE OI rising. Latest: {atmRow.PE.OpenInterest:N0}"));
        return alerts;
    }
    public int GetBullishScore(IReadOnlyList<MarketAlert> alerts)
    {
        var score = 0;
        foreach (var title in new[] { "Opening High Break", "VWAP Hold", "Volume Spike", "CE OI Unwinding", "Fresh PE Writing" }) if (alerts.Any(x => x.Title == title)) score += 20;
        if (alerts.Any(x => x.Title == "Fresh CE Writing")) score -= 20;
        return Math.Clamp(score, 0, 100);
    }
    public int GetBearishScore(IReadOnlyList<MarketAlert> alerts)
    {
        var score = 0;
        foreach (var title in new[] { "Opening Low Breakdown", "VWAP Rejection", "Volume Spike", "PE OI Unwinding", "Fresh CE Writing" }) if (alerts.Any(x => x.Title == title)) score += 20;
        if (alerts.Any(x => x.Title == "Fresh PE Writing")) score -= 20;
        return Math.Clamp(score, 0, 100);
    }
    private static MarketAlert Alert(AlertSeverity s, string t, string m) => new() { Severity = s, Title = t, Message = m };
    private static bool IsContinuouslyRising(IReadOnlyList<long> values) { if (values.Count < 3) return false; var last = values.TakeLast(3).ToList(); return last[2] > last[1] && last[1] > last[0]; }
    private static bool IsContinuouslyFalling(IReadOnlyList<long> values) { if (values.Count < 3) return false; var last = values.TakeLast(3).ToList(); return last[2] < last[1] && last[1] < last[0]; }
}
