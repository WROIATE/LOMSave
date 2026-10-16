using BepInEx;
using BepInEx.Unity.Mono;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LOMSave.Probe
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class LOMSaveProbePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "bk.plugins.LOMSave.Probe";
        public const string PluginName = "LOMSave Probe";
        public const string PluginVersion = "0.22.3";

        private const float PortraitLoadTimeoutSeconds = 5f;
        private static readonly object LogLock = new object();
        private static string _logFile;
        private static string _currentView;
        private static ProbeSaveState _pendingState;
        private static bool _canRestorePendingState;
        private static bool _restoreInProgress;
        private static StoryCommandCheckpoint _fallbackRestartCheckpoint;
        private static JObject _fallbackStoryBeginningSave;
        private static string _fallbackRestartReason;
        private static JObject _storyBeginningSave;
        private static MethodInfo _storyViewMethod;
        private static MethodInfo _storyViewLoadMethod;

        private void Awake()
        {
            var directory = Path.Combine(Paths.BepInExRootPath, "LOMSaveProbe");
            Directory.CreateDirectory(directory);
            _logFile = Path.Combine(
                directory,
                "LOMSaveProbe-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".jsonl");

            WriteEvent("probe_started", new
            {
                pluginVersion = PluginVersion,
                gameVersion = Application.version,
                unityVersion = Application.unityVersion,
                logFile = _logFile
            });

            try
            {
                CodeNameMapping.Initialize();
                EventInspector.Initialize();
                ActionCalendar.Initialize();
                var harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(ActionAutoSave));
                harmony.PatchAll(typeof(AutoDialogueSpeed));
                harmony.PatchAll(typeof(SaveNotes));
                harmony.PatchAll(typeof(ReplayAudio));
                harmony.PatchAll(typeof(SceneAudio));
                harmony.PatchAll(typeof(ReplayInput));
                harmony.PatchAll(typeof(ProbePatches));
                harmony.PatchAll(typeof(DiceRollTrace));
                harmony.PatchAll(typeof(OptionImpactPreview));
                harmony.PatchAll(typeof(PreviewInteraction));
                harmony.PatchAll(typeof(EventInspector));
                harmony.PatchAll(typeof(CheckpointRecall));
                harmony.PatchAll(typeof(TeamBattleSkip));
                harmony.PatchAll(typeof(FeaturePanel));
                harmony.PatchAll(typeof(ActionCalendar));
                harmony.PatchAll(typeof(PinballSkip));
                harmony.PatchAll(typeof(DebugMenuEnhancements));
                InstallStoryViewPatch(harmony);
                WriteEvent("patches_installed", new { });
            }
            catch (Exception exception)
            {
                WriteError("patch_install_failed", exception);
                throw;
            }
        }

        private static void InstallStoryViewPatch(Harmony harmony)
        {
            try
            {
                _storyViewMethod = AccessTools.Method(
                    typeof(ViewFlowchartController),
                    "ShowView",
                    new[] { typeof(string) })
                    ?? AccessTools.Method(
                        typeof(ViewFlowchartController),
                        "View",
                        new[] { typeof(string) });
                if (_storyViewMethod == null)
                {
                    WriteEvent("story_view_patch_missing", new { });
                    return;
                }

                if (_storyViewMethod.Name == "ShowView")
                {
                    _storyViewLoadMethod = AccessTools.Method(
                        typeof(ViewFlowchartController),
                        "LoadView",
                        new[] { typeof(string) });
                }

                var prefix = AccessTools.Method(
                    typeof(ProbePatches),
                    nameof(ProbePatches.BeforeStoryView));
                harmony.Patch(_storyViewMethod, prefix: new HarmonyMethod(prefix));
                WriteEvent("story_view_patch_installed", new
                {
                    method = _storyViewMethod.Name,
                    loadMethod = _storyViewLoadMethod == null
                        ? null
                        : _storyViewLoadMethod.Name
                });
            }
            catch (Exception exception)
            {
                _storyViewMethod = null;
                WriteError("story_view_patch_failed", exception);
            }
        }

        private void Update()
        {
            PreviewInteraction.Update();
            CodeNameMapping.Update();
            FeaturePanel.Update();
            RecallShortcut.Update();
            if (Input.GetKeyDown(KeyCode.F10))
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) CaptureCurrentState("manual_ctrl_f10");
                else ActionCalendar.Toggle();
            }
            TryRestartAtStoryBeginning();
            ReplayRuntime.Update();
            CheckpointToast.Update();
            TryRestorePendingState();
        }

        private void OnGUI()
        {
            ReplayRuntime.DrawMask();
            OptionImpactPreview.Draw();
        }

        private void LateUpdate()
        {
            RecallShortcut.LateUpdate();
        }

        private void OnDestroy()
        {
            ReplayRuntime.Reset("plugin_destroyed");
            FeaturePanel.ForceClose();
        }

        internal static void CaptureCurrentState(string reason)
        {
            try
            {
                var saveSystem = SaveSystem.Instance;
                var createSaveData = Traverse.Create(saveSystem).Method("CreateSaveData");
                var gameSave = createSaveData.GetValue<GameSave>();
                WriteGameSave("state_snapshot", reason, gameSave);
            }
            catch (Exception exception)
            {
                WriteError("state_snapshot_failed", exception, new { reason });
            }
        }

        internal static void WriteGameSave(string eventName, string reason, GameSave gameSave)
        {
            if (gameSave == null)
            {
                WriteEvent(eventName, new { reason, isNull = true });
                return;
            }

            try
            {
                var json = JsonConvert.SerializeObject(gameSave);
                var data = JObject.Parse(json);
                WriteEvent(eventName, new
                {
                    reason,
                    hash = Sha256(json),
                    jsonLength = json.Length,
                    summary = SelectFields(data)
                });
            }
            catch (Exception exception)
            {
                WriteError(eventName + "_failed", exception, new { reason });
            }
        }

        internal static void WriteEvent(string eventName, object payload)
        {
            try
            {
                var line = JsonConvert.SerializeObject(new
                {
                    utc = DateTime.UtcNow.ToString("O"),
                    frame = Time.frameCount,
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    eventName,
                    payload
                });

                lock (LogLock)
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Diagnostics must never interrupt the game.
            }
        }

        internal static void WriteError(string eventName, Exception exception, object context = null)
        {
            WriteEvent(eventName, new
            {
                exception = exception.ToString(),
                context
            });
        }

        internal static string Sha256(string value)
        {
            using (var algorithm = SHA256.Create())
            {
                var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (var item in bytes)
                {
                    result.Append(item.ToString("x2"));
                }
                return result.ToString();
            }
        }

        internal static void RecordCurrentView(string view)
        {
            _currentView = view;
            WriteEvent("story_view_changed", new { view });
        }

        internal static void SaveSlotState(string saveFile, GameSave gameSave, string saveFileHash)
        {
            var file = saveFile + ".lomsaveprobe.json";
            try
            {
                var currentScene = gameSave.CurrentScene;
                var script = string.IsNullOrEmpty(gameSave.CurrentStoryScript) ? gameSave.StartStoryScript : gameSave.CurrentStoryScript;
                var command = StoryCommandTrace.Current;
                if (!string.Equals(
                        currentScene,
                        "Story",
                        StringComparison.Ordinal)
                    || command == null || !string.Equals(command.Script, script, StringComparison.Ordinal))
                {
                    // A new native story-start save must not inherit the previous
                    // script's UI/checkpoint; free/battle overwrites clear it too.
                    if (File.Exists(file)) File.Delete(file);
                    WriteEvent("slot_state_save_skipped", new
                    {
                        saveFile,
                        currentScene,
                        script,
                        reason = currentScene != "Story" ? "non_story_scene" : "matching_checkpoint_missing"
                    });
                    return;
                }

                string music;
                string envSound;
                CaptureAudio(out music, out envSound);
                var state = new ProbeSaveState
                {
                    Version = StorySaveFiles.StateVersion,
                    SaveTimeTick = gameSave.TimeTick,
                    SaveFileHash = saveFileHash,
                    View = _currentView,
                    Music = music,
                    EnvSound = envSound,
                    AudioLevels = SceneAudio.Capture(),
                    Portraits = CapturePortraits(),
                    Command = command,
                    ChoiceHistory = StoryCommandTrace.CaptureChoiceHistory(),
                    StoryBeginningSave = _storyBeginningSave == null
                        ? null
                        : (JObject)_storyBeginningSave.DeepClone()
                };
                File.WriteAllText(file, JsonConvert.SerializeObject(state), Encoding.UTF8);
                WriteEvent("slot_state_saved", new
                {
                    saveFile,
                    state.SaveTimeTick,
                    state.View,
                    state.Music,
                    state.EnvSound,
                    portraitCount = state.Portraits.Count,
                    command = state.Command,
                    choiceHistoryCount = state.ChoiceHistory.Count,
                    hasStoryBeginningSave = state.StoryBeginningSave != null
                });
            }
            catch (Exception exception)
            {
                WriteError("slot_state_save_failed", exception, new { saveFile });
            }
        }

        private static void CaptureAudio(out string music, out string envSound)
        {
            music = null;
            envSound = null;
            var soundManager = SoundManager.Instance;
            if (soundManager == null)
            {
                WriteEvent("story_audio_captured", new
                {
                    music,
                    envSound,
                    reason = "sound_manager_missing"
                });
                return;
            }

            var soundManagerState = Traverse.Create(soundManager);
            music = soundManagerState.Field("_currentMusic").GetValue<string>();
            envSound = soundManagerState.Field("_currentEnvSound").GetValue<string>();
            WriteEvent("story_audio_captured", new { music, envSound });
        }

        internal static void LoadSlotState(
            SaveSystem saveSystem,
            string slot,
            GameSave gameSave)
        {
            var currentScene = gameSave == null ? null : gameSave.CurrentScene;
            _pendingState = null;
            _canRestorePendingState = false;
            StoryCommandTrace.ConfigureReplay(null);
            if (!string.Equals(
                    currentScene,
                    "Story",
                    StringComparison.Ordinal))
            {
                WriteEvent("slot_state_load_skipped", new
                {
                    slot,
                    currentScene,
                    reason = "non_story_scene"
                });
                return;
            }

            try
            {
                var file = GetSlotStateFile(saveSystem, slot);
                if (!File.Exists(file))
                {
                    WriteEvent("slot_state_missing", new { slot });
                    return;
                }

                var state = JsonConvert.DeserializeObject<ProbeSaveState>(
                    File.ReadAllText(file, Encoding.UTF8));
                if (state == null) return;
                var script = string.IsNullOrEmpty(gameSave.CurrentStoryScript) ? gameSave.StartStoryScript : gameSave.CurrentStoryScript;
                var mismatch = StorySaveFiles.Mismatch(state.Version, state.SaveTimeTick, gameSave.TimeTick,
                    state.SaveFileHash, state.Version >= StorySaveFiles.StateVersion
                        ? StorySaveFiles.Hash(file.Substring(0, file.Length - ".lomsaveprobe.json".Length)) : null,
                    state.Command == null ? null : state.Command.Script, script);
                if (mismatch != null)
                {
                    WriteEvent("slot_state_rejected", new { slot, file, reason = mismatch, script,
                        checkpointScript = state.Command == null ? null : state.Command.Script });
                    return;
                }
                if (state != null && state.Version < 6 && state.Portraits != null)
                {
                    WriteEvent("legacy_portraits_discarded", new
                    {
                        slot,
                        version = state.Version,
                        portraitCount = state.Portraits.Count,
                        reason = "legacy_capture_did_not_check_canvas_alpha"
                    });
                    state.Portraits.Clear();
                }
                _pendingState = state;
                StoryCommandTrace.ConfigureReplay(
                    state == null ? null : state.Command,
                    state == null ? null : state.ChoiceHistory);
                if (state.Command != null && !StoryCommandTrace.IsReplayPending)
                {
                    _pendingState = null;
                    WriteEvent("slot_state_rejected", new { slot, file, reason = "unsupported_checkpoint" });
                    return;
                }
                WriteEvent("slot_state_loaded", new
                {
                    slot,
                    file,
                    version = state == null ? 0 : state.Version,
                    view = state == null ? null : state.View,
                    music = state == null ? null : state.Music,
                    envSound = state == null ? null : state.EnvSound,
                    portraitCount = state == null || state.Portraits == null
                        ? 0
                        : state.Portraits.Count,
                    command = state == null ? null : state.Command,
                    choiceHistoryCount = state == null
                        || state.ChoiceHistory == null
                            ? 0
                            : state.ChoiceHistory.Count,
                    hasStoryBeginningSave = state != null
                        && state.StoryBeginningSave != null
                });
            }
            catch (Exception exception)
            {
                _pendingState = null;
                StoryCommandTrace.ConfigureReplay(null);
                WriteError("slot_state_load_failed", exception, new { slot });
            }
        }

        internal static void DeleteSlotState(SaveSystem saveSystem, string slot)
        {
            try
            {
                var file = GetSlotStateFile(saveSystem, slot);
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception exception)
            {
                WriteError("slot_state_delete_failed", exception, new { slot });
            }
        }

        internal static void EnablePendingStateRestore()
        {
            _canRestorePendingState = true;
            TryRestorePendingState();
        }

        internal static void OnLuaInitialized()
        {
            if (_pendingState != null
                && _pendingState.Command != null
                && StoryCommandTrace.IsReplayPending)
            {
                WriteEvent("presentation_restore_deferred", new
                {
                    command = _pendingState.Command
                });
                return;
            }

            EnablePendingStateRestore();
        }

        internal static void ReplayTargetReached(StoryCommandCheckpoint checkpoint)
        {
            ReplayRuntime.RestoreSpeed();
            ReplayStateValidator.RestoreAndValidate(checkpoint);
            EnablePendingStateRestore();
        }

        internal static void ReplayAborted(
            string reason,
            StoryCommandCheckpoint checkpoint)
        {
            ReplayRuntime.RestoreSpeed();
            _fallbackStoryBeginningSave = _pendingState == null
                || _pendingState.StoryBeginningSave == null
                    ? null
                    : (JObject)_pendingState.StoryBeginningSave.DeepClone();
            _pendingState = null;
            _canRestorePendingState = false;
            _fallbackRestartCheckpoint = checkpoint;
            _fallbackRestartReason = reason;
            WriteEvent("story_replay_fallback_scheduled", new
            {
                reason,
                checkpoint,
                fallback = "story_beginning",
                hasStoryBeginningSave = _fallbackStoryBeginningSave != null
            });
            WriteEvent("presentation_restore_cancelled", new
            {
                reason,
                fallback = "story_beginning"
            });
        }

        private static void TryRestartAtStoryBeginning()
        {
            var checkpoint = _fallbackRestartCheckpoint;
            if (checkpoint == null)
            {
                return;
            }

            var reason = _fallbackRestartReason;
            var storyBeginningSave = _fallbackStoryBeginningSave;
            _fallbackRestartCheckpoint = null;
            _fallbackStoryBeginningSave = null;
            _fallbackRestartReason = null;
            var restarted = ReplayStateValidator.RestartAtStoryBeginning(
                checkpoint.Script,
                reason,
                storyBeginningSave);
            ReplayRuntime.Reset(
                restarted
                    ? "fallback_story_beginning_loaded"
                    : "fallback_story_beginning_failed");
        }

        internal static void CaptureStoryBeginningState()
        {
            if (_pendingState != null)
            {
                _storyBeginningSave = _pendingState.StoryBeginningSave == null
                    ? null
                    : (JObject)_pendingState.StoryBeginningSave.DeepClone();
                WriteEvent("story_beginning_state_selected", new
                {
                    source = _storyBeginningSave == null
                        ? "legacy_sidecar_missing"
                        : "sidecar",
                    script = PlayerStatManagerData.Instance.CurrentStoryScript
                });
                return;
            }

            try
            {
                var gameSave = Traverse.Create(SaveSystem.Instance)
                    .Method("CreateSaveData")
                    .GetValue<GameSave>();
                _storyBeginningSave = gameSave == null
                    ? null
                    : JObject.FromObject(gameSave);
                WriteEvent("story_beginning_state_selected", new
                {
                    source = "current_game",
                    script = PlayerStatManagerData.Instance.CurrentStoryScript,
                    captured = _storyBeginningSave != null
                });
            }
            catch (Exception exception)
            {
                _storyBeginningSave = null;
                WriteError(
                    "story_beginning_state_capture_failed",
                    exception,
                    new
                    {
                        script = PlayerStatManagerData.Instance.CurrentStoryScript
                    });
            }
        }

        private static List<PortraitSaveState> CapturePortraits()
        {
            var result = new List<PortraitSaveState>();
            var stage = Stage.GetActiveStage();
            if (stage == null)
            {
                WriteEvent("story_portraits_captured", new
                {
                    count = 0,
                    reason = "active_stage_missing"
                });
                return result;
            }

            foreach (var character in stage.CharactersOnStage)
            {
                if (character == null
                    || character.State == null
                    || !IsPortraitVisible(character))
                {
                    continue;
                }

                var portrait = character.State.portrait;
                var position = character.State.position;
                result.Add(new PortraitSaveState
                {
                    Character = character.name,
                    Portrait = portrait == null ? null : portrait.name,
                    Position = position == null ? null : position.name,
                    Facing = (int)character.State.facing,
                    Dimmed = character.State.dimmed
                });
            }

            WriteEvent("story_portraits_captured", new
            {
                count = result.Count,
                portraits = result
            });
            return result;
        }

        private static bool IsPortraitVisible(Character character)
        {
            if (!character.State.onScreen)
            {
                return false;
            }

            try
            {
                var image = Traverse.Create(character.State)
                    .Field("portraitImage")
                    .GetValue<Component>();
                if (image == null || !image.gameObject.activeInHierarchy)
                {
                    return false;
                }

                var renderer = image.GetComponent<CanvasRenderer>();
                return renderer != null && renderer.GetAlpha() > 0.01f;
            }
            catch (Exception exception)
            {
                WriteError("story_portrait_visibility_failed", exception, new
                {
                    character = character.name
                });
                return false;
            }
        }

        private static void TryRestorePendingState()
        {
            if (!_canRestorePendingState || _pendingState == null || _restoreInProgress)
            {
                return;
            }

            var state = _pendingState;
            var viewController = UnityEngine.Object.FindObjectOfType<ViewFlowchartController>();
            if (!string.IsNullOrEmpty(state.View)
                && (viewController == null || _storyViewMethod == null))
            {
                return;
            }

            var luaManager = LuaManager.Instance;
            if (luaManager == null)
            {
                return;
            }
            if ((!string.IsNullOrEmpty(state.Music)
                    || !string.IsNullOrEmpty(state.EnvSound))
                && SoundManager.Instance == null)
            {
                return;
            }

            var portraits = state.Portraits ?? new List<PortraitSaveState>();
            var characterPlaceholder = CharacterPlaceholder.Instance;
            var portraitController = UnityEngine.Object.FindObjectOfType<PortraitController>();
            if (portraits.Count > 0
                && (characterPlaceholder == null || portraitController == null))
            {
                return;
            }

            try
            {
                _canRestorePendingState = false;
                _restoreInProgress = true;
                _pendingState = null;
                luaManager.StartCoroutine(RestorePresentationState(
                    state,
                    viewController,
                    characterPlaceholder,
                    portraitController));
            }
            catch (Exception exception)
            {
                _restoreInProgress = false;
                ReplayRuntime.HideMask("presentation_restore_start_failed");
                WriteError("presentation_restore_failed", exception);
            }
        }

        private static IEnumerator RestorePresentationState(
            ProbeSaveState state,
            ViewFlowchartController viewController,
            CharacterPlaceholder characterPlaceholder,
            PortraitController portraitController)
        {
            if (!string.IsNullOrEmpty(state.View))
            {
                var restoreView = RestoreStoryView(viewController, state.View);
                while (restoreView.MoveNext())
                {
                    yield return restoreView.Current;
                }
            }

            RestoreAudio(state);
            // Wwise processes RTPC changes on its audio thread. Let the new
            // channel levels settle before lifting the replay's output mute.
            yield return new WaitForSecondsRealtime(0.1f);
            WriteEvent("story_audio_settled", new { levels = SceneAudio.Capture() });
            var portraits = state.Portraits ?? new List<PortraitSaveState>();
            foreach (var portrait in portraits)
            {
                IEnumerator loadCharacter;
                try
                {
                    loadCharacter = characterPlaceholder.LoadCharacterAsset(
                        portrait.Character);
                    WriteEvent("story_portrait_load_started", new
                    {
                        portrait.Character,
                        portrait.Portrait
                    });
                }
                catch (Exception exception)
                {
                    WriteError("story_portrait_load_failed", exception, portrait);
                    continue;
                }

                if (loadCharacter != null)
                {
                    var loadStartedAt = Time.unscaledTime;
                    var loadTimedOut = false;
                    while (loadCharacter.MoveNext())
                    {
                        if (Time.unscaledTime - loadStartedAt
                            >= PortraitLoadTimeoutSeconds)
                        {
                            loadTimedOut = true;
                            break;
                        }
                        yield return loadCharacter.Current;
                    }
                    if (loadTimedOut)
                    {
                        var disposable = loadCharacter as IDisposable;
                        disposable?.Dispose();
                        WriteEvent("story_portrait_load_timeout", new
                        {
                            portrait.Character,
                            portrait.Portrait,
                            timeoutSeconds = PortraitLoadTimeoutSeconds
                        });
                        continue;
                    }
                }

                try
                {
                    var character = characterPlaceholder.Get(portrait.Character);
                    if (character == null)
                    {
                        WriteEvent("story_portrait_character_missing", portrait);
                        continue;
                    }

                    character.State.facing = (FacingDirection)portrait.Facing;
                    var position = string.IsNullOrEmpty(portrait.Position)
                        ? "middle"
                        : portrait.Position;
                    portraitController.Show(
                        character,
                        portrait.Portrait,
                        position,
                        position);
                    portraitController.SetDimmed(character, portrait.Dimmed);
                    WriteEvent("story_portrait_restored", portrait);
                }
                catch (Exception exception)
                {
                    WriteError("story_portrait_restore_failed", exception, portrait);
                }
            }

            _restoreInProgress = false;
            WriteEvent("presentation_state_restored", new
            {
                state.View,
                state.Music,
                state.EnvSound,
                portraitCount = portraits.Count
            });
            yield return new WaitForEndOfFrame();
            ReplayRuntime.HideMask("presentation_restored");
        }

        private static IEnumerator RestoreStoryView(
            ViewFlowchartController controller,
            string view)
        {
            IEnumerator load = null;
            if (_storyViewLoadMethod != null)
            {
                try
                {
                    load = (IEnumerator)_storyViewLoadMethod.Invoke(
                        controller,
                        new object[] { view });
                    WriteEvent("story_view_load_started", new { view });
                }
                catch (Exception exception)
                {
                    WriteError("story_view_load_failed", exception, new { view });
                    yield break;
                }
            }

            if (load != null)
            {
                yield return load;
                WriteEvent("story_view_load_completed", new { view });
            }

            try
            {
                _storyViewMethod.Invoke(controller, new object[] { view });
                WriteEvent("story_view_restored", new { view });
            }
            catch (Exception exception)
            {
                WriteError("story_view_restore_failed", exception, new { view });
            }
        }

        private static void RestoreAudio(ProbeSaveState state)
        {
            try
            {
                var soundManager = SoundManager.Instance;
                if (soundManager == null)
                {
                    WriteEvent("story_audio_restore_skipped", new
                    {
                        state.Music,
                        state.EnvSound,
                        reason = "sound_manager_missing"
                    });
                    return;
                }

                SceneAudio.Restore(state.Music, state.EnvSound, state.AudioLevels);
                WriteEvent("story_audio_restored", new
                {
                    state.Music,
                    state.EnvSound,
                    source = state.AudioLevels == null ? "replayed_channels" : "saved_channels",
                    saved = state.AudioLevels,
                    restored = SceneAudio.Capture()
                });
            }
            catch (Exception exception)
            {
                WriteError("story_audio_restore_failed", exception, new
                {
                    state.Music,
                    state.EnvSound
                });
            }
        }

        private static string GetSlotStateFile(SaveSystem saveSystem, string slot)
        {
            var method = Traverse.Create(saveSystem).Method(
                "GetSaveDataFile",
                new[] { typeof(string) });
            var saveFile = (string)method.GetValue(slot);
            return saveFile + ".lomsaveprobe.json";
        }

        private static IDictionary<string, JToken> SelectFields(JObject data)
        {
            var names = new[]
            {
                "TimeTick", "CurrentStoryScript", "StartStoryScript", "CurrentScene",
                "CurrentSceneKey", "CurrentNextScene", "CurrentTravelScript",
                "CurrentYear", "CurrentMonth", "CurrentStage"
            };
            var result = new Dictionary<string, JToken>();
            foreach (var name in names)
            {
                if (data.TryGetValue(name, out var value))
                {
                    result[name] = value;
                }
            }
            return result;
        }

        private sealed class ProbeSaveState
        {
            public int Version;
            public long SaveTimeTick;
            public string SaveFileHash;
            public string View;
            public string Music;
            public string EnvSound;
            public AudioSceneState AudioLevels;
            public List<PortraitSaveState> Portraits;
            public StoryCommandCheckpoint Command;
            public List<StoryChoiceDecision> ChoiceHistory;
            public JObject StoryBeginningSave;
        }

        private sealed class PortraitSaveState
        {
            public string Character;
            public string Portrait;
            public string Position;
            public int Facing;
            public bool Dimmed;
        }

    }

    internal static class ProbePatches
    {
        private const int ManualSaveSlotCount = 50;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveGamePanel), "Awake")]
        private static void ExpandSaveGameSlots(SaveGamePanel __instance)
        {
            ExpandManualSaveSlots(__instance, "SaveGamePanel");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LoadGamePanel), "Awake")]
        private static void ExpandLoadGameSlots(LoadGamePanel __instance)
        {
            ExpandManualSaveSlots(__instance, "LoadGamePanel");
        }

        private static void ExpandManualSaveSlots(object panel, string source)
        {
            try
            {
                var slotCount = Traverse.Create(panel).Field("_slotCount");
                var originalCount = slotCount.GetValue<int>();
                var expandedCount = Math.Max(
                    originalCount,
                    ManualSaveSlotCount);
                slotCount.SetValue(expandedCount);
                LOMSaveProbePlugin.WriteEvent("manual_save_slots_expanded", new
                {
                    source,
                    originalCount,
                    expandedCount
                });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "manual_save_slots_expand_failed",
                    exception,
                    new { source });
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MenuPanel), "ToggleSaveButton", typeof(bool))]
        private static void UnlockMenuSaveButton(ref bool active)
        {
            LogSaveUnlock("MenuPanel", active);
            active = true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(StoryManager), "ToggleSaveButton", typeof(bool))]
        private static void UnlockStorySaveButton(ref bool active)
        {
            LogSaveUnlock("StoryManager", active);
            active = true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveSystem), "SaveGameData", typeof(string))]
        private static void BeforeSaveGameData(SaveSystem __instance, string slot)
        {
            LOMSaveProbePlugin.CaptureCurrentState("before_manual_save:" + slot);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), "ExecuteSaveData", typeof(string), typeof(object))]
        private static void BeforeSaveFile(string __0, object __1, out string __state)
        {
            __state = null;
            if (!(__1 is GameSave) && !(__1 is AutoGameSave)) return;
            try { __state = StorySaveFiles.Hash(__0); }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("slot_state_save_stamp_failed", e, new { file = __0 }); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SaveSystem), "ExecuteSaveData", typeof(string), typeof(object))]
        private static void AfterSaveFile(string __0, object __1, string __state)
        {
            if (__state == null || ReplayRuntime.IsRestoring || StoryCommandTrace.IsReplayPending) return;
            try
            {
                var gameSave = __1 as GameSave ?? (__1 as AutoGameSave)?.GameSave;
                if (gameSave == null) return;
                var committedHash = StorySaveFiles.CommittedHash(__0, __state);
                if (committedHash == null)
                {
                    LOMSaveProbePlugin.WriteEvent("slot_state_save_skipped", new { saveFile = __0, reason = "save_not_committed" });
                    return;
                }
                LOMSaveProbePlugin.SaveSlotState(__0, gameSave, committedHash);
                ActionCalendar.SaveHistory(__0, gameSave, committedHash);
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("slot_state_save_failed", e, new { saveFile = __0 }); }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), "AutoSaveData", typeof(string))]
        private static bool BeforeAutoSaveData()
        { return !ReplayRuntime.IsRestoring && !StoryCommandTrace.IsReplayPending; }

        [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), "AutoLoadGameData", typeof(string))]
        private static void BeforeAutoLoadGameData(string slot, out string __state)
        { __state = SaveSlotContext.Begin(slot); }

        [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), "LoadGameData")]
        private static void BeforeLoadGameData(SaveSystem __instance, out string __state)
        { __state = SaveSlotContext.Begin(__instance.CurrentSlot); }

        [HarmonyFinalizer, HarmonyPatch(typeof(SaveSystem), "AutoLoadGameData", typeof(string))]
        private static Exception AfterAutoLoadGameData(Exception __exception, string __state)
        {
            SaveSlotContext.End(__state);
            return __exception;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(SaveSystem), "LoadGameData")]
        private static Exception AfterLoadGameData(Exception __exception, string __state)
        {
            SaveSlotContext.End(__state);
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveSystem), "AutoSaveStoryData")]
        private static void BeforeAutoSaveStoryData()
        {
            LOMSaveProbePlugin.CaptureCurrentState("before_story_auto_save");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData", typeof(GameSave))]
        private static void BeforeExecuteLoadGameData(SaveSystem __instance, GameSave gameSave)
        {
            if (ReplayStateValidator.IsRestoring)
            {
                return;
            }

            LOMSaveProbePlugin.WriteGameSave("load_argument", "before_execute_load", gameSave);
            ReplayStateValidator.Prepare(gameSave);
            LOMSaveProbePlugin.LoadSlotState(
                __instance,
                SaveSlotContext.Resolve(__instance.CurrentSlot),
                gameSave);
            if (gameSave != null
                && gameSave.CurrentScene == "Story"
                && StoryCommandTrace.IsReplayPending
                && !string.IsNullOrEmpty(gameSave.CurrentStoryScript))
            {
                var originalStartScript = gameSave.StartStoryScript;
                gameSave.StartStoryScript = gameSave.CurrentStoryScript;
                LOMSaveProbePlugin.WriteEvent("resume_script_redirected", new
                {
                    originalStartScript,
                    resumeScript = gameSave.CurrentStoryScript
                });
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData", typeof(GameSave))]
        private static void AfterExecuteLoadGameData()
        {
            if (ReplayStateValidator.IsRestoring)
            {
                return;
            }

            LOMSaveProbePlugin.CaptureCurrentState("after_execute_load");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveSystem), "DeleteSaveData", typeof(string))]
        private static void AfterDeleteSaveData(SaveSystem __instance, string slot)
        {
            LOMSaveProbePlugin.DeleteSlotState(__instance, slot);
        }

        internal static void BeforeStoryView(string name)
        {
            LOMSaveProbePlugin.RecordCurrentView(name);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerStatManagerData), "SetStoryScript")]
        private static void BeforeSetStoryScript(string script)
        {
            LOMSaveProbePlugin.WriteEvent("story_script_changed", new
            {
                previous = PlayerStatManagerData.Instance.CurrentStoryScript,
                next = script
            });
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerStatManagerData), "Save", typeof(GameSave))]
        private static void AfterPlayerStatSave(PlayerStatManagerData __instance, GameSave gameSave)
        {
            var currentScript = __instance.CurrentStoryScript;
            if (gameSave == null
                || gameSave.CurrentScene != "Story"
                || string.IsNullOrEmpty(currentScript))
            {
                return;
            }

            var originalScript = gameSave.CurrentStoryScript;
            gameSave.CurrentStoryScript = currentScript;
            if (originalScript != currentScript)
            {
                LOMSaveProbePlugin.WriteEvent("resume_script_recorded", new
                {
                    originalScript,
                    currentScript
                });
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LuaManager), "ExecuteScript")]
        private static void BeforeExecuteScript(string script)
        {
            LOMSaveProbePlugin.WriteEvent("lua_execute_script", new
            {
                scriptHash = LOMSaveProbePlugin.Sha256(script),
                length = script == null ? 0 : script.Length,
                preview = Preview(script)
            });
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LuaManager), "ExecuteLuaScript")]
        private static void BeforeExecuteLuaScript()
        {
            StoryCommandTrace.Begin();
            LOMSaveProbePlugin.CaptureStoryBeginningState();
            LOMSaveProbePlugin.WriteEvent("lua_execute_story", new
            {
                script = PlayerStatManagerData.Instance.CurrentStoryScript
            });
            LOMSaveProbePlugin.CaptureCurrentState("before_execute_lua_script");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LuaManager), "GetStoryText", typeof(string))]
        private static bool BeforeGetStoryText(ref string __result, string key)
        {
            if (!StoryCommandTrace.TryHandle(key, out var result))
            {
                return true;
            }

            __result = result;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LuaManager), "ChangeScene")]
        private static void BeforeChangeScene(string name, string key, string nextScene)
        {
            LOMSaveProbePlugin.WriteEvent("scene_change_requested", new
            {
                name,
                key,
                nextScene,
                storyScript = PlayerStatManagerData.Instance.CurrentStoryScript
            });
            LOMSaveProbePlugin.CaptureCurrentState("before_scene_change:" + name);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(LuaManager), "Init")]
        private static Exception AfterLuaInit(Exception __exception)
        {
            if (__exception == null)
            {
                LOMSaveProbePlugin.WriteEvent("lua_initialized", new
                {
                    script = PlayerStatManagerData.Instance.CurrentStoryScript
                });
                LOMSaveProbePlugin.OnLuaInitialized();
            }
            else
            {
                LOMSaveProbePlugin.WriteError("lua_init_failed", __exception);
            }
            return __exception;
        }

        private static string Preview(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }
            const int maximumLength = 240;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        private static void LogSaveUnlock(string source, bool requestedState)
        {
            if (!requestedState)
            {
                LOMSaveProbePlugin.WriteEvent("save_unlock_applied", new { source });
            }
        }
    }
}
