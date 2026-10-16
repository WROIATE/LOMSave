using System;
using System.Globalization;

namespace LOMSave.Probe
{
    internal static class ActionAutoSavePolicy
    {
        // Native action menus encode type + title + action cost + contribution cost.
        // Read the same cost field as BreakOptionButton; no map/script IDs needed.
        internal static int OptionCost(string text, bool interactable, bool hideOption)
        {
            if (!interactable || hideOption || string.IsNullOrEmpty(text)) return 0;
            var parts = text.Split('+');
            int cost, contribution;
            if (parts.Length != 4 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1])
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cost)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out contribution)
                || cost <= 0 || contribution < 0) return 0;
            return cost;
        }

        internal static Action BeforeOption(int cost, Action selected, Func<bool> canSave, Action<int> save)
        {
            if (cost <= 0 || selected == null) return selected;
            return () =>
            {
                // Evaluate at selection, not when the menu is populated during loading.
                if (canSave()) save(cost);
                selected();
            };
        }
    }
}
