using System.Collections.Generic;

namespace LOMSave.Probe
{
    internal static class CalendarEventLog
    {
        private static CalendarEventJournal _journal = new CalendarEventJournal();
        internal static int Revision { get; private set; }
        // Do not import the legacy global journal: it cannot identify which
        // slot/run produced its receipts. Old saves begin unread, then rescan.
        internal static void Initialize() { Restore(null); }
        internal static CalendarEventJournal Capture() { return _journal.Snapshot(); }
        internal static void Restore(CalendarEventJournal journal)
        { _journal = journal == null ? new CalendarEventJournal() : journal.Snapshot(); Revision++; }
        internal static IReadOnlyList<CalendarEventRecord> On(string date) { return _journal.On(date); }
        internal static int Unread(IEnumerable<CalendarStoryEvent> current) { return _journal.Unread(current); }
        internal static bool IsRead(CalendarStoryEvent item) { return _journal.IsRead(item); }
        internal static void Observe(string date, IEnumerable<CalendarStoryEvent> current)
        { if (_journal.Observe(date, current)) Revision++; }
        internal static void MarkRead(IEnumerable<CalendarStoryEvent> displayed)
        { if (_journal.MarkRead(displayed)) Revision++; }
    }
}
