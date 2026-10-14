using System;
using System.IO;

namespace LOMSave.Probe
{
    public static class StorySaveFilesTests
    {
        private static int _checks;
        private static void Check(bool value, string message)
        { _checks++; if (!value) throw new Exception(message); }

        public static string Run(string directory)
        {
            _checks = 0;
            Directory.CreateDirectory(directory);
            var manual = Path.Combine(directory, "Save_049.dat");
            var automatic = Path.Combine(directory, "Save_auto.dat");
            File.WriteAllText(manual, "manual meeting save");
            File.WriteAllText(manual + ".lomsaveprobe.json", "old meeting checkpoint");
            File.WriteAllText(automatic, "native automatic story save");
            if (File.Exists(automatic + ".lomsaveprobe.json")) File.Delete(automatic + ".lomsaveprobe.json");

            Check(SaveSlotContext.Resolve("049") == "049", "Direct/manual loads use the selected manual slot");
            var previous = SaveSlotContext.Begin("auto");
            Check(SaveSlotContext.Resolve("049") == "auto", "An automatic load must not use its originating manual slot");
            var source = Path.Combine(directory, "Save_" + SaveSlotContext.Resolve("049") + ".dat.lomsaveprobe.json");
            Check(!File.Exists(source) && File.Exists(manual + ".lomsaveprobe.json"), "Missing auto metadata must not borrow the existing 049 meeting record");
            var nested = SaveSlotContext.Begin("002");
            Check(SaveSlotContext.Resolve("049") == "002", "A nested manual load has its own source");
            SaveSlotContext.End(nested);
            Check(SaveSlotContext.Resolve("049") == "auto", "The nested load restores the outer automatic source");
            SaveSlotContext.End(previous);
            Check(SaveSlotContext.Resolve("049") == "049", "Completed automatic loads do not affect later manual loads");
            try
            {
                previous = SaveSlotContext.Begin("auto_free");
                throw new IOException("Simulated native read failure");
            }
            catch (IOException) { }
            finally { SaveSlotContext.End(previous); }
            Check(SaveSlotContext.Resolve("049") == "049", "A failed load cannot leak its source into subsequent loads");

            foreach (var slot in new[] { "auto", "auto_free", "auto_battle", "auto_action", "001", "050" })
            {
                previous = SaveSlotContext.Begin(slot);
                Check(SaveSlotContext.Resolve("049") == slot, "Actual source is preserved: " + slot);
                SaveSlotContext.End(previous);
            }

            var hash = StorySaveFiles.Hash(automatic);
            Check(hash.Length == 64 && hash != StorySaveFiles.Hash(manual), "Different save files have different content identities");
            Check(StorySaveFiles.CommittedHash(automatic, hash) == null, "An unchanged/failed write cannot replace the prior sidecar");
            File.WriteAllText(automatic, "new automatic save at a story checkpoint");
            var committed = StorySaveFiles.CommittedHash(automatic, hash);
            Check(committed != null && committed == StorySaveFiles.Hash(automatic), "A committed save is bound to the new on-disk contents");
            var missing = Path.Combine(directory, "missing.dat");
            Check(StorySaveFiles.Hash(missing) == "" && StorySaveFiles.CommittedHash(missing, "") == null, "A missing/failed new save never gets a checkpoint record");
            Check(StorySaveFiles.CommittedHash(automatic, "") == committed, "First-time saves can create their own metadata");

            Check(StorySaveFiles.Mismatch(9, 0, 20, null, null, "meeting_option_01_temp", "ch2_5_1") == "checkpoint_script_mismatch", "The reported old meeting record is rejected for ch2_5_1 even if a caller uses the wrong path");
            Check(StorySaveFiles.Mismatch(9, 0, 20, null, null, "meeting_option_01_temp", "meeting_option_01_temp") == null, "Matching existing version 9 manual saves remain compatible");
            Check(StorySaveFiles.Mismatch(10, 20, 20, committed, committed, "ch2_5_1", "ch2_5_1") == null, "Matching new automatic and manual records can restore precise positions");
            Check(StorySaveFiles.Mismatch(10, 20, 21, committed, committed, "ch2_5_1", "ch2_5_1") == "save_identity_mismatch", "A different save generation cannot reuse a checkpoint, even in the same script");
            Check(StorySaveFiles.Mismatch(10, 20, 20, hash, committed, "ch2_5_1", "ch2_5_1") == "save_identity_mismatch", "External overwrite/copy with different file contents is rejected");
            Check(StorySaveFiles.Mismatch(10, 0, 20, committed, committed, "ch2_5_1", "ch2_5_1") == "save_identity_missing", "New-format files need a valid save timestamp");
            Check(StorySaveFiles.Mismatch(10, 20, 20, null, committed, "ch2_5_1", "ch2_5_1") == "save_identity_missing", "A truncated identity cannot enable replay");
            Check(StorySaveFiles.Mismatch(10, 20, 20, committed, committed, "meeting_option_01_temp", "ch2_5_1") == "checkpoint_script_mismatch", "Content identity alone cannot allow a checkpoint from another script");
            Check(StorySaveFiles.Mismatch(11, 20, 20, committed, committed, "ch2_5_1", "ch2_5_1") == "unsupported_state_version", "Unknown future sidecar versions use the native fallback");
            return _checks + " save-source, commit and checkpoint binding assertions passed";
        }
    }
}
