using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class TrainingPointLimits
    {
        private static TrainingLimitSettings _settings = new TrainingLimitSettings();
        private static readonly Dictionary<TrainingLimitKind, StatConvertCollectionData> Native = new Dictionary<TrainingLimitKind, StatConvertCollectionData>();
        private static string FilePath { get { return Path.Combine(BepInEx.Paths.ConfigPath, "LOMSaveProbe.TrainingLimits.json"); } }

        internal static void Initialize()
        {
            try { _settings = LocalJsonFile.Read<TrainingLimitSettings>(FilePath); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("training_limits_load_failed", e); }
        }
        internal static string Name(TrainingLimitKind kind)
        { return kind == TrainingLimitKind.Martial ? "修炼回合上限" : kind == TrainingLimitKind.Alchemy ? "炼丹回合上限" : "锻造回合上限"; }
        internal static bool TryNative(TrainingLimitKind kind, out int value)
        {
            StatConvertCollectionData data;
            value = 0;
            if (!Native.TryGetValue(kind, out data) || data == null) return false;
            value = Mathf.RoundToInt(data.GetSummary()); return true;
        }
        internal static string Display(TrainingLimitKind kind)
        {
            var setting = _settings.Get(kind);
            return setting.HasValue ? setting.Value.ToString() : "自动";
        }
        internal static bool IsDefault(TrainingLimitKind kind) { return !_settings.Get(kind).HasValue; }
        internal static bool Change(TrainingLimitKind kind, int delta)
        {
            int native;
            // Story UI assets may not exist yet on a cold Free load. An
            // explicit first edit still works (0 + 5 -> a fixed cap of 5).
            TryNative(kind, out native);
            return Save(kind, _settings.Adjusted(kind, native, delta));
        }
        internal static bool Restore(TrainingLimitKind kind) { return Save(kind, null); }
        private static bool Save(TrainingLimitKind kind, int? value)
        {
            var previous = _settings.Get(kind);
            if (previous == value) return true;
            _settings.Set(kind, value);
            try { LocalJsonFile.Write(FilePath, _settings); return true; }
            catch (Exception e)
            { _settings.Set(kind, previous); LOMSaveProbePlugin.WriteError("training_limits_write_failed", e); return false; }
        }

        // Called only when F9 opens, never by an idle-frame resource scan.
        // Native converters remain unmodified, including shared asset instances.
        internal static void Discover()
        {
            foreach (var panel in Resources.FindObjectsOfTypeAll<MartialPanel>())
                Remember(TrainingLimitKind.Martial, EventInspector.Read<StatConvertCollectionData>(panel, "_maxDataConvert"));
            foreach (var panel in Resources.FindObjectsOfTypeAll<UpgradePanel>())
            {
                TrainingLimitKind kind;
                if (UpgradeKind(EventInspector.Read<GameStat>(panel, "_pointData"), out kind))
                    Remember(kind, EventInspector.Read<StatConvertCollectionData>(panel, "_maxDataConvert"));
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
        internal static float MartialSummary(MartialPanel panel)
        {
            var data = EventInspector.Read<StatConvertCollectionData>(panel, "_maxDataConvert");
            Remember(TrainingLimitKind.Martial, data);
            return _settings.Resolve(TrainingLimitKind.Martial, data.GetSummary());
        }
        internal static float UpgradeSummary(UpgradePanel panel)
        {
            var data = EventInspector.Read<StatConvertCollectionData>(panel, "_maxDataConvert");
            TrainingLimitKind kind;
            if (!UpgradeKind(EventInspector.Read<GameStat>(panel, "_pointData"), out kind)) return data.GetSummary();
            Remember(kind, data);
            return _settings.Resolve(kind, data.GetSummary());
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(MartialPanel), "Open", MethodType.Enumerator)]
        private static IEnumerable<CodeInstruction> MartialOpen(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, typeof(MartialPanel), nameof(MartialSummary)); }
        [HarmonyTranspiler, HarmonyPatch(typeof(MartialPanel), "UpdateMartialPointInfo")]
        private static IEnumerable<CodeInstruction> MartialDisplay(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, typeof(MartialPanel), nameof(MartialSummary)); }
        [HarmonyTranspiler, HarmonyPatch(typeof(UpgradePanel), "Open", MethodType.Enumerator)]
        private static IEnumerable<CodeInstruction> UpgradeOpen(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, typeof(UpgradePanel), nameof(UpgradeSummary)); }
        [HarmonyTranspiler, HarmonyPatch(typeof(UpgradePanel), "UpdateStatus")]
        private static IEnumerable<CodeInstruction> UpgradeDisplay(IEnumerable<CodeInstruction> instructions)
        { return Replace(instructions, typeof(UpgradePanel), nameof(UpgradeSummary)); }

        internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, Type owner, string helper)
        {
            var code = new List<CodeInstruction>();
            foreach (var instruction in instructions) code.Add(new CodeInstruction(instruction));
            var replacements = 0;
            for (var i = 0; i + 1 < code.Count; i++)
            {
                var field = code[i].operand as FieldInfo;
                var call = code[i + 1].operand as MethodInfo;
                if (code[i].opcode != OpCodes.Ldfld || field == null || field.DeclaringType != owner || field.Name != "_maxDataConvert"
                    || code[i + 1].opcode != OpCodes.Callvirt || call == null || call.DeclaringType != typeof(StatConvertCollectionData)
                    || call.Name != "GetSummary") continue;
                // The panel is already on the evaluation stack. Keep that owner
                // instead of loading its converter, and use it to select a limit.
                code[i].opcode = OpCodes.Nop; code[i].operand = null;
                code[i + 1].opcode = OpCodes.Call;
                code[i + 1].operand = AccessTools.Method(typeof(TrainingPointLimits), helper);
                replacements++;
            }
            if (replacements != 1) throw new InvalidOperationException("Expected one training limit calculation in " + owner.Name + ", found " + replacements);
            return code;
        }
    }
}
