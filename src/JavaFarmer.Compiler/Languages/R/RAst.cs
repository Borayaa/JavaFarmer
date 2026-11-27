using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    // R syntax tree (private to the R frontend). In R everything is an expression; statements are expressions in a
    // statement list (top level or braces).

    internal abstract class RlExpr
    {
        public Span Span;
        /// <summary>Outermost parentheses written around this expression (RlParen is unwrapped by the lowering).</summary>
        public Span ParenSpan = Span.None;
    }

    internal enum RlConstKind { True, False, Null, NA, Inf, NaN }

    internal sealed class RlNum : RlExpr
    {
        public double Value;
        /// <summary>Written as an integer (no '.', no exponent) or with the L suffix.</summary>
        public bool LooksInt;
        public bool HasL;
        public string Text;
    }

    internal sealed class RlStr : RlExpr { public string Value; public bool Raw; }
    internal sealed class RlConst : RlExpr { public RlConstKind Kind; public string Text; }
    internal sealed class RlIdent : RlExpr { public string Name; public bool Backtick; }

    internal sealed class RlArg
    {
        public string Name;          // null = positional
        public Span NameSpan = Span.None;
        public RlExpr Value;          // null = empty argument ("a = ," or "f(, 1)")
        public Span Span;
    }

    internal sealed class RlCall : RlExpr
    {
        public RlExpr Fn;
        public List<RlArg> Args = new List<RlArg>();
        public Span OpenSpan, CloseSpan;
        public string FnName => (Fn as RlIdent)?.Name;
    }

    internal sealed class RlIndex : RlExpr
    {
        public RlExpr Target;
        public List<RlArg> Args = new List<RlArg>();
        public bool Double;          // [[ ]]
        public Span OpenSpan;        // "[" or "[["
    }

    internal sealed class RlDollar : RlExpr
    {
        public RlExpr Target;
        public string Name;
        public Span NameSpan;
        public Span OpSpan;
        public bool At;              // @ (slot)
    }

    internal sealed class RlNamespace : RlExpr { public string Pkg, Name; public Span OpSpan; }

    internal sealed class RlBinary : RlExpr
    {
        public string Op;            // "+", "%%", "%in%", ":", "==", "&&", "|>", "~" ...
        public RlExpr Left, Right;
        public Span OpSpan;
    }

    internal sealed class RlUnary : RlExpr { public string Op; public RlExpr Operand; public Span OpSpan; }

    internal sealed class RlAssign : RlExpr
    {
        public string Op;            // "<-", "<<-", "=", "->", "->>" (rightwards are normalised: Target is the name side)
        public RlExpr Target, Value;
        public Span OpSpan;
        public bool Super => Op == "<<-" || Op == "->>";
    }

    internal sealed class RlParam
    {
        public string Name;
        public Span Span;
        public RlExpr Default;
        public bool Dots;
    }

    internal sealed class RlFunction : RlExpr
    {
        public List<RlParam> Params = new List<RlParam>();
        public RlExpr Body;
        public Span HeaderSpan;      // "function(...)" / "\(...)"
        public bool Backslash;
    }

    internal sealed class RlBlock : RlExpr
    {
        public List<RlExpr> Exprs = new List<RlExpr>();
        public Span OpenSpan, CloseSpan;
    }

    internal sealed class RlIf : RlExpr
    {
        public RlExpr Cond, Then, Else;
        public Span KwSpan, ElseSpan = Span.None;
    }

    internal sealed class RlFor : RlExpr
    {
        public string Var;
        public Span VarSpan, KwSpan, HeaderSpan;
        public RlExpr Seq, Body;
    }

    internal sealed class RlWhile : RlExpr { public RlExpr Cond, Body; public Span KwSpan; }
    internal sealed class RlRepeat : RlExpr { public RlExpr Body; public Span KwSpan; }
    internal sealed class RlBreak : RlExpr { }
    internal sealed class RlNext : RlExpr { }
    internal sealed class RlParen : RlExpr { public RlExpr Inner; }

    internal sealed class RlProgram
    {
        public List<RlExpr> Exprs = new List<RlExpr>();
        public List<IrComment> Comments = new List<IrComment>();
        public string Source;
    }

    internal sealed class RlSyntaxError : System.Exception
    {
        public Span Span;
        public RlSyntaxError(string message, Span span) : base(message) { Span = span; }
    }
}
