namespace WpfCSharp.Models;
public class BotPreferences
{
    public string IndexSymbol { get; set; } = "NIFTY";
    public int StrikeRange { get; set; } = 10;
    public int RefreshSeconds { get; set; } = 1;
    public bool PaperTradingMode { get; set; } = true;
    public decimal Capital { get; set; } = 100000m;
    public decimal MaxRiskPercentPerTrade { get; set; } = 2m;
}
