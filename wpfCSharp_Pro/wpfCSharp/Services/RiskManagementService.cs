namespace WpfCSharp.Services;
public class RiskManagementService
{
    public bool CanTakeTrade(decimal capital, decimal entryPrice, int quantity, decimal maxRiskPercent)
    {
        var tradeValue = entryPrice * quantity;
        var maxAllowed = capital * maxRiskPercent / 100m;
        return tradeValue <= maxAllowed;
    }
}
