using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game;
using HarmonyLib;
using Model;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;

namespace PassengerClasses
{
    // Who sits in which car. The game loads each stopped passenger car, in no order, with the people waiting for the car's own
    // destinations (oldest group first, one or two a step); it has no idea of a class. This file adds one rule, a cascade: a sleeper car
    // takes only sleeper riders; a first-class (parlor) car takes first-class riders and the sleeper riders no free berth is held for;
    // coaches leave those riders alone while a sleeper or parlor car at the stop has room for them. Anyone else, and any rider with no
    // such car to board, boards as the game always did. Which car is which is Classes.cs. Sleeper riders are only those whose trip is
    // at least the setting's distance (Logic.SleeperShareFor).
    //
    // How it is hooked (five small patches on PassengerStop; every decision is in Logic.cs):
    //  - LoadUnloadCar (prefix and finalizer): says which car is being served, so the boarding below knows whose seat it is.
    //  - FindCars (postfix): the game's own list of the cars it is about to serve at this stop, kept for its first-class cars.
    //  - AllocateWaitingPassengersForDestinations (prefix and postfix): the boarding itself. The prefix narrows the destinations the
    //    game may pick from and caps how many it takes; the game's own body then runs unchanged. The postfix books who boarded.
    // Nothing is saved. The counts live here, per station and destination, and a world load starts them again from the share.
    //
    // Everything here names game types, so Main reaches it only through NoInlining trampolines inside a try.
    internal static class Boarding
    {
        internal static volatile bool Live;
        private static int _failures;

        private static Car _cur;            // the car LoadUnloadCar is serving
        private static int _curClass;       // 0 coach, 1 first class, 2 sleeper
        private static bool _narrowed;      // this boarding went through the prefix's narrowing
        private static bool _fcHere, _slHere;   // a first-class / sleeper car is at the stop being served

        // The seats a class's cars at a stop still have, shared across the destinations they take: a car's free seats are held back
        // at most once in all, however many destinations its marker names.
        private sealed class Budget
        {
            private readonly List<int> _free = new List<int>();
            private readonly List<HashSet<string>> _dests = new List<HashSet<string>>();
            internal void Clear() { _free.Clear(); _dests.Clear(); }
            internal void Add(int free, HashSet<string> dests) { _free.Add(free); _dests.Add(dests); }
            internal void AddDestinationsTo(HashSet<string> into)
            {
                for (int i = 0; i < _dests.Count; i++) foreach (string d in _dests[i]) into.Add(d);
            }
            internal int Avail(string d)
            {
                int n = 0;
                for (int i = 0; i < _free.Count; i++) if (_dests[i].Contains(d)) n += _free[i];
                return n;
            }
            internal void Take(string d, int n)
            {
                for (int i = 0; i < _free.Count && n > 0; i++)
                {
                    if (!_dests[i].Contains(d)) continue;
                    int t = Math.Min(n, _free[i]);
                    _free[i] -= t; n -= t;
                }
            }
        }
        private static readonly HashSet<string> Union = new HashSet<string>();
        private static readonly Budget FcSeats = new Budget(), SlBerths = new Budget();

        // What the feed shows: per station, the first-class and sleeper riders who found no seat or berth since the last train pulled in;
        // per premium car, the same since it last pulled into a station.
        private sealed class Visit { internal bool Present; internal int NoRoom, NoBerth; internal Dictionary<string, int> BagHeld; }
        private static readonly Dictionary<PassengerStop, Visit> Visits = new Dictionary<PassengerStop, Visit>();
        private static readonly Dictionary<string, int> NoRoomByCar = new Dictionary<string, int>();
        private static readonly List<Car> PrevScratch = new List<Car>();

        private static readonly Dictionary<PassengerStop, List<Car>> FcAt = new Dictionary<PassengerStop, List<Car>>();
        private static readonly Dictionary<PassengerStop, List<Car>> SlAt = new Dictionary<PassengerStop, List<Car>>();
        private static readonly Dictionary<PassengerStop, Dictionary<string, Logic.Crowd>> Crowds = new Dictionary<PassengerStop, Dictionary<string, Logic.Crowd>>();
        private static readonly HashSet<string> Narrow = new HashSet<string>();
        private static readonly Dictionary<string, int> TotalBefore = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> ResSlFor = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> ResFcFor = new Dictionary<string, int>();
        private static Func<PassengerStop, Car, int> _capacity;

        internal static void ResetWorld()
        {
            _failures = 0; _cur = null; _curClass = 0; _narrowed = false; _fcHere = false; _slHere = false;
            FcAt.Clear(); SlAt.Clear(); Crowds.Clear(); Narrow.Clear(); TotalBefore.Clear(); ResSlFor.Clear(); ResFcFor.Clear();
            Visits.Clear(); NoRoomByCar.Clear(); FcSeats.Clear(); SlBerths.Clear(); Union.Clear(); MilesByIds.Clear(); Baggage.ResetWorld();
            _leadFor = null; _leadId = null; _leadName = null; Daily.Reset();
            DebugReset();
        }

        // What the debug log remembers between lines (so a line is written when something changes, not every step): starts again at a world load,
        // a train pulling in, and any change of the switch.
        private static readonly Dictionary<string, string> RefusedMem = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> BagHeldMem = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> FitFor = new Dictionary<string, int>(StringComparer.Ordinal);
        private static int _dbgMax, _dbgCap;      // the game's own maximum for this boarding, and the cap the rule put on it
        internal static void DebugReset() { RefusedMem.Clear(); BagHeldMem.Clear(); Observation.DebugReset(); }

        // PassengerStop.LoadUnloadCar(Car, int) is private and serves one stopped passenger car: it unloads (each rider's fare goes through QueuePayment), then loads through LoadOne [load]
        // PassengerStop.FindCars(TrainController trainController, bool requireStopped = true) is private and returns the HashSet of the passenger cars at the stop (the stopped ones, or every one when requireStopped is false) and the cars coupled to them; CheckForCars calls it once a step with the default, HasWorkFor with requireStopped false [load]
        // PassengerStop.CarHasWork(Car) is private, bool, and its only caller is the public HasWorkFor (the Auto Engineer's hold at a stop): true when the car has riders to get off here, or names this stop, or has seats and TryFindLongestWaitingGroup finds a group waiting for the car's destinations; it never calls the boarding's Allocate, so a rider the mod would turn away still counts as work [load]
        // PassengerStop.AllocateWaitingPassengersForDestinations(int, HashSet<string>, out string, out string, out GameDateTime) is private; LoadOne is its only caller; it takes the oldest group among the destinations and returns the requested count (1 or 2, at most the maximum) [load]
        // PassengerStop.PassengerCapacity(Car) is private and returns the car's passenger seats [load]
        // PassengerStop.Waiting is a public IReadOnlyDictionary<string, WaitingInfo> (destination to groups), WaitingInfo.Total its public count [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            var ps = typeof(PassengerStop);
            if (!Has(ps, "LoadUnloadCar", typeof(void), typeof(Car), typeof(int))) return "PassengerStop.LoadUnloadCar";
            if (!Has(ps, "FindCars", typeof(HashSet<Car>), typeof(TrainController), typeof(bool))) return "PassengerStop.FindCars";
            if (!Has(ps, "CarHasWork", typeof(bool), typeof(Car))) return "PassengerStop.CarHasWork";
            MethodInfo al = AccessTools.Method(ps, "AllocateWaitingPassengersForDestinations");
            if (al == null || al.ReturnType != typeof(int)) return "PassengerStop.AllocateWaitingPassengersForDestinations";
            ParameterInfo[] p = al.GetParameters();
            if (p.Length != 5 || p[0].ParameterType != typeof(int) || p[1].ParameterType != typeof(HashSet<string>) || !p[2].ParameterType.IsByRef
                || !p[3].ParameterType.IsByRef || !p[4].ParameterType.IsByRef || p[4].ParameterType.GetElementType() != typeof(GameDateTime)
                || p[0].Name != "maximum" || p[1].Name != "destinations" || p[2].Name != "destinationOut")
                return "PassengerStop.AllocateWaitingPassengersForDestinations (parameters)";
            if (!Has(ps, "PassengerCapacity", typeof(int), typeof(Car))) return "PassengerStop.PassengerCapacity";
            MethodInfo mi = AccessTools.Method(ps, "TryCalculateMilesBetweenPassengerStops");
            if (mi == null || !mi.IsStatic || mi.ReturnType != typeof(bool)) return "PassengerStop.TryCalculateMilesBetweenPassengerStops";
            ParameterInfo[] mp = mi.GetParameters();
            if (mp.Length != 3 || mp[0].ParameterType != typeof(string) || mp[1].ParameterType != typeof(string) || !mp[2].ParameterType.IsByRef
                || mp[2].ParameterType.GetElementType() != typeof(PassengerStop.DistanceInfo)) return "PassengerStop.TryCalculateMilesBetweenPassengerStops (parameters)";
            if (typeof(PassengerStop.DistanceInfo).GetField("DistanceInMiles") == null || typeof(PassengerStop.DistanceInfo).GetField("Success") == null) return "PassengerStop.DistanceInfo";
            if (typeof(Car).GetField("id") == null || typeof(Car).GetField("id").FieldType != typeof(string)) return "Car.id";
            PropertyInfo w = ps.GetProperty("Waiting");
            if (w == null || w.PropertyType != typeof(IReadOnlyDictionary<string, PassengerStop.WaitingInfo>)) return "PassengerStop.Waiting";
            return null;
        }

        // The day book's own game members (CHECKS C22, C23, C24): resolved apart from the boarding's, so a game version that lacks one loses the day
        // book alone (Daily.Unavailable, the feed says so) and never the boarding or the fares. Null when all are there, else the first missing member.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string ResolveDaily() => ResolveDailyOn(typeof(PassengerStop), typeof(Car), typeof(TimeWeather), typeof(GameDateTime), typeof(GameDateTime));

        // The same test on the types it is handed (the game-code checks hand it stand-ins that lack a member; `clockReading` is the type that holds TotalSeconds).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string ResolveDailyOn(Type ps, Type car, Type timeWeather, Type gameDateTime, Type clockReading)
        {
            // PassengerStop.ExpirePassengers(GameDateTime expiration) is public, int: it removes the waiting groups whose origin is not this stop and whose boarding time is not after `expiration`, and returns how many riders that was; PassengerExpiration.Tick calls it every 60 s and on time advances [load]
            MethodInfo ex = AccessTools.Method(ps, "ExpirePassengers", new[] { gameDateTime });
            if (ex == null || ex.ReturnType != typeof(int) || ex.GetParameters()[0].Name != "expiration") return "PassengerStop.ExpirePassengers";
            PropertyInfo il = car.GetProperty("IsLocomotive"), dn = car.GetProperty("DisplayName"), tn = timeWeather.GetProperty("Now"), ts = clockReading.GetProperty("TotalSeconds");
            if (il == null || il.PropertyType != typeof(bool) || il.GetGetMethod() == null || !il.GetGetMethod().IsVirtual) return "Car.IsLocomotive";
            if (dn == null || dn.PropertyType != typeof(string)) return "Car.DisplayName";
            if (tn == null || tn.PropertyType != gameDateTime) return "TimeWeather.Now";
            if (ts == null || ts.PropertyType != typeof(double)) return "GameDateTime.TotalSeconds";
            return null;
        }

        private static bool Has(Type t, string name, Type ret, params Type[] args)
        {
            MethodInfo m = AccessTools.Method(t, name, args);
            return m != null && m.ReturnType == ret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            _capacity = (Func<PassengerStop, Car, int>)Delegate.CreateDelegate(typeof(Func<PassengerStop, Car, int>), AccessTools.Method(typeof(PassengerStop), "PassengerCapacity"));
            h.Patch(AccessTools.Method(typeof(PassengerStop), "LoadUnloadCar"), prefix: new HarmonyMethod(typeof(Boarding), nameof(ServePrefix)), finalizer: new HarmonyMethod(typeof(Boarding), nameof(ServeFinalizer)));
            h.Patch(AccessTools.Method(typeof(PassengerStop), "FindCars"), postfix: new HarmonyMethod(typeof(Boarding), nameof(FindCarsPostfix)));
            h.Patch(AccessTools.Method(typeof(PassengerStop), "CarHasWork"), postfix: new HarmonyMethod(typeof(Boarding), nameof(WorkPostfix)));
            MethodInfo al = AccessTools.Method(typeof(PassengerStop), "AllocateWaitingPassengersForDestinations");
            h.Patch(al, prefix: new HarmonyMethod(typeof(Boarding), nameof(AllocatePrefix)), postfix: new HarmonyMethod(typeof(Boarding), nameof(AllocatePostfix)));
        }

        // The day book's own hook (the 13th patch), applied apart from the boarding's: if it won't go on, the day book alone is off (Daily.Unavailable) and the
        // method is left as the game has it. True when it is on. FailDailyPatchForCheck is for the game-code checks: it makes the patch throw.
        internal static bool FailDailyPatchForCheck { get; set; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool PatchDaily(Harmony h)
        {
            MethodInfo m = null;
            try
            {
                m = AccessTools.Method(typeof(PassengerStop), "ExpirePassengers", new[] { typeof(GameDateTime) });
                if (FailDailyPatchForCheck) throw new InvalidOperationException("the check made this patch fail");
                h.Patch(m, prefix: new HarmonyMethod(typeof(Boarding), nameof(ExpirePrefix)), postfix: new HarmonyMethod(typeof(Boarding), nameof(ExpirePostfix)));
                return true;
            }
            catch (Exception e)
            {
                try { if (m != null) h.Unpatch(m, HarmonyPatchType.All, Main.HarmonyId); } catch (Exception) { }
                Main.WarnOnce("daily-patch", "Passenger Classes: the day figures for the feed are off (the riders-who-gave-up hook wouldn't go on: " + PathClean.CleanText(e.Message) + "); boarding and fares go on");
                if (DebugLog.On) DebugLog.Event(Main.Id, "daily-down", "reason", "patch-threw", "type", e.GetType().Name);
                return false;
            }
        }

        // The model's answer (Classes.cs), kept per definition id; nothing is allocated once a model has been seen.
        // Car.DefinitionInfo.Identifier is the car's definition id, Car.CarType its type (a struct that reads as its code text), Car.Definition.IsPassengerCar() the game's own passenger-slot test [load]
        internal static Classes.Resolved ModelOf(Car car)
        {
            var info = car?.DefinitionInfo;
            if (info == null) return null;
            string id = info.Identifier;
            if (Classes.TryCached(id, out Classes.Resolved r)) return r;
            return ModelOfDefinition(id, car.Definition);
        }

        // The model by its definition (a car isn't needed): worked out once, and its seats set in the definition when the player's class changes them. Every
        // reader of a car's seats (the boarding, the hover text, the car inspector, other mods) reads the rr/passengers slot of this one shared definition,
        // so the one write at the source is what they all see.
        // CarDefinition is one object shared by every car of a model, and its rr/passengers LoadSlot.MaximumCapacity (a settable float) is what PassengerStop.PassengerCapacity, PassengerExtensions.PassengerCountString (the hover text and the car inspector) and the other mods' seat reads use [load]
        internal static Classes.Resolved ModelOfDefinition(string id, CarDefinition def)
        {
            if (Classes.TryCached(id, out Classes.Resolved r)) return r;
            int now = SeatsOf(def);
            if (!Classes.Native.TryGetValue(id, out int native)) Classes.Native[id] = native = now;
            r = Classes.Get(id, def.CarType.ToString(), def.IsPassengerCar(), native, Baggage.SlotPounds(def));
            if (DebugLog.On) LogResolved(id, def, r, now);
            if (def.IsPassengerCar() && now != r.Seats)
            {
                SetSeats(def, r.Seats); Main.InfoOnce("seats-" + id + r.Seats, "Passenger Classes: every " + id + " now has " + r.Seats + " seats, from the class set for it");
                if (DebugLog.On) DebugLog.Event(Main.Id, "seats-set", "model", id, "from", now, "to", r.Seats, "native", r.NativeSeats, "class", Classes.Words[r.Cls]);
            }
            return r;
        }

        // The debug log's line for a model resolved: its class and where that came from (the player's choice, a pack's file, the built-in table
        // by the twin's id, the stock list or the type code, or the default), the seats it had and has.
        private static readonly string[] FromWords = "default,table,pack,player".Split(',');   // Classes.Resolved.ClsFrom, 0 to 3
        private static void LogResolved(string id, CarDefinition def, Classes.Resolved r, int seatsNow)
        {
            string via = r.ClsFrom == 1 ? (Logic.ClassOfId(id) != 0 ? "twin" : Classes.IsStock(id) ? "stock" : "type") : "-";
            DebugLog.Event(Main.Id, "class-resolved", "model", id, "type", def.CarType.ToString(), "pax", def.IsPassengerCar(), "class", Classes.Words[r.Cls],
                "from", FromWords[r.ClsFrom], "via", via, "native", Classes.Words[r.NativeCls], "obs", r.Obs, "obsFrom", r.ObsFrom == 0 ? "default" : FromWords[r.ObsFrom],
                "otherEnd", r.AtOtherEnd, "seatsNow", seatsNow, "seats", r.Seats, "nativeSeats", r.NativeSeats, "packSeats", r.PackSeats, "bagLb", r.BaggageLb, "roomLb", r.CompartmentLb);
        }

        // The world as it loaded (host or client, the cars and how many are First Class's twins): the migration's "twins found".
        internal static void LogWorld()
        {
            var shared = TrainController.Shared;
            int cars = 0, pax = 0, fc = 0, sl = 0;
            if (shared != null)
                foreach (Car c in shared.Cars)
                {
                    cars++;
                    if (c.IsPassengerCar()) pax++;
                    int k = Logic.ClassOfId(c.DefinitionInfo?.Identifier);
                    if (k == 1) fc++; else if (k == 2) sl++;
                }
            DebugLog.Event(Main.Id, "world", "host", Game.State.StateManager.IsHost, "cars", cars, "passenger", pax, "twinFirstClass", fc, "twinSleeper", sl);
        }

        private static void SetSeats(CarDefinition def, int seats)
        {
            foreach (LoadSlot slot in def.LoadSlots)
                if (slot.RequiredLoadIdentifier == "rr/passengers") { slot.MaximumCapacity = seats; return; }
        }

        // The model of this definition id looked at again: after a choice or a settings change, its seats follow (cars of the model need not be asked).
        internal static void ApplyModel(string definitionId)
        {
            IContentLibrary lib = TrainController.Shared?.ContentLibrary;
            if (lib == null || !lib.TryGetDefinitionInfo(definitionId, out TypedContainerItem<CarDefinition> info)) return;
            ModelOfDefinition(definitionId, info.Definition);
        }

        // Every model the player has a choice for (a world has loaded, or the seat percents changed).
        private static readonly List<string> ChosenScratch = new List<string>();
        internal static void ApplyChosen()
        {
            ChosenScratch.Clear();
            foreach (string id in Classes.Player.Keys) ChosenScratch.Add(id);
            foreach (string id in ChosenScratch) ApplyModel(id);
        }

        internal static int ClassOf(Car car)
        {
            Classes.Resolved r = ModelOf(car);
            return r == null ? 0 : r.BoardClass;
        }

        // ---- the sleeper distance ----
        // The miles between a station and a destination, asked of the game once per pair and kept for the world (NaN: no route known).
        // PassengerStop.TryCalculateMilesBetweenPassengerStops(string, string, out DistanceInfo) is public static; it is false for the same stop twice or an unknown stop; DistanceInfo.DistanceInMiles is the float the fare uses [load]
        
        private static readonly Dictionary<string, Dictionary<string, float>> MilesByIds = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);

        private static float MilesTo(PassengerStop stop, string dest) => MilesBetween(stop.identifier, dest);

        // The same, by two stops' ids (a rider's origin and destination): asked of the game once per pair and kept for the world.
        internal static float MilesBetween(string from, string to)
        {
            if (!MilesByIds.TryGetValue(from, out Dictionary<string, float> m)) MilesByIds[from] = m = new Dictionary<string, float>(StringComparer.Ordinal);
            if (m.TryGetValue(to, out float v)) return v;
            // A stop on unbuilt track is unreachable to the game itself (IsEnabledReachable; set on every
            // machine by TrackReachabilityManager.UpdateReachability) and the game would log "No route between" for each pair
            // asked. Don't ask, and don't keep the miss: once crews build the track, the next
            // call asks.
            if (!Reachable(from) || !Reachable(to)) return float.NaN;
            v = PassengerStop.TryCalculateMilesBetweenPassengerStops(from, to, out PassengerStop.DistanceInfo info) && info.Success ? info.DistanceInMiles : float.NaN;
            m[to] = v;
            return v;
        }

        // Whether the game can reach a stop by id. A plain loop over the game's array (no allocation); an id the game doesn't
        // list, or no ops yet, counts as reachable so the game answers as before. Plain reference tests, as the game's own
        // PassengerStop.FindAll and GetPassengerStopForId use (OpsController.OnDestroy clears Shared), not Unity's "alive" ==.
        // OpsController.Shared.AllPassengerStops is a PassengerStop[]; PassengerStop.IsEnabledReachable is a public bool [load]
        private static bool Reachable(string id)
        {
            OpsController ops = OpsController.Shared;
            PassengerStop[] all = (object)ops != null ? ops.AllPassengerStops : null;
            if (all == null) return true;
            for (int i = 0; i < all.Length; i++)
            {
                PassengerStop s = all[i];
                if ((object)s != null && string.Equals(s.identifier, id, StringComparison.Ordinal)) return s.IsEnabledReachable;
            }
            return true;
        }

        // The sleeper share of one destination's crowd (the setting, or none for a trip under the sleeper distance).
        private static int SleeperShare(PassengerStop stop, string dest) => Logic.SleeperShareFor(MilesTo(stop, dest), Logic.SleeperMiles, Logic.SleeperSharePercent);

        // The class of the car now being served: 0 coach, 1 first class, 2 sleeper (Fares reads it too).
        internal static int ServingClass => _curClass;
        internal static Car Serving => _cur;

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "boarding", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("board-" + what, "Passenger Classes: " + what + " failed, the game's own boarding stands for this step: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && Live)
            {
                Live = false;
                _cur = null; _curClass = 0; _narrowed = false;
                Main.WarnOnce("board-down", "Passenger Classes: boarding stands down until the next world load; first-class and sleeper cars load like coaches");
            }
        }

        internal static void ServePrefix(Car car)
        {
            _cur = null; _curClass = 0; _leadFor = null;
            if (!Live) return;
            try { _cur = car; _curClass = ClassOf(car); if (Logic.BaggageRiders) Baggage.StartServe(); }
            catch (Exception e) { Failed("serving a car", e); }
        }

        internal static void ServeFinalizer()
        {
            // the car's bags made again from the riders aboard now (riders got off, or the game's own clock dropped some)
            if (_cur != null && Logic.BaggageRiders && Live)
            {
                try { Baggage.Recompute(_cur); }
                catch (Exception e) { Failed("counting a car's baggage", e); }
            }
            _cur = null; _curClass = 0; _narrowed = false; _leadFor = null;
        }

        // requireStopped false is the game's own HasWorkFor looking at cars that may be moving: not a step of the stop, so it changes nothing here.
        internal static void FindCarsPostfix(PassengerStop __instance, HashSet<Car> __result, bool requireStopped)
        {
            if (!Live || !requireStopped) return;
            try
            {
                Refresh(__instance, FcAt, __result, 1);
                Refresh(__instance, SlAt, __result, 2);
                if (!Visits.TryGetValue(__instance, out Visit v)) Visits[__instance] = v = new Visit();
                bool present = __result != null && __result.Count > 0;
                if (present && !v.Present) { v.NoRoom = 0; v.NoBerth = 0; if (v.BagHeld != null) v.BagHeld.Clear(); if (DebugLog.On) DebugReset(); }
                if (!present && v.BagHeld != null && v.BagHeld.Count > 0) v.BagHeld.Clear();      // no train at the platform: nobody is being held by a train's bags     // a train pulled in: the count starts again
                v.Present = present;
            }
            catch (Exception e) { Failed("listing the first-class and sleeper cars at a stop", e); }
        }

        // The stop's cars of one class; a car that was not there last step has just pulled in, so its count starts again.
        private static void Refresh(PassengerStop stop, Dictionary<PassengerStop, List<Car>> at, HashSet<Car> result, int cls)
        {
            if (!at.TryGetValue(stop, out List<Car> list)) at[stop] = list = new List<Car>();
            PrevScratch.Clear(); PrevScratch.AddRange(list);
            list.Clear();
            if (result == null) return;
            foreach (Car c in result)
            {
                if (ClassOf(c) != cls) continue;
                list.Add(c);
                if (!PrevScratch.Contains(c)) NoRoomByCar[c.id] = 0;
            }
        }

        // The Auto Engineer holds a train at a stop while the game's HasWorkFor says there is work (CarHasWork). The game's test never asks the boarding
        // rule, so a car the rule turns every rider away from (a share, the bags, seats held for a premium car) would keep the train there for ever. This
        // postfix asks the same rule, as a dry run (nothing written, nothing logged), and says no work when it would board nobody. Riders to get off
        // here are work whatever the rule says, and a car the rule does not touch keeps the game's own answer.
        internal static void WorkPostfix(PassengerStop __instance, Car car, ref bool __result)
        {
            if (!__result || !Live) return;
            try
            {
                PassengerMarker? mk = car?.GetPassengerMarker();
                if (!mk.HasValue || mk.Value.Destinations == null) return;
                PassengerMarker m = mk.Value;
                if (m.HasPassengerToRemove(__instance.identifier) || m.Destinations.Contains(__instance.identifier)) return;
                int seats = _capacity(__instance, car) - m.TotalPassengers;
                if (seats <= 0) return;
                if (!WouldBoard(__instance, car, m.Destinations, seats)) __result = false;
            }
            catch (Exception e) { Failed("asking whether a car has work at a stop", e); }
        }

        private static bool _dry;
        private static readonly Dictionary<string, Logic.Crowd> DryCrowds = new Dictionary<string, Logic.Crowd>(StringComparer.Ordinal);
        private static bool WouldBoard(PassengerStop stop, Car car, HashSet<string> destinations, int seats)
        {
            Car oldCur = _cur; int oldClass = _curClass; bool oldNarrowed = _narrowed;
            try
            {
                _cur = car; _curClass = ClassOf(car); _dry = true;
                int max = seats; HashSet<string> d = destinations;
                AllocatePrefix(stop, ref max, ref d);
                return !_narrowed || (Narrow.Count > 0 && max > 0);      // the rule did not apply (or failed): the game's own answer stands
            }
            finally { _dry = false; _cur = oldCur; _curClass = oldClass; _narrowed = oldNarrowed; }
        }

        internal static void AllocatePrefix(PassengerStop __instance, ref int maximum, ref HashSet<string> destinations)
        {
            _narrowed = false;
            if (!Live || _cur == null || destinations == null) return;
            try
            {
                FcAt.TryGetValue(__instance, out List<Car> fcCars);
                SlAt.TryGetValue(__instance, out List<Car> slCars);
                _fcHere = fcCars != null && fcCars.Count > 0;
                _slHere = slCars != null && slCars.Count > 0;
                bool bags = Logic.BaggageRiders;
                if (_curClass == 0 && !_fcHere && !_slHere && !bags) return;     // no premium car at this stop and no baggage rule: the game's own boarding, untouched
                IReadOnlyDictionary<string, PassengerStop.WaitingInfo> waiting = __instance.Waiting;
                if (!Crowds.TryGetValue(__instance, out Dictionary<string, Logic.Crowd> crowds)) Crowds[__instance] = crowds = new Dictionary<string, Logic.Crowd>();
                if (_dry)
                {   // a dry run reads the crowds and writes a copy: the stop's own counts move only with a real boarding
                    DryCrowds.Clear();
                    foreach (KeyValuePair<string, Logic.Crowd> kv in crowds) DryCrowds[kv.Key] = kv.Value;
                    crowds = DryCrowds;
                }
                TotalBefore.Clear(); ResSlFor.Clear(); ResFcFor.Clear(); Narrow.Clear(); FitFor.Clear();
                if (_curClass == 0) SeatsFree(__instance, fcCars, FcSeats); else FcSeats.Clear();
                SeatsFree(__instance, slCars, SlBerths);
                // The held-back seats are worked out for every destination the premium cars name, in one order, each car's free seats
                // spent once in all, so a coach for one destination holds back what the others' riders leave of the same seats.
                Union.Clear(); SlBerths.AddDestinationsTo(Union); FcSeats.AddDestinationsTo(Union);
                foreach (string d in Union)
                {
                    if (!waiting.TryGetValue(d, out PassengerStop.WaitingInfo wi) || wi.Total <= 0) continue;
                    crowds.TryGetValue(d, out Logic.Crowd uc);
                    Logic.Reconcile(ref uc, wi.Total, Logic.SharePercent, SleeperShare(__instance, d));
                    crowds[d] = uc;
                    int rs = Logic.Reserved(uc.Sl, SlBerths.Avail(d));          // sleeper riders a free berth is held for
                    SlBerths.Take(d, rs);
                    ResSlFor[d] = rs;
                }
                if (_curClass == 0)
                {
                    foreach (string d in Union)
                    {
                        if (!ResSlFor.TryGetValue(d, out int rs) || !crowds.TryGetValue(d, out Logic.Crowd uc)) continue;
                        int rf = Logic.Reserved(Logic.FirstClassAvailable(uc, rs), FcSeats.Avail(d));
                        FcSeats.Take(d, rf);
                        ResFcFor[d] = rf;
                    }
                }
                int cap = maximum;
                foreach (string d in destinations)
                {
                    if (!waiting.TryGetValue(d, out PassengerStop.WaitingInfo info) || info.Total <= 0) continue;
                    crowds.TryGetValue(d, out Logic.Crowd c);
                    Logic.Reconcile(ref c, info.Total, Logic.SharePercent, SleeperShare(__instance, d));
                    crowds[d] = c;
                    ResSlFor.TryGetValue(d, out int resSl);
                    ResFcFor.TryGetValue(d, out int resFc);
                    if (_curClass == 2)
                    {
                        if (c.Sl <= 0) { if (DebugLog.On) LogRefused(__instance, d, "no-sleeper-riders", info.Total, c, resSl, resFc); continue; }
                        cap = Math.Min(cap, c.Sl);
                    }
                    else if (_curClass == 1)
                    {
                        int avail = Logic.FirstClassAvailable(c, resSl);
                        if (avail <= 0) { if (DebugLog.On) LogRefused(__instance, d, "no-first-class-riders", info.Total, c, resSl, resFc); continue; }
                        cap = Math.Min(cap, avail);
                    }
                    else
                    {
                        int held = resSl + resFc;
                        if (held > 0)
                        {
                            int avail = Logic.CoachAvailable(info.Total, held);
                            if (avail <= 0) { if (DebugLog.On) LogRefused(__instance, d, "held-for-premium", info.Total, c, resSl, resFc); continue; }
                            cap = Math.Min(cap, avail);
                        }
                    }
                    if (bags)
                    {
                        // riders whose bags don't fit the train's baggage room stay on the platform, where they are (in the crowd, with their age)
                        int fit = Baggage.Fit(__instance, _cur, _curClass, d, info.Total);
                        SetBagHeld(__instance, d, info.Total - fit);
                        if (DebugLog.On) FitFor[d] = fit;
                        if (fit <= 0) { if (DebugLog.On) LogRefused(__instance, d, "bags", info.Total, c, resSl, resFc); continue; }
                        cap = Math.Min(cap, fit);
                    }
                    Narrow.Add(d);
                    TotalBefore[d] = info.Total;
                }
                destinations = Narrow;                            // the game only reads it; the car's own set is not touched
                _dbgMax = maximum;
                maximum = Logic.Cap(maximum, cap);
                _dbgCap = maximum;
                _narrowed = true;
            }
            catch (Exception e) { _narrowed = false; Failed("the boarding rule", e); }
        }

        // The seats (or berths) the cars of one class at this stop still have: capacity less the riders who stay aboard. The riders
        // getting off here are leaving, so their seats count as free (they are unloaded two a step while the others load).
        private static void SeatsFree(PassengerStop stop, List<Car> cars, Budget into)
        {
            into.Clear();
            if (cars == null) return;
            foreach (Car car in cars)
            {
                PassengerMarker? m = car.GetPassengerMarker();
                if (!m.HasValue) continue;
                int free = _capacity(stop, car) - (m.Value.TotalPassengers - m.Value.CountPassengersForStop(stop.identifier));
                if (free > 0) into.Add(free, m.Value.Destinations);
            }
        }

        internal static void AllocatePostfix(PassengerStop __instance, int __result, string destinationOut, string originOut)
        {
            if (__result > 0 && _cur != null && Live) Fares.RideBoarded(_cur, originOut ?? __instance.identifier, destinationOut, __result);     // the heat fare's ride book: every boarding, ours or the game's own
            if (__result > 0 && _cur != null && Live && !Daily.Off) DayBoarded(__instance, __result);     // every boarding, ours or the game's own
            if (!_narrowed) return;
            _narrowed = false;
            if (!Live || __result <= 0 || destinationOut == null) return;
            try
            {
                if (!TotalBefore.TryGetValue(destinationOut, out int before)) return;
                if (!Crowds.TryGetValue(__instance, out Dictionary<string, Logic.Crowd> crowds)) return;
                crowds.TryGetValue(destinationOut, out Logic.Crowd c);
                Logic.Crowd was = c;
                int now = __instance.Waiting.TryGetValue(destinationOut, out PassengerStop.WaitingInfo info) ? info.Total : 0;
                int boarded = Math.Max(0, before - now);        // what left the platform; the game's returned count can be one more
                int noRoom = 0, noBerth = 0;
                if (_curClass == 2) Logic.BoardedSleeper(ref c, boarded, now);
                else if (_curClass == 1)
                {
                    Logic.BoardedFirstClass(ref c, boarded, now, out int overflow);
                    if (_slHere) noBerth = overflow;                // sleeper riders in a first-class seat, with a sleeper car at the stop and no berth
                }
                else
                {
                    ResSlFor.TryGetValue(destinationOut, out int resSl);
                    ResFcFor.TryGetValue(destinationOut, out int resFc);
                    Logic.BoardedCoach(ref c, boarded, before, resSl, resFc, now, out int dFc, out int dSl);
                    if (_fcHere) noRoom = dFc;
                    if (_slHere) noBerth = dSl;
                }
                crowds[destinationOut] = c;
                if (DebugLog.On) LogBoarded(__instance, destinationOut, originOut, boarded, before, now, was, c, noRoom, noBerth);
                if (Logic.BaggageRiders) Baggage.Boarded(_cur, _curClass, originOut ?? __instance.identifier, destinationOut, boarded);
                if (noRoom > 0 || noBerth > 0) { Count(__instance, noRoom, noBerth); if (!Daily.Off) DayLeft(__instance, originOut, destinationOut, noRoom, noBerth); }
            }
            catch (Exception e) { Failed("booking a boarding", e); }
        }

        // The debug log's line for a boarding the rule shaped: who boarded which car from which crowd, and the counts the rule worked from.
        private static void LogBoarded(PassengerStop stop, string dest, string origin, int boarded, int before, int now, Logic.Crowd was, Logic.Crowd after, int noRoom, int noBerth)
        {
            ResSlFor.TryGetValue(dest, out int resSl);
            ResFcFor.TryGetValue(dest, out int resFc);
            int fit = -1;
            if (Logic.BaggageRiders) FitFor.TryGetValue(dest, out fit);
            float miles = MilesTo(stop, dest);
            DebugLog.Event(Main.Id, "board", "stop", stop.identifier, "car", _cur?.id, "class", ClassWord(_curClass), "from", origin, "to", dest, "riders", boarded,
                "max", _dbgMax, "cap", _dbgCap, "total", before, "left", now, "fcBefore", was.Fc, "slBefore", was.Sl, "fc", after.Fc, "sl", after.Sl,
                "resSl", resSl, "resFc", resFc, "noRoom", noRoom, "noBerth", noBerth, "miles", float.IsNaN(miles) ? -1f : miles, "fit", fit);
        }

        // A car turned riders away for a reason, written when the reason for that car and destination changes (or a train has just pulled in).
        private static void LogRefused(PassengerStop stop, string dest, string reason, int total, Logic.Crowd c, int resSl, int resFc)
        {
            if (_dry) return;      // the Auto Engineer's work question runs the rule without leaving a trace
            string key = stop.identifier + "|" + _cur?.id + "|" + dest;
            if (RefusedMem.TryGetValue(key, out string last) && last == reason) return;
            RefusedMem[key] = reason;
            DebugLog.Event(Main.Id, "refused", "stop", stop.identifier, "car", _cur?.id, "class", ClassWord(_curClass), "to", dest, "reason", reason, "total", total,
                "fc", c.Fc, "sl", c.Sl, "resSl", resSl, "resFc", resFc);
        }

        internal static string ClassWord(int boardClass) => boardClass == 2 ? "sleeper" : boardClass == 1 ? "parlor" : "coach";

        // riders waiting for `dest` who couldn't board for their bags (0 clears it), kept in the station's visit
        private static void SetBagHeld(PassengerStop stop, string dest, int held)
        {
            if (_dry) return;      // the Auto Engineer's work question runs the rule without leaving a trace
            if (DebugLog.On) LogBagHeld(stop, dest, held);
            if (held > 0 && !Daily.Off) DayHeld(stop, dest, held);
            if (!Visits.TryGetValue(stop, out Visit v)) Visits[stop] = v = new Visit();
            if (held <= 0) { if (v.BagHeld != null) v.BagHeld.Remove(dest); return; }
            if (v.BagHeld == null) v.BagHeld = new Dictionary<string, int>(StringComparer.Ordinal);
            v.BagHeld[dest] = held;
        }

        // Riders held back by their bags, written when a car starts or stops holding riders back for a destination (the count crossing zero), not each
        // time the count moves: a train of several coaches has a different count for each.
        private static void LogBagHeld(PassengerStop stop, string dest, int held)
        {
            string key = stop.identifier + "|" + _cur?.id + "|" + dest;
            BagHeldMem.TryGetValue(key, out int last);
            BagHeldMem[key] = held;
            if ((last == 0) == (held == 0)) return;
            DebugLog.Event(Main.Id, "bag-held", "stop", stop.identifier, "car", _cur?.id, "class", ClassWord(_curClass), "to", dest, "held", held, "was", last,
                "allowance", Baggage.LastAllowance, "headroomLb", Baggage.LastHeadroom);
        }

        private static void Count(PassengerStop stop, int noRoom, int noBerth)
        {
            if (Visits.TryGetValue(stop, out Visit v)) { v.NoRoom += noRoom; v.NoBerth += noBerth; }
            if (noRoom > 0 && FcAt.TryGetValue(stop, out List<Car> fc)) foreach (Car car in fc) { NoRoomByCar.TryGetValue(car.id, out int n); NoRoomByCar[car.id] = n + noRoom; }
            if (noBerth > 0 && SlAt.TryGetValue(stop, out List<Car> sl)) foreach (Car car in sl) { NoRoomByCar.TryGetValue(car.id, out int n); NoRoomByCar[car.id] = n + noBerth; }
        }

        // ---- the day book for the feed (Daily.cs): what the hooks above and Fares.cs hand it ----

        // The game clock in seconds, and whether this computer is the host (the feed's sources, and every figure's date).
        // TimeWeather.Now.TotalSeconds is the game's clock in seconds (a double); the books' day is its floor over 86,400 [load]
        // StateManager.IsHost is a public static bool [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static double NowSeconds() => TimeWeather.Now.TotalSeconds;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsHostNow() => Game.State.StateManager.IsHost;

        // The engine that leads the serving car's train: the first locomotive from the A end (the stable pick; a train with engines at both ends
        // keeps the one at A), kept for the serve. No locomotive in the coupled cars: no train row (the station and the railroad still count).
        // Car.IsLocomotive is a public virtual bool (BaseLocomotive: true; a tender is not), Car.id the car's own id and Car.DisplayName its road mark and number; Car.CoupledTo(LogicalEnd) walks the train (C51) [load]
        private static Car _leadFor; private static string _leadId, _leadName;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Lead(out string id, out string name)
        {
            if (_cur == null) { _leadFor = null; _leadId = null; _leadName = null; id = null; name = null; return; }      // no car served (boarding stood down): no train, never the last one
            if (!ReferenceEquals(_leadFor, _cur))
            {
                _leadFor = _cur; _leadId = null; _leadName = null;
                if (_cur != null)
                {
                    Car first = _cur, next;
                    int guard = 0;
                    while (guard++ < 10000 && (next = first.CoupledTo(Car.LogicalEnd.A)) != null) first = next;
                    guard = 0;
                    for (Car c = first; c != null && guard++ < 10000; c = c.CoupledTo(Car.LogicalEnd.B))
                        if (c.IsLocomotive) { _leadId = c.id; _leadName = c.DisplayName; break; }
                }
            }
            id = _leadId; name = _leadName;
        }

        internal static void DailyFailed(string what, Exception e)
        {
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "daily", "what", what, "type", e.GetType().Name);
            Main.WarnOnce("daily-" + what, "Passenger Classes: the day figures for the feed failed (" + what + "): " + PathClean.CleanText(e.Message));
            if (Daily.Failed()) Main.WarnOnce("daily-down", "Passenger Classes: the day figures for the feed stand down until the next world load; boarding and fares go on");
        }

        // `n` riders boarded the serving car at this stop (the game's own count: what the car received, and so what pays a fare when it gets off).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DayBoarded(PassengerStop stop, int n)
        {
            try
            {
                Lead(out string tid, out string tname);
                Daily.Boarded(NowSeconds(), stop.identifier, stop.DisplayName, tid, tname, _curClass, n);
            }
            catch (Exception e) { DailyFailed("boarding", e); }
        }

        // The fare band of a trip for the feed's left-behind counts: 1 to 8, or 0 when the game has no route (it pays no fare either).
        internal static int BandOf(string from, string to)
        {
            if (from == null || to == null) return 0;
            float miles = MilesBetween(from, to);
            return float.IsNaN(miles) ? 0 : Logic.FareBand(miles);
        }

        // Riders who wanted first class or a sleeper and rode lower: from the group that just boarded (origin to destination).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DayLeft(PassengerStop stop, string origin, string dest, int noRoom, int noBerth)
        {
            try
            {
                Lead(out string tid, out string tname);
                double now = NowSeconds();
                int band = BandOf(origin ?? stop.identifier, dest);
                if (noRoom > 0) Daily.Left(now, stop.identifier, stop.DisplayName, tid, tname, Daily.NoRoom, band, noRoom);
                if (noBerth > 0) Daily.Left(now, stop.identifier, stop.DisplayName, tid, tname, Daily.NoBerth, band, noBerth);
            }
            catch (Exception e) { DailyFailed("left behind", e); }
        }

        // Riders held waiting by the serving train's bags for a destination (a level: the day book counts its peak once).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DayHeld(PassengerStop stop, string dest, int held)
        {
            try
            {
                Lead(out string tid, out string tname);
                Daily.Holding(NowSeconds(), stop.identifier, stop.DisplayName, tid, tname, dest, held, BandOf(stop.identifier, dest));
            }
            catch (Exception e) { DailyFailed("held by bags", e); }
        }

        // Riders who gave up waiting: PassengerExpiration drops a platform group whose origin is not this stop and whose boarding time is not after
        // the cutoff. The prefix counts those groups by the fare band of their trip (nothing is allocated: the platform's own dictionary is walked
        // with its struct enumerator); the postfix checks the sum against the game's own count and hands it to the day book.
        private static readonly int[] ExpBands = new int[Daily.Bands];
        private static int _expSum;
        internal static void ExpirePrefix(PassengerStop __instance, GameDateTime expiration)
        {
            _expSum = 0;
            if (!Live || Daily.Off) return;
            try
            {
                Array.Clear(ExpBands, 0, ExpBands.Length);
                if (!(__instance.Waiting is Dictionary<string, PassengerStop.WaitingInfo> waiting)) return;
                string me = __instance.identifier;
                foreach (KeyValuePair<string, PassengerStop.WaitingInfo> kv in waiting)
                {
                    IReadOnlyList<WaitingPassengerGroup> groups = kv.Value.Groups;
                    for (int i = 0; i < groups.Count; i++)
                    {
                        WaitingPassengerGroup g = groups[i];
                        if (g.Origin == me || g.Boarded > expiration) continue;
                        ExpBands[BandOf(g.Origin, kv.Key)] += g.Count;
                        _expSum += g.Count;
                    }
                }
            }
            catch (Exception e) { _expSum = 0; DailyFailed("counting riders who gave up", e); }
        }

        internal static void ExpirePostfix(PassengerStop __instance, int __result)
        {
            if (!Live || Daily.Off || __result <= 0) return;
            try
            {
                // the prefix's count is used only when it agrees with the game's own (a mismatch lands in the unknown band)
                Daily.GaveUpAt(NowSeconds(), __instance.identifier, __instance.DisplayName, _expSum == __result ? ExpBands : null, __result);
                if (DebugLog.On) DebugLog.Event(Main.Id, "gave-up", "stop", __instance.identifier, "riders", __result, "counted", _expSum);
            }
            catch (Exception e) { DailyFailed("riders who gave up", e); }
        }

        // ---- the feed's station side: what the crowds are now, worked out without changing the counts ----

        internal static List<object> ReadStations()
        {
            var list = new List<object>();
            IEnumerable<PassengerStop> stops = PassengerStop.FindAll();
            if (stops == null) return list;
            foreach (PassengerStop s in stops)
            {
                int total = 0, fc = 0, sl = 0;
                Crowds.TryGetValue(s, out Dictionary<string, Logic.Crowd> crowds);
                foreach (KeyValuePair<string, PassengerStop.WaitingInfo> kv in s.Waiting)
                {
                    Logic.Crowd c = default(Logic.Crowd);
                    if (crowds != null) crowds.TryGetValue(kv.Key, out c);
                    Logic.Reconcile(ref c, kv.Value.Total, Logic.SharePercent, SleeperShare(s, kv.Key));
                    total += kv.Value.Total; fc += c.Fc; sl += c.Sl;
                }
                Visits.TryGetValue(s, out Visit v);
                var row = new Dictionary<string, object>
                {
                    { "id", s.identifier }, { "industryId", s.Industry?.identifier }, { "name", s.DisplayName },
                    { "waiting", total }, { "firstClassWaiting", fc }, { "sleeperWaiting", sl },
                    { "firstClassNoRoom", v == null ? 0 : v.NoRoom }, { "sleeperNoBerth", v == null ? 0 : v.NoBerth },
                };
                if (Logic.BaggageRiders)
                {
                    int held = 0;
                    if (v != null && v.BagHeld != null) foreach (KeyValuePair<string, int> kv in v.BagHeld) held += kv.Value;
                    row["heldByBaggage"] = held;
                }
                list.Add(row);
            }
            return list;
        }

        // The first-class and sleeper riders of one destination's crowd at a station, worked out as the feed's station rows do and without
        // changing the counts (the station window's First and Sleeper columns read it).
        internal static void StationFigures(PassengerStop stop, string dest, int total, out int fc, out int sl)
        {
            Logic.Crowd c = default(Logic.Crowd);
            if (Crowds.TryGetValue(stop, out Dictionary<string, Logic.Crowd> crowds)) crowds.TryGetValue(dest, out c);
            Logic.Reconcile(ref c, total, Logic.SharePercent, SleeperShare(stop, dest));
            fc = c.Fc; sl = c.Sl;
        }

        // The cars the feed lists: every passenger car, and any car that has a class (a baggage car). The rich feed ("passenger-classes")
        // gets every key; the compatibility feed ("first-class", the keys First Class published) gets the older ones.
        internal static List<object> ReadCarsRich() => ReadCars(false);
        internal static List<object> ReadCarsCompat() => ReadCars(true);

        private static List<object> ReadCars(bool compat)
        {
            var list = new List<object>();
            var shared = TrainController.Shared;
            if (shared == null) return list;
            foreach (Car c in shared.Cars)
            {
                bool pax = c.IsPassengerCar();
                Classes.Resolved m = ModelOf(c);
                if (m == null || (!pax && m.Cls == Classes.None)) continue;
                int cls = m.BoardClass;
                if (compat && !pax) continue;
                var row = new Dictionary<string, object> { { "id", c.id }, { "definition", c.DefinitionInfo.Identifier } };
                PassengerMarker? mk = pax ? c.GetPassengerMarker() : null;
                int aboard = mk.HasValue ? mk.Value.TotalPassengers : 0;
                if (compat)
                {
                    row["firstClass"] = cls == 1; row["sleeper"] = cls == 2;
                }
                else
                {
                    // a built-in lounge reads as a coach while the lounge rule is off
                    row["class"] = Classes.Words[m.Cls == Classes.Lounge && m.ClsFrom == 1 && !Logic.LoungePatience ? Classes.Coach : m.Cls]; row["classSource"] = SourceWords[m.ClsFrom]; row["observation"] = m.Obs;
                    if (pax) { row["seats"] = Seats(c); row["seatsUsed"] = aboard; HeatRow(row, c.id, cls); }
                    if (m.BaggageLb >= 0 && Logic.BaggageRiders) row["baggageLb"] = m.BaggageLb;
                    if (Logic.BaggageRiders && (pax || m.Cls != Classes.None)) { Baggage.Read(c, out double used, out double room); row["baggageUsedLb"] = (int)Math.Round(used); row["baggageRoomLb"] = (int)Math.Round(room); row["baggageCompartmentLb"] = (int)Math.Round(Baggage.Compartment(c)); }
                }
                if (cls != 0)
                {
                    if (cls == 1) row["firstClassSeatsUsed"] = aboard; else row["sleeperBerthsUsed"] = aboard;
                    NoRoomByCar.TryGetValue(c.id, out int n);
                    row["noRoomSinceArrival"] = n;
                }
                list.Add(row);
            }
            return list;
        }

        // The cold-weather fare's effect on this car now (Heat.cs): a signed percent and its words, only when it moves the fare.
        private static void HeatRow(Dictionary<string, object> row, string id, int cls)
        {
            double pct = Heat.Percent(id, cls, out bool heated, out double _, out double inside);
            string words = Heat.Words(pct, heated, inside);
            if (words == null) { string inc = Heat.IncludedWords(); if (inc != null) row["heatEffect"] = inc; return; }
            row["heatEffectPercent"] = Math.Round(pct, 1); row["heatEffect"] = words;
        }

        private static readonly string[] SourceWords = "default,built-in,pack file,player".Split(',');   // Classes.Resolved.ClsFrom, 0 to 3

        // The seats: the first passenger slot's capacity, as the game's own PassengerCapacity reads it.
        // CarDefinition.LoadSlots holds LoadSlot with RequiredLoadIdentifier "rr/passengers" and a float MaximumCapacity: the seats (PassengerStop.PassengerCapacity) [load]
        private static int Seats(Car c) => SeatsOf(c.Definition);

        private static int SeatsOf(CarDefinition d)
        {
            foreach (var slot in d.LoadSlots)
                if (slot.RequiredLoadIdentifier == "rr/passengers") return (int)slot.MaximumCapacity;
            return 0;
        }
    }
}
