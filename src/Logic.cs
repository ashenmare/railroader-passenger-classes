using System;
using System.Collections.Generic;

namespace PassengerClasses
{
    // Everything the boarding, the fares and the prices decide that needs no game type: the twin ids, the price rule, the setting
    // limits, the share model (how many of a crowd are first-class riders), the sleeper distance rule, the observation bonus, the
    // coach brake and the fare extra. The car classes are in Classes.cs. tests/LogicCheck.cs compiles these files without the game and
    // checks them against hand-worked numbers. The hooks (Boarding.cs, Fares.cs, Pricing.cs, ...) only move game values in and out of them.
    internal static class Logic
    {
        // ---- the cars ----

        internal const string ClonePrefix = "ashenmare/fc-", SleeperPrefix = "ashenmare/sl-";

        // [base id, clone id]; must match Scripts/Data.rl (tests/LogicCheck.cs parses the script and compares). The prefix says the class.
        internal static readonly string[][] Cars =
        {
            new[] { "rr/pb-pullman-lw-wood-19xx", "ashenmare/fc-pb-pullman-lw-wood-19xx" },
            new[] { "rr/pb-osgbrad-lightweight-steel-1915", "ashenmare/fc-pb-osgbrad-lightweight-steel-1915" },
            new[] { "rr/pb-pullman-heavyweight-early", "ashenmare/fc-pb-pullman-heavyweight-early" },
            new[] { "rr/pbo-pullman-heavyweight-early", "ashenmare/fc-pbo-pullman-heavyweight-early" },
            new[] { "rr/pb-pullman-heavyweight-early", "ashenmare/sl-pb-pullman-heavyweight-early" },
        };

        internal static bool IsFirstClassId(string id) => id != null && id.StartsWith(ClonePrefix, StringComparison.Ordinal);
        internal static bool IsSleeperId(string id) => id != null && id.StartsWith(SleeperPrefix, StringComparison.Ordinal);

        // 0 a coach (or anything else), 1 first class, 2 sleeper.
        internal static int ClassOfId(string id) => IsSleeperId(id) ? 2 : IsFirstClassId(id) ? 1 : 0;

        // The base car's id for a clone id, or null.
        internal static string BaseOf(string cloneId)
        {
            foreach (string[] c in Cars) if (c[1] == cloneId) return c[0];
            return null;
        }

        // ---- the settings ----

        internal const int DefaultSharePercent = 15, MinSharePercent = 0, MaxSharePercent = 100;
        internal const int DefaultFarePercent = 200, MinFarePercent = 100, MaxFarePercent = 500;
        internal const int DefaultPricePercent = 120, MinPricePercent = 100, MaxPricePercent = 300;
        internal const int DefaultSleeperSharePercent = 6, DefaultSleeperFarePercent = 350, DefaultSleeperPricePercent = 140;

        // What Data.rl bakes in for the seats, as a percent of the stock car's (not settings: a definition is read once at load).
        internal const int FirstClassSeatPercent = 70, SleeperSeatPercent = 70;

        // The observation bonus is the game's 1.2 (CalculateBonusMultiplier); the sleeper rule is the fare table's top end, where the
        // game's fare stops growing (50 miles) brought down to 30.
        internal const int DefaultObservationPercent = 120, MinObservationPercent = 100, MaxObservationPercent = 300;
        internal const int DefaultSleeperMiles = 30, MinSleeperMiles = 0, MaxSleeperMiles = 200;

        internal static int SharePercent = DefaultSharePercent, FarePercent = DefaultFarePercent, PricePercent = DefaultPricePercent;
        internal static int SleeperSharePercent = DefaultSleeperSharePercent, SleeperFarePercent = DefaultSleeperFarePercent, SleeperPricePercent = DefaultSleeperPricePercent;
        internal static int ObservationPercent = DefaultObservationPercent, SleeperMiles = DefaultSleeperMiles;

        // A car model the player turns into a parlor or a sleeper (not one built as that class) gets that class's real room: a share of its seats.
        internal const int DefaultConvertParlorPercent = 70, DefaultConvertSleeperPercent = 50, MinConvertPercent = 10, MaxConvertPercent = 100;
        internal static int ConvertParlorPercent = DefaultConvertParlorPercent, ConvertSleeperPercent = DefaultConvertSleeperPercent;
        internal static void ApplyConversion(int parlor, int sleeper)
        {
            ConvertParlorPercent = Clamp(parlor, MinConvertPercent, MaxConvertPercent);
            ConvertSleeperPercent = Clamp(sleeper, MinConvertPercent, MaxConvertPercent);
        }
        // The seats of a converted model: its seats times the class's percent, rounded down, at least one (as the twins' Data.rl does).
        internal static int ConvertedSeats(int seats, int percent) => Math.Max(1, (int)((long)seats * Clamp(percent, MinConvertPercent, MaxConvertPercent) / 100));
        // Pack files are read unless the player turns it off. The two round-2 switches are kept and shown, and nothing reads them yet.
        internal static bool ReadPackFiles = true, LoungePatience, BaggageRiders;
        // The car right-click menu's class entries (off by default: the game's menu stands). Classes already chosen work either way.
        internal static bool ClassMenu;

        // Baggage (off by default). A rider's checked bags by the game's fare band (the $1 to $8 distance fare) and the class of the car they sit in:
        // $1 and $2 none; $3 to $5 the near figure; $6 to $8 the near figure times the far percent. A share of riders bring double (the excess),
        // and the excess pays a fee per 100 lb. Each car's seats carry their own riders' normal bags (the rack figure a seat); a baggage car or a
        // combine's baggage end holds what overflows. The lounge rule: the hours a lounge car in the train adds to the riders who change trains.
        internal const int DefaultBagNearCoach = 25, DefaultBagNearFirst = 50, DefaultBagNearSleeper = 100, MinBagLb = 0, MaxBagLb = 1000;
        internal const int DefaultBagFarPercent = 200, MinBagFarPercent = 100, MaxBagFarPercent = 500;
        internal const int DefaultBagExcessPercent = 20, MinBagExcessPercent = 0, MaxBagExcessPercent = 100;
        internal const int DefaultBagFeePer100 = 2, MinBagFeePer100 = 0, MaxBagFeePer100 = 100;
        // The racks by class (lb a seat; a berth for a sleeper) and the small luggage area every passenger car has for its own riders' bags past the racks.
        internal const int DefaultBagRackLb = 30, DefaultBagRackParlorLb = 60, DefaultBagRackSleeperLb = 120, MinBagRackLb = 0, MaxBagRackLb = 300;
        internal const int DefaultBagLuggageLb = 500, MinBagLuggageLb = 0, MaxBagLuggageLb = 2000;
        internal const int DefaultLoungeHours = 1, MinLoungeHours = 0, MaxLoungeHours = 12;
        internal static int BagNearCoach = DefaultBagNearCoach, BagNearFirst = DefaultBagNearFirst, BagNearSleeper = DefaultBagNearSleeper, BagFarPercent = DefaultBagFarPercent;
        internal static int BagExcessPercent = DefaultBagExcessPercent, BagFeePer100 = DefaultBagFeePer100, BagRackLb = DefaultBagRackLb, LoungeHours = DefaultLoungeHours;
        internal static int BagRackParlorLb = DefaultBagRackParlorLb, BagRackSleeperLb = DefaultBagRackSleeperLb, BagLuggageLb = DefaultBagLuggageLb;

        internal static void ApplyBaggage(int nearCoach, int nearFirst, int nearSleeper, int farPercent, int excessPercent, int feePer100, int rackLb, int rackParlorLb, int rackSleeperLb, int luggageLb, int loungeHours)
        {
            BagNearCoach = Clamp(nearCoach, MinBagLb, MaxBagLb); BagNearFirst = Clamp(nearFirst, MinBagLb, MaxBagLb); BagNearSleeper = Clamp(nearSleeper, MinBagLb, MaxBagLb);
            BagFarPercent = Clamp(farPercent, MinBagFarPercent, MaxBagFarPercent); BagExcessPercent = Clamp(excessPercent, MinBagExcessPercent, MaxBagExcessPercent);
            BagFeePer100 = Clamp(feePer100, MinBagFeePer100, MaxBagFeePer100); BagRackLb = Clamp(rackLb, MinBagRackLb, MaxBagRackLb);
            BagRackParlorLb = Clamp(rackParlorLb, MinBagRackLb, MaxBagRackLb); BagRackSleeperLb = Clamp(rackSleeperLb, MinBagRackLb, MaxBagRackLb);
            BagLuggageLb = Clamp(luggageLb, MinBagLuggageLb, MaxBagLuggageLb);
            LoungeHours = Clamp(loungeHours, MinLoungeHours, MaxLoungeHours);
        }

        internal static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        internal static void Apply(int share, int fare, int price, int sleeperShare, int sleeperFare, int sleeperPrice,
                                   int observation, int sleeperMiles, bool readPackFiles, bool loungePatience, bool baggageRiders)
        {
            ObservationPercent = Clamp(observation, MinObservationPercent, MaxObservationPercent);
            SleeperMiles = Clamp(sleeperMiles, MinSleeperMiles, MaxSleeperMiles);
            ReadPackFiles = readPackFiles; LoungePatience = loungePatience; BaggageRiders = baggageRiders;
            SharePercent = Clamp(share, MinSharePercent, MaxSharePercent);
            FarePercent = Clamp(fare, MinFarePercent, MaxFarePercent);
            PricePercent = Clamp(price, MinPricePercent, MaxPricePercent);
            SleeperSharePercent = Clamp(sleeperShare, MinSharePercent, MaxSharePercent);
            SleeperFarePercent = Clamp(sleeperFare, MinFarePercent, MaxFarePercent);
            SleeperPricePercent = Clamp(sleeperPrice, MinPricePercent, MaxPricePercent);
        }

        // ---- the price and the seats ----

        // The listed price of a clone: the base's, times the percent, to the nearest $10 (halves up). Data.rl does the same
        // sum on its own, with the default percent, and the two are checked to agree.
        internal static int Price(int baseListed, int percent) => (int)(((long)baseListed * percent + 500) / 1000) * 10;

        // The seats of a clone: the stock car's, times the percent, rounded down, at least one. Data.rl does the same sum.
        internal static int Seats(int baseSeats, int percent) => Math.Max(1, (int)((long)baseSeats * percent / 100));

        // ---- the refit (a coach rebuilt as its first-class or sleeper twin at a repair track, and back) ----

        internal const int DefaultRefitLaborPercent = 10, MinRefitLaborPercent = 0, MaxRefitLaborPercent = 100;
        internal const int DefaultRefitLaborMinimum = 250, MinRefitLaborMinimum = 0, MaxRefitLaborMinimum = 5000;
        internal static int RefitLaborPercent = DefaultRefitLaborPercent, RefitLaborMinimum = DefaultRefitLaborMinimum;
        internal static void ApplyRefit(int laborPercent, int laborMinimum)
        {
            RefitLaborPercent = Clamp(laborPercent, MinRefitLaborPercent, MaxRefitLaborPercent);
            RefitLaborMinimum = Clamp(laborMinimum, MinRefitLaborMinimum, MaxRefitLaborMinimum);
        }

        // The twin of a stock car in a class (1 first class, 2 sleeper), or null when this mod has none.
        internal static string TwinOf(string baseId, int cls)
        {
            foreach (string[] c in Cars) if (c[0] == baseId && ClassOfId(c[1]) == cls) return c[1];
            return null;
        }

        // The shop's labor: a percent of the twin's price in whole dollars (halves up), never under the minimum. The same both ways.
        internal static int RefitLabor(int twinPrice, int percent, int minimum) => Math.Max(minimum, (int)(((long)Math.Max(0, twinPrice) * percent + 50) / 100));

        // What the twin costs over the stock car: paid on the way to the twin, never given back on the way to a coach (no money loop).
        internal static int RefitDifference(int stockPrice, int twinPrice, bool toTwin) => toTwin ? Math.Max(0, twinPrice - stockPrice) : 0;

        internal static int RefitFee(int stockPrice, int twinPrice, bool toTwin, int percent, int minimum) => RefitDifference(stockPrice, twinPrice, toTwin) + RefitLabor(twinPrice, percent, minimum);

        internal static string Money(int amount) => "$" + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        // The words for a class of ClassOfId (0 coach, 1 first class, 2 sleeper): the menu entry, the ledger memo and the question.
        internal static string RefitWord(int cls) => cls == 2 ? "sleeper" : cls == 1 ? "first class" : "coach";
        internal static string RefitEntry(int cls) => cls == 0 ? "Refit back to coach" : "Refit as " + RefitWord(cls);
        internal static string RefitMemo(string car, int from, int to) => "Refit: " + car + " " + RefitWord(from) + " to " + RefitWord(to);
        internal static string RefitQuestion(string car, int to, int fee, int seats) =>
            "Refit " + car + (to == 0 ? " back to a coach" : " as " + (to == 2 ? "a sleeper" : "first class")) + " for " + Money(fee) + "?" + (seats > 0 ? " " + seats + (to == 2 ? " berths." : " seats.") : "");

        // ---- the riders ----

        // What the mod knows about one destination's crowd at one station: how many of the people waiting (Seen) it last counted,
        // how many of those it counts as first-class riders (Fc) and as sleeper riders (Sl), and the fraction of a rider each carries
        // between looks (CarryF, CarryS, between -1 and 1: a whole rider is added or removed only once the fractions add up to one).
        // Nothing here is saved: a world load starts it from the shares again.
        internal struct Crowd
        {
            internal int Seen, Fc, Sl;
            internal double CarryF, CarryS;
            internal bool Known;
        }

        // One class's count follows a change in the crowd: a rise adds the share of the rise; a fall removes the same fraction of the
        // class as the fall is of the crowd; whole riders only, the fraction carried.
        private static void Move(ref int n, ref double carry, int oldSeen, int total, int share)
        {
            double change = total > oldSeen
                ? (total - oldSeen) * share / 100.0
                : -((oldSeen - total) * (double)n / oldSeen);
            double x = change + carry;
            double whole = Math.Truncate(x + (x >= 0 ? 1e-9 : -1e-9));
            n += (int)whole;
            carry = x - whole;
        }

        // Neither class passes the crowd, and together they don't either (first class gives way).
        private static void Fit(ref Crowd c, int total)
        {
            if (c.Sl > total) { c.Sl = total; c.CarryS = 0; }
            if (c.Sl < 0) { c.Sl = 0; c.CarryS = 0; }
            if (c.Fc > total - c.Sl) { c.Fc = total - c.Sl; c.CarryF = 0; }
            if (c.Fc < 0) { c.Fc = 0; c.CarryF = 0; }
        }

        // Brings a crowd up to date with the game's total for that destination. First look: each class is its share of the total, to the
        // nearest. Then it follows the changes (Move).
        internal static void Reconcile(ref Crowd c, int total, int sharePercent, int sleeperSharePercent)
        {
            if (total <= 0) { c = default(Crowd); return; }
            // No sleeper share here (a destination under the sleeper distance, or the distance raised in play): no sleeper riders.
            if (sleeperSharePercent <= 0) { c.Sl = 0; c.CarryS = 0; }
            if (!c.Known)
            {
                c.Known = true; c.Seen = total; c.CarryF = 0; c.CarryS = 0;
                c.Fc = (int)Math.Floor(total * sharePercent / 100.0 + 0.5);
                c.Sl = (int)Math.Floor(total * sleeperSharePercent / 100.0 + 0.5);
            }
            else if (total != c.Seen)
            {
                Move(ref c.Fc, ref c.CarryF, c.Seen, total, sharePercent);
                Move(ref c.Sl, ref c.CarryS, c.Seen, total, sleeperSharePercent);
                c.Seen = total;
            }
            Fit(ref c, total);
        }

        // Seats (or berths) held back: the riders a car with room can still take, at most its free seats.
        internal static int Reserved(int demand, int seatsFree) => Math.Min(Math.Max(0, demand), Math.Max(0, seatsFree));

        // What a first-class car may take from this destination: the first-class riders, and the sleeper riders no free berth is held for.
        internal static int FirstClassAvailable(Crowd c, int berthsHeld) => c.Fc + Math.Max(0, c.Sl - berthsHeld);

        // What a coach may take from this destination: the crowd less the held-back riders.
        internal static int CoachAvailable(int total, int reserved) => Math.Max(0, total - reserved);

        // After a sleeper car boarded n people: they were sleeper riders.
        internal static void BoardedSleeper(ref Crowd c, int n, int totalNow)
        {
            c.Sl = Math.Max(0, c.Sl - Math.Max(0, n));
            c.Seen = totalNow;
            Fit(ref c, totalNow);
        }

        // After a first-class car boarded n people: first-class riders first, then sleeper riders who had no berth (overflow says how many).
        internal static void BoardedFirstClass(ref Crowd c, int n, int totalNow, out int overflow)
        {
            n = Math.Max(0, n);
            int fromFc = Math.Min(n, c.Fc);
            overflow = Math.Min(n - fromFc, c.Sl);
            c.Fc -= fromFc;
            c.Sl -= overflow;
            c.Seen = totalNow;
            Fit(ref c, totalNow);
        }

        // After a coach boarded n people while resSl berths and resFc first-class seats' worth of riders were held back: they came from the
        // rest, in the rest's share of each class. The held-back first-class riders are the first-class riders first, then sleeper riders
        // with no berth. dFc and dSl say how many first-class and sleeper riders went into the coach.
        internal static void BoardedCoach(ref Crowd c, int n, int totalBefore, int resSl, int resFc, int totalNow, out int dFc, out int dSl)
        {
            dFc = 0; dSl = 0;
            int protFc = Math.Min(c.Fc, resFc);
            int fcRest = c.Fc - protFc;
            int slRest = Math.Max(0, c.Sl - resSl - (resFc - protFc));
            int rest = totalBefore - resSl - resFc;
            if (n > 0 && rest > 0)
            {
                dFc = MoveOut(ref c.Fc, ref c.CarryF, n * (double)fcRest / rest);
                dSl = MoveOut(ref c.Sl, ref c.CarryS, n * (double)slRest / rest);
            }
            c.Seen = totalNow;
            Fit(ref c, totalNow);
        }

        private static int MoveOut(ref int n, ref double carry, double amount)
        {
            double x = -amount + carry;
            double whole = Math.Truncate(x + (x >= 0 ? 1e-9 : -1e-9));
            int removed = Math.Max(0, (int)-whole);
            n -= removed;
            carry = x + removed;
            return removed;
        }

        // How many people a boarding may take in one go so it can't pass `available`: the game takes 1 or 2.
        internal static int Cap(int maximum, int available) => Math.Max(0, Math.Min(maximum, available));

        // The stock baggage car (CBS, 18 ft, 60,000 lb empty, a 1,000 lb mail slot) is listed at $0. $3,500: the stock coaches cost between
        // $0.05 and $0.085 a pound of empty weight (steel coach 6,450 / 125,200 lb, open platform coach 4,450 / 57,400 lb, palace car
        // 6,850 / 81,100 lb). The baggage car has no seats or fittings, so it sits at the low end for its weight (60,000 lb at about
        // $0.06), under the cheapest coach's $4,450 and under the $5,500 a heavier third-party baggage-express car lists for.
        internal const string BaggageCarId = "rr/cbs-baggage01";
        internal const int BaggagePrice = 3500;

        // ---- baggage and the lounge ----

        // The game's fare band for a trip (PassengerStop.QueuePayment: Mathf.RoundToInt(Mathf.Lerp(1, 8, Mathf.InverseLerp(2, 50, miles)))), in the
        // same float arithmetic and the same round-half-to-even.
        internal static int FareBand(float miles)
        {
            if (float.IsNaN(miles)) return 1;    // no route the game can measure: it pays no fare either
            float t = (miles - 2f) / 48f;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            float v = 1f + (8f - 1f) * t;
            return (int)Math.Round(v);
        }

        // ---- the coach fare (0.3.0) ----
        // The game's coach fare is $1 up to 2 miles, then linear to $8 at 50 miles, then $8. Three settings own it now: the fare at 2 miles and under, the dollars
        // a mile past 2, and a cap (the last step of its slider, 51, is Unlimited). The defaults (1, 7/48, 8) are the game's own, whole dollars like the game's.
        // fare = Round(base + perMile x max(0, miles - 2)), held to the cap unless it is Unlimited.
        internal const int DefaultFareBase = 1, MinFareBase = 0, MaxFareBase = 50;
        internal const double DefaultFarePerMile = 7.0 / 48.0, MinFarePerMile = 0, MaxFarePerMile = 20;
        internal const int DefaultFareCap = 8, MinFareCap = 1, MaxFareCap = 50, UnlimitedFareCap = 51;      // 51 and over: no cap
        internal const int FarePerMileDecimals = 6;                // the settings screen saves the rate to this many decimals
        internal static int FareBase = DefaultFareBase, FareCap = DefaultFareCap;
        internal static double FarePerMile = DefaultFarePerMile;

        // A rate that reads as the default at the screen's own decimals IS the default (7/48 saved as 0.145833 must still pay the game's fares).
        internal static double SnapPerMile(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return DefaultFarePerMile;
            v = Math.Max(MinFarePerMile, Math.Min(MaxFarePerMile, v));
            return Math.Round(v, FarePerMileDecimals) == Math.Round(DefaultFarePerMile, FarePerMileDecimals) ? DefaultFarePerMile : v;
        }

        internal static void ApplyFares(int baseFare, double perMile, int cap)
        {
            FareBase = Clamp(baseFare, MinFareBase, MaxFareBase);
            FarePerMile = SnapPerMile(perMile);
            FareCap = Clamp(cap, MinFareCap, UnlimitedFareCap);
        }

        internal static bool FaresAreGames() => FareBase == DefaultFareBase && FarePerMile == DefaultFarePerMile && FareCap == DefaultFareCap;

        // The coach fare for a trip, whole dollars. Worked in the game's own float steps (t = (miles - 2) / 48, then base + (perMile x 48) x t) and rounded the way
        // Mathf.RoundToInt does (Math.Round, halves to even), so the defaults give the game's number for every distance, not only to within a cent. NaN miles: the base.
        internal static int CoachFare(float miles) => CoachFare(miles, FareBase, FarePerMile, FareCap);
        internal static int CoachFare(float miles, int baseFare, double perMile, int cap)
        {
            float over = miles - 2f;
            if (!(over > 0f)) over = 0f;
            float t = over / 48f;
            float v = (float)baseFare + (float)(perMile * 48.0) * t;
            if (cap < UnlimitedFareCap) v = Math.Min(v, (float)cap);
            if (v > 1000000f) v = 1000000f;
            return (int)Math.Round(v);
        }

        // Pounds of checked bags one rider brings, by fare band and the class of the car (0 coach, 1 first class, 2 sleeper). Halves round up.
        internal static int Allowance(int band, int cls)
        {
            if (band <= 2) return 0;
            int near = cls == 2 ? BagNearSleeper : cls == 1 ? BagNearFirst : BagNearCoach;
            return band <= 5 ? near : (int)(((long)near * BagFarPercent + 50) / 100);
        }

        // A car's racks: its seats (berths) times its class's rack figure (boardClass 0 coach, lounge and anything else; 1 parlor; 2 sleeper).
        internal static double RackLb(int seats, int boardClass) => Math.Max(0, seats) * (double)(boardClass == 2 ? BagRackSleeperLb : boardClass == 1 ? BagRackParlorLb : BagRackLb);

        // The luggage area of a car with seats: the room for its own riders' bags past the racks (normal overflow first, then the excess). A car with no seats has none.
        internal static double LuggageLb(int seats) => seats > 0 ? BagLuggageLb : 0;

        // The part of a car's excess bags that the luggage area did not take (what needs baggage room elsewhere in the train): the area left after the normal
        // overflow past the racks takes the excess first.
        internal static double ExcessOverflow(double normal, double racks, double luggage, double excess) => Math.Max(0.0, excess - Math.Max(0.0, luggage - NormalOverflow(normal, racks)));

        // The share of one car's excess bags that rides: what its own luggage area took, and the train's share of the rest (`train`, 0 to 1). A car with no excess takes the train's share.
        internal static double CarCarried(double excess, double left, double train) => excess <= 0 ? train : (excess - left * (1.0 - train)) / excess;

        // The excess bags of a rider with this allowance, on average: the share of riders who bring double, each bringing one more allowance.
        internal static double ExcessLb(int allowance) => allowance * BagExcessPercent / 100.0;

        // What a car's riders' normal bags put in the baggage room: the part past the car's own racks. Riders board only while this fits.
        internal static double NormalOverflow(double normal, double rack) => Math.Max(0.0, normal - rack);

        // How many of `wanted` riders with this allowance a car can take before the normal-bag overflow it adds passes `headroom` (the baggage room of
        // the train less every car's normal-bag overflow now; below zero when the train is already over, which lets in only what adds no overflow). A rider with no bags always fits.
        internal static int FitCount(double headroom, double normal, double rack, int allowance, int wanted)
        {
            if (wanted <= 0) return 0;
            if (allowance <= 0) return wanted;
            double now = NormalOverflow(normal, rack);
            int lo = 0, hi = wanted;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                double add = NormalOverflow(normal + mid * (double)allowance, rack) - now;
                if (add <= Math.Max(0.0, headroom) + 1e-9) lo = mid; else hi = mid - 1;   // a rider who adds no overflow fits however far over its room the train is
            }
            return lo;
        }

        // The share of the train's excess bags the room left after the normal bags can carry (0 to 1). Excess bags ride and pay their fee only if
        // there is room; the riders who bring them are not turned away for it.
        internal static double ExcessCarried(double spare, double excessTotal)
        {
            if (excessTotal <= 0) return 1.0;
            double f = spare / excessTotal;
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }

        // The excess fee for `count` riders with this allowance getting off, carried at `carried` (0 to 1): their expected excess pounds at the fee per 100 lb.
        internal static double Fee(int count, int allowance, double carried) => Math.Max(0, count) * ExcessLb(allowance) * carried * BagFeePer100 / 100.0;

        // ---- the sleeper distance, the observation bonus, the coach brake ----

        // The share of a destination's crowd that wants a sleeper: the setting for a trip of the minimum distance or more, none for a
        // shorter one. A trip whose distance the game can't work out (NaN) is not held back: it pays nothing either way.
        internal static int SleeperShareFor(float miles, int minMiles, int sharePercent) =>
            float.IsNaN(miles) || miles >= minMiles ? sharePercent : 0;

        // The fare multiplier an observation car at the tail gives the whole train: the setting, as a float the way the game writes 1.2f.
        internal static float ObservationBonus(int percent) => Clamp(percent, MinObservationPercent, MaxObservationPercent) / 100f;

        // The game's own end test (PassengerStop.CalculateBonusMultiplier): the platform is a free end that is not the car's F end.
        // A car whose model has the platform at the other end (Classes: platformAtOtherEnd) takes the F end instead.
        internal static bool PlatformEndFree(bool endIsF, bool uncoupled, bool atOtherEnd) => uncoupled && (atOtherEnd ? endIsF : !endIsF);

        // The game's coach brake: 0.9 of the empty weight (Car.SetNominalBrakingRatio), where a car of another kind gets 0.7.
        internal static float CoachBrake(int weightEmpty) => 0.9f * (float)weightEmpty;

        // ---- the fare ----

        // The extra a first-class rider pays over the fare the game worked out for them (that fare, observation bonus included).
        internal static double Extra(double gameFare, int farePercent) => gameFare * (Clamp(farePercent, MinFarePercent, MaxFarePercent) - 100) / 100.0;

        // ---- the heat fare (0.2.0) ----
        // With Realistic Steam and Fuel Use's passenger heat on, a car's fare moves with the weather. How cold it is, from 0 (60 F and above) to 1 (20 F and
        // below), linear between. An UNHEATED car earns less by its class's cut times that; a HEATED car earns a little more by half of the sourced
        // bonus times that (the dig's option B at half strength: sleeper 10, parlor 6, coach 4). The percents are of the car's whole fare (the game's fare
        // plus the first-class or sleeper extra). The heatFarePercent setting scales both (100 as designed, 0 off).
        internal const int ColdStartF = 60, ColdFullF = 20;
        internal const int DefaultHeatFarePercent = 100, MinHeatFarePercent = 0, MaxHeatFarePercent = 200;
        internal static int HeatFarePercent = DefaultHeatFarePercent;
        // by class: coach, first class (parlor), sleeper
        internal static readonly int[] UnheatedCutPercent = { 25, 30, 40 };
        internal static readonly int[] HeatedBonusPercent = { 4, 6, 10 };

        internal static void ApplyHeat(int percent) { HeatFarePercent = Clamp(percent, MinHeatFarePercent, MaxHeatFarePercent); }

        // The "Steam heat" setting: the whole heat feature. Off: no Heat line on a car and no heated or unheated fare effect, with or without Realistic Steam and
        // Fuel Use. heatFarePercent only scales the fare effect while this is on.
        internal static bool SteamHeat = true;
        internal static void ApplySteamHeat(bool on) { SteamHeat = on; }

        // The two readouts (0.2.0): the Fare line in the car window and the breakdown line a stop's payment gets. Each its own switch, on by default; neither moves any money.
        internal static bool FareLine = true, PayBreakdown = true;
        internal static void ApplyReadouts(bool fareLine, bool payBreakdown) { FareLine = fareLine; PayBreakdown = payBreakdown; }

        // Without Realistic Steam and Fuel Use's heat (not installed, its heat off or not readable), the cars are simply heated in the cold months, at no cost, and no fare is cut.
        // The cold months are November to March, a design choice (Sylva's mean air is under 60 F then). The month is the game's own calendar: TimeWeather.StartDateTime
        // (1940-04-01) plus the clock's seconds.
        internal static readonly DateTime GameStart = new DateTime(1940, 4, 1);
        internal static int MonthOf(double totalSeconds) => double.IsNaN(totalSeconds) || Math.Abs(totalSeconds) > 3e9 ? 0 : GameStart.AddSeconds(totalSeconds).Month;
        internal static bool ColdMonth(int month) => month >= 1 && month <= 12 && (month >= 11 || month <= 3);

        internal static double Cold(double outsideF) => outsideF >= ColdStartF ? 0.0 : outsideF <= ColdFullF ? 1.0 : (ColdStartF - outsideF) / (ColdStartF - ColdFullF);

        // The percent the car's fare moves by (negative: less), for its class (0 coach, 1 first class, 2 sleeper), whether an engine heats it and the air.
        // A baggage or mail car pays no fare, so it is not asked.
        internal static double HeatPercent(int cls, bool heated, double outsideF, int strengthPercent)
        {
            if (cls < 0 || cls > 2 || double.IsNaN(outsideF) || double.IsInfinity(outsideF)) return 0;
            double basis = heated ? HeatedBonusPercent[cls] : -UnheatedCutPercent[cls];
            return basis * Cold(outsideF) * Clamp(strengthPercent, MinHeatFarePercent, MaxHeatFarePercent) / 100.0;
        }

        // The sliding heat fare, when the feed gives the car's inside temperature (heat to 70, no cut until below 62): the comfort is +1 at 70 F and warmer, sliding
        // linearly to 0 at 62 F (no cut down to there), then sliding linearly to -1 at 50 F and colder. Above 0 the effect is the comfort times the class's heated bonus, below 0 the comfort times
        // the class's cold-car cut; then the outside-cold ramp and the setting, as HeatPercent. A comfort of -1 at 20 F outside is the same -25 / -30 / -40 as an unheated car; +1 the same +4 / +6 / +10 as a heated one.
        // THE THREE CORNERS ARE THE SAME AS Realistic Steam and Fuel Use's Thermal.Comfort (its feed's comfortSeconds is the integral of that curve); a both-dlls row pins them.
        internal const double ComfortFullF = 70.0, ComfortZeroF = 62.0, ComfortCutFullF = 50.0;
        internal static double Comfort(double insideF)
        {
            if (double.IsNaN(insideF)) return 0.0;
            if (insideF >= ComfortFullF) return 1.0;
            if (insideF >= ComfortZeroF) return (insideF - ComfortZeroF) / (ComfortFullF - ComfortZeroF);
            if (insideF > ComfortCutFullF) return (insideF - ComfortZeroF) / (ComfortZeroF - ComfortCutFullF);
            return -1.0;
        }

        // The fare effect of a comfort between -1 and +1 (the average over a ride, or Comfort of the temperature now).
        internal static double HeatPercentComfort(int cls, double comfort, double outsideF, int strengthPercent)
        {
            if (cls < 0 || cls > 2 || double.IsNaN(comfort) || double.IsInfinity(comfort) || double.IsNaN(outsideF) || double.IsInfinity(outsideF)) return 0;
            double c = comfort > 1.0 ? 1.0 : comfort < -1.0 ? -1.0 : comfort;
            double basis = c > 0 ? c * HeatedBonusPercent[cls] : c * UnheatedCutPercent[cls];
            return basis * Cold(outsideF) * Clamp(strengthPercent, MinHeatFarePercent, MaxHeatFarePercent) / 100.0;
        }

        internal static double HeatPercentInside(int cls, double insideF, double outsideF, int strengthPercent)
        {
            if (double.IsNaN(insideF) || double.IsInfinity(insideF)) return 0;
            return HeatPercentComfort(cls, Comfort(insideF), outsideF, strengthPercent);
        }

        // The average comfort over a ride from two readings of a car's comfort integral (seconds of comfort, the game second each is as of). NaN when the readings cannot say: no earlier one, the
        // clock running backwards, under a second between them, or a number that is not finite. Held to -1 and +1.
        internal static double RideComfort(double sumBoard, double atBoard, double sumNow, double atNow)
        {
            if (double.IsNaN(sumBoard) || double.IsNaN(atBoard) || double.IsNaN(sumNow) || double.IsNaN(atNow) || double.IsInfinity(sumBoard) || double.IsInfinity(sumNow)) return double.NaN;
            double dt = atNow - atBoard;
            if (!(dt >= 1.0)) return double.NaN;
            double c = (sumNow - sumBoard) / dt;
            return c > 1.0 ? 1.0 : c < -1.0 ? -1.0 : c;
        }

        // The change in dollars to a fare (the game's plus the extra) for that percent.
        internal static double HeatDelta(double fare, double percent) => fare * percent / 100.0;

        // The ledger line: the extras added up and rounded up to whole dollars, as the game rounds its own batch.
        // (A heat cut makes the sum negative: it rounds toward zero, the same rounding up.)
        internal static int Dollars(double extra) => (int)Math.Ceiling(extra - 1e-9);
    }
}
