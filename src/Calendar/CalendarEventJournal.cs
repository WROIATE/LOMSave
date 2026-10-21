using System;
using System.Collections.Generic;
using System.Linq;

namespace LOMSave.Probe
{
    internal sealed class CalendarStoryEvent
    {
        public string Key, Title, Kind, Trigger;
        public bool Random;
    }

    internal sealed class CalendarEventRecord
    {
        public string Date, Key, Title, Kind;
        public bool Random;
        public List<string> Triggers = new List<string>();
    }

    // Personal reading receipts and observed availability survive reloads,
    // like calendar notes. These are observations, never a future schedule.
    internal sealed class CalendarEventJournal
    {
        public HashSet<string> ReadKeys = new HashSet<string>(StringComparer.Ordinal);
        public List<CalendarEventRecord> Records = new List<CalendarEventRecord>();
        private Dictionary<string, List<CalendarEventRecord>> _dates;
        private Dictionary<string, CalendarEventRecord> _entries;

        private void Index()
        {
            if (_dates != null) return;
            ReadKeys = ReadKeys ?? new HashSet<string>(StringComparer.Ordinal);
            Records = (Records ?? new List<CalendarEventRecord>()).Where(x => x != null
                && !string.IsNullOrEmpty(x.Date) && !string.IsNullOrEmpty(x.Key))
                .GroupBy(x => x.Date + "\n" + x.Key).Select(x => x.First()).ToList();
            _dates = Records.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToList());
            _entries = Records.ToDictionary(x => x.Date + "\n" + x.Key);
            foreach (var record in Records) record.Triggers = record.Triggers ?? new List<string>();
        }
        internal IReadOnlyList<CalendarEventRecord> On(string date)
        {
            Index();
            List<CalendarEventRecord> result;
            return date != null && _dates.TryGetValue(date, out result) ? result : (IReadOnlyList<CalendarEventRecord>)Array.Empty<CalendarEventRecord>();
        }
        internal bool IsRead(CalendarStoryEvent item)
        { Index(); return item != null && item.Key != null && ReadKeys.Contains(item.Key); }
        internal int Unread(IEnumerable<CalendarStoryEvent> current)
        { Index(); return current.Count(x => x != null && !string.IsNullOrEmpty(x.Key) && !ReadKeys.Contains(x.Key)); }
        internal bool MarkRead(IEnumerable<CalendarStoryEvent> displayed)
        {
            Index(); var changed = false;
            foreach (var item in displayed)
                if (item != null && !string.IsNullOrEmpty(item.Key)) changed |= ReadKeys.Add(item.Key);
            return changed;
        }
        internal bool Observe(string date, IEnumerable<CalendarStoryEvent> current)
        {
            if (string.IsNullOrEmpty(date)) return false;
            Index(); var changed = false;
            foreach (var item in current)
            {
                if (item == null || string.IsNullOrEmpty(item.Key)) continue;
                var id = date + "\n" + item.Key;
                CalendarEventRecord record;
                if (!_entries.TryGetValue(id, out record))
                {
                    record = new CalendarEventRecord { Date = date, Key = item.Key, Title = item.Title, Kind = item.Kind, Random = item.Random };
                    Records.Add(record); _entries.Add(id, record);
                    List<CalendarEventRecord> day;
                    if (!_dates.TryGetValue(date, out day)) _dates.Add(date, day = new List<CalendarEventRecord>());
                    day.Add(record); changed = true;
                }
                // Keep distinct trigger paths observed at different moments
                // of the same ten-day stage, even if eligibility later ends.
                foreach (var trigger in (item.Trigger ?? "").Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries))
                    if (!record.Triggers.Contains(trigger)) { record.Triggers.Add(trigger); changed = true; }
            }
            return changed;
        }
    }
}
