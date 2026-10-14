using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using System;
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
            var cost = ActionAutoSavePolicy.OptionCost(__0, __1, __2);
            if (cost <= 0 || __3 == null) return;
            var original = __3;
            var wrapped = ActionAutoSavePolicy.BeforeOption(cost, original.Invoke, CanSaveStoryOption,
                actionCost => SaveBeforeAction(-actionCost, "story_option"));
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
                var file = (string)SaveDataFileMethod.Invoke(system, new object[] { Slot });
                var previousHash = StorySaveFiles.Hash(file);
                AutoSaveDataMethod.Invoke(
                    system,
                    new object[] { Slot });
                if (StorySaveFiles.CommittedHash(file, previousHash) == null)
                {
                    LOMSaveProbePlugin.WriteEvent("action_start_auto_save_skipped",
                        new { slot = Slot, source, reason = "save_not_committed" });
                    return;
                }
                LOMSaveProbePlugin.WriteEvent(
                    "action_start_auto_save_written",
                    new
                    {
                        slot = Slot,
                        source,
                        displayName = DisplayName,
                        actionCost = -value,
                        actionCount
                    });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "action_start_auto_save_failed",
                    exception,
                    new
                    {
                        slot = Slot,
                        source,
                        actionCost = -value
                    });
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LoadGamePanel), "Awake")]
        private static void AddLoadSlot(LoadGamePanel __instance)
        {
            GameObject clone = null;
            try
            {
                var slotsField = Traverse.Create(__instance)
                    .Field("_autoSaveSlot");
                var slots = slotsField.GetValue<AutoSaveSlotPanel[]>();
                if (slots == null || slots.Length == 0)
                {
                    LOMSaveProbePlugin.WriteEvent(
                        "action_auto_save_slot_missing",
                        new { reason = "auto_save_template_missing" });
                    return;
                }

                foreach (var slot in slots)
                {
                    if (slot != null
                        && string.Equals(
                            slot.Slot,
                            Slot,
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
                        "action_auto_save_slot_missing",
                        new { reason = "auto_save_template_invalid" });
                    return;
                }

                clone = UnityEngine.Object.Instantiate(
                    templateRow.gameObject,
                    templateRow.parent,
                    false);
                clone.name = GameObjectName;
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
                    .SetValue(Slot);
                actionSlot.Setup();

                var expandedSlots =
                    new AutoSaveSlotPanel[slots.Length + 1];
                Array.Copy(slots, expandedSlots, slots.Length);
                expandedSlots[expandedSlots.Length - 1] = actionSlot;
                slotsField.SetValue(expandedSlots);
                FitAutoSaveSlots(expandedSlots, slots.Length);
                LOMSaveProbePlugin.WriteEvent(
                    "action_auto_save_slot_added",
                    new
                    {
                        slot = Slot,
                        displayName = DisplayName,
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
                    "action_auto_save_slot_add_failed",
                    exception);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AutoSaveSlotPanel), "Setup")]
        private static void ApplyDisplayName(AutoSaveSlotPanel __instance)
        {
            if (!string.Equals(
                    __instance.Slot,
                    Slot,
                    StringComparison.Ordinal))
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
                        new { slot = Slot });
                    return;
                }

                slotText.text = DisplayName;
                var category = __instance.transform.Find("Header/Text");
                if (category != null)
                {
                    foreach (var behaviour in category.GetComponents<MonoBehaviour>())
                        if (behaviour.GetType().FullName == "Lean.Localization.LeanLocalizedText")
                            behaviour.enabled = false;
                    var categoryText = category.GetComponent<Text>();
                    if (categoryText != null) categoryText.text = "行动前";
                }
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "action_auto_save_label_failed",
                    exception,
                    new { slot = Slot });
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
