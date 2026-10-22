using System;
using System.Collections.Generic;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class SaveButtonAvailability
    {
        private sealed class Binding
        {
            internal Component Owner;
            internal Button[] Buttons;
            internal MenuToggleButton Toggle;
            internal Text[] Hints;
            internal bool Visible
            {
                get
                {
                    if (Toggle != null && Toggle.gameObject.activeInHierarchy) return true;
                    if (Owner is SaveGamePanel && Owner.gameObject.activeInHierarchy) return true;
                    foreach (var button in Buttons) if (button != null && button.gameObject.activeInHierarchy) return true;
                    return false;
                }
            }
            internal void Apply(ManualSaveBlock block)
            {
                var allowed = block == ManualSaveBlock.None;
                foreach (var button in Buttons) if (button != null && button.interactable != allowed) button.interactable = allowed;
                if (Toggle != null && Toggle.Active != allowed) { Toggle.SetActive(allowed); Toggle.Init(); }
                var hint = ManualSavePolicy.Hint(block);
                foreach (var text in Hints) if (text != null && text.text != hint) text.text = hint;
            }
        }
        private static readonly List<Binding> Bindings = new List<Binding>();
        private static float _next;
        private static bool _refreshing;

        [HarmonyPostfix, HarmonyPatch(typeof(MenuPanel), "Awake")]
        private static void BindMenu(MenuPanel __instance)
        {
            var save = EventInspector.Read<Button>(__instance, "_saveButton");
            var confirm = EventInspector.Read<Button>(__instance, "_confirmSaveButton");
            var toggle = EventInspector.Read<MenuToggleButton>(__instance, "_saveMenuToggleButton");
            Bindings.Add(new Binding { Owner = __instance, Buttons = new[] { save, confirm }, Toggle = toggle,
                // The menu toggle decorates the same save entry, rather than
                // providing a second icon. Give that entry a single reason.
                Hints = new[] { Hint(save, false), Hint(confirm, false) } });
        }

        [HarmonyPostfix, HarmonyPatch(typeof(FreeManager), "Awake")]
        private static void BindFree(FreeManager __instance)
        {
            var toggle = EventInspector.Read<MenuToggleButton>(__instance, "_saveButton");
            Bindings.Add(new Binding { Owner = __instance, Buttons = new Button[0], Toggle = toggle,
                Hints = new[] { Hint(toggle, true) } });
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(SaveGamePanel), "Awake")]
        private static void BindSlots(SaveGamePanel __instance)
        {
            var slots = EventInspector.Read<SaveSlotPanel[]>(__instance, "_saveSlots");
            var buttons = new List<Button>();
            if (slots != null) foreach (var slot in slots) if (slot != null) buttons.Add(slot.SlotButton);
            var hint = NewHint(__instance.transform, buttons.Count == 0 ? null : buttons[0]);
            var rect = hint.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1); rect.anchoredPosition = new Vector2(0, -100);
            rect.sizeDelta = new Vector2(1000, 36); hint.alignment = TextAnchor.MiddleCenter;
            Bindings.Add(new Binding { Owner = __instance, Buttons = buttons.ToArray(), Hints = new[] { hint } });
        }

        private static Text Hint(Component button, bool icon)
        {
            if (button == null) return null;
            var hint = NewHint(button.transform, button);
            var rect = hint.rectTransform;
            if (icon)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
                rect.pivot = new Vector2(1, 1); rect.anchoredPosition = new Vector2(60, -12);
                rect.sizeDelta = new Vector2(360, 48); hint.alignment = TextAnchor.UpperRight;
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
                rect.pivot = new Vector2(0, 0.5f); rect.anchoredPosition = new Vector2(20, 0);
                rect.sizeDelta = new Vector2(560, 42); hint.alignment = TextAnchor.MiddleLeft;
            }
            return hint;
        }

        private static Text NewHint(Transform parent, Component source)
        {
            var obj = new GameObject("LOMSaveSaveAvailability", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var hint = obj.GetComponent<Text>();
            var original = source == null ? null : source.GetComponentInChildren<Text>(true);
            hint.font = original != null && original.font != null ? original.font : PreviewFont.Resolve();
            hint.fontSize = 18; hint.fontStyle = FontStyle.Normal;
            hint.color = new Color(0.83f, 0.75f, 0.59f);
            hint.raycastTarget = false; hint.supportRichText = false;
            hint.horizontalOverflow = HorizontalWrapMode.Wrap; hint.verticalOverflow = VerticalWrapMode.Overflow;
            return hint;
        }

        // Refresh only cached controls. No resource/scene enumeration, native
        // save creation, snapshot capture, or file I/O in this path.
        internal static void Refresh(bool force = false)
        {
            if (_refreshing || !force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.08f;
            _refreshing = true;
            try
            {
                for (var i = Bindings.Count - 1; i >= 0; i--) if (Bindings[i].Owner == null) Bindings.RemoveAt(i);
                var visible = false;
                foreach (var binding in Bindings) if (binding.Visible) { visible = true; break; }
                if (!visible) return;
                var block = ManualSaveAvailability.Check();
                foreach (var binding in Bindings) if (binding.Visible) binding.Apply(block);
            }
            finally { _refreshing = false; }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MenuPanel), "OnPanelOpen")]
        [HarmonyPatch(typeof(SaveGamePanel), "OnPanelOpen")]
        [HarmonyPatch(typeof(MenuPanel), "OnSavePanelOpen")]
        private static void Opened() { Refresh(true); }

        [HarmonyPrefix, HarmonyPatch(typeof(MenuPanel), "SaveButtonClick")]
        [HarmonyPatch(typeof(SaveSlotPanel), "OnTitleClick")]
        [HarmonyPatch(typeof(FreeManager), "SaveButtonClick", typeof(PointerEventData))]
        [HarmonyPatch(typeof(FreeManager), "SaveActionPerformed")]
        private static bool BeforeOpen()
        { var allowed = ManualSaveAvailability.Check() == ManualSaveBlock.None; if (!allowed) Refresh(true); return allowed; }
    }
}
