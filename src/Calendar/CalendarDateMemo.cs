using System;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal sealed class CalendarDateMemo : MonoBehaviour
    {
        private Text _text, _source, _iconLabel;
        private Button _icon;
        private float _next;
        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            if (_text == null)
            {
                _source = EventInspector.Read<Text>(GetComponent<GameDateTimeText>(), "_text");
                if (_source == null) return;
                _text = FeaturePanel.Label(_source.transform, "", 22, 0, 0, 600, 120);
                _text.gameObject.name = "LOMSaveCalendarDateMemo";
                _text.rectTransform.anchorMin = _text.rectTransform.anchorMax = new Vector2(0, 0);
                _text.rectTransform.anchoredPosition = new Vector2(0, -8);
                _text.color = new Color(1f, 0.85f, 0.5f);
                _text.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);
                _icon = FeaturePanel.Button(_source.transform, "", 0, 0, 80, 40, ActionCalendar.Toggle);
                _icon.gameObject.name = "LOMSaveCalendarStoryIndicator";
                // Keep the invisible hit area, but tint only the glyphs so
                // hovering/pressing never restores a rectangular backdrop.
                _icon.GetComponent<Image>().color = Color.clear;
                _iconLabel = _icon.GetComponentInChildren<Text>();
                _iconLabel.fontSize = 22;
                _iconLabel.alignment = TextAnchor.MiddleLeft;
                _iconLabel.color = Color.white;
                _icon.targetGraphic = _iconLabel;
                var colors = _icon.colors;
                colors.normalColor = colors.selectedColor = new Color(0.89f, 0.72f, 0.42f);
                colors.highlightedColor = new Color(1f, 0.88f, 0.58f);
                colors.pressedColor = new Color(0.76f, 0.60f, 0.30f);
                _icon.colors = colors;
                var outline = _iconLabel.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.12f, 0.09f, 0.04f, 0.55f);
                outline.effectDistance = new Vector2(0.6f, -0.6f);
            }
            var note = ActionCalendar.Note(ActionCalendar.Today);
            _text.text = note.Length == 0 ? "" : "日历备注 · " + note;
            _text.rectTransform.sizeDelta = new Vector2(600, Math.Max(40, _text.preferredHeight));
            var count = CalendarStoryEvents.UnreadCount;
            _icon.gameObject.SetActive(count > 0);
            if (count == 0) return;
            _iconLabel.text = "◇ " + count;
            var bounds = _source.rectTransform.rect;
            var alignment = (int)_source.alignment;
            var width = Math.Min(bounds.width, _source.preferredWidth);
            var x = alignment % 3 == 0 ? width : alignment % 3 == 1 ? (bounds.width + width) / 2 : bounds.width;
            var y = alignment / 3 == 0 ? 0 : alignment / 3 == 1 ? (bounds.height - 40) / 2 : bounds.height - 40;
            FeaturePanel.Place((RectTransform)_icon.transform, x + 12, Math.Max(0, y), 80, 40);
        }
    }
}
