using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>Python identifier rules shared by every frontend (reserved names get a suffix).</summary>
    public static class PyNaming
    {
        /// <summary>Source identifier → a legal, non-reserved game Python identifier.</summary>
        public static string Ident(string name) => PyNames.Ident(name ?? "x");

        /// <summary>
        /// Python names of a module's top-level declarations (functions: Ident(name); globals: Ident(name) plus "_f" when a
        /// function has the same source name). Frontends use it to preset IrSymbol.PyName of other modules' symbols.
        /// </summary>
        public static Dictionary<string, string> ModuleLevel(IEnumerable<string> functionNames, IEnumerable<string> globalNames, out Dictionary<string, string> globals)
        {
            var fns = new Dictionary<string, string>();
            foreach (var f in functionNames) fns[f] = Ident(f);
            globals = new Dictionary<string, string>();
            foreach (var g in globalNames) globals[g] = Ident(g) + (fns.ContainsKey(g) ? "_f" : "");
            return fns;
        }
    }

    /// <summary>
    /// The shared IR → game Python lowering (SPEC §6, SPEC-v2 §2). Records a SourceMap segment for every character:
    /// User / Structural / Synthetic, DependencyOverride ("for" on a while made from a classic for), NoHighlight scaffolding.
    /// </summary>
    public sealed partial class IrEmitter
    {
        /// <summary>Lowers a checked IR module to game Python. Never throws.</summary>
        public static CompileResult Compile(IrModule module, string source, CompileOptions options)
        {
            source = source ?? "";
            try
            {
                var em = new IrEmitter(module, options);
                var py = em.Emit(out var segs);
                if (em.Errors.Count > 0) return CompileResults.Fail(source, em.Errors.OrderBy(d => d.Start).ToList());
                return new CompileResult { Success = true, Python = py, Map = new SourceMap(segs), Errors = new List<Diagnostic>(), Names = em.names };
            }
            catch (Exception ex)
            {
                return CompileResults.Fail(source, new List<Diagnostic> { CompileResults.Internal(ex, source) });
            }
        }

        readonly IrModule mod;
        readonly CompileOptions options;
        readonly string fileName;
        PyWriter w = new PyWriter();
        internal readonly List<Diagnostic> Errors = new List<Diagnostic>();
        readonly HashSet<string> helpers = new HashSet<string>();
        readonly Dictionary<IrSymbol, string> py = new Dictionary<IrSymbol, string>();
        readonly Dictionary<string, string> names = new Dictionary<string, string>();

        sealed class LoopCtx { public Action BeforeContinue; }
        List<LoopCtx> loops = new List<LoopCtx>();
        readonly HashSet<IrSymbol> nonNegLocals = new HashSet<IrSymbol>();
        int tempCounter, lambdaCounter;
        bool sideEffectSeen;          // a side effect was emitted earlier in the current statement
        bool hoistAllowed = true;     // statements may be hoisted before the current statement
        bool structural;              // emitting a classic-for header: User parts become Structural
        bool synth;                   // the node being emitted is Synthetic (frontend-invented)
        int conditionalDepth;         // inside the right operand of and / or

        IrEmitter(IrModule m, CompileOptions o)
        {
            mod = m; options = o;
            fileName = !string.IsNullOrEmpty(m.Name) ? m.Name : string.IsNullOrEmpty(o?.FileName) ? "main" : o.FileName;
        }

        void Error(string msg, Span s) => Errors.Add(new Diagnostic(msg, Math.Max(0, s.Start), Math.Max(0, s.End)));
        void Error(string msg, IrNode n) => Error(msg, n?.Span ?? Span.None);

        Attr U(Span s)
        {
            if (s.IsNone) return Attr.Helper;
            if (synth) return Attr.Syn(s.Start, s.End);
            return structural ? Attr.Struct(s.Start, s.End) : Attr.User(s.Start, s.End);
        }
        Attr U(IrNode n) => U(n?.Span ?? Span.None);
        /// <summary>Attr for parentheses around / text standing for a whole operand: its source parentheses if it had any.</summary>
        Attr PU(IrExpr e) => U(e == null ? Span.None : e.ParenSpan.IsNone ? e.Span : e.ParenSpan);
        static Attr PS(IrExpr e) => S(e == null ? Span.None : e.ParenSpan.IsNone ? e.Span : e.ParenSpan);
        static Attr S(Span s) => s.IsNone ? Attr.Helper : Attr.Syn(s.Start, s.End);
        static Attr S(IrNode n) => S(n?.Span ?? Span.None);
        static Attr Scaffold(Span s) => s.IsNone ? Attr.Helper : Attr.Scaffold(s.Start, s.End);
        static Attr Struct(Span s, string ovr = null) => s.IsNone ? Attr.Helper : Attr.Struct(s.Start, s.End, ovr);
        static Attr UserAttr(Span s) => s.IsNone ? Attr.Helper : Attr.User(s.Start, s.End);
        /// <summary>Attr for the closing-brace "pass" of an empty body.</summary>
        static Attr EndOf(IrBlock b) => b == null || b.Span.IsNone ? Attr.Helper : Attr.User(Math.Max(b.Span.Start, b.Span.End - 1), b.Span.End);

        // ================================================================== naming
        string Py(IrSymbol s)
        {
            if (s == null) return "None";
            if (py.TryGetValue(s, out var n)) return n;
            // symbols the pre-pass did not see (external, or frontend oddities)
            n = s.PyName ?? PyNaming.Ident(s.Name);
            py[s] = n;
            return n;
        }

        List<IrNode> allNodes;
        readonly Dictionary<IrNode, HashSet<IrSymbol>> assignedCache = new Dictionary<IrNode, HashSet<IrSymbol>>();

        bool AssignsIn(IrNode body, IrSymbol sym)
        {
            if (!assignedCache.TryGetValue(body, out var set)) assignedCache[body] = set = IrWalk.AssignedSet(body);
            return set.Contains(sym);
        }

        void AssignNames()
        {
            allNodes = new List<IrNode>();
            IrWalk.CollectDescendants(mod, allNodes);
            var fns = mod.Functions.Where(f => f.Symbol != null).ToList();
            var fnNames = new HashSet<string>(fns.Select(f => f.Symbol.Name));
            var fileLevel = new HashSet<string>();
            foreach (var f in fns) { var n = PyNaming.Ident(f.Symbol.Name); py[f.Symbol] = n; fileLevel.Add(n); Note(n, f.Symbol.Name); }
            foreach (var g in mod.Globals)
            {
                if (g.Symbol == null) continue;
                var n = PyNaming.Ident(g.Symbol.Name) + (fnNames.Contains(g.Symbol.Name) ? "_f" : "");
                py[g.Symbol] = n; fileLevel.Add(n); Note(n, g.Symbol.Name);
            }
            fileLevel.Add(fileName);
            fileLevel.Add("main");
            if (options?.OtherFiles != null) foreach (var k in options.OtherFiles.Keys) fileLevel.Add(k);
            foreach (var imp in allNodes.OfType<IrImport>()) if (imp.Module != null) fileLevel.Add(imp.Module);

            // locals: v1 rule (suffix _l when clashing with a module-level name) + shadowing in nested scopes (C, Rust, JS)
            var scopes = new List<List<IrSymbol>>();
            void Declare(IrSymbol s)
            {
                if (s == null || py.ContainsKey(s)) { if (s != null && scopes.Count > 0) scopes[scopes.Count - 1].Add(s); return; }
                var n = PyNaming.Ident(s.Name);
                if (fileLevel.Contains(n)) n += "_l";
                var live = new HashSet<string>(scopes.SelectMany(x => x).Select(x => py[x]));
                if (live.Contains(n))
                {
                    int k = 2;
                    while (live.Contains(n + "_" + k) || fileLevel.Contains(n + "_" + k)) k++;
                    n = n + "_" + k;
                }
                py[s] = n;
                Note(n, s.Name);
                if (scopes.Count > 0) scopes[scopes.Count - 1].Add(s);
            }
            void Push() => scopes.Add(new List<IrSymbol>());
            void Pop() => scopes.RemoveAt(scopes.Count - 1);
            void Walk(IrNode n)
            {
                switch (n)
                {
                    case null: return;
                    case IrFunction f:
                        Push();
                        foreach (var p in f.Params) { Declare(p.Symbol); Walk(p.Default); }
                        foreach (var s in f.Body.Stmts) Walk(s);
                        Pop();
                        return;
                    case IrLambda l:
                        Push();
                        foreach (var p in l.Params) { Declare(p.Symbol); Walk(p.Default); }
                        if (l.BodyExpr != null) Walk(l.BodyExpr); else if (l.Body != null) foreach (var s in l.Body.Stmts) Walk(s);
                        Pop();
                        return;
                    case IrBlock b:
                        Push(); foreach (var s in b.Stmts) Walk(s); Pop();
                        return;
                    case IrVarDecl v:
                        Walk(v.Init); Declare(v.Symbol);
                        return;
                    case IrClassicFor cf:
                        Push();
                        foreach (var s in cf.Init) Walk(s);
                        Walk(cf.Cond);
                        foreach (var s in cf.Update) Walk(s);
                        Walk(cf.Body);
                        Pop();
                        return;
                    case IrRangeFor rf:
                        Walk(rf.Start); Walk(rf.End); Walk(rf.Step);
                        Push(); DeclareLoopVar(rf.Var); Walk(rf.Body); Pop();
                        return;
                    case IrForEach fe:
                        Walk(fe.Iterable);
                        Push(); DeclareLoopVar(fe.Var); DeclareLoopVar(fe.ValueVar); Walk(fe.Body); Pop();
                        return;
                    case IrAssign a:
                        Walk(a.Value);
                        // Python-style first assignment declares a script/function local
                        foreach (var t in a.Target is IrTupleLit tl ? tl.Items : new List<IrExpr> { a.Target })
                            if (t is IrName nm && nm.Symbol != null && (nm.Symbol.Kind == IrSymbolKind.Local) && !py.ContainsKey(nm.Symbol)) Declare(nm.Symbol);
                            else Walk(t);
                        return;
                }
                foreach (var c in IrWalk.Children(n)) Walk(c);
            }
            void DeclareLoopVar(IrSymbol s)
            {
                if (s == null) return;
                if (s.Kind == IrSymbolKind.Local || s.Kind == IrSymbolKind.Param) Declare(s);
            }
            // top-level statements of a script share one scope (a block inside them that shadows a script local gets
            // another name); functions do not see it
            var scriptScope = new List<IrSymbol>();
            foreach (var item in mod.Items)
            {
                if (item is IrFunction f) Walk(f);
                else if (item is IrGlobal g) { Push(); Walk(g.Init); Pop(); }
                else { scopes.Add(scriptScope); Walk(item); Pop(); }
            }
            // names used but never declared (e.g. Python locals assigned in a nested position)
            foreach (var n in allNodes.OfType<IrName>())
                if (n.Symbol != null && !py.ContainsKey(n.Symbol) && (n.Symbol.Kind == IrSymbolKind.Local || n.Symbol.Kind == IrSymbolKind.Param))
                {
                    var nm = PyNaming.Ident(n.Symbol.Name);
                    if (fileLevel.Contains(nm)) nm += "_l";
                    py[n.Symbol] = nm; Note(nm, n.Symbol.Name);
                }
        }

        void Note(string pyName, string src) { if (pyName != null && src != null && pyName != src) names[pyName] = src; }

        // ================================================================== globals
        /// <summary>"global x" lines of a function / lambda body: own-module globals it assigns, first assignment order.</summary>
        List<(string Py, Span At)> GlobalsOf(IEnumerable<IrNode> body)
        {
            var res = new List<(string, Span)>();
            void Add(IrExpr target)
            {
                if (target is IrTupleLit t) { foreach (var i in t.Items) Add(i); return; }
                if (target is IrName n && n.Symbol != null && n.Symbol.Kind == IrSymbolKind.Global)
                {
                    var p = Py(n.Symbol);
                    if (!res.Any(r => r.Item1 == p)) res.Add((p, n.Span));
                }
            }
            void AddSym(IrSymbol s, Span at)
            {
                if (s == null || s.Kind != IrSymbolKind.Global) return;
                var p = Py(s);
                if (!res.Any(r => r.Item1 == p)) res.Add((p, at));
            }
            void Walk(IrNode n)
            {
                switch (n)
                {
                    case null: return;
                    case IrLambda _: return;   // lambdas get their own global lines
                    case IrAssign a: Add(a.Target); Walk(a.Target is IrName ? null : a.Target); Walk(a.Value); return;
                    case IrClassicFor cf:
                        foreach (var s in cf.Init) Walk(s);
                        Walk(cf.Cond); Walk(cf.Body);
                        foreach (var s in cf.Update) Walk(s);
                        return;
                    case IrRangeFor rf: AddSym(rf.Var, rf.VarSpan); break;
                    case IrForEach fe: AddSym(fe.Var, fe.VarSpan); AddSym(fe.ValueVar, fe.ValueVarSpan); break;
                }
                foreach (var c in IrWalk.Children(n)) Walk(c);
            }
            foreach (var n in body) Walk(n);
            return res;
        }

        void EmitGlobals(IEnumerable<IrNode> body)
        {
            foreach (var (p, at) in GlobalsOf(body)) w.Line("global " + p, Scaffold(at));
        }

        // ================================================================== module
        string Emit(out List<Segment> segments)
        {
            AssignNames();
            var body = w;
            var entry = mod.Entry;
            if (mod.IsScript)
            {
                foreach (var item in mod.Items)
                {
                    switch (item)
                    {
                        case IrFunction f: EmitFunction(f, asEntry: false); break;
                        case IrGlobal g: EmitGlobal(g); break;
                        case IrStmt s: ResetFunctionState(); EmitStmt(s); break;
                    }
                }
            }
            else
            {
                foreach (var f in mod.Functions) if (!f.IsEntry) EmitFunction(f, false);
                foreach (var g in mod.Globals) EmitGlobal(g);
                if (entry != null) EmitFunction(entry, true);
            }
            if (entry != null)
            {
                var hdr = Scaffold(entry.HeaderSpan);
                w.Line("if __name__ == \"__main__\":", hdr);
                w.Indent++;
                w.Line(Py(entry.Symbol) + "()", hdr);
                w.Indent--;
            }

            // prefix: helpers, imports of other modules used without an explicit import
            var outW = new PyWriter();
            foreach (var h in Helpers.Ordered(helpers)) outW.Write(Helpers.Source[h], Attr.Helper);
            var explicitImports = new HashSet<string>(allNodes.OfType<IrImport>().Select(i => i.Module));
            foreach (var (m, at) in ModuleUses())
                if (!explicitImports.Contains(m)) outW.Line("import " + m, UserAttr(at));
            outW.Append(body);
            segments = outW.Segments;
            return outW.Text;
        }

        /// <summary>Other modules used by this module → span of their first use (qualifier), ordered by that span.</summary>
        List<(string Module, Span At)> ModuleUses()
        {
            var first = new Dictionary<string, Span>();
            foreach (var n in allNodes)
            {
                IrSymbol s = null; Span q = Span.None;
                switch (n)
                {
                    case IrName nm: s = nm.Symbol; q = nm.QualifierSpan; break;
                    case IrCall c when c.Kind == IrCallKind.Function: s = c.Function; q = c.QualifierSpan; break;
                    case IrFuncRef fr: s = fr.Function; q = fr.QualifierSpan; break;
                }
                if (s == null || !s.IsExternal || string.IsNullOrEmpty(s.Module)) continue;
                if (!first.TryGetValue(s.Module, out var prev) || (!q.IsNone && (prev.IsNone || q.Start < prev.Start))) first[s.Module] = q;
            }
            return first.OrderBy(kv => kv.Value.IsNone ? int.MaxValue : kv.Value.Start).Select(kv => (kv.Key, kv.Value)).ToList();
        }

        void EmitGlobal(IrGlobal g)
        {
            ResetFunctionState();
            BeginStatement();
            bool old = synth; synth = g.Synthetic;
            var lineAttr = U(g);
            var target = new Frag(Py(g.Symbol), U(g.NameSpan));
            if (g.Init == null)
            {
                var t = g.Symbol.Type ?? IrType.Unknown;
                string def = t.Kind == IrTypeKind.Bool ? "False" : t.IsNumeric ? "0" : "None";
                w.Line(new Frag().Add(target).Add(" = ", lineAttr).Add(def, lineAttr), lineAttr);
            }
            else EmitAssignValue(target, g.Init, lineAttr, lineAttr);
            synth = old;
        }

        void ResetFunctionState()
        {
            loops = new List<LoopCtx>();
            nonNegLocals.Clear();
        }

        void EmitFunction(IrFunction f, bool asEntry)
        {
            ResetFunctionState();
            bool old = synth; synth = f.Synthetic;
            var hdr = U(f.HeaderSpan);
            if (asEntry)
            {
                w.Line("def " + Py(f.Symbol) + "():", Scaffold(f.HeaderSpan));
            }
            else
            {
                var fr = new Frag("def ", hdr).Add(Py(f.Symbol), U(f.NameSpan)).Add("(", hdr);
                for (int i = 0; i < f.Params.Count; i++)
                {
                    if (i > 0) fr.Add(", ", hdr);
                    var p = f.Params[i];
                    fr.Add(Py(p.Symbol), U(p.NameSpan));
                    if (p.Default != null) fr.Add("=", hdr).Add(E(p.Default));
                }
                fr.Add("):", hdr);
                w.Line(fr, hdr);
            }
            w.Indent++;
            int mark = w.Length;
            EmitGlobals(f.Body.Stmts);
            if (asEntry)
                foreach (var p in f.Params)
                    w.Line(Py(p.Symbol) + " = []", Scaffold(p.Span));
            foreach (var s in f.Body.Stmts) EmitStmt(s);
            if (w.Length == mark) w.Line("pass", EndOf(f.Body));
            w.Indent--;
            synth = old;
        }

        // ================================================================== statements
        void BeginStatement() { sideEffectSeen = false; hoistAllowed = true; }

        void EmitBody(IrStmt s, Attr passAttr)
        {
            w.Indent++;
            int mark = w.Length;
            EmitStmt(s);
            if (w.Length == mark) w.Line("pass", passAttr);
            w.Indent--;
        }

        void EmitBody(List<IrStmt> stmts, Attr passAttr)
        {
            w.Indent++;
            int mark = w.Length;
            foreach (var s in stmts) EmitStmt(s);
            if (w.Length == mark) w.Line("pass", passAttr);
            w.Indent--;
        }

        void EmitStmt(IrStmt s)
        {
            bool old = synth; synth = s.Synthetic;
            try { EmitStmtCore(s, true); }
            finally { synth = old; }
        }

        void EmitStmtCore(IrStmt s, bool allowHoist)
        {
            BeginStatement();
            hoistAllowed = allowHoist;
            switch (s)
            {
                case IrBlock b:
                    foreach (var st in b.Stmts) EmitStmt(st);
                    break;
                case IrVarDecl v:
                    {
                        if (v.Init == null) break;
                        var target = new Frag(Py(v.Symbol), U(v.NameSpan));
                        EmitAssignValue(target, v.Init, U(v), U(v));
                        break;
                    }
                case IrExprStmt es:
                    EmitExprStmt(es.Expr, U(es));
                    break;
                case IrAssign a:
                    EmitAssign(a, U(a));
                    break;
                case IrIf ifs:
                    EmitIf(ifs);
                    break;
                case IrWhile ws:
                    {
                        var kw = U(ws.KwSpan);
                        hoistAllowed = false;
                        var cond = E(ws.Cond);
                        w.Line(new Frag("while ", kw).Add(cond).Add(":", kw), kw);
                        loops.Add(new LoopCtx());
                        EmitBody(ws.Body, kw);
                        loops.RemoveAt(loops.Count - 1);
                        break;
                    }
                case IrLoop lp:
                    {
                        var kw = U(lp.KwSpan);
                        w.Line(new Frag("while ", kw).Add("True", Struct(lp.KwSpan)).Add(":", kw), kw);
                        loops.Add(new LoopCtx());
                        EmitBody(lp.Body, kw);
                        loops.RemoveAt(loops.Count - 1);
                        break;
                    }
                case IrDoWhile dw:
                    EmitDoWhile(dw);
                    break;
                case IrClassicFor f:
                    EmitClassicFor(f);
                    break;
                case IrRangeFor rf:
                    EmitRangeFor(rf);
                    break;
                case IrForEach fe:
                    EmitForEach(fe);
                    break;
                case IrBreak br:
                    w.Line("break", U(br));
                    break;
                case IrContinue co:
                    {
                        var loop = loops.Count > 0 ? loops[loops.Count - 1] : null;
                        loop?.BeforeContinue?.Invoke();
                        w.Line("continue", U(co));
                        break;
                    }
                case IrReturn r:
                    {
                        var kw = U(r);
                        if (r.Value == null) { w.Line("return", kw); break; }
                        EmitValueTo(r.Value, f => w.Line(new Frag("return ", kw).Add(f), kw));
                        break;
                    }
                case IrSwitch sw:
                    EmitSwitch(sw);
                    break;
                case IrImport imp:
                    w.Line("import " + imp.Module, U(imp));
                    break;
                default:
                    Error("unsupported statement", s);
                    break;
            }
        }

        /// <summary>Emits "target = value", lowering a ?: value into if/else assignments.</summary>
        void EmitAssignValue(Frag target, IrExpr value, Attr eqAttr, Attr lineAttr)
        {
            EmitValueTo(value, vf => w.Line(new Frag().Add(target).Add(" = ", eqAttr).Add(vf), lineAttr));
        }

        /// <summary>Emits code that computes the value and hands it to sink (which writes one line). ?: becomes if/else.</summary>
        void EmitValueTo(IrExpr value, Action<Frag> sink)
        {
            if (value is IrConditional ce)
            {
                bool old = synth; synth = ce.Synthetic;
                var cond = E(ce.Cond);
                var qa = U(ce.QSpan);
                w.Line(new Frag("if ", qa).Add(cond).Add(":", qa), qa);
                w.Indent++;
                BeginStatement();
                synth = old;
                EmitValueTo(ce.Then, sink);
                w.Indent--;
                w.Line("else:", PU(ce.Else));
                w.Indent++;
                BeginStatement();
                EmitValueTo(ce.Else, sink);
                w.Indent--;
                return;
            }
            sink(E(value));
        }

        void EmitIf(IrIf ifs)
        {
            // flatten else-if chains; compute all conditions first so hoisted defs land before the 'if'
            var chain = new List<IrIf> { ifs };
            IrStmt tail = ifs.Else;
            while (tail is IrIf next) { chain.Add(next); tail = next.Else; }
            var conds = new List<Frag>();
            for (int i = 0; i < chain.Count; i++)
            {
                hoistAllowed = i == 0;
                bool old = synth; synth = chain[i].Synthetic;
                conds.Add(E(chain[i].Cond));
                synth = old;
            }
            for (int i = 0; i < chain.Count; i++)
            {
                var c = chain[i];
                bool old = synth; synth = c.Synthetic;
                var kw = U(c.KwSpan);
                w.Line(new Frag(i == 0 ? "if " : "elif ", kw).Add(conds[i]).Add(":", kw), kw);
                synth = old;
                EmitBody(c.Then, kw);
            }
            if (tail != null)
            {
                var ea = U(tail);
                w.Line("else:", ea);
                EmitBody(tail, ea);
            }
        }

        void EmitDoWhile(IrDoWhile dw)
        {
            var doKw = U(dw.KwSpan);
            w.Line(new Frag("while ", doKw).Add("True", Struct(dw.KwSpan)).Add(":", doKw), doKw);
            var ctx = new LoopCtx();
            ctx.BeforeContinue = () => EmitDoWhileCheck(dw);
            loops.Add(ctx);
            w.Indent++;
            EmitStmt(dw.Body);
            BeginStatement();
            EmitDoWhileCheck(dw);
            w.Indent--;
            loops.RemoveAt(loops.Count - 1);
        }

        void EmitDoWhileCheck(IrDoWhile dw)
        {
            var wa = Struct(dw.WhileSpan);
            hoistAllowed = true;
            var cond = E(dw.Cond);
            var f = new Frag("if ", wa).Add("not ", wa).Add(Paren(cond, Frag.Prec_Not + 1, PU(dw.Cond))).Add(":", wa);
            w.Line(f, wa);
            w.Indent++;
            w.Line("break", wa);
            w.Indent--;
        }

        // ------------------------------------------------------------------ for loops
        sealed class RangeInfo { public IrSymbol Var; public IrExpr From, To; public bool Inclusive; public int Step; }

        static IrSymbol LocalOf(IrExpr e) => e is IrName n && n.Symbol != null && (n.Symbol.Kind == IrSymbolKind.Local || n.Symbol.Kind == IrSymbolKind.Param) ? n.Symbol : null;

        /// <summary>The classic-for → range() analysis (SPEC §6.2).</summary>
        RangeInfo TryRange(IrClassicFor f)
        {
            if (f.Init.Count != 1 || !(f.Init[0] is IrVarDecl decl)) return null;
            var sym = decl.Symbol;
            if (sym == null || decl.Init == null || !(sym.Type?.IsInt ?? false) || !(decl.Init.Type?.IsInt ?? false)) return null;
            if (f.Cond == null || f.Update.Count != 1) return null;
            if (!(f.Cond is IrBinary cond) || LocalOf(cond.Left) != sym) return null;
            if (!(cond.Right.Type?.IsInt ?? false)) return null;
            if (!(f.Update[0] is IrAssign a) || LocalOf(a.Target) != sym || a.TruncateToInt) return null;
            if (!(a.Op == IrBinOp.Add || a.Op == IrBinOp.Sub) || !(a.Value is IrLiteral lit) || lit.Kind != IrLitKind.Int || lit.Int <= 0 || lit.Int > int.MaxValue) return null;
            int step = (int)(a.Op == IrBinOp.Add ? lit.Int : -lit.Int);
            bool inclusive;
            switch (cond.Op)
            {
                case IrBinOp.Lt: if (step < 0) return null; inclusive = false; break;
                case IrBinOp.Le: if (step < 0) return null; inclusive = true; break;
                case IrBinOp.Gt: if (step > 0) return null; inclusive = false; break;
                case IrBinOp.Ge: if (step > 0) return null; inclusive = true; break;
                default: return null;
            }
            if (AssignsIn(f.Body, sym)) return null;
            if (!IsInvariant(cond.Right, f.Body)) return null;
            return new RangeInfo { Var = sym, From = decl.Init, To = cond.Right, Inclusive = inclusive, Step = step };
        }

        bool IsInvariant(IrExpr e, IrNode body)
        {
            switch (e)
            {
                case IrLiteral l: return l.Kind == IrLitKind.Int;
                case IrUnary u when u.Op == IrUnOp.Neg: return IsInvariant(u.Operand, body);
                case IrBinary b when b.Op == IrBinOp.Add || b.Op == IrBinOp.Sub || b.Op == IrBinOp.Mul: return IsInvariant(b.Left, body) && IsInvariant(b.Right, body);
                case IrName n when n.Symbol != null:
                    switch (n.Symbol.Kind)
                    {
                        case IrSymbolKind.Local: case IrSymbolKind.Param: return !AssignsIn(body, n.Symbol);
                        case IrSymbolKind.Global: return n.Symbol.IsConst;
                    }
                    return false;
                case IrCall c when c.Kind == IrCallKind.Builtin: return GameApi.Find(c.Builtin)?.LoopInvariant ?? false;
                case IrOpCall o when o.Op == IrOp.Len && o.Args.Count == 1:
                    {
                        var s = LocalOf(o.Args[0]);
                        if (s == null) return false;
                        var k = o.Args[0].Type?.Kind ?? IrTypeKind.Unknown;
                        if (k == IrTypeKind.String || k == IrTypeKind.Array) return !AssignsIn(body, s);
                        if (k == IrTypeKind.List) return !AssignsIn(body, s) && OnlyReads(body, s);
                        return false;
                    }
            }
            return false;
        }

        static readonly HashSet<IrOp> ReadOnlyOps = new HashSet<IrOp> { IrOp.Len, IrOp.IsEmpty, IrOp.Contains, IrOp.ListIndexOf };

        /// <summary>True if every use of the list local in the subtree is a read-only access.</summary>
        static bool OnlyReads(IrNode tree, IrSymbol sym)
        {
            foreach (var n in IrWalk.Descendants(tree))
            {
                foreach (var ch in IrWalk.Children(n))
                {
                    if (!(ch is IrExpr ce) || LocalOf(ce) != sym) continue;
                    switch (n)
                    {
                        case IrOpCall c when c.Args.Count > 0 && c.Args[0] == ch && ReadOnlyOps.Contains(c.Op): continue;
                        case IrIndex ix when ix.Target == ch: continue;
                        case IrBinary b when b.Op == IrBinOp.Eq || b.Op == IrBinOp.Ne: continue;
                        case IrCast c when c.Target.IsString: continue;
                    }
                    return false;
                }
            }
            return true;
        }

        void EmitClassicFor(IrClassicFor f)
        {
            var kw = U(f.KwSpan);
            var rf = TryRange(f);
            if (rf != null)
            {
                var hdr = U(f.HeaderSpan);
                var decl = (IrVarDecl)f.Init[0];
                bool oldSynth = synth; synth = synth || decl.Synthetic;
                var from = E(rf.From);
                var to = E(rf.To);
                synth = oldSynth;
                bool fromZero = rf.From is IrLiteral fl && fl.Kind == IrLitKind.Int && fl.Int == 0;
                var call = new Frag("range", hdr).Add("(", hdr);
                if (rf.Inclusive)
                {
                    var adj = Struct(f.Cond.ParenSpan.IsNone ? f.Cond.Span : f.Cond.ParenSpan);
                    to = new Frag().Add(Paren(to, Frag.Prec_Add, PU(rf.To))).Add(rf.Step > 0 ? " + 1" : " - 1", adj);
                    to.Prec = Frag.Prec_Add;
                }
                if (!(fromZero && rf.Step == 1)) call.Add(from).Add(", ", hdr);
                call.Add(to);
                if (rf.Step != 1) call.Add(", ", hdr).Add(rf.Step.ToString(CultureInfo.InvariantCulture), hdr);
                call.Add(")", hdr);
                var line = new Frag("for ", kw).Add(Py(rf.Var), U(decl.NameSpan)).Add(" in ", hdr).Add(call).Add(":", kw);
                w.Line(line, kw);
                bool nonNeg = rf.Step > 0 && IsNonNeg(rf.From);
                if (nonNeg) nonNegLocals.Add(rf.Var);
                loops.Add(new LoopCtx());
                EmitBody(f.Body, kw);
                loops.RemoveAt(loops.Count - 1);
                return;
            }

            // general form: init; while cond: body; update   (header is Structural; 'while' costs "for")
            structural = true;
            foreach (var init in f.Init) EmitStmt(init);
            BeginStatement();
            hoistAllowed = false;
            Frag cond = f.Cond != null ? E(f.Cond) : new Frag("True", Struct(f.HeaderSpan));
            structural = false;
            var wkw = Struct(f.KwSpan, "for");
            w.Line(new Frag("while ", wkw).Add(cond).Add(":", wkw), wkw);
            var ctx = new LoopCtx { BeforeContinue = () => EmitForUpdate(f) };
            loops.Add(ctx);
            w.Indent++;
            int mark = w.Length;
            EmitStmt(f.Body);
            EmitForUpdate(f);
            if (w.Length == mark) w.Line("pass", kw);
            w.Indent--;
            loops.RemoveAt(loops.Count - 1);
        }

        void EmitForUpdate(IrClassicFor f)
        {
            bool old = structural;
            structural = true;
            foreach (var u in f.Update)
            {
                bool os = synth; synth = u.Synthetic;
                EmitStmtCore(u, false);
                synth = os;
            }
            structural = old;
        }

        void EmitRangeFor(IrRangeFor rf)
        {
            var kw = U(rf.KwSpan);
            var hdr = U(rf.HeaderSpan.IsNone ? rf.KwSpan : rf.HeaderSpan);
            hoistAllowed = false;
            int step = 1;
            bool stepLiteral = true;
            if (rf.Step != null)
            {
                if (rf.Step is IrLiteral sl && sl.Kind == IrLitKind.Int) step = (int)sl.Int;
                else if (rf.Step is IrUnary su && su.Op == IrUnOp.Neg && su.Operand is IrLiteral sl2 && sl2.Kind == IrLitKind.Int) step = -(int)sl2.Int;
                else stepLiteral = false;
            }
            if (rf.Inclusive && !stepLiteral)
            {
                Error("an inclusive range needs a constant step in JavaFarmer", rf.Step);
                return;
            }
            var call = new Frag("range", hdr).Add("(", hdr);
            bool fromZero = rf.Start == null || (rf.Start is IrLiteral fl && fl.Kind == IrLitKind.Int && fl.Int == 0);
            bool stepOne = rf.Step == null || (stepLiteral && step == 1);
            var to = E(rf.End);
            if (rf.Inclusive)
            {
                to = new Frag().Add(Paren(to, Frag.Prec_Add, PU(rf.End))).Add(step > 0 ? " + 1" : " - 1", Struct(rf.End.Span));
                to.Prec = Frag.Prec_Add;
            }
            if (!(fromZero && stepOne)) call.Add(rf.Start != null ? E(rf.Start) : new Frag("0", hdr)).Add(", ", hdr);
            call.Add(to);
            if (!stepOne) call.Add(", ", hdr).Add(E(rf.Step));
            call.Add(")", hdr);
            w.Line(new Frag("for ", kw).Add(Py(rf.Var), U(rf.VarSpan)).Add(" in ", hdr).Add(call).Add(":", kw), kw);
            if (stepLiteral && step > 0 && (rf.Start == null || IsNonNeg(rf.Start))) nonNegLocals.Add(rf.Var);
            loops.Add(new LoopCtx());
            EmitBody(rf.Body, kw);
            loops.RemoveAt(loops.Count - 1);
        }

        void EmitForEach(IrForEach fe)
        {
            var kw = U(fe.KwSpan);
            Frag iter;
            if (fe.Iterable is IrOpCall c && c.Op == IrOp.MapKeys && c.Args.Count == 1)
                iter = Operand(c.Args[0], Frag.Prec_Atom);
            else iter = ValueFrag(fe.Iterable);
            Frag mapRef = null;
            if (fe.ValueVar != null)
            {
                bool simple = fe.Iterable is IrName;
                if (!simple)
                {
                    var tmp = "__jfe" + (++tempCounter);
                    w.Line(new Frag(tmp + " = ", S(fe.Iterable)).Add(iter), S(fe.Iterable));
                    iter = new Frag(tmp, S(fe.Iterable));
                }
                mapRef = iter;
            }
            var line = new Frag("for ", kw).Add(Py(fe.Var), U(fe.VarSpan)).Add(" in ", kw).Add(iter).Add(":", kw);
            w.Line(line, kw);
            loops.Add(new LoopCtx());
            w.Indent++;
            int mark = w.Length;
            if (fe.ValueVar != null)
            {
                var a = S(fe.ValueVarSpan.IsNone ? fe.VarSpan : fe.ValueVarSpan);
                w.Line(new Frag(Py(fe.ValueVar) + " = ", a).Add(mapRef).Add("[" + Py(fe.Var) + "]", a), a);
            }
            EmitStmt(fe.Body);
            if (w.Length == mark) w.Line("pass", kw);
            w.Indent--;
            loops.RemoveAt(loops.Count - 1);
        }

        void EmitSwitch(IrSwitch sw)
        {
            var kw = U(sw.KwSpan);
            var sel = ValueFrag(sw.Selector);
            Frag selRef;
            bool simple = (sw.Selector is IrName ne && ne.Symbol != null && !ne.Symbol.IsExternal) || sw.Selector is IrLiteral;
            if (simple) selRef = sel;
            else
            {
                var tmp = "__js" + (++tempCounter);
                w.Line(new Frag(tmp + " = ", PS(sw.Selector)).Add(sel), PS(sw.Selector));
                selRef = new Frag(tmp, PS(sw.Selector));
            }
            var normal = sw.Cases.Where(g => !g.IsDefault).ToList();
            var dflt = sw.Cases.FirstOrDefault(g => g.IsDefault);
            bool firstBranch = true;
            foreach (var g in normal)
            {
                var la = U(g.LabelSpan);
                var cond = new Frag();
                for (int i = 0; i < g.Labels.Count; i++)
                {
                    if (i > 0) cond.Add(" or ", S(g.LabelSpan));
                    var lf = E(g.Labels[i]);
                    cond.Add(selRef).Add(" == ", PS(g.Labels[i])).Add(Paren(lf, Frag.Prec_Add, PU(g.Labels[i])));
                }
                if (g.Labels.Count > 1) cond.Prec = Frag.Prec_Or;
                var head = firstBranch ? new Frag("if ", kw) : new Frag("elif ", la);
                w.Line(head.Add(cond).Add(":", firstBranch ? kw : la), firstBranch ? kw : la);
                EmitBody(g.Body, la);
                firstBranch = false;
            }
            if (dflt != null)
            {
                var da = U(dflt.LabelSpan);
                if (firstBranch)
                {
                    foreach (var s in dflt.Body.Stmts) EmitStmt(s);
                }
                else
                {
                    w.Line("else:", da);
                    EmitBody(dflt.Body, da);
                }
            }
        }

        // ================================================================== expression statements
        void EmitExprStmt(IrExpr e, Attr lineAttr)
        {
            if (e is IrOpCall c && (c.Op == IrOp.ListSet || c.Op == IrOp.MapPut) && c.Args.Count == 3)
            {
                bool old = synth; synth = c.Synthetic;
                var target = new Frag().Add(Operand(c.Args[0], Frag.Prec_Atom)).Add("[", U(c)).Add(E(c.Args[1])).Add("]", U(c));
                var val = E(c.Args[2]);
                w.Line(new Frag().Add(target).Add(" = ", U(c.NameSpan)).Add(val), lineAttr);
                synth = old;
                return;
            }
            var f = E(e);
            if (f.IsCall) w.Line(f, lineAttr);
            else w.Line(new Frag("__jv = ", S(e)).Add(f), lineAttr);
        }

        static string OpText(IrBinOp op)
        {
            switch (op)
            {
                case IrBinOp.Add: case IrBinOp.Concat: return "+";
                case IrBinOp.Sub: return "-";
                case IrBinOp.Mul: return "*";
                case IrBinOp.TrueDiv: return "/";
                case IrBinOp.IntDivFloor: case IrBinOp.IntDivTrunc: return "//";
                case IrBinOp.ModFloor: case IrBinOp.ModTrunc: return "%";
                case IrBinOp.Pow: return "**";
            }
            return "?";
        }

        void EmitAssign(IrAssign a, Attr lineAttr)
        {
            var opA = U(a.OpSpan);
            var target = a.Target;
            var tt = target.Type ?? IrType.Unknown;
            if (a.Op == null)
            {
                bool simpleTarget = (target is IrName tn && tn.Symbol != null && !tn.Symbol.IsExternal) || target is IrTupleLit;
                if (a.Value is IrConditional && !simpleTarget)
                {
                    // evaluate into a temp, then assign
                    var tmp = "__jt" + (++tempCounter);
                    EmitValueTo(a.Value, vf => w.Line(new Frag(tmp + " = ", PS(a.Value)).Add(vf), PS(a.Value)));
                    w.Line(new Frag().Add(Target(target)).Add(" = ", opA).Add(tmp, PS(a.Value)), lineAttr);
                    return;
                }
                var tf = Target(target);
                EmitAssignValue(tf, a.Value, opA, lineAttr);
                return;
            }
            var op = a.Op.Value;
            var vt = a.Value.Type ?? IrType.Unknown;
            var value = ValueFrag(a.Value);
            if (op == IrBinOp.Concat)
            {
                if (!vt.IsString) value = StrCall(value, a.Value);
                w.Line(new Frag().Add(Target(target)).Add(" += ", opA).Add(value), lineAttr);
                return;
            }
            bool plain = op == IrBinOp.Add || op == IrBinOp.Sub || op == IrBinOp.Mul || op == IrBinOp.TrueDiv || op == IrBinOp.ModFloor;
            if (plain && !a.TruncateToInt)
            {
                w.Line(new Frag().Add(Target(target)).Add(" " + OpText(op) + "= ", opA).Add(value), lineAttr);
                return;
            }
            if (!(target is IrName) && !IsPure(target))
            {
                Error("compound assignment on this target is not supported in JavaFarmer; use a temporary variable", a);
                return;
            }
            // t = f(t, v)
            var cur = E(target);
            var helperA = S(a.OpSpan);
            Frag rhs;
            switch (op)
            {
                case IrBinOp.IntDivTrunc: rhs = HelperCall("__jdiv", helperA, cur, value); break;
                case IrBinOp.ModTrunc: rhs = HelperCall("__jmod", helperA, cur, value); break;
                case IrBinOp.Pow: rhs = HelperCall("__jpow", helperA, cur, value); break;
                default:
                    {
                        var inner = new Frag().Add(Paren(cur, Frag.Prec_Mul, U(target))).Add(" " + OpText(op) + " ", opA).Add(Paren(value, Frag.Prec_Mul + 1, PU(a.Value)));
                        inner.Prec = op == IrBinOp.Add || op == IrBinOp.Sub ? Frag.Prec_Add : Frag.Prec_Mul;
                        rhs = a.TruncateToInt ? HelperCall("__jtrunc", helperA, inner) : inner;
                        break;
                    }
            }
            if ((op == IrBinOp.ModTrunc || op == IrBinOp.IntDivTrunc || op == IrBinOp.Pow) && a.TruncateToInt) rhs = HelperCall("__jtrunc", helperA, rhs);
            w.Line(new Frag().Add(Target(target)).Add(" = ", opA).Add(rhs), lineAttr);
        }

        /// <summary>E(), but a whole-value ?: is hoisted into a temp when evaluation order allows it.</summary>
        Frag ValueFrag(IrExpr value)
        {
            if (value is IrConditional ce && hoistAllowed && !sideEffectSeen && conditionalDepth == 0)
            {
                var tmp = "__jt" + (++tempCounter);
                EmitValueTo(ce, vf => w.Line(new Frag(tmp + " = ", S(ce)).Add(vf), S(ce)));
                return new Frag(tmp, S(ce));
            }
            return E(value);
        }

        Frag Target(IrExpr target)
        {
            if (target is IrTupleLit t)
            {
                var f = new Frag();
                for (int i = 0; i < t.Items.Count; i++)
                {
                    if (i > 0) f.Add(", ", U(t));
                    f.Add(E(t.Items[i]));
                }
                return f;
            }
            return E(target);
        }

        // ================================================================== expressions
        Frag Paren(Frag f, int minPrec, Attr a)
        {
            if (f.Prec >= minPrec) return f;
            var p = new Frag("(", a).Add(f).Add(")", a);
            p.Prec = Frag.Prec_Atom;
            return p;
        }

        Frag Operand(IrExpr e, int minPrec) => Paren(E(e), minPrec, PU(e));

        Frag HelperCall(string name, Attr a, params Frag[] args)
        {
            helpers.Add(name);
            var f = new Frag(name, a).Add("(", a);
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) f.Add(", ", a);
                f.Add(args[i]);
            }
            f.Add(")", a);
            f.IsCall = true;
            return f;
        }

        Frag StrCall(Frag inner, IrExpr src)
        {
            var a = PS(src);
            var f = new Frag("str(", a).Add(inner).Add(")", a);
            f.IsCall = true;
            return f;
        }

        Frag Call(string name, Attr nameAttr, Attr parenAttr, List<Frag> args)
        {
            var f = new Frag(name, nameAttr).Add("(", parenAttr);
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) f.Add(", ", parenAttr);
                f.Add(args[i]);
            }
            f.Add(")", parenAttr);
            f.IsCall = true;
            return f;
        }

        Frag Method(IrExpr target, string name, Attr nameAttr, Attr parenAttr, List<Frag> args)
        {
            var f = new Frag().Add(Operand(target, Frag.Prec_Atom)).Add(".", nameAttr).Add(name, nameAttr).Add("(", parenAttr);
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) f.Add(", ", parenAttr);
                f.Add(args[i]);
            }
            f.Add(")", parenAttr);
            f.IsCall = true;
            return f;
        }

        Frag E(IrExpr e)
        {
            if (e == null) return new Frag("None", Attr.Helper);
            bool old = synth; synth = e.Synthetic;
            try { return ECore(e); }
            finally { synth = old; }
        }

        Frag ECore(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l: return Literal(l);
                case IrName n:
                    {
                        var s = n.Symbol;
                        if (s == null) { Error("cannot find symbol", n); return new Frag("None", U(n)); }
                        if (s.Kind == IrSymbolKind.ExternalGlobal)
                            return new Frag(s.Module, U(n.QualifierSpan.IsNone ? n.Span : n.QualifierSpan)).Add(".", U(n)).Add(Py(s), U(n.NameSpan.IsNone ? n.Span : n.NameSpan));
                        if (s.Kind == IrSymbolKind.Function || s.Kind == IrSymbolKind.ExternalFunction)
                            return FuncRef(new IrFuncRef { Function = s, Span = n.Span, QualifierSpan = n.QualifierSpan, NameSpan = n.NameSpan });
                        return new Frag(Py(s), U(n));
                    }
                case IrConst c:
                    {
                        if (c.Category == null || c.Category == "Directions") return new Frag(c.Name, U(c));
                        return new Frag(c.Category + "." + c.Name, U(c));
                    }
                case IrCall c: return EmitCall(c);
                case IrOpCall o: return EmitOp(o);
                case IrIndex ix:
                    {
                        var a = U(ix);
                        var idx = E(ix.Index);
                        if (ix.BaseOffset != 0)
                        {
                            idx = new Frag().Add(Paren(idx, Frag.Prec_Add, PU(ix.Index))).Add(ix.BaseOffset > 0 ? " - " + ix.BaseOffset : " + " + (-ix.BaseOffset), S(ix.Index));
                            idx.Prec = Frag.Prec_Add;
                        }
                        return new Frag().Add(Operand(ix.Target, Frag.Prec_Atom)).Add("[", a).Add(idx).Add("]", a);
                    }
                case IrUnary u:
                    {
                        var opA = U(u.OpSpan.IsNone ? u.Span : u.OpSpan);
                        if (u.Op == IrUnOp.Not)
                        {
                            var f = new Frag("not ", opA).Add(Operand(u.Operand, Frag.Prec_Not));
                            f.Prec = Frag.Prec_Not;
                            return f;
                        }
                        var g = new Frag(u.Op == IrUnOp.Neg ? "-" : "+", opA).Add(Operand(u.Operand, Frag.Prec_Unary));
                        g.Prec = Frag.Prec_Unary;
                        return g;
                    }
                case IrBinary b: return Binary(b);
                case IrConditional ce:
                    Error("?: is only supported as a whole value here (in a declaration, an assignment, a return or as a whole argument); use if/else", ce);
                    return new Frag("None", U(ce));
                case IrCast ca:
                    {
                        var tt = ca.Target ?? IrType.Unknown;
                        var ot = ca.Operand.Type ?? IrType.Unknown;
                        if (ca.IsTypeAssertion) return E(ca.Operand);
                        if (tt.Kind == IrTypeKind.Int && (ot.Kind == IrTypeKind.Float || ot.Kind == IrTypeKind.Unknown))
                        {
                            if (IsNonNeg(ca.Operand))
                            {
                                var f = new Frag().Add(Operand(ca.Operand, Frag.Prec_Mul)).Add(" // 1", U(ca.OpSpan));
                                f.Prec = Frag.Prec_Mul;
                                return f;
                            }
                            return HelperCall("__jtrunc", S(ca.OpSpan), E(ca.Operand));
                        }
                        if (tt.Kind == IrTypeKind.String)
                            return Call("str", U(ca.OpSpan.IsNone ? ca.Span : ca.OpSpan), U(ca), Args(new List<IrExpr> { ca.Operand }));
                        return E(ca.Operand);
                    }
                case IrListLit ll:
                    {
                        var a = U(ll);
                        var f = new Frag("[", a);
                        var items = Args(ll.Items);
                        for (int i = 0; i < items.Count; i++) { if (i > 0) f.Add(", ", a); f.Add(items[i]); }
                        return f.Add("]", a);
                    }
                case IrSetLit sl:
                    {
                        var a = U(sl);
                        if (sl.Items.Count == 0) { var e0 = new Frag("set()", a); e0.IsCall = true; return e0; }
                        var f = new Frag("{", a);
                        var items = Args(sl.Items);
                        for (int i = 0; i < items.Count; i++) { if (i > 0) f.Add(", ", a); f.Add(items[i]); }
                        return f.Add("}", a);
                    }
                case IrMapLit ml:
                    {
                        var a = U(ml);
                        var f = new Frag("{", a);
                        var all = new List<IrExpr>();
                        for (int i = 0; i < ml.Keys.Count; i++) { all.Add(ml.Keys[i]); all.Add(ml.Values[i]); }
                        var items = Args(all);
                        for (int i = 0; i + 1 < items.Count; i += 2)
                        {
                            if (i > 0) f.Add(", ", a);
                            f.Add(items[i]).Add(": ", a).Add(items[i + 1]);
                        }
                        return f.Add("}", a);
                    }
                case IrTupleLit tl:
                    {
                        var a = U(tl);
                        var f = new Frag("(", a);
                        var items = Args(tl.Items);
                        for (int i = 0; i < items.Count; i++) { if (i > 0) f.Add(", ", a); f.Add(items[i]); }
                        if (items.Count == 1) f.Add(",", a);
                        return f.Add(")", a);
                    }
                case IrLambda lam: return EmitLambda(lam);
                case IrFuncRef fr: return FuncRef(fr);
            }
            Error("unsupported expression", e);
            return new Frag("None", U(e));
        }

        Frag FuncRef(IrFuncRef fr)
        {
            if (fr.Builtin != null) return new Frag(fr.Builtin, U(fr));
            if (fr.Function == null) return new Frag("None", U(fr));
            if (fr.Function.Kind == IrSymbolKind.ExternalFunction)
                return new Frag(fr.Function.Module, U(fr.QualifierSpan.IsNone ? fr.Span : fr.QualifierSpan)).Add(".", U(fr)).Add(Py(fr.Function), U(fr.NameSpan.IsNone ? fr.Span : fr.NameSpan));
            return new Frag(Py(fr.Function), U(fr));
        }

        // ------------------------------------------------------------------ literals
        Frag Literal(IrLiteral l)
        {
            var a = U(l);
            switch (l.Kind)
            {
                case IrLitKind.Int:
                    {
                        var f = new Frag(l.Int.ToString(CultureInfo.InvariantCulture), a);
                        if (l.Int < 0) f.Prec = Frag.Prec_Unary;
                        return f;
                    }
                case IrLitKind.Float:
                    {
                        var f = new Frag(PyFormat.Double(l.Float), a);
                        if (l.Float < 0 || (l.Float == 0 && double.IsNegativeInfinity(1 / l.Float))) f.Prec = Frag.Prec_Unary;
                        return f;
                    }
                case IrLitKind.Bool: return new Frag(l.Bool ? "True" : "False", a);
                case IrLitKind.Null: return new Frag("None", a);
                default:
                    if ((l.Str ?? "").IndexOf('\n') >= 0 || (l.Str ?? "").IndexOf('\r') >= 0)
                        Error("line breaks inside strings are not supported by the game", l);
                    return StringLiteral(l.Str ?? "", a);
            }
        }

        Frag StringLiteral(string v, Attr a)
        {
            if (v.IndexOf('"') < 0) return new Frag("\"" + v + "\"", a);
            if (v.IndexOf('\'') < 0) return new Frag("'" + v + "'", a);
            // both quote kinds: concatenate pieces
            var f = new Frag("(", a);
            var sb = new StringBuilder();
            bool first = true;
            char quote = '"';
            void Flush()
            {
                if (sb.Length == 0) return;
                if (!first) f.Add(" + ", a);
                f.Add(quote + sb.ToString() + quote, a);
                sb.Clear();
                first = false;
            }
            foreach (var ch in v)
            {
                if (ch == quote) { Flush(); quote = quote == '"' ? '\'' : '"'; }
                sb.Append(ch);
            }
            Flush();
            f.Add(")", a);
            return f;
        }

        // ------------------------------------------------------------------ operators
        Frag Binary(IrBinary b)
        {
            var opA = U(b.OpSpan.IsNone ? b.Span : b.OpSpan);
            Frag Bin(string pyOp, int prec, bool cmp)
            {
                var l = Operand(b.Left, cmp ? Frag.Prec_Add : prec);
                Frag r;
                if (pyOp == " and " || pyOp == " or ") { conditionalDepth++; r = Operand(b.Right, prec + 1); conditionalDepth--; }
                else r = Operand(b.Right, cmp ? Frag.Prec_Add : prec + 1);
                var f = new Frag().Add(l).Add(pyOp, opA).Add(r);
                f.Prec = prec;
                return f;
            }
            switch (b.Op)
            {
                case IrBinOp.Concat:
                    {
                        var l = IsNonNullString(b.Left) ? Operand(b.Left, Frag.Prec_Add) : StrCall(E(b.Left), b.Left);
                        var r = IsNonNullString(b.Right) ? Operand(b.Right, Frag.Prec_Add + 1) : StrCall(E(b.Right), b.Right);
                        var f = new Frag().Add(l).Add(" + ", opA).Add(r);
                        f.Prec = Frag.Prec_Add;
                        return f;
                    }
                case IrBinOp.Add: return Bin(" + ", Frag.Prec_Add, false);
                case IrBinOp.Sub: return Bin(" - ", Frag.Prec_Add, false);
                case IrBinOp.Mul: return Bin(" * ", Frag.Prec_Mul, false);
                case IrBinOp.TrueDiv: return Bin(" / ", Frag.Prec_Mul, false);
                case IrBinOp.IntDivFloor: return Bin(" // ", Frag.Prec_Mul, false);
                case IrBinOp.ModFloor: return Bin(" % ", Frag.Prec_Mul, false);
                case IrBinOp.IntDivTrunc:
                    if (IsNonNeg(b.Left) && IsNonNeg(b.Right)) return Bin(" // ", Frag.Prec_Mul, false);
                    return HelperCall("__jdiv", opA, E(b.Left), E(b.Right));
                case IrBinOp.ModTrunc:
                    if (IsNonNeg(b.Left) && IsNonNeg(b.Right)) return Bin(" % ", Frag.Prec_Mul, false);
                    return HelperCall("__jmod", opA, E(b.Left), E(b.Right));
                case IrBinOp.Pow: return HelperCall("__jpow", U(b), E(b.Left), E(b.Right));
                case IrBinOp.Lt: return Bin(" < ", Frag.Prec_Cmp, true);
                case IrBinOp.Gt: return Bin(" > ", Frag.Prec_Cmp, true);
                case IrBinOp.Le: return Bin(" <= ", Frag.Prec_Cmp, true);
                case IrBinOp.Ge: return Bin(" >= ", Frag.Prec_Cmp, true);
                case IrBinOp.Eq: return Bin(" == ", Frag.Prec_Cmp, true);
                case IrBinOp.Ne: return Bin(" != ", Frag.Prec_Cmp, true);
                case IrBinOp.And: return Bin(" and ", Frag.Prec_And, false);
                case IrBinOp.Or: return Bin(" or ", Frag.Prec_Or, false);
            }
            Error("unsupported operator " + b.Op, b.OpSpan);
            return new Frag("None", opA);
        }

        /// <summary>String-typed expressions that can never be null (others get str() so null prints as None instead of failing).</summary>
        static bool IsNonNullString(IrExpr e)
        {
            if (e.Type == null || !e.Type.IsString) return false;
            switch (e)
            {
                case IrLiteral l: return l.Kind == IrLitKind.String;
                case IrBinary b: return b.Op == IrBinOp.Concat;
                case IrCast c: return c.Target != null && c.Target.IsString;
                case IrIndex ix: return ix.Target.Type != null && ix.Target.Type.IsString;
            }
            return false;
        }

        // ------------------------------------------------------------------ calls
        List<Frag> Args(List<IrExpr> args)
        {
            var res = new List<Frag>();
            foreach (var a in args)
            {
                if (a is IrConditional ce)
                {
                    if (!hoistAllowed || sideEffectSeen || conditionalDepth > 0)
                    {
                        Error("?: is only supported as a whole value here; compute it in a variable first", ce);
                        res.Add(new Frag("None", U(ce)));
                        continue;
                    }
                    var tmp = "__jt" + (++tempCounter);
                    bool oldSe = sideEffectSeen;
                    EmitValueTo(ce, vf => w.Line(new Frag(tmp + " = ", S(ce)).Add(vf), S(ce)));
                    sideEffectSeen = oldSe;
                    res.Add(new Frag(tmp, S(ce)));
                    continue;
                }
                res.Add(E(a));
            }
            return res;
        }

        /// <summary>True if the call itself (ignoring its arguments) has no side effects (v1 rules).</summary>
        static bool IsPureCall(IrExpr c)
        {
            switch (c)
            {
                case IrCall call:
                    return call.Kind == IrCallKind.Builtin && (call.Builtin == "min" || call.Builtin == "max" || call.Builtin == "abs");
                case IrOpCall o:
                    {
                        var k = o.Args.Count > 0 ? o.Args[0].Type?.Kind ?? IrTypeKind.Unknown : IrTypeKind.Unknown;
                        switch (o.Op)
                        {
                            case IrOp.Len: return k == IrTypeKind.List || k == IrTypeKind.String || k == IrTypeKind.Array;
                            case IrOp.Contains: return k == IrTypeKind.Map;
                            case IrOp.MapGetOrNull: case IrOp.Floor: case IrOp.Ceil: case IrOp.Round: case IrOp.Sqrt:
                            case IrOp.PosX: case IrOp.PosY: case IrOp.CompanionEntity: case IrOp.CompanionPos:
                                return true;
                        }
                        return false;
                    }
                case IrCast _: return true;
            }
            return true;
        }

        /// <summary>True if evaluating the expression cannot have side effects (no calls except pure reads).</summary>
        static bool IsPure(IrExpr e)
        {
            foreach (var n in IrWalk.Descendants(e))
            {
                switch (n)
                {
                    case IrCall c: if (!IsPureCall(c)) return false; break;
                    case IrOpCall o: if (!IsPureCall(o)) return false; break;
                    case IrListLit _: case IrSetLit _: case IrMapLit _: case IrLambda _: return false;
                }
            }
            return true;
        }

        Frag EmitCall(IrCall c)
        {
            var f = EmitCallCore(c);
            if (!IsPureCall(c)) sideEffectSeen = true;
            return f;
        }

        Frag EmitCallCore(IrCall c)
        {
            var nameA = U(c.NameSpan.IsNone ? c.Span : c.NameSpan);
            var callA = U(c);
            switch (c.Kind)
            {
                case IrCallKind.Function:
                    {
                        if (c.Function == null) break;
                        if (c.Function.Kind == IrSymbolKind.ExternalFunction)
                        {
                            var args = Args(c.Args);
                            var f = new Frag(c.Function.Module, U(c.QualifierSpan.IsNone ? c.NameSpan : c.QualifierSpan)).Add(".", nameA).Add(Py(c.Function), nameA).Add("(", callA);
                            for (int i = 0; i < args.Count; i++) { if (i > 0) f.Add(", ", callA); f.Add(args[i]); }
                            f.Add(")", callA);
                            f.IsCall = true;
                            return f;
                        }
                        return Call(Py(c.Function), nameA, callA, Args(c.Args));
                    }
                case IrCallKind.Builtin:
                    {
                        var args = Args(c.Args);
                        if (c.Builtin == "print" && args.Count == 0) args.Add(new Frag("\"\"", S(c)));
                        return Call(c.Builtin, nameA, callA, args);
                    }
                case IrCallKind.Value:
                    {
                        var target = Operand(c.Callee, Frag.Prec_Atom);
                        var args = Args(c.Args);
                        var f = new Frag().Add(target).Add("(", callA);
                        for (int i = 0; i < args.Count; i++) { if (i > 0) f.Add(", ", callA); f.Add(args[i]); }
                        f.Add(")", callA);
                        f.IsCall = true;
                        return f;
                    }
            }
            Error("unsupported call", c);
            return new Frag("None", callA);
        }

        Frag EmitOp(IrOpCall c)
        {
            var f = EmitOpCore(c);
            if (!IsPureCall(c)) sideEffectSeen = true;
            return f;
        }

        Frag EmitOpCore(IrOpCall c)
        {
            var nameA = U(c.NameSpan.IsNone ? c.Span : c.NameSpan);
            var callA = U(c);
            var a = c.Args;
            IrExpr R() => a.Count > 0 ? a[0] : null;
            List<Frag> Rest() => Args(a.Skip(1).ToList());
            Frag Len(IrExpr target)
            {
                var f = new Frag("len", nameA).Add("(", callA).Add(E(target)).Add(")", callA);
                f.IsCall = true;
                return f;
            }
            Frag Index(IrExpr target, string idx) => new Frag().Add(Operand(target, Frag.Prec_Atom)).Add("[" + idx + "]", nameA);
            int need = 1;
            switch (c.Op)
            {
                case IrOp.Contains: case IrOp.ListAppend: case IrOp.ListIndexOf: case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent:
                case IrOp.SetAdd: case IrOp.MapGetOrNull: case IrOp.MapRemove: case IrOp.MapRemoveIfPresent: case IrOp.ArrayNew:
                    need = 2; break;
                case IrOp.ListInsert: case IrOp.ListSet: case IrOp.MapPut: case IrOp.MapGetOrDefault: case IrOp.ArrayNew2:
                    need = 3; break;
            }
            if (a.Count < need) { Error("wrong number of arguments for " + c.Op, c); return new Frag("None", callA); }
            var rk = R().Type?.Kind ?? IrTypeKind.Unknown;
            switch (c.Op)
            {
                case IrOp.Len: return Len(R());
                case IrOp.IsEmpty:
                    {
                        var f = new Frag().Add(Len(R())).Add(" == 0", nameA);
                        f.Prec = Frag.Prec_Cmp;
                        return f;
                    }
                case IrOp.Contains:
                    {
                        var item = Args(new List<IrExpr> { a[1] })[0];
                        var f = new Frag().Add(Paren(item, Frag.Prec_Add, PU(a[1]))).Add(" in ", nameA).Add(Operand(R(), Frag.Prec_Add));
                        f.Prec = Frag.Prec_Cmp;
                        return f;
                    }
                case IrOp.ListAppend: return Method(R(), "append", nameA, callA, Rest());
                case IrOp.ListInsert: return Method(R(), "insert", nameA, callA, Rest());
                case IrOp.ListPop: return Method(R(), "pop", nameA, callA, Rest());
                case IrOp.SetAdd: return Method(R(), "add", nameA, callA, Rest());
                case IrOp.RemoveValue: return Method(R(), "remove", nameA, callA, Rest());
                case IrOp.MapRemove: return Method(R(), "pop", nameA, callA, Rest());
                case IrOp.RemoveValueIfPresent: return HelperCall("__jremove", callA, E(R()), Rest()[0]);
                case IrOp.ListIndexOf: return HelperCall("__jindexof", callA, E(R()), Rest()[0]);
                case IrOp.Clear:
                    switch (rk)
                    {
                        case IrTypeKind.List: case IrTypeKind.Array: return HelperCall("__jlclear", callA, E(R()));
                        case IrTypeKind.Set: return HelperCall("__jsclear", callA, E(R()));
                        case IrTypeKind.Map: return HelperCall("__jmclear", callA, E(R()));
                    }
                    Error("clear() needs a list, set or map", c);
                    return new Frag("None", callA);
                case IrOp.MapGetOrNull: return HelperCall("__jget", callA, E(R()), Rest()[0]);
                case IrOp.MapGetOrDefault: { var r = Rest(); return HelperCall("__jgetd", callA, E(R()), r[0], r[1]); }
                case IrOp.MapRemoveIfPresent: return HelperCall("__jmremove", callA, E(R()), Rest()[0]);
                case IrOp.MapKeys: return Call("set", nameA, callA, new List<Frag> { E(R()) });
                case IrOp.MapValues: return HelperCall("__jvalues", callA, E(R()));
                case IrOp.ListCopy: return Call("list", nameA, callA, new List<Frag> { E(R()) });
                case IrOp.SetCopy: return Call("set", nameA, callA, new List<Frag> { E(R()) });
                case IrOp.MapCopy: return Call("dict", nameA, callA, new List<Frag> { E(R()) });
                case IrOp.ArrayNew: return HelperCall("__jarr", callA, E(a[0]), E(a[1]));
                case IrOp.ArrayNew2: return HelperCall("__jarr2", callA, E(a[0]), E(a[1]), E(a[2]));
                case IrOp.Floor: return HelperCall("__jfloor", callA, Args(a).ToArray());
                case IrOp.Ceil: return HelperCall("__jceil", callA, Args(a).ToArray());
                case IrOp.Round: return HelperCall("__jround", callA, Args(a).ToArray());
                case IrOp.Sqrt: return HelperCall("__jsqrt", callA, Args(a).ToArray());
                case IrOp.PosX: return Index(R(), "0");
                case IrOp.PosY: return Index(R(), "1");
                case IrOp.CompanionEntity: return Index(R(), "0");
                case IrOp.CompanionPos: return Index(R(), "1");
                case IrOp.ListSet: case IrOp.MapPut:
                    Error("this operation can only be used as a separate statement", c);
                    return new Frag("None", callA);
            }
            Error("unsupported operation " + c.Op, c);
            return new Frag("None", callA);
        }

        // ------------------------------------------------------------------ value analyses
        /// <summary>Conservative "provably &gt;= 0" check (SPEC §6.3).</summary>
        bool IsNonNeg(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l:
                    return (l.Kind == IrLitKind.Int && l.Int >= 0) || (l.Kind == IrLitKind.Float && l.Float >= 0);
                case IrBinary b when b.Op == IrBinOp.Add || b.Op == IrBinOp.Mul || b.Op == IrBinOp.TrueDiv || b.Op == IrBinOp.IntDivTrunc
                                  || b.Op == IrBinOp.IntDivFloor || b.Op == IrBinOp.ModTrunc || b.Op == IrBinOp.ModFloor:
                    return IsNonNeg(b.Left) && IsNonNeg(b.Right);
                case IrCall c when c.Kind == IrCallKind.Builtin:
                    if (c.Builtin == "min") return c.Args.All(IsNonNeg);
                    if (c.Builtin == "max") return c.Args.Any(IsNonNeg);
                    return GameApi.Find(c.Builtin)?.NonNegative ?? false;
                case IrOpCall o:
                    switch (o.Op)
                    {
                        case IrOp.Len: case IrOp.Sqrt: return true;
                        case IrOp.Floor: case IrOp.Ceil: case IrOp.Round: return o.Args.Count > 0 && IsNonNeg(o.Args[0]);
                    }
                    return false;
                case IrName n: return n.Symbol != null && nonNegLocals.Contains(n.Symbol);
                case IrCast ca: return IsNonNeg(ca.Operand);
            }
            return false;
        }

        // ------------------------------------------------------------------ lambdas
        Frag EmitLambda(IrLambda lam)
        {
            // Python closures can read but not rebind outer locals (no nonlocal in the game)
            var captured = IrWalk.Captures(lam);
            if (captured.Count > 0)
            {
                var assigned = IrWalk.AssignedSet(lam);
                foreach (var s in captured)
                    if (assigned.Contains(s))
                    {
                        Error("a closure cannot assign the outer variable '" + s.Name + "' in JavaFarmer (the game has no nonlocal); use a global variable or a list element", lam);
                        break;
                    }
            }
            var name = "__jlambda" + (++lambdaCounter);
            names[name] = "lambda";
            var hdr = S(lam.HeaderSpan.IsNone ? lam.Span : lam.HeaderSpan);
            var oldLoops = loops; bool oldSe = sideEffectSeen, oldHoist = hoistAllowed, oldStruct = structural; int oldCond = conditionalDepth;
            loops = new List<LoopCtx>(); structural = false; conditionalDepth = 0;
            var def = new Frag("def " + name + "(", hdr);
            for (int i = 0; i < lam.Params.Count; i++)
            {
                if (i > 0) def.Add(", ", hdr);
                def.Add(Py(lam.Params[i].Symbol), U(lam.Params[i].NameSpan));
            }
            w.Line(def.Add("):", hdr), hdr);
            w.Indent++;
            int mark = w.Length;
            EmitGlobals(lam.BodyExpr != null ? new IrNode[] { new IrExprStmt(lam.BodyExpr) } : (IEnumerable<IrNode>)(lam.Body?.Stmts ?? new List<IrStmt>()));
            if (lam.BodyExpr != null)
            {
                BeginStatement();
                if (lam.ReturnsValue)
                {
                    var ra = hdr;
                    EmitValueTo(lam.BodyExpr, f => w.Line(new Frag("return ", ra).Add(f), ra));
                }
                else EmitExprStmt(lam.BodyExpr, PU(lam.BodyExpr));
            }
            else if (lam.Body != null)
            {
                foreach (var s in lam.Body.Stmts) EmitStmt(s);
            }
            if (w.Length == mark) w.Line("pass", hdr);
            w.Indent--;
            loops = oldLoops; sideEffectSeen = oldSe; hoistAllowed = oldHoist; structural = oldStruct; conditionalDepth = oldCond;
            return new Frag(name, U(lam.HeaderSpan.IsNone ? lam.Span : lam.HeaderSpan));
        }
    }
}
