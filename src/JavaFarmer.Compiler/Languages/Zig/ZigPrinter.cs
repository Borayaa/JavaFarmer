using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.ZigImpl
{
    /// <summary>
    /// Farm IR → idiomatic Zig 0.17 (docs/languages/zig.md). Accepts IR from any frontend: types come from the Zig
    /// frontend's symbol tags when present, else from JavaTypeGuess (inference for untyped Python IR) plus an int
    /// width decision (usize for counters / indexes, i32 otherwise). The output satisfies Zig's AstGen rules (const
    /// unless mutated, _ = x for unused values, no shadowing) so it compiles again with ZigLanguage.Compile.
    /// Constructs Zig cannot express are reported in Problems.
    /// </summary>
    internal sealed partial class ZigPrinter : PrinterBase
    {
        public readonly List<string> Problems = new List<string>();

        public static string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            var p = new ZigPrinter(module, options);
            try { p.Module(); }
            catch (Exception ex)
            {
                p.Problems.Add("internal printer error: " + ex.Message);
                problems = p.Problems;
                return "// JavaFarmer could not print this code: " + ex.Message + "\n";
            }
            problems = p.Problems;
            return p.Result;
        }

        readonly IrModule mod;
        JavaTypeGuess types;
        readonly bool fromZig;

        ZigPrinter(IrModule m, PrintOptions o) : base(o)
        {
            mod = m ?? new IrModule();
            fromZig = mod.SourceLanguage == "zig";
        }

        protected override string BlockCommentStart => null;

        void Problem(string what) { if (!Problems.Contains(what)) Problems.Add(what); }

        // ================================================================== names
        static readonly HashSet<string> Reserved = new HashSet<string>(ZigLexer.Keywords)
        {
            "std", "farm", "true", "false", "null", "undefined", "main", "_",
        };

        readonly Dictionary<IrSymbol, string> names = new Dictionary<IrSymbol, string>();
        readonly HashSet<string> containerNames = new HashSet<string>(StringComparer.Ordinal) { "std", "farm" };
        readonly List<Dictionary<string, IrSymbol>> scopes = new List<Dictionary<string, IrSymbol>>();
        readonly HashSet<string> fnUsedNames = new HashSet<string>(StringComparer.Ordinal);

        static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "x";
            var sb = new StringBuilder();
            foreach (var ch in name) sb.Append(char.IsLetterOrDigit(ch) && ch < 128 || ch == '_' ? ch : '_');
            var n = sb.ToString();
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = "j" + n.Substring(3);
            if (char.IsDigit(n[0])) n = "_" + n;
            if (n == "_") n = "ignored";
            if (Reserved.Contains(n) || ZigChecker.IsPrimitiveName(n)) n += "_";
            return n;
        }

        void PushScope() => scopes.Add(new Dictionary<string, IrSymbol>(StringComparer.Ordinal));
        void PopScope() { if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1); }

        bool Live(string n, IrSymbol except)
        {
            foreach (var s in scopes) if (s.TryGetValue(n, out var x) && x != except) return true;
            return false;
        }

        /// <summary>Declares a local / param / capture name: no shadowing of container names or live locals (Zig forbids it).</summary>
        string Declare(IrSymbol s, string preferred = null)
        {
            if (s == null) return "x";
            var b = preferred ?? Clean(s.Name);
            var n = b;
            int k = 2;
            while (containerNames.Contains(n) || Live(n, s)) n = b + k++;
            names[s] = n;
            if (scopes.Count == 0) PushScope();
            scopes[scopes.Count - 1][n] = s;
            return n;
        }

        string Name(IrSymbol s)
        {
            if (s == null) return "x";
            if (names.TryGetValue(s, out var n)) return n;
            if (s.IsExternal) { n = Clean(s.Name); names[s] = n; return n; }
            n = Clean(s.Name);
            names[s] = n;
            return n;
        }

        // ================================================================== analysis
        IrBlock mainBlock;                 // script statements (Python) printed as main()
        IrFunction entry;
        readonly List<IrGlobal> fieldGlobals = new List<IrGlobal>();
        readonly HashSet<IrSymbol> mainLocals = new HashSet<IrSymbol>();
        readonly Dictionary<IrSymbol, IrLambda> hoisted = new Dictionary<IrSymbol, IrLambda>();
        readonly List<(string Name, IrLambda Lambda)> hoistedFns = new List<(string, IrLambda)>();
        readonly Dictionary<IrLambda, string> lambdaNames = new Dictionary<IrLambda, string>();
        readonly HashSet<IrSymbol> mutated = new HashSet<IrSymbol>();
        readonly HashSet<IrSymbol> reassigned = new HashSet<IrSymbol>();
        readonly Dictionary<IrSymbol, int> refs = new Dictionary<IrSymbol, int>();
        readonly HashSet<IrSymbol> undefinedVars = new HashSet<IrSymbol>();
        readonly List<IrSymbol> undefinedFns = new List<IrSymbol>();
        readonly HashSet<string> otherModules = new HashSet<string>(StringComparer.Ordinal);

        static bool IsUndefined(IrSymbol s) => s != null && Equals(s.Tag, PyLowering.UndefinedTag);
        static ZigTag TagOf(IrSymbol s) => s?.Tag as ZigTag;
        static IrSymbol NameSym(IrExpr e) => e is IrName n ? n.Symbol : null;

        static readonly HashSet<IrOp> MutOps = new HashSet<IrOp>
        {
            IrOp.ListAppend, IrOp.ListInsert, IrOp.ListPop, IrOp.ListSet, IrOp.RemoveValue, IrOp.RemoveValueIfPresent, IrOp.Clear,
            IrOp.SetAdd, IrOp.MapPut, IrOp.MapRemove, IrOp.MapRemoveIfPresent,
        };

        void Analyse()
        {
            types = new JavaTypeGuess(mod);
            entry = mod.Entry;
            foreach (var n in IrWalk.Descendants(mod))
            {
                switch (n)
                {
                    case IrAssign a:
                        foreach (var asym in a.Target is IrTupleLit atl ? atl.Items.Select(NameSym) : new[] { NameSym(a.Target) })
                            if (asym != null) assignCount[asym] = assignCount.TryGetValue(asym, out var ac) ? ac + 1 : 1;
                        if (a.Target is IrName tn) { reassigned.Add(tn.Symbol); mutated.Add(tn.Symbol); }
                        else if (a.Target is IrIndex ix && NameSym(ix.Target) is IrSymbol bs) mutated.Add(bs);
                        else if (a.Target is IrTupleLit tl) foreach (var it in tl.Items) if (NameSym(it) is IrSymbol us) { reassigned.Add(us); mutated.Add(us); }
                        break;
                    case IrOpCall o when MutOps.Contains(o.Op) && o.Args.Count > 0 && NameSym(o.Args[0]) is IrSymbol ms:
                        mutated.Add(ms);
                        break;
                    case IrName nm when nm.Symbol != null:
                        refs[nm.Symbol] = refs.TryGetValue(nm.Symbol, out var c) ? c + 1 : 1;
                        if (IsUndefined(nm.Symbol) && nm.Symbol.Kind == IrSymbolKind.Global && nm.Symbol.Name != "__name__") undefinedVars.Add(nm.Symbol);
                        if (nm.Symbol.IsExternal && nm.Symbol.Module != null) otherModules.Add(nm.Symbol.Module);
                        break;
                    case IrCall call when call.Kind == IrCallKind.Function && call.Function != null:
                        if (IsUndefined(call.Function) && call.Function.Function == null && !undefinedFns.Contains(call.Function)) undefinedFns.Add(call.Function);
                        if (call.Function.IsExternal && call.Function.Module != null) otherModules.Add(call.Function.Module);
                        break;
                    case IrFuncRef fr when fr.Function != null:
                        if (fr.Function.IsExternal && fr.Function.Module != null) otherModules.Add(fr.Function.Module);
                        if (IsUndefined(fr.Function) && fr.Function.Function == null && !undefinedFns.Contains(fr.Function)) undefinedFns.Add(fr.Function);
                        break;
                    case IrRangeFor rf when rf.Var != null: refs[rf.Var] = refs.TryGetValue(rf.Var, out var c2) ? c2 : 0; break;
                }
            }
            // loop variables are assigned by their loop, not "reassigned"
            if (mod.IsScript)
            {
                var stmts = new List<IrStmt>();
                var fnRefs = new HashSet<IrSymbol>();
                foreach (var f in mod.Functions)
                    foreach (var n in IrWalk.Descendants(f)) if (n is IrName nm && nm.Symbol != null) fnRefs.Add(nm.Symbol);
                foreach (var item in mod.Items)
                {
                    if (item is IrGlobal g && g.Symbol != null)
                    {
                        if (fnRefs.Contains(g.Symbol)) { fieldGlobals.Add(g); if (g.Init != null && !IsConstantInit(g.Init)) stmts.Add(new IrAssign { Target = new IrName(g.Symbol) { Span = g.NameSpan }, Value = g.Init, Span = g.Span, Leading = g.Leading, Trailing = g.Trailing }); }
                        else { mainLocals.Add(g.Symbol); stmts.Add(new IrVarDecl { Symbol = g.Symbol, Init = g.Init, Span = g.Span, Leading = g.Leading, Trailing = g.Trailing, TypeExplicit = g.TypeExplicit }); }
                    }
                    else if (item is IrStmt s) stmts.Add(s);
                }
                mainBlock = new IrBlock(stmts);
                // module-level names assigned by statements but declared nowhere: fields (if functions use them) or main locals
                var declared = new HashSet<IrSymbol>(mod.Globals.Select(g => g.Symbol));
                foreach (var s in stmts)
                    foreach (var sym in IrWalk.AssignedSymbols(s))
                        if (sym != null && sym.Kind == IrSymbolKind.Global && !declared.Contains(sym) && declared.Add(sym))
                        {
                            if (fnRefs.Contains(sym)) extraFields.Add(sym);
                            else mainLocals.Add(sym);
                        }
            }
            else foreach (var g in mod.Globals) if (g.Symbol != null) fieldGlobals.Add(g);
            // capture-free nested functions (Python "def" inside a function / lambda values) become top-level functions
            foreach (var n in IrWalk.Descendants(mod))
            {
                if (n is IrVarDecl v && v.Init is IrLambda lam && v.Symbol != null && !reassigned.Contains(v.Symbol))
                {
                    if (IrWalk.Captures(lam).Count == 0) { hoisted[v.Symbol] = lam; }
                    else Problem("the nested function " + v.Symbol.Name + " uses variables of the function around it (a closure); Zig has no closures");
                }
            }
            foreach (var f in mod.Functions) if (f.Symbol != null) containerNames.Add(Name(f.Symbol));
            foreach (var g in fieldGlobals) if (g.Symbol != null) containerNames.Add(Name(g.Symbol));
            foreach (var s in extraFields) containerNames.Add(Name(s));
            foreach (var s in undefinedVars) containerNames.Add(Name(s));
            foreach (var s in undefinedFns) containerNames.Add(Name(s));
            foreach (var m in otherModules) containerNames.Add(m);
            foreach (var kv in hoisted)
            {
                var b = Clean(kv.Key.Name);
                var n = b; int k = 2;
                while (containerNames.Contains(n)) n = b + k++;
                containerNames.Add(n);
                names[kv.Key] = n;
                lambdaNames[kv.Value] = n;
                hoistedFns.Add((n, kv.Value));
            }
            IntWidths();
            NeedsTry();
        }

        readonly List<IrSymbol> extraFields = new List<IrSymbol>();

        static bool IsConstantInit(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral _: case IrConst _: return true;
                case IrUnary u: return IsConstantInit(u.Operand);
                case IrBinary b: return b.Op != IrBinOp.Concat && IsConstantInit(b.Left) && IsConstantInit(b.Right);
                case IrListLit l: return l.IsArray && l.Items.All(IsConstantInit);
                case IrTupleLit t: return t.Items.All(IsConstantInit);
            }
            return false;
        }

        // ------------------------------------------------------------------ int widths
        /// <summary>Int symbols printed as usize (counters, indexes, sizes); every other int is i32.</summary>
        readonly HashSet<IrSymbol> usize = new HashSet<IrSymbol>();

        void IntWidths()
        {
            var srcs = new Dictionary<IrSymbol, List<IrExpr>>();
            var indexUse = new HashSet<IrSymbol>();
            var signedUse = new HashSet<IrSymbol>();
            void Add(IrSymbol s, IrExpr e) { if (s == null || e == null) return; if (!srcs.TryGetValue(s, out var l)) srcs[s] = l = new List<IrExpr>(); l.Add(e); }
            foreach (var n in IrWalk.Descendants(mod))
            {
                switch (n)
                {
                    case IrVarDecl v: Add(v.Symbol, v.Init); break;
                    case IrGlobal g: Add(g.Symbol, g.Init); break;
                    case IrAssign a when a.Target is IrName tn && a.Op == null: Add(tn.Symbol, a.Value); break;
                    case IrRangeFor rf when rf.Var != null: usize.Add(rf.Var); foreach (var e in new[] { rf.Start, rf.End }) if (NameSym(e) is IrSymbol rs) indexUse.Add(rs); break;
                    case IrIndex ix when NameSym(ix.Index) is IrSymbol isy: indexUse.Add(isy); break;
                    case IrUnary u when u.Op == IrUnOp.Neg && NameSym(u.Operand) is IrSymbol ns: signedUse.Add(ns); break;
                    case IrCall c when c.Kind == IrCallKind.Function && c.Function?.Function != null:
                        for (int i = 0; i < c.Args.Count && i < c.Function.Function.Params.Count; i++) Add(c.Function.Function.Params[i].Symbol, c.Args[i]);
                        break;
                }
            }
            for (int iter = 0; iter < 6; iter++)
            {
                bool changed = false;
                foreach (var kv in srcs)
                {
                    var s = kv.Key;
                    if (TagOf(s)?.Type != null || usize.Contains(s) || signedUse.Contains(s)) continue;
                    if (!SymIr(s).IsInt) continue;
                    bool anyUsize = false, anySigned = false;
                    foreach (var e in kv.Value)
                    {
                        var k = IntKindOf(e);
                        if (k == 1) anyUsize = true;
                        else if (k == 2) anySigned = true;
                    }
                    if (!anySigned && (anyUsize || indexUse.Contains(s))) { usize.Add(s); changed = true; }
                }
                if (!changed) break;
            }
        }

        /// <summary>0 = literal / unknown, 1 = usize, 2 = signed (i32).</summary>
        int IntKindOf(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l: return l.Kind == IrLitKind.Int && l.Int < 0 ? 2 : 0;
                case IrUnary u when u.Op == IrUnOp.Neg: return 2;
                case IrName n:
                    if (n.Symbol == null) return 0;
                    if (TagOf(n.Symbol)?.Type is ZType zt) return zt.K == ZK.Int ? (zt.Signed ? 2 : 1) : 0;
                    if (usize.Contains(n.Symbol)) return 1;
                    return SymIr(n.Symbol).IsInt && refs.ContainsKey(n.Symbol) ? 0 : 0;
                case IrOpCall o:
                    switch (o.Op) { case IrOp.Len: case IrOp.PosX: case IrOp.PosY: return 1; }
                    return 0;
                case IrCall c when c.Kind == IrCallKind.Builtin:
                    {
                        var api = GameApi.Find(c.Builtin);
                        if (api != null && api.ReturnType.IsInt) return api.NonNegative ? 1 : 2;
                        return 0;
                    }
                case IrBinary b:
                    {
                        if (b.Op == IrBinOp.Sub && b.Left is IrLiteral) return 2;
                        int l = IntKindOf(b.Left), r = IntKindOf(b.Right);
                        if (l == 2 || r == 2) return 2;
                        return l == 1 || r == 1 ? 1 : 0;
                    }
            }
            return 0;
        }

        // ------------------------------------------------------------------ error unions
        /// <summary>Functions (by symbol) whose body needs 'try' (list.append ...): printed with a !T return type.</summary>
        readonly HashSet<IrSymbol> errorFns = new HashSet<IrSymbol>();
        bool mainNeedsTry;

        void NeedsTry()
        {
            bool Needs(IrNode body) => IrWalk.Descendants(body, false).Any(n => n is IrOpCall o && TryOp(o) || n is IrCall c && c.Kind == IrCallKind.Function && c.Function != null && errorFns.Contains(c.Function));
            for (int iter = 0; iter < 5; iter++)
            {
                bool changed = false;
                foreach (var f in mod.Functions)
                {
                    if (f.Symbol == null || errorFns.Contains(f.Symbol)) continue;
                    var sig = TagOf(f.Symbol)?.Sig;
                    if ((sig != null && sig.RetError) || (sig == null && Needs(f.Body))) { errorFns.Add(f.Symbol); changed = true; }
                }
                if (!changed) break;
            }
            if (mainBlock != null) mainNeedsTry = Needs(mainBlock);
        }

        static bool TryOp(IrOpCall o) => o.Op == IrOp.ListAppend || o.Op == IrOp.ListInsert || o.Op == IrOp.SetAdd || o.Op == IrOp.MapPut || o.Op == IrOp.ListCopy;

        /// <summary>Inside a function that returns an error union (try allowed) or not (catch unreachable).</summary>
        bool tryAllowed;

        // ================================================================== module
        void Module()
        {
            Analyse();
            Comments(mod.Leading);
            bool snippet = Options.Snippet;
            bool any = false;
            if (!snippet)
            {
                Line("const std = @import(\"std\");");
                Line("const farm = @import(\"farm\");");
                foreach (var m in otherModules.OrderBy(x => x, StringComparer.Ordinal)) Line("const " + m + " = @import(\"" + m + ".zig\");");
                any = true;
            }
            // declarations a snippet uses but never defines (docs fragments)
            if (!snippet && undefinedVars.Count > 0)
            {
                Line();
                foreach (var s in undefinedVars)
                {
                    if (Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0 || s.Name == "Directions") Problem(s.Name + " cannot be used as a value in Zig");
                    var t = SymZ(s);
                    // "for x in sequence" over a variable of an earlier block: a slice of numbers is the plainest guess
                    if ((t == null || t.IsIntLike) && IrWalk.Descendants(mod).Any(n => n is IrForEach fe && NameSym(fe.Iterable) == s))
                    {
                        t = ZType.SliceOf(ZType.I32, true);
                        forcedTypes[s] = t; symCache.Remove(s);
                    }
                    Line("// defined elsewhere");
                    Line("var " + Name(s) + ": " + TypeText(t, s) + " = " + DefaultText(t) + ";");
                }
            }
            foreach (var s in extraFields)
            {
                if (!any) { } else if (s == extraFields[0]) Line();
                var t = SymZ(s);
                Line("var " + Name(s) + ": " + TypeText(t, s) + " = " + DefaultText(t) + ";");
            }
            int last = any ? 1 : 0;   // 0 nothing, 1 decl, 2 function
            bool mainDone = false;
            foreach (var item in mod.Items)
            {
                switch (item)
                {
                    case IrGlobal g:
                        if (g.Symbol == null || mainLocals.Contains(g.Symbol)) break;
                        if (last == 2 || (last == 1 && g == fieldGlobals.FirstOrDefault() && !snippet)) Line();
                        Global(g);
                        last = 1;
                        break;
                    case IrFunction f:
                        if (last != 0) Line();
                        Function(f);
                        last = 2;
                        break;
                    case IrStmt st:
                        if (mainDone || mainBlock == null) break;
                        mainDone = true;
                        if (snippet)
                        {
                            if (last != 0) Line();
                            tryAllowed = true;
                            PushScope();
                            // a lone expression in prose prints bare
                            if (mainBlock.Stmts.Count == 1 && mainBlock.Stmts[0] is IrExprStmt lone && lone.Leading == null && lone.Trailing == null && !(lone.Expr is IrCall lc && lc.Kind == IrCallKind.Builtin && (lc.Builtin == "print" || lc.Builtin == "quick_print")))
                            {
                                var t = T(lone.Expr);
                                proseMention = true;
                                var tx = X(lone.Expr, null, false).T;
                                proseMention = false;
                                if (t == null) Line(lone.Expr is IrCall ? tx + ";" : tx);
                                else if (t.K == ZK.Void || t.K == ZK.Error) Line(tx + ";"); else Line(tx);
                            }
                            else Stmts(mainBlock.Stmts, true);
                            PopScope();
                            last = 2;
                            break;
                        }
                        if (last != 0) Line();
                        Line("pub fn main() " + (mainNeedsTry ? "!void" : "void") + " {");
                        Indent();
                        tryAllowed = mainNeedsTry;
                        PushScope();
                        Stmts(mainBlock.Stmts, true);
                        PopScope();
                        Dedent();
                        Line("}");
                        last = 2;
                        break;
                }
            }
            foreach (var (n, lam) in hoistedFns)
            {
                Line();
                HoistedFn(n, lam);
            }
            if (!snippet) UndefinedFunctions();
            Comments(mod.TrailingComments);
        }

        void Global(IrGlobal g)
        {
            LeadingComments(g);
            var s = g.Symbol;
            var t = SymZ(s);
            if (t == null) { Problem("the type of " + s.Name + " is unknown"); t = ZType.I32; }
            bool isVar = reassigned.Contains(s) || mutated.Contains(s) || !s.IsConst && fromZig && TagOf(s) == null;
            if (fromZig && !s.IsConst) isVar = true;
            if (!fromZig && !s.IsConst && reassigned.Contains(s)) isVar = true;
            var pub = !g.IsPrivate && fromZig ? "pub " : "";
            string init;
            if (TagOf(s)?.Undefined == true) init = "undefined";   // var a: i32 = undefined; (the game keeps a simulated value)
            else if (g.Init == null || (mod.IsScript && !IsConstantInit(g.Init))) init = DefaultText(t);
            else
            {
                var after = new List<string>();
                init = DeclInit(g.Init, t, s, out _, after);
                if (after.Count > 0) { Problem("a global collection with initial items"); init = DefaultText(t); }
            }
            bool annotate = isVar || init == "undefined" || !(g.Init is IrLiteral) || t.K != ZK.Int && t.K != ZK.Float && t.K != ZK.Bool && !t.IsString;
            if (g.Init is IrLiteral gl && gl.Kind == IrLitKind.String && !isVar) annotate = false;
            if (t.K == ZK.Map && !annotate) { }
            Line(pub + (isVar ? "var " : "const ") + Name(s) + (annotate ? ": " + TypeText(t, s) : "") + " = " + init + ";", g);
        }

        IrType CurrentReturnIr;
        ZType curRet;
        IrFunction curFn;

        void Function(IrFunction f)
        {
            LeadingComments(f);
            PushScope();
            var sig = TagOf(f.Symbol)?.Sig;
            var ps = new List<string>();
            var copies = new List<string>();
            var unusedParams = new List<string>();
            for (int i = 0; i < f.Params.Count; i++)
            {
                var p = f.Params[i];
                var pt = SymZ(p.Symbol);
                if (pt == null) { Problem("the type of parameter " + p.Symbol?.Name + " is unknown"); pt = ZType.I32; }
                if (p.Default != null) Problem("default parameter values (" + p.Symbol?.Name + " = ...) do not exist in Zig");
                var tt = ParamTypeText(pt, p.Symbol);
                if (p.Symbol != null && reassigned.Contains(p.Symbol))
                {
                    var n = Declare(p.Symbol);
                    var argName = n + "_arg";
                    ps.Add(argName + ": " + tt);
                    copies.Add("var " + n + ": " + TypeText(pt, p.Symbol) + " = " + argName + ";");
                }
                else
                {
                    var n = Declare(p.Symbol);
                    ps.Add(n + ": " + tt);
                    if (!refs.ContainsKey(p.Symbol)) unusedParams.Add(n);
                }
            }
            var rt = RetZ(f);
            bool err = f.Symbol != null && errorFns.Contains(f.Symbol);
            if (f.IsEntry) { rt = ZType.Void; err = err || IrWalk.Descendants(f.Body, false).Any(n => n is IrOpCall o && TryOp(o)); }
            var outerRet = curRet; var outerFn = curFn; var outerTry = tryAllowed;
            curRet = rt; curFn = f; tryAllowed = err;
            string header;
            if (f.IsEntry) header = "pub fn main() " + (err ? "!void" : "void");
            else header = (!f.IsPrivate && fromZig ? "pub " : "") + "fn " + Name(f.Symbol) + "(" + string.Join(", ", ps) + ") " + (err ? "!" : "") + TypeText(rt, null);
            Line(header + " {", f);
            Indent();
            if (!Options.Snippet || true) foreach (var u in unusedParams) Line("_ = " + u + ";");
            foreach (var c in copies) Line(c);
            Stmts(f.Body.Stmts, true);
            if (!fromZig && rt.K != ZK.Void && CanComplete(f.Body)) Line("return " + DefaultText(rt) + ";");
            Comments(f.Body.TrailingComments);
            Dedent();
            PopScope();
            Line("}");
            curRet = outerRet; curFn = outerFn; tryAllowed = outerTry;
        }

        void HoistedFn(string name, IrLambda lam)
        {
            PushScope();
            var ps = new List<string>();
            foreach (var p in lam.Params)
            {
                var pt = SymZ(p.Symbol) ?? ZType.I32;
                ps.Add(Declare(p.Symbol) + ": " + ParamTypeText(pt, p.Symbol));
            }
            var rt = LambdaRet(lam);
            var outerRet = curRet; var outerTry = tryAllowed;
            curRet = rt;
            bool err = lam.Body != null && IrWalk.Descendants(lam.Body, false).Any(n => n is IrOpCall o && TryOp(o));
            tryAllowed = err;
            Line("fn " + name + "(" + string.Join(", ", ps) + ") " + (err ? "!" : "") + TypeText(rt, null) + " {");
            Indent();
            if (lam.BodyExpr != null)
            {
                if (lam.ReturnsValue && rt.K != ZK.Void) Line("return " + X(lam.BodyExpr, rt, true).T + ";");
                else ExprStmt(new IrExprStmt(lam.BodyExpr));
            }
            else if (lam.Body != null)
            {
                Stmts(lam.Body.Stmts, true);
                if (rt.K != ZK.Void && CanComplete(lam.Body)) Line("return " + DefaultText(rt) + ";");
            }
            Dedent();
            PopScope();
            Line("}");
            curRet = outerRet; tryAllowed = outerTry;
        }

        ZType LambdaRet(IrLambda lam)
        {
            var t = types.TypeOf(lam);
            if (t.Kind == IrTypeKind.Func && t.Elem != null && t.Elem.IsKnown) return FromIr(t.Elem, null) ?? ZType.Void;
            return ZType.Void;
        }

        void UndefinedFunctions()
        {
            foreach (var s in undefinedFns)
            {
                var calls = IrWalk.Descendants(mod).OfType<IrCall>().Where(c => c.Kind == IrCallKind.Function && c.Function == s).ToList();
                int arity = calls.Count == 0 ? 0 : calls.Max(c => c.Args.Count);
                var ps = new List<string>();
                var discard = new List<string>();
                for (int i = 0; i < arity; i++)
                {
                    IrType t = IrType.Unknown;
                    foreach (var c in calls) if (i < c.Args.Count) t = IrType.Join(t, types.TypeOf(c.Args[i]));
                    var zt = FromIr(t, null);
                    if (zt == null) { Problem("the parameter types of " + s.Name + " are unknown"); zt = ZType.I32; }
                    var pn = ((char)('a' + Math.Min(i, 25))).ToString();
                    ps.Add(pn + ": " + ParamTypeText(zt, null));
                    discard.Add(pn);
                }
                var rt = types.Ret(s);
                bool used = IrWalk.Descendants(mod).Any(x => !(x is IrExprStmt) && IrWalk.Children(x).Any(ch => calls.Contains(ch)));
                ZType zr = ZType.Void;
                if (used)
                {
                    zr = rt.IsKnown && rt.Kind != IrTypeKind.Void ? FromIr(rt, null) : null;
                    if (zr == null) { Problem("the result type of " + s.Name + " is unknown"); zr = ZType.I32; }
                }
                Line();
                Line("// defined elsewhere");
                Line("fn " + Name(s) + "(" + string.Join(", ", ps) + ") " + TypeText(zr, null) + " {");
                Indent();
                foreach (var d in discard) Line("_ = " + d + ";");
                if (zr.K != ZK.Void) Line("return " + DefaultText(zr) + ";");
                Dedent();
                Line("}");
            }
        }

        static bool CanComplete(IrStmt s)
        {
            switch (s)
            {
                case null: return true;
                case IrReturn _: case IrBreak _: case IrContinue _: return false;
                case IrBlock b:
                    foreach (var st in b.Stmts) if (!CanComplete(st)) return false;
                    return true;
                case IrIf i: return i.Else == null || CanComplete(i.Then) || CanComplete(i.Else);
                case IrWhile w: return !(w.Cond is IrLiteral l && l.Kind == IrLitKind.Bool && l.Bool) || HasBreak(w.Body);
                case IrLoop lp: return HasBreak(lp.Body);
                case IrSwitch sw: return !sw.Cases.Any(c => c.IsDefault) || sw.Cases.Any(c => CanComplete(c.Body));
            }
            return true;
        }

        static bool HasBreak(IrNode n)
        {
            switch (n)
            {
                case IrBreak _: return true;
                case IrWhile _: case IrLoop _: case IrDoWhile _: case IrRangeFor _: case IrForEach _: case IrClassicFor _: return false;
            }
            foreach (var c in IrWalk.Children(n)) if (c is IrStmt && HasBreak(c)) return true;
            return false;
        }

        // ================================================================== statements
        void Stmts(List<IrStmt> stmts, bool ownScope)
        {
            // "a, b = ..." after "var a; var b;" declarations: const a, const b = ...
            for (int i = 0; i < stmts.Count; i++)
            {
                if (!(stmts[i] is IrAssign ua) || !(ua.Target is IrTupleLit utl)) continue;
                var targetSyms = new HashSet<IrSymbol>(utl.Items.SelectMany(x => x is IrTupleLit nt ? nt.Items : new List<IrExpr> { x }).Select(NameSym).Where(x => x != null));
                for (int j = i - 1; j >= 0 && stmts[j] is IrVarDecl pv && pv.Init == null && pv.Symbol != null && targetSyms.Contains(pv.Symbol) && pv.Leading == null; j--)
                    pendingCandidate.Add(pv);
            }
            bool dead = false;
            for (int si = 0; si < stmts.Count; si++)
            {
                var s = stmts[si];
                if (skipStmts.Contains(s)) continue;
                if (!dead && s is IrVarDecl ov && OptionalPattern(ov, stmts, si)) { si++; continue; }
                if (dead)
                {
                    int mark = Out.Length;
                    Stmt(s);
                    var text = Out.ToString(mark, Out.Length - mark);
                    Out.Length = mark;
                    foreach (var line in text.Split(new[] { Options.Newline }, StringSplitOptions.None))
                        if (line.Length > 0) Line("// " + line.TrimStart());
                    continue;
                }
                Stmt(s);
                if (s is IrBreak || s is IrContinue || s is IrReturn) dead = true;
            }
        }

        void Body(IrBlock b)
        {
            Indent();
            PushScope();
            if (b != null) { Stmts(b.Stmts, true); Comments(b.TrailingComments); }
            PopScope();
            Dedent();
        }

        void Stmt(IrStmt s)
        {
            if (s == null) return;
            LeadingComments(s);
            switch (s)
            {
                case IrBlock b:
                    Line("{");
                    Body(b);
                    Line("}", b);
                    return;
                case IrVarDecl v: VarDecl(v); return;
                case IrAssign a: Assign(a); return;
                case IrExprStmt es: ExprStmt(es); return;
                case IrIf f when IsMainGuard(f.Cond) && f.Else == null:
                    // if __name__ == "__main__": the body is what main() runs anyway
                    Stmts(f.Then.Stmts, false);
                    return;
                case IrIf f when f.Synthetic && f.Else == null && PopGuardStmt(f) is IrStmt guarded: Stmt(guarded); return;   // _ = l.pop(); (guarded by the frontend)
                case IrIf f: If(f); return;
                case IrWhile w:
                    {
                        var cont = ContinueExpression(w.Body, out var dropped);
                        if (cont != null) foreach (var d in dropped) skipStmts.Add(d);
                        Line("while (" + Cond(w.Cond) + ")" + (cont != null ? " : (" + cont + ")" : "") + " {", w);
                        Body(w.Body);
                        Line("}");
                        return;
                    }
                case IrLoop lp:
                    Line("while (true) {", lp);
                    Body(lp.Body);
                    Line("}");
                    return;
                case IrDoWhile dw:
                    if (IrWalk.Descendants(dw.Body, false).Any(n => n is IrContinue)) Problem("continue inside a do-while loop");
                    Line("while (true) {", dw);
                    Indent(); PushScope();
                    Stmts(dw.Body.Stmts, true);
                    Line("if (!(" + Cond(dw.Cond) + ")) break;");
                    PopScope(); Dedent();
                    Line("}");
                    return;
                case IrClassicFor cf: ClassicFor(cf); return;
                case IrRangeFor rf: RangeFor(rf); return;
                case IrForEach fe: ForEach(fe); return;
                case IrSwitch sw: Switch(sw); return;
                case IrBreak _: Line("break;", s); return;
                case IrContinue _: Line("continue;", s); return;
                case IrReturn r:
                    if (r.Value == null) { Line("return;", r); return; }
                    if (r.Value is IrTupleLit && (curRet == null || curRet.K != ZK.Tuple)) Problem("returning several values (a tuple)");
                    if (r.Value is IrConditional rc && OptGuard(rc) == null) { CondReturn(rc, r); return; }
                    Line("return " + X(r.Value, curRet, true).T + ";", r);
                    return;
                case IrImport imp:
                    Problem("import " + imp.Module + ": Zig files use @import(\"" + imp.Module + ".zig\") at the top of the file");
                    return;
            }
            Problem("unsupported statement " + s.GetType().Name);
            Line("// unsupported statement: " + s.GetType().Name);
        }

        /// <summary>Statements printed as part of another construct (a while continue expression).</summary>
        readonly HashSet<IrStmt> skipStmts = new HashSet<IrStmt>();
        /// <summary>Printing a lone expression of a docs snippet (an inline mention in prose, not a program).</summary>
        bool proseMention;

        static bool IsMainGuard(IrExpr c) =>
            c is IrBinary b && b.Op == IrBinOp.Eq && NameSym(b.Left)?.Name == "__name__" && b.Right is IrLiteral l && l.Kind == IrLitKind.String && l.Str == "__main__";

        static bool IsNullLit(IrExpr e) => e is IrLiteral l && l.Kind == IrLitKind.Null;

        /// <summary>x == None / x != None for the symbol s: Eq / Ne, else null.</summary>
        static IrBinOp? NullTest(IrExpr cond, IrSymbol s)
        {
            if (!(cond is IrBinary b) || (b.Op != IrBinOp.Eq && b.Op != IrBinOp.Ne)) return null;
            if ((NameSym(b.Left) == s && IsNullLit(b.Right)) || (NameSym(b.Right) == s && IsNullLit(b.Left))) return b.Op;
            return null;
        }

        /// <summary>
        /// "x = opt; if x != None: ..." → if (opt) |x| { ... }   and   "x = opt; if x == None: continue" → const x = opt orelse continue;
        /// (the Zig frontend lowers both forms to exactly these statements). Returns false when the pattern does not apply.
        /// </summary>
        bool OptionalPattern(IrVarDecl v, List<IrStmt> stmts, int i)
        {
            var s = v.Symbol;
            if (s == null || v.Init == null || v.Init is IrLambda || i + 1 >= stmts.Count || hoisted.ContainsKey(s) || reassigned.Contains(s)) return false;
            if (!(stmts[i + 1] is IrIf f) || skipStmts.Contains(f)) return false;
            var op = NullTest(f.Cond, s);
            if (op == null) return false;
            var it = T(v.Init);
            if (it == null || it.K != ZK.Optional || it.Elem == null) return false;
            if (op == IrBinOp.Ne)
            {
                for (int j = i + 2; j < stmts.Count; j++) if (UsedIn(stmts[j], s)) return false;
                if (f.Else != null && UsedIn(f.Else, s)) return false;
                if (!UsedIn(f.Then, s) || f.Leading != null) return false;
                var initText = X(v.Init, it, false).T;
                LeadingComments(v);
                PushScope();
                forcedTypes[s] = it.Elem;
                var name = Declare(s);
                If(f, "if (" + initText + ") |" + name + "| {");
                PopScope();
                return true;
            }
            // == None: the then branch only leaves (continue / break / return)
            if (f.Else != null || f.Leading != null) return false;
            var rest = f.Then.Stmts.Where(x => !skipStmts.Contains(x)).ToList();
            if (rest.Count != 1 || rest[0].Leading != null || rest[0].Trailing != null) return false;
            string leave;
            switch (rest[0])
            {
                case IrContinue _: leave = "continue"; break;
                case IrBreak _: leave = "break"; break;
                case IrReturn r when r.Value == null: leave = "return"; break;
                default: return false;
            }
            foreach (var x in f.Then.Stmts) if (x != rest[0]) skipStmts.Add(x);
            var init = X(v.Init, it, false).T;
            forcedTypes[s] = it.Elem;
            symCache.Remove(s);
            bool isVar = mutated.Contains(s) && (it.Elem.K == ZK.List || it.Elem.K == ZK.Map);
            LeadingComments(v);
            var vn = Declare(s);
            Line((isVar ? "var " : "const ") + vn + " = " + init + " orelse " + leave + ";", v);
            if (!refs.ContainsKey(s) && !Options.Snippet) Line("_ = " + vn + ";");
            return true;
        }

        /// <summary>
        /// while (c) : (i += 1) { ... }: the last statement of the body is an assignment that also comes right before
        /// every continue of this loop (the Zig frontend appends the continue expression in both places).
        /// </summary>
        string ContinueExpression(IrBlock body, out List<IrStmt> dropped)
        {
            dropped = new List<IrStmt>();
            if (body == null || body.Stmts.Count < 2) return null;
            if (!(body.Stmts[body.Stmts.Count - 1] is IrAssign last) || !(last.Target is IrName) || last.Leading != null || last.Trailing != null) return null;
            if (last.Value != null && IrWalk.Descendants(last.Value).Any(n => n is IrCall || n is IrOpCall || n is IrConditional)) return null;
            int mark = Problems.Count;
            var text = AssignText(last);
            if (text == null || Problems.Count != mark) { while (Problems.Count > mark) Problems.RemoveAt(Problems.Count - 1); return null; }
            var drop = new List<IrStmt> { last };
            bool ok = true;
            void Visit(List<IrStmt> list)
            {
                for (int k = 0; k < list.Count && ok; k++)
                {
                    switch (list[k])
                    {
                        case IrContinue _:
                            if (k > 0 && list[k - 1] is IrAssign pa && pa.Target is IrName && pa.Leading == null && pa.Trailing == null && AssignText(pa) == text) drop.Add(pa);
                            else ok = false;
                            break;
                        case IrIf fi:
                            Visit(fi.Then.Stmts);
                            for (var e = fi.Else; e != null && ok;)
                            {
                                if (e is IrIf ei) { Visit(ei.Then.Stmts); e = ei.Else; }
                                else { if (e is IrBlock eb) Visit(eb.Stmts); break; }
                            }
                            break;
                        case IrBlock bb: Visit(bb.Stmts); break;
                        case IrSwitch sw: foreach (var c in sw.Cases) Visit(c.Body.Stmts); break;
                    }
                }
            }
            Visit(body.Stmts.Take(body.Stmts.Count - 1).ToList());
            if (!ok) return null;
            dropped = drop;
            return text;
        }

        void CondReturn(IrConditional c, IrReturn r)
        {
            Line("return " + X(c, curRet, true).T + ";", r);
        }

        void If(IrIf f, string header = null)
        {
            Line(header ?? "if (" + Cond(f.Cond) + ") {", f);
            Body(f.Then);
            var tail = f.Else;
            while (tail is IrIf ei)
            {
                if (Options.Comments && ei.Leading != null) { Indent(); Comments(ei.Leading); Dedent(); }
                Line("} else if (" + Cond(ei.Cond) + ") {", ei);
                Body(ei.Then);
                tail = ei.Else;
            }
            if (tail != null)
            {
                Line("} else {", tail);
                if (tail is IrBlock eb) Body(eb);
                else { Indent(); PushScope(); Stmt(tail); PopScope(); Dedent(); }
            }
            Line("}");
        }

        void ClassicFor(IrClassicFor f)
        {
            // { init; while (cond) : (update) { body } }
            Line("{", f);
            Indent(); PushScope();
            foreach (var st in f.Init) Stmt(st);
            string cont = null;
            if (f.Update.Count == 1 && f.Update[0] is IrAssign ua) cont = AssignText(ua);
            else if (f.Update.Count > 0) Problem("a for loop with several updates");
            var cond = f.Cond != null ? Cond(f.Cond) : "true";
            Line("while (" + cond + ")" + (cont != null ? " : (" + cont + ")" : "") + " {");
            Body(f.Body);
            Line("}");
            PopScope(); Dedent();
            Line("}");
        }

        void RangeFor(IrRangeFor rf)
        {
            int step = 1;
            bool literalStep = true;
            if (rf.Step != null)
            {
                if (rf.Step is IrLiteral sl && sl.Kind == IrLitKind.Int) step = (int)sl.Int;
                else if (rf.Step is IrUnary su && su.Op == IrUnOp.Neg && su.Operand is IrLiteral sl2 && sl2.Kind == IrLitKind.Int) step = -(int)sl2.Int;
                else literalStep = false;
            }
            if (rf.Var != null && UsedOutside(rf)) Problem("the loop variable " + rf.Var.Name + " is used after its loop (a Zig loop capture only exists inside the loop)");
            if (literalStep && step == 1)
            {
                var start = rf.Start == null ? "0" : X(rf.Start, ZType.Usize, false).T;
                var end = X(rf.End, ZType.Usize, false);
                var endText = rf.Inclusive ? Wrap(end.T, end.P, PrecAdd + 1) + " + 1" : end.T;
                PushScope();
                var v = rf.Var != null && refs.ContainsKey(rf.Var) && UsedIn(rf.Body, rf.Var) ? Declare(rf.Var) : "_";
                if (rf.Var != null && v == "_") names[rf.Var] = "_";
                Line("for (" + start + ".." + endText + ") |" + v + "| {", rf);
                Body(rf.Body);
                PopScope();
                Line("}");
                return;
            }
            if (!literalStep) Problem("a range with a computed step");
            // counting with a step: a while loop with a continue expression in its own block
            Line("{", rf);
            Indent(); PushScope();
            var t = rf.Var != null ? (SymZ(rf.Var) ?? ZType.I32) : ZType.I32;
            if (step < 0 && t.K == ZK.Int && !t.Signed) t = ZType.I32;
            forcedTypes[rf.Var] = t;
            var vn = Declare(rf.Var);
            Line("var " + vn + ": " + TypeText(t, rf.Var) + " = " + (rf.Start == null ? "0" : X(rf.Start, t, true).T) + ";");
            var endX = X(rf.End, t, false);
            string cmp = step > 0 ? (rf.Inclusive ? " <= " : " < ") : (rf.Inclusive ? " >= " : " > ");
            string upd = step > 0 ? vn + " += " + step.ToString(CultureInfo.InvariantCulture) : vn + " -= " + (-step).ToString(CultureInfo.InvariantCulture);
            if (!literalStep) upd = vn + " += " + X(rf.Step, t, true).T;
            Line("while (" + vn + cmp + Wrap(endX.T, endX.P, PrecCmp + 1) + ") : (" + upd + ") {");
            Body(rf.Body);
            Line("}");
            PopScope(); Dedent();
            Line("}");
        }

        readonly Dictionary<IrSymbol, ZType> forcedTypes = new Dictionary<IrSymbol, ZType>();

        static bool UsedIn(IrNode n, IrSymbol s) => IrWalk.Descendants(n).Any(x => x is IrName nm && nm.Symbol == s);

        bool UsedOutside(IrRangeFor rf)
        {
            var inside = new HashSet<IrNode>(IrWalk.Descendants(rf));
            IrNode scope = (IrNode)curFn ?? mainBlock ?? (IrNode)mod;
            foreach (var n in IrWalk.Descendants(scope))
            {
                if (inside.Contains(n)) continue;
                if (n is IrName nm && nm.Symbol == rf.Var) return true;
                if (n is IrRangeFor other && other != rf && other.Var == rf.Var) continue;
            }
            return false;
        }

        // map / set iteration: the loop variable prints as k.* / entry.key_ptr.*
        readonly Dictionary<IrSymbol, string> derefNames = new Dictionary<IrSymbol, string>();

        void ForEach(IrForEach fe)
        {
            var it = fe.Iterable;
            IrExpr coll = it;
            bool keys = false, values = false;
            if (it is IrOpCall mk && mk.Op == IrOp.MapKeys) { coll = mk.Args[0]; keys = true; }
            if (it is IrOpCall mv && mv.Op == IrOp.MapValues) { coll = mv.Args[0]; values = true; }
            var ct = T(coll) ?? new ZType { K = ZK.Error };
            if (ct.K == ZK.Optional) ct = ct.Elem;
            if (ct.K == ZK.Ptr) ct = ct.Elem;
            if (ct.K == ZK.Map)
            {
                var mapText = Postfix(coll, true);
                PushScope();
                var itName = Declare(new IrSymbol("it", IrSymbolKind.Local));
                if (fe.ValueVar != null && !keys && !values)
                {
                    var cap = TagOf(fe.Var)?.EntryOf ?? "entry";
                    var capSym = new IrSymbol(cap, IrSymbolKind.Local);
                    var capName = Declare(capSym);
                    derefNames[fe.Var] = capName + ".key_ptr.*";
                    derefNames[fe.ValueVar] = capName + ".value_ptr.*";
                    Line("var " + itName + " = " + mapText + ".iterator();", fe);
                    Line("while (" + itName + ".next()) |" + capName + "| {");
                    Body(fe.Body);
                    Line("}");
                }
                else
                {
                    var capName = fe.Var != null && UsedIn(fe.Body, fe.Var) ? Declare(fe.Var, Clean(fe.Var.Name)) : "_";
                    if (fe.Var != null) derefNames[fe.Var] = capName + ".*";
                    Line("var " + itName + " = " + mapText + (values ? ".valueIterator();" : ".keyIterator();"), fe);
                    Line("while (" + itName + ".next()) |" + capName + "| {");
                    Body(fe.Body);
                    Line("}");
                }
                PopScope();
                return;
            }
            string src;
            if (ct.K == ZK.List) src = Postfix(coll, false) + ".items";
            else if (ct.K == ZK.Array || ct.K == ZK.Slice && !ct.IsString) src = X(coll, null, false).T;
            else
            {
                if (ct.IsString) Problem("iterating over the characters of a string");
                else Problem("a for loop over a value of unknown type");
                src = X(coll, null, false).T;
            }
            PushScope();
            var v = fe.Var != null && UsedIn(fe.Body, fe.Var) ? Declare(fe.Var) : "_";
            if (fe.ValueVar != null) Problem("a for-each loop with a value variable over a non-map");
            Line("for (" + src + ") |" + v + "| {", fe);
            Body(fe.Body);
            PopScope();
            Line("}");
        }

        void Switch(IrSwitch sw)
        {
            var st = T(sw.Selector);
            bool constLabels = sw.Cases.All(c => c.IsDefault || c.Labels.All(l => l is IrLiteral li && li.Kind == IrLitKind.Int || l is IrConst || l is IrUnary u && u.Operand is IrLiteral));
            if (!(st.IsIntLike || st.K == ZK.Game) || !constLabels) { SwitchAsIf(sw); return; }
            Line("switch (" + X(sw.Selector, null, false).T + ") {", sw);
            Indent();
            var seen = new HashSet<string>();
            bool hasDefault = false;
            foreach (var c in sw.Cases)
            {
                LeadingComments(c);
                if (c.IsDefault) hasDefault = true;
                var labels = c.IsDefault ? "else" : string.Join(", ", c.Labels.Select(l => { var tx = X(l, st, true).T; seen.Add(tx); return tx; }));
                var body = c.Body;
                if (body != null && body.Stmts.Count == 1 && body.Stmts[0] is IrBlock inner && inner.Leading == null) body = inner;
                if (body == null || body.Stmts.Count == 0) { Line(labels + " => {},"); continue; }
                if (body.Stmts.Count == 1 && body.Stmts[0].Leading == null && (body.TrailingComments == null || body.TrailingComments.Count == 0))
                {
                    var one = OneLiner(body.Stmts[0]);
                    if (one != null) { Line(labels + " => " + one + ",", body.Stmts[0]); continue; }
                }
                Line(labels + " => {");
                Body(body);
                Line("},");
            }
            if (!hasDefault)
            {
                bool complete = st.K == ZK.Game && st.Name == "Direction" && GameApi.DirectionNames.All(d => seen.Contains("." + d));
                if (!complete) Line("else => {},");
            }
            Dedent();
            Line("}");
        }

        /// <summary>A statement as a switch prong value (a call or an assignment), or null.</summary>
        string OneLiner(IrStmt s)
        {
            int mark = Out.Length;
            int depth = Depth;
            Depth = 0;
            PushScope();
            Stmt(s);
            PopScope();
            Depth = depth;
            var text = Out.ToString(mark, Out.Length - mark);
            Out.Length = mark;
            var lines = text.Split(new[] { Options.Newline }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 1) return null;
            var l = lines[0].Trim();
            if (l.Contains("//") || !l.EndsWith(";") || l.StartsWith("var ") || l.StartsWith("const ") || l.StartsWith("break") || l.StartsWith("continue") || l.StartsWith("return")) return null;
            return l.Substring(0, l.Length - 1);
        }

        void SwitchAsIf(IrSwitch sw)
        {
            string sel;
            var st = T(sw.Selector);
            if (sw.Selector is IrName || sw.Selector is IrLiteral) sel = X(sw.Selector, null, false).T;
            else
            {
                var tmp = new IrSymbol("selector", IrSymbolKind.Local);
                var init = X(sw.Selector, null, true).T;
                sel = Declare(tmp);
                Line("const " + sel + " = " + init + ";");
            }
            bool first = true;
            foreach (var c in sw.Cases.Where(c => !c.IsDefault))
            {
                var conds = c.Labels.Select(l => EqText(sel, st, l)).ToList();
                LeadingComments(c);
                Line((first ? "if (" : "} else if (") + string.Join(" or ", conds) + ") {");
                Body(c.Body);
                first = false;
            }
            var d = sw.Cases.FirstOrDefault(c => c.IsDefault);
            if (d != null)
            {
                if (first) { Line("{"); Body(d.Body); Line("}"); return; }
                Line("} else {");
                Body(d.Body);
            }
            if (!first) Line("}");
        }

        string EqText(string sel, ZType st, IrExpr label)
        {
            if (st.IsString) return "std.mem.eql(u8, " + sel + ", " + X(label, ZType.Str, true).T + ")";
            return sel + " == " + X(label, st, false).T;
        }
    }
}
