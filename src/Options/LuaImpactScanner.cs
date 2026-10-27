using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LOMSave.Probe
{
    // Read generated story scripts only; never execute a branch to preview it.
    internal static class LuaImpactScanner
    {
        internal sealed class Action
        {
            internal string Method;
            internal string[] Arguments;
            internal bool Conditional;
            internal int RouteDepth;
        }

        internal sealed class Option
        {
            internal string Key, RootKey;
            internal List<Action> Actions;
        }

        private sealed class Block
        {
            internal readonly List<Node> Nodes = new List<Node>();
            internal Block Parent;
            internal Node Owner;
        }

        private sealed class Node
        {
            internal string Line;
            internal readonly List<Block> Branches = new List<Block>();
            internal bool HasElse;
        }

        private sealed class Marker
        {
            internal string Key;
            internal Block Block;
            internal int Index;
        }

        private static readonly Regex Index = new Regex(@"--index:\s*(O[\w_]+)");
        private static readonly Regex Assignment = new Regex(@"^(\w+)\[(\d+)\]\s*=.*?([""'])(.*?)\3");
        private static readonly Regex Choice = new Regex(@"(?:local\s+)?(\w+)\s*=\s*choose(?:timer)?\(\s*(\w+)");
        private static readonly Regex ChoiceBranch = new Regex(@"^(?:if|elseif)\s+(\w+)\s*==\s*(\d+)\s+then");
        private static readonly Regex Call = new Regex(@"\b(?:statmodifymanager\.(\w+)|luamanager\.(SwapUpgradeItem))\s*\(");
        private static readonly Regex Route = new Regex("luamanager\\.SetNextScript\\(\\s*[\"']([^\"']+)");
        private static readonly Regex Scene = new Regex("luamanager\\.ChangeScene\\(\\s*[\"']([^\"']+)");
        private static readonly Regex Pool = new Regex("checkpointmanager\\.Position\\(\\s*[\"']([^\"']+)");

        internal static List<Action> ScanEntry(string lua, Func<string, string> load)
        {
            var result = new List<Action>();
            var budget = 128;
            var root = Parse(lua, new List<Marker>());
            Walk(root, 0, false, result, ParsedLoader(lua, root, load),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, ref budget);
            return result;
        }

        internal static Dictionary<string, List<Action>> Scan(string lua, Func<string, string> load)
        {
            var result = new Dictionary<string, List<Action>>(StringComparer.Ordinal);
            foreach (var option in ScanOptions(lua, load)) result[option.Key] = option.Actions;
            return result;
        }

        // Preserve the enclosing player action for nested dialogue options.
        // Display grouping must not merge independent actions sharing a pool.
        internal static List<Option> ScanOptions(string lua, Func<string, string> load)
        {
            var markers = new List<Marker>();
            var root = Parse(lua, markers);
            var parsedLoad = ParsedLoader(lua, root, load);
            var result = new List<Option>();
            var markersByBlock = new Dictionary<Block, List<Marker>>();
            foreach (var marker in markers)
            {
                List<Marker> list;
                if (!markersByBlock.TryGetValue(marker.Block, out list)) markersByBlock.Add(marker.Block, list = new List<Marker>());
                list.Add(marker);
            }
            foreach (var marker in markers)
            {
                var actions = new List<Action>();
                var block = marker.Block;
                var start = marker.Index;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var budget = 128;
                while (block != null)
                {
                    if (Walk(block, start, false, actions, parsedLoad, visited, 0, ref budget)) break;
                    var owner = block.Owner;
                    block = block.Parent;
                    if (block != null) start = block.Nodes.IndexOf(owner) + 1;
                }
                var rootKey = marker.Key;
                var child = marker.Block;
                while (child.Parent != null)
                {
                    var ownerIndex = child.Parent.Nodes.IndexOf(child.Owner);
                    List<Marker> candidates;
                    if (markersByBlock.TryGetValue(child.Parent, out candidates))
                    {
                        var low = 0; var high = candidates.Count;
                        while (low < high)
                        {
                            var mid = low + (high - low) / 2;
                            if (candidates[mid].Index <= ownerIndex) low = mid + 1; else high = mid;
                        }
                        if (low > 0) rootKey = candidates[low - 1].Key;
                    }
                    child = child.Parent;
                }
                result.Add(new Option { Key = marker.Key, RootKey = rootKey, Actions = actions });
            }
            return result;
        }

        private static Func<string, Block> ParsedLoader(string rootLua, Block root, Func<string, string> load)
        {
            // Every option walks its own routes, but their immutable syntax
            // trees can be shared. Large hub scripts often route back to the
            // same source, which previously reparsed it for every option/path.
            var parsed = new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);
            return name =>
            {
                Block block;
                if (parsed.TryGetValue(name, out block)) return block;
                var source = load(name);
                block = string.IsNullOrEmpty(source) ? null
                    : string.Equals(source, rootLua, StringComparison.Ordinal) ? root
                    : Parse(source, new List<Marker>());
                parsed[name] = block;
                return block;
            };
        }

        private static Block Parse(string lua, List<Marker> markers)
        {
            var root = new Block();
            var block = root;
            var tables = new Dictionary<string, string>();
            var choices = new Dictionary<string, string>();
            foreach (var raw in LuaRouteLiterals.Expand(lua).Split('\n'))
            {
                var line = raw.Trim();
                var assignment = Assignment.Match(line);
                if (assignment.Success && assignment.Groups[4].Value != "~x")
                    tables[assignment.Groups[1].Value + ":" + assignment.Groups[2].Value] = assignment.Groups[4].Value;
                var choice = Choice.Match(line);
                if (choice.Success) choices[choice.Groups[1].Value] = choice.Groups[2].Value;
                var index = Index.Match(line);
                if (index.Success)
                    markers.Add(new Marker { Key = index.Groups[1].Value, Block = block, Index = block.Nodes.Count });
                if (line.StartsWith("--", StringComparison.Ordinal) || line.Length == 0) continue;
                if (Regex.IsMatch(line, @"^(if\b|for\b|while\b|repeat\b)"))
                {
                    var owner = new Node { Line = line };
                    block.Nodes.Add(owner);
                    var child = new Block { Parent = block, Owner = owner };
                    owner.Branches.Add(child);
                    block = child;
                }
                else if (Regex.IsMatch(line, @"^(elseif\b|else$)") && block.Parent != null)
                {
                    var owner = block.Owner;
                    if (line == "else") owner.HasElse = true;
                    var child = new Block { Parent = block.Parent, Owner = owner };
                    owner.Branches.Add(child);
                    block = child;
                }
                else if (Regex.IsMatch(line, @"^(end\b|until\b)") && block.Parent != null)
                    block = block.Parent;
                else block.Nodes.Add(new Node { Line = line });

                // Older scripts contain literal menu labels and no --index comments.
                var branch = ChoiceBranch.Match(line);
                string table, key;
                if (branch.Success && choices.TryGetValue(branch.Groups[1].Value, out table)
                    && tables.TryGetValue(table + ":" + branch.Groups[2].Value, out key))
                {
                    var optionKey = Regex.Match(key, @"O_[\w_]+");
                    markers.Add(new Marker { Key = optionKey.Success ? optionKey.Value : key,
                        Block = block, Index = block.Nodes.Count });
                }
            }
            return root;
        }

        // true means this path ended or reached the next player decision.
        private static bool Walk(Block block, int start, bool conditional, List<Action> result,
            Func<string, Block> load, HashSet<string> visited, int depth, ref int budget)
        {
            for (var i = start; i < block.Nodes.Count; i++)
            {
                var node = block.Nodes[i];
                var line = node.Line;
                if (Regex.IsMatch(line, @"\bchoose(?:timer)?\s*\(|\.ExecuteRoll\s*\(")) return true;
                if (node.Branches.Count > 0)
                {
                    var terminated = node.HasElse;
                    var someTerminated = false;
                    foreach (var branch in node.Branches)
                    {
                        var ended = Walk(branch, 0, true, result, load,
                            new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase), depth, ref budget);
                        terminated &= ended;
                        someTerminated |= ended;
                    }
                    if (terminated) return true;
                    // Only branches that did not leave this script reach its continuation.
                    if (someTerminated) conditional = true;
                    continue;
                }
                foreach (Match call in Call.Matches(line))
                {
                    var method = call.Groups[1].Success ? call.Groups[1].Value : call.Groups[2].Value;
                    if (method == "GetDisplayTime") continue;
                    result.Add(new Action { Method = method,
                        Arguments = ReadArguments(line, call.Index + call.Length), Conditional = conditional, RouteDepth = depth });
                }
                var route = Route.Match(line);
                var pool = Pool.Match(line);
                if (pool.Success)
                    result.Add(new Action { Method = "PositionPool", Arguments = new[] { pool.Groups[1].Value },
                        Conditional = conditional, RouteDepth = depth });
                if (route.Success)
                {
                    var name = route.Groups[1].Value;
                    if (depth < 8 && budget-- > 0 && visited.Add(name))
                    {
                        var next = load(name);
                        if (next != null)
                            Walk(next, 0, conditional, result, load, visited, depth + 1, ref budget);
                    }
                    return true;
                }
                var scene = Scene.Match(line);
                if (scene.Success)
                {
                    result.Add(new Action { Method = "ChangeScene", Arguments = new[] { scene.Groups[1].Value }, Conditional = conditional, RouteDepth = depth });
                    return true;
                }
                if (line == "return") return true;
            }
            return false;
        }

        internal static string[] ReadArguments(string line, int start)
        {
            var values = new List<string>();
            var value = new StringBuilder();
            var depth = 0;
            var quote = '\0';
            for (var i = start; i < line.Length; i++)
            {
                var c = line[i];
                if (quote != '\0')
                {
                    value.Append(c);
                    if (c == '\\' && i + 1 < line.Length) value.Append(line[++i]);
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'') quote = c;
                else if (c == '(' || c == '{' || c == '[') depth++;
                else if (c == ')' && depth == 0) { values.Add(value.ToString().Trim()); break; }
                else if (c == ')' || c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0) { values.Add(value.ToString().Trim()); value.Length = 0; continue; }
                value.Append(c);
            }
            return values.ToArray();
        }
    }
}
