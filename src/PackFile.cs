using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace PassengerClasses
{
    // PassengerClasses.json, the file a pack's author puts beside the pack's Definitions.json (never inside it: a component the game
    // doesn't know rejects the whole pack). No game type here. The rule for readers, now and in every later version: read the "format"
    // number if there is one and read any number the same way; read the words and numbers this version knows, ignore every other key
    // and any word it doesn't know; a file that can't be read at all is skipped, once, with the reason.
    //
    //   { "format": 1, "cars": { "<definition id>": { "class": "parlor", "observation": true, "baggage": 600 } } }
    //
    // Keys of a car (all optional): class (coach, parlor, sleeper, lounge, baggage), observation (true or false), platformAtOtherEnd
    // (true when the platform is at the car's F end), baggage (pounds of luggage the car's own racks hold), and the numbers from the
    // CustomBonus component of the older mod, by the same names: bonusAmount, minNumOfCars, maxNumOfCars, startHour, endHour,
    // minimumOfCartype, maximumOfCartype; plus farePercent. This version reads all of them and acts on class, observation and
    // platformAtOtherEnd.
    internal static class PackFile
    {
        internal const string FileName = "PassengerClasses.json";

        internal sealed class Result
        {
            internal readonly Dictionary<string, Classes.Decl> Cars = new Dictionary<string, Classes.Decl>(StringComparer.Ordinal);
            internal double Format = double.NaN;
            internal string Problem;                    // set when the file was skipped; Cars is empty then
            internal int Ignored;                       // entries that were not objects
        }

        // Parses the text of a file. Never throws.
        internal static Result Parse(string text)
        {
            var r = new Result();
            try
            {
                JToken root = JToken.Parse(text);
                var o = root as JObject;
                if (o == null) { r.Problem = "the file is not a JSON object"; return r; }
                JToken fmt = o.GetValue("format", StringComparison.OrdinalIgnoreCase);
                if (fmt != null && (fmt.Type == JTokenType.Integer || fmt.Type == JTokenType.Float)) r.Format = (double)fmt;
                JToken cars = o.GetValue("cars", StringComparison.OrdinalIgnoreCase);
                if (cars == null) return r;
                var carObj = cars as JObject;
                if (carObj == null) { r.Problem = "\"cars\" is not an object"; return r; }
                foreach (JProperty p in carObj.Properties())
                {
                    var e = p.Value as JObject;
                    if (e == null || string.IsNullOrEmpty(p.Name)) { r.Ignored++; continue; }
                    r.Cars[p.Name] = Entry(e);
                }
            }
            catch (Exception ex)
            {
                r.Cars.Clear();
                r.Problem = "not valid JSON (" + ex.GetType().Name + ")";
            }
            return r;
        }

        private static Classes.Decl Entry(JObject e)
        {
            var d = new Classes.Decl();
            JToken t = e.GetValue("class", StringComparison.OrdinalIgnoreCase);
            if (t != null && t.Type == JTokenType.String) d.Cls = Classes.ParseWord((string)t);
            d.Obs = Flag(e, "observation");
            d.AtOtherEnd = Flag(e, "platformAtOtherEnd");
            d.BaggageLb = Whole(e, "baggage");
            d.Seats = Whole(e, "seats");
            d.FarePercent = Whole(e, "farePercent");
            d.StartHour = Whole(e, "startHour"); d.EndHour = Whole(e, "endHour");
            d.MinCars = Whole(e, "minNumOfCars"); d.MaxCars = Whole(e, "maxNumOfCars");
            d.MinOfType = Whole(e, "minimumOfCartype"); d.MaxOfType = Whole(e, "maximumOfCartype");
            JToken b = e.GetValue("bonusAmount", StringComparison.OrdinalIgnoreCase);
            if (b != null && (b.Type == JTokenType.Integer || b.Type == JTokenType.Float))
            {
                double v = (double)b;
                if (!double.IsNaN(v) && !double.IsInfinity(v) && v >= 0) d.BonusAmount = v;
            }
            return d;
        }

        private static int Flag(JObject e, string key)
        {
            JToken t = e.GetValue(key, StringComparison.OrdinalIgnoreCase);
            return t != null && t.Type == JTokenType.Boolean ? ((bool)t ? 1 : 0) : -1;
        }

        // A whole number of at least 0 (a number with a fraction is read as its whole part); anything else is "not said".
        private static int Whole(JObject e, string key)
        {
            JToken t = e.GetValue(key, StringComparison.OrdinalIgnoreCase);
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return -1;
            double v = (double)t;
            if (double.IsNaN(v) || v < 0 || v > int.MaxValue) return -1;
            return (int)v;
        }

        // The files read so far, by pack folder (the game's folder text, used as a key and never logged). Null: the pack has none.
        private static readonly Dictionary<string, Result> Folders = new Dictionary<string, Result>(StringComparer.Ordinal);

        internal static void Reset() { Folders.Clear(); }

        // The file in a pack's folder, read once. Null when there is none. A file that can't be read is a Result with Problem set.
        internal static Result ForFolder(string folder, Action<string> problem)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            Result r;
            if (Folders.TryGetValue(folder, out r)) return r;
            string path = Path.Combine(folder, FileName);
            if (!File.Exists(path)) { Folders[folder] = null; return null; }
            try { r = Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                r = new Result { Problem = "couldn't be read (" + ex.GetType().Name + ")" };
            }
            Folders[folder] = r;
            if (r.Problem != null && problem != null) problem(r.Problem);
            return r;
        }
    }

    // The player's choices, one file beside settings.json: choices.json, written only by this mod, from the right-click menu.
    //   { "format": 1, "models": { "<definition id>": { "class": "parlor", "observation": true } } }
    internal static class Choices
    {
        internal const string FileName = "choices.json";

        // Reads the file into Classes.Player. Returns null, or why it was left alone (the file is not touched either way).
        internal static string Load(string path)
        {
            Classes.Player.Clear();
            _unreadable = false;
            if (!File.Exists(path)) return null;
            try
            {
                string text = File.ReadAllText(path);
                JToken root = JToken.Parse(text);
                var o = root as JObject;
                if (o == null) { _unreadable = true; return "choices.json is not a JSON object"; }
                var models = o.GetValue("models", StringComparison.OrdinalIgnoreCase) as JObject;
                if (models == null) return null;
                foreach (JProperty p in models.Properties())
                {
                    var e = p.Value as JObject;
                    if (e == null || string.IsNullOrEmpty(p.Name)) continue;
                    var d = new Classes.Decl();
                    JToken c = e.GetValue("class", StringComparison.OrdinalIgnoreCase);
                    if (c != null && c.Type == JTokenType.String) d.Cls = Classes.ParseWord((string)c);
                    JToken ob = e.GetValue("observation", StringComparison.OrdinalIgnoreCase);
                    if (ob != null && ob.Type == JTokenType.Boolean) d.Obs = (bool)ob ? 1 : 0;
                    if (d.Cls >= 0 || d.Obs >= 0) Classes.Player[p.Name] = d;
                }
            }
            catch (Exception ex) { Classes.Player.Clear(); _unreadable = true; return "choices.json couldn't be read (" + ex.GetType().Name + ")"; }
            return null;
        }

        private static bool _unreadable;     // the file on disk is one Load couldn't read: Save keeps a copy of it before writing over it

        // Writes the file from Classes.Player (a temp file, then a move, so a crash never leaves half a file). Returns null or why not.
        internal static string Save(string path)
        {
            try
            {
                var models = new JObject();
                foreach (KeyValuePair<string, Classes.Decl> kv in Classes.Player)
                {
                    var e = new JObject();
                    if (kv.Value.Cls >= 0) e["class"] = Classes.Words[kv.Value.Cls];
                    if (kv.Value.Obs >= 0) e["observation"] = kv.Value.Obs == 1;
                    if (e.Count > 0) models[kv.Key] = e;
                }
                var o = new JObject { { "format", 1 }, { "models", models } };
                if (_unreadable && File.Exists(path)) { File.Copy(path, path + ".bad", true); _unreadable = false; }
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, o.ToString(Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(path)) File.Replace(tmp, path, null);   // one step: a crash never leaves only the temp file
                else File.Move(tmp, path);
                return null;
            }
            catch (Exception ex) { return "choices.json couldn't be written (" + ex.GetType().Name + ")"; }
        }
    }

    // First Class kept one thing of its own: settings.json, six whole numbers. This mod keeps the same key names, so they carry over as
    // they are. Anything else in that file is left behind.
    internal static class Carry
    {
        internal static readonly string[] Keys = { "sharePercent", "farePercent", "pricePercent", "sleeperSharePercent", "sleeperFarePercent", "sleeperPricePercent" };

        // The text of a settings.json holding only First Class's own keys from `text`, or null when none are there to carry.
        internal static string FromFirstClass(string text)
        {
            try
            {
                var o = JToken.Parse(text) as JObject;
                if (o == null) return null;
                var carried = new JObject();
                foreach (string k in Keys)
                {
                    JToken t = o.GetValue(k, StringComparison.OrdinalIgnoreCase);
                    if (t != null && t.Type == JTokenType.Integer) carried[k] = (long)t;
                }
                return carried.Count == 0 ? null : carried.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception) { return null; }
        }
    }
}
