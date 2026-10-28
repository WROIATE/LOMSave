using System;
using BepInEx.Unity.Mono.Bootstrap;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    // Open the game's own panel. Its rows and edits are supplied by the native
    // game and DebugMenuEnhancements; no third-party trainer is distributed.
    internal static class DebugMenuShortcut
    {
        private static GameObject _panel;
        private static bool _owned, _storyPause;
        private static float _speed;
        private static StoryManager _story;
        private static Transform _parent;
        private static int _sibling;
        private static float _closedAt = -1;

        internal static bool IsOpen { get { return _panel != null && _panel.activeInHierarchy; } }
        internal static bool BlocksTrainingInput { get { return IsOpen || Time.unscaledTime - _closedAt < 0.2f; } }
        internal static bool Contains(Transform item)
        { return _panel != null && item != null && item.IsChildOf(_panel.transform); }

        internal static void Attach(GameObject panel)
        {
            if (_panel != panel) Close();
            _panel = panel;
            // Release builds destroy DevelopmentOnly objects on first Start.
            // Retain only the explicitly exposed test panel, not other dev UI.
            if (panel != null)
                foreach (var marker in panel.GetComponentsInChildren<DevelopmentOnly>(true)) marker.enabled = false;
            if (panel != null && panel.GetComponent<DebugMenuShortcutLifetime>() == null)
                panel.AddComponent<DebugMenuShortcutLifetime>();
        }

        internal static bool HasTrainer()
        {
            var loader = UnityChainloader.Instance;
            BepInEx.PluginInfo info;
            if (loader == null || !loader.Plugins.TryGetValue("ScriptTrainer.Jim97.Mortal", out info)) return false;
            var trainer = info.Instance as Behaviour;
            return trainer != null && trainer.isActiveAndEnabled;
        }

        internal static void Update()
        {
            if (!Application.isFocused) return;
            if (_owned && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.F9)) Toggle();
        }

        internal static void Toggle()
        {
            if (_owned) { Close(); return; }
            // Awake can bind another additive scene's panel, which is later
            // unloaded. Reacquire the current scene's panel on demand only.
            try { EnsureCurrentPanel(); TrainingPointLimits.Discover(); }
            catch (Exception error) { LOMSaveProbePlugin.WriteError("debug_shortcut_bind_failed", error); return; }
            var blocked = HasTrainer() ? "external_trainer" : BlockedReason();
            if (blocked != null)
            {
                LOMSaveProbePlugin.WriteEvent("debug_shortcut_blocked", new { reason = blocked });
                return;
            }
            _speed = Time.timeScale;
            _story = StoryManager.Instance;
            _storyPause = _story != null && _story.IsStoryPause;
            _owned = true;
            try
            {
                _parent = _panel.transform.parent;
                _sibling = _panel.transform.GetSiblingIndex();
                // The native test panel lives inside the normally hidden status
                // page. Display it under the nearest live canvas ancestor without
                // opening that page or firing its unrelated enable callbacks.
                var host = VisibleParent();
                if (host != _parent) _panel.transform.SetParent(host, false);
                _panel.transform.SetAsLastSibling();
                if (_story != null) Traverse.Create(_story).Property("IsStoryPause").SetValue(true);
                Time.timeScale = 0;
                var view = _panel.GetComponent<DebugMenuView>() ?? _panel.AddComponent<DebugMenuView>();
                _panel.SetActive(true);
                for (var i = 0; i < _panel.transform.childCount; i++)
                    _panel.transform.GetChild(i).gameObject.SetActive(true);
                // Native Awake callbacks need active rows, but the view keeps
                // them invisible until the enhanced layout is ready this frame.
                if (!view.PrepareForDisplay())
                    throw new InvalidOperationException("Debug menu layout is not ready.");
                LOMSaveProbePlugin.WriteEvent("debug_shortcut_opened", new { nativePanel = true });
            }
            catch (Exception error)
            {
                Close();
                LOMSaveProbePlugin.WriteError("debug_shortcut_failed", error);
            }
        }

        private static void EnsureCurrentPanel()
        {
            var active = SceneManager.GetActiveScene();
            if (active.name != "Free" && active.name != "Story") return;
            if (_panel != null && _panel.scene == active) return;
            foreach (var status in Resources.FindObjectsOfTypeAll<PlayerStatusPanel>())
            {
                if (status == null || status.gameObject.scene != active) continue;
                var panel = EventInspector.Read<GameObject>(status, "_testPanel");
                if (panel == null) continue;
                DebugMenuEnhancements.Attach(panel);
                LOMSaveProbePlugin.WriteEvent("debug_shortcut_rebound", new { scene = active.name });
                return;
            }
            // Never expose a panel from a different or unloading scene.
            Attach(null);
        }

        private static string BlockedReason()
        {
            if (_panel == null) return "panel_missing";
            if (!Application.isFocused) return "unfocused";
            if (FeaturePanel.BlocksInput) return "feature_panel";
            if (SaveNotes.IsOpen) return "save_notes";
            if (EventInspectorPanel.BlocksInput) return "event_preview";
            if (OptionImpactPreview.IsOpen) return "option_preview";
            if (AutoDialogueSpeed.SettingsVisible) return "settings";
            if (ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying || StoryCommandTrace.IsReplayPending
                || ReplayStateValidator.IsRestoring) return "restoring";
            if (VisibleParent() == null) return "panel_host_hidden";
            var scene = SceneController.Instance;
            if (scene == null || scene.IsPrepare || scene.IsLoading) return "scene_loading";
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.GetComponentInParent<InputField>() != null) return "text_input";
            var name = SceneManager.GetActiveScene().name;
            // Training panels deliberately disable the map/story hotkeys. They
            // are a valid F9 entry, not evidence of a loading/replay operation.
            if ((name == "Free" || name == "Story") && TrainingPointLimits.HasOpenPanel()) return null;
            if (name == "Free")
                return FreeManager.Instance != null && FreeManager.Instance.EnableHotKey && !FreeManager.Instance.IsProcessing
                    ? null : "free_action_or_menu";
            var story = StoryManager.Instance;
            if (name != "Story" || story == null) return "unsupported_scene";
            if (!story.EnableAction) return "story_action_disabled";
            if (story.IsStoryPause || EventInspector.Read<bool>(story, "_panelOpen")) return "story_menu";
            return EventInspector.Read<bool>(story, "_logOpen") ? "story_log" : null;
        }

        internal static void Close()
        {
            if (!_owned) return;
            _owned = false;
            _closedAt = Time.unscaledTime;
            try { if (_panel != null) _panel.SetActive(false); }
            finally
            {
                if (_panel != null && _parent != null)
                {
                    _panel.transform.SetParent(_parent, false);
                    _panel.transform.SetSiblingIndex(_sibling);
                }
                _parent = null;
                Time.timeScale = _speed;
                if (_story != null) Traverse.Create(_story).Property("IsStoryPause").SetValue(_storyPause);
                _story = null;
                try { TrainingPointLimits.RefreshOpenPanels(); }
                catch (Exception error) { LOMSaveProbePlugin.WriteError("debug_training_refresh_failed", error); }
                LOMSaveProbePlugin.WriteEvent("debug_shortcut_closed", new { });
            }
        }

        private static Transform VisibleParent()
        {
            var parent = _panel.transform.parent;
            while (parent != null && !parent.gameObject.activeInHierarchy) parent = parent.parent;
            return parent;
        }

        internal static void PanelClosed(GameObject panel)
        { if (ReferenceEquals(_panel, panel)) Close(); }
    }

    internal sealed class DebugMenuShortcutLifetime : MonoBehaviour
    {
        private void OnDisable() { DebugMenuShortcut.PanelClosed(gameObject); }
        private void OnDestroy() { DebugMenuShortcut.PanelClosed(gameObject); }
    }
}
