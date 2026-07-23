using System.Net.Http;
using System.Text;
using System.Text.Json;
using WpfCSharp.Models;
namespace WpfCSharp.Services;
public class LiveFyersOrderService
{
    private readonly HttpClient _httpClient = new();
    private readonly FyersSettings _settings;
    public LiveFyersOrderService(FyersSettings settings) { _settings = settings; }
    public async Task<string> PlaceMarketOrderAsync(string fyersOptionSymbol, int quantity, int side, CancellationToken cancellationToken)
    {
        if (!_settings.EnableLiveOrders) return "Blocked: EnableLiveOrders is false. Keep paper mode until fully tested.";
        if (string.IsNullOrWhiteSpace(_settings.AppId) || string.IsNullOrWhiteSpace(_settings.AccessToken)) return "Blocked: FYERS AppId/access token missing.";
        var payload = new { symbol = fyersOptionSymbol, qty = quantity, type = 2, side, productType = _settings.ProductType, limitPrice = 0, stopPrice = 0, disclosedQty = 0, validity = _settings.Validity, offlineOrder = false, stopLoss = 0, takeProfit = 0, orderTag = "WpfCSharpBot", isSliceOrder = false };
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api-t1.fyers.in/api/v3/orders/sync");
        request.Headers.TryAddWithoutValidation("Authorization", $"{_settings.AppId}:{_settings.AccessToken}");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode ? body : $"HTTP {(int)response.StatusCode}: {body}";
    }
}
