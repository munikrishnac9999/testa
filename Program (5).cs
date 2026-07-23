using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FyersOptionChain
{
    internal class Program
    {
        private const string APPID = "ED88TMB033-100";
        private const string DEFAULT_SYMBOL = "NSE:NIFTY50-INDEX";
        private const int DEFAULT_PLUS_MINUS_STRIKES = 10;
        private const int REFRESH_SECONDS = 5;

        private static readonly Dictionary<string, OptionLeg> PreviousLegs = new Dictionary<string, OptionLeg>();

        private static string Symbol = DEFAULT_SYMBOL;
        private static int StrikeStep = 50;
        private static int PlusMinusStrikes = DEFAULT_PLUS_MINUS_STRIKES;
        private static string CurrentExpiryDisplay = "Nearest / Current";
        private static string LastChainStatus = "-";
        private static string LastQuoteStatus = "-";

        static async Task Main(string[] args)
        {
            Console.Title = "FYERS Option Chain Two API Console";

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("FYERS OPTION CHAIN TWO API CONSOLE");
            Console.WriteLine("-----------------------------------");
            Console.WriteLine();

            Console.WriteLine("Symbol examples:");
            Console.WriteLine("1. NSE:NIFTY50-INDEX     -> Strike gap 50");
            Console.WriteLine("2. NSE:NIFTYBANK-INDEX   -> Strike gap 100");
            Console.WriteLine("3. BSE:SENSEX-INDEX      -> Strike gap 100");
            Console.WriteLine("4. Stock/EQ options      -> Enter custom strike gap manually");
            Console.WriteLine();

            Console.Write($"Enter Symbol or press ENTER for default [{DEFAULT_SYMBOL}]: ");
            string inputSymbol = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(inputSymbol))
                Symbol = inputSymbol;

            StrikeStep = DetectStrikeStep(Symbol);

            Console.Write($"Strike gap detected as {StrikeStep}. Press ENTER to keep or enter custom gap: ");
            string stepText = Console.ReadLine()?.Trim();
            if (int.TryParse(stepText, out int customStep) && customStep > 0)
                StrikeStep = customStep;

            Console.Write($"How many strikes above/below ATM? Press ENTER for default [{DEFAULT_PLUS_MINUS_STRIKES}]: ");
            string countText = Console.ReadLine()?.Trim();
            if (int.TryParse(countText, out int customCount) && customCount > 0)
                PlusMinusStrikes = customCount;

            Console.Write("Enter FYERS Access Token: ");
            string accessToken = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Access Token cannot be empty.");
                Console.ResetColor();
                return;
            }

            accessToken = accessToken
                .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim();

            string authHeader = accessToken.StartsWith(APPID + ":", StringComparison.OrdinalIgnoreCase)
                ? accessToken
                : $"{APPID}:{accessToken}";

            Console.WriteLine();
            Console.WriteLine($"AppId       : [{APPID}]");
            Console.WriteLine($"TokenLength : {accessToken.Length}");
            Console.WriteLine($"Auth Start  : {authHeader.Substring(0, Math.Min(35, authHeader.Length))}...");
            Console.WriteLine();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Clear();

            bool headerAdded = client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
            if (!headerAdded)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Authorization header not added. Please check access token.");
                Console.ResetColor();
                return;
            }

            while (true)
            {
                try
                {
                    string optionChainUrl =
                        $"https://api-t1.fyers.in/data/options-chain-v3?symbol={Uri.EscapeDataString(Symbol)}&strikecount={PlusMinusStrikes}&timestamp=&greeks=1";

                    HttpResponseMessage chainResponse = await client.GetAsync(optionChainUrl);
                    string chainJson = await chainResponse.Content.ReadAsStringAsync();
                    LastChainStatus = $"{(int)chainResponse.StatusCode} - {chainResponse.StatusCode}";

                    Console.Clear();
                    PrintTopHeader();

                    if (!chainResponse.IsSuccessStatusCode)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("OPTION CHAIN API ERROR:");
                        Console.WriteLine(chainJson);
                        Console.ResetColor();
                    }
                    else
                    {
                        List<ChainRow> rows = ParseOptionChain(chainJson);

                        if (rows.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("No option chain data found from first API.");
                            Console.WriteLine();
                            Console.WriteLine("Raw Option Chain Response:");
                            Console.WriteLine(chainJson);
                            Console.ResetColor();
                        }
                        else
                        {
                            List<string> optionSymbols = rows
                                .SelectMany(x => new[] { x.CE?.Symbol, x.PE?.Symbol })
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            if (optionSymbols.Count > 0)
                            {
                                string quoteUrl =
                                    $"https://api-t1.fyers.in/data/quotes?symbols={Uri.EscapeDataString(string.Join(",", optionSymbols))}";

                                HttpResponseMessage quoteResponse = await client.GetAsync(quoteUrl);
                                string quoteJson = await quoteResponse.Content.ReadAsStringAsync();
                                LastQuoteStatus = $"{(int)quoteResponse.StatusCode} - {quoteResponse.StatusCode}";

                                if (quoteResponse.IsSuccessStatusCode)
                                {
                                    ApplyQuotes(rows, quoteJson);
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine("QUOTE API ERROR. Showing option-chain values only:");
                                    Console.WriteLine(quoteJson);
                                    Console.WriteLine();
                                    Console.ResetColor();
                                }
                            }
                            else
                            {
                                LastQuoteStatus = "No CE/PE symbols found from option chain";
                            }

                            Console.Clear();
                            PrintTopHeader();
                            PrintOptionChain(rows);
                            SaveSnapshot(rows);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Clear();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR:");
                    Console.WriteLine(ex.Message);
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine();
                Console.WriteLine($"Refreshing every {REFRESH_SECONDS} seconds. Press Ctrl + C to stop.");
                Console.ResetColor();

                await Task.Delay(REFRESH_SECONDS * 1000);
            }
        }

        private static int DetectStrikeStep(string symbol)
        {
            string s = symbol.ToUpperInvariant();

            if (s.Contains("BANKNIFTY") || s.Contains("NIFTYBANK"))
                return 100;

            if (s.Contains("SENSEX"))
                return 100;

            if (s.Contains("NIFTY") || s.Contains("NIFTY50"))
                return 50;

            return 50;
        }

        private static decimal RoundToNearestStrike(decimal spot, int step)
        {
            if (step <= 0)
                return spot;

            return Math.Round(spot / step, MidpointRounding.AwayFromZero) * step;
        }

        private static void PrintTopHeader()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("================================================================================");
            Console.WriteLine("FYERS OPTION CHAIN TWO API CONSOLE");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"Symbol       : {Symbol}");
            Console.WriteLine($"AppId        : {APPID}");
            Console.WriteLine($"Strike Gap   : {StrikeStep}");
            Console.WriteLine($"Range        : ATM +/- {PlusMinusStrikes} strikes");
            Console.WriteLine($"Expiry       : {CurrentExpiryDisplay}");
            Console.WriteLine($"Chain API    : {LastChainStatus}");
            Console.WriteLine($"Quotes API   : {LastQuoteStatus}");
            Console.WriteLine($"Time         : {DateTime.Now:dd-MMM-yyyy HH:mm:ss}");
            Console.WriteLine("================================================================================");
            Console.WriteLine();
        }

        private static List<ChainRow> ParseOptionChain(string json)
        {
            JObject root = JObject.Parse(json);

            JArray chainArray =
                root["data"]?["optionsChain"] as JArray
                ?? root["data"]?["optionChain"] as JArray
                ?? root["optionsChain"] as JArray
                ?? new JArray();

            SetCurrentExpiryDisplay(root, chainArray);

            Dictionary<decimal, ChainRow> map = new Dictionary<decimal, ChainRow>();

            foreach (JToken item in chainArray)
            {
                decimal strike = GetDecimal(item, "strike_price", "strikePrice", "strike");
                if (strike <= 0)
                    continue;

                string optionType = GetString(item, "option_type", "optionType", "type", "symbol");

                if (!map.ContainsKey(strike))
                    map[strike] = new ChainRow { Strike = strike };

                ChainRow row = map[strike];

                JToken ceToken = item["CE"] ?? item["ce"];
                JToken peToken = item["PE"] ?? item["pe"];

                if (ceToken != null)
                    row.CE = ParseLeg(ceToken, "CE", strike);

                if (peToken != null)
                    row.PE = ParseLeg(peToken, "PE", strike);

                if (!string.IsNullOrWhiteSpace(optionType))
                {
                    if (optionType.Contains("CE", StringComparison.OrdinalIgnoreCase))
                        row.CE = ParseLeg(item, "CE", strike);

                    if (optionType.Contains("PE", StringComparison.OrdinalIgnoreCase))
                        row.PE = ParseLeg(item, "PE", strike);
                }
            }

            List<ChainRow> allRows = map.Values.OrderBy(x => x.Strike).ToList();
            if (allRows.Count == 0)
                return allRows;

            decimal spot = GetDecimal(root["data"], "underlyingValue", "underlying_value", "ltp", "spot");
            if (spot <= 0)
                spot = GetSpotFromOptions(allRows);

            decimal atmStrike = RoundToNearestStrike(spot, StrikeStep);
            decimal minStrike = atmStrike - (PlusMinusStrikes * StrikeStep);
            decimal maxStrike = atmStrike + (PlusMinusStrikes * StrikeStep);

            List<ChainRow> filteredRows = allRows
                .Where(x => x.Strike >= minStrike && x.Strike <= maxStrike)
                .OrderBy(x => x.Strike)
                .ToList();

            if (filteredRows.Count == 0)
            {
                int atmIndex = allRows
                    .Select((row, index) => new { Index = index, Diff = Math.Abs(row.Strike - spot) })
                    .OrderBy(x => x.Diff)
                    .First()
                    .Index;

                int start = Math.Max(0, atmIndex - PlusMinusStrikes);
                int end = Math.Min(allRows.Count - 1, atmIndex + PlusMinusStrikes);

                filteredRows = allRows.Skip(start).Take(end - start + 1).ToList();
                atmStrike = allRows[atmIndex].Strike;
            }

            foreach (ChainRow row in filteredRows)
                row.IsAtm = row.Strike == atmStrike;

            return filteredRows;
        }

        private static decimal GetSpotFromOptions(List<ChainRow> rows)
        {
            decimal best = 0;
            decimal minDiff = decimal.MaxValue;

            foreach (ChainRow row in rows)
            {
                decimal ce = row.CE?.Ltp ?? 0;
                decimal pe = row.PE?.Ltp ?? 0;
                decimal diff = Math.Abs(ce - pe);

                if (ce > 0 && pe > 0 && diff < minDiff)
                {
                    minDiff = diff;
                    best = row.Strike;
                }
            }

            return best > 0 ? best : rows[rows.Count / 2].Strike;
        }

        private static void SetCurrentExpiryDisplay(JObject root, JArray chainArray)
        {
            JArray expiryData = root["data"]?["expiryData"] as JArray
                                ?? root["data"]?["expiry_data"] as JArray
                                ?? root["expiryData"] as JArray;

            if (expiryData != null && expiryData.Count > 0)
            {
                JToken first = expiryData[0];
                string label = GetString(first, "date", "expiry", "expiryDate", "label");
                string ts = GetString(first, "expiry", "timestamp", "expiryTs", "expiry_ts");

                CurrentExpiryDisplay = !string.IsNullOrWhiteSpace(label)
                    ? label
                    : (!string.IsNullOrWhiteSpace(ts) ? ts : "Nearest / Current");

                return;
            }

            DateTime? expiry = null;
            foreach (JToken item in chainArray)
            {
                expiry = GetExpiryDate(item);
                if (expiry.HasValue)
                    break;
            }

            CurrentExpiryDisplay = expiry.HasValue ? expiry.Value.ToString("dd-MMM-yyyy") : "Nearest / Current";
        }

        private static DateTime? GetExpiryDate(JToken item)
        {
            JToken value = item["expiry"] ?? item["expiryDate"] ?? item["expiry_date"] ?? item["exp_date"] ?? item["exd"];
            DateTime? parsed = ParseExpiryValue(value);
            if (parsed.HasValue)
                return parsed;

            parsed = ParseExpiryValue(item["CE"]?["expiry"] ?? item["CE"]?["expiryDate"] ?? item["CE"]?["expiry_date"]);
            if (parsed.HasValue)
                return parsed;

            parsed = ParseExpiryValue(item["PE"]?["expiry"] ?? item["PE"]?["expiryDate"] ?? item["PE"]?["expiry_date"]);
            if (parsed.HasValue)
                return parsed;

            return null;
        }

        private static DateTime? ParseExpiryValue(JToken value)
        {
            if (value == null)
                return null;

            string text = value.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (long.TryParse(text, out long numeric))
            {
                try
                {
                    if (text.Length == 8)
                    {
                        int year = int.Parse(text.Substring(0, 4));
                        int month = int.Parse(text.Substring(4, 2));
                        int day = int.Parse(text.Substring(6, 2));
                        return new DateTime(year, month, day);
                    }

                    if (numeric > 100000000000)
                        return DateTimeOffset.FromUnixTimeMilliseconds(numeric).LocalDateTime.Date;

                    if (numeric > 1000000000)
                        return DateTimeOffset.FromUnixTimeSeconds(numeric).LocalDateTime.Date;
                }
                catch
                {
                    return null;
                }
            }

            if (DateTime.TryParse(text, out DateTime dt))
                return dt.Date;

            return null;
        }

        private static OptionLeg ParseLeg(JToken token, string side, decimal strike)
        {
            return new OptionLeg
            {
                Symbol = GetString(token, "symbol", "n"),
                Side = side,
                Strike = strike,
                Ltp = GetDecimal(token, "ltp", "lp", "last_price", "lastPrice"),
                Oi = GetDecimal(token, "oi", "open_interest", "openInterest"),
                OiChange = GetDecimal(token, "oich", "oi_change", "oiChange", "change_oi", "changeOi"),
                Volume = GetDecimal(token, "volume", "vol", "v"),
                Iv = GetDecimal(token, "iv", "implied_volatility", "impliedVolatility"),
                Delta = GetGreek(token, "delta"),
                Gamma = GetGreek(token, "gamma"),
                Theta = GetGreek(token, "theta"),
                Vega = GetGreek(token, "vega")
            };
        }

        private static void ApplyQuotes(List<ChainRow> rows, string quoteJson)
        {
            JObject root = JObject.Parse(quoteJson);
            JArray data = root["d"] as JArray ?? root["data"] as JArray ?? new JArray();

            Dictionary<string, JToken> quoteMap = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

            foreach (JToken q in data)
            {
                string symbol = GetString(q, "n", "symbol");
                if (string.IsNullOrWhiteSpace(symbol))
                    symbol = GetString(q["v"], "symbol", "short_name");

                if (!string.IsNullOrWhiteSpace(symbol) && !quoteMap.ContainsKey(symbol))
                    quoteMap[symbol] = q;
            }

            foreach (ChainRow row in rows)
            {
                ApplyQuoteToLeg(row.CE, quoteMap);
                ApplyQuoteToLeg(row.PE, quoteMap);
            }
        }

        private static void ApplyQuoteToLeg(OptionLeg leg, Dictionary<string, JToken> quoteMap)
        {
            if (leg == null || string.IsNullOrWhiteSpace(leg.Symbol))
                return;

            if (!quoteMap.TryGetValue(leg.Symbol, out JToken quote))
                return;

            JToken v = quote["v"] ?? quote;

            decimal lp = GetDecimal(v, "lp", "ltp", "last_price", "lastPrice");
            if (lp > 0)
                leg.Ltp = lp;

            decimal volume = GetDecimal(v, "volume", "vol", "v");
            if (volume > 0)
                leg.Volume = volume;

            decimal oi = GetDecimal(v, "oi", "open_interest", "openInterest");
            if (oi > 0)
                leg.Oi = oi;

            decimal bid = GetDecimal(v, "bid", "bid_price", "bidPrice");
            decimal ask = GetDecimal(v, "ask", "ask_price", "askPrice");
            decimal ch = GetDecimal(v, "ch", "change");
            decimal chp = GetDecimal(v, "chp", "change_percent", "changePercent");

            leg.Bid = bid;
            leg.Ask = ask;
            leg.PriceChange = ch;
            leg.PriceChangePercent = chp;
        }

        private static void PrintOptionChain(List<ChainRow> rows)
        {
            decimal maxCeOiChange = rows.Where(x => x.CE != null).Select(x => x.CE.OiChange).DefaultIfEmpty(0).Max();
            decimal maxPeOiChange = rows.Where(x => x.PE != null).Select(x => x.PE.OiChange).DefaultIfEmpty(0).Max();

            ChainRow resistanceRow = rows.FirstOrDefault(x => x.CE != null && x.CE.OiChange == maxCeOiChange);
            ChainRow supportRow = rows.FirstOrDefault(x => x.PE != null && x.PE.OiChange == maxPeOiChange);

            decimal resistanceStrike = resistanceRow?.Strike ?? 0;
            decimal supportStrike = supportRow?.Strike ?? 0;

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"CURRENT EXPIRY : {CurrentExpiryDisplay}");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"RESISTANCE : {resistanceStrike} | Max CE OI Change : {maxCeOiChange:N0}");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"SUPPORT    : {supportStrike} | Max PE OI Change : {maxPeOiChange:N0}");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"FLOW       : First options-chain-v3 gets CE/PE symbols, then quotes API updates live prices");
            Console.WriteLine($"RANGE      : ATM +/- {PlusMinusStrikes} strikes | Step {StrikeStep} points | Max rows {PlusMinusStrikes * 2 + 1}");

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();

            Console.WriteLine(
                "Strike".PadRight(10) +
                " | CE LTP".PadRight(11) +
                "CE OI".PadRight(12) +
                "CE OI Chg".PadRight(13) +
                "CE IV".PadRight(8) +
                "CE Chg".PadRight(10) +
                "CE Signal".PadRight(18) +
                " || " +
                "PE LTP".PadRight(10) +
                "PE OI".PadRight(12) +
                "PE OI Chg".PadRight(13) +
                "PE IV".PadRight(8) +
                "PE Chg".PadRight(10) +
                "PE Signal"
            );

            Console.WriteLine(new string('-', 150));

            foreach (ChainRow row in rows)
            {
                bool isResistance = row.Strike == resistanceStrike;
                bool isSupport = row.Strike == supportStrike;

                if (row.IsAtm)
                    Console.ForegroundColor = ConsoleColor.Cyan;
                else if (isResistance)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (isSupport)
                    Console.ForegroundColor = ConsoleColor.Green;
                else
                    Console.ForegroundColor = ConsoleColor.Gray;

                string strikeText = row.IsAtm ? $">>{row.Strike:0}<<" : row.Strike.ToString("0");
                Console.Write($"{strikeText,-10} | ");

                WriteLegWithColor(row.CE, "CE", isResistance, isSupport);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" || ");

                WriteLegWithColor(row.PE, "PE", isResistance, isSupport);

                Console.WriteLine();
            }

            PrintAlerts(rows, resistanceStrike, supportStrike, maxCeOiChange, maxPeOiChange);
            Console.ResetColor();
        }

        private static void WriteLegWithColor(OptionLeg leg, string side, bool isResistance, bool isSupport)
        {
            if (leg == null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("-".PadRight(10) + "-".PadRight(12) + "-".PadRight(13) + "-".PadRight(8) + "-".PadRight(10) + "-".PadRight(18));
                return;
            }

            string signal = GetSignal(leg);

            if (side == "CE" && isResistance)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (side == "PE" && isSupport)
                Console.ForegroundColor = ConsoleColor.Green;
            else if (signal.Contains("Writing"))
                Console.ForegroundColor = ConsoleColor.Red;
            else if (signal.Contains("Short Cover") || signal.Contains("Long Build"))
                Console.ForegroundColor = ConsoleColor.Green;
            else if (signal.Contains("Long Unwinding"))
                Console.ForegroundColor = ConsoleColor.Yellow;
            else
                Console.ForegroundColor = ConsoleColor.Gray;

            Console.Write(
                $"{leg.Ltp,-10:0.00}" +
                $"{leg.Oi,-12:0}" +
                $"{leg.OiChange,-13:0}" +
                $"{leg.Iv,-8:0.00}" +
                $"{leg.PriceChange,-10:0.00}" +
                $"{signal,-18}"
            );
        }

        private static void PrintAlerts(List<ChainRow> rows, decimal resistanceStrike, decimal supportStrike, decimal maxCeOiChange, decimal maxPeOiChange)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            Console.WriteLine("ALERTS:");
            Console.WriteLine("-------");

            bool hasAlert = false;

            foreach (ChainRow row in rows)
            {
                if (row.Strike == resistanceStrike)
                {
                    hasAlert = true;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} | RESISTANCE | {row.Strike} | Max CE OI Change {maxCeOiChange:N0} | Possible Call Writing");
                }

                if (row.Strike == supportStrike)
                {
                    hasAlert = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} | SUPPORT    | {row.Strike} | Max PE OI Change {maxPeOiChange:N0} | Possible Put Writing");
                }

                string ceSignal = GetSignal(row.CE);
                string peSignal = GetSignal(row.PE);

                if (ceSignal != "-")
                {
                    hasAlert = true;
                    PrintSignalAlert(row.Strike, "CE", ceSignal);
                }

                if (peSignal != "-")
                {
                    hasAlert = true;
                    PrintSignalAlert(row.Strike, "PE", peSignal);
                }
            }

            if (!hasAlert)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("No active alerts yet. First refresh stores snapshot, next refresh starts comparison.");
            }

            Console.ResetColor();
        }

        private static void PrintSignalAlert(decimal strike, string side, string signal)
        {
            if (signal.Contains("Writing") || signal.Contains("Long Unwinding"))
                Console.ForegroundColor = ConsoleColor.Red;
            else
                Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} | {strike} {side} | {signal}");
        }

        private static string GetSignal(OptionLeg current)
        {
            if (current == null)
                return "-";

            string key = $"{current.Strike}_{current.Side}";

            if (!PreviousLegs.ContainsKey(key))
                return "-";

            OptionLeg previous = PreviousLegs[key];

            decimal oiDiff = current.Oi - previous.Oi;
            decimal priceDiff = current.Ltp - previous.Ltp;

            if (oiDiff > 0 && priceDiff < 0)
                return current.Side == "CE" ? "Call Writing" : "Put Writing";

            if (oiDiff < 0 && priceDiff > 0)
                return "Short Cover";

            if (oiDiff > 0 && priceDiff > 0)
                return "Long Build";

            if (oiDiff < 0 && priceDiff < 0)
                return "Long Unwinding";

            return "-";
        }

        private static void SaveSnapshot(List<ChainRow> rows)
        {
            foreach (ChainRow row in rows)
            {
                if (row.CE != null)
                    PreviousLegs[$"{row.Strike}_CE"] = row.CE.Clone();

                if (row.PE != null)
                    PreviousLegs[$"{row.Strike}_PE"] = row.PE.Clone();
            }
        }

        private static decimal GetDecimal(JToken token, params string[] names)
        {
            if (token == null)
                return 0;

            foreach (string name in names)
            {
                JToken value = token[name];
                if (value != null && decimal.TryParse(value.ToString(), out decimal result))
                    return result;
            }

            return 0;
        }

        private static string GetString(JToken token, params string[] names)
        {
            if (token == null)
                return string.Empty;

            foreach (string name in names)
            {
                JToken value = token[name];
                if (value != null)
                    return value.ToString();
            }

            return string.Empty;
        }

        private static decimal GetGreek(JToken token, string name)
        {
            if (token == null)
                return 0;

            JToken direct = token[name];
            if (direct != null && decimal.TryParse(direct.ToString(), out decimal directValue))
                return directValue;

            JToken greek = token["greeks"]?[name];
            if (greek != null && decimal.TryParse(greek.ToString(), out decimal greekValue))
                return greekValue;

            return 0;
        }
    }

    internal class ChainRow
    {
        public decimal Strike { get; set; }
        public bool IsAtm { get; set; }
        public OptionLeg CE { get; set; }
        public OptionLeg PE { get; set; }
    }

    internal class OptionLeg
    {
        public string Symbol { get; set; }
        public string Side { get; set; }
        public decimal Strike { get; set; }
        public decimal Ltp { get; set; }
        public decimal Oi { get; set; }
        public decimal OiChange { get; set; }
        public decimal Volume { get; set; }
        public decimal Iv { get; set; }
        public decimal Delta { get; set; }
        public decimal Gamma { get; set; }
        public decimal Theta { get; set; }
        public decimal Vega { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal PriceChange { get; set; }
        public decimal PriceChangePercent { get; set; }

        public OptionLeg Clone()
        {
            return new OptionLeg
            {
                Symbol = Symbol,
                Side = Side,
                Strike = Strike,
                Ltp = Ltp,
                Oi = Oi,
                OiChange = OiChange,
                Volume = Volume,
                Iv = Iv,
                Delta = Delta,
                Gamma = Gamma,
                Theta = Theta,
                Vega = Vega,
                Bid = Bid,
                Ask = Ask,
                PriceChange = PriceChange,
                PriceChangePercent = PriceChangePercent
            };
        }
    }
}
