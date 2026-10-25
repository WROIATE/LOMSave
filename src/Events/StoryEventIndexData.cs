using System;
using System.Collections.Generic;
using System.Linq;

#pragma warning disable 0649 // Data fields are populated by JSON deserialization.

namespace LOMSave.Probe
{
    // Pure, read-only descriptions. Runtime values are supplied by native
    // getters; no Lua, random draws, checkpoint execution or game mutations.
    internal sealed class StoryIndexValue
    {
        public int Kind, Constant;
        public bool Negative;
        public string Group, Condition, Field, Target;
    }
    internal sealed class StoryIndexGroup { public int Operation; public StoryIndexValue[] Values; }
    internal sealed class StoryIndexComparison { public int Op; public string Left, Right; }
    internal sealed class StoryIndexCondition { public int Logic; public StoryIndexComparison[] Comparisons; }
    internal sealed class StoryIndexSwitch { public bool Counted; public StoryIndexCondition[] Items; }
    internal sealed class StoryIndexRate { public StoryIndexCondition Condition; public int Rate; }
    internal sealed class StoryIndexEvent
    {
        public string Code, Note, NativeTitle, Script;
        public bool Conditional;
        public StoryIndexCondition Active;
        public int Rate;
        public StoryIndexRate[] Rates;
    }
    internal sealed class StoryEventIndexData
    {
        public int Version;
        public string GameVersion;
        public Dictionary<string, string> Hashes;
        public Dictionary<string, StoryIndexGroup> Groups;
        public Dictionary<string, StoryIndexCondition[]> Conditions;
        public Dictionary<string, StoryIndexSwitch> Switches;
        public Dictionary<string, StoryIndexEvent[]> Pools;
        internal Func<StoryIndexValue, int?> ReadNative;
        internal double LoadMilliseconds;

        internal bool? Condition(string key, int depth = 0)
        {
            StoryIndexCondition[] items;
            if (depth > 16 || key == null || Conditions == null || !Conditions.TryGetValue(key, out items) || items == null) return null;
            var unknown = false;
            foreach (var item in items)
            { var pass = Check(item, depth + 1); if (pass == true) return true; if (!pass.HasValue) unknown = true; }
            return unknown ? (bool?)null : false;
        }
        internal int? Selector(string key)
        {
            StoryIndexSwitch data;
            if (key == null || Switches == null || !Switches.TryGetValue(key, out data) || data == null || data.Counted || data.Items == null) return null;
            for (var i = 0; i < data.Items.Length; i++)
            { var pass = Check(data.Items[i], 0); if (!pass.HasValue) return null; if (pass.Value) return i + 1; }
            return data.Items.Length + 1;
        }
        internal bool? Check(StoryIndexCondition condition, int depth = 0)
        {
            if (depth > 16 || condition == null || condition.Comparisons == null || condition.Logic < 0 || condition.Logic > 1) return null;
            var and = condition.Logic == 0; var unknown = false;
            foreach (var comparison in condition.Comparisons)
            {
                if (comparison == null) { unknown = true; continue; }
                var a = Group(comparison.Left, depth + 1); var b = Group(comparison.Right, depth + 1);
                if (!a.HasValue || !b.HasValue || comparison.Op < 0 || comparison.Op > 5) { unknown = true; continue; }
                var pass = comparison.Op == 0 ? a == b : comparison.Op == 1 ? a != b : comparison.Op == 2 ? a < b
                    : comparison.Op == 3 ? a <= b : comparison.Op == 4 ? a > b : a >= b;
                if (and && !pass) return false;
                if (!and && pass) return true;
            }
            return unknown ? (bool?)null : and;
        }
        internal int? Group(string key, int depth = 0)
        {
            StoryIndexGroup group;
            if (depth > 16 || key == null || Groups == null || !Groups.TryGetValue(key, out group) || group == null || group.Values == null) return null;
            var values = new List<int>();
            foreach (var source in group.Values)
            {
                if (source == null) return null;
                int? value;
                if (source.Kind == 0) value = source.Negative ? -source.Constant : source.Constant;
                else if (source.Kind == 7) return null;
                else if (source.Kind == 8) value = Group(source.Group, depth + 1);
                else if (source.Kind == 16)
                { var pass = Condition(source.Condition, depth + 1); value = pass.HasValue ? (int?)(pass.Value ? 1 : 0) : null; }
                else if (source.Kind > 0 && source.Kind < 16) value = ReadNative == null ? null : ReadNative(source);
                else return null;
                if (!value.HasValue) return null;
                values.Add(value.Value);
            }
            switch (group.Operation)
            {
                case 1: return values.Sum();
                case 2: return values.Count == 0 ? (int?)null : values.Max();
                case 3: return values.Count == 0 ? (int?)null : values.Min();
                case 4: return values.Aggregate(1, (a, b) => a * b);
                default: return 0;
            }
        }
        internal int? Rate(StoryIndexEvent item)
        {
            if (item == null) return null;
            if (item.Conditional)
            { var active = Check(item.Active); if (active == false) return 0; if (!active.HasValue) return null; }
            var rate = item.Rate;
            foreach (var change in item.Rates ?? new StoryIndexRate[0])
            {
                var pass = Check(change.Condition);
                if (pass == true) rate += change.Rate;
                else if (!pass.HasValue && change.Rate != 0) return null;
            }
            return Math.Max(0, rate);
        }
    }
}
