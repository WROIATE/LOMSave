using HarmonyLib;
using Mortal.Core;
using System;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class ReplayAudio
    {
        private static bool _muted;
        private static string _masterVolumeParameter;

        internal static void Mute()
        {
            if (_muted) return;
            try
            {
                _masterVolumeParameter = Traverse.Create(typeof(SystemSettings))
                    .Field("_rtpcMasterVolume").GetValue<string>();
                if (string.IsNullOrEmpty(_masterVolumeParameter))
                    throw new InvalidOperationException("Master volume RTPC is missing.");

                // Change only the Wwise output parameter, never the user's settings.
                SetOutputVolume(0f);
                _muted = true;
                LOMSaveProbePlugin.WriteEvent("story_replay_audio_muted", new { });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError("story_replay_audio_mute_failed", exception);
            }
        }

        internal static void Restore()
        {
            if (!_muted) return;
            try
            {
                SetOutputVolume(SystemSettings.MasterVolume);
                _muted = false;
                LOMSaveProbePlugin.WriteEvent("story_replay_audio_restored",
                    new { volume = SystemSettings.MasterVolume });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError("story_replay_audio_restore_failed", exception);
            }
        }

        private static void SetOutputVolume(float volume)
        {
            var result = AkSoundEngine.SetRTPCValue(
                _masterVolumeParameter, volume, (GameObject)null, 0);
            if (result != AKRESULT.AK_Success)
                throw new InvalidOperationException("Wwise volume update failed: " + result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SystemSettings), "SetMasterVolume")]
        private static void KeepReplayMuted()
        {
            if (!_muted) return;
            try { SetOutputVolume(0f); }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError("story_replay_audio_mute_failed", exception);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SoundManager), "PlaySound")]
        private static bool SkipReplaySound()
        {
            // Do not start transient effects that could outlive the black mask.
            // Music and ambient events still run so their saved state can be restored.
            return !_muted && !StoryCommandTrace.IsReplayPending;
        }
    }
}
