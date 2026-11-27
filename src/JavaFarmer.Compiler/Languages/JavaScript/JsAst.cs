using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    // JavaScript AST of the JavaFarmer subset (internal to the JS frontend). Spans are source offsets, End exclusive.

    internal abstract class JsNode { public Span Span; public int BlankLines; }
    internal abstract class JsStmt : JsNode { }
    internal abstract class JsExpr : JsNode { public Span Paren = Span.None; }

    // ------------------------------------------------------------------ statements
    internal sealed class JsDeclarator
    {
        public JsExpr Target;            // JsIdent or JsArrayLit pattern (items JsIdent / null holes)
        public JsExpr Init;
        public Span Span;
    }

    internal sealed class JsVarDecl : JsStmt
    {
        public string Kind;              // let / const / var
        public Span KwSpan;
        public List<JsDeclarator> Decls = new List<JsDeclarator>();
        public bool IsExport;
    }

    internal sealed class JsParam
    {
        public string Name;
        public Span NameSpan;
        public JsExpr Default;
        public Span Span;
    }

    internal sealed class JsFunctionDecl : JsStmt
    {
        public string Name;
        public Span NameSpan, HeaderSpan;
        public List<JsParam> Params = new List<JsParam>();
        public JsBlock Body;
        public bool IsExport;
    }

    internal sealed class JsExprStmt : JsStmt { public JsExpr Expr; }
    internal sealed class JsBlock : JsStmt { public List<JsStmt> Stmts = new List<JsStmt>(); }
    internal sealed class JsEmpty : JsStmt { }

    internal sealed class JsIf : JsStmt
    {
        public JsExpr Cond;
        public JsStmt Then, Else;
        public Span KwSpan, ElseSpan = Span.None;
    }

    internal sealed class JsWhile : JsStmt { public JsExpr Cond; public JsStmt Body; public Span KwSpan; }
    internal sealed class JsDoWhile : JsStmt { public JsStmt Body; public JsExpr Cond; public Span KwSpan, WhileSpan; }

    internal sealed class JsFor : JsStmt
    {
        public JsVarDecl InitDecl;
        public List<JsExpr> InitExprs;
        public JsExpr Cond;
        public List<JsExpr> Update = new List<JsExpr>();
        public JsStmt Body;
        public Span KwSpan, HeaderSpan;
    }

    internal sealed class JsForOf : JsStmt
    {
        public bool IsIn;                // for (x in o)
        public string DeclKind;          // let / const / var, or null (assign to an existing name)
        public JsExpr Target;            // JsIdent or JsArrayLit pattern
        public JsExpr Iter;
        public JsStmt Body;
        public Span KwSpan, HeaderSpan;
    }

    internal sealed class JsCase { public JsExpr Test; public List<JsStmt> Body = new List<JsStmt>(); public Span LabelSpan, Span; }

    internal sealed class JsSwitch : JsStmt
    {
        public JsExpr Disc;
        public List<JsCase> Cases = new List<JsCase>();
        public Span KwSpan;
    }

    internal sealed class JsBreak : JsStmt { }
    internal sealed class JsContinue : JsStmt { }
    internal sealed class JsReturn : JsStmt { public JsExpr Value; }

    internal sealed class JsImportName { public string Imported, Local; public Span ImportedSpan, LocalSpan; }

    internal sealed class JsImport : JsStmt
    {
        public string Path;              // "./Utils.js"
        public Span PathSpan;
        public string Namespace;         // import * as X
        public Span NamespaceSpan;
        public List<JsImportName> Names; // import { a, b as c }
        public string Default;           // import X from (rejected)
    }

    internal sealed class JsExportList : JsStmt { public List<JsImportName> Names = new List<JsImportName>(); }

    // ------------------------------------------------------------------ expressions
    internal sealed class JsNum : JsExpr { public double Value; public bool IsFloat; public string Text; }
    internal sealed class JsStr : JsExpr { public string Value; }
    internal sealed class JsTemplate : JsExpr { public List<string> Parts = new List<string>(); public List<Span> PartSpans = new List<Span>(); public List<JsExpr> Exprs = new List<JsExpr>(); }
    internal sealed class JsIdent : JsExpr { public string Name; }
    internal sealed class JsBool : JsExpr { public bool Value; }
    internal sealed class JsNull : JsExpr { }
    internal sealed class JsArrayLit : JsExpr { public List<JsExpr> Items = new List<JsExpr>(); }
    internal sealed class JsSpread : JsExpr { public JsExpr Arg; }

    internal sealed class JsProp
    {
        public string Key;               // non-computed key (identifier, string or number text)
        public JsExpr KeyExpr;           // computed [k]
        public JsExpr Value;
        public Span KeySpan, Span;
    }

    internal sealed class JsObjectLit : JsExpr { public List<JsProp> Props = new List<JsProp>(); }

    internal sealed class JsFunctionExpr : JsExpr
    {
        public string Name;              // function name() {} (optional)
        public List<JsParam> Params = new List<JsParam>();
        public JsBlock Body;
        public JsExpr ExprBody;          // arrow with an expression body
        public bool IsArrow;
        public Span HeaderSpan;
    }

    internal sealed class JsUnary : JsExpr { public string Op; public JsExpr Operand; public Span OpSpan; }
    internal sealed class JsUpdate : JsExpr { public string Op; public bool Prefix; public JsExpr Target; public Span OpSpan; }
    internal sealed class JsBinary : JsExpr { public string Op; public JsExpr Left, Right; public Span OpSpan; }
    internal sealed class JsAssign : JsExpr { public string Op; public JsExpr Target, Value; public Span OpSpan; }
    internal sealed class JsCond : JsExpr { public JsExpr Test, Then, Else; public Span QSpan; }
    internal sealed class JsCall : JsExpr { public JsExpr Callee; public List<JsExpr> Args = new List<JsExpr>(); public Span ArgsSpan; }
    internal sealed class JsNew : JsExpr { public JsExpr Callee; public List<JsExpr> Args = new List<JsExpr>(); public bool HasArgs; public Span KwSpan; }
    internal sealed class JsMember : JsExpr { public JsExpr Object; public string Name; public Span NameSpan; }
    internal sealed class JsIndex : JsExpr { public JsExpr Object, Index; }

    /// <summary>A syntax error with V8's wording, raised at the first problem.</summary>
    internal sealed class JsSyntaxError : System.Exception
    {
        public readonly Span Span;
        public JsSyntaxError(string msg, Span span) : base(msg) { Span = span; }
    }
}
