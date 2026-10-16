using HarmonyLib;
using Mortal.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LOMSave.Probe
{
    // Never call ConditionResultData.Execute, SwitchResultData.Execute or a
    // random StatValueReference. All preview checks must leave gameplay intact.
    internal static class EventConditions
    {
        internal static T Field<T>(object source, string name)
        {
            if (source == null) return default(T);
            return Traverse.Create(source).Field(name).GetValue<T>();
        }

        internal static bool? Check(ConditionResultItem condition, int depth = 0)
        {
            if (condition == null || depth > 16) return null;
            var items = Field<List<StatCompareItem>>(condition, "_items");
            if (items == null) return null;
            var isAnd = Field<LogicOperatorType>(condition, "_logicOp") == LogicOperatorType.AND;
            var unknown = false;
            foreach (var item in items)
            {
                var a = Value(Field<StatGroupVariable>(item, "_value1"), depth + 1);
                var b = Value(Field<StatGroupVariable>(item, "_value2"), depth + 1);
                if (!a.HasValue || !b.HasValue) { unknown = true; continue; }
                var pass = GameStatUtils.StatCompare(a.Value, b.Value, Field<StatCompareType>(item, "_compareOp"));
                if (isAnd && !pass) return false;
                if (!isAnd && pass) return true;
            }
            return unknown ? (bool?)null : isAnd;
        }

        internal static bool? CheckData(ConditionResultData data, int depth = 0)
        {
            if (data == null || depth > 16) return null;
            var items = Field<List<ConditionResultItem>>(data, "_items");
            if (items == null) return null;
            var unknown = false;
            foreach (var item in items)
            {
                var result = Check(item, depth + 1);
                if (result == true) return true;
                if (!result.HasValue) unknown = true;
            }
            return unknown ? (bool?)null : false;
        }

        private static int? Value(StatGroupVariable group, int depth)
        {
            if (group == null || depth > 16) return null;
            var sources = Field<List<StatValueReference>>(group, "_value");
            if (sources == null) return null;
            var values = new List<int>();
            foreach (var source in sources)
            {
                var kind = (int)source.CheckType;
                int? value;
                if (kind == 7) return null;
                if (kind == 8) value = Value(Field<StatGroupVariable>(source, "_statGroup"), depth + 1);
                else if (kind == 16)
                {
                    var condition = CheckData(Field<ConditionResultData>(source, "_condition"), depth + 1);
                    value = condition.HasValue ? (int?)(condition.Value ? 1 : 0) : null;
                }
                else if (kind >= 0 && kind <= 15) value = source.GetValue();
                else return null;
                if (!value.HasValue) return null;
                values.Add(value.Value);
            }
            switch ((int)Field<StatGroupType>(group, "_groupType"))
            {
                case 1: return values.Sum();
                case 2: return values.Count == 0 ? (int?)null : values.Max();
                case 3: return values.Count == 0 ? (int?)null : values.Min();
                case 4: return values.Aggregate(1, (a, b) => a * b);
                default: return 0;
            }
        }

        internal static int? Rate(Mortal.Story.PositionEventData data)
        {
            if (Field<bool>(data, "_toggleCondition"))
            {
                var active = Check(Field<ConditionResultItem>(data, "_activeCondition"));
                if (active == false) return 0;
                if (!active.HasValue) return null;
            }
            var rate = Field<int>(data, "_defaultRate");
            var items = Field<Mortal.Story.PositionEventRateItem[]>(data, "_eventRateItem");
            if (items != null)
                foreach (var item in items)
                {
                    var pass = Check(item.Condition);
                    if (pass == true) rate += item.Rate;
                    else if (!pass.HasValue && item.Rate != 0) return null;
                }
            return Math.Max(0, rate);
        }

        internal static string Describe(ConditionResultItem condition) { return Describe(condition, null); }

        private static string FlagMetadata(FlagData flag)
        {
            if (flag == null) return null;
            var note = CheckpointNameRules.Label(flag.DevNote, flag.name);
            if (note != null) return note;
            try
            {
                return CheckpointNameRules.Label(LocalizationManager.Instance.LocaleResolver.GetString(flag.GetNameKey()), flag.name);
            }
            catch { }
            return null;
        }

        internal static string FlagName(FlagData flag)
        {
            if (flag == null) return "剧情标记";
            return EventInspector.Clean(CodeNameMapping.Resolve("flags", flag.name, FlagMetadata(flag)));
        }

        internal static string CheckpointName(string key, object data)
        {
            var scope = data is Mortal.Story.SwitchResultData ? "switches" : "conditions";
            return EventInspector.Clean(CodeNameMapping.Resolve(scope, key, Field<string>(data, "_devNote")));
        }

        internal static string Describe(ConditionResultItem condition, Dictionary<StatGroupVariable, int> observed,
            Dictionary<StatCompareItem, bool> comparisons = null)
        {
            var items = Field<List<StatCompareItem>>(condition, "_items");
            if (items == null) return "条件未知";
            if (items.Count == 0) return "无额外限制";
            var operators = new[] { "=", "≠", "<", "≤", ">", "≥" };
            var parts = new List<string>();
            foreach (var item in items)
            {
                var left = Field<StatGroupVariable>(item, "_value1");
                var right = Field<StatGroupVariable>(item, "_value2");
                var op = (int)Field<StatCompareType>(item, "_compareOp");
                var part = DescribeGroup(left, observed) + " " + (op >= 0 && op < operators.Length ? operators[op] : "?") + " " + DescribeGroup(right, observed);
                bool passed;
                if (comparisons != null && comparisons.TryGetValue(item, out passed))
                    part = "<color=" + (passed ? "#84C98C" : "#E99C91") + ">" + part + "</color>";
                parts.Add(part);
            }
            return string.Join(Field<LogicOperatorType>(condition, "_logicOp") == LogicOperatorType.AND ? " 且 " : " 或 ", parts.ToArray());
        }

        private static string DescribeGroup(StatGroupVariable group, Dictionary<StatGroupVariable, int> observed)
        {
            var sources = Field<List<StatValueReference>>(group, "_value");
            int seen;
            var value = observed == null ? Value(group, 0) : group != null && observed.TryGetValue(group, out seen) ? (int?)seen : null;
            if (sources == null || sources.Count != 1) return "综合数值" + (value.HasValue ? "(" + value.Value + ")" : "");
            var source = sources[0];
            string label;
            switch ((int)source.CheckType)
            {
                case 0: return value.HasValue ? value.Value.ToString() : "?";
                case 1:
                    var stat = Field<GameStat>(source, "_stat");
                    label = stat == null ? "属性" : OptionImpactLocalization.GameStat(stat.StatType.ToString());
                    break;
                case 2:
                    var relation = Field<RelationshipStat>(source, "_relationship");
                    label = relation == null ? "好感" : OptionImpactLocalization.Simplified(relation.Type.ToString()) + "好感";
                    break;
                case 3:
                    var flag = Field<FlagData>(source, "_flag");
                    label = flag == null ? "剧情标记" : FlagName(flag);
                    break;
                case 7: return "随机判定" + (value.HasValue ? "(" + value.Value + ")" : "（触发时判定）");
                case 9: label = "游戏旬数"; break;
                case 10: label = "天赋等级"; break;
                case 16: label = "前置条件"; break;
                default: label = "物品 / 养成进度"; break;
            }
            return label + (value.HasValue ? "(当前" + value.Value + ")" : "");
        }
    }
}
