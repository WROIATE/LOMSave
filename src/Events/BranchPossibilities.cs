using System;
using System.Collections.Generic;
using System.Linq;

namespace LOMSave.Probe
{
    // Boolean domains: 0 unknown, 1 false, 2 true, 3 either. Never draws RNG.
    internal sealed class BranchPossibilities
    {
        private readonly StoryEventIndexData _data;
        internal BranchPossibilities(StoryEventIndexData data) { _data = data; }
        internal bool HasRandom(string kind, string key)
        { return Items(kind, key)?.Any(x => Random(x, 0)) == true; }
        private StoryIndexCondition[] Items(string kind, string key)
        {
            if (_data == null) return null;
            StoryIndexCondition[] c; StoryIndexSwitch s;
            return kind == "Condition" ? (_data.Conditions.TryGetValue(key, out c) ? c : null)
                : _data.Switches.TryGetValue(key, out s) && !s.Counted ? s.Items : null;
        }
        internal int Count(string kind, string key)
        { var items = Items(kind, key); return items == null ? 0 : kind == "Condition" ? 2 : items.Length + 1; }
        internal bool? CanChoose(string kind, string key, int outcome)
        {
            var items = Items(kind, key);
            if (items == null || outcome < 1 || outcome > Count(kind, key)) return false;
            if (kind == "Condition")
            {
                var mask = 1;
                foreach (var item in items) mask = Or(mask, Check(item, 0));
                return Possible(mask, outcome == 1 ? 2 : 1);
            }
            var unknown = false;
            for (var i = 0; i < outcome - 1; i++)
            {
                var pass = Possible(Check(items[i], 0), 1);
                if (pass == false) return false;
                unknown |= !pass.HasValue;
            }
            if (outcome <= items.Length)
            {
                var pass = Possible(Check(items[outcome - 1], 0), 2);
                if (pass == false) return false;
                unknown |= !pass.HasValue;
            }
            return unknown ? (bool?)null : true;
        }
        private static bool? Possible(int mask, int expected) { return mask == 0 ? (bool?)null : (mask & expected) != 0; }
        private static int And(int a, int b)
        { if (a == 1 || b == 1) return 1; if (a == 0 || b == 0) return 0; return ((a & b & 2) != 0 ? 2 : 0) | ((a & 1) != 0 || (b & 1) != 0 ? 1 : 0); }
        private static int Or(int a, int b)
        { if (a == 2 || b == 2) return 2; if (a == 0 || b == 0) return 0; return ((a & b & 1) != 0 ? 1 : 0) | ((a & 2) != 0 || (b & 2) != 0 ? 2 : 0); }
        private int Check(StoryIndexCondition c, int depth)
        {
            if (depth > 16 || c == null || c.Comparisons == null) return 0;
            var result = c.Logic == 0 ? 2 : c.Logic == 1 ? 1 : 0;
            foreach (var comparison in c.Comparisons)
            {
                var a = Values(comparison.Left, depth + 1); var b = Values(comparison.Right, depth + 1);
                var mask = 0;
                if (a != null && b != null && a.Count > 0 && b.Count > 0)
                {
                    bool yes, no;
                    switch (comparison.Op)
                    {
                        case 0: yes = a.Overlaps(b); no = a.Count != 1 || b.Count != 1 || a.First() != b.First(); break;
                        case 1: no = a.Overlaps(b); yes = a.Count != 1 || b.Count != 1 || a.First() != b.First(); break;
                        case 2: yes = a.Min() < b.Max(); no = a.Max() >= b.Min(); break;
                        case 3: yes = a.Min() <= b.Max(); no = a.Max() > b.Min(); break;
                        case 4: yes = a.Max() > b.Min(); no = a.Min() <= b.Max(); break;
                        case 5: yes = a.Max() >= b.Min(); no = a.Min() < b.Max(); break;
                        default: return 0;
                    }
                    mask = (yes ? 2 : 0) | (no ? 1 : 0);
                }
                result = c.Logic == 0 ? And(result, mask) : Or(result, mask);
            }
            return result;
        }
        private HashSet<int> Values(string key, int depth)
        {
            StoryIndexGroup g;
            if (depth > 16 || key == null || !_data.Groups.TryGetValue(key, out g) || g.Values == null) return null;
            if (g.Operation == 0) return new HashSet<int> { 0 };
            if (g.Operation < 0 || g.Operation > 6) return null;
            HashSet<int> result = null;
            foreach (var v in g.Values)
            {
                HashSet<int> values = null;
                if (v.Kind == 7)
                {
                    if (!v.RandomMin.HasValue || !v.RandomMax.HasValue || v.RandomMax < v.RandomMin
                        || (long)v.RandomMax.Value - v.RandomMin.Value > 2048) return null;
                    values = new HashSet<int>(Enumerable.Range(v.RandomMin.Value, v.RandomMax.Value - v.RandomMin.Value + 1));
                }
                else if (v.Kind == 8) values = Values(v.Group, depth + 1);
                else if (v.Kind == 16)
                {
                    StoryIndexCondition[] items;
                    if (!_data.Conditions.TryGetValue(v.Condition, out items)) return null;
                    var mask = 1; foreach (var item in items) mask = Or(mask, Check(item, depth + 1));
                    if (mask == 0) return null;
                    values = new HashSet<int>(); if ((mask & 1) != 0) values.Add(0); if ((mask & 2) != 0) values.Add(1);
                }
                else
                {
                    int? value = v.Kind == 0 ? (int?)(v.Negative ? -v.Constant : v.Constant) : _data.ReadNative?.Invoke(v);
                    if (value.HasValue) values = new HashSet<int> { value.Value };
                }
                if (values == null) return null;
                if (result == null) { result = g.Operation >= 5 ? new HashSet<int>(values.Select(n => n != 0 ? 1 : 0)) : values; continue; }
                if ((long)result.Count * values.Count > 8192) return null;
                var next = new HashSet<int>();
                foreach (var a in result) foreach (var b in values)
                    next.Add(g.Operation == 1 ? unchecked(a + b) : g.Operation == 2 ? Math.Max(a, b) : g.Operation == 3 ? Math.Min(a, b)
                        : g.Operation == 4 ? unchecked(a * b) : g.Operation == 5 ? (a != 0 && b != 0 ? 1 : 0) : (a != 0 || b != 0 ? 1 : 0));
                if (next.Count > 2048) return null;
                result = next;
            }
            return result ?? (g.Operation == 1 || g.Operation == 6 ? new HashSet<int> { 0 } : g.Operation == 4 || g.Operation == 5 ? new HashSet<int> { 1 } : null);
        }
        private bool Random(StoryIndexCondition c, int depth)
        { return depth <= 16 && c?.Comparisons?.Any(x => RandomGroup(x.Left, depth + 1) || RandomGroup(x.Right, depth + 1)) == true; }
        private bool RandomGroup(string key, int depth)
        {
            StoryIndexGroup g;
            if (depth > 16 || key == null || !_data.Groups.TryGetValue(key, out g)) return false;
            return g.Values.Any(v => v.Kind == 7 || v.Kind == 8 && RandomGroup(v.Group, depth + 1)
                || v.Kind == 16 && Items("Condition", v.Condition)?.Any(x => Random(x, depth + 1)) == true);
        }
    }
}
