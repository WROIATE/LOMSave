using System;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class DeathLoadMenu
    {
        private const string ButtonName = "LOMSaveDeathLoadButton";
        private static GameObject _template, _cache;

        [HarmonyPrefix, HarmonyPatch(typeof(LoadGamePanel), "Awake")]
        private static void CacheLoadPanel(LoadGamePanel __instance)
        {
            if (_template != null) return;
            try
            {
                // GameOver contains no load panel. Retain one dormant native
                // template before Awake creates slots; never load another scene
                // or scan assets to open the menu. An inactive parent prevents
                // the copy from running Awake or replacing the live singleton.
                _cache = new GameObject("LOMSaveDeathLoadTemplate", typeof(RectTransform));
                _cache.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_cache);
                _template = UnityEngine.Object.Instantiate(__instance.gameObject, _cache.transform, false);
                _template.SetActive(false);
            }
            catch (Exception error)
            {
                if (_cache != null) UnityEngine.Object.Destroy(_cache);
                _template = null; _cache = null;
                LOMSaveProbePlugin.WriteError("death_load_template_failed", error);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameOverController), "Awake")]
        private static void AddButton(Button ____titleButton, ref CanvasGroup ____titleButtonCanvas)
        {
            GameObject group = null;
            var originalCanvas = ____titleButtonCanvas;
            var originalParent = ____titleButton == null ? null : ____titleButton.transform.parent;
            try
            {
                var title = ____titleButton;
                if (title == null || ____titleButtonCanvas == null || _template == null
                    || title.transform.parent.Find(ButtonName) != null) return;
                // Build both controls before Start, then let the original
                // coroutine animate one shared group. Alpha, raycasts and
                // interaction therefore change on exactly the same frame.
                group = new GameObject("LOMSaveDeathButtons", typeof(RectTransform), typeof(CanvasGroup));
                var host = (RectTransform)group.transform;
                host.SetParent(title.transform.parent, false);
                host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one;
                host.offsetMin = Vector2.zero; host.offsetMax = Vector2.zero;
                var reveal = group.GetComponent<CanvasGroup>();
                reveal.alpha = 0; reveal.interactable = false; reveal.blocksRaycasts = false;
                ____titleButtonCanvas.alpha = 1;
                ____titleButtonCanvas.interactable = true;
                ____titleButtonCanvas.blocksRaycasts = true;
                var button = UnityEngine.Object.Instantiate(title, host, false);
                button.name = ButtonName;
                var rect = (RectTransform)button.transform;
                var source = (RectTransform)title.transform;
                rect.anchoredPosition = source.anchoredPosition + new Vector2(0, source.rect.height + 12);
                foreach (var behaviour in button.GetComponentsInChildren<MonoBehaviour>(true))
                    if ((behaviour.GetType().FullName ?? "").StartsWith("Lean.Localization.LeanLocalizedText", StringComparison.Ordinal))
                        behaviour.enabled = false;
                var textButton = button.GetComponentInChildren<TextButton>(true);
                if (textButton != null) textButton.Setup("读取游戏");
                else foreach (var text in button.GetComponentsInChildren<Text>(true)) text.text = "读取游戏";
                button.onClick = new Button.ButtonClickedEvent();
                GameObject menu = null;
                button.onClick.AddListener(() => Open(button, ref menu));
                var up = button.navigation;
                up.mode = Navigation.Mode.Explicit; up.selectOnDown = title; up.selectOnUp = title;
                button.navigation = up;
                var down = title.navigation;
                down.mode = Navigation.Mode.Explicit; down.selectOnUp = button; down.selectOnDown = button;
                title.navigation = down;
                button.gameObject.SetActive(true);
                title.transform.SetParent(host, false);
                ____titleButtonCanvas = reveal;
                LOMSaveProbePlugin.WriteEvent("death_load_button_added", new { sharedReveal = true });
            }
            catch (Exception error)
            {
                if (____titleButton != null && originalParent != null)
                    ____titleButton.transform.SetParent(originalParent, false);
                ____titleButtonCanvas = originalCanvas;
                if (group != null) UnityEngine.Object.Destroy(group);
                LOMSaveProbePlugin.WriteError("death_load_button_failed", error);
            }
        }

        private static void Open(Button button, ref GameObject menu)
        {
            try
            {
                var scene = SceneController.Instance;
                if (scene == null || scene.IsPrepare || scene.IsLoading) return;
                if (menu == null)
                {
                    menu = UnityEngine.Object.Instantiate(_template, button.transform.parent, false);
                    menu.name = "LOMSaveDeathLoadPanel";
                    var panel = menu.GetComponent<CommonPanel>();
                    var load = menu.GetComponent<LoadGamePanel>();
                    // Remove callbacks into the destroyed title/pause menu.
                    // Internal slot, delete and close controls remain native.
                    var fields = Traverse.Create(panel);
                    fields.Field("_hideWhenStart").SetValue(false);
                    fields.Field("_openEvent").SetValue(new OBB.Framework.ScriptableEvent.GameEvent[0]);
                    fields.Field("_closeEvent").SetValue(new OBB.Framework.ScriptableEvent.GameEvent[0]);
                    var opened = new UnityEvent(); opened.AddListener(load.OnPanelOpen);
                    var closed = new UnityEvent(); closed.AddListener(() => { if (button != null) button.Select(); });
                    fields.Field("_openResponse").SetValue(opened);
                    fields.Field("_closeResponse").SetValue(closed);
                    // GameOver has no PlayerInputController or menu action
                    // map. Do not enable native hints which require that host.
                    foreach (var hint in menu.GetComponentsInChildren<GamepadIconController>(true))
                    {
                        hint.enabled = false;
                        var display = Traverse.Create(hint).Field("_displayObject").GetValue<GameObject>();
                        if (display != null) display.SetActive(false);
                    }
                    foreach (var input in menu.GetComponentsInChildren<InputButton>(true)) input.enabled = false;
                    var keyboard = menu.AddComponent<DeathLoadPanelInput>();
                    keyboard.Panel = panel;
                    keyboard.Load = load;
                    keyboard.Delete = Traverse.Create(load).Field("_deletePanel").GetValue<CommonPanel>();
                }
                menu.transform.SetAsLastSibling();
                menu.GetComponent<CommonPanel>().Show(true);
                LOMSaveProbePlugin.WriteEvent("death_load_menu_opened", new { });
            }
            catch (Exception error)
            {
                if (menu != null) UnityEngine.Object.Destroy(menu);
                menu = null;
                LOMSaveProbePlugin.WriteError("death_load_menu_failed", error);
            }
        }
    }

    internal sealed class DeathLoadPanelInput : MonoBehaviour
    {
        internal CommonPanel Panel, Delete;
        internal LoadGamePanel Load;
        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (Delete != null && Delete.gameObject.activeSelf) Load.CloseDeletePanel();
            else Panel.Show(false);
        }
    }
}
