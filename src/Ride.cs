using System;
using System.Collections.Generic;

namespace PassengerClasses
{
    // The heat fare over a ride (the average comfort over each passenger's ride, not the temperature at the stop). The game does not say when a rider boarded the CAR: its
    // PassengerGroup.Boarded is the moment the group began to wait on the platform (the game gives the car the waiting group's own stamp, and a transfer carries it on), so this
    // mod keeps its own book. When riders board a car (the postfix on the game's allocation, Boarding.AllocatePostfix) the car's comfort integral is read from Realistic Steam and Fuel Use's feed
    // (comfortSeconds, comfortAt: the integral of its comfort curve over time, and the game second it is as of) and written down with the group (origin, destination, count). When one rider gets off
    // at their stop (QueuePayment), the book gives the reading from when they boarded, and the average comfort over the ride is the integral's change over the time between. The integral lives in
    // Realistic Steam and Fuel Use because only its step sees a car's whole path through a sleep or a skip; this mod has no tick. Nothing is saved: after a world load the book is empty and a rider
    // who boarded before it is worked at the temperature at the stop, as in 0.2.0's first form. Game-free (tests/LogicCheck.cs runs it).
    internal static class Ride
    {
        private struct Entry { internal string Origin, Dest; internal int Count; internal double Sum, At; }
        private const int MaxEntries = 64;     // a boarding is 1 or 2 riders, so an 80-seat coach is some 50 entries unmerged; entries of one reading are merged (Board), and more than this drops the oldest

        private static readonly Dictionary<string, List<Entry>> Cars = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);

        internal static void Reset() => Cars.Clear();
        internal static int Cars_ => Cars.Count;
        internal static int Entries(string carId) => Cars.TryGetValue(carId, out List<Entry> l) ? l.Count : 0;
        private static bool Same(double a, double b) => a == b || (double.IsNaN(a) && double.IsNaN(b));

        // `n` riders from `origin` to `dest` boarded car `carId` when its comfort integral read `sum` as of game second `at` (NaN: not known, so these riders fall back).
        internal static void Board(string carId, string origin, string dest, int n, double sum, double at)
        {
            if (carId == null || origin == null || dest == null || n <= 0) return;
            if (!Cars.TryGetValue(carId, out List<Entry> list)) Cars[carId] = list = new List<Entry>();
            // The newest entry of the same pair with the very same reading (a boarding reads the feed's 2 s cache, so the boardings inside one cache window share a reading) is the same group of riders: merge, so a busy coach holds one entry for each pair and cache window, not one a boarding. Only
            // the newest entry of the pair counts, so the order of the pair's riders (oldest first, as Alight takes them) is exactly as if each had its own entry.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Entry e = list[i];
                if (e.Origin != origin || e.Dest != dest) continue;
                if (e.Count > 0 && Same(e.Sum, sum) && Same(e.At, at)) { e.Count += n; list[i] = e; return; }
                break;
            }
            if (list.Count >= MaxEntries) list.RemoveAt(0);
            list.Add(new Entry { Origin = origin, Dest = dest, Count = n, Sum = sum, At = at });
        }

        // `n` riders of (origin, dest) get off car `carId` at their stop, the car's comfort integral reading `sumNow` as of `atNow`. `remaining` is how many riders of that pair the car's own passenger
        // marker still holds after these left (-1: not known): the book never holds more than remaining + n of them (riders who left by a transfer are not paid for, so the oldest entries are
        // dropped to fit). Returns the average comfort over the ride, the riders' own average weighted by how many each entry had; NaN when the book holds none of them.
        internal static double Alight(string carId, string origin, string dest, int n, int remaining, double sumNow, double atNow)
        {
            if (carId == null || origin == null || dest == null || n <= 0) return double.NaN;
            if (!Cars.TryGetValue(carId, out List<Entry> list)) return double.NaN;
            if (remaining >= 0)
            {
                int total = 0;
                for (int i = 0; i < list.Count; i++) if (list[i].Origin == origin && list[i].Dest == dest) total += list[i].Count;
                int excess = total - (remaining + n);
                for (int i = 0; i < list.Count && excess > 0; i++)
                {
                    Entry e = list[i];
                    if (e.Origin != origin || e.Dest != dest) continue;
                    int drop = Math.Min(excess, e.Count);
                    e.Count -= drop; excess -= drop; list[i] = e;
                }
            }
            int need = n, got = 0;
            double sum = 0.0;
            for (int i = 0; i < list.Count && need > 0; i++)
            {
                Entry e = list[i];
                if (e.Origin != origin || e.Dest != dest || e.Count <= 0) continue;
                int take = Math.Min(need, e.Count);
                double c = Logic.RideComfort(e.Sum, e.At, sumNow, atNow);
                e.Count -= take; need -= take; list[i] = e;
                if (!double.IsNaN(c)) { sum += c * take; got += take; }
            }
            list.RemoveAll(x => x.Count <= 0);
            if (list.Count == 0) Cars.Remove(carId);
            return got > 0 ? sum / got : double.NaN;
        }
    }
}
