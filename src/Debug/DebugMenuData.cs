using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mortal.Core;

namespace LOMSave.Probe
{
    internal static class DebugMenuData
    {
        internal static bool IsEditing { get; private set; }
        internal static void AddStat(GameStat stat, int delta)
        {
            if (stat == null) return;
            var target = Adjust(stat.Value, delta, stat.Min, stat.Max);
            var previous = IsEditing;
            IsEditing = true;
            try { stat.AddValue(target - stat.Value, true); }
            finally { IsEditing = previous; }
        }
        internal static List<GameStat> Stats(PlayerStatData data)
        {
            return data == null || data.List == null ? new List<GameStat>() : data.List
                .Where(x => x != null).Distinct().OrderBy(x => Priority(x.StatType)).ThenBy(x => (int)x.StatType).ToList();
        }

        private static int Priority(GameStatType type)
        {
            switch (type)
            {
                case GameStatType.變心: return 0;
                case GameStatType.毒藥: return 1;
                case GameStatType.武學點數: return 2;
                case GameStatType.鍛造: return 3;
                case GameStatType.全毒藥點數: return 4;
                case GameStatType.全武學點數: return 5;
                case GameStatType.全鍛造點數: return 6;
                default: return 7;
            }
        }

        internal static string StatName(GameStat stat)
        {
            switch (stat.StatType)
            {
                case GameStatType.變心: return "变心";
                case GameStatType.毒藥: return "炼丹点数";
                case GameStatType.武學點數: return "武学点数";
                case GameStatType.鍛造: return "锻造点数";
                case GameStatType.全毒藥點數: return "累计炼丹点数";
                case GameStatType.全武學點數: return "累计武学点数";
                case GameStatType.全鍛造點數: return "累计锻造点数";
                case GameStatType.額外行動次數_1: return "额外行动1";
                case GameStatType.額外行動次數_2: return "额外行动2";
                case GameStatType.額外行動次數_3: return "额外行动3";
                default: return OptionImpactLocalization.GameStat(stat.StatType.ToString());
            }
        }

        internal static List<FlagData> Flags(MissionManagerData manager)
        {
            return manager == null || manager.FlagsCollection == null ? new List<FlagData>() : manager.FlagsCollection
                .Where(x => x != null && x.List != null).SelectMany(x => x.List).Where(x => x != null)
                .GroupBy(x => x.name, StringComparer.Ordinal).Select(x => x.First()).OrderBy(x => x.name, StringComparer.Ordinal).ToList();
        }

        internal static string FlagName(FlagData flag)
        {
            string localized = null;
            try { localized = LocalizationManager.Instance.LocaleResolver.GetString(flag.GetNameKey()); }
            catch { }
            return CodeNameMapping.Resolve("flags", flag.name, flag.DevNote, localized);
        }

        internal static string FlagTooltip(FlagData flag)
        {
            if (flag == null) return "";
            return FlagName(flag) + "\n代码：" + flag.name;
        }

        internal static bool Matches(string query, params string[] text)
        {
            query = (query ?? "").Trim();
            return query.Length == 0 || text.Any(x => x != null && x.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool TryValue(string text, int min, int max, out int value, out string error)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            { error = "请输入有效整数。"; return false; }
            if (value < min || value > max)
            { error = "请输入 " + min + " 至 " + max + " 之间的整数。"; return false; }
            error = null;
            return true;
        }

        // SetValue raises the native UI notifications and keeps the native limits.
        // Editing a stock and its lifetime total are deliberately independent.
        internal static bool SetStat(GameStat stat, string text, out string error)
        {
            int value;
            if (stat == null) { error = "属性已失效，请重新打开菜单。"; return false; }
            if (!TryValue(text, stat.Min, stat.Max, out value, out error)) return false;
            var previous = IsEditing;
            IsEditing = true;
            try { stat.SetValue(value); }
            finally { IsEditing = previous; }
            return true;
        }

        internal static bool SetFlag(FlagData flag, string text, out string error)
        {
            int value;
            if (flag == null) { error = "旗标已失效，请重新打开菜单。"; return false; }
            if (!TryValue(text, int.MinValue, int.MaxValue, out value, out error)) return false;
            flag.State = value;
            return true;
        }

        internal static int Adjust(int value, int delta, int min, int max)
        { return (int)Math.Max(min, Math.Min(max, (long)value + delta)); }
    }
}
