using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Messages;
using Game.State;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Network;
using Track;
using UI.Common;

namespace PassengerClasses
{
    // The refit: a coach standing on a repair track is rebuilt, where it stands, as its first-class or sleeper twin, or a twin back as its coach.
    // Host only (the host owns the cars and the money). The car keeps its id, its road number, its place in the train and everything the game
    // keeps in its properties (couplings, anglecocks, handbrake, waybill, condition, odometer, paint); the air pressures and its switch list
    // entries are copied by hand.
    //
    // How: the game's own in-place rebuild, TrainController.RebuildCars (what it runs when a car definition changes), with one string swapped:
    // snapshot the car and its properties, remove it, add it again under the same id from the snapshot (observers deferred), fire the observers,
    // rejoin the train. RebuildCars and the two steps it is made of are private, so the two steps are reached by reflection and checked at
    // load (Resolve): a game version without them, or with other signatures, never shows the refit entries, with one warning, and the rest of
    // the mod goes on. This class is reached only through NoInlining trampolines inside a try (Main, Menu): its fields name game types.
    //
    // Money: the fee is worked out once, checked against the balance before anything is touched, and paid as one Equipment ledger line only
    // after the twin stands on the track. If the game refuses to build the twin, the car is put back from its own snapshot and nothing is paid.
    internal static class Refit
    {
        internal static volatile bool Live;
        internal static bool Off = true;          // the preflight failed (or has not run): the entries never show
        private static int _failures;
        private static Func<TrainController, Snapshot.Car, Dictionary<string, IPropertyValue>, int, bool, Car> _add;
        private static Action<TrainController, HashSet<Car>> _updateSets;

        // Used Equipment's mark on every car of a leased or financed order (its own key, read here and never written).
        private const string LeaseKey = "_ashenmare.leaseStamp";

        // Test seam (tools/gamecheck): the game's toast needs its UI; when set it is called with the text instead.
#pragma warning disable CS0649   // set by tools/gamecheck by reflection, never by the mod
        internal static Action<string> NoteSeam;
#pragma warning restore CS0649

        internal static void ResetWorld() { _failures = 0; }

        // TrainController.AddCarInternal(Snapshot.Car, Dictionary<string, IPropertyValue>, int version, bool deferObservers) is private, returns the Car: it builds the car under the snapshot's id, ident, facing and properties, and with version 1 places it by the snapshot's location (the body's centre); RebuildCars calls it with (snapshot, properties, 1, true) after removing the old car [load]
        // TrainController.UpdateSets(HashSet<Car>) is private, void: on the host it joins each car to the neighbours within 3 m of its wheel bounds and sends the set changes; RebuildCars calls it after FirePendingKeyValueObservers [load]
        // TrainController.RemoveCar(string) is public and sends RemoveCars (the car's id and road number are free again at once); Car.Snapshot(SnapshotOption) returns a Snapshot.Car struct whose id, definitionIdentifier and roadNumber are public string fields; AddCars(List<Snapshot.Car>) is the host's message for clients (HandleAddCars does nothing on the host); Car.FirePendingKeyValueObservers() is public and is what sends the deferred properties [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            _add = null; _updateSets = null;
            const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Instance, pub = BindingFlags.Public | BindingFlags.Instance;
            MethodInfo add = typeof(TrainController).GetMethod("AddCarInternal", priv, null, new[] { typeof(Snapshot.Car), typeof(Dictionary<string, IPropertyValue>), typeof(int), typeof(bool) }, null);
            if (add == null || add.ReturnType != typeof(Car)) return "TrainController.AddCarInternal";
            MethodInfo sets = typeof(TrainController).GetMethod("UpdateSets", priv, null, new[] { typeof(HashSet<Car>) }, null);
            if (sets == null || sets.ReturnType != typeof(void)) return "TrainController.UpdateSets";
            MethodInfo remove = typeof(TrainController).GetMethod("RemoveCar", pub, null, new[] { typeof(string) }, null);
            if (remove == null) return "TrainController.RemoveCar";
            MethodInfo snap = typeof(Car).GetMethod("Snapshot", pub, null, new[] { typeof(Car.SnapshotOption) }, null);
            if (snap == null || snap.ReturnType != typeof(Snapshot.Car)) return "Car.Snapshot";
            foreach (string f in new[] { "id", "definitionIdentifier", "roadNumber" })
            {
                FieldInfo fi = typeof(Snapshot.Car).GetField(f, pub);
                if (fi == null || fi.FieldType != typeof(string)) return "Snapshot.Car." + f;
            }
            if (typeof(Car).GetMethod("FirePendingKeyValueObservers", pub, null, Type.EmptyTypes, null) == null) return "Car.FirePendingKeyValueObservers";
            if (typeof(AddCars).GetConstructor(new[] { typeof(List<Snapshot.Car>) }) == null) return "AddCars";
            _add = (Func<TrainController, Snapshot.Car, Dictionary<string, IPropertyValue>, int, bool, Car>)Delegate.CreateDelegate(typeof(Func<TrainController, Snapshot.Car, Dictionary<string, IPropertyValue>, int, bool, Car>), add);
            _updateSets = (Action<TrainController, HashSet<Car>>)Delegate.CreateDelegate(typeof(Action<TrainController, HashSet<Car>>), sets);
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SetOff(bool off) { Off = off; if (off) { _add = null; _updateSets = null; } }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SetLive(bool on) { Live = on; }

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "refit", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("refit-" + what, "Passenger Classes: " + what + " failed: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && Live) { Live = false; Main.WarnOnce("refit-down", "Passenger Classes: the refit entries stand down until the next world load"); }
        }

        // ---- the menu ----

        // The entries for a car's right-click menu: nothing unless this is the host, the car's model has a twin here (or is one) and the car
        // stands on a repair track. Every other condition is answered at the click, with a reason.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void AddEntries(UI.ContextMenu.ContextMenu menu, Car car)
        {
            if (Off || !Live || car == null || !StateManager.IsHost) return;
            string model = car.DefinitionInfo?.Identifier;
            if (model == null) return;
            string carId = car.id;
            int entries = 0;
            string stock = Logic.BaseOf(model);
            if (stock != null)
            {
                if (!OnRepairTrack(car)) return;
                Menu.Add(menu, Logic.RefitEntry(0), () => AskSafe(carId, stock));
                entries++;
            }
            else
            {
                bool looked = false;
                for (int cls = 1; cls <= 2; cls++)
                {
                    string twin = Logic.TwinOf(model, cls);
                    if (twin == null) continue;
                    if (!looked) { if (!OnRepairTrack(car)) return; looked = true; }
                    if (!TryDefinition(twin, out _)) continue;
                    Menu.Add(menu, Logic.RefitEntry(cls), () => AskSafe(carId, twin));
                    entries++;
                }
                if (!looked) return;
            }
            if (DebugLog.On) DebugLog.Event(Main.Id, "refit-menu", "car", carId, "model", model, "entries", entries);
        }

        // The game's own test for a car at a repair track (RepairTrack.FindRepairTrackForCar, private) is: not in bardo, and some RepairTrack component of OpsController.Shared.AllIndustries has a trackSpans entry whose Contains(car.LocationA) is true. Its first branch (a car merely sent to a repair track) is left out here: the car must stand on one
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool OnRepairTrack(Car car)
        {
            if (car.IsInBardo) return false;
            OpsController ops = OpsController.Shared;
            Industry[] all = ops == null ? null : ops.AllIndustries;
            if (all == null) return false;
            Location a = car.LocationA;
            for (int i = 0; i < all.Length; i++)
            {
                foreach (IndustryComponent component in all[i].Components)
                {
                    if (!(component is RepairTrack)) continue;
                    TrackSpan[] spans = component.trackSpans;
                    for (int j = 0; j < spans.Length; j++) if (spans[j].Contains(a)) return true;
                }
            }
            return false;
        }

        private static bool TryDefinition(string id, out TypedContainerItem<CarDefinition> info)
        {
            info = null;
            TrainController tc = TrainController.Shared;
            return tc != null && tc.ContentLibrary != null && tc.ContentLibrary.TryGetDefinitionInfo(id, out info) && info != null && info.Definition != null;
        }

        // ---- the gates ----

        private sealed class Plan
        {
            internal Car Car;
            internal string From, To;
            internal int FromClass, ToClass, StockPrice, TwinPrice, Difference, Labor, Fee, Seats;
        }

        // Every reason a refit is refused, checked before the question and again at the yes (the window stays open while the game runs).
        // Null when the refit may go ahead; then plan holds the one fee both the balance check and the payment use.
        private static string Refuse(string carId, string target, out Plan plan)
        {
            plan = null;
            TrainController tc = TrainController.Shared;
            if (Off || !Live || tc == null || !StateManager.IsHost) return "host";
            if (!tc.TryGetCarForId(carId, out Car car) || car == null) return "gone";
            string from = car.DefinitionInfo?.Identifier;
            bool toTwin = Logic.BaseOf(target) == from && from != null;
            bool toStock = from != null && Logic.BaseOf(from) == target;
            if (!toTwin && !toStock) return "model";
            if (!TryDefinition(from, out TypedContainerItem<CarDefinition> fromInfo) || !TryDefinition(target, out TypedContainerItem<CarDefinition> toInfo)) return "model";
            if (!OnRepairTrack(car)) return "track";
            if (!car.IsOwnedByPlayer) return "foreign";
            if (!car.KeyValueObject[LeaseKey].IsNull) return "leased";
            if (!AtRest(car)) return "moving";
            if (Loaded(car)) return "loaded";
            CarDefinition stock = toTwin ? fromInfo.Definition : toInfo.Definition, twin = toTwin ? toInfo.Definition : fromInfo.Definition;
            plan = new Plan
            {
                Car = car, From = from, To = target, FromClass = Logic.ClassOfId(from), ToClass = Logic.ClassOfId(target),
                StockPrice = stock.BasePrice, TwinPrice = twin.BasePrice, Seats = SeatsOf(toInfo.Definition),
            };
            plan.Difference = Logic.RefitDifference(plan.StockPrice, plan.TwinPrice, toTwin);
            plan.Labor = Logic.RefitLabor(plan.TwinPrice, Logic.RefitLaborPercent, Logic.RefitLaborMinimum);
            plan.Fee = plan.Difference + plan.Labor;
            if (!SameShape(fromInfo.Definition, toInfo.Definition)) return "shape";
            if (!StateManager.Shared.CanAfford(plan.Fee)) return "money";
            return null;
        }

        // The car and every car coupled to it stand still.
        private static bool AtRest(Car car)
        {
            if (!Still(car)) return false;
            if (car.set == null) return true;
            foreach (Car c in car.EnumerateCoupled()) if (c != null && !Still(c)) return false;
            return true;
        }

        private static bool Still(Car c) => c.IsStopped() && Math.Abs(c.velocity) < 0.01f;

        // Riders aboard, or anything in a load slot: the twin has fewer seats, so the car is refitted empty.
        private static bool Loaded(Car car)
        {
            PassengerMarker? m = car.GetPassengerMarker();
            if (m.HasValue && m.Value.Groups != null && m.Value.TotalPassengers > 0) return true;
            int slots = car.Definition.LoadSlots.Count;
            for (int i = 0; i < slots; i++)
            {
                CarLoadInfo? load = car.GetLoadInfo(i);
                if (load.HasValue && load.Value.Quantity > 0f) return true;
            }
            return false;
        }

        // The rebuild puts the car back by its length and wheel insets (TrainController.WheelBoundsFrontFromSnapshotLocation): a twin of another
        // shape would shift on the track and could come uncoupled, so only the same shape is refitted.
        private static bool SameShape(CarDefinition a, CarDefinition b)
        {
            a.CalculateWheelInsets(out float af, out float ar);
            b.CalculateWheelInsets(out float bf, out float br);
            return a.Length == b.Length && af == bf && ar == br;
        }

        private static int SeatsOf(CarDefinition d)
        {
            foreach (LoadSlot slot in d.LoadSlots) if (slot.RequiredLoadIdentifier == "rr/passengers") return (int)slot.MaximumCapacity;
            return 0;
        }

        private static string RefuseText(string why, Plan plan)
        {
            switch (why)
            {
                case "track": return "Move it to a repair track first.";
                case "foreign": return "Only your own cars can be refitted.";
                case "leased": return "A leased car can't be refitted.";
                case "moving": return "Stop the train first.";
                case "loaded": return "Unload it first.";
                case "money": return "Not enough money: " + Logic.Money(plan == null ? 0 : plan.Fee) + ".";
                case "host": return null;
                default: return "This car can't be refitted.";
            }
        }

        private static void Refused(string carId, string target, string why, Plan plan)
        {
            if (DebugLog.On) DebugLog.Event(Main.Id, "refit-refused", "car", carId, "to", target, "why", why, "fee", plan == null ? -1 : plan.Fee);
            if (why == "shape") Main.WarnOnce("refit-shape-" + target, "Passenger Classes: " + target + " is not the shape of its stock car, so it is not refitted");
            string text = RefuseText(why, plan);
            if (text != null) Note(text);
        }

        // UI.Common.Toast.Present(string, ToastPosition) is public static: a short line on the screen of this player only; StateManager.CanAfford(int) is public (true in sandbox); ApplyToBalance(int, Ledger.Category, EntityReference?, string, int, bool) is public, host only, and writes one ledger line (Ledger.Category.Equipment is what a purchase uses)
        private static void Note(string text)
        {
            if (NoteSeam != null) NoteSeam(text); else Toast(text);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Toast(string text) => UI.Common.Toast.Present(text);

        // ---- the question and the yes ----

        internal static void AskSafe(string carId, string target)
        {
            try { Ask(carId, target); }
            catch (Exception e) { Failed("asking about a refit", e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Ask(string carId, string target)
        {
            string why = Refuse(carId, target, out Plan plan);
            if (why != null) { Refused(carId, target, why, plan); return; }
            string text = Logic.RefitQuestion(plan.Car.DisplayName, plan.ToClass, plan.Fee, plan.Seats);
            if (DebugLog.On) DebugLog.Event(Main.Id, "refit-ask", "car", carId, "from", plan.From, "to", target, "fee", plan.Fee);
            Action yes = () => DoSafe(carId, target);
            if (Menu.ConfirmSeam != null) Menu.ConfirmSeam(text, yes);
            else Present(text, yes, () => { if (DebugLog.On) DebugLog.Event(Main.Id, "refit-cancel", "car", carId, "to", target); });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Present(string text, Action yes, Action no)
        {
            ModalAlertController.Present("Refit car?", text, new[] { (true, "Refit"), (false, "Cancel") }, confirmed => { if (confirmed) yes(); else no(); });
        }

        internal static void DoSafe(string carId, string target)
        {
            try { Do(carId, target); }
            catch (Exception e) { Failed("a refit", e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Do(string carId, string target)
        {
            string why = Refuse(carId, target, out Plan plan);
            if (why != null) { Refused(carId, target, why, plan); return; }
            string name = plan.Car.DisplayName;
            if (!Swap(plan, out Car now, out string kept)) return;
            bool paid = true;
            try { if (plan.Fee != 0) StateManager.Shared.ApplyToBalance(-plan.Fee, Game.History.Ledger.Category.Equipment, null, Logic.RefitMemo(name, plan.FromClass, plan.ToClass)); }
            catch (Exception e) { paid = false; Main.WarnOnce("refit-pay", "Passenger Classes: the car was refitted but the fee could not be charged: " + PathClean.CleanText(e.Message)); }
            if (DebugLog.On) DebugLog.Event(Main.Id, "refit", "car", carId, "from", plan.From, "to", plan.To, "toTwin", plan.ToClass != 0, "stockPrice", plan.StockPrice, "twinPrice", plan.TwinPrice, "percent", Logic.RefitLaborPercent, "minimum", Logic.RefitLaborMinimum, "difference", plan.Difference, "labor", plan.Labor, "fee", plan.Fee, "paid", paid, "clients", Multiplayer.IsClientActive, "train", kept);
        }

        // ---- the swap ----

        // Car.air holds the three reservoirs (BrakeLine, BrakeReservoir, BrakeCylinder) whose public float Pressure is not in the car's properties (TrainController.FillAir sets them the same way); the switch lists are read with SwitchListController.PopulateSnapshot(ref Snapshot) (crew id to entries of car ids) and written with SetSwitchListCarIds(crew, ids, send); removing a car takes it off every list; TrainController.SelectedCar can be set; Car.CoupledTo(LogicalEnd) is the neighbour at an end
        // The car is rebuilt as `plan.To`. True when the twin stands where the car stood. False when the game would not build it: the car is
        // then back as it was (or, if the game would not take even that, a warning says so) and nothing may be charged.
        private static bool Swap(Plan plan, out Car now, out string kept)
        {
            now = null; kept = "-";
            TrainController tc = TrainController.Shared;
            Car car = plan.Car;
            string id = car.id;
            // everything is read before the car is touched
            Snapshot.Car original = car.Snapshot();
            Dictionary<string, IPropertyValue> props = PropertyValueConverter.RuntimeToSnapshot(car.KeyValueObject.Dictionary);
            float line = car.air.BrakeLine.Pressure, reservoir = car.air.BrakeReservoir.Pressure, cylinder = car.air.BrakeCylinder.Pressure;
            Dictionary<string, List<string>> lists = ListsWith(id);
            bool selected = ReferenceEquals(tc.SelectedCar, car);
            Car beforeA = car.set == null ? null : car.CoupledTo(Car.LogicalEnd.A), beforeB = car.set == null ? null : car.CoupledTo(Car.LogicalEnd.B);
            Snapshot.Car twin = original;
            twin.definitionIdentifier = plan.To;
            string failType = null, failText = null;
            bool restored = false;
            using (StateManager.TransactionScope())
            {
                tc.RemoveCar(id);
                if (tc.TryGetCarForId(id, out _))
                {   // the game kept the car (the message was refused): nothing happened
                    if (DebugLog.On) DebugLog.Event(Main.Id, "refit-failed", "car", id, "to", plan.To, "type", "not-removed", "restored", true);
                    Main.WarnOnce("refit-remove", "Passenger Classes: the game would not take the car off the track for its refit; nothing was changed or charged");
                    return false;
                }
                try
                {
                    now = Place(tc, twin, props);
                    if (now == null || now.id != id || now.DefinitionInfo == null || now.DefinitionInfo.Identifier != plan.To) throw new InvalidOperationException("the game built another car than the one asked for");
                }
                catch (Exception e)
                {
                    failType = e.GetType().Name; failText = PathClean.CleanText(e.Message); now = null;
                    try
                    {
                        if (tc.TryGetCarForId(id, out _)) tc.RemoveCar(id);      // a half-built twin
                        try { now = Place(tc, original, props); }
                        catch (Exception)
                        {   // the road number may still be held by the half-built twin: the car comes back under a new one
                            if (tc.TryGetCarForId(id, out _)) tc.RemoveCar(id);
                            Snapshot.Car renumbered = original; renumbered.roadNumber = null;
                            now = Place(tc, renumbered, props);
                        }
                        restored = now != null;
                    }
                    catch (Exception) { restored = false; }
                }
            }
            if (now != null)
            {
                try
                {
                    now.air.BrakeLine.Pressure = line; now.air.BrakeReservoir.Pressure = reservoir; now.air.BrakeCylinder.Pressure = cylinder;
                    if (lists != null) foreach (KeyValuePair<string, List<string>> l in lists) OpsController.Shared.SwitchListController.SetSwitchListCarIds(l.Key, l.Value, true);
                    if (selected) tc.SelectedCar = now;
                }
                catch (Exception e)
                {
                    if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "refit", "what", "after the swap", "type", e.GetType().Name, "n", _failures);
                    Main.WarnOnce("refit-after", "Passenger Classes: the refitted car's air, switch list entry or selection could not be put back: " + PathClean.CleanText(e.Message));
                }
            }
            if (failType != null)
            {
                _failures++;
                if (DebugLog.On) DebugLog.Event(Main.Id, "refit-failed", "car", id, "to", plan.To, "type", failType, "restored", restored);
                Main.WarnOnce("refit-build", restored
                    ? "Passenger Classes: the game would not build the refitted car, so the car was put back as it was and nothing was charged: " + failText
                    : "Passenger Classes: the game would not build the refitted car and would not put the old one back; nothing was charged, load your last save: " + failText);
                if (_failures >= 3 && Live) { Live = false; Main.WarnOnce("refit-down", "Passenger Classes: the refit entries stand down until the next world load"); }
                Note(restored ? "The refit failed. The car is as it was." : "The refit failed.");
                now = null;
                return false;
            }
            bool a = Same(now, Car.LogicalEnd.A, beforeA), b = Same(now, Car.LogicalEnd.B, beforeB);
            kept = a && b ? "kept" : "parted";
            if (!(a && b)) Main.WarnOnce("refit-parted", "Passenger Classes: a refitted car did not rejoin its train; couple it again");
            return true;
        }

        private static bool Same(Car now, Car.LogicalEnd end, Car before)
        {
            Car after;
            try { after = now.set == null ? null : now.CoupledTo(end); } catch (Exception) { return false; }      // a car the game has in no train
            return ReferenceEquals(after, before);
        }

        // One car onto the track from a snapshot, the way RebuildCars does it, with the clients told in the order they need: the car first
        // (AddCars), then its properties (the deferred observers are what send them), then the train it belongs to.
        private static Car Place(TrainController tc, Snapshot.Car snapshot, Dictionary<string, IPropertyValue> props)
        {
            Car car = _add(tc, snapshot, props, 1, true);
            if (Multiplayer.IsClientActive) StateManager.ApplyLocal(new AddCars(new List<Snapshot.Car> { car.Snapshot() }));
            car.FirePendingKeyValueObservers();
            _updateSets(tc, new HashSet<Car> { car });
            return car;
        }

        // The switch lists the car is on: crew id to that list's car ids in their order. Null when it is on none.
        private static Dictionary<string, List<string>> ListsWith(string carId)
        {
            OpsController ops = OpsController.Shared;
            SwitchListController lists = ops == null ? null : ops.SwitchListController;
            if (lists == null) return null;
            Snapshot snapshot = default(Snapshot);
            lists.PopulateSnapshot(ref snapshot);
            if (snapshot.SwitchLists == null) return null;
            Dictionary<string, List<string>> found = null;
            foreach (KeyValuePair<string, SwitchList> kv in snapshot.SwitchLists)
            {
                List<SwitchList.Entry> entries = kv.Value.Entries;
                if (entries == null) continue;
                bool has = false;
                foreach (SwitchList.Entry entry in entries) if (entry.CarId == carId) { has = true; break; }
                if (!has) continue;
                var ids = new List<string>(entries.Count);
                foreach (SwitchList.Entry entry in entries) ids.Add(entry.CarId);
                (found ?? (found = new Dictionary<string, List<string>>()))[kv.Key] = ids;
            }
            return found;
        }
    }
}
