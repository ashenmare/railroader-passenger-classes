// ModFeeds.cs (Revision 4)
// Find and read the data other Railroader mods share, and share your own. Every mod that carries this file uses one
// shared list of feeds in the game, so a feed any mod publishes can be found and read by every other mod.
//
// USE
//   1. Copy this file into your mod. Change only the namespace line.
//   2. Read:    foreach (ModFeeds.FeedInfo f in ModFeeds.List()) { ... }
//               object answer = ModFeeds.Read("cars-waiting");
//   3. Publish: ModFeeds.Publish("my-feed", MyFeed.Read, "My data", "My Mod", "1.0.0", 1, fields) when your mod is
//      switched on, and ModFeeds.Withdraw("my-feed") when it is switched off. Call both, and Read, on the game's main
//      thread.
//   4. Read a key: if (ModFeeds.TryGetDouble(answer, "carsWaiting", 1, out double cars)) { ... }
//      The 1 is the api your reader was written for. A feed on a newer api is read just the same: only a key its
//      "changed" map lists above 1, and a missing key or one of another kind, is skipped (the call says false; log
//      that once). Never switch a feed off because its api is newer than the one you know.
//   5. Columns (Revision 4): a feed can say which values in its answer are about a game thing (a station, an industry,
//      a car, a locomotive). A reader then shows them without knowing the mod:
//      foreach (ModFeeds.ColumnValue v in ModFeeds.ValuesFor("station", industryId)) { v.Mod, v.Label, v.Text }
//      Declare them when you publish (see COLUMNS below).
//
// RULES (for a feed you publish)
//   - Your read function returns plain data only: Dictionary<string, object>, List<object>, string, bool, int, long,
//     float, double or null. Start the answer with "api" (int, your schema version) and "feed" (string, the id). Use
//     camelCase keys. No NaN or infinity.
//   - It never throws, changes nothing, and is cheap enough to call every couple of seconds. It runs on the game's main
//     thread.
//   - No file paths, account names or machine names in an answer or in the description you publish.
//   - When a key's meaning changes (its unit, scale, what it counts, its type), bump api and list the key in a
//     top-level "changed" map (a Dictionary<string, object>) in the answer: { "key": api }, the api where its meaning last changed. Keys you only
//     add stay out of it. Readers use it to skip just that key and keep reading the rest, so keep every other key
//     as it was: prefer a new key to changing an old one, and never rename one.
//   - Describe every top-level field in `fields`, units included, so anyone who finds the feed knows what it means.
//   - If you change this file, bump Revision and keep the shared contract below exactly: it is what lets copies of any
//     revision, from any author, work together.
//
// COLUMNS (Revision 4, optional)
//   ModFeeds.Publish(id, read, title, mod, version, api, fields, columns, out string refused) takes a list of
//   ModFeeds.Column: new ModFeeds.Column("timetable", "station", "Timetable", "tier", "stations[].timetable.tier",
//   key: "industryId", tiers: new[] { "None", "Wall clock", "Platform clocks", "Bell tower" }).
//   - id: camelCase, unique in the feed. entity: "station", "industry", "car" or "locomotive". A station and an
//     industry are matched by the game's industry id (Industry.identifier), a car or locomotive by its car id.
//   - label: short, as a player reads it (at most 40 characters). kind: "number", "percent" (a number that already is
//     a percent: 125 is 125%), "multiplier" (1.5 is x1.5), "tier" (a whole number, named by tiers), "text" or "bool".
//   - unit: optional, "number" only, at most 12 characters ("lb", "mph"). tiers: required for "tier" (1 to 16 names,
//     the value is the index), refused for other kinds.
//   - path: where the values live in the answer, always a list of dictionaries: the list's key, then [], then the
//     value's key inside each item. Keys are camelCase and may be dotted: "stations[].timetable.tier". The list
//     itself may sit under dotted keys too: "data.stations[].level".
//   - key: the item's key that holds the entity id, dotted allowed; "id" when left out. A feed that lists things in a
//     dictionary keyed by id has to also offer the list shape to declare a column.
//   A declaration that breaks a rule is refused whole (Publish false and refused says why); nothing half-published.
//   Items that lack the value, hold another kind, or are an error entry are simply skipped for that thing.
//
// BEHAVIOR
//   - A mod is its assembly. The first mod to publish an id keeps it; another mod publishing the same id gets false.
//     Publishing again from the same mod (any copy of this file in that assembly) replaces its own entry.
//   - Withdraw removes only your own entry.
//   - Read itself never throws, even when a feed does: it answers { feed, error } instead. What a feed returns is the
//     publisher's data, handed over as is: treat it as read only, and as possibly large or deeply nested.
//   - Nothing runs on its own. Work happens only when a mod calls in.
//   - It names no game type, so it is safe to call from any lifecycle method.
//
// THE SHARED CONTRACT (frozen: every copy of this file, of any revision, keeps it exactly)
//   - AppDomain.CurrentDomain.GetData("ashenmare.feeds.v1") is a Dictionary<string, Dictionary<string, object>>, keyed
//     by feed id (lowercase letters and digits in words joined by '-', at most 64 characters). The first copy that
//     needs it creates it, holding lock (AppDomain.CurrentDomain).
//   - Every read and write of the dictionary holds lock (the dictionary).
//   - Each entry: "read" Func<object>; "title", "mod", "version" string; "api" int;
//     "fields" Dictionary<string, string>; "owner" string (the publishing assembly's simple name).
//   - A copy adds, replaces or removes only entries whose "owner" is its own assembly's name. Entries are replaced
//     whole, never changed in place.
//   - Readers ignore keys they don't know and treat a missing or mistyped key as absent, so a later revision can add
//     keys without breaking an earlier one.
//   - The dictionaries use ordinal string keys. Every call runs on the game's main thread.
//   - Nothing else crosses between mods.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PassengerClasses
{
    internal static class ModFeeds
    {
        internal const int Revision = 4;

        private const string SlotKey = "ashenmare.feeds.v1";
        private const int MaxIdLength = 64;

        // This mod's name as an owner: every entry it publishes carries it, and it changes only those.
        private static readonly string Owner = OwnerName();

        private static string OwnerName()
        {
            try { return typeof(ModFeeds).Assembly.GetName().Name ?? ""; }
            catch { return ""; }
        }

        // What List gives back: a feed's description, copied, so nothing a caller does reaches the shared list.
        internal sealed class FeedInfo
        {
            internal readonly string Id, Title, Mod, Version;
            internal readonly int Api;
            internal readonly IDictionary<string, string> Fields;
            // The columns the feed declares (Revision 4); empty when it declares none.
            internal readonly IList<Column> Columns;

            internal FeedInfo(string id, string title, string mod, string version, int api,
                              IDictionary<string, string> fields) : this(id, title, mod, version, api, fields, null) { }

            internal FeedInfo(string id, string title, string mod, string version, int api,
                              IDictionary<string, string> fields, IList<Column> columns)
            {
                Id = id; Title = title; Mod = mod; Version = version; Api = api; Fields = fields;
                Columns = columns ?? new Column[0];
            }
        }

        // Adds this mod's feed. False when the id is invalid, read is null, the id belongs to another mod, or the
        // shared list can't be reached.
        internal static bool Publish(string id, Func<object> read, string title, string mod, string version, int api,
                                     IDictionary<string, string> fields) =>
            Publish(id, read, title, mod, version, api, fields, null, out string _);

        // The same, with the columns the feed offers (Revision 4). False, with refused saying why, when a column breaks
        // a rule: nothing is published then, and an earlier entry of this mod's with that id stays as it was.
        internal static bool Publish(string id, Func<object> read, string title, string mod, string version, int api,
                                     IDictionary<string, string> fields, IEnumerable<Column> columns, out string refused)
        {
            refused = null;
            try
            {
                List<Dictionary<string, object>> cols = null;
                if (columns != null)
                {
                    refused = CheckColumns(columns, out cols);
                    if (refused != null) return false;
                }
                refused = "the feed was refused (invalid id, no read function, id held by another mod, or the shared list can't be reached)";
                if (!ValidId(id) || read == null || Owner.Length == 0) return false;
                Dictionary<string, Dictionary<string, object>> slot = Slot(true);
                if (slot == null) return false;
                var copy = new Dictionary<string, string>(StringComparer.Ordinal);
                if (fields != null)
                    foreach (KeyValuePair<string, string> f in fields)
                        if (f.Key != null) copy[f.Key] = f.Value ?? "";
                var entry = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "read", read }, { "title", title ?? id }, { "mod", mod ?? "" }, { "version", version ?? "" },
                    { "api", api }, { "fields", copy }, { "owner", Owner },
                };
                if (cols != null && cols.Count > 0) entry["columns"] = cols;
                lock (slot)
                {
                    if (slot.TryGetValue(id, out Dictionary<string, object> held) && !IsMine(held)) return false;
                    slot[id] = entry;
                }
                refused = null;
                return true;
            }
            catch { refused = "the feed was refused (an error)"; return false; }
        }

        // Removes this mod's feed. Another mod's entry with the same id is left alone.
        internal static void Withdraw(string id)
        {
            try
            {
                Dictionary<string, Dictionary<string, object>> slot = Slot(false);
                if (slot == null || id == null) return;
                lock (slot)
                {
                    if (slot.TryGetValue(id, out Dictionary<string, object> held) && IsMine(held)) slot.Remove(id);
                }
            }
            catch { }
        }

        // Every feed loaded right now, sorted by id. Never null.
        internal static List<FeedInfo> List()
        {
            var list = new List<FeedInfo>();
            try
            {
                Dictionary<string, Dictionary<string, object>> slot = Slot(false);
                if (slot == null) return list;
                lock (slot)
                {
                    foreach (KeyValuePair<string, Dictionary<string, object>> kv in slot)
                    {
                        Dictionary<string, object> e = kv.Value;
                        if (e == null) continue;
                        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                        if (Get(e, "fields") is Dictionary<string, string> f)
                            foreach (KeyValuePair<string, string> p in f) fields[p.Key] = p.Value;
                        list.Add(new FeedInfo(kv.Key, Get(e, "title") as string ?? kv.Key,
                                              Get(e, "mod") as string ?? "", Get(e, "version") as string ?? "",
                                              Get(e, "api") is int a ? a : 0, fields, ParseColumns(Get(e, "columns"))));
                    }
                }
                list.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
            }
            catch { }
            return list;
        }


        // ---- Columns (Revision 4) ----

        internal const string EntityStation = "station", EntityIndustry = "industry", EntityCar = "car",
                              EntityLocomotive = "locomotive";
        private static readonly string[] Entities = { EntityStation, EntityIndustry, EntityCar, EntityLocomotive };
        private static readonly string[] Kinds = { "number", "percent", "multiplier", "tier", "text", "bool" };
        private const int MaxColumns = 32, MaxTiers = 16, MaxLabel = 40, MaxUnit = 12, MaxTierName = 30, MaxText = 200;
        private const int MaxItems = 20000;                      // list items looked at per column
        private static readonly long CacheTicks = Stopwatch.Frequency / 10;   // 100 ms

        // One value a feed offers about a game thing (see COLUMNS in the header). Immutable.
        internal sealed class Column
        {
            internal readonly string Id, Entity, Label, Kind, Unit, Path, Key;
            internal readonly string[] Tiers;       // empty unless Kind is "tier"

            internal Column(string id, string entity, string label, string kind, string path, string key = null,
                            string unit = null, string[] tiers = null)
            {
                Id = id; Entity = entity; Label = label; Kind = kind; Path = path; Key = key ?? "id";
                Unit = unit ?? "";
                Tiers = tiers == null ? new string[0] : (string[])tiers.Clone();
            }
        }

        // What ValuesFor gives back: one column's value for one thing, from one feed.
        internal sealed class ColumnValue
        {
            internal readonly string FeedId, FeedTitle, Mod, ColumnId, Label, Kind, Unit;
            internal readonly object Raw;       // double (number, percent, multiplier), int (tier), bool or string
            internal readonly string Text;      // the value as a player reads it: "Bell tower", "125%", "x1.5", "1,200 lb", "Yes"

            internal ColumnValue(FeedInfo feed, Column c, object raw, string text)
            {
                FeedId = feed.Id; FeedTitle = feed.Title; Mod = feed.Mod; ColumnId = c.Id; Label = c.Label;
                Kind = c.Kind; Unit = c.Unit; Raw = raw; Text = text;
            }
        }

        private static bool Has(string[] set, string v) => Array.IndexOf(set, v) >= 0;

        // camelCase word: a lowercase letter, then letters and digits.
        private static bool CamelWord(string w, int max)
        {
            if (string.IsNullOrEmpty(w) || w.Length > max || !(w[0] >= 'a' && w[0] <= 'z')) return false;
            for (int i = 1; i < w.Length; i++)
            {
                char c = w[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        // Dotted camelCase words: "timetable.tier". At most 6 words.
        private static bool DottedKey(string k)
        {
            if (string.IsNullOrEmpty(k)) return false;
            string[] parts = k.Split('.');
            if (parts.Length > 6) return false;
            foreach (string p in parts) if (!CamelWord(p, 40)) return false;
            return true;
        }

        private static bool Clean(string s, int max) // 1..max characters, no control characters, no edge spaces
        {
            if (string.IsNullOrEmpty(s) || s.Length > max || s != s.Trim()) return false;
            foreach (char c in s) if (c < ' ' || c == '\u007f') return false;
            return true;
        }

        // The reason a column can't be published, or null.
        private static string Check(Column c)
        {
            if (c == null) return "a column is null";
            if (!CamelWord(c.Id, 40)) return "column id \"" + c.Id + "\" isn't camelCase (a lowercase letter, then letters and digits, at most 40)";
            string at = "column " + c.Id + ": ";
            if (!Has(Entities, c.Entity)) return at + "entity must be station, industry, car or locomotive";
            if (!Clean(c.Label, MaxLabel)) return at + "label must be 1 to " + MaxLabel + " characters, with no control characters or edge spaces";
            if (!Has(Kinds, c.Kind)) return at + "kind must be number, percent, multiplier, tier, text or bool";
            if (c.Unit.Length > 0 && (c.Kind != "number" || !Clean(c.Unit, MaxUnit))) return at + "unit is for kind number only, 1 to " + MaxUnit + " characters";
            if (c.Kind == "tier")
            {
                if (c.Tiers.Length < 1 || c.Tiers.Length > MaxTiers) return at + "a tier column needs 1 to " + MaxTiers + " tier names";
                foreach (string t in c.Tiers) if (!Clean(t, MaxTierName)) return at + "each tier name must be 1 to " + MaxTierName + " characters";
            }
            else if (c.Tiers.Length != 0) return at + "tiers are for kind tier only";
            string listPart; string valuePart;
            if (!SplitPath(c.Path, out listPart, out valuePart)) return at + "path must be like \"stations[].timetable.tier\": dotted camelCase keys, one [] after the list's key, then the value's key";
            if (!DottedKey(c.Key)) return at + "key must be dotted camelCase keys (default \"id\")";
            return null;
        }

        // "a.b[].c.d" -> "a.b" and "c.d"; false unless it has exactly that shape.
        private static bool SplitPath(string path, out string list, out string value)
        {
            list = value = null;
            if (string.IsNullOrEmpty(path) || path.Length > 120) return false;
            int i = path.IndexOf("[].", StringComparison.Ordinal);
            if (i <= 0) return false;      // a second [] fails below: [ and ] aren't key characters
            list = path.Substring(0, i); value = path.Substring(i + 3);
            return DottedKey(list) && DottedKey(value);
        }

        // Checks every column and gives the stored form (plain data), or the reason.
        private static string CheckColumns(IEnumerable<Column> columns, out List<Dictionary<string, object>> stored)
        {
            stored = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Column c in columns)
            {
                string why = Check(c);
                if (why != null) return why;
                if (!seen.Add(c.Id)) return "column id " + c.Id + " is used twice";
                if (seen.Count > MaxColumns) return "a feed may declare at most " + MaxColumns + " columns";
                var d = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "id", c.Id }, { "entity", c.Entity }, { "label", c.Label }, { "kind", c.Kind },
                    { "path", c.Path }, { "key", c.Key }, { "unit", c.Unit },
                };
                var tiers = new List<object>();
                foreach (string t in c.Tiers) tiers.Add(t);
                d["tiers"] = tiers;
                stored.Add(d);
            }
            return null;
        }

        // Reads the stored form back, trusting nothing: a column that isn't valid is left out.
        private static IList<Column> ParseColumns(object stored)
        {
            var list = new List<Column>();
            try
            {
                if (!(stored is List<Dictionary<string, object>> src)) return list;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (Dictionary<string, object> d in src)
                {
                    if (d == null || list.Count >= MaxColumns) continue;
                    var tiers = new List<string>();
                    if (Get(d, "tiers") is List<object> tl)
                        foreach (object t in tl) tiers.Add(t as string);
                    var c = new Column(Get(d, "id") as string, Get(d, "entity") as string, Get(d, "label") as string,
                                       Get(d, "kind") as string, Get(d, "path") as string, Get(d, "key") as string,
                                       Get(d, "unit") as string, tiers.ToArray());
                    if (Check(c) == null && seen.Add(c.Id)) list.Add(c);
                }
            }
            catch { }
            return list;
        }

        // What ValuesFor remembers about one feed, for as long as its entry is the one in the shared list.
        private sealed class Seen
        {
            internal object Entry;
            internal FeedInfo Info;
            internal long At;                                        // when Index was built
            internal Dictionary<string, Dictionary<string, object>> Index;   // column id -> entity id -> raw value
        }
        private static readonly Dictionary<string, Seen> Cache = new Dictionary<string, Seen>(StringComparer.Ordinal);

        // Every column any loaded feed offers about this game thing, with its value now. entity is "station",
        // "industry", "car" or "locomotive"; entityId the game's industry id or car id. A feed without a value for it
        // (or one that throws, or answers with an error) adds nothing. Never throws; empty for an unknown entity.
        // Cost: each feed with columns is read at most once per 100 ms (its values are indexed by entity id then),
        // so a window that asks about 50 things in one refresh pays for one read per feed plus a lookup per call.
        internal static List<ColumnValue> ValuesFor(string entity, string entityId)
        {
            var result = new List<ColumnValue>();
            try
            {
                if (entityId == null || !Has(Entities, entity)) return result;
                var mine = new List<Seen>();
                Dictionary<string, Dictionary<string, object>> slot = Slot(false);
                if (slot == null) return result;
                lock (Cache)
                {
                    lock (slot)
                    {
                        List<string> gone = null;
                        foreach (KeyValuePair<string, Seen> k in Cache)
                            if (!slot.TryGetValue(k.Key, out Dictionary<string, object> now) || !ReferenceEquals(now, k.Value.Entry))
                                (gone ?? (gone = new List<string>())).Add(k.Key);
                        if (gone != null) foreach (string g in gone) Cache.Remove(g);
                        foreach (KeyValuePair<string, Dictionary<string, object>> kv in slot)
                        {
                            Dictionary<string, object> e = kv.Value;
                            if (e == null) continue;
                            if (!Cache.TryGetValue(kv.Key, out Seen s))
                            {
                                IList<Column> cols = ParseColumns(Get(e, "columns"));
                                if (cols.Count == 0) continue;
                                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                                s = new Seen
                                {
                                    Entry = e,
                                    Info = new FeedInfo(kv.Key, Get(e, "title") as string ?? kv.Key, Get(e, "mod") as string ?? "",
                                                        Get(e, "version") as string ?? "", Get(e, "api") is int a ? a : 0, fields, cols),
                                };
                                Cache[kv.Key] = s;
                            }
                            mine.Add(s);
                        }
                    }
                    mine.Sort((x, y) => string.CompareOrdinal(x.Info.Id, y.Info.Id));
                    foreach (Seen s in mine)
                    {
                        bool wanted = false;
                        foreach (Column c in s.Info.Columns) if (c.Entity == entity) { wanted = true; break; }
                        if (!wanted) continue;
                        long now = Stopwatch.GetTimestamp();
                        if (s.Index == null || now - s.At > CacheTicks || now < s.At) { s.Index = BuildIndex(s); s.At = now; }
                        foreach (Column c in s.Info.Columns)
                        {
                            if (c.Entity != entity) continue;
                            if (!s.Index.TryGetValue(c.Id, out Dictionary<string, object> byId)
                                || !byId.TryGetValue(entityId, out object raw)) continue;
                            result.Add(new ColumnValue(s.Info, c, raw, Format(c, raw)));
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        // Reads the feed once and files each column's values under the entity ids. Never throws; a bad answer is empty.
        private static Dictionary<string, Dictionary<string, object>> BuildIndex(Seen s)
        {
            var index = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            try
            {
                Func<object> read = (s.Entry as Dictionary<string, object>)?["read"] as Func<object>;
                object answer = read == null ? null : read();
                if (!(answer is IDictionary<string, object> top) || top.ContainsKey("error")) return index;
                foreach (Column c in s.Info.Columns)
                {
                    var byId = new Dictionary<string, object>(StringComparer.Ordinal);
                    index[c.Id] = byId;
                    try
                    {
                        SplitPath(c.Path, out string listPath, out string valuePath);
                        if (!(Walk(top, listPath) is IList<object> items)) continue;
                        int n = 0;
                        foreach (object item in items)
                        {
                            if (++n > MaxItems) break;
                            if (!(item is IDictionary<string, object> d) || d.ContainsKey("error")) continue;
                            string id = IdText(Walk(d, c.Key));
                            if (id == null || byId.ContainsKey(id)) continue;
                            object raw = Convert(c, Walk(d, valuePath));
                            if (raw != null) byId[id] = raw;
                        }
                    }
                    catch { }
                }
            }
            catch { index.Clear(); }
            return index;
        }

        // Follows dotted keys through dictionaries; null when any step is missing or isn't a dictionary.
        private static object Walk(object from, string dotted)
        {
            object at = from;
            int start = 0;
            while (start <= dotted.Length)
            {
                int dot = dotted.IndexOf('.', start);
                string key = dot < 0 ? dotted.Substring(start) : dotted.Substring(start, dot - start);
                if (!(at is IDictionary<string, object> d) || !d.TryGetValue(key, out at) || at == null) return null;
                if (dot < 0) return at;
                start = dot + 1;
            }
            return at;
        }

        private static string IdText(object v)
        {
            if (v is string s) return s.Length > 0 && s.Length <= 120 ? s : null;
            if (v is int i) return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (v is long l) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return null;
        }

        private static bool Finite(object v, out double x)
        {
            x = 0;
            if (v is double dd) x = dd; else if (v is int i) x = i; else if (v is long l) x = l; else if (v is float f) x = f;
            else return false;
            return !(double.IsNaN(x) || double.IsInfinity(x));
        }

        // The raw value in the column's kind (double, int, bool, string), or null when the value isn't of that kind.
        private static object Convert(Column c, object v)
        {
            if (v == null) return null;
            switch (c.Kind)
            {
                case "number": case "percent": case "multiplier":
                    return Finite(v, out double x) ? (object)x : null;
                case "tier":
                    if (!Finite(v, out double t) || t != Math.Floor(t) || t < 0 || t > 1000000) return null;
                    return (int)t;
                case "bool": return v is bool b ? (object)b : null;
                case "text":
                    if (!(v is string s)) return null;
                    s = s.Trim();
                    if (s.Length > MaxText) s = s.Substring(0, MaxText);
                    var clean = new System.Text.StringBuilder(s.Length);
                    foreach (char ch in s) clean.Append(ch < ' ' || ch == '\u007f' ? ' ' : ch);
                    return clean.ToString();
            }
            return null;
        }

        // The value as a player reads it. Invariant culture, so the same words on every machine.
        private static string Format(Column c, object raw)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (c.Kind)
            {
                case "number":
                    double n = (double)raw;
                    string body = n == Math.Floor(n) && Math.Abs(n) < 1e15 ? n.ToString("#,0", inv) : n.ToString("#,0.##", inv);
                    return c.Unit.Length > 0 ? body + " " + c.Unit : body;
                case "percent":
                    double p = (double)raw;
                    return (p == Math.Floor(p) && Math.Abs(p) < 1e15 ? p.ToString("#,0", inv) : p.ToString("#,0.##", inv)) + "%";
                case "multiplier": return "x" + ((double)raw).ToString("0.##", inv);
                case "tier":
                    int t = (int)raw;
                    return t < c.Tiers.Length ? c.Tiers[t] : "Tier " + t.ToString(inv);
                case "bool": return (bool)raw ? "Yes" : "No";
                default: return (string)raw;
            }
        }

        // ---- Reading an answer (Revision 3) ----
        // For a reader that knows a feed as of api knownApi. Each Try call returns false, and never throws, when the answer
        // isn't a dictionary, is an error answer, has no such key, has one of another kind, or lists the key in its
        // "changed" map at an api above knownApi. A feed on a newer api is otherwise read like any other.

        // The answer's "api" (an int), or 0 when it has none.
        internal static int ApiOf(object answer)
        {
            try
            {
                return answer is IDictionary<string, object> d && d.TryGetValue("api", out object v) && v is int a
                    ? a : 0;
            }
            catch { return 0; }
        }

        // The api at which the answer's "changed" map says key last changed; 0 when it isn't listed or there is no
        // map (absent or null "changed"); int.MaxValue, so the key is skipped, when the key can't be vouched for: its api
        // isn't a whole number, "changed" is present but isn't a Dictionary<string, object>, or reading it throws.
        internal static int ChangedAt(object answer, string key)
        {
            try
            {
                if (key == null || !(answer is IDictionary<string, object> d)) return 0;
                if (!d.TryGetValue("changed", out object m) || m == null) return 0;
                if (!(m is IDictionary<string, object> map)) return int.MaxValue;
                if (!map.TryGetValue(key, out object v)) return 0;
                if (v is int i) return i;
                if (v is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
                return int.MaxValue;
            }
            catch { return int.MaxValue; }
        }

        // The key's value as the feed gave it.
        internal static bool TryGet(object answer, string key, int knownApi, out object value)
        {
            value = null;
            try
            {
                if (key == null || !(answer is IDictionary<string, object> d) || d.ContainsKey("error")) return false;
                if (!d.TryGetValue(key, out object v) || v == null) return false;
                if (ChangedAt(answer, key) > knownApi) return false;
                value = v;
                return true;
            }
            catch { value = null; return false; }
        }

        // A number (int, long, float or double, finite) as a double.
        internal static bool TryGetDouble(object answer, string key, int knownApi, out double value)
        {
            value = 0;
            if (!TryGet(answer, key, knownApi, out object v)) return false;
            double x;
            if (v is double dd) x = dd;
            else if (v is int i) x = i;
            else if (v is long l) x = l;
            else if (v is float f) x = f;
            else return false;
            if (double.IsNaN(x) || double.IsInfinity(x)) return false;
            value = x;
            return true;
        }

        internal static bool TryGetBool(object answer, string key, int knownApi, out bool value)
        {
            value = false;
            if (!TryGet(answer, key, knownApi, out object v) || !(v is bool b)) return false;
            value = b;
            return true;
        }

        internal static bool TryGetString(object answer, string key, int knownApi, out string value)
        {
            value = null;
            if (!TryGet(answer, key, knownApi, out object v) || !(v is string s)) return false;
            value = s;
            return true;
        }

        // A feed's answer, or { feed, error } when it isn't loaded or its read threw. Never throws.
        internal static object Read(string id) => Read(id, out double _);

        // The same, with how long the feed's read took, in milliseconds (0 when it wasn't called).
        internal static object Read(string id, out double milliseconds)
        {
            milliseconds = 0;
            Func<object> read = null;
            try
            {
                Dictionary<string, Dictionary<string, object>> slot = Slot(false);
                if (slot != null)
                    lock (slot)
                        if (id != null && slot.TryGetValue(id, out Dictionary<string, object> e) && e != null)
                            read = Get(e, "read") as Func<object>;
            }
            catch { }
            if (read == null) return Error(id, "not loaded");
            Stopwatch clock = Stopwatch.StartNew();
            try { return read(); }
            catch (Exception e) { return Error(id, "the feed threw " + e.GetType().Name); }
            finally { milliseconds = clock.Elapsed.TotalMilliseconds; }
        }

        private static Dictionary<string, object> Error(string id, string why) =>
            new Dictionary<string, object>(StringComparer.Ordinal) { { "feed", id ?? "" }, { "error", why } };

        private static bool IsMine(Dictionary<string, object> entry) =>
            entry != null && Owner.Length > 0 && Get(entry, "owner") is string o
            && string.Equals(o, Owner, StringComparison.Ordinal);

        private static object Get(Dictionary<string, object> entry, string key) =>
            entry.TryGetValue(key, out object v) ? v : null;

        // The shared list, created on first need when create is true. Null when the slot holds something else.
        private static Dictionary<string, Dictionary<string, object>> Slot(bool create)
        {
            AppDomain domain = AppDomain.CurrentDomain;
            object held = domain.GetData(SlotKey);
            if (held != null || !create) return held as Dictionary<string, Dictionary<string, object>>;
            lock (domain)
            {
                held = domain.GetData(SlotKey);
                if (held == null)
                {
                    held = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
                    domain.SetData(SlotKey, held);
                }
            }
            return held as Dictionary<string, Dictionary<string, object>>;
        }

        // Lowercase letters and digits in words joined by single '-', at most 64 characters: my-feed, cars-waiting-2.
        private static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;
            if (id[0] == '-' || id[id.Length - 1] == '-') return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c == '-' && id[i - 1] != '-');
                if (!ok) return false;
            }
            return true;
        }
    }
}
