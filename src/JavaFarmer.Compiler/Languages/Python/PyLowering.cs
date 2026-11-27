using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Python AST → Farm IR (see docs/IR.md and Ir.cs). Python scoping: a name assigned in a function (and not declared
    /// global) is a local of that function; params are Params; nested defs are closures (IrVarDecl + IrLambda).
    /// Module level: defs → IrFunction; top-level names that a module-level function (or anything nested in one) reads
    /// or assigns → Global symbols with an IrGlobal item at their first plain top-level assignment; other top-level
    /// names → script Locals. Names defined nowhere (docs snippets) → a Function symbol with a null Function when they
    /// are called, else a Global symbol without an IrGlobal item; both keep the source spelling (PyName).
    /// Type-directed choices (a + b → Concat for strings, x.pop(k) → MapRemove for dicts) are fixed up after
    /// IrTypeInference by PythonFrontend.
    /// </summary>
    internal sealed class PyLowering
    {
        /// <summary>IrSymbol.Tag of the hidden loop variable of "for a, b in xs:" (the body starts with "a, b = it").</summary>
        public const string TupleForTag = "python:for-tuple";
        /// <summary>IrSymbol.Tag of names that are not defined anywhere in the file.</summary>
        public const string UndefinedTag = "python:undefined";

        readonly List<Diagnostic> errors;
        readonly IrModule mod;

        sealed class Scope
        {
            public Scope Parent;                       // enclosing function scope (null = module)
            public readonly Dictionary<string, IrSymbol> Locals = new Dictionary<string, IrSymbol>();
            public readonly HashSet<string> Globals = new HashSet<string>();
        }

        sealed class FuncInfo
        {
            public readonly HashSet<string> Params = new HashSet<string>();
            public readonly HashSet<string> Assigned = new HashSet<string>();
            public readonly HashSet<string> Globals = new HashSet<string>();
        }

        readonly Dictionary<PyDefStmt, FuncInfo> funcs = new Dictionary<PyDefStmt, FuncInfo>();
        readonly Dictionary<string, IrSymbol> moduleFuncs = new Dictionary<string, IrSymbol>();
        readonly HashSet<PyDefStmt> moduleDefs = new HashSet<PyDefStmt>();
        readonly HashSet<string> topAssigned = new HashSet<string>();
        readonly HashSet<string> globalNames = new HashSet<string>();
        readonly Dictionary<string, IrSymbol> moduleVars = new Dictionary<string, IrSymbol>();
        readonly Dictionary<string, IrSymbol> undefined = new Dictionary<string, IrSymbol>();
        readonly Dictionary<string, IrSymbol> externals = new Dictionary<string, IrSymbol>();
        readonly Dictionary<string, string> fromImported = new Dictionary<string, string>();
        readonly HashSet<string> calledNames = new HashSet<string>();
        readonly HashSet<string> importedModules = new HashSet<string>();
        string starModule;
        readonly HashSet<IrSymbol> globalPlaced = new HashSet<IrSymbol>();

        PyLowering(List<Diagnostic> errors, string name)
        {
            this.errors = errors;
            mod = new IrModule { Name = name, SourceLanguage = "python" };
        }

        public static IrModule Lower(PyModuleAst ast, string name, List<Diagnostic> errors)
        {
            var l = new PyLowering(errors, name);
            l.Analyze(ast);
            l.LowerModule(ast);
            return l.mod;
        }

        void Error(string msg, Span s) => errors.Add(new Diagnostic(msg, Math.Max(0, s.Start), Math.Max(0, s.End)));

        // ================================================================== analysis
        static IEnumerable<PyNameExpr> TargetNames(PyExpr target)
        {
            switch (target)
            {
                case PyNameExpr n: yield return n; break;
                case PySeqExpr s: foreach (var i in s.Items) foreach (var x in TargetNames(i)) yield return x; break;
            }
        }

        static IEnumerable<PyBlock> Blocks(PyStmt s)
        {
            switch (s)
            {
                case PyIfStmt i:
                    yield return i.Body;
                    if (i.Elif != null) foreach (var b in Blocks(i.Elif)) yield return b;
                    if (i.ElseBody != null) yield return i.ElseBody;
                    break;
                case PyWhileStmt w: yield return w.Body; break;
                case PyForStmt f: yield return f.Body; break;
            }
        }

        /// <summary>All statements of a block, through if/while/for but not into defs.</summary>
        static IEnumerable<PyStmt> Flat(PyBlock b)
        {
            foreach (var s in b.Stmts)
            {
                yield return s;
                foreach (var sub in Blocks(s)) foreach (var x in Flat(sub)) yield return x;
            }
        }

        static IEnumerable<PyExpr> ExprsOf(PyStmt s)
        {
            switch (s)
            {
                case PyExprStmt e: yield return e.Expr; break;
                case PyAssignStmt a: yield return a.Target; yield return a.Value; break;
                case PyIfStmt i: yield return i.Cond; if (i.Elif != null) foreach (var x in ExprsOf(i.Elif)) yield return x; break;
                case PyWhileStmt w: yield return w.Cond; break;
                case PyForStmt f: yield return f.Target; yield return f.Iter; break;
                case PyReturnStmt r: if (r.Value != null) yield return r.Value; break;
                case PyDefStmt d: foreach (var p in d.Params) if (p.Default != null) yield return p.Default; break;
            }
        }

        /// <summary>The expression and all sub-expressions (pre-order), appended to into.</summary>
        static void SubExprs(PyExpr e, List<PyExpr> into)
        {
            if (e == null) return;
            into.Add(e);
            switch (e)
            {
                case PyAttrExpr a: SubExprs(a.Target, into); break;
                case PyCallExpr c: SubExprs(c.Func, into); foreach (var x in c.Args) SubExprs(x, into); break;
                case PyIndexExpr ix: SubExprs(ix.Target, into); SubExprs(ix.Index, into); break;
                case PySeqExpr s: foreach (var x in s.Items) SubExprs(x, into); break;
                case PyDictExpr d: foreach (var x in d.Keys) SubExprs(x, into); foreach (var x in d.Values) SubExprs(x, into); break;
                case PyUnaryExpr u: SubExprs(u.Operand, into); break;
                case PyBinExpr b: SubExprs(b.Left, into); SubExprs(b.Right, into); break;
                case PyCompareExpr c: foreach (var x in c.Operands) SubExprs(x, into); break;
                case PyIfExpr i: SubExprs(i.Body, into); SubExprs(i.Cond, into); SubExprs(i.Else, into); break;
            }
        }

        readonly List<PyExpr> exprBuf = new List<PyExpr>();

        /// <summary>All expressions of a statement's own parts (not its sub-blocks).</summary>
        List<PyExpr> AllExprs(PyStmt s)
        {
            exprBuf.Clear();
            foreach (var e in ExprsOf(s)) SubExprs(e, exprBuf);
            return exprBuf;
        }

        void Analyze(PyModuleAst ast)
        {
            foreach (var s in ast.Body.Stmts) if (s is PyDefStmt d) moduleDefs.Add(d);
            // module-level bindings
            foreach (var s in Flat(ast.Body))
            {
                switch (s)
                {
                    case PyAssignStmt a: foreach (var n in TargetNames(a.Target)) topAssigned.Add(n.Id); break;
                    case PyForStmt f: foreach (var n in TargetNames(f.Target)) topAssigned.Add(n.Id); break;
                    case PyDefStmt d:
                        if (moduleDefs.Contains(d)) { if (!moduleFuncs.ContainsKey(d.Name)) moduleFuncs[d.Name] = new IrSymbol(d.Name, IrSymbolKind.Function) { DeclSpan = d.NameSpan }; }
                        else topAssigned.Add(d.Name);
                        break;
                    case PyImportStmt im: foreach (var m in im.Modules) importedModules.Add(m); break;
                    case PyFromImportStmt fi:
                        importedModules.Add(fi.Module);
                        if (fi.Star) { if (starModule == null) starModule = fi.Module; }
                        else foreach (var n in fi.Names) fromImported[n] = fi.Module;
                        break;
                }
            }
            // function scopes (any depth)
            void Collect(PyDefStmt d)
            {
                var fi = new FuncInfo();
                foreach (var p in d.Params) fi.Params.Add(p.Name);
                foreach (var s in Flat(d.Body))
                {
                    switch (s)
                    {
                        case PyAssignStmt a: foreach (var n in TargetNames(a.Target)) fi.Assigned.Add(n.Id); break;
                        case PyForStmt f: foreach (var n in TargetNames(f.Target)) fi.Assigned.Add(n.Id); break;
                        case PyDefStmt nd: fi.Assigned.Add(nd.Name); Collect(nd); break;
                        case PyImportStmt im: foreach (var m in im.Modules) importedModules.Add(m); break;
                        case PyGlobalStmt g: foreach (var n in g.Names) fi.Globals.Add(n); break;
                    }
                }
                fi.Assigned.ExceptWith(fi.Globals);
                fi.Params.ExceptWith(fi.Globals);
                funcs[d] = fi;
            }
            foreach (var s in Flat(ast.Body)) if (s is PyDefStmt d) Collect(d);
            // called names
            foreach (var d in funcs.Keys.ToList()) foreach (var s in Flat(d.Body)) foreach (var x in AllExprs(s)) NoteCall(x);
            foreach (var s in Flat(ast.Body)) foreach (var x in AllExprs(s)) NoteCall(x);

            // names a module-level def (or a def nested in one) reads / writes at module scope → Global
            void Refs(PyDefStmt d, List<FuncInfo> chain, bool moduleDef)
            {
                var fi = funcs[d];
                var ch = new List<FuncInfo>(chain) { fi };
                foreach (var g in fi.Globals) globalNames.Add(g);
                foreach (var s in Flat(d.Body))
                {
                    if (s is PyDefStmt nd) Refs(nd, ch, moduleDef);
                    if (!moduleDef) continue;
                    foreach (var x in AllExprs(s))
                        if (x is PyNameExpr n && topAssigned.Contains(n.Id) && !moduleFuncs.ContainsKey(n.Id) && ResolvesToModule(n.Id, ch))
                            globalNames.Add(n.Id);
                }
            }
            foreach (var s in Flat(ast.Body))
                if (s is PyDefStmt d) Refs(d, new List<FuncInfo>(), moduleDefs.Contains(d));
        }

        void NoteCall(PyExpr x) { if (x is PyCallExpr c && c.Func is PyNameExpr n) calledNames.Add(n.Id); }

        static bool ResolvesToModule(string name, List<FuncInfo> chain)
        {
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i].Globals.Contains(name)) return true;
                if (chain[i].Params.Contains(name) || chain[i].Assigned.Contains(name)) return false;
            }
            return true;
        }

        // ================================================================== symbols
        IrSymbol ModuleVar(string name)
        {
            if (moduleVars.TryGetValue(name, out var s)) return s;
            s = new IrSymbol(name, globalNames.Contains(name) ? IrSymbolKind.Global : IrSymbolKind.Local);
            moduleVars[name] = s;
            return s;
        }

        IrSymbol External(string module, string name, bool function)
        {
            var key = module + "|" + name + "|" + (function ? "f" : "g");
            if (externals.TryGetValue(key, out var s)) return s;
            s = new IrSymbol(name, function ? IrSymbolKind.ExternalFunction : IrSymbolKind.ExternalGlobal) { Module = module, PyName = name };
            externals[key] = s;
            return s;
        }

        IrSymbol Undefined(string name, bool asFunction)
        {
            if (starModule != null) return External(starModule, name, asFunction || calledNames.Contains(name));
            if (undefined.TryGetValue(name, out var s)) return s;
            bool fn = asFunction || calledNames.Contains(name);
            s = new IrSymbol(name, fn ? IrSymbolKind.Function : IrSymbolKind.Global) { PyName = name, Tag = UndefinedTag };
            undefined[name] = s;
            return s;
        }

        /// <summary>User symbol a name refers to in the scope, or null (builtin, constant or undefined).</summary>
        IrSymbol Resolve(string name, Scope sc)
        {
            for (var s = sc; s != null; s = s.Parent)
            {
                if (s.Globals.Contains(name)) break;
                if (s.Locals.TryGetValue(name, out var l)) return l;
            }
            if (moduleFuncs.TryGetValue(name, out var f)) return f;
            if (topAssigned.Contains(name) || globalNames.Contains(name)) return ModuleVar(name);
            if (fromImported.TryGetValue(name, out var m)) return External(m, name, calledNames.Contains(name));
            return null;
        }

        Scope NewScope(PyDefStmt d, Scope parent)
        {
            var fi = funcs[d];
            var sc = new Scope { Parent = parent };
            foreach (var g in fi.Globals) sc.Globals.Add(g);
            foreach (var p in d.Params) if (!sc.Globals.Contains(p.Name)) sc.Locals[p.Name] = new IrSymbol(p.Name, IrSymbolKind.Param) { DeclSpan = p.Span };
            foreach (var a in fi.Assigned) if (!sc.Locals.ContainsKey(a)) sc.Locals[a] = new IrSymbol(a, IrSymbolKind.Local);
            return sc;
        }

        IrSymbol ParamSymbol(Scope sc, PyParam p) => sc.Locals.TryGetValue(p.Name, out var s) ? s : new IrSymbol(p.Name, IrSymbolKind.Param) { DeclSpan = p.Span };

        // ================================================================== module
        void LowerModule(PyModuleAst ast)
        {
            var pendingLead = new List<IrComment>();
            int pendingBlank = -1;
            foreach (var s in ast.Body.Stmts)
            {
                var produced = new List<IrNode>();
                if (s is PyDefStmt d && moduleDefs.Contains(d) && moduleFuncs.TryGetValue(d.Name, out var fsym))
                    produced.Add(LowerFunction(d, fsym));
                else if (s is PyAssignStmt a && a.Op == null && a.Target is PyNameExpr tn && Resolve(tn.Id, null) is IrSymbol gs
                         && gs.Kind == IrSymbolKind.Global && !globalPlaced.Contains(gs))
                {
                    globalPlaced.Add(gs);
                    produced.Add(new IrGlobal { Symbol = gs, Init = LowerExpr(a.Value, null), NameSpan = tn.Span, TypeExplicit = false, Span = a.Span });
                }
                else
                {
                    var list = new List<IrStmt>();
                    LowerStmt(s, null, list);
                    // globals first bound inside this statement: declare them just before it
                    if (globalNames.Count > globalPlaced.Count)
                        foreach (var st in list)
                            foreach (var g in IrWalk.AssignedSymbols(st).ToList())
                                if (g.Kind == IrSymbolKind.Global && moduleVars.TryGetValue(g.Name, out var mv) && mv == g && globalPlaced.Add(g))
                                    produced.Add(ImplicitGlobal(g));
                    produced.AddRange(list);
                }
                Attach(s, produced, pendingLead, ref pendingBlank);
                mod.Items.AddRange(produced);
            }
            var trailing = new List<IrComment>(pendingLead);
            if (ast.Body.TrailingComments != null) trailing.AddRange(ast.Body.TrailingComments);
            if (trailing.Count > 0) mod.TrailingComments = trailing;

            // globals that only functions assign: declare them before the first item that uses them
            foreach (var g in moduleVars.Values.Where(v => v.Kind == IrSymbolKind.Global && !globalPlaced.Contains(v)).ToList())
            {
                globalPlaced.Add(g);
                int at = mod.Items.FindIndex(it => IrWalk.Descendants(it).Any(n => n is IrName nm && nm.Symbol == g));
                if (at < 0) at = mod.Items.Count;
                mod.Items.Insert(at, ImplicitGlobal(g));
            }
            mod.Span = new Span(0, Math.Max(0, ast.Body.Limit - 1));
        }

        static IrGlobal ImplicitGlobal(IrSymbol g) => new IrGlobal { Symbol = g, Init = null, ImplicitInit = true, Synthetic = true, TypeExplicit = false };

        /// <summary>Moves the statement's comments / blank lines to the first IR node it produced (or keeps them pending).</summary>
        static void Attach(PyStmt s, List<IrNode> produced, List<IrComment> pendingLead, ref int pendingBlank)
        {
            var first = produced.FirstOrDefault(n => !(n is IrGlobal g && g.Synthetic));
            if (first == null)
            {
                if (pendingBlank < 0) pendingBlank = s.BlankLines;
                if (s.Leading != null) pendingLead.AddRange(s.Leading);
                if (s.Trailing != null) pendingLead.Add(s.Trailing);
                return;
            }
            var lead = new List<IrComment>(pendingLead);
            if (s.Leading != null) lead.AddRange(s.Leading);
            if (lead.Count > 0) { if (first.Leading == null) first.Leading = lead; else first.Leading.InsertRange(0, lead); }
            first.BlankLinesBefore = pendingBlank >= 0 && lead.Count == 0 ? pendingBlank : s.BlankLines;
            if (s.Trailing != null && first.Trailing == null) first.Trailing = s.Trailing;
            pendingLead.Clear();
            pendingBlank = -1;
        }

        IrFunction LowerFunction(PyDefStmt d, IrSymbol sym)
        {
            var sc = NewScope(d, null);
            var f = new IrFunction { Symbol = sym, Span = d.Span, HeaderSpan = d.HeaderSpan, NameSpan = d.NameSpan };
            if (sym.Function == null) sym.Function = f;
            foreach (var p in d.Params)
                f.Params.Add(new IrParam { Symbol = ParamSymbol(sc, p), Default = p.Default != null ? LowerExpr(p.Default, null) : null, NameSpan = p.Span, Span = p.Default != null ? Span.Cover(p.Span, p.Default.Span) : p.Span });
            f.Body = LowerBlock(d.Body, sc);
            return f;
        }

        IrLambda LowerLambda(PyDefStmt d, Scope parent)
        {
            var sc = NewScope(d, parent);
            var l = new IrLambda { Span = d.Span, HeaderSpan = d.HeaderSpan };
            foreach (var p in d.Params)
                l.Params.Add(new IrParam { Symbol = ParamSymbol(sc, p), Default = p.Default != null ? LowerExpr(p.Default, parent) : null, NameSpan = p.Span, Span = p.Span });
            l.Body = LowerBlock(d.Body, sc);
            return l;
        }

        IrBlock LowerBlock(PyBlock b, Scope sc)
        {
            var blk = new IrBlock { Span = b.Span };
            var pendingLead = new List<IrComment>();
            int pendingBlank = -1;
            if (b.HeaderComment != null) pendingLead.Add(b.HeaderComment);
            foreach (var s in b.Stmts)
            {
                var list = new List<IrStmt>();
                LowerStmt(s, sc, list);
                Attach(s, list.Cast<IrNode>().ToList(), pendingLead, ref pendingBlank);
                blk.Stmts.AddRange(list);
            }
            var trailing = new List<IrComment>(pendingLead);
            if (b.TrailingComments != null) trailing.AddRange(b.TrailingComments);
            if (trailing.Count > 0) blk.TrailingComments = trailing;
            return blk;
        }

        // ================================================================== statements
        static IrBinOp AugOp(string op)
        {
            switch (op)
            {
                case "+=": return IrBinOp.Add;
                case "-=": return IrBinOp.Sub;
                case "*=": return IrBinOp.Mul;
                case "/=": return IrBinOp.TrueDiv;
                case "//=": return IrBinOp.IntDivFloor;
                case "%=": return IrBinOp.ModFloor;
                default: return IrBinOp.Pow;   // **=
            }
        }

        void LowerStmt(PyStmt s, Scope sc, List<IrStmt> outList)
        {
            switch (s)
            {
                case PyExprStmt e:
                    outList.Add(new IrExprStmt(LowerExpr(e.Expr, sc)) { Span = e.Span });
                    return;
                case PyAssignStmt a:
                    {
                        var value = LowerExpr(a.Value, sc);
                        var target = LowerTarget(a.Target, sc);
                        outList.Add(new IrAssign { Target = target, Value = value, Op = a.Op == null ? (IrBinOp?)null : AugOp(a.Op), OpSpan = a.OpSpan, Span = a.Span });
                        return;
                    }
                case PyIfStmt i:
                    outList.Add(LowerIf(i, sc));
                    return;
                case PyWhileStmt w:
                    outList.Add(new IrWhile { Cond = LowerExpr(w.Cond, sc), Body = LowerBlock(w.Body, sc), KwSpan = w.KwSpan, Span = w.Span });
                    return;
                case PyForStmt f:
                    outList.Add(LowerFor(f, sc));
                    return;
                case PyDefStmt d:
                    {
                        // nested def (or a def inside a module-level compound statement): a closure stored in a local
                        IrSymbol sym = null;
                        if (sc != null) sc.Locals.TryGetValue(d.Name, out sym);
                        if (sym == null) sym = sc != null ? Resolve(d.Name, sc) : null;
                        if (sym == null || sym.Kind == IrSymbolKind.Function) sym = ModuleVar(d.Name);
                        var lam = LowerLambda(d, sc);
                        outList.Add(new IrVarDecl { Symbol = sym, Init = lam, NameSpan = d.NameSpan, TypeExplicit = false, Span = d.Span });
                        return;
                    }
                case PyReturnStmt r:
                    outList.Add(new IrReturn { Value = r.Value != null ? LowerExpr(r.Value, sc) : null, Span = r.Span });
                    return;
                case PyJumpStmt j:
                    if (j.Kind == PyJump.Break) outList.Add(new IrBreak { Span = j.Span });
                    else if (j.Kind == PyJump.Continue) outList.Add(new IrContinue { Span = j.Span });
                    return;
                case PyGlobalStmt _:
                    return;   // re-derived by the emitter / printers from the assignments
                case PyImportStmt im:
                    for (int k = 0; k < im.Modules.Count; k++)
                        outList.Add(new IrImport { Module = im.Modules[k], NameSpan = im.ModuleSpans[k], Span = im.Modules.Count == 1 ? im.Span : im.ModuleSpans[k] });
                    return;
                case PyFromImportStmt fi:
                    outList.Add(new IrImport { Module = fi.Module, NameSpan = fi.ModuleSpan, Span = fi.Span, ImportAll = fi.Star, Names = fi.Star ? null : new List<string>(fi.Names) });
                    return;
            }
            Error("unsupported statement", s.Span);
        }

        IrIf LowerIf(PyIfStmt i, Scope sc)
        {
            var r = new IrIf { Cond = LowerExpr(i.Cond, sc), Then = LowerBlock(i.Body, sc), KwSpan = i.KwSpan, Span = i.Span };
            if (i.Elif != null)
            {
                var e = LowerIf(i.Elif, sc);
                e.Trailing = i.Elif.Trailing;
                r.Else = e;
            }
            else if (i.ElseBody != null)
            {
                var eb = LowerBlock(i.ElseBody, sc);
                eb.Span = Span.Cover(i.ElseSpan, eb.Span);
                r.Else = eb;
            }
            return r;
        }

        IrStmt LowerFor(PyForStmt f, Scope sc)
        {
            if (f.Target is PyNameExpr tn && f.Iter is PyCallExpr call && call.Func is PyNameExpr rn && rn.Id == "range"
                && Resolve("range", sc) == null && call.Args.Count >= 1 && call.Args.Count <= 3)
            {
                var args = call.Args.Select(a => LowerExpr(a, sc)).ToList();
                var rf = new IrRangeFor
                {
                    Var = TargetSymbol(tn, sc),
                    Start = args.Count >= 2 ? args[0] : null,
                    End = args.Count == 1 ? args[0] : args[1],
                    Step = args.Count == 3 ? args[2] : null,
                    KwSpan = f.KwSpan, HeaderSpan = f.HeaderSpan, VarSpan = tn.Span, Span = f.Span,
                };
                rf.Body = LowerBlock(f.Body, sc);
                return rf;
            }
            var fe = new IrForEach { Iterable = LowerExpr(f.Iter, sc), KwSpan = f.KwSpan, Span = f.Span, VarSpan = f.Target.Span };
            if (f.Target is PyNameExpr n)
            {
                fe.Var = TargetSymbol(n, sc);
                fe.Body = LowerBlock(f.Body, sc);
                return fe;
            }
            if (f.Target is PySeqExpr seq)
            {
                // for a, b in xs:  →  for <it> in xs: a, b = <it>
                var names = TargetNames(seq).Select(x => x.Id).ToList();
                fe.Var = new IrSymbol(string.Join("_", names), IrSymbolKind.Local) { Tag = TupleForTag };
                fe.Body = LowerBlock(f.Body, sc);
                var unpack = new IrAssign { Target = LowerTarget(seq, sc), Value = new IrName(fe.Var) { Span = seq.Span, NameSpan = seq.Span, Synthetic = true }, Synthetic = true, Span = seq.Span };
                fe.Body.Stmts.Insert(0, unpack);
                return fe;
            }
            Error("this for-loop target is not supported by JavaFarmer (use a plain variable name)", f.Target.Span);
            fe.Var = new IrSymbol("_", IrSymbolKind.Local);
            fe.Body = LowerBlock(f.Body, sc);
            return fe;
        }

        IrSymbol TargetSymbol(PyNameExpr n, Scope sc)
        {
            var s = Resolve(n.Id, sc);
            if (s == null) s = sc != null && sc.Locals.TryGetValue(n.Id, out var l) ? l : ModuleVar(n.Id);
            return s;
        }

        IrExpr LowerTarget(PyExpr e, Scope sc)
        {
            switch (e)
            {
                case PyNameExpr n:
                    return new IrName(TargetSymbol(n, sc)) { Span = n.Span, NameSpan = n.Span };
                case PySeqExpr s:
                    {
                        var t = new IrTupleLit { Span = s.Span };
                        foreach (var i in s.Items) t.Items.Add(LowerTarget(i, sc));
                        return t;
                    }
            }
            return LowerExpr(e, sc);
        }

        // ================================================================== expressions
        static readonly HashSet<string> PyBuiltinCtors = new HashSet<string> { "len", "str", "list", "set", "dict", "range" };

        static bool IsBuiltinName(string n) => GameApi.Find(n) != null || PyBuiltinCtors.Contains(n);

        IrExpr LowerExpr(PyExpr e, Scope sc)
        {
            switch (e)
            {
                case PyNumExpr n:
                    {
                        var l = n.IsFloat ? IrLiteral.OfFloat(n.Float) : IrLiteral.OfInt(n.Int);
                        l.Span = n.Span;
                        return l;
                    }
                case PyStrExpr s:
                    {
                        var l = IrLiteral.OfString(s.Value);
                        l.Span = s.Span;
                        return l;
                    }
                case PyNameExpr n: return LowerName(n, sc);
                case PyAttrExpr a: return LowerAttr(a, sc);
                case PyCallExpr c: return LowerCall(c, sc);
                case PyIndexExpr ix:
                    return new IrIndex { Target = LowerExpr(ix.Target, sc), Index = LowerExpr(ix.Index, sc), Span = ix.Span };
                case PySeqExpr s:
                    {
                        var items = s.Items.Select(i => LowerExpr(i, sc)).ToList();
                        switch (s.Kind)
                        {
                            case PySeqKind.List: { var l = new IrListLit { Span = s.Span }; l.Items.AddRange(items); return l; }
                            case PySeqKind.Set: { var l = new IrSetLit { Span = s.Span }; l.Items.AddRange(items); return l; }
                            default: { var l = new IrTupleLit { Span = s.Span }; l.Items.AddRange(items); return l; }
                        }
                    }
                case PyDictExpr d:
                    {
                        var m = new IrMapLit { Span = d.Span };
                        for (int i = 0; i < d.Keys.Count; i++) { m.Keys.Add(LowerExpr(d.Keys[i], sc)); m.Values.Add(LowerExpr(d.Values[i], sc)); }
                        return m;
                    }
                case PyUnaryExpr u:
                    {
                        var op = u.Op == "not" ? IrUnOp.Not : u.Op == "-" ? IrUnOp.Neg : IrUnOp.Plus;
                        return new IrUnary { Op = op, Operand = LowerExpr(u.Operand, sc), OpSpan = u.OpSpan, Span = u.Span };
                    }
                case PyBinExpr b:
                    {
                        IrBinOp op;
                        switch (b.Op)
                        {
                            case "and": op = IrBinOp.And; break;
                            case "or": op = IrBinOp.Or; break;
                            case "+": op = IrBinOp.Add; break;
                            case "-": op = IrBinOp.Sub; break;
                            case "*": op = IrBinOp.Mul; break;
                            case "/": op = IrBinOp.TrueDiv; break;
                            case "//": op = IrBinOp.IntDivFloor; break;
                            case "%": op = IrBinOp.ModFloor; break;
                            default: op = IrBinOp.Pow; break;
                        }
                        return new IrBinary { Op = op, Left = LowerExpr(b.Left, sc), Right = LowerExpr(b.Right, sc), OpSpan = b.OpSpan, Span = b.Span };
                    }
                case PyCompareExpr c: return LowerCompare(c, sc);
                case PyIfExpr ie:
                    return new IrConditional { Cond = LowerExpr(ie.Cond, sc), Then = LowerExpr(ie.Body, sc), Else = LowerExpr(ie.Else, sc), QSpan = ie.IfSpan, Span = ie.Span };
            }
            Error("unsupported expression", e.Span);
            return new IrLiteral { Kind = IrLitKind.Null, Span = e.Span };
        }

        IrExpr LowerName(PyNameExpr n, Scope sc)
        {
            switch (n.Id)
            {
                case "True": return new IrLiteral { Kind = IrLitKind.Bool, Bool = true, Type = IrType.Bool, Span = n.Span };
                case "False": return new IrLiteral { Kind = IrLitKind.Bool, Bool = false, Type = IrType.Bool, Span = n.Span };
                case "None": return new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = n.Span };
            }
            if (Array.IndexOf(GameApi.DirectionNames, n.Id) >= 0) return new IrConst("Directions", n.Id) { Span = n.Span };
            var s = Resolve(n.Id, sc);
            if (s == null)
            {
                if (Array.IndexOf(GameApi.ConstantCategories, n.Id) >= 0)
                    return new IrName(ConstantBag(n.Id)) { Span = n.Span, NameSpan = n.Span };
                // a game builtin used as a value (spawn_drone(harvest)); list/set/dict/len/str/range used as a value are
                // far more often a docs snippet's variable named like that (dict[k] = v) than the constructor itself
                if (GameApi.Find(n.Id) != null) return new IrFuncRef { Builtin = n.Id, Span = n.Span, NameSpan = n.Span };
                s = Undefined(n.Id, false);
            }
            if (s.Kind == IrSymbolKind.Function || s.Kind == IrSymbolKind.ExternalFunction)
                return new IrFuncRef { Function = s, Span = n.Span, NameSpan = n.Span };
            return new IrName(s) { Span = n.Span, NameSpan = n.Span };
        }

        /// <summary>A whole constant bag used as a value ("sim_unlocks = Unlocks").</summary>
        IrSymbol ConstantBag(string name)
        {
            var key = "#bag:" + name;
            if (undefined.TryGetValue(key, out var s)) return s;
            s = new IrSymbol(name, IrSymbolKind.Global, IrType.Any) { PyName = name, Tag = UndefinedTag, IsConst = true };
            undefined[key] = s;
            return s;
        }

        /// <summary>X in "X.f()" / "X.y" names another module (imported, or not a variable of this file).</summary>
        bool IsModuleRef(PyExpr e, Scope sc, out PyNameExpr name)
        {
            name = e as PyNameExpr;
            if (name == null) return false;
            if (Resolve(name.Id, sc) != null) return false;
            if (importedModules.Contains(name.Id)) return true;
            if (name.Id == "True" || name.Id == "False" || name.Id == "None") return false;
            if (Array.IndexOf(GameApi.ConstantCategories, name.Id) >= 0 || Array.IndexOf(GameApi.DirectionNames, name.Id) >= 0) return false;
            return !IsBuiltinName(name.Id);
        }

        IrExpr LowerAttr(PyAttrExpr a, Scope sc)
        {
            if (a.Target is PyNameExpr cn && Array.IndexOf(GameApi.ConstantCategories, cn.Id) >= 0 && Resolve(cn.Id, sc) == null)
                return new IrConst(cn.Id, a.Name) { Span = a.Span };
            if (IsModuleRef(a.Target, sc, out var mn))
                return new IrName(External(mn.Id, a.Name, false)) { Span = a.Span, QualifierSpan = mn.Span, NameSpan = a.NameSpan };
            Error("'." + a.Name + "' is not supported by the game: only module.name, Entities.X and the list / set / dict methods exist", a.Span);
            return new IrLiteral { Kind = IrLitKind.Null, Span = a.Span };
        }

        IrExpr LowerCall(PyCallExpr c, Scope sc)
        {
            var args = c.Args.Select(x => LowerExpr(x, sc)).ToList();
            if (c.Func is PyAttrExpr a)
            {
                if (IsModuleRef(a.Target, sc, out var mn))
                {
                    var call = new IrCall { Kind = IrCallKind.Function, Function = External(mn.Id, a.Name, true), NameSpan = a.NameSpan, QualifierSpan = mn.Span, Span = c.Span };
                    call.Args.AddRange(args);
                    return call;
                }
                if (a.Target is PyNameExpr cn && Array.IndexOf(GameApi.ConstantCategories, cn.Id) >= 0 && Resolve(cn.Id, sc) == null)
                {
                    Error("TypeError: '" + cn.Id + "." + a.Name + "' is not callable", c.Span);
                    return new IrLiteral { Kind = IrLitKind.Null, Span = c.Span };
                }
                var recv = LowerExpr(a.Target, sc);
                IrOp op; int min, max;
                switch (a.Name)
                {
                    case "append": op = IrOp.ListAppend; min = max = 1; break;
                    case "insert": op = IrOp.ListInsert; min = max = 2; break;
                    case "pop": op = IrOp.ListPop; min = 0; max = 1; break;
                    case "remove": op = IrOp.RemoveValue; min = max = 1; break;
                    case "add": op = IrOp.SetAdd; min = max = 1; break;
                    default:
                        Error("'" + a.Name + "' is not a method the game supports (only append, add, remove, pop and insert)", a.NameSpan);
                        return new IrLiteral { Kind = IrLitKind.Null, Span = c.Span };
                }
                if (args.Count < min || args.Count > max)
                    Error("TypeError: " + a.Name + "() takes " + (min == max ? "exactly " + min : "at most " + max) + " argument" + (max == 1 ? "" : "s") + " (" + args.Count + " given)", c.Span);
                return new IrOpCall(op, new[] { recv }.Concat(args).ToArray()) { NameSpan = a.NameSpan, Span = c.Span };
            }
            if (c.Func is PyNameExpr n)
            {
                var s = Resolve(n.Id, sc);
                if (s != null)
                {
                    if (s.Kind == IrSymbolKind.Function || s.Kind == IrSymbolKind.ExternalFunction)
                    {
                        var fc = new IrCall { Kind = IrCallKind.Function, Function = s, NameSpan = n.Span, Span = c.Span };
                        fc.Args.AddRange(args);
                        return fc;
                    }
                    var vc = new IrCall { Kind = IrCallKind.Value, Callee = new IrName(s) { Span = n.Span, NameSpan = n.Span }, NameSpan = n.Span, Span = c.Span };
                    vc.Args.AddRange(args);
                    return vc;
                }
                switch (n.Id)
                {
                    case "len":
                        if (args.Count != 1) { Error("TypeError: len() takes exactly one argument (" + args.Count + " given)", c.Span); break; }
                        return new IrOpCall(IrOp.Len, args[0]) { NameSpan = n.Span, Span = c.Span };
                    case "str":
                        if (args.Count != 1) { Error("str() takes exactly one argument in the game (" + args.Count + " given)", c.Span); break; }
                        return new IrCast { Target = IrType.String, Operand = args[0], OpSpan = n.Span, Span = c.Span, Type = IrType.String };
                    case "list":
                    case "set":
                    case "dict":
                        if (args.Count > 1) { Error("TypeError: " + n.Id + "() takes at most 1 argument (" + args.Count + " given)", c.Span); break; }
                        if (args.Count == 0)
                        {
                            if (n.Id == "list") return new IrListLit { Span = c.Span };
                            if (n.Id == "set") return new IrSetLit { Span = c.Span };
                            return new IrMapLit { Span = c.Span };
                        }
                        return new IrOpCall(n.Id == "list" ? IrOp.ListCopy : n.Id == "set" ? IrOp.SetCopy : IrOp.MapCopy, args[0]) { NameSpan = n.Span, Span = c.Span };
                }
                if (n.Id != "range" && GameApi.Find(n.Id) != null)
                {
                    var bc = new IrCall { Kind = IrCallKind.Builtin, Builtin = n.Id, NameSpan = n.Span, Span = c.Span };
                    bc.Args.AddRange(args);
                    return bc;
                }
                if (n.Id == "True" || n.Id == "False" || n.Id == "None" || Array.IndexOf(GameApi.DirectionNames, n.Id) >= 0)
                {
                    Error("TypeError: '" + n.Id + "' is not callable", c.Span);
                    return new IrLiteral { Kind = IrLitKind.Null, Span = c.Span };
                }
                var uc = new IrCall { Kind = IrCallKind.Function, Function = Undefined(n.Id, true), NameSpan = n.Span, Span = c.Span };
                uc.Args.AddRange(args);
                return uc;
            }
            var call2 = new IrCall { Kind = IrCallKind.Value, Callee = LowerExpr(c.Func, sc), Span = c.Span };
            call2.Args.AddRange(args);
            return call2;
        }

        /// <summary>a &lt; b &lt; c → (a &lt; b) and (b' &lt; c): the And and the copy b' are Synthetic (printers re-chain them).</summary>
        IrExpr LowerCompare(PyCompareExpr c, Scope sc)
        {
            var ops = c.Operands.Select(o => LowerExpr(o, sc)).ToList();
            IrExpr acc = null;
            for (int i = 0; i < c.Ops.Count; i++)
            {
                var left = i == 0 ? ops[0] : IrClone.Expr(ops[i]);
                if (i > 0) left.Synthetic = true;
                var right = ops[i + 1];
                var span = new Span(left.Span.Start, right.Span.End);
                IrExpr pair;
                switch (c.Ops[i])
                {
                    case "in": pair = new IrOpCall(IrOp.Contains, right, left) { NameSpan = c.OpSpans[i], Span = span }; break;
                    case "not in":
                        pair = new IrUnary { Op = IrUnOp.Not, Operand = new IrOpCall(IrOp.Contains, right, left) { NameSpan = c.OpSpans[i], Span = span }, OpSpan = c.OpSpans[i], Span = span };
                        break;
                    default:
                        {
                            IrBinOp op;
                            switch (c.Ops[i])
                            {
                                case "==": op = IrBinOp.Eq; break;
                                case "!=": op = IrBinOp.Ne; break;
                                case "<": op = IrBinOp.Lt; break;
                                case "<=": op = IrBinOp.Le; break;
                                case ">": op = IrBinOp.Gt; break;
                                default: op = IrBinOp.Ge; break;
                            }
                            pair = new IrBinary { Op = op, Left = left, Right = right, OpSpan = c.OpSpans[i], Span = span };
                            break;
                        }
                }
                acc = acc == null ? pair : new IrBinary { Op = IrBinOp.And, Left = acc, Right = pair, Synthetic = true, OpSpan = c.OpSpans[i], Span = new Span(acc.Span.Start, pair.Span.End) };
            }
            return acc;
        }
    }

    /// <summary>Deep copy of IR expressions (symbols are shared, nodes are new; lambdas are not copied).</summary>
    internal static class IrClone
    {
        static T Base<T>(T copy, IrExpr from) where T : IrExpr
        {
            copy.Span = from.Span; copy.Type = from.Type; copy.Synthetic = from.Synthetic;
            return copy;
        }

        static List<IrExpr> L(List<IrExpr> xs) => xs.Select(Expr).ToList();

        public static IrExpr Expr(IrExpr e)
        {
            switch (e)
            {
                case null: return null;
                case IrLiteral l: return Base(new IrLiteral { Kind = l.Kind, Int = l.Int, Float = l.Float, Bool = l.Bool, Str = l.Str }, e);
                case IrName n: return Base(new IrName { Symbol = n.Symbol, QualifierSpan = n.QualifierSpan, NameSpan = n.NameSpan }, e);
                case IrConst c: return Base(new IrConst { Category = c.Category, Name = c.Name }, e);
                case IrBinary b: return Base(new IrBinary { Op = b.Op, Left = Expr(b.Left), Right = Expr(b.Right), OpSpan = b.OpSpan }, e);
                case IrUnary u: return Base(new IrUnary { Op = u.Op, Operand = Expr(u.Operand), OpSpan = u.OpSpan }, e);
                case IrConditional c: return Base(new IrConditional { Cond = Expr(c.Cond), Then = Expr(c.Then), Else = Expr(c.Else), QSpan = c.QSpan }, e);
                case IrCall c:
                    {
                        var r = Base(new IrCall { Kind = c.Kind, Builtin = c.Builtin, Function = c.Function, Callee = Expr(c.Callee), NameSpan = c.NameSpan, QualifierSpan = c.QualifierSpan }, e);
                        r.Args.AddRange(L(c.Args));
                        return r;
                    }
                case IrOpCall o: { var r = Base(new IrOpCall { Op = o.Op, NameSpan = o.NameSpan }, e); r.Args.AddRange(L(o.Args)); return r; }
                case IrIndex ix: return Base(new IrIndex { Target = Expr(ix.Target), Index = Expr(ix.Index), BaseOffset = ix.BaseOffset }, e);
                case IrListLit l: { var r = Base(new IrListLit { IsArray = l.IsArray }, e); r.Items.AddRange(L(l.Items)); return r; }
                case IrSetLit s: { var r = Base(new IrSetLit(), e); r.Items.AddRange(L(s.Items)); return r; }
                case IrTupleLit t: { var r = Base(new IrTupleLit(), e); r.Items.AddRange(L(t.Items)); return r; }
                case IrMapLit m: { var r = Base(new IrMapLit(), e); r.Keys.AddRange(L(m.Keys)); r.Values.AddRange(L(m.Values)); return r; }
                case IrCast c: return Base(new IrCast { Target = c.Target, Operand = Expr(c.Operand), OpSpan = c.OpSpan }, e);
                case IrFuncRef f: return Base(new IrFuncRef { Function = f.Function, Builtin = f.Builtin, QualifierSpan = f.QualifierSpan, NameSpan = f.NameSpan }, e);
            }
            return e;
        }
    }
}
