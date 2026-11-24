using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.AI.SpeedControl.Rollout;

namespace PassengerClasses
{
    // The coach brake for every car this mod calls a passenger or baggage car. The game gives a car 0.9 of its empty weight as nominal
    // braking only when its type code starts with PB (coach) or CB (baggage); every other freight-like type gets 0.7, so a pack's
    // PC, CA, PPO or BE cars brake a fifth weaker than the stock coaches. Two places do the sum and both are matched: the car itself
    // (Car.SetNominalBrakingRatio, run once when the car is set up, stored in a field) and the Auto Engineer's stopping plan
    // (RolloutPlantBuilder.NominalBrakingForceLbs). Locomotives (1.0), tenders (0.8) and cars the game already gives 0.9 are left alone.
    // A change of a model's class in the right-click menu runs the car's own setup again for the cars of that model (Reapply).
    internal static class Brakes
    {
        internal static volatile bool Live;
        private static int _failures;
        private static Action<Car> _set;

        internal static void ResetWorld() { _failures = 0; }

        // Car.SetNominalBrakingRatio() is private, void: it sets the private float nominalBrakingForce from the car type (locomotive 1.0, coach or baggage 0.9, tender 0.8, anything else 0.7) times Definition.WeightEmpty; Car setup calls it once [load]
        // RolloutPlantBuilder.NominalBrakingForceLbs(Car car) is private static, float: the same sum for the Auto Engineer's stopping plan [load]
        // CarType is a struct with public IsLocomotive, IsCoach, IsBaggage and IsTender (the type code's first letters); CarDefinition.WeightEmpty is an int [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            MethodInfo set = AccessTools.Method(typeof(Car), "SetNominalBrakingRatio", Type.EmptyTypes);
            if (set == null || set.ReturnType != typeof(void) || set.IsStatic) return "Car.SetNominalBrakingRatio";
            FieldInfo f = AccessTools.Field(typeof(Car), "nominalBrakingForce");
            if (f == null || f.FieldType != typeof(float)) return "Car.nominalBrakingForce";
            MethodInfo ro = AccessTools.Method(typeof(RolloutPlantBuilder), "NominalBrakingForceLbs", new[] { typeof(Car) });
            if (ro == null || ro.ReturnType != typeof(float) || !ro.IsStatic || ro.GetParameters()[0].Name != "car") return "RolloutPlantBuilder.NominalBrakingForceLbs";
            if (typeof(CarType).GetProperty("IsCoach") == null || typeof(CarType).GetProperty("IsBaggage") == null || typeof(CarType).GetProperty("IsLocomotive") == null || typeof(CarType).GetProperty("IsTender") == null) return "CarType.IsCoach";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            MethodInfo set = AccessTools.Method(typeof(Car), "SetNominalBrakingRatio", Type.EmptyTypes);
            _set = (Action<Car>)Delegate.CreateDelegate(typeof(Action<Car>), set);
            h.Patch(set, postfix: new HarmonyMethod(typeof(Brakes), nameof(CarPostfix)));
            h.Patch(AccessTools.Method(typeof(RolloutPlantBuilder), "NominalBrakingForceLbs", new[] { typeof(Car) }), postfix: new HarmonyMethod(typeof(Brakes), nameof(RolloutPostfix)));
        }

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "brakes", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("brake-" + what, "Passenger Classes: " + what + " failed, the game's own braking stands: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && Live)
            {
                Live = false;
                Main.WarnOnce("brake-down", "Passenger Classes: the coach brake stands down until the next world load");
            }
        }

        // The sum the game made took its 0.7 branch (no locomotive, coach, baggage or tender), and the mod calls the car a passenger or
        // baggage car: give it the coach figure.
        private static bool Wants(Car car)
        {
            CarType t = car.CarType;
            if (t.IsLocomotive || t.IsCoach || t.IsBaggage || t.IsTender) return false;
            Classes.Resolved m = Boarding.ModelOf(car);
            return m != null && m.CoachBrakes;
        }

        internal static void CarPostfix(Car __instance, ref float ___nominalBrakingForce)
        {
            if (!Live) return;
            try { if (Wants(__instance)) ___nominalBrakingForce = Logic.CoachBrake(__instance.Definition.WeightEmpty); }
            catch (Exception e) { Failed("setting a car's brakes", e); }
        }

        internal static void RolloutPostfix(Car car, ref float __result)
        {
            if (!Live) return;
            try { if (Wants(car)) __result = Logic.CoachBrake(car.Definition.WeightEmpty); }
            catch (Exception e) { Failed("planning a stop", e); }
        }

        // The same for every car: a setting that decides classes changed.
        internal static void ReapplyAll()
        {
            var shared = TrainController.Shared;
            if (shared == null || _set == null) return;
            foreach (Car c in shared.Cars) try { _set(c); } catch (Exception e) { Failed("changing a car's brakes", e); }
        }

        // Runs the car's own setup again for every car of one model: its brakes follow the model's class now.
        internal static void Reapply(string definitionId)
        {
            var shared = TrainController.Shared;
            if (shared == null || _set == null) return;
            foreach (Car c in shared.Cars)
                if (c.DefinitionInfo != null && c.DefinitionInfo.Identifier == definitionId)
                    try { _set(c); } catch (Exception e) { Failed("changing a car's brakes", e); }
        }
    }
}
