using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class ActionCalendar
    {
        private static CalendarNotes _notes = new CalendarNotes();
        private static List<CalendarAction> _actions = new List<CalendarAction>();
        private static string _label;
        private static int _loading;
        private static int _resettingActions;
        internal static bool IsOpen;
        private static string NotesFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "LOMSaveProbe.CalendarNotes.json"); } }
        internal static GameTime CurrentTime { get { return PlayerStatManagerData.Instance == null ? null : PlayerStatManagerData.Instance.GameTime; } }
        internal static string Today
        {
            get { var time = CurrentTime; return ReferenceEquals(time, null) ? null : CalendarNotes.DateKey(time.Year, time.Month, (int)time.Stage); }
        }
        internal static string Note(string date) { return date == null ? "" : _notes.Get(date); }
        internal static IEnumerable<CalendarAction> Actions(string date) { return _actions.Where(x => x != null && x.Date == date); }

        internal static void Initialize()
        {
            try { _notes = LocalJsonFile.Read<CalendarNotes>(NotesFile); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("calendar_notes_load_failed", e); }
        }
        internal static void SaveNote(string date, string value)
        {
            var previous = _notes.Get(date);
            _notes.Set(date, value);
            try { LocalJsonFile.Write(NotesFile, _notes); }
            catch { _notes.Set(date, previous); throw; }
        }
        internal static void SaveHistory(string file, GameSave save, string hash)
        {
            if (!CanRecord) return;
            try { LocalJsonFile.Write(file + ".lomsavecalendar.json", new CalendarHistory
                { SaveTimeTick = save.TimeTick, SaveFileHash = hash, Actions = _actions }); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("calendar_history_save_failed", e); }
        }
        private static bool CanRecord
        {
            get { return _loading == 0 && _resettingActions == 0 && !DebugMenuData.IsEditing
                && !ReplayRuntime.IsRestoring && !StoryCommandTrace.IsReplayPending && !ReplayStateValidator.IsRestoring; }
        }

        // Observe the actual action-stat deduction: covers map coroutines and
        // overseas Lua menus, while ignoring abandoned/disabled choices.
        [HarmonyPrefix, HarmonyPatch(typeof(GameStat), "AddValue")]
        [HarmonyPatch(typeof(GameStat), "SetValue")]
        private static void BeforeAction(GameStat __instance, out CalendarAction __state)
        {
            __state = null;
            if (__instance.StatType != GameStatType.行動次數 || !CanRecord) return;
            var player = PlayerStatManagerData.Instance;
            if (player == null || player.Stats == null || __instance != player.Stats.Get(GameStatType.行動次數)) return;
            var date = Today;
            if (date == null) return;
            __state = new CalendarAction { Date = date, Before = __instance.Value,
                Label = _label, Script = player.CurrentStoryScript };
        }
        [HarmonyPostfix, HarmonyPatch(typeof(GameStat), "AddValue")]
        [HarmonyPatch(typeof(GameStat), "SetValue")]
        private static void AfterAction(GameStat __instance, CalendarAction __state)
        {
            if (__state == null || !CanRecord || __instance.Value >= __state.Before) return;
            __state.After = __instance.Value;
            if (string.IsNullOrEmpty(__state.Label)) __state.Label = "剧情行动";
            _actions.Add(__state);
            _label = null;
            LOMSaveProbePlugin.WriteEvent("calendar_action_recorded", __state);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerStatManagerData), "ResetActionCount")]
        [HarmonyPatch(typeof(PlayerStatManagerData), "ResetData")]
        private static void BeginReset() { _resettingActions++; }
        [HarmonyFinalizer, HarmonyPatch(typeof(PlayerStatManagerData), "ResetActionCount")]
        [HarmonyPatch(typeof(PlayerStatManagerData), "ResetData")]
        private static void EndReset() { _resettingActions = Math.Max(0, _resettingActions - 1); }

        [HarmonyPrefix, HarmonyPatch(typeof(PositionController), "OnPositionClick")]
        private static void BuildingAction(PositionController __instance)
        {
            if (!CanRecord || FeaturePanel.BlocksInput || EventInspectorPanel.BlocksInput) return;
            _label = EventInspector.Clean(EventInspector.Read<PositionType>(__instance, "_position").ToString());
        }
        [HarmonyPrefix, HarmonyPatch(typeof(MenuDialog), "AddOption", typeof(string), typeof(bool), typeof(bool), typeof(UnityEngine.Events.UnityAction))]
        private static void ActionOption(string __0, bool __1, bool __2, ref UnityEngine.Events.UnityAction __3)
        {
            if (ActionAutoSavePolicy.OptionCost(__0, __1, __2) <= 0 || __3 == null) return;
            var label = EventInspector.Clean(__0.Split('+')[1]);
            var original = __3;
            __3 = () => { if (CanRecord) _label = label; original(); };
        }

        [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData", typeof(GameSave))]
        private static void BeginLoad()
        {
            _loading++;
            FeaturePanel.ForceClose();
            _label = null;
        }
        [HarmonyFinalizer, HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData", typeof(GameSave))]
        private static Exception EndLoad(SaveSystem __instance, GameSave __0, Exception __exception)
        {
            _loading = Math.Max(0, _loading - 1);
            if (__exception != null || ReplayStateValidator.IsRestoring || __0 == null) return __exception;
            try
            {
                var slot = SaveSlotContext.Resolve(__instance.CurrentSlot);
                var file = Traverse.Create(__instance).Method("GetSaveDataFile", new[] { typeof(string) }).GetValue<string>(slot);
                var history = LocalJsonFile.Read<CalendarHistory>(file + ".lomsavecalendar.json");
                _actions = history.Matches(__0.TimeTick, StorySaveFiles.Hash(file)) && history.Actions != null
                    ? history.Actions.Where(x => x != null).ToList() : new List<CalendarAction>();
            }
            catch (Exception e) { _actions.Clear(); LOMSaveProbePlugin.WriteError("calendar_history_load_failed", e); }
            return __exception;
        }
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerStatManagerData), "ResetData")]
        private static void NewGame()
        { if (_loading == 0 && !ReplayStateValidator.IsRestoring) { _actions.Clear(); _label = null; FeaturePanel.ForceClose(); } }

        [HarmonyPostfix, HarmonyPatch(typeof(SaveSystem), "DeleteSaveData", typeof(string))]
        private static void DeleteHistory(SaveSystem __instance, string __0)
        {
            try
            {
                var file = Traverse.Create(__instance).Method("GetSaveDataFile", new[] { typeof(string) }).GetValue<string>(__0) + ".lomsavecalendar.json";
                if (File.Exists(file)) File.Delete(file);
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("calendar_history_delete_failed", e); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameDateTimeText), "Start")]
        [HarmonyPatch(typeof(GameDateTimeText), "UpdateText")]
        private static void BindDate(GameDateTimeText __instance)
        {
            if (__instance != EventInspector.Read<GameDateTimeText>(StoryManager.Instance, "_gameTime")
                && __instance != EventInspector.Read<GameDateTimeText>(FreeManager.Instance, "_timeText")) return;
            if (__instance.GetComponent<CalendarDateMemo>() == null) __instance.gameObject.AddComponent<CalendarDateMemo>();
        }

        internal static void Toggle()
        {
            if (IsOpen) { FeaturePanel.Close(); return; }
            if (!Application.isFocused || FeaturePanel.BlocksInput || SaveNotes.IsOpen || OptionImpactPreview.IsOpen
                || EventInspectorPanel.BlocksInput || ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != "Free" && scene != "Story") return;
            if (AutoDialogueSpeed.SettingsVisible) return;
            if (scene == "Story" && StoryManager.Instance != null && (StoryManager.Instance.IsStoryPause || !StoryManager.Instance.EnableAction)) return;
            if (scene == "Free" && FreeManager.Instance != null && (FreeManager.Instance.IsProcessing || !FreeManager.Instance.EnableHotKey)) return;
            var time = CurrentTime;
            if (!ReferenceEquals(time, null)) CalendarPanel.ShowOverview(time.Year);
        }
    }

    internal sealed class CalendarDateMemo : MonoBehaviour
    {
        private Text _text;
        private float _next;
        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            if (_text == null)
            {
                var source = EventInspector.Read<Text>(GetComponent<GameDateTimeText>(), "_text");
                if (source == null) return;
                _text = FeaturePanel.Label(source.transform, "", 22, 0, 0, 600, 120);
                _text.gameObject.name = "LOMSaveCalendarDateMemo";
                _text.rectTransform.anchorMin = _text.rectTransform.anchorMax = new Vector2(0, 0);
                _text.rectTransform.anchoredPosition = new Vector2(0, -8);
                _text.color = new Color(1f, 0.85f, 0.5f);
                _text.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);
            }
            var note = ActionCalendar.Note(ActionCalendar.Today);
            _text.text = note.Length == 0 ? "" : "日历备注 · " + note;
            _text.rectTransform.sizeDelta = new Vector2(600, Math.Max(40, _text.preferredHeight));
        }
    }
}
