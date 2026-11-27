using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.CSharp
{
    internal enum CK { Error, Void, Null, Int, Double, Bool, String, Char, Object, Array, List, Set, Map, Game, Delegate, Tuple, KeyValue }

    /// <summary>Compile-time C# type of the JavaFarmer subset. The game has a single number type; int/double exist only here.</summary>
    internal sealed class CType
    {
        public CK Kind;
        public CType Elem;              // Array/List/Set element, Map value, Delegate return (Void for Action), KeyValue value
        public CType Key;               // Map / KeyValue key
        public int Rank = 1;            // Array: 1 (T[]) or 2 (T[,])
        public string Game;             // Entity, Ground, Item, Hat, Unlock, Leaderboard, Direction, Drone, Position, Companion
        public List<CType> Params;      // Delegate parameters, Tuple items
        public bool Nullable;           // Entity? (display only)
        public bool View;               // Dictionary.Keys / Values: enumerable views, not a HashSet / List

        public static readonly CType Error = new CType { Kind = CK.Error };
        public static readonly CType Void = new CType { Kind = CK.Void };
        public static readonly CType Null = new CType { Kind = CK.Null };
        public static readonly CType Int = new CType { Kind = CK.Int };
        public static readonly CType Double = new CType { Kind = CK.Double };
        public static readonly CType Bool = new CType { Kind = CK.Bool };
        public static readonly CType String = new CType { Kind = CK.String };
        public static readonly CType Char = new CType { Kind = CK.Char };
        public static readonly CType Object = new CType { Kind = CK.Object };
        public static readonly CType Action = new CType { Kind = CK.Delegate, Elem = Void, Params = new List<CType>() };

        public static readonly HashSet<string> GameTypeNames = new HashSet<string>
        { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion" };

        public static CType ArrayOf(CType e, int rank = 1) => new CType { Kind = CK.Array, Elem = e, Rank = rank };
        public static CType ListOf(CType e) => new CType { Kind = CK.List, Elem = e };
        public static CType SetOf(CType e) => new CType { Kind = CK.Set, Elem = e };
        public static CType MapOf(CType k, CType v) => new CType { Kind = CK.Map, Key = k, Elem = v };
        public static CType KeyValueOf(CType k, CType v) => new CType { Kind = CK.KeyValue, Key = k, Elem = v };
        public static CType GameType(string name) => new CType { Kind = CK.Game, Game = name };
        public static CType DelegateOf(CType ret, IEnumerable<CType> ps) => new CType { Kind = CK.Delegate, Elem = ret ?? Void, Params = (ps ?? Enumerable.Empty<CType>()).ToList() };
        public static CType TupleOf(IEnumerable<CType> items) => new CType { Kind = CK.Tuple, Params = items.ToList() };

        public bool IsError => Kind == CK.Error;
        public bool IsNumeric => Kind == CK.Int || Kind == CK.Double;
        public bool IsInt => Kind == CK.Int;
        public bool IsGame(string n) => Kind == CK.Game && Game == n;
        /// <summary>Value types: cannot hold null (int, double, bool, char, Direction, tuples, KeyValuePair).</summary>
        public bool IsValueType => Kind == CK.Int || Kind == CK.Double || Kind == CK.Bool || Kind == CK.Char || Kind == CK.Tuple || Kind == CK.KeyValue || (Kind == CK.Game && Game == "Direction");
        public bool IsReference => !IsValueType && Kind != CK.Void && Kind != CK.Error;
        public bool IsCollection => Kind == CK.Array || Kind == CK.List || Kind == CK.Set || Kind == CK.Map;
        public bool IsFunc => Kind == CK.Delegate && Elem != null && Elem.Kind != CK.Void;

        public CType WithNullable(bool n) => new CType { Kind = Kind, Elem = Elem, Key = Key, Rank = Rank, Game = Game, Params = Params, Nullable = n, View = View };
        public static CType ViewOf(CType coll) => new CType { Kind = coll.Kind, Elem = coll.Elem, View = true };

        public bool SameAs(CType o)
        {
            if (o == null) return false;
            if (ReferenceEquals(this, o)) return true;
            if (Kind != o.Kind) return false;
            switch (Kind)
            {
                case CK.Array: return Rank == o.Rank && Elem.SameAs(o.Elem);
                case CK.List: case CK.Set: return Elem.SameAs(o.Elem);
                case CK.Map: case CK.KeyValue: return Key.SameAs(o.Key) && Elem.SameAs(o.Elem);
                case CK.Game: return Game == o.Game;
                case CK.Delegate:
                    if (!Elem.SameAs(o.Elem) || Params.Count != o.Params.Count) return false;
                    for (int i = 0; i < Params.Count; i++) if (!Params[i].SameAs(o.Params[i])) return false;
                    return true;
                case CK.Tuple:
                    if (Params.Count != o.Params.Count) return false;
                    for (int i = 0; i < Params.Count; i++) if (!Params[i].SameAs(o.Params[i])) return false;
                    return true;
            }
            return true;
        }

        /// <summary>The C# spelling used in diagnostics ('int', 'List&lt;int&gt;', 'Func&lt;int&gt;' ...).</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case CK.Error: return "?";
                case CK.Void: return "void";
                case CK.Null: return "<null>";
                case CK.Int: return "int";
                case CK.Double: return "double";
                case CK.Bool: return "bool";
                case CK.String: return "string" + (Nullable ? "?" : "");
                case CK.Char: return "char";
                case CK.Object: return "object" + (Nullable ? "?" : "");
                case CK.Array: return Elem + "[" + new string(',', Rank - 1) + "]";
                case CK.List: return View ? "Dictionary.ValueCollection" : "List<" + Elem + ">";
                case CK.Set: return View ? "Dictionary.KeyCollection" : "HashSet<" + Elem + ">";
                case CK.Map: return "Dictionary<" + Key + ", " + Elem + ">" + (Nullable ? "?" : "");
                case CK.KeyValue: return "KeyValuePair<" + Key + ", " + Elem + ">";
                case CK.Game: return Game + (Nullable ? "?" : "");
                case CK.Tuple: return "(" + string.Join(", ", Params) + ")";
                case CK.Delegate:
                    if (Elem.Kind == CK.Void) return Params.Count == 0 ? "Action" : "Action<" + string.Join(", ", Params) + ">";
                    return "Func<" + string.Join(", ", Params.Concat(new[] { Elem })) + ">";
            }
            return Kind.ToString();
        }

        /// <summary>Implicit conversion (C# 12 rules restricted to the subset).</summary>
        public static bool Assignable(CType from, CType to)
        {
            if (from == null || to == null) return true;
            if (from.IsError || to.IsError) return true;
            if (to.Kind == CK.Void || from.Kind == CK.Void) return false;
            if (from.Kind == CK.Null) return to.IsReference;
            if (to.Kind == CK.Object) return true;
            if (from.View != to.View) return false;
            if (from.Kind == CK.Int && to.Kind == CK.Double) return true;
            if (from.Kind == CK.Tuple && to.Kind == CK.Tuple)
            {
                if (from.Params.Count != to.Params.Count) return false;
                for (int i = 0; i < from.Params.Count; i++) if (!Assignable(from.Params[i], to.Params[i])) return false;
                return true;
            }
            return from.SameAs(to);
        }

        /// <summary>Explicit conversion (casts).</summary>
        public static bool Castable(CType from, CType to)
        {
            if (from.IsError || to.IsError) return true;
            if (Assignable(from, to) || Assignable(to, from)) return true;
            if (from.IsNumeric && to.IsNumeric) return true;
            if (from.Kind == CK.Object) return true;
            return false;
        }

        /// <summary>Neutral IR type of this C# type.</summary>
        public IrType Ir()
        {
            switch (Kind)
            {
                case CK.Int: return IrType.Int;
                case CK.Double: return IrType.Float;
                case CK.Bool: return IrType.Bool;
                case CK.String: case CK.Char: return IrType.String;
                case CK.Object: return IrType.Any;
                case CK.Void: return IrType.Void;
                case CK.Null: return IrType.Null;
                case CK.Array:
                    return Rank == 2 ? IrType.Array(IrType.Array(Elem.Ir())) : IrType.Array(Elem.Ir());
                case CK.List: return IrType.List(Elem.Ir());
                case CK.Set: return IrType.Set(Elem.Ir());
                case CK.Map: return Nullable ? IrType.Map(Key.Ir(), Elem.Ir()).AsNullable() : IrType.Map(Key.Ir(), Elem.Ir());
                case CK.Game: return Nullable ? IrType.Game(Game).AsNullable() : IrType.Game(Game);
                case CK.Delegate: return IrType.Func(Elem.Ir(), Params.Select(p => p.Ir()).ToArray());
                case CK.Tuple: return IrType.Tuple(Params.Select(p => p.Ir()).ToArray());
            }
            return IrType.Unknown;
        }

        /// <summary>C# type of a neutral IR type (registered builtins, other files).</summary>
        public static CType FromIr(IrType t)
        {
            if (t == null) return Object;
            CType r;
            switch (t.Kind)
            {
                case IrTypeKind.Int: return Int;
                case IrTypeKind.Float: return Double;
                case IrTypeKind.Bool: return Bool;
                case IrTypeKind.String: return String;
                case IrTypeKind.Void: return Void;
                case IrTypeKind.List: return ListOf(FromIr(t.Elem));
                case IrTypeKind.Array: return t.Elem != null && t.Elem.Kind == IrTypeKind.Array ? ArrayOf(FromIr(t.Elem.Elem), 2) : ArrayOf(FromIr(t.Elem));
                case IrTypeKind.Set: return SetOf(FromIr(t.Elem));
                case IrTypeKind.Map: r = MapOf(FromIr(t.Key), FromIr(t.Elem)); break;
                case IrTypeKind.Game: r = GameType(t.GameName); break;
                case IrTypeKind.Func: return DelegateOf(FromIr(t.Elem), (t.Items ?? new List<IrType>()).Select(FromIr));
                case IrTypeKind.Tuple: return TupleOf((t.Items ?? new List<IrType>()).Select(FromIr));
                default: return Object;
            }
            return t.Nullable ? r.WithNullable(true) : r;
        }
    }
}
