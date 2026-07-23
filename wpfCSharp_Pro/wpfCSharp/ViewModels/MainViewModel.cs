using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using WpfCSharp.Helpers;
using WpfCSharp.Models;
using WpfCSharp.Services;
using WpfCSharp.Views;

namespace WpfCSharp.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly FyersSettings _settings = new();
    private readonly IFyersMarketDataService _mock = new MockFyersMarketDataService();
    private IFyersMarketDataService _market;
    private readonly ExpiryService _expiry = new();
    private readonly OptionChainBuilder _builder = new();
    private readonly PriceActionAlertService _alerts = new();
    private readonly IndicatorService _ind = new();
    private readonly SoundService _sound = new();
    private readonly DispatcherTimer _timer;
    private readonly List<CandleSnapshot> _candles = new();
    private readonly List<long> _ceOiTrend = new();
    private readonly List<long> _peOiTrend = new();
    private readonly int[] _timeframes = { 1, 3, 5, 10, 15, 30, 60, 120, 180, 240 };

    private string _selectedSymbol = "NIFTY";
    private string _selectedTimeframe = "5m";
    private decimal _spotPrice;
    private decimal _atmStrike;
    private decimal _openingHigh;
    private decimal _openingLow;
    private decimal _supportStrike;
    private decimal _resistanceStrike;
    private decimal _pcr;
    private decimal _vwap;
    private decimal _ema20;
    private decimal _ema50;
    private decimal _rsi;
    private decimal _atr;
    private decimal _adx;
    private decimal _gapPoints;
    private long _totalCeVolume;
    private long _totalPeVolume;
    private int _bullishScore;
    private int _bearishScore;
    private string _status = "Mock mode";
    private bool _paperTradingMode = true;
    private bool _useLiveFyers;
    private bool _enableLiveOrders;
    private string _appId = string.Empty;
    private string _accessToken = string.Empty;
    private int _quantity = 50;
    private int _strikeRange = 10;
    private bool _isDarkChartMode;
    private string _chartBackgroundBrush = "White";
    private string _chartTextBrush = "#334155";
    private string _chartGridBrush = "#E2E8F0";
    private string _ema20Points = string.Empty;
    private string _ema50Points = string.Empty;
    private string _vwapPoints = string.Empty;
    private string _rsiPoints = string.Empty;
    private string _macdPoints = string.Empty;
    private string _macdSignalPoints = string.Empty;

    public MainViewModel()
    {
        _market = _mock;
        Symbols = new ObservableCollection<string> { "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "SENSEX", "BANKEX" };
        Timeframes = new ObservableCollection<string>(_timeframes.Select(x => $"{x}m"));
        OptionChain = new ObservableCollection<OptionChainRow>();
        Alerts = new ObservableCollection<MarketAlert>();
        ChartCandles = new ObservableCollection<CandleSnapshot>();
        TradeLog = new ObservableCollection<string>();
        SelectedExpiry = _expiry.GetNearestExpiry(SelectedSymbol, DateTime.Now);
        StartCommand = new RelayCommand(async _ => await StartAsync());
        StopCommand = new RelayCommand(_ => Stop());
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
        ApplyFyersCommand = new RelayCommand(_ => ApplyFyers());
        OpenAlertsCommand = new RelayCommand(_ => new AlertCenterWindow { DataContext = this }.Show());
        OpenOptionChainCommand = new RelayCommand(_ => new OptionChainWindow { DataContext = this }.Show());
        BuyCeCommand = new RelayCommand(_ => AddLog("BUY CE clicked. Paper mode recommended."));
        BuyPeCommand = new RelayCommand(_ => AddLog("BUY PE clicked. Paper mode recommended."));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _ = RefreshAsync();
    }

    public ObservableCollection<string> Symbols { get; }
    public ObservableCollection<string> Timeframes { get; }
    public ObservableCollection<OptionChainRow> OptionChain { get; }
    public ObservableCollection<MarketAlert> Alerts { get; }
    public ObservableCollection<CandleSnapshot> ChartCandles { get; }
    public ObservableCollection<string> TradeLog { get; }
    public DateTime SelectedExpiry { get; set; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ApplyFyersCommand { get; }
    public ICommand OpenAlertsCommand { get; }
    public ICommand OpenOptionChainCommand { get; }
    public ICommand BuyCeCommand { get; }
    public ICommand BuyPeCommand { get; }

    public string SelectedSymbol { get => _selectedSymbol; set { if (SetProperty(ref _selectedSymbol, value)) { SelectedExpiry = _expiry.GetNearestExpiry(value, DateTime.Now); ResetState(); _ = RefreshAsync(); } } }
    public string SelectedTimeframe { get => _selectedTimeframe; set => SetProperty(ref _selectedTimeframe, value); }
    public decimal SpotPrice { get => _spotPrice; set => SetProperty(ref _spotPrice, value); }
    public decimal AtmStrike { get => _atmStrike; set => SetProperty(ref _atmStrike, value); }
    public decimal OpeningHigh { get => _openingHigh; set => SetProperty(ref _openingHigh, value); }
    public decimal OpeningLow { get => _openingLow; set => SetProperty(ref _openingLow, value); }
    public decimal SupportStrike { get => _supportStrike; set => SetProperty(ref _supportStrike, value); }
    public decimal ResistanceStrike { get => _resistanceStrike; set => SetProperty(ref _resistanceStrike, value); }
    public decimal Pcr { get => _pcr; set => SetProperty(ref _pcr, value); }
    public decimal Vwap { get => _vwap; set => SetProperty(ref _vwap, value); }
    public decimal Ema20 { get => _ema20; set => SetProperty(ref _ema20, value); }
    public decimal Ema50 { get => _ema50; set => SetProperty(ref _ema50, value); }
    public decimal Rsi { get => _rsi; set => SetProperty(ref _rsi, value); }
    public decimal Atr { get => _atr; set => SetProperty(ref _atr, value); }
    public decimal Adx { get => _adx; set => SetProperty(ref _adx, value); }
    public decimal GapPoints { get => _gapPoints; set => SetProperty(ref _gapPoints, value); }
    public long TotalCeVolume { get => _totalCeVolume; set => SetProperty(ref _totalCeVolume, value); }
    public long TotalPeVolume { get => _totalPeVolume; set => SetProperty(ref _totalPeVolume, value); }
    public int BullishScore { get => _bullishScore; set => SetProperty(ref _bullishScore, value); }
    public int BearishScore { get => _bearishScore; set => SetProperty(ref _bearishScore, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool PaperTradingMode { get => _paperTradingMode; set => SetProperty(ref _paperTradingMode, value); }
    public bool UseLiveFyers { get => _useLiveFyers; set { if (SetProperty(ref _useLiveFyers, value)) ApplyFyers(); } }
    public bool EnableLiveOrders { get => _enableLiveOrders; set { if (SetProperty(ref _enableLiveOrders, value)) _settings.EnableLiveOrders = value; } }
    public string AppId { get => _appId; set => SetProperty(ref _appId, value); }
    public string AccessToken { get => _accessToken; set => SetProperty(ref _accessToken, value); }
    public int Quantity { get => _quantity; set => SetProperty(ref _quantity, value); }
    public int StrikeRange { get => _strikeRange; set => SetProperty(ref _strikeRange, value); }
    public bool IsDarkChartMode
    {
        get => _isDarkChartMode;
        set
        {
            if (SetProperty(ref _isDarkChartMode, value))
            {
                ChartBackgroundBrush = value ? "#0B1220" : "White";
                ChartTextBrush = value ? "#E5E7EB" : "#334155";
                ChartGridBrush = value ? "#1F2937" : "#E2E8F0";
                ApplyChartLayout();
                ChartCandles.Clear(); foreach (var c in _candles.TakeLast(80)) ChartCandles.Add(c);
            }
        }
    }
    public string ChartBackgroundBrush { get => _chartBackgroundBrush; set => SetProperty(ref _chartBackgroundBrush, value); }
    public string ChartTextBrush { get => _chartTextBrush; set => SetProperty(ref _chartTextBrush, value); }
    public string ChartGridBrush { get => _chartGridBrush; set => SetProperty(ref _chartGridBrush, value); }
    public string Ema20Points { get => _ema20Points; set => SetProperty(ref _ema20Points, value); }
    public string Ema50Points { get => _ema50Points; set => SetProperty(ref _ema50Points, value); }
    public string VwapPoints { get => _vwapPoints; set => SetProperty(ref _vwapPoints, value); }
    public string RsiPoints { get => _rsiPoints; set => SetProperty(ref _rsiPoints, value); }
    public string MacdPoints { get => _macdPoints; set => SetProperty(ref _macdPoints, value); }
    public string MacdSignalPoints { get => _macdSignalPoints; set => SetProperty(ref _macdSignalPoints, value); }

    private async Task StartAsync() { _timer.Start(); Status = UseLiveFyers ? "Running - FYERS live" : "Running - mock"; await RefreshAsync(); }
    private void Stop() { _timer.Stop(); Status = "Stopped"; }
    private void ApplyFyers()
    {
        _settings.AppId = AppId.Trim(); _settings.AccessToken = AccessToken.Trim(); _settings.UseLiveFyers = UseLiveFyers; _settings.EnableLiveOrders = EnableLiveOrders;
        if (UseLiveFyers && (string.IsNullOrWhiteSpace(_settings.AppId) || string.IsNullOrWhiteSpace(_settings.AccessToken))) { _market = _mock; Status = "Enter FYERS AppId and token"; return; }
        _market = UseLiveFyers ? new LiveFyersMarketDataService(_settings) : _mock;
        Status = UseLiveFyers ? "FYERS live data mode" : "Mock data mode";
    }

    private async Task RefreshAsync()
    {
        try
        {
            var token = CancellationToken.None;
            SpotPrice = await _market.GetSpotPriceAsync(SelectedSymbol, token);
            AtmStrike = _builder.GetAtmStrike(SelectedSymbol, SpotPrice);
            var rows = (await _market.GetOptionChainAsync(SelectedSymbol, SelectedExpiry, StrikeRange, token)).OrderBy(x => x.Strike).ToList();
            AnalyzeOptionChain(rows);
            OptionChain.Clear(); foreach (var r in rows) OptionChain.Add(r);
            UpdateCandles(rows);
            UpdateAlerts(rows.FirstOrDefault(x => x.IsAtm));
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; AddLog(Status); }
    }

    private void AnalyzeOptionChain(List<OptionChainRow> rows)
    {
        if (rows.Count == 0) return;
        SupportStrike = rows.OrderByDescending(x => x.PE.OpenInterest).First().Strike;
        ResistanceStrike = rows.OrderByDescending(x => x.CE.OpenInterest).First().Strike;
        TotalCeVolume = rows.Sum(x => x.CE.Volume); TotalPeVolume = rows.Sum(x => x.PE.Volume);
        Pcr = rows.Sum(x => Math.Max(1, x.PE.OpenInterest)) / Math.Max(1m, rows.Sum(x => Math.Max(1, x.CE.OpenInterest)));
        foreach (var r in rows)
        {
            r.IsSupport = r.Strike == SupportStrike; r.IsResistance = r.Strike == ResistanceStrike;
            AnalyzeSide(r.CE, true); AnalyzeSide(r.PE, false);
        }
        var oldSupport = SupportStrike; var oldResistance = ResistanceStrike;
    }
    private static void AnalyzeSide(OptionSideData side, bool isCe)
    {
        var priceUp = side.LtpChange > 0; var priceDown = side.LtpChange < 0; var oiUp = side.OiChange > 0; var oiDown = side.OiChange < 0;
        if (isCe && priceUp && oiDown) { side.Analysis = "CE Unwinding"; side.AnalysisBrush = "#22C55E"; }
        else if (isCe && priceDown && oiUp) { side.Analysis = "CE Writing"; side.AnalysisBrush = "#EF4444"; }
        else if (!isCe && priceUp && oiUp) { side.Analysis = "PE Writing"; side.AnalysisBrush = "#22C55E"; }
        else if (!isCe && priceDown && oiDown) { side.Analysis = "PE Unwinding"; side.AnalysisBrush = "#EF4444"; }
        else if (priceUp && oiUp) { side.Analysis = "Long Build"; side.AnalysisBrush = "#22C55E"; }
        else if (priceDown && oiDown) { side.Analysis = "Long Unwind"; side.AnalysisBrush = "#F97316"; }
        else side.Analysis = "Neutral";
    }
    private void UpdateCandles(List<OptionChainRow> rows)
    {
        var previous = _candles.LastOrDefault();
        var open = previous?.Close ?? SpotPrice;
        var high = Math.Max(open, SpotPrice); var low = Math.Min(open, SpotPrice);
        var volume = rows.Sum(x => x.CE.Volume + x.PE.Volume);
        var typical = SpotPrice;
        var vwap = _candles.Count == 0 ? SpotPrice : Math.Round((_candles.Sum(x => x.Close * Math.Max(1, x.Volume)) + typical * Math.Max(1, volume)) / (_candles.Sum(x => Math.Max(1, x.Volume)) + Math.Max(1, volume)), 2);
        var candle = new CandleSnapshot { Time = DateTime.Now, Open = open, High = high, Low = low, Close = SpotPrice, Volume = volume, Vwap = vwap };
        candle.Ema20 = _ind.Ema(previous?.Ema20 ?? 0, SpotPrice, 20); candle.Ema50 = _ind.Ema(previous?.Ema50 ?? 0, SpotPrice, 50);
        candle.Ema12 = _ind.Ema(previous?.Ema12 ?? 0, SpotPrice, 12);
        candle.Ema26 = _ind.Ema(previous?.Ema26 ?? 0, SpotPrice, 26);
        candle.Macd = Math.Round(candle.Ema12 - candle.Ema26, 2);
        candle.MacdSignal = _ind.Ema(previous?.MacdSignal ?? 0, candle.Macd, 9);
        candle.MacdHistogram = Math.Round(candle.Macd - candle.MacdSignal, 2);
        candle.Rsi = _ind.Rsi(_candles.Append(candle).ToList()); candle.Atr = _ind.Atr(_candles.Append(candle).ToList()); candle.Adx = _ind.Adx(_candles.Append(candle).ToList());
        candle.Cvd = (previous?.Cvd ?? 0) + (SpotPrice >= open ? volume : -volume);
        _candles.Add(candle); while (_candles.Count > 240) _candles.RemoveAt(0);
        ApplyChartLayout();
        ChartCandles.Clear(); foreach (var c in _candles.TakeLast(80)) ChartCandles.Add(c);
        if (_candles.Count <= 15) { OpeningHigh = _candles.Max(x => x.High); OpeningLow = _candles.Min(x => x.Low); GapPoints = _candles.First().Open - (_candles.Count > 1 ? _candles.First().Close : _candles.First().Open); }
        Vwap = candle.Vwap; Ema20 = candle.Ema20; Ema50 = candle.Ema50; Rsi = candle.Rsi; Atr = candle.Atr; Adx = candle.Adx;
    }

    private void ApplyChartLayout()
    {
        var visible = _candles.TakeLast(80).ToList();
        if (visible.Count == 0)
        {
            Ema20Points = Ema50Points = VwapPoints = RsiPoints = MacdPoints = MacdSignalPoints = string.Empty;
            return;
        }

        const double chartHeight = 330;
        const double volumeHeight = 80;
        const double candleStep = 13;
        var maxPrice = visible.Max(x => x.High);
        var minPrice = visible.Min(x => x.Low);
        var range = Math.Max(1m, maxPrice - minPrice);
        var maxVolume = Math.Max(1L, visible.Max(x => x.Volume));

        for (int i = 0; i < visible.Count; i++)
        {
            var c = visible[i];
            c.X = i * candleStep;
            c.WickTop = (double)((maxPrice - c.High) / range) * chartHeight;
            var wickBottom = (double)((maxPrice - c.Low) / range) * chartHeight;
            c.WickHeight = Math.Max(2, wickBottom - c.WickTop);

            var bodyTopPrice = Math.Max(c.Open, c.Close);
            var bodyBottomPrice = Math.Min(c.Open, c.Close);
            c.BodyTop = (double)((maxPrice - bodyTopPrice) / range) * chartHeight;
            var bodyBottom = (double)((maxPrice - bodyBottomPrice) / range) * chartHeight;
            c.BodyHeight = Math.Max(4, bodyBottom - c.BodyTop);
            c.CandleBrush = c.Close >= c.Open ? "#10BFA3" : "#F04B2F";

            c.VolumeHeight = Math.Max(3, (double)c.Volume / maxVolume * volumeHeight);
            c.VolumeTop = 74 - c.VolumeHeight;
        }

        Ema20Points = BuildPriceLine(visible, x => x.Ema20, maxPrice, range, candleStep, chartHeight);
        Ema50Points = BuildPriceLine(visible, x => x.Ema50, maxPrice, range, candleStep, chartHeight);
        VwapPoints = BuildPriceLine(visible, x => x.Vwap, maxPrice, range, candleStep, chartHeight);
        RsiPoints = BuildOscillatorLine(visible, x => x.Rsi, 0m, 100m, candleStep, 65);
        var macdMin = visible.Min(x => Math.Min(x.Macd, x.MacdSignal));
        var macdMax = visible.Max(x => Math.Max(x.Macd, x.MacdSignal));
        if (macdMax == macdMin) { macdMax += 1; macdMin -= 1; }
        MacdPoints = BuildOscillatorLine(visible, x => x.Macd, macdMin, macdMax, candleStep, 65);
        MacdSignalPoints = BuildOscillatorLine(visible, x => x.MacdSignal, macdMin, macdMax, candleStep, 65);
    }

    private static string BuildPriceLine(IReadOnlyList<CandleSnapshot> values, Func<CandleSnapshot, decimal> selector, decimal maxPrice, decimal range, double candleStep, double chartHeight)
    {
        return string.Join(" ", values.Select((c, i) =>
        {
            var y = (double)((maxPrice - selector(c)) / range) * chartHeight;
            var x = i * candleStep + 6;
            return $"{x:0.##},{y:0.##}";
        }));
    }

    private static string BuildOscillatorLine(IReadOnlyList<CandleSnapshot> values, Func<CandleSnapshot, decimal> selector, decimal minValue, decimal maxValue, double candleStep, double height)
    {
        var range = Math.Max(0.0001m, maxValue - minValue);
        return string.Join(" ", values.Select((c, i) =>
        {
            var y = (double)((maxValue - selector(c)) / range) * height;
            var x = i * candleStep + 6;
            return $"{x:0.##},{y:0.##}";
        }));
    }

    private void UpdateAlerts(OptionChainRow? atmRow)
    {
        if (atmRow == null) return;
        _ceOiTrend.Add(atmRow.CE.OpenInterest); _peOiTrend.Add(atmRow.PE.OpenInterest);
        while (_ceOiTrend.Count > 10) _ceOiTrend.RemoveAt(0); while (_peOiTrend.Count > 10) _peOiTrend.RemoveAt(0);
        var newAlerts = _alerts.Evaluate(_candles, OpeningHigh, OpeningLow, atmRow, _ceOiTrend, _peOiTrend);
        BullishScore = _alerts.GetBullishScore(newAlerts); BearishScore = _alerts.GetBearishScore(newAlerts);
        foreach (var alert in newAlerts)
        {
            if (!Alerts.Take(8).Any(x => x.Title == alert.Title && x.Message == alert.Message)) { Alerts.Insert(0, alert); AddLog(alert.ToString()); if (alert.Severity != AlertSeverity.Info) _sound.PlayAlert(); }
        }
        while (Alerts.Count > 200) Alerts.RemoveAt(Alerts.Count - 1);
    }
    private void ResetState() { _candles.Clear(); _ceOiTrend.Clear(); _peOiTrend.Clear(); Alerts.Clear(); ChartCandles.Clear(); Ema20Points = Ema50Points = VwapPoints = RsiPoints = MacdPoints = MacdSignalPoints = string.Empty; }
    private void AddLog(string message) { TradeLog.Insert(0, $"{DateTime.Now:HH:mm:ss} | {message}"); while (TradeLog.Count > 200) TradeLog.RemoveAt(TradeLog.Count - 1); }
}
