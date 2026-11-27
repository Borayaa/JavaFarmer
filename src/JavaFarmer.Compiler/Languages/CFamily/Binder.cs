using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JavaFarmer.Compiler.CFamily
{
    internal enum SymKind { Local, Param, Global, Define, Function }

    internal sealed class Sym
    {
        public string Name;
        public SymKind Kind;
        public CT Type;
        public IrSymbol Ir;
        public bool Const;
        public bool ArrayParam;          // decayed array parameter (sizeof is wrong on it)
        public bool RefBinding;          // C++ reference (alias), or range-for reference variable
        public bool LoopRef;             // range-for by-reference variable (assigning it is rejected)
        public IrExpr DefineValue;       // #define value (inlined at every use)
        public bool ConstantExpr;        // usable in C file-scope initialisers (literal #define / constexpr)
        public FnSig Fn;
        public int DeclStart;
        public Func<IrExpr> PairFirst, PairSecond;   // C++ range-for over a map with a pair variable
    }

    internal sealed class FnSig
    {
        public string Name;
        public CT Ret;
        public List<CT> Params = new List<CT>();
        public List<IrExpr> Defaults = new List<IrExpr>();
        public bool Defined;
        public bool DefinedHere;           // defined in this file (now or later)
        public string Module;              // another file's function (prototype resolved there)
        public IrSymbol Ir;
        public bool IsStatic;
        public bool IsMain;
        public int ProtoStart, ProtoEnd;
        public bool Used;
        public bool Unresolved;            // prototype with no definition anywhere
        public int ForeignMinArgs = -1;
        public string ForeignError;         // why the prototype may be unresolved: a window in another language does not compile     // a function of a window in another language (its own optional parameters)
    }

    /// <summary>
    /// Name resolution + type checking (gcc / g++ wording) + lowering to Farm IR, in one pass over the AST in source
    /// order (C and C++ both require declaration before use). See docs/languages/c.md and cpp.md for the subset.
    /// </summary>
    internal sealed partial class Binder
    {
        readonly Dialect d;
        readonly string src;
        readonly string fileName;
        readonly CompileOptions opts;
        readonly OtherFiles others;
        public readonly List<Diagnostic> Errors = new List<Diagnostic>();
        readonly IrModule mod;
        bool Cpp => d == Dialect.Cpp;
        bool C => d == Dialect.C;

        readonly HashSet<string> headers = new HashSet<string>();
        bool usingStd;

        sealed class Scope
        {
            public readonly Dictionary<string, Sym> Names = new Dictionary<string, Sym>();
            public Scope Parent;
            public bool FunctionTop;
        }
        readonly Scope globals = new Scope();
        Scope cur;
        readonly Dictionary<string, FnSig> functions = new Dictionary<string, FnSig>();
        readonly HashSet<string> definedHere = new HashSet<string>();

        // function context
        FnSig curFn;
        CT curRet;
        bool inEntry;
        int loopDepth, switchDepth, lambdaDepth;
        readonly List<CT> lambdaRets = new List<CT>();
        readonly List<Scope> lambdaScopes = new List<Scope>();

        // output buffering (printf without a trailing newline)
        bool bufferMode;
        IrSymbol outBuf;
        public const string BufferTag = "cfamily:outbuf";
        public const string BufferName = "__jout";
        /// <summary>IrSymbol.Tag of module constants made from #define (printers write them back as #define).</summary>
        public const string DefineTag = "cfamily:define";
        /// <summary>IrSymbol.Tag of C++ reference parameters (T&amp; x).</summary>
        public const string RefTag = "cfamily:ref";

        public Binder(Dialect dialect, string source, CompileOptions options, OtherFiles otherFiles)
        {
            d = dialect; src = source; opts = options ?? new CompileOptions(); others = otherFiles;
            fileName = string.IsNullOrEmpty(opts.FileName) ? "main" : opts.FileName;
            mod = new IrModule { Name = fileName, SourceLanguage = d == Dialect.C ? "c" : "cpp", Span = new Span(0, source.Length) };
            cur = globals;
        }

        // ================================================================== diagnostics

        void Err(string msg, int s, int e)
        {
            if (s < 0) s = 0;
            if (e < s) e = s;
            foreach (var x in Errors) if (x.Start == s && x.Message == msg) return;
            Errors.Add(new Diagnostic(msg, s, e));
        }
        void Err(string msg, Node n) => Err(msg, n?.Start ?? 0, n?.End ?? 0);

        static Span Sp(Node n) => n == null ? Span.None : new Span(n.Start, n.End);
        static Span Sp(int s, int e) => s < 0 ? Span.None : new Span(s, e);

        string Q(string name) => Cpp ? "'" + name + "'" : "'" + name + "'";
        string Show(CT t) => t.Show(d);

        // ================================================================== module

        public IrModule Bind(Unit u, List<IrComment> comments)
        {
            // pre-pass: which functions does this file define (prototypes of them are forward declarations)
            foreach (var it in u.Items) if (it is FuncDecl f && f.Body != null) definedHere.Add(f.Name);
            bufferMode = NeedsBuffer(u);
            if (bufferMode)
            {
                outBuf = new IrSymbol(BufferName, IrSymbolKind.Global, IrType.String) { Tag = BufferTag };
                var lit = IrLiteral.OfString(""); lit.Synthetic = true;
                mod.Items.Add(new IrGlobal { Symbol = outBuf, Init = lit, Synthetic = true, IsPrivate = true });
            }
            foreach (var it in u.Items)
            {
                switch (it)
                {
                    case Include inc: IncludeDirective(inc); break;
                    case Define def: DefineDirective(def); break;
                    case OtherDirective od:
                        if (od.Text == "pragma") break;
                        Err("#" + od.Text + " is not supported in JavaFarmer", od);
                        break;
                    case UsingNamespace un:
                        if (C) { Err("expected '=', ',', ';', 'asm' or '__attribute__' before 'namespace'", un); break; }
                        if (un.Ns == "std") usingStd = true;
                        else if (un.Ns != "farm") Err("namespace '" + un.Ns + "' is not supported in JavaFarmer", un);
                        break;
                    case Unsupported us: Err(us.Message, us); break;
                    case FuncDecl fd: Function(fd); break;
                    case VarDecl vd: GlobalDecl(vd); break;
                }
            }
            foreach (var f in functions.Values)
                if (f.Used && f.Unresolved && !f.DefinedHere)
                    Err(f.ForeignError != null ? "cannot use '" + f.Name + "': " + f.ForeignError : "undefined reference to '" + f.Name + "' (no code window defines it)", f.ProtoStart, f.ProtoEnd);
            if (comments != null && comments.Count > 0) IrComments.Attach(mod, comments, src);
            return mod;
        }

        // ------------------------------------------------------------------ directives

        static readonly HashSet<string> CHeaders = new HashSet<string> { "stdio.h", "stdbool.h", "math.h", "string.h", "stdlib.h", "stddef.h" };
        static readonly HashSet<string> CppHeaders = new HashSet<string>
        {
            "iostream", "vector", "map", "unordered_map", "set", "unordered_set", "string", "cmath", "algorithm", "functional",
            "optional", "print", "cstdio", "utility", "format", "cstdlib", "stdio.h", "math.h", "ranges",
        };

        public string StubName => C ? "farm.h" : "farm.hpp";

        void IncludeDirective(Include inc)
        {
            var path = inc.Path.Trim();
            if (!inc.System)
            {
                if (path == StubName) { headers.Add(path); return; }
                if (path == "farm.h" || path == "farm.hpp")
                {
                    Err(path + ": the JavaFarmer API header for " + (C ? "C is \"farm.h\"" : "C++ is \"farm.hpp\""), inc.PathStart, inc.PathEnd);
                    return;
                }
                Err("#include \"" + path + "\" is not supported in JavaFarmer: other code windows are used by declaring their functions (a prototype such as void plant_row(void);)", inc.PathStart, inc.PathEnd);
                return;
            }
            if ((C ? CHeaders : CppHeaders).Contains(path)) { headers.Add(path); return; }
            Err("#include <" + path + "> is not supported in JavaFarmer", inc.PathStart, inc.PathEnd);
        }

        bool Farm => headers.Contains(StubName);
        bool Has(params string[] hs) { foreach (var h in hs) if (headers.Contains(h)) return true; return false; }

        void DefineDirective(Define def)
        {
            if (def.Error != null) { Err(def.Error, def.ErrStart, def.ErrEnd); return; }
            if (!CheckName(def.Name, def.NameStart, def.NameEnd)) return;
            if (globals.Names.ContainsKey(def.Name)) { Err("\"" + def.Name + "\" redefined", def.NameStart, def.NameEnd); return; }
            var v = BindExpr(def.Value);
            if (v.T.IsError) return;
            if (!IsConstant(def.Value) || !(v.T.IsNumeric || v.T.Kind == CK.Bool || v.T.IsStr))
            {
                Err("#define " + def.Name + " must be a number, string or bool constant in JavaFarmer", def.Value);
                return;
            }
            // a #define is a module-level constant (the game Python has no macros)
            var irs = new IrSymbol(def.Name, IrSymbolKind.Global, v.T.Ir()) { IsConst = true, DeclSpan = Sp(def.NameStart, def.NameEnd), Tag = DefineTag };
            mod.Items.Add(new IrGlobal { Symbol = irs, Init = v.E, Span = Sp(def), NameSpan = Sp(def.NameStart, def.NameEnd), IsPrivate = true });
            globals.Names[def.Name] = new Sym { Name = def.Name, Kind = SymKind.Define, Type = v.T, Ir = irs, Const = true, ConstantExpr = true, DeclStart = def.Start, DefineValue = v.E };
        }

        bool CheckName(string name, int s, int e)
        {
            if (name != null && name.StartsWith("__j", StringComparison.Ordinal))
            {
                Err("identifiers starting with __j are reserved by JavaFarmer", s, e);
                return false;
            }
            return true;
        }

        // ================================================================== types

        static readonly Dictionary<string, string> StdNames = new Dictionary<string, string>();

        /// <summary>Written type + declarator → checker type (errors for what the subset lacks).</summary>
        CT Resolve(TypeSyn ts, Declarator dc = null, bool param = false)
        {
            if (ts == null) return CT.Error;
            var t = ResolveBase(ts);
            if (t.IsError) return t;
            int ptrs = ts.Pointers + (dc?.Pointers ?? 0);
            if (ptrs > 0)
            {
                if (t.Kind == CK.Char && ptrs == 1) t = CT.CStr;
                else
                {
                    Err("pointers are not supported in JavaFarmer" + (t.Kind == CK.Char ? "" : " (only char * strings)"), ts.Start, dc != null && dc.NameStart > 0 ? dc.NameStart : ts.End);
                    return CT.Error;
                }
            }
            else if (t.Kind == CK.Char && !(dc != null && dc.Dims.Count > 0))
            {
                Err("the char type is not supported in JavaFarmer (use " + (Cpp ? "std::string" : "const char *") + " for text)", ts);
                return CT.Error;
            }
            if (dc != null && dc.FuncPtr)
            {
                if (Cpp) { Err("function pointers are not supported in JavaFarmer C++ (use std::function<void()> or a lambda)", dc); return CT.Error; }
                var ps = new List<CT>();
                foreach (var p in dc.FnParams) { var pt = Resolve(p.Type, p.D, true); if (pt.IsError) return pt; ps.Add(pt); }
                return CT.Func(t, ps);
            }
            if (dc != null && dc.Dims.Count > 0)
            {
                if (t.Kind == CK.Void) { Err("declaration of '" + dc.Name + "' as array of voids", dc); return CT.Error; }
                if (t.Kind == CK.Char) { Err("char arrays are not supported in JavaFarmer (use " + (Cpp ? "std::string" : "const char *") + ")", dc); return CT.Error; }
                for (int i = dc.Dims.Count - 1; i >= 0; i--) t = CT.Array(t);
            }
            return t;
        }

        CT ResolveBase(TypeSyn ts)
        {
            if (ts.Name != null && ts.Name.StartsWith("#")) { Err("a template argument must be a type here", ts); return CT.Error; }
            if (ts.Unsigned) { Err("unsigned types are not supported in JavaFarmer (use int)", ts); return CT.Error; }
            if (ts.Short || ts.LongLong) { Err("'" + ts.Written + "' is not supported in JavaFarmer (use int)", ts); return CT.Error; }
            if (ts.Ref || ts.RRef)
            {
                if (C) { Err("expected ';', ',' or ')' before '&' token", ts); return CT.Error; }
            }
            if (ts.Std && !Cpp) { Err("unknown type name 'std'", ts); return CT.Error; }
            if (Cpp && !ts.Std && !usingStd && IsStdOnly(ts.Name))
            {
                Err("'" + ts.Name + "' does not name a type; did you mean 'std::" + ts.Name + "'?", ts.Start, ts.Start + ts.Name.Length);
                return CT.Error;
            }
            switch (ts.Name)
            {
                case "int": return ts.Long ? new CT { Kind = CK.Int, Long = true } : CT.Int;
                case "double": if (ts.Long) { Err("long double is not supported in JavaFarmer (use double)", ts); return CT.Error; } return CT.Float;
                case "float": return new CT { Kind = CK.Float, Single = true };
                case "bool": return CT.Bool;
                case "void": return CT.Void;
                case "char": return CT.Char;
                case "auto": return new CT { Kind = CK.Any, Game = "auto" };
                case "cstring":
                    if (!Farm) { Err("unknown type name 'string'; did you forget '#include \"farm.h\"'?", ts); return CT.Error; }
                    return CT.CStr;
                case "string":
                    if (C) goto case "cstring";
                    return CT.StdStr;
                case "List": case "Map": case "Set":
                    {
                        if (!Farm) { Err("unknown type name '" + ts.Name + "'; did you forget '#include \"farm.h\"'?", ts); return CT.Error; }
                        int want = ts.Name == "Map" ? 2 : 1;
                        if (ts.Args.Count != want) { Err("macro \"" + ts.Name + "\" requires " + want + " argument" + (want == 1 ? "" : "s") + ", but " + ts.Args.Count + " given", ts); return CT.Error; }
                        var a = new List<CT>();
                        foreach (var x in ts.Args) { var r = ElementType(x, ts.Name == "Map" && a.Count == 0); if (r.IsError) return r; a.Add(r); }
                        return ts.Name == "List" ? CT.List(a[0]) : ts.Name == "Set" ? CT.Set(a[0]) : CT.Map(a[0], a[1]);
                    }
                case "vector": case "set": case "unordered_set":
                    {
                        if (ts.Args.Count != 1) { Err("wrong number of template arguments for 'std::" + ts.Name + "'", ts); return CT.Error; }
                        var e = ElementType(ts.Args[0], ts.Name != "vector");
                        if (e.IsError) return e;
                        return ts.Name == "vector" ? CT.List(e, true) : CT.Set(e, ts.Name == "unordered_set");
                    }
                case "map": case "unordered_map":
                    {
                        if (ts.Args.Count != 2) { Err("wrong number of template arguments for 'std::" + ts.Name + "'", ts); return CT.Error; }
                        var k = ElementType(ts.Args[0], true); if (k.IsError) return k;
                        var v = ElementType(ts.Args[1], false); if (v.IsError) return v;
                        return CT.Map(k, v, ts.Name == "unordered_map");
                    }
                case "function":
                    {
                        if (ts.FnRet == null) { Err("std::function needs a signature: std::function<void()>", ts); return CT.Error; }
                        var r = ResolveBase(ts.FnRet);
                        if (r.IsError) return r;
                        var ps = new List<CT>();
                        foreach (var p in ts.FnParams) { var pt = ResolveBase(p); if (pt.IsError) return pt; ps.Add(pt); }
                        return CT.Func(r, ps, true);
                    }
                case "optional":
                    {
                        if (ts.Args.Count != 1) { Err("wrong number of template arguments for 'std::optional'", ts); return CT.Error; }
                        var inner = ElementType(ts.Args[0], false);
                        if (inner.IsError) return inner;
                        return CT.Optional(inner);
                    }
                case "Entity": case "Ground": case "Item": case "Hat": case "Unlock": case "Leaderboard": case "Direction": case "Drone":
                    if (!Farm) { Err("unknown type name '" + ts.Name + "'; did you forget '#include \"" + StubName + "\"'?", ts); return CT.Error; }
                    return CT.Handle(ts.Name);
                case "Position":
                    if (!Farm) { Err("unknown type name 'Position'; did you forget '#include \"" + StubName + "\"'?", ts); return CT.Error; }
                    return CT.Position;
                case "Companion":
                    if (!Farm) { Err("unknown type name 'Companion'; did you forget '#include \"" + StubName + "\"'?", ts); return CT.Error; }
                    return CT.Companion;
            }
            Err("'" + (ts.Std ? "std::" : "") + ts.Name + "' is not supported in JavaFarmer", ts);
            return CT.Error;
        }

        static bool IsStdOnly(string n) => n == "vector" || n == "map" || n == "unordered_map" || n == "set" || n == "unordered_set" || n == "function" || n == "optional" || n == "string" || n == "pair" || n == "array" || n == "size_t" || n == "tuple" || n == "list" || n == "deque";

        CT ElementType(TypeSyn ts, bool key)
        {
            var t = ResolveBase(ts);
            if (t.IsError) return t;
            if (ts.Pointers > 0) { if (t.Kind == CK.Char && ts.Pointers == 1 && C) t = CT.CStr; else { Err("pointers are not supported in JavaFarmer", ts); return CT.Error; } }
            if (t.Kind == CK.Any && t.Game == "auto") { Err("'auto' is not allowed here", ts); return CT.Error; }
            if (t.Kind == CK.Void || t.Kind == CK.Char) { Err("a collection of " + Show(t) + " is not supported in JavaFarmer", ts); return CT.Error; }
            if (C && (t.IsCollection || t.Kind == CK.Func || t.Kind == CK.Companion || (key && t.Kind == CK.Bool)))
            {
                Err("farm.h has no " + (key ? "key" : "element") + " type " + Show(t) + " (supported: int, double, bool, string, Position and the game types)", ts);
                return CT.Error;
            }
            if (Cpp && key && !(t.IsNumeric || t.IsStr || t.Kind == CK.Handle || t.Kind == CK.Position || t.Kind == CK.Bool))
            {
                Err(Show(t) + " cannot be a set element or map key in JavaFarmer", ts);
                return CT.Error;
            }
            return t;
        }

        // ================================================================== globals

        void GlobalDecl(VarDecl vd)
        {
            foreach (var dc in vd.Vars)
            {
                if (dc.Name == null) continue;
                if (!CheckName(dc.Name, dc.NameStart, dc.NameEnd)) continue;
                var t = Resolve(vd.Type, dc);
                if (t.IsError) continue;
                if (t.Kind == CK.Void) { Err("variable '" + dc.Name + "' declared void", dc.NameStart, dc.NameEnd); continue; }
                if (t.Kind == CK.Any && t.Game == "auto" && dc.Init == null && dc.CtorArgs == null) { Err("declaration of 'auto " + dc.Name + "' has no initializer", dc); continue; }
                if (vd.Extern)
                {
                    ExternGlobal(vd, dc, t);
                    continue;
                }
                if (globals.Names.TryGetValue(dc.Name, out var prev))
                {
                    if (prev.Kind == SymKind.Global && prev.Ir.Kind == IrSymbolKind.ExternalGlobal) Err("JavaFarmer: '" + dc.Name + "' is declared extern above; define it in only one code window", dc.NameStart, dc.NameEnd);
                    else Err(C ? "redefinition of '" + dc.Name + "'" : "redefinition of '" + Show(t) + " " + dc.Name + "'", dc.NameStart, dc.NameEnd);
                    continue;
                }
                if (functions.ContainsKey(dc.Name)) { Err("'" + dc.Name + "' redeclared as different kind of symbol", dc.NameStart, dc.NameEnd); continue; }
                bool isConst = (vd.Type.Const || vd.Constexpr) && vd.Type.Pointers + dc.Pointers == 0;
                var irs = new IrSymbol(dc.Name, IrSymbolKind.Global, t.Ir()) { IsConst = isConst, DeclSpan = Sp(dc.NameStart, dc.NameEnd) };
                var g = new IrGlobal { Symbol = irs, Span = Sp(vd.Vars.Count == 1 ? vd : (Node)dc), NameSpan = Sp(dc.NameStart, dc.NameEnd), IsPrivate = vd.Static, TypeExplicit = !(t.Kind == CK.Any && t.Game == "auto") };
                if (g.Span.Start > dc.Start) g.Span = new Span(dc.Start, dc.End);
                if (vd.Vars.Count == 1) g.Span = Sp(vd);
                var sym = new Sym { Name = dc.Name, Kind = SymKind.Global, Type = t, Ir = irs, Const = isConst, DeclStart = dc.Start };
                IrExpr init = null;
                if (dc.Init != null || dc.CtorArgs != null || t.Kind == CK.Array || (Cpp && (t.IsCollection || t.IsStr)))
                {
                    if (C && dc.Init != null && !IsConstant(dc.Init)) { Err("initializer element is not constant", dc.Init); continue; }
                    if (C && t.Kind == CK.Array && !DimsConstant(dc)) continue;
                    var res = InitValue(t, dc, out var actual);
                    if (res == null) continue;
                    if (t.Kind == CK.Any) { t = actual; irs.Type = t.Ir(); sym.Type = t; }
                    init = res;
                }
                else if (isConst && !vd.Extern)
                {
                    if (vd.Constexpr || Cpp) { Err(C ? "'constexpr' object '" + dc.Name + "' requires an initializer" : "uninitialized 'const " + dc.Name + "'", dc.NameStart, dc.NameEnd); continue; }
                }
                g.Init = init;
                if (init == null) g.ImplicitInit = false;
                sym.ConstantExpr = isConst && vd.Constexpr && init != null && (init is IrLiteral || init is IrUnary);
                if (C && vd.Type.Const && !vd.Constexpr) sym.ConstantExpr = false;
                globals.Names[dc.Name] = sym;
                mod.Items.Add(g);
            }
        }

        bool DimsConstant(Declarator dc)
        {
            foreach (var dim in dc.Dims)
                if (dim != null && !IsConstant(dim)) { Err("variably modified '" + dc.Name + "' at file scope", dc.NameStart, dc.NameEnd); return false; }
            return true;
        }

        void ExternGlobal(VarDecl vd, Declarator dc, CT t)
        {
            if (dc.Init != null) { Err("'" + dc.Name + "' has both 'extern' and initializer", dc); return; }
            if (globals.Names.ContainsKey(dc.Name)) return;
            var o = others?.FindGlobal(dc.Name, fileName, this);
            if (o == null)
            {
                if (others != null && others.ForeignErrors.Count > 0) Err("cannot use '" + dc.Name + "': " + others.ForeignErrors[0], dc.NameStart, dc.NameEnd);
                else Err("undefined reference to '" + dc.Name + "' (no other code window defines it)", dc.NameStart, dc.NameEnd);
                return;
            }
            var gforeign = others.ForeignOf(o.Value.Module);
            if (gforeign != null ? !ForeignCompatible(o.Value.Type, t) : !o.Value.Type.Same(t)) Err("conflicting types for '" + dc.Name + "'; the code window " + o.Value.Module + " defines it as " + Show(o.Value.Type), dc);
            var irs = new IrSymbol(dc.Name, IrSymbolKind.ExternalGlobal, t.Ir()) { Module = o.Value.Module, PyName = o.Value.PyName, IsConst = vd.Type.Const, DeclSpan = Sp(dc.NameStart, dc.NameEnd) };
            globals.Names[dc.Name] = new Sym { Name = dc.Name, Kind = SymKind.Global, Type = t, Ir = irs, Const = vd.Type.Const, DeclStart = dc.Start };
        }

        /// <summary>C constant expression (file-scope initialisers, array sizes, case labels).</summary>
        bool IsConstant(Expr e)
        {
            switch (e)
            {
                case null: return true;
                case Lit l: return l.Kind != LitKind.Char || true;
                case UnaryExpr u: return (u.Op == "-" || u.Op == "+" || u.Op == "!") && IsConstant(u.E);
                case BinaryExpr b: return IsConstant(b.L) && IsConstant(b.R);
                case CondExpr c: return IsConstant(c.C) && IsConstant(c.T) && IsConstant(c.F);
                case InitList il: foreach (var x in il.Items) if (!IsConstant(x)) return false; return true;
                case CastExpr ca: return IsConstant(ca.E);
                case NameExpr n:
                    if (n.Qual.Count > 0) return Cpp;
                    if (n.Name == "NULL") return true;
                    if (C && (n.Name == "North" || n.Name == "East" || n.Name == "South" || n.Name == "West") && Lookup("North") == null) return true;
                    var s = Lookup(n.Name);
                    return s != null && (s.Kind == SymKind.Define || s.ConstantExpr);
                case SizeofExpr _: return true;
                case ConstructExpr ce when ce.Compound: foreach (var x in ce.Args) if (!IsConstant(x)) return false; return true;
            }
            return false;
        }

        /// <summary>Initial value of a declared variable (null + error when invalid). actual: the deduced type for auto.</summary>
        IrExpr InitValue(CT t, Declarator dc, out CT actual)
        {
            actual = t;
            if (t.Kind == CK.Array)
            {
                // int a[n]; int a[3] = {1, 2, 3}; int g[w][h]; int a[] = {...}
                var dims = new List<IrExpr>();
                foreach (var dim in dc.Dims)
                {
                    if (dim == null) { dims.Add(null); continue; }
                    var dv = BindExpr(dim);
                    if (dv.T.IsError) return null;
                    if (dv.T.Kind != CK.Int) { Err("size of array '" + dc.Name + "' has non-integer type", dim); return null; }
                    dims.Add(dv.E);
                }
                if (dc.Init == null)
                {
                    if (dims.Count > 0 && dims[0] == null) { Err("array size missing in '" + dc.Name + "'", dc.NameStart, dc.NameEnd); return null; }
                    if (dc.CtorArgs != null && dc.CtorArgs.Count > 0) { Err("array must be initialized with a brace-enclosed initializer", dc); return null; }
                    return ArrayFill(t, dims, dc);
                }
                if (!(dc.Init is InitList il)) { Err("array must be initialized with a brace-enclosed initializer", dc.Init); return null; }
                return ArrayLiteral(t, dims, 0, il, dc);
            }
            if (dc.CtorArgs != null)
            {
                if (dc.BraceInit)
                {
                    var il = new InitList { Start = dc.AssignStart, End = dc.End };
                    il.Items.AddRange(dc.CtorArgs);
                    if (t.Kind == CK.Any && t.Game == "auto")
                    {
                        if (dc.CtorArgs.Count != 1) { Err("direct-list-initialization of 'auto' requires exactly one element", dc); return null; }
                        var b1 = BindExpr(dc.CtorArgs[0]);
                        if (b1.T.IsError) return null;
                        actual = Decay(b1.T);
                        return b1.E;
                    }
                    if (t.IsNumeric || t.Kind == CK.Bool || t.IsStr || t.Kind == CK.Handle)
                    {
                        if (dc.CtorArgs.Count == 0) return DefaultValue(t, dc);
                        if (dc.CtorArgs.Count != 1) { Err("too many initializers for '" + Show(t) + "'", dc); return null; }
                        var b = BindExpr(dc.CtorArgs[0]);
                        if (b.T.IsError) return null;
                        if (b.T.Kind == CK.Float && t.Kind == CK.Int) { Err("narrowing conversion of '" + Text(dc.CtorArgs[0]) + "' from 'double' to 'int'", dc.CtorArgs[0]); return null; }
                        var cb = Convert(b, t, dc.CtorArgs[0], "initialization");
                        return cb.T.IsError ? null : cb.E;
                    }
                    var ib = BindTo(il, t);
                    return ib.T.IsError ? null : ib.E;
                }
                var ce = new ConstructExpr { Start = dc.AssignStart, End = dc.End, Type = null };
                ce.Args.AddRange(dc.CtorArgs);
                var cr = Construct(t, ce.Args, false, dc);
                return cr.T.IsError ? null : cr.E;
            }
            if (dc.Init == null)
            {
                // C++ objects are default-constructed; C scalars start as 0 (JavaFarmer's choice for indeterminate values)
                return DefaultValue(t, dc);
            }
            if (t.Kind == CK.Any && t.Game == "auto")
            {
                if (dc.Init is InitList) { Err("unable to deduce 'auto' from a braced initializer list in JavaFarmer", dc.Init); return null; }
                var b = BindExpr(dc.Init);
                if (b.T.IsError) return null;
                if (b.T.Kind == CK.Void) { Err("void value not ignored as it ought to be", dc.Init); return null; }
                if (b.T.Kind == CK.Null) { Err("unable to deduce 'auto' from 'nullptr'", dc.Init); return null; }
                actual = Decay(b.T);
                if (actual.Kind == CK.Str && b.T.IsLiteralStr) actual = CT.CStr;
                return CopyIfValue(b, actual, dc.Ref);
            }
            var r = BindTo(dc.Init, t);
            if (r.T.IsError) return null;
            return CopyIfValue(r, t, dc.Ref);
        }

        static CT Decay(CT t)
        {
            if (t.Kind == CK.Str && t.IsLiteralStr) { var c = t.Clone(); c.IsLiteralStr = false; return c; }
            return t;
        }

        /// <summary>C++ value semantics: "std::vector&lt;int&gt; b = a;" copies (Python would alias).</summary>
        IrExpr CopyIfValue(BE b, CT t, bool isRef)
        {
            if (!Cpp || isRef || !b.LValue) return b.E;
            IrOp op;
            switch (t.Kind)
            {
                case CK.List: op = IrOp.ListCopy; break;
                case CK.Set: op = IrOp.SetCopy; break;
                case CK.Map: op = IrOp.MapCopy; break;
                default: return b.E;
            }
            return new IrOpCall(op, b.E) { Synthetic = true, Span = b.E.Span, Type = t.Ir() };
        }

        IrExpr DefaultValue(CT t, Node at)
        {
            IrExpr e;
            switch (t.Kind)
            {
                case CK.Int: e = IrLiteral.OfInt(0); break;
                case CK.Float: e = new IrLiteral { Kind = IrLitKind.Int, Int = 0, Type = IrType.Float }; break;
                case CK.Bool: e = IrLiteral.OfBool(false); break;
                case CK.Str: e = Cpp ? IrLiteral.OfString("") : (IrExpr)IrLiteral.Null(); break;
                case CK.List: e = Cpp ? new IrListLit { Type = t.Ir() } : (IrExpr)IrLiteral.Null(); break;
                case CK.Set: e = Cpp ? new IrSetLit { Type = t.Ir() } : (IrExpr)IrLiteral.Null(); break;
                case CK.Map: e = Cpp ? new IrMapLit { Type = t.Ir() } : (IrExpr)IrLiteral.Null(); break;
                case CK.Position:
                    {
                        var tl = new IrTupleLit { Type = t.Ir() };
                        tl.Items.Add(Syn(IrLiteral.OfInt(0), at)); tl.Items.Add(Syn(IrLiteral.OfInt(0), at));
                        e = tl;
                        break;
                    }
                case CK.Any:
                    Err("declaration of 'auto' has no initializer", at);
                    return null;
                default: e = IrLiteral.Null(); break;
            }
            e.Synthetic = true;
            e.Span = Sp(at);
            if (e.Type == null || !e.Type.IsKnown || e.Type.Kind == IrTypeKind.Null) e.Type = t.Ir();
            return e;
        }

        static IrExpr Syn(IrExpr e, Node at) { e.Synthetic = true; e.Span = Sp(at); return e; }

        IrExpr ArrayFill(CT t, List<IrExpr> dims, Node at)
        {
            var leaf = t;
            while (leaf.Kind == CK.Array) leaf = leaf.Elem;
            var dflt = DefaultValue(leaf, at);
            if (dflt == null) return null;
            if (dims.Count == 1) return new IrOpCall(IrOp.ArrayNew, dims[0], dflt) { Span = Sp(at), Type = t.Ir() };
            if (dims.Count == 2) return new IrOpCall(IrOp.ArrayNew2, dims[0], dims[1], dflt) { Span = Sp(at), Type = t.Ir() };
            Err("arrays with more than 2 dimensions are not supported in JavaFarmer", at);
            return null;
        }

        IrExpr ArrayLiteral(CT t, List<IrExpr> dims, int level, InitList il, Declarator dc)
        {
            var elem = t.Elem;
            IrExpr dim = level < dims.Count ? dims[level] : null;
            long n = dim == null ? -1 : ConstInt(dim);
            if (dim != null && n < 0) { Err("variable-sized object may not be initialized", il); return null; }
            if (n >= 0 && il.Items.Count > n) { Err("excess elements in array initializer", il.Items[(int)n]); return null; }
            // {0} / {} : n copies of the default
            bool allZero = il.Items.Count == 0 || (il.Items.Count == 1 && il.Items[0] is Lit z && ((z.Kind == LitKind.Int && z.I == 0) || (z.Kind == LitKind.Float && z.F == 0) || (z.Kind == LitKind.Bool && !z.B) || z.Kind == LitKind.Null || z.Kind == LitKind.Nullptr));
            if (allZero && n > 0 && (elem.Kind != CK.Array || dims.Count - level == 2))
            {
                var rest = dims.GetRange(level, dims.Count - level);
                var fill = ArrayFill(t, rest, il);
                if (fill != null && il.Items.Count == 1 && elem.Kind != CK.Array)
                {
                    // keep the written default ({false}, {0.0})
                }
                return fill;
            }
            var lit = new IrListLit { IsArray = true, Span = Sp(il), Type = t.Ir() };
            foreach (var item in il.Items)
            {
                if (elem.Kind == CK.Array)
                {
                    if (!(item is InitList sub)) { Err("braces around nested array initializer expected", item); return null; }
                    var inner = ArrayLiteral(elem, dims, level + 1, sub, dc);
                    if (inner == null) return null;
                    lit.Items.Add(inner);
                }
                else
                {
                    var b = BindTo(item, elem);
                    if (b.T.IsError) return null;
                    lit.Items.Add(b.E);
                }
            }
            // missing trailing elements are zero (C semantics)
            if (n > il.Items.Count)
            {
                if (n - il.Items.Count > 256) { Err("JavaFarmer: initialise this array completely or with {0}", il); return null; }
                for (long i = il.Items.Count; i < n; i++)
                {
                    if (elem.Kind == CK.Array)
                    {
                        var sub = new InitList { Start = il.End - 1, End = il.End };
                        var inner = ArrayLiteral(elem, dims, level + 1, sub, dc);
                        if (inner == null) return null;
                        lit.Items.Add(inner);
                    }
                    else lit.Items.Add(DefaultValue(elem, il));
                }
            }
            return lit;
        }

        // ================================================================== functions

        void Function(FuncDecl f)
        {
            if (!CheckName(f.Name, f.NameStart, f.NameEnd)) return;
            if (f.TrailingRet != null) { Err("trailing return types are not supported in JavaFarmer", f.TrailingRet); return; }
            var ret = Resolve(f.Ret);
            if (ret.IsError) return;
            if (ret.Kind == CK.Any) { Err("functions returning 'auto' are not supported in JavaFarmer: write the return type", f.Ret); return; }
            if (ret.Kind == CK.Array) { Err("'" + f.Name + "' declared as function returning an array", f.NameStart, f.NameEnd); return; }
            if (f.Variadic) { Err("variadic functions are not supported in JavaFarmer", f); return; }
            bool isMain = f.Name == "main";
            if (isMain)
            {
                if (ret.Kind != CK.Int) { Err(C ? "return type of 'main' is not 'int'" : "'::main' must return 'int'", f.Ret); return; }
                if (f.Params.Count > 0) { Err("main with parameters is not supported in JavaFarmer (use int main(" + (C ? "void" : "") + "))", f.Params[0].Start, f.Params[f.Params.Count - 1].End); return; }
                if (f.Body == null) return;
                if (functions.TryGetValue("main", out var pm) && pm.Defined) { Err("redefinition of 'main'", f.NameStart, f.NameEnd); return; }
            }
            var ps = new List<CT>();
            var names = new List<(string, int, int)>();
            var defaults = new List<IrExpr>();
            foreach (var p in f.Params)
            {
                var pt = Resolve(p.Type, p.D, true);
                if (pt.IsError) return;
                if (pt.Kind == CK.Void) { Err("'void' must be the only parameter", p); return; }
                if (pt.Kind == CK.Any) { Err("'auto' parameters are not supported in JavaFarmer", p.Type); return; }
                if (p.D != null && p.D.Ref && !Cpp) return;
                ps.Add(pt);
                names.Add((p.D?.Name, p.D?.NameStart ?? p.Start, p.D?.NameEnd ?? p.End));
                defaults.Add(null);
            }
            if (C && f.EmptyParams && f.Body == null && !isMain)
            {
                // C23: () is (void)
            }
            if (globals.Names.TryGetValue(f.Name, out var clash) && clash.Kind != SymKind.Function)
            {
                Err("'" + f.Name + "' redeclared as different kind of symbol", f.NameStart, f.NameEnd);
                return;
            }
            if (GameApi.Find(f.Name) != null && Farm && !isMain)
            {
                Err("conflicting types for '" + f.Name + "': " + StubName + " already declares this game function", f.NameStart, f.NameEnd);
                return;
            }
            if (!functions.TryGetValue(f.Name, out var sig))
            {
                sig = new FnSig { Name = f.Name, Ret = ret, Params = ps, IsStatic = f.Static, IsMain = isMain, DefinedHere = definedHere.Contains(f.Name), ProtoStart = f.NameStart, ProtoEnd = f.NameEnd };
                sig.Defaults = defaults;
                if (sig.DefinedHere || f.Body != null)
                {
                    sig.Ir = new IrSymbol(f.Name, IrSymbolKind.Function, IrType.Func(ret.Ir(), ps.Select(x => x.Ir()).ToArray())) { ParamTypes = ps.Select(x => x.Ir()).ToList(), DeclSpan = Sp(f.NameStart, f.NameEnd) };
                }
                else
                {
                    // a prototype of a function of another code window
                    var o = others?.FindFunction(f.Name, fileName, this);
                    if (o == null)
                    {
                        sig.Unresolved = true;
                        if (others != null && others.ForeignErrors.Count > 0) sig.ForeignError = others.ForeignErrors[0];
                        sig.Ir = new IrSymbol(f.Name, IrSymbolKind.ExternalFunction, IrType.Func(ret.Ir())) { Module = "?", DeclSpan = Sp(f.NameStart, f.NameEnd) };
                    }
                    else
                    {
                        var foreign = others.ForeignOf(o.Value.Module);
                        bool paramsOk = foreign != null
                            ? ps.Count <= o.Value.Sig.Params.Count && ps.Count >= Math.Max(0, o.Value.Sig.ForeignMinArgs) && o.Value.Sig.Params.Take(ps.Count).Zip(ps, ForeignCompatible).All(x => x)
                            : o.Value.Sig.Params.Count == ps.Count && o.Value.Sig.Params.Zip(ps, (a, b) => a.Same(b)).All(x => x);
                        bool retOk = foreign != null ? ForeignCompatible(o.Value.Sig.Ret, ret) : o.Value.Sig.Ret.Same(ret);
                        if (!retOk || !paramsOk)
                            Err("conflicting types for '" + f.Name + "'; the code window " + o.Value.Module + (foreign != null ? " (" + foreign + ")" : "") + " defines " + SigText(o.Value.Sig), f.NameStart, f.NameEnd);
                        sig.Module = o.Value.Module;
                        sig.Ir = new IrSymbol(f.Name, IrSymbolKind.ExternalFunction, IrType.Func(ret.Ir())) { Module = o.Value.Module, PyName = o.Value.PyName, ParamTypes = ps.Select(x => x.Ir()).ToList(), DeclSpan = Sp(f.NameStart, f.NameEnd) };
                    }
                }
                functions[f.Name] = sig;
                globals.Names[f.Name] = new Sym { Name = f.Name, Kind = SymKind.Function, Type = CT.Func(ret, ps), Fn = sig, Ir = sig.Ir };
            }
            else
            {
                if (sig.Defined && f.Body != null) { Err("redefinition of '" + f.Name + "'", f.NameStart, f.NameEnd); return; }
                bool same = sig.Ret.Same(ret) && sig.Params.Count == ps.Count;
                if (same) for (int i = 0; i < ps.Count; i++) if (!sig.Params[i].Same(ps[i])) same = false;
                if (!same) { Err("conflicting types for '" + f.Name + "'", f.NameStart, f.NameEnd); return; }
                if (f.Static) sig.IsStatic = true;
            }
            if (f.Body == null)
            {
                // C++ default arguments on the prototype
                if (Cpp) for (int i = 0; i < f.Params.Count; i++) if (f.Params[i].Default != null) sig.Defaults[i] = BindTo(f.Params[i].Default, ps[i]).E;
                return;
            }
            sig.Defined = true;
            var fn = new IrFunction
            {
                Symbol = sig.Ir, ReturnType = ret.Ir(), IsEntry = isMain, IsPrivate = f.Static, Span = Sp(f),
                HeaderSpan = Sp(f.HeaderStart, f.HeaderEnd), NameSpan = Sp(f.NameStart, f.NameEnd)
            };
            sig.Ir.Function = fn;
            // body scope
            var outer = cur;
            cur = new Scope { Parent = globals, FunctionTop = true };
            curFn = sig; curRet = ret; inEntry = isMain; loopDepth = switchDepth = 0;
            var copies = new List<IrStmt>();
            for (int i = 0; i < f.Params.Count; i++)
            {
                var p = f.Params[i];
                var name = p.D?.Name;
                IrExpr dflt = null;
                if (p.Default != null)
                {
                    var db = BindTo(p.Default, ps[i]);
                    if (db.T.IsError) { cur = outer; return; }
                    dflt = db.E;
                    sig.Defaults[i] = dflt;
                }
                else if (Cpp && i > 0 && f.Params[i - 1].Default != null)
                {
                    Err("default argument missing for parameter " + (i + 1) + " of '" + f.Name + "'", p);
                }
                if (name == null) { Err(C ? "parameter name omitted" : "unnamed parameters are not supported in JavaFarmer", p); continue; }
                if (!CheckName(name, p.D.NameStart, p.D.NameEnd)) continue;
                if (cur.Names.ContainsKey(name)) { Err("redefinition of parameter '" + name + "'", p.D.NameStart, p.D.NameEnd); continue; }
                var irs = new IrSymbol(name, IrSymbolKind.Param, ps[i].Ir()) { DeclSpan = Sp(p.D.NameStart, p.D.NameEnd), IsConst = p.Type.Const };
                if (p.D.Ref || p.Type.Ref) irs.Tag = RefTag;
                fn.Params.Add(new IrParam { Symbol = irs, Span = Sp(p), NameSpan = Sp(p.D.NameStart, p.D.NameEnd), Default = dflt });
                var psym = new Sym { Name = name, Kind = SymKind.Param, Type = ps[i], Ir = irs, Const = p.Type.Const && !p.D.Ref && p.Type.Pointers + p.D.Pointers == 0, ArrayParam = ps[i].Kind == CK.Array, RefBinding = p.D.Ref || p.Type.Ref, DeclStart = p.Start };
                cur.Names[name] = psym;
                // C++ passes containers by value: a function that modifies its copy must not change the caller's
                if (Cpp && !psym.RefBinding && (ps[i].Kind == CK.List || ps[i].Kind == CK.Set || ps[i].Kind == CK.Map) && Mutates(f.Body, name))
                {
                    var op = ps[i].Kind == CK.List ? IrOp.ListCopy : ps[i].Kind == CK.Set ? IrOp.SetCopy : IrOp.MapCopy;
                    var copy = new IrOpCall(op, new IrName(irs) { Synthetic = true, Span = Sp(p) }) { Synthetic = true, Span = Sp(p), Type = ps[i].Ir() };
                    copies.Add(new IrAssign { Target = new IrName(irs) { Synthetic = true, Span = Sp(p) }, Value = copy, Synthetic = true, Span = Sp(p) });
                }
            }
            fn.Body = new IrBlock { Span = Sp(f.Body) };
            fn.Body.Stmts.AddRange(copies);
            BlockInto(f.Body, fn.Body.Stmts, false);
            // drop the final "return 0;" of main
            if (isMain && fn.Body.Stmts.Count > 0 && fn.Body.Stmts[fn.Body.Stmts.Count - 1] is IrReturn last && last.Value == null && IsFinalReturnZero(f.Body))
                fn.Body.Stmts.RemoveAt(fn.Body.Stmts.Count - 1);
            if (!isMain && ret.Kind != CK.Void && CanComplete(f.Body))
                Err("control reaches end of non-void function '" + f.Name + "' (add a return statement)", f.Body.End - 1, f.Body.End);
            cur = outer;
            curFn = null; curRet = null; inEntry = false;
            mod.Items.Add(fn);
        }

        /// <summary>A foreign declaration's type against the prototype's: unknown types (dynamic languages) accept anything.</summary>
        static bool ForeignCompatible(CT foreign, CT proto)
        {
            if (foreign == null || foreign.Kind == CK.Any) return true;
            if (foreign.Kind == CK.Optional) foreign = foreign.Elem;
            if (proto.Kind == CK.Optional) proto = proto.Elem;
            if (foreign.Kind == CK.Func && proto.Kind == CK.Func) return true;
            if ((foreign.Kind == CK.List || foreign.Kind == CK.Array) && (proto.Kind == CK.List || proto.Kind == CK.Array)) return foreign.Elem.Kind == CK.Any || ForeignCompatible(foreign.Elem, proto.Elem);
            return foreign.Same(proto);
        }

        static bool IsFinalReturnZero(Block b) =>
            b.Stmts.Count > 0 && b.Stmts[b.Stmts.Count - 1] is ReturnStmt r && r.Value is Lit l && l.Kind == LitKind.Int && l.I == 0;

        string SigText(FnSig s) => Show(s.Ret) + " " + s.Name + "(" + (s.Params.Count == 0 && C ? "void" : string.Join(", ", s.Params.Select(Show))) + ")";

        /// <summary>Whether the body may modify the container named name (method calls that mutate, element assignment).</summary>
        static bool Mutates(Node body, string name)
        {
            bool found = false;
            void Walk(object n)
            {
                if (found || n == null) return;
                switch (n)
                {
                    case CallExpr c:
                        if (c.Callee is MemberExpr me && me.Target is NameExpr tn && tn.Name == name && tn.Qual.Count == 0 &&
                            me.Name != "size" && me.Name != "empty" && me.Name != "at" && me.Name != "count" && me.Name != "contains" && me.Name != "find" && me.Name != "begin" && me.Name != "end" && me.Name != "front" && me.Name != "back" && me.Name != "length")
                            found = true;
                        Walk(c.Callee); foreach (var a in c.Args) Walk(a);
                        return;
                    case AssignExpr a:
                        if (a.Target is IndexExpr ix && ix.Target is NameExpr xn && xn.Name == name) found = true;
                        if (a.Target is NameExpr an && an.Name == name) found = true;
                        Walk(a.Target); Walk(a.Value);
                        return;
                    case IncDecExpr id:
                        if (id.Target is IndexExpr ix2 && ix2.Target is NameExpr xn2 && xn2.Name == name) found = true;
                        return;
                    case RangeForStmt rf:
                        if (rf.Range is NameExpr rn && rn.Name == name && (rf.Var?.Ref ?? false)) found = true;
                        Walk(rf.Range); Walk(rf.Body);
                        return;
                }
                foreach (var ch in AstChildren(n)) Walk(ch);
            }
            Walk(body);
            return found;
        }

        internal static IEnumerable<object> AstChildren(object n)
        {
            switch (n)
            {
                case Block b: foreach (var s in b.Stmts) yield return s; break;
                case ExprStmt es: yield return es.E; break;
                case VarDecl vd: foreach (var v in vd.Vars) { if (v.Init != null) yield return v.Init; if (v.CtorArgs != null) foreach (var a in v.CtorArgs) yield return a; } break;
                case BindingDecl bd: yield return bd.Init; break;
                case IfStmt i: yield return i.Cond; yield return i.Then; yield return i.Else; break;
                case WhileStmt w: yield return w.Cond; yield return w.Body; break;
                case DoWhileStmt dw: yield return dw.Body; yield return dw.Cond; break;
                case ForStmt f: yield return f.Init; yield return f.Cond; foreach (var u in f.Update) yield return u; yield return f.Body; break;
                case RangeForStmt rf: yield return rf.Range; yield return rf.Body; break;
                case SwitchStmt sw: yield return sw.Sel; yield return sw.Body; break;
                case ReturnStmt r: yield return r.Value; break;
                case BinaryExpr be: yield return be.L; yield return be.R; break;
                case UnaryExpr ue: yield return ue.E; break;
                case IncDecExpr ide: yield return ide.Target; break;
                case AssignExpr ae: yield return ae.Target; yield return ae.Value; break;
                case CondExpr ce: yield return ce.C; yield return ce.T; yield return ce.F; break;
                case CallExpr call: yield return call.Callee; foreach (var a in call.Args) yield return a; break;
                case IndexExpr ix: yield return ix.Target; yield return ix.Index; break;
                case MemberExpr me: yield return me.Target; break;
                case CastExpr ca: yield return ca.E; break;
                case InitList il: foreach (var x in il.Items) yield return x; break;
                case ConstructExpr cn: foreach (var x in cn.Args) yield return x; break;
                case LambdaExpr le: yield return le.Body; break;
                case SizeofExpr so: yield return so.E; break;
                case CommaExpr cm: foreach (var x in cm.Items) yield return x; break;
            }
        }

        /// <summary>gcc -Wreturn-type approximation: can control reach the closing brace?</summary>
        static bool CanComplete(Stmt s)
        {
            switch (s)
            {
                case null: return true;
                case ReturnStmt _: return false;
                case Block b:
                    foreach (var x in b.Stmts) if (!CanComplete(x)) return false;
                    return true;
                case IfStmt i: return i.Else == null || CanComplete(i.Then) || CanComplete(i.Else);
                case WhileStmt w: return !(IsTrue(w.Cond)) || HasBreak(w.Body);
                case ForStmt f: return !(f.Cond == null || IsTrue(f.Cond)) || HasBreak(f.Body);
                case DoWhileStmt dw: return !IsTrue(dw.Cond) ? CanComplete(dw.Body) || HasBreak(dw.Body) || true : HasBreak(dw.Body);
                case SwitchStmt sw:
                    {
                        bool hasDefault = sw.Body.Stmts.Any(x => x is CaseStmt c && c.IsDefault);
                        if (!hasDefault) return true;
                        if (HasBreak(sw.Body, true)) return true;
                        return CanComplete(sw.Body.Stmts.Count > 0 ? sw.Body.Stmts[sw.Body.Stmts.Count - 1] : null) && !(sw.Body.Stmts.LastOrDefault() is ReturnStmt);
                    }
            }
            return true;
        }

        static bool IsTrue(Expr e) => e is Lit l && ((l.Kind == LitKind.Bool && l.B) || (l.Kind == LitKind.Int && l.I != 0));

        /// <summary>A break that leaves this loop (not one of a nested loop/switch).</summary>
        static bool HasBreak(Stmt s, bool switchLevel = false)
        {
            switch (s)
            {
                case null: return false;
                case BreakStmt _: return true;
                case WhileStmt _: case DoWhileStmt _: case ForStmt _: case RangeForStmt _: return false;
                case SwitchStmt sw: return switchLevel ? false : false;
                case Block b: foreach (var x in b.Stmts) if (HasBreak(x, switchLevel)) return true; return false;
                case IfStmt i: return HasBreak(i.Then, switchLevel) || HasBreak(i.Else, switchLevel);
            }
            return false;
        }

        // ================================================================== statements

        void Push() => cur = new Scope { Parent = cur };
        void Pop() => cur = cur.Parent;

        Sym Lookup(string name)
        {
            for (var s = cur; s != null; s = s.Parent)
                if (s.Names.TryGetValue(name, out var sym)) return sym;
            return null;
        }

        IrBlock BlockOf(Stmt s)
        {
            var b = new IrBlock { Span = Sp(s) };
            if (s is Block blk) BlockInto(blk, b.Stmts, true);
            else
            {
                Push();
                Stmt(s, b.Stmts);
                Pop();
            }
            return b;
        }

        void BlockInto(Block b, List<IrStmt> outList, bool scope)
        {
            if (scope) Push();
            foreach (var s in b.Stmts) Stmt(s, outList);
            if (scope) Pop();
        }

        void Stmt(Stmt s, List<IrStmt> outList)
        {
            switch (s)
            {
                case null: return;
                case Block b:
                    {
                        var blk = new IrBlock { Span = Sp(b) };
                        BlockInto(b, blk.Stmts, true);
                        outList.Add(blk);
                        return;
                    }
                case EmptyStmt _: return;
                case UnsupportedStmt us: Err(us.Message, us); return;
                case VarDecl vd: LocalDecl(vd, outList); return;
                case BindingDecl bd: Binding(bd, outList); return;
                case ExprStmt es: ExprStatement(es, outList); return;
                case IfStmt i: outList.Add(If(i)); return;
                case WhileStmt w:
                    {
                        var c = Cond(w.Cond);
                        loopDepth++;
                        var body = BlockOf(w.Body);
                        loopDepth--;
                        outList.Add(new IrWhile { Cond = c, Body = body, KwSpan = Sp(w.Start, w.KwEnd), Span = Sp(w) });
                        return;
                    }
                case DoWhileStmt dw:
                    {
                        loopDepth++;
                        var body = BlockOf(dw.Body);
                        loopDepth--;
                        var c = Cond(dw.Cond);
                        outList.Add(new IrDoWhile { Body = body, Cond = c, KwSpan = Sp(dw.Start, dw.Start + 2), WhileSpan = Sp(dw.WhileStart, dw.WhileEnd), Span = Sp(dw) });
                        return;
                    }
                case ForStmt f: outList.Add(For(f)); return;
                case RangeForStmt rf: { var x = RangeFor(rf); if (x != null) outList.Add(x); return; }
                case SwitchStmt sw: { var x = Switch(sw); if (x != null) outList.Add(x); return; }
                case CaseStmt cs: Err(cs.IsDefault ? "'default' label not within a switch statement" : "case label not within a switch statement", cs); return;
                case BreakStmt br:
                    if (loopDepth == 0 && switchDepth == 0) { Err("break statement not within loop or switch", br); return; }
                    outList.Add(new IrBreak { Span = Sp(br) });
                    return;
                case ContinueStmt co:
                    if (loopDepth == 0) { Err("continue statement not within a loop", co); return; }
                    outList.Add(new IrContinue { Span = Sp(co) });
                    return;
                case ReturnStmt r: { var x = Return(r); if (x != null) outList.Add(x); return; }
            }
            Err("unsupported statement", s);
        }

        IrStmt Return(ReturnStmt r)
        {
            var ret = lambdaDepth > 0 ? lambdaRets[lambdaRets.Count - 1] : curRet;
            if (ret == null) { Err("return outside a function", r); return null; }
            if (lambdaDepth > 0 && ret.Kind == CK.Any)
            {
                // lambda return type deduction
                if (r.Value == null) { lambdaRets[lambdaRets.Count - 1] = CT.Void; return new IrReturn { Span = Sp(r) }; }
                var bv = BindExpr(r.Value);
                if (bv.T.IsError) return null;
                lambdaRets[lambdaRets.Count - 1] = Decay(bv.T);
                return new IrReturn { Value = bv.E, Span = Sp(r) };
            }
            if (r.Value == null)
            {
                if (ret.Kind != CK.Void && !(inEntry && lambdaDepth == 0)) { Err(C ? "'return' with no value, in function returning non-void" : "return-statement with no value, in function returning '" + Show(ret) + "'", r); return null; }
                return new IrReturn { Span = Sp(r) };
            }
            if (ret.Kind == CK.Void)
            {
                Err(C ? "'return' with a value, in function returning void" : "return-statement with a value, in function returning 'void'", r);
                return null;
            }
            var b = BindTo(r.Value, ret);
            if (b.T.IsError) return null;
            // main's exit status has no meaning in the game
            if (inEntry && lambdaDepth == 0) return new IrReturn { Span = Sp(r) };
            return new IrReturn { Value = b.E, Span = Sp(r) };
        }

        IrIf If(IfStmt i)
        {
            if (i.Constexpr) Err("if constexpr is not supported in JavaFarmer", i);
            var c = Cond(i.Cond);
            var res = new IrIf { Cond = c, Then = BlockOf(i.Then), KwSpan = Sp(i.Start, i.KwEnd), Span = Sp(i) };
            if (i.Else is IfStmt ei) res.Else = If(ei);
            else if (i.Else != null) res.Else = BlockOf(i.Else);
            return res;
        }

        IrStmt For(ForStmt f)
        {
            Push();
            var cf = new IrClassicFor { KwSpan = Sp(f.Start, f.KwEnd), HeaderSpan = Sp(f.Start, f.HeaderEnd), Span = Sp(f) };
            if (f.Init is VarDecl vd) LocalDecl(vd, cf.Init);
            else if (f.Init is ExprStmt es)
            {
                if (es.E is CommaExpr ce) foreach (var x in ce.Items) ExprStatementCore(x, Sp(x), cf.Init, true);
                else ExprStatementCore(es.E, Sp(es.E), cf.Init, true);
            }
            if (f.Cond != null) cf.Cond = Cond(f.Cond);
            foreach (var u in f.Update) ExprStatementCore(u, Sp(u), cf.Update, true);
            loopDepth++;
            cf.Body = BlockOf(f.Body);
            loopDepth--;
            Pop();
            return cf;
        }

        // ------------------------------------------------------------------ declarations

        void LocalDecl(VarDecl vd, List<IrStmt> outList)
        {
            if (vd.Extern) { Err("extern declarations inside functions are not supported in JavaFarmer", vd); return; }
            if (vd.Static) { Err("static local variables are not supported in JavaFarmer (use a global variable)", vd); return; }
            foreach (var dc in vd.Vars)
            {
                if (dc.Name == null) continue;
                if (!CheckName(dc.Name, dc.NameStart, dc.NameEnd)) continue;
                var t = Resolve(vd.Type, dc);
                if (t.IsError) continue;
                if (t.Kind == CK.Void) { Err("variable or field '" + dc.Name + "' declared void", dc.NameStart, dc.NameEnd); continue; }
                if (cur.Names.TryGetValue(dc.Name, out var prev) || (cur.Parent != null && cur.Parent.FunctionTop && cur.FunctionTop == false && false))
                {
                    Err(C ? "redeclaration of '" + dc.Name + "' with no linkage" : "redeclaration of '" + Show(t) + " " + dc.Name + "'", dc.NameStart, dc.NameEnd);
                    continue;
                }
                if (Cpp && cur.Parent != null && cur.Parent.FunctionTop && cur.Parent.Names.TryGetValue(dc.Name, out var pp) && pp.Kind == SymKind.Param && cur.FunctionTop)
                {
                    Err("declaration of '" + Show(t) + " " + dc.Name + "' shadows a parameter", dc.NameStart, dc.NameEnd);
                    continue;
                }
                if (cur.FunctionTop && cur.Names.TryGetValue(dc.Name, out var par) && par.Kind == SymKind.Param)
                {
                    Err(C ? "'" + dc.Name + "' redeclared as different kind of symbol" : "declaration of '" + Show(t) + " " + dc.Name + "' shadows a parameter", dc.NameStart, dc.NameEnd);
                    continue;
                }
                bool isConst = (vd.Type.Const || vd.Constexpr) && vd.Type.Pointers + dc.Pointers == 0;
                if (isConst && dc.Init == null && dc.CtorArgs == null && t.Kind != CK.Array)
                {
                    if (Cpp || vd.Constexpr) { Err(Cpp ? "uninitialized 'const " + dc.Name + "'" : "'constexpr' object '" + dc.Name + "' requires an initializer", dc.NameStart, dc.NameEnd); continue; }
                }
                if (t.Kind == CK.Any && t.Game == "auto" && dc.Init == null && dc.CtorArgs == null) { Err("declaration of 'auto " + dc.Name + "' has no initializer", dc.NameStart, dc.NameEnd); continue; }
                bool isRef = dc.Ref || vd.Type.Ref;
                if (isRef && dc.Init == null) { Err("'" + dc.Name + "' declared as reference but not initialized", dc.NameStart, dc.NameEnd); continue; }
                // the initialiser cannot see the new name
                var init = InitValue(t, dc, out var actual);
                if (init == null && (dc.Init != null || dc.CtorArgs != null || t.Kind == CK.Array)) continue;
                if (t.Kind == CK.Any && t.Game == "auto") t = actual;
                var irs = new IrSymbol(dc.Name, IrSymbolKind.Local, t.Ir()) { IsConst = isConst, DeclSpan = Sp(dc.NameStart, dc.NameEnd) };
                var sym = new Sym { Name = dc.Name, Kind = SymKind.Local, Type = t, Ir = irs, Const = isConst, RefBinding = isRef, DeclStart = dc.Start };
                sym.ConstantExpr = vd.Constexpr && init is IrLiteral;
                cur.Names[dc.Name] = sym;
                var decl = new IrVarDecl { Symbol = irs, Init = init, NameSpan = Sp(dc.NameStart, dc.NameEnd), Span = Sp(vd.Vars.Count == 1 ? (Node)vd : dc), TypeExplicit = !(vd.Type.Name == "auto") };
                if (vd.Vars.Count > 1 && vd.Vars[0] == dc) decl.Span = new Span(vd.Start, dc.End);
                outList.Add(decl);
            }
        }

        void Binding(BindingDecl bd, List<IrStmt> outList)
        {
            if (C) { Err("structured bindings are not supported in C", bd); return; }
            if (bd.Type.Name != "auto") { Err("structured binding declaration cannot have type '" + bd.Type.Written + "'", bd.Type); return; }
            var v = BindExpr(bd.Init);
            if (v.T.IsError) return;
            var t = v.T;
            List<(IrExpr E, CT T)> parts = new List<(IrExpr, CT)>();
            IrExpr holder = v.E;
            if (!(v.E is IrName))
            {
                // evaluate once into a hidden local
                var tmp = new IrSymbol(bd.Names[0].Name + "_pair", IrSymbolKind.Local, t.Ir());
                outList.Add(new IrVarDecl { Symbol = tmp, Init = v.E, Span = Sp(bd), Synthetic = true, NameSpan = Sp(bd.Names[0].Start, bd.Names[0].End) });
                holder = null;
                Func<IrExpr> h = () => new IrName(tmp) { Synthetic = true, Span = Sp(bd) };
                if (!BindingParts(t, h, bd, parts)) return;
            }
            else
            {
                var hn = (IrName)v.E;
                Func<IrExpr> h = () => new IrName(hn.Symbol) { Span = hn.Span, Type = hn.Type };
                if (!BindingParts(t, h, bd, parts)) return;
            }
            for (int i = 0; i < bd.Names.Count; i++)
            {
                var (name, s, e) = bd.Names[i];
                if (!CheckName(name, s, e)) return;
                if (cur.Names.ContainsKey(name)) { Err("redeclaration of '" + name + "'", s, e); return; }
                var irs = new IrSymbol(name, IrSymbolKind.Local, parts[i].T.Ir()) { DeclSpan = Sp(s, e) };
                cur.Names[name] = new Sym { Name = name, Kind = SymKind.Local, Type = parts[i].T, Ir = irs, DeclStart = s };
                outList.Add(new IrVarDecl { Symbol = irs, Init = parts[i].E, NameSpan = Sp(s, e), Span = Sp(bd), TypeExplicit = false });
            }
        }

        bool BindingParts(CT t, Func<IrExpr> holder, BindingDecl bd, List<(IrExpr, CT)> parts)
        {
            if (t.Kind == CK.Position)
            {
                if (bd.Names.Count != 2) { Err("only 2 names provided for structured binding" == "" ? "" : "type 'Position' decomposes into 2 elements, but " + bd.Names.Count + " names were provided", bd); return false; }
                parts.Add((Op(IrOp.PosX, CT.Int, bd, holder()), CT.Int));
                parts.Add((Op(IrOp.PosY, CT.Int, bd, holder()), CT.Int));
                return true;
            }
            if (t.Kind == CK.Companion)
            {
                if (bd.Names.Count != 2) { Err("type 'Companion' decomposes into 2 elements, but " + bd.Names.Count + " names were provided", bd); return false; }
                parts.Add((Op(IrOp.CompanionEntity, CT.Handle("Entity"), bd, holder()), CT.Handle("Entity")));
                parts.Add((Op(IrOp.CompanionPos, CT.Position, bd, holder()), CT.Position));
                return true;
            }
            Err("cannot decompose " + Show(t) + " in JavaFarmer (structured bindings work on Position, Companion and in range-for over maps)", bd);
            return false;
        }

        IrOpCall Op(IrOp op, CT t, Node at, params IrExpr[] args) => new IrOpCall(op, args) { Type = t.Ir(), Span = Sp(at), NameSpan = Sp(at) };

        // ------------------------------------------------------------------ range-for / for_each

        IrStmt RangeFor(RangeForStmt rf)
        {
            if (rf.Macro && !Farm) { Err("implicit declaration of function 'for_each'; did you forget '#include \"farm.h\"'?", rf.Start, rf.KwEnd); return null; }
            var range = BindExpr(rf.Range, rf.Macro ? "for_each" : "range");
            if (range.T.IsError) return null;
            var rt = range.T;
            if (rt.Kind == CK.Optional) { Err("cannot iterate over " + Show(rt) + " (dereference it: *" + Text(rf.Range) + ")", rf.Range); return null; }
            CT elem;
            switch (rt.Kind)
            {
                case CK.List: case CK.Array: case CK.Set: elem = rt.Elem; break;
                case CK.Map: elem = rt.Key; break;
                case CK.Str:
                    Err("iterating over the characters of a string is not supported in JavaFarmer", rf.Range);
                    return null;
                default:
                    Err(rf.Macro ? "for_each needs a List, Set or Map, not " + Show(rt) : "'" + Text(rf.Range) + "' of type " + Show(rt) + " cannot be used in a range-based for loop", rf.Range);
                    return null;
            }
            if (rt.Kind == CK.Array && C && !rf.Macro) { }
            if (rf.Macro && rt.Kind == CK.Array) { Err("for_each works on List, Set and Map; loop over an array with an index", rf.Range); return null; }
            Push();
            var fe = new IrForEach { Iterable = range.E, KwSpan = Sp(rf.Start, rf.KwEnd), Span = Sp(rf) };
            if (rf.Binding != null)
            {
                var bd = rf.Binding;
                if (bd.Type.Name != "auto") { Err("structured binding declaration cannot have type '" + bd.Type.Written + "'", bd.Type); Pop(); return null; }
                if (rt.Kind != CK.Map) { Err("cannot decompose the elements of " + Show(rt) + " in JavaFarmer (range-for bindings work on maps)", rf.Range); Pop(); return null; }
                if (bd.Names.Count != 2) { Err("std::pair decomposes into 2 elements, but " + bd.Names.Count + " names were provided", bd); Pop(); return null; }
                var (kn, ks, ke) = bd.Names[0];
                var (vn, vs, ve) = bd.Names[1];
                if (!CheckName(kn, ks, ke) || !CheckName(vn, vs, ve)) { Pop(); return null; }
                var kSym = new IrSymbol(kn, IrSymbolKind.Local, rt.Key.Ir()) { DeclSpan = Sp(ks, ke), IsConst = true };
                var vSym = new IrSymbol(vn, IrSymbolKind.Local, rt.Elem.Ir()) { DeclSpan = Sp(vs, ve) };
                cur.Names[kn] = new Sym { Name = kn, Kind = SymKind.Local, Type = rt.Key, Ir = kSym, Const = true, LoopRef = bd.Type.Ref };
                cur.Names[vn] = new Sym { Name = vn, Kind = SymKind.Local, Type = rt.Elem, Ir = vSym, LoopRef = bd.Type.Ref, Const = bd.Type.Const };
                fe.Var = kSym; fe.ValueVar = vSym;
                fe.VarSpan = Sp(ks, ke); fe.ValueVarSpan = Sp(vs, ve);
            }
            else
            {
                var dc = rf.Var;
                var vt = Resolve(rf.Type, dc);
                if (vt.IsError) { Pop(); return null; }
                if (!CheckName(dc.Name, dc.NameStart, dc.NameEnd)) { Pop(); return null; }
                bool isRef = dc.Ref || rf.Type.Ref;
                if (vt.Kind == CK.Any && vt.Game == "auto") vt = elem;
                if (rt.Kind == CK.Map && !rf.Macro)
                {
                    // for (auto& p : m): p.first / p.second
                    if (!(rf.Type.Name == "auto")) { Err("the elements of " + Show(rt) + " are std::pair: write for (auto& [key, value] : " + Text(rf.Range) + ")", rf.Type); Pop(); return null; }
                    var kSym = new IrSymbol(dc.Name, IrSymbolKind.Local, rt.Key.Ir()) { DeclSpan = Sp(dc.NameStart, dc.NameEnd), IsConst = true };
                    var vSym = new IrSymbol(dc.Name + "_second", IrSymbolKind.Local, rt.Elem.Ir()) { DeclSpan = Sp(dc.NameStart, dc.NameEnd) };
                    var ps = new Sym { Name = dc.Name, Kind = SymKind.Local, Type = new CT { Kind = CK.Any, Game = "pair", Key = rt.Key, Elem = rt.Elem }, Ir = kSym, Const = true, LoopRef = isRef };
                    ps.PairFirst = () => new IrName(kSym) { Type = rt.Key.Ir() };
                    ps.PairSecond = () => new IrName(vSym) { Type = rt.Elem.Ir() };
                    cur.Names[dc.Name] = ps;
                    fe.Var = kSym; fe.ValueVar = vSym;
                    fe.VarSpan = Sp(dc.NameStart, dc.NameEnd);
                }
                else
                {
                    if (!vt.Same(elem) && !(vt.IsNumeric && elem.IsNumeric) && !(vt.IsStr && elem.IsStr))
                    {
                        Err(C ? "for_each: the variable has type " + Show(vt) + " but the elements are " + Show(elem) : "invalid initialization of '" + Show(vt) + "' from an element of type '" + Show(elem) + "'", rf.Type.Start, dc.End);
                        Pop(); return null;
                    }
                    if (vt.Kind == CK.Int && elem.Kind == CK.Float) { Err("JavaFarmer: the elements are double; declare the loop variable as double", rf.Type); Pop(); return null; }
                    var irs = new IrSymbol(dc.Name, IrSymbolKind.Local, vt.Ir()) { DeclSpan = Sp(dc.NameStart, dc.NameEnd), IsConst = rf.Type.Const };
                    cur.Names[dc.Name] = new Sym { Name = dc.Name, Kind = SymKind.Local, Type = vt, Ir = irs, Const = rf.Type.Const, LoopRef = isRef };
                    fe.Var = irs;
                    fe.VarSpan = Sp(dc.NameStart, dc.NameEnd);
                }
            }
            // C for_each over map_keys(m): iterate the map directly
            if (rf.Macro && fe.Iterable is IrOpCall lc && lc.Op == IrOp.ListCopy && lc.Args.Count == 1 && lc.Args[0].Type.Kind == IrTypeKind.Map && lc.Synthetic)
                fe.Iterable = new IrOpCall(IrOp.MapKeys, lc.Args[0]) { Span = lc.Span, NameSpan = lc.NameSpan, Type = IrType.Set(lc.Args[0].Type.Key) };
            loopDepth++;
            fe.Body = BlockOf(rf.Body);
            loopDepth--;
            Pop();
            return fe;
        }

        // ------------------------------------------------------------------ switch

        IrStmt Switch(SwitchStmt sw)
        {
            var sel = BindExpr(sw.Sel);
            if (sel.T.IsError) return null;
            if (sel.T.Kind != CK.Int && sel.T.Kind != CK.Bool)
            {
                Err(C ? "switch quantity not an integer" : "switch quantity not an integer (switch over " + Show(sel.T) + " is not allowed; use if / else if)", sw.Sel);
                return null;
            }
            var res = new IrSwitch { Selector = sel.E, KwSpan = Sp(sw.Start, sw.KwEnd), Span = Sp(sw), Style = IrSwitchStyle.Colon };
            var stmts = sw.Body.Stmts;
            int i = 0;
            if (stmts.Count > 0 && !(stmts[0] is CaseStmt))
            {
                Err("statements before the first case label are never executed (not supported in JavaFarmer)", stmts[0]);
                return null;
            }
            var seen = new HashSet<long>();
            bool sawDefault = false;
            switchDepth++;
            Push();
            while (i < stmts.Count)
            {
                var ic = new IrCase();
                int labelStart = stmts[i].Start, labelEnd = 0;
                var labels = new List<CaseStmt>();
                while (i < stmts.Count && stmts[i] is CaseStmt cs)
                {
                    labels.Add(cs);
                    labelEnd = cs.LabelEnd;
                    i++;
                }
                foreach (var cs in labels)
                {
                    if (cs.IsDefault)
                    {
                        if (sawDefault) { Err("multiple default labels in one switch", cs); continue; }
                        sawDefault = true;
                        ic.IsDefault = true;
                        continue;
                    }
                    if (!IsConstant(cs.Value)) { Err(C ? "case label does not reduce to an integer constant" : "the value of '" + Text(cs.Value) + "' is not usable in a constant expression", cs.Value); continue; }
                    var lv = BindExpr(cs.Value);
                    if (lv.T.IsError) continue;
                    if (lv.T.Kind != CK.Int && lv.T.Kind != CK.Bool) { Err("case label does not reduce to an integer constant", cs.Value); continue; }
                    long key = ConstValue(lv.E);
                    if (key != long.MinValue && !seen.Add(key)) { Err("duplicate case value", cs.Value); continue; }
                    ic.Labels.Add(lv.E);
                }
                int bodyStart = i;
                while (i < stmts.Count && !(stmts[i] is CaseStmt)) i++;
                var body = stmts.GetRange(bodyStart, i - bodyStart);
                bool last = i >= stmts.Count;
                // the case must end with break (dropped) / return / continue, except the last one
                bool endsBreak = body.Count > 0 && body[body.Count - 1] is BreakStmt;
                if (endsBreak) body.RemoveAt(body.Count - 1);
                else if (body.Count > 0 && body[body.Count - 1] is Block lastBlock && lastBlock.Stmts.Count > 0 && lastBlock.Stmts[lastBlock.Stmts.Count - 1] is BreakStmt)
                {
                    // case 2: { ...; break; }
                    var inner = new Block { Start = lastBlock.Start, End = lastBlock.End };
                    inner.Stmts.AddRange(lastBlock.Stmts.Take(lastBlock.Stmts.Count - 1));
                    body[body.Count - 1] = inner;
                    endsBreak = true;
                }
                else if (!last && (body.Count == 0 || CanCompleteCase(body)))
                {
                    if (body.Count > 0)
                    {
                        Err("fallthrough into the next case is not supported in JavaFarmer (end the case with break;)", stmts[i].Start, stmts[i].Start + 4);
                        break;
                    }
                }
                foreach (var b in body)
                    if (BreakLeavesSwitch(b)) { Err("break inside a switch case is only supported as the last statement of the case in JavaFarmer", FindBreak(b)); }
                ic.LabelSpan = Sp(labelStart, labelEnd);
                ic.Span = Sp(labelStart, body.Count > 0 ? body[body.Count - 1].End : labelEnd);
                ic.Body = new IrBlock { Span = ic.Span };
                Push();
                foreach (var b in body) Stmt(b, ic.Body.Stmts);
                Pop();
                res.Cases.Add(ic);
            }
            Pop();
            switchDepth--;
            // merge label-only cases (case 1: case 2: ...) into the following case
            var merged = new List<IrCase>();
            IrCase pending = null;
            foreach (var c in res.Cases)
            {
                if (pending != null) { c.Labels.InsertRange(0, pending.Labels); c.IsDefault |= pending.IsDefault; c.LabelSpan = Span.Cover(pending.LabelSpan, c.LabelSpan); pending = null; }
                merged.Add(c);
            }
            res.Cases = merged;
            return res;
        }

        /// <summary>Value of an integer constant expression in IR (literals, #define names, + - * / %); -1 if not constant.</summary>
        long ConstInt(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l when l.Kind == IrLitKind.Int: return l.Int;
                case IrName n when n.Symbol != null && Equals(n.Symbol.Tag, DefineTag):
                    foreach (var g in mod.Globals) if (g.Symbol == n.Symbol) return ConstInt(g.Init);
                    return -1;
                case IrUnary u when u.Op == IrUnOp.Neg: { var v = ConstInt(u.Operand); return v < 0 ? -1 : -v; }
                case IrBinary b:
                    {
                        long a = ConstInt(b.Left), c = ConstInt(b.Right);
                        if (a < 0 || c < 0) return -1;
                        switch (b.Op)
                        {
                            case IrBinOp.Add: return a + c;
                            case IrBinOp.Sub: return a - c;
                            case IrBinOp.Mul: return a * c;
                            case IrBinOp.IntDivTrunc: return c == 0 ? -1 : a / c;
                            case IrBinOp.ModTrunc: return c == 0 ? -1 : a % c;
                        }
                        return -1;
                    }
            }
            return -1;
        }

        static long ConstValue(IrExpr e)
        {
            if (e is IrLiteral l && l.Kind == IrLitKind.Int) return l.Int;
            if (e is IrLiteral b && b.Kind == IrLitKind.Bool) return b.Bool ? 1 : 0;
            if (e is IrUnary u && u.Op == IrUnOp.Neg && u.Operand is IrLiteral ul && ul.Kind == IrLitKind.Int) return -ul.Int;
            return long.MinValue;
        }

        static bool CanCompleteCase(List<Stmt> body)
        {
            foreach (var s in body) if (!CanComplete(s) || s is ContinueStmt) return false;
            return true;
        }

        static bool BreakLeavesSwitch(Stmt s)
        {
            switch (s)
            {
                case BreakStmt _: return true;
                case WhileStmt _: case DoWhileStmt _: case ForStmt _: case RangeForStmt _: case SwitchStmt _: return false;
                case Block b: foreach (var x in b.Stmts) if (BreakLeavesSwitch(x)) return true; return false;
                case IfStmt i: return BreakLeavesSwitch(i.Then) || BreakLeavesSwitch(i.Else);
            }
            return false;
        }

        static Node FindBreak(Stmt s)
        {
            switch (s)
            {
                case BreakStmt b: return b;
                case Block bl: foreach (var x in bl.Stmts) { var r = FindBreak(x); if (r != null) return r; } return null;
                case IfStmt i: return FindBreak(i.Then) ?? FindBreak(i.Else);
            }
            return null;
        }

        string Text(Node n) => n == null || n.Start < 0 || n.End > src.Length || n.End < n.Start ? "" : src.Substring(n.Start, n.End - n.Start);
    }
}
