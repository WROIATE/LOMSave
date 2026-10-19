using System;
using System.Collections.Generic;
using System.Linq;

namespace LOMSave.Probe
{
    public static class StoryContinuationTests
    {
        public static string Compile(string source) { return new StoryContinuationCompiler(source).Instrumented(); }
        public static string CompileWithInheritedContext(string source)
        { return new StoryContinuationCompiler(source).Instrumented(true); }
        public static string Resume(string source, int point)
        { return new StoryContinuationCompiler(source).Resume(point, name => "__saved." + name); }
        public static int[] Points(string source) { return new StoryContinuationCompiler(source).Points.Keys.ToArray(); }
        public static void Run()
        {
            foreach (var source in new[] { "while true do\nsay('x')\nend", "local x, y = 1, 2", "if true then\nsay('x')", "say([[x]])" })
            {
                var rejected = false;
                try { new StoryContinuationCompiler(source); } catch (FormatException) { rejected = true; }
                if (!rejected) throw new Exception("Unsupported syntax was accepted: " + source);
            }
            var s = "local result = 1\nif result == 1 then\nlocal result = 2\nsay('x')\nuse(result)\nend\nuse(result)";
            var p = new StoryContinuationCompiler(s);
            var point = p.Points.Values.Single();
            if (point.Locals.Count != 6) throw new Exception("Shadowed outer variable or global snapshot was lost");
            var resumed = p.Resume(point.Id, name => "saved." + name);
            if (resumed.Contains("if __lom_v1 == 1") || resumed.Contains("= 2\n")) throw new Exception("Already executed branch/declaration was replayed");
        }
    }
}
