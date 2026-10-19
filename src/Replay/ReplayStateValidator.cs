using HarmonyLib;
using Mortal.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace LOMSave.Probe
{
    internal static class ReplayStateValidator
    {
        private const int MaximumLoggedDifferences = 100;
        private static readonly HashSet<string> IgnoredFields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "TimeTick",
                "FreeRandomState"
            };

        private static JObject _expected;
        private static bool _prepareFailed;

        internal static bool IsRestoring { get; private set; }

        internal static void Prepare(GameSave gameSave, bool migrateVersion = false)
        {
            _prepareFailed = false;
            try
            {
                _expected = gameSave == null ? null : JObject.FromObject(gameSave);
                if (gameSave != null && migrateVersion
                    && !string.IsNullOrEmpty(UnityEngine.Application.version)
                    && !string.Equals(gameSave.Version, UnityEngine.Application.version, StringComparison.Ordinal))
                {
                    // Build the expected state through the game's own migration BEFORE
                    // replaying anything. Never infer it from the replay's result or
                    // ignore whole flag/relationship collections to accommodate updates.
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    IsRestoring = true;
                    var source = _expected.ToObject<GameSave>();
                    Traverse.Create(SaveSystem.Instance)
                        .Method("ExecuteLoadGameData", new[] { typeof(GameSave) }).GetValue(source);
                    PlayerStatManagerData.Instance.SetStartScript(source.StartStoryScript);
                    PlayerStatManagerData.Instance.SetStoryScript(source.CurrentStoryScript);
                    _expected = JObject.FromObject(Traverse.Create(SaveSystem.Instance)
                        .Method("CreateSaveData").GetValue<GameSave>());
                    LOMSaveProbePlugin.WriteEvent("story_replay_target_migrated", new
                    {
                        fromVersion = gameSave.Version,
                        toVersion = _expected.Value<string>("Version"),
                        elapsedMilliseconds = timer.Elapsed.TotalMilliseconds
                    });
                }
            }
            catch (Exception exception)
            {
                _expected = null;
                _prepareFailed = true;
                LOMSaveProbePlugin.WriteError(
                    "story_replay_validation_prepare_failed",
                    exception);
            }
            finally
            {
                IsRestoring = false;
            }
        }

        internal static void Reset()
        {
            _expected = null;
            _prepareFailed = false;
            IsRestoring = false;
        }

        // Direct continuation must prove it did not execute any prior effects.
        // Unlike legacy recovery, validation never writes the expected data back.
        internal static bool ValidateDirect(StoryCommandCheckpoint checkpoint)
        {
            var expected = _expected; _expected = null;
            if (expected == null || _prepareFailed) return false;
            var actual = JObject.FromObject(Traverse.Create(SaveSystem.Instance).Method("CreateSaveData").GetValue<GameSave>());
            foreach (var field in IgnoredFields) { expected.Remove(field); actual.Remove(field); }
            expected.Remove("RestoreData"); actual.Remove("RestoreData");
            var differences = new List<StateDifference>(); var count = 0;
            CollectDifferences(expected, actual, "", differences, ref count);
            LOMSaveProbePlugin.WriteEvent("continuation_state_validated", new { checkpoint, differenceCount = count, differences });
            return count == 0;
        }

        internal static bool RestartAtStoryBeginning(
            string script,
            string reason,
            JObject storyBeginningSave)
        {
            _expected = null;
            if (storyBeginningSave == null || string.IsNullOrEmpty(script))
            {
                LOMSaveProbePlugin.WriteEvent(
                    "story_replay_fallback_failed",
                    new
                    {
                        reason,
                        script,
                        failure = storyBeginningSave == null
                            ? "story_beginning_save_missing"
                            : "story_script_missing"
                    });
                return false;
            }

            try
            {
                var authoritativeSave = storyBeginningSave.ToObject<GameSave>();
                authoritativeSave.CurrentStoryScript = script;
                authoritativeSave.StartStoryScript = script;
                IsRestoring = true;
                Traverse.Create(SaveSystem.Instance)
                    .Method("ExecuteLoadGameData", new[] { typeof(GameSave) })
                    .GetValue(authoritativeSave);
                // Loading data alone leaves the old Lua coroutine suspended in place.
                // A real scene reload discards it and begins the checkpoint script afresh.
                ReplayCharacterContext.PrepareFallback(script);
                SceneController.Instance.LoadStory();
                LOMSaveProbePlugin.WriteEvent(
                    "story_replay_fallback_loaded",
                    new
                    {
                        reason,
                        script,
                        fallback = "story_beginning"
                    });
                return true;
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "story_replay_fallback_failed",
                    exception,
                    new
                    {
                        reason,
                        script,
                        fallback = "story_beginning"
                    });
                return false;
            }
            finally
            {
                IsRestoring = false;
            }
        }

        internal static bool RestoreAndValidate(StoryCommandCheckpoint checkpoint, bool strict = false)
        {
            var expected = _expected;
            _expected = null;
            if (expected == null)
            {
                LOMSaveProbePlugin.WriteEvent(
                    "story_replay_state_validation_skipped",
                    new
                    {
                        reason = "authoritative_save_missing",
                        checkpoint
                    });
                return !strict && !_prepareFailed;
            }

            // Validate the state produced by the replay before restoring the
            // authoritative target. Comparing only after an overwrite hides
            // incorrect paths and duplicated effects.
            try
            {
                if (strict)
                {
                    var replayed = JObject.FromObject(Traverse.Create(SaveSystem.Instance).Method("CreateSaveData").GetValue<GameSave>());
                    var wanted = (JObject)expected.DeepClone();
                    foreach (var field in IgnoredFields) { wanted.Remove(field); replayed.Remove(field); }
                    // Native automatic-save backup text is not gameplay state;
                    // actual disk writes are suppressed throughout restoration.
                    wanted.Remove("RestoreData"); replayed.Remove("RestoreData");
                    var differences = new List<StateDifference>(); var count = 0;
                    CollectDifferences(wanted, replayed, "", differences, ref count);
                    LOMSaveProbePlugin.WriteEvent(count == 0 ? "story_replay_path_validated" : "story_replay_path_mismatch",
                        new { checkpoint, differenceCount = count, differences });
                    if (count != 0) return false;
                }
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError("story_replay_path_validation_failed", exception, checkpoint);
                return false;
            }

            try
            {
                var authoritativeSave = expected.ToObject<GameSave>();
                IsRestoring = true;
                Traverse.Create(SaveSystem.Instance)
                    .Method("ExecuteLoadGameData", new[] { typeof(GameSave) })
                    .GetValue(authoritativeSave);
                PlayerStatManagerData.Instance.SetStartScript(
                    authoritativeSave.StartStoryScript);
                PlayerStatManagerData.Instance.SetStoryScript(checkpoint.Script);
                LOMSaveProbePlugin.WriteEvent(
                    "story_replay_authoritative_state_restored",
                    new
                    {
                        checkpoint,
                        authoritativeSave.StartStoryScript
                    });
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "story_replay_authoritative_state_restore_failed",
                    exception,
                    checkpoint);
                return false;
            }
            finally
            {
                IsRestoring = false;
            }

            try
            {
                var currentSave = Traverse.Create(SaveSystem.Instance)
                    .Method("CreateSaveData")
                    .GetValue<GameSave>();
                var actual = JObject.FromObject(currentSave);
                var changedFields = new List<string>();
                foreach (var property in expected.Properties())
                {
                    if (IgnoredFields.Contains(property.Name))
                    {
                        continue;
                    }

                    if (!JToken.DeepEquals(property.Value, actual[property.Name]))
                    {
                        changedFields.Add(property.Name);
                    }
                }
                foreach (var property in actual.Properties())
                {
                    if (!IgnoredFields.Contains(property.Name)
                        && expected[property.Name] == null)
                    {
                        changedFields.Add(property.Name);
                    }
                }
                var expectedNormalized = (JObject)expected.DeepClone();
                var actualNormalized = (JObject)actual.DeepClone();
                foreach (var field in IgnoredFields)
                {
                    expectedNormalized.Remove(field);
                    actualNormalized.Remove(field);
                }

                var differences = new List<StateDifference>();
                var differenceCount = 0;
                CollectDifferences(
                    expectedNormalized,
                    actualNormalized,
                    string.Empty,
                    differences,
                    ref differenceCount);
                var expectedHash = LOMSaveProbePlugin.Sha256(
                    expectedNormalized.ToString(
                        Newtonsoft.Json.Formatting.None));
                var actualHash = LOMSaveProbePlugin.Sha256(
                    actualNormalized.ToString(
                        Newtonsoft.Json.Formatting.None));

                LOMSaveProbePlugin.WriteEvent(
                    differenceCount == 0
                        ? "story_replay_state_validated"
                        : "story_replay_state_mismatch",
                    new
                    {
                        checkpoint,
                        ignoredFields = IgnoredFields,
                        expectedHash,
                        actualHash,
                        changedFields,
                        differenceCount,
                        differences,
                        differencesTruncated =
                            differenceCount > differences.Count
                    });
                if (differenceCount != 0) return false;
            }
            catch (Exception exception)
            {
                LOMSaveProbePlugin.WriteError(
                    "story_replay_state_validation_failed",
                    exception,
                    checkpoint);
                return false;
            }
            return true;
        }

        private static void CollectDifferences(
            JToken expected,
            JToken actual,
            string path,
            List<StateDifference> differences,
            ref int differenceCount)
        {
            if (JToken.DeepEquals(expected, actual))
            {
                return;
            }

            if (expected is JObject expectedObject
                && actual is JObject actualObject)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in expectedObject.Properties())
                {
                    names.Add(property.Name);
                }
                foreach (var property in actualObject.Properties())
                {
                    names.Add(property.Name);
                }
                foreach (var name in names)
                {
                    CollectDifferences(
                        expectedObject[name],
                        actualObject[name],
                        AppendProperty(path, name),
                        differences,
                        ref differenceCount);
                }
                return;
            }

            if (expected is JArray expectedArray
                && actual is JArray actualArray)
            {
                var count = Math.Max(expectedArray.Count, actualArray.Count);
                for (var index = 0; index < count; index++)
                {
                    var expectedItem = index < expectedArray.Count
                        ? expectedArray[index]
                        : null;
                    var actualItem = index < actualArray.Count
                        ? actualArray[index]
                        : null;
                    CollectDifferences(
                        expectedItem,
                        actualItem,
                        AppendArrayItem(path, index, expectedItem ?? actualItem),
                        differences,
                        ref differenceCount);
                }
                return;
            }

            differenceCount++;
            if (differences.Count >= MaximumLoggedDifferences)
            {
                return;
            }

            differences.Add(new StateDifference
            {
                Path = string.IsNullOrEmpty(path) ? "$" : path,
                Expected = Preview(expected),
                Actual = Preview(actual)
            });
        }

        private static string AppendProperty(string path, string name)
        {
            return string.IsNullOrEmpty(path) ? name : path + "." + name;
        }

        private static string AppendArrayItem(
            string path,
            int index,
            JToken item)
        {
            var itemObject = item as JObject;
            var key = itemObject?["Key"]
                ?? itemObject?["Name"]
                ?? itemObject?["Id"];
            return path + "["
                + (key == null ? index.ToString() : key.ToString())
                + "]";
        }

        private static string Preview(JToken value)
        {
            if (value == null)
            {
                return "<missing>";
            }

            const int maximumLength = 240;
            var text = value.ToString(Newtonsoft.Json.Formatting.None);
            return text.Length <= maximumLength
                ? text
                : text.Substring(0, maximumLength);
        }

        private sealed class StateDifference
        {
            public string Path;
            public string Expected;
            public string Actual;
        }
    }
}
