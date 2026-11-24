using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.Ops;

namespace PassengerClasses
{
    // The observation bonus for every car the mod calls observation. The game pays a train's riders 1.2 times the fare when the first
    // or last car of the whole coupled train has the exact type code PBO with its platform end free (PassengerStop.CalculateBonusMultiplier,
    // read once per car per step). This replaces the game's answer with the same test run on the model's class: an observation car at an
    // end, its platform end uncoupled, gives the bonus setting (default the game's 1.2); anything else gives 1. A model the player
    // turned observation off for (the stock PBO included) gives none; a model the mod calls observation by its type code or a pack's
    // file (PO, PPO) gives it. The end test is the game's own: the car's end that faces the train's end must not be the car's F end.
    internal static class Observation
    {
        internal static volatile bool Live;
        private static int _failures;

        internal static void ResetWorld() { _failures = 0; }

        // What the debug log last said for each car, so a car's bonus is written when it changes (the game asks every car every step).
        private static readonly System.Collections.Generic.Dictionary<string, float> BonusMem = new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);
        internal static void DebugReset() { BonusMem.Clear(); }

        // PassengerStop.CalculateBonusMultiplier(Car) is private static, float: 1.2 when the first or last car of the coupled train is exactly type PBO, its LogicalToEnd(the train-facing logical end) is not F and nothing is coupled there; the result is applied to every rider's fare at drop-off [load]
        // Car.CoupledTo(LogicalEnd) returns the neighbour car or null; the train's first car is the one reached by following A until null, its last by following B (IntegrationSet.GetCouplerConnection, the same order EnumerateCoupled walks); Car.LogicalToEnd(LogicalEnd) says which physical end [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            MethodInfo m = AccessTools.Method(typeof(PassengerStop), "CalculateBonusMultiplier", new[] { typeof(Car) });
            if (m == null || !m.IsStatic || m.ReturnType != typeof(float)) return "PassengerStop.CalculateBonusMultiplier";
            if (typeof(Car).GetMethod("CoupledTo", new[] { typeof(Car.LogicalEnd) }) == null || typeof(Car).GetMethod("LogicalToEnd", new[] { typeof(Car.LogicalEnd) }) == null) return "Car.CoupledTo";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(PassengerStop), "CalculateBonusMultiplier", new[] { typeof(Car) }), postfix: new HarmonyMethod(typeof(Observation), nameof(BonusPostfix)));
        }

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "observation", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("obs-" + what, "Passenger Classes: " + what + " failed, the game's own bonus stands: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && Live)
            {
                Live = false;
                Main.WarnOnce("obs-down", "Passenger Classes: the observation bonus stands down until the next world load");
            }
        }

        internal static void BonusPostfix(Car car, ref float __result)
        {
            if (!Live) return;
            try
            {
                float game = __result;
                __result = Bonus(car, out Car first, out Car last);
                if (DebugLog.On) LogBonus(car, first, last, game, __result);
            }
            catch (Exception e) { Failed("working out the observation bonus", e); }
        }

        // The bonus for this car's train: the setting when an observation car with its platform end free is at either end of the whole coupled train, else 1.
        private static float Bonus(Car car, out Car first, out Car last)
        {
            first = car; last = car;
            Car next;
            int guard = 0;
            while (guard++ < 10000 && (next = first.CoupledTo(Car.LogicalEnd.A)) != null) first = next;
            guard = 0;
            while (guard++ < 10000 && (next = last.CoupledTo(Car.LogicalEnd.B)) != null) last = next;
            return End(first, Car.LogicalEnd.A) || End(last, Car.LogicalEnd.B) ? Logic.ObservationBonus(Logic.ObservationPercent) : 1f;
        }

        // The bonus this car's riders get, as a signed whole percent over the fare (20 for the game's 1.2; 0 for none), for the car window's Fare line. 0 while the
        // hook is not live (the game's own bonus is then in force and not worked out here).
        internal static int BonusPercent(Car car) => !Live ? 0 : (int)Math.Round((Bonus(car, out Car _, out Car _) - 1f) * 100f, MidpointRounding.AwayFromZero);

        // The debug log's line for a car's bonus: the game's own answer, the mod's, and for each end of the train whether an observation car
        // with its platform end free is there. Written when the car's answer changes.
        private static void LogBonus(Car car, Car first, Car last, float game, float result)
        {
            if (BonusMem.TryGetValue(car.id, out float seen) && seen == result) return;
            BonusMem[car.id] = result;
            Classes.Resolved hm = Boarding.ModelOf(first), tm = Boarding.ModelOf(last);
            DebugLog.Event(Main.Id, "bonus", "car", car.id, "game", game, "bonus", result, "percent", Logic.ObservationPercent,
                           "head", End(first, Car.LogicalEnd.A), "tail", End(last, Car.LogicalEnd.B),
                           "headModel", first.DefinitionInfo?.Identifier, "tailModel", last.DefinitionInfo?.Identifier,
                           "headObs", hm != null && hm.Obs, "tailObs", tm != null && tm.Obs,
                           "headFree", first.CoupledTo(Car.LogicalEnd.A) == null, "tailFree", last.CoupledTo(Car.LogicalEnd.B) == null,
                           "headF", first.LogicalToEnd(Car.LogicalEnd.A) == Car.End.F, "tailF", last.LogicalToEnd(Car.LogicalEnd.B) == Car.End.F,
                           "headOtherEnd", hm != null && hm.AtOtherEnd, "tailOtherEnd", tm != null && tm.AtOtherEnd);
        }

        // An observation car at this end of the train with its platform end free.
        private static bool End(Car end, Car.LogicalEnd logical)
        {
            Classes.Resolved m = Boarding.ModelOf(end);
            if (m == null || !m.Obs) return false;
            return Logic.PlatformEndFree(end.LogicalToEnd(logical) == Car.End.F, end.CoupledTo(logical) == null, m.AtOtherEnd);
        }
    }
}
