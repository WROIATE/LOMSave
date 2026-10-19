using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LOMSave.Probe
{
    // Compile generated, structured story Lua into independently resumable tails.
    // Conditions above a checkpoint are never evaluated again. A selected branch
    // contributes only its remaining statements, followed by its parent's tail.
    internal sealed class StoryContinuationCompiler
    {
        internal sealed class Point
        {
            internal int Id;
            internal string Kind, Presentation;
            internal List<string> Locals;
        }
        private sealed class Node
        {
            internal int Id;
            internal string Code;
            internal readonly List<Branch> Branches = new List<Branch>();
        }
        private sealed class Branch
        {
            internal string Head;
            internal List<Node> Nodes;
        }
        private readonly List<Node> _root;
        private readonly Dictionary<int, List<string>> _scope = new Dictionary<int, List<string>>();
        private readonly Dictionary<string, int> _lastUse = new Dictionary<string, int>();
        internal readonly Dictionary<int, Point> Points = new Dictionary<int, Point>();
        private static readonly Regex Local = new Regex(@"^local\s+([A-Za-z_]\w*)\s*=\s*(.+)$", RegexOptions.Singleline);
        private static readonly Regex Variables = new Regex(@"\b__lom_v\d+\b");
        private static readonly Regex Interactive = new Regex(@"^(?:say\s*\(|local\s+\w+\s*=\s*(choose|choosetimer)\s*\(|runwait\s*\(\s*dicemenudialog\.ExecuteRoll\s*\()");
        private int _id;

        internal StoryContinuationCompiler(string source)
        {
            var lines = Statements(source);
            var cursor = 0;
            _root = Parse(lines, ref cursor, new Dictionary<string, string>(), new List<string>());
            if (cursor != lines.Count) throw new FormatException("Unexpected Lua block terminator");
            foreach (var point in Points.Values)
            {
                point.Locals = _scope[point.Id].Where(v => _lastUse.TryGetValue(v, out var last) && last >= point.Id).ToList();
                for (var i = 1; i <= 4; i++) point.Locals.Add("__lom_global" + i);
            }
        }

        internal string Instrumented(bool ensureCharacters = false)
        {
            var output = new StringBuilder(Helpers);
            // Recording a normal playthrough must keep native resource loading
            // unchanged. Only a restored tail can lack inherited actors.
            Emit(_root, output, true, ensureCharacters);
            return output.ToString();
        }

        internal string Resume(int id, Func<string, string> localValue, string visualSetup = "")
        {
            if (!Points.TryGetValue(id, out var point)) throw new FormatException("Unknown continuation point");
            var output = new StringBuilder(Helpers);
            output.AppendLine("while luamanager.GetStoryText('__LOM_CONT_READY__') ~= 'ready' do if luamanager.GetStoryText('__LOM_CONT_READY__') == 'failed' then error('LOMSave continuation cancelled') end; coroutine.yield() end");
            foreach (var variable in point.Locals)
                output.Append(variable.StartsWith("__lom_global", StringComparison.Ordinal) ? "var" + variable.Substring(12) : "local " + variable)
                    .Append(" = ").Append(localValue(variable)).AppendLine();
            output.AppendLine(visualSetup);
            output.AppendLine(point.Presentation);
            if (point.Kind == "dice") output.AppendLine("luamanager.GetStoryText('__LOM_CONT_DICE__')");
            EmitTail(_root, id, output, true);
            return output.ToString();
        }

        private List<Node> Parse(List<string> lines, ref int cursor, Dictionary<string, string> names, List<string> scope)
        {
            var result = new List<Node>();
            while (cursor < lines.Count)
            {
                var line = lines[cursor];
                if (line == "end" || line == "else" || line.StartsWith("elseif ", StringComparison.Ordinal)) break;
                cursor++;
                var node = new Node { Id = ++_id };
                if (line.StartsWith("if ", StringComparison.Ordinal))
                {
                    while (true)
                    {
                        if (line != "else" && !line.EndsWith(" then", StringComparison.Ordinal)) throw new FormatException("Unsupported if statement");
                        var branch = new Branch { Head = Rename(line, names) };
                        RecordUses(branch.Head, _id + 1);
                        branch.Nodes = Parse(lines, ref cursor, new Dictionary<string, string>(names), new List<string>(scope));
                        node.Branches.Add(branch);
                        if (cursor >= lines.Count) throw new FormatException("Unterminated if statement");
                        line = lines[cursor++];
                        if (line == "end") break;
                        if (line != "else" && !line.StartsWith("elseif ", StringComparison.Ordinal)) throw new FormatException("Unexpected branch");
                    }
                }
                else
                {
                    if (Regex.IsMatch(MaskStrings(line), @"\b(function|while|repeat|until|for|goto|break)\b") || line == "do")
                        throw new FormatException("Unsupported control flow");
                    var interactive = Interactive.Match(line);
                    if (interactive.Success)
                    {
                        var kind = line.StartsWith("say", StringComparison.Ordinal) ? "say"
                            : line.StartsWith("runwait", StringComparison.Ordinal) ? "dice" : interactive.Groups[1].Value;
                        Points[node.Id] = new Point { Id = node.Id, Kind = kind, Presentation = Presentation(result, kind) };
                        _scope[node.Id] = new List<string>(scope);
                    }
                    var local = Local.Match(line);
                    if (local.Success)
                    {
                        var value = Rename(local.Groups[2].Value, names);
                        var unique = "__lom_v" + node.Id;
                        names[local.Groups[1].Value] = unique;
                        scope.Add(unique);
                        node.Code = "local " + unique + " = " + value;
                    }
                    else
                    {
                        if (line.StartsWith("local ", StringComparison.Ordinal)) throw new FormatException("Unsupported declaration");
                        var assignment = Regex.Match(line, @"^(\w+)\s*=[^=]");
                        if (assignment.Success && !names.ContainsKey(assignment.Groups[1].Value)
                            && !Regex.IsMatch(assignment.Groups[1].Value, @"^var[1-4]$"))
                            throw new FormatException("Untracked story global: " + assignment.Groups[1].Value);
                        node.Code = Rename(line, names);
                    }
                    RecordUses(node.Code, node.Id);
                }
                result.Add(node);
            }
            return result;
        }

        private void RecordUses(string code, int id)
        { foreach (Match m in Variables.Matches(MaskStrings(code))) _lastUse[m.Value] = id; }

        private static string Presentation(List<Node> previous, string kind)
        {
            var start = previous.FindLastIndex(n => n.Code != null && n.Code.StartsWith(kind == "say" ? "setsaydialog(" : "setmenudialog(", StringComparison.Ordinal));
            if (start < 0) return "";
            var output = new StringBuilder();
            for (var i = start; i < previous.Count; i++)
            {
                var code = previous[i].Code;
                if (code == null) continue;
                // Only dialogue presentation setters; never character movement,
                // story callbacks, random draws or gameplay mutation methods.
                if (Regex.IsMatch(code, @"^(setsaydialog\(|setmenudialog\(|setcharacter\(|sayoptions\.|stage\.showPortrait\(|characters\.(LoadCharacterPortrait|Focus|RestoreRotation)\()"))
                { EnsureCharacters(code, output); output.AppendLine(code); }
            }
            return output.ToString();
        }

        private void Emit(List<Node> nodes, StringBuilder output, bool instrument, bool ensureCharacters)
        { foreach (var node in nodes) EmitNode(node, output, instrument, ensureCharacters); }

        private void EmitNode(Node node, StringBuilder output, bool instrument, bool ensureCharacters)
        {
            if (node.Branches.Count > 0)
            {
                foreach (var branch in node.Branches) { output.AppendLine(branch.Head); Emit(branch.Nodes, output, instrument, ensureCharacters); }
                output.AppendLine("end");
                return;
            }
            if (instrument && Points.TryGetValue(node.Id, out var point))
            {
                output.Append("__lom_checkpoint(").Append(node.Id).Append(", {");
                foreach (var variable in point.Locals) output.Append(variable).Append('=')
                    .Append(variable.StartsWith("__lom_global", StringComparison.Ordinal) ? "var" + variable.Substring(12) : variable).Append(',');
                output.AppendLine("})");
            }
            if (ensureCharacters) EnsureCharacters(node.Code, output);
            output.AppendLine(node.Code);
            if (instrument && Points.ContainsKey(node.Id)) output.AppendLine("luamanager.GetStoryText('__LOM_CONT_CLEAR__')");
        }

        private bool EmitTail(List<Node> nodes, int id, StringBuilder output, bool instrument)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var found = node.Id == id;
                if (found) EmitNode(node, output, instrument, true);
                else foreach (var branch in node.Branches)
                {
                    var inside = new StringBuilder();
                    if (!EmitTail(branch.Nodes, id, inside, instrument)) continue;
                    output.AppendLine("do").Append(inside).AppendLine("end");
                    found = true; break;
                }
                if (!found) continue;
                for (var j = i + 1; j < nodes.Count; j++) EmitNode(nodes[j], output, instrument, true);
                return true;
            }
            return false;
        }

        private static void EnsureCharacters(string code, StringBuilder output)
        {
            // Native Hide safely ignores a missing actor. Loading one merely
            // to hide it creates an unshown actor with no native Mood panel;
            // HideMood then throws, terminating the story after fade-out.
            if (Regex.IsMatch(code, @"^stage\s*\.\s*hide\b")) return;
            foreach (Match m in Regex.Matches(code, "characters\\.(?:Get|GetPortrait|LoadCharacterPortrait|Focus|RestoreRotation|ResetRotation|Shake|Rotate|MoveOffset)\\(\\s*([\"'])([A-Za-z0-9_]+)\\1"))
                output.Append("__lom_actor('").Append(m.Groups[2].Value).AppendLine("')");
        }

        private const string Helpers = @"
local function __lom_actor(key)
    if luamanager.GetStoryText('__LOM_CONT_ACTOR__' .. key) == 'missing' then
        runwait(characters.LoadCharacterAsset(key))
    end
    luamanager.GetStoryText('__LOM_CONT_ACTOR_RESTORE__' .. key)
end
local function __lom_checkpoint(id, values)
    local function pack(value, depth)
        if depth > 16 then error('snapshot nesting limit') end
        local kind = type(value)
        if kind == 'table' then
            local entries = {}
            for key, item in pairs(value) do
                if #entries >= 1024 then error('snapshot table limit') end
                entries[#entries + 1] = {key=pack(key, depth+1), value=pack(item, depth+1)}
            end
            return {kind='table', entries=entries}
        end
        if kind ~= 'nil' and kind ~= 'string' and kind ~= 'number' and kind ~= 'boolean' then error('unsupported snapshot value') end
        return {kind=kind, value=value}
    end
    local ok, data = pcall(function()
        local packed = {}
        for key, value in pairs(values) do packed[key] = pack(value, 0) end
        return json.serialize(packed)
    end)
    luamanager.GetStoryText('__LOM_CONT_POINT__' .. tostring(id) .. '|' .. (ok and data or '!'))
end
";

        // Preserve strings verbatim; rename identifiers only, never field names
        // or string contents. The generated scripts use one declaration per line.
        private static string Rename(string code, Dictionary<string, string> names)
        {
            var output = new StringBuilder();
            char previous = '\0';
            for (var i = 0; i < code.Length;)
            {
                var c = code[i];
                if (c == '\'' || c == '"')
                {
                    var end = StringEnd(code, i); output.Append(code, i, end - i); i = end; previous = c;
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    var end = i + 1; while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_')) end++;
                    var word = code.Substring(i, end - i);
                    output.Append(previous != '.' && previous != ':' && names.TryGetValue(word, out var replacement) ? replacement : word);
                    previous = word[word.Length - 1]; i = end;
                }
                else { output.Append(c); if (!char.IsWhiteSpace(c)) previous = c; i++; }
            }
            return output.ToString();
        }

        private static int StringEnd(string text, int start)
        {
            for (var i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == text[start]) return i + 1;
            }
            throw new FormatException("Unterminated string");
        }

        private static string MaskStrings(string text)
        {
            var chars = text.ToCharArray();
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\'' || text[i] == '"') { var end = StringEnd(text, i); for (; i < end; i++) chars[i] = ' '; i--; }
            return new string(chars);
        }

        private static List<string> Statements(string source)
        {
            var result = new List<string>(); var current = new StringBuilder(); var depth = 0;
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (c == '\'' || c == '"') { var end = StringEnd(source, i); current.Append(source, i, end - i); i = end - 1; continue; }
                if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
                {
                    if (i + 2 < source.Length && source[i + 2] == '[') throw new FormatException("Unsupported long comment");
                    while (i < source.Length && source[i] != '\n') i++;
                    c = '\n';
                }
                if (c == '[' && i + 1 < source.Length && (source[i + 1] == '[' || source[i + 1] == '=')) throw new FormatException("Unsupported long string");
                if (c == '(' || c == '{' || c == '[') depth++;
                if (c == ')' || c == '}' || c == ']') depth--;
                if (depth < 0) throw new FormatException("Unbalanced expression");
                if ((c == '\n' || c == ';') && depth == 0)
                { var line = current.ToString().Trim(); if (line.Length > 0) result.Add(line); current.Clear(); }
                else current.Append(c);
            }
            if (depth != 0) throw new FormatException("Unbalanced expression");
            if (current.ToString().Trim().Length > 0) result.Add(current.ToString().Trim());
            return result;
        }
    }
}
