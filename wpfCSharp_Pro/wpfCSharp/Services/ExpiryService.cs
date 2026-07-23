namespace WpfCSharp.Services;
public class ExpiryService
{
    private readonly Dictionary<string, DayOfWeek> _weeklyExpiryDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NIFTY"] = DayOfWeek.Thursday,
        ["BANKNIFTY"] = DayOfWeek.Tuesday,
        ["FINNIFTY"] = DayOfWeek.Tuesday,
        ["MIDCPNIFTY"] = DayOfWeek.Wednesday,
        ["SENSEX"] = DayOfWeek.Thursday,
        ["BANKEX"] = DayOfWeek.Thursday
    };
    public DateTime GetNearestExpiry(string symbol, DateTime now)
    {
        var targetDay = _weeklyExpiryDays.TryGetValue(symbol, out var day) ? day : DayOfWeek.Thursday;
        var date = now.Date;
        for (int i = 0; i < 10; i++) { if (date.DayOfWeek == targetDay) return date; date = date.AddDays(1); }
        return now.Date;
    }
}
