using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.State;
using HarmonyLib;
using Model;
using Model.Database;
using Model.Ops;
using RollingStock;
using UI.Common;
using UI.ContextMenu;

namespace PassengerClasses
{
    // Two game-typed pieces that feed Classes.cs.
    //
    // Menu: the car's right-click menu gets the class choices for the car's model ("Make it a parlor (first class)", and so on, the current
    // one left out, "Turn observation on" or "off", and "Back to its default" once a choice is set; the menu's first line says what the car is now). The choice is the player's, for
    // every car of that model, saved in choices.json beside settings.json. Only the host sees the entries: the boarding and the fares
    // run there. Built the way the game builds its own menu: CarPickable.HandleShowContextMenu says which car, ContextMenu.Show is the
    // moment to add buttons.
    internal static class Menu
    {
        internal static volatile bool Live;
        private static int _failures;
        private static Car _menuCar;
        // Test seam (tools/gamecheck): the menu itself needs the game's UI prefab; when set it is called instead of AddButton.
#pragma warning disable CS0649   // set by tools/gamecheck by reflection, never by the mod
        internal static Action<string, Action> MenuSeam;
#pragma warning restore CS0649

        // The refit entries (Refit.cs): hidden for good when their own load check failed (Main sets it), and for the rest of a world after three failures.
        internal static bool RefitHidden;
        private static int _refitFailures;

        internal static void ResetWorld() { _failures = 0; _refitFailures = 0; _menuCar = null; }

        // CarPickable.HandleShowContextMenu(Car) is private static, void: it clears the shared ContextMenu, adds the game's buttons for the car and calls Show(car.DisplayName) [load]
        // UI.ContextMenu.ContextMenu.Show(string) is public, void; AddButton(ContextMenuQuadrant, string, Action) is public; the click runs the action and hides the menu [load]
        // StateManager.IsHost is a public static bool; the boarding and the fares run only on the host [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            MethodInfo handle = typeof(CarPickable).GetMethod("HandleShowContextMenu", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Car) }, null);
            if (handle == null || handle.ReturnType != typeof(void)) return "CarPickable.HandleShowContextMenu";
            MethodInfo show = typeof(UI.ContextMenu.ContextMenu).GetMethod("Show", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (show == null || show.ReturnType != typeof(void)) return "ContextMenu.Show";
            if (typeof(UI.ContextMenu.ContextMenu).GetMethod("AddButton", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(ContextMenuQuadrant), typeof(string), typeof(Action) }, null) == null) return "ContextMenu.AddButton";
            bool modal = false;
            foreach (MethodInfo mi in typeof(ModalAlertController).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (mi.Name == "Present" && mi.IsGenericMethodDefinition && mi.GetParameters().Length == 4 && mi.GetParameters()[0].ParameterType == typeof(string)) modal = true;
            if (!modal) return "ModalAlertController.Present";
            PropertyInfo host = typeof(StateManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Static);
            if (host == null || host.PropertyType != typeof(bool)) return "StateManager.IsHost";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony h)
        {
            h.Patch(typeof(CarPickable).GetMethod("HandleShowContextMenu", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Car) }, null),
                    prefix: new HarmonyMethod(typeof(Menu), nameof(HandlePrefix)), finalizer: new HarmonyMethod(typeof(Menu), nameof(HandleFinalizer)));
            h.Patch(typeof(UI.ContextMenu.ContextMenu).GetMethod("Show", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null),
                    prefix: new HarmonyMethod(typeof(Menu), nameof(ShowPrefix)));
        }

        private static void Failed(string what, Exception e)
        {
            _failures++;
            if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "menu", "what", what, "type", e.GetType().Name, "n", _failures);
            Main.WarnOnce("menu-" + what, "Passenger Classes: " + what + " failed, the game's own menu stands: " + PathClean.CleanText(e.Message));
            if (_failures >= 3 && Live) { Live = false; Main.WarnOnce("menu-down", "Passenger Classes: the car menu choices stand down until the next world load"); }
        }

        // The car the menu is for. The class entries are behind a setting (off, none are added); the refit entries (Refit.cs) are not.
        internal static void HandlePrefix(Car car) { _menuCar = null; if (Live) _menuCar = car; }

        // Runs whether or not the game's method threw, so the car never reaches another menu.
        internal static void HandleFinalizer() { _menuCar = null; }

        internal static void ShowPrefix(UI.ContextMenu.ContextMenu __instance, ref string centerText)
        {
            Car car = _menuCar;
            _menuCar = null;
            if (car == null || !Live) return;
            if (Logic.ClassMenu) ClassEntries(__instance, car, ref centerText);
            if (RefitHidden || _refitFailures >= 3) return;
            try { RefitEntries(__instance, car); }
            catch (Exception e)
            {
                _refitFailures++;
                if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "refit-menu", "what", "adding the refit choices", "type", e.GetType().Name, "n", _refitFailures);
                Main.WarnOnce("refit-menu", "Passenger Classes: adding the refit choices to the car menu failed, the rest of the menu stands: " + PathClean.CleanText(e.Message));
            }
        }

        // Refit.cs names game members of its own (checked apart at load): it is reached only through this, inside the try above.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefitEntries(UI.ContextMenu.ContextMenu menu, Car car) => Refit.AddEntries(menu, car);

        private static void ClassEntries(UI.ContextMenu.ContextMenu __instance, Car car, ref string centerText)
        {
            try
            {
                if (!StateManager.IsHost)
                {
                    if (DebugLog.On) DebugLog.Event(Main.Id, "menu", "model", car.DefinitionInfo?.Identifier, "host", false, "entries", 0);
                    return;
                }
                Classes.Resolved m = Boarding.ModelOf(car);
                if (m == null || (m.Cls == Classes.None && !car.Definition.IsPassengerCar() && !Classes.Player.ContainsKey(car.DefinitionInfo.Identifier))) return;
                string id = car.DefinitionInfo.Identifier;
                int entries = 0;
                for (int cls = Classes.None; cls < Classes.Words.Length; cls++)
                {
                    if (cls == m.Cls) continue;
                    int pick = cls;
                    Add(__instance, ClassAction(cls), () => Ask(car, pick, -2));
                    entries++;
                }
                int obs = m.Obs ? 0 : 1;
                Add(__instance, m.Obs ? "Turn observation off" : "Turn observation on", () => Ask(car, -2, obs));
                entries++;
                bool chosen = Classes.Player.TryGetValue(id, out Classes.Decl d) && (d.Cls >= 0 || d.Obs >= 0);
                if (chosen)
                {
                    Add(__instance, "Back to its default", () => Ask(car, -1, -1));
                    entries++;
                }
                // The state is the game's own non-clickable first line: in the list menu the title is added disabled (ContextMenu.ShowList).
                centerText = StateTitle(centerText, m.Cls, m.Obs, chosen);
                if (DebugLog.On) DebugLog.Event(Main.Id, "menu", "model", id, "host", true, "class", Classes.Words[m.Cls], "obs", m.Obs, "entries", entries);
            }
            catch (Exception e) { Failed("adding the class choices to the car menu", e); }
        }

        // The action for a class the car is not: worded as what the click does.
        internal static string ClassAction(int cls)
        {
            switch (cls)
            {
                case Classes.None: return "Take its class off";
                case Classes.Parlor: return "Make it a parlor (first class)";
                case Classes.Baggage: return "Make it a baggage car";
                default: return "Make it a " + Classes.Words[cls];
            }
        }

        // What the car is now, for the menu's first line.
        internal static string StateText(int cls, bool obs, bool chosen)
        {
            string c = cls == Classes.None ? "No class" : cls == Classes.Parlor ? "Parlor (first class)" : char.ToUpperInvariant(Classes.Words[cls][0]) + Classes.Words[cls].Substring(1);
            return c + (obs ? ", observation" : "") + (chosen ? ", your choice" : "");
        }

        internal static string StateTitle(string name, int cls, bool obs, bool chosen) => (string.IsNullOrEmpty(name) ? "" : name + ": ") + StateText(cls, obs, chosen);

        internal static void Add(UI.ContextMenu.ContextMenu menu, string title, Action click)
        {
            if (MenuSeam != null) MenuSeam(title, click); else AddButton(menu, title, click);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddButton(UI.ContextMenu.ContextMenu menu, string title, Action click) => menu.AddButton(ContextMenuQuadrant.General, title, click);

        // Test seam (tools/gamecheck): the game's confirmation window needs its UI; when set it is called with the text and the confirm action.
#pragma warning disable CS0649
        internal static Action<string, Action> ConfirmSeam;
#pragma warning restore CS0649

        // A choice changes every car of the model, so it is asked first: the game's own yes and cancel window, with the model's name and the number of
        // its cars on the railroad. Nothing changes until the player confirms; cancel changes nothing. If the window can't be shown nothing changes either.
        // UI.Common.ModalAlertController.Present<T>(string title, string message, IEnumerable<(T, string)> buttons, Action<T> onButton) is public static; it throws when there is no controller; the game asks "Delete railroad?" the same way (RailroadLibrary.ConfirmDeleteRailroad) [load]
        internal static void Ask(Car car, int cls, int obs)
        {
            try
            {
                string id = car.DefinitionInfo.Identifier;
                string name = car.DefinitionInfo.Metadata?.Name;
                if (string.IsNullOrEmpty(name)) name = id;
                int count = CountOf(id);
                // the seats the model would have in the class (when they would change): the same sum the model's own look takes
                int seats = -1;
                Classes.Resolved m = Boarding.ModelOf(car);
                if (m != null && car.Definition.IsPassengerCar())
                {
                    int toCls = cls >= 0 ? cls : cls == -1 ? m.NativeCls : m.Cls;
                    int from = cls >= 0 ? 3 : cls == -1 ? 0 : m.ClsFrom;
                    int target = Classes.SeatsFor(toCls, from, m.NativeCls, m.NativeSeats, m.PackSeats);
                    if (target != m.Seats && (cls != -2)) seats = target;
                }
                string text = Classes.ConfirmText(name, cls, obs, count, seats);
                Action yes = () => Choose(id, cls, obs);
                if (DebugLog.On) DebugLog.Event(Main.Id, "class-ask", "model", id, "class", Token(cls), "obs", ObsToken(obs), "cars", count, "seats", seats);
                if (ConfirmSeam != null) ConfirmSeam(text, yes); else Present(text, yes, () => Cancelled(id, cls, obs));
            }
            catch (Exception e) { Failed("asking about a class choice", e); }
        }

        // The debug log's line for a choice that was confirmed: what was asked and what the model is now (class and where it came from, seats).
        private static void LogChanged(string id, int cls, int obs)
        {
            Classes.TryCached(id, out Classes.Resolved r);
            DebugLog.Event(Main.Id, "class-change", "model", id, "class", Token(cls), "obs", ObsToken(obs), "now", r == null ? "-" : Classes.Words[r.Cls],
                           "from", r == null ? "-" : r.ClsFrom == 3 ? "player" : r.ClsFrom == 2 ? "pack" : r.ClsFrom == 1 ? "table" : "default",
                           "obsNow", r != null && r.Obs, "seats", r == null ? -1 : r.Seats, "nativeSeats", r == null ? -1 : r.NativeSeats,
                           "choices", Classes.Player.Count);
        }

        private static int CountOf(string definitionId)
        {
            int n = 0;
            var shared = TrainController.Shared;
            if (shared == null) return n;
            foreach (Car c in shared.Cars) if (c.DefinitionInfo != null && c.DefinitionInfo.Identifier == definitionId) n++;
            return n;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Present(string text, Action yes, Action no)
        {
            ModalAlertController.Present("Change class?", text, new[] { (true, "Change"), (false, "Cancel") }, confirmed => { if (confirmed) yes(); else no(); });
        }

        // The window's Cancel: nothing changes. (The debug log says so; the click itself is only the game's to show.)
        internal static void Cancelled(string id, int cls, int obs)
        {
            if (DebugLog.On) DebugLog.Event(Main.Id, "class-cancel", "model", id, "class", Token(cls), "obs", ObsToken(obs));
        }

        // The words the debug log uses for a requested choice: a class word, "default" (-1), or "-" (-2, not changed).
        private static string Token(int cls) => cls == -1 ? "default" : cls < 0 ? "-" : Classes.Words[cls];
        private static string ObsToken(int obs) => obs == -1 ? "default" : obs < 0 ? "-" : obs == 1 ? "on" : "off";

        // The choice itself: cls is the new class (-1 default, -2 leave it), obs the new observation choice (-1 default, -2 leave it). Saves, then
        // runs the brake setup again for the cars of the model.
        internal static void Choose(string id, int cls, int obs)
        {
            try
            {
                if (cls >= -1) Classes.ChooseClass(id, cls);
                if (obs >= -1) Classes.ChooseObservation(id, obs);
                Main.SaveChoices();
                Boarding.ApplyModel(id);
                Brakes.Reapply(id);
                if (DebugLog.On) LogChanged(id, cls, obs);
            }
            catch (Exception e) { Failed("saving a class choice", e); }
        }
    }

    // Where a model's pack file is: the game says which pack a definition came from (AssetPackManager.GetPackBasePathForDefinition),
    // PackFile reads the file beside that pack's Definitions.json once. Only packs the game has loaded are asked.
    internal static class PackSource
    {
        // AssetPackManager.Shared is a public static; GetPackBasePathForDefinition(string) is a public instance method returning the folder of the pack that holds the definition, or null [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            if (typeof(AssetPackManager).GetProperty("Shared", BindingFlags.Public | BindingFlags.Static) == null) return "AssetPackManager.Shared";
            MethodInfo m = typeof(AssetPackManager).GetMethod("GetPackBasePathForDefinition", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (m == null || m.ReturnType != typeof(string)) return "AssetPackManager.GetPackBasePathForDefinition";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Func<string, Classes.Decl> Make() => Entry;

        // The pack files the debug log has already described (a file is read once; the line is written once a world).
        private static readonly System.Collections.Generic.HashSet<PackFile.Result> Described = new System.Collections.Generic.HashSet<PackFile.Result>();
        internal static void ResetLog() { Described.Clear(); }

        // The pack file's entry for a definition, or null (no file, no entry, a bad file, or the game can't say).
        private static Classes.Decl Entry(string definitionId)
        {
            try
            {
                string folder = AssetPackManager.Shared?.GetPackBasePathForDefinition(definitionId);
                PackFile.Result r = PackFile.ForFolder(folder, why => Main.WarnOnce("file-" + folder, "Passenger Classes: the " + PackFile.FileName + " beside the pack of " + definitionId + " was skipped: " + why));
                if (r == null) return null;
                if (DebugLog.On && Described.Add(r)) DebugLog.Event(Main.Id, "pack-file", "model", definitionId, "entries", r.Cars.Count, "format", double.IsNaN(r.Format) ? -1.0 : r.Format, "ignored", r.Ignored, "problem", r.Problem ?? "-");
                r.Cars.TryGetValue(definitionId, out Classes.Decl d);
                return d;
            }
            catch (Exception e)
            {
                Main.WarnOnce("file-lookup", "Passenger Classes: looking for a pack's " + PackFile.FileName + " failed, the built-in classes stand: " + PathClean.CleanText(e.Message));
                return null;
            }
        }
    }

    // First Class's saved settings, read once. The game lists every installed pack (AvailablePacks) with its folder, so the file is found
    // wherever the player put the folder. Nothing is written unless this mod has no settings.json of its own yet.
    internal static class Migration
    {
        // AssetPackManager.AvailablePacks is a public IReadOnlyDictionary<string, DiscoveredPack> of every installed pack by identifier; DiscoveredPack.Path is its folder [load]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Import(string settingsPath)
        {
            if (File.Exists(settingsPath)) { if (DebugLog.On) DebugLog.Event(Main.Id, "first-class-import", "carried", false, "why", "own-settings-exist", "keys", 0); return null; }
            AssetPackManager apm = AssetPackManager.Shared;
            if (apm == null || !apm.AvailablePacks.TryGetValue("ashenmare/first-class", out AssetPackManager.DiscoveredPack pack))
            {
                if (DebugLog.On) DebugLog.Event(Main.Id, "first-class-import", "carried", false, "why", "first-class-not-installed", "keys", 0);
                return null;
            }
            string source = Path.Combine(pack.Path, "settings.json");
            if (!File.Exists(source)) { if (DebugLog.On) DebugLog.Event(Main.Id, "first-class-import", "carried", false, "why", "no-settings-file", "keys", 0); return null; }
            string carried = Carry.FromFirstClass(File.ReadAllText(source));
            if (carried == null) { if (DebugLog.On) DebugLog.Event(Main.Id, "first-class-import", "carried", false, "why", "nothing-to-carry", "keys", 0); return null; }
            File.WriteAllText(settingsPath, carried);
            if (DebugLog.On) DebugLog.Event(Main.Id, "first-class-import", "carried", true, "why", "carried", "keys", Newtonsoft.Json.Linq.JObject.Parse(carried).Count);
            return "carried the settings First Class saved over to this mod";
        }
    }
}
