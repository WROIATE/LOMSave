using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mortal.Core;

// Exercise the production command replay and mission prefix without native Unity.
namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
}
namespace Mortal.Core
{
    public class MissionManagerData { }
    public class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public string CurrentStoryScript;
    }
}
namespace Mortal.Story
{
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
        internal static int Reached, Aborted;
        internal static string AbortReason;
        internal static string Sha256(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        internal static void WriteEvent(string name, object payload) { }
        internal static void WriteError(string name, Exception error, object payload) { throw error; }
        internal static void ReplayTargetReached(StoryCommandCheckpoint checkpoint)
        { Reached++; ReplayRuntime.IsReplaying = false; }
        internal static void ReplayAborted(string reason, StoryCommandCheckpoint target)
        { Aborted++; AbortReason = reason; ReplayRuntime.Reset(reason); }
    }
    public static class StoryReplayTests
    {
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
            return _checks + " story replay and mission routing assertions passed";
        }
    }
}
