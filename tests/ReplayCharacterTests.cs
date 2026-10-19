using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method, params Type[] args) { } }
}
namespace Fungus
{
    public class LuaEnvironment { }
    public class PortraitController { }
    public class PortraitOptions { public Character character; public object portrait; }
    public class Character { public PortraitState State = new PortraitState(); public System.Collections.Generic.List<object> Portraits = new System.Collections.Generic.List<object>(); }
    public class PortraitState { public object portrait; }
}
namespace MoonSharp.Interpreter { public class Closure { } }
namespace Mortal.Core
{
    public class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public string CurrentStoryScript;
    }
}
namespace Mortal.Story { public class CharacterPlaceholder { } }
namespace LOMSave.Probe
{
    internal class StoryCommandCheckpoint { public int Visit; public string Script; }
    internal static class ReplayRuntime { internal static bool IsReplaying; internal static int Generation { get; set; } }
    internal static class StoryCommandTrace
    {
        internal static int Aborts;
        internal static string Reason;
        internal static bool AbortActiveReplay(string reason)
        { Aborts++; Reason = reason; ReplayRuntime.IsReplaying = false; return true; }
    }
    internal static class LOMSaveProbePlugin
    {
        internal static int Errors;
        internal static void WriteEvent(string name, object payload) { }
        internal static void WriteError(string name, Exception exception) { Errors++; }
    }
    public static class ReplayCharacterTests
    {
        private static int _checks;
        private const string NativeChunk = "ch6_10_1_part_2.LuaScript";
        private const string Source = "stage.show{character=characters.Get(\"player\")}\ncharacters.LoadCharacterPortrait(\"girl5\", \"normal\")\ncharacters.Focus(\"player\")";
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
        private static MethodInfo Hook(string method)
        { return typeof(ReplayCharacterContext).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic); }
        public static string Prepare(string source, bool replay, string chunk = NativeChunk)
        {
            ReplayRuntime.IsReplaying = replay;
            Mortal.Core.PlayerStatManagerData.Instance.CurrentStoryScript = "ch6_10_1_part_2";
            var args = new object[] { source, chunk };
            Hook("BeforeLoadLuaFunction").Invoke(null, args);
            return (string)args[0];
        }
        private sealed class Routine : IEnumerator, IDisposable
        {
            internal bool Fail, Disposed;
            private int _step;
            public object Current { get { return "native-yield"; } }
            public bool MoveNext() { if (_step++ == 0) return true; if (Fail) throw new InvalidOperationException("native Lua actor missing"); return false; }
            public void Reset() { throw new NotSupportedException(); }
            public void Dispose() { Disposed = true; }
        }
        public static string Run()
        {
            Check(ReplayCharacterContext.ReferencedCharacters(null).Count == 0, "Missing source has no dependencies");
            var keys = ReplayCharacterContext.ReferencedCharacters(Source);
            Check(keys.SequenceEqual(new[] { "player", "girl5" }), "Literal keys are unique in first-use order");
            Check(ReplayCharacterContext.ReferencedCharacters("-- characters.Get(\"absent\")\n--[[characters.Get(\"absent2\")]]\ncharacters.Get('player')").SequenceEqual(new[] { "player" }), "Comments are excluded and single quotes supported");
            Check(ReplayCharacterContext.ReferencedCharacters("characters.Get(dynamicKey)").Count == 0, "Dynamic expressions remain native");
            var prepared = Prepare(Source, true);
            Check(prepared.EndsWith(Source, StringComparison.Ordinal), "The original script remains byte-for-byte at the end");
            Check(prepared.IndexOf("LoadCharacterAsset(\"player\")", StringComparison.Ordinal) < prepared.IndexOf("stage.show", StringComparison.Ordinal), "Inherited actors load before the first stage call");
            Check(prepared.IndexOf("LoadCharacterAsset(\"girl5\")", StringComparison.Ordinal) < prepared.IndexOf("LoadCharacterPortrait", StringComparison.Ordinal), "Portrait assets have an initialized actor");
            Check(Prepare(prepared, true) == prepared, "Preparing a chunk twice cannot duplicate the preamble");
            Check(Prepare(Source, false) == Source, "Normal story execution is unchanged");
            Check(Prepare(Source, true, "tmp.LuaScript") == Source, "Command-trace helper chunks are not rewritten");
            Check(Prepare(Source, true, "other.LuaScript") == Source, "Unrelated chunks are not rewritten");
            Check(Prepare("return 1", true) == "return 1", "Chunks without character calls stay unchanged");
            var routeHash = new string('a', 64);
            StoryReplayJournal.Configure(new[] { new StoryScriptVisit { Visit = 1, Script = "ch6_10_1_part_2", SourceHash = routeHash } }, null);
            Check(Prepare(Source, true) == Source, "Complete native routes do not eagerly load unvisited branches");
            StoryReplayJournal.Configure(null, null);
            Prepare(Source, true);
            var sprite = new object(); var actor = new Fungus.Character(); actor.Portraits.Add(sprite);
            var show = new Fungus.PortraitOptions { character = actor };
            Hook("SeedInheritedPortrait").Invoke(null, new object[] { show });
            Check(ReferenceEquals(show.portrait, sprite), "A dynamic actor's loaded default seeds an inherited move-only show");
            var explicitSprite = new object(); show.portrait = explicitSprite;
            Hook("SeedInheritedPortrait").Invoke(null, new object[] { show });
            Check(ReferenceEquals(show.portrait, explicitSprite), "An explicitly requested expression is never replaced");
            show.portrait = null; actor.State.portrait = explicitSprite;
            Hook("SeedInheritedPortrait").Invoke(null, new object[] { show });
            Check(show.portrait == null, "A previous valid expression remains inherited normally");
            ReplayCharacterContext.PrepareFallback("ch6_10_1_part_2");
            Check(Prepare(Source, false, "tmp.LuaScript") == Source, "Helpers do not consume fallback context");
            Check(Prepare(Source, false).EndsWith(Source, StringComparison.Ordinal) && Prepare(Source, false) == Source, "Fallback context loads actors once, without enabling replay");

            var load = new Routine();
            var loadArgs = new object[] { load };
            Hook("AfterLoadCharacterAsset").Invoke(null, loadArgs);
            Check(ReferenceEquals(loadArgs[0], load), "Ordinary play does not wrap asset loads for profiling");
            Prepare(Source, true);
            Hook("AfterLoadCharacterAsset").Invoke(null, loadArgs);
            var measuredLoad = (IEnumerator)loadArgs[0];
            Check(measuredLoad.MoveNext() && (string)measuredLoad.Current == "native-yield",
                "Profiling preserves each native asset yield without inserting another frame");
            Check(!measuredLoad.MoveNext() && load.Disposed,
                "A measured completed asset coroutine is disposed");
            load = new Routine(); loadArgs = new object[] { load };
            Hook("AfterLoadCharacterAsset").Invoke(null, loadArgs);
            measuredLoad = (IEnumerator)loadArgs[0]; measuredLoad.MoveNext(); ReplayRuntime.Generation++;
            Check(!measuredLoad.MoveNext() && load.Disposed,
                "A superseded measured load stops before another native step");
            Prepare(Source, true);
            var cachedActors = new System.Collections.Generic.Dictionary<string, Fungus.Character> { { "player", actor } };
            var cachedArgs = new object[] { "player", cachedActors, load };
            Check((bool)Hook("SkipCachedCharacterLoad").Invoke(null, cachedArgs),
                "Non-Lua callers retain the native IEnumerator contract during recovery");
            StoryReplayJournal.EnterLua();
            Check(!(bool)Hook("SkipCachedCharacterLoad").Invoke(null, cachedArgs) && cachedArgs[2] == null,
                "An already loaded actor bypasses the empty native Task during replay");
            cachedArgs = new object[] { "absent", cachedActors, load };
            Check((bool)Hook("SkipCachedCharacterLoad").Invoke(null, cachedArgs) && ReferenceEquals(cachedArgs[2], load),
                "A missing actor keeps its complete native asset coroutine");
            cachedActors["player"] = null;
            cachedArgs = new object[] { "player", cachedActors, load };
            Check((bool)Hook("SkipCachedCharacterLoad").Invoke(null, cachedArgs), "An invalid cached actor is not skipped");
            cachedActors["player"] = actor; ReplayRuntime.IsReplaying = false;
            Check((bool)Hook("SkipCachedCharacterLoad").Invoke(null, cachedArgs), "Ordinary play keeps native cached-load behavior");
            StoryReplayJournal.ExitLua();
            ReplayRuntime.IsReplaying = true;

            var closure = new MoonSharp.Interpreter.Closure();
            ReplayRuntime.IsReplaying = true;
            Hook("AfterLoadLuaFunction").Invoke(null, new object[] { NativeChunk, closure });
            var native = new Routine();
            var helperArgs = new object[] { new MoonSharp.Interpreter.Closure(), native };
            Hook("AfterRunLuaCoroutine").Invoke(null, helperArgs);
            Check(ReferenceEquals(helperArgs[1], native), "Unrelated Lua coroutines are not guarded");
            var args = new object[] { closure, native };
            Hook("AfterRunLuaCoroutine").Invoke(null, args);
            Check(!ReferenceEquals(args[1], native), "The matching replay closure receives the guard");
            var again = new object[] { closure, native };
            Hook("AfterRunLuaCoroutine").Invoke(null, again);
            Check(ReferenceEquals(again[1], native), "A tracked closure is consumed once");

            native = new Routine { Fail = true };
            var guarded = ReplayCharacterContext.Guard(native);
            ReplayRuntime.IsReplaying = true;
            var aborts = StoryCommandTrace.Aborts;
            Check(guarded.MoveNext() && (string)guarded.Current == "native-yield", "The native yield is preserved");
            Check(!guarded.MoveNext() && StoryCommandTrace.Aborts == aborts + 1 && StoryCommandTrace.Reason == "lua_coroutine_failed", "A real coroutine exception aborts immediately");
            Check(native.Disposed && LOMSaveProbePlugin.Errors == 1, "Failure disposes the coroutine and records the exception");
            native = new Routine(); guarded = ReplayCharacterContext.Guard(native); ReplayRuntime.IsReplaying = true;
            guarded.MoveNext();
            Check(!guarded.MoveNext() && StoryCommandTrace.Reason == "lua_completed_before_checkpoint", "An early Lua completion cannot strand the replay mask");
            native = new Routine(); guarded = ReplayCharacterContext.Guard(native); ReplayRuntime.IsReplaying = true;
            guarded.MoveNext(); ReplayRuntime.IsReplaying = false; aborts = StoryCommandTrace.Aborts;
            Check(!guarded.MoveNext() && StoryCommandTrace.Aborts == aborts && native.Disposed, "Normal completion after reaching the checkpoint is unchanged");
            native = new Routine { Fail = true }; guarded = ReplayCharacterContext.Guard(native); ReplayRuntime.IsReplaying = true;
            guarded.MoveNext(); ReplayRuntime.IsReplaying = false;
            var propagated = false;
            try { guarded.MoveNext(); } catch (InvalidOperationException) { propagated = true; }
            Check(propagated && native.Disposed && StoryCommandTrace.Aborts == aborts, "Exceptions after replay keep native handling");

            native = new Routine { Fail = true }; guarded = ReplayCharacterContext.Guard(native);
            ReplayRuntime.IsReplaying = true; guarded.MoveNext(); ReplayRuntime.Generation++;
            Check(!guarded.MoveNext() && native.Disposed && StoryCommandTrace.Aborts == aborts,
                "A superseded recovery disposes its old coroutine before any further native step");

            StoryReplayJournal.Configure(null, null);
            var owner = new object(); var hash = new string('a', 64);
            StoryReplayJournal.Begin(owner, "origin", hash, false);
            native = new Routine(); guarded = ReplayCharacterContext.Guard(native);
            ReplayRuntime.IsReplaying = true; guarded.MoveNext();
            Check(!StoryReplayJournal.InLua, "Lua scope is released while the coroutine is suspended");
            StoryReplayJournal.Begin(owner, "continuation", hash, false);
            Check(!guarded.MoveNext() && native.Disposed && StoryCommandTrace.Aborts == aborts,
                "A parent script may finish after starting its continuation without aborting that continuation");
            return _checks + " replay character context assertions passed";
        }
    }
}
