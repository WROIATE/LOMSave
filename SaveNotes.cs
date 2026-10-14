using System;
using System.IO;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class SaveNotes
    {
        private static GameObject _editor;
        internal static bool IsOpen { get { return _editor != null; } }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LoadSlotPanel), "Setup")]
        private static void AfterLoadSetup(LoadSlotPanel __instance)
        { Attach(__instance, __instance.SlotButton, __instance.HasSaveData); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveSlotPanel), "Setup")]
        private static void AfterSaveSetup(SaveSlotPanel __instance)
        { Attach(__instance, __instance.SlotButton, __instance.HasSaveData); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveSystem), "DeleteSaveData", typeof(string))]
        private static void AfterDelete(SaveSystem __instance, string slot)
        {
            try { SaveNoteStore.Delete(SaveFile(__instance, slot)); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("save_note_delete_failed", e); }
        }

        private static string SaveFile(SaveSystem system, string slot)
        { return Traverse.Create(system).Method("GetSaveDataFile", new[] { typeof(string) }).GetValue<string>(slot); }

        private static void Attach(Component panel, Button button, bool hasData)
        {
            try
            {
                if (button == null) return;
                var slot = Traverse.Create(panel).Field("_slot").GetValue<string>();
                int index;
                if (!int.TryParse(slot, out index) || index < 1) return;
                var times = Traverse.Create(panel).Field("_timeText").GetValue<Text[]>();
                if (times == null || times.Length == 0 || times[0] == null) return;
                var row = button.GetComponent<SaveNoteRow>();
                if (row == null) row = button.gameObject.AddComponent<SaveNoteRow>();
                row.Configure(slot, SaveFile(SaveSystem.Instance, slot), times[0], hasData);
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("save_note_attach_failed", e); }
        }

        internal static void Edit(string slot, string file, Font font)
        {
            if (!File.Exists(file)) return;
            Close();
            _editor = new GameObject("LOMSaveNoteEditor", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _editor.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = _editor.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var bg = OptionImpactPreview.CreateImage("Backdrop", _editor.transform, new Color(0, 0, 0, 0.75f));
            OptionImpactPreview.Stretch(bg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var box = OptionImpactPreview.CreateImage("Panel", bg.transform, new Color(0.07f, 0.065f, 0.055f, 1));
            Place(box.rectTransform, new Vector2(820, 360), Vector2.zero);
            var title = Label(box.transform, "存档 " + slot + " 的备注", font, 32, new Vector2(730, 50), new Vector2(0, 115));
            title.color = new Color(0.89f, 0.72f, 0.42f);
            var fieldBg = OptionImpactPreview.CreateImage("NoteInput", box.transform, new Color(0.18f, 0.17f, 0.14f));
            Place(fieldBg.rectTransform, new Vector2(730, 70), new Vector2(0, 35));
            var input = fieldBg.gameObject.AddComponent<InputField>();
            input.targetGraphic = fieldBg;
            input.characterLimit = SaveNoteStore.Limit;
            input.lineType = InputField.LineType.SingleLine;
            var text = Label(fieldBg.transform, "", font, 27, new Vector2(698, 58), Vector2.zero);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            input.textComponent = text;
            var placeholder = Label(fieldBg.transform, "例如：小师妹招亲前、崆峒留学", font, 25, new Vector2(698, 58), Vector2.zero);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.color = new Color(0.6f, 0.59f, 0.55f);
            input.placeholder = placeholder;
            input.text = SaveNoteStore.Read(file);
            var status = Label(box.transform, "最多 80 字；留空保存可清除备注", font, 21, new Vector2(730, 42), new Vector2(0, -32));
            status.color = new Color(0.72f, 0.70f, 0.64f);
            Button(box.transform, "取消", font, new Vector2(-130, -115), Close);
            Button(box.transform, "保存备注", font, new Vector2(130, -115), () =>
            {
                try
                {
                    SaveNoteStore.Write(file, input.text);
                    foreach (var row in Resources.FindObjectsOfTypeAll<SaveNoteRow>()) row.Refresh();
                    LOMSaveProbePlugin.WriteEvent("save_note_saved", new { slot, length = SaveNoteStore.Normalize(input.text).Length });
                    Close();
                }
                catch (Exception e)
                {
                    status.text = "备注保存失败，请重试。";
                    LOMSaveProbePlugin.WriteError("save_note_write_failed", e, new { slot });
                }
            });
            input.Select();
            input.ActivateInputField();
        }

        private static void Close()
        { if (_editor != null) UnityEngine.Object.Destroy(_editor); _editor = null; }

        private static Text Label(Transform parent, string value, Font font, int size, Vector2 dimensions, Vector2 position)
        {
            var text = OptionImpactPreview.CreateText("Label", parent, value, font, size, TextAnchor.MiddleCenter, Color.white);
            Place(text.rectTransform, dimensions, position);
            return text;
        }

        private static void Place(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Button(Transform parent, string text, Font font, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var bg = OptionImpactPreview.CreateImage("Button", parent, new Color(0.34f, 0.27f, 0.17f));
            Place(bg.rectTransform, new Vector2(220, 54), position);
            var button = bg.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(action);
            Label(bg.transform, text, font, 25, new Vector2(210, 50), Vector2.zero);
        }
    }

    internal sealed class SaveNoteRow : MonoBehaviour
    {
        private string _slot, _file;
        private Text _label, _time;
        private bool _hasData;
        private RectTransform _layoutRow, _scrollContent;
        private LayoutGroup _scrollLayout;
        private readonly Vector3[] _corners = new Vector3[4];

        internal void Configure(string slot, string file, Text time, bool hasData)
        {
            _slot = slot; _file = file; _time = time; _hasData = hasData;
            BindScrollLayout();
            if (_label == null)
            {
                _label = OptionImpactPreview.CreateText("LOMSaveNote", transform, "", time.font, 20,
                    TextAnchor.MiddleLeft, new Color(0.84f, 0.73f, 0.53f));
                _label.raycastTarget = true;
                _label.verticalOverflow = VerticalWrapMode.Truncate;
                _label.horizontalOverflow = HorizontalWrapMode.Wrap;
                var button = _label.gameObject.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = _label;
                button.onClick.AddListener(() => SaveNotes.Edit(_slot, _file, _time.font));
            }
            Refresh();
            Position();
        }

        private void LateUpdate() { if (_label != null && _hasData) Position(); }

        private void Position()
        {
            var parent = (RectTransform)transform;
            _time.rectTransform.GetWorldCorners(_corners);
            var bottom = parent.InverseTransformPoint(_corners[0]);
            var rect = _label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 1);
            rect.localPosition = new Vector3(parent.rect.xMin + 12, bottom.y + 18, 0);
            rect.sizeDelta = new Vector2(parent.rect.width - 24, 30);
            ReserveScrollSpace();
        }

        private void BindScrollLayout()
        {
            for (var ancestor = transform.parent; ancestor != null; ancestor = ancestor.parent)
            {
                var scroll = ancestor.GetComponent<ScrollRect>();
                if (scroll == null || scroll.content == null) continue;
                var row = transform;
                while (row.parent != null && row.parent != scroll.content) row = row.parent;
                if (row.parent != scroll.content) continue;
                _layoutRow = row as RectTransform;
                _scrollContent = scroll.content;
                _scrollLayout = scroll.content.GetComponent<LayoutGroup>();
                return;
            }
        }

        private void ReserveScrollSpace()
        {
            if (!_hasData || _layoutRow == null || _scrollLayout == null) return;
            // A note extends below the native row. Include that overhang in the
            // content's layout so the last note can scroll fully above the mask.
            _label.rectTransform.GetWorldCorners(_corners);
            var bottom = float.PositiveInfinity;
            foreach (var corner in _corners)
                bottom = Mathf.Min(bottom, _layoutRow.InverseTransformPoint(corner).y);
            var overhang = Mathf.Max(0, _layoutRow.rect.yMin - bottom);
            var extent = _scrollContent.InverseTransformVector(
                _layoutRow.TransformVector(new Vector3(0, overhang, 0)));
            var required = Mathf.CeilToInt(Mathf.Abs(extent.y) + 8f);
            var padding = _scrollLayout.padding;
            if (padding.bottom >= required) return;
            // Set an absolute minimum: repeated Setup / menu opens must not grow it.
            _scrollLayout.padding = new RectOffset(padding.left, padding.right, padding.top, required);
            LayoutRebuilder.MarkLayoutForRebuild(_scrollContent);
            LOMSaveProbePlugin.WriteEvent("save_note_scroll_padding", new
            { panel = _scrollLayout.GetComponentInParent<LoadGamePanel>() != null ? "load" : "save", bottom = required });
        }

        internal void Refresh()
        {
            if (_label == null || string.IsNullOrEmpty(_file)) return;
            _label.gameObject.SetActive(_hasData);
            if (!_hasData) return;
            try
            {
                var note = SaveNoteStore.Read(_file);
                _label.text = string.IsNullOrEmpty(note) ? "＋ 添加备注" : "备注：" + (note.Length > 28 ? note.Substring(0, 28) + "…" : note);
            }
            catch (Exception e)
            {
                _label.text = "备注读取失败";
                LOMSaveProbePlugin.WriteError("save_note_read_failed", e, new { slot = _slot });
            }
        }
    }
}
