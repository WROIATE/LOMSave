using System;
using System.Text.RegularExpressions;

namespace LOMSave.Probe
{
    // Only consume metadata supplied by the object or an explicit game-data
    // reference. Never infer story names from IDs, scripts or condition text.
    internal static class CheckpointNameRules
    {
        internal static string Label(string text, string key)
        {
            text = Regex.Replace(text ?? "", "<[^>]*>", "").Trim().Split('\n')[0].Trim();
            return string.IsNullOrEmpty(text) || string.Equals(text, key, StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Flag/Name/", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Story/", StringComparison.OrdinalIgnoreCase) ? null : text;
        }

        internal static string Resolve(string key, string note, string localized = null)
        {
            var title = Label(note, key) ?? Label(localized, key);
            if (title == null) return key;
            return title.Length > 64 ? title.Substring(0, 64) + "…" : title;
        }
    }
}
