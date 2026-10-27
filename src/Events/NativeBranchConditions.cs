using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;
using Mortal.Story;

namespace LOMSave.Probe
{
    internal static class NativeBranchConditions
    {
        private static readonly Dictionary<object, StoryEventIndexData> Cache = new Dictionary<object, StoryEventIndexData>();
        internal static StoryEventIndexData Build(object checkpoint, string key)
        {
            StoryEventIndexData cached;
            if (Cache.TryGetValue(checkpoint, out cached)) return cached;
            var data = new StoryEventIndexData { Groups = new Dictionary<string, StoryIndexGroup>(),
                Conditions = new Dictionary<string, StoryIndexCondition[]>(), Switches = new Dictionary<string, StoryIndexSwitch>() };
            var groups = new Dictionary<StatGroupVariable, string>(); var natives = new Dictionary<StoryIndexValue, StatValueReference>();
            Func<StatGroupVariable, string> group = null;
            Func<ConditionResultItem, StoryIndexCondition> condition = item => new StoryIndexCondition {
                Logic = (int)EventConditions.Field<LogicOperatorType>(item, "_logicOp"),
                Comparisons = (EventConditions.Field<List<StatCompareItem>>(item, "_items") ?? new List<StatCompareItem>()).Select(x => new StoryIndexComparison {
                    Op = (int)EventConditions.Field<StatCompareType>(x, "_compareOp"),
                    Left = group(EventConditions.Field<StatGroupVariable>(x, "_value1")), Right = group(EventConditions.Field<StatGroupVariable>(x, "_value2")) }).ToArray() };
            group = native => {
                if (native == null) return null;
                string id; if (groups.TryGetValue(native, out id)) return id;
                id = groups.Count.ToString(); groups[native] = id;
                var target = new StoryIndexGroup { Operation = (int)EventConditions.Field<StatGroupType>(native, "_groupType") };
                data.Groups[id] = target;
                target.Values = (EventConditions.Field<List<StatValueReference>>(native, "_value") ?? new List<StatValueReference>()).Select(source => {
                    var v = new StoryIndexValue { Kind = (int)source.CheckType, Constant = EventConditions.Field<int>(source, "_constant"),
                        Negative = EventConditions.Field<bool>(source, "_negative"), RandomMin = EventConditions.Field<int>(source, "_randomMin"),
                        RandomMax = EventConditions.Field<int>(source, "_randomMax") };
                    if (v.Kind == 8) v.Group = group(EventConditions.Field<StatGroupVariable>(source, "_statGroup"));
                    if (v.Kind == 16)
                    {
                        var nested = EventConditions.Field<ConditionResultData>(source, "_condition");
                        if (nested != null) { v.Condition = nested.name; if (!data.Conditions.ContainsKey(v.Condition)) {
                            data.Conditions[v.Condition] = new StoryIndexCondition[0];
                            data.Conditions[v.Condition] = (EventConditions.Field<List<ConditionResultItem>>(nested, "_items") ?? new List<ConditionResultItem>()).Select(condition).ToArray(); } }
                    }
                    natives[v] = source; return v;
                }).ToArray(); return id;
            };
            var items = EventConditions.Field<List<ConditionResultItem>>(checkpoint, "_items");
            if (checkpoint is SwitchResultData) data.Switches[key] = new StoryIndexSwitch {
                Counted = EventConditions.Field<bool>(checkpoint, "_useCount"), Items = items == null ? null : items.Select(condition).ToArray() };
            else data.Conditions[key] = items == null ? null : items.Select(condition).ToArray();
            data.ReadNative = v => v.Kind > 0 && v.Kind < 16 && v.Kind != 7 && v.Kind != 8 ? (int?)natives[v].GetValue() : null;
            if (Cache.Count > 128) Cache.Clear(); Cache[checkpoint] = data; return data;
        }
    }
}
