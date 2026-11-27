using System.Collections.Generic;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal abstract class ZNode
    {
        public int Start, End;
        public Span Span => new Span(Start, End);
    }

    // ================================================================== expressions
    internal abstract class ZExpr : ZNode { }

    internal sealed class ZIdent : ZExpr { public string Name; public bool Quoted; }
    internal sealed class ZIntLit : ZExpr { public long Value; public string Text; }
    internal sealed class ZFloatLit : ZExpr { public double Value; public string Text; }
    internal sealed class ZStrLit : ZExpr { public string Value; public bool Multi; }
    internal sealed class ZCharLit : ZExpr { public long Value; }
    internal sealed class ZEnumLit : ZExpr { public string Name; }                       // .North
    internal sealed class ZField : ZExpr { public ZExpr Target; public string Name; public int NameStart, NameEnd; }
    internal sealed class ZDeref : ZExpr { public ZExpr Target; }                        // x.*
    internal sealed class ZUnwrap : ZExpr { public ZExpr Target; }                       // x.?
    internal sealed class ZIndex : ZExpr { public ZExpr Target, Index; }
    internal sealed class ZSliceExpr : ZExpr { public ZExpr Target, From, To; }
    internal sealed class ZCall : ZExpr { public ZExpr Callee; public List<ZExpr> Args = new List<ZExpr>(); public int LParen; }
    internal sealed class ZBuiltinCall : ZExpr { public string Name; public int NameEnd; public List<ZExpr> Args = new List<ZExpr>(); }
    internal sealed class ZBinary : ZExpr { public string Op; public ZExpr Left, Right; public int OpStart, OpEnd; }
    internal sealed class ZUnary : ZExpr { public string Op; public ZExpr Operand; public int OpEnd; }   // - ! & ~ -% try
    internal sealed class ZGrouped : ZExpr { public ZExpr Inner; }
    internal sealed class ZIfExpr : ZExpr { public ZExpr Cond; public ZCapture Capture; public ZExpr Then, Else; public int KwEnd; }
    internal sealed class ZSwitchExpr : ZExpr { public ZSwitch Switch; }
    internal sealed class ZCatchExpr : ZExpr { public ZExpr Left; public ZCapture Capture; public ZExpr Right; public int OpStart, OpEnd; }
    /// <summary>T{...}, .{...}: positional items or named fields.</summary>
    internal sealed class ZInit : ZExpr
    {
        public ZExpr Type;   // null for anonymous .{ }
        public List<ZExpr> Items = new List<ZExpr>();
        public List<(string Name, int NameStart, int NameEnd, ZExpr Value)> Fields;
        public int OpenStart;
    }
    /// <summary>return / break / continue / unreachable used as an expression (orelse return, catch unreachable).</summary>
    internal sealed class ZControlExpr : ZExpr { public string Kw; public ZExpr Value; public string Label; }
    internal sealed class ZUnsupportedExpr : ZExpr { public string What; }
    /// <summary>A block used as an expression: catch {}, prong bodies, defer bodies.</summary>
    internal sealed class ZBlockValue : ZExpr { public ZBlock Block; }

    // types
    internal sealed class ZOptionalType : ZExpr { public ZExpr Child; }
    internal sealed class ZArrayType : ZExpr { public ZExpr Len; public bool Inferred; public ZExpr Child; public bool Sentinel; }
    internal sealed class ZSliceType : ZExpr { public ZExpr Child; public bool Const; public bool Sentinel; }
    internal sealed class ZPointerType : ZExpr { public ZExpr Child; public bool Const; public bool Many; }
    internal sealed class ZErrorUnionType : ZExpr { public ZExpr Error; public ZExpr Payload; }     // !T (Error null) or E!T
    internal sealed class ZFnType : ZExpr { public List<ZParam> Params = new List<ZParam>(); public ZExpr Ret; }
    internal sealed class ZTupleType : ZExpr { public List<ZExpr> Fields = new List<ZExpr>(); }   // struct { T, U }

    internal sealed class ZCapture
    {
        public List<(string Name, int Start, int End, bool Ref)> Names = new List<(string, int, int, bool)>();
        public int Start, End;
    }

    // ================================================================== statements
    internal abstract class ZStmt : ZNode { }

    internal sealed class ZVarDecl : ZStmt
    {
        public bool IsConst, IsPub, IsComptime, IsExtern;
        public string Name; public int NameStart, NameEnd;
        public int KwStart, KwEnd;
        public ZExpr Type, Init;
    }

    internal sealed class ZDestructure : ZStmt
    {
        /// <summary>Targets: a declaration (Decl != null, no init) or an assignable expression.</summary>
        public List<(ZVarDecl Decl, ZExpr Target)> Targets = new List<(ZVarDecl, ZExpr)>();
        public ZExpr Value;
        public int EqStart;
    }

    internal sealed class ZAssign : ZStmt { public ZExpr Target; public string Op; public ZExpr Value; public int OpStart, OpEnd; }
    internal sealed class ZExprStmt : ZStmt { public ZExpr Expr; }
    internal sealed class ZBlock : ZStmt { public List<ZStmt> Stmts = new List<ZStmt>(); public string Label; }
    internal sealed class ZIf : ZStmt { public ZExpr Cond; public ZCapture Capture; public ZStmt Then, Else; public ZCapture ElseCapture; public int KwEnd; }
    internal sealed class ZWhile : ZStmt
    {
        public ZExpr Cond; public ZCapture Capture; public ZStmt Cont; public ZStmt Body, Else; public string Label; public bool Inline; public int KwEnd;
    }
    internal sealed class ZForInput { public ZExpr Expr; public ZExpr RangeFrom, RangeTo; public bool IsRange; public int Start, End; }
    internal sealed class ZFor : ZStmt
    {
        public List<ZForInput> Inputs = new List<ZForInput>(); public ZCapture Capture; public ZStmt Body, Else; public string Label; public bool Inline; public int KwEnd; public int HeaderEnd;
    }
    internal sealed class ZProng
    {
        public List<ZExpr> Items = new List<ZExpr>();
        public List<(ZExpr From, ZExpr To)> Ranges;
        public bool IsElse;
        public ZCapture Capture;
        public ZStmt Body;          // ZBlock, ZExprStmt or ZAssign
        public int Start, End, ArrowEnd;
    }
    internal sealed class ZSwitch : ZStmt { public ZExpr Selector; public List<ZProng> Prongs = new List<ZProng>(); public int KwEnd; public string Label; }
    internal sealed class ZBreak : ZStmt { public string Label; public ZExpr Value; }
    internal sealed class ZContinue : ZStmt { public string Label; }
    internal sealed class ZReturn : ZStmt { public ZExpr Value; }
    internal sealed class ZDefer : ZStmt { public ZStmt Body; public bool IsErr; }
    internal sealed class ZUnsupportedStmt : ZStmt { public string What; }

    // ================================================================== declarations
    internal sealed class ZParam
    {
        public string Name; public int NameStart, NameEnd, Start, End;
        public ZExpr Type; public bool IsComptime, IsAnytype, NoAlias;
    }

    internal sealed class ZFnDecl : ZNode
    {
        public bool IsPub, IsExport, IsExtern, IsInline;
        public string Name; public int NameStart, NameEnd;
        public List<ZParam> Params = new List<ZParam>();
        public ZExpr Ret; public bool RetBang; public int HeaderEnd;
        public ZBlock Body;
        public string CallConv;
    }

    internal sealed class ZTopUnsupported : ZNode { public string What; }

    internal sealed class ZFile
    {
        public List<ZNode> Decls = new List<ZNode>();   // ZFnDecl / ZVarDecl / ZTopUnsupported
        public List<ZToken> Comments = new List<ZToken>();
        /// <summary>How often each identifier occurs as a token not preceded by '.' (AstGen-style "is it referenced").</summary>
        public Dictionary<string, int> IdentCount = new Dictionary<string, int>(System.StringComparer.Ordinal);
    }
}
