using System.Collections.Generic;

namespace JavaFarmer.Compiler.CFamily
{
    internal abstract class Node { public int Start, End; }

    // ================================================================== type syntax

    /// <summary>
    /// A written type: "const int", "char *", "List(int)" (farm.h macro), "std::vector<std::string>", "auto&amp;",
    /// "std::function&lt;void()&gt;". Name is canonical (see Parser.TypeName): int, long, double, float, bool, void, char,
    /// string (std::string), cstring (farm.h typedef string), auto, game types, List/Map/Set (C macros), vector, map,
    /// unordered_map, set, unordered_set, function, optional, pair ...
    /// </summary>
    internal sealed class TypeSyn : Node
    {
        public string Name;
        public List<TypeSyn> Args = new List<TypeSyn>();
        public TypeSyn FnRet;                 // std::function<R(P...)>
        public List<TypeSyn> FnParams;
        public bool Const, Std, Unsigned, Signed, Short, Long, LongLong, Volatile;
        public int Pointers;
        public bool Ref, RRef;
        public string Written;                // source text (for messages)
    }

    internal sealed class Param : Node
    {
        public TypeSyn Type;
        public Declarator D;                  // name may be null
        public Expr Default;                  // C++ default argument
    }

    internal sealed class Declarator : Node
    {
        public string Name;
        public int NameStart, NameEnd;
        public int Pointers;
        public bool Ref;
        /// <summary>Array dimensions; a null entry is "[]".</summary>
        public List<Expr> Dims = new List<Expr>();
        public Expr Init;
        public int AssignStart = -1, AssignEnd = -1;
        /// <summary>C++ "T x(a, b)" / "T x{a, b}" direct initialisation.</summary>
        public List<Expr> CtorArgs;
        public bool BraceInit;
        /// <summary>Function pointer declarator "(*name)(params)".</summary>
        public bool FuncPtr;
        public List<Param> FnParams;
    }

    // ================================================================== top level

    internal sealed class Unit
    {
        public List<Node> Items = new List<Node>();
        public int Length;
    }

    internal sealed class Include : Node { public string Path; public bool System; public int PathStart, PathEnd; }
    internal sealed class Define : Node { public string Name; public int NameStart, NameEnd; public Expr Value; public string Error; public int ErrStart, ErrEnd; }
    internal sealed class OtherDirective : Node { public string Text; }
    internal sealed class UsingNamespace : Node { public string Ns; }
    internal sealed class Unsupported : Node { public string What; public string Message; }

    internal sealed class FuncDecl : Node
    {
        public TypeSyn Ret;
        public string Name;
        public int NameStart, NameEnd;
        public List<Param> Params = new List<Param>();
        public bool VoidParams;               // (void)
        public bool EmptyParams;              // ()
        public bool Variadic;
        public Block Body;                    // null: prototype
        public bool Static, Extern, Inline, Constexpr;
        public int HeaderStart, HeaderEnd;    // ret type .. ')'
        public TypeSyn TrailingRet;           // auto f() -> int
    }

    // ================================================================== statements

    internal abstract class Stmt : Node { }

    internal sealed class Block : Stmt { public List<Stmt> Stmts = new List<Stmt>(); }
    internal sealed class ExprStmt : Stmt { public Expr E; }
    internal sealed class EmptyStmt : Stmt { }
    internal sealed class VarDecl : Stmt
    {
        public TypeSyn Type;
        public List<Declarator> Vars = new List<Declarator>();
        public bool Static, Extern, Constexpr, Inline;
    }
    /// <summary>C++ structured binding: auto [a, b] = init; (also the range-for form).</summary>
    internal sealed class BindingDecl : Stmt
    {
        public TypeSyn Type;
        public List<(string Name, int Start, int End)> Names = new List<(string, int, int)>();
        public Expr Init;
    }
    internal sealed class IfStmt : Stmt { public Expr Cond; public Stmt Then, Else; public int KwEnd; public bool Constexpr; }
    internal sealed class WhileStmt : Stmt { public Expr Cond; public Stmt Body; public int KwEnd; }
    internal sealed class DoWhileStmt : Stmt { public Stmt Body; public Expr Cond; public int WhileStart, WhileEnd; }
    internal sealed class ForStmt : Stmt
    {
        public Stmt Init;                     // VarDecl / ExprStmt / null
        public Expr Cond;
        public List<Expr> Update = new List<Expr>();
        public Stmt Body;
        public int KwEnd, HeaderEnd;
    }
    /// <summary>C++ range-for "for (T x : range)" / "for (auto [k, v] : m)"; C farm.h "for_each (T x, coll)".</summary>
    internal sealed class RangeForStmt : Stmt
    {
        public TypeSyn Type;
        public Declarator Var;
        public BindingDecl Binding;
        public Expr Range;
        public Stmt Body;
        public int KwEnd, HeaderEnd;
        public bool Macro;                    // C for_each
    }
    internal sealed class SwitchStmt : Stmt { public Expr Sel; public Block Body; public int KwEnd; }
    internal sealed class CaseStmt : Stmt { public Expr Value; public bool IsDefault; public int LabelEnd; }
    internal sealed class BreakStmt : Stmt { }
    internal sealed class ContinueStmt : Stmt { }
    internal sealed class ReturnStmt : Stmt { public Expr Value; }
    internal sealed class UnsupportedStmt : Stmt { public string Message; }

    // ================================================================== expressions

    internal abstract class Expr : Node { public int ParenStart = -1, ParenEnd = -1; }

    internal enum LitKind { Int, Float, String, Char, Bool, Null, Nullptr }

    internal sealed class Lit : Expr
    {
        public LitKind Kind;
        public long I;
        public double F;
        public string S;
        public bool B;
        public string Suffix = "";
        public string Text;
    }

    /// <summary>A (possibly qualified) name: harvest, std::cout, Entities::Carrot, farm::random, std::ranges::contains.</summary>
    internal sealed class NameExpr : Expr
    {
        public string Name;
        public List<string> Qual = new List<string>();
        public int NameStart;
        public string Full => Qual.Count == 0 ? Name : string.Join("::", Qual) + "::" + Name;
        /// <summary>Template arguments written after the name (std::numeric_limits&lt;int&gt; ...).</summary>
        public List<TypeSyn> TArgs;
    }

    internal sealed class BinaryExpr : Expr { public string Op; public Expr L, R; public int OpStart, OpEnd; }
    internal sealed class UnaryExpr : Expr { public string Op; public Expr E; public int OpEnd; }
    internal sealed class IncDecExpr : Expr { public string Op; public bool Prefix; public Expr Target; public int OpStart, OpEnd; }
    internal sealed class AssignExpr : Expr { public string Op; public Expr Target, Value; public int OpStart, OpEnd; }
    internal sealed class CondExpr : Expr { public Expr C, T, F; public int QStart; }
    internal sealed class CallExpr : Expr
    {
        public Expr Callee;
        public List<Expr> Args = new List<Expr>();
        /// <summary>Type arguments of farm.h macros: list_new(int), map_of(Entity, int, ...), sizeof(int).</summary>
        public List<TypeSyn> TypeArgs;
        public int LParen, RParen;
    }
    internal sealed class IndexExpr : Expr { public Expr Target, Index; }
    internal sealed class MemberExpr : Expr { public Expr Target; public string Name; public bool Arrow; public int NameStart, NameEnd; }
    /// <summary>(T) e, static_cast&lt;T&gt;(e), T(e).</summary>
    internal sealed class CastExpr : Expr { public TypeSyn Type; public Expr E; public string Style; public int OpEnd; }
    /// <summary>{a, b, c} (initialiser list; designators are not supported).</summary>
    internal sealed class InitList : Expr { public List<Expr> Items = new List<Expr>(); }
    /// <summary>(Position){1, 2} (C compound literal), Position{1, 2}, std::vector&lt;int&gt;{1, 2}, std::vector&lt;int&gt;(n, 0).</summary>
    internal sealed class ConstructExpr : Expr { public TypeSyn Type; public List<Expr> Args = new List<Expr>(); public bool Braces; public bool Compound; }
    internal sealed class LambdaExpr : Expr
    {
        public string Default = "";           // "&", "=" or ""
        public List<(string Name, bool ByRef)> Captures = new List<(string, bool)>();
        public List<Param> Params = new List<Param>();
        public bool HasParams;
        public TypeSyn Ret;
        public Block Body;
        public int HeaderEnd;
    }
    internal sealed class SizeofExpr : Expr { public Expr E; public TypeSyn T; }
    internal sealed class CommaExpr : Expr { public List<Expr> Items = new List<Expr>(); }
    internal sealed class BadExpr : Expr { }
}
