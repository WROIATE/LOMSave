using System;
using System.Reflection;
// Test doubles for the engine boundary. The tests compile the real
// ReplayAudio.cs and ReplayRuntime.cs alongside these deterministic adapters.
namespace HarmonyLib
{
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string name) { } }
    public sealed class Traverse
    {
        private Type type;
        private string field;
        public static Traverse Create(Type t) { return new Traverse { type = t }; }
        public Traverse Field(string name) { field = name; return this; }
        public T GetValue<T>() { return (T)type.GetField(field).GetValue(null); }
    }
}
namespace UnityEngine
{
    public sealed class GameObject { }
    public static class Time { public static float timeScale = 1f, unscaledTime; }
    public static class Application { public static bool isFocused = true; }
    public struct Color { public static Color black; }
    public struct Rect { public Rect(float x, float y, float w, float h) { } }
    public static class Screen { public static int width, height; }
    public sealed class Texture2D { public static Texture2D whiteTexture; }
    public static class GUI
    {
        public static int depth;
        public static Color color;
        public static void DrawTexture(Rect rect, Texture2D texture) { }
    }
}
namespace Mortal.Core
{
    public static class SystemSettings
    {
        public static string _rtpcMasterVolume = "MasterVolume";
        public static int MasterVolume = 73;
    }
    public sealed class SoundManager { }
}
public enum AKRESULT { AK_Success, AK_Fail }
public static class AkSoundEngine
{
    public static float OutputVolume = 73f;
    public static bool Fail;
    public static AKRESULT SetRTPCValue(string name, float volume, UnityEngine.GameObject obj, int delay)
    {
        if (Fail) return AKRESULT.AK_Fail;
        OutputVolume = volume;
        return AKRESULT.AK_Success;
    }
}
namespace LOMSave.Probe
{
    internal static class LOMSaveProbePlugin
    {
        public static int Errors;
        public static void WriteEvent(string name, object data) { }
        public static void WriteError(string name, Exception error) { Errors++; }
    }
    internal static class StoryCommandTrace
    {
        public static bool IsReplayPending;
        public static int Aborts;
        public static bool AbortActiveReplay(string reason)
        {
            Aborts++;
            IsReplayPending = false;
            ReplayRuntime.RestoreSpeed();
            return true;
        }
    }
    public static class ReplayRuntimeTests
    {
        private static int assertions;
        private static void Check(bool passed, string message)
        {
            assertions++;
            if (!passed) throw new Exception(message);
        }
        private static bool SoundAllowed()
        {
            return (bool)typeof(ReplayAudio).GetMethod("SkipReplaySound",
                BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        }
        public static string Run()
        {
            ReplayRuntime.Prepare();
            Check(AkSoundEngine.OutputVolume == 0f, "Loading must be silent");
            Check(Mortal.Core.SystemSettings.MasterVolume == 73, "Do not overwrite user volume");
            Check(!SoundAllowed(), "Do not queue transient replay effects");
            UnityEngine.Time.unscaledTime = 20f;
            ReplayRuntime.Update();
            Check(AkSoundEngine.OutputVolume == 0f, "Scene loading has a separate grace period");
            ReplayRuntime.Start();
            Check(UnityEngine.Time.timeScale == 50f, "Replay acceleration");
            ReplayRuntime.RestoreSpeed();
            Check(UnityEngine.Time.timeScale == 1f && AkSoundEngine.OutputVolume == 0f,
                "Reaching checkpoint must stay silent until presentation finishes");
            Mortal.Core.SystemSettings.MasterVolume = 41;
            typeof(ReplayAudio).GetMethod("KeepReplayMuted", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);
            Check(AkSoundEngine.OutputVolume == 0f, "Setting refresh cannot unmute replay");
            ReplayRuntime.HideMask("presentation_restored");
            Check(AkSoundEngine.OutputVolume == 41f && SoundAllowed(), "Restore latest user volume");
            ReplayRuntime.Reset("repeated_cleanup");
            Check(AkSoundEngine.OutputVolume == 41f, "Cleanup is idempotent");

            ReplayRuntime.Prepare();
            UnityEngine.Time.unscaledTime += 31f;
            ReplayRuntime.Update();
            Check(AkSoundEngine.OutputVolume == 41f && StoryCommandTrace.Aborts == 1,
                "Scene load timeout must release audio");
            ReplayRuntime.Prepare();
            ReplayRuntime.Start();
            UnityEngine.Time.unscaledTime += 20f;
            ReplayRuntime.ReportProgress();
            UnityEngine.Time.unscaledTime += 20f;
            ReplayRuntime.Update();
            Check(ReplayRuntime.IsRestoring, "Progress renews replay deadline beyond total 30 seconds");
            UnityEngine.Time.timeScale = 0f;
            ReplayRuntime.Update();
            Check(UnityEngine.Time.timeScale == 50f, "Loading callbacks cannot pause replay");
            UnityEngine.Application.isFocused = false;
            UnityEngine.Time.unscaledTime += 50f;
            ReplayRuntime.Update();
            Check(ReplayRuntime.IsRestoring, "Losing focus must not cause a false timeout");
            UnityEngine.Application.isFocused = true;
            UnityEngine.Time.unscaledTime += 31f;
            ReplayRuntime.DrawMask();
            Check(AkSoundEngine.OutputVolume == 41f && UnityEngine.Time.timeScale == 1f,
                "Render-path replay timeout must release audio and speed");

            ReplayRuntime.Prepare();
            UnityEngine.Application.isFocused = false;
            UnityEngine.Time.unscaledTime += 121f;
            ReplayRuntime.Update();
            Check(!ReplayRuntime.IsRestoring && AkSoundEngine.OutputVolume == 41f,
                "A replay that never starts must release its mask even while unfocused");
            UnityEngine.Application.isFocused = true;
            ReplayRuntime.Prepare();
            ReplayRuntime.Start();
            UnityEngine.Time.unscaledTime += 121f;
            ReplayRuntime.ReportProgress();
            ReplayRuntime.Update();
            Check(!ReplayRuntime.IsRestoring && UnityEngine.Time.timeScale == 1f,
                "Repeated progress cannot extend recovery beyond its absolute deadline");

            ReplayRuntime.Prepare();
            ReplayRuntime.Start();
            ReplayRuntime.Reset("plugin_destroyed");
            Check(AkSoundEngine.OutputVolume == 41f && UnityEngine.Time.timeScale == 1f,
                "Plugin destruction must restore engine state");
            AkSoundEngine.Fail = true;
            ReplayRuntime.Prepare();
            Check(LOMSaveProbePlugin.Errors == 1, "Native mute failures must be reported");
            AkSoundEngine.Fail = false;
            ReplayRuntime.Reset("failed_prepare");
            Check(SoundAllowed(), "Failed mute must not leave a stale suppression flag");
            Mortal.Core.SystemSettings.MasterVolume = 0;
            ReplayRuntime.Prepare();
            ReplayRuntime.HideMask("saved_silence");
            Check(AkSoundEngine.OutputVolume == 0f, "Preserve a user's zero volume");
            return assertions + " replay lifecycle assertions passed";
        }
    }
}
