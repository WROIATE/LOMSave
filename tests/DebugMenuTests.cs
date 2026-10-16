using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;

namespace Mortal.Core
{
    public enum GameStatType { 體力, 變心 = 30, 毒藥 = 18, 武學點數 = 31, 鍛造 = 17, 全毒藥點數 = 37, 全武學點數 = 32, 全鍛造點數 = 36,
        額外行動次數_1 = 26, 額外行動次數_2 = 39, 額外行動次數_3 = 40 }
    public class GameStat
    {
        public GameStatType StatType;
        public int Value, Min, Max = 100, SetCount;
        public Action OnSet;
        public void SetValue(int value) { SetCount++; OnSet?.Invoke(); Value = value; }
        public GameStat Total;
        public void AddValue(int delta, bool countTotal)
        {
            OnSet?.Invoke(); Value = Math.Max(Min, Math.Min(Max, Value + delta));
            if (countTotal && delta > 0 && Total != null) Total.Value = Math.Min(Total.Max, Total.Value + delta);
        }
    }
    public class PlayerStatData { public List<GameStat> List = new List<GameStat>(); }
    public class FlagData
    {
        public string name, DevNote;
        public int State;
        public string GetNameKey() { return "Flag/Name/" + name; }
    }
    public class FlagCollectionData { public List<FlagData> List = new List<FlagData>(); }
    public class MissionManagerData { public FlagCollectionData[] FlagsCollection; }
    public class LocalizationManager
    {
        public static LocalizationManager Instance = new LocalizationManager();
        public Resolver LocaleResolver = new Resolver();
    }
    public class Resolver
    {
        public Dictionary<string, string> Values = new Dictionary<string, string>();
        public string GetString(string key) { return Values.ContainsKey(key) ? Values[key] : key; }
    }
}
namespace LOMSave.Probe
{
    internal static class OptionImpactLocalization { internal static string GameStat(string key) { return "localized:" + key; } }
    internal static class CodeNameMapping
    {
        internal static CodeNameMapStore Store;
        internal static string Resolve(string scope, string code, string note, string localized)
        { return Store.Lookup(scope, code) ?? CheckpointNameRules.Resolve(code, note, localized); }
    }
    public static class DebugMenuTests
    {
        private static int _checks;
        private static void Check(bool pass, string message) { _checks++; if (!pass) throw new Exception(message); }
        public static string Run(string mapping)
        {
            string error;
            CodeNameMapping.Store = new CodeNameMapStore(mapping);
            Check(CodeNameMapping.Store.Reload(out error) == CodeNameMapStore.LoadResult.Loaded, "Production YAML loads");
            var named = new FlagData { name = "E13003", DevNote = "other description", State = 7 };
            var mapped = CodeNameMapping.Store.Lookup("flags", named.name);
            Check(!string.IsNullOrEmpty(mapped) && DebugMenuData.FlagName(named) == mapped, "F9 flag uses existing mapping ahead of developer note");
            Check(DebugMenuData.FlagTooltip(named) == mapped + "\n代码：E13003", "Hover contains only one mapped title and the exact flag code");
            var sameNote = new FlagData { name = "E13003", DevNote = mapped };
            Check(DebugMenuData.FlagTooltip(sameNote) == mapped + "\n代码：E13003", "Mapped description is not repeated in hover");
            var nanGong = new FlagData { name = "H01001", DevNote = "南宮世家好感度" };
            Check(DebugMenuData.FlagTooltip(nanGong) == "南宫世家好感度\n代码：H01001", "Traditional native note cannot duplicate the simplified title");
            Check(DebugMenuData.FlagTooltip(null) == "", "A missing hover target is safe");
            var unknown = new FlagData { name = "FutureUnknown" };
            Check(DebugMenuData.FlagName(unknown) == unknown.name, "Unmapped flag remains identifiable by code");
            unknown.DevNote = "native description";
            Check(DebugMenuData.FlagName(unknown) == "native description", "Developer note fallback works");
            unknown.DevNote = null;
            LocalizationManager.Instance.LocaleResolver.Values[unknown.GetNameKey()] = "native localization";
            Check(DebugMenuData.FlagName(unknown) == "native localization", "Native localized name fallback works");
            Check(DebugMenuData.Matches("e13003", named.name, mapped), "Search code is case insensitive");
            Check(DebugMenuData.Matches(mapped.Substring(0, 2), named.name, mapped), "Search mapped Chinese event name");
            Check(DebugMenuData.Matches(" ", named.name) && !DebugMenuData.Matches("missing", named.name), "Empty query and unmatched query");
            var flags = DebugMenuData.Flags(new MissionManagerData { FlagsCollection = new[] {
                new FlagCollectionData { List = new List<FlagData> { named, null, unknown } }, null,
                new FlagCollectionData { List = new List<FlagData> { named } }, new FlagCollectionData { List = null } } });
            Check(flags.Count == 2 && flags[0] == named, "All flag collections are included, nulls and duplicates excluded");
            Check(DebugMenuData.Flags(null).Count == 0 && DebugMenuData.Flags(new MissionManagerData()).Count == 0, "Unavailable game data is safe");
            Check(DebugMenuData.SetFlag(named, "-8", out error) && named.State == -8, "Signed flags can be edited without a fabricated 0/1 limit");
            Check(!DebugMenuData.SetFlag(named, "2.5", out error) && named.State == -8, "Invalid flag edit cannot mutate data");
            var heart = new GameStat { StatType = GameStatType.變心, Value = 7, Max = 10 };
            var poison = new GameStat { StatType = GameStatType.毒藥, Max = 500 };
            var martial = new GameStat { StatType = GameStatType.武學點數, Max = 500 };
            var total = new GameStat { StatType = GameStatType.全武學點數, Max = 350, Value = 80 };
            var future = new GameStat { StatType = (GameStatType)999 };
            var data = new PlayerStatData { List = new List<GameStat> { future, total, heart, poison, null, martial, heart } };
            var stats = DebugMenuData.Stats(data);
            Check(stats.Count == 5 && stats[0] == heart && stats.Contains(future), "Enumerate actual stats without a hardcoded whitelist or property visibility filter");
            Check(stats.Contains(poison) && stats.Contains(martial) && stats.Contains(total), "Alchemy, martial and cumulative points are separate editable entries");
            Check(DebugMenuData.Stats(null).Count == 0 && DebugMenuData.Stats(new PlayerStatData { List = null }).Count == 0, "Unavailable stats are safe");
            Check(DebugMenuData.StatName(poison) == "炼丹点数" && DebugMenuData.StatName(heart) == "变心", "Expose recognizable names for internal fields");
            Check(DebugMenuData.StatName(total) == "累计武学点数", "Lifetime total cannot be mistaken for spendable martial points");
            Check(DebugMenuData.StatName(future) == "localized:999", "Future fields use native localization path");
            heart.OnSet = () => Check(DebugMenuData.IsEditing, "Native setter is inside manual-edit scope");
            Check(DebugMenuData.SetStat(heart, "0", out error) && heart.Value == 0, "Change-heart can be cleared to zero");
            Check(DebugMenuData.SetStat(heart, "10", out error) && heart.Value == 10, "Change-heart maximum can be set");
            Check(!DebugMenuData.SetStat(heart, "11", out error) && heart.Value == 10 && heart.SetCount == 2, "Reject out-of-range value before native mutation");
            Check(!DebugMenuData.SetStat(heart, "-1", out error) && !DebugMenuData.SetStat(heart, "", out error), "Reject below minimum and empty input");
            Check(DebugMenuData.SetStat(martial, "250", out error) && martial.Value == 250 && total.Value == 80, "Stock editing does not silently change lifetime total");
            Check(DebugMenuData.SetStat(total, "300", out error) && total.Value == 300 && martial.Value == 250, "Lifetime total is separately writable");
            var assets = new GameStat { Min = -99999, Max = 99999 };
            Check(DebugMenuData.SetStat(assets, "-300", out error) && assets.Value == -300, "Native negative stat ranges remain editable");
            int value;
            Check(!DebugMenuData.TryValue("2147483648", int.MinValue, int.MaxValue, out value, out error), "Overflow input is rejected");
            Check(!DebugMenuData.TryValue("1e2", 0, 500, out value, out error), "Scientific notation cannot bypass integer validation");
            Check(DebugMenuData.Adjust(int.MaxValue, 100, int.MinValue, int.MaxValue) == int.MaxValue
                && DebugMenuData.Adjust(int.MinValue, -100, int.MinValue, int.MaxValue) == int.MinValue, "Quick adjustment cannot wrap integers");
            Check(DebugMenuData.Adjust(7, 100, 0, 10) == 10 && DebugMenuData.Adjust(7, -100, 0, 10) == 0, "Quick adjustment respects change-heart limits");
            Check(!DebugMenuData.SetStat(null, "1", out error) && !DebugMenuData.SetFlag(null, "1", out error), "Destroyed selection is refused");
            heart.OnSet = () => Check(DebugMenuData.IsEditing, "Native +/- is a manual edit, not a calendar action");
            DebugMenuData.AddStat(heart, -100);
            Check(heart.Value == 0, "Native minus reaches change-heart minimum");
            DebugMenuData.AddStat(heart, 100);
            Check(heart.Value == 10, "Native plus respects change-heart maximum");
            martial.Total = total; var previousTotal = total.Value;
            DebugMenuData.AddStat(martial, 1);
            Check(martial.Value == 251 && total.Value == previousTotal + 1, "Native +/- preserves native lifetime-point accumulation");
            DebugMenuData.AddStat(martial, -1);
            Check(martial.Value == 250 && total.Value == previousTotal + 1, "Native minus preserves native total semantics");
            heart.OnSet = () => { throw new InvalidOperationException("native failure"); };
            try { DebugMenuData.SetStat(heart, "1", out error); throw new Exception("Expected native error"); }
            catch (InvalidOperationException) { }
            Check(!DebugMenuData.IsEditing, "Native failure clears edit scope so later actions are still recorded");
            return _checks + " debug menu checks passed.";
        }
    }
}
