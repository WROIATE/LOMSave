using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LOMSave.Probe
{
    internal sealed class StoryContinuationState
    {
        public int Format = 1, Point;
        public string Script, SourceHash, Kind;
        public JObject Locals;
        public JObject Dice;
        public JObject Pictures;
        public List<ContinuationActor> Actors;
    }

    internal sealed class ContinuationActor
    {
        public string Key, Portrait, PortraitKey, Position;
        public int Facing;
        public bool Dimmed;
    }

    internal static class StoryContinuation
    {
        private sealed class Plan
        {
            internal string Script, Hash, Instrumented;
            internal StoryContinuationCompiler Compiler;
        }
        private static readonly Dictionary<string, Plan> Plans = new Dictionary<string, Plan>();
        private static readonly Queue<string> PlanOrder = new Queue<string>();
        private static Plan _active;
        private static StoryContinuationState _current, _pending;
        private static StoryCommandCheckpoint _target;
        private static string _nativeStart;
        private static object _owner;
        private static LuaEnvironment _environment;
        private static bool _presentationReady, _failed;
        private static float _effectUntil;
        private static readonly Dictionary<string, ContinuationActor> PendingActors = new Dictionary<string, ContinuationActor>();
        private static readonly Dictionary<string, string> InheritedPortraits = new Dictionary<string, string>();
        private static readonly JObject Pictures = new JObject();
        internal static bool IsPending { get { return _pending != null; } }
        internal static bool NativeSaving { get { return SaveOriginPolicy.NativeSaving; } }

        internal static void Reset()
        { _active = null; _current = _pending = null; _target = null; _nativeStart = null; _owner = null; _environment = null; _failed = _presentationReady = false; _effectUntil = 0; PendingActors.Clear(); InheritedPortraits.Clear(); Pictures.RemoveAll(); }

        private static Plan GetPlan(string script, string source = null)
        {
            if (Plans.TryGetValue(script, out var cached)) return cached;
            if (source == null) source = Resources.Load<TextAsset>("story/chinesetraditional/" + script.ToLowerInvariant())?.text;
            if (source == null) throw new InvalidOperationException("Story source missing: " + script);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var compiler = new StoryContinuationCompiler(source);
            var plan = new Plan { Script = script, Hash = LOMSaveProbePlugin.Sha256(source), Compiler = compiler, Instrumented = compiler.Instrumented() };
            if (Plans.Count >= 24) Plans.Remove(PlanOrder.Dequeue());
            Plans[script] = plan; PlanOrder.Enqueue(script);
            LOMSaveProbePlugin.WriteEvent("continuation_compiled", new { script, points = compiler.Points.Count, milliseconds = timer.Elapsed.TotalMilliseconds });
            return plan;
        }

        internal static bool PrepareLoad(StoryContinuationState state, StoryCommandCheckpoint target, GameSave save)
        {
            try
            {
                var plan = GetPlan(state.Script);
                if (state.Format != 1 || state.Script != target.Script || state.SourceHash != plan.Hash
                    || !plan.Compiler.Points.TryGetValue(state.Point, out var point) || point.Kind != target.Kind
                    || state.Kind != target.Kind || state.Locals == null)
                    throw new InvalidOperationException("Continuation identity/source mismatch");
                foreach (var variable in point.Locals)
                    if (state.Locals.Property(variable) == null) throw new InvalidOperationException("Continuation local missing: " + variable);
                _pending = state; _target = target; _nativeStart = save.StartStoryScript;
                if (state.Actors != null) foreach (var actor in state.Actors) PendingActors[actor.Key] = actor;
                // Native scene creation still runs, but it launches this script
                // directly and never loads the historical beginning snapshot.
                save.StartStoryScript = save.CurrentStoryScript = state.Script;
                ReplayRuntime.Prepare();
                LOMSaveProbePlugin.WriteEvent("continuation_load_prepared", new { state.Script, state.Point, state.Kind });
                return true;
            }
            catch (Exception e) { Fail(e); return false; }
        }

        internal static bool BeforeStory()
        {
            if (_failed) return false;
            if (!ReferenceEquals(_owner, LuaManager.Instance) && _pending == null)
            { PendingActors.Clear(); InheritedPortraits.Clear(); Pictures.RemoveAll(); }
            _owner = LuaManager.Instance;
            _current = null;
            if (_pending != null)
            {
                if (PlayerStatManagerData.Instance.CurrentStoryScript != _pending.Script)
                { Fail(new InvalidOperationException("Native scene selected another script")); return false; }
                PlayerStatManagerData.Instance.SetStartScript(_nativeStart);
            }
            return true;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(LuaEnvironment), "LoadLuaFunction", typeof(string), typeof(string))]
        private static void Compile(LuaEnvironment __instance, ref string __0, string __1)
        {
            var script = PlayerStatManagerData.Instance.CurrentStoryScript;
            if (string.IsNullOrEmpty(script) || __1 != script + ".LuaScript") return;
            try
            {
                _environment = __instance;
                // Always compile the original asset: the legacy recovery patch
                // can prepend resource helpers to __0, which are not checkpoints.
                _active = GetPlan(script);
                __0 = _pending == null ? (ReplayCharacterContext.HasLegacyContext(__0)
                    ? _active.Compiler.Instrumented(true) : _active.Instrumented)
                    : _active.Compiler.Resume(_pending.Point, name => PackedLiteral(_pending.Locals[name]), RestorePictures(_pending.Pictures));
            }
            catch (Exception e)
            {
                _active = null; _current = null;
                LOMSaveProbePlugin.WriteError("continuation_compile_failed", e, new { script });
                if (_pending != null) { Fail(e); __0 = "error('LOMSave: continuation unavailable')"; }
            }
        }

        internal static bool TryHandle(string key, out string result)
        {
            result = "";
            if (key == null || !key.StartsWith("__LOM_CONT_", StringComparison.Ordinal)) return false;
            if (key == "__LOM_CONT_READY__") result = _failed ? "failed" : _presentationReady ? "ready" : "waiting";
            else if (key == "__LOM_CONT_CLEAR__") _current = null;
            else if (key.StartsWith("__LOM_CONT_ACTOR_RESTORE__", StringComparison.Ordinal))
                RestoreActor(key.Substring("__LOM_CONT_ACTOR_RESTORE__".Length));
            else if (key.StartsWith("__LOM_CONT_ACTOR__", StringComparison.Ordinal))
            {
                var actors = Traverse.Create(CharacterPlaceholder.Instance).Field("_characters").GetValue<Dictionary<string, Character>>();
                var name = key.Substring("__LOM_CONT_ACTOR__".Length);
                result = actors != null && actors.TryGetValue(name, out var actor) && actor != null ? "ready" : "missing";
            }
            else if (key == "__LOM_CONT_DICE__") RestoreDice();
            else if (key.StartsWith("__LOM_CONT_POINT__", StringComparison.Ordinal))
            {
                _current = null;
                try
                {
                    var body = key.Substring("__LOM_CONT_POINT__".Length); var split = body.IndexOf('|');
                    if (body.Length > 131072) throw new FormatException("Continuation snapshot size limit");
                    if (_active == null || split < 1 || !int.TryParse(body.Substring(0, split), out var id)
                        || !_active.Compiler.Points.TryGetValue(id, out var point)) return true;
                    var json = JToken.Parse(body.Substring(split + 1));
                    var locals = json as JObject ?? (json is JArray empty && !empty.HasValues ? new JObject() : null);
                    if (locals == null) return true;
                    // Lua nil fields are absent from a serialized table.
                    foreach (var variable in point.Locals) if (locals.Property(variable) == null) locals[variable] = JValue.CreateNull();
                    _current = new StoryContinuationState { Script = _active.Script, SourceHash = _active.Hash,
                        Point = id, Kind = point.Kind, Locals = locals };
                }
                catch (Exception e) { LOMSaveProbePlugin.WriteError("continuation_capture_failed", e); }
            }
            return true;
        }

        internal static StoryContinuationState Capture(StoryCommandCheckpoint command)
        {
            if (_current == null || command == null || command.Script != _current.Script || command.Kind != _current.Kind) return null;
            try
            {
            var copy = JObject.FromObject(_current).ToObject<StoryContinuationState>();
            copy.Pictures = (JObject)Pictures.DeepClone();
            if (command.Kind == "dice") copy.Dice = CaptureDice();
            copy.Actors = new List<ContinuationActor>();
            var actors = Traverse.Create(CharacterPlaceholder.Instance).Field("_characters").GetValue<Dictionary<string, Character>>();
            if (actors != null) foreach (var item in actors)
            {
                var state = item.Value == null ? null : item.Value.State;
                if (state == null) continue;
                var spriteName = state.portrait?.name;
                var controller = item.Value.GetComponent<StoryCharacterController>();
                var mappings = controller == null ? null : Traverse.Create(controller).Field("_dicPortrait").GetValue<Dictionary<string, string>>();
                string portraitKey = null;
                if (mappings != null) foreach (var mapping in mappings) if (mapping.Value == spriteName) { portraitKey = mapping.Key; break; }
                copy.Actors.Add(new ContinuationActor { Key = item.Key, Portrait = spriteName, PortraitKey = portraitKey, Position = state.position?.name,
                    Facing = (int)state.facing, Dimmed = state.dimmed });
            }
            return copy;
            }
            catch (Exception e)
            {
                LOMSaveProbePlugin.WriteError("continuation_snapshot_unavailable", e);
                return null;
            }
        }

        internal static void RestoreActor(string key)
        {
            if (!PendingActors.TryGetValue(key, out var saved)) return;
            var character = CharacterPlaceholder.Instance.Get(key);
            if (character?.State == null) return;
            if (!string.IsNullOrEmpty(saved.Position)) character.State.position = Stage.GetActiveStage().GetPosition(saved.Position);
            character.State.facing = (FacingDirection)saved.Facing;
            character.State.dimmed = saved.Dimmed;
            if (!string.IsNullOrEmpty(saved.Portrait))
            {
                if (!string.IsNullOrEmpty(saved.PortraitKey)) CharacterPlaceholder.Instance.LoadCharacterPortrait(key, saved.PortraitKey);
                InheritedPortraits[character.name] = saved.Portrait;
            }
            PendingActors.Remove(key);
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(PortraitController), "Show", typeof(PortraitOptions))]
        private static void RestoreInheritedPortrait(PortraitOptions __0)
        {
            var character = __0?.character;
            if (character == null || !InheritedPortraits.TryGetValue(character.name, out var saved)) return;
            if (__0.portrait == null) foreach (var sprite in character.Portraits)
                if (sprite != null && sprite.name == saved) { __0.portrait = sprite; break; }
            InheritedPortraits.Remove(character.name);
        }

        internal static void PresentationReady() { _presentationReady = true; }

        internal static void Update()
        { if (IsPending && !ReplayRuntime.IsRestoring) Fail(new TimeoutException("Direct continuation did not finish")); }

        internal static bool CommandStarted(StoryCommandCheckpoint command)
        {
            if (_pending == null) return true;
            try
            {
            if (_current == null || _current.Point != _pending.Point || command.Kind != _target.Kind
                || command.PayloadHash != _target.PayloadHash || command.Script != _target.Script
                || !ReplayStateValidator.ValidateDirect(command))
            { Fail(new InvalidOperationException("Continuation checkpoint/state validation failed")); return false; }
            LOMSaveProbePlugin.WriteEvent("continuation_target_reached", new { _pending.Script, _pending.Point, _pending.Kind,
                historicalStatementsExecuted = 0 });
            _pending = null; _target = null;
            ReplayRuntime.HideMask("presentation_restored");
            return true;
            }
            catch (Exception e) { Fail(e); return false; }
        }

        internal static bool CanManualSave()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Story") return true;
            var command = StoryCommandTrace.Current;
            if (IsPending || _failed || ReplayRuntime.IsRestoring || command?.Kind != "say" || Capture(command) == null) return false;
            var dialog = SayDialog.ActiveSayDialog;
            var writer = dialog == null ? null : dialog.GetComponent<Writer>();
            if (writer == null || !writer.IsWaitingForInput || writer.HasWordsRemaining || Time.time < _effectUntil) return false;
            // Only inspect active story visuals when the player actually saves;
            // never scan the scene or allocate tween lists each frame.
            var stage = Stage.GetActiveStage();
            if (stage != null) foreach (var actor in stage.CharactersOnStage)
            {
                var state = actor == null ? null : actor.State;
                if (state == null) continue;
                if (Animating(state.holder)) return false;
                if (state.allPortraits != null) foreach (var portrait in state.allPortraits)
                    if (portrait != null && Animating(portrait.rectTransform)) return false;
            }
            return true;
        }

        private static bool Animating(RectTransform target)
        { return target != null && (LeanTween.isTweening(target.gameObject) || DG.Tweening.DOTween.IsTweening(target, true)); }

        [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), "ShowPicture", typeof(string))]
        private static void PictureShown(string __0) { Pictures["ShowPicture"] = new JArray(__0); }
        [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), "HidePicture")]
        private static void PictureHidden() { Pictures.Remove("ShowPicture"); }
        [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), "ShowItemPicture", typeof(string))]
        private static void ItemPictureShown(string __0) { Pictures["ShowItemPicture"] = new JArray(__0); }
        [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), "HideItemPicture")]
        private static void ItemPictureHidden() { Pictures.Remove("ShowItemPicture"); }
        [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), "ShowBigPicture", typeof(string), typeof(int))]
        private static void BigPictureShown(string __0, int __1) { Pictures["ShowBigPicture"] = new JArray(__0, __1); }
        [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), "HideBigPicture")]
        private static void BigPictureHidden() { Pictures.Remove("ShowBigPicture"); }

        private static string RestorePictures(JObject pictures)
        {
            var code = new StringBuilder();
            if (pictures == null) return "";
            foreach (var picture in pictures.Properties())
            {
                // Saved data must never supply arbitrary Lua calls.
                var count = picture.Name == "ShowBigPicture" ? 2 :
                    picture.Name == "ShowPicture" || picture.Name == "ShowItemPicture" ? 1 : 0;
                if (count == 0 || !(picture.Value is JArray args) || args.Count != count || args[0].Type != JTokenType.String
                    || (count == 2 && args[1].Type != JTokenType.Integer))
                    throw new FormatException("Invalid continuation picture");
                code.Append("runwait(mainui.").Append(picture.Name).Append('(').Append(Literal(args[0]));
                if (count == 2) code.Append(',').Append(Literal(args[1]));
                code.AppendLine("))");
            }
            return code.ToString();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(StoryCamera), "FadeVolume", typeof(string), typeof(int), typeof(float))]
        private static void CameraEffectStarted(float __2)
        { _effectUntil = Mathf.Max(_effectUntil, Time.time + Mathf.Max(0, __2)); }

        private static JObject CaptureDice()
        {
            var dialog = CurrentDiceDialog();
            var fields = Traverse.Create(dialog);
            // _resultCount is the score (which may be zero or negative), not
            // the number of choices. A configured header identifies setup.
            if (string.IsNullOrEmpty(fields.Field("_header").GetValue<string>()))
                throw new InvalidOperationException("Dice setup is not ready");
            return JObject.FromObject(new { max = fields.Field("_defaultDiceMax").GetValue<int>(),
                random = fields.Field("_currentRandomValue").GetValue<int>(), count = fields.Field("_resultCount").GetValue<int>(),
                selection = fields.Field("_resultSelection").GetValue<int>(),
                header = fields.Field("_header").GetValue<string>(), additions = fields.Field("_additions").GetValue<DiceCheckAddition[]>() });
        }

        private static void RestoreDice()
        {
            var data = _pending?.Dice;
            if (data == null || string.IsNullOrEmpty(data.Value<string>("header")) || data["count"] == null)
                throw new InvalidOperationException("Dice snapshot missing/incomplete");
            var dialog = CurrentDiceDialog();
            dialog.SetRandom(data.Value<int>("max"), data.Value<int>("random"));
            dialog.Setup(data.Value<int>("count"), data.Value<int>("selection"), data.Value<string>("header"), data["additions"]?.ToObject<DiceCheckAddition[]>());
        }

        private static DiceMenuDialog CurrentDiceDialog()
        {
            // ExecuteRoll has not enabled the UI when the branch snapshot is
            // taken. Resolve the exact Lua binding, including inactive objects.
            var dialog = _environment?.Interpreter.Globals.Get("dicemenudialog").UserData?.Object as DiceMenuDialog;
            if (dialog == null) throw new InvalidOperationException("Story dice binding is unavailable");
            return dialog;
        }

        private static void Fail(Exception error)
        {
            _failed = true; _pending = null; _current = null;
            ReplayRuntime.Reset("continuation_failed");
            LOMSaveProbePlugin.WriteError("continuation_failed", error);
            LOMSaveProbePlugin.ContinuationFailed();
        }

        internal static void Reject(string reason) { Fail(new InvalidOperationException(reason)); }

        internal static string Literal(JToken value, int depth = 0)
        {
            if (depth > 16) throw new FormatException("Continuation value depth limit");
            if (value == null || value.Type == JTokenType.Null) return "nil";
            if (value.Type == JTokenType.Boolean) return value.Value<bool>() ? "true" : "false";
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                var number = value.Value<double>(); if (double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Non-finite Lua number");
                return number.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value.Type == JTokenType.String)
            {
                var s = value.Value<string>(); var b = new StringBuilder("\"");
                foreach (var c in s) { if (c == '\\' || c == '"') b.Append('\\').Append(c); else if (c < 32) b.Append('\\').Append(((int)c).ToString("D3")); else b.Append(c); }
                return b.Append('"').ToString();
            }
            var output = new StringBuilder("{");
            if (value is JArray array) foreach (var item in array) output.Append(Literal(item, depth + 1)).Append(',');
            else if (value is JObject obj) foreach (var property in obj.Properties())
                output.Append('[').Append(Literal(new JValue(property.Name), depth + 1)).Append("]=").Append(Literal(property.Value, depth + 1)).Append(',');
            else throw new FormatException("Unsupported Lua snapshot value");
            return output.Append('}').ToString();
        }

        private static string PackedLiteral(JToken value, int depth = 0)
        {
            if (depth > 16) throw new FormatException("Continuation value depth limit");
            if (value == null || value.Type == JTokenType.Null) return "nil";
            if (!(value is JObject obj)) throw new FormatException("Untyped continuation value");
            var kind = obj.Value<string>("kind");
            if (kind == "nil") return "nil";
            if (kind == "string" || kind == "number" || kind == "boolean") return Literal(obj["value"]);
            if (kind != "table") throw new FormatException("Unsupported continuation value");
            var entries = obj["entries"] as JArray;
            // MoonSharp serializes an empty Lua table as {}, not [].
            if (entries == null && obj["entries"] is JObject empty && !empty.HasValues) return "{}";
            if (entries == null || entries.Count > 1024) throw new FormatException("Invalid continuation table");
            var output = new StringBuilder("{");
            foreach (var entry in entries)
                output.Append('[').Append(PackedLiteral(entry["key"], depth + 1)).Append("]=").Append(PackedLiteral(entry["value"], depth + 1)).Append(',');
            return output.Append('}').ToString();
        }
    }
}
