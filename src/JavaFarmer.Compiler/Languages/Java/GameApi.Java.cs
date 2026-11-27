using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>One overload of a game builtin as seen from Java.</summary>
    internal sealed class ApiFunctionDef
    {
        public string JavaName;       // "useItem"
        public string PythonName;     // "use_item"
        public string ReturnType;     // "boolean"
        public string[] ParamTypes;   // "Item", "int", "Entity|Item|Unlock", "Object...", "Runnable", "Supplier"
        public string[] ParamNames;
        public bool Unqualified = true;   // callable without a qualifier (static-import style)
        /// <summary>Registered extension builtins: exact neutral types (null for the v1 table, which uses the strings above).</summary>
        public IrType IrReturnType;
        public IrType[] IrParamTypes;

        public string Signature => ReturnType + " " + JavaName + "(" +
            string.Join(", ", ParamTypes.Select((t, i) => t + " " + ParamNames[i])) + ")";
    }

    public static partial class GameApi
    {
        static ApiFunctionDef F(string java, string py, string ret, params string[] ps)
        {
            var types = new List<string>(); var names = new List<string>();
            foreach (var p in ps)
            {
                int sp = p.LastIndexOf(' ');
                types.Add(p.Substring(0, sp)); names.Add(p.Substring(sp + 1));
            }
            return new ApiFunctionDef { JavaName = java, PythonName = py, ReturnType = ret, ParamTypes = types.ToArray(), ParamNames = names.ToArray() };
        }

        /// <summary>Game builtins callable unqualified from Java (SPEC §5). Overloads are separate entries.</summary>
        internal static readonly List<ApiFunctionDef> Defs = new List<ApiFunctionDef>
        {
            F("harvest", "harvest", "boolean"),
            F("canHarvest", "can_harvest", "boolean"),
            F("plant", "plant", "boolean", "Entity entity"),
            F("move", "move", "boolean", "Direction direction"),
            F("canMove", "can_move", "boolean", "Direction direction"),
            F("swap", "swap", "boolean", "Direction direction"),
            F("till", "till", "void"),
            F("getPosX", "get_pos_x", "int"),
            F("getPosY", "get_pos_y", "int"),
            F("getWorldSize", "get_world_size", "int"),
            F("getEntityType", "get_entity_type", "Entity"),
            F("getGroundType", "get_ground_type", "Ground"),
            F("getTime", "get_time", "double"),
            F("getTickCount", "get_tick_count", "double"),
            F("useItem", "use_item", "boolean", "Item item"),
            F("useItem", "use_item", "boolean", "Item item", "int n"),
            F("getWater", "get_water", "double"),
            F("doAFlip", "do_a_flip", "void"),
            F("petThePiggy", "pet_the_piggy", "void"),
            F("clear", "clear", "void"),
            F("changeHat", "change_hat", "void", "Hat hat"),
            F("quickPrint", "quick_print", "void", "Object... values"),
            F("numItems", "num_items", "double", "Item item"),
            F("getCost", "get_cost", "Map<Item,Double>", "Entity|Item|Unlock thing"),
            F("getCost", "get_cost", "Map<Item,Double>", "Unlock unlock", "int level"),
            F("getCompanion", "get_companion", "Companion"),
            F("unlock", "unlock", "boolean", "Unlock unlock"),
            F("numUnlocked", "num_unlocked", "int", "Unlock|Entity|Ground|Item thing"),
            F("measure", "measure", "Object"),
            F("measure", "measure", "Object", "Direction direction"),
            F("leaderboardRun", "leaderboard_run", "void", "Leaderboard leaderboard", "String fileName", "double speedup"),
            F("simulate", "simulate", "double", "String fileName", "Object unlocks", "Map<Item,Double> items", "Map<String,Object> globals", "double seed", "double speedup"),
            F("setExecutionSpeed", "set_execution_speed", "void", "double speed"),
            F("setWorldSize", "set_world_size", "void", "double size"),
            F("spawnDrone", "spawn_drone", "Drone", "Runnable task"),
            F("spawnDrone", "spawn_drone", "Drone", "Supplier<T> task"),
            F("waitFor", "wait_for", "Object", "Drone drone"),
            F("hasFinished", "has_finished", "boolean", "Drone drone"),
            F("numDrones", "num_drones", "int"),
            F("maxDrones", "max_drones", "int"),
        };

        /// <summary>Qualified Java library calls that map to game builtins (for tooltips / completion).</summary>
        internal static readonly List<ApiFunctionDef> LibraryDefs = new List<ApiFunctionDef>
        {
            Q(F("System.out.println", "print", "void", "Object value")),
            Q(F("System.out.print", "print", "void", "Object value")),
            Q(F("Math.min", "min", "int|double", "int|double a", "int|double b")),
            Q(F("Math.max", "max", "int|double", "int|double a", "int|double b")),
            Q(F("Math.abs", "abs", "int|double", "int|double a")),
            Q(F("Math.random", "random", "double")),
            Q(F("String.valueOf", "str", "String", "Object value")),
        };

        static ApiFunctionDef Q(ApiFunctionDef d) { d.Unqualified = false; return d; }

        /// <summary>Every builtin name the game defines (current and some historical), used to avoid name clashes.</summary>
        internal static readonly string[] AllPythonBuiltins =
        {
            "harvest", "can_harvest", "swap", "range", "plant", "move", "can_move", "till", "get_pos_x", "get_pos_y",
            "get_world_size", "get_entity_type", "get_ground_type", "get_time", "get_tick_count", "use_item", "get_water",
            "do_a_flip", "change_hat", "print", "quick_print", "len", "num_items", "get_cost", "clear", "get_companion",
            "unlock", "num_unlocked", "leaderboard_run", "measure", "min", "max", "abs", "str", "random", "simulate",
            "list", "set", "dict", "set_execution_speed", "set_world_size", "spawn_drone", "num_drones", "max_drones",
            "wait_for", "has_finished", "pet_the_piggy", "trade", "get_drone_id", "send", "receive", "get_active_power",
        };

        static Dictionary<string, string> j2p, p2j;
        static readonly object gate = new object();

        static void Init()
        {
            lock (gate)
            {
                if (j2p != null) return;
                var a = new Dictionary<string, string>();
                var b = new Dictionary<string, string>();
                foreach (var d in AllDefs.Concat(LibraryDefs))
                {
                    if (!a.ContainsKey(d.JavaName)) a[d.JavaName] = d.PythonName;
                    int dot = d.JavaName.LastIndexOf('.');
                    if (dot >= 0)
                    {
                        var shortName = d.JavaName.Substring(dot + 1);
                        if (!a.ContainsKey(shortName) && !AllDefs.Any(x => x.JavaName == shortName)) a[shortName] = d.PythonName;
                    }
                    if (!b.ContainsKey(d.PythonName)) b[d.PythonName] = d.JavaName;
                }
                p2j = b; j2p = a;
            }
        }

        static string ToPythonImpl(string javaName)
        {
            if (javaName == null) return null;
            Init();
            return j2p.TryGetValue(javaName, out var p) ? p : null;
        }

        static string ToJavaImpl(string pythonName)
        {
            if (pythonName == null) return null;
            Init();
            return p2j.TryGetValue(pythonName, out var j) ? j : null;
        }

        static IReadOnlyList<ApiFunction> functions;
        static IReadOnlyList<ApiFunction> FunctionsImpl
        {
            get
            {
                if (functions == null)
                {
                    functions = AllDefs.Concat(LibraryDefs).Select(d => new ApiFunction
                    {
                        JavaName = d.JavaName, PythonName = d.PythonName, Signature = d.Signature, ReturnType = d.ReturnType
                    }).ToList();
                }
                return functions;
            }
        }

        static Dictionary<string, List<ApiFunctionDef>> byJavaName;

        /// <summary>Java view of builtins registered at runtime (GameApi.Register), one def per overload.</summary>
        static List<ApiFunctionDef> extensionDefs = new List<ApiFunctionDef>();

        /// <summary>v1 table + registered extension builtins.</summary>
        internal static IEnumerable<ApiFunctionDef> AllDefs => Defs.Concat(extensionDefs);

        static partial void LanguageViewsChanged()
        {
            var ext = new List<ApiFunctionDef>();
            foreach (var b in Builtins)
            {
                if (b.Extension == null) continue;
                int required = b.Params.Count(p => !p.Optional && !p.Variadic);
                int total = b.Params.Count;
                for (int n = required; n <= total; n++)
                {
                    var ps = b.Params.Take(n).ToList();
                    ext.Add(new ApiFunctionDef
                    {
                        JavaName = ToStyle(b.PythonName, NamingStyle.Camel),
                        PythonName = b.PythonName,
                        ReturnType = JavaTypeText(b.ReturnType, b.ReturnText),
                        ParamTypes = ps.Select(p => p.Variadic ? "Object..." : JavaTypeText(p.Type, p.TypeText)).ToArray(),
                        ParamNames = ps.Select(p => ToStyle(p.Name, NamingStyle.Camel)).ToArray(),
                        IrReturnType = b.ReturnType,
                        IrParamTypes = ps.Select(p => p.Type).ToArray(),
                    });
                    if (ps.Any(p => p.Variadic)) break;
                }
            }
            lock (gate)
            {
                extensionDefs = ext;
                j2p = null; p2j = null; functions = null; byJavaName = null;
            }
        }

        /// <summary>Neutral type → Java type text (as written in signatures).</summary>
        internal static string JavaTypeText(IrType t, string text = null)
        {
            if (text != null && text.Contains("|") && !text.Contains("null")) return text.Replace(" ", "");
            string Box(IrType x)
            {
                switch (x.Kind)
                {
                    case IrTypeKind.Int: return "Integer";
                    case IrTypeKind.Float: return "Double";
                    case IrTypeKind.Bool: return "Boolean";
                }
                return JavaTypeText(x);
            }
            switch (t.Kind)
            {
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "boolean";
                case IrTypeKind.String: return "String";
                case IrTypeKind.Void: return "void";
                case IrTypeKind.List: return "List<" + Box(t.Elem) + ">";
                case IrTypeKind.Array: return JavaTypeText(t.Elem) + "[]";
                case IrTypeKind.Set: return "Set<" + Box(t.Elem) + ">";
                case IrTypeKind.Map: return "Map<" + Box(t.Key) + "," + Box(t.Elem) + ">";
                case IrTypeKind.Game: return t.GameName;
                case IrTypeKind.Func: return t.Elem == null || t.Elem.Kind == IrTypeKind.Void ? "Runnable" : "Supplier<" + Box(t.Elem) + ">";
            }
            return "Object";
        }
        static readonly List<ApiFunctionDef> None = new List<ApiFunctionDef>();

        internal static List<ApiFunctionDef> Overloads(string javaName)
        {
            var map = byJavaName;
            if (map == null)
            {
                map = new Dictionary<string, List<ApiFunctionDef>>();
                foreach (var d in AllDefs)
                {
                    if (!map.TryGetValue(d.JavaName, out var l)) map[d.JavaName] = l = new List<ApiFunctionDef>();
                    l.Add(d);
                }
                byJavaName = map;
            }
            return javaName != null && map.TryGetValue(javaName, out var res) ? res : None;
        }
    }
}
