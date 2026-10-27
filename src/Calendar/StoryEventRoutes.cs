using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LOMSave.Probe
{
    // A bounded, read-only index of generated menus. This never executes Lua.
    // Unknown guards are retained, so callers can omit unproven routes.
    internal sealed class StoryEventRoutes
    {
        internal sealed class Route
        {
            internal string Pool, Checkpoint, Kind, Script;
            internal string[] Options, Guards;
        }
        private sealed class Edge
        {
            internal string Pool, Script, Checkpoint, Kind;
            internal string[] Options, Guards;
        }
        private sealed class Branch
        {
            internal readonly List<string> Previous = new List<string>();
            internal string Guard, Option;
            internal bool Menu;
        }
        private sealed class Choice { internal string Label, Guard; }
        private readonly Dictionary<string, List<Edge>> _cache = new Dictionary<string, List<Edge>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Assignment = new Regex(@"^(?:local\s+)?(\w+)\[(\d+)\]\s*=\s*(.*)$");
        private static readonly Regex Literal = new Regex("^[\"']([^\"']*)[\"']$");
        private static readonly Regex ConditionalLabel = new Regex("^(.*?)\\s+and\\s+[\"']([^\"']+)[\"']\\s+or\\s+[\"']~x[\"']$");
        private static readonly Regex Variable = new Regex("^(?:local\\s+)?(\\w+)\\s*=\\s*checkpointmanager\\.(Condition|Switch)\\(\\s*[\"']([^\"']+)[\"']\\s*\\)");
        private static readonly Regex ChoiceCall = new Regex(@"^(?:local\s+)?(\w+)\s*=\s*choose(?:timer)?\(\s*(\w+)");
        private static readonly Regex ChoiceBranch = new Regex(@"^(\w+)\s*==\s*(\d+)$");
        private static readonly Regex PoolCall = new Regex("checkpointmanager\\.Position\\(\\s*[\"']([^\"']+)[\"']");
        private static readonly Regex NextCall = new Regex("luamanager\\.SetNextScript\\(\\s*[\"']([^\"']+)[\"']");

        internal List<Route> Scan(string script, Func<string, string> load, bool checkpoints = false)
        {
            var result = new List<Route>();
            var budget = 64;
            Walk(script, load, new string[0], new string[0], new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, ref budget, result);
            return checkpoints ? result : result.Where(x => x.Pool != null).ToList();
        }

        private void Walk(string script, Func<string, string> load, string[] options, string[] guards,
            HashSet<string> visited, int depth, ref int budget, List<Route> result)
        {
            if (string.IsNullOrEmpty(script) || depth > 8 || budget-- <= 0 || !visited.Add(script)) return;
            List<Edge> edges;
            if (!_cache.TryGetValue(script, out edges))
            {
                edges = Parse(load(script));
                if (_cache.Count >= 128) _cache.Clear();
                _cache[script] = edges;
            }
            foreach (var edge in edges)
            {
                var path = options.Concat(edge.Options).ToArray();
                var checks = guards.Concat(edge.Guards).ToArray();
                if (edge.Checkpoint != null) result.Add(new Route { Checkpoint = edge.Checkpoint, Kind = edge.Kind, Script = script, Options = path, Guards = checks });
                else if (edge.Pool != null) result.Add(new Route { Pool = edge.Pool, Options = path, Guards = checks });
                else Walk(edge.Script, load, path, checks, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase), depth + 1, ref budget, result);
            }
        }

        private static List<Edge> Parse(string lua)
        {
            var edges = new List<Edge>();
            var tables = new Dictionary<string, Choice>();
            var choices = new Dictionary<string, string>();
            var variables = new Dictionary<string, string>();
            var branches = new List<Branch>();
            foreach (var raw in LuaRouteLiterals.Expand(lua).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("--")) continue;
                var variable = Variable.Match(line);
                if (variable.Success) variables[variable.Groups[1].Value] = variable.Groups[2].Value + ":" + variable.Groups[3].Value;
                var assignment = Assignment.Match(line);
                if (assignment.Success)
                {
                    var value = assignment.Groups[3].Value;
                    var literal = Literal.Match(value);
                    var conditional = ConditionalLabel.Match(value);
                    tables[assignment.Groups[1].Value + ":" + assignment.Groups[2].Value] = new Choice {
                        Label = literal.Success ? literal.Groups[1].Value : conditional.Success ? conditional.Groups[2].Value : "~x",
                        Guard = literal.Success ? "true" : conditional.Success ? Expand(conditional.Groups[1].Value, variables) : "?" };
                }
                var choose = ChoiceCall.Match(line);
                if (choose.Success) choices[choose.Groups[1].Value] = choose.Groups[2].Value;
                if (line.StartsWith("if ") && line.EndsWith(" then"))
                {
                    var branch = new Branch();
                    SetBranch(branch, line.Substring(3, line.Length - 8), choices, tables, variables);
                    branches.Add(branch);
                }
                else if (line.StartsWith("elseif ") && line.EndsWith(" then") && branches.Count > 0)
                {
                    var branch = branches[branches.Count - 1];
                    branch.Previous.Add(branch.Guard);
                    SetBranch(branch, line.Substring(7, line.Length - 12), choices, tables, variables);
                }
                else if (line == "else" && branches.Count > 0)
                {
                    var branch = branches[branches.Count - 1];
                    branch.Previous.Add(branch.Guard); branch.Option = null; branch.Guard = branch.Menu ? "?" : "true";
                }
                else if (Regex.IsMatch(line, @"^(for|while|repeat)\b")) branches.Add(new Branch { Guard = "?" });
                else if (Regex.IsMatch(line, @"^(end\b|until\b)") && branches.Count > 0) branches.RemoveAt(branches.Count - 1);

                var pool = PoolCall.Match(line);
                var next = NextCall.Match(line);
                if (!pool.Success && !next.Success && !variable.Success) continue;
                var checks = new List<string>();
                foreach (var branch in branches)
                {
                    checks.Add(branch.Guard);
                    // Mutually exclusive menu choices are player-controlled,
                    // whereas condition elseif/else must exclude earlier arms.
                    foreach (var previous in branch.Previous.Where(x => x != "@choice")) checks.Add("not (" + previous + ")");
                }
                edges.Add(new Edge { Pool = pool.Success ? pool.Groups[1].Value : null,
                    Script = next.Success ? next.Groups[1].Value : null,
                    Checkpoint = variable.Success ? variable.Groups[3].Value : null, Kind = variable.Success ? variable.Groups[2].Value : null,
                    Options = branches.Where(x => x.Option != null).Select(x => x.Option).ToArray(),
                    Guards = checks.Where(x => x != "@choice" && x != "true").ToArray() });
            }
            return edges;
        }

        private static void SetBranch(Branch branch, string expression, Dictionary<string, string> choices,
            Dictionary<string, Choice> tables, Dictionary<string, string> variables)
        {
            branch.Option = null;
            branch.Menu = false;
            var choice = ChoiceBranch.Match(expression);
            string table;
            if (choice.Success && choices.TryGetValue(choice.Groups[1].Value, out table))
            {
                branch.Menu = true;
                Choice option;
                if (!tables.TryGetValue(table + ":" + choice.Groups[2].Value, out option) || option.Label == "~x") { branch.Guard = "?"; return; }
                branch.Option = option.Label;
                branch.Guard = option.Guard == "true" ? "@choice" : option.Guard;
                // Menu guards belong to that arm, not to other menu arms.
                branch.Previous.Clear();
                return;
            }
            branch.Guard = Expand(expression, variables);
        }

        private static string Expand(string expression, Dictionary<string, string> variables)
        { return Regex.Replace(expression, @"\b\w+\b", m => variables.ContainsKey(m.Value) ? variables[m.Value] : m.Value); }

        internal static bool? Check(string expression, Func<string, bool?> condition, Func<string, int?> selector)
        {
            expression = (expression ?? "").Trim();
            while (expression.StartsWith("(") && expression.EndsWith(")") && Encloses(expression))
                expression = expression.Substring(1, expression.Length - 2).Trim();
            foreach (var op in new[] { " or ", " and " })
            {
                var depth = 0;
                for (var i = 0; i <= expression.Length - op.Length; i++)
                {
                    if (expression[i] == '(') depth++;
                    if (expression[i] == ')') depth--;
                    if (depth != 0 || string.CompareOrdinal(expression, i, op, 0, op.Length) != 0) continue;
                    var a = Check(expression.Substring(0, i), condition, selector);
                    var b = Check(expression.Substring(i + op.Length), condition, selector);
                    if (op == " and ") return a == false || b == false ? false : a == true && b == true ? (bool?)true : null;
                    return a == true || b == true ? true : a == false && b == false ? (bool?)false : null;
                }
            }
            if (expression.StartsWith("not ")) { var value = Check(expression.Substring(4), condition, selector); return value.HasValue ? (bool?)!value.Value : null; }
            if (expression == "true") return true;
            if (expression == "false") return false;
            var simple = Regex.Match(expression, @"^Condition:([\w_]+)$");
            if (simple.Success) return condition(simple.Groups[1].Value);
            var compare = Regex.Match(expression, @"^(Switch|Condition):([\w_]+)\s*(==|~=|>=|<=|>|<)\s*(\d+|true|false)$");
            if (!compare.Success) return null;
            int? number;
            if (compare.Groups[1].Value == "Switch") number = selector(compare.Groups[2].Value);
            else { var value = condition(compare.Groups[2].Value); number = value.HasValue ? (int?)(value.Value ? 1 : 0) : null; }
            if (!number.HasValue) return null;
            var rhs = compare.Groups[4].Value;
            var expected = rhs == "true" ? 1 : rhs == "false" ? 0 : int.Parse(rhs);
            switch (compare.Groups[3].Value)
            { case "==": return number == expected; case "~=": return number != expected; case ">=": return number >= expected;
                case "<=": return number <= expected; case ">": return number > expected; default: return number < expected; }
        }
        private static bool Encloses(string expression)
        {
            var depth = 0;
            for (var i = 0; i < expression.Length - 1; i++)
            { if (expression[i] == '(') depth++; if (expression[i] == ')' && --depth == 0) return false; }
            return true;
        }
    }
}
