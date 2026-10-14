using System;
using System.IO;
using System.Text;

namespace LOMSave.Probe
{
    public static class CodeNameMappingTests
    {
        private static int count;
        private static void Check(bool value, string message)
        { count++; if (!value) throw new Exception(message); }

        public static string Run(string catalogFile, string workDirectory)
        {
            count = 0;
            var catalog = new CodeNameMapStore(catalogFile);
            string error;
            Check(catalog.Reload(out error) == CodeNameMapStore.LoadResult.Loaded, "Generated YAML loads: " + error);
            Check(catalog.EntryCount > 4000 && catalog.NameCount > 2000, "Inventory covers game-wide codes rather than selected examples");
            Check(catalog.Lookup("conditions", "Facility_002") == "茶肆可用条件", "Lua option gate was traced to the game's visible caption");
            Check(catalog.Lookup("flags", "Facility_002") == "茶肆可用标记", "The explicitly linked flag has its own meaning");
            Check(catalog.Lookup("switches", "S_Mental_GE_65") == "心相 ≥ 65", "A full static condition yields a readable label");
            Check(catalog.Lookup("flags", "S0604_01") == null, "A complex event gate does not invent a flag's meaning");
            Check(catalog.Lookup("conditions", "Unknown_ID") == null, "Missing codes retain the caller's native fallback");
            Check(catalog.Reload(out error) == CodeNameMapStore.LoadResult.Unchanged, "An unchanged map is not repeatedly parsed");
            Directory.CreateDirectory(workDirectory);
            var file = Path.Combine(workDirectory, "reload.yaml");
            File.WriteAllText(file, @"version: 1
mappings:
  flags:
    Same_ID:
      name: '用户备注: #可用'
  conditions:
    Same_ID:
      name: 剧情条件
    Empty_ID:
      name: ''
", new UTF8Encoding(true));
            var store = new CodeNameMapStore(file);
            Check(store.Reload(out error) == CodeNameMapStore.LoadResult.Loaded, "BOM, quoted colons/comments and Chinese are valid YAML");
            Check(store.Lookup("flags", "Same_ID") == "用户备注: #可用" && store.Lookup("conditions", "Same_ID") == "剧情条件", "Scopes isolate a flag from a checkpoint with the same ID");
            Check(store.Lookup("conditions", "Empty_ID") == null, "Blank names preserve native descriptions and IDs");
            File.WriteAllText(file, "version: 1\nmappings:\n  flags:\n    Same_ID:\n      name: 编辑后的名称\n");
            Check(store.Reload(out error) == CodeNameMapStore.LoadResult.Loaded && store.Lookup("flags", "Same_ID") == "编辑后的名称", "A changed file reloads without restarting the store");
            File.WriteAllText(file, "version: 1\nmappings: [unfinished");
            Check(store.Reload(out error) == CodeNameMapStore.LoadResult.Invalid && error != null, "A half-written file reports a parse error");
            Check(store.Lookup("flags", "Same_ID") == "编辑后的名称", "Bad edits retain the complete last good map");
            Check(store.Reload(out error) == CodeNameMapStore.LoadResult.Unchanged, "The same invalid revision is not retried every frame");
            File.WriteAllText(file, "version: 1\nmappings:\n  flags:\n    Duplicate: { name: 第一条 }\n    Duplicate: { name: 第二条 }\n");
            Check(store.Reload(out error, true) == CodeNameMapStore.LoadResult.Invalid, "Duplicate IDs cannot silently overwrite one another");
            File.WriteAllText(file, "version: 2\nmappings: {}\n");
            Check(store.Reload(out error, true) == CodeNameMapStore.LoadResult.Invalid, "Unknown schema versions retain the last good data");
            File.WriteAllText(file, "version: 1\nmappings:\n  flgas: {}\n");
            Check(store.Reload(out error, true) == CodeNameMapStore.LoadResult.Invalid, "A misspelled scope cannot silently disable names");
            File.WriteAllText(file, "version: 1\nmappings:\n  flags:\n    HTML_ID:\n      name: '<b>普通文字</b>'\n");
            Check(store.Reload(out error, true) == CodeNameMapStore.LoadResult.Loaded && store.Lookup("flags", "HTML_ID") == "普通文字", "External names cannot inject rich-text styling into condition hints");
            File.Delete(file);
            Check(store.Reload(out error) == CodeNameMapStore.LoadResult.Missing && store.NameCount == 0, "Removing the map restores all native fallbacks");
            return count + " YAML inventory and reload assertions passed; " + catalog.EntryCount + " codes / " + catalog.NameCount + " names";
        }
    }
}
