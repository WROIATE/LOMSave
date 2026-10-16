using System;
using System.Collections;
using HarmonyLib;
using Mortal.Pinball;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class PinballSkip
    {
        [HarmonyPostfix, HarmonyPatch(typeof(PinballManager), "Awake")]
        private static void Attach(PinballManager __instance)
        { if (__instance.GetComponent<PinballSkipControl>() == null) __instance.gameObject.AddComponent<PinballSkipControl>(); }

        [HarmonyPostfix, HarmonyPatch(typeof(PinballManager), "Start")]
        private static void WaitForReady(PinballManager __instance, ref IEnumerator __result)
        { __result = ObserveReady(__instance, __result); }

        private static IEnumerator ObserveReady(PinballManager manager, IEnumerator original)
        {
            while (original != null && original.MoveNext()) yield return original.Current;
            if (manager != null)
            {
                var control = manager.GetComponent<PinballSkipControl>();
                if (control != null) control.Ready = true;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PinballManager), "Shake")]
        [HarmonyPatch(typeof(PinballManager), "LauncherMarble")]
        [HarmonyPatch(typeof(PinballManager), "RestartGame")]
        [HarmonyPatch(typeof(SpringLauncher2D), "OnLaunchStarted")]
        [HarmonyPatch(typeof(SpringLauncher2D), "OnLaunchCanceled")]
        private static bool GuardInput() { return !FeaturePanel.IsOpen; }

        [HarmonyPrefix, HarmonyPatch(typeof(PinballManager), "ExitScene")]
        private static bool GuardExit(PinballManager __instance)
        {
            if (FeaturePanel.IsOpen) return false;
            var control = __instance.GetComponent<PinballSkipControl>();
            return control == null || control.BeginExit();
        }
    }

    internal sealed class PinballSkipControl : MonoBehaviour
    {
        private GameObject _root;
        private PinballManager _manager;
        private bool _settled, _pickerOpen, _exitStarted;
        internal bool Ready;

        internal bool BeginExit()
        {
            if (_exitStarted) return false;
            _exitStarted = _settled = true;
            if (_root != null) _root.SetActive(false);
            return true;
        }

        private void Start()
        {
            _manager = GetComponent<PinballManager>();
            _root = FeaturePanel.Canvas("LOMSavePinballShortcut");
            var button = FeaturePanel.Button(_root.transform, "快速结束 · 选分数", 0, 24, 280, 52, ShowPicker);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-304, -110);
            _root.SetActive(Ready);
        }
        private void Update()
        {
            if (_root != null) _root.SetActive(Ready && !_settled && _manager != null && !EventInspector.Read<bool>(_manager, "_isBroken"));
        }
        private void OnDestroy()
        {
            if (_pickerOpen) FeaturePanel.Close();
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
        private void ShowPicker()
        {
            if (!Ready || _settled || FeaturePanel.BlocksInput || ReplayRuntime.IsRestoring || _manager == null
                || EventInspector.Read<bool>(_manager, "_isBroken")) return;
            var panel = FeaturePanel.Open("四师兄弹珠 · 快速结束", () => _pickerOpen = false);
            _pickerOpen = true;
            FeaturePanel.Label(panel, "选择本局总分，返回剧情后由游戏发放对应奖励。\n奖励内容仍取决于当前剧情与已领取次数。", 25, 50, 110, 1120, 100);
            for (var index = 0; index < PinballRewards.Scores.Length; index++)
            {
                var score = PinballRewards.Scores[index];
                FeaturePanel.Button(panel, PinballRewards.Labels[index], 260, 240 + index * 114, 720, 74, () => Finish(score));
            }
        }
        private void Finish(int score)
        {
            if (_settled || !PinballRewards.IsSupported(score) || _manager == null
                || EventInspector.Read<bool>(_manager, "_isBroken")) return;
            _settled = true;
            FeaturePanel.Close();
            try
            {
                _manager.StopAllCoroutines();
                var spring = EventInspector.Read<SpringLauncher2D>(_manager, "_spring");
                if (spring != null) spring.enabled = false;
                Traverse.Create(_manager).Method("DisableMarblePhysicsBehaviour", new[] { typeof(Marble) }).GetValue((object)null);
                Traverse.Create(_manager).Method("SetScoreTriggerActive", new[] { typeof(bool) }).GetValue(false);
                Traverse.Create(_manager).Field("_currentScore").SetValue(0);
                Traverse.Create(_manager).Field("_totalScore").SetValue(score);
                // ExitScene owns score flags, broken-machine state and the scene transition.
                Traverse.Create(_manager).Method("ExitScene").GetValue();
                LOMSaveProbePlugin.WriteEvent("pinball_skipped", new { score });
            }
            catch (Exception e)
            {
                // Keep the settlement latch: re-entry could award the result twice.
                LOMSaveProbePlugin.WriteError("pinball_skip_failed", e);
            }
        }
    }
}
