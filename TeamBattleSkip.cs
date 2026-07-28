using HarmonyLib;
using Mortal.Battle;
using System;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class TeamBattleSkip
    {
        private const string ButtonLabel = "直接胜利";

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PausePanel), "Setup", typeof(bool), typeof(bool))]
        private static void ConfigureButton(PausePanel __instance)
        {
            Button winButton = null;
            try
            {
                winButton = Traverse.Create(__instance)
                    .Field("_testWinButton")
                    .GetValue<Button>();
                var manager = GameLevelManager.Instance;
                var level = manager == null
                    ? null
                    : Traverse.Create(manager)
                        .Field("_levelData")
                        .GetValue<BattleLevel>();
                var events = level == null
                    ? null
                    : Traverse.Create(level)
                        .Field("_events")
                        .GetValue<BattleEventData[]>();
                var eventCount = events == null ? 0 : events.Length;
                var visible = winButton != null
                    && level != null
                    && eventCount == 0;

                if (winButton != null)
                {
                    winButton.gameObject.SetActive(visible);
                    if (visible)
                    {
                        var label = winButton.GetComponentInChildren<Text>(true);
                        if (label != null)
                        {
                            label.text = ButtonLabel;
                        }
                    }
                }

                LOMSaveProbePlugin.WriteEvent(
                    "team_battle_skip_button_configured",
                    new
                    {
                        visible,
                        eventCount,
                        level = level == null ? null : level.name,
                        reason = level == null
                            ? "battle_level_missing"
                            : eventCount > 0
                                ? "battle_has_events"
                                : winButton == null
                                    ? "win_button_missing"
                                    : null
                    });
            }
            catch (Exception exception)
            {
                if (winButton != null)
                {
                    winButton.gameObject.SetActive(false);
                }
                LOMSaveProbePlugin.WriteError(
                    "team_battle_skip_button_configure_failed",
                    exception);
            }
        }
    }
}
