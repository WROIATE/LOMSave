using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class TrainingPointLimits
    {
        private static TrainingLimitSettings _settings = new TrainingLimitSettings();
        private static readonly Dictionary<TrainingLimitKind, StatConvertCollectionData> Native = new Dictionary<TrainingLimitKind, StatConvertCollectionData>();
        private static MartialPanel[] _martial = new MartialPanel[0];
        private static UpgradePanel[] _upgrades = new UpgradePanel[0];
        private static string FilePath { get { return Path.Combine(BepInEx.Paths.ConfigPath, "LOMSaveProbe.TrainingLimits.json"); } }

        internal static void Initialize()
        {
            try { _settings = LocalJsonFile.Read<TrainingLimitSettings>(FilePath); _settings.Migrate(); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("training_limits_load_failed", e); }
        }
        internal static string Name(TrainingLimitKind kind)
        { return kind == TrainingLimitKind.Martial ? "修炼回合上限" : kind == TrainingLimitKind.Alchemy ? "炼丹回合上限" : "锻造回合上限"; }
        internal static bool TryNative(TrainingLimitKind kind, out int value)
        {
            StatConvertCollectionData data;
            value = 0;
            if (!Native.TryGetValue(kind, out data) || data == null) return TrainingLimitFormulas.TryGet(kind, out value);
            value = Mathf.RoundToInt(data.GetSummary()); return true;
        }
        internal static string Display(TrainingLimitKind kind)
        { int value; return TryNative(kind, out value) ? value.ToString() : "未载入"; }
        internal static bool Unlimited(TrainingLimitKind kind) { return _settings.Get(kind); }
        internal static bool Toggle(TrainingLimitKind kind)
        {
            var previous = _settings.Get(kind);
            _settings.Set(kind, !previous);
            try { LocalJsonFile.Write(FilePath, _settings); return true; }
            catch (Exception e)
            { _settings.Set(kind, previous); LOMSaveProbePlugin.WriteError("training_limits_write_failed", e); return false; }
        }

        // Called only on opening F9. Live formulas, no fabricated fallback.
        internal static void Discover()
        {
            _martial = Resources.FindObjectsOfTypeAll<MartialPanel>();
            _upgrades = Resources.FindObjectsOfTypeAll<UpgradePanel>();
            foreach (var panel in _martial)
                Remember(TrainingLimitKind.Martial, EventInspector.Read<StatConvertCollectionData>(panel, "_maxDataConvert"));
            foreach (var panel in _upgrades)
            {
                TrainingLimitKind kind;
                if (UpgradeKind(EventInspector.Read<GameStat>(panel, "_pointData"), out kind))
                    Remember(kind, EventInspector.Read<StatConvertCollectionData>(panel, "_maxDataConvert"));
            }
            foreach (var data in Resources.FindObjectsOfTypeAll<StatConvertCollectionData>())
            {
                if (data.name == "修練次數_總和") Remember(TrainingLimitKind.Martial, data);
                else if (data.name == "煉丹次數_總和") Remember(TrainingLimitKind.Alchemy, data);
                else if (data.name == "鍛造次數_總和") Remember(TrainingLimitKind.Forge, data);
            }
        }
        internal static bool HasOpenPanel()
        {
            foreach (var panel in _martial) if (Open(panel)) return true;
            foreach (var panel in _upgrades) if (Open(panel)) return true;
            return false;
        }
        private static bool Open(Component panel)
        {
            if (panel == null || !panel.gameObject.activeInHierarchy) return false;
            var common = EventInspector.Read<CommonPanel>(panel, "_commonPanel");
            return common != null && common.gameObject.activeInHierarchy;
        }
        internal static void RefreshOpenPanels()
        {
            foreach (var panel in _martial) if (Open(panel)) AccessTools.Method(typeof(MartialPanel), "UpdateCurrentStatus").Invoke(panel, null);
            foreach (var panel in _upgrades) if (Open(panel))
            {
                AccessTools.Method(typeof(UpgradePanel), "UpdateStatus").Invoke(panel, null);
                AccessTools.Method(typeof(UpgradePanel), "UpdateButton").Invoke(panel, null);
            }
        }
        private static void Remember(TrainingLimitKind kind, StatConvertCollectionData data)
        { if (data != null) Native[kind] = data; }
        private static bool UpgradeKind(GameStat point, out TrainingLimitKind kind)
        {
            kind = TrainingLimitKind.Alchemy;
            if (point == null) return false;
            if (point.StatType == GameStatType.毒藥) return true;
            kind = TrainingLimitKind.Forge;
            return point.StatType == GameStatType.鍛造;
        }
        internal static int MartialRemaining(MartialPanel panel)
        {
            var points = EventInspector.Read<GameStat>(panel, "_martialPoint");
            return _settings.Remaining(TrainingLimitKind.Martial, EventInspector.Read<int>(panel, "_currentCount"), points == null ? 0 : points.FinalValue);
        }
        internal static int UpgradeRemaining(UpgradePanel panel)
        {
            var points = EventInspector.Read<GameStat>(panel, "_pointData");
            var remaining = EventInspector.Read<int>(panel, "_currentCount");
            TrainingLimitKind kind;
            return UpgradeKind(points, out kind) ? _settings.Remaining(kind, remaining, points.FinalValue) : remaining;
        }

        // Change only availability and spend-size checks. Native initialization,
        // decrement, money, level conditions and rewards are untouched. Keeping
        // the actual native counter also prevents refunds when toggling off.
        [HarmonyTranspiler, HarmonyPatch(typeof(MartialPanel), "CanUseLearnButton")]
        [HarmonyPatch(typeof(MartialPanel), "HandleLearnNextItem")]
        private static IEnumerable<CodeInstruction> MartialChecks(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, typeof(MartialPanel), nameof(MartialRemaining)); }
        [HarmonyTranspiler, HarmonyPatch(typeof(UpgradePanel), "UpgradeButtonClick")]
        [HarmonyPatch(typeof(UpgradePanel), "NextLevelButtonClick")]
        [HarmonyPatch(typeof(UpgradePanel), "UpdateButton")]
        [HarmonyPatch(typeof(UpgradePanel), "HandleUpgradeNextItem")]
        private static IEnumerable<CodeInstruction> UpgradeChecks(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, typeof(UpgradePanel), nameof(UpgradeRemaining)); }

        [HarmonyPostfix, HarmonyPatch(typeof(MartialPanel), "UpdateMartialPointInfo")]
        private static void MartialDisplay(StatConvertCollectionData ____maxDataConvert, Text ____maxCountText, int ____currentCount, int ____mode)
        { Remember(TrainingLimitKind.Martial, ____maxDataConvert); if (____mode == 0) Label(TrainingLimitKind.Martial, ____maxCountText, ____currentCount, ____maxDataConvert); }
        [HarmonyPostfix, HarmonyPatch(typeof(UpgradePanel), "UpdateStatus")]
        private static void UpgradeDisplay(StatConvertCollectionData ____maxDataConvert, GameStat ____pointData, Text ____maxCountText, int ____currentCount)
        {
            TrainingLimitKind kind;
            if (UpgradeKind(____pointData, out kind)) { Remember(kind, ____maxDataConvert); Label(kind, ____maxCountText, ____currentCount, ____maxDataConvert); }
        }
        private static void Label(TrainingLimitKind kind, Text text, int remaining, StatConvertCollectionData data)
        { if (text != null && data != null) text.text = Unlimited(kind) ? "不限回合用量" : Math.Max(0, remaining) + "/" + Mathf.RoundToInt(data.GetSummary()); }

        internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, Type owner, string helper)
        {
            var code = new List<CodeInstruction>();
            var replacements = 0;
            foreach (var instruction in instructions)
            {
                var next = new CodeInstruction(instruction);
                var field = next.operand as FieldInfo;
                if (next.opcode == OpCodes.Ldfld && field != null && field.DeclaringType == owner && field.Name == "_currentCount")
                { next.opcode = OpCodes.Call; next.operand = AccessTools.Method(typeof(TrainingPointLimits), helper); replacements++; }
                code.Add(next);
            }
            if (replacements != 1) throw new InvalidOperationException("Expected one training budget read in " + owner.Name + ", found " + replacements);
            return code;
        }
    }
}
