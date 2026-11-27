using System.Collections.Generic;

namespace JavaFarmer.Compiler.CSharp
{
    internal abstract class CNode { public int Start, End; }

    internal sealed class CTypeRef : CNode
    {
        public string Name;                 // "int", "List", "Entity", "var", "void", "Action", "Func"
        public List<CTypeRef> Args;         // generic arguments
        public List<int> Ranks = new List<int>();   // array ranks, outermost first: int[] = [1], int[,] = [2], int[][] = [1,1]
        public bool Nullable;               // T?
        public bool IsTuple;                // (int, int)
        public int NameStart, NameEnd;
        public CType Resolved;
        public override string ToString()
        {
            var s = Name;
            if (Args != null) s += "<" + string.Join(", ", Args) + ">";
            if (Nullable) s += "?";
            foreach (var r in Ranks) s += "[" + new string(',', r - 1) + "]";
            return s;
        }
    }

    [System.Flags]
    internal enum CMods { None = 0, Public = 1, Private = 2, Protected = 4, Internal = 8, Static = 16, Const = 32, Readonly = 64, Other = 128 }

    internal sealed class CUsing : CNode { public string Name; public bool IsStatic, IsGlobal; public string Alias; }

    internal sealed class CUnit : CNode
    {
        public List<CUsing> Usings = new List<CUsing>();
        /// <summary>Top-level statements (incl. local functions as CLocalFuncStmt).</summary>
        public List<CStmt> TopLevel = new List<CStmt>();
        public CClass Class;
    }

    internal sealed class CClass : CNode
    {
        public CMods Mods;
        public string Name;
        public int NameStart, NameEnd;
        public int HeaderEnd;                 // end of "class Name"
        public List<CField> Fields = new List<CField>();
        public List<CMethod> Methods = new List<CMethod>();
        /// <summary>Members in source order (CField / CMethod).</summary>
        public List<CNode> Members = new List<CNode>();
    }

    internal sealed class CField : CNode
    {
        public CMods Mods;
        public CTypeRef Type;
        public List<CVarDeclarator> Vars = new List<CVarDeclarator>();
    }

    internal sealed class CMethod : CNode
    {
        public CMods Mods;
        public CTypeRef ReturnType;
        public string Name;
        public int NameStart, NameEnd;
        public List<CParam> Params = new List<CParam>();
        public CBlock Body;
        public CExpr ExprBody;           // "=> expr;"
        public int ArrowStart;
        public int HeaderStart, HeaderEnd;
        public bool IsLocal;             // top-level local function
        public CMethodSym Symbol;
    }

    internal sealed class CParam : CNode
    {
        public CTypeRef Type;
        public string Name;
        public int NameStart, NameEnd;
        public CExpr Default;
    }

    internal sealed class CVarDeclarator : CNode
    {
        public string Name;
        public int NameStart, NameEnd;
        public CExpr Init;
    }

    // ---------------------------------------------------------------- statements
    internal abstract class CStmt : CNode { }
    internal sealed class CBlock : CStmt { public List<CStmt> Stmts = new List<CStmt>(); }
    internal sealed class CEmpty : CStmt { }
    internal sealed class CExprStmt : CStmt { public CExpr E; }
    internal sealed class CLocalVar : CStmt
    {
        public CTypeRef Type;          // Name "var" for var
        public bool IsConst;
        public List<CVarDeclarator> Vars = new List<CVarDeclarator>();
    }
    /// <summary>var (a, b) = expr;  /  (a, b) = expr; (Assign with a tuple target is a CExprStmt of CAssign)</summary>
    internal sealed class CDeconstruct : CStmt
    {
        public CTupleExpr Names;       // nested CTupleExpr / CName (declared names) / CDiscard
        public CExpr Value;
        public int OpStart, OpEnd;
    }
    internal sealed class CLocalFuncStmt : CStmt { public CMethod Method; }
    internal sealed class CIf : CStmt { public CExpr Cond; public CStmt Then, Else; public int KwEnd; }
    internal sealed class CWhile : CStmt { public CExpr Cond; public CStmt Body; public int KwEnd; }
    internal sealed class CDoWhile : CStmt { public CStmt Body; public CExpr Cond; public int WhileStart, WhileEnd; }
    internal sealed class CFor : CStmt
    {
        public List<CStmt> Init = new List<CStmt>();
        public CExpr Cond;
        public List<CExpr> Update = new List<CExpr>();
        public CStmt Body;
        public int KwEnd, HeaderEnd;
    }
    internal sealed class CForEach : CStmt
    {
        public CTypeRef VarType;       // null for deconstruction "var (k, v)"
        public string Name;            // single variable
        public int NameStart, NameEnd;
        public CTupleExpr Deconstruct; // foreach (var (k, v) in dict)
        public CExpr Iterable;
        public CStmt Body;
        public int KwEnd, HeaderEnd;
    }
    internal sealed class CBreak : CStmt { public bool Strip; }
    internal sealed class CContinue : CStmt { }
    internal sealed class CReturn : CStmt { public CExpr Value; }
    internal sealed class CSwitch : CStmt
    {
        public CExpr Selector;
        public List<CSection> Sections = new List<CSection>();
        public int KwEnd;
    }
    internal sealed class CLabel : CNode
    {
        public bool IsDefault;
        public List<CExpr> Values = new List<CExpr>();   // case 1 or 2:
        public int LabelEnd;
    }
    internal sealed class CSection : CNode
    {
        public List<CLabel> Labels = new List<CLabel>();
        public List<CStmt> Body = new List<CStmt>();
    }

    // ---------------------------------------------------------------- expressions
    internal abstract class CExpr : CNode { public CType Type; }

    internal enum CLitKind { Int, Double, Bool, String, Char, Null }
    internal sealed class CLiteral : CExpr
    {
        public CLitKind Kind;
        public long IntValue;
        public double DoubleValue;
        public bool BoolValue;
        public string StrValue;
    }
    internal sealed class CInterp : CExpr
    {
        public List<object> Parts = new List<object>();   // string or CExpr
    }
    internal sealed class CName : CExpr { public string Name; public CTypeRef TypeRef; /* typed deconstruction: (int a, int b) = ... */ }
    internal sealed class CDiscard : CExpr { }
    internal sealed class CMember : CExpr { public CExpr Target; public string Name; public int NameStart, NameEnd; public List<CTypeRef> TypeArgs; }
    internal sealed class CPredefined : CExpr { public string Name; }   // int / double / string / object as a member target (int.MaxValue)
    internal sealed class CCall : CExpr
    {
        public CExpr Callee;           // CName / CMember / other expression
        public List<CArg> Args = new List<CArg>();
        public int LParen;
    }
    internal sealed class CArg : CNode
    {
        public CExpr Value;
        public string Name;            // named argument
        public string Modifier;        // ref / out / in
        public CTypeRef OutType;       // out var x / out int x
        public string OutName;
        public int OutNameStart, OutNameEnd;
    }
    internal sealed class CNew : CExpr
    {
        public CTypeRef TypeRef;       // null: target-typed new()
        public List<CArg> Args = new List<CArg>();
        public bool HasArgs;
        public List<CExpr> Items;      // collection initializer { 1, 2 }
        public List<(CExpr Key, CExpr Value, bool Indexer)> Pairs;   // { {k, v} } / { [k] = v }
        public int InitStart = -1;
    }
    internal sealed class CNewArray : CExpr
    {
        public CTypeRef Elem;          // element type (no ranks); null for new[] { }
        public List<CExpr> Sizes = new List<CExpr>();   // new int[3, 4] → [3, 4]
        public List<int> Ranks = new List<int>();       // rank specifiers after the sizes (int[3][] → [1])
        public int FirstRank;          // rank of the first bracket (int[3, 4] → 2)
        public CArrayInit Init;
    }
    internal sealed class CArrayInit : CExpr { public List<CExpr> Items = new List<CExpr>(); }
    /// <summary>C# 12 collection expression [a, b] (also []).</summary>
    internal sealed class CCollection : CExpr { public List<CExpr> Items = new List<CExpr>(); public bool HasSpread; }
    internal sealed class CIndex : CExpr { public CExpr Target; public List<CExpr> Indices = new List<CExpr>(); }
    internal sealed class CFromEnd : CExpr { public CExpr Operand; }   // ^1
    internal sealed class CUnary : CExpr { public string Op; public CExpr Operand; public int OpEnd; }
    internal sealed class CIncDec : CExpr { public string Op; public bool Prefix; public CExpr Target; public int OpStart, OpEnd; }
    internal sealed class CBinary : CExpr { public string Op; public CExpr Left, Right; public int OpStart, OpEnd; }
    internal sealed class CAssign : CExpr { public string Op; public CExpr Target, Value; public int OpStart, OpEnd; }
    internal sealed class CConditional : CExpr { public CExpr Cond, Then, Else; public int QStart; }
    internal sealed class CCast : CExpr { public CTypeRef TypeRef; public CExpr Operand; public int CloseParen; }
    internal sealed class CParen : CExpr { public CExpr Inner; }
    internal sealed class CTupleExpr : CExpr { public List<CExpr> Items = new List<CExpr>(); public bool Declares; }
    internal sealed class CLambda : CExpr
    {
        public List<(CTypeRef Type, string Name, int Start, int End)> Params = new List<(CTypeRef, string, int, int)>();
        public CExpr BodyExpr;
        public CBlock BodyBlock;
        public int ArrowEnd;
        public bool IsStatic;
    }
    /// <summary>x is null / x is not null / x is 5</summary>
    internal sealed class CIs : CExpr { public CExpr Operand; public bool Not; public CExpr Pattern; public CTypeRef TypePattern; public int KwStart, KwEnd; }
    internal sealed class CSwitchExpr : CExpr
    {
        public CExpr Selector;
        public List<(List<CExpr> Patterns, bool Discard, CExpr Value, int Start, int End)> Arms = new List<(List<CExpr>, bool, CExpr, int, int)>();
        public int KwStart, KwEnd;
    }
    internal sealed class CDefault : CExpr { public CTypeRef TypeRef; }
}
