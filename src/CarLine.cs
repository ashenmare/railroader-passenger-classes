using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.Ops;
using UI.Builder;
using UI.CarInspector;

namespace PassengerClasses
{
    // The Fare line (0.2.0): one row on the game's own Passenger tab of a passenger car's window saying what the car's fare is made of now, e.g. "Sleeper +250%,
    // heated +4%". Optional, like the station columns: its own resolve, its own latch, and a game version without a member it needs just has no row.
    // It is the host's own row (the figures are the host's: the fare settings, the class of each model and the heat the boarding and fares run on, as the station
    // columns are); a joined client has the game's window as it is. Nothing is saved, nothing is polled: the row is one of the game's Periodic fields, read while the
    // window shows it, and the words are rebuilt only when a figure in them changes (Receipt.FareLine).
    internal static class CarLine
    {
        internal static volatile bool Live;
        internal static bool Off;                   // set at load when a member is missing or the hook won't go on
        private static bool _down;                  // stood down (three failures); reset on world load
        private static int _failures;
        private static FieldInfo _carF;

        // The row's two moves, for the check: when set they stand in for the game's builder (a window needs the game's prefab).
        internal static Action<string, Func<string>> RowSeam = null;

        internal static void ResetWorld() { _failures = 0; _down = false; Receipt.ForgetLine(); }

        // CarInspector.PopulatePassengerCarPanel(UIPanelBuilder builder) is a private instance void method that builds the Passenger tab of a passenger car's window (the first row is Passengers); the window's car is its private field _car [load]
        // UIPanelBuilder.AddField(string, Func<string>, UIPanelBuilder.Frequency) adds a label row whose text the game re-reads on its own periodic update (Frequency.Periodic), so the row follows without a rebuild [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve() => ResolveOn(typeof(CarInspector));

        // `window` is a parameter so tools/gamecheck can ask it of a type that has no such method.
        internal static string ResolveOn(Type window)
        {
            MethodInfo m = AccessTools.Method(window, "PopulatePassengerCarPanel", new[] { typeof(UIPanelBuilder) });
            if (m == null || m.IsStatic || m.ReturnType != typeof(void) || m.GetParameters()[0].Name != "builder") return "CarInspector.PopulatePassengerCarPanel";
            _carF = window.GetField("_car", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_carF == null || _carF.FieldType != typeof(Car)) return "CarInspector._car";
            if (typeof(UIPanelBuilder).GetMethod("AddField", new[] { typeof(string), typeof(Func<string>), typeof(UIPanelBuilder.Frequency) }) == null) return "UIPanelBuilder.AddField";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(CarInspector), "PopulatePassengerCarPanel", new[] { typeof(UIPanelBuilder) }), prefix: new HarmonyMethod(typeof(CarLine), nameof(Prefix)));
        }

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "car-window", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("carline-" + what, "Passenger Classes: " + what + " failed, the car window stands as the game made it: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && !_down) { _down = true; Main.WarnOnce("carline-down", "Passenger Classes: the Fare line stands down until the next world load"); }
        }

        internal static bool Active() => Live && !Off && !_down && Logic.FareLine && Boarding.Live && Boarding.IsHostNow();

        // Before the game builds the Passenger tab: the Fare row goes in first, above Passengers. A passenger car the mod gives no class (none) gets no row.
        internal static void Prefix(CarInspector __instance, UIPanelBuilder builder)
        {
            if (!Live) return;
            try
            {
                if (!Active()) return;
                Car car = _carF.GetValue(__instance) as Car;
                if (car == null || !HasLine(car)) return;
                Func<string> read = () => Read(car);
                if (RowSeam != null) { RowSeam("Fare", read); return; }
                builder.AddField("Fare", read, UIPanelBuilder.Frequency.Periodic);
            }
            catch (Exception e) { Failed("adding the Fare line", e); }
        }

        // The row's text each time the game asks: blank when the setting was switched off or the mod stood down meanwhile; never throws.
        private static string Read(Car car)
        {
            try { return Active() ? TextFor(car) : ""; }
            catch (Exception e) { Failed("reading the Fare line", e); return ""; }
        }

        // A car has the line when it is a passenger car the mod classes (coach, parlor, sleeper or lounge). A car with no class has none.
        internal static bool HasLine(Car car)
        {
            Classes.Resolved m = Boarding.ModelOf(car);
            return m != null && m.Cls != Classes.None && car.IsPassengerCar();
        }

        // What the car's fare is made of now: its class (and that class's fare against the coach), the heat when it moves the fare (or the included heat in the cold
        // months), and the observation bonus when its train has one. The same decisions the fare makes (Fares.cs): Boarding's class of the car, Heat.Percent, the
        // observation walk of the train. Receipt.FareLine words it.
        internal static string TextFor(Car car)
        {
            Classes.Resolved m = Boarding.ModelOf(car);
            if (m == null) return "";
            int cls = m.BoardClass;
            int fare = cls == 2 ? Logic.SleeperFarePercent : cls == 1 ? Logic.FarePercent : 100;
            double pct = Heat.Percent(car.id, cls, out bool heated, out double _, out double inside);
            int hp = (int)Math.Round(pct, MidpointRounding.AwayFromZero);
            int kind = hp == 0 ? 0 : hp > 0 ? 1 : 2;       // the sign says it: a heated car can be cold inside, and the binary rule gives each sign its own kind anyway
            if (kind == 0 && Heat.IncludedWords() != null) kind = 3;
            int shown = double.IsNaN(inside) || kind == 0 ? Receipt.NoTemp : (int)Math.Round(Math.Max(-999, Math.Min(999, inside)), MidpointRounding.AwayFromZero);
            return Receipt.FareLine(cls, fare, kind, hp, Observation.BonusPercent(car), shown);
        }
    }
}
