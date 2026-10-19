using System;
using System.Collections.Generic;

namespace LOMSave.Probe
{
    // Loaded game resources are static for this process. Cache raw actions,
    // not descriptions or eligibility, which still depend on current stats.
    internal sealed class StoryImpactCache
    {
        private sealed class Entry
        {
            internal string Source;
            internal Dictionary<string, List<LuaImpactScanner.Action>> Actions;
        }
        private readonly int _capacity;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _order = new Queue<string>();
        internal StoryImpactCache(int capacity = 32) { _capacity = Math.Max(1, capacity); }
        internal bool TryGet(string name, string source, out Dictionary<string, List<LuaImpactScanner.Action>> actions)
        {
            Entry entry;
            if (_entries.TryGetValue(name, out entry) && string.Equals(entry.Source, source, StringComparison.Ordinal))
            { actions = entry.Actions; return true; }
            actions = null;
            return false;
        }
        internal void Store(string name, string source, Dictionary<string, List<LuaImpactScanner.Action>> actions)
        {
            if (!_entries.ContainsKey(name))
            {
                if (_entries.Count >= _capacity) _entries.Remove(_order.Dequeue());
                _order.Enqueue(name);
            }
            _entries[name] = new Entry { Source = source, Actions = actions };
        }
    }
}
