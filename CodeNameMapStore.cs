using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LOMSave.Probe
{
    internal sealed class CodeNameEntry
    {
        public CodeNameEntry() { }
        public string Name { get; set; }
        public string Status { get; set; }
        public List<string> Sources { get; set; }
    }

    internal sealed class CodeNameDocument
    {
        public CodeNameDocument() { }
        public int Version { get; set; }
        public Dictionary<string, Dictionary<string, CodeNameEntry>> Mappings { get; set; }
    }

    internal sealed class CodeNameMapStore
    {
        internal enum LoadResult { Unchanged, Loaded, Missing, Invalid }
        private readonly string _path;
        private long _lastWrite = long.MinValue, _lastSize = long.MinValue;
        private Dictionary<string, Dictionary<string, string>> _names = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        internal int EntryCount { get; private set; }
        internal int NameCount { get { return _names.Values.Sum(x => x.Count); } }
        internal CodeNameMapStore(string path) { _path = path; }

        internal string Lookup(string scope, string code)
        {
            Dictionary<string, string> section;
            string name;
            return scope != null && code != null && _names.TryGetValue(scope, out section) && section.TryGetValue(code, out name) ? name : null;
        }

        internal LoadResult Reload(out string error, bool force = false)
        {
            error = null;
            try
            {
                var file = new FileInfo(_path);
                var stamp = file.Exists ? file.LastWriteTimeUtc.Ticks : -1;
                var size = file.Exists ? file.Length : -1;
                if (!force && stamp == _lastWrite && size == _lastSize) return LoadResult.Unchanged;
                _lastWrite = stamp; _lastSize = size;
                if (!file.Exists)
                {
                    _names = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                    EntryCount = 0;
                    return LoadResult.Missing;
                }
                if (size > 4 * 1024 * 1024) throw new InvalidDataException("名称映射表超过 4 MiB。");
                var parser = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .WithDuplicateKeyChecking().IgnoreUnmatchedProperties().Build();
                var document = parser.Deserialize<CodeNameDocument>(File.ReadAllText(_path));
                if (document == null || document.Version != 1 || document.Mappings == null)
                    throw new InvalidDataException("名称映射表需要 version: 1 和 mappings。");
                var next = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                var count = 0;
                foreach (var group in document.Mappings)
                {
                    if (group.Key != "flags" && group.Key != "conditions" && group.Key != "switches"
                        && group.Key != "events" && group.Key != "missions" && group.Key != "mission_groups")
                        throw new InvalidDataException("未知映射分类：" + group.Key);
                    if (group.Value == null) throw new InvalidDataException("映射分类不能为空：" + group.Key);
                    var section = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in group.Value)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Any(char.IsControl) || pair.Value == null)
                            throw new InvalidDataException("编码或条目无效：" + group.Key);
                        if (++count > 50000) throw new InvalidDataException("名称映射条目超过 50000 个。");
                        var name = CheckpointNameRules.Label(pair.Value.Name, pair.Key);
                        if (name != null)
                        {
                            if (name.Any(char.IsControl)) throw new InvalidDataException("名称包含控制字符：" + pair.Key);
                            section.Add(pair.Key, name.Length > 64 ? name.Substring(0, 64) + "…" : name);
                        }
                    }
                    next.Add(group.Key, section);
                }
                // Publish only a fully parsed and validated document. A
                // half-written editor save never replaces the last good map.
                _names = next;
                EntryCount = count;
                return LoadResult.Loaded;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return LoadResult.Invalid;
            }
        }
    }
}
