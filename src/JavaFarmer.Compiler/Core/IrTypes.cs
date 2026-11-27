using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    public enum IrTypeKind
    {
        Unknown,   // not known (dynamic source before inference)
        Any,       // explicitly anything (Java Object, Python Any)
        Void,
        Null,      // type of the null literal
        Int, Float, Bool, String,
        List,      // growable list (Elem)
        Array,     // fixed-size array (Elem): Java T[], C T[n], Rust [T; n]
        Set,       // (Elem)
        Map,       // (Key -> Elem)
        Tuple,     // (Items)
        Game,      // GameName: Entity, Ground, Item, Hat, Unlock, Leaderboard, Direction, Drone, Position, Companion
        Func       // function value: Params -> Elem (return type; Void for procedures)
    }

    /// <summary>
    /// Language-neutral static type. Immutable by convention: build new instances instead of mutating shared ones.
    /// The game has a single number type; Int vs Float exists for static checking and printing only.
    /// </summary>
    public sealed class IrType
    {
        public IrTypeKind Kind;
        public IrType Elem;            // List/Array/Set element, Map value, Func return
        public IrType Key;             // Map key
        public string GameName;        // Game
        public List<IrType> Items;     // Tuple items / Func parameters
        public bool Nullable;          // may be null/None (game returns: get_entity_type, get_cost, spawn_drone, ...)

        public static readonly IrType Unknown = new IrType { Kind = IrTypeKind.Unknown };
        public static readonly IrType Any = new IrType { Kind = IrTypeKind.Any };
        public static readonly IrType Void = new IrType { Kind = IrTypeKind.Void };
        public static readonly IrType Null = new IrType { Kind = IrTypeKind.Null };
        public static readonly IrType Int = new IrType { Kind = IrTypeKind.Int };
        public static readonly IrType Float = new IrType { Kind = IrTypeKind.Float };
        public static readonly IrType Bool = new IrType { Kind = IrTypeKind.Bool };
        public static readonly IrType String = new IrType { Kind = IrTypeKind.String };

        public static readonly string[] GameTypeNames =
            { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion" };

        public static IrType List(IrType elem) => new IrType { Kind = IrTypeKind.List, Elem = elem ?? Unknown };
        public static IrType Array(IrType elem) => new IrType { Kind = IrTypeKind.Array, Elem = elem ?? Unknown };
        public static IrType Set(IrType elem) => new IrType { Kind = IrTypeKind.Set, Elem = elem ?? Unknown };
        public static IrType Map(IrType key, IrType value) => new IrType { Kind = IrTypeKind.Map, Key = key ?? Unknown, Elem = value ?? Unknown };
        public static IrType Tuple(params IrType[] items) => new IrType { Kind = IrTypeKind.Tuple, Items = items.ToList() };
        public static IrType Game(string name) => new IrType { Kind = IrTypeKind.Game, GameName = name };
        public static IrType Func(IrType ret, params IrType[] ps) => new IrType { Kind = IrTypeKind.Func, Elem = ret ?? Void, Items = ps.ToList() };

        public IrType AsNullable() { if (Nullable || !CanBeNull) return this; var t = Clone(); t.Nullable = true; return t; }
        public IrType NonNull() { if (!Nullable) return this; var t = Clone(); t.Nullable = false; return t; }
        IrType Clone() => new IrType { Kind = Kind, Elem = Elem, Key = Key, GameName = GameName, Items = Items, Nullable = Nullable };

        public bool IsKnown => Kind != IrTypeKind.Unknown;
        public bool IsNumeric => Kind == IrTypeKind.Int || Kind == IrTypeKind.Float;
        public bool IsInt => Kind == IrTypeKind.Int;
        public bool IsString => Kind == IrTypeKind.String;
        public bool IsCollection => Kind == IrTypeKind.List || Kind == IrTypeKind.Array || Kind == IrTypeKind.Set || Kind == IrTypeKind.Map;
        public bool IsListLike => Kind == IrTypeKind.List || Kind == IrTypeKind.Array;
        public bool IsGame(string name) => Kind == IrTypeKind.Game && GameName == name;
        /// <summary>Reference-like types that can hold null in most languages.</summary>
        public bool CanBeNull => !(Kind == IrTypeKind.Int || Kind == IrTypeKind.Float || Kind == IrTypeKind.Bool || Kind == IrTypeKind.Void);

        public bool SameAs(IrType o)
        {
            if (o == null) return false;
            if (ReferenceEquals(this, o)) return true;
            if (Kind != o.Kind) return false;
            switch (Kind)
            {
                case IrTypeKind.List: case IrTypeKind.Array: case IrTypeKind.Set: return Elem.SameAs(o.Elem);
                case IrTypeKind.Map: return Key.SameAs(o.Key) && Elem.SameAs(o.Elem);
                case IrTypeKind.Game: return GameName == o.GameName;
                case IrTypeKind.Tuple:
                    return Items.Count == o.Items.Count && Items.Zip(o.Items, (a, b) => a.SameAs(b)).All(x => x);
                case IrTypeKind.Func:
                    return Elem.SameAs(o.Elem) && (Items?.Count ?? 0) == (o.Items?.Count ?? 0);
            }
            return true;
        }

        /// <summary>
        /// Least common type used by inference: Int+Float = Float, Null+T = nullable T, Unknown+T = T,
        /// equal = itself, collections join element-wise, anything else = Any.
        /// </summary>
        public static IrType Join(IrType a, IrType b)
        {
            if (a == null || a.Kind == IrTypeKind.Unknown) return b ?? Unknown;
            if (b == null || b.Kind == IrTypeKind.Unknown) return a;
            if (a.Kind == IrTypeKind.Null) return b.AsNullable();
            if (b.Kind == IrTypeKind.Null) return a.AsNullable();
            bool nul = a.Nullable || b.Nullable;
            IrType r;
            if (a.SameAs(b)) r = a;
            else if (a.IsNumeric && b.IsNumeric) r = Float;
            else if (a.Kind == b.Kind && (a.Kind == IrTypeKind.List || a.Kind == IrTypeKind.Array || a.Kind == IrTypeKind.Set))
                r = new IrType { Kind = a.Kind, Elem = Join(a.Elem, b.Elem) };
            else if (a.Kind == IrTypeKind.Map && b.Kind == IrTypeKind.Map) r = Map(Join(a.Key, b.Key), Join(a.Elem, b.Elem));
            else if (a.Kind == IrTypeKind.Tuple && b.Kind == IrTypeKind.Tuple && a.Items.Count == b.Items.Count)
                r = Tuple(a.Items.Zip(b.Items, Join).ToArray());
            else if (a.Kind == IrTypeKind.Func && b.Kind == IrTypeKind.Func) r = Func(Join(a.Elem, b.Elem));
            else r = Any;
            return nul ? r.AsNullable() : r;
        }

        /// <summary>Neutral spelling used by GameApi signatures and docs: int, float, bool, string, void, any, list&lt;int&gt;, map&lt;Item,float&gt;, Entity?, func&lt;int&gt; ...</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            switch (Kind)
            {
                case IrTypeKind.Unknown: sb.Append("unknown"); break;
                case IrTypeKind.Any: sb.Append("any"); break;
                case IrTypeKind.Void: sb.Append("void"); break;
                case IrTypeKind.Null: sb.Append("null"); break;
                case IrTypeKind.Int: sb.Append("int"); break;
                case IrTypeKind.Float: sb.Append("float"); break;
                case IrTypeKind.Bool: sb.Append("bool"); break;
                case IrTypeKind.String: sb.Append("string"); break;
                case IrTypeKind.List: sb.Append("list<").Append(Elem).Append('>'); break;
                case IrTypeKind.Array: sb.Append("array<").Append(Elem).Append('>'); break;
                case IrTypeKind.Set: sb.Append("set<").Append(Elem).Append('>'); break;
                case IrTypeKind.Map: sb.Append("map<").Append(Key).Append(',').Append(Elem).Append('>'); break;
                case IrTypeKind.Tuple: sb.Append("tuple<").Append(string.Join(",", Items)).Append('>'); break;
                case IrTypeKind.Game: sb.Append(GameName); break;
                case IrTypeKind.Func:
                    sb.Append("func");
                    if (Items != null && Items.Count > 0) sb.Append('(').Append(string.Join(",", Items)).Append(')');
                    if (Elem != null && Elem.Kind != IrTypeKind.Void) sb.Append('<').Append(Elem).Append('>');
                    break;
            }
            if (Nullable && Kind != IrTypeKind.Null && Kind != IrTypeKind.Any && Kind != IrTypeKind.Unknown) sb.Append('?');
            return sb.ToString();
        }

        /// <summary>
        /// Parses the neutral spelling (see ToString). Unions "Entity|Item" parse as Any (keep the text for display).
        /// Accepts Python-ish aliases: str, double, number, None, dict&lt;&gt;, Optional[...] is not supported. Returns Any on error.
        /// </summary>
        public static IrType Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Unknown;
            int pos = 0;
            var t = ParseUnion(text.Replace(" ", ""), ref pos);
            return t ?? Any;
        }

        static IrType ParseUnion(string s, ref int pos)
        {
            var first = ParseOne(s, ref pos);
            bool union = false;
            bool nullable = false;
            while (pos < s.Length && s[pos] == '|')
            {
                pos++;
                var next = ParseOne(s, ref pos);
                if (next != null && (next.Kind == IrTypeKind.Null || next.Kind == IrTypeKind.Void)) nullable = true;
                else union = true;
            }
            if (first == null) return null;
            if (union) first = Any;
            return nullable ? first.AsNullable() : first;
        }

        static IrType ParseOne(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
            var name = s.Substring(start, pos - start);
            var args = new List<IrType>();
            if (pos < s.Length && s[pos] == '<')
            {
                pos++;
                while (pos < s.Length && s[pos] != '>')
                {
                    var a = ParseUnion(s, ref pos);
                    args.Add(a ?? Any);
                    if (pos < s.Length && s[pos] == ',') pos++;
                    else if (pos < s.Length && s[pos] != '>') return null;
                }
                if (pos < s.Length) pos++;
            }
            IrType Arg(int i) => i < args.Count ? args[i] : Unknown;
            IrType r;
            switch (name)
            {
                case "int": case "long": case "i32": case "i64": r = Int; break;
                case "float": case "double": case "number": case "f64": r = Float; break;
                case "bool": case "boolean": r = Bool; break;
                case "string": case "str": case "String": r = String; break;
                case "void": case "None": r = Void; break;
                case "null": r = Null; break;
                case "any": case "Any": case "object": case "Object": r = Any; break;
                case "list": case "List": r = List(Arg(0)); break;
                case "array": r = Array(Arg(0)); break;
                case "set": case "Set": r = Set(Arg(0)); break;
                case "map": case "dict": case "Map": r = Map(Arg(0), Arg(1)); break;
                case "tuple": r = Tuple(args.ToArray()); break;
                case "func": r = Func(args.Count > 0 ? args[0] : Void); break;
                default:
                    if (System.Array.IndexOf(GameTypeNames, name) >= 0) r = Game(name);
                    else return name.Length == 0 ? null : Any;
                    break;
            }
            if (pos < s.Length && s[pos] == '?') { pos++; r = r.Kind == IrTypeKind.Void ? r : r.AsNullable(); }
            return r;
        }
    }
}
