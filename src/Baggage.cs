using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Model;
using Model.Definition.Data;
using Model.Ops;

namespace PassengerClasses
{
    // Checked baggage (switched off by default; Logic.BaggageRiders). A rider's bags follow the game's fare band and the class of the car they sit in
    // (Logic.Allowance). Each car keeps two sums for its riders aboard: their normal bags and their excess bags (on average: the share who bring
    // double). The car's own racks (seats x its class's rack figure) take its riders' normal bags; its small luggage area takes what passes the racks
    // (normal bags first, then the excess); what is left, is the car's overflow, and the overflow of the whole train must fit the baggage rooms of its cars (a baggage car's 40,000 lb, a combine's baggage end, a
    // pack's declared figure). A rider whose bags would not fit waits on the platform for the next train, in the crowd where they were.
    // The sums are kept per car, added to as riders board and made again from the riders aboard each time the car is served (so unloading, and
    // a loaded game, are right from the next service on). Nothing is saved.
    internal static class Baggage
    {
        private sealed class State { internal double Normal, Excess; }
        private static readonly Dictionary<Car, State> Cars = new Dictionary<Car, State>();
        private static readonly List<Car> Scratch = new List<Car>();

        internal static void ResetWorld() { Cars.Clear(); Scratch.Clear(); _fracCar = null; }

        // The cars of the train `car` is in, head to tail, in a list reused.
        internal static List<Car> TrainOf(Car car)
        {
            Scratch.Clear();
            Car first = car, next;
            int guard = 0;
            while (guard++ < 10000 && (next = first.CoupledTo(Car.LogicalEnd.A)) != null) first = next;
            for (Car c = first; c != null && Scratch.Count < 10000; c = c.CoupledTo(Car.LogicalEnd.B)) Scratch.Add(c);
            return Scratch;
        }

        // The car's riders' bags, from the riders aboard now (a rider's class is the class of the car they sit in).
        // PassengerMarker.Groups is a public List<PassengerGroup> of structs with Origin, Destination (stop ids) and Count; GetPassengerMarker(car) is null for a car with no marker [load]
        internal static void Recompute(Car car)
        {
            State st;
            if (!Cars.TryGetValue(car, out st)) Cars[car] = st = new State();
            st.Normal = 0; st.Excess = 0;
            PassengerMarker? m = car.GetPassengerMarker();
            if (!m.HasValue) return;
            Classes.Resolved r = Boarding.ModelOf(car);
            int cls = r == null ? 0 : r.BoardClass;
            List<PassengerGroup> groups = m.Value.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                PassengerGroup g = groups[i];
                if (g.Origin == null || g.Destination == null || g.Count <= 0) continue;
                int a = AllowanceOf(g.Origin, g.Destination, cls);
                st.Normal += g.Count * (double)a;
                st.Excess += g.Count * Logic.ExcessLb(a);
            }
        }

        private static State StateOf(Car car)
        {
            State st;
            if (!Cars.TryGetValue(car, out st)) { Recompute(car); st = Cars[car]; }
            return st;
        }

        // The car's racks by its class, its luggage area, and the two together (what its own riders' normal bags may fill before they overflow into a baggage room).
        internal static double Racks(Car car) { Classes.Resolved r = Boarding.ModelOf(car); return r == null ? 0 : Logic.RackLb(r.Seats, r.BoardClass); }
        internal static double Luggage(Car car) { Classes.Resolved r = Boarding.ModelOf(car); return r == null ? 0 : Logic.LuggageLb(r.Seats); }
        internal static double Rack(Car car) => Racks(car) + Luggage(car);
        private static int SeatsOfCar(Car car) { Classes.Resolved r = Boarding.ModelOf(car); return r == null ? 0 : r.Seats; }
        internal static double Compartment(Car car) { Classes.Resolved r = Boarding.ModelOf(car); return r == null ? 0 : r.CompartmentLb; }

        // The baggage room of the train left, after every car's normal-bag overflow (can be below zero); excess is the train's excess bags.
        // `excess` is the part of the train's excess bags no car's luggage area took.
        internal static double Headroom(List<Car> train, out double excess)
        {
            double room = 0, over = 0;
            excess = 0;
            for (int i = 0; i < train.Count; i++)
            {
                Car c = train[i];
                room += Compartment(c);
                State st = StateOf(c);
                over += Logic.NormalOverflow(st.Normal, Rack(c));
                excess += Logic.ExcessOverflow(st.Normal, Racks(c), Luggage(c), st.Excess);
            }
            return room - over;
        }

        // The share of the serving car's train's excess bags that have room (cached for the car being served).
        private static Car _fracCar; private static double _frac;
        internal static void StartServe() { _fracCar = null; }
        internal static double CarriedShare(Car car)
        {
            if (ReferenceEquals(_fracCar, car)) return _frac;
            double excess;
            double spare = Headroom(TrainOf(car), out excess);
            State st = StateOf(car);
            _frac = Logic.CarCarried(st.Excess, Logic.ExcessOverflow(st.Normal, Racks(car), Luggage(car), st.Excess), Logic.ExcessCarried(spare, excess)); _fracCar = car;
            return _frac;
        }

        // The allowance of a rider by the one rule used everywhere, as the fare does: the rider's origin to their destination.
        private static int AllowanceOf(string origin, string dest, int cls) => Logic.Allowance(Logic.FareBand(Boarding.MilesBetween(origin, dest)), cls);

        // How many of `wanted` riders waiting at `stop` for `dest` the serving car can take. The waiting riders came from different stops (some are
        // changing trains), so the allowance counted is the largest among the groups waiting for `dest`: the oldest group is the next to board.
        internal static double LastHeadroom; internal static int LastAllowance;   // the last Fit's inputs, for the debug log
        internal static int Fit(PassengerStop stop, Car car, int cls, string dest, int wanted)
        {
            int a = 0;
            if (stop.Waiting.TryGetValue(dest, out PassengerStop.WaitingInfo wi) && wi.Groups != null)
                for (int i = 0; i < wi.Groups.Count; i++)
                {
                    int g = AllowanceOf(wi.Groups[i].Origin ?? stop.identifier, dest, cls);
                    if (g > a) a = g;
                }
            LastAllowance = a; LastHeadroom = 0;
            if (a <= 0) return wanted;
            State st = StateOf(car);
            double excess;
            LastHeadroom = Headroom(TrainOf(car), out excess);
            return Logic.FitCount(LastHeadroom, st.Normal, Rack(car), a, wanted);
        }

        // `n` riders from `origin` to `dest` boarded the car.
        internal static void Boarded(Car car, int cls, string origin, string dest, int n)
        {
            if (n <= 0) return;
            int a = AllowanceOf(origin, dest, cls);
            State st = StateOf(car);
            st.Normal += n * (double)a;
            st.Excess += n * Logic.ExcessLb(a);
            if (DebugLog.On) DebugLog.Event(Main.Id, "bag-board", "car", car?.id, "class", Boarding.ClassWord(cls), "from", origin, "to", dest, "riders", n, "allowance", a,
                                            "normalLb", st.Normal, "excessLb", st.Excess, "rackLb", Racks(car), "luggageLb", Luggage(car), "seats", SeatsOfCar(car));
        }

        // For the feed: bags aboard (normal and excess) and the room the car's own racks and baggage compartment give.
        internal static void Read(Car car, out double used, out double room)
        {
            Recompute(car);
            State st = Cars[car];
            used = st.Normal + st.Excess;
            room = Rack(car) + Compartment(car);   // the racks, the luggage area and the baggage compartment
        }

        // The room a model gives and what a baggage slot is: pounds of any load (a combine's baggage end, a baggage-express car).
        // CarDefinition.LoadSlots: LoadSlot.LoadUnits Pounds with an empty RequiredLoadIdentifier is a slot any load fits (the combine's 12,000 lb, the baggage-express car's 40,000 lb) [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int SlotPounds(CarDefinition def)
        {
            float lb = 0;
            foreach (LoadSlot slot in def.LoadSlots)
                if (slot.LoadUnits == LoadUnits.Pounds && string.IsNullOrEmpty(slot.RequiredLoadIdentifier)) lb += slot.MaximumCapacity;
            return (int)lb;
        }

        // Is there a lounge car in the train this car is in?
        internal static bool TrainHasLounge(Car car)
        {
            List<Car> train = TrainOf(car);
            for (int i = 0; i < train.Count; i++)
            {
                Classes.Resolved r = Boarding.ModelOf(train[i]);
                if (r != null && r.Cls == Classes.Lounge) return true;
            }
            return false;
        }
    }
}
