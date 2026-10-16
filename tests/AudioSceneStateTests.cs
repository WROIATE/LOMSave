using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public sealed class Traverse
    {
        private object _source; private string _field;
        public static Traverse Create(object source) { return new Traverse { _source = source }; }
        public Traverse Field(string field) { _field = field; return this; }
        public T GetValue<T>() { return (T)_source.GetType().GetField(_field).GetValue(_source); }
    }
}
namespace UnityEngine
{
    public class GameObject { }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Mathf { public static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); } }
}
public enum AKRESULT { AK_Success, AK_Fail }
public static class AkSoundEngine
{
    public static readonly Dictionary<string, float> Values = new Dictionary<string, float>();
    public static readonly Dictionary<string, float> Targets = new Dictionary<string, float>();
    public static readonly Dictionary<string, int> Durations = new Dictionary<string, int>();
    public static bool PlayingMusic, PlayingEnvironment;
    public static AKRESULT GetRTPCValue(string name, UnityEngine.GameObject obj, uint id, out float value, ref int type)
    { return Values.TryGetValue(name, out value) ? AKRESULT.AK_Success : AKRESULT.AK_Fail; }
    public static AKRESULT SetRTPCValue(string name, float value, UnityEngine.GameObject obj, int delay)
    {
        if (delay == 0) Values[name] = value;
        Targets[name] = value; Durations[name] = delay;
        return AKRESULT.AK_Success;
    }
    public static void StopAll() { PlayingMusic = PlayingEnvironment = false; }
}
namespace Mortal.Core
{
    public sealed class SoundManager
    {
        public static SoundManager Instance = new SoundManager();
        public string _rtpcBackgroundMusic = "Music", _rtpcEnvSound = "Environment", _rtpcMusicLowpass = "Lowpass";
        public string _currentMusic, _currentEnvSound;
        public int EnvironmentStarts;
        public void StopMusic() { _currentMusic = _currentEnvSound = ""; AkSoundEngine.StopAll(); }
        public void PlayMusic(string name) { if (_currentMusic == name) return; _currentMusic = name; AkSoundEngine.PlayingMusic = true; AkSoundEngine.Values["Music"] = 100; }
        public void PlayEnvSound(string name)
        {
            if (_currentEnvSound == name) return;
            _currentEnvSound = name; EnvironmentStarts++; AkSoundEngine.PlayingEnvironment = true; AkSoundEngine.Values["Environment"] = 100;
        }
        public void FadeOutEnvSound(int value, int duration) { }
    }
}
namespace LOMSave.Probe
{
    public static class AudioSceneStateTests
    {
        private static int _assertions;
        private static void Check(bool pass, string description) { _assertions++; if (!pass) throw new Exception(description); }
        public static string Run()
        {
            var manager = Mortal.Core.SoundManager.Instance;
            manager.PlayMusic("calm"); manager.PlayEnvSound("rain");
            AkSoundEngine.Values["Music"] = 72; AkSoundEngine.Values["Environment"] = 0; AkSoundEngine.Values["Lowpass"] = 24;
            var saved = SceneAudio.Capture();
            Check(saved.Environment.Value == 0, "A silent rain loop must be saved as silent despite its cached name");
            manager.PlayEnvSound("wind");
            SceneAudio.Restore("calm", "rain", saved);
            Check(AkSoundEngine.Values["Environment"] == 0, "Loading must not resurrect rain at full volume");
            Check(AkSoundEngine.Values["Music"] == 72, "Preserve music channel volume");
            Check(AkSoundEngine.Values["Lowpass"] == 24, "Preserve music lowpass");
            var starts = manager.EnvironmentStarts;
            SceneAudio.Restore("calm", "rain", saved);
            Check(manager.EnvironmentStarts == starts + 1 && AkSoundEngine.PlayingEnvironment, "Same-name reload must restart its stopped loop");
            SceneAudio.Restore(null, null, saved);
            Check(!AkSoundEngine.PlayingEnvironment && !AkSoundEngine.PlayingMusic, "Silent saves clear both previous tracks");
            Check(manager._currentEnvSound == "", "Stopped ambience must not retain a stale manager cache");
            manager.PlayEnvSound("rain"); AkSoundEngine.Values["Environment"] = 0;
            SceneAudio.Restore("calm", "rain", null);
            Check(AkSoundEngine.Values["Environment"] == 0, "Legacy sidecars inherit replayed fade state");
            AkSoundEngine.Values["Environment"] = 60; UnityEngine.Time.realtimeSinceStartup = 5;
            typeof(SceneAudio).GetMethod("TrackFade", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null,
                new object[] { manager, typeof(Mortal.Core.SoundManager).GetMethod("FadeOutEnvSound"), 0, 4000 });
            UnityEngine.Time.realtimeSinceStartup = 6;
            saved = SceneAudio.Capture();
            Check(saved.Environment.Target == 0 && saved.Environment.RemainingMilliseconds == 3000, "Save a pending fade target and remaining wall time");
            SceneAudio.Restore("calm", "rain", saved);
            Check(AkSoundEngine.Values["Environment"] == 60 && AkSoundEngine.Targets["Environment"] == 0
                && AkSoundEngine.Durations["Environment"] == 3000, "Restore in-progress fade after starting the sound");
            UnityEngine.Time.realtimeSinceStartup = 10; AkSoundEngine.Values["Environment"] = 0;
            Check(SceneAudio.Capture().Environment.RemainingMilliseconds == 0, "Expired fade must not be replayed");
            AkSoundEngine.Values.Remove("Environment");
            Check(SceneAudio.Capture().Environment == null, "Failed RTPC query is unknown rather than an invented volume");
            Mortal.Core.SoundManager.Instance = null;
            Check(SceneAudio.Capture() == null, "Missing sound manager is supported");
            SceneAudio.Restore("calm", "rain", saved);
            return _assertions + " audio restoration assertions passed";
        }
    }
}
