using System;
using System.IO;
using System.Security.Cryptography;

namespace LOMSave.Probe
{
    internal static class SaveSlotContext
    {
        private static string _sourceSlot;
        internal static string Begin(string slot)
        { var previous = _sourceSlot; _sourceSlot = slot; return previous; }
        internal static void End(string previous) { _sourceSlot = previous; }
        internal static string Resolve(string selectedSlot) { return _sourceSlot ?? selectedSlot; }
    }

    internal static class StorySaveFiles
    {
        internal const int StateVersion = 12;

        internal static string Hash(string file)
        {
            if (!File.Exists(file)) return "";
            using (var stream = File.OpenRead(file))
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static string CommittedHash(string file, string previousHash)
        {
            var current = Hash(file);
            return current.Length == 64 && !string.Equals(previousHash, current, StringComparison.Ordinal) ? current : null;
        }

        internal static string Mismatch(int version, long savedTick, long loadedTick,
            string savedHash, string fileHash, string checkpointScript, string saveScript)
        {
            if (version > StateVersion) return "unsupported_state_version";
            if (version >= 10)
            {
                if (savedTick <= 0 || savedHash == null || savedHash.Length != 64) return "save_identity_missing";
                if (savedTick != loadedTick || !string.Equals(savedHash, fileHash, StringComparison.Ordinal))
                    return "save_identity_mismatch";
            }
            if (!string.IsNullOrEmpty(checkpointScript)
                && !string.Equals(checkpointScript, saveScript, StringComparison.Ordinal))
                return "checkpoint_script_mismatch";
            return null;
        }
    }
}
