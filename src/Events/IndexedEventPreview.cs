using System;
using System.Collections.Generic;
using System.Linq;

namespace LOMSave.Probe
{
    // Descriptions and stable string identities only. Never manufacture native
    // ScriptableObjects or run Lua to make a cold-start preview work.
    internal static class IndexedEventPreview
    {
        internal static List<EventInspector.Entry> Pool(string key, string group)
        {
            var result = new List<EventInspector.Entry>();
            var index = StoryEventIndex.Data;
            StoryIndexEvent[] items;
            if (index == null || !index.Pools.TryGetValue(key, out items)) return result;
            foreach (var item in items)
            {
                if (item == null) continue;
                var rate = index.Rate(item);
                var title = EventInspector.Clean(CodeNameMapping.Resolve("events", item.Code, item.Note, item.NativeTitle));
                title = EventPreviewNames.Event(title, Mortal.Core.LocalizationManager.Instance.LocaleResolver.GetStoryText);
                var detail = "当前权重 " + (rate.HasValue ? rate.Value.ToString() : "待判定") + "  ·  基础权重 " + item.Rate;
                var conditions = item.Conditional ? Describe(index, item.Active) : "无额外触发条件";
                foreach (var rule in item.Rates ?? new StoryIndexRate[0])
                    if (rule.Rate != 0) conditions += "\n" + Describe(index, rule.Condition) + "：权重 " + (rule.Rate > 0 ? "+" : "") + rule.Rate;
                conditions += "\n事件代码：" + item.Code + "\n事件池：" + key;
                var effects = new List<string>();
                if (!string.IsNullOrEmpty(item.Script))
                    foreach (var action in LuaImpactScanner.ScanEntry(EventInspector.Load(item.Script), EventInspector.Load))
                    {
                        var effect = OptionImpactPreview.DescribeAction(action.Method, action.Arguments);
                        if (!string.IsNullOrEmpty(effect)) effects.Add((action.Conditional ? "可能：" : "") + effect);
                    }
                detail += effects.Count == 0 ? "\n剧情事件" : "\n" + string.Join("；", effects.Take(8).ToArray()) + (effects.Count > 8 ? "…" : "");
                result.Add(new EventInspector.Entry { Title = title, Detail = detail, Conditions = conditions, Group = group,
                    IndexedPool = key, IndexedEvent = item.Code, CanSelect = rate.HasValue && rate.Value > 0,
                    Eligible = rate.HasValue ? (bool?)(rate.Value > 0) : null });
                foreach (var branch in BranchEventSelection.ForScript(item.Script))
                {
                    branch.Group = title + " · 后续条件剧情";
                    if (!rate.HasValue || rate.Value <= 0) { branch.CanSelect = false; branch.Eligible = rate.HasValue ? (bool?)false : null; }
                    result.Add(branch);
                }
            }
            return result;
        }

        internal static string Describe(StoryEventIndexData data, StoryIndexCondition condition)
        {
            if (condition == null || condition.Comparisons == null) return "条件未知";
            if (condition.Comparisons.Length == 0) return "无额外限制";
            var ops = new[] { "=", "≠", "<", "≤", ">", "≥" };
            return string.Join(condition.Logic == 0 ? " 且 " : " 或 ", condition.Comparisons.Select(x =>
                x == null ? "条件未知" : Group(data, x.Left, 0) + " " + (x.Op >= 0 && x.Op < ops.Length ? ops[x.Op] : "?") + " " + Group(data, x.Right, 0)).ToArray());
        }

        private static string Group(StoryEventIndexData data, string key, int depth)
        {
            StoryIndexGroup group;
            if (depth > 16 || key == null || !data.Groups.TryGetValue(key, out group) || group.Values == null) return "未知条件";
            var parts = new List<string>();
            foreach (var source in group.Values)
            {
                if (source == null) { parts.Add("未知数值"); continue; }
                if (source.Kind == 0) parts.Add((source.Negative ? -source.Constant : source.Constant).ToString());
                else if (source.Kind == 7) parts.Add("随机数（不预演）");
                else if (source.Kind == 8) parts.Add(Group(data, source.Group, depth + 1));
                else if (source.Kind == 16) parts.Add(EventInspector.Clean(CodeNameMapping.Resolve("conditions", source.Condition, null)));
                else parts.Add(EventInspector.Clean(CodeNameMapping.Resolve(source.Kind == 3 ? "flags" : source.Kind == 2 ? "relationships" : "stats", source.Target ?? "未知数值", null)));
            }
            var text = string.Join(group.Operation == 4 ? " × " : " + ", parts.ToArray());
            if (group.Operation == 2 || group.Operation == 3) text = (group.Operation == 2 ? "最大值(" : "最小值(") + text + ")";
            var value = data.Group(key);
            return text + (value.HasValue && group.Values.Any(x => x != null && x.Kind != 0) ? "(当前" + value.Value + ")" : "");
        }
    }
}
