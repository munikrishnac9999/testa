using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using WpfCSharp.Models;
namespace WpfCSharp.Services;
public class LiveFyersMarketDataService : IFyersMarketDataService
{
    private readonly HttpClient _httpClient = new();
    private readonly OptionChainBuilder _builder = new();
    private readonly FyersSettings _settings;
    private readonly Dictionary<string, OptionChainRow> _previousRows = new();
    public LiveFyersMarketDataService(FyersSettings settings) { _settings = settings; }
    public async Task<decimal> GetSpotPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var fyersSymbol = GetSpotSymbol(symbol);
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api-t1.fyers.in/data/quotes?symbols={Uri.EscapeDataString(fyersSymbol)}");
        AddAuth(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(json);
        if (TryFindProperty(doc.RootElement, "lp", out var lp) && TryGetDecimal(lp, out var price)) return price;
        if (TryFindProperty(doc.RootElement, "ltp", out var ltp) && TryGetDecimal(ltp, out price)) return price;
        throw new InvalidOperationException("Unable to read LTP from FYERS quotes response. Please verify symbol and token.");
    }
    public async Task<IReadOnlyList<OptionChainRow>> GetOptionChainAsync(string symbol, DateTime expiry, int strikeRange, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var spot = await GetSpotPriceAsync(symbol, cancellationToken);
        var atm = _builder.GetAtmStrike(symbol, spot);
        var fyersSymbol = GetSpotSymbol(symbol);
        var requestUrl = $"https://api-t1.fyers.in/data/options-chain-v3?symbol={Uri.EscapeDataString(fyersSymbol)}&strikecount={strikeRange}&timestamp={ToUnixExpiry(expiry)}";
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        AddAuth(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var parsed = TryParseOptionChain(json, atm).OrderBy(x => x.Strike).ToList();
        ApplyPreviousValues(parsed);
        if (parsed.Count > 0) return parsed;
        throw new InvalidOperationException("FYERS option-chain response could not be parsed. Check API version/permissions.");
    }
    private void ApplyPreviousValues(List<OptionChainRow> rows)
    {
        foreach (var row in rows)
        {
            var key = row.Strike.ToString(CultureInfo.InvariantCulture);
            if (_previousRows.TryGetValue(key, out var prev))
            {
                row.CE.PreviousLtp = prev.CE.Ltp; row.PE.PreviousLtp = prev.PE.Ltp;
                row.CE.PreviousOpenInterest = prev.CE.OpenInterest; row.PE.PreviousOpenInterest = prev.PE.OpenInterest;
                row.CE.PreviousVolume = prev.CE.Volume; row.PE.PreviousVolume = prev.PE.Volume;
            }
            else
            {
                row.CE.PreviousLtp = row.CE.Ltp; row.PE.PreviousLtp = row.PE.Ltp;
                row.CE.PreviousOpenInterest = row.CE.OpenInterest; row.PE.PreviousOpenInterest = row.PE.OpenInterest;
                row.CE.PreviousVolume = row.CE.Volume; row.PE.PreviousVolume = row.PE.Volume;
            }
            row.CE.OiChange = row.CE.OpenInterest - row.CE.PreviousOpenInterest;
            row.PE.OiChange = row.PE.OpenInterest - row.PE.PreviousOpenInterest;
            _previousRows[key] = new OptionChainRow { Strike = row.Strike, CE = Clone(row.CE), PE = Clone(row.PE) };
        }
    }
    private static OptionSideData Clone(OptionSideData x) => new() { Ltp = x.Ltp, PreviousLtp = x.PreviousLtp, OpenInterest = x.OpenInterest, PreviousOpenInterest = x.PreviousOpenInterest, OiChange = x.OiChange, Volume = x.Volume, PreviousVolume = x.PreviousVolume, Iv = x.Iv, Delta = x.Delta, Gamma = x.Gamma, Theta = x.Theta, Vega = x.Vega };
    private List<OptionChainRow> TryParseOptionChain(string json, decimal atm)
    {
        using var doc = JsonDocument.Parse(json);
        var rows = new Dictionary<decimal, OptionChainRow>();
        Walk(doc.RootElement, element =>
        {
            if (element.ValueKind != JsonValueKind.Object) return;
            if (!TryFindProperty(element, "strike_price", out var strikeProp) && !TryFindProperty(element, "strike", out strikeProp)) return;
            if (!TryGetDecimal(strikeProp, out var strike)) return;
            var optionType = ReadString(element, "option_type") ?? ReadString(element, "optionType") ?? ReadString(element, "type") ?? string.Empty;
            var side = optionType.Contains("PE", StringComparison.OrdinalIgnoreCase) || optionType.Equals("P", StringComparison.OrdinalIgnoreCase) ? "PE" : "CE";
            if (!rows.TryGetValue(strike, out var row)) { row = new OptionChainRow { Strike = strike, IsAtm = strike == atm }; rows[strike] = row; }
            var data = side == "PE" ? row.PE : row.CE;
            data.Ltp = ReadDecimal(element, "ltp", "lp", "last_price");
            data.OpenInterest = (long)ReadDecimal(element, "oi", "open_interest");
            data.OiChange = (long)ReadDecimal(element, "oich", "oi_change", "change_in_oi");
            data.Volume = (long)ReadDecimal(element, "volume", "vol_traded_today");
            data.Iv = ReadDecimal(element, "iv", "implied_volatility");
            data.Delta = ReadDecimal(element, "delta"); data.Gamma = ReadDecimal(element, "gamma"); data.Theta = ReadDecimal(element, "theta"); data.Vega = ReadDecimal(element, "vega");
        });
        return rows.Values.Where(x => x.CE.Ltp > 0 || x.PE.Ltp > 0).ToList();
    }
    private static void Walk(JsonElement element, Action<JsonElement> visitor) { visitor(element); if (element.ValueKind == JsonValueKind.Object) foreach (var p in element.EnumerateObject()) Walk(p.Value, visitor); else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Walk(item, visitor); }
    private void AddAuth(HttpRequestMessage request) => request.Headers.TryAddWithoutValidation("Authorization", $"{_settings.AppId}:{_settings.AccessToken}");
    private void EnsureConfigured() { if (string.IsNullOrWhiteSpace(_settings.AppId) || string.IsNullOrWhiteSpace(_settings.AccessToken)) throw new InvalidOperationException("Please enter FYERS AppId and Access Token before switching to live mode."); }
    private static string GetSpotSymbol(string symbol) => symbol.ToUpperInvariant() switch { "BANKNIFTY" => "NSE:NIFTYBANK-INDEX", "FINNIFTY" => "NSE:FINNIFTY-INDEX", "MIDCPNIFTY" => "NSE:MIDCPNIFTY-INDEX", "SENSEX" => "BSE:SENSEX-INDEX", "BANKEX" => "BSE:BANKEX-INDEX", _ => "NSE:NIFTY50-INDEX" };
    private static long ToUnixExpiry(DateTime expiry) => new DateTimeOffset(expiry.Date.AddHours(15).AddMinutes(30), TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
    private static decimal ReadDecimal(JsonElement element, params string[] names) { foreach (var name in names) if (TryFindProperty(element, name, out var prop) && TryGetDecimal(prop, out var value)) return value; return 0m; }
    private static string? ReadString(JsonElement element, string name) => TryFindProperty(element, name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    private static bool TryFindProperty(JsonElement element, string name, out JsonElement value) { value = default; if (element.ValueKind != JsonValueKind.Object) return false; foreach (var prop in element.EnumerateObject()) if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = prop.Value; return true; } return false; }
    private static bool TryGetDecimal(JsonElement element, out decimal value) { value = 0m; return element.ValueKind switch { JsonValueKind.Number => element.TryGetDecimal(out value), JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value), _ => false }; }
}
