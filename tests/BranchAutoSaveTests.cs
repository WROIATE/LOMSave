using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mortal.Core;
using Mortal.Story;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Drive the production command/dice trace, branch writer, validation and F8
// route against a native boundary. All files belong to a unique obj directory.
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public class Traverse
    {
        private object _value; private string _field;
        public static Traverse Create(object value) { return new Traverse { _value = value }; }
        public Traverse Field(string field) { _field = field; return this; }
        public void SetValue(object value) { _value.GetType().GetField(_field).SetValue(_value, value); }
    }
}
namespace UnityEngine
{
    public enum KeyCode { F8 }
    public static class Input { public static bool Pressed; public static bool GetKeyDown(KeyCode key) { return Pressed; } }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float timeScale = 1; }
    public class GameObject
    {
        public object ParentInput;
        public T GetComponentInParent<T>() where T : class { return ParentInput as T; }
    }
}
namespace UnityEngine.UI { public class InputField { } }
namespace UnityEngine.EventSystems
{
    public class EventSystem { public static EventSystem current = new EventSystem(); public GameObject currentSelectedGameObject; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager { public static string Name = "Story"; public static Scene GetActiveScene() { return new Scene { name = Name }; } }
}
namespace Mortal.Story
{
    public class LuaManager { public static LuaManager Instance = new LuaManager(); public void ExecuteScript(string script) { } }
    public class StoryManager
    {
        public static StoryManager Instance = new StoryManager(); public bool IsStoryPause, _panelOpen, _logOpen;
        public bool EnableAction = true, Auto = true, Skip = true;
        public void AutoDialog(bool value) { Auto = value; }
        public void SkipDialog(bool value) { Skip = value; }
    }
    public class StoryMainUI
    { public static StoryMainUI Instance = new StoryMainUI(); public string LastNotice; public void DisplayMessageText(string text) { LastNotice = text; } }
    public class DiceMenuDialog { public int _resultSelection; public int ResultSelection { get { return _resultSelection; } } }
}
namespace Mortal.Free
{
    public class FreeManager { public static FreeManager Instance = new FreeManager(); public bool EnableHotKey = true, IsProcessing; }
}
namespace Mortal.Core
{
    public class GameSave { public string CurrentScene = "Story", CurrentStoryScript, StartStoryScript; public long TimeTick; public int Flag; }
    public class AutoGameSave { public string Slot; public GameSave GameSave; }
    public class PlayerStatManagerData { public static PlayerStatManagerData Instance = new PlayerStatManagerData(); public string CurrentStoryScript; }
    public class SceneController
    {
        public static SceneController Instance = new SceneController(); public bool IsPrepare, IsLoading; public int Loads;
        public void LoadCurrentScene() { Loads++; IsPrepare = true; }
    }
    public class MissionManagerData
    {
        public static MissionManagerData Instance = new MissionManagerData(); public int Advances;
        public void UpdateCheckMissions()
        {
            var allowed = (bool)typeof(LOMSave.Probe.ReplayMissionGuard).GetMethod("PreserveLoadedCheckpoint", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (allowed) { Advances++; PlayerStatManagerData.Instance.CurrentStoryScript = "unrelated_main_story"; }
        }
    }
    public class SoundManager { public static SoundManager Instance = new SoundManager(); public int Stops; public void StopMusic() { Stops++; } }
    public class SaveSystem
    {
        public static SaveSystem Instance = new SaveSystem(); public string Directory, CurrentSlot = "17", LoadedSource;
        public int Writes, Flag; public bool SuppressWrite;
        private long _tick = 100;
        public string GetSaveDataFile(string slot) { return Path.Combine(Directory, slot + ".dat"); }
        public void SetSlot(string slot) { CurrentSlot = slot; }
        public AutoGameSave GetAutoSaveData(string slot)
        {
            var file = GetSaveDataFile(slot);
            return File.Exists(file) ? JsonConvert.DeserializeObject<AutoGameSave>(File.ReadAllText(file)) : null;
        }
        public void AutoSaveData(string slot)
        {
            if (SuppressWrite) return;
            var command = LOMSave.Probe.StoryCommandTrace.Current;
            var save = new AutoGameSave { Slot = CurrentSlot, GameSave = new GameSave {
                CurrentStoryScript = PlayerStatManagerData.Instance.CurrentStoryScript, StartStoryScript = PlayerStatManagerData.Instance.CurrentStoryScript,
                TimeTick = ++_tick, Flag = Flag } };
            var file = GetSaveDataFile(slot);
            File.WriteAllText(file, JsonConvert.SerializeObject(save));
            File.WriteAllText(file + ".lomsaveprobe.json", JsonConvert.SerializeObject(new {
                Version = LOMSave.Probe.StorySaveFiles.StateVersion, SaveTimeTick = save.GameSave.TimeTick,
                SaveFileHash = LOMSave.Probe.StorySaveFiles.Hash(file), Command = command,
                ChoiceHistory = LOMSave.Probe.StoryCommandTrace.CaptureChoiceHistory() }));
            Writes++;
        }
        public void AutoLoadGameData(string slot)
        {
            var previous = LOMSave.Probe.SaveSlotContext.Begin(slot);
            try
            {
                LoadedSource = LOMSave.Probe.SaveSlotContext.Resolve(CurrentSlot);
                var save = GetAutoSaveData(slot).GameSave;
                var state = JObject.Parse(File.ReadAllText(GetSaveDataFile(slot) + ".lomsaveprobe.json"));
                LOMSave.Probe.StoryCommandTrace.ConfigureReplay(state["Command"].ToObject<LOMSave.Probe.StoryCommandCheckpoint>(),
                    state["ChoiceHistory"].ToObject<List<LOMSave.Probe.StoryChoiceDecision>>());
                PlayerStatManagerData.Instance.CurrentStoryScript = save.CurrentStoryScript; Flag = save.Flag;
            }
            finally { LOMSave.Probe.SaveSlotContext.End(previous); }
        }
    }
}
namespace LOMSave.Probe
{
    internal static class ActionAutoSave
    {
        internal const string BranchSlot = "auto_branch", BranchDisplayName = "最近分支";
        internal static string GetSaveFile(string slot) { return SaveSystem.Instance.GetSaveDataFile(slot); }
        internal static bool SaveAutomatic(string slot, string source, int actionCost)
        { var before = SaveSystem.Instance.Writes; SaveSystem.Instance.AutoSaveData(slot); return SaveSystem.Instance.Writes > before; }
    }
    internal static class ReplayRuntime
    {
        internal static bool IsRestoring, IsReplaying;
        internal static void Reset(string reason) { IsRestoring = IsReplaying = false; }
        internal static void Prepare() { IsRestoring = true; }
        internal static void Start() { IsReplaying = true; }
        internal static void ReportProgress() { }
    }
    internal static class ReplayStateValidator { internal static bool IsRestoring; }
    internal static class FeaturePanel { internal static bool BlocksInput; }
    internal static class SaveNotes { internal static bool IsOpen; }
    internal static class EventInspectorPanel { internal static bool BlocksInput; }
    internal static class OptionImpactPreview { internal static bool IsOpen; }
    internal static class AutoDialogueSpeed { internal static bool SettingsVisible; }
    internal static class EventInspector
    { internal static T Read<T>(object value, string name) { return (T)value.GetType().GetField(name).GetValue(value); } }
    internal static class LOMSaveProbePlugin
    {
        internal static int Reached; internal static List<string> Errors = new List<string>();
        internal static string Sha256(string value)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        internal static void WriteEvent(string name, object payload) { }
        internal static void WriteError(string name, Exception error, object context = null) { Errors.Add(name); }
        internal static void ReplayTargetReached(StoryCommandCheckpoint checkpoint) { Reached++; ReplayRuntime.IsReplaying = false; }
        internal static void ReplayAborted(string reason, StoryCommandCheckpoint checkpoint) { ReplayRuntime.Reset(reason); }
    }
    public static class BranchAutoSaveTests
    {
        private static int _checks;
        private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
        private static string Begin(string kind, string payload = "[\"A\",\"B\"]")
        {
            string result; StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__" + kind + "|begin|" + payload, out result);
            return result.Split('|')[0];
        }
        private static void End(string kind, string token, int selection = 1)
        { string ignored; StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__" + kind + "|end|" + token + "|" + selection, out ignored); }
        private static JObject State() { return JObject.Parse(File.ReadAllText(ActionAutoSave.GetSaveFile(ActionAutoSave.BranchSlot) + ".lomsaveprobe.json")); }
        private static void NormalPlay()
        {
            ReplayRuntime.Reset("test"); ReplayStateValidator.IsRestoring = false;
            StoryCommandTrace.ConfigureReplay(null); SceneController.Instance.IsPrepare = SceneController.Instance.IsLoading = false;
        }
        private static void Blocked(string reason, Action setup, Action reset)
        {
            var loads = SceneController.Instance.Loads; setup(); BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == loads, reason); reset();
        }
        private static IEnumerator NativeRoll(DiceMenuDialog dialog)
        { SaveSystem.Instance.Flag = 99; yield return "roll-started"; dialog._resultSelection = 2; }
        public static string Run(string directory)
        {
            Directory.CreateDirectory(directory); SaveSystem.Instance.Directory = directory;
            var saves = SaveSystem.Instance; var player = PlayerStatManagerData.Instance;
            player.CurrentStoryScript = "branch_test"; StoryCommandTrace.Begin();
            var token = Begin("say", "before choice"); End("say", token);
            Check(saves.Writes == 0, "Ordinary dialogue does not write a branch snapshot");
            token = Begin("choose");
            Check(saves.Writes == 1 && State()["Command"].Value<string>("Kind") == "choose", "Normal choice entry saves its exact checkpoint");
            Check(!State()["ChoiceHistory"].Any(), "The pending choice result is absent from the snapshot");
            BranchAutoSave.BeforeBranch(StoryCommandTrace.Current);
            Check(saves.Writes == 1, "Repeated notification for the same occurrence cannot save twice");
            saves.Flag = 1; End("choose", token, 2);
            Check(saves.GetAutoSaveData(ActionAutoSave.BranchSlot).GameSave.Flag == 0, "A choice snapshot contains pre-selection stats");
            token = Begin("say", "after choice"); End("say", token);
            Check(saves.Writes == 1, "Subsequent dialogue retains the last branch file");
            token = Begin("choosetimer");
            Check(saves.Writes == 2 && State()["Command"].Value<string>("Kind") == "choosetimer", "Timed choices save before their native timer starts");
            Check(State()["ChoiceHistory"].Count() == 1 && State()["ChoiceHistory"][0].Value<int>("Result") == 2,
                "Earlier choices remain available to replay the route to the newest branch");
            End("choosetimer", token);
            var dialog = new DiceMenuDialog();
            var rollArgs = new object[] { dialog, new[] { "first", "second" }, 0, "check", NativeRoll(dialog) };
            typeof(DiceRollTrace).GetMethod("AfterExecuteRoll", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, rollArgs);
            var routine = (IEnumerator)rollArgs[4]; routine.MoveNext();
            Check(saves.Writes == 3 && State()["Command"].Value<string>("Kind") == "dice", "The production dice hook captures before native roll execution");
            Check(saves.GetAutoSaveData(ActionAutoSave.BranchSlot).GameSave.Flag == 1 && saves.Flag == 99,
                "Dice snapshot precedes native roll side effects");
            routine.MoveNext();
            Check(StoryCommandTrace.CaptureChoiceHistory().Last().Result == 2, "Dice results are recorded only after native execution finishes");
            token = Begin("choose", "[\"继续\"]"); End("choose", token);
            Check(saves.Writes == 3 && State()["Command"].Value<string>("Kind") == "dice", "A single continue button cannot displace the last dice branch");
            token = Begin("choose", "{\"1\":\"~locked\",\"2\":\"continue\"}"); End("choose", token, 2);
            Check(saves.Writes == 3, "Lua table menus with only one available choice retain the prior branch");
            token = Begin("choose", "[]"); End("choose", token);
            Check(saves.Writes == 3, "Empty menus are not branches");
            var checkpoint = State()["Command"].ToObject<StoryCommandCheckpoint>();
            var writes = saves.Writes;
            UnityEngine.SceneManagement.SceneManager.Name = "Free"; BranchAutoSave.BeforeBranch(checkpoint);
            UnityEngine.SceneManagement.SceneManager.Name = "Story";
            Check(saves.Writes == writes, "Only story command checkpoints can write branch snapshots");
            checkpoint.Script = "another_script"; BranchAutoSave.BeforeBranch(checkpoint); checkpoint.Script = player.CurrentStoryScript;
            Check(saves.Writes == writes, "A checkpoint from another script cannot overwrite the branch slot");
            var sequence = checkpoint.Sequence; checkpoint.Sequence = 0; BranchAutoSave.BeforeBranch(checkpoint); checkpoint.Sequence = sequence;
            Check(saves.Writes == writes, "Unsupported checkpoint sequences cannot displace a valid branch snapshot");
            ReplayStateValidator.IsRestoring = true; BranchAutoSave.BeforeBranch(checkpoint); ReplayStateValidator.IsRestoring = false;
            ReplayRuntime.IsRestoring = true; BranchAutoSave.BeforeBranch(checkpoint); ReplayRuntime.IsRestoring = false;
            ReplayRuntime.IsReplaying = true; BranchAutoSave.BeforeBranch(checkpoint); ReplayRuntime.IsReplaying = false;
            Check(saves.Writes == writes, "Every restore phase protects the existing branch file");
            StoryCommandTrace.ConfigureReplay(checkpoint); StoryCommandTrace.Begin(); Begin("choose", "different payload");
            Check(saves.Writes == writes, "A replay mismatch cannot overwrite the last valid branch while aborting");
            NormalPlay(); StoryCommandTrace.Begin();
            saves.SuppressWrite = true; token = Begin("choose");
            Check(saves.Writes == writes, "Suppressed native writes retain the prior branch");
            saves.SuppressWrite = false; BranchAutoSave.BeforeBranch(StoryCommandTrace.Current);
            Check(saves.Writes == writes + 1, "A failed write does not mark an occurrence as saved");
            End("choose", token);

            // A new script gives F8 a sequence-one target, so the full quick-load
            // route and replay arrival can be exercised without any native UI.
            player.CurrentStoryScript = "quick_load_test"; StoryCommandTrace.Begin(); saves.Flag = 7;
            token = Begin("choose"); saves.Flag = 8; End("choose", token, 1);
            Begin("say", "after branch");
            var file = ActionAutoSave.GetSaveFile(ActionAutoSave.BranchSlot);
            var beforeHash = StorySaveFiles.Hash(file); writes = saves.Writes;
            Input.Pressed = true; Time.timeScale = 0.5f;
            BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == 1 && saves.LoadedSource == "auto_branch", "F8 loads the dedicated branch source through the native auto-load route");
            Check(saves.CurrentSlot == "17" && SaveSlotContext.Resolve("17") == "17", "Quick loading preserves the original manual slot and unwinds source context");
            Check(saves.Flag == 7 && Time.timeScale == 1 && MissionManagerData.Instance.Advances == 0,
                "Quick loading restores pre-choice stats, resumes time and preserves the checkpoint mission route");
            Check(!StoryManager.Instance.Auto && !StoryManager.Instance.Skip, "Quick loading stops old automatic dialogue and skip before scene unload");
            BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == 1, "A pending scene transition cannot receive another quick load");
            SceneController.Instance.IsPrepare = false; StoryCommandTrace.Begin(); token = Begin("choose");
            Check(LOMSaveProbePlugin.Reached == 1 && !StoryCommandTrace.IsReplayPending,
                "Replay returns to the saved choice without selecting it");
            Check(saves.Writes == writes && StorySaveFiles.Hash(file) == beforeHash,
                "Reaching the replay target preserves the original branch file");
            ReplayRuntime.Reset("presentation_restored"); End("choose", token, 2);
            Check(StoryCommandTrace.CaptureChoiceHistory().Last().Result == 2, "A different selection can be made after returning");
            token = Begin("choose", "[\"next A\",\"next B\"]"); End("choose", token);
            Check(saves.Writes == writes + 1 && State()["Command"].Value<int>("Sequence") == 2,
                "The next actual branch replaces the special slot");
            Begin("say", "stable input point");

            Blocked("Background windows do not quick load", () => Application.isFocused = false, () => Application.isFocused = true);
            Blocked("Feature modal blocks quick loading", () => FeaturePanel.BlocksInput = true, () => FeaturePanel.BlocksInput = false);
            Blocked("Note input blocks quick loading", () => SaveNotes.IsOpen = true, () => SaveNotes.IsOpen = false);
            Blocked("Event preview blocks quick loading", () => EventInspectorPanel.BlocksInput = true, () => EventInspectorPanel.BlocksInput = false);
            Blocked("Impact preview blocks quick loading", () => OptionImpactPreview.IsOpen = true, () => OptionImpactPreview.IsOpen = false);
            Blocked("Dialogue settings block quick loading", () => AutoDialogueSpeed.SettingsVisible = true, () => AutoDialogueSpeed.SettingsVisible = false);
            Blocked("Restore validation blocks quick loading", () => ReplayStateValidator.IsRestoring = true, () => ReplayStateValidator.IsRestoring = false);
            Blocked("Loading scenes block quick loading", () => SceneController.Instance.IsLoading = true, () => SceneController.Instance.IsLoading = false);
            Blocked("Paused story blocks quick loading", () => StoryManager.Instance.IsStoryPause = true, () => StoryManager.Instance.IsStoryPause = false);
            Blocked("Non-interactive story sequences cannot be interrupted by F8", () => StoryManager.Instance.EnableAction = false, () => StoryManager.Instance.EnableAction = true);
            Blocked("Native panels block quick loading", () => StoryManager.Instance._panelOpen = true, () => StoryManager.Instance._panelOpen = false);
            Blocked("Recall blocks quick loading", () => StoryManager.Instance._logOpen = true, () => StoryManager.Instance._logOpen = false);
            Blocked("A selected child of an input field blocks quick loading", () => UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject = new GameObject { ParentInput = new UnityEngine.UI.InputField() },
                () => UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject = null);
            Blocked("Title scene does not quick load", () => UnityEngine.SceneManagement.SceneManager.Name = "Title", () => UnityEngine.SceneManagement.SceneManager.Name = "Story");
            var bytes = File.ReadAllBytes(file); var sidecar = File.ReadAllText(file + ".lomsaveprobe.json");
            var loads = SceneController.Instance.Loads; var stops = SoundManager.Instance.Stops;
            File.WriteAllText(file, Encoding.UTF8.GetString(bytes) + " "); BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == loads && SoundManager.Instance.Stops == stops,
                "Stale sidecar hashes are rejected before changing slot, audio or scene");
            File.WriteAllBytes(file, bytes); File.Delete(file + ".lomsaveprobe.json"); BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == loads, "Missing precise records cannot silently load the story beginning");
            File.WriteAllText(file + ".lomsaveprobe.json", "broken json"); BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == loads && LOMSaveProbePlugin.Errors.Last() == "branch_quick_load_failed", "Corrupt records produce a failure notice without loading");
            File.WriteAllText(file + ".lomsaveprobe.json", sidecar);
            var invalidState = JObject.Parse(sidecar); invalidState["Command"]["Kind"] = "say";
            File.WriteAllText(file + ".lomsaveprobe.json", invalidState.ToString()); BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == loads, "A non-branch record is not accepted as recent branch");
            File.WriteAllText(file + ".lomsaveprobe.json", sidecar); File.Delete(file); BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == loads && StoryMainUI.Instance.LastNotice.Contains("还没有"), "A missing branch slot explains when automatic saving will begin");
            File.WriteAllBytes(file, bytes);
            UnityEngine.SceneManagement.SceneManager.Name = "Free";
            Blocked("A busy free scene cannot quick load", () => Mortal.Free.FreeManager.Instance.IsProcessing = true, () => Mortal.Free.FreeManager.Instance.IsProcessing = false);
            BranchAutoSave.Update();
            Check(SceneController.Instance.Loads == loads + 1, "Free-roam play can use the same F8 return route");

            NormalPlay(); UnityEngine.SceneManagement.SceneManager.Name = "Story";
            player.CurrentStoryScript = "quick_dice_test"; StoryCommandTrace.Begin(); saves.Flag = 42;
            dialog = new DiceMenuDialog();
            rollArgs = new object[] { dialog, new[] { "first", "second" }, 0, "check", NativeRoll(dialog) };
            typeof(DiceRollTrace).GetMethod("AfterExecuteRoll", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, rollArgs);
            routine = (IEnumerator)rollArgs[4]; routine.MoveNext(); routine.MoveNext(); Begin("say", "after dice result");
            writes = saves.Writes; beforeHash = StorySaveFiles.Hash(file); BranchAutoSave.Update();
            Check(saves.Flag == 42 && StoryCommandTrace.IsReplayPending, "F8 returns to a pre-roll snapshot after a dice result");
            SceneController.Instance.IsPrepare = false; StoryCommandTrace.Begin();
            dialog = new DiceMenuDialog();
            rollArgs = new object[] { dialog, new[] { "first", "second" }, 0, "check", NativeRoll(dialog) };
            typeof(DiceRollTrace).GetMethod("AfterExecuteRoll", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, rollArgs);
            routine = (IEnumerator)rollArgs[4];
            Check(routine.MoveNext() && saves.Flag == 99 && dialog.ResultSelection == 0,
                "Reaching a dice checkpoint starts a fresh native roll instead of replaying the old result");
            Check(saves.Writes == writes && StorySaveFiles.Hash(file) == beforeHash,
                "Restoring a dice checkpoint does not replace the pre-roll branch snapshot");
            routine.MoveNext(); NormalPlay();
            StoryCommandTrace.Begin(); writes = saves.Writes;
            token = Begin("choosetimer", "[\"only choice before timeout\"]"); End("choosetimer", token);
            Check(saves.Writes == writes + 1, "A timed single-option menu still saves because timeout can choose another result");
            return _checks + " branch autosave, dice, replay and quick-load assertions passed";
        }
    }
}
