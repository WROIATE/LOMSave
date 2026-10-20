using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float timeScale = 1; }
    public enum KeyCode { None, F9, Escape }
    public static class Input { public static KeyCode Pressed; public static bool GetKeyDown(KeyCode key) { return key == Pressed; } }
    public class Behaviour { public bool enabled = true; public bool isActiveAndEnabled { get { return enabled; } } }
    public class MonoBehaviour : Behaviour { public GameObject gameObject; }
    public class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public readonly List<Transform> Children = new List<Transform>();
        public int childCount { get { return Children.Count; } }
        public Transform GetChild(int index) { return Children[index]; }
        public bool IsChildOf(Transform value) { for (var item = this; item != null; item = item.parent) if (item == value) return true; return false; }
        public int GetSiblingIndex() { return parent == null ? 0 : parent.Children.IndexOf(this); }
        public void SetSiblingIndex(int index) { if (parent == null) return; parent.Children.Remove(this); parent.Children.Insert(Math.Max(0, Math.Min(index, parent.Children.Count)), this); }
        public void SetParent(Transform next, bool worldPositionStays) { if (parent != null) parent.Children.Remove(this); parent = next; if (next != null) next.Children.Add(this); }
    }
    public class GameObject
    {
        public bool activeSelf, InputField, FailOnOpen;
        public readonly Transform transform;
        private readonly Dictionary<Type, MonoBehaviour> _components = new Dictionary<Type, MonoBehaviour>();
        public GameObject(bool active = true) { activeSelf = active; transform = new Transform { gameObject = this }; }
        public bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        public T GetComponent<T>() where T : MonoBehaviour { MonoBehaviour value; return _components.TryGetValue(typeof(T), out value) ? (T)value : null; }
        public T AddComponent<T>() where T : MonoBehaviour, new() { var value = new T { gameObject = this }; _components[typeof(T)] = value; return value; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : MonoBehaviour
        {
            var result = new List<T>();
            foreach (var component in _components.Values) if (component is T) result.Add((T)component);
            foreach (var child in transform.Children) result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
        public T GetComponentInParent<T>() where T : class, new() { return InputField && typeof(T) == typeof(UI.InputField) ? new T() : null; }
        public void SetActive(bool value)
        {
            if (value && FailOnOpen) throw new InvalidOperationException("native activation failed");
            if (activeSelf == value) return;
            activeSelf = value;
            if (!value) Notify("OnDisable");
        }
        public void Notify(string method)
        { foreach (var component in _components.Values) component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null); }
    }
}
namespace UnityEngine.UI { public class InputField { } }
namespace UnityEngine.EventSystems
{
    public class EventSystem { public static EventSystem current = new EventSystem(); public UnityEngine.GameObject currentSelectedGameObject; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager { public static string Name = "Story"; public static Scene GetActiveScene() { return new Scene { name = Name }; } }
}
namespace BepInEx { public class PluginInfo { public object Instance; } }
namespace BepInEx.Unity.Mono.Bootstrap
{
    public class UnityChainloader { public static UnityChainloader Instance; public readonly Dictionary<string, BepInEx.PluginInfo> Plugins = new Dictionary<string, BepInEx.PluginInfo>(); }
}
namespace HarmonyLib
{
    public class Traverse
    {
        private object _value; private string _property;
        public static Traverse Create(object value) { return new Traverse { _value = value }; }
        public Traverse Property(string name) { _property = name; return this; }
        public void SetValue(object value) { _value.GetType().GetProperty(_property).SetValue(_value, value, null); }
    }
}
namespace Mortal.Core
{
    public class DevelopmentOnly : UnityEngine.MonoBehaviour { }
    public class SceneController { public static SceneController Instance; public bool IsPrepare, IsLoading; }
}
namespace Mortal.Free
{
    public class FreeManager { public static FreeManager Instance; public bool EnableHotKey = true, IsProcessing; }
}
namespace Mortal.Story
{
    public class StoryManager { public static StoryManager Instance; public bool EnableAction = true, _panelOpen, _logOpen; public bool IsStoryPause { get; set; } }
}
namespace LOMSave.Probe
{
    internal static class FeaturePanel { internal static bool Blocked; internal static bool BlocksInput { get { return Blocked || DebugMenuShortcut.IsOpen; } } }
    internal static class SaveNotes { internal static bool IsOpen; }
    internal static class EventInspectorPanel { internal static bool BlocksInput; }
    internal static class OptionImpactPreview { internal static bool IsOpen; }
    internal static class AutoDialogueSpeed { internal static bool SettingsVisible; }
    internal static class ReplayRuntime { internal static bool IsRestoring, IsReplaying; }
    internal static class StoryCommandTrace { internal static bool IsReplayPending; }
    internal static class ReplayStateValidator { internal static bool IsRestoring; }
    internal static class EventInspector { internal static T Read<T>(object value, string field) { return (T)value.GetType().GetField(field).GetValue(value); } }
    internal static class LOMSaveProbePlugin
    {
        internal static int Opened, Closed, Errors;
        internal static void WriteEvent(string name, object value) { if (name == "debug_shortcut_opened") Opened++; if (name == "debug_shortcut_closed") Closed++; }
        internal static void WriteError(string name, Exception error) { Errors++; }
    }
    public static class DebugMenuShortcutTests
    {
        private static int _checks;
        private static UnityEngine.GameObject _panel;
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
        private static void Press(UnityEngine.KeyCode key) { UnityEngine.Input.Pressed = key; DebugMenuShortcut.Update(); UnityEngine.Input.Pressed = UnityEngine.KeyCode.None; }
        private static void Reset()
        {
            DebugMenuShortcut.Close();
            UnityEngine.Application.isFocused = true;
            UnityEngine.Time.timeScale = 1.75f;
            UnityEngine.Input.Pressed = UnityEngine.KeyCode.None;
            UnityEngine.SceneManagement.SceneManager.Name = "Story";
            UnityEngine.EventSystems.EventSystem.current = new UnityEngine.EventSystems.EventSystem();
            Mortal.Core.SceneController.Instance = new Mortal.Core.SceneController();
            Mortal.Free.FreeManager.Instance = new Mortal.Free.FreeManager();
            Mortal.Story.StoryManager.Instance = new Mortal.Story.StoryManager();
            BepInEx.Unity.Mono.Bootstrap.UnityChainloader.Instance = null;
            FeaturePanel.Blocked = SaveNotes.IsOpen = EventInspectorPanel.BlocksInput = OptionImpactPreview.IsOpen = AutoDialogueSpeed.SettingsVisible = false;
            ReplayRuntime.IsRestoring = ReplayRuntime.IsReplaying = StoryCommandTrace.IsReplayPending = ReplayStateValidator.IsRestoring = false;
            LOMSaveProbePlugin.Opened = LOMSaveProbePlugin.Closed = LOMSaveProbePlugin.Errors = 0;
            _panel = new UnityEngine.GameObject(false);
            _panel.transform.parent = new UnityEngine.GameObject().transform;
            _panel.transform.Children.Add(new UnityEngine.GameObject(false).transform);
            _panel.AddComponent<Mortal.Core.DevelopmentOnly>();
            _panel.transform.GetChild(0).gameObject.AddComponent<Mortal.Core.DevelopmentOnly>();
            DebugMenuShortcut.Attach(_panel);
        }
        public static string Run()
        {
            Reset(); Press(UnityEngine.KeyCode.F9);
            Check(!_panel.GetComponent<Mortal.Core.DevelopmentOnly>().enabled && !_panel.transform.GetChild(0).gameObject.GetComponent<Mortal.Core.DevelopmentOnly>().enabled, "Release-only destruction markers disabled on retained panel and children");
            Check(DebugMenuShortcut.IsOpen && _panel.transform.GetChild(0).gameObject.activeSelf, "F9 opens native panel and its rows without trainer");
            Check(UnityEngine.Time.timeScale == 0 && Mortal.Story.StoryManager.Instance.IsStoryPause, "Story is paused while editing");
            Check(FeaturePanel.BlocksInput, "Other shortcuts are blocked by debug panel");
            var button = new UnityEngine.GameObject().transform; button.SetParent(_panel.transform, false);
            Check(DebugMenuShortcut.Contains(button) && !DebugMenuShortcut.Contains(new UnityEngine.GameObject().transform), "Debug buttons remain clickable while unrelated buttons are blocked");
            Press(UnityEngine.KeyCode.None);
            Check(DebugMenuShortcut.IsOpen && LOMSaveProbePlugin.Opened == 1, "No per-frame reopen or repeated activation");
            Press(UnityEngine.KeyCode.F9);
            Check(!DebugMenuShortcut.IsOpen && UnityEngine.Time.timeScale == 1.75f && !Mortal.Story.StoryManager.Instance.IsStoryPause, "F9 restores exact pause and speed state");
            Check(LOMSaveProbePlugin.Closed == 1, "Native disable callback cannot close twice");
            Press(UnityEngine.KeyCode.F9); Press(UnityEngine.KeyCode.Escape);
            Check(!DebugMenuShortcut.IsOpen && UnityEngine.Time.timeScale == 1.75f, "Escape closes owned panel");
            Press(UnityEngine.KeyCode.F9); _panel.SetActive(false);
            Check(UnityEngine.Time.timeScale == 1.75f && !Mortal.Story.StoryManager.Instance.IsStoryPause, "Native external close releases pause");
            Press(UnityEngine.KeyCode.F9); _panel.Notify("OnDestroy");
            Check(UnityEngine.Time.timeScale == 1.75f, "Scene destruction releases pause");
            Press(UnityEngine.KeyCode.F9); DebugMenuShortcut.Attach(new UnityEngine.GameObject(false));
            Check(UnityEngine.Time.timeScale == 1.75f && !Mortal.Story.StoryManager.Instance.IsStoryPause, "Replacing scene panel cannot retain old pause");
            Reset(); UnityEngine.SceneManagement.SceneManager.Name = "Free"; Mortal.Story.StoryManager.Instance = null;
            Press(UnityEngine.KeyCode.F9); Check(DebugMenuShortcut.IsOpen, "Free map works without StoryManager");
            Press(UnityEngine.KeyCode.Escape); Check(UnityEngine.Time.timeScale == 1.75f, "Free map speed restored");
            Reset();
            var status = _panel.transform.parent;
            var canvas = new UnityEngine.GameObject().transform;
            status.SetParent(canvas, false); status.gameObject.SetActive(false);
            _panel.transform.SetParent(status, false);
            Press(UnityEngine.KeyCode.F9);
            Check(DebugMenuShortcut.IsOpen && _panel.transform.parent == canvas && !status.gameObject.activeSelf, "Hidden native status parent does not prevent standalone entry");
            Press(UnityEngine.KeyCode.Escape);
            Check(_panel.transform.parent == status && !status.gameObject.activeSelf && _panel.transform.GetSiblingIndex() == 0, "Closing restores native hierarchy without opening status page");
            Reset(); var loader = new BepInEx.Unity.Mono.Bootstrap.UnityChainloader();
            var trainer = new UnityEngine.Behaviour();
            loader.Plugins["ScriptTrainer.Jim97.Mortal"] = new BepInEx.PluginInfo { Instance = trainer };
            BepInEx.Unity.Mono.Bootstrap.UnityChainloader.Instance = loader;
            Press(UnityEngine.KeyCode.F9);
            Check(!DebugMenuShortcut.IsOpen && LOMSaveProbePlugin.Opened == 0, "Existing active trainer owns F9; no second toggle");
            trainer.enabled = false; Press(UnityEngine.KeyCode.F9);
            Check(DebugMenuShortcut.IsOpen, "Disabled trainer does not remove built-in shortcut");
            var blockers = new Action[] {
                () => UnityEngine.Application.isFocused = false,
                () => FeaturePanel.Blocked = true,
                () => SaveNotes.IsOpen = true,
                () => EventInspectorPanel.BlocksInput = true,
                () => OptionImpactPreview.IsOpen = true,
                () => AutoDialogueSpeed.SettingsVisible = true,
                () => ReplayRuntime.IsRestoring = true,
                () => ReplayRuntime.IsReplaying = true,
                () => StoryCommandTrace.IsReplayPending = true,
                () => ReplayStateValidator.IsRestoring = true,
                () => Mortal.Core.SceneController.Instance.IsLoading = true,
                () => Mortal.Core.SceneController.Instance.IsPrepare = true,
                () => Mortal.Core.SceneController.Instance = null,
                () => Mortal.Story.StoryManager.Instance.IsStoryPause = true,
                () => Mortal.Story.StoryManager.Instance.EnableAction = false,
                () => Mortal.Story.StoryManager.Instance._panelOpen = true,
                () => Mortal.Story.StoryManager.Instance._logOpen = true,
                () => UnityEngine.SceneManagement.SceneManager.Name = "Title",
                () => UnityEngine.SceneManagement.SceneManager.Name = "Battle",
                () => _panel.transform.parent.gameObject.SetActive(false),
                () => DebugMenuShortcut.Attach(null),
                () => UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject = new UnityEngine.GameObject { InputField = true },
                () => { UnityEngine.SceneManagement.SceneManager.Name = "Free"; Mortal.Free.FreeManager.Instance.EnableHotKey = false; },
                () => { UnityEngine.SceneManagement.SceneManager.Name = "Free"; Mortal.Free.FreeManager.Instance.IsProcessing = true; }
            };
            foreach (var block in blockers) { Reset(); block(); Press(UnityEngine.KeyCode.F9); Check(!DebugMenuShortcut.IsOpen && LOMSaveProbePlugin.Opened == 0, "Blocked context must not open or mutate pause state"); }
            Reset(); _panel.FailOnOpen = true; Press(UnityEngine.KeyCode.F9);
            Check(!DebugMenuShortcut.IsOpen && LOMSaveProbePlugin.Errors == 1, "Activation failure is reported");
            Check(UnityEngine.Time.timeScale == 1.75f && !Mortal.Story.StoryManager.Instance.IsStoryPause, "Failed open cannot freeze gameplay");
            return _checks + " built-in F9 shortcut assertions passed.";
        }
    }
}
