using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace LOMSave.Probe
{
    internal static class LocalJsonFile
    {
        internal static T Read<T>(string file) where T : class, new()
        { return File.Exists(file) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(file, Encoding.UTF8)) ?? new T() : new T(); }

        internal static void Write<T>(string file, T data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(data, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file);
        }
    }
}
