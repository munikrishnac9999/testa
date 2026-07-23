# wpfCSharp Pro - FYERS Only WPF Trading Terminal

This is a new WPF project generated from the source files provided in chat.

## Included

- Module 1: graphical trading dashboard
- Module 2: FYERS-style option chain screen
- Module 3: separate alert center screen
- ATM +/- 10 strike option chain
- CE/PE side-by-side layout
- LTP change and percentage change
- OI change and percentage change
- Volume change percentage
- IV and Greeks columns
- CE Writing, CE Unwinding, PE Writing, PE Unwinding detection
- Support and Resistance from Max PE OI / Max CE OI
- VWAP, EMA20/50, RSI, ATR, ADX, CVD placeholders/calculations
- Mock mode and FYERS live mode
- Sound notification using built-in system sound

## Run

Open in Visual Studio 2022:

```text
wpfCSharp_Pro/wpfCSharp.sln
```

Start with mock mode. For FYERS live mode, enter AppId and Access Token, check Live FYERS, and click Apply.

## Safety

Paper mode is default. Do not enable live orders until all FYERS symbols, option-chain data and risk controls are verified.


## Latest Update

- Added white-background candlestick chart similar to the supplied example image.
- Added red/green candle colors and volume bars.
- Added 1m, 3m, 5m, 10m, 15m, 30m and higher timeframe tabs in the GUI.
- Fixed volume spike decimal/double compile issue in PriceActionAlertService.cs.
- Added option-chain row coloring for ATM/support/resistance.
- Added colored CE/PE analysis badges and colored LTP change/% change.
- Added colored alert severity in MainWindow and AlertCenterWindow.


## Dark Mode + MA/RSI/MACD Update

- Added **Dark Chart** option in the main dashboard.
- Added EMA20, EMA50 and VWAP line overlays on the candlestick chart.
- Added RSI panel below the volume panel.
- Added MACD and MACD signal overlay panel below RSI.
- Kept green/red candle colors similar to the uploaded example image.
- Timeframe selector still supports 1m, 3m, 5m, 10m, 15m, 30m, 60m, 120m, 180m and 240m.


## ATM +/- 10 CE/PE Option Chain Update

- Module 2 uses **ATM +/- 10 strikes** by default.
- The selected/current ATM strike appears in the center strike column.
- Ten strikes above and ten strikes below the ATM strike are shown.
- CE and PE are displayed side-by-side like a FYERS-style option chain.
- Columns include LTP, LTP change, LTP %, OI, OI change, OI %, volume, volume %, IV, Delta, Gamma, Theta, Vega and writing/unwinding analysis.
- ATM, support and resistance rows are highlighted.
