using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace JavaFarmer.Compiler
{
    // Neutral model of runtime game values for ILanguage.FormatValue. The plugin converts the game's Py* objects:
    //   PyNone/null → null, PyBool → bool, PyNumber → double, PyString → string, PyList → GameList, PySet → GameSet,
    //   PyDict → GameDict, PyTuple → GameTuple, PyDroneHandle → GameDrone, PyFunction → GameFunction,
    //   PyModule → GameModule, game enum constants (Entities.Tree ...) → GameConstant. Anything else: ToString().

    public sealed class GameList { public readonly List<object> Items = new List<object>(); public GameList() { } public GameList(IEnumerable<object> items) { Items.AddRange(items); } }
    public sealed class GameSet { public readonly List<object> Items = new List<object>(); public GameSet() { } public GameSet(IEnumerable<object> items) { Items.AddRange(items); } }
    public sealed class GameDict { public readonly List<KeyValuePair<object, object>> Entries = new List<KeyValuePair<object, object>>(); public GameDict Add(object k, object v) { Entries.Add(new KeyValuePair<object, object>(k, v)); return this; } }
    public sealed class GameTuple { public readonly List<object> Items = new List<object>(); public GameTuple() { } public GameTuple(params object[] items) { Items.AddRange(items); } }
    public sealed class GameDrone { public int Id; public GameDrone(int id) { Id = id; } }
    /// <summary>A function value; PythonName is the emitted name (user function, "__jlambdaN", or builtin).</summary>
    public sealed class GameFunction { public string PythonName; public GameFunction(string n) { PythonName = n; } }
    public sealed class GameModule { public string Name; public GameModule(string n) { Name = n; } }
    /// <summary>A game constant: Category "Entities"/"Items"/.../"Directions", Name "Tree"/"North".</summary>
    public sealed class GameConstant { public string Category, Name; public GameConstant(string c, string n) { Category = c; Name = n; } }

    /// <summary>
    /// Configurable value formatter: languages set the knobs (or override methods) to get their print style.
    /// Defaults = Java style (the v1 plugin ValueFormatter).
    /// </summary>
    public class ValueFormat
    {
        public const int MaxDepth = 100;
        public string NullText = "null", TrueText = "true", FalseText = "false";
        public string ListOpen = "[", ListClose = "]", SetOpen = "[", SetClose = "]", MapOpen = "{", MapClose = "}";
        public string ItemSep = ", ", KeyValueSep = "=";
        /// <summary>Quote strings inside collections (Python style). Top-level strings are never quoted.</summary>
        public bool QuoteNestedStrings;
        public char Quote = '\'';
        /// <summary>Number format (game numbers are doubles): "0.##" like the game.</summary>
        public string NumberFormat = "0.##";
        /// <summary>{0}=x {1}=y</summary>
        public string PositionFormat = "Position[x={0}, y={1}]";
        /// <summary>{0}=entity {1}=formatted position</summary>
        public string CompanionFormat = "Companion[entity={0}, position={1}]";
        /// <summary>{0}=drone id</summary>
        public string DroneFormat = "Drone@{0}";
        public string CycleText = "(this Collection)";
        /// <summary>Python function name → display name (user/builtin name mapping); default returns "lambda" for __jlambda*.</summary>
        public Func<string, string> FunctionName = n => n != null && n.StartsWith("__jlambda") ? "lambda" : n;
        /// <summary>(category, name) → display text; default "Entities.Tree", "North".</summary>
        public Func<string, string, string> ConstantText = (c, n) => c == null || c == "Directions" ? n : c + "." + n;
        /// <summary>Tuples that are not positions/companions: open/close.</summary>
        public string TupleOpen = "[", TupleClose = "]";
        /// <summary>Recognise (x, y) number pairs as Position and (e, (x, y)) as Companion.</summary>
        public bool RecognisePositions = true;

        public string Format(object v) => Format(v, 0, null, false);

        protected virtual string Format(object obj, int depth, HashSet<object> seen, bool nested)
        {
            if (depth > MaxDepth) return "";
            switch (obj)
            {
                case null: return NullText;
                case bool b: return b ? TrueText : FalseText;
                case double d: return FormatNumber(d);
                case float f: return FormatNumber(f);
                case int i: return FormatNumber(i);
                case long l: return FormatNumber(l);
                case string s: return nested && QuoteNestedStrings ? Quote + s + Quote : s;
                case GameDrone dr: return string.Format(CultureInfo.InvariantCulture, DroneFormat, dr.Id);
                case GameFunction fn: return FunctionName?.Invoke(fn.PythonName) ?? fn.PythonName;
                case GameModule m: return m.Name;
                case GameConstant c: return ConstantText?.Invoke(c.Category, c.Name) ?? c.Name;
            }
            if (seen != null && seen.Contains(obj)) return CycleText;
            switch (obj)
            {
                case GameList list: return Join(ListOpen, list.Items, ListClose, depth, Enter(ref seen, obj));
                case GameSet set: return Join(SetOpen, set.Items, SetClose, depth, Enter(ref seen, obj));
                case GameDict dict:
                    {
                        Enter(ref seen, obj);
                        var sb = new StringBuilder(MapOpen);
                        bool first = true;
                        foreach (var kv in dict.Entries)
                        {
                            if (!first) sb.Append(ItemSep);
                            first = false;
                            sb.Append(Format(kv.Key, depth + 1, seen, true)).Append(KeyValueSep).Append(Format(kv.Value, depth + 1, seen, true));
                        }
                        return sb.Append(MapClose).ToString();
                    }
                case GameTuple t: return FormatTuple(t, depth, seen);
            }
            try { return Convert.ToString(obj, CultureInfo.InvariantCulture); }
            catch { return obj.GetType().Name; }
        }

        public virtual string FormatNumber(double d) => d.ToString(NumberFormat, CultureInfo.InvariantCulture);

        static bool IsNum(object o) => o is double || o is int || o is long || o is float;

        protected virtual string FormatTuple(GameTuple t, int depth, HashSet<object> seen)
        {
            var e = t.Items;
            if (RecognisePositions)
            {
                if (e.Count == 2 && IsNum(e[0]) && IsNum(e[1]))
                    return string.Format(CultureInfo.InvariantCulture, PositionFormat, Format(e[0], depth + 1, seen, true), Format(e[1], depth + 1, seen, true));
                if (e.Count == 2 && e[1] is GameTuple p && p.Items.Count == 2 && IsNum(p.Items[0]) && IsNum(p.Items[1]))
                    return string.Format(CultureInfo.InvariantCulture, CompanionFormat, Format(e[0], depth + 1, seen, true), FormatTuple(p, depth + 1, seen));
            }
            return Join(TupleOpen, e, TupleClose, depth, seen);
        }

        string Join(string open, IEnumerable<object> items, string close, int depth, HashSet<object> seen)
        {
            var sb = new StringBuilder(open);
            bool first = true;
            foreach (var item in items)
            {
                if (!first) sb.Append(ItemSep);
                first = false;
                sb.Append(Format(item, depth + 1, seen, true));
            }
            return sb.Append(close).ToString();
        }

        static HashSet<object> Enter(ref HashSet<object> seen, object obj)
        {
            if (seen == null) seen = new HashSet<object>(RefEq.Instance);
            seen.Add(obj);
            return seen;
        }

        sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }
    }
}
