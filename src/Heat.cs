using System;
using System.Collections.Generic;
using System.Globalization;

namespace PassengerClasses
{
    // The heat fare's weather (0.2.0). Realistic Steam and Fuel Use's feed says, for each car with a steam line, whether an engine heats it
    // and the air it was worked at; the rule itself is Logic.HeatPercent. Read through Mod Feeds by name, no reference to that mod.
    // Without the feed, with its passenger heat off, or for a car it does not list, there is no effect: fares are exactly as without this file.
    // Game-free (plain strings and numbers), so tests/LogicCheck.cs runs it against a fixture feed. The answer is kept for a couple of seconds:
    // a stop's riders all get off in one step and the feed builds a row for every car.
    internal static class Heat
    {
        internal const string FeedId = "realistic-fuel-use";
        private const int KnownApi = 3;             // the feed's api as of passengerHeat; a newer one is read key by key (ModFeeds.TryGet)
        private const int KeepMs = 2000;

        private struct Car { internal bool Heated, NoFare; internal double OutsideF, InsideF, ComfortSum, ComfortAt; }   // InsideF, ComfortSum and ComfortAt are NaN when the row has none

        private static readonly Dictionary<string, Car> Cars = new Dictionary<string, Car>(StringComparer.Ordinal);
        private static bool _on, _have;
        private static int _stamp;
        internal static int Reads;                  // how many times the feed was asked (the checks count them)

        internal static void Reset() { Cars.Clear(); _on = false; _have = false; }

        // the feed "realistic-fuel-use" api 3 has passengerHeat.on (bool) and passengerCars[] with id, heated, outsideF, type and (additive, later) insideF; any car or key that is not so is skipped and the fare is as without this file
        private static void Load()
        {
            int now = Environment.TickCount;
            if (_have && unchecked(now - _stamp) < KeepMs) return;
            _have = true; _stamp = now; _on = false; Cars.Clear(); Reads++;
            object answer = ModFeeds.Read(FeedId);
            if (!ModFeeds.TryGet(answer, "passengerHeat", KnownApi, out object ph)) return;
            if (!ModFeeds.TryGetBool(ph, "on", KnownApi, out bool on) || !on) return;
            if (!ModFeeds.TryGet(answer, "passengerCars", KnownApi, out object list) || !(list is System.Collections.IEnumerable rows)) return;
            foreach (object o in rows)
            {
                if (!ModFeeds.TryGetString(o, "id", KnownApi, out string id) || !ModFeeds.TryGetBool(o, "heated", KnownApi, out bool heated)
                    || !ModFeeds.TryGetDouble(o, "outsideF", KnownApi, out double f)) continue;
                ModFeeds.TryGetString(o, "type", KnownApi, out string type);
                if (!ModFeeds.TryGetDouble(o, "insideF", KnownApi, out double inside)) inside = double.NaN;      // absent or not a finite number: the binary rule
                // 0.2.0 over a ride: the car's comfort integral (seconds) and the game second it is as of, both or neither (an older Realistic Steam and Fuel Use has none: the temperature at the stop is used)
                if (!ModFeeds.TryGetDouble(o, "comfortSeconds", KnownApi, out double csum) || !ModFeeds.TryGetDouble(o, "comfortAt", KnownApi, out double cat)) { csum = double.NaN; cat = double.NaN; }
                Cars[id] = new Car { Heated = heated, OutsideF = f, InsideF = inside, ComfortSum = csum, ComfortAt = cat, NoFare = type == "baggage" || type == "mail" };
            }
            _on = true;
        }

        // Whether the rule can move any fare now: the setting is above 0 and the feed is there with the heat on. The fare hook asks before it reads a fare for the heat alone.
        internal static bool Live()
        {
            if (!Logic.SteamHeat || Logic.HeatFarePercent <= 0) return false;
            Load();
            return _on;
        }

        // The percent a car's fare moves by now (negative: less); 0 when the rule is off, the feed is not there, or the car is not in it.
        // cls is 0 coach, 1 first class, 2 sleeper. insideF is the car's inside temperature when the feed row has a finite one (the sliding rule,
        // Logic.HeatPercentInside); NaN when it has none, and then today's binary rule applies unchanged (heated: full bonus, unheated: full cut, each times the cold ramp).
        internal static double Percent(string carId, int cls, out bool heated, out double outsideF, out double insideF) => Percent(carId, cls, double.NaN, out heated, out outsideF, out insideF);

        // The same over a ride: rideComfort is the average comfort of the rider's ride (Ride.Alight) when there is one (not NaN) and the row has an inside temperature, and then it, not the temperature now,
        // sets the effect; NaN keeps the temperature now.
        internal static double Percent(string carId, int cls, double rideComfort, out bool heated, out double outsideF, out double insideF)
        {
            heated = false; outsideF = double.NaN; insideF = double.NaN;
            if (!Logic.SteamHeat || Logic.HeatFarePercent <= 0 || carId == null) return 0;
            Load();
            if (!_on || !Cars.TryGetValue(carId, out Car c) || c.NoFare) return 0;
            heated = c.Heated; outsideF = c.OutsideF; insideF = c.InsideF;
            if (!double.IsNaN(c.InsideF))
                return !double.IsNaN(rideComfort) ? Logic.HeatPercentComfort(cls, rideComfort, c.OutsideF, Logic.HeatFarePercent) : Logic.HeatPercentInside(cls, c.InsideF, c.OutsideF, Logic.HeatFarePercent);
            return Logic.HeatPercent(cls, c.Heated, c.OutsideF, Logic.HeatFarePercent);
        }

        // The car's comfort integral and the game second it is as of, from the feed's row; false when the heat is not live, the row is not there or has none.
        internal static bool Comfort(string carId, out double sum, out double at)
        {
            sum = double.NaN; at = double.NaN;
            if (!Logic.SteamHeat || Logic.HeatFarePercent <= 0 || carId == null) return false;
            Load();
            if (!_on || !Cars.TryGetValue(carId, out Car c) || c.NoFare || double.IsNaN(c.InsideF) || double.IsNaN(c.ComfortSum) || double.IsNaN(c.ComfortAt)) return false;
            sum = c.ComfortSum; at = c.ComfortAt;
            return true;
        }

        internal static double Percent(string carId, int cls, out bool heated, out double outsideF) => Percent(carId, cls, out heated, out outsideF, out double _);

        // Set by Main to the game clock in seconds (a trampoline, so this file holds no game type).
        internal static Func<double> Clock;

        // Without Realistic Steam and Fuel Use's heat (no feed, its heat off, or its answer not readable): a passenger car in the cold months reads "Heated", at no cost, and no
        // fare moves (Percent is 0 then). null when Steam heat is off, the real heat is in play, or it is not a cold month (or the clock is not there).
        internal static string IncludedWords()
        {
            if (!Logic.SteamHeat) return null;
            Load();
            if (_on) return null;
            try { return Clock != null && Logic.ColdMonth(Logic.MonthOf(Clock())) ? "Heated" : null; }
            catch (Exception) { return null; }
        }

        // The words for the car's feed row: "cold car, fares -25%" or "heated, +4%"; null when the effect rounds to nothing. With an inside temperature the sign says
        // which (a car can be heated and still cold inside).
        internal static string Words(double percent, bool heated, double insideF = double.NaN)
        {
            int p = (int)Math.Round(percent, MidpointRounding.AwayFromZero);
            if (p == 0) return null;
            string n = p.ToString(CultureInfo.InvariantCulture);
            if (!double.IsNaN(insideF)) heated = p > 0;
            return heated ? "heated, +" + n + "%" : "cold car, fares " + n + "%";
        }
    }
}
