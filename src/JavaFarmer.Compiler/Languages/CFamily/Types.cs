using System.Collections.Generic;
using System.Text;

namespace JavaFarmer.Compiler.CFamily
{
    internal enum CK { Error, Void, Int, Float, Bool, Str, Char, Null, Handle, Position, Companion, List, Array, Map, Set, Func, Optional, Any, Stream, Endl, Iter }

    /// <summary>Checker type of the C / C++ subset. Ir() gives the neutral IR type.</summary>
    internal sealed class CT
    {
        public CK Kind;
        public string Game;            // Handle: Entity, Item, Ground, Hat, Unlock, Leaderboard, Direction, Drone
        public CT Elem, Key;           // List/Array/Set element, Map value, Optional inner, Func return
        public List<CT> Params;        // Func parameters
        public bool StdString;         // Str: std::string (C++) rather than const char *
        public bool Long;              // Int spelled long
        public bool Single;            // Float spelled float
        public bool Unordered;         // C++ unordered_map / unordered_set
        public bool Lambda;            // C++ closure type (auto f = [&] {...})
        public bool StdFunction;       // C++ std::function (vs function pointer)
        public bool IsLiteralStr;      // a string literal (C++: const char[N])
        public bool CppVector;         // List written as std::vector
        public string IterOf;          // Iter: "begin"/"end" of the receiver
        public object IterRecv;

        public static readonly CT Error = new CT { Kind = CK.Error };
        public static readonly CT Void = new CT { Kind = CK.Void };
        public static readonly CT Int = new CT { Kind = CK.Int };
        public static readonly CT Float = new CT { Kind = CK.Float };
        public static readonly CT Bool = new CT { Kind = CK.Bool };
        public static readonly CT CStr = new CT { Kind = CK.Str };
        public static readonly CT StdStr = new CT { Kind = CK.Str, StdString = true };
        public static readonly CT Char = new CT { Kind = CK.Char };
        public static readonly CT Null = new CT { Kind = CK.Null };
        public static readonly CT Position = new CT { Kind = CK.Position };
        public static readonly CT Companion = new CT { Kind = CK.Companion };
        public static readonly CT Any = new CT { Kind = CK.Any };
        public static readonly CT Stream = new CT { Kind = CK.Stream };
        public static readonly CT Endl = new CT { Kind = CK.Endl };

        public static CT Handle(string game) => new CT { Kind = CK.Handle, Game = game };
        public static CT List(CT e, bool vector = false) => new CT { Kind = CK.List, Elem = e, CppVector = vector };
        public static CT Array(CT e) => new CT { Kind = CK.Array, Elem = e };
        public static CT Set(CT e, bool unordered = false) => new CT { Kind = CK.Set, Elem = e, Unordered = unordered };
        public static CT Map(CT k, CT v, bool unordered = false) => new CT { Kind = CK.Map, Key = k, Elem = v, Unordered = unordered };
        public static CT Func(CT ret, List<CT> ps, bool stdFunction = false, bool lambda = false) => new CT { Kind = CK.Func, Elem = ret, Params = ps ?? new List<CT>(), StdFunction = stdFunction, Lambda = lambda };
        public static CT Optional(CT inner) => new CT { Kind = CK.Optional, Elem = inner };

        public bool IsError => Kind == CK.Error;
        public bool IsNumeric => Kind == CK.Int || Kind == CK.Float;
        public bool IsStr => Kind == CK.Str;
        public bool IsCollection => Kind == CK.List || Kind == CK.Array || Kind == CK.Set || Kind == CK.Map;

        public CT Clone() => (CT)MemberwiseClone();

        /// <summary>Same type for assignment purposes (C: List(int) vs List(double) differ; spelling flags ignored).</summary>
        public bool Same(CT o)
        {
            if (o == null) return false;
            if (ReferenceEquals(this, o)) return true;
            if (Kind != o.Kind) return false;
            switch (Kind)
            {
                case CK.Handle: return Game == o.Game;
                case CK.List: case CK.Array: case CK.Set: case CK.Optional: return Elem.Same(o.Elem);
                case CK.Map: return Key.Same(o.Key) && Elem.Same(o.Elem);
                case CK.Func:
                    if (!Elem.Same(o.Elem) || Params.Count != o.Params.Count) return false;
                    for (int i = 0; i < Params.Count; i++) if (!Params[i].Same(o.Params[i])) return false;
                    return true;
                case CK.Str: return true;
            }
            return true;
        }

        public IrType Ir()
        {
            switch (Kind)
            {
                case CK.Void: return IrType.Void;
                case CK.Int: return IrType.Int;
                case CK.Float: return IrType.Float;
                case CK.Bool: return IrType.Bool;
                case CK.Str: case CK.Char: return IrType.String;
                case CK.Null: return IrType.Null;
                case CK.Handle: return IrType.Game(Game);
                case CK.Position: return IrType.Game("Position");
                case CK.Companion: return IrType.Game("Companion");
                case CK.List: return IrType.List(Elem.Ir());
                case CK.Array: return IrType.Array(Elem.Ir());
                case CK.Set: return IrType.Set(Elem.Ir());
                case CK.Map: return IrType.Map(Key.Ir(), Elem.Ir());
                case CK.Func:
                    {
                        var ps = new IrType[Params.Count];
                        for (int i = 0; i < ps.Length; i++) ps[i] = Params[i].Ir();
                        return IrType.Func(Elem.Ir(), ps);
                    }
                case CK.Optional: return Elem.Ir().AsNullable();
                case CK.Any: return IrType.Any;
            }
            return IrType.Unknown;
        }

        /// <summary>Type spelling in messages (gcc style).</summary>
        public string Show(Dialect d)
        {
            bool cpp = d == Dialect.Cpp;
            switch (Kind)
            {
                case CK.Error: return "<error>";
                case CK.Void: return "void";
                case CK.Int: return Long ? "long int" : "int";
                case CK.Float: return Single ? "float" : "double";
                case CK.Bool: return "bool";
                case CK.Str: return StdString ? "std::string" : cpp ? "const char*" : "const char *";
                case CK.Char: return "char";
                case CK.Null: return cpp ? "std::nullptr_t" : "void *";
                case CK.Handle: return Game;
                case CK.Position: return "Position";
                case CK.Companion: return "Companion";
                case CK.List: return cpp ? "std::vector<" + Elem.Show(d) + ">" : "List(" + Elem.Show(d) + ")";
                case CK.Array: return Elem.Show(d) + "[]";
                case CK.Set: return cpp ? (Unordered ? "std::unordered_set<" : "std::set<") + Elem.Show(d) + ">" : "Set(" + Elem.Show(d) + ")";
                case CK.Map: return cpp ? (Unordered ? "std::unordered_map<" : "std::map<") + Key.Show(d) + ", " + Elem.Show(d) + ">" : "Map(" + Key.Show(d) + ", " + Elem.Show(d) + ")";
                case CK.Func:
                    {
                        var sb = new StringBuilder();
                        if (Lambda) return "lambda";
                        if (StdFunction) sb.Append("std::function<");
                        sb.Append(Elem.Show(d)).Append(StdFunction ? "(" : " (*)(");
                        for (int i = 0; i < Params.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(Params[i].Show(d)); }
                        if (Params.Count == 0 && !cpp) sb.Append("void");
                        sb.Append(')');
                        if (StdFunction) sb.Append('>');
                        return sb.ToString();
                    }
                case CK.Optional: return "std::optional<" + Elem.Show(d) + ">";
                case CK.Any: return cpp ? "farm::Value" : "double";
                case CK.Stream: return "std::ostream";
                case CK.Endl: return "std::endl";
                case CK.Iter: return "iterator";
            }
            return Kind.ToString();
        }

        /// <summary>CT of a neutral IR type (GameApi signatures). Nullable game values: C pointers / C++ optionals.</summary>
        public static CT FromIr(IrType t, Dialect d)
        {
            if (t == null) return Any;
            CT r;
            switch (t.Kind)
            {
                case IrTypeKind.Void: return Void;
                case IrTypeKind.Int: return Int;
                case IrTypeKind.Float: return Float;
                case IrTypeKind.Bool: return Bool;
                case IrTypeKind.String: return d == Dialect.Cpp ? StdStr : CStr;
                case IrTypeKind.Game:
                    if (t.GameName == "Position") r = Position;
                    else if (t.GameName == "Companion") r = Companion;
                    else return Handle(t.GameName);
                    break;
                case IrTypeKind.List: case IrTypeKind.Array: r = List(FromIr(t.Elem, d), d == Dialect.Cpp); break;
                case IrTypeKind.Set: r = Set(FromIr(t.Elem, d)); break;
                case IrTypeKind.Map: r = Map(FromIr(t.Key, d), FromIr(t.Elem, d)); break;
                case IrTypeKind.Func: return Func(t.Elem == null || !t.Elem.IsKnown ? Void : FromIr(t.Elem, d), new List<CT>(), d == Dialect.Cpp);
                default: return Any;
            }
            if (t.Nullable && d == Dialect.Cpp && (r.Kind == CK.Map || r.Kind == CK.List || r.Kind == CK.Set || r.Kind == CK.Companion || r.Kind == CK.Position)) return Optional(r);
            return r;
        }
    }
}
