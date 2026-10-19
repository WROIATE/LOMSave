using System;
using System.Collections.Generic;

namespace LOMSave.Probe
{
    // Only metadata lives here. No resource loading, state serialization or
    // disk writes occur while dialogue and checkpoint outcomes are recorded.
    internal static class StoryReplayJournal
    {
        private const int MaximumVisits = 512, MaximumOutcomes = 16384;
        private static readonly List<StoryScriptVisit> Visits = new List<StoryScriptVisit>();
        private static readonly List<StoryReplayOutcome> Outcomes = new List<StoryReplayOutcome>();
        private static IList<StoryScriptVisit> _expectedVisits;
        private static IList<StoryReplayOutcome> _expectedOutcomes;
        private static object _owner;
        private static string _nativeStart;
        private static int _outcomeIndex, _outcomeCursor;
        private static bool _complete;
        private static int _luaDepth;
        internal static bool InLua { get { return _luaDepth > 0; } }
        internal static void EnterLua() { _luaDepth++; }
        internal static void ExitLua() { _luaDepth = Math.Max(0, _luaDepth - 1); }
        internal static void Invalidate() { _complete = false; }
        internal static bool StartsAnchor { get; private set; }
        internal static int Visit { get { return Visits.Count; } }
        internal static bool IsDeterministic { get { return _expectedVisits != null; } }
        internal static bool CanCapture { get { return _complete && Visits.Count > 0; } }

        internal static void Configure(IList<StoryScriptVisit> visits, IList<StoryReplayOutcome> outcomes)
        {
            Visits.Clear(); Outcomes.Clear(); _owner = null; _nativeStart = null; _outcomeIndex = _outcomeCursor = 0;
            StartsAnchor = false; _complete = visits != null && visits.Count > 0;
            _expectedVisits = _complete ? visits : null;
            _expectedOutcomes = _complete ? outcomes ?? new List<StoryReplayOutcome>() : null;
        }

        internal static string Validate(IList<StoryScriptVisit> visits, IList<StoryReplayOutcome> outcomes, StoryCommandCheckpoint target)
        {
            if (visits == null || visits.Count == 0) return "route_missing";
            if (visits.Count > MaximumVisits || outcomes == null || outcomes.Count > MaximumOutcomes) return "route_limit";
            if (target == null || target.Visit != visits.Count || visits[visits.Count - 1]?.Script != target.Script) return "route_target_mismatch";
            for (var i = 0; i < visits.Count; i++)
                if (visits[i] == null || visits[i].Visit != i + 1 || string.IsNullOrEmpty(visits[i].Script)
                    || visits[i].SourceHash == null || visits[i].SourceHash.Length != 64) return "route_visit_invalid";
            var visit = 0; var index = 0;
            foreach (var outcome in outcomes)
            {
                if (outcome == null || outcome.Visit < visit || outcome.Visit < 1 || outcome.Visit > visits.Count
                    || string.IsNullOrEmpty(outcome.Kind) || outcome.Key == null || outcome.Value == null) return "route_outcome_invalid";
                if (outcome.Visit != visit) { visit = outcome.Visit; index = 0; }
                if (outcome.Index != ++index) return "route_outcome_order";
            }
            return null;
        }

        internal static string Begin(object owner, string script, string hash, bool restoring, string nativeStart = null)
        {
            StartsAnchor = false;
            if (IsDeterministic)
            {
                if (Visits.Count >= _expectedVisits.Count) return "route_extra_script";
                var expected = _expectedVisits[Visits.Count];
                if (expected.Script != script || (hash != null && expected.SourceHash != hash)) return "route_script_mismatch";
                _owner = owner;
                Visits.Add(Clone(expected));
            }
            else
            {
                var nativeEntry = !restoring && !string.IsNullOrEmpty(nativeStart)
                    && string.Equals(nativeStart, script, StringComparison.Ordinal)
                    && (!_complete || !string.Equals(nativeStart, _nativeStart, StringComparison.Ordinal));
                if (!ReferenceEquals(owner, _owner) || Visits.Count == 0 || nativeEntry)
                {
                    Visits.Clear(); Outcomes.Clear(); _owner = owner;
                    _complete = !restoring; StartsAnchor = true;
                }
                if (Visits.Count >= MaximumVisits) { _complete = false; return null; }
                Visits.Add(new StoryScriptVisit { Visit = Visits.Count + 1, Script = script, SourceHash = hash });
                if (string.IsNullOrEmpty(hash)) _complete = false;
            }
            _nativeStart = nativeStart;
            _outcomeIndex = 0;
            return null;
        }

        internal static string Outcome(string kind, string key, string actual, out string value)
        {
            value = actual;
            if (Visit == 0) return null;
            var index = ++_outcomeIndex;
            if (IsDeterministic)
            {
                if (_outcomeCursor >= _expectedOutcomes.Count) return "route_outcome_missing";
                var expected = _expectedOutcomes[_outcomeCursor];
                if (expected.Visit != Visit || expected.Index != index || expected.Kind != kind || expected.Key != key)
                    return "route_outcome_mismatch";
                _outcomeCursor++;
                value = expected.Value;
            }
            if (Outcomes.Count >= MaximumOutcomes) { _complete = false; return null; }
            Outcomes.Add(new StoryReplayOutcome { Visit = Visit, Index = index, Kind = kind, Key = key, Value = value });
            return null;
        }

        internal static bool Finish()
        {
            if (IsDeterministic && (_outcomeCursor != _expectedOutcomes.Count || Visits.Count != _expectedVisits.Count)) return false;
            _expectedVisits = null; _expectedOutcomes = null;
            return true;
        }

        internal static List<StoryScriptVisit> CaptureVisits()
        {
            if (!CanCapture) return null;
            var result = new List<StoryScriptVisit>(Visits.Count);
            foreach (var visit in Visits) result.Add(Clone(visit));
            return result;
        }

        internal static List<StoryReplayOutcome> CaptureOutcomes()
        {
            if (!CanCapture) return null;
            var result = new List<StoryReplayOutcome>(Outcomes.Count);
            foreach (var item in Outcomes) result.Add(new StoryReplayOutcome {
                Visit = item.Visit, Index = item.Index, Kind = item.Kind, Key = item.Key, Value = item.Value });
            return result;
        }
        private static StoryScriptVisit Clone(StoryScriptVisit value)
        { return new StoryScriptVisit { Visit = value.Visit, Script = value.Script, SourceHash = value.SourceHash }; }
    }

    internal sealed class StoryScriptVisit
    {
        public int Visit;
        public string Script, SourceHash;
    }
    internal sealed class StoryReplayOutcome
    {
        public int Visit, Index;
        public string Kind, Key, Value;
    }
}
