using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LOMSave.Probe
{
    public static class FeatureDataTests
    {
        private static int _checks;
        private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
        public static string Run(string directory)
        {
            Directory.CreateDirectory(directory);
            var events = new EventSelectionStore();
            events.Set("pool:training", "event-a"); events.Set("pool:work", "event-b"); events.Set("mission:1", "mission-c");
            var eventsFile = Path.Combine(directory, "events.json");
            LocalJsonFile.Write(eventsFile, events);
            events = LocalJsonFile.Read<EventSelectionStore>(eventsFile);
            Check(events.Get("pool:training") == "event-a" && events.Get("pool:work") == "event-b", "Fixed selection survives reload independently for each pool");
            events.Set("pool:training", "event-new");
            Check(events.Get("pool:training") == "event-new" && events.Get("mission:1") == "mission-c", "Replacing a fixed event preserves other scopes");
            events.Set("pool:training", null);
            Check(events.Get("pool:training") == null && events.Get("pool:work") == "event-b", "Cancel removes only its scope");
            Check(!EventSelectionStore.Show(false, false), "Impossible events are hidden by default");
            Check(EventSelectionStore.Show(false, true), "Manual reveal includes impossible events");
            Check(EventSelectionStore.Show(null, false), "Unknown/random conditions must not be called impossible");
            Check(EventSelectionStore.Show(true, false), "Available main missions remain visible even if not selectable");

            var notes = new CalendarNotes();
            Check(CalendarNotes.Preview("找四师兄\n\t先炼丹", 20) == "找四师兄 先炼丹", "Overview note preview combines lines without control characters");
            Check(CalendarNotes.Preview("甲乙丙丁", 3) == "甲乙丙…", "Overview truncates long notes with a visible ellipsis");
            Check(CalendarNotes.Preview("甲\U0001F31F乙", 2) == "甲\U0001F31F…", "Overview never splits a Unicode character");
            Check(CalendarNotes.Preview(null, 14) == "", "Empty dates have no invented note preview");
            var date = CalendarNotes.DateKey(1128, 5, 1);
            notes.Set(date, "记得练武\n先备份存档");
            notes.Set(CalendarNotes.DateKey(1129, 5, 1), "下一年不同备注");
            var notesFile = Path.Combine(directory, "notes.json");
            LocalJsonFile.Write(notesFile, notes);
            var newPlaythroughNotes = LocalJsonFile.Read<CalendarNotes>(notesFile);
            Check(newPlaythroughNotes.Get(date) == "记得练武\n先备份存档", "New playthrough reuses the same in-game date note");
            Check(newPlaythroughNotes.Get(CalendarNotes.DateKey(1129, 5, 1)) == "下一年不同备注", "Years must not collide");
            newPlaythroughNotes.Set(date, new string('字', 310));
            Check(newPlaythroughNotes.Get(date).Length == 300, "Cap note size");
            newPlaythroughNotes.Set(date, "  \n ");
            Check(newPlaythroughNotes.Get(date) == "", "Blank note deletes the reminder");
            LocalJsonFile.Write(notesFile, newPlaythroughNotes);
            Check(File.Exists(notesFile + ".bak") && LocalJsonFile.Read<CalendarNotes>(notesFile + ".bak").Get(date) == notes.Get(date), "Atomic writes keep the previous notes as backup");
            Check(!File.Exists(notesFile + ".tmp"), "Completed writes leave no temp file");

            var hash = new string('a', 64);
            var history = new CalendarHistory { SaveTimeTick = 123, SaveFileHash = hash };
            history.Actions.Add(new CalendarAction { Date = date, Label = "练武", Script = "practice", Before = 3, After = 2 });
            var historyFile = Path.Combine(directory, "save.lomsavecalendar.json");
            LocalJsonFile.Write(historyFile, history);
            history.Actions.Add(new CalendarAction { Date = date, Label = "休息", Before = 2, After = 1 });
            var loaded = LocalJsonFile.Read<CalendarHistory>(historyFile);
            Check(loaded.Matches(123, hash) && loaded.Actions.Count == 1, "Loading an earlier save rolls back later actions");
            Check(loaded.Actions[0].Date == date && loaded.Actions[0].After == 2, "Action date and remaining turns survive serialization");
            Check(!loaded.Matches(124, hash), "Reject overwritten slot timestamp");
            Check(!loaded.Matches(123, new string('b', 64)), "Reject another save with same timestamp");
            loaded.Version = 2; Check(!loaded.Matches(123, hash), "Reject future calendar formats");
            Check(new CalendarHistory().Actions.Count == 0, "Old saves start with no fabricated history");

            var journal = new CalendarEventJournal();
            var story = new CalendarStoryEvent { Key = "mission:story", Title = "一次剧情", Kind = "已就绪剧情", Trigger = "后山 · 进入即触发" };
            Check(!journal.MarkRead(new CalendarStoryEvent[0]), "Empty/historical-only page never reads hidden events");
            Check(journal.Observe(date, new[] { story }) && !journal.IsRead(story), "Automatic observation records availability without marking read");
            Check(!journal.Observe(date, new[] { story }), "Repeated observation does not dirty persistent journal");
            var record = journal.On(date).Single();
            story.Trigger = "出门 · 进入即触发";
            Check(journal.Observe(date, new[] { story }) && record.Triggers.Count == 2, "One date retains both observed trigger paths without duplicate event rows");
            Check(journal.MarkRead(new[] { story }) && !journal.MarkRead(new[] { story }), "Reading same event is idempotent");
            var renamed = new CalendarStoryEvent { Key = story.Key, Title = "本地化名称更新", Trigger = story.Trigger };
            Check(journal.IsRead(renamed), "Translated titles do not reset read state");
            Check(journal.Observe(CalendarNotes.DateKey(1128, 5, 2), new[] { renamed }) && journal.Unread(new[] { renamed }) == 0,
                "Same event spanning stages records both stages but only notifies once");
            Check(journal.Unread(new CalendarStoryEvent[0]) == 0 && journal.On(date).Count == 1, "Expired event remains historical but never contributes to unread count");
            var journalFile = Path.Combine(directory, "calendar-events.json");
            LocalJsonFile.Write(journalFile, journal);
            journal = LocalJsonFile.Read<CalendarEventJournal>(journalFile);
            Check(journal.IsRead(story) && journal.On(date).Single().Triggers.Count == 2 && journal.On(CalendarNotes.DateKey(1128, 5, 2)).Count == 1,
                "Read receipts, dates and trigger paths survive serialization");
            var beforeRecordCount = journal.Records.Count;
            journal.Observe(date, new[] { story });
            Check(journal.Records.Count == beforeRecordCount && journal.On("9999-01-1").Count == 0, "Deserialization rebuilds indexes without inventing future records");

            Check(PinballRewards.Scores.SequenceEqual(new[] { 0, 30, 60, 100 }), "Verified score reward thresholds");
            Check(!PinballRewards.IsSupported(-1) && !PinballRewards.IsSupported(99) && PinballRewards.IsSupported(100), "Only supported score choices can settle");
            var random = new Random(1234);
            var budgets = new HashSet<int>(); var tiers = new HashSet<int>();
            for (var i = 0; i < 1000; i++)
            {
                var budget = BattleRewardPolicy.MoneyBudget(random, 2500); budgets.Add(budget);
                if (budget < 625 || budget > 2500) throw new Exception("Money budget outside safety cap");
                var performance = BattleRewardPolicy.Performance(random, 0, 300); tiers.Add(performance / 100);
                if (performance < 100 || performance > 300) throw new Exception("Performance outside native reward range");
                if (BattleRewardPolicy.Performance(random, 280, 300) < 280) throw new Exception("Lost earned performance");
            }
            Check(budgets.Count > 500, "Money budget varies widely across victories instead of piling up at a fixed cap");
            Check(tiers.SetEquals(new[] { 1, 2, 3 }), "All native performance tiers can occur");
            Check(BattleRewardPolicy.Performance(random, 300, 300) == 300, "Keep earned maximum performance");
            Check(BattleRewardPolicy.Performance(random, 620, 300) == 620, "Keep raw performance above the display cap for the combined track");
            Check(BattleRewardPolicy.Performance(random, 12, 0) == 12, "Missing reward data cannot erase earned performance");
            return _checks + " persistence, calendar, event and reward assertions passed (1,000 reward samples)";
        }
    }
}
