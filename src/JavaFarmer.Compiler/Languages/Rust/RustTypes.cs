using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    internal enum RK { Var, Int, Float, Bool, Char, Str, String, Unit, Never, Ref, Vec, Slice, Array, Map, Set, Option, Tuple, Game, Drone, Fn, Closure, Range, Iter, Error }

    /// <summary>
    /// Rust type with inference variables (union-find via Link). Var classes: 0 any type, 1 {integer}, 2 {float}.
    /// Constraints name a trait the final type must implement (Costable, Measurement ...), checked when the checker finishes.
    /// </summary>
    internal sealed class RTy
    {
        public RK Kind;
        public string Name;            // Int/Float: i32, f64 ...; Game: Entity ...
        public RTy A, B;               // element / key,value / inner / Drone result / Range element
        public List<RTy> Items;        // tuple items / fn params
        public RTy Ret;                // fn / closure result
        public bool Mut;               // &mut
        public long Len = -1;          // array length when known
        // inference variables
        public RTy Link;
        public int Class;
        public int OriginStart = -1, OriginEnd = -1;
        public string OriginWhat;      // "Vec<_>" for "type annotations needed for `Vec<_>`"
        public List<string> Constraints;
        public int Id;
        // closures / fn items
        public RClosure Closure;
        public RFnSym FnSym;
        public string Builtin;

        static int nextId;

        public static readonly RTy Bool = new RTy { Kind = RK.Bool };
        public static readonly RTy Unit = new RTy { Kind = RK.Unit };
        public static readonly RTy Never = new RTy { Kind = RK.Never };
        public static readonly RTy String = new RTy { Kind = RK.String };
        public static readonly RTy Str = new RTy { Kind = RK.Str };
        public static readonly RTy Error = new RTy { Kind = RK.Error };
        public static readonly RTy Char = new RTy { Kind = RK.Char };
        public static readonly RTy I32 = Int("i32");
        public static readonly RTy USize = Int("usize");
        public static readonly RTy U32 = Int("u32");
        public static readonly RTy F64 = new RTy { Kind = RK.Float, Name = "f64" };
        public static RTy StrRef => Ref(Str, false);

        public static RTy Int(string name) => new RTy { Kind = RK.Int, Name = name };
        public static RTy Float(string name) => name == "f64" ? F64 : new RTy { Kind = RK.Float, Name = name };

        public static RTy NewVar(int cls = 0, int originStart = -1, int originEnd = -1, string what = null) =>
            new RTy { Kind = RK.Var, Class = cls, OriginStart = originStart, OriginEnd = originEnd, OriginWhat = what, Id = ++nextId };
        public static RTy Ref(RTy inner, bool mut) => new RTy { Kind = RK.Ref, A = inner, Mut = mut };
        public static RTy Vec(RTy e) => new RTy { Kind = RK.Vec, A = e };
        public static RTy Slice(RTy e) => new RTy { Kind = RK.Slice, A = e };
        public static RTy Array(RTy e, long len = -1) => new RTy { Kind = RK.Array, A = e, Len = len };
        public static RTy Map(RTy k, RTy v) => new RTy { Kind = RK.Map, A = k, B = v };
        public static RTy Set(RTy e) => new RTy { Kind = RK.Set, A = e };
        public static RTy Option(RTy e) => new RTy { Kind = RK.Option, A = e };
        public static RTy Tuple(params RTy[] items) => new RTy { Kind = RK.Tuple, Items = items.ToList() };
        public static RTy Game(string name) => new RTy { Kind = RK.Game, Name = name };
        public static RTy Drone(RTy result) => new RTy { Kind = RK.Drone, A = result };
        public static RTy Fn(List<RTy> ps, RTy ret) => new RTy { Kind = RK.Fn, Items = ps, Ret = ret };
        public static RTy Range(RTy e) => new RTy { Kind = RK.Range, A = e };
        public static RTy Iter(RTy item) => new RTy { Kind = RK.Iter, A = item };
        public static RTy Position => Tuple(I32, I32);
        public static RTy Companion => Tuple(Game("Entity"), Position);

        public RTy P => Prune(this);

        public static RTy Prune(RTy t)
        {
            while (t != null && t.Kind == RK.Var && t.Link != null) t = t.Link;
            return t;
        }

        public bool IsVar => Prune(this).Kind == RK.Var;
        public bool IsIntLike { get { var t = Prune(this); return t.Kind == RK.Int || (t.Kind == RK.Var && t.Class == 1); } }
        public bool IsFloatLike { get { var t = Prune(this); return t.Kind == RK.Float || (t.Kind == RK.Var && t.Class == 2); } }
        public bool IsNumeric => IsIntLike || IsFloatLike;
        public bool IsUnsigned { get { var t = Prune(this); return t.Kind == RK.Int && t.Name.StartsWith("u"); } }

        /// <summary>Strips every reference.</summary>
        public static RTy Deref(RTy t)
        {
            t = Prune(t);
            while (t != null && t.Kind == RK.Ref) t = Prune(t.A);
            return t;
        }

        public static int RefDepth(RTy t)
        {
            int n = 0;
            t = Prune(t);
            while (t != null && t.Kind == RK.Ref) { n++; t = Prune(t.A); }
            return n;
        }

        /// <summary>Copy types (no move semantics needed; for printing / patterns).</summary>
        public static bool IsCopy(RTy t)
        {
            t = Prune(t);
            switch (t.Kind)
            {
                case RK.Int: case RK.Float: case RK.Bool: case RK.Char: case RK.Game: case RK.Drone: case RK.Unit: case RK.Never: case RK.Fn: return true;
                case RK.Ref: return !t.Mut;
                case RK.Var: return t.Class != 0;
                case RK.Tuple: return t.Items.All(IsCopy);
                case RK.Option: return IsCopy(t.A);
                case RK.Array: return IsCopy(t.A);
            }
            return false;
        }

        public override string ToString() => Show(this);

        public static string Show(RTy t)
        {
            var sb = new StringBuilder();
            Show(t, sb);
            return sb.ToString();
        }

        static void Show(RTy t, StringBuilder sb)
        {
            t = Prune(t);
            if (t == null) { sb.Append('_'); return; }
            switch (t.Kind)
            {
                case RK.Var: sb.Append(t.Class == 1 ? "{integer}" : t.Class == 2 ? "{float}" : "_"); break;
                case RK.Int: case RK.Float: sb.Append(t.Name); break;
                case RK.Bool: sb.Append("bool"); break;
                case RK.Char: sb.Append("char"); break;
                case RK.Str: sb.Append("str"); break;
                case RK.String: sb.Append("String"); break;
                case RK.Unit: sb.Append("()"); break;
                case RK.Never: sb.Append('!'); break;
                case RK.Error: sb.Append("{error}"); break;
                case RK.Ref: sb.Append('&'); if (t.Mut) sb.Append("mut "); Show(t.A, sb); break;
                case RK.Vec: sb.Append("Vec<"); Show(t.A, sb); sb.Append('>'); break;
                case RK.Slice: sb.Append('['); Show(t.A, sb); sb.Append(']'); break;
                case RK.Array: sb.Append('['); Show(t.A, sb); sb.Append("; ").Append(t.Len >= 0 ? t.Len.ToString() : "_").Append(']'); break;
                case RK.Map: sb.Append("HashMap<"); Show(t.A, sb); sb.Append(", "); Show(t.B, sb); sb.Append('>'); break;
                case RK.Set: sb.Append("HashSet<"); Show(t.A, sb); sb.Append('>'); break;
                case RK.Option: sb.Append("Option<"); Show(t.A, sb); sb.Append('>'); break;
                case RK.Tuple:
                    sb.Append('(');
                    for (int i = 0; i < t.Items.Count; i++) { if (i > 0) sb.Append(", "); Show(t.Items[i], sb); }
                    if (t.Items.Count == 1) sb.Append(',');
                    sb.Append(')');
                    break;
                case RK.Game: sb.Append(t.Name); break;
                case RK.Drone: sb.Append("Drone<"); Show(t.A, sb); sb.Append('>'); break;
                case RK.Fn:
                    sb.Append("fn(");
                    for (int i = 0; i < (t.Items?.Count ?? 0); i++) { if (i > 0) sb.Append(", "); Show(t.Items[i], sb); }
                    sb.Append(')');
                    if (t.Ret != null && Prune(t.Ret).Kind != RK.Unit) { sb.Append(" -> "); Show(t.Ret, sb); }
                    if (t.FnSym != null) sb.Append(" {").Append(t.FnSym.Name).Append('}');
                    else if (t.Builtin != null) sb.Append(" {").Append(t.Builtin).Append('}');
                    break;
                case RK.Closure: sb.Append("{closure}"); break;
                case RK.Range: sb.Append("Range<"); Show(t.A, sb); sb.Append('>'); break;
                case RK.Iter: sb.Append("impl Iterator<Item = "); Show(t.A, sb); sb.Append('>'); break;
            }
        }

        /// <summary>The neutral IR type (references dropped, Option = nullable).</summary>
        public static IrType ToIr(RTy t)
        {
            t = Prune(t);
            if (t == null) return IrType.Unknown;
            switch (t.Kind)
            {
                case RK.Var: return t.Class == 1 ? IrType.Int : t.Class == 2 ? IrType.Float : IrType.Unknown;
                case RK.Int: return IrType.Int;
                case RK.Float: return IrType.Float;
                case RK.Bool: return IrType.Bool;
                case RK.Str: case RK.String: case RK.Char: return IrType.String;
                case RK.Unit: case RK.Never: return IrType.Void;
                case RK.Ref: return ToIr(t.A);
                case RK.Vec: case RK.Slice: return IrType.List(ToIr(t.A));
                case RK.Array: return IrType.Array(ToIr(t.A));
                case RK.Map: return IrType.Map(ToIr(t.A), ToIr(t.B));
                case RK.Set: return IrType.Set(ToIr(t.A));
                case RK.Option: return ToIr(t.A).AsNullable();
                case RK.Tuple: return IrType.Tuple(t.Items.Select(ToIr).ToArray());
                case RK.Game: return IrType.Game(t.Name);
                case RK.Drone: return IrType.Game("Drone");
                case RK.Fn: return IrType.Func(ToIr(t.Ret), (t.Items ?? new List<RTy>()).Select(ToIr).ToArray());
                case RK.Closure: return IrType.Func(ToIr(t.Ret), (t.Items ?? new List<RTy>()).Select(ToIr).ToArray());
            }
            return IrType.Unknown;
        }
    }

    internal enum RVarKind { Local, Param, Static, Const, External }

    /// <summary>A variable binding (let / param / pattern binding / static / const). Each let creates a new one (shadowing).</summary>
    internal sealed class RVar
    {
        public string Name;
        public RVarKind Kind;
        public bool Mut;
        public RTy Type;
        public int DeclStart = -1, DeclEnd = -1;
        public int LoopDepth;          // loop nesting at the declaration (assigning a non-mut binding inside a deeper loop = twice)
        public int ScopeLevel;         // index of the scope that declared it (captures)
        public int FnId;               // function / closure that owns the binding
        public string Module;          // External: other file
        public bool Pub;
        public IrSymbol Ir;
        public bool IsGlobal => Kind == RVarKind.Static || Kind == RVarKind.Const || Kind == RVarKind.External;
        public override string ToString() => Name + ":" + Type;
    }

    internal sealed class RFnSym
    {
        public string Name;
        public List<RTy> Params = new List<RTy>();
        public List<string> ParamNames = new List<string>();
        public RTy Ret = RTy.Unit;
        public bool Pub;
        public string Module;          // null = this file
        public string PyName;
        public RFn Decl;
        public IrSymbol Ir;
        public int NameStart = -1, NameEnd = -1;
    }
}
