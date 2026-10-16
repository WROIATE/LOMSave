using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LOMSave.Probe
{
    internal static class CalendarPanel
    {
        private static InputField _input;
        private static string _date;
        private static Text _status;

        internal static void ShowOverview(int year)
        {
            if (!SaveDraft()) return;
            var panel = FeaturePanel.Open("行动日历 · " + year + " 年总览", () => ActionCalendar.IsOpen = false, SaveDraft);
            ActionCalendar.IsOpen = true;
            _input = null;
            _date = null;
            _status = null;
            FeaturePanel.Button(panel, "‹ 上一年", 36, 90, 156, 48, () => ShowOverview(year - 1)).interactable = year > 1;
            FeaturePanel.Button(panel, "下一年 ›", 208, 90, 156, 48, () => ShowOverview(year + 1)).interactable = year < 9999;
            FeaturePanel.Button(panel, "回到当前年份", 380, 90, 240, 48, () =>
            {
                var time = ActionCalendar.CurrentTime;
                if (!ReferenceEquals(time, null)) ShowOverview(time.Year);
            });
            FeaturePanel.Button(panel, "今日详情", 636, 90, 180, 48, () =>
            {
                var time = ActionCalendar.CurrentTime;
                if (!ReferenceEquals(time, null)) Show(time.Year, time.Month, (int)time.Stage);
            });
            FeaturePanel.Label(panel, "月份", 22, 36, 162, 80, 36).alignment = TextAnchor.MiddleCenter;
            for (var stage = 1; stage <= 3; stage++)
                FeaturePanel.Label(panel, Stage(stage), 24, 132 + (stage - 1) * 360, 162, 352, 36).alignment = TextAnchor.MiddleCenter;
            var noted = 0;
            for (var month = 1; month <= 12; month++)
            {
                var selectedMonth = month;
                var y = 208 + (month - 1) * 48;
                FeaturePanel.Label(panel, month + " 月", 22, 36, y, 80, 44).alignment = TextAnchor.MiddleCenter;
                for (var stage = 1; stage <= 3; stage++)
                {
                    var selectedStage = stage;
                    var key = CalendarNotes.DateKey(year, month, stage);
                    var note = ActionCalendar.Note(key);
                    var today = key == ActionCalendar.Today;
                    var count = ActionCalendar.Actions(key).Count();
                    var hasNote = !string.IsNullOrWhiteSpace(note);
                    if (hasNote) noted++;
                    var value = hasNote ? "◆ " + CalendarNotes.Preview(note, 14)
                        : today ? "今天 · 暂无备注" : count > 0 ? count + " 次行动 · 暂无备注" : "—";
                    var button = FeaturePanel.Button(panel, value, 132 + (stage - 1) * 360, y, 352, 44,
                        () => Show(year, selectedMonth, selectedStage));
                    var image = button.GetComponent<Image>();
                    image.color = hasNote ? new Color(0.36f, 0.28f, 0.13f) : new Color(0.14f, 0.13f, 0.11f);
                    var label = button.GetComponentInChildren<Text>();
                    label.fontSize = 20;
                    label.alignment = hasNote ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                    label.rectTransform.anchoredPosition += new Vector2(8, 0);
                    label.rectTransform.sizeDelta -= new Vector2(16, 0);
                    if (hasNote) label.color = new Color(1f, 0.87f, 0.59f);
                    if (today)
                    {
                        var border = button.gameObject.AddComponent<Outline>();
                        border.effectColor = new Color(1f, 0.8f, 0.35f);
                        border.effectDistance = new Vector2(2, -2);
                    }
                }
            }
            FeaturePanel.Label(panel, "已有备注 " + noted + " 处", 22, 856, 100, 340, 36);
            FeaturePanel.Label(panel, "◆ 有备注    金色边框：今天    点击日期查看行动、编辑备注    F10 关闭", 20, 36, 802, 1168, 40);
        }

        private static bool SaveDraft()
        {
            if (_input == null || _date == null || _input.text == ActionCalendar.Note(_date)) return true;
            try { ActionCalendar.SaveNote(_date, _input.text); return true; }
            catch (Exception e)
            {
                if (_status != null) _status.text = "备注保存失败，请重试；编辑内容仍保留。";
                LOMSaveProbePlugin.WriteError("calendar_note_save_failed", e);
                return false;
            }
        }

        internal static void Show(int year, int month, int stage)
        {
            if (!SaveDraft()) return;
            var panel = FeaturePanel.Open("行动日历 · " + year + " 年 " + month + " 月", () =>
            {
                ActionCalendar.IsOpen = false;
            }, SaveDraft);
            ActionCalendar.IsOpen = true;
            _date = CalendarNotes.DateKey(year, month, stage);
            _input = null;
            FeaturePanel.Button(panel, "‹ 上月", 36, 90, 180, 48, () => MoveMonth(year, month, -1, stage));
            FeaturePanel.Button(panel, "下月 ›", 232, 90, 180, 48, () => MoveMonth(year, month, 1, stage));
            FeaturePanel.Button(panel, "回到当前日期", 428, 90, 240, 48, () =>
            {
                var time = ActionCalendar.CurrentTime;
                if (!ReferenceEquals(time, null)) Show(time.Year, time.Month, (int)time.Stage);
            });
            FeaturePanel.Button(panel, "全年总览", 710, 90, 180, 48, () => ShowOverview(year));
            FeaturePanel.Label(panel, "F10 开关日历", 21, 914, 100, 282, 40);
            for (var i = 1; i <= 3; i++)
            {
                var selectedStage = i;
                var key = CalendarNotes.DateKey(year, month, i);
                var title = Stage(i) + (i == stage ? " 〔当前查看〕" : "") + (key == ActionCalendar.Today ? " · 今天" : "");
                FeaturePanel.Button(panel, title, 36 + (i - 1) * 396, 162, 376, 52, () => Show(year, month, selectedStage));
                var count = ActionCalendar.Actions(key).Count();
                FeaturePanel.Label(panel, count + " 次行动" + (ActionCalendar.Note(key).Length > 0 ? "  ·  已有备注" : ""), 21,
                    48 + (i - 1) * 396, 224, 350, 36);
            }
            var entries = ActionCalendar.Actions(_date).ToList();
            var text = entries.Count == 0 ? "这一天还没有行动记录。\n新版本启用后的行动会自动记录，读旧档不能补回历史行动。"
                : string.Join("\n\n", entries.Select((x, index) => (index + 1) + ". " + x.Label
                    + "  （消耗 " + (x.Before - x.After) + " 次行动，剩余 " + x.After + "）").ToArray());
            FeaturePanel.ScrollText(panel, text, 36, 278, 1168, 286);
            FeaturePanel.Label(panel, year + " 年 " + month + " 月" + Stage(stage) + " · 手动备注", 25, 36, 582, 1100, 42);
            _input = FeaturePanel.Input(panel, ActionCalendar.Note(_date), 36, 632, 1168, 130);
            _status = FeaturePanel.Label(panel, "最多 300 字；切换日期或关闭时保存，留空可清除。到达备注日期时在左上角常驻提示。", 20, 36, 786, 890, 56);
            FeaturePanel.Button(panel, "保存备注", 964, 786, 240, 48, () =>
            {
                if (SaveDraft()) _status.text = "备注已保存，所有周目在该日期都会显示。";
            });
        }
        private static string Stage(int stage) { return stage == 1 ? "上旬" : stage == 2 ? "中旬" : "下旬"; }
        private static void MoveMonth(int year, int month, int delta, int stage)
        {
            month += delta;
            if (month < 1) { month = 12; year--; }
            if (month > 12) { month = 1; year++; }
            if (year >= 1 && year <= 9999) Show(year, month, stage);
        }
    }
}
