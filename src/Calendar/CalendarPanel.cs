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
        private static Text _events, _eventHeading;
        private static int _eventRevision = -1;
        private static int _journalRevision = -1;
        private static bool _currentView;

        internal static void ShowCurrent()
        {
            if (!SaveDraft()) return;
            var panel = FeaturePanel.Open("行动日历 · 当前可触发剧情", () => { ActionCalendar.IsOpen = false; _events = null; }, SaveDraft);
            ActionCalendar.IsOpen = true; _input = null; _date = null; _status = null; _currentView = true;
            var time = ActionCalendar.CurrentTime;
            if (ReferenceEquals(time, null)) return;
            FeaturePanel.Button(panel, "可触发剧情", 36, 90, 224, 48, ShowCurrent);
            FeaturePanel.Button(panel, "全年总览", 280, 90, 224, 48, () => ShowOverview(time.Year));
            FeaturePanel.Button(panel, "今日行动 / 备注", 524, 90, 272, 48, () => Show(time.Year, time.Month, (int)time.Stage));
            _eventHeading = FeaturePanel.Label(panel, "", 25, 36, 160, 1168, 42);
            _events = FeaturePanel.ScrollText(panel, "", 36, 216, 1168, 544);
            FeaturePanel.Label(panel, "查看本页即标为已读；◇ 仅提示当前可做且未读的事件。随机剧情仍可能受后续选择影响。\n已读和剧情记录随存档恢复，新周目重新记录；手动备注跨周目保留，不预测未经历的日期。", 20, 36, 782, 1168, 66);
            _eventRevision = -1; Update();
        }

        internal static void Update()
        {
            if (!ActionCalendar.IsOpen || _events == null || (_eventRevision == CalendarStoryEvents.Revision
                && _journalRevision == CalendarEventLog.Revision)) return;
            _eventRevision = CalendarStoryEvents.Revision;
            if (!_currentView) { UpdateDetail(); _journalRevision = CalendarEventLog.Revision; return; }
            var time = ActionCalendar.CurrentTime;
            var items = CalendarStoryEvents.Current;
            _eventHeading.text = (ReferenceEquals(time, null) ? "" : time.Year + " 年 " + time.Month + " 月" + Stage((int)time.Stage) + " · ")
                + items.Count + " 项剧情" + (CalendarStoryEvents.Scanning ? " · 正在更新…" : "");
            var text = string.Join("\n\n", items.Select((item, i) => (i + 1) + ". 〔" + item.Kind + "〕" + item.Title + "\n    " + item.Trigger
                + (item.Random ? " · 可能遇到" : "")).ToArray());
            if (items.Count == 0) text = CalendarStoryEvents.Scanning ? "正在检查当前可触发剧情…"
                : "当前没有已就绪的剧情或满足条件的特殊事件。";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Free") text = "回到养成地图后，将在此汇总当前可触发剧情。";
            else if (!CalendarStoryEvents.HasPoolData) text += StoryEventIndex.Pending ? "\n\n正在准备特殊事件提示…"
                : "\n\n特殊事件索引暂不可用，请检查安装文件是否与游戏版本匹配。";
            _events.text = text;
            _events.rectTransform.sizeDelta = new Vector2(1136, Math.Max(520, _events.preferredHeight + 24));
            CalendarEventLog.MarkRead(items);
            _journalRevision = CalendarEventLog.Revision;
        }

        internal static void ShowOverview(int year)
        {
            if (!SaveDraft()) return;
            var panel = FeaturePanel.Open("行动日历 · " + year + " 年总览", () => ActionCalendar.IsOpen = false, SaveDraft);
            ActionCalendar.IsOpen = true;
            _events = null;
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
            FeaturePanel.Button(panel, "当前剧情", 636, 90, 180, 48, ShowCurrent);
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
                    var recorded = CalendarEventLog.On(key).Count;
                    if (recorded > 0) value = "剧情记录 " + recorded + " · " + (hasNote ? "◆ " + CalendarNotes.Preview(note, 7) : today ? "今天" : count + " 次行动");
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
            FeaturePanel.Label(panel, "◆ 有备注    金色边框：今天    剧情记录：该旬曾可触发的事件    点击日期查看详情    F10 关闭", 20, 36, 802, 1168, 40);
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
                _events = null;
            }, SaveDraft);
            ActionCalendar.IsOpen = true;
            _date = CalendarNotes.DateKey(year, month, stage);
            _currentView = false;
            _input = null;
            FeaturePanel.Button(panel, "‹ 上月", 36, 90, 180, 48, () => MoveMonth(year, month, -1, stage));
            FeaturePanel.Button(panel, "下月 ›", 232, 90, 180, 48, () => MoveMonth(year, month, 1, stage));
            FeaturePanel.Button(panel, "回到当前日期", 428, 90, 240, 48, () =>
            {
                var time = ActionCalendar.CurrentTime;
                if (!ReferenceEquals(time, null)) Show(time.Year, time.Month, (int)time.Stage);
            });
            FeaturePanel.Button(panel, "全年总览", 710, 90, 180, 48, () => ShowOverview(year));
            FeaturePanel.Button(panel, "当前剧情", 914, 90, 282, 48, ShowCurrent);
            for (var i = 1; i <= 3; i++)
            {
                var selectedStage = i;
                var key = CalendarNotes.DateKey(year, month, i);
                var title = Stage(i) + (i == stage ? " 〔当前查看〕" : "") + (key == ActionCalendar.Today ? " · 今天" : "");
                FeaturePanel.Button(panel, title, 36 + (i - 1) * 396, 162, 376, 52, () => Show(year, month, selectedStage));
                var count = ActionCalendar.Actions(key).Count();
                FeaturePanel.Label(panel, count + " 次行动" + (ActionCalendar.Note(key).Length > 0 ? "  ·  已有备注" : "")
                    + (CalendarEventLog.On(key).Count > 0 ? " · 剧情记录 " + CalendarEventLog.On(key).Count : ""), 21,
                    48 + (i - 1) * 396, 224, 350, 36);
            }
            _events = FeaturePanel.ScrollText(panel, "", 36, 278, 1168, 286);
            _eventRevision = -1; Update();
            FeaturePanel.Label(panel, year + " 年 " + month + " 月" + Stage(stage) + " · 手动备注", 25, 36, 582, 1100, 42);
            _input = FeaturePanel.Input(panel, ActionCalendar.Note(_date), 36, 632, 1168, 130);
            _status = FeaturePanel.Label(panel, "最多 300 字；切换日期或关闭时保存，留空可清除。到达备注日期时在左上角常驻提示。", 20, 36, 786, 890, 56);
            FeaturePanel.Button(panel, "保存备注", 964, 786, 240, 48, () =>
            {
                if (SaveDraft()) _status.text = "备注已保存，所有周目在该日期都会显示。";
            });
        }
        private static void UpdateDetail()
        {
            var entries = ActionCalendar.Actions(_date).ToList();
            var text = entries.Count == 0 ? "这一天还没有行动记录。\n新版本启用后的行动会自动记录，读旧档不能补回历史行动。"
                : string.Join("\n\n", entries.Select((x, index) => (index + 1) + ". " + x.Label
                    + "  （消耗 " + (x.Before - x.After) + " 次行动，剩余 " + x.After + "）").ToArray());
            text = "行动记录：\n" + text;
            var records = CalendarEventLog.On(_date);
            text = (records.Count == 0 ? "剧情记录：尚未记录该旬的可触发事件。启用后在养成地图自动记录，不能补回旧档历史。"
                : "该旬曾可触发的剧情（实际观察记录）：\n" + string.Join("\n\n", records.Select(x => "· 〔" + x.Kind + "〕" + x.Title + "\n  "
                    + string.Join(" / ", x.Triggers.ToArray()) + (x.Random ? " · 随机遇到" : "")).ToArray())
                    + "\n记录随存档保存和恢复；只说明曾满足条件，并非整旬随时可做。") + "\n\n" + text;
            if (_date == ActionCalendar.Today)
            {
                var current = CalendarStoryEvents.Current;
                text = "当前可触发剧情：\n" + (current.Count == 0 ? "当前暂无可触发剧情，或正在刷新。"
                    : string.Join("\n", current.Select(x => "· " + x.Title + "（" + x.Trigger + (x.Random ? " · 可能遇到" : "") + "）").ToArray()))
                    + "\n\n" + text;
                CalendarEventLog.MarkRead(current);
            }
            _events.text = text;
            _events.rectTransform.sizeDelta = new Vector2(1136, Math.Max(262, _events.preferredHeight + 24));
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
