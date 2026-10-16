using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LOMSave.Probe
{
    internal sealed class CalendarAction
    {
        public string Date, Label, Script;
        public int Before, After;
    }
    internal sealed class CalendarHistory
    {
        public int Version = 1;
        public long SaveTimeTick;
        public string SaveFileHash;
        public List<CalendarAction> Actions = new List<CalendarAction>();
        internal bool Matches(long tick, string hash)
        { return Version == 1 && tick > 0 && SaveTimeTick == tick && hash != null && hash.Length == 64 && hash == SaveFileHash; }
    }
    internal sealed class CalendarNotes
    {
        public Dictionary<string, string> Notes = new Dictionary<string, string>();
        internal static string Preview(string note, int limit)
        {
            var text = string.Join(" ", (note ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            var elements = StringInfo.ParseCombiningCharacters(text);
            return elements.Length <= limit ? text : text.Substring(0, elements[Math.Max(0, limit)]) + "…";
        }
        internal static string DateKey(int year, int month, int stage)
        { return year.ToString("D4") + "-" + month.ToString("D2") + "-" + stage; }
        internal string Get(string date)
        {
            string value;
            return Notes != null && Notes.TryGetValue(date, out value) ? value ?? "" : "";
        }
        internal void Set(string date, string value)
        {
            if (Notes == null) Notes = new Dictionary<string, string>();
            value = new string((value ?? "").Where(c => !char.IsControl(c) || c == '\n').Take(300).ToArray()).Trim();
            if (value.Length == 0) Notes.Remove(date);
            else Notes[date] = value;
        }
    }
}
