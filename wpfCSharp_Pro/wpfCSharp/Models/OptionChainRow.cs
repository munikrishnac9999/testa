namespace WpfCSharp.Models;
public class OptionChainRow
{
    public decimal Strike { get; set; }
    public bool IsAtm { get; set; }
    public bool IsSupport { get; set; }
    public bool IsResistance { get; set; }
    public OptionSideData CE { get; set; } = new();
    public OptionSideData PE { get; set; } = new();
    public string RowBrush => IsAtm ? "#3A300A" : IsSupport ? "#0B3B25" : IsResistance ? "#3B1111" : "#0B1220";
    public string StrikeTag => IsAtm ? $"{Strike:N0} ATM" : IsSupport ? $"{Strike:N0} SUP" : IsResistance ? $"{Strike:N0} RES" : $"{Strike:N0}";
}
