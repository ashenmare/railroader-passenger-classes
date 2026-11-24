using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;

namespace PassengerClasses
{
    // The stock baggage car rr/cbs-baggage01 ships with a listed price of 0, and the game offers a car for sale only above 0
    // (EquipmentPurchase.IsOfferedForSale), so it can't be bought. It gets BaggagePrice (Logic) while the listed price is 0; a listed
    // price the game or another mod gives it later is left alone.
    // The price of a first-class or sleeper car: the stock car's listed price times that class's setting (Logic.Price). A coach has no computed price
    // (EquipmentPricing.PriceFor is 0 for it) so CarDefinition.BasePrice uses the authored basePrice, which Data.rl bakes in at the default percent;
    // this postfix gives PriceFor's answer for a clone definition, so the setting is live and everything that reads BasePrice
    // follows (the buy window, the charge, trade-in, Used Equipment). With the dll off, the baked price stands.
    // The map from a clone definition to its stock definition is built on first use and kept while the library's
    // AllCarDefinitionInfos list is the same object (the game drops that list whenever its definitions are rebuilt).
    internal static class Pricing
    {
        private sealed class Entry { internal CarDefinition Stock; internal bool Sleeper; internal int Fixed; }
        private static Dictionary<CarDefinition, Entry> _stockOf;
        private static object _builtFrom;
        internal static volatile bool Live;
        private static int _failures;

        internal static void ResetWorld() { _failures = 0; _stockOf = null; _builtFrom = null; }

        // EquipmentPricing.PriceFor(CarDefinition) is public static, int; it is 0 for a car that is not a locomotive or tender, and CarDefinition.BasePrice then uses AuthoredBasePrice [load]
        // AssetPackManager.AllCarDefinitionInfos returns a cached list that DefinitionsDidChange sets to null whenever the definitions are rebuilt, so a new list object means new definition objects
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Resolve()
        {
            var pf = AccessTools.Method(typeof(EquipmentPricing), "PriceFor", new[] { typeof(CarDefinition) });
            if (pf == null || pf.ReturnType != typeof(int)) return "EquipmentPricing.PriceFor";
            if (typeof(IContentLibrary).GetProperty("AllCarDefinitionInfos") == null) return "IContentLibrary.AllCarDefinitionInfos";
            if (typeof(CarDefinition).GetProperty("AuthoredBasePrice") == null) return "CarDefinition.AuthoredBasePrice";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(EquipmentPricing), "PriceFor", new[] { typeof(CarDefinition) }), postfix: new HarmonyMethod(typeof(Pricing), nameof(PriceForPostfix)));
        }

        internal static void PriceForPostfix(CarDefinition definition, ref int __result)
        {
            if (!Live || definition == null || definition is LocomotiveDefinition) return;
            try
            {
                IContentLibrary lib = TrainController.Shared?.ContentLibrary;
                if (lib == null) return;
                object list = lib.AllCarDefinitionInfos;
                Dictionary<CarDefinition, Entry> map = ReferenceEquals(list, _builtFrom) ? _stockOf : null;
                if (map == null) map = Build(lib, list);
                if (map != null && map.TryGetValue(definition, out Entry e))
                    __result = e.Fixed > 0 ? (definition.AuthoredBasePrice > 0 ? __result : e.Fixed) : Logic.Price(e.Stock.AuthoredBasePrice, e.Sleeper ? Logic.SleeperPricePercent : Logic.PricePercent);
            }
            catch (Exception ex)
            {
                _failures++;
                if (DebugLog.On) DebugLog.Event(Main.Id, "failed", "where", "pricing", "what", "pricing a car", "type", ex.GetType().Name, "n", _failures);
                Main.WarnOnce("price", "Passenger Classes: pricing a car failed (the listed price stands): " + PathClean.CleanText(ex.Message));
                if (_failures >= 3) { Live = false; Main.WarnOnce("price-down", "Passenger Classes: pricing stands down until the next world load"); }
            }
        }

        private static Dictionary<CarDefinition, Entry> Build(IContentLibrary lib, object list)
        {
            var map = new Dictionary<CarDefinition, Entry>();
            foreach (string[] c in Logic.Cars)
            {
                if (!lib.TryGetDefinitionInfo(c[0], out TypedContainerItem<CarDefinition> stock)) continue;
                if (lib.TryGetDefinitionInfo(c[1], out TypedContainerItem<CarDefinition> clone) && !ReferenceEquals(clone.Definition, stock.Definition)) map[clone.Definition] = new Entry { Stock = stock.Definition, Sleeper = Logic.IsSleeperId(c[1]) };
            }
            if (lib.TryGetDefinitionInfo(Logic.BaggageCarId, out TypedContainerItem<CarDefinition> bag) && bag.Definition.AuthoredBasePrice <= 0) map[bag.Definition] = new Entry { Fixed = Logic.BaggagePrice };
            _stockOf = map;
            _builtFrom = list;
            return map;
        }
    }
}
