using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railroader.Modding;

namespace PassengerClasses
{
    // settings.json (Preferences > Mods). All are read live: the boarding, the fare, the price and the brakes ask for them each time.
    // loungePatience and baggageRiders (and the numbers beside them) are off by default.
    internal sealed class Settings
    {
        public int sharePercent = Logic.DefaultSharePercent;
        public int farePercent = Logic.DefaultFarePercent;
        public int pricePercent = Logic.DefaultPricePercent;
        public int sleeperSharePercent = Logic.DefaultSleeperSharePercent;
        public int sleeperFarePercent = Logic.DefaultSleeperFarePercent;
        public int sleeperPricePercent = Logic.DefaultSleeperPricePercent;
        public int observationPercent = Logic.DefaultObservationPercent;
        public int sleeperMiles = Logic.DefaultSleeperMiles;
        public bool readPackFiles = true;
        public bool loungePatience = false;
        public bool baggageRiders = false;
        public bool classMenu = false;
        public int refitLaborPercent = Logic.DefaultRefitLaborPercent;
        public int refitLaborMinimum = Logic.DefaultRefitLaborMinimum;
        public int bagNearCoachLb = Logic.DefaultBagNearCoach;
        public int bagNearFirstLb = Logic.DefaultBagNearFirst;
        public int bagNearSleeperLb = Logic.DefaultBagNearSleeper;
        public int bagFarPercent = Logic.DefaultBagFarPercent;
        public int bagExcessPercent = Logic.DefaultBagExcessPercent;
        public int bagFeePer100 = Logic.DefaultBagFeePer100;
        public int bagRackLb = Logic.DefaultBagRackLb;
        public int bagRackParlorLb = Logic.DefaultBagRackParlorLb;
        public int bagRackSleeperLb = Logic.DefaultBagRackSleeperLb;
        public int bagLuggageLb = Logic.DefaultBagLuggageLb;
        public int loungeHours = Logic.DefaultLoungeHours;
        public int convertParlorPercent = Logic.DefaultConvertParlorPercent;
        public int convertSleeperPercent = Logic.DefaultConvertSleeperPercent;
        public int heatFarePercent = Logic.DefaultHeatFarePercent;
        public bool steamHeat = true;
        public bool fareLine = true;
        public bool payBreakdown = true;
        // The shared debug log's switch (DebugLog.cs): one switch for every ashenmare mod, so this is only this mod's copy of it.
        public bool debugLog = false;
    }

    // Railroader 0.6 entry point. The twin cars themselves are Scripts/Data.rl; this dll is the class of every passenger car (Classes.cs),
    // the rule for who sits in them (Boarding.cs), what they pay (Fares.cs, Observation.cs), what they cost (Pricing.cs) and how they
    // brake (Brakes.cs). Required to join (Info.json scope "unscoped"): the twin definitions must exist on every peer. The boarding and
    // the fares run where the game runs them, on the host.
    //
    // First Class: this mod replaces it. Info.json lists First Class as incompatible, so the game itself leaves this mod off while
    // First Class is on (AssetPackManager.CheckIncompatibilities). If that ever failed, OnModLoad checks again and stands down. The
    // settings First Class saved are carried over once, when this mod has none of its own yet.
    //
    // Mod load resolves every game member the code reaches and patches nothing unless all are there, then applies the patches all or
    // nothing. Either way a game update that breaks it leaves one warning and the game as it is: the first-class cars exist, load like
    // coaches, and pay the coach fare.
    // The class-load rule: the lifecycle methods name only BCL and this mod's own game-free types (Logic, Api, PathClean, ModFeeds);
    // Boarding, Fares and Pricing are reached only through the NoInlining trampolines below, each called inside a try.
    // Railroader.Modding's IModEntryPoint, IWorldLifecycle, IModContext (ModDirectory, Log) and IModLogger, as the house mods use them.
    public sealed class Main : IModEntryPoint, IWorldLifecycle
    {
        internal const string HarmonyId = "ashenmare.passenger-classes";
        private const string FirstClassId = "ashenmare/first-class";
        internal const string Id = "passenger-classes";   // the mod's name in the debug log
        internal static IModLogger Log;
        private static readonly HashSet<string> Logged = new HashSet<string>();
        private Harmony _harmony;
        private static bool _ready;

        internal static void WarnOnce(string key, string text) { if (Logged.Add(key)) Log?.Warning(text); }
        internal static void InfoOnce(string key, string text) { if (Logged.Add(key)) Log?.Info(text); }

        internal static string ChoicesPath;

        public void OnModLoad(IModContext context)
        {
            // Assemblies never unload and statics survive a mod-set change: reset everything here.
            Log = context.Log;
            string settingsPath = Path.Combine(context.ModDirectory, "settings.json");
            ChoicesPath = Path.Combine(context.ModDirectory, Choices.FileName);
            Logged.Clear();
            _ready = false;
            Api.CarSource = null; Api.CompatCarSource = null; Api.StationSource = null; Api.NowSource = null; Api.HostSource = null;
            Classes.Reset(); Classes.PackEntryFor = null; PackFile.Reset();
            try { DebugInit(); } catch (Exception) { }
            bool firstClassOn = false;
            try { firstClassOn = context.CheckDependency(FirstClassId); } catch (Exception) { }
            if (firstClassOn)
            {
                Log.Warning("Passenger Classes: First Class is loaded too, so this mod is standing down; remove First Class (this mod replaces it)");
                if (DebugLog.On) DebugLog.Event(Id, "stand-down", "reason", "first-class-loaded");
                return;
            }
            try { string note = ImportFirstClass(settingsPath); if (note != null) Log.Info("Passenger Classes: " + note); } catch (Exception) { }
            Settings loaded = LoadSettings(settingsPath);
            Apply(loaded);
            try { StartDebug(settingsPath, loaded); } catch (Exception) { }
            if (DebugLog.On) LogSettings("settings-load");
            string bad = Choices.Load(ChoicesPath);
            if (bad != null) Log.Warning("Passenger Classes: " + bad + "; the class choices start empty and the file is kept as it is until you make one");
            _harmony = new Harmony(HarmonyId);
            try { _harmony.UnpatchAll(HarmonyId); } catch (Exception) { }
            string missing;
            try { missing = ResolveGame(); }
            catch (Exception e)
            {
                Log.Warning("Passenger Classes couldn't start on this game version; nothing is patched and first-class cars load like coaches: " + PathClean.CleanText(e.ToString(), 4000));
                if (DebugLog.On) DebugLog.Event(Id, "stand-down", "reason", "resolve-threw", "type", e.GetType().Name);
                return;
            }
            if (missing != null)
            {
                Log.Warning("Passenger Classes couldn't start on this game version (not found: " + missing + "); nothing is patched and first-class cars load like coaches");
                if (DebugLog.On) DebugLog.Event(Id, "stand-down", "reason", "member-missing", "missing", missing);
                return;
            }
            DayBookResolve();
            StationResolve();
            CarLineResolve();
            RefitResolve();
            try { PatchGame(_harmony); }
            catch (Exception e)
            {
                try { _harmony.UnpatchAll(HarmonyId); } catch (Exception) { }
                Log.Warning("Passenger Classes couldn't start on this game version (a hook wouldn't go on); nothing is patched and first-class cars load like coaches: " + PathClean.CleanText(e.ToString(), 4000));
                if (DebugLog.On) DebugLog.Event(Id, "stand-down", "reason", "patch-threw", "type", e.GetType().Name);
                return;
            }
            _ready = true;
            try { SetLive(true); } catch (Exception) { }
            try { RefitLive(true); } catch (Exception) { }
            try { Classes.PackEntryFor = PackEntries(); } catch (Exception) { }
            try { Api.CarSource = CarReader(); Api.CompatCarSource = CompatCarReader(); Api.StationSource = StationReader(); Api.NowSource = NowReader(); Heat.Clock = NowReader(); Api.HostSource = HostReader(); } catch (Exception) { }
            try { SettingsUi.Start(_harmony, settingsPath); }
            catch (Exception e)
            {
                StopSettings();
                Log.Warning("Passenger Classes: the settings aren't in Preferences > Mods on this game version (the mod works without them): " + PathClean.CleanText(e.Message));
            }
            try { if (!PublishFeed()) Log.Warning("Passenger Classes: another mod already publishes the feed id \"" + Api.Feed + "\", so this one isn't listed"); }
            catch (Exception) { }
            Log.Info("Passenger Classes loaded: " + Logic.Cars.Length + " first-class and sleeper twins, " + Classes.Player.Count + " class choices of your own");
            if (DebugLog.On) DebugLog.Event(Id, "load", "twins", Logic.Cars.Length, "choices", Classes.Player.Count, "choicesUnreadable", bad != null);
        }

        // The off state first, so the hooks are inert from here; then the feed and the patches off.
        public void OnModUnload()
        {
            try { SetLive(false); } catch (Exception) { }
            try { RefitLive(false); } catch (Exception) { }
            try { DebugLog.Forget(Id); } catch (Exception) { }
            StopSettings();
            Api.CarSource = null; Api.CompatCarSource = null; Api.StationSource = null; Api.NowSource = null; Api.HostSource = null;
            Classes.PackEntryFor = null;
            try { ModFeeds.Withdraw(Api.Feed); } catch (Exception) { }
            try { ModFeeds.Withdraw(FirstClassFeed.Api.Feed); } catch (Exception) { }
            try { _harmony?.UnpatchAll(HarmonyId); }
            catch (Exception e) { Log?.Warning("Passenger Classes: couldn't take its hooks off: " + PathClean.CleanText(e.Message)); }
            _harmony = null;
            _ready = false;
        }

        private static void StopSettings()
        {
            try { SettingsUi.Stop(); } catch (Exception) { }
        }

        private static void Apply(Settings s)
        {
            Settings v = s ?? new Settings();
            Logic.Apply(v.sharePercent, v.farePercent, v.pricePercent, v.sleeperSharePercent, v.sleeperFarePercent, v.sleeperPricePercent,
                        v.observationPercent, v.sleeperMiles, v.readPackFiles, v.loungePatience, v.baggageRiders);
            Logic.ApplyBaggage(v.bagNearCoachLb, v.bagNearFirstLb, v.bagNearSleeperLb, v.bagFarPercent, v.bagExcessPercent, v.bagFeePer100, v.bagRackLb, v.bagRackParlorLb, v.bagRackSleeperLb, v.bagLuggageLb, v.loungeHours);
            Logic.ApplyConversion(v.convertParlorPercent, v.convertSleeperPercent);
            Logic.ApplyHeat(v.heatFarePercent);
            Logic.ApplySteamHeat(v.steamHeat);
            Logic.ApplyReadouts(v.fareLine, v.payBreakdown);
            Logic.ClassMenu = v.classMenu;
            Logic.ApplyRefit(v.refitLaborPercent, v.refitLaborMinimum);
            Classes.Reset();
        }

        private static Settings LoadSettings(string path)
        {
            try { if (File.Exists(path)) return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path)) ?? new Settings(); }
            catch (Exception e) { Log?.Warning("Passenger Classes: couldn't read settings.json, using the defaults: " + PathClean.CleanText(e.Message)); }
            return new Settings();
        }

        // After a Save in Preferences > Mods: the file just written.
        internal static void Applied(JObject saved)
        {
            Settings now = saved.ToObject<Settings>();
            Apply(now);
            // The row is this mod's copy of the shared switch: when the saved file has it and it differs from the switch, the player
            // just changed it here; a file without the key says nothing about it.
            try { if (saved.TryGetValue("debugLog", StringComparison.OrdinalIgnoreCase, out _) && now.debugLog != DebugLog.On) DebugLog.Set(now.debugLog); }
            catch (Exception) { }
            if (DebugLog.On) LogSettings("settings-saved");      // before the brakes and the seats follow, so the lines after it say what they ran on
            try { ReapplyBrakes(); } catch (Exception) { }
            Log?.Info("Passenger Classes: first class share " + Logic.SharePercent + "%, fare " + Logic.FarePercent + "%, price " + Logic.PricePercent + "%; sleeper share " + Logic.SleeperSharePercent + "%, fare " + Logic.SleeperFarePercent + "%, price " + Logic.SleeperPricePercent + "%, from " + Logic.SleeperMiles + " miles; observation " + Logic.ObservationPercent + "%; pack files " + (Logic.ReadPackFiles ? "read" : "off"));
        }

        // ---- the shared debug log (DebugLog.cs). The switch itself is shared; the row in this mod's settings is a copy of it, so at load
        // the copy is made to match, and whenever any mod changes the switch every loaded mod's copy follows. ----
        private static string _settingsPath;

        // Before anything else decides: the log can say why this mod stood down.
        private static void DebugInit()
        {
            Version v = typeof(Main).Assembly.GetName().Version;
            DebugLog.Init(Id, v == null ? "" : v.Major + "." + v.Minor + "." + v.Build);
        }

        private static void StartDebug(string settingsPath, Settings loaded)
        {
            _settingsPath = settingsPath;
            if (loaded.debugLog != DebugLog.On) MirrorDebug(DebugLog.On);
            DebugLog.OnChanged(Id, on =>
            {
                MirrorDebug(on);
                try { DebugChanged(); } catch (Exception) { }
                if (DebugLog.On && on) LogSettings("settings-on");      // switched on in the middle of a play: the numbers the lines after it run on
            });
        }

        // Writes the switch into this mod's settings.json when the file's own value differs, keeping every other key. A file
        // that can't be read is left alone.
        private static void MirrorDebug(bool on)
        {
            try
            {
                if (_settingsPath == null || !File.Exists(_settingsPath)) return;
                JObject o = JObject.Parse(File.ReadAllText(_settingsPath));
                if (o.TryGetValue("debugLog", StringComparison.OrdinalIgnoreCase, out JToken t) && t.Type == JTokenType.Boolean && (bool)t == on) return;
                o["debugLog"] = on;
                string tmp = _settingsPath + ".tmp";
                File.WriteAllText(tmp, o.ToString(Formatting.Indented));
                File.Delete(_settingsPath);
                File.Move(tmp, _settingsPath);
            }
            catch (Exception e) { Log?.Warning("Passenger Classes: couldn't update the debug log row in settings.json: " + PathClean.CleanText(e.Message)); }
        }

        // The switch changed (any mod): what the log remembers between lines starts again, so the next lines say where things stand.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DebugChanged() => Boarding.DebugReset();

        // Every setting the decisions run on, so the arithmetic in the log can be checked against the numbers it ran on.
        internal static void LogSettings(string name) =>
            DebugLog.Event(Id, name, "share", Logic.SharePercent, "fare", Logic.FarePercent, "price", Logic.PricePercent,
                "slShare", Logic.SleeperSharePercent, "slFare", Logic.SleeperFarePercent, "slPrice", Logic.SleeperPricePercent, "slMiles", Logic.SleeperMiles,
                "obs", Logic.ObservationPercent, "steamHeat", Logic.SteamHeat, "heatFarePercent", Logic.HeatFarePercent, "fareLine", Logic.FareLine, "payBreakdown", Logic.PayBreakdown, "convParlor", Logic.ConvertParlorPercent, "convSleeper", Logic.ConvertSleeperPercent,
                "packFiles", Logic.ReadPackFiles, "classMenu", Logic.ClassMenu, "refitPercent", Logic.RefitLaborPercent, "refitMinimum", Logic.RefitLaborMinimum, "lounge", Logic.LoungePatience, "loungeHours", Logic.LoungeHours, "bags", Logic.BaggageRiders,
                "bagCoach", Logic.BagNearCoach, "bagFirst", Logic.BagNearFirst, "bagSleeper", Logic.BagNearSleeper, "bagFar", Logic.BagFarPercent,
                "bagExcess", Logic.BagExcessPercent, "bagFee", Logic.BagFeePer100, "bagRack", Logic.BagRackLb, "bagRackFirst", Logic.BagRackParlorLb, "bagRackSleeper", Logic.BagRackSleeperLb, "bagLuggage", Logic.BagLuggageLb);

        public void OnWorldLoad() { NewWorld(); try { DebugWorld(); } catch (Exception) { } }

        public void OnWorldUnload() { NewWorld(); }

        // A world starts clean: the counts, the pending extras, the stand-down latches and the price map.
        private static void NewWorld()
        {
            if (!_ready) return;
            Logged.Clear();
            try { ResetGame(); } catch (Exception) { }
            try { ReapplyBrakes(); } catch (Exception) { }
            try { SetLive(true); } catch (Exception) { }
            try { RefitWorld(); } catch (Exception) { }
        }

        // The world as it loaded: how many cars, and how many are the First Class twins (the saved first-class and sleeper cars found).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DebugWorld()
        {
            if (DebugLog.On && _ready) Boarding.LogWorld();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ResolveGame()
        {
            var list = new List<string>();
            foreach (string m in new[] { Boarding.Resolve(), Fares.Resolve(), Pricing.Resolve(), Brakes.Resolve(), Lounge.Resolve(), Observation.Resolve(), Menu.Resolve(), PackSource.Resolve() })
                if (m != null) list.Add(m);
            return list.Count == 0 ? null : string.Join(", ", list.ToArray());
        }

        // The day book (the feed's today and yesterday) is optional: a game version that lacks a member it needs, or a hook that won't go on, turns the
        // day book off (the feed says `unavailable`) and nothing else. Boarding, fares and every other hook are not touched.
        private static void DayBookResolve()
        {
            string why = null;
            try { why = ResolveDay(); }
            catch (Exception e) { why = "its members (" + e.GetType().Name + ")"; }
            Daily.SetUnavailable(why != null);
            if (why == null) return;
            Log.Warning("Passenger Classes: the day figures for the feed are off on this game version (not found: " + why + "); boarding and fares go on");
            if (DebugLog.On) DebugLog.Event(Id, "daily-down", "reason", "member-missing", "missing", why);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ResolveDay() => Boarding.ResolveDaily();

        // The station window's First and Sleeper columns are optional too: a game version that lacks a member the window needs, or whose table
        // method isn't the one this was made for, or a hook that won't go on, turns the columns off (the game's own table) and nothing else.
        private static void StationResolve()
        {
            string why = null;
            try { why = ResolveStation(); }
            catch (Exception e) { why = "its members (" + e.GetType().Name + ")"; }
            StationOff(why != null);
            if (why == null) return;
            Log.Warning("Passenger Classes: the First and Sleeper columns in the station window are off on this game version (" + why + "); everything else goes on");
            if (DebugLog.On) DebugLog.Event(Id, "station-down", "reason", "member-missing", "missing", why);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ResolveStation() => StationCols.Resolve();

        // The Fare line in a passenger car's window (CarLine.cs) is optional the same way: a game version without a member it needs, or a hook that won't go on,
        // leaves the game's window as it is and nothing else changes.
        private static void CarLineResolve()
        {
            string why = null;
            try { why = ResolveCarLine(); }
            catch (Exception e) { why = "its members (" + e.GetType().Name + ")"; }
            CarLineOff(why != null);
            if (why == null) return;
            Log.Warning("Passenger Classes: the Fare line in the car window is off on this game version (not found: " + why + "); everything else goes on");
            if (DebugLog.On) DebugLog.Event(Id, "carline-down", "reason", "member-missing", "missing", why);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ResolveCarLine() => CarLine.Resolve();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CarLineOff(bool off) { CarLine.Off = off; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchCarLine(Harmony harmony)
        {
            if (CarLine.Off) return;
            try { CarLine.Patch(harmony); }
            catch (Exception e)
            {
                try { harmony.Unpatch(HarmonyLib.AccessTools.Method(typeof(UI.CarInspector.CarInspector), "PopulatePassengerCarPanel", new[] { typeof(UI.Builder.UIPanelBuilder) }), HarmonyPatchType.All, HarmonyId); } catch (Exception) { }
                CarLine.Off = true;
                WarnOnce("carline-patch", "Passenger Classes: the Fare line is off (the car window hook wouldn't go on: " + PathClean.CleanText(e.Message) + "); everything else goes on");
                if (DebugLog.On) DebugLog.Event(Id, "carline-down", "reason", "patch-threw", "type", e.GetType().Name);
            }
        }

        // The refit (Refit.cs) is optional as well: it reaches two private members of the game's car handling by reflection, so a game version
        // without them, or with other signatures, never shows the refit entries (one warning) and nothing else changes. It adds no hook of its own.
        private static void RefitResolve()
        {
            string why = null;
            try { why = ResolveRefit(); }
            catch (Exception e) { why = "its members (" + e.GetType().Name + ")"; }
            try { RefitHidden(why != null); } catch (Exception) { }
            try { RefitOff(why != null); } catch (Exception) { }
            if (why == null) return;
            Log.Warning("Passenger Classes: the refit entries in the car menu are off on this game version (not found: " + why + "); everything else goes on");
            if (DebugLog.On) DebugLog.Event(Id, "refit-down", "reason", "member-missing", "missing", why);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ResolveRefit() => Refit.Resolve();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefitOff(bool off) => Refit.SetOff(off);

        // Menu's own switch, set apart from Refit's: it holds even when Refit.cs itself cannot load on this game version.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefitHidden(bool off) { Menu.RefitHidden = off; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefitLive(bool on) => Refit.SetLive(on);

        // A world starts with the refit's failure count at zero and, where the preflight passed, the entries back on.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefitWorld() { Refit.ResetWorld(); Refit.SetLive(true); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StationOff(bool off) { StationCols.Off = off; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchStation(Harmony harmony)
        {
            if (StationCols.Off) return;
            try { StationCols.Patch(harmony); }
            catch (Exception e)
            {
                try { harmony.Unpatch(HarmonyLib.AccessTools.Method(typeof(UI.StationWindow.StationWindow), "BuildPassengerTab"), HarmonyPatchType.All, HarmonyId); } catch (Exception) { }
                StationCols.Off = true;
                WarnOnce("station-patch", "Passenger Classes: the First and Sleeper columns are off (the station window hook wouldn't go on: " + PathClean.CleanText(e.Message) + "); everything else goes on");
                if (DebugLog.On) DebugLog.Event(Id, "station-down", "reason", "patch-threw", "type", e.GetType().Name);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchDay(Harmony harmony)
        {
            if (!Daily.Unavailable && !Boarding.PatchDaily(harmony)) Daily.SetUnavailable(true);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchGame(Harmony harmony)
        {
            Boarding.Patch(harmony);
            Fares.Patch(harmony);
            Pricing.Patch(harmony);
            Brakes.Patch(harmony);
            Observation.Patch(harmony);
            Lounge.Patch(harmony);
            Menu.Patch(harmony);
            PatchDay(harmony);
            PatchStation(harmony);
            PatchCarLine(harmony);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetLive(bool on)
        {
            Boarding.Live = on;
            Fares.Live = on;
            Pricing.Live = on;
            Brakes.Live = on;
            Observation.Live = on;
            Lounge.Live = on;
            Menu.Live = on;
            StationCols.Live = on;
            CarLine.Live = on;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResetGame()
        {
            Boarding.ResetWorld();
            Fares.ResetWorld();
            Pricing.ResetWorld();
            Brakes.ResetWorld();
            Observation.ResetWorld();
            Lounge.ResetWorld();
            Menu.ResetWorld();
            StationCols.ResetWorld();
            CarLine.ResetWorld();
            Classes.Reset(); PackFile.Reset(); PackSource.ResetLog();
        }

        // A model's class changed, or a setting that decides classes: every car runs its brake setup again.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReapplyBrakes() { Boarding.ApplyChosen(); Brakes.ReapplyAll(); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ImportFirstClass(string settingsPath) => Migration.Import(settingsPath);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Func<string, Classes.Decl> PackEntries() => PackSource.Make();

        // Writes the player's class choices. Game-free: Menu calls it after every click.
        internal static void SaveChoices()
        {
            string bad = Choices.Save(ChoicesPath);
            if (bad != null) WarnOnce("choices-save", "Passenger Classes: " + bad + "; the choice holds for this session");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Func<List<object>> CarReader() => Boarding.ReadCarsRich;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Func<List<object>> CompatCarReader() => Boarding.ReadCarsCompat;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Func<List<object>> StationReader() => Boarding.ReadStations;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Func<double> NowReader() => Boarding.NowSeconds;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Func<bool> HostReader() => Boarding.IsHostNow;

        internal static bool PublishFeed()
        {
            Version v = typeof(Main).Assembly.GetName().Version;
            string version = v == null ? "" : v.Major + "." + v.Minor + "." + v.Build;
            var fields = new Dictionary<string, string>
            {
                { "settings", "the settings in force: sharePercent, farePercent, pricePercent (first class), sleeperSharePercent, sleeperFarePercent, sleeperPricePercent, observationPercent (the whole-train bonus as a percent), sleeperMiles (the shortest trip with sleeper riders), convertParlorPercent and convertSleeperPercent (the share of its seats a coach keeps when the player makes it a parlor or a sleeper), steamHeat (the whole heat feature, on or off), fareLine and payBreakdown (the car window's Fare line and the stop payment's breakdown line, on or off), heatFarePercent (the cold-weather fare effect, a percent of its set size, 0 off), readPackFiles, loungePatience and loungeHours (the lounge rule), baggageRiders and the bag settings (bagNearCoachLb, bagNearFirstLb, bagNearSleeperLb, bagFarPercent, bagExcessPercent, bagFeePer100, bagRackLb, bagRackParlorLb, bagRackSleeperLb, bagLuggageLb)" },
                { "classes", "the class words a car can have" },
                { "cars", "each passenger car, and each car with a class (baggage cars): id (car id), definition (definition id), class, classSource (default, built-in, pack file or player), observation, and for a passenger car seats and seatsUsed; baggageLb when a pack gave the car a baggage figure; for a first-class or sleeper car firstClassSeatsUsed or sleeperBerthsUsed and noRoomSinceArrival (riders of its class who found no seat or berth since it last pulled into a station); heatEffectPercent and heatEffect (words) when the cold-weather fare moves this passenger car's fare now; heatEffect alone, \"Heated\", in the cold months when Realistic Steam and Fuel Use's heat is not in play" },
                { "today", "the game day so far, from the host's own boarding and fares: day, known, partial, total (the whole railroad), stations and trains, each with boarded riders by class, gameFare and extra and heatFare by class, bagFees, paid (not on a train), left-behind counts (noRoom, noBerth, heldForBags, gaveUp) and the fare band of each (bands); stationsFull and trainsFull when a day passed the row limits. Counting starts at a world load; nothing is saved" },
                { "yesterday", "the game day before, the same shape; known is false until a midnight has passed since the load, and on a client" },
                { "stations", "each station: id, industryId, name, waiting (people waiting, all destinations), firstClassWaiting and sleeperWaiting (how many of them want that class; the host's count), firstClassNoRoom and sleeperNoBerth (riders who wanted that class and rode a lower one, since the last train pulled in), and with baggage on heldByBaggage (riders left waiting because their bags did not fit)" },
            };
            var columns = new List<ModFeeds.Column>
            {
                new ModFeeds.Column("passengerClass", "car", "Passenger class", "text", "cars[].class", key: "id"),
                new ModFeeds.Column("observation", "car", "Observation car", "bool", "cars[].observation", key: "id"),
                new ModFeeds.Column("seats", "car", "Seats", "number", "cars[].seats", key: "id"),
                new ModFeeds.Column("seatsUsed", "car", "Seats used", "number", "cars[].seatsUsed", key: "id"),
                new ModFeeds.Column("firstClassWaiting", "station", "First-class riders waiting", "number", "stations[].firstClassWaiting", key: "industryId"),
                new ModFeeds.Column("firstClassNoRoom", "station", "First class, no room", "number", "stations[].firstClassNoRoom", key: "industryId"),
                new ModFeeds.Column("sleeperWaiting", "station", "Sleeper riders waiting", "number", "stations[].sleeperWaiting", key: "industryId"),
                new ModFeeds.Column("sleeperNoBerth", "station", "Sleeper, no berth", "number", "stations[].sleeperNoBerth", key: "industryId"),
                new ModFeeds.Column("heatEffect", "car", "Cold-weather fare", "text", "cars[].heatEffect", key: "id"),
                new ModFeeds.Column("baggageUsedLb", "car", "Baggage aboard", "number", "cars[].baggageUsedLb", key: "id", unit: "lb"),
                new ModFeeds.Column("baggageRoomLb", "car", "Baggage room", "number", "cars[].baggageRoomLb", key: "id", unit: "lb"),
                new ModFeeds.Column("heldByBaggage", "station", "Held by baggage", "number", "stations[].heldByBaggage", key: "industryId"),
            };
            bool main = ModFeeds.Publish(Api.Feed, Api.Read, "Passenger Classes", "Passenger Classes", version, Api.Version, fields, columns, out string _);
            // First Class's feed, under its old id and keys, with no columns (the ones above carry the values once).
            var old = new Dictionary<string, string>
            {
                { "settings", "the first six settings: sharePercent, farePercent, pricePercent, sleeperSharePercent, sleeperFarePercent, sleeperPricePercent" },
                { "firstClassIds", "the definition ids of the first-class twins" },
                { "sleeperIds", "the definition ids of the sleeper twins" },
                { "cars", "each passenger car: id, definition, firstClass and sleeper (true for that class), and for a first-class or sleeper car firstClassSeatsUsed or sleeperBerthsUsed and noRoomSinceArrival" },
                { "stations", "each station: id, industryId, name, waiting, firstClassWaiting, sleeperWaiting, firstClassNoRoom, sleeperNoBerth" },
            };
            // its four car columns as First Class declared them (same ids, labels and keys); the station columns are the new feed's alone
            var oldColumns = new List<ModFeeds.Column>
            {
                new ModFeeds.Column("firstClass", "car", "First class", "bool", "cars[].firstClass", key: "id"),
                new ModFeeds.Column("firstClassSeatsUsed", "car", "First class seats used", "number", "cars[].firstClassSeatsUsed", key: "id"),
                new ModFeeds.Column("sleeper", "car", "Sleeper", "bool", "cars[].sleeper", key: "id"),
                new ModFeeds.Column("sleeperBerthsUsed", "car", "Sleeper berths used", "number", "cars[].sleeperBerthsUsed", key: "id"),
            };
            try { ModFeeds.Publish(FirstClassFeed.Api.Feed, FirstClassFeed.Api.Read, "First Class (kept for older readers)", "Passenger Classes", version, FirstClassFeed.Api.Version, old, oldColumns, out string _); }
            catch (Exception) { }
            return main;
        }
    }

    // Everything typed by SettingsSection.cs (and so by the game's UI) lives here, off Main, behind NoInlining methods called inside a
    // try, so a game update that breaks the section can't take the mod with it (the house pattern).
    internal static class SettingsUi
    {
        private static object _section;   // a SettingsSection

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Start(Harmony harmony, string settingsPath)
        {
            Stop();
            var fields = new[]
            {
                SettingField.Int("sharePercent", "First-class riders (% of each crowd)", Logic.DefaultSharePercent, Logic.MinSharePercent, Logic.MaxSharePercent, 5, true, "How many riders want first class."),
                SettingField.Int("farePercent", "First-class fare (% of the coach fare)", Logic.DefaultFarePercent, Logic.MinFarePercent, Logic.MaxFarePercent, 5, true, "What a first-class rider pays."),
                SettingField.Int("pricePercent", "First-class car price (% of the stock car)", Logic.DefaultPricePercent, Logic.MinPricePercent, Logic.MaxPricePercent, 5, true, "What a first-class car costs."),
                SettingField.Int("sleeperSharePercent", "Sleeper riders (% of each crowd)", Logic.DefaultSleeperSharePercent, Logic.MinSharePercent, Logic.MaxSharePercent, 1, true, "How many riders want a sleeper."),
                SettingField.Int("sleeperFarePercent", "Sleeper fare (% of the coach fare)", Logic.DefaultSleeperFarePercent, Logic.MinFarePercent, Logic.MaxFarePercent, 5, true, "What a sleeper rider pays."),
                SettingField.Int("sleeperPricePercent", "Sleeper car price (% of the stock car)", Logic.DefaultSleeperPricePercent, Logic.MinPricePercent, Logic.MaxPricePercent, 5, true, "What a sleeper car costs."),
                SettingField.Int("convertParlorPercent", "A coach made a parlor keeps this % of its seats", Logic.DefaultConvertParlorPercent, Logic.MinConvertPercent, Logic.MaxConvertPercent, 5, true, "From the right-click menu. Cars built as parlors keep theirs."),
                SettingField.Int("convertSleeperPercent", "A coach made a sleeper keeps this % of its seats as berths", Logic.DefaultConvertSleeperPercent, Logic.MinConvertPercent, Logic.MaxConvertPercent, 5, true, "From the right-click menu."),
                SettingField.Int("sleeperMiles", "Sleeper riders: trips of at least (miles)", Logic.DefaultSleeperMiles, Logic.MinSleeperMiles, Logic.MaxSleeperMiles, 5, true, "Shorter trips have no sleeper riders."),
                SettingField.Int("observationPercent", "Observation car bonus (% of every fare in the train)", Logic.DefaultObservationPercent, Logic.MinObservationPercent, Logic.MaxObservationPercent, 5, true, "The game's own is 120."),
                SettingField.Bool("steamHeat", "Steam heat", true, true, "Heated cars in the cold, and fares that follow."),
                SettingField.Bool("fareLine", "Fare line in the car window", true, true, "What a car's fare is made of."),
                SettingField.Bool("payBreakdown", "Fare breakdown when a stop pays", true, true, "A second line in the console."),
                SettingField.Int("heatFarePercent", "Cold-weather fares (% of the set effect)", Logic.DefaultHeatFarePercent, Logic.MinHeatFarePercent, Logic.MaxHeatFarePercent, 10, true, "Needs Realistic Steam and Fuel Use and Steam heat. 0 turns it off."),
                SettingField.Bool("readPackFiles", "Read class files from car packs", true, true, "PassengerClasses.json beside a pack's definitions."),
                SettingField.Bool("classMenu", "Change car classes (right-click menu)", false, true, "Off hides the class entries in the car menu."),
                SettingField.Int("refitLaborPercent", "Refit labor (% of the first-class or sleeper car's price)", Logic.DefaultRefitLaborPercent, Logic.MinRefitLaborPercent, Logic.MaxRefitLaborPercent, 1, true, "Paid on every refit, on top of the price difference."),
                SettingField.Int("refitLaborMinimum", "Refit labor, at least ($)", Logic.DefaultRefitLaborMinimum, Logic.MinRefitLaborMinimum, Logic.MaxRefitLaborMinimum, 50, true, "The least a refit's labor costs."),
                SettingField.Bool("loungePatience", "Lounge cars: riders who change trains wait longer", false, true, "Off by default."),
                SettingField.Int("loungeHours", "Lounge car: extra hours of patience", Logic.DefaultLoungeHours, Logic.MinLoungeHours, Logic.MaxLoungeHours, 1, true, "Added to the game's 4 hours."),
                SettingField.Bool("baggageRiders", "Riders bring baggage", false, true, "Off by default."),
                SettingField.Int("bagNearCoachLb", "Baggage, $3 to $5 fare: coach rider (lb)", Logic.DefaultBagNearCoach, Logic.MinBagLb, Logic.MaxBagLb, 5, true, "Pounds of bags a coach rider brings."),
                SettingField.Int("bagNearFirstLb", "Baggage, $3 to $5 fare: first-class rider (lb)", Logic.DefaultBagNearFirst, Logic.MinBagLb, Logic.MaxBagLb, 5, true, "Pounds of bags a first-class rider brings."),
                SettingField.Int("bagNearSleeperLb", "Baggage, $3 to $5 fare: sleeper rider (lb)", Logic.DefaultBagNearSleeper, Logic.MinBagLb, Logic.MaxBagLb, 5, true, "Pounds of bags a sleeper rider brings."),
                SettingField.Int("bagFarPercent", "Baggage, $6 to $8 fare (% of the $3 to $5 figure)", Logic.DefaultBagFarPercent, Logic.MinBagFarPercent, Logic.MaxBagFarPercent, 10, true, "Longer trips, more bags."),
                SettingField.Int("bagExcessPercent", "Baggage: riders with double the allowance (%)", Logic.DefaultBagExcessPercent, Logic.MinBagExcessPercent, Logic.MaxBagExcessPercent, 5, true, "Their extra pounds ride in the baggage car and pay a fee."),
                SettingField.Int("bagFeePer100", "Baggage: excess fee ($ per 100 lb)", Logic.DefaultBagFeePer100, Logic.MinBagFeePer100, Logic.MaxBagFeePer100, 1, true, "Paid with the fare."),
                SettingField.Int("bagRackLb", "Baggage: racks, coach car (lb per seat)", Logic.DefaultBagRackLb, Logic.MinBagRackLb, Logic.MaxBagRackLb, 5, true, "What a coach or lounge car holds of its own riders' bags."),
                SettingField.Int("bagRackParlorLb", "Baggage: racks, parlor car (lb per seat)", Logic.DefaultBagRackParlorLb, Logic.MinBagRackLb, Logic.MaxBagRackLb, 5, true, "What a first-class car holds of its own riders' bags."),
                SettingField.Int("bagRackSleeperLb", "Baggage: racks, sleeper car (lb per berth)", Logic.DefaultBagRackSleeperLb, Logic.MinBagRackLb, Logic.MaxBagRackLb, 5, true, "What a sleeper holds of its own riders' bags."),
                SettingField.Int("bagLuggageLb", "Baggage: luggage area in each car (lb)", Logic.DefaultBagLuggageLb, Logic.MinBagLuggageLb, Logic.MaxBagLuggageLb, 50, true, "Holds its own riders' bags past the racks."),
                SettingField.Bool("debugLog", "Debug log (for bug reports)", false, true, "Writes Documents/Railroader/Logs; send the file with a bug report."),
            };
            _section = new SettingsSection("Passenger Classes", settingsPath, fields, Main.Applied, m => Main.Log?.Warning("Passenger Classes: " + m), null);
            PreferencesModsTab.Add(harmony, b => (_section as SettingsSection)?.Build(b, true), m => Main.Log?.Warning("Passenger Classes: " + m));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Stop()
        {
            PreferencesModsTab.Remove();
            _section = null;
        }
    }
}
