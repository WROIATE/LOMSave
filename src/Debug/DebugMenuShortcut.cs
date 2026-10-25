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

        internal static bool IsOpen { get { return _panel != null && _panel.activeInHierarchy; } }
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
            // Existing installations retain their trainer's configured shortcut.
            // Check only when pressed, after all plugins have finished loading.
            if (HasTrainer() || !CanOpen()) return;
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
                if (_story != null) Traverse.Create(_story).Property("IsStoryPause").SetValue(true);
                Time.timeScale = 0;
                _panel.SetActive(true);
                for (var i = 0; i < _panel.transform.childCount; i++)
                    _panel.transform.GetChild(i).gameObject.SetActive(true);
                LOMSaveProbePlugin.WriteEvent("debug_shortcut_opened", new { nativePanel = true });
            }
            catch (Exception error)
            {
                Close();
                LOMSaveProbePlugin.WriteError("debug_shortcut_failed", error);
            }
        }

        private static bool CanOpen()
        {
            if (_panel == null || !Application.isFocused || FeaturePanel.BlocksInput || SaveNotes.IsOpen
                || EventInspectorPanel.BlocksInput || OptionImpactPreview.IsOpen || AutoDialogueSpeed.SettingsVisible
                || ReplayRuntime.IsRestoring || ReplayRuntime.IsReplaying || StoryCommandTrace.IsReplayPending
                || ReplayStateValidator.IsRestoring) return false;
            if (VisibleParent() == null) return false;
            var scene = SceneController.Instance;
            if (scene == null || scene.IsPrepare || scene.IsLoading) return false;
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.GetComponentInParent<InputField>() != null) return false;
            var name = SceneManager.GetActiveScene().name;
            if (name == "Free")
                return FreeManager.Instance != null && FreeManager.Instance.EnableHotKey && !FreeManager.Instance.IsProcessing;
            var story = StoryManager.Instance;
            return name == "Story" && story != null && story.EnableAction && !story.IsStoryPause
                && !EventInspector.Read<bool>(story, "_panelOpen") && !EventInspector.Read<bool>(story, "_logOpen");
        }

        internal static void Close()
        {
            if (!_owned) return;
            _owned = false;
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
