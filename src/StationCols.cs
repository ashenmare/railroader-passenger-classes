using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Core;
using Game;
using HarmonyLib;
using Model.Ops;
using UI.Builder;

namespace PassengerClasses
{
    // The station agent window's Passengers tab with two more columns: Count | First | Sleeper | Destination | Longest Wait. First and Sleeper are
    // parts of Count (riders who want first class, riders who want a sleeper), the figures the feed's stations rows carry.
    //
    // The route: a prefix on StationWindow.BuildPassengerTab that builds the tab itself, the same controls in the same order with the same
    // widths, plus the two columns, and skips the game's own. Chosen over a transpiler because the lambdas the compiler makes of that method are
    // named and numbered by the compiler, and a wrong instruction would show only as an exception when a player opens the window; this is ordinary
    // C# checked against the game's own types. Station Upgrades puts its section on the same method with a postfix, and a postfix runs whether or
    // not a prefix skipped the original, so its section stays where it was. The prefix takes over only when it has something to add: the host,
    // the mod running, a first-class or sleeper share above 0, and the game's own method unchanged (a fingerprint of its instructions taken at
    // load). Otherwise the game builds its own table. The hook is optional: a game version that lacks a member or differs in that method turns
    // this off and nothing else (one warning).
    internal static class StationCols
    {
        internal static volatile bool Live;
        internal static bool Off;                 // set at load: the hook isn't on (a member missing, a different method, a patch that wouldn't go on)
        private static int _failures;
        private static bool _down;                // three failures: the game's own table until the next world load
        // Test seam (tools/gamecheck): the tab needs the game's UI prefab; when set it is called instead of the builder.
#pragma warning disable CS0649
        internal static Action<PassengerStop> BuildSeam;
#pragma warning restore CS0649
        // The fingerprint of BuildPassengerTab and the compiler's lambdas of it; 0.6.2's and 0.6.3's are the same (tools/gamecheck ST2 proves it on the real 0.6.3 dll).
        internal const string Known = "ebd11cb9a732739a";

        internal static void ResetWorld() { _failures = 0; _down = false; }

        // StationWindow.BuildPassengerTab(UIPanelBuilder, PassengerStop) is a private instance void method: one VScrollView that rebuilds every second, a heading, then the Count | Destination | Longest Wait table; Station Upgrades' postfix on it adds its own section after [load]
        // PassengerStop.NameForIdentifier(string) is public static; GameDateTime.IntervalString(GameDateTime, Style) is the game's extension; TimeWeather.Now is a static GameDateTime; WaitingPassengerGroup has Origin, Boarded [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve() => ResolveOn(typeof(UI.StationWindow.StationWindow), Known);

        // The same test on the type it is handed and the fingerprint it expects (the game-code checks hand it a wrong one).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string ResolveOn(Type window, string known)
        {
            MethodInfo m = AccessTools.Method(window, "BuildPassengerTab");
            if (m == null || m.IsStatic || m.ReturnType != typeof(void)) return "StationWindow.BuildPassengerTab";
            ParameterInfo[] p = m.GetParameters();
            if (p.Length != 2 || p[0].ParameterType != typeof(UIPanelBuilder) || p[1].ParameterType != typeof(PassengerStop) || p[0].Name != "builder" || p[1].Name != "passengerStop")
                return "StationWindow.BuildPassengerTab (parameters)";
            if (AccessTools.Method(typeof(PassengerStop), "NameForIdentifier", new[] { typeof(string) }) == null) return "PassengerStop.NameForIdentifier";
            if (typeof(TimeWeather).GetProperty("Now") == null) return "TimeWeather.Now";
            PropertyInfo ts = typeof(GameDateTime).GetProperty("TotalSeconds"), dn = typeof(PassengerStop).GetProperty("DisplayName");
            if (ts == null || ts.PropertyType != typeof(double)) return "GameDateTime.TotalSeconds";
            if (dn == null || dn.PropertyType != typeof(string)) return "PassengerStop.DisplayName";
            if (typeof(WaitingPassengerGroup).GetField("Origin") == null || typeof(WaitingPassengerGroup).GetField("Boarded") == null) return "WaitingPassengerGroup";
            if (typeof(Game.GameDateTimeInterval).GetMethod("IntervalString") == null) return "GameDateTime.IntervalString";
            string now = FingerprintOf(window);
            if (now != known) return "StationWindow.BuildPassengerTab (the game's method is not the one this was made for, " + now + ")";
            return null;
        }

        // BuildPassengerTab and the lambdas the compiler made of it (their names carry its name), by name.
        internal static string FingerprintOf(Type window)
        {
            var list = new List<MethodBase>();
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MethodBase m in window.GetMethods(all)) if (m.Name.Contains("BuildPassengerTab")) list.Add(m);
            foreach (Type n in window.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (MethodBase m in n.GetMethods(all)) if (m.Name.Contains("BuildPassengerTab")) list.Add(m);
            list.Sort((x, y) => string.CompareOrdinal(x.DeclaringType.Name + "::" + x.Name, y.DeclaringType.Name + "::" + y.Name));
            return Fingerprint.Of(list);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(UI.StationWindow.StationWindow), "BuildPassengerTab"), prefix: new HarmonyMethod(typeof(StationCols), nameof(Prefix)));
        }

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "station-window", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("station-" + what, "Passenger Classes: " + what + " failed, the game's own station table stands: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && !_down) { _down = true; Main.WarnOnce("station-down", "Passenger Classes: the First and Sleeper columns stand down until the next world load"); }
        }

        // True when the columns have something to show: this is the host (the crowds are the host's), the mod is running, and a first-class or sleeper
        // share is set. A client, or a game with both shares at 0, gets the game's own table.
        internal static bool Active()
        {
            if (!Live || Off || _down || !Boarding.Live) return false;
            if (Logic.SharePercent <= 0 && Logic.SleeperSharePercent <= 0) return false;
            return Boarding.IsHostNow();
        }

        // The host's figures for one destination's crowd; false when they can't be read (the cells then show "-").
        private static bool Figures(PassengerStop stop, string dest, int total, out int fc, out int sl)
        {
            fc = 0; sl = 0;
            try { Boarding.StationFigures(stop, dest, total, out fc, out sl); return true; }
            catch (Exception e) { Failed("reading a crowd for the station window", e); return false; }
        }

        internal static bool Prefix(UIPanelBuilder builder, PassengerStop passengerStop)
        {
            if (!Live) return true;
            try
            {
                if (passengerStop == null || !Active()) return true;
                if (DebugLog.On) DebugLog.Event(Main.Id, "station-window", "stop", passengerStop.identifier, "columns", true);
                if (BuildSeam != null) { BuildSeam(passengerStop); return false; }
                BuildTab(builder, passengerStop);
                return false;
            }
            catch (Exception e) { Failed("building the station window's passenger table", e); return true; }
        }

        // The table's controls as data, so the check can hold them against the game's own instructions: the header texts and the cell widths in order
        // (0 = no width, the last cell takes what is left). The game's table is Count, Destination, Longest Wait; ours puts First and Sleeper after Count.
        internal static readonly string[] HeadGame = { "<b>Count</b>", "<b>Destination</b>", "<b>Longest Wait</b>" };
        internal static readonly float[] WidthGame = { 80f, 200f, 0f };
        internal static readonly string[] HeadCols = { "<b>Count</b>", "<b>First</b>", "<b>Sleeper</b>", "<b>Destination</b>", "<b>Longest Wait</b>" };
        internal static readonly float[] WidthCols = { 80f, 60f, 70f, 200f, 0f };

        // One row's cells in order, matching the header it goes under.
        internal static string[] RowCells(bool cols, string count, string first, string sleeper, string dest, string longest) =>
            cols ? new[] { count, first, sleeper, dest, longest } : new[] { count, dest, longest };

        private static void Cell(UIPanelBuilder b, string text, float width)
        {
            var r = b.AddLabel(text);
            if (width > 0f) r.Width(width);
        }

        // The game's own table (StationWindow.BuildPassengerTab, 0.6.2 and 0.6.3): the same heading, header and rows, the Longest Wait figure the game's own. The
        // closure runs again every second (RebuildOnInterval), so it checks again each time whether the columns still apply.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BuildTab(UIPanelBuilder builder, PassengerStop passengerStop)
        {
            builder.VScrollView(delegate (UIPanelBuilder scroll)
            {
                scroll.RebuildOnInterval(1f);
                IReadOnlyDictionary<string, PassengerStop.WaitingInfo> waiting = passengerStop.Waiting;
                bool none = waiting.Count == 0;
                int number = 0;
                foreach (KeyValuePair<string, PassengerStop.WaitingInfo> kv in waiting) number += kv.Value.Total;
                string text = none ? "no passengers" : number.Pluralize("passenger");
                scroll.AddLabel(passengerStop.DisplayName + " has " + text + " waiting.");
                scroll.Spacer(8f);
                if (none) return;
                bool cols = Active();
                string[] head = cols ? HeadCols : HeadGame;
                float[] widths = cols ? WidthCols : WidthGame;
                scroll.HStack(delegate (UIPanelBuilder h)
                {
                    for (int i = 0; i < head.Length; i++) Cell(h, head[i], widths[i]);
                });
                List<string[]> table = TableRows(passengerStop, cols, TimeWeather.Now, out bool total);
                for (int r = 0; r < table.Count; r++)
                {
                    string[] cells = table[r];
                    if (total && r == table.Count - 1) scroll.AddHRule();
                    scroll.HStack(delegate (UIPanelBuilder row)
                    {
                        for (int i = 0; i < cells.Length; i++) Cell(row, cells[i], widths[i]);
                    });
                }
            });
        }

        // The table's rows below the header, as text (the drawing is BuildTab's): one per destination with someone waiting, then, with the columns on and
        // two or more of them, the Total row (total = true; BuildTab sets a rule above it).
        internal static List<string[]> TableRows(PassengerStop stop, bool cols, GameDateTime now, out bool total)
        {
            var table = new List<string[]>();
            int rows = 0, sumCount = 0, sumFc = 0, sumSl = 0;
            bool figsOk = true;
            var shown = new List<PassengerStop.WaitingInfo>();
            foreach (KeyValuePair<string, PassengerStop.WaitingInfo> item in stop.Waiting)
            {
                if (item.Value.Total <= 0) continue;
                string first = null, sleeper = null;
                if (cols)
                {
                    if (Figures(stop, item.Key, item.Value.Total, out int fc, out int sl)) { first = $"{fc}"; sleeper = $"{sl}"; sumFc += fc; sumSl += sl; }
                    else { first = "-"; sleeper = "-"; figsOk = false; }
                    rows++; sumCount += item.Value.Total;
                    shown.Add(item.Value);
                }
                table.Add(RowCells(cols, $"{item.Value.Total}", first, sleeper, PassengerStop.NameForIdentifier(item.Key), LongestWait(item.Value, stop.identifier, now)));
            }
            total = cols && ShowTotal(rows);
            if (total) table.Add(TotalCells(sumCount, sumFc, sumSl, figsOk, TotalWait(shown, stop.identifier, now)));
            return table;
        }

        // A Total row closes the list when there are two or more destinations (one row would only say itself again).
        internal static bool ShowTotal(int rows) => rows >= 2;

        // The Total row's cells, bold, under the same widths: the sums, "Total" in the Destination column, the longest of the rows' waits
        // ("N/A" when none of them has one, as the game's rows). A "-" in First and Sleeper when any row's figures could not be read.
        internal static string[] TotalCells(int count, int first, int sleeper, bool figsOk, string longest) =>
            RowCells(true, "<b>" + count + "</b>", figsOk ? "<b>" + first + "</b>" : "<b>-</b>", figsOk ? "<b>" + sleeper + "</b>" : "<b>-</b>", "<b>Total</b>",
                "<b>" + longest + "</b>");

        // The oldest boarding time among the groups that did not start here; false when there is none (the game reads that as "N/A").
        internal static bool Oldest(PassengerStop.WaitingInfo waiting, string stopId, out GameDateTime oldest)
        {
            oldest = new GameDateTime(3.4028234663852886E+38);
            GameDateTime newest = GameDateTime.Zero;
            foreach (WaitingPassengerGroup group in waiting.Groups)
            {
                if (!(group.Origin == stopId))
                {
                    if (group.Boarded < oldest) oldest = group.Boarded;
                    if (group.Boarded > newest) newest = group.Boarded;
                }
            }
            return newest.TotalSeconds != 0.0;
        }

        // The Total row's Longest Wait: the longest of the rows' own (transfers only), "N/A" when no row has one.
        internal static string TotalWait(IEnumerable<PassengerStop.WaitingInfo> rows, string stopId, GameDateTime now)
        {
            bool any = false;
            GameDateTime oldestAll = new GameDateTime(3.4028234663852886E+38);
            foreach (PassengerStop.WaitingInfo w in rows)
                if (Oldest(w, stopId, out GameDateTime o) && o < oldestAll) { oldestAll = o; any = true; }
            return any ? (oldestAll.IntervalString(now, GameDateTimeInterval.Style.Short) ?? "") : "N/A";
        }

        // The game's Longest Wait cell (0.6.2 and 0.6.3 read it the same way): the oldest boarding time of the groups that did not start here, as the
        // game's short interval from now; "N/A" when there is none (riders who began at this stop do not count, so a station of its own riders reads N/A).
        internal static string LongestWait(PassengerStop.WaitingInfo waiting, string stopId, GameDateTime now) =>
            Oldest(waiting, stopId, out GameDateTime oldest) ? (oldest.IntervalString(now, GameDateTimeInterval.Style.Short) ?? "") : "N/A";
    }
}
