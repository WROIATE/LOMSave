using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    public sealed class HarmonyTranspiler : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public static class AccessTools { public static MethodInfo Method(Type type, string method) { return type.GetMethod(method, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } }
    public sealed class CodeInstruction
    {
        public OpCode opcode; public object operand;
        public List<string> labels = new List<string>(), blocks = new List<string>();
        public CodeInstruction(OpCode code, object value = null) { opcode = code; operand = value; }
        public CodeInstruction(CodeInstruction value) { opcode = value.opcode; operand = value.operand; labels.AddRange(value.labels); blocks.AddRange(value.blocks); }
    }
}
namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy = true; }
    public class Component { public GameObject gameObject = new GameObject(); }
    public static class Mathf { public static int RoundToInt(float value) { return (int)Math.Round(value, MidpointRounding.ToEven); }
        public static float InverseLerp(float a, float b, float v) { return a == b ? 0 : Math.Max(0, Math.Min(1, (v-a)/(b-a))); } }
    public enum WeightedMode { None }
    public enum WrapMode { Default }
    public struct Keyframe { public float time,value; public WeightedMode weightedMode;
        public Keyframe(float t,float v,float a,float b,float c,float d) { time=t;value=v;weightedMode=WeightedMode.None; } }
    // Test adapter for linear curves; production uses Unity's native evaluator.
    public class AnimationCurve { private Keyframe[] keys; public WrapMode preWrapMode,postWrapMode; public AnimationCurve(Keyframe[] k) { keys=k; }
        public float Evaluate(float t) { return keys[0].value+(keys[keys.Length-1].value-keys[0].value)*t; } }
    public static class Resources
    {
        public static object[] Objects = new object[0]; public static int FindCount;
        public static T[] FindObjectsOfTypeAll<T>() { FindCount++; return Objects.OfType<T>().ToArray(); }
    }
}
namespace UnityEngine.UI { public class Text { public string text; } }
namespace BepInEx { public static class Paths { public static string ConfigPath; } }
namespace Mortal.Core
{
    public enum GameStatType { 毒藥, 鍛造, 銀兩 }
    public class GameStat { public GameStatType StatType; public int FinalValue,Min,Max=100; }
    public class PlayerStatData { public List<GameStat> List=new List<GameStat>(); public GameStat Get(GameStatType type) { return List.FirstOrDefault(x=>x.StatType==type); } }
    public class PlayerStatManagerData { public static PlayerStatManagerData Instance; public PlayerStatData Stats; }
    public class StatConvertCollectionData { public string name; public float Summary; public float GetSummary() { return Summary; } }
    public class CommonPanel : UnityEngine.Component { }
    public class MartialPanel : UnityEngine.Component
    {
        public StatConvertCollectionData _maxDataConvert; public GameStat _martialPoint;
        public int _currentCount, RefreshCount; public CommonPanel _commonPanel = new CommonPanel();
        public void UpdateCurrentStatus() { RefreshCount++; }
    }
    public class UpgradePanel : UnityEngine.Component
    {
        public StatConvertCollectionData _maxDataConvert; public GameStat _pointData;
        public int _currentCount, RefreshCount, ButtonRefreshCount; public CommonPanel _commonPanel = new CommonPanel();
        public void UpdateStatus() { RefreshCount++; } public void UpdateButton() { ButtonRefreshCount++; }
    }
}
namespace LOMSave.Probe
{
    internal static class StoryEventIndex { internal static StoryEventIndexData Data; }
    internal static class EventInspector { internal static T Read<T>(object instance, string field) { return (T)instance.GetType().GetField(field).GetValue(instance); } }
    internal static class LOMSaveProbePlugin { internal static int Errors; internal static void WriteError(string name, Exception error) { Errors++; } }
    public static class TrainingLimitTests
    {
        private static int _checks;
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
        public static string Run(string directory, string indexFile)
        {
            Directory.CreateDirectory(directory); BepInEx.Paths.ConfigPath = directory;
            var settings = new TrainingLimitSettings { Martial = 200, Alchemy = 10, Forge = -1 };
            settings.Migrate();
            Check(settings.Version == 2 && settings.Martial == null && !settings.Get(TrainingLimitKind.Martial), "Legacy caps safely return to native, never silently unlimited");
            Check(settings.Remaining(TrainingLimitKind.Martial, 24, 400) == 24, "Default keeps native remaining allowance");
            settings.Set(TrainingLimitKind.Martial, true);
            Check(settings.Remaining(TrainingLimitKind.Martial, -100, 30000) == 30000, "Unlimited uses available stock, without an arbitrary 9999 cap");
            Check(settings.Remaining(TrainingLimitKind.Martial, 24, 0) == 0, "Unlimited cannot create points");
            Check(settings.Remaining(TrainingLimitKind.Alchemy, 4, 100) == 4, "Activity toggles are independent");
            settings.Set(TrainingLimitKind.Martial, false);
            Check(settings.Remaining(TrainingLimitKind.Martial, -100, 500) == 0, "Turning off cannot refund an overspent native budget");
            Check(settings.Remaining(TrainingLimitKind.Martial, 5, 500) == 5, "Turning off retains the unused native allowance");

            TrainingPointLimits.Initialize();
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial) == "未载入", "No fake number before the native formula exists");
            var index = Newtonsoft.Json.JsonConvert.DeserializeObject<StoryEventIndexData>(File.ReadAllText(indexFile));
            Check(index.TrainingLimits.Count == 3 && index.TrainingLimits.Values.All(x=>x.Curves.Length==2), "All three installed native formulas exported");
            var formulaStats = new Mortal.Core.PlayerStatData();
            foreach (var id in index.TrainingLimits.Values.SelectMany(x=>x.Curves).Select(x=>x.Stat).Distinct())
                formulaStats.List.Add(new Mortal.Core.GameStat { StatType=(Mortal.Core.GameStatType)id, Min=0,Max=100,FinalValue=50 });
            Mortal.Core.PlayerStatManagerData.Instance = new Mortal.Core.PlayerStatManagerData { Stats=formulaStats };
            StoryEventIndex.Data=index;
            foreach (TrainingLimitKind kind in Enum.GetValues(typeof(TrainingLimitKind)))
                Check(TrainingPointLimits.Display(kind)=="16", "Cold native linear curve, constant component and ties-to-even: "+kind);
            foreach(var stat in formulaStats.List) stat.FinalValue=100;
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial)=="25", "Cached formula reads live stats after edits/load");
            foreach(var stat in formulaStats.List) stat.FinalValue=999;
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial)=="25", "InverseLerp keeps native clamped range");
            foreach(var stat in formulaStats.List) stat.Min=stat.Max;
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial)=="8", "Equal bounds retain native zero interpolation");
            Mortal.Core.PlayerStatManagerData.Instance.Stats=new Mortal.Core.PlayerStatData();
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial)=="未载入", "Missing live stat refuses stale formula binding");
            StoryEventIndex.Data=null;
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial)=="未载入", "Unvalidated or unavailable index cannot supply values");
            var shared = new Mortal.Core.StatConvertCollectionData { Summary = 25 };
            var martialData = new Mortal.Core.StatConvertCollectionData { Summary = 20 };
            var martial = new Mortal.Core.MartialPanel { _maxDataConvert = martialData, _martialPoint = new Mortal.Core.GameStat { FinalValue = 200 }, _currentCount = 20 };
            var alchemy = new Mortal.Core.UpgradePanel { _maxDataConvert = shared, _pointData = new Mortal.Core.GameStat { StatType = Mortal.Core.GameStatType.毒藥, FinalValue = 100 }, _currentCount = 25 };
            var forge = new Mortal.Core.UpgradePanel { _maxDataConvert = shared, _pointData = new Mortal.Core.GameStat { StatType = Mortal.Core.GameStatType.鍛造, FinalValue = 300 }, _currentCount = 25 };
            UnityEngine.Resources.Objects = new object[] { martial, alchemy, forge }; TrainingPointLimits.Discover();
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial) == "20" && TrainingPointLimits.Display(TrainingLimitKind.Alchemy) == "25" && TrainingPointLimits.Display(TrainingLimitKind.Forge) == "25", "All three rows show current system values");
            StoryEventIndex.Data=index; Mortal.Core.PlayerStatManagerData.Instance.Stats=formulaStats;
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial)=="20", "Loaded native converter takes priority over index");
            StoryEventIndex.Data=null;
            Check(TrainingPointLimits.Toggle(TrainingLimitKind.Martial) && TrainingPointLimits.MartialRemaining(martial) == 200, "Martial toggle removes round cap");
            Check(TrainingPointLimits.Toggle(TrainingLimitKind.Alchemy) && TrainingPointLimits.UpgradeRemaining(alchemy) == 100 && TrainingPointLimits.UpgradeRemaining(forge) == 25, "Alchemy toggle does not alter forge even with a shared converter");
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial) == "20" && martialData.Summary == 20 && martial._currentCount == 20 && martial._martialPoint.FinalValue == 200, "Toggle changes neither the displayed system formula, stock nor native budget");
            martialData.Summary = 40;
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial) == "40", "System value follows current stats while unlimited is on");
            martial._currentCount = -30; martial._martialPoint.FinalValue = 150;
            Check(TrainingPointLimits.MartialRemaining(martial) == 150, "Spent stock is still authoritative");
            TrainingPointLimits.Toggle(TrainingLimitKind.Martial);
            Check(TrainingPointLimits.MartialRemaining(martial) == 0 && martial._currentCount == -30, "Switch off after overspending leaves native counter unchanged");
            TrainingPointLimits.Initialize();
            Check(!TrainingPointLimits.Unlimited(TrainingLimitKind.Martial) && TrainingPointLimits.Unlimited(TrainingLimitKind.Alchemy) && !TrainingPointLimits.Unlimited(TrainingLimitKind.Forge), "Toggle states persist independently across reloads");
            Check(File.Exists(Path.Combine(directory, "LOMSaveProbe.TrainingLimits.json.bak")), "Atomic setting writes preserve backup");
            var count = UnityEngine.Resources.FindCount;
            for (var i = 0; i < 100; i++) { TrainingPointLimits.Display(TrainingLimitKind.Martial); TrainingPointLimits.UpgradeRemaining(alchemy); }
            Check(count == UnityEngine.Resources.FindCount, "Display and gameplay do not rescan resources");
            Check(TrainingPointLimits.HasOpenPanel(), "Native training panel can admit F9");
            martial._commonPanel.gameObject.activeInHierarchy = false; alchemy.gameObject.activeInHierarchy = false; forge.gameObject.activeInHierarchy = false;
            Check(!TrainingPointLimits.HasOpenPanel(), "Hidden native training components cannot bypass F9 guards");
            martial._commonPanel.gameObject.activeInHierarchy = true;
            TrainingPointLimits.RefreshOpenPanels();
            Check(martial.RefreshCount == 1 && alchemy.RefreshCount == 0, "Closing F9 refreshes only visible native menus");
            var unrelated = new Mortal.Core.UpgradePanel { _pointData = new Mortal.Core.GameStat { StatType = Mortal.Core.GameStatType.銀兩 }, _currentCount = 7 };
            Check(TrainingPointLimits.UpgradeRemaining(unrelated) == 7, "Unrelated upgrade activity remains untouched");
            var invalid = Path.Combine(directory, "not-a-directory"); File.WriteAllText(invalid, "fixture"); BepInEx.Paths.ConfigPath = invalid;
            Check(!TrainingPointLimits.Toggle(TrainingLimitKind.Alchemy) && TrainingPointLimits.Unlimited(TrainingLimitKind.Alchemy) && LOMSaveProbePlugin.Errors == 1, "Write failure rolls toggle back");
            BepInEx.Paths.ConfigPath = directory;
            var fallback = new Mortal.Core.StatConvertCollectionData { name = "煉丹次數_總和", Summary = 73 };
            UnityEngine.Resources.Objects = new object[] { fallback }; TrainingPointLimits.Discover();
            Check(TrainingPointLimits.Display(TrainingLimitKind.Alchemy) == "73", "Retained native formula can be read without a loaded panel");
            foreach (var type in new[] { typeof(Mortal.Core.MartialPanel), typeof(Mortal.Core.UpgradePanel) })
            {
                var field = type.GetField("_currentCount");
                var read = new HarmonyLib.CodeInstruction(OpCodes.Ldfld, field); read.labels.Add("branch"); read.blocks.Add("exception");
                var spend = new HarmonyLib.CodeInstruction(OpCodes.Stfld, field);
                var changed = TrainingPointLimits.Replace(new[] { read, spend }, type, type == typeof(Mortal.Core.MartialPanel) ? "MartialRemaining" : "UpgradeRemaining").ToArray();
                Check(changed[0].opcode == OpCodes.Call && changed[1].opcode == OpCodes.Stfld, "Only the availability read changes, spending is untouched");
                Check(changed[0].labels.Single() == "branch" && changed[0].blocks.Single() == "exception", "Transpiler preserves control flow metadata");
                Check(read.opcode == OpCodes.Ldfld, "Original IL remains unmodified");
                try { TrainingPointLimits.Replace(new[] { spend }, type, "MartialRemaining"); throw new Exception("Expected missing pattern rejection"); } catch (InvalidOperationException) { _checks++; }
                try { TrainingPointLimits.Replace(new[] { read, read }, type, "MartialRemaining"); throw new Exception("Expected ambiguity rejection"); } catch (InvalidOperationException) { _checks++; }
            }
            return _checks + " training toggle assertions passed";
        }
    }
}
