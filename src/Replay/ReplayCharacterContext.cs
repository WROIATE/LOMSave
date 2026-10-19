using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using MoonSharp.Interpreter;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LOMSave.Probe
{
    internal static class ReplayCharacterContext
    {
        private const string Marker = "-- LOMSave replay character context\n";
        private static readonly Regex CharacterCalls = new Regex(
            @"\bcharacters\.(?:Get|GetPortrait|LoadCharacterPortrait|Focus|RestoreRotation|ResetRotation|Shake|Rotate|MoveOffset)\s*\(\s*['""](?<key>[A-Za-z0-9_]+)['""]",
            RegexOptions.CultureInvariant);
        private static string _fallbackScript;
        private static Closure _storyClosure;
        private static string _preparedScript;
        private static PerformanceSample _performance;

        private sealed class PerformanceSample
        {
            internal string Script;
            internal int Generation, AssetCalls, CancelledLoads, CachedLoadsSkipped;
            internal double StartedAt, AssetSeconds, LuaCpuSeconds;
            internal bool Reported;
        }

        private static double Now { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }

        internal static void ReportPerformance(string reason)
        {
            var sample = _performance;
            if (sample == null || sample.Reported || sample.Generation != ReplayRuntime.Generation) return;
            sample.Reported = true;
            LOMSaveProbePlugin.WriteEvent("story_replay_performance", new
            {
                script = sample.Script, reason,
                seconds = Now - sample.StartedAt,
                characterLoadCalls = sample.AssetCalls,
                cachedCharacterLoadsSkipped = sample.CachedLoadsSkipped,
                characterLoadSeconds = sample.AssetSeconds,
                cancelledCharacterLoads = sample.CancelledLoads,
                luaResumeCpuSeconds = sample.LuaCpuSeconds
            });
        }

        internal static void PrepareFallback(string script) { _fallbackScript = script; }

        internal static bool HasLegacyContext(string source)
        { return source != null && source.StartsWith(Marker, StringComparison.Ordinal); }

        [HarmonyPrefix, HarmonyPatch(typeof(LuaEnvironment), "LoadLuaFunction", typeof(string), typeof(string))]
        private static void BeforeLoadLuaFunction(ref string __0, string __1)
        {
            var script = PlayerStatManagerData.Instance.CurrentStoryScript;
            if (string.IsNullOrEmpty(script) || __1 != script + ".LuaScript"
                || (!ReplayRuntime.IsReplaying && _fallbackScript != script)) return;
            ReportPerformance("script_changed");
            _performance = ReplayRuntime.IsReplaying ? new PerformanceSample
            {
                Script = script, Generation = ReplayRuntime.Generation, StartedAt = Now
            } : null;
            _fallbackScript = null;
            _preparedScript = script;
            // A complete route starts at the native story entry. Its own
            // loaders recreate inherited actors in the same order as play.
            // Eagerly loading every branch here only duplicates that work.
            // Legacy continuation recovery still needs its missing context.
            if (StoryReplayJournal.IsDeterministic)
            {
                LOMSaveProbePlugin.WriteEvent("story_replay_native_character_loading", new { script });
                return;
            }
            var keys = ReferencedCharacters(__0);
            if (keys.Count == 0 || __0.StartsWith(Marker, StringComparison.Ordinal)) return;
            __0 = WithCharacterContext(__0, keys);
            LOMSaveProbePlugin.WriteEvent("story_replay_characters_prepared", new { script, characters = keys });
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CharacterPlaceholder), "LoadCharacterAsset", typeof(string))]
        private static bool SkipCachedCharacterLoad(string __0,
            Dictionary<string, Character> ____characters, ref IEnumerator __result)
        {
            if (!ReplayRuntime.IsReplaying || !StoryReplayJournal.InLua
                || string.IsNullOrEmpty(__0) || ____characters == null
                || !____characters.TryGetValue(__0, out var character) || character == null) return true;
            // The native loader immediately completes for an existing actor.
            // Returning null also lets native runwait bypass the otherwise
            // empty Unity Task and its frame handoffs. Uncached actors still
            // await the complete native loader; say retains its frame yield.
            __result = null;
            if (_performance != null && !_performance.Reported
                && _performance.Generation == ReplayRuntime.Generation) _performance.CachedLoadsSkipped++;
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CharacterPlaceholder), "LoadCharacterAsset", typeof(string))]
        private static void AfterLoadCharacterAsset(ref IEnumerator __result)
        {
            var sample = _performance;
            if (!ReplayRuntime.IsReplaying || sample == null || sample.Reported
                || sample.Generation != ReplayRuntime.Generation || __result == null) return;
            __result = MeasureCharacterLoad(__result, sample);
        }

        private static IEnumerator MeasureCharacterLoad(IEnumerator routine, PerformanceSample sample)
        {
            var started = Now;
            var completed = false;
            sample.AssetCalls++;
            try
            {
                while (sample.Generation == ReplayRuntime.Generation)
                {
                    if (!routine.MoveNext()) { completed = true; yield break; }
                    yield return routine.Current;
                }
            }
            finally
            {
                sample.AssetSeconds += Now - started;
                if (!completed) sample.CancelledLoads++;
                (routine as IDisposable)?.Dispose();
            }
        }

        internal static List<string> ReferencedCharacters(string source)
        {
            var keys = new List<string>();
            if (string.IsNullOrEmpty(source)) return keys;
            // Generated scripts use literal character keys. Comments are not
            // resource dependencies, and dynamic keys remain native Lua.
            var code = Regex.Replace(source, @"--\[(=*)\[[\s\S]*?\]\1\]|--[^\r\n]*", "");
            foreach (Match match in CharacterCalls.Matches(code))
            {
                var key = match.Groups["key"].Value;
                if (!keys.Contains(key)) keys.Add(key);
            }
            return keys;
        }

        internal static string WithCharacterContext(string source, List<string> keys)
        {
            if (string.IsNullOrEmpty(source) || keys.Count == 0 || source.StartsWith(Marker, StringComparison.Ordinal)) return source;
            var builder = new StringBuilder(Marker);
            // Continue scripts may use actors inherited from the previous
            // script. Await the native cached loader before any stage calls;
            // this neither shows portraits nor changes story command counts.
            foreach (var key in keys)
                builder.Append("runwait(characters.LoadCharacterAsset(\"").Append(key).Append("\"))\n");
            return builder.Append(source).ToString();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PortraitController), "Show", typeof(PortraitOptions))]
        private static void SeedInheritedPortrait(PortraitOptions __0)
        {
            if (_preparedScript != PlayerStatManagerData.Instance.CurrentStoryScript) return;
            var character = __0?.character;
            if (character?.State == null || __0.portrait != null || character.State.portrait != null) return;
            // Dynamic actors can have a null prefab ProfileSprite even though
            // Setup already loaded their default portrait. A continuation's
            // move-only show inherits that sprite in normal play. Supply the
            // loaded default to the native Show instead of creating a second
            // portrait object or issuing an extra offstage show.
            foreach (var sprite in character.Portraits)
                if (sprite != null) { __0.portrait = sprite; break; }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(LuaEnvironment), "LoadLuaFunction", typeof(string), typeof(string))]
        private static void AfterLoadLuaFunction(string __1, Closure __result)
        {
            var script = PlayerStatManagerData.Instance.CurrentStoryScript;
            if (!string.IsNullOrEmpty(script) && __1 == script + ".LuaScript")
                _storyClosure = __result;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(LuaEnvironment), "RunLuaCoroutine")]
        private static void AfterRunLuaCoroutine(Closure __0, ref IEnumerator __result)
        {
            if (__result == null || !ReferenceEquals(__0, _storyClosure)) return;
            _storyClosure = null;
            __result = Guard(__result);
        }

        internal static IEnumerator Guard(IEnumerator routine)
        {
            var visit = StoryReplayJournal.Visit;
            var generation = ReplayRuntime.Generation;
            var sample = _performance;
            try
            {
                while (true)
                {
                    if (generation != ReplayRuntime.Generation) yield break;
                    bool more;
                    object current = null;
                    try
                    {
                        var measuring = ReplayRuntime.IsReplaying && sample != null
                            && !sample.Reported && sample.Generation == generation;
                        var started = measuring ? Now : 0;
                        StoryReplayJournal.EnterLua();
                        try { more = routine.MoveNext(); if (more) current = routine.Current; }
                        finally
                        {
                            StoryReplayJournal.ExitLua();
                            if (measuring) sample.LuaCpuSeconds += Now - started;
                        }
                    }
                    catch (Exception exception)
                    {
                        // Once the checkpoint is reached this is ordinary Lua:
                        // preserve native exception handling outside recovery.
                        if (!ReplayRuntime.IsReplaying) throw;
                        LOMSaveProbePlugin.WriteError("story_replay_coroutine_failed", exception);
                        StoryCommandTrace.AbortActiveReplay("lua_coroutine_failed");
                        yield break;
                    }
                    if (!more)
                    {
                        if (ReplayRuntime.IsReplaying && visit == StoryReplayJournal.Visit)
                            StoryCommandTrace.AbortActiveReplay("lua_completed_before_checkpoint");
                        yield break;
                    }
                    yield return current;
                }
            }
            finally { (routine as IDisposable)?.Dispose(); }
        }
    }
}
