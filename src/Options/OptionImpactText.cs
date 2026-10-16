using System;

namespace LOMSave.Probe
{
    internal static class OptionImpactText
    {
        internal static string Scene(string key)
        {
            switch (key)
            {
                case "Battle": return "进入混战";
                case "Combat": return "进入对决";
                case "Pinball": return "进入弹珠台";
                case "GameOver": return "进入死亡结局";
                case "End": return "进入结局";
                case "DemoEnd": return "结束试玩";
                // Ordinary menu/story transitions carry no additional option effect.
                case "Free":
                case "Story":
                case "Title": return null;
                default: return string.IsNullOrEmpty(key) ? null : "进入场景“" + key + "”";
            }
        }

        internal static string UpgradeItem(string name, int? level, int? maxLevel)
        {
            var description = "替换装备为“" + name + "”";
            if (!level.HasValue) return description + "（等级由剧情决定）";
            if (level.Value < 0) return description + "（保留该装备升级进度）";
            var target = maxLevel.HasValue
                ? Math.Max(0, Math.Min(level.Value, maxLevel.Value)) : level.Value;
            return description + "（等级 " + target + "）";
        }
    }
}
