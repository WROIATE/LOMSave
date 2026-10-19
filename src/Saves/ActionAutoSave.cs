using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class ActionAutoSave
    {
        private const string Slot = "auto_action";
        private const string DisplayName = "最近行动";
        private const string GameObjectName = "LOMSaveRecentActionAutoSave";
        internal const string BranchSlot = "auto_branch";
        internal const string BranchDisplayName = "最近分支";
        private static string _freeMenuScript, _pendingFreeMenuScript;

        [HarmonyPrefix, HarmonyPatch(typeof(Mortal.Story.LuaManager), "Init")]
        private static void BeginScript()
        { _freeMenuScript = _pendingFreeMenuScript = null; }

        [HarmonyPostfix, HarmonyPatch(typeof(Mortal.Story.LuaManager), "AutoFreeSave")]
        private static void RememberFreeMenu()
        { _freeMenuScript = _pendingFreeMenuScript = PlayerStatManagerData.Instance.CurrentStoryScript; }

        private static readonly MethodInfo AutoSaveDataMethod =
            AccessTools.Method(
                typeof(SaveSystem),
                "AutoSaveData",
                new[] { typeof(string) });

        private static readonly MethodInfo SaveDataFileMethod =
            AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile", new[] { typeof(string) });

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FreeManager), "UpdateActionValue", typeof(int))]
        private static void SaveBeforeAction(int value)
        {
            SaveBeforeAction(value, "free_action");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MenuDialog), "AddOption", typeof(string), typeof(bool), typeof(bool), typeof(UnityEngine.Events.UnityAction))]
        private static void BeforeAddActionOption(string __0, bool __1, bool __2, ref UnityEngine.Events.UnityAction __3)
        {
            var script = PlayerStatManagerData.Instance.CurrentStoryScript;
            if (_pendingFreeMenuScript == script && !string.IsNullOrEmpty(script) && CanSaveStoryOption())
            {
                _pendingFreeMenuScript = null;
                // Native AutoFreeSave precedes choose(), so its first write has
                // no command checkpoint. Complete it once the choice is known.
                SaveAutomatic("auto_free", "free_menu_checkpoint", 0);
            }
            var cost = ActionAutoSavePolicy.OptionCost(__0, __1, __2);
            var freeMenuOption = _freeMenuScript == script && !string.IsNullOrEmpty(script)
                && ActionAutoSavePolicy.ZeroCostEventOption(__0, __1, __2);
            if ((cost <= 0 && !freeMenuOption) || __3 == null) return;
            var original = __3;
            var wrapped = ActionAutoSavePolicy.BeforeOption(cost, original.Invoke, CanSaveStoryOption,
                actionCost => SaveAutomatic(Slot, freeMenuOption ? "story_free_option" : "story_option", actionCost), freeMenuOption);
            __3 = wrapped.Invoke;
        }

        private static bool CanSaveStoryOption()
        {
            var checkpoint = StoryCommandTrace.Current;
            return checkpoint != null
                && (checkpoint.Kind == "choose" || checkpoint.Kind == "choosetimer")
                && string.Equals(checkpoint.Script, PlayerStatManagerData.Instance.CurrentStoryScript, StringComparison.Ordinal)
                && !ReplayRuntime.IsRestoring && !StoryCommandTrace.IsReplayPending && !ReplayStateValidator.IsRestoring;
        }

        private static void SaveBeforeAction(int value, string source)
        {
            if (value >= 0 || ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending || ReplayStateValidator.IsRestoring)
            {
                return;
            }

            SaveAutomatic(Slot, source, -value);
        }

        internal static string GetSaveFile(string slot)
        { return (string)SaveDataFileMethod.Invoke(SaveSystem.Instance, new object[] { slot }); }

        internal static bool SaveAutomatic(string slot, string source, int actionCost)
        {
            if (ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending || ReplayStateValidator.IsRestoring) return false;
            var eventPrefix = slot == BranchSlot ? "branch_auto_save" : "action_start_auto_save";
            var started = Stopwatch.GetTimestamp();

            try
            {
                if (AutoSaveDataMethod == null || SaveDataFileMethod == null)
                {
                    throw new MissingMethodException(
                        typeof(SaveSystem).FullName,
                        "AutoSaveData");
                }

                var actionCount =
                    PlayerStatManagerData.Instance.CurrentActionCount;
                var system = SaveSystem.Instance;
                var file = (string)SaveDataFileMethod.Invoke(system, new object[] { slot });
                var previousHash = StorySaveFiles.Hash(file);
                AutoSaveDataMethod.Invoke(
                    system,
                    new object[] { slot });
                if (StorySaveFiles.CommittedHash(file, previousHash) == null)
                {
                    LOMSaveProbePlugin.WriteEvent(eventPrefix + "_skipped",
                        new { slot, source, reason = "save_not_committed" });
                    return false;
                }
                LOMSaveProbePlugin.WriteEvent(
                    eventPrefix + "_written",
                    new
                    {
                        slot,
                        source,
                        displayName = slot == Slot ? DisplayName : slot == BranchSlot ? BranchDisplayName : "养成自动存档",
                        actionCost,
                        actionCount,
                        elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency
                    });
                return true;
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    eventPrefix + "_failed",
                    exception,
                    new
                    {
                        slot,
                        source,
                        actionCost
                    });
                return false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LoadGamePanel), "Awake")]
        private static void AddSpecialLoadSlots(LoadGamePanel __instance)
        {
            AddLoadSlot(__instance, Slot, GameObjectName, DisplayName);
            AddLoadSlot(__instance, BranchSlot, "LOMSaveRecentBranchAutoSave", BranchDisplayName);
        }

        private static void AddLoadSlot(LoadGamePanel panel, string targetSlot, string objectName, string displayName)
        {
            GameObject clone = null;
            try
            {
                var slotsField = Traverse.Create(panel)
                    .Field("_autoSaveSlot");
                var slots = slotsField.GetValue<AutoSaveSlotPanel[]>();
                if (slots == null || slots.Length == 0)
                {
                    LOMSaveProbePlugin.WriteEvent(
                        "special_auto_save_slot_missing",
                        new { slot = targetSlot, reason = "auto_save_template_missing" });
                    return;
                }

                foreach (var slot in slots)
                {
                    if (slot != null
                        && string.Equals(
                            slot.Slot,
                            targetSlot,
                            StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                var template = slots[slots.Length - 1];
                var templateRow = FindSlotRow(template);
                if (templateRow == null || templateRow.parent == null)
                {
                    LOMSaveProbePlugin.WriteEvent(
                        "special_auto_save_slot_missing",
                        new { slot = targetSlot, reason = "auto_save_template_invalid" });
                    return;
                }

                clone = UnityEngine.Object.Instantiate(
                    templateRow.gameObject,
                    templateRow.parent,
                    false);
                clone.name = objectName;
                clone.transform.SetSiblingIndex(
                    templateRow.GetSiblingIndex() + 1);
                var actionSlot = clone.GetComponentInChildren<AutoSaveSlotPanel>(true);
                if (actionSlot == null)
                {
                    throw new MissingComponentException(
                        typeof(AutoSaveSlotPanel).FullName);
                }

                Traverse.Create(actionSlot)
                    .Field("_slot")
                    .SetValue(targetSlot);
                actionSlot.Setup();

                var expandedSlots =
                    new AutoSaveSlotPanel[slots.Length + 1];
                Array.Copy(slots, expandedSlots, slots.Length);
                expandedSlots[expandedSlots.Length - 1] = actionSlot;
                slotsField.SetValue(expandedSlots);
                FitAutoSaveSlots(expandedSlots, slots.Length);
                LOMSaveProbePlugin.WriteEvent(
                    "special_auto_save_slot_added",
                    new
                    {
                        slot = targetSlot,
                        displayName,
                        originalCount = slots.Length,
                        expandedCount = expandedSlots.Length,
                        positionY = clone.transform.localPosition.y
                    });
            }
            catch (Exception exception)
            {
                if (clone != null)
                {
                    UnityEngine.Object.Destroy(clone);
                }
                LOMSaveProbePlugin.WriteError(
                    "special_auto_save_slot_add_failed",
                    exception, new { slot = targetSlot });
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AutoSaveSlotPanel), "Setup")]
        private static void ApplyDisplayName(AutoSaveSlotPanel __instance)
        {
            var isBranch = string.Equals(__instance.Slot, BranchSlot, StringComparison.Ordinal);
            if (!isBranch && !string.Equals(__instance.Slot, Slot, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var slotText = Traverse.Create(__instance)
                    .Field("_slotText")
                    .GetValue<Text>();
                if (slotText == null)
                {
                    LOMSaveProbePlugin.WriteEvent(
                        "action_auto_save_label_missing",
                        new { slot = __instance.Slot });
                    return;
                }

                slotText.text = isBranch ? BranchDisplayName : DisplayName;
                var category = __instance.transform.Find("Header/Text");
                if (category != null)
                {
                    foreach (var behaviour in category.GetComponents<MonoBehaviour>())
                        if (behaviour.GetType().FullName == "Lean.Localization.LeanLocalizedText")
                            behaviour.enabled = false;
                    var categoryText = category.GetComponent<Text>();
                    if (categoryText != null) categoryText.text = isBranch ? "F8 返回" : "行动前";
                }
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "action_auto_save_label_failed",
                    exception,
                    new { slot = __instance.Slot });
            }
        }

        private static RectTransform FindSlotRow(AutoSaveSlotPanel slot)
        {
            if (slot == null) return null;
            var row = slot.transform;
            var delete = slot.DeleteButton;
            while (row.parent != null && delete != null
                && !delete.transform.IsChildOf(row))
                row = row.parent;
            return row as RectTransform;
        }

        private static void FitAutoSaveSlots(
            AutoSaveSlotPanel[] slots,
            int originalCount)
        {
            var parent = FindSlotRow(slots[0]).parent as RectTransform;
            var layout = parent.GetComponent<VerticalLayoutGroup>();
            // The shipped prefab has a VerticalLayoutGroup. Moving the clone's
            // anchoredPosition in Awake is overwritten by its first layout pass.
            var originalSpacing = layout == null ? 20f : layout.spacing;
            var originalHeight = 0f;
            var expandedHeight = 0f;
            foreach (var slot in slots)
            {
                var rect = FindSlotRow(slot);
                expandedHeight += rect.rect.height * rect.localScale.y;
            }
            for (var index = 0; index < originalCount; index++)
            {
                var rect = FindSlotRow(slots[index]);
                originalHeight += rect.rect.height * rect.localScale.y;
            }
            originalHeight += originalSpacing * (originalCount - 1);
            var spacing = Mathf.Min(4f, Mathf.Max(0f, originalSpacing));
            var scale = Mathf.Min(1f,
                (originalHeight - spacing * (slots.Length - 1))
                / Mathf.Max(expandedHeight, 1f));
            foreach (var slot in slots)
            {
                FindSlotRow(slot).localScale *= scale;
            }
            if (layout != null)
            {
                layout.spacing = spacing;
                layout.childControlHeight = false;
                layout.childForceExpandHeight = false;
                layout.childScaleHeight = true;
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            }
            else
            {
                var y = FindSlotRow(slots[0]).localPosition.y;
                foreach (var slot in slots)
                {
                    var row = FindSlotRow(slot);
                    var position = row.localPosition;
                    position.y = y;
                    row.localPosition = position;
                    y -= row.rect.height * row.localScale.y + spacing;
                }
            }
            LOMSaveProbePlugin.WriteEvent("action_auto_save_layout_fitted",
                new { originalHeight, scale, spacing, count = slots.Length });
        }
    }
}
