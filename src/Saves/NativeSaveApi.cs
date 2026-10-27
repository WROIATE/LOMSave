using HarmonyLib;
using Mortal.Core;

namespace LOMSave.Probe
{
    internal static class NativeSaveApi
    {
        // 1.0.6001 added includeStoryLog. Reflection does not supply defaults.
        // Capture gameplay first, then the complete current log explicitly;
        // CreateSaveData(true) truncates story logs at the native restart point.
        internal static GameSave Capture(SaveSystem system)
        {
            var save = Traverse.Create(system).Method("CreateSaveData", new[] { typeof(bool) }).GetValue<GameSave>(false);
            StoryLogData.Save(save, false);
            return save;
        }

        internal static void Load(GameSave save)
        {
            Traverse.Create(SaveSystem.Instance).Method("ExecuteLoadGameData", new[] { typeof(GameSave) }).GetValue(save);
            // Native LoadGameData does this outside ExecuteLoadGameData.
            StoryLogData.Load(save);
        }
    }
}
