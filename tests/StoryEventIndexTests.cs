using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace UnityEngine
{
    public class Object { public string name; }
    public static class Resources
    {
        public static List<Object> Objects = new List<Object>(); public static int Queries;
        public static Object[] FindObjectsOfTypeAll(Type type) { Queries++; return Objects.Where(type.IsInstanceOfType).ToArray(); }
    }
    public static class Application { public static string dataPath, version; }
}
namespace HarmonyLib
{
    public static class AccessTools { public static FieldInfo Field(Type type, string key) { return type.GetField(key); } }
    public sealed class Traverse
    {
        private object _target; private FieldInfo _field;
        public static Traverse Create(object value) { return new Traverse { _target = value }; }
        public Traverse Field(string name) { _field = _target.GetType().GetField(name); return this; }
        public void SetValue(object value) { _field.SetValue(_target, value); }
    }
}
namespace Mortal.Core
{
    public enum StatCheckType { Constant }
    public class TestStat : UnityEngine.Object { public int Value; }
    public class StatValueReference
    {
        public StatCheckType _checkType; public bool _negative;
        public TestStat _stat, _relationship, _flag, _book, _misc, _specialItem, _gameTime, _talentData, _upgradeItemData, _bookCollection, _libraryAchieve;
        public int GetValue()
        {
            var kind = (int)_checkType;
            if (kind == 7 || kind == 8 || kind == 16) throw new Exception("Unsafe native getter");
            var target = kind == 1 ? _stat : kind == 2 ? _relationship : kind == 3 ? _flag : kind == 9 ? _gameTime : _book;
            return target.Value * (_negative && kind <= 3 ? -1 : 1);
        }
    }
}
namespace LOMSave.Probe
{
    internal static class LOMSaveProbePlugin
    {
        internal static void WriteEvent(string name, object data) { }
        internal static void WriteError(string name, Exception error) { throw error; }
    }
    public static class StoryEventIndexTests
    {
        private static int _checks;
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
        private static StoryIndexCondition Compare(string left, int op, string right)
        { return new StoryIndexCondition { Logic = 0, Comparisons = new[] { new StoryIndexComparison { Left = left, Op = op, Right = right } } }; }
        private static StoryIndexGroup Literal(int value)
        { return new StoryIndexGroup { Operation = 1, Values = new[] { new StoryIndexValue { Constant = value } } }; }
        private static void Reject(Action action, string message)
        { var rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, message); }
        public static string Run(string root, string fixture)
        {
            var stat = new Mortal.Core.TestStat { name = "LiveFlag", Value = 1 };
            UnityEngine.Resources.Objects.Add(stat);
            var source = new StoryIndexValue { Kind = 3, Field = "_flag", Target = stat.name };
            var read = typeof(StoryEventIndex).GetMethod("ReadValue", BindingFlags.Static | BindingFlags.NonPublic);
            var data = new StoryEventIndexData { Version = 1, GameVersion = "test", Groups = new Dictionary<string, StoryIndexGroup> {
                ["one"] = Literal(1), ["two"] = Literal(2),
                ["live"] = new StoryIndexGroup { Operation = 1, Values = new[] { source } },
                ["random"] = new StoryIndexGroup { Operation = 1, Values = new[] { new StoryIndexValue { Kind = 7 } } },
                ["negative"] = new StoryIndexGroup { Operation = 1, Values = new[] { new StoryIndexValue { Constant = 2, Negative = true } } }
            }, Conditions = new Dictionary<string, StoryIndexCondition[]>(), Switches = new Dictionary<string, StoryIndexSwitch>(),
                Pools = new Dictionary<string, StoryIndexEvent[]>(), ReadNative = x => (int?)read.Invoke(null, new object[] { x }) };
            Check(data.Check(Compare("live", 0, "one")) == true, "Cold condition reads current native flag");
            stat.Value = 2;
            Check(data.Check(Compare("live", 0, "one")) == false && data.Check(Compare("live", 0, "two")) == true,
                "Native value binding caches identity, never the player's value");
            Check(UnityEngine.Resources.Queries == 1, "Subsequent checks do not enumerate resources again");
            Check(data.Group("negative") == -2 && data.Group("missing") == null, "Signed constants and unknown expression handling");
            var queries = UnityEngine.Resources.Queries;
            Check(data.Group("random") == null && UnityEngine.Resources.Queries == queries, "Random values remain unknown without calling native RNG");
            var condition = Compare("one", 2, "two");
            for (var op = 0; op < 6; op++)
                Check(data.Check(Compare("one", op, "two")) == new[] { false, true, true, true, false, false }[op], "Native comparison operator " + op);
            var compound = new StoryIndexCondition { Logic = 0, Comparisons = new[] {
                new StoryIndexComparison { Left = "random", Right = "one" }, new StoryIndexComparison { Left = "one", Right = "two" } } };
            Check(data.Check(compound) == false, "Known false AND suppresses an uncertain route");
            compound.Logic = 1; compound.Comparisons[1].Op = 2;
            Check(data.Check(compound) == true, "Known true OR permits an otherwise uncertain route");
            data.Conditions["test"] = new[] { condition };
            data.Groups["nested"] = new StoryIndexGroup { Operation = 1, Values = new[] { new StoryIndexValue { Kind = 16, Condition = "test" } } };
            Check(data.Group("nested") == 1, "Nested checkpoint conditions use the index without native Execute");
            data.Groups["cycle"] = new StoryIndexGroup { Operation = 1, Values = new[] { new StoryIndexValue { Kind = 8, Group = "cycle" } } };
            Check(data.Group("cycle") == null, "Expression cycles are bounded");
            data.Switches["choice"] = new StoryIndexSwitch { Items = new[] { Compare("one", 0, "two"), condition } };
            Check(data.Selector("choice") == 2, "Read-only selector returns first satisfied arm");
            data.Switches["choice"].Counted = true;
            Check(data.Selector("choice") == null, "Counted selectors cannot guess mutable execution counts");
            var item = new StoryIndexEvent { Rate = 10, Conditional = true, Active = condition,
                Rates = new[] { new StoryIndexRate { Condition = condition, Rate = 5 } } };
            Check(data.Rate(item) == 15, "Native baseline and conditional weight additions");
            item.Rates[0].Condition = Compare("random", 0, "one");
            Check(data.Rate(item) == null, "Uncertain nonzero weight cannot advertise eligibility");
            item.Active = Compare("one", 0, "two");
            Check(data.Rate(item) == 0, "Disabled encounter remains unavailable");

            Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "Managed"));
            data.Hashes = new Dictionary<string, string>();
            foreach (var file in new[] { "sharedassets2.assets", "Managed/Mortal.Core.dll", "Managed/Mortal.Story.dll" })
            {
                File.WriteAllText(Path.Combine(root, file), file);
                using (var sha = SHA256.Create()) data.Hashes[file] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, file)))).Replace("-", "");
            }
            var path = Path.Combine(root, "index.json"); File.WriteAllText(path, JsonConvert.SerializeObject(data));
            Check(StoryEventIndex.Load(path, root, "test").Conditions.Count == 1, "Matching game build loads index");
            Reject(() => StoryEventIndex.Load(path, root, "different"), "Game version changes reject stale index");
            File.AppendAllText(Path.Combine(root, "sharedassets2.assets"), "changed");
            Reject(() => StoryEventIndex.Load(path, root, "test"), "Changed native configuration rejects index even if version string is unchanged");
            File.WriteAllText(Path.Combine(root, "sharedassets2.assets"), "sharedassets2.assets");
            File.WriteAllText(Path.Combine(root, "sharedassets3.assets"), "additional expressions");
            using (var sha = SHA256.Create()) data.Hashes["sharedassets3.assets"] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, "sharedassets3.assets")))).Replace("-", "");
            File.WriteAllText(path, JsonConvert.SerializeObject(data));
            Check(StoryEventIndex.Load(path, root, "test").Hashes.Count == 4, "All contributing expression files can be fingerprinted");
            File.AppendAllText(Path.Combine(root, "sharedassets3.assets"), "changed");
            Reject(() => StoryEventIndex.Load(path, root, "test"), "Changes to a secondary expression asset also reject the index");
            var shipped = JsonConvert.DeserializeObject<StoryEventIndexData>(File.ReadAllText(fixture));
            Check(shipped.Pools.Count > 50 && shipped.Conditions.Count > 500 && shipped.Hashes.Count >= 3, "Shipped index contains native game data and source fingerprints");
            Check(shipped.Groups.Keys.Select(x => x.Split(':')[0]).All(shipped.Hashes.ContainsKey),
                "Source fingerprint covers every file contributing exported expressions");
            foreach (var c in shipped.Conditions.Values.Concat(shipped.Switches.Values.Select(x => x.Items)))
                foreach (var compare in c.SelectMany(x => x.Comparisons))
                    Check(shipped.Groups.ContainsKey(compare.Left) && shipped.Groups.ContainsKey(compare.Right), "Every exported checkpoint comparison resolves its expressions");
            return _checks + " story event index assertions passed";
        }
    }
}
