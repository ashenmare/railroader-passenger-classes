using System;
using System.Collections.Generic;

namespace PassengerClasses
{
    // What class a car model has, and where that comes from. No game type here: the hooks hand in the model's definition id, its car type
    // text and whether it has a passenger slot, and get back one answer per model. tests/LogicCheck.cs checks it against hand-worked cases.
    //
    // First match wins, one field at a time (class, observation, platform end, baggage):
    //   1. the player's own choice for the model (Choices.cs, set from the car's right-click menu),
    //   2. the pack's PassengerClasses.json (PackFile.cs), unless the player turned pack files off,
    //   3. the built-in table: the stock cars, the First Class twins, and the type codes,
    //   4. a car with a passenger slot is a coach; anything else has no class.
    internal static class Classes
    {
        // The classes. None is "not a passenger or baggage car".
        internal const int None = 0, Coach = 1, Parlor = 2, Sleeper = 3, Lounge = 4, Baggage = 5;
        internal static readonly string[] Words = { "none", "coach", "parlor", "sleeper", "lounge", "baggage" };

        // The word a file or a choice uses for a class (coach to baggage, or none), any case; -1 for anything else (an unknown word is ignored, never an error).
        internal static int ParseWord(string w)
        {
            if (w == null) return -1;
            if (string.Equals(w.Trim(), Words[None], StringComparison.OrdinalIgnoreCase)) return None;   // "none": the model has no class
            for (int i = Coach; i < Words.Length; i++) if (string.Equals(w.Trim(), Words[i], StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // What one source says about a model. -1 (NaN for the one real number) is "not said". Every field a pack may set is kept
        // whether or not 0.1 acts on it: that is how a later version reads the same file.
        internal sealed class Decl
        {
            internal int Cls = -1;                      // a class above
            internal int Obs = -1;                      // 1 an observation car, 0 not one
            internal int AtOtherEnd = -1;               // 1 the platform is at the car's F end (the game's test is "not F")
            internal int Seats = -1;                    // the seats the car has as a parlor or a sleeper, when the pack says (wins over the conversion percent)
            internal int BaggageLb = -1;                // pounds of luggage the car carries in its own racks (carried, not used in 0.1)
            internal int FarePercent = -1;              // carried, not used in 0.1
            internal double BonusAmount = double.NaN;   // carried, not used in 0.1 (a fare multiplier; 1.0 is no change)
            internal int StartHour = -1, EndHour = -1, MinCars = -1, MaxCars = -1, MinOfType = -1, MaxOfType = -1;   // carried, not used in 0.1
        }

        // The answer for one model. Where each field came from: 0 default, 1 built-in, 2 the pack's file, 3 the player.
        internal sealed class Resolved
        {
            internal string Id;
            internal int Cls, ClsFrom;
            internal int Seats, NativeSeats, PackSeats = -1, NativeCls, CompartmentLb;   // the car's seats; pounds its baggage compartment holds (the declared figure, else a baggage slot; 0 for a car with no class)
            internal bool Obs, AtOtherEnd;
            internal int ObsFrom, BaggageLb;
            // True for a car the game's own brake rule would treat as a freight car: any class at all is a coach or baggage car.
            internal bool CoachBrakes => Cls != None;
            // 0 coach (or anything unclassed), 1 first class, 2 sleeper: what Boarding serves.
            internal int BoardClass => Cls == Parlor ? 1 : Cls == Sleeper ? 2 : 0;
        }

        // ---- the built-in table ----

        // One row of the built-in table: a model the mod knows by id, its class and its baggage figure (pounds of luggage the car's own racks hold;
        // -1 unset). The figures for the stock cars are left unset until the baggage rule is designed; the column is read and carried now.
        internal sealed class Known
        {
            internal readonly string Id; internal readonly int Cls, BaggageLb;
            internal Known(string id, int cls, int baggageLb) { Id = id; Cls = cls; BaggageLb = baggageLb; }
        }

        // The four stock passenger cars (coaches; the observation car also by its type). The twins are known by their id prefix.
        internal static readonly List<Known> Stock = new List<Known>
        {
            new Known("rr/pb-pullman-lw-wood-19xx", Coach, -1), new Known("rr/pb-osgbrad-lightweight-steel-1915", Coach, -1),
            new Known("rr/pb-pullman-heavyweight-early", Coach, -1), new Known("rr/pbo-pullman-heavyweight-early", Coach, -1),
            new Known("rr/cbs-baggage01", Baggage, 40000),     // its slot says 1,000 of a mail load nothing defines; the baggage room is the 40,000 lb of the design
        };

        internal static bool IsStock(string id)
        {
            foreach (Known k in Stock) if (k.Id == id) return true;
            return false;
        }

        internal static bool IsBaggageType(string t) =>
            t != null && (t.StartsWith("CB", StringComparison.Ordinal) || t == "BE" || t == "BM" || t == "BH");   // CB*: the game's own baggage prefix; BE, BM, BH: exact codes

        // What the table says about a model: its id (the stock and twin cars), then its car type text. Returns a new Decl each call;
        // it runs once per model.
        internal static Decl BuiltIn(string id, string carType, bool hasPassengerSlot)
        {
            var d = new Decl();
            int byId = Logic.ClassOfId(id);                 // twins: first class 1 -> parlor, sleeper 2
            if (byId == 1) d.Cls = Parlor;
            else if (byId == 2) d.Cls = Sleeper;
            else
            {
                foreach (Known k in Stock) if (k.Id == id) { d.Cls = k.Cls; d.BaggageLb = k.BaggageLb; break; }
            }
            if (carType != null)
            {
                switch (carType)
                {
                    case "PBO": d.Obs = 1; break;
                    case "PO": d.Obs = 1; if (d.Cls < 0) d.Cls = Lounge; break;
                    case "DLC": if (d.Cls < 0) d.Cls = Lounge; break;
                    case "PPO": d.Obs = 1; if (d.Cls < 0) d.Cls = Parlor; break;
                    case "PPL": if (d.Cls < 0) d.Cls = Parlor; break;
                }
                if (d.Cls < 0 && IsBaggageType(carType)) d.Cls = Baggage;
            }
            return d;
        }

        // ---- the answer ----

        // First set wins, per field. `pack` is null when the model has no entry or pack files are off; `player` null for no choice.
        internal static Resolved Resolve(string id, string carType, bool hasPassengerSlot, Decl player, Decl pack, int seats = 0, int slotLb = 0)
        {
            Decl b = BuiltIn(id, carType, hasPassengerSlot);
            var r = new Resolved { Id = id };
            int nat = pack != null && pack.Cls >= 0 ? pack.Cls : b.Cls >= 0 ? b.Cls : hasPassengerSlot ? Coach : None;   // the class without the player's say
            if (player != null && player.Cls >= 0) { r.Cls = player.Cls; r.ClsFrom = 3; }
            else if (pack != null && pack.Cls >= 0) { r.Cls = pack.Cls; r.ClsFrom = 2; }
            else if (b.Cls >= 0) { r.Cls = b.Cls; r.ClsFrom = 1; }
            else { r.Cls = hasPassengerSlot ? Coach : None; r.ClsFrom = 0; }
            if (player != null && player.Obs >= 0) { r.Obs = player.Obs == 1; r.ObsFrom = 3; }
            else if (pack != null && pack.Obs >= 0) { r.Obs = pack.Obs == 1; r.ObsFrom = 2; }
            else if (b.Obs >= 0) { r.Obs = b.Obs == 1; r.ObsFrom = 1; }
            r.AtOtherEnd = pack != null && pack.AtOtherEnd == 1;
            r.BaggageLb = pack != null && pack.BaggageLb >= 0 ? pack.BaggageLb : b.BaggageLb;
            r.NativeSeats = seats; r.PackSeats = pack != null ? pack.Seats : -1; r.NativeCls = nat;
            r.Seats = SeatsFor(r.Cls, r.ClsFrom, nat, seats, r.PackSeats);
            r.CompartmentLb = r.Cls == None ? 0 : r.BaggageLb >= 0 ? r.BaggageLb : slotLb;
            return r;
        }

        // The seats a model has in its class. A pack that states seats for a parlor or a sleeper wins. A model the player turns into a parlor or a sleeper,
        // from a class that isn't one already, has a share of its seats (Logic.Convert*Percent); a car built as a parlor or sleeper, and any other class, keeps
        // the seats its definition has.
        internal static int SeatsFor(int cls, int clsFrom, int nativeCls, int nativeSeats, int packSeats)
        {
            bool premium = cls == Parlor || cls == Sleeper;
            if (!premium) return nativeSeats;
            if (packSeats >= 1) return packSeats;
            if (clsFrom == 3 && nativeCls != Parlor && nativeCls != Sleeper) return Logic.ConvertedSeats(nativeSeats, cls == Parlor ? Logic.ConvertParlorPercent : Logic.ConvertSleeperPercent);
            return nativeSeats;
        }

        // The seats each model's definition had before this mod changed them (the first look), by definition id.
        internal static readonly Dictionary<string, int> Native = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- the models' answers, kept ----
        // One answer per definition id, made on first use and kept until the player's choices, the pack files or a setting change, or
        // a world loads. A lookup allocates nothing.
        private static readonly Dictionary<string, Resolved> Cache = new Dictionary<string, Resolved>(StringComparer.Ordinal);
        internal static int Revision;                       // counts every reset: the feed and the tests can see a change
        // The pack file's entry for a model, set at mod load by the code that knows the game's pack list (null: no files).
        internal static Func<string, Decl> PackEntryFor;

        internal static void Reset() { Cache.Clear(); Revision++; }

        internal static bool TryCached(string id, out Resolved r) => Cache.TryGetValue(id, out r);

        // The player's choices as Choices.cs holds them.
        internal static readonly Dictionary<string, Decl> Player = new Dictionary<string, Decl>(StringComparer.Ordinal);

        internal static Resolved Get(string id, string carType, bool hasPassengerSlot, int seats = 0, int slotLb = 0)
        {
            Resolved r;
            if (Cache.TryGetValue(id, out r)) return r;
            Decl player; Player.TryGetValue(id, out player);
            Decl pack = null;
            if (Logic.ReadPackFiles && PackEntryFor != null) pack = PackEntryFor(id);
            r = Resolve(id, carType, hasPassengerSlot, player, pack, seats, slotLb);
            Cache[id] = r;
            return r;
        }

        // ---- the question asked before a choice changes every car of a model ----

        // The text of the confirmation: the model's name and how many cars of it are on the railroad. cls is the new class (a class number, -1 default,
        // -2 not changed), obs the new observation choice (1 on, 0 off, -1 default, -2 not changed). Plain, short, no colons or semicolons.
        internal static string ConfirmText(string model, int cls, int obs, int count, int seats = -1)
        {
            string tail = (seats >= 0 ? " " + seats + (cls == Sleeper ? " berths each." : " seats each.") : "") + " " + count + " on your railroad.";
            if (cls == -1 && obs == -1) return "Put every " + model + " back to its default class?" + tail;
            if (cls == None) return "Take the class off every " + model + "?" + tail;
            if (cls >= Coach && cls < Words.Length) return "Make every " + model + (cls == Baggage ? " a baggage car" : " a " + Words[cls]) + "?" + tail;
            if (obs == 1) return "Make every " + model + " an observation car?" + tail;
            if (obs == 0) return "Stop every " + model + " counting as an observation car?" + tail;
            return "Change every " + model + "?" + tail;
        }

        // ---- the player's choice, as a rule on the table ----

        // Sets one model's class choice: cls a class, or -1 to go back to default.
        internal static void ChooseClass(string id, int cls)
        {
            Decl d; if (!Player.TryGetValue(id, out d)) Player[id] = d = new Decl();
            d.Cls = cls;
            Tidy(id, d);
        }

        // Sets one model's observation choice: 1 on, 0 off, -1 default.
        internal static void ChooseObservation(string id, int obs)
        {
            Decl d; if (!Player.TryGetValue(id, out d)) Player[id] = d = new Decl();
            d.Obs = obs;
            Tidy(id, d);
        }

        private static void Tidy(string id, Decl d)
        {
            if (d.Cls < 0 && d.Obs < 0) Player.Remove(id);
            Reset();
        }
    }
}
