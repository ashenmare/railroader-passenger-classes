using System;
using System.Collections.Generic;

namespace PassengerClasses
{
    // The public feeds, in the ashenmare feed shape (release/FEEDS.md, shipped in the zip). Read by reflection or through Mod Feeds,
    // so no mod depends on this one. Two feeds come from this one dll:
    //  - "passenger-classes" (this class): the class of every passenger and baggage car, per station the first-class and sleeper
    //    riders waiting and the riders who found no room, and today's and yesterday's figures (Daily.cs, the host's own). Plain data, main thread,
    //    changes nothing, never throws.
    //  - "first-class" (FirstClassFeed.Api below): First Class's own feed, with the same keys it had, so a reader written for
    //    that mod (the Industry Inspector's Passengers tab) keeps working after First Class is removed. It declares no columns, so a
    //    reader that shows columns shows the values once.
    // Both answer at any time (empty lists with no world) and return a new dictionary on every call. The cars and stations come from
    // the game through delegates set at mod load, so these classes name no game type.
    public static class Api
    {
        public const int Version = 1;
        public const string Feed = "passenger-classes";

        internal static Func<List<object>> CarSource, CompatCarSource, StationSource;
        internal static Func<double> NowSource;     // the game clock in seconds, set at mod load (a game type stays out of this class)
        internal static Func<bool> HostSource;

        public static object Read()
        {
            try
            {
                var settings = new Dictionary<string, object>
                {
                    { "sharePercent", Logic.SharePercent }, { "farePercent", Logic.FarePercent }, { "pricePercent", Logic.PricePercent },
                    { "sleeperSharePercent", Logic.SleeperSharePercent }, { "sleeperFarePercent", Logic.SleeperFarePercent }, { "sleeperPricePercent", Logic.SleeperPricePercent },
                    { "observationPercent", Logic.ObservationPercent }, { "sleeperMiles", Logic.SleeperMiles },
                    { "convertParlorPercent", Logic.ConvertParlorPercent }, { "convertSleeperPercent", Logic.ConvertSleeperPercent }, { "heatFarePercent", Logic.HeatFarePercent }, { "fareBase", Logic.FareBase }, { "farePerMile", Logic.FarePerMile }, { "fareCap", Logic.FareCap >= Logic.UnlimitedFareCap ? null : (object)Logic.FareCap }, { "steamHeat", Logic.SteamHeat }, { "fareLine", Logic.FareLine }, { "payBreakdown", Logic.PayBreakdown },
                    { "readPackFiles", Logic.ReadPackFiles }, { "loungePatience", Logic.LoungePatience }, { "baggageRiders", Logic.BaggageRiders },
                    { "loungeHours", Logic.LoungeHours }, { "bagNearCoachLb", Logic.BagNearCoach }, { "bagNearFirstLb", Logic.BagNearFirst }, { "bagNearSleeperLb", Logic.BagNearSleeper },
                    { "bagFarPercent", Logic.BagFarPercent }, { "bagExcessPercent", Logic.BagExcessPercent }, { "bagFeePer100", Logic.BagFeePer100 }, { "bagRackLb", Logic.BagRackLb },
                    { "bagRackParlorLb", Logic.BagRackParlorLb }, { "bagRackSleeperLb", Logic.BagRackSleeperLb }, { "bagLuggageLb", Logic.BagLuggageLb },
                };
                var words = new List<object>();
                for (int i = Classes.Coach; i < Classes.Words.Length; i++) words.Add(Classes.Words[i]);
                Daily.Read(Now(), Host(), out object today, out object yesterday);
                return new Dictionary<string, object>
                {
                    { "api", Version }, { "feed", Feed }, { "settings", settings }, { "classes", words },
                    { "cars", Rows(CarSource) }, { "stations", Rows(StationSource) },
                    { "today", today }, { "yesterday", yesterday },
                };
            }
            catch (Exception e)
            {
                string why;
                try { why = e.GetType().Name + ": " + PathClean.CleanText(e.Message); }
                catch (Exception) { why = "the feed failed"; }
                return Error(Feed, Version, why);
            }
        }

        internal static object ReadFirstClass()
        {
            try
            {
                var settings = new Dictionary<string, object>
                {
                    { "sharePercent", Logic.SharePercent }, { "farePercent", Logic.FarePercent }, { "pricePercent", Logic.PricePercent },
                    { "sleeperSharePercent", Logic.SleeperSharePercent }, { "sleeperFarePercent", Logic.SleeperFarePercent }, { "sleeperPricePercent", Logic.SleeperPricePercent },
                };
                var ids = new List<object>();
                var sleepers = new List<object>();
                foreach (string[] c in Logic.Cars) (Logic.IsSleeperId(c[1]) ? sleepers : ids).Add(c[1]);
                return new Dictionary<string, object>
                {
                    { "api", FirstClassFeed.Api.Version }, { "feed", FirstClassFeed.Api.Feed }, { "settings", settings }, { "firstClassIds", ids }, { "sleeperIds", sleepers },
                    { "cars", Rows(CompatCarSource) }, { "stations", Rows(StationSource) },
                };
            }
            catch (Exception e)
            {
                string why;
                try { why = e.GetType().Name + ": " + PathClean.CleanText(e.Message); }
                catch (Exception) { why = "the feed failed"; }
                return Error(FirstClassFeed.Api.Feed, FirstClassFeed.Api.Version, why);
            }
        }

        private static object Error(string feed, int version, string why)
        {
            try { return new Dictionary<string, object> { { "api", version }, { "feed", feed }, { "error", why } }; }
            catch (Exception) { return null; }
        }

        private static double Now()
        {
            try { return NowSource == null ? double.NaN : NowSource(); }
            catch (Exception) { return double.NaN; }
        }

        private static bool Host()
        {
            try { return HostSource != null && HostSource(); }
            catch (Exception) { return false; }
        }

        private static List<object> Rows(Func<List<object>> src)
        {
            try { return (src == null ? null : src()) ?? new List<object>(); }
            catch (Exception) { return new List<object>(); }
        }
    }
}

namespace PassengerClasses.FirstClassFeed
{
    // First Class's feed, kept under its own id and keys (api 1) for the readers written for that mod.
    public static class Api
    {
        public const int Version = 1;
        public const string Feed = "first-class";

        public static object Read() => PassengerClasses.Api.ReadFirstClass();
    }
}
