using WpfCSharp.Models;
namespace WpfCSharp.Services;
public class MockFyersMarketDataService : IFyersMarketDataService
{
    private readonly Random _random = new();
    private readonly OptionChainBuilder _builder = new();
    private readonly Dictionary<string, decimal> _spots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NIFTY"] = 25142m, ["BANKNIFTY"] = 57235m, ["SENSEX"] = 82567m, ["FINNIFTY"] = 24120m, ["MIDCPNIFTY"] = 12820m, ["BANKEX"] = 63500m
    };
    public Task<decimal> GetSpotPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        if (!_spots.ContainsKey(symbol)) _spots[symbol] = 25000m;
        var max = symbol.Equals("BANKNIFTY", StringComparison.OrdinalIgnoreCase) ? 24 : symbol.Equals("SENSEX", StringComparison.OrdinalIgnoreCase) ? 35 : 12;
        _spots[symbol] += RandomMove(max);
        return Task.FromResult(Math.Round(_spots[symbol], 2));
    }
    public async Task<IReadOnlyList<OptionChainRow>> GetOptionChainAsync(string symbol, DateTime expiry, int strikeRange, CancellationToken cancellationToken)
    {
        var spot = await GetSpotPriceAsync(symbol, cancellationToken);
        var atm = _builder.GetAtmStrike(symbol, spot);
        var rows = new List<OptionChainRow>();
        foreach (var strike in _builder.BuildStrikes(symbol, spot, strikeRange))
        {
            var distance = Math.Abs(strike - spot);
            var ceIntrinsic = Math.Max(0, spot - strike);
            var peIntrinsic = Math.Max(0, strike - spot);
            var timeValue = Math.Max(8, 180 - distance * 0.42m) + RandomMove(8);
            rows.Add(new OptionChainRow
            {
                Strike = strike,
                IsAtm = strike == atm,
                CE = CreateSide(Math.Max(1, ceIntrinsic + timeValue), true, strike, atm),
                PE = CreateSide(Math.Max(1, peIntrinsic + timeValue), false, strike, atm)
            });
        }
        return rows;
    }
    private OptionSideData CreateSide(decimal ltp, bool isCall, decimal strike, decimal atm)
    {
        var distanceFactor = Math.Abs(strike - atm) / 100m;
        var deltaAbs = Math.Clamp(0.52m - distanceFactor * 0.05m, 0.08m, 0.90m);
        var prevLtp = Math.Max(1, ltp + RandomMove(8));
        var oi = _random.Next(200000, 9000000);
        var prevOi = Math.Max(1, oi + _random.Next(-350000, 350000));
        var vol = _random.Next(10000, 750000);
        var prevVol = Math.Max(1, vol + _random.Next(-90000, 90000));
        return new OptionSideData
        {
            Ltp = Math.Round(ltp, 2), PreviousLtp = Math.Round(prevLtp, 2), OpenInterest = oi, PreviousOpenInterest = prevOi,
            OiChange = oi - prevOi, Volume = vol, PreviousVolume = prevVol,
            Iv = Math.Round(12m + (decimal)_random.NextDouble() * 8m, 2),
            Delta = Math.Round(isCall ? deltaAbs : -deltaAbs, 2), Gamma = Math.Round(0.01m + (decimal)_random.NextDouble() * 0.06m, 3),
            Theta = Math.Round(-4m - (decimal)_random.NextDouble() * 14m, 2), Vega = Math.Round(8m + (decimal)_random.NextDouble() * 22m, 2)
        };
    }
    private decimal RandomMove(int maxPoints) => _random.Next(-maxPoints, maxPoints + 1) + Math.Round((decimal)_random.NextDouble(), 2);
}
