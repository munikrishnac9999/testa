namespace WpfCSharp.Models;
public class FyersSettings
{
    public string AppId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public bool UseLiveFyers { get; set; }
    public bool EnableLiveOrders { get; set; }
    public string ProductType { get; set; } = "INTRADAY";
    public string Validity { get; set; } = "DAY";
}
