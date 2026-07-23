namespace WpfCSharp.Services;
public class OptionChainBuilder
{
    private readonly Dictionary<string, decimal> _strikeSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NIFTY"] = 50m, ["FINNIFTY"] = 50m, ["MIDCPNIFTY"] = 25m,
        ["BANKNIFTY"] = 100m, ["SENSEX"] = 100m, ["BANKEX"] = 100m
    };
    public decimal GetStrikeStep(string symbol) => _strikeSteps.TryGetValue(symbol, out var step) ? step : 50m;
    public decimal GetAtmStrike(string symbol, decimal spot)
    {
        var step = GetStrikeStep(symbol);
        return Math.Round(spot / step, MidpointRounding.AwayFromZero) * step;
    }
    public IReadOnlyList<decimal> BuildStrikes(string symbol, decimal spot, int range)
    {
        var step = GetStrikeStep(symbol);
        var atm = GetAtmStrike(symbol, spot);
        return Enumerable.Range(-range, (range * 2) + 1).Select(offset => atm + offset * step).ToList();
    }
}
