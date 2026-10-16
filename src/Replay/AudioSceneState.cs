using System;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;

namespace LOMSave.Probe
{
    internal sealed class AudioLevel
    {
        public float Value;
        public float Target;
        public int RemainingMilliseconds;
    }

    internal sealed class AudioSceneState
    {
        public AudioLevel Music, Environment, LowPass;
    }

    internal static class SceneAudio
    {
        private sealed class Fade
        {
            internal float Target, EndsAt;
        }
        private static readonly System.Collections.Generic.Dictionary<string, Fade> Fades =
            new System.Collections.Generic.Dictionary<string, Fade>();

        private static string Parameter(SoundManager manager, string field)
        { return Traverse.Create(manager).Field(field).GetValue<string>(); }

        private static float? Read(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return null;
            float value;
            int type = 1;
            return AkSoundEngine.GetRTPCValue(parameter, (GameObject)null, 0, out value, ref type) == AKRESULT.AK_Success
                ? (float?)value : null;
        }

        private static AudioLevel CaptureLevel(SoundManager manager, string field)
        {
            var parameter = Parameter(manager, field);
            var value = Read(parameter);
            if (!value.HasValue) return null;
            Fade fade;
            var remaining = parameter != null && Fades.TryGetValue(parameter, out fade)
                ? Math.Max(0, (int)((fade.EndsAt - Time.realtimeSinceStartup) * 1000)) : 0;
            return new AudioLevel { Value = value.Value, Target = remaining > 0 ? Fades[parameter].Target : value.Value,
                RemainingMilliseconds = remaining };
        }

        internal static AudioSceneState Capture()
        {
            var manager = SoundManager.Instance;
            if (manager == null) return null;
            return new AudioSceneState { Music = CaptureLevel(manager, "_rtpcBackgroundMusic"),
                Environment = CaptureLevel(manager, "_rtpcEnvSound"), LowPass = CaptureLevel(manager, "_rtpcMusicLowpass") };
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SoundManager), "FadeOutMusic")]
        [HarmonyPatch(typeof(SoundManager), "FadeOutEnvSound")]
        [HarmonyPatch(typeof(SoundManager), "FadeInMusic")]
        [HarmonyPatch(typeof(SoundManager), "FadeInEnvSound")]
        [HarmonyPatch(typeof(SoundManager), "FadeMusicLowPass")]
        private static void TrackFade(SoundManager __instance, System.Reflection.MethodBase __originalMethod, int __0, int __1)
        {
            var method = __originalMethod.Name;
            var field = method == "FadeMusicLowPass" ? "_rtpcMusicLowpass"
                : method.EndsWith("EnvSound", StringComparison.Ordinal) ? "_rtpcEnvSound" : "_rtpcBackgroundMusic";
            var parameter = Parameter(__instance, field);
            if (string.IsNullOrEmpty(parameter)) return;
            // FadeOut does nothing when already below the requested volume.
            var value = Read(parameter);
            if (method.StartsWith("FadeOut", StringComparison.Ordinal) && (!value.HasValue || value <= __0)) return;
            Fades[parameter] = new Fade { Target = __0, EndsAt = Time.realtimeSinceStartup + Math.Max(0, __1) / 1000f };
        }

        private static void Apply(SoundManager manager, string field, AudioLevel level)
        {
            if (level == null) return;
            var parameter = Parameter(manager, field);
            if (string.IsNullOrEmpty(parameter)) return;
            var value = Mathf.Clamp(level.Value, 0, 100);
            var target = Mathf.Clamp(level.Target, 0, 100);
            var remaining = Math.Max(0, Math.Min(60000, level.RemainingMilliseconds));
            AkSoundEngine.SetRTPCValue(parameter, value, (GameObject)null, 0);
            if (remaining > 0) AkSoundEngine.SetRTPCValue(parameter, target, (GameObject)null, remaining);
            Fades[parameter] = new Fade { Target = target, EndsAt = Time.realtimeSinceStartup + remaining / 1000f };
        }

        internal static void Restore(string music, string environment, AudioSceneState saved)
        {
            var manager = SoundManager.Instance;
            if (manager == null) return;
            // Older sidecars have names only. Use the replayed channel volumes,
            // which include Lua fade commands, instead of forcing them to 100.
            var levels = saved ?? Capture();
            manager.StopMusic();
            AkSoundEngine.StopAll();
            Fades.Clear();
            if (!string.IsNullOrEmpty(music)) manager.PlayMusic(music);
            if (!string.IsNullOrEmpty(environment)) manager.PlayEnvSound(environment);
            if (levels != null)
            {
                Apply(manager, "_rtpcBackgroundMusic", levels.Music);
                Apply(manager, "_rtpcEnvSound", levels.Environment);
                Apply(manager, "_rtpcMusicLowpass", levels.LowPass);
            }
        }
    }
}
