using BepInEx;
using System.IO;
using UnityEngine;

namespace LOMSave.Probe
{
    internal static class CodeNameMapping
    {
        private static CodeNameMapStore _store;
        private static string _path;
        private static float _nextPoll;

        internal static void Initialize()
        {
            _path = Path.Combine(Paths.ConfigPath, "LOMSaveProbe.CodeNames.yaml");
            _store = new CodeNameMapStore(_path);
            Reload();
        }

        internal static void Update()
        {
            if (_store == null || Time.realtimeSinceStartup < _nextPoll) return;
            _nextPoll = Time.realtimeSinceStartup + 1f;
            Reload();
        }

        private static void Reload()
        {
            string error;
            var result = _store.Reload(out error);
            if (result == CodeNameMapStore.LoadResult.Unchanged) return;
            LOMSaveProbePlugin.WriteEvent(result == CodeNameMapStore.LoadResult.Invalid ? "code_names_invalid" : "code_names_loaded",
                new { path = _path, result = result.ToString(), entries = _store.EntryCount, names = _store.NameCount, error });
        }

        internal static string Lookup(string scope, string code)
        { return _store == null ? null : _store.Lookup(scope, code); }

        internal static string Resolve(string scope, string code, string note, string localized = null)
        { return Lookup(scope, code) ?? CheckpointNameRules.Resolve(code, note, localized); }
    }
}
