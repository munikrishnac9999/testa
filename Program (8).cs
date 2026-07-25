using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FyersOptionChain
{
    internal class Program
    {
        private const string APPID = "ED88TMB033-100";
        private const string APPSECRET = "PASTE_YOUR_APP_SECRET_HERE"; // Put same app secret used in your working history code

        private const string VALIDATE_AUTH_URL = "https://api-t1.fyers.in/api/v3/validate-authcode";
        private const string OPTION_CHAIN_URL = "https://api-t1.fyers.in/data/options-chain-v3";
        private const string QUOTES_URL = "https://api-t1.fyers.in/data/quotes";

        private const string DEFAULT_SYMBOL = "NSE:NIFTY50-INDEX";
        private const int DEFAULT_PLUS_MINUS_STRIKES = 10;
        private const int REFRESH_SECONDS = 5;

        private static readonly Dictionary<string, OptionLeg> PreviousLegs = new(StringComparer.OrdinalIgnoreCase);

        private static string Symbol = DEFAULT_SYMBOL;
        private static int StrikeStep = 50;
        private static int PlusMinusStrikes = DEFAULT_PLUS_MINUS_STRIKES;
        private static string CurrentExpiryDisplay = "Nearest / Current";
        private static DateTime? CurrentExpiryDate = null;
        private static decimal CurrentSpot = 0m;
        private const decimal RiskFreeRate = 0.06m;
        private static string LastChainStatus = "-";
        private static string LastQuoteStatus = "-";

        static async Task Main(string[] args)
        {
            Console.Title = "FYERS Option Chain AuthCode Console";

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("FYERS OPTION CHAIN AUTH_CODE CONSOLE");
            Console.WriteLine("------------------------------------");
            Console.WriteLine();

            Console.WriteLine("IMPORTANT:");
            Console.WriteLine("This version works like your working History API code.");
            Console.WriteLine("Paste FYERS AUTH_CODE, not access token.");
            Console.WriteLine();

            Console.WriteLine("Symbol examples:");
            Console.WriteLine("1. NSE:NIFTY50-INDEX     -> Strike gap 50");
            Console.WriteLine("2. NSE:NIFTYBANK-INDEX   -> Strike gap 100");
            Console.WriteLine("3. BSE:SENSEX-INDEX      -> Strike gap 100");
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

            Console.Write("Paste fresh FYERS AUTH_CODE from redirect: ");
            string authCode = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(authCode))
            {
                Error("Auth code cannot be empty.");
                return;
            }

            if (APPSECRET == "PASTE_YOUR_APP_SECRET_HERE")
            {
                Error("Please update APPSECRET in Program.cs using your FYERS app secret.");
                return;
            }

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("FyersOptionChainConsole/1.0");

            string accessToken;
            try
            {
                accessToken = await ExchangeAuthCodeAsync(client, authCode);
            }
            catch (Exception ex)
            {
                Error("AUTH_CODE to ACCESS_TOKEN failed:");
                Console.WriteLine(ex.Message);
                return;
            }

            string compositeToken = $"{APPID}:{accessToken}";

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine("Access token generated successfully.");
            Console.WriteLine($"Header Start: {compositeToken.Substring(0, Math.Min(40, compositeToken.Length))}...");
            Console.ResetColor();

            while (true)
            {
                try
                {
                    string optionChainUrl =
                        OPTION_CHAIN_URL +
                        $"?symbol={Uri.EscapeDataString(Symbol)}" +
                        $"&strikecount={PlusMinusStrikes}" +
                        "&timestamp=" +
                        "&greeks=1";

                    string chainJson = await GetAsync(client, optionChainUrl, compositeToken, "OPTION_CHAIN");

                    List<ChainRow> rows = ParseOptionChain(chainJson);

                    List<string> optionSymbols = rows
                        .SelectMany(x => new[] { x.CE?.Symbol, x.PE?.Symbol })
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(50)
                        .ToList();

                    if (optionSymbols.Count > 0)
                    {
                        string quoteUrl = QUOTES_URL + "?symbols=" + Uri.EscapeDataString(string.Join(",", optionSymbols));
                        string quoteJson = await GetAsync(client, quoteUrl, compositeToken, "QUOTES");
                        ApplyQuotes(rows, quoteJson);
                    }
                    else
                    {
                        LastQuoteStatus = "Skipped - no CE/PE symbols from option chain";
                    }

                    Console.Clear();
                    PrintTopHeader();
                    PrintOptionChain(rows);
                    SaveSnapshot(rows);
                }
                catch (Exception ex)
                {
                    Console.Clear();
                    PrintTopHeader();
                    Error("ERROR:");
                    Console.WriteLine(ex.Message);
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine();
                Console.WriteLine($"Refreshing every {REFRESH_SECONDS} seconds. Press Ctrl + C to stop.");
                Console.ResetColor();
                await Task.Delay(REFRESH_SECONDS * 1000);
            }
        }

        private static async Task<string> ExchangeAuthCodeAsync(HttpClient client, string authCode)
        {
            var payload = new
            {
                grant_type = "authorization_code",
                appIdHash = Sha256Hex($"{APPID}:{APPSECRET}"),
                code = authCode
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, VALIDATE_AUTH_URL);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using HttpResponseMessage res = await client.SendAsync(req);
            string body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)res.StatusCode} {res.ReasonPhrase}\n{body}");

            using JsonDocument doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("access_token", out JsonElement tokenEl))
                throw new Exception("No access_token in response:\n" + body);

            string token = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("access_token is empty:\n" + body);

            return token;
        }

        private static async Task<string> GetAsync(HttpClient client, string url, string compositeToken, string name)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", compositeToken);
            req.Headers.Accept.ParseAdd("application/json");

            using HttpResponseMessage res = await client.SendAsync(req);
            string body = await res.Content.ReadAsStringAsync();

            if (name == "OPTION_CHAIN") LastChainStatus = $"{(int)res.StatusCode} - {res.StatusCode}";
            if (name == "QUOTES") LastQuoteStatus = $"{(int)res.StatusCode} - {res.StatusCode}";

            if (!res.IsSuccessStatusCode)
            {
                throw new Exception($"{name} API failed\nURL: {url}\nHTTP {(int)res.StatusCode} {res.ReasonPhrase}\nResponse:\n{body}");
            }

            JObject root = JObject.Parse(body);
            string s = root["s"]?.ToString();
            string code = root["code"]?.ToString();
            string message = root["message"]?.ToString();

            if (string.Equals(s, "error", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"{name} API returned error\nURL: {url}\nCode: {code}\nMessage: {message}\nResponse:\n{body}");

            return body;
        }

        private static string Sha256Hex(string input)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static int DetectStrikeStep(string symbol)
        {
            string s = symbol.ToUpperInvariant();
            if (s.Contains("BANKNIFTY") || s.Contains("NIFTYBANK")) return 100;
            if (s.Contains("SENSEX")) return 100;
            if (s.Contains("NIFTY") || s.Contains("NIFTY50")) return 50;
            return 50;
        }

        private static decimal RoundToNearestStrike(decimal spot, int step)
        {
            if (step <= 0) return spot;
            return Math.Round(spot / step, MidpointRounding.AwayFromZero) * step;
        }

        private static void PrintTopHeader()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("================================================================================");
            Console.WriteLine("FYERS OPTION CHAIN AUTH_CODE CONSOLE");
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

            SetCurrentExpiryDisplay(root);

            Dictionary<decimal, ChainRow> map = new();

            foreach (JToken item in chainArray)
            {
                decimal strike = GetDecimal(item, "strike_price", "strikePrice", "strike");
                if (strike <= 0) continue;

                string optionType = GetString(item, "option_type", "optionType", "type", "symbol");

                if (!map.ContainsKey(strike))
                    map[strike] = new ChainRow { Strike = strike };

                ChainRow row = map[strike];

                JToken ceToken = item["CE"] ?? item["ce"];
                JToken peToken = item["PE"] ?? item["pe"];

                if (ceToken != null) row.CE = ParseLeg(ceToken, "CE", strike);
                if (peToken != null) row.PE = ParseLeg(peToken, "PE", strike);

                if (!string.IsNullOrWhiteSpace(optionType))
                {
                    if (optionType.Contains("CE", StringComparison.OrdinalIgnoreCase))
                        row.CE = ParseLeg(item, "CE", strike);

                    if (optionType.Contains("PE", StringComparison.OrdinalIgnoreCase))
                        row.PE = ParseLeg(item, "PE", strike);
                }
            }

            List<ChainRow> allRows = map.Values.OrderBy(x => x.Strike).ToList();
            if (allRows.Count == 0) return allRows;

            decimal spot = GetDecimal(root["data"], "underlyingValue", "underlying_value", "ltp", "spot");
            if (spot <= 0) spot = GetSpotFromOptions(allRows);
            CurrentSpot = spot;

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

            CalculateGreeksForRows(filteredRows);

            return filteredRows;
        }

        private static void SetCurrentExpiryDisplay(JObject root)
        {
            CurrentExpiryDate = null;

            JArray expiryData = root["data"]?["expiryData"] as JArray
                                ?? root["data"]?["expiry_data"] as JArray
                                ?? root["expiryData"] as JArray;

            if (expiryData != null && expiryData.Count > 0)
            {
                JToken first = expiryData[0];
                string label = GetString(first, "date", "expiryDate", "label");
                string ts = GetString(first, "expiry", "timestamp", "expiryTs", "expiry_ts");

                CurrentExpiryDate = ParseExpiryText(label) ?? ParseExpiryText(ts);
                CurrentExpiryDisplay = CurrentExpiryDate.HasValue
                    ? CurrentExpiryDate.Value.ToString("dd-MMM-yyyy")
                    : (!string.IsNullOrWhiteSpace(label) ? label : (!string.IsNullOrWhiteSpace(ts) ? ts : "Nearest / Current"));
                return;
            }

            CurrentExpiryDisplay = "Nearest / Current";
        }

        private static DateTime? ParseExpiryText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();

            if (long.TryParse(text, out long numeric))
            {
                try
                {
                    if (text.Length == 8)
                    {
                        int y = int.Parse(text.Substring(0, 4));
                        int m = int.Parse(text.Substring(4, 2));
                        int d = int.Parse(text.Substring(6, 2));
                        return new DateTime(y, m, d);
                    }
                    if (numeric > 100000000000) return DateTimeOffset.FromUnixTimeMilliseconds(numeric).LocalDateTime.Date;
                    if (numeric > 1000000000) return DateTimeOffset.FromUnixTimeSeconds(numeric).LocalDateTime.Date;
                }
                catch { return null; }
            }

            if (DateTime.TryParse(text, out DateTime dt)) return dt.Date;
            return null;
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
            Dictionary<string, JToken> quoteMap = new(StringComparer.OrdinalIgnoreCase);

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
            if (leg == null || string.IsNullOrWhiteSpace(leg.Symbol)) return;
            if (!quoteMap.TryGetValue(leg.Symbol, out JToken quote)) return;

            JToken v = quote["v"] ?? quote;

            decimal lp = GetDecimal(v, "lp", "ltp", "last_price", "lastPrice");
            if (lp > 0) leg.Ltp = lp;

            decimal volume = GetDecimal(v, "volume", "vol", "v");
            if (volume > 0) leg.Volume = volume;

            decimal oi = GetDecimal(v, "oi", "open_interest", "openInterest");
            if (oi > 0) leg.Oi = oi;

            leg.Bid = GetDecimal(v, "bid", "bid_price", "bidPrice");
            leg.Ask = GetDecimal(v, "ask", "ask_price", "askPrice");
            leg.PriceChange = GetDecimal(v, "ch", "change");
            leg.PriceChangePercent = GetDecimal(v, "chp", "change_percent", "changePercent");
        }

        private static void PrintOptionChain(List<ChainRow> rows)
        {
            if (rows.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No rows found in option chain response.");
                Console.ResetColor();
                Console.Out.Flush();
                return;
            }

            decimal maxCeOi = rows.Where(x => x.CE != null).Select(x => x.CE.Oi).DefaultIfEmpty(0).Max();
            decimal maxPeOi = rows.Where(x => x.PE != null).Select(x => x.PE.Oi).DefaultIfEmpty(0).Max();
            decimal maxCeVolume = rows.Where(x => x.CE != null).Select(x => x.CE.Volume).DefaultIfEmpty(0).Max();
            decimal maxPeVolume = rows.Where(x => x.PE != null).Select(x => x.PE.Volume).DefaultIfEmpty(0).Max();
            decimal maxCeOiChange = rows.Where(x => x.CE != null).Select(x => x.CE.OiChange).DefaultIfEmpty(0).Max();
            decimal maxPeOiChange = rows.Where(x => x.PE != null).Select(x => x.PE.OiChange).DefaultIfEmpty(0).Max();

            ChainRow resistanceRow = rows.FirstOrDefault(x => x.CE != null && x.CE.OiChange == maxCeOiChange);
            ChainRow supportRow = rows.FirstOrDefault(x => x.PE != null && x.PE.OiChange == maxPeOiChange);

            decimal resistanceStrike = resistanceRow?.Strike ?? 0;
            decimal supportStrike = supportRow?.Strike ?? 0;

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"CURRENT EXPIRY : {CurrentExpiryDisplay} | SPOT : {CurrentSpot:0.00}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"RESISTANCE : {resistanceStrike} | Max CE OI Chg : {FormatQty(maxCeOiChange)}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"SUPPORT    : {supportStrike} | Max PE OI Chg : {FormatQty(maxPeOiChange)}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("LAYOUT     : Compact grid + ATM Greeks section. Greeks recalculated when IV/spot/expiry are available.");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(new string('=', 104));
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(Center("CALLS", 45) + " | " + Center("STRIKE", 8) + " | " + Center("PUTS", 45));
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(new string('=', 104));

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(
                "Signal".PadRight(12) +
                "IV".PadLeft(7) +
                "OI".PadLeft(9) +
                "OIChg".PadLeft(9) +
                "Vol".PadLeft(8) +
                " | " +
                "Strike".PadLeft(8) +
                " | " +
                "Vol".PadLeft(8) +
                "OI".PadLeft(9) +
                "OIChg".PadLeft(9) +
                "IV".PadLeft(7) +
                "Signal".PadLeft(12));
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(new string('-', 104));

            foreach (ChainRow row in rows)
            {
                bool isResistance = row.Strike == resistanceStrike;
                bool isSupport = row.Strike == supportStrike;

                WriteCallLeg(row.CE, isResistance, maxCeOi, maxCeVolume, maxCeOiChange);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" | ");

                Console.ForegroundColor = GetStrikeColor(row, isResistance, isSupport);
                string strikeText = row.IsAtm ? $">{row.Strike:0}<" : row.Strike.ToString("0");
                Console.Write(strikeText.PadLeft(8));

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" | ");

                WritePutLeg(row.PE, isSupport, maxPeOi, maxPeVolume, maxPeOiChange);
                Console.WriteLine();
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(new string('-', 104));
            PrintAtmGreeks(rows);
            PrintAlerts(rows, resistanceStrike, supportStrike, maxCeOiChange, maxPeOiChange);
            Console.ResetColor();
            Console.Out.Flush();
        }

        private static ConsoleColor GetStrikeColor(ChainRow row, bool isResistance, bool isSupport)
        {
            if (row.IsAtm) return ConsoleColor.Cyan;
            if (isResistance && isSupport) return ConsoleColor.Yellow;
            if (isResistance) return ConsoleColor.Red;
            if (isSupport) return ConsoleColor.Green;
            return ConsoleColor.White;
        }

        private static void WriteCallLeg(OptionLeg leg, bool isResistance, decimal maxOi, decimal maxVolume, decimal maxOiChange)
        {
            if (leg == null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("-".PadRight(12) + "-".PadLeft(7) + "-".PadLeft(9) + "-".PadLeft(9) + "-".PadLeft(8));
                return;
            }

            string signal = GetSignal(leg);
            Console.ForegroundColor = GetAnalysisColor(signal, true, isResistance);
            Console.Write(signal.PadRight(12));

            Console.ForegroundColor = GetIvColor(leg.Iv);
            Console.Write($"{leg.Iv,7:0.00}");

            Console.ForegroundColor = isResistance || leg.Oi == maxOi ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.Write(FormatQty(leg.Oi).PadLeft(9));

            Console.ForegroundColor = isResistance || leg.OiChange == maxOiChange || leg.OiChange > 0 ? ConsoleColor.Red : ConsoleColor.Green;
            Console.Write(FormatQty(leg.OiChange).PadLeft(9));

            Console.ForegroundColor = leg.Volume == maxVolume ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.Write(FormatQty(leg.Volume).PadLeft(8));
        }

        private static void WritePutLeg(OptionLeg leg, bool isSupport, decimal maxOi, decimal maxVolume, decimal maxOiChange)
        {
            if (leg == null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("-".PadLeft(8) + "-".PadLeft(9) + "-".PadLeft(9) + "-".PadLeft(7) + "-".PadLeft(12));
                return;
            }

            string signal = GetSignal(leg);

            Console.ForegroundColor = leg.Volume == maxVolume ? ConsoleColor.Green : ConsoleColor.Gray;
            Console.Write(FormatQty(leg.Volume).PadLeft(8));

            Console.ForegroundColor = isSupport || leg.Oi == maxOi ? ConsoleColor.Green : ConsoleColor.Gray;
            Console.Write(FormatQty(leg.Oi).PadLeft(9));

            Console.ForegroundColor = isSupport || leg.OiChange == maxOiChange || leg.OiChange > 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(FormatQty(leg.OiChange).PadLeft(9));

            Console.ForegroundColor = GetIvColor(leg.Iv);
            Console.Write($"{leg.Iv,7:0.00}");

            Console.ForegroundColor = GetAnalysisColor(signal, false, isSupport);
            Console.Write(signal.PadLeft(12));
        }

        private static void PrintAtmGreeks(List<ChainRow> rows)
        {
            ChainRow atm = rows.FirstOrDefault(x => x.IsAtm) ?? rows[rows.Count / 2];
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            Console.WriteLine($"ATM STRIKE : {atm.Strike:0}");

            PrintGreekLine("CE Greeks", atm.CE, true);
            PrintGreekLine("PE Greeks", atm.PE, false);
        }

        private static void PrintGreekLine(string label, OptionLeg leg, bool isCall)
        {
            if (leg == null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"{label,-10}: -");
                return;
            }

            Console.ForegroundColor = isCall ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write($"{label,-10}: ");
            Console.ForegroundColor = GetDeltaColor(leg.Delta, isCall);
            Console.Write($"Delta {leg.Delta,6:0.000}  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"Gamma {leg.Gamma,7:0.0000}  ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Theta {leg.Theta,7:0.00}  ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write($"Vega {leg.Vega,6:0.00}  ");
            Console.ForegroundColor = GetIvColor(leg.Iv);
            Console.Write($"IV {leg.Iv,5:0.00}");
            Console.WriteLine();
        }

        private static ConsoleColor GetDeltaColor(decimal delta, bool isCall)
        {
            if (delta == 0) return ConsoleColor.DarkGray;
            if (isCall && delta > 0) return ConsoleColor.Green;
            if (!isCall && delta < 0) return ConsoleColor.Red;
            return ConsoleColor.Yellow;
        }

        private static ConsoleColor GetIvColor(decimal iv)
        {
            if (iv >= 25) return ConsoleColor.Yellow;
            if (iv >= 18) return ConsoleColor.White;
            return ConsoleColor.Gray;
        }

        private static ConsoleColor GetAnalysisColor(string signal, bool isCall, bool isKeyLevel)
        {
            if (isKeyLevel) return isCall ? ConsoleColor.Red : ConsoleColor.Green;
            if (signal.Contains("Short Build") || signal.Contains("Call Writing") || signal.Contains("Long Unwind")) return ConsoleColor.Red;
            if (signal.Contains("Long Build") || signal.Contains("Put Writing") || signal.Contains("Short Cover")) return ConsoleColor.Green;
            return ConsoleColor.Gray;
        }

        private static string Center(string text, int width)
        {
            if (text.Length >= width) return text.Substring(0, width);
            int left = (width - text.Length) / 2;
            int right = width - text.Length - left;
            return new string(' ', left) + text + new string(' ', right);
        }

        private static string FormatQty(decimal value)
        {
            decimal abs = Math.Abs(value);
            string sign = value < 0 ? "-" : "";
            if (abs >= 10000000m) return sign + (abs / 10000000m).ToString("0.00") + "Cr";
            if (abs >= 100000m) return sign + (abs / 100000m).ToString("0.00") + "L";
            if (abs >= 1000m) return sign + (abs / 1000m).ToString("0.00") + "K";
            return value.ToString("0");
        }

        private static void PrintAlerts(List<ChainRow> rows, decimal resistanceStrike, decimal supportStrike, decimal maxCeOiChange, decimal maxPeOiChange)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            Console.WriteLine("ALERTS:");
            Console.WriteLine("-------");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} | RESISTANCE | {resistanceStrike} | Max CE OI Change {FormatQty(maxCeOiChange)}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} | SUPPORT    | {supportStrike} | Max PE OI Change {FormatQty(maxPeOiChange)}");

            Console.ResetColor();
        }

        private static void CalculateGreeksForRows(List<ChainRow> rows)
        {
            if (CurrentSpot <= 0) return;

            DateTime expiry = CurrentExpiryDate ?? DateTime.Today.AddDays(7);
            double days = Math.Max(1.0, (expiry.Date.AddHours(15.5) - DateTime.Now).TotalDays);
            double t = days / 365.0;
            double r = (double)RiskFreeRate;

            foreach (ChainRow row in rows)
            {
                CalculateGreeks(row.CE, true, CurrentSpot, row.Strike, t, r);
                CalculateGreeks(row.PE, false, CurrentSpot, row.Strike, t, r);
            }
        }

        private static void CalculateGreeks(OptionLeg leg, bool isCall, decimal spot, decimal strike, double t, double r)
        {
            if (leg == null || spot <= 0 || strike <= 0) return;

            // Use FYERS IV if available. If IV is missing, use a safe fallback of 20%.
            double sigma = leg.Iv > 0 ? (double)leg.Iv / 100.0 : 0.20;
            sigma = Math.Max(0.01, sigma);

            double s = (double)spot;
            double k = (double)strike;
            double sqrtT = Math.Sqrt(t);
            double d1 = (Math.Log(s / k) + (r + 0.5 * sigma * sigma) * t) / (sigma * sqrtT);
            double d2 = d1 - sigma * sqrtT;

            double nd1 = NormalPdf(d1);
            double callDelta = NormalCdf(d1);
            double putDelta = callDelta - 1.0;
            double gamma = nd1 / (s * sigma * sqrtT);
            double thetaCall = (-(s * nd1 * sigma) / (2 * sqrtT) - r * k * Math.Exp(-r * t) * NormalCdf(d2)) / 365.0;
            double thetaPut = (-(s * nd1 * sigma) / (2 * sqrtT) + r * k * Math.Exp(-r * t) * NormalCdf(-d2)) / 365.0;
            double vega = s * nd1 * sqrtT / 100.0;

            leg.Delta = (decimal)(isCall ? callDelta : putDelta);
            leg.Gamma = (decimal)gamma;
            leg.Theta = (decimal)(isCall ? thetaCall : thetaPut);
            leg.Vega = (decimal)vega;
        }

        private static double NormalPdf(double x)
        {
            return Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
        }

        private static double NormalCdf(double x)
        {
            // Abramowitz and Stegun approximation.
            double sign = x < 0 ? -1.0 : 1.0;
            x = Math.Abs(x) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return 0.5 * (1.0 + sign * y);
        }

        private static string GetSignal(OptionLeg current)
        {
            if (current == null) return "-";

            string key = $"{current.Strike}_{current.Side}";
            if (!PreviousLegs.ContainsKey(key)) return "-";

            OptionLeg previous = PreviousLegs[key];
            decimal oiDiff = current.Oi - previous.Oi;
            decimal priceDiff = current.Ltp - previous.Ltp;

            if (oiDiff > 0 && priceDiff < 0) return "Short Build";
            if (oiDiff < 0 && priceDiff > 0) return "Short Cover";
            if (oiDiff > 0 && priceDiff > 0) return "Long Build";
            if (oiDiff < 0 && priceDiff < 0) return "Long Unwind";
            return "-";
        }

        private static void SaveSnapshot(List<ChainRow> rows)
        {
            foreach (ChainRow row in rows)
            {
                if (row.CE != null) PreviousLegs[$"{row.Strike}_CE"] = row.CE.Clone();
                if (row.PE != null) PreviousLegs[$"{row.Strike}_PE"] = row.PE.Clone();
            }
        }

        private static decimal GetDecimal(JToken token, params string[] names)
        {
            if (token == null) return 0;
            foreach (string name in names)
            {
                JToken value = token[name];
                if (value != null && decimal.TryParse(value.ToString(), out decimal result)) return result;
            }
            return 0;
        }

        private static string GetString(JToken token, params string[] names)
        {
            if (token == null) return string.Empty;
            foreach (string name in names)
            {
                JToken value = token[name];
                if (value != null) return value.ToString();
            }
            return string.Empty;
        }

        private static decimal GetGreek(JToken token, string name)
        {
            if (token == null) return 0;

            JToken direct = token[name];
            if (direct != null && decimal.TryParse(direct.ToString(), out decimal directValue)) return directValue;

            JToken greek = token["greeks"]?[name];
            if (greek != null && decimal.TryParse(greek.ToString(), out decimal greekValue)) return greekValue;

            return 0;
        }

        private static void Error(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
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
