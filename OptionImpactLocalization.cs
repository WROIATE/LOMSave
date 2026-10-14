using Mortal.Core;
using OBB.Framework.Utils;
using System;

namespace LOMSave.Probe
{
    internal static class OptionImpactLocalization
    {
        internal static string GameStat(string key)
        {
            GameStatType type;
            if (!EnumUtils.TryParseByStringValue(key, out type) && !Enum.TryParse(key, out type))
            {
                return Simplified(key);
            }
            try
            {
                var stat = PlayerStatManagerData.Instance.Stats.Get(type);
                if (stat != null)
                {
                    var localeKey = stat.GetLocaleKey();
                    var name = LocalizationManager.Instance.LocaleResolver.GetString(localeKey);
                    if (!string.IsNullOrEmpty(name)
                        && !string.Equals(name, localeKey, StringComparison.Ordinal))
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }
            return type == GameStatType.心理衛生 ? "心相" : Simplified(type.ToString());
        }

        internal static string Relationship(string key)
        {
            RelationshipStatType type;
            return EnumUtils.TryParseByStringValue(key, out type)
                ? Simplified(type.ToString())
                : Simplified(key);
        }

        internal static string Simplified(string value)
        {
            return value
                .Replace("體", "体").Replace("內", "内").Replace("輕", "轻")
                .Replace("銀", "银").Replace("學", "学").Replace("衛", "卫")
                .Replace("處", "处").Replace("養", "养").Replace("規", "规")
                .Replace("聲", "声").Replace("數", "数").Replace("鍛", "锻")
                .Replace("藥", "药").Replace("貢", "贡").Replace("廚", "厨")
                .Replace("稱", "称").Replace("愛", "爱").Replace("額", "额")
                .Replace("陰", "阴").Replace("陽", "阳").Replace("禦", "御")
                .Replace("變", "变").Replace("門", "门").Replace("資", "资")
                .Replace("劍", "剑").Replace("軟", "软").Replace("槍", "枪")
                .Replace("戰", "战").Replace("設", "设").Replace("擊", "击")
                .Replace("師", "师").Replace("葉", "叶").Replace("嘯", "啸")
                .Replace("劉", "刘").Replace("龍", "龙").Replace("淵", "渊")
                .Replace("遠", "远").Replace("宮", "宫").Replace("韞", "韫")
                .Replace("蘭", "兰").Replace("螢", "萤").Replace("壯", "壮")
                .Replace("趙", "赵").Replace("車", "车").Replace("軒", "轩")
                .Replace("塵", "尘").Replace("靈", "灵").Replace("雲", "云")
                .Replace("動", "动").Replace("兩", "两").Replace("敗", "败")
                .Replace("買", "买").Replace("賣", "卖").Replace("發", "发")
                .Replace("採", "采").Replace("氣", "气").Replace("練", "练")
                .Replace("觸", "触").Replace("條", "条").Replace("當", "当")
                .Replace("開", "开").Replace("關", "关").Replace("劇", "剧")
                .Replace("溫", "温").Replace("義", "义").Replace("鋪", "铺")
                .Replace("請", "请").Replace("進", "进");
        }
    }
}
