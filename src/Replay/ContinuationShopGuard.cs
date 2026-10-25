using System;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;

namespace LOMSave.Probe
{
    // ShopPanel.Awake adds this month's stock even when the panel is hidden.
    // Rebuilding a direct-save scene must not turn UI creation into gameplay.
    // Keep real time/mission refreshes and native loads outside this scope.
    internal static class ContinuationShopGuard
    {
        private static int _initializing, _loading;

        [HarmonyPrefix, HarmonyPatch(typeof(ShopDatabase), "Load")]
        internal static void BeginLoad(out bool __state)
        {
            __state = StoryContinuation.IsPending;
            if (__state) _loading++;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(ShopDatabase), "Load")]
        internal static Exception EndLoad(Exception __exception, bool __state)
        {
            if (__state) _loading = Math.Max(0, _loading - 1);
            return __exception;
        }

        // Some trainers restock in a ShopDatabase.Load postfix. Keep that
        // postfix's destructive NewShop out of direct checkpoint restoration;
        // the finalizer closes this scope after every postfix, even on failure.
        [HarmonyPrefix, HarmonyPatch(typeof(ShopDatabase), "NewShop")]
        internal static bool BeforeRestock()
        {
            if (_loading == 0) return true;
            LOMSaveProbePlugin.WriteEvent("continuation_shop_load_restock_skipped", new { source = "ShopDatabase.Load" });
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ShopPanel), "Awake")]
        internal static void Begin(out bool __state)
        {
            __state = StoryContinuation.IsPending;
            if (__state) _initializing++;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(ShopPanel), "Awake")]
        internal static Exception End(Exception __exception, bool __state)
        {
            if (__state) _initializing = Math.Max(0, _initializing - 1);
            return __exception;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ShopDatabase), "UpdateItemFromMission")]
        internal static bool BeforeRefresh()
        {
            if (_initializing == 0) return true;
            LOMSaveProbePlugin.WriteEvent("continuation_shop_ui_refresh_skipped", new { source = "ShopPanel.Awake" });
            return false;
        }
    }
}
