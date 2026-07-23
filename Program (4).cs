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
        // Hardcoded FYERS App ID
        private const string APPID = "ED88TMB033-100";

        // Default values. You can change these.
        private const string DEFAULT_SYMBOL = "NSE:NIFTY50-INDEX";
        private const int DEFAULT_PLUS_MINUS_STRIKES = 10;
        private const int REFRESH_SECONDS = 5;

        // Previous snapshot is used to calculate live change and signals
        private static readonly Dictionary<string, OptionLeg> PreviousLegs = new Dictionary<string, OptionLeg>();

        private static string Symbol = DEFAULT_SYMBOL;
        private static int StrikeStep = 50;
        private static int PlusMinusStrikes = DEFAULT_PLUS_MINUS_STRIKES;
        private static string CurrentExpiryDisplay = "Current / Nearest";

        static async Task Main(string[] args)
        {
            Console.Title = "FYERS Option Chain Live Console";

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("FYERS OPTION CHAIN LIVE CONSOLE");
            Console.WriteLine("--------------------------------");
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

            string authHeader = $"{APPID}:{accessToken}";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", authHeader);

            string url = $"https://api-t1.fyers.in/data/option-chain?symbol={Uri.EscapeDataString(Symbol)}";

            while (true)
            {
                try
                {
                    HttpResponseMessage response = await client.GetAsync(url);
                    string json = await response.Content.ReadAsStringAsync();

                    Console.Clear();
                    PrintTopHeader(response);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("API Error Response:");
                        Console.WriteLine(json);
                        Console.ResetColor();
                    }
                    else
                    {
                        List<ChainRow> rows = ParseOptionChain(json);

                        if (rows.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("No option chain data found.");
                            Console.WriteLine();
                            Console.WriteLine("Raw Response:");
                            Console.WriteLine(json);
                            Console.ResetColor();
                        }
                        else
                        {
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

            // Important: Check BANKNIFTY before NIFTY because BANKNIFTY contains NIFTY text.
            if (s.Contains("BANKNIFTY") || s.Contains("NIFTYBANK"))
                return 100;

            if (s.Contains("SENSEX"))
                return 100;

            if (s.Contains("NIFTY") || s.Contains("NIFTY50"))
                return 50;

            // Stock/EQ options strike gap can vary. User can override in console.
            return 50;
        }

        private static decimal RoundToNearestStrike(decimal spot, int step)
        {
            if (step <= 0)
                return spot;

            return Math.Round(spot / step, MidpointRounding.AwayFromZero) * step;
        }

        private static void PrintTopHeader(HttpResponseMessage response)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("================================================================================");
            Console.WriteLine("FYERS OPTION CHAIN LIVE CONSOLE");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"Symbol      : {Symbol}");
            Console.WriteLine($"AppId       : {APPID}");
            Console.WriteLine($"Strike Gap  : {StrikeStep}");
            Console.WriteLine($"Range       : ATM +/- {PlusMinusStrikes} strikes");
            Console.WriteLine($"Expiry      : {CurrentExpiryDisplay}");
            Console.WriteLine($"Status Code : {(int)response.StatusCode} - {response.StatusCode}");
            Console.WriteLine($"Time        : {DateTime.Now:dd-MMM-yyyy HH:mm:ss}");
            Console.WriteLine("================================================================================");
            Console.WriteLine();
        }

        private static JArray FilterCurrentExpiryOnly(JArray chainArray)
        {
            // Some FYERS responses may already return only the selected/current expiry.
            // If expiry fields are not present in each row, we keep the full response.
            List<ExpiryItem> expiryItems = new List<ExpiryItem>();

            foreach (JToken item in chainArray)
            {
                DateTime? expiry = GetExpiryDate(item);

                if (expiry.HasValue)
                {
                    expiryItems.Add(new ExpiryItem
                    {
                        Expiry = expiry.Value.Date,
                        Item = item
                    });
                }
            }

            if (expiryItems.Count == 0)
            {
                CurrentExpiryDisplay = "Not available in response. Showing API returned data.";
                return chainArray;
            }

            DateTime today = DateTime.Today;

            DateTime currentExpiry = expiryItems
                .Where(x => x.Expiry >= today)
                .Select(x => x.Expiry)
                .DefaultIfEmpty(expiryItems.Min(x => x.Expiry))
                .Min();

            CurrentExpiryDisplay = currentExpiry.ToString("dd-MMM-yyyy");

            JArray filtered = new JArray(
                expiryItems
                    .Where(x => x.Expiry == currentExpiry)
                    .Select(x => x.Item)
            );

            return filtered;
        }

        private static DateTime? GetExpiryDate(JToken item)
        {
            // Try direct expiry fields first.
            JToken value =
                item["expiry"]
                ?? item["expiryDate"]
                ?? item["expiry_date"]
                ?? item["exp_date"]
                ?? item["exd"];

            DateTime? parsed = ParseExpiryValue(value);
            if (parsed.HasValue)
                return parsed;

            // Try nested CE/PE expiry fields if direct row field is not available.
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

            // Numeric expiry can be epoch seconds/milliseconds or yyyymmdd.
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

        private static List<ChainRow> ParseOptionChain(string json)
        {
            JObject root = JObject.Parse(json);

            JArray chainArray =
                root["data"]?["optionsChain"] as JArray
                ?? root["data"]?["optionChain"] as JArray
                ?? root["optionsChain"] as JArray
                ?? new JArray();

            chainArray = FilterCurrentExpiryOnly(chainArray);

            Dictionary<decimal, ChainRow> map = new Dictionary<decimal, ChainRow>();

            foreach (JToken item in chainArray)
            {
                decimal strike = GetDecimal(item, "strike_price", "strikePrice", "strike");

                if (strike <= 0)
                    continue;

                if (!map.ContainsKey(strike))
                {
                    map[strike] = new ChainRow
                    {
                        Strike = strike
                    };
                }

                ChainRow row = map[strike];

                // Case 1: API returns CE and PE inside same strike object
                JToken ceToken = item["CE"] ?? item["ce"];
                JToken peToken = item["PE"] ?? item["pe"];

                if (ceToken != null)
                    row.CE = ParseLeg(ceToken, "CE", strike);

                if (peToken != null)
                    row.PE = ParseLeg(peToken, "PE", strike);

                // Case 2: API returns each leg as separate row
                string optionType = GetString(item, "option_type", "optionType", "type", "symbol");

                if (!string.IsNullOrWhiteSpace(optionType))
                {
                    if (optionType.Contains("CE", StringComparison.OrdinalIgnoreCase))
                        row.CE = ParseLeg(item, "CE", strike);

                    if (optionType.Contains("PE", StringComparison.OrdinalIgnoreCase))
                        row.PE = ParseLeg(item, "PE", strike);
                }
            }

            List<ChainRow> allRows = map.Values
                .OrderBy(x => x.Strike)
                .ToList();

            if (allRows.Count == 0)
                return allRows;

            decimal spot = GetDecimal(
                root["data"],
                "underlyingValue",
                "underlying_value",
                "ltp",
                "spot"
            );

            if (spot <= 0)
                spot = allRows[allRows.Count / 2].Strike;

            decimal atmStrike = RoundToNearestStrike(spot, StrikeStep);

            // Select exactly ATM +/- N strikes based on strike gap.
            // Example NIFTY step 50 and +/-10 = ATM +/- 500 points.
            // Example BANKNIFTY/SENSEX step 100 and +/-10 = ATM +/- 1000 points.
            decimal minStrike = atmStrike - (PlusMinusStrikes * StrikeStep);
            decimal maxStrike = atmStrike + (PlusMinusStrikes * StrikeStep);

            List<ChainRow> filteredRows = allRows
                .Where(x => x.Strike >= minStrike && x.Strike <= maxStrike)
                .OrderBy(x => x.Strike)
                .ToList();

            // If FYERS returns a different strike grid, fallback to closest 10 rows above/below ATM.
            if (filteredRows.Count == 0)
            {
                int atmIndex = allRows
                    .Select((row, index) => new { Index = index, Diff = Math.Abs(row.Strike - spot) })
                    .OrderBy(x => x.Diff)
                    .First()
                    .Index;

                int start = Math.Max(0, atmIndex - PlusMinusStrikes);
                int end = Math.Min(allRows.Count - 1, atmIndex + PlusMinusStrikes);

                filteredRows = allRows
                    .Skip(start)
                    .Take(end - start + 1)
                    .ToList();

                atmStrike = allRows[atmIndex].Strike;
            }

            foreach (ChainRow row in filteredRows)
                row.IsAtm = row.Strike == atmStrike;

            return filteredRows;
        }

        private static OptionLeg ParseLeg(JToken token, string side, decimal strike)
        {
            return new OptionLeg
            {
                Side = side,
                Strike = strike,
                Ltp = GetDecimal(token, "ltp", "last_price", "lastPrice"),
                Oi = GetDecimal(token, "oi", "open_interest", "openInterest"),
                OiChange = GetDecimal(token, "oich", "oi_change", "oiChange", "change_oi", "changeOi"),
                Volume = GetDecimal(token, "volume", "vol"),
                Iv = GetDecimal(token, "iv", "implied_volatility", "impliedVolatility"),
                Delta = GetGreek(token, "delta"),
                Gamma = GetGreek(token, "gamma"),
                Theta = GetGreek(token, "theta"),
                Vega = GetGreek(token, "vega")
            };
        }

        private static void PrintOptionChain(List<ChainRow> rows)
        {
            decimal maxCeOiChange = rows
                .Where(x => x.CE != null)
                .Select(x => x.CE.OiChange)
                .DefaultIfEmpty(0)
                .Max();

            decimal maxPeOiChange = rows
                .Where(x => x.PE != null)
                .Select(x => x.PE.OiChange)
                .DefaultIfEmpty(0)
                .Max();

            ChainRow resistanceRow = rows
                .FirstOrDefault(x => x.CE != null && x.CE.OiChange == maxCeOiChange);

            ChainRow supportRow = rows
                .FirstOrDefault(x => x.PE != null && x.PE.OiChange == maxPeOiChange);

            decimal resistanceStrike = resistanceRow?.Strike ?? 0;
            decimal supportStrike = supportRow?.Strike ?? 0;

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"CURRENT EXPIRY : {CurrentExpiryDisplay}");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"RESISTANCE : {resistanceStrike}  | Max CE OI Change : {maxCeOiChange:N0}");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"SUPPORT    : {supportStrike}  | Max PE OI Change : {maxPeOiChange:N0}");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"STRIKE RANGE: ATM +/- {PlusMinusStrikes} strikes | Step {StrikeStep} points | Total max rows {PlusMinusStrikes * 2 + 1}");

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();

            Console.WriteLine(
                "Strike".PadRight(10) +
                " | CE LTP".PadRight(11) +
                "CE OI".PadRight(12) +
                "CE OI Chg".PadRight(13) +
                "CE IV".PadRight(8) +
                "CE Greeks".PadRight(31) +
                "CE Signal".PadRight(18) +
                " || " +
                "PE LTP".PadRight(10) +
                "PE OI".PadRight(12) +
                "PE OI Chg".PadRight(13) +
                "PE IV".PadRight(8) +
                "PE Greeks".PadRight(31) +
                "PE Signal"
            );

            Console.WriteLine(new string('-', 180));

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
                Console.Write(
                    "-".PadRight(10) +
                    "-".PadRight(12) +
                    "-".PadRight(13) +
                    "-".PadRight(8) +
                    "-".PadRight(31) +
                    "-".PadRight(18)
                );
                return;
            }

            string signal = GetSignal(leg);
            string greek = $"D:{leg.Delta:0.00} G:{leg.Gamma:0.000} T:{leg.Theta:0.00} V:{leg.Vega:0.00}";

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
                $"{greek,-31}" +
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

    internal class ExpiryItem
    {
        public DateTime Expiry { get; set; }
        public JToken Item { get; set; }
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

        public OptionLeg Clone()
        {
            return new OptionLeg
            {
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
                Vega = Vega
            };
        }
    }
}
