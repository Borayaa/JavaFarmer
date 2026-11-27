using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    // Syntax tree of the game's Python dialect (PyParser output, PyLowering input). Spans are source offsets.

    internal abstract class PyNode { public Span Span = Span.None; }

    internal abstract class PyExpr : PyNode { }

    internal sealed class PyNameExpr : PyExpr { public string Id; }

    internal sealed class PyNumExpr : PyExpr { public string Text; public bool IsFloat; public long Int; public double Float; }

    /// <summary>Text between the quotes, exactly as written (the game does not process escapes).</summary>
    internal sealed class PyStrExpr : PyExpr { public string Value; public char Quote; }

    internal sealed class PyAttrExpr : PyExpr { public PyExpr Target; public string Name; public Span NameSpan = Span.None; }

    internal sealed class PyCallExpr : PyExpr { public PyExpr Func; public List<PyExpr> Args = new List<PyExpr>(); }

    internal sealed class PyIndexExpr : PyExpr { public PyExpr Target; public PyExpr Index; }

    internal enum PySeqKind { List, Tuple, Set }

    internal sealed class PySeqExpr : PyExpr { public PySeqKind Kind; public List<PyExpr> Items = new List<PyExpr>(); public bool Parens; }

    internal sealed class PyDictExpr : PyExpr { public List<PyExpr> Keys = new List<PyExpr>(); public List<PyExpr> Values = new List<PyExpr>(); }

    /// <summary>"-", "+", "not".</summary>
    internal sealed class PyUnaryExpr : PyExpr { public string Op; public PyExpr Operand; public Span OpSpan = Span.None; }

    /// <summary>"+ - * / // % **" and "and" / "or".</summary>
    internal sealed class PyBinExpr : PyExpr { public string Op; public PyExpr Left, Right; public Span OpSpan = Span.None; }

    /// <summary>a op1 b op2 c ... with op in == != &lt; &gt; &lt;= &gt;= in, "not in".</summary>
    internal sealed class PyCompareExpr : PyExpr
    {
        public List<PyExpr> Operands = new List<PyExpr>();
        public List<string> Ops = new List<string>();
        public List<Span> OpSpans = new List<Span>();
    }

    /// <summary>Body if Cond else Else.</summary>
    internal sealed class PyIfExpr : PyExpr { public PyExpr Body, Cond, Else; public Span IfSpan = Span.None; }

    // ------------------------------------------------------------------ statements

    internal abstract class PyStmt : PyNode
    {
        public List<IrComment> Leading;
        public IrComment Trailing;
        public int BlankLines;
        public int Line;
        public void AddLeading(IrComment c) { (Leading ?? (Leading = new List<IrComment>())).Add(c); }
    }

    internal sealed class PyBlock : PyNode
    {
        public List<PyStmt> Stmts = new List<PyStmt>();
        public List<IrComment> TrailingComments;
        /// <summary>Indentation width of the block's statements.</summary>
        public int Width;
        /// <summary>Start of the first token after the block (comments before it may belong to the block).</summary>
        public int Limit;
        /// <summary>Same-line comment of the header that opened the block ("else:  # c").</summary>
        public IrComment HeaderComment;
    }

    internal sealed class PyExprStmt : PyStmt { public PyExpr Expr; }

    /// <summary>Target = Value, or Target Op Value for augmented assignments (Op "+=" ...).</summary>
    internal sealed class PyAssignStmt : PyStmt { public PyExpr Target; public string Op; public PyExpr Value; public Span OpSpan = Span.None; }

    internal sealed class PyIfStmt : PyStmt
    {
        public PyExpr Cond;
        public PyBlock Body;
        public PyIfStmt Elif;        // elif branch (a PyIfStmt whose KwSpan is the 'elif')
        public PyBlock ElseBody;
        public Span KwSpan = Span.None, ElseSpan = Span.None;
    }

    internal sealed class PyWhileStmt : PyStmt { public PyExpr Cond; public PyBlock Body; public Span KwSpan = Span.None; }

    internal sealed class PyForStmt : PyStmt { public PyExpr Target; public PyExpr Iter; public PyBlock Body; public Span KwSpan = Span.None, HeaderSpan = Span.None; }

    internal sealed class PyParam : PyNode { public string Name; public PyExpr Default; }

    internal sealed class PyDefStmt : PyStmt
    {
        public string Name;
        public Span NameSpan = Span.None, HeaderSpan = Span.None;
        public List<PyParam> Params = new List<PyParam>();
        public PyBlock Body;
    }

    internal sealed class PyReturnStmt : PyStmt { public PyExpr Value; }

    internal enum PyJump { Pass, Break, Continue }

    internal sealed class PyJumpStmt : PyStmt { public PyJump Kind; }

    internal sealed class PyGlobalStmt : PyStmt { public List<string> Names = new List<string>(); public List<Span> NameSpans = new List<Span>(); }

    internal sealed class PyImportStmt : PyStmt { public List<string> Modules = new List<string>(); public List<Span> ModuleSpans = new List<Span>(); }

    internal sealed class PyFromImportStmt : PyStmt
    {
        public string Module;
        public Span ModuleSpan = Span.None;
        public List<string> Names = new List<string>();
        public bool Star;
    }

    internal sealed class PyModuleAst
    {
        public PyBlock Body = new PyBlock();
    }
}
