using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal sealed class ZApiParam
    {
        public string Name;
        public ZType T;            // null: anytype
        public bool IsFunc;        // comptime task: anytype (a function)
        public bool IsTuple;       // a variadic parameter: a tuple of values
        public ApiParam Source;
    }

    /// <summary>One farm.xxx function: a game builtin (or one arity of a builtin with optional parameters).</summary>
    internal sealed class ZApiFn
    {
        public string ZigName;
        public ApiBuiltin Builtin;
        public List<ZApiParam> Params = new List<ZApiParam>();
        /// <summary>The builtin returns "any": the first argument is the result type (comptime T: type).</summary>
        public bool TypeArg;
        public ZType Ret;          // null when TypeArg
    }

    /// <summary>
    /// The Zig view of GameApi (camelCase in the farm module). Builtins with optional parameters get one function per
    /// arity: useItem(item) and useItemN(item, n) (the optional parameters' names, PascalCase, are appended).
    /// print / quick_print / min / max / abs are spelled std.debug.print, farm.quickPrint, @min, @max, @abs.
    /// Rebuilt whenever GameApi.Version changes (registered extension builtins appear immediately).
    /// </summary>
    internal static class ZigApi
    {
        sealed class Table
        {
            public int Version;
            public Dictionary<string, ZApiFn> ByZig = new Dictionary<string, ZApiFn>(StringComparer.Ordinal);
            public Dictionary<string, List<ZApiFn>> ByPy = new Dictionary<string, List<ZApiFn>>(StringComparer.Ordinal);
        }

        static volatile Table table;
        static readonly object gate = new object();

        /// <summary>Builtins with their own Zig spelling (not farm.xxx).</summary>
        public static readonly Dictionary<string, string> Special = new Dictionary<string, string>
        {
            ["print"] = "std.debug.print", ["min"] = "@min", ["max"] = "@max", ["abs"] = "@abs",
        };

        static Table Get()
        {
            var t = table;
            var v = GameApi.Version;
            if (t != null && t.Version == v) return t;
            lock (gate)
            {
                t = table;
                if (t != null && t.Version == v) return t;
                t = Build(v);
                table = t;
                return t;
            }
        }

        static Table Build(int version)
        {
            var t = new Table { Version = version };
            foreach (var b in GameApi.Builtins)
            {
                if (Special.ContainsKey(b.PythonName)) continue;
                var list = new List<ZApiFn>();
                foreach (var f in Variants(b)) { if (!t.ByZig.ContainsKey(f.ZigName)) { t.ByZig[f.ZigName] = f; list.Add(f); } }
                t.ByPy[b.PythonName] = list;
            }
            return t;
        }

        static IEnumerable<ZApiFn> Variants(ApiBuiltin b)
        {
            var baseName = GameApi.ToStyle(b.PythonName, NamingStyle.Camel);
            var required = b.Params.Where(p => !p.Optional && !p.Variadic).ToList();
            var optional = b.Params.Where(p => p.Optional && !p.Variadic).ToList();
            var variadic = b.Params.FirstOrDefault(p => p.Variadic);
            bool typeArg = b.ReturnType == null || b.ReturnType.Kind == IrTypeKind.Any || b.ReturnType.Kind == IrTypeKind.Unknown;
            var ret = typeArg ? null : RetType(b);
            for (int k = 0; k <= optional.Count; k++)
            {
                var f = new ZApiFn { Builtin = b, TypeArg = typeArg, Ret = ret };
                var name = new StringBuilder(baseName);
                foreach (var p in required) f.Params.Add(Param(p));
                for (int j = 0; j < k; j++) { f.Params.Add(Param(optional[j])); name.Append(GameApi.ToStyle(optional[j].Name, NamingStyle.Pascal)); }
                if (variadic != null && k == optional.Count) f.Params.Add(new ZApiParam { Name = variadic.Name, IsTuple = true, Source = variadic });
                f.ZigName = name.ToString();
                yield return f;
            }
        }

        static ZApiParam Param(ApiParam p)
        {
            var zp = new ZApiParam { Name = p.Name, Source = p };
            var t = p.Type ?? IrType.Any;
            if (t.Kind == IrTypeKind.Func) { zp.IsFunc = true; return zp; }
            zp.T = FromIr(t, true);
            return zp;
        }

        static ZType RetType(ApiBuiltin b)
        {
            var t = b.ReturnType;
            if (t.Kind == IrTypeKind.Int) return b.NonNegative ? ZType.Usize : ZType.I32;
            return FromIr(t, false) ?? ZType.Void;
        }

        /// <summary>Neutral type → Zig type (null = anytype). Parameters take slices for lists, results ArrayLists.</summary>
        public static ZType FromIr(IrType t, bool param)
        {
            if (t == null) return null;
            ZType r;
            switch (t.Kind)
            {
                case IrTypeKind.Void: r = ZType.Void; break;
                case IrTypeKind.Bool: r = ZType.Bool; break;
                case IrTypeKind.Int: r = param ? ZType.Usize : ZType.I32; break;
                case IrTypeKind.Float: r = ZType.F64; break;
                case IrTypeKind.String: r = ZType.Str; break;
                case IrTypeKind.Game:
                    if (t.GameName == "Position" || t.GameName == "Companion" || t.GameName == "Drone") r = ZType.StructT(t.GameName);
                    else r = ZType.GameT(t.GameName);
                    break;
                case IrTypeKind.List:
                case IrTypeKind.Array:
                    {
                        var e = FromIr(t.Elem, param);
                        if (e == null) return null;
                        r = param ? ZType.SliceOf(e, true) : ZType.ListOf(e);
                        break;
                    }
                case IrTypeKind.Set:
                    {
                        var e = FromIr(t.Elem, param);
                        if (e == null || e.IsString) return null;
                        r = ZType.MapOf(e, ZType.Void);
                        break;
                    }
                case IrTypeKind.Map:
                    {
                        var k = FromIr(t.Key, param);
                        var v = FromIr(t.Elem, param);
                        if (k == null || v == null || k.IsString) return null;
                        r = ZType.MapOf(k, v);
                        break;
                    }
                default: return null;
            }
            return t.Nullable && r.K != ZK.Void ? ZType.Opt(r) : r;
        }

        public static ZApiFn Find(string zigName)
        {
            if (zigName == null) return null;
            return Get().ByZig.TryGetValue(zigName, out var f) ? f : null;
        }

        /// <summary>farm functions of a builtin, shortest arity first.</summary>
        public static IReadOnlyList<ZApiFn> OfPython(string py)
        {
            if (py == null) return Array.Empty<ZApiFn>();
            return Get().ByPy.TryGetValue(py, out var l) ? l : (IReadOnlyList<ZApiFn>)Array.Empty<ZApiFn>();
        }

        /// <summary>The farm function for a call with argCount game arguments (null if none fits).</summary>
        public static ZApiFn ForArity(string py, int argCount)
        {
            ZApiFn best = null;
            foreach (var f in OfPython(py))
            {
                int n = f.Params.Count(p => !p.IsTuple);
                bool tuple = f.Params.Any(p => p.IsTuple);
                if (n == argCount || (tuple && argCount >= n)) return f;
                if (best == null || n > best.Params.Count) best = f;
            }
            return null;
        }

        public static IEnumerable<ZApiFn> All => Get().ByZig.Values;

        // ------------------------------------------------------------------ stub text

        /// <summary>A type as written inside farm.zig (game types without the "farm." prefix).</summary>
        public static string StubType(ZType t)
        {
            switch (t.K)
            {
                case ZK.Game: case ZK.Struct: return t.Name;
                case ZK.Optional: return "?" + StubType(t.Elem);
                case ZK.Slice: return "[]" + (t.Const ? "const " : "") + StubType(t.Elem);
                case ZK.List: return "std.ArrayList(" + StubType(t.Elem) + ")";
                case ZK.Map: return "std.AutoHashMap(" + StubType(t.Key) + ", " + StubType(t.Elem) + ")";
            }
            return t.ToString();
        }

        /// <summary>
        /// farm.zig: every builtin (incl. registered ones), the game enums with the given members, Position, Companion,
        /// Drone, quickPrint, random and allocator. Bodies only discard their parameters: the game runs the program,
        /// the stub only has to make real Zig accept it.
        /// </summary>
        public static string Stub(IDictionary<string, IEnumerable<string>> members)
        {
            var sb = new StringBuilder();
            sb.Append("//! farm.zig - the JavaFarmer game API for Zig (The Farmer Was Replaced).\n");
            sb.Append("//! Written by the JavaFarmer mod for external editors. The game never runs this file:\n");
            sb.Append("//! your program is translated to the game's own language. Use it with\n");
            sb.Append("//!     const farm = @import(\"farm\");\n");
            sb.Append("//! and build with: zig build-exe --dep farm -Mroot=main.zig -Mfarm=farm.zig\n\n");
            sb.Append("const std = @import(\"std\");\n\n");
            sb.Append("/// The allocator for std.ArrayList / std.AutoHashMap (JavaFarmer ignores it).\n");
            sb.Append("pub const allocator: std.mem.Allocator = std.heap.page_allocator;\n\n");
            foreach (var cat in GameApi.ConstantCategories.Concat(new[] { "Directions" }))
            {
                var type = GameApi.GameTypeOfCategory(cat);
                IEnumerable<string> ms = null;
                if (members != null) members.TryGetValue(cat, out ms);
                var list = (ms ?? Enumerable.Empty<string>()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (cat == "Directions") list = GameApi.DirectionNames.Concat(list.Where(x => !GameApi.DirectionNames.Contains(x))).ToList();
                sb.Append("/// Game constants ").Append(cat).Append(".X (Python) are farm.").Append(type).Append(".X (or .X where the type is known).\n");
                sb.Append("pub const ").Append(type).Append(" = enum {");
                if (list.Count == 0) sb.Append(" _unused");
                else
                {
                    sb.Append('\n');
                    foreach (var m in list) sb.Append("    ").Append(m).Append(",\n");
                }
                sb.Append(list.Count == 0 ? " };\n\n" : "};\n\n");
            }
            sb.Append("/// A tile position (the game's (x, y) tuple).\n");
            sb.Append("pub const Position = struct { x: usize, y: usize };\n\n");
            sb.Append("/// What getCompanion() returns: the plant the current one wants, and where.\n");
            sb.Append("pub const Companion = struct { entity: Entity, x: usize, y: usize };\n\n");
            sb.Append("/// A drone handle (spawnDrone / waitFor / hasFinished).\n");
            sb.Append("pub const Drone = struct { id: usize };\n\n");
            sb.Append("/// Like std.debug.print, but only to the output page (no delay above the drone).\n");
            sb.Append("pub fn quickPrint(comptime fmt: []const u8, args: anytype) void {\n    std.debug.print(fmt, args);\n}\n\n");
            foreach (var f in All.OrderBy(f => IndexOf(f.Builtin)).ThenBy(f => f.Params.Count))
            {
                if (f.Builtin.PythonName == "quick_print") continue;
                var doc = (f.Builtin.Doc ?? "").Replace("\n", " ");
                sb.Append("/// ").Append(doc.Length > 0 ? doc + " " : "").Append("(game: ").Append(f.Builtin.PythonName).Append(f.Builtin.Extension != null ? ", extension " + f.Builtin.Extension : "").Append(")\n");
                sb.Append("pub fn ").Append(f.ZigName).Append('(');
                var ps = new List<string>();
                var used = new List<string>();
                var taken = StubDecls();
                if (f.TypeArg) ps.Add("comptime T: type");
                foreach (var p in f.Params)
                {
                    var n = SafeName(p.Name);
                    while (taken.Contains(n) || used.Contains(n)) n += "_";
                    used.Add(n);
                    if (p.IsFunc) ps.Add("comptime " + n + ": anytype");
                    else if (p.T == null || p.IsTuple) ps.Add(n + ": anytype");
                    else ps.Add(n + ": " + StubType(p.T));
                }
                sb.Append(string.Join(", ", ps)).Append(") ");
                sb.Append(f.TypeArg ? "T" : StubType(f.Ret)).Append(" {\n");
                foreach (var n in used) sb.Append("    _ = ").Append(n).Append(";\n");
                // the game's functions run in the game: a comptime call (a container-level initializer) is an error, as in JavaFarmer
                sb.Append("    if (@inComptime()) @compileError(\"unable to resolve comptime value: farm.").Append(f.ZigName).Append(" runs in the game; call it in a function\");\n");
                if (f.TypeArg) sb.Append("    return undefined;\n");
                else if (f.Ret.K != ZK.Void) sb.Append("    return ").Append(StubDefault(f.Ret)).Append(";\n");
                sb.Append("}\n\n");
            }
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        static HashSet<string> StubDecls()
        {
            var s = new HashSet<string>(StringComparer.Ordinal) { "std", "allocator", "quickPrint", "Position", "Companion", "Drone", "T" };
            foreach (var f in All) s.Add(f.ZigName);
            foreach (var c in GameApi.ConstantCategories) s.Add(GameApi.GameTypeOfCategory(c));
            s.Add("Direction");
            return s;
        }

        static int IndexOf(ApiBuiltin b)
        {
            var all = GameApi.Builtins;
            for (int i = 0; i < all.Count; i++) if (all[i].PythonName == b.PythonName) return i;
            return all.Count;
        }

        static string StubDefault(ZType t)
        {
            switch (t.K)
            {
                case ZK.Bool: return "false";
                case ZK.Int: case ZK.Float: return "0";
                case ZK.Optional: return "null";
                case ZK.Slice: return t.IsString ? "\"\"" : "&.{}";
                case ZK.List: return ".empty";
                case ZK.Map: return "." + "init(allocator)";
            }
            return "undefined";
        }

        static readonly HashSet<string> ZigReserved = new HashSet<string>(ZigLexer.Keywords) { "type", "bool", "void", "true", "false", "null", "undefined", "std", "allocator", "T" };

        public static string SafeName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "arg";
            var c = GameApi.ToStyle(n, NamingStyle.Camel);
            if (ZigReserved.Contains(c) || ZType.IntT(c) != null || c == "f32" || c == "f64") c += "_";
            return c;
        }

        /// <summary>The game's known constant members (only for the player's stub file; checking never uses this list).</summary>
        public static readonly Dictionary<string, IEnumerable<string>> KnownMembers = new Dictionary<string, IEnumerable<string>>
        {
            ["Items"] = new[] { "Carrot", "Fertilizer", "Gold", "Hay", "Power", "Pumpkin", "Water", "Weird_Substance", "Wood", "Cactus", "Bone" },
            ["Hats"] = new[] { "Brown_Hat", "Cactus_Hat", "Carrot_Hat", "Dinosaur_Hat", "Gold_Hat", "Gold_Trophy_Hat", "Golden_Cactus_Hat", "Golden_Carrot_Hat", "Golden_Gold_Hat", "Golden_Pumpkin_Hat", "Golden_Sunflower_Hat", "Golden_Tree_Hat", "Gray_Hat", "Green_Hat", "Pumpkin_Hat", "Purple_Hat", "Silver_Trophy_Hat", "Straw_Hat", "Sunflower_Hat", "The_Farmers_Remains", "Top_Hat", "Traffic_Cone", "Traffic_Cone_Stack", "Tree_Hat", "Wizard_Hat", "Wood_Trophy_Hat" },
            ["Leaderboards"] = new[] { "Cactus", "Cactus_Single", "Carrots", "Carrots_Single", "Dinosaur", "Fastest_Reset", "Hay", "Hay_Single", "Maze", "Maze_Single", "Pumpkins", "Pumpkins_Single", "Sunflowers", "Sunflowers_Single", "Wood", "Wood_Single" },
            ["Entities"] = new[] { "Apple", "Grass", "Bush", "Tree", "Carrot", "Pumpkin", "Dead_Pumpkin", "Sunflower", "Cactus", "Hedge", "Treasure", "Dinosaur" },
            ["Grounds"] = new[] { "Grassland", "Soil" },
            ["Unlocks"] = new[] { "Trees", "Speed", "Plant", "Loops", "Senses", "Expand", "Operators", "Pumpkins", "Variables", "Functions", "Imports", "Watering", "Carrots", "Lists", "Costs", "Fertilizer", "Mazes", "Debug", "Timing", "Grass", "Multi_Trade", "Auto_Unlock", "Polyculture", "Sunflowers", "Leaderboard", "Dictionaries", "Utilities", "Cactus", "Dinosaurs", "Megafarm", "Hats", "Simulation" },
            ["Directions"] = new string[0],
        };
    }
}
