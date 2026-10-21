using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class CalendarEventLog
    {
        private static CalendarEventJournal _journal = new CalendarEventJournal();
        private static bool _dirty;
        private static float _flushAt;
        private static string FilePath { get { return Path.Combine(BepInEx.Paths.ConfigPath, "LOMSaveProbe.CalendarEvents.json"); } }
        internal static int Revision { get; private set; }
        internal static void Initialize()
        {
            try { _journal = LocalJsonFile.Read<CalendarEventJournal>(FilePath); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("calendar_events_load_failed", e); }
            _dirty = false; Revision++;
        }
        internal static IReadOnlyList<CalendarEventRecord> On(string date) { return _journal.On(date); }
        internal static int Unread(IEnumerable<CalendarStoryEvent> current) { return _journal.Unread(current); }
        internal static bool IsRead(CalendarStoryEvent item) { return _journal.IsRead(item); }
        internal static void Observe(string date, IEnumerable<CalendarStoryEvent> current)
        { if (_journal.Observe(date, current)) Changed(); }
        internal static void MarkRead(IEnumerable<CalendarStoryEvent> displayed)
        { if (_journal.MarkRead(displayed)) { Changed(); Flush(); } }
        private static void Changed()
        {
            // Coalesce scans; idle frames neither scan the journal nor write.
            if (!_dirty) _flushAt = Time.unscaledTime + 2;
            _dirty = true; Revision++;
        }
        internal static void Update() { if (_dirty && Time.unscaledTime >= _flushAt) Flush(); }
        internal static void Flush()
        {
            if (!_dirty) return;
            try { LocalJsonFile.Write(FilePath, _journal); _dirty = false; }
            catch (Exception e)
            { _flushAt = Time.unscaledTime + 10; LOMSaveProbePlugin.WriteError("calendar_events_write_failed", e); }
        }
    }
}
