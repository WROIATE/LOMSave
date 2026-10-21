using System;

namespace LOMSave.Probe
{
    // Track the game's request before the mod unlocks its menu button. A
    // previous scene's menu must never authorize a save in a new story.
    internal static class SaveOriginPolicy
    {
        private static object _menu;
        private static bool _allowed;
        private static int _nativeDepth;
        internal static bool NativeSaving { get { return _nativeDepth > 0; } }
        internal static void Observe(object menu, bool allowed)
        { _menu = menu; _allowed = allowed; }
        internal static bool UseNative(string scene, object currentMenu)
        { return scene != "Story" || currentMenu != null && ReferenceEquals(currentMenu, _menu) && _allowed; }
        internal static void BeginNative() { _nativeDepth++; }
        internal static void EndNative() { _nativeDepth = Math.Max(0, _nativeDepth - 1); }
    }
}
