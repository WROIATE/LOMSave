namespace LOMSave.Probe
{
    internal enum ManualSaveBlock
    {
        None, Loading, UnsupportedScene, Processing, CheckpointMissing,
        TextWriting, Presentation, ChoiceNotReady, Dice
    }

    internal static class ManualSavePolicy
    {
        internal static ManualSaveBlock Story(string kind, bool pointValid, bool waitingForInput,
            bool wordsRemaining, bool branchReady, bool presentation)
        {
            if (!pointValid) return ManualSaveBlock.CheckpointMissing;
            if (kind == "dice" || kind == "choosetimer")
            { if (!branchReady) return ManualSaveBlock.Dice; }
            else if (kind == "say")
            { if (!waitingForInput || wordsRemaining) return ManualSaveBlock.TextWriting; }
            else if (kind == "choose")
            { if (!branchReady) return ManualSaveBlock.ChoiceNotReady; }
            else return ManualSaveBlock.CheckpointMissing;
            return presentation ? ManualSaveBlock.Presentation : ManualSaveBlock.None;
        }

        internal static bool DiceReady(bool visible, bool startVisible, bool startEnabled,
            bool startPressed, bool restartPressed, bool resultVisible)
        { return visible && startVisible && startEnabled && !startPressed && !restartPressed && !resultVisible; }

        internal static ManualSaveBlock Evaluate(string scene, bool restoring, bool loading,
            bool processing, bool native, ManualSaveBlock story)
        {
            if (restoring || loading) return ManualSaveBlock.Loading;
            if (scene == "Free") return processing ? ManualSaveBlock.Processing : ManualSaveBlock.None;
            if (scene != "Story") return ManualSaveBlock.UnsupportedScene;
            return native ? ManualSaveBlock.None : story;
        }

        internal static string Hint(ManualSaveBlock block)
        {
            switch (block)
            {
                case ManualSaveBlock.None: return "";
                case ManualSaveBlock.Loading: return "正在加载，暂不可存档";
                case ManualSaveBlock.Processing: return "行动处理中，暂不可存档";
                case ManualSaveBlock.TextWriting: return "文字显示中，暂不可存档";
                case ManualSaveBlock.Presentation: return "演出进行中，暂不可存档";
                case ManualSaveBlock.ChoiceNotReady: return "选项准备中，暂不可存档";
                case ManualSaveBlock.Dice: return "请在投骰前或后续对白处存档";
                case ManualSaveBlock.UnsupportedScene: return "此处不可存档";
                default: return "请在稳定对白或普通选项处存档";
            }
        }
    }
}
