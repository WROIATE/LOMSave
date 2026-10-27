using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LOMSave.Probe
{
    internal static class LuaRouteLiterals
    {
        private sealed class Scope { internal Dictionary<string, string> Before; internal HashSet<string> Written = new HashSet<string>(); }
        // Only prove literal aliases. Branch writes and dynamic expressions
        // invalidate values; never borrow a value from an unchosen sibling.
        internal static string Expand(string lua)
        {
            var values = new Dictionary<string, string>(); var stack = new Stack<Scope>(); var output = new StringBuilder();
            foreach (var raw in (lua ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (Regex.IsMatch(line, @"^(if|for|while|repeat)\b")) stack.Push(new Scope { Before = new Dictionary<string, string>(values) });
                else if (Regex.IsMatch(line, @"^(else|elseif)\b") && stack.Count > 0) values = new Dictionary<string, string>(stack.Peek().Before);
                else if (Regex.IsMatch(line, @"^(end|until)\b") && stack.Count > 0)
                {
                    var scope = stack.Pop(); values = scope.Before;
                    foreach (var key in scope.Written) { values.Remove(key); if (stack.Count > 0) stack.Peek().Written.Add(key); }
                }
                var assignment = Regex.Match(line, @"^(?:local\s+)?(\w+)\s*=\s*(.+)$");
                if (assignment.Success)
                {
                    var key = assignment.Groups[1].Value; var expression = assignment.Groups[2].Value.Trim();
                    var literal = Regex.Match(expression, "^[\"']([\\w_]+)[\"']$"); string alias;
                    if (literal.Success) values[key] = literal.Groups[1].Value;
                    else if (values.TryGetValue(expression, out alias)) values[key] = alias;
                    else values.Remove(key);
                    if (stack.Count > 0) stack.Peek().Written.Add(key);
                }
                output.AppendLine(Regex.Replace(raw, @"luamanager\.SetNextScript\(\s*(\w+)\s*\)", match => {
                    string value; return values.TryGetValue(match.Groups[1].Value, out value)
                        ? "luamanager.SetNextScript(\"" + value + "\")" : match.Value;
                }));
            }
            return output.ToString();
        }
    }
}
