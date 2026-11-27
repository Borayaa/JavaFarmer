using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    public enum NamingStyle { Snake, Camel, Pascal }

    public sealed class ApiParam
    {
        public string Name;
        public IrType Type;
        /// <summary>Neutral type text as declared ("Entity|Item|Unlock", "int", "func").</summary>
        public string TypeText;
        public bool Optional;     // may be omitted (trailing)
        public bool Variadic;     // *values: any number of arguments (>= 0, or >= 1 when not Optional)
    }

    /// <summary>One game builtin in canonical (Python) form with a neutral signature.</summary>
    public sealed class ApiBuiltin
    {
        public string PythonName;
        public List<ApiParam> Params = new List<ApiParam>();
        public IrType ReturnType = IrType.Void;
        /// <summary>Neutral return type text ("bool", "Entity?", "map&lt;Item,float&gt;?", "number").</summary>
        public string ReturnText = "void";
        public string Doc = "";
        /// <summary>Extension id ("cybersecurity", "aiml") or null for the base game.</summary>
        public string Extension;
        /// <summary>Result is always &gt;= 0 (drives the integer division strategy).</summary>
        public bool NonNegative;
        /// <summary>Result does not change while a loop body runs (drives range() detection): get_world_size, max_drones.</summary>
        public bool LoopInvariant;
        /// <summary>Neither has nor observes side effects (min/max/abs): evaluation order around it does not matter.</summary>
        public bool Pure;
        /// <summary>min/max/abs: the result is int when every argument is int, else float.</summary>
        public bool NumericLikeArgs;
        public int MinArgs => Params.Count(p => !p.Optional && !p.Variadic) + (Params.Any(p => p.Variadic && !p.Optional) ? 1 : 0);
        public int MaxArgs => Params.Any(p => p.Variadic) ? int.MaxValue : Params.Count;
        /// <summary>"use_item(item: Item, n?: int) -> bool"</summary>
        public string Signature
        {
            get
            {
                var sb = new StringBuilder(PythonName).Append('(');
                for (int i = 0; i < Params.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var p = Params[i];
                    if (p.Variadic) sb.Append('*');
                    sb.Append(p.Name).Append(p.Optional && !p.Variadic ? "?: " : ": ").Append(p.TypeText);
                }
                return sb.Append(") -> ").Append(ReturnText).ToString();
            }
        }
    }

    /// <summary>
    /// Canonical game API (single source of truth for every language): builtins by snake_case Python name with neutral
    /// signatures, naming conventions, constant categories, and runtime registration for extension builtins.
    /// </summary>
    public static partial class GameApi
    {
        static readonly object apiGate = new object();

        /// <summary>
        /// Immutable snapshot of the table: readers take the current one without a lock; Register / Unregister build a
        /// new one under apiGate and publish it. Enumerating Builtins is therefore never disturbed by a registration
        /// on another thread.
        /// </summary>
        sealed class ApiTable
        {
            public readonly IReadOnlyList<ApiBuiltin> List;
            public readonly Dictionary<string, ApiBuiltin> ByPy;
            /// <summary>Styled name → Python name per NamingStyle (FromApiName), built on first use.</summary>
            public readonly Dictionary<string, string>[] Styled = new Dictionary<string, string>[8];

            public ApiTable(List<ApiBuiltin> list)
            {
                List = list.AsReadOnly();
                ByPy = new Dictionary<string, ApiBuiltin>(list.Count);
                foreach (var b in list) ByPy[b.PythonName] = b;
            }

            public Dictionary<string, string> StyledNames(NamingStyle style)
            {
                int i = (int)style;
                if (i < 0 || i >= Styled.Length) return null;
                var d = Styled[i];
                if (d != null) return d;
                d = new Dictionary<string, string>(List.Count, StringComparer.Ordinal);
                foreach (var b in List)
                {
                    var n = ToStyle(b.PythonName, style);
                    if (n != null && !d.ContainsKey(n)) d[n] = b.PythonName;   // first in declaration order wins
                }
                return Styled[i] = d;
            }
        }

        static volatile ApiTable table;

        /// <summary>Bumped on every Register/Unregister (languages use it to refresh caches).</summary>
        public static int Version { get; private set; }
        /// <summary>Raised after Register/Unregister.</summary>
        public static event Action Changed;

        public static readonly string[] ConstantCategories = { "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards" };
        public static readonly string[] DirectionNames = { "North", "East", "South", "West" };

        /// <summary>"Entities" → "Entity", "Directions" → "Direction"; null if unknown.</summary>
        public static string GameTypeOfCategory(string category)
        {
            switch (category)
            {
                case "Entities": return "Entity";
                case "Items": return "Item";
                case "Grounds": return "Ground";
                case "Unlocks": return "Unlock";
                case "Hats": return "Hat";
                case "Leaderboards": return "Leaderboard";
                case "Directions": return "Direction";
            }
            return null;
        }

        /// <summary>"Entity" → "Entities"; null if the game type has no constant bag.</summary>
        public static string CategoryOfGameType(string gameType)
        {
            switch (gameType)
            {
                case "Entity": return "Entities";
                case "Item": return "Items";
                case "Ground": return "Grounds";
                case "Unlock": return "Unlocks";
                case "Hat": return "Hats";
                case "Leaderboard": return "Leaderboards";
                case "Direction": return "Directions";
            }
            return null;
        }

        static ApiBuiltin B(string py, string sig, string ret, string doc, Action<ApiBuiltin> more = null)
        {
            var b = MakeBuiltin(py, sig, ret, doc, null);
            more?.Invoke(b);
            return b;
        }

        static List<ApiBuiltin> BaseGame() => new List<ApiBuiltin>
        {
            B("harvest", "", "bool", "Harvests the entity under the drone."),
            B("can_harvest", "", "bool", "True if the plant under the drone is fully grown."),
            B("plant", "entity: Entity", "bool", "Plants the entity under the drone."),
            B("move", "direction: Direction", "bool", "Moves the drone one tile in the direction (wraps at the edge)."),
            B("can_move", "direction: Direction", "bool", "True if the drone can move in the direction."),
            B("swap", "direction: Direction", "bool", "Swaps the entity under the drone with the neighbour in the direction."),
            B("till", "", "void", "Tills the ground (grassland <-> soil)."),
            B("get_pos_x", "", "int", "The drone's x position (0 = west edge).", b => b.NonNegative = true),
            B("get_pos_y", "", "int", "The drone's y position (0 = south edge).", b => b.NonNegative = true),
            B("get_world_size", "", "int", "Side length of the farm.", b => { b.NonNegative = true; b.LoopInvariant = true; }),
            B("get_entity_type", "", "Entity?", "The entity under the drone, or null."),
            B("get_ground_type", "", "Ground", "The ground under the drone."),
            B("get_time", "", "float", "Seconds since the start of the game.", b => b.NonNegative = true),
            B("get_tick_count", "", "float", "Ticks performed since the start of execution.", b => b.NonNegative = true),
            B("use_item", "item: Item, n?: int", "bool", "Uses the item n times (default 1)."),
            B("get_water", "", "float", "Water level of the ground under the drone (0..1).", b => b.NonNegative = true),
            B("do_a_flip", "", "void", "Makes the drone do a flip."),
            B("pet_the_piggy", "", "void", "Pets the piggy."),
            B("clear", "", "void", "Clears the farm and moves the drone to (0, 0)."),
            B("change_hat", "hat: Hat", "void", "Changes the drone's hat."),
            B("print", "*values?: any", "void", "Prints the values above the drone and in the output page."),
            B("quick_print", "*values?: any", "void", "Prints the values to the output page only (no delay)."),
            B("num_items", "item: Item", "float", "How many of the item you have.", b => b.NonNegative = true),
            B("get_cost", "thing: Entity|Item|Unlock, level?: int", "map<Item,float>?", "The cost of the thing (for an unlock: of the given level, default the current one), or null at max level."),
            B("get_companion", "", "Companion?", "The companion wanted by the plant under the drone, or null."),
            B("unlock", "unlock: Unlock", "bool", "Unlocks / upgrades the unlock (Auto_Unlock)."),
            B("num_unlocked", "thing: Unlock|Entity|Ground|Item", "int", "How many times the thing was unlocked / upgraded.", b => b.NonNegative = true),
            B("measure", "direction?: Direction", "any", "Measures the entity under / next to the drone (number, Position or null)."),
            B("leaderboard_run", "leaderboard: Leaderboard, file_name: string, speedup: float", "void", "Starts a leaderboard run."),
            B("simulate", "file_name: string, sim_unlocks: any, sim_items: map<Item,float>, sim_globals: map<string,any>, seed: float, speedup: float", "float", "Runs a simulation and returns its duration."),
            B("set_execution_speed", "speed: float", "void", "Limits the execution speed."),
            B("set_world_size", "size: float", "void", "Shrinks the farm (for testing)."),
            B("spawn_drone", "task: func", "Drone?", "Spawns a drone that runs the function; null if no drone is available."),
            B("wait_for", "drone: Drone", "any", "Waits until the drone finishes and returns the function's result."),
            B("has_finished", "drone: Drone", "bool", "True if the drone has finished."),
            B("num_drones", "", "int", "Number of drones on the farm.", b => { b.NonNegative = true; }),
            B("max_drones", "", "int", "Maximum number of drones.", b => { b.NonNegative = true; b.LoopInvariant = true; }),
            B("min", "a: number, b: number, *more?: number", "number", "The smallest argument.", b => { b.NumericLikeArgs = true; b.Pure = true; }),
            B("max", "a: number, b: number, *more?: number", "number", "The largest argument.", b => { b.NumericLikeArgs = true; b.Pure = true; }),
            B("abs", "x: number", "number", "Absolute value.", b => { b.NumericLikeArgs = true; b.Pure = true; b.NonNegative = true; }),
            B("random", "", "float", "A random number in [0, 1).", b => b.NonNegative = true),
        };

        static ApiTable Table
        {
            get
            {
                var t = table;
                if (t != null) return t;
                lock (apiGate)
                {
                    if (table == null) table = new ApiTable(BaseGame());
                    return table;
                }
            }
        }

        /// <summary>All builtins (base game + registered extensions), in declaration order. A read-only snapshot.</summary>
        public static IReadOnlyList<ApiBuiltin> Builtins => Table.List;

        /// <summary>Builtin by canonical Python name; null if unknown.</summary>
        public static ApiBuiltin Find(string pythonName)
        {
            if (pythonName == null) return null;
            return Table.ByPy.TryGetValue(pythonName, out var b) ? b : null;
        }

        /// <summary>
        /// Registers (or replaces) a builtin so every language sees it under its own naming convention.
        /// signature: neutral parameter list, e.g. "host: string, port?: int" or "(host: string)"; "*values: any" for variadic.
        /// returnType: neutral type ("bool", "int", "float", "string", "void", "any", "list&lt;int&gt;", "map&lt;string,int&gt;", "Entity?", ...).
        /// The plugin must also implement the builtin in the game (Python name) — this only declares it to the compilers.
        /// </summary>
        public static ApiBuiltin Register(string pythonName, string signature, string returnType, string doc, string extension = null)
        {
            if (string.IsNullOrEmpty(pythonName)) throw new ArgumentException("name required", nameof(pythonName));
            var b = MakeBuiltin(pythonName, signature, returnType, doc, extension);
            lock (apiGate)
            {
                var list = Table.List.ToList();
                int i = list.FindIndex(x => x.PythonName == pythonName);
                if (i >= 0) list[i] = b; else list.Add(b);
                table = new ApiTable(list);
                Version++;
            }
            LanguageViewsChanged();
            Changed?.Invoke();
            return b;
        }

        /// <summary>Removes a registered builtin (base game builtins cannot be removed). Returns false if not found.</summary>
        public static bool Unregister(string pythonName)
        {
            lock (apiGate)
            {
                var t = Table;
                if (!t.ByPy.TryGetValue(pythonName ?? "", out var b) || b.Extension == null) return false;
                var list = t.List.ToList();
                list.Remove(b);
                table = new ApiTable(list);
                Version++;
            }
            LanguageViewsChanged();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Implemented by language views (Java's GameApi.Defs) to drop caches when the table changes.</summary>
        static partial void LanguageViewsChanged();

        internal static ApiBuiltin MakeBuiltin(string py, string sig, string ret, string doc, string extension)
        {
            var b = new ApiBuiltin { PythonName = py, Doc = doc ?? "", Extension = extension };
            sig = (sig ?? "").Trim();
            if (sig.StartsWith("(") && sig.EndsWith(")")) sig = sig.Substring(1, sig.Length - 2);
            foreach (var raw in SplitTopLevel(sig))
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;
                var ap = new ApiParam();
                if (p.StartsWith("*")) { ap.Variadic = true; p = p.Substring(1); }
                int colon = p.IndexOf(':');
                string name = colon < 0 ? p : p.Substring(0, colon).Trim();
                string type = colon < 0 ? "any" : p.Substring(colon + 1).Trim();
                int eq = type.IndexOf('=');
                if (eq >= 0) { ap.Optional = true; type = type.Substring(0, eq).Trim(); }
                if (name.EndsWith("?")) { ap.Optional = true; name = name.Substring(0, name.Length - 1); }
                ap.Name = name;
                ap.TypeText = type;
                ap.Type = type == "number" ? IrType.Float : IrType.Parse(type);
                b.Params.Add(ap);
            }
            b.ReturnText = string.IsNullOrWhiteSpace(ret) ? "void" : ret.Trim();
            b.ReturnType = b.ReturnText == "number" ? IrType.Float : IrType.Parse(b.ReturnText);
            return b;
        }

        static IEnumerable<string> SplitTopLevel(string s)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<' || s[i] == '(' || s[i] == '[') depth++;
                else if (s[i] == '>' || s[i] == ')' || s[i] == ']') depth--;
                else if (s[i] == ',' && depth == 0) { yield return s.Substring(start, i - start); start = i + 1; }
            }
            if (start < s.Length) yield return s.Substring(start);
        }

        // ------------------------------------------------------------------ naming conventions

        /// <summary>snake_case → camelCase / PascalCase ("do_a_flip" → "doAFlip" / "DoAFlip").</summary>
        public static string ToStyle(string snake, NamingStyle style)
        {
            if (string.IsNullOrEmpty(snake) || style == NamingStyle.Snake) return snake;
            var sb = new StringBuilder();
            bool upper = style == NamingStyle.Pascal;
            foreach (var ch in snake)
            {
                if (ch == '_') { upper = sb.Length > 0 || style == NamingStyle.Pascal; continue; }
                sb.Append(upper ? char.ToUpperInvariant(ch) : ch);
                upper = false;
            }
            return sb.ToString();
        }

        /// <summary>Builtin name in the style; null if not a builtin.</summary>
        public static string ApiName(string pythonName, NamingStyle style) => Find(pythonName) == null ? null : ToStyle(pythonName, style);

        /// <summary>Inverse of ApiName: the builtin whose styled name equals name; null if none.</summary>
        public static string FromApiName(string name, NamingStyle style)
        {
            if (name == null) return null;
            var d = Table.StyledNames(style);
            if (d == null)
            {
                foreach (var b in Builtins)
                    if (ToStyle(b.PythonName, style) == name) return b.PythonName;
                return null;
            }
            return d.TryGetValue(name, out var py) ? py : null;
        }
    }
}
