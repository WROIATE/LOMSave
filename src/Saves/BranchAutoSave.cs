using System;
using System.IO;
using System.Linq;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class BranchAutoSave
    {
        private static StoryCommandCheckpoint _savedCheckpoint;
        private static bool _saving, _loading;

        private static bool IsBranch(string kind)
        { return kind == "choose" || kind == "choosetimer" || kind == "dice"; }

        internal static void BeforeBranch(StoryCommandCheckpoint checkpoint, string payload = null)
        {
            if (_saving || !StoryCommandTrace.CanReplay(checkpoint) || !IsBranch(checkpoint.Kind)
                || (checkpoint.Kind == "choose" && !HasMultipleChoices(payload))
                || ReferenceEquals(checkpoint, _savedCheckpoint)
                || ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying
                || StoryCommandTrace.IsReplayPending || ReplayStateValidator.IsRestoring
                || SceneManager.GetActiveScene().name != "Story"
                || checkpoint.Script != PlayerStatManagerData.Instance.CurrentStoryScript) return;
            _saving = true;
            try
            {
                if (ActionAutoSave.SaveAutomatic(ActionAutoSave.BranchSlot, checkpoint.Kind, 0))
                    _savedCheckpoint = checkpoint;
            }
            finally { _saving = false; }
        }

        private static bool HasMultipleChoices(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return true;
            try
            {
                var token = JToken.Parse(payload);
                var rows = token is JArray array ? array.Children()
                    : token is JObject table ? table.Properties().Select(x => x.Value) : null;
                if (rows == null) return true;
                var count = 0;
                foreach (var row in rows)
                {
                    if (row.Type != JTokenType.String) return true;
                    var text = row.Value<string>();
                    if (!string.IsNullOrEmpty(text) && !text.StartsWith("~", StringComparison.Ordinal) && ++count > 1) return true;
                }
                return false;
            }
            // Serialization can fall back to a Lua table's description. Keep
            // saving uncertain menus rather than silently losing their branch.
            catch (Newtonsoft.Json.JsonReaderException) { return true; }
        }

        internal static void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F8) || !CanQuickLoad()) return;
            QuickLoad();
        }

        private static bool CanQuickLoad()
        {
            if (_loading || !Application.isFocused || FeaturePanel.BlocksInput || SaveNotes.IsOpen
                || EventInspectorPanel.BlocksInput || OptionImpactPreview.IsOpen || AutoDialogueSpeed.SettingsVisible
                || ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying || StoryCommandTrace.IsReplayPending
                || ReplayStateValidator.IsRestoring) return false;
            var controller = SceneController.Instance;
            if (controller == null || controller.IsPrepare || controller.IsLoading) return false;
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.GetComponentInParent<InputField>() != null) return false;
            var scene = SceneManager.GetActiveScene().name;
            if (scene == "Story")
            {
                var story = StoryManager.Instance;
                var current = StoryCommandTrace.Current;
                return story != null && story.EnableAction && !story.IsStoryPause
                    && !EventInspector.Read<bool>(story, "_panelOpen") && !EventInspector.Read<bool>(story, "_logOpen")
                    && current != null && (current.Kind == "say" || IsBranch(current.Kind));
            }
            if (scene == "Free")
                return FreeManager.Instance != null && FreeManager.Instance.EnableHotKey && !FreeManager.Instance.IsProcessing;
            return false;
        }

        private static void QuickLoad()
        {
            try
            {
                var system = SaveSystem.Instance;
                var data = system.GetAutoSaveData(ActionAutoSave.BranchSlot);
                if (data == null || data.GameSave == null)
                { Notice("还没有最近分支存档；遇到选项或骰子判定时会自动保存。", "save_missing"); return; }
                var file = ActionAutoSave.GetSaveFile(ActionAutoSave.BranchSlot);
                if (!HasValidCheckpoint(file, data.GameSave))
                { Notice("最近分支的进度记录不完整，暂时无法快速返回。", "checkpoint_invalid"); return; }

                _loading = true;
                // Follow the native auto-slot load route, including the original
                // manual slot and the sidecar source set by AutoLoadGameData.
                if (SceneManager.GetActiveScene().name == "Story" && StoryManager.Instance != null)
                {
                    StoryManager.Instance.AutoDialog(false);
                    StoryManager.Instance.SkipDialog(false);
                }
                if (!string.IsNullOrEmpty(data.Slot)) system.SetSlot(data.Slot);
                if (SoundManager.Instance != null) SoundManager.Instance.StopMusic();
                Time.timeScale = 1f;
                system.AutoLoadGameData(ActionAutoSave.BranchSlot);
                MissionManagerData.Instance.UpdateCheckMissions();
                SceneController.Instance.LoadCurrentScene();
                LOMSaveProbePlugin.WriteEvent("branch_quick_load_requested", new { slot = ActionAutoSave.BranchSlot });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError("branch_quick_load_failed", exception);
                Notice("最近分支读取失败，可从读档菜单重试。", "load_failed");
            }
            finally { _loading = false; }
        }

        private static bool HasValidCheckpoint(string file, GameSave save)
        {
            if (save.CurrentScene != "Story" || !File.Exists(file + ".lomsaveprobe.json")) return false;
            var state = JObject.Parse(File.ReadAllText(file + ".lomsaveprobe.json"));
            var command = state["Command"]?.ToObject<StoryCommandCheckpoint>();
            if (!StoryCommandTrace.CanReplay(command) || !IsBranch(command.Kind)) return false;
            var script = string.IsNullOrEmpty(save.CurrentStoryScript) ? save.StartStoryScript : save.CurrentStoryScript;
            return state.Value<int>("Version") >= 10 && state.Value<int>("Version") <= StorySaveFiles.StateVersion
                && StorySaveFiles.Mismatch(state.Value<int>("Version"), state.Value<long>("SaveTimeTick"), save.TimeTick,
                    state.Value<string>("SaveFileHash"), StorySaveFiles.Hash(file), command.Script, script) == null;
        }

        private static void Notice(string text, string reason)
        {
            LOMSaveProbePlugin.WriteEvent("branch_quick_load_unavailable", new { reason });
            if (StoryMainUI.Instance != null) StoryMainUI.Instance.DisplayMessageText(text);
        }
    }
}
