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
        private const string DisplayName = "最近行动存档";
        private const string GameObjectName = "LOMSaveRecentActionAutoSave";

        private static readonly MethodInfo AutoSaveDataMethod =
            AccessTools.Method(
                typeof(SaveSystem),
                "AutoSaveData",
                new[] { typeof(string) });

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FreeManager), "UpdateActionValue", typeof(int))]
        private static void SaveBeforeAction(int value)
        {
            if (value >= 0)
            {
                return;
            }

            try
            {
                if (AutoSaveDataMethod == null)
                {
                    throw new MissingMethodException(
                        typeof(SaveSystem).FullName,
                        "AutoSaveData");
                }

                var actionCount =
                    PlayerStatManagerData.Instance.CurrentActionCount;
                AutoSaveDataMethod.Invoke(
                    SaveSystem.Instance,
                    new object[] { Slot });
                LOMSaveProbePlugin.WriteEvent(
                    "action_start_auto_save_written",
                    new
                    {
                        slot = Slot,
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
                if (template == null || template.transform.parent == null)
                {
                    LOMSaveProbePlugin.WriteEvent(
                        "action_auto_save_slot_missing",
                        new { reason = "auto_save_template_invalid" });
                    return;
                }

                clone = UnityEngine.Object.Instantiate(
                    template.gameObject,
                    template.transform.parent,
                    false);
                clone.name = GameObjectName;
                clone.transform.SetSiblingIndex(
                    template.transform.GetSiblingIndex() + 1);
                var actionSlot = clone.GetComponent<AutoSaveSlotPanel>();
                if (actionSlot == null)
                {
                    throw new MissingComponentException(
                        typeof(AutoSaveSlotPanel).FullName);
                }

                Traverse.Create(actionSlot)
                    .Field("_slot")
                    .SetValue(Slot);
                PositionAfterLastSlot(actionSlot, slots);
                actionSlot.Setup();

                var expandedSlots =
                    new AutoSaveSlotPanel[slots.Length + 1];
                Array.Copy(slots, expandedSlots, slots.Length);
                expandedSlots[expandedSlots.Length - 1] = actionSlot;
                slotsField.SetValue(expandedSlots);
                LOMSaveProbePlugin.WriteEvent(
                    "action_auto_save_slot_added",
                    new
                    {
                        slot = Slot,
                        displayName = DisplayName,
                        originalCount = slots.Length,
                        expandedCount = expandedSlots.Length,
                        position = actionSlot.transform.localPosition
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
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "action_auto_save_label_failed",
                    exception,
                    new { slot = Slot });
            }
        }

        private static void PositionAfterLastSlot(
            AutoSaveSlotPanel actionSlot,
            AutoSaveSlotPanel[] existingSlots)
        {
            if (existingSlots.Length < 2)
            {
                return;
            }

            var actionRect =
                actionSlot.transform as RectTransform;
            var lastRect =
                existingSlots[existingSlots.Length - 1].transform
                    as RectTransform;
            var previousRect =
                existingSlots[existingSlots.Length - 2].transform
                    as RectTransform;
            if (actionRect == null
                || lastRect == null
                || previousRect == null)
            {
                return;
            }

            var step =
                lastRect.anchoredPosition
                - previousRect.anchoredPosition;
            if (step.sqrMagnitude < 0.01f)
            {
                step = Vector2.down
                    * Mathf.Max(lastRect.rect.height, 1f);
            }
            actionRect.anchoredPosition =
                lastRect.anchoredPosition + step;
        }
    }
}
