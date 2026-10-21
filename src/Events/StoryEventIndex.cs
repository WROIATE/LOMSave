using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HarmonyLib;
using Mortal.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class StoryEventIndex
    {
        private static Task<StoryEventIndexData> _load;
        private static bool _finished;
        internal static StoryEventIndexData Data { get; private set; }
        internal static int Revision { get; private set; }
        internal static bool Pending { get { return !_finished; } }
        private static readonly Dictionary<StoryIndexValue, StatValueReference> Values = new Dictionary<StoryIndexValue, StatValueReference>();
        private static readonly Dictionary<Type, Dictionary<string, UnityEngine.Object>> Objects = new Dictionary<Type, Dictionary<string, UnityEngine.Object>>();
        private static readonly string[] Fields = { null, "_stat", "_relationship", "_flag", "_book", "_misc", "_specialItem", null,
            null, "_gameTime", "_talentData", "_book", "_upgradeItemData", "_upgradeItemData", "_bookCollection", "_libraryAchieve" };

        internal static void Update()
        {
            if (_finished) return;
            if (_load == null)
            {
                var file = Path.Combine(Path.GetDirectoryName(typeof(StoryEventIndex).Assembly.Location), "LOMSaveProbe.StoryEvents.json");
                var root = Application.dataPath; var version = Application.version;
                _load = Task.Run(() => Load(file, root, version));
                return;
            }
            if (!_load.IsCompleted) return;
            _finished = true; Revision++;
            try
            {
                Data = _load.GetAwaiter().GetResult(); Data.ReadNative = ReadValue;
                LOMSaveProbePlugin.WriteEvent("event_index_ready", new { Data.GameVersion, pools = Data.Pools.Count,
                    conditions = Data.Conditions.Count, switches = Data.Switches.Count, milliseconds = Data.LoadMilliseconds });
            }
            catch (Exception e) { LOMSaveProbePlugin.WriteError("event_index_unavailable", e); }
        }

        internal static StoryEventIndexData Load(string file, string root, string version)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = JsonConvert.DeserializeObject<StoryEventIndexData>(File.ReadAllText(file));
            if (result == null || result.Version != 1 || result.GameVersion != version || result.Hashes == null
                || result.Pools == null || result.Groups == null || result.Conditions == null || result.Switches == null)
                throw new InvalidDataException("Event index version does not match the installed game.");
            var sources = new[] { "sharedassets2.assets", "Managed/Mortal.Core.dll", "Managed/Mortal.Story.dll" };
            if (result.Hashes.Count < sources.Length || result.Hashes.Count > 32 || sources.Any(x => !result.Hashes.ContainsKey(x)))
                throw new InvalidDataException("Invalid event index sources.");
            foreach (var source in result.Hashes.Keys)
            {
                if (!sources.Contains(source) && !Regex.IsMatch(source, @"^(globalgamemanagers|sharedassets\d+)\.assets$"))
                    throw new InvalidDataException("Invalid event index source path.");
                string expected;
                if (!result.Hashes.TryGetValue(source, out expected)) throw new InvalidDataException("Missing event index source: " + source);
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(Path.Combine(root, source)))
                    if (!string.Equals(expected, BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Event index source changed: " + source);
            }
            // This is file I/O/JSON only on a worker. Unity objects and game
            // values are accessed later, on the game thread, for actual checks.
            result.LoadMilliseconds = timer.Elapsed.TotalMilliseconds;
            return result;
        }

        private static int? ReadValue(StoryIndexValue source)
        {
            if (source.Kind <= 0 || source.Kind >= Fields.Length || Fields[source.Kind] == null
                || source.Field != Fields[source.Kind] || string.IsNullOrEmpty(source.Target)) return null;
            StatValueReference value;
            if (!Values.TryGetValue(source, out value))
            {
                var field = AccessTools.Field(typeof(StatValueReference), source.Field);
                Dictionary<string, UnityEngine.Object> objects;
                if (!Objects.TryGetValue(field.FieldType, out objects))
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    objects = Resources.FindObjectsOfTypeAll(field.FieldType).Where(x => x != null)
                        .GroupBy(x => x.name).Where(x => x.Count() == 1).ToDictionary(x => x.Key, x => x.First());
                    Objects.Add(field.FieldType, objects);
                    LOMSaveProbePlugin.WriteEvent("event_index_values_bound", new { type = field.FieldType.Name,
                        count = objects.Count, milliseconds = timer.Elapsed.TotalMilliseconds });
                }
                UnityEngine.Object target;
                value = null;
                if (objects.TryGetValue(source.Target, out target))
                {
                    value = new StatValueReference();
                    Traverse.Create(value).Field("_checkType").SetValue((StatCheckType)source.Kind);
                    Traverse.Create(value).Field("_negative").SetValue(source.Negative);
                    field.SetValue(value, target);
                }
                Values[source] = value;
            }
            // Random, nested groups and checkpoint conditions never reach
            // GetValue: the index handles them without executing game logic.
            return value == null ? (int?)null : value.GetValue();
        }
    }
}
