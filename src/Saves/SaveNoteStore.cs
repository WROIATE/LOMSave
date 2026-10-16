using System;
using System.IO;
using System.Text;

namespace LOMSave.Probe
{
    internal static class SaveNoteStore
    {
        internal const int Limit = 80;
        internal static string Normalize(string value)
        {
            var result = new StringBuilder();
            foreach (var c in value ?? "")
            {
                if (result.Length >= Limit) break;
                if (c == '\r' || c == '\n' || c == '\t') result.Append(' ');
                else if (!char.IsControl(c)) result.Append(c);
            }
            if (result.Length > 0 && char.IsHighSurrogate(result[result.Length - 1])) result.Length--;
            return result.ToString().Trim();
        }

        internal static string Read(string saveFile)
        {
            var file = saveFile + ".note.txt";
            return File.Exists(saveFile) && File.Exists(file) ? Normalize(File.ReadAllText(file)) : "";
        }

        internal static void Write(string saveFile, string text)
        {
            if (!File.Exists(saveFile)) throw new IOException("存档已不存在，请重新打开列表。");
            var file = saveFile + ".note.txt";
            var temp = file + ".tmp";
            File.WriteAllText(temp, Normalize(text), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }

        internal static void Delete(string saveFile)
        {
            var file = saveFile + ".note.txt";
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
