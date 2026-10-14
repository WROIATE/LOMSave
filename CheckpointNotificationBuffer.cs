using System.Collections.Generic;

namespace LOMSave.Probe
{
    // Only schedule arrivals. Each native FloatingText owns its animation and lifetime.
    internal sealed class CheckpointNotificationBuffer
    {
        internal sealed class Item { internal string Key, Text; }
        internal const float ArrivalInterval = 1f;
        private readonly Queue<Item> _pending = new Queue<Item>();
        private float _untilNextArrival;
        internal int PendingCount { get { return _pending.Count; } }
        internal void Add(string key, string text, bool enabled)
        {
            if (enabled) _pending.Enqueue(new Item { Key = key, Text = text });
        }
        internal Item Tick(float elapsed, bool paused)
        {
            if (paused) return null;
            _untilNextArrival = System.Math.Max(0f, _untilNextArrival - System.Math.Max(0f, elapsed));
            if (_untilNextArrival > 0f || _pending.Count == 0) return null;
            // Never stop at the fourth message or catch up with an entire batch after a stall.
            _untilNextArrival = ArrivalInterval;
            return _pending.Dequeue();
        }
        internal void Clear() { _pending.Clear(); _untilNextArrival = 0f; }
    }
}
