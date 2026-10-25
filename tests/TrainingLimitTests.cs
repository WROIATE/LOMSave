using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    public sealed class HarmonyTranspiler : Attribute { }
    public enum MethodType { Enumerator }
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, MethodType kind) { }
    }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string method) { return type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
    }
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
    public static class Mathf { public static int RoundToInt(float value) { return (int)Math.Round(value, MidpointRounding.ToEven); } }
    public static class Resources
    {
        public static object[] Objects = new object[0];
        public static int FindCount;
        public static T[] FindObjectsOfTypeAll<T>() { FindCount++; return Objects.OfType<T>().ToArray(); }
    }
}
namespace BepInEx { public static class Paths { public static string ConfigPath; } }
namespace Mortal.Core
{
    public enum GameStatType { 毒藥, 鍛造, 銀兩 }
    public class GameStat { public GameStatType StatType; public int Value; }
    public class StatConvertCollectionData { public float Summary; public float GetSummary() { return Summary; } }
    public class MartialPanel { public StatConvertCollectionData _maxDataConvert; }
    public class UpgradePanel { public StatConvertCollectionData _maxDataConvert; public GameStat _pointData; }
}
namespace LOMSave.Probe
{
    internal static class EventInspector
    {
        internal static T Read<T>(object instance, string field) { return (T)instance.GetType().GetField(field).GetValue(instance); }
    }
    internal static class LOMSaveProbePlugin { internal static int Errors; internal static void WriteError(string name, Exception error) { Errors++; } }
    public static class TrainingLimitTests
    {
        private static int _checks;
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
        public static string Run(string directory)
        {
            Directory.CreateDirectory(directory); BepInEx.Paths.ConfigPath = directory;
            var settings = new TrainingLimitSettings();
            TrainingPointLimits.Initialize();
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial) == "自动" && TrainingPointLimits.Change(TrainingLimitKind.Martial, 5)
                && TrainingPointLimits.Display(TrainingLimitKind.Martial) == "5", "Cold map can configure a cap before Story UI assets load");
            TrainingPointLimits.Restore(TrainingLimitKind.Martial);
            Check(settings.Resolve(TrainingLimitKind.Martial, 24.5f) == 24.5f, "Default keeps native formula and rounding untouched");
            settings.Set(TrainingLimitKind.Martial, 80);
            Check(settings.Resolve(TrainingLimitKind.Martial, 20) == 80 && settings.Resolve(TrainingLimitKind.Alchemy, 25) == 25, "Limits are independent by activity");
            Check(settings.Adjusted(TrainingLimitKind.Martial, 20, int.MaxValue) == 9999 && settings.Adjusted(TrainingLimitKind.Martial, 20, int.MinValue) == 1, "Quick edits saturate without integer overflow");
            settings.Martial = -1;
            Check(!settings.Get(TrainingLimitKind.Martial).HasValue && settings.Resolve(TrainingLimitKind.Martial, 31) == 31, "Invalid persisted limit falls back to native");
            settings.Martial = 10000;
            Check(!settings.Get(TrainingLimitKind.Martial).HasValue, "Excessive persisted setting is ignored");
            try { settings.Set(TrainingLimitKind.Forge, 0); throw new Exception("Expected bounds rejection"); }
            catch (ArgumentOutOfRangeException) { }

            // Alchemy and forge deliberately share the same native converter.
            // Replacing its GetSummary globally would incorrectly couple them.
            var shared = new Mortal.Core.StatConvertCollectionData { Summary = 25 };
            var martialData = new Mortal.Core.StatConvertCollectionData { Summary = 20 };
            var martial = new Mortal.Core.MartialPanel { _maxDataConvert = martialData };
            var alchemy = new Mortal.Core.UpgradePanel { _maxDataConvert = shared, _pointData = new Mortal.Core.GameStat { StatType = Mortal.Core.GameStatType.毒藥, Value = 100 } };
            var forge = new Mortal.Core.UpgradePanel { _maxDataConvert = shared, _pointData = new Mortal.Core.GameStat { StatType = Mortal.Core.GameStatType.鍛造, Value = 200 } };
            UnityEngine.Resources.Objects = new object[] { martial, alchemy, forge };
            TrainingPointLimits.Initialize(); TrainingPointLimits.Discover();
            Check(TrainingPointLimits.Display(TrainingLimitKind.Martial) == "自动"
                && TrainingPointLimits.Display(TrainingLimitKind.Alchemy) == "自动"
                && TrainingPointLimits.Display(TrainingLimitKind.Forge) == "自动", "All native modes display automatic even after native limits are discovered");
            Check(TrainingPointLimits.Change(TrainingLimitKind.Martial, 5) && TrainingPointLimits.MartialSummary(martial) == 25, "F9 plus overrides next martial entry");
            Check(TrainingPointLimits.Change(TrainingLimitKind.Alchemy, 5) && TrainingPointLimits.UpgradeSummary(alchemy) == 30, "Alchemy changes independently");
            Check(TrainingPointLimits.Change(TrainingLimitKind.Forge, -5) && TrainingPointLimits.UpgradeSummary(forge) == 20, "Forge minus is independent despite shared native asset");
            Check(shared.GetSummary() == 25 && martialData.GetSummary() == 20 && alchemy._pointData.Value == 100 && forge._pointData.Value == 200, "Changing caps never edits native converters or point stocks");
            TrainingPointLimits.Initialize();
            Check(TrainingPointLimits.MartialSummary(martial) == 25 && TrainingPointLimits.UpgradeSummary(alchemy) == 30 && TrainingPointLimits.UpgradeSummary(forge) == 20, "All three settings survive disk reload");
            Check(File.Exists(Path.Combine(directory, "LOMSaveProbe.TrainingLimits.json.bak")), "Atomic edits keep previous configuration backup");
            Check(TrainingPointLimits.Restore(TrainingLimitKind.Martial) && TrainingPointLimits.IsDefault(TrainingLimitKind.Martial), "Individual native reset is available");
            martialData.Summary = 40;
            Check(TrainingPointLimits.MartialSummary(martial) == 40 && TrainingPointLimits.UpgradeSummary(alchemy) == 30, "Restored native cap follows future stat changes while other overrides stay fixed");
            var finds = UnityEngine.Resources.FindCount;
            for (var i = 0; i < 100; i++) TrainingPointLimits.MartialSummary(martial);
            Check(UnityEngine.Resources.FindCount == finds, "Limit evaluation does not rescan resources");
            var unrelated = new Mortal.Core.UpgradePanel { _maxDataConvert = shared, _pointData = new Mortal.Core.GameStat { StatType = Mortal.Core.GameStatType.銀兩 } };
            Check(TrainingPointLimits.UpgradeSummary(unrelated) == 25, "Unrecognized activity stays native");
            var invalidDirectory = Path.Combine(directory, "not-a-directory"); File.WriteAllText(invalidDirectory, "fixture");
            BepInEx.Paths.ConfigPath = invalidDirectory;
            Check(!TrainingPointLimits.Change(TrainingLimitKind.Alchemy, 5) && TrainingPointLimits.UpgradeSummary(alchemy) == 30 && LOMSaveProbePlugin.Errors == 1, "Failed persistence rolls back the in-memory edit");
            BepInEx.Paths.ConfigPath = directory;

            foreach (var type in new[] { typeof(Mortal.Core.MartialPanel), typeof(Mortal.Core.UpgradePanel) })
            {
                var field = type.GetField("_maxDataConvert");
                var native = typeof(Mortal.Core.StatConvertCollectionData).GetMethod("GetSummary");
                var load = new HarmonyLib.CodeInstruction(OpCodes.Ldfld, field); load.labels.Add("branch"); load.blocks.Add("exception");
                var call = new HarmonyLib.CodeInstruction(OpCodes.Callvirt, native);
                var spending = new HarmonyLib.CodeInstruction(OpCodes.Ldc_I4, 123);
                var original = new[] { load, call, spending };
                var changed = TrainingPointLimits.Replace(original, type, type == typeof(Mortal.Core.MartialPanel) ? "MartialSummary" : "UpgradeSummary").ToArray();
                Check(changed[0].opcode == OpCodes.Nop && changed[1].opcode == OpCodes.Call && changed[2].opcode == spending.opcode && (int)changed[2].operand == 123, "Transpiler changes only cap lookup, preserving native spending operations");
                Check(changed[0].labels.Single() == "branch" && changed[0].blocks.Single() == "exception", "Transpiler keeps branch and exception metadata");
                Check(load.opcode == OpCodes.Ldfld && call.opcode == OpCodes.Callvirt, "Transpiler does not mutate original instructions");
                try { TrainingPointLimits.Replace(new[] { spending }, type, "MartialSummary"); throw new Exception("Expected missing pattern rejection"); }
                catch (InvalidOperationException) { _checks++; }
                try { TrainingPointLimits.Replace(original.Concat(original), type, "MartialSummary"); throw new Exception("Expected ambiguous pattern rejection"); }
                catch (InvalidOperationException) { _checks++; }
            }
            return _checks + " training point limit assertions passed";
        }
    }
}
