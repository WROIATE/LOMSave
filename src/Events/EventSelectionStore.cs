using System.Collections.Generic;

namespace LOMSave.Probe
{
    // Persist stable asset names, never scene objects or a previous eligibility result.
    internal sealed class EventSelectionStore
    {
        public Dictionary<string, string> Choices = new Dictionary<string, string>();
        internal string Get(string scope)
        {
            string value;
            return scope != null && Choices != null && Choices.TryGetValue(scope, out value) ? value : null;
        }
        internal void Set(string scope, string value)
        {
            if (scope == null) return;
            if (Choices == null) Choices = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(value)) Choices.Remove(scope);
            else Choices[scope] = value;
        }
        internal static bool Show(bool? eligible, bool showUnavailable)
        { return showUnavailable || eligible != false; }
    }
}
