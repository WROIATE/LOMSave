using UnityEngine;
using System;
using System.Diagnostics;

namespace LOMSave.Probe
{
    internal static class ReplayRuntime
    {
        private const float ReplayTimeScale = 50f;
        private const float MaskTimeoutSeconds = 30f;
        private const float SceneLoadTimeoutSeconds = 30f;
        private const float MaximumRecoverySeconds = 120f;

        private static float _timeScaleBeforeReplay = 1f;
        private static bool _speedActive;
        private static bool _maskVisible;
        private static double _maskShownAt;
        private static double _recoveryStartedAt;
        private static double _lastProgressAt;
        private static float _timeoutSeconds = MaskTimeoutSeconds;
        internal static Func<double> Clock = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        internal static int Generation { get; private set; }
        internal static string Phase { get; private set; } = "ready";

        internal static bool IsRestoring { get { return _maskVisible; } }
        internal static bool IsReplaying { get { return _speedActive; } }
        internal static void ReportProgress() { _lastProgressAt = Clock(); }

        internal static void Start()
        {
            if (_speedActive)
            {
                return;
            }

            _timeScaleBeforeReplay = Time.timeScale > 0f ? Time.timeScale : 1f;
            _speedActive = true;
            Time.timeScale = ReplayTimeScale;
            ReplayAudio.Mute();
            ShowMask();
            // Give scene loading its own grace period; the original replay
            // deadline starts when Lua actually begins to replay the script.
            Phase = "replay";
            ReportProgress();
            _timeoutSeconds = MaskTimeoutSeconds;
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
            Phase = "presentation";
            ReportProgress();
            LOMSaveProbePlugin.WriteEvent("story_replay_speed_restored", new
            {
                restoredTimeScale = Time.timeScale
            });
        }

        internal static void Reset(string reason)
        {
            Generation++;
            RestoreSpeed();
            HideMask(reason);
        }

        internal static void Update()
        {
            // Menu closure and loading callbacks may reset the game's global speed.
            if (_speedActive) Time.timeScale = ReplayTimeScale;
            if (!Application.isFocused) ReportProgress();
            HandleTimeout();
        }

        internal static void DrawMask()
        {
            HandleTimeout();
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

        private static void HandleTimeout()
        {
            // Focus changes may suspend the inactivity timer, but must never
            // keep a failed recovery masked forever.
            var totalExpired = Clock() - _recoveryStartedAt >= MaximumRecoverySeconds;
            if (!_maskVisible || (!totalExpired && (!Application.isFocused
                || Clock() - _lastProgressAt < _timeoutSeconds)))
            {
                return;
            }

            LOMSaveProbePlugin.WriteEvent("story_replay_mask_timeout", new
            {
                timeoutSeconds = totalExpired ? MaximumRecoverySeconds : _timeoutSeconds,
                reason = totalExpired ? "recovery_deadline" : "no_progress"
            });
            // Invalidate any suspended Lua/presentation work before releasing
            // the mask. A late asset completion must not mutate a failed load.
            Generation++;
            if (!StoryCommandTrace.AbortActiveReplay("timeout"))
            {
                RestoreSpeed();
            }
            HideMask("timeout");
        }

        internal static void HideMask(string reason)
        {
            ReplayAudio.Restore();
            if (!_maskVisible)
            {
                return;
            }

            _maskVisible = false;
            Phase = reason == "presentation_restored" ? "ready" : reason;
            LOMSaveProbePlugin.WriteEvent("story_replay_mask_hidden", new
            {
                reason,
                visibleSeconds = Clock() - _maskShownAt,
                generation = Generation,
                phase = Phase
            });
        }

        internal static void Prepare()
        {
            Generation++;
            Phase = "loading";
            ReplayAudio.Mute();
            _timeoutSeconds = SceneLoadTimeoutSeconds;
            ShowMask();
        }

        private static void ShowMask()
        {
            if (_maskVisible)
            {
                return;
            }

            _maskShownAt = Clock();
            _recoveryStartedAt = Clock();
            ReportProgress();
            _maskVisible = true;
            LOMSaveProbePlugin.WriteEvent("story_replay_mask_shown", new
            {
                timeoutSeconds = _timeoutSeconds
            });
        }
    }
}
