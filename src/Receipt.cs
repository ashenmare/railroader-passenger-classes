using System;
using System.Globalization;

namespace PassengerClasses
{
    // The words of the two readouts (0.2.0), game-free so tests/LogicCheck.cs runs them: the Fare line in a passenger car's window (what the car's fare is
    // made of now) and the breakdown line a stop's payment gets (what each class and the heat paid). Nothing here moves money; the breakdown only
    // names whole dollars that add up to the amount the game paid.
    internal static class Receipt
    {
        // ---- the car's Fare line ----
        // heatKind: 0 no heat part, 1 heated with a fare effect (heatPercent), 2 unheated with one, 3 heated at no cost (the included heat, no figure).
        // obsPercent is the observation bonus as a signed whole percent (0 none). The class part names the extra over the coach fare ("First class +100%").
        private static int _kCls = -1, _kFare, _kKind, _kHeat, _kObs, _kIn;
        private static string _kText;

        internal const int NoTemp = int.MinValue;

        // insideF: the car's inside temperature in whole F when the feed gave one (the words then name it: "60 F inside +2%"), NoTemp when not ("heated +4%").
        internal static string FareLine(int cls, int farePercent, int heatKind, int heatPercent, int obsPercent, int insideF = NoTemp)
        {
            if (_kText != null && _kCls == cls && _kFare == farePercent && _kKind == heatKind && _kHeat == heatPercent && _kObs == obsPercent && _kIn == insideF) return _kText;
            string t = cls == 2 ? "Sleeper" : cls == 1 ? "First class" : "Coach";
            if (cls != 0 && farePercent != 100) t += " " + Signed(farePercent - 100) + "%";
            if ((heatKind == 1 || heatKind == 2) && insideF != NoTemp) t += ", " + insideF.ToString(CultureInfo.InvariantCulture) + " F inside " + Signed(heatPercent) + "%";
            else if (heatKind == 1) t += ", heated " + Signed(heatPercent) + "%";
            else if (heatKind == 2) t += ", cold car " + Signed(heatPercent) + "%";
            else if (heatKind == 3) t += ", Heated";
            if (obsPercent != 0) t += ", observation " + Signed(obsPercent) + "%";
            _kCls = cls; _kFare = farePercent; _kKind = heatKind; _kHeat = heatPercent; _kObs = obsPercent; _kIn = insideF; _kText = t;
            return t;
        }

        internal static void ForgetLine() { _kText = null; }

        private static string Signed(int n) => (n >= 0 ? "+" : "-") + Math.Abs(n).ToString(CultureInfo.InvariantCulture);

        // ---- the stop's breakdown ----
        // Whole dollars for each exact part that add up to `total` (what the game paid: its own rounded-up sum plus this mod's rounded-up extras, so the parts'
        // own roundings never add to it). Each part starts at its floor; the dollars left over (or owed) go one at a time to the part furthest below (or above) its
        // exact figure. A part of exactly 0 never takes a dollar. Returns false when there is nothing to divide.
        internal static bool Split(double[] exact, int total, int[] whole)
        {
            int n = exact.Length, sum = 0; bool any = false;
            for (int i = 0; i < n; i++)
            {
                whole[i] = exact[i] == 0 ? 0 : (int)Math.Floor(exact[i]);
                sum += whole[i];
                if (exact[i] != 0) any = true;
            }
            int left = total - sum;
            if (left != 0 && !any) return false;
            for (int guard = 0; left != 0 && guard < 1000; guard++)
            {
                int pick = -1; double best = 0;
                for (int i = 0; i < n; i++)
                {
                    if (exact[i] == 0) continue;
                    double gap = exact[i] - whole[i];            // above 0: owed a dollar; below 0: has one too many
                    if (left > 0 ? (pick < 0 || gap > best) : (pick < 0 || gap < best)) { pick = i; best = gap; }
                }
                whole[pick] += left > 0 ? 1 : -1;
                left += left > 0 ? -1 : 1;
            }
            return left == 0;
        }

        // The parts, in the order the line names them: coach, first class, sleeper, heat, bags.
        internal const int Parts = 5;
        private static readonly string[] Names = { "coach", "first", "sleeper", "heat", "bags" };
        private static readonly double[] _exact = new double[Parts];
        private static readonly int[] _whole = new int[Parts];

        // "Sylva: $120 (coach $34, first $52, sleeper $25, heat +$9)". null when the exact parts are not within $2 of what was paid (a part was not
        // seen, so nothing is said that would be wrong; a NaN or infinite part fails the same test), when no part names a dollar, or when a
        // class part would come out negative.
        // The heat reads "heat +$9" or "heat -$9" (a cold car); a part that works out to $0 is left out.
        internal static string PayLine(string stop, int paid, double coach, double first, double sleeper, double heat, double bags)
        {
            _exact[0] = coach; _exact[1] = first; _exact[2] = sleeper; _exact[3] = heat; _exact[4] = bags;
            double total = coach + first + sleeper + heat + bags;
            if (!(Math.Abs(total - paid) <= 2.0)) return null;
            if (!Split(_exact, paid, _whole)) return null;
            int shown = 0;
            for (int i = 0; i < Parts; i++) { if (_whole[i] != 0) shown++; if (i != 3 && _whole[i] < 0) return null; }     // only the heat can be a cut
            if (shown < 1) return null;
            var sb = new System.Text.StringBuilder();
            sb.Append(stop).Append(": ").Append(Logic.Money(paid)).Append(" (");
            bool firstPart = true;
            for (int i = 0; i < Parts; i++)
            {
                if (_whole[i] == 0) continue;
                if (!firstPart) sb.Append(", ");
                firstPart = false;
                sb.Append(Names[i]).Append(' ');
                if (i == 3) sb.Append(_whole[i] < 0 ? "-" : "+");
                sb.Append(Logic.Money(Math.Abs(_whole[i])));
            }
            return sb.Append(')').ToString();
        }
    }
}
