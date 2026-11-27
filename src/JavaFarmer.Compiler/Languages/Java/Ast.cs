using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    // ---------------------------------------------------------------- base
    internal abstract class Node
    {
        public int Start, End;   // Java span [Start, End)
    }

    internal sealed class TypeRef : Node
    {
        public string Name;               // "int", "List", "String", "var", "void"
        public List<TypeRef> Args;        // generic args (null if none)
        public bool Diamond;              // new ArrayList<>()
        public int Dims;                  // array dimensions
        public int NameStart, NameEnd;
        public JType Resolved;
        public override string ToString()
        {
            var s = Name;
            if (Args != null) s += "<" + string.Join(",", Args) + ">";
            else if (Diamond) s += "<>";
            for (int i = 0; i < Dims; i++) s += "[]";
            return s;
        }
    }

    [System.Flags]
    internal enum Mods { None = 0, Public = 1, Private = 2, Protected = 4, Static = 8, Final = 16, Other = 32 }

    // ---------------------------------------------------------------- declarations
    internal sealed class CompilationUnit : Node
    {
        public List<ImportDecl> Imports = new List<ImportDecl>();
        public bool IsClass;
        public string ClassName;
        public int ClassNameStart, ClassNameEnd;
        public List<FieldDecl> Fields = new List<FieldDecl>();
        public List<MethodDecl> Methods = new List<MethodDecl>();
    }

    internal sealed class ImportDecl : Node
    {
        public string Name;     // "java.util.*"
        public bool IsStatic;
    }

    internal sealed class FieldDecl : Node
    {
        public Mods Mods;
        public int ModsStart;
        public TypeRef Type;
        public List<VarDeclarator> Vars = new List<VarDeclarator>();
    }

    internal sealed class MethodDecl : Node
    {
        public Mods Mods;
        public TypeRef ReturnType;
        public string Name;
        public int NameStart, NameEnd;
        public List<Param> Params = new List<Param>();
        public BlockStmt Body;
        public int HeaderStart, HeaderEnd;  // modifiers .. ')'
        public MethodSymbol Symbol;
        public bool IsMain;
    }

    internal sealed class Param : Node
    {
        public TypeRef Type;
        public string Name;
        public int NameStart, NameEnd;
        public bool IsFinal;
        public LocalSymbol Symbol;
    }

    internal sealed class VarDeclarator : Node
    {
        public string Name;
        public int NameStart, NameEnd;
        public int ExtraDims;
        public Expr Init;
        public LocalSymbol Local;   // for locals
        public FieldSymbol Field;   // for fields
    }

    // ---------------------------------------------------------------- statements
    internal abstract class Stmt : Node { }

    internal sealed class BlockStmt : Stmt { public List<Stmt> Stmts = new List<Stmt>(); }
    internal sealed class EmptyStmt : Stmt { }
    internal sealed class ExprStmt : Stmt { public Expr E; }

    internal sealed class LocalVarStmt : Stmt
    {
        public TypeRef Type;      // Name == "var" for var
        public bool IsFinal;
        public List<VarDeclarator> Vars = new List<VarDeclarator>();
    }

    internal sealed class IfStmt : Stmt
    {
        public Expr Cond; public Stmt Then; public Stmt Else;
        public int KwEnd;
    }

    internal sealed class WhileStmt : Stmt { public Expr Cond; public Stmt Body; public int KwEnd; }

    internal sealed class DoWhileStmt : Stmt
    {
        public Stmt Body; public Expr Cond;
        public int WhileStart, WhileEnd;
    }

    internal sealed class ForStmt : Stmt
    {
        public List<Stmt> Init = new List<Stmt>();
        public Expr Cond;
        public List<Expr> Update = new List<Expr>();
        public Stmt Body;
        public int KwEnd, HeaderEnd;   // 'for' keyword end, ')' end
    }

    internal sealed class ForEachStmt : Stmt
    {
        public TypeRef VarType;
        public bool IsFinal;
        public string Name;
        public int NameStart, NameEnd;
        public Expr Iterable;
        public Stmt Body;
        public int KwEnd;
        public LocalSymbol Local;
    }

    internal sealed class BreakStmt : Stmt
    {
        public bool TargetsSwitch;
        public bool Strip;      // trailing break of a switch case: not emitted
    }

    internal sealed class ContinueStmt : Stmt { }
    internal sealed class ReturnStmt : Stmt { public Expr Value; }

    internal sealed class SwitchStmt : Stmt
    {
        public Expr Selector;
        public List<SwitchCase> Cases = new List<SwitchCase>();
        public bool Arrow;
        public int KwEnd;
    }

    internal sealed class SwitchCase : Node
    {
        public List<Expr> Labels = new List<Expr>();
        public bool IsDefault;
        public List<Stmt> Body = new List<Stmt>();
        public int LabelEnd;   // end of "case X:" / "case X ->"
    }

    // ---------------------------------------------------------------- expressions
    internal abstract class Expr : Node
    {
        public JType Type;
    }

    internal enum LitKind { Int, Double, Bool, String, Null }

    internal sealed class LiteralExpr : Expr
    {
        public LitKind Kind;
        public long IntValue;
        public double DoubleValue;
        public bool BoolValue;
        public string StrValue;
    }

    internal enum NameKind { Unresolved, Local, Field, OtherField, Direction, ConstBag, Class, OtherClass, BuiltinClass, Method }

    internal sealed class NameExpr : Expr
    {
        public string Name;
        public NameKind Kind;
        public LocalSymbol Local;
        public FieldSymbol Field;       // own or other-file field
        public string ClassName;        // for Class/OtherClass/BuiltinClass/ConstBag
    }

    internal sealed class ThisExpr : Expr { }

    internal sealed class FieldAccessExpr : Expr
    {
        public Expr Target;
        public string Name;
        public int NameStart, NameEnd;
        // resolution
        public FieldSymbol Field;       // static field (own or other file)
        public bool IsConstMember;      // Entities.Carrot
        public bool IsArrayLength;
        public bool IsSystemOut;        // System.out
        public string ConstText;        // Integer.MAX_VALUE, Math.PI: emitted as a number literal
    }

    internal enum CallKind
    {
        Unresolved,
        UserMethod,       // own file method
        OtherMethod,      // other file method
        GameApi,          // game builtin
        Print,            // System.out.println/print
        MathFn,           // Math.xxx
        StringValueOf,
        ListOf, SetOf, MapOf,
        Member,           // instance member on a collection/string/game type, see Member
        FunctionalRun,    // r.run() / s.get()
    }

    internal sealed class CallExpr : Expr
    {
        public Expr Target;      // null for unqualified
        public string Name;
        public int NameStart, NameEnd;
        public List<Expr> Args = new List<Expr>();
        public int LParen;
        public CallKind Kind;
        public MethodSymbol Method;
        public ApiFunctionDef Api;
        public string Member;    // normalised member op, e.g. "list.add", "map.get"
        public bool ValueUsed;   // set by the checker when the result is used as a value
    }

    internal sealed class NewExpr : Expr
    {
        public TypeRef TypeRef;
        public List<Expr> Args = new List<Expr>();
    }

    internal sealed class NewArrayExpr : Expr
    {
        public TypeRef Elem;              // element base type (no dims)
        public List<Expr> DimExprs = new List<Expr>();
        public int TotalDims;
        public ArrayInitExpr Init;
    }

    internal sealed class ArrayInitExpr : Expr { public List<Expr> Items = new List<Expr>(); }

    internal sealed class IndexExpr : Expr { public Expr Target, Index; }

    internal sealed class UnaryExpr : Expr { public string Op; public Expr Operand; public int OpEnd; }

    internal sealed class IncDecExpr : Expr
    {
        public string Op; public bool Prefix; public Expr Target;
        public int OpStart, OpEnd;
    }

    internal sealed class BinaryExpr : Expr
    {
        public string Op; public Expr Left, Right;
        public int OpStart, OpEnd;
    }

    internal sealed class AssignExpr : Expr
    {
        public string Op; public Expr Target, Value;
        public int OpStart, OpEnd;
    }

    internal sealed class ConditionalExpr : Expr
    {
        public Expr Cond, Then, Else;
        public int QStart;
    }

    internal sealed class CastExpr : Expr { public TypeRef TypeRef; public Expr Operand; }

    internal sealed class ParenExpr : Expr { public Expr Inner; }

    internal sealed class LambdaExpr : Expr
    {
        public List<string> ParamNames = new List<string>();
        public Expr BodyExpr;
        public BlockStmt BodyBlock;
        public int ArrowEnd;
        public bool IsSupplier;
        public JType ReturnType;   // Supplier<T>: T
    }

    internal sealed class MethodRefExpr : Expr
    {
        public Expr Qualifier;   // ThisExpr or NameExpr
        public string Name;
        public int NameStart, NameEnd;
        public MethodSymbol Method;
        public string OtherFile;  // when the method belongs to another file
    }
}
