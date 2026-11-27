// Farm IR: the language-neutral program representation every frontend produces and every printer consumes.
// See docs/IR.md for the semantics of every node. Frontends build it; Core/IrEmitter lowers it to game Python.
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>A source range [Start, End) in the frontend's source text. None = (-1, -1).</summary>
    public struct Span
    {
        public int Start, End;
        public Span(int start, int end) { Start = start; End = end; }
        public static readonly Span None = new Span(-1, -1);
        public bool IsNone => Start < 0;
        public int Length => IsNone ? 0 : End - Start;
        public static Span Cover(Span a, Span b) => a.IsNone ? b : b.IsNone ? a : new Span(System.Math.Min(a.Start, b.Start), System.Math.Max(a.End, b.End));
        public override string ToString() => IsNone ? "(none)" : Start + "-" + End;
    }

    /// <summary>A source comment. Text excludes the markers ("//", "#", "/*", "*/") but keeps inner spacing.</summary>
    public sealed class IrComment
    {
        public string Text;
        public bool IsBlock;     // /* */ style (may span lines)
        public bool IsDoc;       // /** */, ///, roxygen #' ...
        public Span Span = Span.None;
        /// <summary>Layout hint: empty source lines directly before this comment (printers may reproduce them).</summary>
        public int BlankLinesBefore;
        public IrComment() { }
        public IrComment(string text, bool isBlock = false) { Text = text; IsBlock = isBlock; }
        public override string ToString() => (IsBlock ? "/*" + Text + "*/" : "//" + Text);
    }

    public abstract class IrNode
    {
        /// <summary>Whole construct in the source. The emitter maps Python back to it.</summary>
        public Span Span = Span.None;
        /// <summary>Comments on their own lines directly before this node.</summary>
        public List<IrComment> Leading;
        /// <summary>Comment after this node on the same (last) line.</summary>
        public IrComment Trailing;
        /// <summary>
        /// Invented by the frontend (no source construct of its own, e.g. the str() a C printf lowering adds).
        /// The emitter tags this node's own text Synthetic (never charges unlocks). Children keep their own flag.
        /// </summary>
        public bool Synthetic;
        /// <summary>
        /// Layout hint: empty source lines directly before this node (after its Leading comments, which carry their own
        /// IrComment.BlankLinesBefore). Set by frontends that keep layout (Python); printers may reproduce it.
        /// </summary>
        public int BlankLinesBefore;

        public void AddLeading(IrComment c) { (Leading ?? (Leading = new List<IrComment>())).Add(c); }
    }

    // =====================================================================================  symbols

    public enum IrSymbolKind
    {
        Local,           // function local (or script-level local of a top-level script)
        Param,
        Global,          // module-level variable of this module
        Function,        // function of this module
        ExternalGlobal,  // module-level variable of another module (Module set)
        ExternalFunction // function of another module (Module set)
    }

    /// <summary>A declared name. Identity matters: two locals named "i" in different scopes are different symbols.</summary>
    public sealed class IrSymbol
    {
        public string Name;              // source name, as written by the user
        public IrSymbolKind Kind;
        public IrType Type = IrType.Unknown;
        public bool IsConst;             // final / const / immutable let: never reassigned after its initialisation
        public string Module;            // External*: the other module's (file's) name
        public IrFunction Function;      // Function: its declaration
        public List<IrType> ParamTypes;  // External/Function: parameter types when known
        public Span DeclSpan = Span.None;
        public object Tag;               // free for frontends / printers
        /// <summary>Preset Python name (other modules' symbols, whose renaming the emitter cannot see); null = derived.</summary>
        public string PyName;
        public IrSymbol() { }
        public IrSymbol(string name, IrSymbolKind kind, IrType type = null) { Name = name; Kind = kind; Type = type ?? IrType.Unknown; }
        public bool IsModuleLevel => Kind == IrSymbolKind.Global || Kind == IrSymbolKind.Function;
        public bool IsExternal => Kind == IrSymbolKind.ExternalGlobal || Kind == IrSymbolKind.ExternalFunction;
        public override string ToString() => (Module != null ? Module + "." : "") + Name + ":" + Kind;
    }

    // =====================================================================================  module

    public sealed class IrModule : IrNode
    {
        /// <summary>File / window name (= Python module name).</summary>
        public string Name;
        /// <summary>Source order. IrFunction, IrGlobal, or IrStmt (top-level script statement, incl. IrImport).</summary>
        public List<IrNode> Items = new List<IrNode>();
        /// <summary>Comments after the last item.</summary>
        public List<IrComment> TrailingComments;
        /// <summary>Frontend id that produced this module ("java", "python", ...).</summary>
        public string SourceLanguage;

        public IEnumerable<IrFunction> Functions => Items.OfType<IrFunction>();
        public IEnumerable<IrGlobal> Globals => Items.OfType<IrGlobal>();
        public IEnumerable<IrStmt> TopLevel => Items.OfType<IrStmt>();
        /// <summary>The entry function (main), or null.</summary>
        public IrFunction Entry => Items.OfType<IrFunction>().FirstOrDefault(f => f.IsEntry);
        /// <summary>True when the module has top-level statements (a Python / JS / R style script).</summary>
        public bool IsScript => Items.Any(i => i is IrStmt);
    }

    /// <summary>Module-level variable. Init may be null (the emitter then writes the type's default: 0 / False / None).</summary>
    public sealed class IrGlobal : IrNode
    {
        public IrSymbol Symbol;
        public IrExpr Init;
        /// <summary>Init was supplied by the frontend because the source had none (Java default value). Printers may omit it.</summary>
        public bool ImplicitInit;
        public Span NameSpan = Span.None;
        /// <summary>Type was written explicitly in the source (false: var/let/auto/inferred).</summary>
        public bool TypeExplicit = true;
        public bool IsPrivate;
    }

    public sealed class IrFunction : IrNode
    {
        public IrSymbol Symbol;
        public string Name => Symbol?.Name;
        public List<IrParam> Params = new List<IrParam>();
        public IrType ReturnType = IrType.Unknown;
        public IrBlock Body = new IrBlock();
        /// <summary>The program entry point (main). Emitted as "def main():" + __name__ guard, both scaffolding.</summary>
        public bool IsEntry;
        public bool IsPrivate;
        /// <summary>Header span (modifiers .. ')' ). Entry: the whole header is scaffolding.</summary>
        public Span HeaderSpan = Span.None;
        public Span NameSpan = Span.None;
    }

    public sealed class IrParam : IrNode
    {
        public IrSymbol Symbol;
        public IrExpr Default;           // Python "def f(a = False)"; null otherwise
        public Span NameSpan = Span.None;
    }

    // =====================================================================================  statements

    public abstract class IrStmt : IrNode { }

    /// <summary>A statement list with its own scope (braces in C-like languages). Span covers the braces.</summary>
    public sealed class IrBlock : IrStmt
    {
        public List<IrStmt> Stmts = new List<IrStmt>();
        /// <summary>Comments after the last statement, before the closing brace / dedent.</summary>
        public List<IrComment> TrailingComments;
        public IrBlock() { }
        public IrBlock(IEnumerable<IrStmt> stmts) { Stmts.AddRange(stmts); }
    }

    /// <summary>Declares a local. Init null = declared without value (emits nothing; a later assignment initialises it).</summary>
    public sealed class IrVarDecl : IrStmt
    {
        public IrSymbol Symbol;
        public IrExpr Init;
        public Span NameSpan = Span.None;
        public bool TypeExplicit = true;
    }

    /// <summary>
    /// target = value, or a compound assignment "target op= value" when Op is set.
    /// Target: IrName (local/param/global/external global), IrIndex, or IrTupleLit of names (unpacking).
    /// </summary>
    public sealed class IrAssign : IrStmt
    {
        public IrExpr Target;
        public IrBinOp? Op;              // null for plain '='
        public IrExpr Value;
        public Span OpSpan = Span.None;  // the operator token ("=", "+=", "++")
        /// <summary>Result is truncated toward zero to an integer (Java/C/C# "int x; x += 1.5;" or "x /= 2.0").</summary>
        public bool TruncateToInt;
        /// <summary>Printer hint: written as ++ / -- in the source (Op Add/Sub, Value literal 1).</summary>
        public bool IsIncDec;
        public bool Prefix;
    }

    /// <summary>An expression evaluated for its effect (normally a call).</summary>
    public sealed class IrExprStmt : IrStmt
    {
        public IrExpr Expr;
        public IrExprStmt() { }
        public IrExprStmt(IrExpr e) { Expr = e; Span = e?.Span ?? Span.None; }
    }

    public sealed class IrIf : IrStmt
    {
        public IrExpr Cond;
        public IrBlock Then = new IrBlock();
        /// <summary>null, an IrBlock (else), or an IrIf (else if / elif).</summary>
        public IrStmt Else;
        public Span KwSpan = Span.None;  // from the start of 'if' to the end of the keyword
    }

    public sealed class IrWhile : IrStmt
    {
        public IrExpr Cond;
        public IrBlock Body = new IrBlock();
        public Span KwSpan = Span.None;
    }

    public sealed class IrDoWhile : IrStmt
    {
        public IrBlock Body = new IrBlock();
        public IrExpr Cond;
        public Span KwSpan = Span.None;     // 'do'
        public Span WhileSpan = Span.None;  // 'while' keyword at the end
    }

    /// <summary>Infinite loop (Rust loop, R repeat, while(true) is an IrWhile). Leave with break/return.</summary>
    public sealed class IrLoop : IrStmt
    {
        public IrBlock Body = new IrBlock();
        public Span KwSpan = Span.None;
    }

    /// <summary>C-style for (init; cond; update). The emitter turns the counting pattern into range(), else while.</summary>
    public sealed class IrClassicFor : IrStmt
    {
        public List<IrStmt> Init = new List<IrStmt>();     // IrVarDecl / IrAssign / IrExprStmt
        public IrExpr Cond;                                  // null = forever
        public List<IrStmt> Update = new List<IrStmt>();   // IrAssign / IrExprStmt
        public IrBlock Body = new IrBlock();
        public Span KwSpan = Span.None;
        public Span HeaderSpan = Span.None;   // 'for' .. ')'
    }

    /// <summary>
    /// for Var in [Start, End) by Step (Inclusive: [Start, End]). Start null = 0, Step null = 1.
    /// Python range(), Rust a..b / a..=b, R a:b, Zig for (a..b). Step must be a literal when Inclusive.
    /// </summary>
    public sealed class IrRangeFor : IrStmt
    {
        public IrSymbol Var;
        public IrExpr Start, End, Step;
        public bool Inclusive;
        public IrBlock Body = new IrBlock();
        public Span KwSpan = Span.None;
        public Span HeaderSpan = Span.None;
        public Span VarSpan = Span.None;
    }

    /// <summary>
    /// for Var in Iterable. Lists/arrays/sets/strings iterate elements, maps iterate keys (Python semantics).
    /// ValueVar (optional, maps only): also bound to map[key] at the start of each iteration.
    /// </summary>
    public sealed class IrForEach : IrStmt
    {
        public IrSymbol Var;
        public IrSymbol ValueVar;
        public IrExpr Iterable;
        public IrBlock Body = new IrBlock();
        public Span KwSpan = Span.None;
        public Span VarSpan = Span.None;
        public Span ValueVarSpan = Span.None;
    }

    public enum IrSwitchStyle { Colon, Arrow, Match }

    /// <summary>Selector compared with == against constant labels. No fallthrough: each case is a separate branch.</summary>
    public sealed class IrSwitch : IrStmt
    {
        public IrExpr Selector;
        public List<IrCase> Cases = new List<IrCase>();
        public Span KwSpan = Span.None;
        public IrSwitchStyle Style;      // printer hint only
    }

    public sealed class IrCase : IrNode
    {
        public List<IrExpr> Labels = new List<IrExpr>();
        /// <summary>The default branch (labels, if any, are ignored: default matches everything left).</summary>
        public bool IsDefault;
        public IrBlock Body = new IrBlock();
        public Span LabelSpan = Span.None;   // "case X:" / "X =>"
    }

    /// <summary>Leaves the innermost loop (never a switch: frontends drop the break that ends a switch case).</summary>
    public sealed class IrBreak : IrStmt { }
    public sealed class IrContinue : IrStmt { }
    public sealed class IrReturn : IrStmt { public IrExpr Value; }
    /// <summary>Explicit module import (Python "import X"). Other modules used without an import are imported by the emitter.</summary>
    public sealed class IrImport : IrStmt
    {
        public string Module;
        public Span NameSpan = Span.None;
        /// <summary>
        /// Python "from Module import a, b": the imported names (null for a plain "import Module"). The frontend still
        /// binds a / b as External* symbols of Module, so other printers and the emitter can treat them as Module.a.
        /// </summary>
        public List<string> Names;
        /// <summary>Python "from Module import *" (names that are not defined elsewhere bind to Module).</summary>
        public bool ImportAll;
    }

    // =====================================================================================  expressions

    public abstract class IrExpr : IrNode
    {
        /// <summary>Static type (Unknown for dynamic sources until IrTypeInference runs).</summary>
        public IrType Type = IrType.Unknown;
        /// <summary>
        /// Span of the outermost source parentheses written around this expression, if any. The IR has no paren nodes:
        /// printers may use it to keep the user's parentheses; the emitter maps parentheses it needs in Python to it.
        /// </summary>
        public Span ParenSpan = Span.None;
    }

    public enum IrLitKind { Int, Float, Bool, String, Null }

    public sealed class IrLiteral : IrExpr
    {
        public IrLitKind Kind;
        public long Int;
        public double Float;
        public bool Bool;
        public string Str;     // the string's value (escapes already processed)
        public static IrLiteral OfInt(long v) => new IrLiteral { Kind = IrLitKind.Int, Int = v, Type = IrType.Int };
        public static IrLiteral OfFloat(double v) => new IrLiteral { Kind = IrLitKind.Float, Float = v, Type = IrType.Float };
        public static IrLiteral OfBool(bool v) => new IrLiteral { Kind = IrLitKind.Bool, Bool = v, Type = IrType.Bool };
        public static IrLiteral OfString(string v) => new IrLiteral { Kind = IrLitKind.String, Str = v, Type = IrType.String };
        public static IrLiteral Null() => new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null };
    }

    /// <summary>A variable reference (local, param, global, or another module's global: "Module.name").</summary>
    public sealed class IrName : IrExpr
    {
        public IrSymbol Symbol;
        public Span QualifierSpan = Span.None;   // "Utils" in Utils.count
        public Span NameSpan = Span.None;        // "count" in Utils.count
        public IrName() { }
        public IrName(IrSymbol s) { Symbol = s; Type = s?.Type ?? IrType.Unknown; }
    }

    /// <summary>
    /// Game constant. Category: "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards", or "Directions"
    /// (North/East/South/West, emitted bare). Member lists are game data: never hard-code them.
    /// </summary>
    public sealed class IrConst : IrExpr
    {
        public string Category;
        public string Name;
        public IrConst() { }
        public IrConst(string category, string name) { Category = category; Name = name; Type = IrType.Game(GameApi.GameTypeOfCategory(category)); }
    }

    public enum IrBinOp
    {
        Add, Sub, Mul,
        TrueDiv,        // real division (Python /)
        IntDivTrunc,    // integer division truncating toward 0 (Java/C/C#/Rust/Zig int /)
        IntDivFloor,    // integer division rounding down (Python //, R %/%)
        ModTrunc,       // remainder with the sign of the dividend (Java/C/C#/Rust/JS %, also on doubles)
        ModFloor,       // modulo with the sign of the divisor (Python %, R %%)
        Pow,
        Eq, Ne, Lt, Le, Gt, Ge,
        And, Or,        // short-circuit
        Concat          // string concatenation; a non-string operand is converted with str()
    }

    public enum IrUnOp { Neg, Plus, Not }

    public sealed class IrBinary : IrExpr
    {
        public IrBinOp Op;
        public IrExpr Left, Right;
        public Span OpSpan = Span.None;
    }

    public sealed class IrUnary : IrExpr
    {
        public IrUnOp Op;
        public IrExpr Operand;
        public Span OpSpan = Span.None;
    }

    /// <summary>cond ? a : b. Only valid as a whole value (see docs/IR.md: the emitter hoists it into if/else).</summary>
    public sealed class IrConditional : IrExpr
    {
        public IrExpr Cond, Then, Else;
        public Span QSpan = Span.None;     // the '?' (or 'if') token
    }

    public enum IrCallKind
    {
        Builtin,    // game builtin by canonical Python name (Builtin), incl. print/min/max/abs/random/range-free ones
        Function,   // user function: Function symbol (Kind Function or ExternalFunction)
        Value       // call a function value: Callee
    }

    public sealed class IrCall : IrExpr
    {
        public IrCallKind Kind;
        public string Builtin;              // Builtin: canonical snake_case name ("can_harvest", "print")
        public IrSymbol Function;           // Function
        public IrExpr Callee;               // Value
        public List<IrExpr> Args = new List<IrExpr>();
        public Span NameSpan = Span.None;       // the function name token
        public Span QualifierSpan = Span.None;  // "Utils" in Utils.f()
        public static IrCall ToBuiltin(string name, params IrExpr[] args)
        {
            var c = new IrCall { Kind = IrCallKind.Builtin, Builtin = name };
            c.Args.AddRange(args);
            return c;
        }
    }

    /// <summary>Canonical collection / string / math operations (receiver first in Args). See docs/IR.md catalogue.</summary>
    public enum IrOp
    {
        Len,                    // [c]            len(c)              list/array/set/map/string
        IsEmpty,                // [c]            len(c) == 0
        Contains,               // [c, x]         x in c              list/set/string (substring)/map (key)
        ListAppend,             // [l, x]         l.append(x)
        ListInsert,             // [l, i, x]      l.insert(i, x)
        ListPop,                // [l] / [l, i]   l.pop() / l.pop(i)  (value)
        ListSet,                // [l, i, x]      l[i] = x            statement only
        ListIndexOf,            // [l, x]         index or -1
        RemoveValue,            // [c, x]         c.remove(x)         list/set; error if missing (Python)
        RemoveValueIfPresent,   // [c, x]         removes if present  (Java List.remove(Object), Set.remove)
        Clear,                  // [c]            empties list/set/map in place
        SetAdd,                 // [s, x]         s.add(x)
        MapPut,                 // [m, k, v]      m[k] = v            statement only
        MapGetOrNull,           // [m, k]         m[k] or None if missing
        MapGetOrDefault,        // [m, k, d]      m[k] or d if missing
        MapRemove,              // [m, k]         m.pop(k)            error if missing (Python)
        MapRemoveIfPresent,     // [m, k]         m.pop(k) or None if missing (Java Map.remove)
        MapKeys,                // [m]            the keys (as a set); for-each over it iterates m directly
        MapValues,              // [m]            list of the values
        ListCopy,               // [c]            list(c)
        SetCopy,                // [c]            set(c)
        MapCopy,                // [m]            dict(m)
        ArrayNew,               // [n, d]         list of n copies of d
        ArrayNew2,              // [w, h, d]      w lists of h copies of d
        Floor, Ceil, Round, Sqrt, // [x]
        PosX, PosY,             // [p]            p[0], p[1] of a Position (x, y) tuple
        CompanionEntity,        // [c]            c[0] of (entity, (x, y))
        CompanionPos            // [c]            c[1]
    }

    public sealed class IrOpCall : IrExpr
    {
        public IrOp Op;
        public List<IrExpr> Args = new List<IrExpr>();
        public Span NameSpan = Span.None;     // method / function name token (".add", "len")
        public IrOpCall() { }
        public IrOpCall(IrOp op, params IrExpr[] args) { Op = op; Args.AddRange(args); }
    }

    /// <summary>target[index]. BaseOffset 1 for 1-based sources (R): the emitter subtracts it.</summary>
    public sealed class IrIndex : IrExpr
    {
        public IrExpr Target, Index;
        public int BaseOffset;
    }

    /// <summary>List / array literal. IsArray: a fixed-size array (Java int[]{...}, C {..}, Rust [..]).</summary>
    public sealed class IrListLit : IrExpr { public List<IrExpr> Items = new List<IrExpr>(); public bool IsArray; }
    public sealed class IrSetLit : IrExpr { public List<IrExpr> Items = new List<IrExpr>(); }
    public sealed class IrMapLit : IrExpr { public List<IrExpr> Keys = new List<IrExpr>(); public List<IrExpr> Values = new List<IrExpr>(); }
    public sealed class IrTupleLit : IrExpr { public List<IrExpr> Items = new List<IrExpr>(); }

    /// <summary>
    /// Anonymous function / closure (captures enclosing locals by reference, like Python). Body is a block,
    /// or BodyExpr for expression lambdas. ReturnsValue: BodyExpr is returned (Supplier) rather than evaluated (Runnable).
    /// The emitter hoists it to "def __jlambdaN(...):" right before the statement that contains it.
    /// </summary>
    public sealed class IrLambda : IrExpr
    {
        public List<IrParam> Params = new List<IrParam>();
        public IrBlock Body;
        public IrExpr BodyExpr;
        public bool ReturnsValue;
        public IrType ReturnType = IrType.Unknown;
        public Span HeaderSpan = Span.None;   // "() ->" / "|| " / "function()"
    }

    /// <summary>A function used as a value (method reference, function pointer, Python name of a def).</summary>
    public sealed class IrFuncRef : IrExpr
    {
        public IrSymbol Function;      // Function / ExternalFunction
        public string Builtin;         // or a game builtin by canonical name
        public Span QualifierSpan = Span.None;
        public Span NameSpan = Span.None;
    }

    /// <summary>
    /// Conversion. Target Int: truncation toward zero of a float (no-op for ints). Float: no-op. String: str(x).
    /// Any other target (game type, collection): unchecked cast, no code.
    /// </summary>
    public sealed class IrCast : IrExpr
    {
        public IrType Target;
        public IrExpr Operand;
        public Span OpSpan = Span.None;    // "(int)" / "String.valueOf" / "as i32" token(s)
        /// <summary>Static type assertion only (Java "(String) obj", "(Entity) o"): never generates code, even for String.</summary>
        public bool IsTypeAssertion;
    }
}
