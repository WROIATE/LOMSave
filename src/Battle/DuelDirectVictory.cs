using System;
using System.Collections;
using HarmonyLib;
using Mortal.Combat;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class DuelDirectVictory
    {
        private const string ButtonName = "LOMSaveDuelWinButton";

        [HarmonyPrefix, HarmonyPatch(typeof(CombatManager), "InitCombatRound")]
        internal static void CombatStarted(CombatManager __instance) { State(__instance).Ending = false; }

        // Build on opening the native menu, so it appears with the other items
        // and no scene scan or per-frame menu polling is needed.
        [HarmonyPostfix, HarmonyPatch(typeof(MenuPanel), "OnPanelOpen")]
        internal static void MenuOpened(MenuPanel __instance)
        {
            var manager = CombatManager.Instance;
            var ui = CombatUIManager.Instance;
            var panel = EventInspector.Read<CommonPanel>(ui, "_menuPanel");
            if (manager == null || panel == null || panel.GetComponent<MenuPanel>() != __instance || DebugMenuShortcut.HasTrainer()) return;
            try
            {
                var source = EventInspector.Read<Button>(__instance, "_giveupButton");
                if (source == null || source.transform.parent == null) return;
                var parent = source.transform.parent;
                var existing = parent.Find(ButtonName);
                var button = existing == null ? UnityEngine.Object.Instantiate(source, parent, false) : existing.GetComponent<Button>();
                button.name = ButtonName;
                button.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
                var title = EventInspector.Read<Button>(__instance, "_titleButton");
                var step = title == null ? -70f : source.transform.localPosition.y - parent.InverseTransformPoint(title.transform.position).y;
                if (Mathf.Abs(step) < 1f) step = -70f;
                var position = source.transform.localPosition; position.y += step;
                button.transform.localPosition = position;
                foreach (var text in button.GetComponentsInChildren<MonoBehaviour>(true))
                    if ((text.GetType().FullName ?? "").StartsWith("Lean.Localization.LeanLocalizedText", StringComparison.Ordinal)) text.enabled = false;
                var textButton = button.GetComponentInChildren<TextButton>(true);
                if (textButton != null) textButton.Setup("直接胜利");
                else foreach (var label in button.GetComponentsInChildren<Text>(true)) label.text = "直接胜利";
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => Win(manager, panel, button));
                var state = State(manager);
                button.interactable = manager.CombatLevel != null && !state.Ending;
                button.gameObject.SetActive(true);
                LOMSaveProbePlugin.WriteEvent("duel_win_button_ready", new { interactable = button.interactable });
            }
            catch (Exception error) { LOMSaveProbePlugin.WriteError("duel_win_button_failed", error); }
        }

        internal static void Win(CombatManager manager, CommonPanel panel, Button button)
        {
            if (manager == null || manager != CombatManager.Instance || manager.CombatLevel == null
                || button == null || !button.interactable || State(manager).Ending) return;
            button.interactable = false;
            manager.EnableHotKey = false;
            // End the suspended combat loop before starting the native victory
            // routine. Rewards, flags, conditions and scene continuation remain
            // owned by GameOver(true); we do not grant any of them ourselves.
            manager.StopAllCoroutines();
            if (panel != null) panel.Show(false);
            manager.StartCoroutine(manager.GameOver(true));
            LOMSaveProbePlugin.WriteEvent("duel_direct_victory_requested", new { nativeSettlement = true });
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CombatManager), "GameOver")]
        internal static bool BeginEnd(CombatManager __instance, ref IEnumerator __result)
        {
            var state = State(__instance);
            if (!state.Ending) { state.Ending = true; return true; }
            __result = Empty();
            return false;
        }

        private static DuelVictoryState State(CombatManager manager)
        { return manager.GetComponent<DuelVictoryState>() ?? manager.gameObject.AddComponent<DuelVictoryState>(); }
        private static IEnumerator Empty() { yield break; }
    }

    internal sealed class DuelVictoryState : MonoBehaviour { internal bool Ending; }
}
