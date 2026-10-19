using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Collections;
using Mortal.Core;

// Exercise the production command replay and mission prefix without native Unity.
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public sealed class Traverse
    {
        private object target; private string field;
        public static Traverse Create(object value) { return new Traverse { target = value }; }
        public Traverse Field(string name) { field = name; return this; }
        public void SetValue(object value) { target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value); }
    }
}
namespace Mortal.Core
{
    public class MissionManagerData { }
    public enum GameStatType { 命運 = 7 }
    public sealed class GameStat
    {
        public int Value = 10, AddCalls;
        public void AddValue(int amount, bool update) { AddCalls++; Value += amount; }
    }
    public sealed class PlayerStatData
    {
        public GameStat Fate = new GameStat();
        public GameStat Get(GameStatType type) { return Fate; }
    }
    public class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public string CurrentStoryScript;
        public PlayerStatData Stats = new PlayerStatData();
    }
}
namespace Mortal.Story
{
    public sealed class DiceMenuDialog
    {
        public int _resultSelection;
        public int ResultSelection { get { return _resultSelection; } }
    }
    public class LuaManager
    {
        public static LuaManager Instance = new LuaManager();
        public int InstallCount;
        public void ExecuteScript(string script) { InstallCount++; }
    }
}
namespace LOMSave.Probe
{
    internal static class BranchAutoSave { internal static void BeforeBranch(StoryCommandCheckpoint checkpoint, string payload) { } }
    internal static class ReplayRuntime
    {
        internal static bool IsRestoring, IsReplaying;
        internal static void Reset(string reason) { IsRestoring = IsReplaying = false; }
        internal static void Prepare() { IsRestoring = true; }
        internal static void Start() { IsReplaying = true; }
        internal static void ReportProgress() { }
    }
    internal static class LOMSaveProbePlugin
    {
        internal static bool ValidateDirectCommand(StoryCommandCheckpoint checkpoint) { return true; }
        internal static int Reached, Aborted;
        internal static string AbortReason;
        internal static string Sha256(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        internal static void WriteEvent(string name, object payload) { }
        internal static void WriteError(string name, Exception error, object payload) { throw error; }
        internal static bool ReplayTargetReached(StoryCommandCheckpoint checkpoint)
        { Reached++; ReplayRuntime.IsReplaying = false; return true; }
        internal static void ReplayAborted(string reason, StoryCommandCheckpoint target)
        { Aborted++; AbortReason = reason; ReplayRuntime.Reset(reason); }
    }
    public static class StoryReplayTests
    {
        public static string Installer()
        { return (string)typeof(StoryCommandTrace).GetField("InstallScript", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue(); }
        private static int _checks, _missionAdvances;
        private static readonly string Options = "[\"O_Travel_601_501_001\",\"O_Travel_601_501_002\"]";
        private static void Check(bool condition, string message)
        { _checks++; if (!condition) throw new Exception(message); }
        private static bool CheckMissionsAllowed()
        { return (bool)typeof(ReplayMissionGuard).GetMethod("PreserveLoadedCheckpoint", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null); }
        private static void NativeMissionCheck()
        { PlayerStatManagerData.Instance.CurrentStoryScript = "ch6_3_1"; _missionAdvances++; }
        private static StoryCommandCheckpoint Target()
        {
            return new StoryCommandCheckpoint { Script = "travel_result_601_501", Sequence = 21,
                Kind = "choose", PayloadHash = LOMSaveProbePlugin.Sha256(Options) };
        }
        private static IEnumerator NativeRerolls(Mortal.Story.DiceMenuDialog dialog)
        {
            PlayerStatManagerData.Instance.Stats.Fate.AddValue(-3, true);
            yield return "native dice animation";
            dialog._resultSelection = 2;
        }
        private static IEnumerator Dice(Mortal.Story.DiceMenuDialog dialog, IEnumerator original)
        {
            return (IEnumerator)typeof(DiceRollTrace).GetMethod("TraceRoll", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { dialog, original, new[] { "failure", "success" }, 1, "review_dice" });
        }
        public static string Run()
        {
            // Native load-button order: ExecuteLoadGameData,
            // UpdateCheckMissions, LoadCurrentScene, Lua.Init.
            PlayerStatManagerData.Instance.CurrentStoryScript = Target().Script;
            NativeMissionCheck();
            Check(PlayerStatManagerData.Instance.CurrentStoryScript == "ch6_3_1" && _missionAdvances == 1,
                "The native mission route reproduces both script overwrite and mission advancement");
            PlayerStatManagerData.Instance.CurrentStoryScript = Target().Script;
            StoryCommandTrace.ConfigureReplay(Target());
            if (CheckMissionsAllowed()) NativeMissionCheck();
            Check(PlayerStatManagerData.Instance.CurrentStoryScript == Target().Script && _missionAdvances == 1,
                "Loading a precise checkpoint must preserve the saved script and mission state");
            Check(StoryCommandTrace.Begin() && ReplayRuntime.IsReplaying,
                "The saved travel script starts replay instead of waiting behind the mask");
            for (var i = 0; i < 20; i++)
            {
                string result;
                StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__say|begin|line " + i, out result);
                Check(result.EndsWith("|skip", StringComparison.Ordinal), "Recorded dialogue is skipped during restoration");
            }
            string chooseResult;
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|" + Options, out chooseResult);
            Check(chooseResult.EndsWith("|continue", StringComparison.Ordinal) && LOMSaveProbePlugin.Reached == 1,
                "The saved sequence 21 travel choice is reached without choosing for the player");
            Check(!StoryCommandTrace.IsReplayPending && !ReplayRuntime.IsReplaying,
                "The reached checkpoint ends replay acceleration");
            Check(!CheckMissionsAllowed(), "Presentation restoration must still protect the checkpoint");
            ReplayRuntime.Reset("presentation_restored");
            Check(CheckMissionsAllowed(), "Ordinary mission checking resumes after restoration completes");
            NativeMissionCheck();
            Check(_missionAdvances == 2, "Normal game progression still advances missions");

            StoryCommandTrace.ConfigureReplay(Target());
            PlayerStatManagerData.Instance.CurrentStoryScript = "unexpected_script";
            var installs = Mortal.Story.LuaManager.Instance.InstallCount;
            Check(!StoryCommandTrace.Begin(), "Unexpected startup scripts must not execute under the loading mask");
            Check(!StoryCommandTrace.IsReplayPending && LOMSaveProbePlugin.AbortReason == "startup_script_changed",
                "Startup mismatch immediately schedules recovery instead of waiting for a nonexistent target");
            Check(Mortal.Story.LuaManager.Instance.InstallCount == installs,
                "Rejected startup does not initialize tracing for the unrelated script");

            PlayerStatManagerData.Instance.CurrentStoryScript = "ordinary_story";
            Check(StoryCommandTrace.Begin() && CheckMissionsAllowed(), "Normal Lua startup remains available");
            var invalid = Target(); invalid.Sequence = 0;
            StoryCommandTrace.ConfigureReplay(invalid);
            Check(!StoryCommandTrace.IsReplayPending && !ReplayRuntime.IsRestoring,
                "Invalid checkpoints cannot leave a loading mask armed");

            // A native scene origin, an intermediate script, and re-entry to
            // the first script must retain decisions without ambiguous IDs.
            var hashA = new string('a', 64); var hashB = new string('b', 64);
            StoryCommandTrace.ConfigureReplay(null);
            Mortal.Story.LuaManager.Instance = new Mortal.Story.LuaManager();
            PlayerStatManagerData.Instance.CurrentStoryScript = "origin";
            Check(StoryCommandTrace.Begin(hashA) && StoryReplayJournal.StartsAnchor, "A fresh Lua scene establishes a replay origin");
            string token;
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__say|begin|origin line", out token);
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__say|end|" + token.Split('|')[0] + "|", out token);
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|" + Options, out token);
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|end|" + token.Split('|')[0] + "|2", out token);
            Check(StoryCommandTrace.RecordOutcome("random", "[99]", "7") == "7", "Live randomness is recorded unchanged");
            Check(StoryCommandTrace.RecordOutcome("condition", "gate", "True") == "True", "Live native conditions are recorded unchanged");
            PlayerStatManagerData.Instance.CurrentStoryScript = "bridge";
            StoryCommandTrace.Begin(hashB);
            Check(!StoryReplayJournal.StartsAnchor && StoryCommandTrace.CaptureChoiceHistory().Count == 1,
                "A script transition retains the origin and earlier decisions");
            PlayerStatManagerData.Instance.CurrentStoryScript = "origin";
            StoryCommandTrace.Begin(hashA);
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|" + Options, out token);
            var revisitTarget = StoryCommandTrace.Current;
            var route = StoryReplayJournal.CaptureVisits();
            var outcomes = StoryReplayJournal.CaptureOutcomes();
            var choices = StoryCommandTrace.CaptureChoiceHistory();
            Check(revisitTarget.Visit == 3 && route.Count == 3 && outcomes.Count == 2 && choices[0].Visit == 1,
                "Repeated script names are distinguished by their visit number");
            Check(StoryReplayJournal.Validate(route, outcomes, revisitTarget) == null, "The complete recorded route validates");
            StoryCommandTrace.ConfigureReplay(revisitTarget, choices, route, outcomes);
            PlayerStatManagerData.Instance.CurrentStoryScript = "origin";
            Check(StoryCommandTrace.Begin(hashA), "Cross-script replay starts from the origin rather than the target visit");
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__say|begin|origin line", out token);
            Check(token.EndsWith("|skip", StringComparison.Ordinal), "An earlier script's dialogue is skipped");
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|" + Options, out token);
            Check(token.EndsWith("|replay:2", StringComparison.Ordinal), "The original script's selection is replayed");
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|end|" + token.Split('|')[0] + "|2", out token);
            Check(StoryCommandTrace.RecordOutcome("random", "[99]", "98") == "7", "Recorded random results survive a different native RNG state");
            Check(StoryCommandTrace.RecordOutcome("condition", "gate", "False") == "True", "Recorded condition results keep the same route");
            PlayerStatManagerData.Instance.CurrentStoryScript = "bridge";
            Check(StoryCommandTrace.Begin(hashB), "The expected intermediate script is allowed");
            PlayerStatManagerData.Instance.CurrentStoryScript = "origin";
            Check(StoryCommandTrace.Begin(hashA), "The expected return to a script is allowed");
            var reachedBefore = LOMSaveProbePlugin.Reached;
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|" + Options, out token);
            Check(token.EndsWith("|continue", StringComparison.Ordinal) && LOMSaveProbePlugin.Reached == reachedBefore + 1,
                "Only the target script visit opens the saved choice");
            Check(StoryReplayJournal.CaptureVisits().Count == 3 && StoryCommandTrace.CaptureChoiceHistory().Count == 1,
                "The restored prefix remains available to subsequent branch saves");

            StoryCommandTrace.ConfigureReplay(revisitTarget, choices, route, outcomes);
            PlayerStatManagerData.Instance.CurrentStoryScript = "origin";
            Check(!StoryCommandTrace.Begin(hashB) && LOMSaveProbePlugin.AbortReason == "route_script_mismatch",
                "A script revision stops before executing its body");
            StoryCommandTrace.ConfigureReplay(revisitTarget, choices, route, outcomes);
            StoryCommandTrace.Begin(hashA);
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__say|begin|origin line", out token);
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|changed options", out token);
            Check(token.EndsWith("|abort", StringComparison.Ordinal), "Changed option content cannot select an old numeric index");
            StoryCommandTrace.ConfigureReplay(revisitTarget, choices, route, outcomes); StoryCommandTrace.Begin(hashA);
            var outcomeRejected = false;
            try { StoryCommandTrace.RecordOutcome("random", "[100]", "7"); } catch (InvalidOperationException) { outcomeRejected = true; }
            Check(outcomeRejected && LOMSaveProbePlugin.AbortReason == "route_outcome_mismatch", "Changed random arguments are rejected");
            var savedVisit = route[0].Visit; route[0].Visit = 2;
            Check(StoryReplayJournal.Validate(route, outcomes, revisitTarget) == "route_visit_invalid", "Malformed visit numbering is rejected");
            route[0].Visit = savedVisit;
            var savedIndex = outcomes[0].Index; outcomes[0].Index = 2;
            Check(StoryReplayJournal.Validate(route, outcomes, revisitTarget) == "route_outcome_order", "Malformed outcome ordering is rejected");
            outcomes[0].Index = savedIndex;

            StoryCommandTrace.ConfigureReplay(Target()); PlayerStatManagerData.Instance.CurrentStoryScript = Target().Script;
            StoryCommandTrace.Begin();
            Check(StoryReplayJournal.CaptureVisits() == null, "Legacy replay cannot fabricate unrecorded cross-script history");
            StoryCommandTrace.AbortActiveReplay("fixture_complete");
            PlayerStatManagerData.Instance.CurrentStoryScript = "temple";
            Check(StoryCommandTrace.Begin(hashA, "temple") && StoryReplayJournal.StartsAnchor
                && StoryReplayJournal.CaptureVisits().Count == 1 && StoryCommandTrace.CaptureChoiceHistory().Count == 0,
                "A new native entry after a legacy load rebases history on the same LuaManager");
            PlayerStatManagerData.Instance.CurrentStoryScript = "temple_continuation";
            StoryCommandTrace.Begin(hashB, "temple");
            Check(!StoryReplayJournal.StartsAnchor && StoryReplayJournal.CaptureVisits().Count == 2,
                "A native continuation preserves its entry and complete route");
            PlayerStatManagerData.Instance.CurrentStoryScript = "temple";
            StoryCommandTrace.Begin(hashA, "temple");
            Check(!StoryReplayJournal.StartsAnchor && StoryReplayJournal.CaptureVisits().Count == 3,
                "Re-entering the same native entry does not discard an already complete route");
            PlayerStatManagerData.Instance.CurrentStoryScript = "next_entry";
            StoryCommandTrace.Begin(hashB, "next_entry");
            Check(StoryReplayJournal.StartsAnchor && StoryReplayJournal.CaptureVisits().Count == 1,
                "A changed native story entry starts a fresh route without a scene reload");

            // Exercise the actual native coroutine wrapper and production trace
            // together: consume fate, save after dice, then replay its outcome.
            StoryCommandTrace.ConfigureReplay(null);
            PlayerStatManagerData.Instance.CurrentStoryScript = "dice_entry";
            StoryCommandTrace.Begin(hashA, "dice_entry");
            var fate = PlayerStatManagerData.Instance.Stats.Fate;
            fate.Value = 10; fate.AddCalls = 0;
            var dialog = new Mortal.Story.DiceMenuDialog();
            var dice = Dice(dialog, NativeRerolls(dialog));
            Check(dice.MoveNext() && fate.Value == 7, "Normal dice runs its native rerolls and animation");
            Check(!dice.MoveNext(), "Normal dice finishes its native coroutine");
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|" + Options, out token);
            var afterDice = StoryCommandTrace.Current;
            var diceRoute = StoryReplayJournal.CaptureVisits();
            var diceOutcomes = StoryReplayJournal.CaptureOutcomes();
            var diceChoices = StoryCommandTrace.CaptureChoiceHistory();
            Check(diceChoices.Count == 1 && diceChoices[0].Result == 2 && diceChoices[0].FatePointCost == 3,
                "Saved dice decisions retain final selection and all spent fate points");
            fate.Value = 10; fate.AddCalls = 0;
            StoryCommandTrace.ConfigureReplay(afterDice, diceChoices, diceRoute, diceOutcomes);
            StoryCommandTrace.Begin(hashA, "dice_entry");
            dialog._resultSelection = 0;
            dice = Dice(dialog, NativeRerolls(dialog));
            Check(!dice.MoveNext() && dialog.ResultSelection == 2 && fate.Value == 7 && fate.AddCalls == 1,
                "Replay restores result and cost once while skipping the native dice UI");
            Check(StoryCommandTrace.CaptureChoiceHistory()[0].FatePointCost == 3,
                "Saving after replay keeps the original cost for future loads");
            StoryCommandTrace.TryHandle("__LOMSAVEPROBE_COMMAND__choose|begin|" + Options, out token);
            Check(!StoryCommandTrace.IsReplayPending, "A replayed reroll reaches the later checkpoint normally");
            diceChoices[0].FatePointCost = 0; fate.Value = 10; fate.AddCalls = 0;
            StoryCommandTrace.ConfigureReplay(afterDice, diceChoices, diceRoute, diceOutcomes);
            StoryCommandTrace.Begin(hashA, "dice_entry");
            dice = Dice(dialog, NativeRerolls(dialog));
            Check(!dice.MoveNext() && fate.Value == 10 && fate.AddCalls == 0,
                "Legacy decisions without a recorded cost never invent a fate deduction");
            StoryCommandTrace.ConfigureReplay(afterDice, diceChoices, diceRoute, diceOutcomes);
            StoryCommandTrace.Begin(hashA, "dice_entry");
            var rejectedDice = false;
            try { int? result; StoryCommandTrace.BeginExternal("dice", "changed dice options", out result); }
            catch (InvalidOperationException) { rejectedDice = true; }
            Check(rejectedDice && !StoryCommandTrace.IsReplayPending && fate.Value == 10,
                "A rejected external dice command cannot continue into native UI or spend fate points");
            StoryCommandTrace.ConfigureReplay(null); Mortal.Story.LuaManager.Instance = new Mortal.Story.LuaManager();
            PlayerStatManagerData.Instance.CurrentStoryScript = "bounded"; StoryCommandTrace.Begin(hashA);
            for (var i = 0; i < 16385; i++) StoryCommandTrace.RecordOutcome("random", "[99]", "1");
            Check(StoryReplayJournal.CaptureVisits() == null, "Exceeding the outcome budget invalidates the route rather than truncating it");
            return _checks + " story replay and mission routing assertions passed";
        }
    }
}
