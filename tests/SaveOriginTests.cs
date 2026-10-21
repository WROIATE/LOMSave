using System;

namespace LOMSave.Probe
{
    public static class SaveOriginTests
    {
        private static int _checks;
        private static void Check(bool result, string message)
        { _checks++; if (!result) throw new Exception(message); }
        public static string Run()
        {
            var first = new object(); var second = new object();
            Check(SaveOriginPolicy.UseNative("Free", null), "Native cultivation saves do not require dialogue");
            Check(SaveOriginPolicy.UseNative("Battle", null), "Non-story saves retain their native path");
            Check(!SaveOriginPolicy.UseNative("Story", null), "Unknown story permission does not bypass checkpoint safety");
            SaveOriginPolicy.Observe(first, true);
            Check(SaveOriginPolicy.UseNative("Story", first), "Original allowed story menu uses native saving regardless of text/choice state");
            Check(!SaveOriginPolicy.UseNative("Story", second), "Old scene permission cannot authorize a new menu");
            Check(!SaveOriginPolicy.UseNative("Story", null), "Missing story menu cannot inherit permission");
            SaveOriginPolicy.Observe(first, false);
            Check(!SaveOriginPolicy.UseNative("Story", first), "Mod-unlocked original disabled button requires free-save checkpoint");
            SaveOriginPolicy.Observe(second, true);
            Check(SaveOriginPolicy.UseNative("Story", second) && !SaveOriginPolicy.UseNative("Story", first), "Only live menu is authorized");
            Check(!SaveOriginPolicy.NativeSaving, "Native scope begins closed");
            SaveOriginPolicy.BeginNative();
            Check(SaveOriginPolicy.NativeSaving, "Native save bypass covers CreateSaveData, file writing and sidecar handling");
            SaveOriginPolicy.BeginNative(); SaveOriginPolicy.EndNative();
            Check(SaveOriginPolicy.NativeSaving, "Nested native save finalizer preserves outer scope");
            SaveOriginPolicy.EndNative();
            Check(!SaveOriginPolicy.NativeSaving, "Native save finalizer restores free-save behavior");
            try { SaveOriginPolicy.BeginNative(); throw new Exception("save failure"); }
            catch { }
            finally { SaveOriginPolicy.EndNative(); }
            Check(!SaveOriginPolicy.NativeSaving, "Failed native save cannot leak bypass to later free saves");
            SaveOriginPolicy.EndNative();
            Check(!SaveOriginPolicy.NativeSaving, "Extra cleanup cannot create a negative scope");
            return _checks + " native/free save origin assertions passed";
        }
    }
}
