using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.CSharp
{
    /// <summary>
    /// The C# view of the game API (GameApi): PascalCase names for the Farm class, the BCL spellings of the Python
    /// builtins that are part of C# (print → Console.WriteLine, min → Math.Min ...), and the C# types of every
    /// parameter. Rebuilt whenever GameApi.Version changes (registered extension builtins appear immediately).
    /// </summary>
    internal static class CsApi
    {
        /// <summary>Python builtins that C# spells with a BCL member instead of a Farm function.</summary>
        public static readonly Dictionary<string, string> Bcl = new Dictionary<string, string>
        {
            { "print", "Console.WriteLine" }, { "min", "Math.Min" }, { "max", "Math.Max" }, { "abs", "Math.Abs" },
        };

        static int version = -1;
        static Dictionary<string, ApiBuiltin> byName = new Dictionary<string, ApiBuiltin>();
        static readonly object gate = new object();

        static void Refresh()
        {
            if (version == GameApi.Version && byName.Count > 0) return;
            lock (gate)
            {
                if (version == GameApi.Version && byName.Count > 0) return;
                var d = new Dictionary<string, ApiBuiltin>();
                foreach (var b in GameApi.Builtins)
                {
                    if (Bcl.ContainsKey(b.PythonName)) continue;
                    var n = GameApi.ToStyle(b.PythonName, NamingStyle.Pascal);
                    if (!d.ContainsKey(n)) d[n] = b;
                }
                byName = d;
                version = GameApi.Version;
            }
        }

        /// <summary>Farm function by its C# name ("CanHarvest"); null if none.</summary>
        public static ApiBuiltin Find(string csName)
        {
            if (csName == null) return null;
            Refresh();
            var m = byName;
            return m.TryGetValue(csName, out var b) ? b : null;
        }

        public static IEnumerable<string> Names { get { Refresh(); return byName.Keys.ToList(); } }

        /// <summary>"can_harvest" → "CanHarvest", "print" → "Console.WriteLine"; null if unknown.</summary>
        public static string ApiName(string pythonName)
        {
            if (pythonName == null) return null;
            if (Bcl.TryGetValue(pythonName, out var bcl)) return bcl;
            if (pythonName == "str") return "ToString";
            if (pythonName == "len") return "Count";
            var b = GameApi.Find(pythonName);
            return b == null ? null : GameApi.ToStyle(pythonName, NamingStyle.Pascal);
        }

        /// <summary>Inverse of ApiName (also accepts Console.Write, Farm.Harvest, Min ...).</summary>
        public static string FromApiName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var kv in Bcl) if (kv.Value == name) return kv.Key;
            switch (name)
            {
                case "Console.Write": return "print";
                case "Min": return "min";
                case "Max": return "max";
                case "Abs": return "abs";
                case "ToString": return "str";
            }
            if (name.StartsWith("Farm.")) name = name.Substring(5);
            var b = Find(name);
            return b?.PythonName;
        }

        /// <summary>C# parameter type of a neutral API parameter (unions are handled by the caller).</summary>
        public static CType ParamType(ApiParam p)
        {
            if (p.TypeText == "number") return CType.Double;
            if (p.TypeText != null && p.TypeText.Contains("|") && !p.TypeText.Contains("null")) return CType.Object;
            return CType.FromIr(p.Type);
        }

        public static CType ReturnType(ApiBuiltin b)
        {
            if (b.ReturnText == "number") return CType.Double;
            return CType.FromIr(b.ReturnType);
        }

        /// <summary>Game types accepted by a union parameter ("Entity|Item|Unlock"), or null when it is not a union.</summary>
        public static string[] UnionOptions(ApiParam p)
        {
            if (p.TypeText == null || !p.TypeText.Contains("|")) return null;
            return p.TypeText.Replace(" ", "").Split('|').Where(x => x != "null" && x != "None").ToArray();
        }

        /// <summary>C# spelling of a neutral type for the stub file.</summary>
        public static string TypeText(IrType t, bool forReturn = false)
        {
            if (t == null) return "object";
            string s;
            switch (t.Kind)
            {
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "bool";
                case IrTypeKind.String: s = "string"; break;
                case IrTypeKind.Void: return "void";
                case IrTypeKind.List: s = "List<" + TypeText(t.Elem) + ">"; break;
                case IrTypeKind.Array: s = TypeText(t.Elem) + "[]"; break;
                case IrTypeKind.Set: s = "HashSet<" + TypeText(t.Elem) + ">"; break;
                case IrTypeKind.Map: s = "Dictionary<" + TypeText(t.Key) + ", " + TypeText(t.Elem) + ">"; break;
                case IrTypeKind.Game: s = t.GameName; break;
                case IrTypeKind.Tuple: s = "(" + string.Join(", ", t.Items.Select(i => TypeText(i))) + ")"; break;
                case IrTypeKind.Func: return t.Elem == null || t.Elem.Kind == IrTypeKind.Void ? "Action" : "Func<" + TypeText(t.Elem) + ">";
                default: s = "object"; break;
            }
            return t.Nullable || (forReturn && s == "object") ? s + "?" : s;
        }
    }
}
