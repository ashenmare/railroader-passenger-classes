using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.Ops;

namespace PassengerClasses
{
    // The fare. The game works out each rider's fare when they get off (QueuePayment: miles to dollars, times the observation bonus),
    // adds it to the station's pending total and pays that total about 5 s after the last rider (PayPending, PayPassengerFare). A rider
    // leaving a first-class or sleeper car pays more: the extra is the fare the game just worked out, times (percent - 100) / 100, kept per
    // station beside the game's own total and added to the game's own payment when it is made, so the ledger has one line a station
    // under the station's name, one sound, and the game's fare itself is not touched.
    internal static class Fares
    {
        internal static volatile bool Live;
        private static int _failures;
        private static FieldInfo _pending, _amount;
        // Per station: slot 0 is this mod's extras added to the game's payment (the class extra, the heat change and the bag fee, in dollars, paid once); the rest are the
        // parts the breakdown line names, kept only while Logic.PayBreakdown is on: the game's own fare by class (1 coach, 2 first class, 3 sleeper), the first-class and
        // sleeper extra (4, 5), the heat change (6) and the bag fees (7). Slot 0 is the payment; 1 to 7 only say what it was made of.
        private const int Slots = 8, SCoach = 1, SFirst = 2, SSleeper = 3, SFirstExtra = 4, SSleeperExtra = 5, SHeat = 6, SBags = 7;
        private static readonly Dictionary<PassengerStop, double[]> Pending = new Dictionary<PassengerStop, double[]>();
        private static bool _splitDown;         // the breakdown stood down (it failed three times); reset on world load
        private static int _splitFailures, _receiptPaid;
        private static string _receipt, _receiptStop;      // the breakdown line made in PayPrefix, said by PayPostfix once the game has posted its own line
        private static AccessTools.FieldRef<PassengerStop, object> _pendingRef;
        private static AccessTools.FieldRef<object, float> _amountRef;

        private static bool _heatDown;      // the heat fare stood down (it failed three times); reset on world load

        internal static void ResetWorld() { _failures = 0; Pending.Clear(); _heatDown = false; _heatFailures = 0; _splitDown = false; _splitFailures = 0; _baseDown = false; _baseFailures = 0; _receipt = null; Heat.Reset(); Ride.Reset(); _rideKey = null; }

        // Multiplayer.Broadcast(string) is public static void: it logs the text, then shows it as a console alert on the host and sends it to every client; the game's own PayPassengerFare calls it for the payment line [load, optional]
        private static MethodInfo _broadcast;

        // PassengerStop.QueuePayment(int, string, string, float) is private; it adds the rider's fare (rounded miles fare times the bonus) to the private _pendingPayment's public float Amount [load]
        // PassengerStop.PayPassengerFare(int, int) is private; PayPending calls it once the station's pending fares are 5 s old, then clears them [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            var ps = typeof(PassengerStop);
            MethodInfo q = AccessTools.Method(ps, "QueuePayment", new[] { typeof(int), typeof(string), typeof(string), typeof(float) });
            if (q == null || q.ReturnType != typeof(void)) return "PassengerStop.QueuePayment";
            if (q.GetParameters()[3].Name != "bonusMultiplier") return "PassengerStop.QueuePayment (bonusMultiplier)";
            MethodInfo p = AccessTools.Method(ps, "PayPassengerFare", new[] { typeof(int), typeof(int) });
            if (p == null || p.ReturnType != typeof(void) || p.GetParameters()[1].Name != "amount") return "PassengerStop.PayPassengerFare";
            FieldInfo f = AccessTools.Field(ps, "_pendingPayment");
            FieldInfo a = f?.FieldType.GetField("Amount", BindingFlags.Public | BindingFlags.Instance);
            if (f == null || a == null || a.FieldType != typeof(float)) return "PassengerStop._pendingPayment.Amount";
            if (!f.FieldType.IsClass) return "PassengerStop._pendingPayment (a class)";
            // The breakdown line is optional: without Multiplayer.Broadcast(string) (the console line the game itself posts for each payment) it is simply never made.
            MethodInfo b = typeof(Network.Multiplayer).GetMethod("Broadcast", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            _broadcast = b != null && b.ReturnType == typeof(void) ? b : null;
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            _pending = AccessTools.Field(typeof(PassengerStop), "_pendingPayment");
            _amount = _pending.FieldType.GetField("Amount", BindingFlags.Public | BindingFlags.Instance);
            try { _pendingRef = AccessTools.FieldRefAccess<PassengerStop, object>(_pending); _amountRef = AccessTools.FieldRefAccess<object, float>(_amount); }
            catch (Exception) { _pendingRef = null; _amountRef = null; }     // reflection stands in (it allocates a box a read)
            h.Patch(AccessTools.Method(typeof(PassengerStop), "QueuePayment", new[] { typeof(int), typeof(string), typeof(string), typeof(float) }),
                    prefix: new HarmonyMethod(typeof(Fares), nameof(QueuePrefix)), postfix: new HarmonyMethod(typeof(Fares), nameof(QueuePostfix)));
            h.Patch(AccessTools.Method(typeof(PassengerStop), "PayPassengerFare", new[] { typeof(int), typeof(int) }),
                    prefix: new HarmonyMethod(typeof(Fares), nameof(PayPrefix)),
                    postfix: _broadcast == null ? null : new HarmonyMethod(typeof(Fares), nameof(PayPostfix)));
            if (_broadcast == null) Main.WarnOnce("receipt-resolve", "Passenger Classes: the fare breakdown line is off on this game version (Multiplayer.Broadcast not found); everything else goes on");
        }

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "fares", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("fare-" + what, "Passenger Classes: " + what + " failed, the game's own fare stands: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && Live)
            {
                Live = false;
                Main.WarnOnce("fare-down", "Passenger Classes: the first-class and sleeper fares stand down until the next world load");
            }
        }

        private static float Amount(PassengerStop s)
        {
            if (_pendingRef != null && _amountRef != null) return _amountRef(_pendingRef(s));
            return (float)_amount.GetValue(_pending.GetValue(s));
        }

        // The game's own fare for each rider is read for every class (the feed's day book); the premium extra is worked out only for a
        // first-class or sleeper car.
        internal static void QueuePrefix(PassengerStop __instance, out float __state)
        {
            __state = float.NaN;
            if (!Live) return;
            bool premium = Boarding.ServingClass != 0 && Percent() > 100;
            if (!premium && Daily.Off && !HeatWanted() && !SplitWanted()) return;
            try { __state = Amount(__instance); }
            catch (Exception e)
            {
                __state = float.NaN;
                if (premium) Failed("reading a fare", e); else if (!Daily.Off) Boarding.DailyFailed("reading a fare", e); else if (HeatWanted()) HeatFailed("reading a fare", e); else SplitFailed("reading a fare", e);
            }
        }

        // The debug log's line for a premium rider's extra: the game's own fare for them, the percent in force and the extra added beside it.
        private static void LogPremiumFare(PassengerStop stop, string origin, string dest, int count, double fare, double extra)
        {
            DebugLog.Event(Main.Id, "fare", "stop", stop.identifier, "car", Boarding.Serving?.id, "class", Boarding.ClassWord(Boarding.ServingClass), "from", origin, "to", dest,
                           "riders", count, "game", fare, "percent", Percent(), "extra", extra);
        }

        // Whether a fare is worth reading for the heat alone (the day book is off): the feed is there with the heat on. Never throws.
        private static bool HeatWanted()
        {
            try { return !_heatDown && Heat.Live(); }
            catch (Exception) { return false; }
        }

        private static int Percent() => Boarding.ServingClass == 2 ? Logic.SleeperFarePercent : Logic.FarePercent;

        internal static void QueuePostfix(PassengerStop __instance, float __state, string originId, string destinationId, int count, float bonusMultiplier)
        {
            if (!Live) return;
            if (!_baseDown && !Logic.FaresAreGames()) BaseFare(__instance, originId, destinationId, bonusMultiplier);     // before the reads below, so every part sees the set fare
            double game = 0, extra = 0, heat = 0, bagFee = 0;
            if (!float.IsNaN(__state))
            {
                try
                {
                    double fare = Amount(__instance) - __state;          // what the game just added for this rider
                    if (fare > 0)
                    {
                        game = fare;
                        if (Boarding.ServingClass != 0 && Percent() > 100)
                        {
                            if (!Pending.TryGetValue(__instance, out double[] pend)) Pending[__instance] = pend = new double[Slots];
                            extra = Logic.Extra(fare, Percent());
                            pend[0] += extra;
                            if (DebugLog.On) LogPremiumFare(__instance, originId, destinationId, count, fare, extra);
                        }
                        heat = HeatFare(__instance, originId, destinationId, count, game + extra);     // every class: a coach is not left out
                    }
                }
                catch (Exception ex)
                {
                    // a coach rider's read is for the day book, the heat or the breakdown: the failure counts against whichever still wants it
                    if (Boarding.ServingClass != 0 && Percent() > 100) Failed("working out a premium fare", ex);
                    else if (!Daily.Off) Boarding.DailyFailed("working out a fare", ex);
                    else if (HeatWanted()) HeatFailed("working out a fare", ex);
                    else SplitFailed("working out a fare", ex);
                }
            }
            if (Logic.BaggageRiders) bagFee = BagFee(__instance, originId, destinationId, count);
            if (!Daily.Off && (game > 0 || bagFee > 0)) DayFare(__instance, game, extra, bagFee, heat);
            if (SplitWanted()) Track(__instance, game, extra, heat, bagFee);
        }

        // PassengerStop.QueuePayment's last parameter is named bonusMultiplier (a float) and the pending total _pendingPayment is a class, so its public float Amount can be written in place; QueuePayment adds the fare for one rider group once, whatever the count (always 1) [load]
        private static bool _baseDown;      // the coach fare settings stood down (three failures): the game's own fare stands; reset on world load
        private static int _baseFailures;

        // The coach fare settings (0.3.0, Logic.CoachFare): the game has just added its own fare for this rider (the miles band, whole dollars, times the observation
        // bonus) to the station's pending total; the difference to the fare the settings give, times the same bonus, is added to the same total. So the one payment,
        // the class extra, the heat, the breakdown and the day book all work on the set fare, and the defaults (no difference) change nothing. Never throws.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BaseFare(PassengerStop stop, string originId, string destinationId, float bonus)
        {
            try
            {
                float miles = Boarding.MilesBetween(originId, destinationId);
                if (float.IsNaN(miles)) return;                  // no route: the game added no fare either
                int game = Logic.FareBand(miles), set = Logic.CoachFare(miles);
                if (set == game) return;
                float add = ((float)set - (float)game) * bonus;
                float now = Amount(stop);
                if (_pendingRef != null && _amountRef != null) _amountRef(_pendingRef(stop)) = now + add;
                else _amount.SetValue(_pending.GetValue(stop), now + add);
                if (DebugLog.On) DebugLog.Event(Main.Id, "base-fare", "stop", stop.identifier, "from", originId, "to", destinationId, "miles", miles, "game", game, "set", set, "bonus", bonus, "added", add);
            }
            catch (Exception ex)
            {
                _baseFailures++;
                if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "base-fare", "what", "setting the coach fare", "type", ex.GetType().Name, "n", _baseFailures);
                Main.WarnOnce("base-fare", "Passenger Classes: the coach fare settings failed, the game's own fare stands: " + PathClean.CleanText(ex.Message));
                if (_baseFailures >= 3) { _baseDown = true; Main.WarnOnce("base-down", "Passenger Classes: the coach fare settings stand down until the next world load"); }
            }
        }

        // Whether the stop's payment breakdown is being kept: its setting is on, the console line can be posted, and it has not stood down.
        private static bool SplitWanted() => Logic.PayBreakdown && _broadcast != null && !_splitDown;

        // Keeps what this rider group's fare was made of, by part, beside the station's payment (never read by the payment itself).
        private static void Track(PassengerStop stop, double game, double extra, double heat, double bags)
        {
            if (game == 0 && extra == 0 && heat == 0 && bags == 0) return;
            try
            {
                if (!Pending.TryGetValue(stop, out double[] p)) Pending[stop] = p = new double[Slots];
                int cls = Boarding.ServingClass;
                p[cls == 2 ? SSleeper : cls == 1 ? SFirst : SCoach] += game;
                if (cls == 1) p[SFirstExtra] += extra; else if (cls == 2) p[SSleeperExtra] += extra;
                p[SHeat] += heat; p[SBags] += bags;
            }
            catch (Exception ex) { SplitFailed("keeping the parts of a fare", ex); }
        }

        private static void SplitFailed(string what, Exception ex)
        {
            _splitFailures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "receipt", "what", what, "type", ex.GetType().Name, "n", _splitFailures);
            Main.WarnOnce("receipt-" + what, "Passenger Classes: " + what + " failed, the stop's payment goes on without a breakdown line: " + PathClean.CleanText(ex.Message));
            if (_splitFailures >= 3) { _splitDown = true; Main.WarnOnce("receipt-down", "Passenger Classes: the fare breakdown line stands down until the next world load"); }
        }

        private static int _heatFailures;

        // The weather's change to this rider's fare (Heat.cs, Logic.HeatPercent): added to the station's pending total beside the premium extra, so
        // it reaches the game's one payment once. Worked on the whole fare (the game's plus the extra). Never throws: three failures stand the heat
        // fare down until the next world load, and the game's fare and the premium go on.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double HeatFare(PassengerStop stop, string originId, string destinationId, int count, double fare)
        {
            if (_heatDown) return 0;
            try
            {
                // 0.2.0: the average comfort over this rider's ride when the book has their boarding and the feed has the car's comfort integral; else the temperature at the stop
                double ride = RideComfort(originId, destinationId, count);
                double pct = Heat.Percent(Boarding.Serving?.id, Boarding.ServingClass, ride, out bool heated, out double outsideF, out double insideF);
                if (pct == 0) return 0;
                double delta = Logic.HeatDelta(fare, pct);
                if (!Pending.TryGetValue(stop, out double[] pend)) Pending[stop] = pend = new double[Slots];
                pend[0] += delta;
                if (DebugLog.On) DebugLog.Event(Main.Id, "heat-fare", "stop", stop.identifier, "car", Boarding.Serving?.id, "class", Boarding.ClassWord(Boarding.ServingClass), "from", originId, "to", destinationId,
                                                "riders", count, "fare", fare, "heated", heated, "outsideF", outsideF, "insideF", double.IsNaN(insideF) ? (object)"none" : insideF,
                                                "ride", double.IsNaN(ride) ? (object)"none" : ride, "percent", pct, "delta", delta);
                return delta;
            }
            catch (Exception ex) { HeatFailed("working out the heat fare", ex); return 0; }
        }

        // The average comfort over the ride of `count` riders getting off the serving car, from the book kept at boarding (Ride.cs); NaN when there is none (a rider from before a load, a feed with no
        // comfort figures, the heat not live). The riders leave the book either way.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double RideComfort(string originId, string destinationId, int count)
        {
            string id = Boarding.Serving?.id;
            if (id == null || !Heat.Comfort(id, out double sumNow, out double atNow)) return double.NaN;
            // PassengerStop.LoadUnloadCar unloads through UnloadOne on a LOCAL copy of the car's marker and writes it back after the loop, so during a step's payments
            // the car's own marker still holds every rider of the step: the riders already paid in this step (same car, pair and count read) are taken off the read, one step's payments being consecutive [load]
            int read = Aboard(Boarding.Serving, originId, destinationId);
            string key = id + "|" + originId + "|" + destinationId;
            if (key == _rideKey && read == _rideRead) _ridePaid += count; else { _rideKey = key; _rideRead = read; _ridePaid = 0; }
            int remaining = read < 0 ? -1 : Math.Max(0, read - _ridePaid - count);
            return Ride.Alight(id, originId, destinationId, count, remaining, sumNow, atNow);
        }
        private static string _rideKey; private static int _rideRead, _ridePaid;

        // How many riders from origin to destination the car's own passenger marker still holds (after the ones getting off were taken out); -1 when it cannot be read.
        // PassengerMarker.Groups is a public List<PassengerGroup> of structs with Origin, Destination (stop ids) and Count; GetPassengerMarker(car) is null for a car with no marker [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Aboard(Car car, string originId, string destinationId)
        {
            try
            {
                PassengerMarker? m = car?.GetPassengerMarker();
                if (!m.HasValue) return -1;
                int n = 0;
                List<PassengerGroup> groups = m.Value.Groups;
                for (int i = 0; i < groups.Count; i++) if (groups[i].Origin == originId && groups[i].Destination == destinationId) n += groups[i].Count;
                return n;
            }
            catch (Exception) { return -1; }
        }

        // `n` riders boarded the serving car: the car's comfort integral at this moment is written down with them (Ride.cs). Never throws.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void RideBoarded(Car car, string originId, string destinationId, int n)
        {
            try
            {
                if (_heatDown || !Live || !Heat.Comfort(car.id, out double sum, out double at)) return;
                Ride.Board(car.id, originId, destinationId, n, sum, at);
            }
            catch (Exception ex) { HeatFailed("noting a boarding", ex); }
        }

        private static void HeatFailed(string what, Exception ex)
        {
            _heatFailures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "heat-fare", "what", what, "type", ex.GetType().Name, "n", _heatFailures);
            Main.WarnOnce("heat-fare", "Passenger Classes: the heat fare failed, fares stand without it: " + PathClean.CleanText(ex.Message));
            if (_heatFailures >= 3) { _heatDown = true; Main.WarnOnce("heat-down", "Passenger Classes: the heat fare stands down until the next world load"); }
        }

        // The excess-baggage fee of the riders getting off: with the premium extra, in the game's own payment. No route, no fare, no fee.
        private static double BagFee(PassengerStop stop, string originId, string destinationId, int count)
        {
            try
            {
                if (Boarding.Serving == null) return 0;
                float miles = Boarding.MilesBetween(originId, destinationId);
                if (float.IsNaN(miles)) return 0;
                double fee = Logic.Fee(count, Logic.Allowance(Logic.FareBand(miles), Boarding.ServingClass), Baggage.CarriedShare(Boarding.Serving));
                if (fee <= 0) return 0;
                if (!Pending.TryGetValue(stop, out double[] pend2)) Pending[stop] = pend2 = new double[Slots];
                pend2[0] += fee;
                if (DebugLog.On) DebugLog.Event(Main.Id, "bag-fee", "stop", stop.identifier, "car", Boarding.Serving.id, "class", Boarding.ClassWord(Boarding.ServingClass), "from", originId, "to", destinationId,
                                                "riders", count, "miles", miles, "allowance", Logic.Allowance(Logic.FareBand(miles), Boarding.ServingClass), "carried", Baggage.CarriedShare(Boarding.Serving), "fee", fee);
                return fee;
            }
            catch (Exception ex) { Failed("working out an excess baggage fee", ex); return 0; }
        }

        // The feed's day book: the riders who just got off the serving car, at this stop, in the train it is in.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DayFare(PassengerStop stop, double game, double extra, double bagFee, double heat)
        {
            try
            {
                Boarding.Lead(out string tid, out string tname);
                Daily.Fare(Boarding.NowSeconds(), stop.identifier, stop.DisplayName, tid, tname, Boarding.ServingClass, game, extra, bagFee, heat);
            }
            catch (Exception ex) { Boarding.DailyFailed("counting a fare", ex); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DayPaid(PassengerStop stop, int dollars)
        {
            try { Daily.Paid(Boarding.NowSeconds(), stop.identifier, stop.DisplayName, dollars); }
            catch (Exception ex) { Boarding.DailyFailed("counting a payment", ex); }
        }

        // The breakdown line of what is about to be paid, kept until PayPostfix says it after the game's own line. null (no line) when the parts do not
        // account for the amount, or there is nothing to break down (Receipt.PayLine).
        private static void MakeReceipt(PassengerStop stop, double[] p, int paid)
        {
            try { _receipt = Receipt.PayLine(stop.DisplayName, paid, p[SCoach], p[SFirst] + p[SFirstExtra], p[SSleeper] + p[SSleeperExtra], p[SHeat], p[SBags]); _receiptStop = stop.identifier; _receiptPaid = paid; }
            catch (Exception ex) { SplitFailed("making the breakdown line", ex); }
        }

        // The game has posted its own payment line: the breakdown goes right after it, to the same place (the game's console; on a host to every player).
        internal static void PayPostfix(int amount)
        {
            string line = _receipt;
            if (line == null) return;
            _receipt = null;
            if (!Live || !SplitWanted()) return;
            if (amount != _receiptPaid) return;     // another mod changed the amount after this one: the line would name a payment that was not made
            try
            {
                Say(line);
                if (DebugLog.On) DebugLog.Event(Main.Id, "receipt", "stop", _receiptStop, "paid", _receiptPaid, "text", line);
            }
            catch (Exception ex) { SplitFailed("posting the breakdown line", ex); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Say(string line) => Network.Multiplayer.Broadcast(line);

        // The game is about to pay its total: the station's extras go into the same payment.
        internal static void PayPrefix(PassengerStop __instance, ref int amount)
        {
            _receipt = null;
            if (!Live) return;
            if (Pending.TryGetValue(__instance, out double[] pend))
            {
                try
                {
                    int dollars = Logic.Dollars(pend[0]);
                    if (DebugLog.On && (dollars != 0 || pend[0] != 0)) DebugLog.Event(Main.Id, "pay", "stop", __instance.identifier, "game", amount, "extra", pend[0], "dollars", dollars, "paid", amount > 0 ? Math.Max(0, amount + dollars) : amount);
                    pend[0] = 0;
                    if (dollars != 0 && amount > 0) amount = Math.Max(0, amount + dollars);     // a heat cut makes the dollars negative
                }
                catch (Exception ex) { Failed("paying a premium fare", ex); }
                if (amount > 0 && SplitWanted()) MakeReceipt(__instance, pend, amount);     // the final amount: the game's total and the extras in one
                for (int i = 1; i < Slots; i++) pend[i] = 0;
            }
            if (amount > 0 && !Daily.Off) DayPaid(__instance, amount);     // what the station is paid, the game's total and the extras in one
        }
    }
}
