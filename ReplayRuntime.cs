using UnityEngine;

namespace LOMSave.Probe
{
    internal static class ReplayRuntime
    {
        private const float ReplayTimeScale = 50f;
        private const float MaskTimeoutSeconds = 30f;

        private static float _timeScaleBeforeReplay = 1f;
        private static bool _speedActive;
        private static bool _maskVisible;
        private static float _maskShownAt;

        internal static void Start()
        {
            if (_speedActive)
            {
                return;
            }

            _timeScaleBeforeReplay = Time.timeScale > 0f ? Time.timeScale : 1f;
            _speedActive = true;
            Time.timeScale = ReplayTimeScale;
            ShowMask();
            LOMSaveProbePlugin.WriteEvent("story_replay_speed_started", new
            {
                previousTimeScale = _timeScaleBeforeReplay,
                replayTimeScale = Time.timeScale
            });
        }

        internal static void RestoreSpeed()
        {
            if (!_speedActive)
            {
                return;
            }

            Time.timeScale = _timeScaleBeforeReplay;
            _speedActive = false;
            LOMSaveProbePlugin.WriteEvent("story_replay_speed_restored", new
            {
                restoredTimeScale = Time.timeScale
            });
        }

        internal static void Reset(string reason)
        {
            RestoreSpeed();
            HideMask(reason);
        }

        internal static void Update()
        {
            if (!_maskVisible
                || Time.unscaledTime - _maskShownAt < MaskTimeoutSeconds)
            {
                return;
            }

            LOMSaveProbePlugin.WriteEvent("story_replay_mask_timeout", new
            {
                timeoutSeconds = MaskTimeoutSeconds
            });
            RestoreSpeed();
            HideMask("timeout");
        }

        internal static void DrawMask()
        {
            if (!_maskVisible)
            {
                return;
            }

            var previousColor = GUI.color;
            var previousDepth = GUI.depth;
            GUI.depth = int.MinValue;
            GUI.color = Color.black;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        internal static void HideMask(string reason)
        {
            if (!_maskVisible)
            {
                return;
            }

            _maskVisible = false;
            LOMSaveProbePlugin.WriteEvent("story_replay_mask_hidden", new
            {
                reason,
                visibleSeconds = Time.unscaledTime - _maskShownAt
            });
        }

        private static void ShowMask()
        {
            if (_maskVisible)
            {
                return;
            }

            _maskShownAt = Time.unscaledTime;
            _maskVisible = true;
            LOMSaveProbePlugin.WriteEvent("story_replay_mask_shown", new
            {
                timeoutSeconds = MaskTimeoutSeconds
            });
        }
    }
}
