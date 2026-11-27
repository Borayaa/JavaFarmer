using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.CSharp
{
    /// <summary>
    /// Farm.cs: the C# API stub for external editors and for the Roslyn conformance tests. Every GameApi builtin (also
    /// registered extension builtins) as a static method of class Farm (unions and optional parameters become
    /// overloads), the game types, Direction, and the constant classes Entities / Items / ... (partial, so the members
    /// can be added by CSharpLanguage.ConstantMembers or by a second file).
    /// </summary>
    internal static class CsStub
    {
        public static string Build(Func<string, IEnumerable<string>> members)
        {
            var sb = new StringBuilder();
            sb.Append("// Farm.cs - JavaFarmer C# API, written by the JavaFarmer mod.\n");
            sb.Append("// The game never runs this file: it lets an external editor (Visual Studio, Rider, VS Code) check your code.\n");
            sb.Append("// Compile it together with your files as C# 12 (.NET 8), e.g. in a console project with <Nullable>enable</Nullable>.\n");
            sb.Append("// Your code calls the game without a prefix (Harvest(), Move(North)) thanks to the global usings below.\n\n");
            sb.Append("global using System;\nglobal using System.Collections.Generic;\nglobal using static Farm;\n\n");
            sb.Append("#nullable enable\n\n");
            foreach (var t in new[] { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Drone" })
                sb.Append("public sealed class ").Append(t).Append(" { private ").Append(t).Append("() { } }\n");
            sb.Append("public enum Direction { North, East, South, West }\n");
            sb.Append("public sealed record Position(int X, int Y);\n");
            sb.Append("public sealed record Companion(Entity Entity, Position Position)\n{\n    public int X => Position.X;\n    public int Y => Position.Y;\n}\n\n");
            foreach (var bag in GameApi.ConstantCategories)
            {
                var type = GameApi.GameTypeOfCategory(bag);
                sb.Append("public static partial class ").Append(bag).Append("\n{\n");
                var ms = members?.Invoke(bag);
                if (ms != null)
                    foreach (var m in ms.Distinct().OrderBy(x => x, StringComparer.Ordinal))
                        sb.Append("    public static readonly ").Append(type).Append(' ').Append(m).Append(" = null!;\n");
                sb.Append("}\n");
            }
            sb.Append("\npublic static class Farm\n{\n");
            foreach (var d in GameApi.DirectionNames) sb.Append("    public const Direction ").Append(d).Append(" = Direction.").Append(d).Append(";\n");
            foreach (var b in GameApi.Builtins)
            {
                if (CsApi.Bcl.ContainsKey(b.PythonName)) continue;
                var name = GameApi.ToStyle(b.PythonName, NamingStyle.Pascal);
                var ret = b.ReturnText == "number" ? "double" : CsApi.TypeText(b.ReturnType, true);
                sb.Append("\n    /// <summary>").Append(Xml(b.Doc)).Append(" (game: ").Append(b.PythonName).Append(")</summary>\n");
                foreach (var ps in Variants(b))
                {
                    bool generic = ps.Any(p => p.Contains("Func<T>"));
                    sb.Append("    public static ").Append(ret).Append(' ').Append(name).Append(generic ? "<T>" : "").Append('(').Append(string.Join(", ", ps)).Append(')');
                    sb.Append(ret == "void" ? " { }\n" : " => default!;\n");
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        static string Xml(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        /// <summary>Parameter lists of the overloads of a builtin: optional parameters and unions expand to overloads.</summary>
        static IEnumerable<List<string>> Variants(ApiBuiltin b)
        {
            int required = b.Params.Count(p => !p.Optional && !p.Variadic);
            var res = new List<List<string>>();
            for (int n = required; n <= b.Params.Count; n++)
            {
                var lists = new List<List<string>> { new List<string>() };
                bool stop = false;
                foreach (var p in b.Params.Take(n))
                {
                    var pn = GameApi.ToStyle(p.Name, NamingStyle.Camel);
                    if (pn == "params" || pn == "object" || pn == "string") pn += "_";
                    List<string> opts;
                    if (p.Variadic) { opts = new List<string> { "params object?[] " + pn }; stop = true; }
                    else if (p.Type != null && p.Type.Kind == IrTypeKind.Func) opts = new List<string> { "Action " + pn, "Func<T> " + pn };
                    else if (CsApi.UnionOptions(p) is string[] u) opts = u.Select(o => o + " " + pn).ToList();
                    else opts = new List<string> { (p.TypeText == "number" ? "double" : TypeOrObject(p.Type)) + " " + pn };
                    lists = lists.SelectMany(l => opts.Select(o => l.Concat(new[] { o }).ToList())).ToList();
                }
                res.AddRange(lists);
                if (stop) break;
            }
            // variadic parameter that may be empty: one overload covers both
            return res.GroupBy(l => string.Join(",", l)).Select(g => g.First()).Where(l => !(l.Count == 0 && res.Any(x => x.Count == 1 && x[0].StartsWith("params "))));
        }

        static string TypeOrObject(IrType t)
        {
            var s = CsApi.TypeText(t);
            return s == "object" ? "object?" : s;
        }
    }
}
