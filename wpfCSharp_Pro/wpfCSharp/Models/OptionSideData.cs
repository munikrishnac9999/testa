namespace WpfCSharp.Models;
public class OptionSideData
{
    public decimal Ltp { get; set; }
    public decimal PreviousLtp { get; set; }
    public decimal LtpChange => Ltp - PreviousLtp;
    public decimal LtpChangePercent => PreviousLtp == 0 ? 0 : Math.Round((Ltp - PreviousLtp) / PreviousLtp * 100m, 2);
    public string LtpChangeText => $"{LtpChange:+0.00;-0.00;0.00}";
    public string LtpChangePercentText => $"{LtpChangePercent:+0.00;-0.00;0.00}%";
    public string LtpBrush => LtpChange > 0 ? "#22C55E" : LtpChange < 0 ? "#EF4444" : "#AAB8D5";
    public long OpenInterest { get; set; }
    public long PreviousOpenInterest { get; set; }
    public long OiChange { get; set; }
    public decimal OiChangePercent => PreviousOpenInterest == 0 ? 0 : Math.Round((OpenInterest - PreviousOpenInterest) * 100m / PreviousOpenInterest, 2);
    public string OiChangePercentText => $"{OiChangePercent:+0.00;-0.00;0.00}%";
    public long Volume { get; set; }
    public long PreviousVolume { get; set; }
    public long VolumeChange => Volume - PreviousVolume;
    public decimal VolumeChangePercent => PreviousVolume == 0 ? 0 : Math.Round((Volume - PreviousVolume) * 100m / PreviousVolume, 2);
    public string VolumeChangePercentText => $"{VolumeChangePercent:+0.00;-0.00;0.00}%";
    public decimal Iv { get; set; }
    public decimal Delta { get; set; }
    public decimal Gamma { get; set; }
    public decimal Theta { get; set; }
    public decimal Vega { get; set; }
    public string Analysis { get; set; } = "Neutral";
    public string AnalysisBrush { get; set; } = "#64748B";
}
