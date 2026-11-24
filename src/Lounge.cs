using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game;
using HarmonyLib;
using Model;
using Model.Ops;

namespace PassengerClasses
{
    // The lounge (switched off by default; Logic.LoungePatience). The game drops a rider who changes trains and waits on a platform more than 4 hours
    // after they boarded (PassengerExpiration, ExpirationTimeInGameHours; the platform group keeps the boarding time the car gave it). A rider
    // who gets off a train with a lounge-class car in it, at a stop that is not their destination, is put on the platform with that time moved on by
    // the lounge hours (default 1, so 5 hours), never past now. It moves a time stamp the game reads, so it adds to whatever else moves one: Patient
    // Passengers credits the stamp on the car, Station Upgrades changes what the game reads; each adds its own to this hour.
    internal static class Lounge
    {
        internal static volatile bool Live;
        private static int _failures;

        internal static void ResetWorld() { _failures = 0; }

        // PassengerStop.OffsetWaiting(string destination, string origin, GameDateTime sourceGroupBoarded, int delta) is private, bool; UnloadOne calls it with delta 1 and the car's Boarded for a rider getting off at a stop that is not their destination, and nothing else in LoadUnloadCar adds riders to the platform [load]
        // GameDateTime is a struct with AddingHours(float), the comparison operators and a subtraction in seconds; TimeWeather.Now is the game clock (read in ClockNow, inside the hook's try) [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            MethodInfo m = AccessTools.Method(typeof(PassengerStop), "OffsetWaiting", new[] { typeof(string), typeof(string), typeof(GameDateTime), typeof(int) });
            if (m == null || m.ReturnType != typeof(bool)) return "PassengerStop.OffsetWaiting";
            ParameterInfo[] p = m.GetParameters();
            if (p[2].Name != "sourceGroupBoarded" || p[3].Name != "delta") return "PassengerStop.OffsetWaiting (parameters)";
            if (typeof(GameDateTime).GetMethod("AddingHours", new[] { typeof(float) }) == null) return "GameDateTime.AddingHours";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(PassengerStop), "OffsetWaiting", new[] { typeof(string), typeof(string), typeof(GameDateTime), typeof(int) }), prefix: new HarmonyMethod(typeof(Lounge), nameof(OffsetPrefix)));
        }

        // The game clock, read through a NoInlining trampoline inside the try above: a game without TimeWeather.Now fails here, counted, and never reaches the game.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static GameDateTime ClockNow() => TimeWeather.Now;

        internal static void OffsetPrefix(ref GameDateTime sourceGroupBoarded, int delta)
        {
            if (!Live || !Logic.LoungePatience || Logic.LoungeHours <= 0 || delta <= 0) return;
            Car car = Boarding.Serving;
            if (car == null) return;                 // not while a car is being served (arrivals and the game's own thinning come through here too)
            try
            {
                if (!Baggage.TrainHasLounge(car))
                {
                    if (DebugLog.On) DebugLog.Event(Main.Id, "lounge", "car", car.id, "lounge", false, "hours", 0, "capped", false);
                    return;
                }
                GameDateTime later = sourceGroupBoarded.AddingHours(Logic.LoungeHours);
                GameDateTime now = ClockNow();
                bool capped = later > now;
                if (capped) later = now > sourceGroupBoarded ? now : sourceGroupBoarded;
                if (DebugLog.On) DebugLog.Event(Main.Id, "lounge", "car", car.id, "lounge", true, "hours", Logic.LoungeHours, "capped", capped);
                sourceGroupBoarded = later;
            }
            catch (Exception e)
            {
                _failures++;
                if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "lounge", "what", "the lounge rule", "type", e.GetType().Name, "n", _failures);
                Main.WarnOnce("lounge", "Passenger Classes: the lounge rule failed, riders keep the game's patience: " + PathClean.CleanText(e.Message));
                if (_failures >= 3 && Live) { Live = false; Main.WarnOnce("lounge-down", "Passenger Classes: the lounge rule stands down until the next world load"); }
            }
        }
    }
}
