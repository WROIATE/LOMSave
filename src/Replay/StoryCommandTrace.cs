using Mortal.Core;
using Mortal.Story;
using System;
using System.Collections.Generic;

namespace LOMSave.Probe
{
    internal static class StoryCommandTrace
    {
        private const string MessagePrefix = "__LOMSAVEPROBE_COMMAND__";
        private const string ContinueDirective = "continue";
        private const string SkipDirective = "skip";
        private const string ReplayResultDirectivePrefix = "replay:";
        private const string DiceKind = "dice";
        private const int MaximumReplaySequence = 10000;
        private const string InstallScript = @"
if not LOM_SAVE_PROBE_COMMAND_TRACE then
    LOM_SAVE_PROBE_COMMAND_TRACE = true

    local function emit(kind, phase, payload)
        local value = payload == nil and """" or tostring(payload)
        local ok, result = pcall(function()
            return luamanager.GetStoryText(
                ""__LOMSAVEPROBE_COMMAND__"" .. kind .. ""|"" .. phase .. ""|"" .. value)
        end)
        return ok and result or """"
    end

    local function serialize(value)
        local ok, result = pcall(json.serialize, value)
        return ok and result or tostring(value)
    end

    local function directive(value)
        local text = tostring(value or """")
        local token, action = string.match(text, ""^([^|]+)|([^|]+)$"")
        if action == ""abort"" then error(""LOMSave replay stopped"") end
        return token or text, action or ""continue""
    end

    local function replay_choice(action)
        local value = string.match(action or """", ""^replay:(%-?%d+)$"")
        return value == nil and nil or tonumber(value)
    end

    if type(say) == ""function"" then
        local original_say = say
        say = function(text, voiceclip)
            local token, action = directive(emit(""say"", ""begin"", text))
            if action == ""skip"" then
                emit(""say"", ""end"", token .. ""|"")
                -- Let native portrait/tween coroutines advance between lines.
                -- No dialogue UI or real-time reading delay is introduced.
                coroutine.yield()
                return
            end
            local result = original_say(text, voiceclip)
            emit(""say"", ""end"", token .. ""|"")
            return result
        end
    end

    if type(choose) == ""function"" then
        local original_choose = choose
        choose = function(options)
            local serialized = serialize(options)
            local token, action = directive(emit(""choose"", ""begin"", serialized))
            local replay = replay_choice(action)
            if replay ~= nil then
                emit(""choose"", ""end"", token .. ""|"" .. tostring(replay))
                return replay
            end
            local result = original_choose(options)
            emit(""choose"", ""end"", token .. ""|"" .. tostring(result))
            return result
        end
    end

    if type(choosetimer) == ""function"" then
        local original_choosetimer = choosetimer
        choosetimer = function(options, duration, defaultoption)
            local serialized = serialize(options)
            local token, action = directive(
                emit(""choosetimer"", ""begin"", serialized))
            local replay = replay_choice(action)
            if replay ~= nil then
                emit(""choosetimer"", ""end"", token .. ""|"" .. tostring(replay))
                return replay
            end
            local result = original_choosetimer(options, duration, defaultoption)
            emit(""choosetimer"", ""end"", token .. ""|"" .. tostring(result))
            return result
        end
    end

    local original_random = math.random
    math.random = function(...)
        local value = original_random(...)
        local recorded = emit(""random"", ""end"", serialize({...}) .. ""|"" .. tostring(value))
        if recorded == ""abort"" then error(""LOMSave replay random mismatch"") end
        return tonumber(recorded) or value
    end
end
";

        private static int _sequence;
        private static int _token;
        private static StoryCommandCheckpoint _pendingReplayTarget;
        private static StoryCommandCheckpoint _activeReplayTarget;
        private static List<StoryChoiceDecision> _pendingReplayChoices;
        private static List<StoryChoiceDecision> _activeReplayChoices;
        private static int _skippedDialogueCount;
        private static readonly List<StoryChoiceDecision> ChoiceHistory =
            new List<StoryChoiceDecision>();

        internal static StoryCommandCheckpoint Current { get; private set; }

        internal static bool IsReplayPending
        {
            get
            {
                return _pendingReplayTarget != null || _activeReplayTarget != null;
            }
        }

        internal static List<StoryChoiceDecision> CaptureChoiceHistory()
        {
            return CloneChoices(ChoiceHistory);
        }

        internal static bool AbortActiveReplay(string reason)
        {
            var target = _activeReplayTarget ?? _pendingReplayTarget;
            if (target == null)
            {
                return false;
            }

            AbortReplay(reason, Current, target);
            return true;
        }

        internal static void ConfigureReplay(
            StoryCommandCheckpoint target,
            IList<StoryChoiceDecision> choices = null,
            IList<StoryScriptVisit> visits = null,
            IList<StoryReplayOutcome> outcomes = null)
        {
            ReplayRuntime.Reset("replay_reconfigured");
            // Scene initialization may auto-save before Begin runs. Never let
            // that save inherit a command/choice history from the previous load.
            Current = null;
            ChoiceHistory.Clear();
            StoryReplayJournal.Configure(visits, outcomes);
            _activeReplayTarget = null;
            _activeReplayChoices = null;
            _skippedDialogueCount = 0;
            _pendingReplayTarget = CanReplay(target) ? target : null;
            _pendingReplayChoices = _pendingReplayTarget == null
                ? null
                : CloneChoices(choices);
            if (_pendingReplayTarget != null)
            {
                ReplayRuntime.Prepare();
            }
            LOMSaveProbePlugin.WriteEvent("story_replay_configured", new
            {
                enabled = _pendingReplayTarget != null,
                target = _pendingReplayTarget,
                choiceHistoryCount = _pendingReplayChoices == null
                    ? 0
                    : _pendingReplayChoices.Count,
                reason = target == null
                    ? "checkpoint_missing"
                    : _pendingReplayTarget == null
                        ? "unsupported_checkpoint"
                        : null
            });
        }

        private static bool IsSupportedReplayKind(string kind)
        {
            return string.Equals(kind, "say", StringComparison.Ordinal)
                || string.Equals(kind, "choose", StringComparison.Ordinal)
                || string.Equals(kind, "choosetimer", StringComparison.Ordinal)
                || string.Equals(kind, DiceKind, StringComparison.Ordinal);
        }

        internal static bool CanReplay(StoryCommandCheckpoint target)
        {
            return target != null && IsSupportedReplayKind(target.Kind)
                && !string.IsNullOrEmpty(target.Script)
                && target.Sequence > 0 && target.Sequence <= MaximumReplaySequence
                && target.PayloadHash != null && target.PayloadHash.Length == 64;
        }

        internal static bool Begin(string fingerprint = null, string nativeStart = null)
        {
            _sequence = 0;
            Current = null;
            var script = PlayerStatManagerData.Instance.CurrentStoryScript;
            var restoring = IsReplayPending;
            var routeError = StoryReplayJournal.Begin(LuaManager.Instance, script, fingerprint, restoring, nativeStart);
            if (StoryReplayJournal.StartsAnchor) ChoiceHistory.Clear();
            if (routeError != null)
            {
                AbortActiveReplay(routeError);
                return false;
            }
            if (!StoryReplayJournal.IsDeterministic && _pendingReplayTarget != null && !string.Equals(
                _pendingReplayTarget.Script, script, StringComparison.Ordinal))
            {
                // Do not run an unrelated chapter under the loading mask.
                // The existing fallback reloads the checkpoint's own beginning.
                AbortActiveReplay("startup_script_changed");
                return false;
            }
            if (_activeReplayTarget == null
                && _pendingReplayTarget != null
                && (StoryReplayJournal.IsDeterministic || string.Equals(
                    _pendingReplayTarget.Script,
                    script,
                    StringComparison.Ordinal)))
            {
                _activeReplayTarget = _pendingReplayTarget;
                _pendingReplayTarget = null;
                _activeReplayChoices = _pendingReplayChoices;
                _pendingReplayChoices = null;
                _skippedDialogueCount = 0;
                LOMSaveProbePlugin.WriteEvent("story_replay_started", new
                {
                    script,
                    target = _activeReplayTarget,
                    choiceHistoryCount = _activeReplayChoices == null
                        ? 0
                        : _activeReplayChoices.Count
                });
                ReplayRuntime.Start();
            }
            LOMSaveProbePlugin.WriteEvent("story_command_trace_started", new { script });

            try
            {
                LuaManager.Instance.ExecuteScript(InstallScript);
                LOMSaveProbePlugin.WriteEvent(
                    "story_command_trace_installed",
                    new { script });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "story_command_trace_install_failed",
                    exception,
                    new { script });
            }
            return true;
        }

        internal static bool TryHandle(string key, out string result)
        {
            result = string.Empty;
            if (string.IsNullOrEmpty(key)
                || !key.StartsWith(MessagePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var body = key.Substring(MessagePrefix.Length);
            var kindSeparator = body.IndexOf('|');
            var phaseSeparator = kindSeparator < 0
                ? -1
                : body.IndexOf('|', kindSeparator + 1);
            if (kindSeparator < 1 || phaseSeparator < 0)
            {
                LOMSaveProbePlugin.WriteEvent("story_command_trace_invalid", new
                {
                    hash = LOMSaveProbePlugin.Sha256(body),
                    length = body.Length
                });
                return true;
            }

            var kind = body.Substring(0, kindSeparator);
            var phase = body.Substring(
                kindSeparator + 1,
                phaseSeparator - kindSeparator - 1);
            var payload = body.Substring(phaseSeparator + 1);
            if (kind == "random" && phase == "end")
            {
                var separator = payload.LastIndexOf('|');
                if (separator >= 0)
                {
                    try { result = RecordOutcome("random", payload.Substring(0, separator), payload.Substring(separator + 1)); }
                    catch (InvalidOperationException) { result = "abort"; }
                }
                return true;
            }
            if (phase == "begin")
            {
                string directive;
                var checkpoint = BeginCommand(kind, payload, out directive);
                result = checkpoint.Token + "|" + directive;
            }
            else if (phase == "end")
            {
                End(kind, payload);
            }
            else
            {
                LOMSaveProbePlugin.WriteEvent("story_command_trace_invalid", new
                {
                    kind,
                    phase,
                    payloadHash = LOMSaveProbePlugin.Sha256(payload)
                });
            }
            return true;
        }

        internal static int BeginExternal(
            string kind,
            string payload,
            out int? replayResult)
        {
            int ignored;
            return BeginExternal(kind, payload, out replayResult, out ignored);
        }

        internal static int BeginExternal(
            string kind,
            string payload,
            out int? replayResult,
            out int fatePointCost)
        {
            string directive;
            var checkpoint = BeginCommand(kind, payload, out directive);
            replayResult = null;
            fatePointCost = 0;
            if (directive == "abort")
                throw new InvalidOperationException("LOMSave replay stopped before external command: " + kind);
            if (directive.StartsWith(
                    ReplayResultDirectivePrefix,
                    StringComparison.Ordinal)
                && int.TryParse(
                    directive.Substring(ReplayResultDirectivePrefix.Length),
                    out var parsedResult))
            {
                replayResult = parsedResult;
                fatePointCost = Math.Max(0, FindReplayDecision(checkpoint)?.FatePointCost ?? 0);
            }
            return checkpoint.Token;
        }

        internal static void EndExternal(string kind, int token, string result, int fatePointCost = 0)
        {
            End(kind, token + "|" + (result ?? string.Empty), fatePointCost);
        }

        private static StoryCommandCheckpoint BeginCommand(
            string kind,
            string payload,
            out string directive)
        {
            ReplayRuntime.ReportProgress();
            Current = new StoryCommandCheckpoint
            {
                Token = ++_token,
                Visit = StoryReplayJournal.Visit,
                Script = PlayerStatManagerData.Instance.CurrentStoryScript,
                Sequence = ++_sequence,
                Kind = kind,
                PayloadHash = LOMSaveProbePlugin.Sha256(payload),
                PayloadPreview = Preview(payload)
            };
            // Capture before native choose/dice execution and before replay can
            // clear its guards at a reached or rejected target.
            BranchAutoSave.BeforeBranch(Current, payload);
            if (!LOMSaveProbePlugin.ValidateDirectCommand(Current))
            { directive = "abort"; return Current; }
            directive = GetReplayDirective(Current);
            return Current;
        }

        private static string GetReplayDirective(StoryCommandCheckpoint current)
        {
            var target = _activeReplayTarget;
            if (target == null)
            {
                return ContinueDirective;
            }

            var earlierVisit = StoryReplayJournal.IsDeterministic && current.Visit < target.Visit;
            if (!earlierVisit && !string.Equals(current.Script, target.Script, StringComparison.Ordinal))
            {
                AbortReplay("script_changed", current, target);
                return "abort";
            }

            if (earlierVisit || current.Sequence < target.Sequence)
            {
                if (string.Equals(current.Kind, "say", StringComparison.Ordinal))
                {
                    _skippedDialogueCount++;
                    return SkipDirective;
                }

                var decision = FindReplayDecision(current);
                if (decision != null)
                {
                    LOMSaveProbePlugin.WriteEvent(
                        "story_replay_choice_replayed",
                        new
                        {
                            current,
                            result = decision.Result,
                            recordedPayloadHash = decision.PayloadHash,
                            payloadChanged = !string.Equals(
                                decision.PayloadHash,
                                current.PayloadHash,
                                StringComparison.Ordinal)
                        });
                    return ReplayResultDirectivePrefix + decision.Result;
                }

                AbortReplay("choice_history_missing", current, target);
                return "abort";
            }

            if ((!StoryReplayJournal.IsDeterministic || current.Visit == target.Visit)
                && current.Sequence == target.Sequence
                && string.Equals(current.Kind, target.Kind, StringComparison.Ordinal)
                && string.Equals(
                    current.PayloadHash,
                    target.PayloadHash,
                    StringComparison.Ordinal))
            {
                if (!StoryReplayJournal.Finish())
                {
                    AbortReplay("route_outcomes_remaining", current, target);
                    return "abort";
                }
                _activeReplayTarget = null;
                _activeReplayChoices = null;
                LOMSaveProbePlugin.WriteEvent("story_replay_target_reached", new
                {
                    current,
                    target,
                    skippedDialogueCount = _skippedDialogueCount
                });
                if (!LOMSaveProbePlugin.ReplayTargetReached(current))
                {
                    AbortReplay("state_mismatch", current, target);
                    return "abort";
                }
                return ContinueDirective;
            }

            AbortReplay("checkpoint_mismatch", current, target);
            return "abort";
        }

        private static void AbortReplay(
            string reason,
            StoryCommandCheckpoint current,
            StoryCommandCheckpoint target)
        {
            _activeReplayTarget = null;
            _pendingReplayTarget = null;
            _activeReplayChoices = null;
            _pendingReplayChoices = null;
            StoryReplayJournal.Configure(null, null);
            LOMSaveProbePlugin.WriteEvent("story_replay_aborted", new
            {
                reason,
                current,
                target,
                skippedDialogueCount = _skippedDialogueCount
            });
            LOMSaveProbePlugin.ReplayAborted(reason, target);
        }

        private static void End(string kind, string payload, int fatePointCost = 0)
        {
            var resultSeparator = payload.IndexOf('|');
            var tokenText = resultSeparator < 0
                ? payload
                : payload.Substring(0, resultSeparator);
            var commandResult = resultSeparator < 0
                ? string.Empty
                : payload.Substring(resultSeparator + 1);
            int token;
            var matched = int.TryParse(tokenText, out token)
                && Current != null
                && Current.Token == token;
            if (matched)
            {
                RecordReplayDecision(Current, commandResult, fatePointCost);
                Current = null;
            }
        }

        private static void RecordReplayDecision(
            StoryCommandCheckpoint checkpoint,
            string result,
            int fatePointCost)
        {
            if (!IsReplayableDecisionKind(checkpoint.Kind)
                || !int.TryParse(result, out var numericResult))
            {
                return;
            }

            if (ChoiceHistory.Count >= 4096) { ChoiceHistory.Clear(); StoryReplayJournal.Invalidate(); }

            ChoiceHistory.Add(new StoryChoiceDecision
            {
                Visit = checkpoint.Visit,
                Script = checkpoint.Script,
                Sequence = checkpoint.Sequence,
                Kind = checkpoint.Kind,
                PayloadHash = checkpoint.PayloadHash,
                Result = numericResult,
                FatePointCost = checkpoint.Kind == DiceKind ? Math.Max(0, fatePointCost) : 0
            });
        }

        private static StoryChoiceDecision FindReplayDecision(
            StoryCommandCheckpoint checkpoint)
        {
            if (!IsReplayableDecisionKind(checkpoint.Kind)
                || _activeReplayChoices == null)
            {
                return null;
            }

            foreach (var decision in _activeReplayChoices)
            {
                if (decision != null
                    && decision.Sequence == checkpoint.Sequence
                    && (!StoryReplayJournal.IsDeterministic || decision.Visit == checkpoint.Visit)
                    && string.Equals(
                        decision.Script,
                        checkpoint.Script,
                        StringComparison.Ordinal)
                    && string.Equals(
                        decision.Kind,
                        checkpoint.Kind,
                        StringComparison.Ordinal))
                {
                    if (StoryReplayJournal.IsDeterministic && decision.PayloadHash != checkpoint.PayloadHash) return null;
                    return decision;
                }
            }
            return null;
        }

        private static bool IsReplayableDecisionKind(string kind)
        {
            return string.Equals(kind, "choose", StringComparison.Ordinal)
                || string.Equals(kind, "choosetimer", StringComparison.Ordinal)
                || string.Equals(kind, DiceKind, StringComparison.Ordinal);
        }

        private static List<StoryChoiceDecision> CloneChoices(
            IList<StoryChoiceDecision> choices)
        {
            var result = new List<StoryChoiceDecision>();
            if (choices == null)
            {
                return result;
            }

            foreach (var choice in choices)
            {
                if (choice == null)
                {
                    continue;
                }

                result.Add(new StoryChoiceDecision
                {
                    Visit = choice.Visit,
                    Script = choice.Script,
                    Sequence = choice.Sequence,
                    Kind = choice.Kind,
                    PayloadHash = choice.PayloadHash,
                    Result = choice.Result,
                    FatePointCost = choice.FatePointCost
                });
            }
            return result;
        }

        private static string Preview(string value)
        {
            const int maximumLength = 160;
            return value.Length <= maximumLength
                ? value
                : value.Substring(0, maximumLength);
        }

        internal static string RecordOutcome(string kind, string key, string actual)
        {
            string value;
            var error = StoryReplayJournal.Outcome(kind, key, actual, out value);
            if (error == null) return value;
            AbortActiveReplay(error);
            throw new InvalidOperationException("Replay outcome mismatch: " + kind + " / " + key);
        }
    }

    internal sealed class StoryCommandCheckpoint
    {
        public int Visit;
        public int Token;
        public string Script;
        public int Sequence;
        public string Kind;
        public string PayloadHash;
        public string PayloadPreview;
    }

    internal sealed class StoryChoiceDecision
    {
        public int Visit;
        public string Script;
        public int Sequence;
        public string Kind;
        public string PayloadHash;
        public int Result;
        public int FatePointCost;
    }
}
