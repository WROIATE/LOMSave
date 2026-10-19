using System;
using Newtonsoft.Json.Linq;

namespace LOMSave.Probe
{
    internal static class ReplayOriginPolicy
    {
        internal static bool Matches(JObject beginning, string script)
        {
            return beginning != null && !string.IsNullOrEmpty(script)
                && string.Equals(beginning.Value<string>("CurrentStoryScript"), script, StringComparison.Ordinal);
        }

        internal static JObject Select(JObject target, JObject beginning, string entryScript, out string source)
        {
            if (Matches(beginning, entryScript))
            {
                source = "matching_story_entry";
                return (JObject)beginning.DeepClone();
            }
            // Incomplete legacy routes do not justify using another script's
            // old flags as this script's entry, or jumping back there on error.
            source = beginning == null ? "target_entry_missing" : "target_entry_script_mismatch";
            return target == null ? null : (JObject)target.DeepClone();
        }
    }
}
