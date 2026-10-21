using System;
using System.Text.RegularExpressions;

namespace LOMSave.Probe
{
    // Match the native PositionOptionButton and BreakOptionButton localization
    // namespaces. Their compound source text is metadata, not a display label.
    internal static class EventPreviewNames
    {
        internal static string Option(string source, Func<string, string> getString, Func<string, string> getStory)
        {
            var parts = (source ?? "").Split('+');
            if (parts.Length == 3)
                return Localized("Position/title/" + parts[2], getString)
                    ?? Localized("Position/type/" + parts[1], getString) ?? "行动";
            int actionCost, contributionCost;
            if (parts.Length == 4 && int.TryParse(parts[2], out actionCost) && int.TryParse(parts[3], out contributionCost))
                return Localized(parts[1], getStory) ?? HumanLabel(parts[1]) ?? "选项";
            return Localized(source, getStory) ?? HumanLabel(source) ?? "选项";
        }

        internal static string Event(string title, Func<string, string> getStory)
        {
            var localized = Localized(title, getStory);
            if (localized != null) return localized;
            var result = Regex.Match(title ?? "", @"^P_([\u3400-\u9fff]+)_(\d+)$");
            if (result.Success)
            {
                int number;
                return result.Groups[1].Value + (int.TryParse(result.Groups[2].Value, out number) && number > 1 ? " · " + number : "");
            }
            // Preserve explicit/user-supplied names and unresolved identities;
            // never replace multiple unknown events with an identical guess.
            return string.IsNullOrEmpty(title) ? "未命名事件" : title;
        }

        private static string HumanLabel(string value)
        { return !string.IsNullOrEmpty(value) && !value.Contains("+") && !value.Contains("_")
            && Regex.IsMatch(value, @"[\u3400-\u9fff]") ? value : null; }

        private static string Localized(string key, Func<string, string> resolve)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var value = resolve(key);
            return string.IsNullOrEmpty(value) || value == key || value == "Story/" + key
                || value.StartsWith("Position/", StringComparison.Ordinal) ? null : HumanLabel(value);
        }
    }
}
