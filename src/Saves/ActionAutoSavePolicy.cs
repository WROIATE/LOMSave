using System;
using System.Globalization;

namespace LOMSave.Probe
{
    internal static class ActionAutoSavePolicy
    {
        // Native action menus encode type + title + action cost + contribution cost.
        // Read the same cost field as BreakOptionButton; no map/script IDs needed.
        internal static int OptionCost(string text, bool interactable, bool hideOption)
        { return Math.Max(0, ReadOptionCost(text, interactable, hideOption).GetValueOrDefault()); }

        internal static bool ZeroCostEventOption(string text, bool interactable, bool hideOption)
        {
            if (ReadOptionCost(text, interactable, hideOption) != 0) return false;
            var parts = text.Split('+');
            return parts[0] == "event" && int.Parse(parts[3], CultureInfo.InvariantCulture) == 0;
        }

        private static int? ReadOptionCost(string text, bool interactable, bool hideOption)
        {
            if (!interactable || hideOption || string.IsNullOrEmpty(text)) return null;
            var parts = text.Split('+');
            int cost, contribution;
            if (parts.Length != 4 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1])
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cost)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out contribution)
                || cost < 0 || contribution < 0) return null;
            return cost;
        }

        internal static Action BeforeOption(int cost, Action selected, Func<bool> canSave, Action<int> save, bool includeZeroCost = false)
        {
            if (cost < 0 || (cost == 0 && !includeZeroCost) || selected == null) return selected;
            return () =>
            {
                // Evaluate at selection, not when the menu is populated during loading.
                if (canSave()) save(cost);
                selected();
            };
        }
    }
}
