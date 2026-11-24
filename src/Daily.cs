using System;
using System.Collections.Generic;

namespace PassengerClasses
{
    // The day book for the feed: per game day (today and yesterday), railroad-wide, per station and per train, what boarded by class, what the
    // fares were by part, and who was left behind and why. Game-free: the hooks (Boarding.cs, Fares.cs) hand it plain ids, names, a class, a
    // reason and a fare band, plus the game clock in seconds; the feed (Api.cs) reads it. The hooks call it on the main thread; Read may be called from any thread (one lock around every recording and the read).
    //
    // Days are the game's days, rolled at midnight of the game clock (the clock's seconds divided by 86,400, floored; the game's own Day
    // property goes through a float and can be a few seconds off late in a long game). Nothing is saved: after a load the first figure recorded
    // (or the first read) starts today, marked partial, and yesterday is unknown (known false) until the first midnight after that.
    //
    // Bounded: at most MaxStations station rows and MaxTrains train rows a day (the railroad-wide figures are always whole; a day that
    // overflowed says so in stationsFull / trainsFull), rows and their tallies are reused from day to day, and recording a figure allocates
    // nothing once its row exists.
    internal static class Daily
    {
        internal const int MaxStations = 128, MaxTrains = 64;
        internal const int Bands = 9;                         // fare bands 1 to 8, and index 0 for a trip the game cannot measure
        internal const int Coach = 0, First = 1, Sleeper = 2;  // Boarding.ServingClass
        // the ways a rider is left behind
        internal const int NoRoom = 0, NoBerth = 1, Held = 2, GaveUp = 3, Reasons = 4;

        // One set of figures: a day's railroad, or one station's or one train's share of it.
        internal sealed class Tally
        {
            internal readonly int[] Boarded = new int[3];
            internal readonly double[] Game = new double[3];     // the game's own fare for the riders who got off, by the class of the car (the observation bonus is in it)
            internal readonly double[] Extra = new double[3];    // the first-class and sleeper extra this mod added (index 0 stays 0)
            internal readonly double[] Heat = new double[3];     // what the heat fare added (a cold unheated car's cut is negative), by the class of the car
            internal double BagFees, Paid;
            internal readonly int[] Left = new int[Reasons];
            internal readonly int[][] Band = new int[Reasons][];
            internal Tally() { for (int i = 0; i < Reasons; i++) Band[i] = new int[Bands]; }
            internal void Clear()
            {
                Array.Clear(Boarded, 0, Boarded.Length); Array.Clear(Game, 0, Game.Length); Array.Clear(Extra, 0, Extra.Length); Array.Clear(Heat, 0, Heat.Length);
                BagFees = 0; Paid = 0; Array.Clear(Left, 0, Left.Length);
                for (int i = 0; i < Reasons; i++) Array.Clear(Band[i], 0, Bands);
            }
        }

        internal sealed class Row { internal string Id, Name; internal readonly Tally T = new Tally(); }

        internal sealed class Day
        {
            internal long Number;                 // the game clock's day, from 0
            internal bool Known, Partial;
            internal readonly Tally Total = new Tally();
            internal readonly Dictionary<string, Row> StationIx = new Dictionary<string, Row>(StringComparer.Ordinal), TrainIx = new Dictionary<string, Row>(StringComparer.Ordinal);
            internal readonly List<Row> Stations = new List<Row>(), Trains = new List<Row>();
            internal bool StationsFull, TrainsFull;
            internal void Clear()
            {
                for (int i = 0; i < Stations.Count; i++) Pool.Push(Stations[i]);
                for (int i = 0; i < Trains.Count; i++) Pool.Push(Trains[i]);
                Stations.Clear(); Trains.Clear(); StationIx.Clear(); TrainIx.Clear();
                Total.Clear(); StationsFull = false; TrainsFull = false;
            }
        }

        private static readonly Stack<Row> Pool = new Stack<Row>();
        private const long None = long.MinValue;
        private static Day _today = new Day(), _yesterday = new Day();
        private static long _cur = None;
        // the largest number of riders held back by bags seen today for a station and a destination (a level, not an event: counted once per day by its peak)
        private static readonly Dictionary<string, Dictionary<string, int>> HeldPeak = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        // Off: a recording call failed three times (a world load starts it again). Unavailable: this game version lacks a member the day book needs
        // (set at mod load, Main.cs). Either way the day book stops and the fares and boarding go on; the feed says so (`unavailable`).
        private static bool _off;
        internal static bool Unavailable;
        internal static bool Off => _off || Unavailable;
        private static int _failures;

        // One lock around every recording and the read, so a reader on another thread sees a whole day. `_ver` moves with every change to what the
        // book holds (a record, a roll, a reset, a stand-down): the feed's answer is rebuilt only when it has moved (see Read).
        private static readonly object Gate = new object();
        private static long _ver;

        internal static void Reset()
        {
            lock (Gate)
            {
                _ver++;
                _today.Clear(); _yesterday.Clear(); _cur = None; _off = false; _failures = 0;
                foreach (Dictionary<string, int> d in HeldPeak.Values) d.Clear();
            }
        }

        // Main.cs, at mod load: the members the day book needs are all there (null) or one is missing (its name).
        internal static void SetUnavailable(bool unavailable) { lock (Gate) { _ver++; Unavailable = unavailable; } }

        // A recording call threw: counts toward standing the day book down. True when it just stood down.
        internal static bool Failed()
        {
            lock (Gate)
            {
                _failures++;
                if (_failures >= 3 && !_off) { _off = true; _ver++; return true; }
                return false;
            }
        }

        internal static long DayOf(double nowSeconds) => (long)Math.Floor(nowSeconds / 86400.0);

        // Called by every recording: the day changes at midnight of the game clock.
        internal static void Roll(double nowSeconds)
        {
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds)) return;
            long d = DayOf(nowSeconds);
            if (d == _cur) return;
            if (_cur == None || d < _cur)
            {
                // the first day since the load (or the clock went back: a world loaded without a reset): all of it is new and partial
                _today.Clear(); _yesterday.Clear();
                _today.Number = d; _today.Known = true; _today.Partial = true;
                _yesterday.Number = d - 1; _yesterday.Known = false; _yesterday.Partial = false;
            }
            else
            {
                if (d == _cur + 1) { Day t = _yesterday; _yesterday = _today; _today = t; }
                else { _yesterday.Clear(); _yesterday.Known = true; _yesterday.Partial = false; }   // days passed with nothing recorded: a real zero
                _today.Clear();
                _yesterday.Number = d - 1;
                _today.Number = d; _today.Known = true; _today.Partial = false;
            }
            foreach (Dictionary<string, int> p in HeldPeak.Values) p.Clear();
            _cur = d;
            _ver++;
        }

        private static Row RowFor(Day day, bool station, string id, string name)
        {
            if (id == null) return null;
            Dictionary<string, Row> ix = station ? day.StationIx : day.TrainIx;
            if (ix.TryGetValue(id, out Row r)) return r;
            List<Row> list = station ? day.Stations : day.Trains;
            if (list.Count >= (station ? MaxStations : MaxTrains)) { if (station) day.StationsFull = true; else day.TrainsFull = true; return null; }
            r = Pool.Count > 0 ? Pool.Pop() : new Row();
            r.Id = id; r.Name = name; r.T.Clear();
            ix[id] = r; list.Add(r);
            return r;
        }

        private static int Cls(int c) => c < 0 ? 0 : c > 2 ? 2 : c;
        private static int Band(int b) => b < 0 || b >= Bands ? 0 : b;

        // `riders` boarded a car of `cls` at a station, in the train led by the engine trainId (null: no engine in the train, so no train row).
        internal static void Boarded(double now, string stId, string stName, string trId, string trName, int cls, int riders)
        {
            if (riders <= 0) return;
            lock (Gate)
            {
                Roll(now); _ver++;
                int c = Cls(cls);
                _today.Total.Boarded[c] += riders;
                Row s = RowFor(_today, true, stId, stName); if (s != null) s.T.Boarded[c] += riders;
                Row t = RowFor(_today, false, trId, trName); if (t != null) t.T.Boarded[c] += riders;
            }
        }

        // Riders got off a car of `cls` at a station: the game's fare for them, the extra this mod added, and the excess-baggage fee.
        internal static void Fare(double now, string stId, string stName, string trId, string trName, int cls, double game, double extra, double bagFee, double heat = 0)
        {
            lock (Gate)
            {
                Roll(now); _ver++;
                int c = Cls(cls);
                Tally tot = _today.Total;
                tot.Game[c] += game; tot.Extra[c] += extra; tot.Heat[c] += heat; tot.BagFees += bagFee;
                Row s = RowFor(_today, true, stId, stName); if (s != null) { s.T.Game[c] += game; s.T.Extra[c] += extra; s.T.Heat[c] += heat; s.T.BagFees += bagFee; }
                Row t = RowFor(_today, false, trId, trName); if (t != null) { t.T.Game[c] += game; t.T.Extra[c] += extra; t.T.Heat[c] += heat; t.T.BagFees += bagFee; }
            }
        }

        // The dollars the game paid a station (its own total and this mod's extras, in the one payment).
        internal static void Paid(double now, string stId, string stName, double dollars)
        {
            lock (Gate)
            {
                Roll(now); _ver++;
                _today.Total.Paid += dollars;
                Row s = RowFor(_today, true, stId, stName); if (s != null) s.T.Paid += dollars;
            }
        }

        // Riders who rode a lower class than they wanted (NoRoom, NoBerth) or who gave up (GaveUp), counted once each, in a fare band.
        internal static void Left(double now, string stId, string stName, string trId, string trName, int reason, int band, int riders)
        {
            if (riders <= 0 || reason < 0 || reason >= Reasons) return;
            lock (Gate)
            {
                Roll(now); _ver++;
                int b = Band(band);
                Add(_today.Total, reason, b, riders);
                Row s = RowFor(_today, true, stId, stName); if (s != null) Add(s.T, reason, b, riders);
                Row t = RowFor(_today, false, trId, trName); if (t != null) Add(t.T, reason, b, riders);
            }
        }

        private static void Add(Tally t, int reason, int band, int n) { t.Left[reason] += n; t.Band[reason][band] += n; }

        // Riders held back by a train's bags: `held` is how many are held now for this destination (a level that the boarding step repeats each
        // step). A day counts the most seen held for the station and destination, once: the increase over the day's peak is what is added.
        internal static void Holding(double now, string stId, string stName, string trId, string trName, string dest, int held, int band)
        {
            if (held <= 0 || stId == null || dest == null) return;
            lock (Gate)
            {
                Roll(now);
                if (!HeldPeak.TryGetValue(stId, out Dictionary<string, int> peaks)) HeldPeak[stId] = peaks = new Dictionary<string, int>(StringComparer.Ordinal);
                peaks.TryGetValue(dest, out int peak);
                if (held <= peak) return;
                peaks[dest] = held; _ver++;
                Left(now, stId, stName, trId, trName, Held, band, held - peak);
            }
        }

        // A station's riders who gave up waiting (the game's four-hour rule), by fare band (index 0: trip unknown); `total` is the game's own count.
        internal static void GaveUpAt(double now, string stId, string stName, int[] bands, int total)
        {
            if (total <= 0) return;
            lock (Gate)
            {
                Roll(now);
                int sum = 0;
                if (bands != null) for (int i = 0; i < Bands; i++) { if (bands[i] > 0) { Left(now, stId, stName, null, null, GaveUp, i, bands[i]); sum += bands[i]; } }
                if (total > sum) Left(now, stId, stName, null, null, GaveUp, 0, total - sum);
            }
        }

        // ---- the feed ----

        private static double Round(double v) => Math.Round(v, 2);

        private static Dictionary<string, object> ByClass(int[] v) => new Dictionary<string, object> { { "coach", v[0] }, { "first", v[1] }, { "sleeper", v[2] } };
        private static Dictionary<string, object> ByClass(double[] v) => new Dictionary<string, object> { { "coach", Round(v[0]) }, { "first", Round(v[1]) }, { "sleeper", Round(v[2]) } };

        private static List<object> BandList(int[] b)
        {
            var l = new List<object>(Bands);
            for (int i = 0; i < Bands; i++) l.Add(b[i]);
            return l;
        }

        private static void Put(Dictionary<string, object> into, Tally t, bool withPaid)
        {
            into["boarded"] = ByClass(t.Boarded);
            into["gameFare"] = ByClass(t.Game);
            into["extra"] = new Dictionary<string, object> { { "first", Round(t.Extra[1]) }, { "sleeper", Round(t.Extra[2]) } };
            into["heatFare"] = ByClass(t.Heat);
            into["bagFees"] = Round(t.BagFees);
            if (withPaid) into["paid"] = Round(t.Paid);
            into["left"] = new Dictionary<string, object> { { "noRoom", t.Left[NoRoom] }, { "noBerth", t.Left[NoBerth] }, { "heldForBags", t.Left[Held] }, { "gaveUp", t.Left[GaveUp] } };
            into["bands"] = new Dictionary<string, object> { { "noRoom", BandList(t.Band[NoRoom]) }, { "noBerth", BandList(t.Band[NoBerth]) }, { "heldForBags", BandList(t.Band[Held]) }, { "gaveUp", BandList(t.Band[GaveUp]) } };
        }

        private static List<object> Rows(List<Row> rows, bool withPaid)
        {
            var l = new List<object>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                var d = new Dictionary<string, object> { { "id", r.Id }, { "name", r.Name } };
                Put(d, r.T, withPaid);
                l.Add(d);
            }
            return l;
        }

        // A day as the feed shows it; `day` null is a day with nothing recorded.
        private static Dictionary<string, object> View(Day day, long number, bool known, bool partial, bool unavailable)
        {
            var d = new Dictionary<string, object> { { "day", number + 1 }, { "known", known }, { "partial", partial } };
            var total = new Dictionary<string, object>();
            Put(total, day == null ? EmptyTally : day.Total, true);
            d["total"] = total;
            d["stations"] = day == null ? new List<object>() : Rows(day.Stations, true);
            d["trains"] = day == null ? new List<object>() : Rows(day.Trains, false);
            d["stationsFull"] = day != null && day.StationsFull;
            d["trainsFull"] = day != null && day.TrainsFull;
            d["unavailable"] = unavailable;
            return d;
        }

        private static readonly Tally EmptyTally = new Tally();

        // The answer is built once and handed out again until something changes: a figure recorded, a roll, a reset, a stand-down, the clock's day or
        // the reader's role (the key below). The objects returned are shared between reads and are never changed after they are built, so a reader
        // must treat them as read-only (FEEDS.md says so). A read with nothing changed allocates nothing.
        private static long _cVer = -1, _cDay;
        private static int _cMode = -1;
        private static object _cToday, _cYesterday;

        // Today and yesterday as of `nowSeconds`, without changing the book (a read never rolls the day). Not the host (the boarding and
        // the fares are the host's): both days are unknown, with nothing in them. `unavailable` says the book is down on this computer.
        internal static void Read(double nowSeconds, bool host, out object today, out object yesterday)
        {
            lock (Gate)
            {
                bool bad = !host || Off || double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds);
                long d = double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds) ? -1 : DayOf(nowSeconds);
                // the shape of the answer: 0 nothing known; 1 today as recorded; 2 a load not yet recorded; 3 the day after the last recorded; 4 a gap of days. Bit 4: the book is down.
                int mode = bad ? 0 : (_cur == None || d < _cur) ? 2 : d == _cur ? 1 : d == _cur + 1 ? 3 : 4;
                bool down = Off;
                if (down) mode |= 16;
                if (_cVer == _ver && _cMode == mode && _cDay == d) { today = _cToday; yesterday = _cYesterday; return; }
                if (mode == 0 || mode == 16) { today = View(null, d, false, false, down); yesterday = View(null, d - 1, false, false, down); }
                else switch (mode & 15)
                {
                    case 2: today = View(null, d, true, true, down); yesterday = View(null, d - 1, false, false, down); break;
                    case 1: today = View(_today, _today.Number, _today.Known, _today.Partial, down); yesterday = View(_yesterday, _yesterday.Number, _yesterday.Known, _yesterday.Partial, down); break;
                    case 3: today = View(null, d, true, false, down); yesterday = View(_today, _today.Number, _today.Known, _today.Partial, down); break;
                    default: today = View(null, d, true, false, down); yesterday = View(null, d - 1, true, false, down); break;
                }
                _cVer = _ver; _cMode = mode; _cDay = d; _cToday = today; _cYesterday = yesterday;
            }
        }

        // For the checks: the stored day without going through the feed.
        internal static Day TodayBook => _today;
        internal static Day YesterdayBook => _yesterday;
    }
}
