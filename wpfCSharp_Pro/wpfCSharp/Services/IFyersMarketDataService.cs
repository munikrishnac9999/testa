using WpfCSharp.Models;
namespace WpfCSharp.Services;
public interface IFyersMarketDataService
{
    Task<decimal> GetSpotPriceAsync(string symbol, CancellationToken cancellationToken);
    Task<IReadOnlyList<OptionChainRow>> GetOptionChainAsync(string symbol, DateTime expiry, int strikeRange, CancellationToken cancellationToken);
}
