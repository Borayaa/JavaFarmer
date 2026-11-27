using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// IR → idiomatic game Python (only what the game's parser and runtime support: no slicing, comprehensions,
    /// f-strings, try, lambda, conditional expressions; methods append / add / remove / pop / insert only).
    /// - Scripts print their items in order. Modules with an entry function (Java main) print the other functions and
    ///   globals, then the entry body at top level when that is safe (no local shares a module-level name, no return,
    ///   no used parameter), else "def main():" + "main()".
    /// - Lambdas become a nested def right before the statement that uses them (IrVarDecl of a lambda prints as the
    ///   def itself). IrConditional becomes if / else assigning the target (or a temp "value" when nested).
    /// - Counting IrClassicFor → for-range, else while + update (continue runs the update first); IrDoWhile →
    ///   while True + "if not c: break"; IrSwitch → if / elif / else.
    /// - Java-only operations become helper defs printed once at the top (get_or_default, index_of, trunc, int_div, ...);
    ///   (int) of a float prints "x // 1" when x is provably &gt;= 0, else trunc(x).
    /// - Layout: when the IR carries layout (Python source, BlankLinesBefore) blank lines are reproduced; otherwise a
    ///   blank line separates top-level defs from their neighbours. Comments are kept.
    /// </summary>
    internal sealed class PythonPrinter : PrinterBase
    {
        protected override string LineCommentPrefix => "#";
        protected override string BlockCommentStart => null;
        protected override string BlockCommentEnd => null;

        const int PCond = 0, POr = 1, PAnd = 2, PNot = 3, PCmp = 4, PAdd = 5, PMul = 6, PUnary = 7, PPow = 8, PPost = 9, PAtom = 10;

        readonly IrModule mod;
        bool layout;
        readonly Dictionary<IrSymbol, string> names = new Dictionary<IrSymbol, string>();
        readonly HashSet<string> taken = new HashSet<string>();
        readonly Dictionary<string, string> helperNames = new Dictionary<string, string>();
        readonly HashSet<string> helpersUsed = new HashSet<string>();
        readonly Dictionary<IrExpr, string> hoisted = new Dictionary<IrExpr, string>();
        readonly HashSet<string> fromNames = new HashSet<string>();
        readonly HashSet<string> starModules = new HashSet<string>();
        readonly HashSet<IrSymbol> nonNeg = new HashSet<IrSymbol>();
        sealed class LoopCtx { public Action BeforeContinue; }
        List<LoopCtx> loops = new List<LoopCtx>();
        int codeLines;

        PythonPrinter(IrModule m, PrintOptions o) : base(o) { mod = m; }

        public static string Print(IrModule m, PrintOptions o)
        {
            if (m == null) return "";
            var p = new PythonPrinter(m, o ?? new PrintOptions());
            return p.Run();
        }

        // ================================================================== names
        static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
            "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
            "pass", "raise", "return", "try", "while", "with", "yield",
            "Entities", "Grounds", "Items", "Unlocks", "Leaderboards", "Hats", "North", "South", "East", "West", "__name__",
        };

        HashSet<string> reserved;

        string Clean(string n)
        {
            if (string.IsNullOrEmpty(n)) n = "x";
            var sb = new StringBuilder();
            foreach (var ch in n) sb.Append(ch == '_' || char.IsLetterOrDigit(ch) ? ch : '_');
            n = sb.ToString();
            char c = n[0];
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_')) n = "v_" + n;
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = n.Substring(3).TrimStart('_') is var r && r.Length > 0 ? r : "v";
            while (reserved.Contains(n)) n += "_";
            return n;
        }

        string Unique(string n, ICollection<string> live)
        {
            if (!live.Contains(n)) return n;
            int k = 2;
            while (live.Contains(n + k)) k++;
            return n + k;
        }

        string Name(IrSymbol s)
        {
            if (s == null) return "None";
            if (names.TryGetValue(s, out var n)) return n;
            if (s.Tag as string == PyLowering.UndefinedTag || s.IsExternal) n = s.PyName ?? s.Name;
            else n = Clean(s.Name);
            names[s] = n;
            taken.Add(n);
            return n;
        }

        void AssignNames()
        {
            reserved = new HashSet<string>(Keywords);
            if (mod.SourceLanguage != "python")
            {
                foreach (var b in GameApi.Builtins) reserved.Add(b.PythonName);
                foreach (var b in new[] { "len", "str", "list", "set", "dict", "range" }) reserved.Add(b);
            }
            foreach (var imp in IrWalk.Descendants(mod).OfType<IrImport>())
            {
                if (imp.Module != null) taken.Add(imp.Module);
                if (imp.ImportAll && imp.Module != null) starModules.Add(imp.Module);
                if (imp.Names != null) foreach (var x in imp.Names) fromNames.Add(imp.Module + "." + x);
            }
            var moduleLevel = new Dictionary<string, IrSymbol>();
            foreach (var f in mod.Functions)
            {
                if (f.Symbol == null || names.ContainsKey(f.Symbol)) continue;
                var n = f.IsEntry ? "main" : Clean(f.Symbol.Name);
                n = Unique(n, moduleLevel.Keys);
                names[f.Symbol] = n; taken.Add(n); moduleLevel[n] = f.Symbol;
            }
            foreach (var g in mod.Globals)
            {
                if (g.Symbol == null || names.ContainsKey(g.Symbol)) continue;
                var n = g.Symbol.Tag as string == PyLowering.UndefinedTag ? (g.Symbol.PyName ?? g.Symbol.Name) : Clean(g.Symbol.Name);
                if (moduleLevel.ContainsKey(n)) n = Unique(n + "_", moduleLevel.Keys);
                names[g.Symbol] = n; taken.Add(n); moduleLevel[n] = g.Symbol;
            }
            foreach (var n in IrWalk.Descendants(mod))
            {
                IrSymbol s = (n as IrName)?.Symbol ?? (n as IrCall)?.Function ?? (n as IrFuncRef)?.Function;
                if (s != null && (s.IsExternal || s.Tag as string == PyLowering.UndefinedTag)) Name(s);
            }

            // locals: scope-aware (block scoping of other languages), rename on clash with keywords, with a live local of
            // another symbol, or with a module-level name the same function uses
            var scopes = new List<List<IrSymbol>>();
            var liveCount = new Dictionary<string, int>();
            HashSet<string> fnModuleRefs = null;
            bool IsLive(string n) => (liveCount.TryGetValue(n, out var c) && c > 0) || (fnModuleRefs != null && fnModuleRefs.Contains(n));
            void Enter(IrSymbol s)
            {
                if (scopes.Count == 0) return;
                scopes[scopes.Count - 1].Add(s);
                var nm = names[s];
                liveCount[nm] = liveCount.TryGetValue(nm, out var c) ? c + 1 : 1;
            }
            void Declare(IrSymbol s)
            {
                if (s == null) return;
                if (names.ContainsKey(s)) { if (!s.IsModuleLevel && !s.IsExternal) Enter(s); return; }
                if (s.IsModuleLevel || s.IsExternal) { Name(s); return; }
                var n = Clean(s.Name);
                if (IsLive(n))
                {
                    int k = 2;
                    while (IsLive(n + k)) k++;
                    n = n + k;
                }
                names[s] = n; taken.Add(n);
                Enter(s);
            }
            void Push() => scopes.Add(new List<IrSymbol>());
            void Pop()
            {
                foreach (var s in scopes[scopes.Count - 1]) liveCount[names[s]]--;
                scopes.RemoveAt(scopes.Count - 1);
            }
            HashSet<string> ModuleRefs(IrNode root)
            {
                var r = new HashSet<string>();
                foreach (var x in IrWalk.Descendants(root))
                {
                    IrSymbol s = (x as IrName)?.Symbol ?? (x as IrCall)?.Function ?? (x as IrFuncRef)?.Function;
                    if (s != null && s.IsModuleLevel && names.TryGetValue(s, out var nm)) r.Add(nm);
                }
                return r;
            }
            void Walk(IrNode n)
            {
                switch (n)
                {
                    case null: return;
                    case IrLambda l:
                        Push();
                        foreach (var p in l.Params) { Declare(p.Symbol); Walk(p.Default); }
                        if (l.BodyExpr != null) Walk(l.BodyExpr); else if (l.Body != null) foreach (var s in l.Body.Stmts) Walk(s);
                        Pop();
                        return;
                    case IrBlock b: Push(); foreach (var s in b.Stmts) Walk(s); Pop(); return;
                    case IrVarDecl v: Walk(v.Init); Declare(v.Symbol); return;
                    case IrClassicFor cf:
                        Push();
                        foreach (var s in cf.Init) Walk(s);
                        Walk(cf.Cond); foreach (var s in cf.Update) Walk(s); Walk(cf.Body);
                        Pop();
                        return;
                    case IrRangeFor rf: Walk(rf.Start); Walk(rf.End); Walk(rf.Step); Push(); Declare(rf.Var); Walk(rf.Body); Pop(); return;
                    case IrForEach fe: Walk(fe.Iterable); Push(); Declare(fe.Var); Declare(fe.ValueVar); Walk(fe.Body); Pop(); return;
                    case IrName nm:
                        if (nm.Symbol != null && (nm.Symbol.Kind == IrSymbolKind.Local || nm.Symbol.Kind == IrSymbolKind.Param) && !names.ContainsKey(nm.Symbol)) Declare(nm.Symbol);
                        return;
                }
                foreach (var c in IrWalk.Children(n)) Walk(c);
            }
            foreach (var item in mod.Items)
            {
                if (item is IrFunction f)
                {
                    fnModuleRefs = ModuleRefs(f);
                    Push();
                    foreach (var p in f.Params) { Declare(p.Symbol); Walk(p.Default); }
                    foreach (var s in f.Body.Stmts) Walk(s);
                    Pop();
                    fnModuleRefs = null;
                }
                else if (item is IrGlobal g) { Push(); Walk(g.Init); Pop(); }
                else { Push(); Walk(item); Pop(); }
            }
        }

        string Helper(string logical)
        {
            if (!helperNames.TryGetValue(logical, out var n))
            {
                n = logical;
                while (taken.Contains(n)) n += "_";
                helperNames[logical] = n;
                taken.Add(n);
            }
            helpersUsed.Add(logical);
            if (logical == "int_mod") Helper("int_div");
            if (logical == "new_array2") Helper("new_array");
            return n;
        }

        string Fresh(string baseName)
        {
            var n = baseName;
            int k = 2;
            while (taken.Contains(n) || reserved.Contains(n)) n = baseName + k++;
            taken.Add(n);
            return n;
        }

        // ================================================================== module
        string Run()
        {
            layout = mod.SourceLanguage == "python" || IrWalk.Descendants(mod).Any(n => n.BlankLinesBefore > 0);
            AssignNames();
            var entry = Options.Snippet ? null : mod.Entry;
            var items = mod.Items.ToList();
            if (mod.Entry != null)
            {
                var others = items.Where(i => i != mod.Entry).ToList();
                bool globalsCallFunctions = others.OfType<IrGlobal>().Any(g => g.Init != null && IrWalk.Descendants(g.Init).Any(x => (x is IrCall c && c.Kind == IrCallKind.Function && c.Function != null && !c.Function.IsExternal) || (x is IrFuncRef fr && fr.Function != null && !fr.Function.IsExternal)));
                if (globalsCallFunctions) others = others.OfType<IrFunction>().Cast<IrNode>().Concat(others.Where(i => !(i is IrFunction))).ToList();
                bool prev = false;
                int idx = 0;
                foreach (var it in others) { PrintItem(it, idx++, ref prev); }
                var e = mod.Entry;
                if (Options.Snippet || EntryAtTopLevel(e))
                {
                    if (!layout && idx > 0 && (prev || true)) Line();
                    LeadingComments(e);
                    foreach (var s in e.Body.Stmts) PrintStmt(s);
                    Comments(e.Body.TrailingComments);
                }
                else
                {
                    if (idx > 0) Line();
                    PrintFunction(e, "main");
                    Line();
                    Line("main()");
                }
            }
            else
            {
                bool prev = false;
                int idx = 0;
                foreach (var it in items) PrintItem(it, idx++, ref prev);
            }
            Comments(mod.TrailingComments);

            // prefix: imports of other modules used without an import, helpers
            var body = Out.ToString();
            Out.Clear();
            var explicitImports = new HashSet<string>(IrWalk.Descendants(mod).OfType<IrImport>().Select(i => i.Module));
            // other modules used without an import (Java "Utils.f()"); a Python source keeps exactly its own imports
            if (mod.SourceLanguage != "python")
                foreach (var m in IrWalk.ReferencedModules(mod)) if (!explicitImports.Contains(m)) Line("import " + m);
            bool any = Out.Length > 0;
            foreach (var h in HelperOrder) if (helpersUsed.Contains(h)) { PrintHelper(h); any = true; Line(); }
            if (any && Out.Length > 0 && !body.StartsWith(Options.Newline) && !Out.ToString().EndsWith(Options.Newline + Options.Newline)) Line();
            Out.Append(body);
            return Out.ToString();
        }

        bool EntryAtTopLevel(IrFunction e)
        {
            var moduleNames = new HashSet<string>(mod.Items.Where(i => i != e).SelectMany(i =>
                i is IrFunction f ? new[] { names.TryGetValue(f.Symbol, out var a) ? a : "" } :
                i is IrGlobal g ? new[] { names.TryGetValue(g.Symbol, out var b) ? b : "" } : new string[0]));
            foreach (var n in IrWalk.Descendants(e.Body))
            {
                if (n is IrReturn) { if (!InsideLambda(e.Body, n)) return false; }
                IrSymbol s = (n as IrName)?.Symbol ?? (n as IrVarDecl)?.Symbol ?? (n as IrRangeFor)?.Var ?? (n as IrForEach)?.Var;
                if (s != null && (s.Kind == IrSymbolKind.Local || s.Kind == IrSymbolKind.Param) && names.TryGetValue(s, out var nm) && moduleNames.Contains(nm)) return false;
                if (s != null && s.Kind == IrSymbolKind.Param) return false;
            }
            return true;
        }

        static bool InsideLambda(IrNode root, IrNode target)
        {
            foreach (var l in IrWalk.Descendants(root).OfType<IrLambda>())
                if (IrWalk.Descendants(l).Contains(target)) return true;
            return false;
        }

        void PrintItem(IrNode it, int idx, ref bool prevWasDef)
        {
            bool isDef = it is IrFunction;
            if (it is IrGlobal g0 && g0.Synthetic && g0.ImplicitInit && g0.Init == null)
            {
                // placeholder of a Python global first bound later: nothing to print
                Pre(it);
                return;
            }
            if (!layout && idx > 0 && (isDef || prevWasDef)) Line();
            switch (it)
            {
                case IrFunction f: PrintFunction(f, null); break;
                case IrGlobal g:
                    {
                        Pre(g);
                        string init;
                        if (g.Init != null)
                        {
                            Hoist(g.Init, true);
                            if (g.Init is IrConditional c && !hoisted.ContainsKey(c)) { PrintCondInto(c, v => Name(g.Symbol) + " = " + v, g); break; }
                            init = Value(g.Init);
                        }
                        else
                        {
                            var t = g.Symbol.Type ?? IrType.Unknown;
                            init = t.Kind == IrTypeKind.Bool ? "False" : t.IsNumeric ? "0" : "None";
                        }
                        Code(Name(g.Symbol) + " = " + init, g);
                        break;
                    }
                case IrStmt s: PrintStmt(s); break;
            }
            prevWasDef = isDef;
        }

        /// <summary>Leading comments and blank lines of a node.</summary>
        readonly HashSet<IrNode> preDone = new HashSet<IrNode>();

        void Pre(IrNode n)
        {
            if (n == null || !preDone.Add(n)) return;
            if (Options.Comments && n.Leading != null)
                foreach (var c in n.Leading)
                {
                    if (layout) for (int i = 0; i < c.BlankLinesBefore; i++) Line();
                    Comment(c);
                }
            if (layout) for (int i = 0; i < n.BlankLinesBefore; i++) Line();
        }

        void Code(string text, IrNode commentsOf = null)
        {
            codeLines++;
            if (commentsOf != null) Line(text, commentsOf); else Line(text);
        }

        void BlockComments(IEnumerable<IrComment> cs)
        {
            if (!Options.Comments || cs == null) return;
            foreach (var c in cs)
            {
                if (layout) for (int i = 0; i < c.BlankLinesBefore; i++) Line();
                Comment(c);
            }
        }

        // ================================================================== functions
        void PrintFunction(IrFunction f, string nameOverride)
        {
            Pre(f);
            var sb = new StringBuilder("def ").Append(nameOverride ?? Name(f.Symbol)).Append('(');
            AppendParams(sb, f.Params);
            sb.Append("):");
            Code(sb.ToString(), f);
            var oldLoops = loops; loops = new List<LoopCtx>();
            var oldNonNeg = new HashSet<IrSymbol>(nonNeg); nonNeg.Clear();
            Indent();
            int mark = codeLines;
            var globals = GlobalsOf(f.Body.Stmts);
            // comments above the first statement stay above the (re-derived) global lines, where "global x" was
            if (globals.Count > 0 && f.Body.Stmts.Count > 0) Pre(f.Body.Stmts[0]);
            foreach (var g in globals) Code("global " + g);
            if (f.IsEntry) foreach (var p in f.Params) if (IrWalk.Descendants(f.Body).Any(x => x is IrName n && n.Symbol == p.Symbol)) Code(Name(p.Symbol) + " = []");
            foreach (var s in f.Body.Stmts) PrintStmt(s);
            BlockComments(f.Body.TrailingComments);
            if (codeLines == mark) Code("pass");
            Dedent();
            loops = oldLoops;
            nonNeg.Clear(); nonNeg.UnionWith(oldNonNeg);
        }

        void AppendParams(StringBuilder sb, List<IrParam> ps)
        {
            for (int i = 0; i < ps.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Name(ps[i].Symbol));
                if (ps[i].Default != null) sb.Append(" = ").Append(Ex(ps[i].Default, PCond + 1));
            }
        }

        /// <summary>"global x" names: own-module globals the body assigns (not inside lambdas), first assignment order.</summary>
        List<string> GlobalsOf(IEnumerable<IrNode> body)
        {
            var res = new List<string>();
            void Add(IrSymbol s) { if (s != null && s.Kind == IrSymbolKind.Global && s.Tag as string != PyLowering.UndefinedTag) { var n = Name(s); if (!res.Contains(n)) res.Add(n); } }
            void AddT(IrExpr t) { if (t is IrTupleLit tl) foreach (var i in tl.Items) AddT(i); else if (t is IrName nm) Add(nm.Symbol); }
            void Walk(IrNode n)
            {
                switch (n)
                {
                    case null: return;
                    case IrLambda _: return;
                    case IrAssign a: AddT(a.Target); break;
                    case IrVarDecl v: Add(v.Symbol); break;
                    case IrRangeFor rf: Add(rf.Var); break;
                    case IrForEach fe: Add(fe.Var); Add(fe.ValueVar); break;
                }
                foreach (var c in IrWalk.Children(n)) Walk(c);
            }
            foreach (var n in body) Walk(n);
            return res;
        }

        void PrintLambdaDef(string name, IrLambda l)
        {
            var sb = new StringBuilder("def ").Append(name).Append('(');
            AppendParams(sb, l.Params);
            sb.Append("):");
            Code(sb.ToString());
            var oldLoops = loops; loops = new List<LoopCtx>();
            Indent();
            int mark = codeLines;
            IEnumerable<IrNode> body = l.BodyExpr != null ? new IrNode[] { new IrExprStmt(l.BodyExpr) } : (IEnumerable<IrNode>)(l.Body?.Stmts ?? new List<IrStmt>());
            var globals = GlobalsOf(body);
            if (globals.Count > 0 && l.Body != null && l.Body.Stmts.Count > 0) Pre(l.Body.Stmts[0]);
            foreach (var g in globals) Code("global " + g);
            if (l.BodyExpr != null)
            {
                Hoist(l.BodyExpr, true);
                if (l.ReturnsValue) PrintValueTo(l.BodyExpr, v => "return " + v, null);
                else PrintValueTo(l.BodyExpr, v => v, null);
            }
            else if (l.Body != null)
            {
                foreach (var s in l.Body.Stmts) PrintStmt(s);
                BlockComments(l.Body.TrailingComments);
            }
            if (codeLines == mark) Code("pass");
            Dedent();
            loops = oldLoops;
        }

        // ================================================================== hoisting
        /// <summary>
        /// Prints, before the current statement, the defs of lambdas in the expression and temps for nested ?:
        /// (post-order, so inner ones come first). whole: the expression is the statement's whole value.
        /// </summary>
        void Hoist(IrExpr e, bool whole)
        {
            if (e == null) return;
            var list = new List<(IrExpr, string)>();
            void Collect(IrExpr x, bool isWhole, string lambdaBase)
            {
                if (x == null) return;
                switch (x)
                {
                    case IrLambda l:
                        foreach (var p in l.Params) Collect(p.Default, false, null);
                        list.Add((l, lambdaBase ?? "func"));
                        return;
                    case IrConditional c:
                        Collect(c.Cond, false, null);
                        Collect(c.Then, isWhole, lambdaBase);
                        Collect(c.Else, isWhole, lambdaBase);
                        if (!isWhole) list.Add((c, "value"));
                        return;
                    case IrCall call when call.Kind == IrCallKind.Builtin && call.Builtin == "spawn_drone":
                        foreach (var a in call.Args) Collect(a, false, "task");
                        return;
                }
                foreach (var ch in IrWalk.Children(x)) if (ch is IrExpr ce) Collect(ce, false, null);
            }
            Collect(e, whole, null);
            foreach (var (x, b) in list)
            {
                if (hoisted.ContainsKey(x)) continue;
                var name = Fresh(b);
                if (x is IrLambda l) { PrintLambdaDef(name, l); hoisted[x] = name; }
                else { PrintCondInto((IrConditional)x, v => name + " = " + v, null); hoisted[x] = name; }
            }
        }

        /// <summary>sink(valueText) for a value that may be a whole ?: (→ if / else with one sink line per branch).</summary>
        void PrintValueTo(IrExpr value, Func<string, string> sink, IrNode commentsOf, bool bare = false)
        {
            if (value is IrConditional c && !hoisted.ContainsKey(c)) { PrintCondInto(c, sink, commentsOf); return; }
            Code(sink(Value(value, bare)), commentsOf);
        }

        void PrintCondInto(IrConditional c, Func<string, string> sink, IrNode commentsOf)
        {
            Code("if " + Ex(c.Cond, PCond + 1) + ":", commentsOf);
            Indent(); PrintValueTo(c.Then, sink, null); Dedent();
            Code("else:");
            Indent(); PrintValueTo(c.Else, sink, null); Dedent();
        }

        /// <summary>A whole value (bare: a tuple prints without parentheses, as in "a, b = 1, 2" / "return x, y").</summary>
        string Value(IrExpr e, bool bare = false)
        {
            if (bare && e is IrTupleLit t && t.Items.Count >= 2) return string.Join(", ", t.Items.Select(i => Ex(i, PCond + 1)));
            return Ex(e, PCond);
        }

        // ================================================================== statements
        void PrintStmt(IrStmt s)
        {
            if (s == null) return;
            switch (s)
            {
                case IrBlock b:
                    Pre(b);
                    foreach (var x in b.Stmts) PrintStmt(x);
                    BlockComments(b.TrailingComments);
                    return;
                case IrVarDecl v:
                    Pre(v);
                    if (v.Init == null) { if (v.Trailing != null && Options.Comments) Comment(v.Trailing); return; }
                    if (v.Init is IrLambda lam) { PrintLambdaDef(Name(v.Symbol), lam); return; }
                    Hoist(v.Init, true);
                    PrintValueTo(v.Init, x => Name(v.Symbol) + " = " + x, v);
                    return;
                case IrAssign a: Pre(a); PrintAssign(a); return;
                case IrExprStmt es:
                    Pre(es);
                    Hoist(es.Expr, true);
                    if (es.Expr is IrOpCall o && (o.Op == IrOp.ListSet || o.Op == IrOp.MapPut) && o.Args.Count == 3)
                    {
                        Code(Ex(o.Args[0], PPost) + "[" + Value(o.Args[1]) + "] = " + Value(o.Args[2]), es);
                        return;
                    }
                    PrintValueTo(es.Expr, x => x, es);
                    return;
                case IrIf i: Pre(i); PrintIf(i); return;
                case IrWhile w:
                    Pre(w);
                    Hoist(w.Cond, false);
                    Code("while " + Ex(w.Cond, PCond + 1) + ":", w);
                    Loop(w.Body, null);
                    return;
                case IrLoop lp:
                    Pre(lp);
                    Code("while True:", lp);
                    Loop(lp.Body, null);
                    return;
                case IrDoWhile dw: Pre(dw); PrintDoWhile(dw); return;
                case IrClassicFor cf: Pre(cf); PrintClassicFor(cf); return;
                case IrRangeFor rf: Pre(rf); PrintRangeFor(rf); return;
                case IrForEach fe: Pre(fe); PrintForEach(fe); return;
                case IrSwitch sw: Pre(sw); PrintSwitch(sw); return;
                case IrBreak br: Pre(br); Code("break", br); return;
                case IrContinue co:
                    Pre(co);
                    if (loops.Count > 0) loops[loops.Count - 1].BeforeContinue?.Invoke();
                    Code("continue", co);
                    return;
                case IrReturn r:
                    Pre(r);
                    if (r.Value == null) { Code("return", r); return; }
                    Hoist(r.Value, true);
                    PrintValueTo(r.Value, x => "return " + x, r, true);
                    return;
                case IrImport imp:
                    Pre(imp);
                    if (imp.ImportAll) Code("from " + imp.Module + " import *", imp);
                    else if (imp.Names != null && imp.Names.Count > 0) Code("from " + imp.Module + " import " + string.Join(", ", imp.Names), imp);
                    else Code("import " + imp.Module, imp);
                    return;
            }
            Pre(s);
            Code("pass  # unsupported statement");
        }

        void Body(IrBlock b)
        {
            Indent();
            int mark = codeLines;
            if (b != null)
            {
                foreach (var s in b.Stmts) PrintStmt(s);
                BlockComments(b.TrailingComments);
            }
            if (codeLines == mark) Code("pass");
            Dedent();
        }

        void Loop(IrBlock body, Action beforeContinue, Action atEnd = null)
        {
            loops.Add(new LoopCtx { BeforeContinue = beforeContinue });
            Indent();
            int mark = codeLines;
            if (body != null) { foreach (var s in body.Stmts) PrintStmt(s); BlockComments(body.TrailingComments); }
            atEnd?.Invoke();
            if (codeLines == mark) Code("pass");
            Dedent();
            loops.RemoveAt(loops.Count - 1);
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

        string Target(IrExpr t, bool top = true)
        {
            if (t is IrTupleLit tl)
            {
                var s = string.Join(", ", tl.Items.Select(i => Target(i, false)));
                return top && tl.Items.Count >= 2 ? s : "(" + s + (tl.Items.Count == 1 ? "," : "") + ")";
            }
            return Ex(t, PPost);
        }

        void PrintAssign(IrAssign a)
        {
            Hoist(a.Target, false);
            Hoist(a.Value, a.Op == null);
            var target = Target(a.Target);
            if (a.Op == null)
            {
                PrintValueTo(a.Value, v => target + " = " + v, a, a.Target is IrTupleLit);
                return;
            }
            var op = a.Op.Value;
            if (op == IrBinOp.Concat)
            {
                Code(target + " += " + StrOperand(a.Value, PCond + 1), a);
                return;
            }
            if (a.IsIncDec && !a.TruncateToInt && (op == IrBinOp.Add || op == IrBinOp.Sub))
            {
                Code(target + (op == IrBinOp.Add ? " += " : " -= ") + Value(a.Value), a);
                return;
            }
            bool plain = op == IrBinOp.Add || op == IrBinOp.Sub || op == IrBinOp.Mul || op == IrBinOp.TrueDiv || op == IrBinOp.IntDivFloor
                         || op == IrBinOp.ModFloor || op == IrBinOp.Pow;
            bool nonNegTrunc = (op == IrBinOp.IntDivTrunc || op == IrBinOp.ModTrunc) && IsNonNeg(a.Target) && IsNonNeg(a.Value);
            if ((plain || nonNegTrunc) && !a.TruncateToInt)
            {
                Code(target + " " + OpText(op) + "= " + Value(a.Value), a);
                return;
            }
            string rhs;
            var cur = Ex(a.Target, PPost);
            switch (op)
            {
                case IrBinOp.IntDivTrunc: rhs = nonNegTrunc ? cur + " // " + Ex(a.Value, PMul + 1) : Helper("int_div") + "(" + cur + ", " + Ex(a.Value, PCond + 1) + ")"; break;
                case IrBinOp.ModTrunc: rhs = nonNegTrunc ? cur + " % " + Ex(a.Value, PMul + 1) : Helper("int_mod") + "(" + cur + ", " + Ex(a.Value, PCond + 1) + ")"; break;
                default:
                    {
                        int p = op == IrBinOp.Add || op == IrBinOp.Sub ? PAdd : op == IrBinOp.Pow ? PPow : PMul;
                        rhs = cur + " " + OpText(op) + " " + Ex(a.Value, p + 1);
                        break;
                    }
            }
            if (a.TruncateToInt) rhs = Helper("trunc") + "(" + rhs + ")";
            Code(target + " = " + rhs, a);
        }

        void PrintIf(IrIf ifs)
        {
            Hoist(ifs.Cond, false);
            var chain = new List<IrIf> { ifs };
            IrStmt tail = ifs.Else;
            while (tail is IrIf next) { chain.Add(next); tail = next.Else; }
            for (int i = 1; i < chain.Count; i++) Hoist(chain[i].Cond, false);
            for (int i = 0; i < chain.Count; i++)
            {
                var c = chain[i];
                if (i > 0 && Options.Comments && c.Leading != null) foreach (var cm in c.Leading) Comment(cm);
                Code((i == 0 ? "if " : "elif ") + Ex(c.Cond, PCond + 1) + ":", c);
                Body(c.Then);
            }
            if (tail != null)
            {
                Code("else:", tail is IrBlock tb && tb.Trailing != null ? tb : null);
                Body(tail as IrBlock ?? new IrBlock(new[] { tail }));
            }
        }

        void PrintDoWhile(IrDoWhile dw)
        {
            Code("while True:", dw);
            void Check()
            {
                Hoist(dw.Cond, false);
                Code("if not " + Ex(dw.Cond, PNot) + ":");
                Indent(); Code("break"); Dedent();
            }
            Loop(dw.Body, Check, Check);
        }

        // ------------------------------------------------------------------ for loops
        static IrSymbol LocalOf(IrExpr e) => e is IrName n && n.Symbol != null && (n.Symbol.Kind == IrSymbolKind.Local || n.Symbol.Kind == IrSymbolKind.Param) ? n.Symbol : null;

        static readonly HashSet<IrOp> Mutating = new HashSet<IrOp>
        {
            IrOp.ListAppend, IrOp.ListInsert, IrOp.ListPop, IrOp.RemoveValue, IrOp.RemoveValueIfPresent, IrOp.Clear, IrOp.SetAdd,
            IrOp.MapPut, IrOp.MapRemove, IrOp.MapRemoveIfPresent,
        };

        bool Invariant(IrExpr e, IrNode body)
        {
            switch (e)
            {
                case IrLiteral l: return l.Kind == IrLitKind.Int;
                case IrUnary u when u.Op == IrUnOp.Neg: return Invariant(u.Operand, body);
                case IrBinary b when b.Op == IrBinOp.Add || b.Op == IrBinOp.Sub || b.Op == IrBinOp.Mul: return Invariant(b.Left, body) && Invariant(b.Right, body);
                case IrName n when n.Symbol != null:
                    if (n.Symbol.Kind == IrSymbolKind.Local || n.Symbol.Kind == IrSymbolKind.Param) return !IrWalk.Assigns(body, n.Symbol);
                    return n.Symbol.Kind == IrSymbolKind.Global && n.Symbol.IsConst;
                case IrCall c when c.Kind == IrCallKind.Builtin: return c.Args.Count == 0 && (GameApi.Find(c.Builtin)?.LoopInvariant ?? false);
                case IrOpCall o when o.Op == IrOp.Len && o.Args.Count == 1:
                    {
                        var s = LocalOf(o.Args[0]);
                        if (s == null || IrWalk.Assigns(body, s)) return false;
                        return !IrWalk.Descendants(body).OfType<IrOpCall>().Any(x => Mutating.Contains(x.Op) && x.Args.Count > 0 && LocalOf(x.Args[0]) == s);
                    }
            }
            return false;
        }

        void PrintClassicFor(IrClassicFor f)
        {
            if (f.Init.Count == 1 && f.Init[0] is IrVarDecl decl && decl.Init != null && f.Cond is IrBinary cond && LocalOf(cond.Left) == decl.Symbol
                && f.Update.Count == 1 && f.Update[0] is IrAssign up && LocalOf(up.Target) == decl.Symbol && !up.TruncateToInt
                && (up.Op == IrBinOp.Add || up.Op == IrBinOp.Sub) && up.Value is IrLiteral lit && lit.Kind == IrLitKind.Int && lit.Int > 0
                && (decl.Symbol.Type?.IsInt ?? true) && (decl.Init.Type?.Kind ?? IrTypeKind.Int) != IrTypeKind.Float
                && !IrWalk.Assigns(f.Body, decl.Symbol) && Invariant(cond.Right, f.Body))
            {
                long step = up.Op == IrBinOp.Add ? lit.Int : -lit.Int;
                bool ok = true, incl = false;
                switch (cond.Op)
                {
                    case IrBinOp.Lt: ok = step > 0; break;
                    case IrBinOp.Le: ok = step > 0; incl = true; break;
                    case IrBinOp.Gt: ok = step < 0; break;
                    case IrBinOp.Ge: ok = step < 0; incl = true; break;
                    default: ok = false; break;
                }
                if (ok)
                {
                    Pre(decl);
                    Hoist(decl.Init, false); Hoist(cond.Right, false);
                    var from = Ex(decl.Init, PCond + 1);
                    var to = incl ? Ex(cond.Right, PAdd) + (step > 0 ? " + 1" : " - 1") : Ex(cond.Right, PCond + 1);
                    bool zero = decl.Init is IrLiteral z && z.Kind == IrLitKind.Int && z.Int == 0;
                    var args = zero && step == 1 ? to : from + ", " + to + (step != 1 ? ", " + step.ToString(CultureInfo.InvariantCulture) : "");
                    Code("for " + Name(decl.Symbol) + " in range(" + args + "):", f);
                    if (step > 0 && IsNonNeg(decl.Init)) nonNeg.Add(decl.Symbol);
                    Loop(f.Body, null);
                    return;
                }
            }
            foreach (var i in f.Init) PrintStmt(i);
            if (f.Cond != null) Hoist(f.Cond, false);
            Code("while " + (f.Cond != null ? Ex(f.Cond, PCond + 1) : "True") + ":", f);
            void Update() { foreach (var u in f.Update) PrintStmt(u); }
            Loop(f.Body, Update, Update);
        }

        void PrintRangeFor(IrRangeFor rf)
        {
            Hoist(rf.Start, false); Hoist(rf.End, false); Hoist(rf.Step, false);
            long? step = rf.Step == null ? 1 : rf.Step is IrLiteral sl && sl.Kind == IrLitKind.Int ? sl.Int
                       : rf.Step is IrUnary su && su.Op == IrUnOp.Neg && su.Operand is IrLiteral sl2 && sl2.Kind == IrLitKind.Int ? -sl2.Int : (long?)null;
            string end = rf.Inclusive ? Ex(rf.End, PAdd) + ((step ?? 1) < 0 ? " - 1" : " + 1") : Ex(rf.End, PCond + 1);
            string args;
            if (rf.Start == null && rf.Step == null) args = end;
            else
            {
                args = (rf.Start != null ? Ex(rf.Start, PCond + 1) : "0") + ", " + end;
                if (rf.Step != null) args += ", " + Ex(rf.Step, PCond + 1);
            }
            if (rf.Start is IrLiteral z0 && z0.Kind == IrLitKind.Int && z0.Int == 0 && rf.Step == null && mod.SourceLanguage != "python") args = end;
            Code("for " + Name(rf.Var) + " in range(" + args + "):", rf);
            if ((step ?? 0) > 0 && (rf.Start == null || IsNonNeg(rf.Start))) nonNeg.Add(rf.Var);
            Loop(rf.Body, null);
        }

        void PrintForEach(IrForEach fe)
        {
            Hoist(fe.Iterable, false);
            string iter = fe.Iterable is IrOpCall mk && mk.Op == IrOp.MapKeys && mk.Args.Count == 1 ? Ex(mk.Args[0], PCond + 1) : Value(fe.Iterable);
            var body = fe.Body;
            string var = Name(fe.Var);
            if (fe.Var?.Tag as string == PyLowering.TupleForTag && body != null)
            {
                // "for a, b in xs" was lowered to "for it in xs: a, b = it" (declarations of a / b may precede the unpack)
                int k = 0;
                while (k < body.Stmts.Count && body.Stmts[k] is IrVarDecl vd && vd.Init == null) k++;
                if (k < body.Stmts.Count && body.Stmts[k] is IrAssign un && un.Synthetic && un.Target is IrTupleLit && un.Value is IrName uv && uv.Symbol == fe.Var)
                {
                    var = Target(un.Target);
                    body = new IrBlock(body.Stmts.Where((s, i) => i != k)) { TrailingComments = body.TrailingComments, Span = body.Span };
                }
            }
            string mapRef = iter;
            if (fe.ValueVar != null && !(fe.Iterable is IrName) && !(fe.Iterable is IrOpCall mk2 && mk2.Op == IrOp.MapKeys && mk2.Args[0] is IrName))
            {
                mapRef = Fresh("items");
                Code(mapRef + " = " + iter);
                iter = mapRef;
            }
            Code("for " + var + " in " + iter + ":", fe);
            Action head = null;
            if (fe.ValueVar != null)
            {
                var vname = Name(fe.ValueVar);
                var kname = var;
                head = () => Code(vname + " = " + mapRef + "[" + kname + "]");
            }
            loops.Add(new LoopCtx());
            Indent();
            int mark = codeLines;
            head?.Invoke();
            if (body != null) { foreach (var s in body.Stmts) PrintStmt(s); BlockComments(body.TrailingComments); }
            if (codeLines == mark) Code("pass");
            Dedent();
            loops.RemoveAt(loops.Count - 1);
        }

        void PrintSwitch(IrSwitch sw)
        {
            Hoist(sw.Selector, false);
            string sel;
            if (sw.Selector is IrName || sw.Selector is IrLiteral || sw.Selector is IrConst || hoisted.ContainsKey(sw.Selector)) sel = Ex(sw.Selector, PAdd);
            else
            {
                sel = Fresh("selected");
                Code(sel + " = " + Value(sw.Selector), sw);
            }
            var normal = sw.Cases.Where(c => !c.IsDefault).ToList();
            var dflt = sw.Cases.FirstOrDefault(c => c.IsDefault);
            bool first = true;
            foreach (var c in normal)
            {
                foreach (var l in c.Labels) Hoist(l, false);
                if (Options.Comments && c.Leading != null) foreach (var cm in c.Leading) Comment(cm);
                var cond = string.Join(" or ", c.Labels.Select(l => sel + " == " + Ex(l, PAdd)));
                if (c.Labels.Count == 0) cond = "False";
                Code((first ? "if " : "elif ") + cond + ":", c);
                Body(c.Body);
                first = false;
            }
            if (dflt != null)
            {
                if (first) { foreach (var s in dflt.Body.Stmts) PrintStmt(s); BlockComments(dflt.Body.TrailingComments); }
                else { Code("else:", dflt); Body(dflt.Body); }
            }
        }

        // ================================================================== expressions
        string Ex(IrExpr e, int min)
        {
            var s = E(e, out int p);
            return p < min ? "(" + s + ")" : s;
        }

        bool IsNonNeg(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l: return (l.Kind == IrLitKind.Int && l.Int >= 0) || (l.Kind == IrLitKind.Float && l.Float >= 0);
                case IrBinary b when b.Op == IrBinOp.Add || b.Op == IrBinOp.Mul || b.Op == IrBinOp.TrueDiv || b.Op == IrBinOp.IntDivTrunc
                                  || b.Op == IrBinOp.IntDivFloor || b.Op == IrBinOp.ModTrunc || b.Op == IrBinOp.ModFloor:
                    return IsNonNeg(b.Left) && IsNonNeg(b.Right);
                case IrCall c when c.Kind == IrCallKind.Builtin:
                    if (c.Builtin == "min") return c.Args.All(IsNonNeg);
                    if (c.Builtin == "max") return c.Args.Any(IsNonNeg);
                    return GameApi.Find(c.Builtin)?.NonNegative ?? false;
                case IrOpCall o: return o.Op == IrOp.Len || o.Op == IrOp.Sqrt;
                case IrName n: return n.Symbol != null && nonNeg.Contains(n.Symbol);
                case IrCast ca: return IsNonNeg(ca.Operand);
            }
            return false;
        }

        static string Str(string v)
        {
            v = (v ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
            if (v.IndexOf('"') < 0) return "\"" + v + "\"";
            if (v.IndexOf('\'') < 0) return "'" + v + "'";
            var parts = new List<string>();
            var sb = new StringBuilder();
            char q = '"';
            foreach (var ch in v)
            {
                if (ch == q) { if (sb.Length > 0) parts.Add(q + sb.ToString() + q); sb.Clear(); q = q == '"' ? '\'' : '"'; }
                sb.Append(ch);
            }
            if (sb.Length > 0) parts.Add(q + sb.ToString() + q);
            return string.Join(" + ", parts);
        }

        static bool KnownNonString(IrExpr e) => e?.Type != null && e.Type.IsKnown && !e.Type.IsString && e.Type.Kind != IrTypeKind.Any && e.Type.Kind != IrTypeKind.Null;

        string StrOperand(IrExpr e, int min) => KnownNonString(e) ? "str(" + Ex(e, PCond + 1) + ")" : Ex(e, min);

        string Args(IEnumerable<IrExpr> args) => string.Join(", ", args.Select(a => Ex(a, PCond + 1)));

        /// <summary>(left, op, right) of a comparison-like node, or null.</summary>
        static (IrExpr L, string Op, IrExpr R)? Cmp(IrExpr e)
        {
            switch (e)
            {
                case IrBinary b:
                    switch (b.Op)
                    {
                        case IrBinOp.Eq: return (b.Left, "==", b.Right);
                        case IrBinOp.Ne: return (b.Left, "!=", b.Right);
                        case IrBinOp.Lt: return (b.Left, "<", b.Right);
                        case IrBinOp.Le: return (b.Left, "<=", b.Right);
                        case IrBinOp.Gt: return (b.Left, ">", b.Right);
                        case IrBinOp.Ge: return (b.Left, ">=", b.Right);
                    }
                    return null;
                case IrOpCall o when o.Op == IrOp.Contains && o.Args.Count == 2: return (o.Args[1], "in", o.Args[0]);
                case IrUnary u when u.Op == IrUnOp.Not && u.Operand is IrOpCall c && c.Op == IrOp.Contains && c.Args.Count == 2: return (c.Args[1], "not in", c.Args[0]);
            }
            return null;
        }

        static void FlattenAnd(IrExpr e, List<IrExpr> into)
        {
            if (e is IrBinary b && b.Op == IrBinOp.And && b.Synthetic) { FlattenAnd(b.Left, into); into.Add(b.Right); }
            else into.Add(e);
        }

        string Chain(IrBinary and)
        {
            var parts = new List<IrExpr>();
            FlattenAnd(and, parts);
            if (parts.Count < 2) return null;
            var cmps = parts.Select(Cmp).ToList();
            if (cmps.Any(c => c == null)) return null;
            var sb = new StringBuilder(Ex(cmps[0].Value.L, PAdd));
            for (int i = 0; i < cmps.Count; i++)
            {
                if (i > 0)
                {
                    var l = cmps[i].Value.L;
                    if (!l.Synthetic || Ex(l, PAdd) != Ex(cmps[i - 1].Value.R, PAdd)) return null;
                }
                sb.Append(' ').Append(cmps[i].Value.Op).Append(' ').Append(Ex(cmps[i].Value.R, PAdd));
            }
            return sb.ToString();
        }

        string E(IrExpr e, out int prec)
        {
            prec = PAtom;
            if (e == null) return "None";
            if (hoisted.TryGetValue(e, out var hn)) return hn;
            switch (e)
            {
                case IrLiteral l:
                    switch (l.Kind)
                    {
                        case IrLitKind.Int: if (l.Int < 0) prec = PUnary; return l.Int.ToString(CultureInfo.InvariantCulture);
                        case IrLitKind.Float:
                            if (l.Float < 0 || (l.Float == 0 && double.IsNegativeInfinity(1 / l.Float))) prec = PUnary;
                            return PyFormat.Double(l.Float);
                        case IrLitKind.Bool: return l.Bool ? "True" : "False";
                        case IrLitKind.Null: return "None";
                        default:
                            {
                                var v = l.Str ?? "";
                                if (v.IndexOf('"') >= 0 && v.IndexOf('\'') >= 0) prec = PAdd;
                                return Str(v);
                            }
                    }
                case IrName n:
                    {
                        var s = n.Symbol;
                        if (s == null) return "None";
                        if (s.Kind == IrSymbolKind.ExternalGlobal || s.Kind == IrSymbolKind.ExternalFunction) return Qualified(s);
                        return Name(s);
                    }
                case IrConst c: return c.Category == null || c.Category == "Directions" ? c.Name : c.Category + "." + c.Name;
                case IrBinary b: return Binary(b, out prec);
                case IrUnary u:
                    if (u.Op == IrUnOp.Not)
                    {
                        if (Cmp(u) is var cm && cm != null && cm.Value.Op == "not in") { prec = PCmp; return Ex(cm.Value.L, PAdd) + " not in " + Ex(cm.Value.R, PAdd); }
                        prec = PNot;
                        return "not " + Ex(u.Operand, PNot);
                    }
                    prec = PUnary;
                    return (u.Op == IrUnOp.Neg ? "-" : "+") + Ex(u.Operand, PUnary);
                case IrConditional ce:
                    prec = PCond;
                    return Ex(ce.Then, POr) + " if " + Ex(ce.Cond, POr) + " else " + Ex(ce.Else, PCond);
                case IrCall c:
                    switch (c.Kind)
                    {
                        case IrCallKind.Builtin: return c.Builtin + "(" + Args(c.Args) + ")";
                        case IrCallKind.Function:
                            {
                                var f = c.Function;
                                string fn = f == null ? "None" : f.IsExternal ? Qualified(f) : Name(f);
                                return fn + "(" + Args(c.Args) + ")";
                            }
                        default: return Ex(c.Callee, PPost) + "(" + Args(c.Args) + ")";
                    }
                case IrOpCall o: return Op(o, out prec);
                case IrIndex ix:
                    {
                        string idx;
                        if (ix.Index is IrTupleLit it && it.Items.Count >= 2) idx = "(" + Args(it.Items) + ")";
                        else idx = Ex(ix.Index, PCond + 1);
                        if (ix.BaseOffset != 0) idx = Ex(ix.Index, PAdd) + (ix.BaseOffset > 0 ? " - " + ix.BaseOffset : " + " + (-ix.BaseOffset));
                        prec = PPost;
                        return Ex(ix.Target, PPost) + "[" + idx + "]";
                    }
                case IrListLit ll: return "[" + Args(ll.Items) + "]";
                case IrSetLit sl: return sl.Items.Count == 0 ? "set()" : "{" + Args(sl.Items) + "}";
                case IrMapLit ml: return "{" + string.Join(", ", ml.Keys.Select((k, i) => Ex(k, PCond + 1) + ": " + Ex(ml.Values[i], PCond + 1))) + "}";
                case IrTupleLit tl: return "(" + Args(tl.Items) + (tl.Items.Count == 1 ? "," : "") + ")";
                case IrLambda _: return "None";
                case IrFuncRef fr:
                    if (fr.Builtin != null) return fr.Builtin;
                    if (fr.Function == null) return "None";
                    return fr.Function.IsExternal ? Qualified(fr.Function) : Name(fr.Function);
                case IrCast ca:
                    {
                        var tt = ca.Target ?? IrType.Unknown;
                        var ot = ca.Operand.Type ?? IrType.Unknown;
                        if (tt.Kind == IrTypeKind.String) return "str(" + Ex(ca.Operand, PCond + 1) + ")";
                        if (tt.Kind == IrTypeKind.Int && ot.Kind != IrTypeKind.Int && ot.Kind != IrTypeKind.Bool)
                        {
                            if (IsNonNeg(ca.Operand)) { prec = PMul; return Ex(ca.Operand, PMul) + " // 1"; }
                            return Helper("trunc") + "(" + Ex(ca.Operand, PCond + 1) + ")";
                        }
                        return E(ca.Operand, out prec);
                    }
            }
            return "None";
        }

        string Qualified(IrSymbol s)
        {
            var n = Name(s);
            if (s.Module == null || starModules.Contains(s.Module) || fromNames.Contains(s.Module + "." + s.Name)) return n;
            return s.Module + "." + n;
        }

        string Binary(IrBinary b, out int prec)
        {
            if (b.Op == IrBinOp.And && b.Synthetic)
            {
                var ch = Chain(b);
                if (ch != null) { prec = PCmp; return ch; }
            }
            string Bin(string op, int p, int lmin, int rmin)
            {
                return Ex(b.Left, lmin) + " " + op + " " + Ex(b.Right, rmin);
            }
            switch (b.Op)
            {
                case IrBinOp.Or: prec = POr; return Bin("or", POr, POr, POr + 1);
                case IrBinOp.And: prec = PAnd; return Bin("and", PAnd, PAnd, PAnd + 1);
                case IrBinOp.Eq: case IrBinOp.Ne: case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge:
                    prec = PCmp; return Bin(Cmp(b).Value.Op, PCmp, PAdd, PAdd);
                case IrBinOp.Concat:
                    prec = PAdd;
                    return StrOperand(b.Left, PAdd) + " + " + StrOperand(b.Right, PAdd + 1);
                case IrBinOp.Add: prec = PAdd; return Bin("+", PAdd, PAdd, PAdd + 1);
                case IrBinOp.Sub: prec = PAdd; return Bin("-", PAdd, PAdd, PAdd + 1);
                case IrBinOp.Mul: prec = PMul; return Bin("*", PMul, PMul, PMul + 1);
                case IrBinOp.TrueDiv: prec = PMul; return Bin("/", PMul, PMul, PMul + 1);
                case IrBinOp.IntDivFloor: prec = PMul; return Bin("//", PMul, PMul, PMul + 1);
                case IrBinOp.ModFloor: prec = PMul; return Bin("%", PMul, PMul, PMul + 1);
                case IrBinOp.IntDivTrunc:
                    if (IsNonNeg(b.Left) && IsNonNeg(b.Right)) { prec = PMul; return Bin("//", PMul, PMul, PMul + 1); }
                    prec = PAtom; return Helper("int_div") + "(" + Ex(b.Left, PCond + 1) + ", " + Ex(b.Right, PCond + 1) + ")";
                case IrBinOp.ModTrunc:
                    if (IsNonNeg(b.Left) && IsNonNeg(b.Right)) { prec = PMul; return Bin("%", PMul, PMul, PMul + 1); }
                    prec = PAtom; return Helper("int_mod") + "(" + Ex(b.Left, PCond + 1) + ", " + Ex(b.Right, PCond + 1) + ")";
                case IrBinOp.Pow:
                    // the game's ** is left-associative: a ** b ** c == (a ** b) ** c
                    prec = PPow; return Bin("**", PPow, PPow, PPost);
            }
            prec = PAtom;
            return "None";
        }

        string Op(IrOpCall o, out int prec)
        {
            prec = PAtom;
            var a = o.Args;
            string R(int min = PPost) => a.Count > 0 ? Ex(a[0], min) : "None";
            string Rest(int from = 1) => Args(a.Skip(from));
            string Method(string m) => R() + "." + m + "(" + Rest() + ")";
            string H(string h) => Helper(h) + "(" + Args(a) + ")";
            switch (o.Op)
            {
                case IrOp.Len: return "len(" + R(PCond + 1) + ")";
                case IrOp.IsEmpty: prec = PCmp; return "len(" + R(PCond + 1) + ") == 0";
                case IrOp.Contains: prec = PCmp; return (a.Count > 1 ? Ex(a[1], PAdd) : "None") + " in " + R(PAdd);
                case IrOp.ListAppend: return Method("append");
                case IrOp.ListInsert: return Method("insert");
                case IrOp.ListPop: return Method("pop");
                case IrOp.SetAdd: return Method("add");
                case IrOp.RemoveValue: return Method("remove");
                case IrOp.MapRemove: return Method("pop");
                case IrOp.ListSet: case IrOp.MapPut:
                    return R() + "[" + (a.Count > 1 ? Ex(a[1], PCond + 1) : "") + "] = " + (a.Count > 2 ? Ex(a[2], PCond + 1) : "None");
                case IrOp.ListIndexOf: return H("index_of");
                case IrOp.RemoveValueIfPresent: return H("remove_if_present");
                case IrOp.Clear:
                    {
                        var k = a.Count > 0 ? a[0].Type?.Kind ?? IrTypeKind.Unknown : IrTypeKind.Unknown;
                        return H(k == IrTypeKind.Set ? "clear_set" : k == IrTypeKind.Map ? "clear_dict" : "clear_list");
                    }
                case IrOp.MapGetOrNull: return H("get_or_none");
                case IrOp.MapGetOrDefault: return H("get_or_default");
                case IrOp.MapRemoveIfPresent: return H("pop_if_present");
                case IrOp.MapKeys: return "set(" + R(PCond + 1) + ")";
                case IrOp.MapValues: return H("values_of");
                case IrOp.ListCopy: return "list(" + R(PCond + 1) + ")";
                case IrOp.SetCopy: return "set(" + R(PCond + 1) + ")";
                case IrOp.MapCopy: return "dict(" + R(PCond + 1) + ")";
                case IrOp.ArrayNew: return H("new_array");
                case IrOp.ArrayNew2: return H("new_array2");
                case IrOp.Floor: prec = PMul; return R(PMul) + " // 1";
                case IrOp.Ceil: prec = PUnary; return "-(-" + R(PUnary) + " // 1)";
                case IrOp.Round: prec = PMul; return "(" + R(PAdd) + " + 0.5) // 1";
                case IrOp.Sqrt: prec = PPow; return R(PPow) + " ** 0.5";
                case IrOp.PosX: case IrOp.CompanionEntity: prec = PPost; return R() + "[0]";
                case IrOp.PosY: case IrOp.CompanionPos: prec = PPost; return R() + "[1]";
            }
            return "None";
        }

        // ================================================================== helpers
        static readonly string[] HelperOrder =
        {
            "trunc", "int_div", "int_mod", "get_or_none", "get_or_default", "pop_if_present", "remove_if_present", "index_of",
            "values_of", "clear_list", "clear_set", "clear_dict", "new_array", "new_array2",
        };

        static readonly Dictionary<string, string> HelperSource = new Dictionary<string, string>
        {
            ["trunc"] = "def trunc(x):\n\tif x < 0:\n\t\treturn -(-x // 1)\n\treturn x // 1\n",
            ["int_div"] = "def int_div(a, b):\n\tq = a // b\n\tif q < 0 and q * b != a:\n\t\tq += 1\n\treturn q\n",
            ["int_mod"] = "def int_mod(a, b):\n\treturn a - b * int_div(a, b)\n",
            ["get_or_none"] = "def get_or_none(m, k):\n\tif k in m:\n\t\treturn m[k]\n\treturn None\n",
            ["get_or_default"] = "def get_or_default(m, k, d):\n\tif k in m:\n\t\treturn m[k]\n\treturn d\n",
            ["pop_if_present"] = "def pop_if_present(m, k):\n\tif k in m:\n\t\treturn m.pop(k)\n\treturn None\n",
            ["remove_if_present"] = "def remove_if_present(c, x):\n\tif x in c:\n\t\tc.remove(x)\n",
            ["index_of"] = "def index_of(l, x):\n\ti = 0\n\tfor e in l:\n\t\tif e == x:\n\t\t\treturn i\n\t\ti += 1\n\treturn -1\n",
            ["values_of"] = "def values_of(m):\n\tr = []\n\tfor k in m:\n\t\tr.append(m[k])\n\treturn r\n",
            ["clear_list"] = "def clear_list(l):\n\twhile len(l) > 0:\n\t\tl.pop()\n",
            ["clear_set"] = "def clear_set(s):\n\tfor x in list(s):\n\t\ts.remove(x)\n",
            ["clear_dict"] = "def clear_dict(m):\n\tfor k in list(m):\n\t\tm.pop(k)\n",
            ["new_array"] = "def new_array(n, d):\n\tr = []\n\tfor i in range(n):\n\t\tr.append(d)\n\treturn r\n",
            ["new_array2"] = "def new_array2(w, h, d):\n\tr = []\n\tfor i in range(w):\n\t\tr.append(new_array(h, d))\n\treturn r\n",
        };

        void PrintHelper(string logical)
        {
            var src = HelperSource[logical];
            // rename the helper (and the helpers it calls) when a user name took the default name
            foreach (var kv in helperNames) if (kv.Key != kv.Value) src = System.Text.RegularExpressions.Regex.Replace(src, @"\b" + kv.Key + @"\(", kv.Value + "(");
            foreach (var line in src.Split('\n'))
            {
                if (line.Length == 0) continue;
                int tabs = 0;
                while (tabs < line.Length && line[tabs] == '\t') tabs++;
                Out.Append(string.Concat(Enumerable.Repeat(Options.Indent, tabs))).Append(line.Substring(tabs)).Append(Options.Newline);
            }
        }
    }
}
