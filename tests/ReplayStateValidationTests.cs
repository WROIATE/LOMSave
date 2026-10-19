using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UnityEngine
{
    public static class Application { public static string version = "current"; }
}

namespace HarmonyLib
{
    public sealed class Traverse
    {
        private object target; private string method;
        public static Traverse Create(object value) { return new Traverse { target = value }; }
        public Traverse Method(string name, params Type[] types) { method = name; return this; }
        public object GetValue(params object[] args)
        { return target.GetType().GetMethod(method).Invoke(target, args); }
        public T GetValue<T>() { return (T)GetValue(new object[0]); }
    }
}
namespace Mortal.Core
{
    public sealed class GameSave
    {
        public string Version = "current";
        public int AddedFlag;
        public string StartStoryScript = "origin", CurrentStoryScript = "target", RestoreData = "backup";
        public long TimeTick = 1;
        public int FreeRandomState, Money = 10, Flag = 2;
    }
    public sealed class SaveSystem
    {
        public static SaveSystem Instance = new SaveSystem();
        public GameSave Live = new GameSave();
        public int Loads;
        public bool FailCapture;
        public GameSave CreateSaveData()
        { if (FailCapture) throw new InvalidOperationException("native capture failed"); return JObject.FromObject(Live).ToObject<GameSave>(); }
        public void ExecuteLoadGameData(GameSave save)
        {
            Loads++; Live = JObject.FromObject(save).ToObject<GameSave>();
            if (Live.Version != UnityEngine.Application.version) { Live.Version = UnityEngine.Application.version; Live.AddedFlag = 7; }
        }
    }
    public sealed class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public void SetStartScript(string script) { SaveSystem.Instance.Live.StartStoryScript = script; }
        public void SetStoryScript(string script) { SaveSystem.Instance.Live.CurrentStoryScript = script; }
    }
    public sealed class SceneController
    {
        public static SceneController Instance = new SceneController();
        public int Loads;
        public void LoadStory() { Loads++; }
    }
}
namespace LOMSave.Probe
{
    internal sealed class StoryCommandCheckpoint { public string Script = "target"; }
    internal static class ReplayCharacterContext
    {
        public static string Fallback;
        public static void PrepareFallback(string script) { Fallback = script; }
    }
    internal static class LOMSaveProbePlugin
    {
        public static List<string> Events = new List<string>();
        public static void WriteEvent(string name, object data) { Events.Add(name); }
        public static void WriteError(string name, Exception error, object data = null) { Events.Add(name); }
        public static string Sha256(string text) { return text; }
    }
    public static class ReplayStateValidationTests
    {
        private static int checks;
        private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        public static int TempleEntryFlag()
        {
            string source;
            return ReplayOriginPolicy.Select(
                JObject.Parse("{ 'CurrentStoryScript': 'temple', 'Flag': 0 }"),
                JObject.Parse("{ 'CurrentStoryScript': 'inn', 'Flag': 1 }"),
                "temple", out source).Value<int>("Flag");
        }
        public static string Run()
        {
            var system = Mortal.Core.SaveSystem.Instance;
            var target = new Mortal.Core.GameSave(); var checkpoint = new StoryCommandCheckpoint();
            var temple = JObject.Parse("{ 'CurrentStoryScript': 'temple', 'StartStoryScript': 'temple', 'Flag': 0 }");
            var inn = JObject.Parse("{ 'CurrentStoryScript': 'inn', 'StartStoryScript': 'inn', 'Flag': 1 }");
            string source;
            var origin = ReplayOriginPolicy.Select(temple, inn, "temple", out source);
            Check(origin.Value<string>("CurrentStoryScript") == "temple" && origin.Value<int>("Flag") == 0,
                "A temple checkpoint cannot inherit the inn's entry flags after a legacy load");
            Check(source == "target_entry_script_mismatch" && !ReferenceEquals(origin, temple),
                "Rejected origins use an independent authoritative target for replay and fallback");
            origin["Flag"] = 99;
            Check(temple.Value<int>("Flag") == 0 && inn.Value<int>("Flag") == 1, "Origin selection cannot modify either stored save");
            origin = ReplayOriginPolicy.Select(temple, inn, "inn", out source);
            Check(origin.Value<int>("Flag") == 1 && source == "matching_story_entry", "A validated complete route retains its actual earlier entry");
            Check(!ReplayOriginPolicy.Matches(inn, "temple") && ReplayOriginPolicy.Matches(temple, "temple"),
                "Complete-route origin validation binds to the first visited script");
            origin = ReplayOriginPolicy.Select(temple, null, "temple", out source);
            Check(origin.Value<int>("Flag") == 0 && source == "target_entry_missing", "A legacy missing origin uses the requested save without fabricating history");
            ReplayStateValidator.Prepare(target); system.Live.Money = 20;
            Check(!ReplayStateValidator.RestoreAndValidate(checkpoint, true), "Duplicated effects must fail before authoritative overwrite");
            Check(system.Loads == 0 && system.Live.Money == 20, "Validation cannot conceal a wrong route by overwriting it");
            Check(LOMSaveProbePlugin.Events.Contains("story_replay_path_mismatch"), "Route mismatch has distinct diagnostic evidence");

            ReplayStateValidator.Prepare(target);
            Check(ReplayStateValidator.RestoreAndValidate(checkpoint), "Legacy recovery still restores the authoritative target");
            Check(system.Loads == 1 && system.Live.Money == 10, "Legacy target state is restored once");

            ReplayStateValidator.Prepare(target); system.Live.TimeTick = 123; system.Live.FreeRandomState = 999;
            system.Live.RestoreData = "transient native backup";
            Check(ReplayStateValidator.RestoreAndValidate(checkpoint, true), "Native metadata differences do not invalidate a correct route");
            Check(LOMSaveProbePlugin.Events.Contains("story_replay_path_validated"), "A successful route is validated before restoring its target");
            ReplayStateValidator.Prepare(target); system.Live.Flag = 3;
            Check(!ReplayStateValidator.RestoreAndValidate(checkpoint, true) && system.Loads == 2, "A changed plot flag cannot pass strict validation");
            Check(!ReplayStateValidator.IsRestoring, "Validation failure leaves the native restore guard clear");
            ReplayStateValidator.Reset();
            Check(!ReplayStateValidator.RestoreAndValidate(checkpoint, true), "A missing authoritative target cannot validate a new route");
            Check(ReplayStateValidator.RestoreAndValidate(checkpoint), "Legacy missing-target behavior stays compatible");

            ReplayStateValidator.Prepare(target); system.FailCapture = true;
            Check(!ReplayStateValidator.RestoreAndValidate(checkpoint, true) && !ReplayStateValidator.IsRestoring,
                "A native capture exception fails recovery cleanly instead of escaping the target callback");
            system.FailCapture = false;

            Check(ReplayStateValidator.RestartAtStoryBeginning("origin", "test", JObject.FromObject(target)), "A failed route can restart its origin");
            Check(Mortal.Core.SceneController.Instance.Loads == 1 && ReplayCharacterContext.Fallback == "origin"
                && system.Live.CurrentStoryScript == "origin" && !ReplayStateValidator.IsRestoring,
                "Fallback reloads the scene and prepares actor context with cleanup");

            var old = new Mortal.Core.GameSave { Version = "old", Money = 35, Flag = 9 };
            var beforeLoads = system.Loads;
            ReplayStateValidator.Prepare(old, true);
            Check(system.Loads == beforeLoads + 1 && system.Live.AddedFlag == 7,
                "Old-version targets use native migration once before the replay starts");
            Check(old.Version == "old" && old.AddedFlag == 0 && !ReplayStateValidator.IsRestoring,
                "Migration cannot modify the disk target and must release the recursive load guard");
            Check(ReplayStateValidator.RestoreAndValidate(checkpoint, true) && system.Live.Money == 35 && system.Live.Flag == 9,
                "Legitimate native version migration does not reject a correct replay");
            ReplayStateValidator.Prepare(old, true); system.Live.Flag++;
            beforeLoads = system.Loads;
            Check(!ReplayStateValidator.RestoreAndValidate(checkpoint, true) && system.Loads == beforeLoads,
                "Migration must not hide a wrong replay route by restoring the target first");
            ReplayStateValidator.Prepare(old, true); system.Live.AddedFlag++;
            Check(!ReplayStateValidator.RestoreAndValidate(checkpoint, true),
                "Newly introduced game flags are validated as strictly as existing flags");
            beforeLoads = system.Loads;
            ReplayStateValidator.Prepare(new Mortal.Core.GameSave(), true);
            Check(system.Loads == beforeLoads, "Current-version saves add no native migration load");
            ReplayStateValidator.Prepare(old, false);
            Check(system.Loads == beforeLoads, "Non-replay loads add no native migration load");
            system.FailCapture = true; ReplayStateValidator.Prepare(old, true);
            Check(!ReplayStateValidator.IsRestoring && !ReplayStateValidator.RestoreAndValidate(checkpoint, true),
                "Failed migration capture cannot leave the guard set or validate a strict replay");
            Check(!ReplayStateValidator.RestoreAndValidate(checkpoint),
                "Migration failure cannot silently accept an unvalidated legacy checkpoint");
            system.FailCapture = false;
            system.Live = new Mortal.Core.GameSave();
            beforeLoads = system.Loads;
            ReplayStateValidator.Prepare(new Mortal.Core.GameSave());
            Check(ReplayStateValidator.ValidateDirect(checkpoint) && system.Loads == beforeLoads,
                "Direct continuation validates without executing another load or overwriting gameplay state");
            ReplayStateValidator.Prepare(new Mortal.Core.GameSave()); system.Live.Money--;
            Check(!ReplayStateValidator.ValidateDirect(checkpoint) && system.Loads == beforeLoads && system.Live.Money == 9,
                "Direct continuation detects duplicate deductions and does not hide them with an overwrite");
            return checks + " replay state validation assertions passed";
        }
    }
}
