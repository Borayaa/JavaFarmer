using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    internal enum TK { Error, Void, Null, Int, Double, Boolean, String, Object, Array, List, Set, Map, Collection, Game, Runnable, Supplier }

    /// <summary>Compile-time Java type. The game has a single number type; int/double exist only here.</summary>
    internal sealed class JType
    {
        public TK Kind;
        public bool Boxed;          // Integer / Double / Boolean
        public JType Elem;          // Array/List/Set/Collection element, Map value, Supplier result
        public JType Key;           // Map key
        public string Game;         // Entity, Ground, Item, Hat, Unlock, Leaderboard, Direction, Drone, Position, Companion
        public string Display;      // "ArrayList" vs "List" etc. (display only)

        public static readonly JType Error = new JType { Kind = TK.Error };
        public static readonly JType Void = new JType { Kind = TK.Void };
        public static readonly JType Null = new JType { Kind = TK.Null };
        public static readonly JType Int = new JType { Kind = TK.Int };
        public static readonly JType Double = new JType { Kind = TK.Double };
        public static readonly JType Boolean = new JType { Kind = TK.Boolean };
        public static readonly JType Integer = new JType { Kind = TK.Int, Boxed = true };
        public static readonly JType DoubleBox = new JType { Kind = TK.Double, Boxed = true };
        public static readonly JType BooleanBox = new JType { Kind = TK.Boolean, Boxed = true };
        public static readonly JType String = new JType { Kind = TK.String };
        public static readonly JType Object = new JType { Kind = TK.Object };
        public static readonly JType Runnable = new JType { Kind = TK.Runnable };

        public static readonly HashSet<string> GameTypeNames = new HashSet<string>
        { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion" };

        public static JType ArrayOf(JType e) => new JType { Kind = TK.Array, Elem = e };
        public static JType ListOf(JType e, string display = null) => new JType { Kind = TK.List, Elem = e, Display = display };
        public static JType SetOf(JType e, string display = null) => new JType { Kind = TK.Set, Elem = e, Display = display };
        public static JType MapOf(JType k, JType v, string display = null) => new JType { Kind = TK.Map, Key = k, Elem = v, Display = display };
        public static JType CollectionOf(JType e) => new JType { Kind = TK.Collection, Elem = e };
        public static JType SupplierOf(JType e) => new JType { Kind = TK.Supplier, Elem = e };
        public static JType GameType(string name) => new JType { Kind = TK.Game, Game = name };

        public bool IsError => Kind == TK.Error;
        public bool IsNumeric => Kind == TK.Int || Kind == TK.Double;
        public bool IsPrimitive => !Boxed && (Kind == TK.Int || Kind == TK.Double || Kind == TK.Boolean);
        public bool IsReference => !IsPrimitive && Kind != TK.Void;
        public bool IsIntLike => Kind == TK.Int;
        public bool IsBool => Kind == TK.Boolean;
        public bool IsGame(string name) => Kind == TK.Game && Game == name;
        public bool IsIterable => Kind == TK.Array || Kind == TK.List || Kind == TK.Set || Kind == TK.Collection;

        public JType Unboxed => !Boxed ? this : Kind == TK.Int ? Int : Kind == TK.Double ? Double : Boolean;
        public JType BoxedVersion => IsPrimitive ? (Kind == TK.Int ? Integer : Kind == TK.Double ? DoubleBox : BooleanBox) : this;

        public bool SameAs(JType o)
        {
            if (o == null) return false;
            if (ReferenceEquals(this, o)) return true;
            if (Kind != o.Kind || Boxed != o.Boxed) return false;
            switch (Kind)
            {
                case TK.Array: case TK.List: case TK.Set: case TK.Collection: case TK.Supplier: return Elem.SameAs(o.Elem);
                case TK.Map: return Key.SameAs(o.Key) && Elem.SameAs(o.Elem);
                case TK.Game: return Game == o.Game;
            }
            return true;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case TK.Error: return "<error>";
                case TK.Void: return "void";
                case TK.Null: return "<null>";
                case TK.Int: return Boxed ? "Integer" : "int";
                case TK.Double: return Boxed ? "Double" : "double";
                case TK.Boolean: return Boxed ? "Boolean" : "boolean";
                case TK.String: return "String";
                case TK.Object: return "Object";
                case TK.Array: return Elem + "[]";
                case TK.List: return (Display ?? "List") + "<" + Elem + ">";
                case TK.Set: return (Display ?? "Set") + "<" + Elem + ">";
                case TK.Collection: return "Collection<" + Elem + ">";
                case TK.Map: return (Display ?? "Map") + "<" + Key + "," + Elem + ">";
                case TK.Game: return Game;
                case TK.Runnable: return "Runnable";
                case TK.Supplier: return "Supplier<" + Elem + ">";
            }
            return Kind.ToString();
        }

        /// <summary>Assignment conversion (JLS 5.2, simplified).</summary>
        public static bool Assignable(JType from, JType to)
        {
            if (from == null || to == null) return true;
            if (from.IsError || to.IsError) return true;
            if (to.Kind == TK.Void || from.Kind == TK.Void) return false;
            if (from.Kind == TK.Null) return to.IsReference;
            if (to.Kind == TK.Object && !to.Boxed) return true;          // everything boxes to Object
            if (from.IsPrimitive && to.IsPrimitive)
            {
                if (from.Kind == to.Kind) return true;
                return from.Kind == TK.Int && to.Kind == TK.Double;       // widening
            }
            if (from.IsPrimitive && to.Boxed) return from.Kind == to.Kind; // boxing (no widening+boxing)
            if (from.Boxed && to.IsPrimitive)                              // unboxing (+ widening)
                return from.Kind == to.Kind || (from.Kind == TK.Int && to.Kind == TK.Double);
            if (to.Kind == TK.Collection && (from.Kind == TK.List || from.Kind == TK.Set || from.Kind == TK.Collection))
                return from.Elem.SameAs(to.Elem);
            return from.SameAs(to);
        }

        /// <summary>Casting conversion (simplified).</summary>
        public static bool Castable(JType from, JType to)
        {
            if (from.IsError || to.IsError) return true;
            if (Assignable(from, to) || Assignable(to, from)) return true;
            if (from.IsNumeric && to.IsNumeric && (from.IsPrimitive || to.IsPrimitive)) return true;
            if (from.Kind == TK.Object) return true;                      // downcast / unboxing cast from Object
            return false;
        }

    }
}
