using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    // Rust AST (private to the Rust frontend). Offsets are source offsets, End exclusive.
    internal abstract class RNode { public int Start, End; }

    internal sealed class RFile : RNode
    {
        public List<RItem> Items = new List<RItem>();
        public List<RToken> Comments = new List<RToken>();
    }

    internal abstract class RItem : RNode { public bool Pub; public int PubStart = -1; }

    internal sealed class RFn : RItem
    {
        public string Name; public int NameStart, NameEnd, HeaderEnd;
        public List<RParam> Params = new List<RParam>();
        public RTypeRef Ret;
        public RBlock Body;
        public RFnSym Sym;
    }

    internal sealed class RParam : RNode { public RPat Pat; public RTypeRef Type; }

    /// <summary>const / static / static mut</summary>
    internal sealed class RConst : RItem
    {
        public bool IsStatic, Mut;
        public string Name; public int NameStart, NameEnd;
        public RTypeRef Type;
        public RExpr Init;
        public RVar Var;
    }

    internal sealed class RUse : RItem
    {
        public List<string> Path = new List<string>();
        public bool Glob;
        /// <summary>use a::{B, C}: the names in braces (null when none).</summary>
        public List<(string Name, int Start, int End)> Group;
        public int PathEnd;
    }

    internal sealed class RMod : RItem { public string Name; public int NameStart, NameEnd; }

    // ------------------------------------------------------------------ types
    internal enum RTypeKind { Path, Ref, Tuple, Array, Slice, Infer, Never, Fn, ImplTrait }

    internal sealed class RTypeRef : RNode
    {
        public RTypeKind Kind;
        public List<string> Path;                // Path: segments ("std", "collections", "HashMap")
        public List<RTypeRef> Args;              // generic args of the last segment / tuple items / fn params
        public RTypeRef Elem;                    // Ref / Array / Slice element, Fn return
        public bool Mut;                         // &mut
        public RExpr Len;                        // [T; N]
        public string Lifetime;
        public string Name => Path != null && Path.Count > 0 ? Path[Path.Count - 1] : null;
    }

    // ------------------------------------------------------------------ patterns
    internal enum RPatKind { Ident, Wild, Tuple, Lit, Path, TupleStruct, Or, Ref, Range, Rest }

    internal sealed class RPat : RNode
    {
        public RPatKind Kind;
        public string Name; public bool Mut, ByRef;    // Ident
        public List<RPat> Items;                       // Tuple / TupleStruct / Or
        public RExpr Lit;                              // Lit / Range start
        public RExpr LitEnd;                           // Range end
        public List<string> Path; public int PathEnd;  // Path / TupleStruct
        public RPat Inner;                             // Ref
        public RVar Var;                               // Ident: the binding
        public RTy Type;                               // checker
    }

    // ------------------------------------------------------------------ statements
    internal abstract class RStmt : RNode { }
    internal sealed class RLet : RStmt { public RPat Pat; public RTypeRef Type; public RExpr Init; public RBlock Else; }
    internal sealed class RExprStmt : RStmt { public RExpr E; public bool Semi; }
    internal sealed class RItemStmt : RStmt { public RItem Item; }
    internal sealed class REmptyStmt : RStmt { }

    internal sealed class RBlock : RNode
    {
        public List<RStmt> Stmts = new List<RStmt>();
        /// <summary>Final expression without ';' (the block's value), or null.</summary>
        public RExpr Tail;
        public bool Unsafe;
    }

    // ------------------------------------------------------------------ expressions
    internal abstract class RExpr : RNode
    {
        public RTy Type;          // set by the checker
        public int ParenStart = -1, ParenEnd = -1;
    }

    internal enum RLitKind { Int, Float, Bool, Str, Char }
    internal sealed class RLit : RExpr { public RLitKind Kind; public ulong I; public double F; public bool B; public string S; public string Suffix; public RToken Tok; }

    internal sealed class RPathSeg { public string Name; public int Start, End; public List<RTypeRef> Generics; public bool Raw; }
    internal sealed class RPathExpr : RExpr
    {
        public List<RPathSeg> Segs = new List<RPathSeg>();
        public string Last => Segs[Segs.Count - 1].Name;
        // resolution (checker)
        public RVar Var; public RFnSym Fn; public string Builtin; public string ConstCategory, ConstName; public string Module;
        public bool IsNone;
    }

    internal sealed class RUnary : RExpr { public string Op; public int OpEnd; public RExpr Operand; }
    internal sealed class RBinary : RExpr { public string Op; public int OpStart, OpEnd; public RExpr Left, Right; }
    internal sealed class RAssign : RExpr { public string Op; public int OpStart, OpEnd; public RExpr Target, Value; }
    internal sealed class RCast : RExpr { public RExpr Operand; public RTypeRef To; public int AsStart; }
    internal sealed class RCall : RExpr
    {
        public RExpr Callee; public List<RExpr> Args = new List<RExpr>(); public int ParenStart2;
        // checker
        public string Special;            // "builtin", "fn", "value", "some", "vec.new", "map.new", "set.new", "map.from", "set.from", "vec.from", "string.new", "string.from", "min", "max"
        public RustApi.Entry Api;
        public RFnSym FnSym;
        public List<RExpr> SpreadArgs;    // variadic builtin: the arguments after spreading &[...]
    }
    internal sealed class RMethodCall : RExpr
    {
        public RExpr Recv; public string Name; public int NameStart, NameEnd; public List<RTypeRef> Turbofish;
        public List<RExpr> Args = new List<RExpr>();
        public string Resolved;   // checker: canonical operation ("vec.push", "opt.unwrap", ...)
    }
    internal sealed class RField : RExpr { public RExpr Recv; public string Name; public int NameStart, NameEnd; public int TupleIndex = -1; }
    internal sealed class RIndex : RExpr { public RExpr Target, Index; }
    internal sealed class RRange : RExpr { public RExpr From, To; public bool Inclusive; public int OpStart, OpEnd; }
    internal sealed class RTuple : RExpr { public List<RExpr> Items = new List<RExpr>(); }
    internal sealed class RArray : RExpr { public List<RExpr> Items = new List<RExpr>(); public RExpr RepeatValue, RepeatCount; public bool IsVecMacro; }
    internal sealed class RBlockExpr : RExpr { public RBlock Block; }
    internal sealed class RIf : RExpr
    {
        public RExpr Cond; public RPat LetPat; public RExpr LetExpr;   // "if let P = e"
        public RBlock Then; public RExpr Else;                           // Else: RIf or RBlockExpr
        public int KwEnd;
    }
    internal sealed class RArm : RNode { public RPat Pat; public RExpr Guard; public RExpr Body; public int ArrowEnd; }
    internal sealed class RMatch : RExpr { public RExpr Scrutinee; public List<RArm> Arms = new List<RArm>(); public int KwEnd; }
    internal sealed class RWhile : RExpr { public RExpr Cond; public RPat LetPat; public RExpr LetExpr; public RBlock Body; public int KwEnd; public string Label; }
    internal sealed class RLoop : RExpr { public RBlock Body; public int KwEnd; }
    internal sealed class RFor : RExpr
    {
        public RPat Pat; public RExpr Iter; public RBlock Body; public int KwEnd, HeaderEnd;
        // checker: "range", "elems" (list/array/set/string), "map" (key, value), "keys", "values", "enumerate"
        public string Shape;
        public RExpr Source;          // the collection / range being iterated (after stripping .iter(), &, ...)
        public RRange Range; public bool Rev; public RExpr Step;
    }
    internal sealed class RBreak : RExpr { public RExpr Value; public string Label; }
    internal sealed class RContinue : RExpr { public string Label; }
    internal sealed class RReturn : RExpr { public RExpr Value; }
    internal sealed class RClosure : RExpr
    {
        public bool Move; public List<RParam> Params = new List<RParam>(); public RTypeRef Ret; public RExpr Body;
        public int HeaderEnd;
        public List<RVar> Captures = new List<RVar>();
    }
    internal sealed class RMacro : RExpr
    {
        public string Name; public int NameStart, NameEnd;
        public List<RExpr> Args = new List<RExpr>();
        public RLit Format;                 // println!/format!/...: the format string
        public List<RFmtPiece> Pieces;      // parsed format string
        public int BangEnd;
    }
    internal sealed class RFmtPiece
    {
        public string Text;        // literal text (null for a placeholder)
        public RExpr Arg;          // placeholder argument (positional or inline name)
        public bool Debug;         // {:?}
        public string Spec;        // the text after ':' ("?", ".2")
        public int Start, End;     // span of the placeholder in the source (inside the string literal)
    }
}
