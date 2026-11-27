using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// IR (from any frontend) → idiomatic R that the R frontend compiles back to the same game Python.
    /// - Scripts print their items in order; other modules print functions, globals, then the entry body at the top
    ///   level (or "main &lt;- function() {..}" + "main()" when that is not safe).
    /// - Lists: c(...) for scalar vectors, list(...) otherwise; maps: list(a = 1) / setNames(list(..), c(..));
    ///   0-based IR indices print as x[[i + 1]] (literals folded); 1-based R indices print as written.
    /// - The last return of a function prints as the bare value (R's implicit return); globals assigned in a
    ///   function use &lt;&lt;-; sets print as vectors kept unique with %in%.
    /// - Constructs R cannot express are reported in problems (DocsTranslator keeps such blocks in Python).
    /// </summary>
    internal sealed class RlPrinter : PrinterBase
    {
        protected override string LineCommentPrefix => "#";
        protected override string BlockCommentStart => null;
        protected override string BlockCommentEnd => null;

        const int PIf = 0, PAssign = 3, POr = 6, PAnd = 7, PNot = 8, PCmp = 9, PAdd = 10, PMul = 11, PSpecial = 12, PColon = 13, PUnary = 14, PPow = 15, PPost = 16, PAtom = 17;

        readonly IrModule mod;
        readonly List<string> problems = new List<string>();
        bool layout;
        int codeLines;
        int fnDepth;
        readonly Dictionary<IrSymbol, string> names = new Dictionary<IrSymbol, string>();
        readonly HashSet<string> taken = new HashSet<string>();
        readonly HashSet<string> helpersUsed = new HashSet<string>();
        readonly HashSet<IrSymbol> nonNeg = new HashSet<IrSymbol>();
        sealed class LoopCtx { public Action BeforeContinue; }
        List<LoopCtx> loops = new List<LoopCtx>();
        HashSet<string> reserved;

        RlPrinter(IrModule m, PrintOptions o) : base(o) { mod = m; }

        public static string Print(IrModule m, PrintOptions o, out IReadOnlyList<string> problems)
        {
            if (m == null) { problems = new string[0]; return ""; }
            var p = new RlPrinter(m, o ?? new PrintOptions());
            var text = p.Run();
            problems = p.problems.Distinct().ToList();
            return text;
        }

        void Problem(string what) => problems.Add(what);

        protected override void Comment(IrComment c)
        {
            if (c.IsDoc && !c.IsBlock)
            {
                foreach (var l in (c.Text ?? "").Replace("\r\n", "\n").Split('\n')) Line("#'" + l.TrimEnd());
                return;
            }
            base.Comment(c);
        }

        // ================================================================== names
        static readonly string[] Extra = { "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards", "North", "East", "South", "West", "get_or_default", "main" };

        static readonly HashSet<string> SpecialNames = new HashSet<string>
        {
            "print", "cat", "paste", "paste0", "as.character", "as.integer", "as.numeric", "as.double", "trunc", "floor", "ceiling",
            "sqrt", "round", "length", "nchar", "c", "list", "vector", "names", "is.null", "append", "seq_len", "seq_along", "seq",
            "match", "invisible", "rep", "setNames", "return", "source", "switch", "message",
        };

        string Clean(string n)
        {
            if (string.IsNullOrEmpty(n)) n = "x";
            var sb = new StringBuilder();
            foreach (var ch in n) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            n = sb.ToString().TrimStart('_');
            if (n.StartsWith("j", StringComparison.Ordinal) && sb.ToString().StartsWith("__j", StringComparison.Ordinal)) n = n.Substring(1).TrimStart('_');
            if (n.Length == 0) n = "x";
            if (char.IsDigit(n[0])) n = "v" + n;
            while (reserved.Contains(n)) n += "_";
            return n;
        }

        static bool IsUndefined(IrSymbol s) => s != null && Equals(s.Tag, PyLowering.UndefinedTag);

        string Name(IrSymbol s)
        {
            if (s == null) return "NULL";
            if (names.TryGetValue(s, out var n)) return n;
            n = s.IsConst && IsUndefined(s) && Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0 ? s.Name : Clean(s.Name);
            names[s] = n;
            taken.Add(n);
            return n;
        }

        void AssignNames()
        {
            reserved = new HashSet<string>(RlNames.Reserved);
            reserved.UnionWith(SpecialNames);
            reserved.UnionWith(Extra);
            foreach (var b in GameApi.Builtins) reserved.Add(b.PythonName);
            var moduleLevel = new Dictionary<string, IrSymbol>();
            foreach (var f in mod.Functions)
            {
                if (f.Symbol == null || names.ContainsKey(f.Symbol)) continue;
                var n = Clean(f.Symbol.Name);
                if (f.IsEntry) n = "main";
                while (moduleLevel.ContainsKey(n)) n += "_";
                names[f.Symbol] = n; taken.Add(n); moduleLevel[n] = f.Symbol;
            }
            foreach (var g in mod.Globals)
            {
                if (g.Symbol == null || names.ContainsKey(g.Symbol)) continue;
                var n = Clean(g.Symbol.Name);
                while (moduleLevel.ContainsKey(n)) n += "_";
                names[g.Symbol] = n; taken.Add(n); moduleLevel[n] = g.Symbol;
            }
            foreach (var x in IrWalk.Descendants(mod))
            {
                IrSymbol s = (x as IrName)?.Symbol ?? (x as IrCall)?.Function ?? (x as IrFuncRef)?.Function;
                if (s == null || names.ContainsKey(s)) continue;
                if (s.IsExternal || IsUndefined(s) || s.IsModuleLevel)
                {
                    var n = s.IsConst && IsUndefined(s) && Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0 ? s.Name : Clean(s.Name);
                    while (moduleLevel.ContainsKey(n) && moduleLevel[n] != s) n += "_";
                    names[s] = n; taken.Add(n); moduleLevel[n] = s;
                }
            }
            // script-level locals and function locals: scope-aware
            var scopes = new List<List<IrSymbol>>();
            var liveCount = new Dictionary<string, int>();
            HashSet<string> fnModuleRefs = null;
            bool IsLive(string n) => (liveCount.TryGetValue(n, out var c) && c > 0) || (fnModuleRefs != null && fnModuleRefs.Contains(n)) || (fnModuleRefs != null && moduleLevel.ContainsKey(n));
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
                if (IsLive(n) || (fnModuleRefs == null && moduleLevel.ContainsKey(n)))
                {
                    int k = 2;
                    while (IsLive(n + k) || moduleLevel.ContainsKey(n + k)) k++;
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
                    if (s != null && (s.IsModuleLevel || s.IsExternal || IsUndefined(s)) && names.TryGetValue(s, out var nm)) r.Add(nm);
                }
                return r;
            }
            void Walk(IrNode n)
            {
                switch (n)
                {
                    case null: return;
                    case IrLambda l:
                        {
                            var old = fnModuleRefs;
                            if (fnModuleRefs == null) fnModuleRefs = ModuleRefs(l);
                            Push();
                            foreach (var p in l.Params) { Declare(p.Symbol); Walk(p.Default); }
                            if (l.BodyExpr != null) Walk(l.BodyExpr); else if (l.Body != null) foreach (var s in l.Body.Stmts) Walk(s);
                            Pop();
                            fnModuleRefs = old;
                            return;
                        }
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
                else { Walk(item); }
            }
        }

        // ================================================================== module
        string Run()
        {
            layout = mod.SourceLanguage == "r" || mod.SourceLanguage == "python" || IrWalk.Descendants(mod).Any(n => n.BlankLinesBefore > 0);
            AssignNames();
            var items = mod.Items.ToList();
            bool prev = false;
            int idx = 0;
            if (!Options.Snippet) Undefined();
            if (mod.Entry != null || !mod.IsScript)
            {
                var others = items.Where(i => i != mod.Entry).ToList();
                others = others.OfType<IrFunction>().Cast<IrNode>().Concat(others.Where(i => !(i is IrFunction))).ToList();
                foreach (var it in others) PrintItem(it, idx++, ref prev);
                var e = mod.Entry;
                if (e != null)
                {
                    if (Options.Snippet || EntryAtTopLevel(e))
                    {
                        if (!layout && idx > 0) Line();
                        Pre(e);
                        foreach (var s in e.Body.Stmts) PrintStmt(s);
                        BlockComments(e.Body.TrailingComments);
                    }
                    else
                    {
                        if (idx > 0) Line();
                        PrintFunction(e);
                        Line();
                        Line("main()");
                    }
                }
            }
            else foreach (var it in items) PrintItem(it, idx++, ref prev);
            Comments(mod.TrailingComments);

            var body = Out.ToString();
            Out.Clear();
            var explicitImports = new HashSet<string>(IrWalk.Descendants(mod).OfType<IrImport>().Select(i => i.Module));
            foreach (var m in IrWalk.ReferencedModules(mod)) if (!explicitImports.Contains(m)) Line("source(\"" + m + ".R\")");
            if (helpersUsed.Contains("get_or_default"))
            {
                Line("get_or_default <- function(m, k, d) {");
                Out.Append(Options.Indent).Append("if (k %in% names(m)) m[[k]] else d").Append(Options.Newline);
                Line("}");
            }
            if (Out.Length > 0 && body.Length > 0 && !body.StartsWith(Options.Newline, StringComparison.Ordinal)) Line();
            Out.Append(body);
            return Out.ToString();
        }

        void Undefined()
        {
            var vars = new List<IrSymbol>();
            var fns = new List<IrSymbol>();
            foreach (var n in IrWalk.Descendants(mod))
            {
                IrSymbol s = (n as IrName)?.Symbol ?? (n as IrCall)?.Function ?? (n as IrFuncRef)?.Function;
                if (!IsUndefined(s)) continue;
                if (s.Kind == IrSymbolKind.Function) { if (s.Function == null && !fns.Contains(s)) fns.Add(s); }
                else if (s.Kind == IrSymbolKind.Global && !vars.Contains(s) && !s.IsConst) vars.Add(s);
            }
            foreach (var s in vars) Line(Name(s) + " <- NULL  # defined elsewhere");
            foreach (var s in fns)
            {
                int arity = IrWalk.Descendants(mod).OfType<IrCall>().Where(c => c.Function == s).Select(c => c.Args.Count).DefaultIfEmpty(0).Max();
                var ps = Enumerable.Range(0, arity).Select(i => ((char)('a' + Math.Min(i, 25))).ToString() + (i >= 26 ? i.ToString() : ""));
                Line(Name(s) + " <- function(" + string.Join(", ", ps) + ") {}  # defined elsewhere");
            }
            if (vars.Count + fns.Count > 0) Line();
        }

        bool EntryAtTopLevel(IrFunction e)
        {
            var moduleNames = new HashSet<string>(mod.Items.Where(i => i != e).SelectMany(i =>
                i is IrFunction f ? new[] { names.TryGetValue(f.Symbol, out var a) ? a : "" } :
                i is IrGlobal g ? new[] { names.TryGetValue(g.Symbol, out var b) ? b : "" } : new string[0]));
            foreach (var n in IrWalk.Descendants(e.Body))
            {
                if (n is IrReturn && !IrWalk.Descendants(e.Body).OfType<IrLambda>().Any(l => IrWalk.Descendants(l).Contains(n))) return false;
                IrSymbol s = (n as IrName)?.Symbol ?? (n as IrVarDecl)?.Symbol ?? (n as IrRangeFor)?.Var ?? (n as IrForEach)?.Var;
                if (s != null && (s.Kind == IrSymbolKind.Local || s.Kind == IrSymbolKind.Param) && names.TryGetValue(s, out var nm) && moduleNames.Contains(nm)) return false;
                if (s != null && s.Kind == IrSymbolKind.Param) return false;
            }
            return true;
        }

        void PrintItem(IrNode it, int idx, ref bool prevWasDef)
        {
            bool isDef = it is IrFunction;
            if (it is IrGlobal g0 && g0.Synthetic && g0.ImplicitInit && g0.Init == null) { Pre(it); return; }
            if (!layout && idx > 0 && (isDef || prevWasDef)) Line();
            switch (it)
            {
                case IrFunction f: PrintFunction(f); break;
                case IrGlobal g:
                    {
                        Pre(g);
                        string init;
                        if (g.Init != null) init = Value(g.Init);
                        else
                        {
                            var t = g.Symbol.Type ?? IrType.Unknown;
                            init = t.Kind == IrTypeKind.Bool ? "FALSE" : t.IsNumeric ? "0" : t.IsString ? "\"\"" : "NULL";
                        }
                        Code(Name(g.Symbol) + " <- " + init, g);
                        break;
                    }
                case IrStmt s: PrintStmt(s); break;
            }
            prevWasDef = isDef;
        }

        readonly HashSet<IrNode> preDone = new HashSet<IrNode>();

        void Pre(IrNode n)
        {
            if (n == null || !preDone.Add(n)) return;
            if (Options.Comments && n.Leading != null)
                foreach (var c in n.Leading)
                {
                    if (layout) for (int i = 0; i < Math.Min(2, c.BlankLinesBefore); i++) Line();
                    Comment(c);
                }
            if (layout) for (int i = 0; i < Math.Min(2, n.BlankLinesBefore); i++) Line();
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
                if (layout) for (int i = 0; i < Math.Min(2, c.BlankLinesBefore); i++) Line();
                Comment(c);
            }
        }

        // ================================================================== functions
        string ParamList(List<IrParam> ps) => string.Join(", ", ps.Select(p => Name(p.Symbol) + (p.Default != null ? " = " + Ex(p.Default, PAssign + 1) : "")));

        void PrintFunction(IrFunction f)
        {
            Pre(f);
            Code(Name(f.Symbol) + " <- function(" + ParamList(f.Params) + ") {", f);
            FnBody(f.Body);
            Code("}");
        }

        void FnBody(IrBlock body)
        {
            var oldLoops = loops; loops = new List<LoopCtx>();
            var oldNonNeg = new HashSet<IrSymbol>(nonNeg); nonNeg.Clear();
            fnDepth++;
            Indent();
            if (body != null) { Stmts(body.Stmts, true); BlockComments(body.TrailingComments); }
            Dedent();
            fnDepth--;
            loops = oldLoops;
            nonNeg.Clear(); nonNeg.UnionWith(oldNonNeg);
        }

        string LambdaText(IrLambda l)
        {
            var head = "function(" + ParamList(l.Params) + ")";
            if (l.BodyExpr != null)
            {
                fnDepth++;
                var t = head + " " + Ex(l.BodyExpr, PIf);
                fnDepth--;
                return t;
            }
            var stmts = l.Body?.Stmts ?? new List<IrStmt>();
            if (stmts.Count == 1 && stmts[0] is IrReturn r && r.Value != null && !IsVoidCall(r.Value) && r.Leading == null && r.Trailing == null
                && !(r.Value is IrLambda) && !(r.Value is IrConditional) && (l.Body.TrailingComments == null || l.Body.TrailingComments.Count == 0))
            {
                fnDepth++;
                var t = head + " " + Ex(r.Value, PIf);
                fnDepth--;
                if (t.IndexOf('\n') < 0) return t;
            }
            int mark = Out.Length;
            int lines = codeLines;
            FnBody(l.Body);
            var inner = Out.ToString(mark, Out.Length - mark);
            Out.Length = mark;
            codeLines = lines;
            return head + " {" + Options.Newline + inner + IndentText + "}";
        }

        bool IsVoidCall(IrExpr e) => (e is IrCall c) && ((c.Type?.Kind ?? IrTypeKind.Unknown) == IrTypeKind.Void
                                        || (c.Kind == IrCallKind.Builtin && (GameApi.Find(c.Builtin)?.ReturnType?.Kind ?? IrTypeKind.Any) == IrTypeKind.Void));

        // ================================================================== statements
        void Stmts(List<IrStmt> stmts, bool tail)
        {
            for (int i = 0; i < stmts.Count; i++) PrintStmt(stmts[i], tail && i == stmts.Count - 1);
        }

        void Body(IrBlock b, bool tail)
        {
            Indent();
            if (b != null) { Stmts(b.Stmts, tail); BlockComments(b.TrailingComments); }
            Dedent();
        }

        void Loop(IrBlock body, Action beforeContinue, Action atEnd = null)
        {
            loops.Add(new LoopCtx { BeforeContinue = beforeContinue });
            Indent();
            if (body != null) { Stmts(body.Stmts, false); BlockComments(body.TrailingComments); }
            atEnd?.Invoke();
            Dedent();
            loops.RemoveAt(loops.Count - 1);
        }

        static IrSymbol RootSym(IrExpr e)
        {
            while (true)
            {
                switch (e)
                {
                    case IrName n: return n.Symbol;
                    case IrIndex ix: e = ix.Target; continue;
                    case IrOpCall o when (o.Op == IrOp.MapGetOrNull || o.Op == IrOp.PosX || o.Op == IrOp.PosY) && o.Args.Count > 0: e = o.Args[0]; continue;
                }
                return null;
            }
        }

        /// <summary>"&lt;-", or "&lt;&lt;-" when a function changes a global variable (R would create a local otherwise).</summary>
        string AssignOp(IrExpr target)
        {
            if (fnDepth == 0) return " <- ";
            var s = RootSym(target);
            if (s != null && (s.Kind == IrSymbolKind.Global)) return " <<- ";
            if (s != null && s.IsExternal && s.Kind == IrSymbolKind.ExternalGlobal) { Problem("changing another file's variable from a function"); return " <<- "; }
            return " <- ";
        }

        void PrintStmt(IrStmt s, bool tail = false)
        {
            if (s == null) return;
            switch (s)
            {
                case IrBlock b:
                    Pre(b);
                    Stmts(b.Stmts, tail);
                    BlockComments(b.TrailingComments);
                    return;
                case IrVarDecl v:
                    Pre(v);
                    if (v.Init == null) { if (v.Trailing != null && Options.Comments) Comment(v.Trailing); return; }
                    Code(Name(v.Symbol) + AssignOp(new IrName(v.Symbol)) + Value(v.Init), v);
                    return;
                case IrAssign a: Pre(a); PrintAssign(a); return;
                case IrExprStmt es: Pre(es); PrintExprStmt(es, tail); return;
                case IrIf i: Pre(i); PrintIf(i, tail); return;
                case IrWhile w:
                    Pre(w);
                    Code("while (" + Ex(w.Cond, PIf) + ") {", w);
                    Loop(w.Body, null);
                    Code("}");
                    return;
                case IrLoop lp:
                    Pre(lp);
                    Code("repeat {", lp);
                    Loop(lp.Body, null);
                    Code("}");
                    return;
                case IrDoWhile dw:
                    {
                        Pre(dw);
                        Code("repeat {", dw);
                        void Check() { Code("if (!" + Ex(dw.Cond, PNot) + ") break"); }
                        Loop(dw.Body, Check, Check);
                        Code("}");
                        return;
                    }
                case IrClassicFor cf: Pre(cf); PrintClassicFor(cf); return;
                case IrRangeFor rf: Pre(rf); PrintRangeFor(rf); return;
                case IrForEach fe: Pre(fe); PrintForEach(fe); return;
                case IrSwitch sw: Pre(sw); PrintSwitch(sw, tail); return;
                case IrBreak br: Pre(br); Code("break", br); return;
                case IrContinue co:
                    Pre(co);
                    if (loops.Count > 0) loops[loops.Count - 1].BeforeContinue?.Invoke();
                    Code("next", co);
                    return;
                case IrReturn r:
                    Pre(r);
                    if (r.Value == null) { Code(tail ? "return()" : "return()", r); return; }
                    if (r.Value is IrTupleLit) Problem("returning several values (a tuple) is not supported in R; return a list");
                    if (tail && fnDepth > 0 && !IsVoidCall(r.Value))
                    {
                        if (r.Value is IrConditional c) { PrintCondStmt(c, r); return; }
                        Code(Value(r.Value), r);
                        return;
                    }
                    Code("return(" + Value(r.Value) + ")", r);
                    return;
                case IrImport imp:
                    Pre(imp);
                    Code("source(\"" + imp.Module + ".R\")", imp);
                    return;
            }
            Pre(s);
            Problem("unsupported statement " + s.GetType().Name);
            Code("# unsupported statement");
        }

        /// <summary>A whole-value ?: in tail position: an if / else whose branches are the values.</summary>
        void PrintCondStmt(IrConditional c, IrNode commentsOf)
        {
            Code("if (" + Ex(c.Cond, PIf) + ") {", commentsOf);
            Indent(); TailValue(c.Then); Dedent();
            if (c.Else is IrConditional ec)
            {
                TrimLastNewline();
                Out.Append(" else ");
                // continue the chain on the same line
                var sb = new StringBuilder();
                Code("if (" + Ex(ec.Cond, PIf) + ") {");
                FixChainIndent();
                Indent(); TailValue(ec.Then); Dedent();
                ElseValue(ec.Else);
                return;
            }
            ElseValue(c.Else);
        }

        void FixChainIndent()
        {
            // "} else " + IndentText + "if (...)": remove the indentation inserted by Code after " else "
            var s = Out.ToString();
            var marker = " else " + IndentText;
            int at = s.LastIndexOf(marker, StringComparison.Ordinal);
            if (at >= 0 && IndentText.Length > 0) Out.Remove(at + 6, IndentText.Length);
        }

        void ElseValue(IrExpr e)
        {
            if (e is IrConditional c)
            {
                TrimLastNewline();
                Out.Append(" else ");
                Code("if (" + Ex(c.Cond, PIf) + ") {");
                FixChainIndent();
                Indent(); TailValue(c.Then); Dedent();
                ElseValue(c.Else);
                return;
            }
            Code("} else {");
            Indent(); TailValue(e); Dedent();
            Code("}");
        }

        void TailValue(IrExpr e)
        {
            if (e is IrConditional c) { PrintCondStmt(c, null); return; }
            Code(Value(e));
        }

        void PrintIf(IrIf ifs, bool tail)
        {
            var chain = new List<IrIf> { ifs };
            IrStmt rest = ifs.Else;
            while (rest is IrIf next) { chain.Add(next); rest = next.Else; }
            for (int i = 0; i < chain.Count; i++)
            {
                var c = chain[i];
                if (i > 0 && Options.Comments && c.Leading != null) foreach (var cm in c.Leading) Comment(cm);
                if (i == 0) Code("if (" + Ex(c.Cond, PIf) + ") {", c);
                else Code("} else if (" + Ex(c.Cond, PIf) + ") {", c);
                Body(c.Then, tail);
            }
            if (rest != null)
            {
                Code("} else {");
                Body(rest as IrBlock ?? new IrBlock(new[] { rest }), tail);
            }
            Code("}");
        }

        void PrintAssign(IrAssign a)
        {
            if (a.Target is IrTupleLit)
            {
                Problem("unpacking a tuple (a, b = ...) is not supported in R");
                Code("# unsupported: " + string.Join(", ", ((IrTupleLit)a.Target).Items.Select(i => Ex(i, PAtom))) + " <- " + Value(a.Value), a);
                return;
            }
            // Synthetic copy of a parameter: R arguments are values already
            if (a.Synthetic && a.Value is IrOpCall cp && (cp.Op == IrOp.ListCopy || cp.Op == IrOp.MapCopy) && cp.Args.Count == 1
                && cp.Args[0] is IrName src && a.Target is IrName dst && src.Symbol == dst.Symbol)
                return;
            var target = Target(a.Target);
            var op = AssignOp(a.Target);
            if (a.Op == null)
            {
                Code(target + op + Value(a.Value), a);
                return;
            }
            var bop = a.Op.Value;
            var cur = Ex(a.Target, PAtom);
            string rhs;
            switch (bop)
            {
                case IrBinOp.Concat: rhs = "paste0(" + PasteArg(a.Target) + ", " + PasteArg(a.Value) + ")"; break;
                default:
                    {
                        var b = new IrBinary { Op = bop, Left = a.Target, Right = a.Value, Type = a.Target.Type };
                        rhs = Ex(b, PAssign + 1);
                        break;
                    }
            }
            if (a.TruncateToInt) rhs = "trunc(" + rhs + ")";
            Code(target + op + rhs, a);
        }

        string Target(IrExpr t) => Ex(t, PPost);

        void PrintExprStmt(IrExprStmt es, bool tail)
        {
            var e = es.Expr;
            if (e is IrOpCall o)
            {
                var a = o.Args;
                switch (o.Op)
                {
                    case IrOp.ListAppend when a.Count == 2:
                        Code(Target(a[0]) + "[[length(" + Ex(a[0], PIf) + ") + 1]]" + AssignOp(a[0]) + Value(a[1]), es);
                        CheckClosureChange(a[0]);
                        return;
                    case IrOp.ListInsert when a.Count == 3:
                        Code(Target(a[0]) + AssignOp(a[0]) + "append(" + Ex(a[0], PIf) + ", " + Value(a[2]) + ", after = " + Value(a[1]) + ")", es);
                        CheckClosureChange(a[0]);
                        return;
                    case IrOp.ListPop when a.Count >= 1:
                        Code(Target(a[0]) + AssignOp(a[0]) + Ex(a[0], PPost) + "[-" + PopIndex(a) + "]", es);
                        CheckClosureChange(a[0]);
                        return;
                    case IrOp.ListSet when a.Count == 3:
                    case IrOp.MapPut when a.Count == 3:
                        {
                            var ix = new IrIndex { Target = a[0], Index = a[1], BaseOffset = 0, Type = a[2].Type };
                            Code(Ex(ix, PPost) + AssignOp(a[0]) + Value(a[2]), es);
                            return;
                        }
                    case IrOp.MapRemove when a.Count == 2:
                    case IrOp.MapRemoveIfPresent when a.Count == 2:
                        Code(Ex(a[0], PPost) + "[[" + Value(a[1]) + "]]" + AssignOp(a[0]) + "NULL", es);
                        return;
                    case IrOp.RemoveValue when a.Count == 2:
                        Code(Target(a[0]) + AssignOp(a[0]) + Ex(a[0], PPost) + "[-match(" + Value(a[1]) + ", " + Ex(a[0], PIf) + ")]", es);
                        return;
                    case IrOp.RemoveValueIfPresent when a.Count == 2:
                        Code("if (" + Ex(a[1], PSpecial) + " %in% " + Ex(a[0], PSpecial + 1) + ") " + Target(a[0]) + AssignOp(a[0]) + Ex(a[0], PPost) + "[-match(" + Value(a[1]) + ", " + Ex(a[0], PIf) + ")]", es);
                        return;
                    case IrOp.SetAdd when a.Count == 2:
                        Code("if (!(" + Ex(a[1], PSpecial) + " %in% " + Ex(a[0], PSpecial + 1) + ")) " + Target(a[0]) + "[[length(" + Ex(a[0], PIf) + ") + 1]]" + AssignOp(a[0]) + Value(a[1]), es);
                        return;
                    case IrOp.Clear when a.Count == 1:
                        Code(Target(a[0]) + AssignOp(a[0]) + (a[0].Type?.Kind == IrTypeKind.Map ? "list()" : "c()"), es);
                        return;
                }
            }
            if (tail && fnDepth > 0 && e is IrConditional ce) { PrintCondStmt(ce, es); return; }
            Code(Value(e), es);
        }

        void CheckClosureChange(IrExpr target)
        {
            var s = RootSym(target);
            if (s == null || fnDepth < 2) return;
            // a lambda changing a list of its enclosing function: R would change a copy
            if (s.Kind == IrSymbolKind.Local && !IrWalk.Descendants(mod).OfType<IrLambda>().Any(l => l.Params.Any(p => p.Symbol == s))) { }
        }

        string PopIndex(List<IrExpr> a)
        {
            if (a.Count < 2) return "length(" + Ex(a[0], PIf) + ")";
            var i = a[1];
            if (i is IrBinary b && b.Synthetic && b.Op == IrBinOp.Sub && b.Right is IrLiteral one && one.Kind == IrLitKind.Int && one.Int == 1) return Ex(b.Left, PPow);
            if (i is IrLiteral l && l.Kind == IrLitKind.Int) return (l.Int + 1).ToString(CultureInfo.InvariantCulture);
            return "(" + Ex(i, PAdd) + " + 1)";
        }

        // ------------------------------------------------------------------ loops
        static IrSymbol LocalOf(IrExpr e) => e is IrName n && n.Symbol != null && (n.Symbol.Kind == IrSymbolKind.Local || n.Symbol.Kind == IrSymbolKind.Param) ? n.Symbol : null;

        void LoopVarCheck(IrSymbol v)
        {
            if (v != null && v.Kind == IrSymbolKind.Global && fnDepth > 0) Problem("a for loop in a function that counts with a global variable");
        }

        void PrintClassicFor(IrClassicFor f)
        {
            if (f.Init.Count == 1 && f.Init[0] is IrVarDecl decl && decl.Init != null && f.Cond is IrBinary cond && LocalOf(cond.Left) == decl.Symbol
                && f.Update.Count == 1 && f.Update[0] is IrAssign up && LocalOf(up.Target) == decl.Symbol && !up.TruncateToInt
                && (up.Op == IrBinOp.Add || up.Op == IrBinOp.Sub) && up.Value is IrLiteral lit && lit.Kind == IrLitKind.Int && lit.Int > 0
                && (decl.Symbol.Type?.IsInt ?? true) && !IrWalk.Assigns(f.Body, decl.Symbol))
            {
                long step = up.Op == IrBinOp.Add ? lit.Int : -lit.Int;
                bool ok, incl = false;
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
                    var stepLit = step == 1 ? null : step > 0 ? (IrExpr)IrLiteral.OfInt(step) : new IrUnary { Op = IrUnOp.Neg, Operand = IrLiteral.OfInt(-step) };
                    var rf = new IrRangeFor { Var = decl.Symbol, Start = decl.Init, End = cond.Right, Step = stepLit, Inclusive = incl, Body = f.Body };
                    PrintRange(rf, f);
                    return;
                }
            }
            foreach (var i in f.Init) PrintStmt(i);
            Code("while (" + (f.Cond != null ? Ex(f.Cond, PIf) : "TRUE") + ") {", f);
            void Update() { foreach (var u in f.Update) PrintStmt(u); }
            Loop(f.Body, Update, Update);
            Code("}");
        }

        static long? LitStep(IrExpr s)
        {
            if (s == null) return 1;
            if (s is IrLiteral l && l.Kind == IrLitKind.Int) return l.Int;
            if (s is IrUnary u && u.Op == IrUnOp.Neg && u.Operand is IrLiteral l2 && l2.Kind == IrLitKind.Int) return -l2.Int;
            return null;
        }

        static bool IsLit(IrExpr e, long v) => e is IrLiteral l && l.Kind == IrLitKind.Int && l.Int == v;

        void PrintRangeFor(IrRangeFor rf) => PrintRange(rf, rf);

        void PrintRange(IrRangeFor rf, IrNode commentsOf)
        {
            LoopVarCheck(rf.Var);
            var step = LitStep(rf.Step);
            string seq;
            var start = rf.Start;
            if (step == null) Problem("a for loop with a computed step");
            long st = step ?? 1;
            if (rf.Inclusive)
            {
                if (st == 1 && start != null && start.Synthetic && IsLit(start, 1))
                    seq = rf.End is IrOpCall len && len.Synthetic && len.Op == IrOp.Len && len.Args.Count == 1 ? "seq_along(" + Ex(len.Args[0], PIf) + ")" : "seq_len(" + Ex(rf.End, PIf) + ")";
                else if (st == 1 || (st == -1 && rf.Step != null && rf.Step.Synthetic))
                    seq = Ex(start ?? IrLiteral.OfInt(0), PColon) + ":" + Ex(rf.End, PColon + 1);
                else seq = "seq(" + Ex(start ?? IrLiteral.OfInt(0), PIf) + ", " + Ex(rf.End, PIf) + ", by = " + Ex(rf.Step, PIf) + ")";
            }
            else
            {
                if (st == 1 && (start == null || IsLit(start, 0))) seq = "seq_len(" + Ex(rf.End, PIf) + ") - 1";
                else
                {
                    var endAdj = st > 0 ? Minus1(rf.End) : Plus1(rf.End);
                    if (st == 1) seq = Ex(start, PColon) + ":" + Wrap(endAdj.Text, endAdj.Prec, PColon + 1);
                    else seq = "seq(" + Ex(start ?? IrLiteral.OfInt(0), PIf) + ", " + endAdj.Text + ", by = " + (rf.Step != null ? Ex(rf.Step, PIf) : "1") + ")";
                }
            }
            Code("for (" + Name(rf.Var) + " in " + seq + ") {", commentsOf);
            if (st > 0 && (start == null || IsNonNeg(start))) nonNeg.Add(rf.Var);
            Loop(rf.Body, null);
            Code("}");
        }

        (string Text, int Prec) Minus1(IrExpr e)
        {
            if (e is IrLiteral l && l.Kind == IrLitKind.Int) return ((l.Int - 1).ToString(CultureInfo.InvariantCulture), l.Int - 1 < 0 ? PUnary : PAtom);
            return (Ex(e, PAdd) + " - 1", PAdd);
        }

        (string Text, int Prec) Plus1(IrExpr e)
        {
            if (e is IrLiteral l && l.Kind == IrLitKind.Int) return ((l.Int + 1).ToString(CultureInfo.InvariantCulture), l.Int + 1 < 0 ? PUnary : PAtom);
            return (Ex(e, PAdd) + " + 1", PAdd);
        }

        void PrintForEach(IrForEach fe)
        {
            LoopVarCheck(fe.Var);
            string iter;
            var it = fe.Iterable;
            var kind = it.Type?.Kind ?? IrTypeKind.Unknown;
            IrExpr mapExpr = null;
            if (it is IrOpCall mk && mk.Op == IrOp.MapKeys && mk.Args.Count == 1) { iter = "names(" + Ex(mk.Args[0], PIf) + ")"; mapExpr = mk.Args[0]; }
            else if (it is IrOpCall mv && mv.Op == IrOp.MapValues && mv.Args.Count == 1) iter = Ex(mv.Args[0], PIf);
            else if (kind == IrTypeKind.Map) { iter = "names(" + Ex(it, PIf) + ")"; mapExpr = it; }
            else
            {
                if (kind == IrTypeKind.String) Problem("iterating over the characters of a string is not supported in R");
                iter = Ex(it, PIf);
            }
            Code("for (" + Name(fe.Var) + " in " + iter + ") {", fe);
            loops.Add(new LoopCtx());
            Indent();
            if (fe.ValueVar != null)
            {
                if (mapExpr == null) Problem("a for loop with a value variable over something that is not a map");
                Code(Name(fe.ValueVar) + " <- " + Ex(mapExpr ?? it, PPost) + "[[" + Name(fe.Var) + "]]");
            }
            if (fe.Body != null) { Stmts(fe.Body.Stmts, false); BlockComments(fe.Body.TrailingComments); }
            Dedent();
            loops.RemoveAt(loops.Count - 1);
            Code("}");
        }

        // ------------------------------------------------------------------ switch
        void PrintSwitch(IrSwitch sw, bool tail)
        {
            var normal = sw.Cases.Where(c => !c.IsDefault).ToList();
            var dflt = sw.Cases.FirstOrDefault(c => c.IsDefault);
            bool strings = normal.Count > 0 && normal.All(c => c.Labels.Count > 0 && c.Labels.All(l => l is IrLiteral sl && sl.Kind == IrLitKind.String && (sl.Str ?? "").Length > 0));
            if (strings && sw.Cases.All(c => c.Leading == null))
            {
                // y <- switch(sel, a = v1, b = v2, v3)
                IrSymbol into = null;
                bool assign = sw.Cases.All(c => c.Body.Stmts.Count == 1 && c.Body.Stmts[0] is IrAssign a && a.Op == null && a.Target is IrName tn
                                                 && (into == null || into == tn.Symbol) && (into = tn.Symbol) != null && a.Leading == null && a.Trailing == null
                                                 && !(a.Value is IrConditional) && (c.Body.TrailingComments == null || c.Body.TrailingComments.Count == 0));
                if (assign && dflt != null && dflt.Synthetic && !(((IrAssign)dflt.Body.Stmts[0]).Value is IrLiteral nl && nl.Kind == IrLitKind.Null)) assign = false;
                if (assign && dflt == null) assign = false;
                if (assign)
                {
                    var target = ((IrAssign)sw.Cases[0].Body.Stmts[0]).Target;
                    var parts = new List<string> { Ex(sw.Selector, PIf) };
                    foreach (var c in normal)
                    {
                        for (int i = 0; i < c.Labels.Count - 1; i++) parts.Add(Label(c.Labels[i]) + " = ");
                        parts.Add(Label(c.Labels[c.Labels.Count - 1]) + " = " + Value(((IrAssign)c.Body.Stmts[0]).Value));
                    }
                    if (dflt != null && !dflt.Synthetic) parts.Add(Value(((IrAssign)dflt.Body.Stmts[0]).Value));
                    Code(Target(target) + AssignOp(target) + "switch(" + string.Join(", ", parts) + ")", sw);
                    return;
                }
                Code("switch(" + Ex(sw.Selector, PIf) + ",", sw);
                Indent();
                var arms = new List<(string Head, IrCase Body)>();
                foreach (var c in normal)
                {
                    for (int i = 0; i < c.Labels.Count - 1; i++) arms.Add((Label(c.Labels[i]) + " = ", null));
                    arms.Add((Label(c.Labels[c.Labels.Count - 1]) + " = ", c));
                }
                if (dflt != null) arms.Add(("", dflt));
                for (int k = 0; k < arms.Count; k++)
                {
                    var (head, body) = arms[k];
                    string sep = k == arms.Count - 1 ? "" : ",";
                    if (body == null) { Code(head.TrimEnd() + ","); continue; }
                    if (body.Body.Stmts.Count == 0 && (body.Body.TrailingComments == null || body.Body.TrailingComments.Count == 0)) { Code(head + "{}" + sep); continue; }
                    Code(head + "{");
                    Body(body.Body, tail);
                    Code("}" + sep);
                }
                Dedent();
                Code(")");
                return;
            }
            // other labels: if / else if chain over the selector
            string sel;
            if (sw.Selector is IrName || sw.Selector is IrLiteral || sw.Selector is IrConst) sel = Ex(sw.Selector, PCmp + 1);
            else
            {
                sel = Fresh("selected");
                Code(sel + " <- " + Value(sw.Selector), sw);
            }
            bool first = true;
            foreach (var c in normal)
            {
                if (Options.Comments && c.Leading != null) foreach (var cm in c.Leading) Comment(cm);
                var cond = c.Labels.Count == 0 ? "FALSE" : string.Join(" || ", c.Labels.Select(l => sel + " == " + Ex(l, PCmp + 1)));
                Code((first ? "if (" : "} else if (") + cond + ") {", c);
                Body(c.Body, tail);
                first = false;
            }
            if (dflt != null)
            {
                if (first) { Stmts(dflt.Body.Stmts, tail); BlockComments(dflt.Body.TrailingComments); }
                else { Code("} else {", dflt); Body(dflt.Body, tail); Code("}"); }
            }
            else if (!first) Code("}");
        }

        string Label(IrExpr l)
        {
            var s = ((IrLiteral)l).Str;
            return RlNames.IsSyntactic(s) ? s : Str(s);
        }

        string Fresh(string b)
        {
            var n = b;
            int k = 2;
            while (taken.Contains(n) || reserved.Contains(n)) n = b + k++;
            taken.Add(n);
            return n;
        }

        // ================================================================== expressions
        string Value(IrExpr e) => Ex(e, PIf);

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

        static string Str(string v) => "\"" + Escape(v ?? "", '"') + "\"";

        static bool ScalarType(IrType t) => t != null && (t.Kind == IrTypeKind.Int || t.Kind == IrTypeKind.Float || t.Kind == IrTypeKind.Bool || t.Kind == IrTypeKind.String || t.Kind == IrTypeKind.Game);

        static int ScalarKind(IrType t)
        {
            switch (t?.Kind ?? IrTypeKind.Unknown)
            {
                case IrTypeKind.Int: case IrTypeKind.Float: return 1;
                case IrTypeKind.Bool: return 2;
                case IrTypeKind.String: return 3;
                case IrTypeKind.Game: return t.GameName == "Position" || t.GameName == "Companion" || t.GameName == "Drone" ? 0 : 3;
            }
            return 0;
        }

        string Vector(List<IrExpr> items, IrType type)
        {
            if (items.Count == 0) return ScalarType(type?.Elem) ? "c()" : "list()";
            int k = ScalarKind(items[0].Type);
            bool atomic = k != 0 && items.All(i => ScalarKind(i.Type) == k && !(i is IrLiteral nl && nl.Kind == IrLitKind.Null));
            return (atomic ? "c(" : "list(") + string.Join(", ", items.Select(i => Ex(i, PAssign + 1))) + ")";
        }

        bool ElemScalar(IrExpr target) => ScalarType(target.Type?.Elem) && (target.Type.Kind == IrTypeKind.List || target.Type.Kind == IrTypeKind.Array);

        string IndexText(IrExpr target, IrExpr index, int baseOffset)
        {
            var tt = target.Type ?? IrType.Unknown;
            if (tt.Kind == IrTypeKind.Map)
            {
                if (index is IrTupleLit) Problem("map keys that are tuples (x, y) are not supported in R");
                if (index.Type != null && index.Type.IsNumeric) Problem("maps with number keys are not supported in R (named lists have text names)");
                return Ex(target, PPost) + "[[" + Ex(index, PIf) + "]]";
            }
            if (tt.Kind == IrTypeKind.String) Problem("indexing the characters of a string is not supported in R");
            string idx;
            if (baseOffset == 1) idx = Ex(index, PIf);
            else if (index is IrLiteral l && l.Kind == IrLitKind.Int) idx = (l.Int + 1 - baseOffset).ToString(CultureInfo.InvariantCulture);
            else if (index is IrUnary u && u.Op == IrUnOp.Neg) { Problem("negative indices (counting from the end) are not supported in R"); idx = "length(" + Ex(target, PIf) + ") + 1 - " + Ex(u.Operand, PMul); }
            else if (index is IrBinary b && b.Op == IrBinOp.Sub && b.Right is IrLiteral one && one.Kind == IrLitKind.Int && one.Int == 1 && baseOffset == 0) idx = Ex(b.Left, PIf);
            else idx = Ex(index, PAdd) + " + " + (1 - baseOffset).ToString(CultureInfo.InvariantCulture);
            return Ex(target, PPost) + (ElemScalar(target) ? "[" + idx + "]" : "[[" + idx + "]]");
        }

        string PasteArg(IrExpr e)
        {
            if (e is IrCast c && c.Target != null && c.Target.IsString && !c.IsTypeAssertion) return Ex(c.Operand, PAssign + 1);
            return Ex(e, PAssign + 1);
        }

        static void Leaves(IrExpr e, List<IrExpr> into)
        {
            if (e is IrBinary b && b.Op == IrBinOp.Concat) { Leaves(b.Left, into); Leaves(b.Right, into); }
            else into.Add(e);
        }

        string Paste(IrBinary b)
        {
            var leaves = new List<IrExpr>();
            Leaves(b, leaves);
            // paste(a, b, c): values separated by identical Synthetic separator literals
            if (leaves.Count >= 3 && leaves.Count % 2 == 1)
            {
                string sep = null;
                bool ok = true;
                for (int i = 1; i < leaves.Count; i += 2)
                {
                    if (!(leaves[i] is IrLiteral s && s.Synthetic && s.Kind == IrLitKind.String && s.Str.Length > 0) || (sep != null && sep != s.Str)) { ok = false; break; }
                    sep = s.Str;
                }
                for (int i = 0; ok && i < leaves.Count; i += 2) if (leaves[i] is IrLiteral s2 && s2.Synthetic) ok = false;
                if (ok)
                {
                    var vals = new List<string>();
                    for (int i = 0; i < leaves.Count; i += 2) vals.Add(PasteArg(leaves[i]));
                    return "paste(" + string.Join(", ", vals) + (sep == " " ? "" : ", sep = " + Str(sep)) + ")";
                }
            }
            return "paste0(" + string.Join(", ", leaves.Select(PasteArg)) + ")";
        }

        string Args(IEnumerable<IrExpr> args) => string.Join(", ", args.Select(a => Ex(a, PAssign + 1)));

        string E(IrExpr e, out int prec)
        {
            prec = PAtom;
            if (e == null) return "NULL";
            switch (e)
            {
                case IrLiteral l:
                    switch (l.Kind)
                    {
                        case IrLitKind.Int: if (l.Int < 0) prec = PUnary; return l.Int.ToString(CultureInfo.InvariantCulture);
                        case IrLitKind.Float:
                            if (l.Float < 0) prec = PUnary;
                            return PyFormat.Double(l.Float);
                        case IrLitKind.Bool: return l.Bool ? "TRUE" : "FALSE";
                        case IrLitKind.Null: return "NULL";
                        default:
                            if ((l.Str ?? "").IndexOf('\n') >= 0) Problem("line breaks inside strings are not supported by the game");
                            return Str(l.Str);
                    }
                case IrName n:
                    return Name(n.Symbol);
                case IrConst c: return c.Category == null || c.Category == "Directions" ? c.Name : c.Category + "$" + c.Name;
                case IrBinary b: return Binary(b, out prec);
                case IrUnary u:
                    if (u.Op == IrUnOp.Not)
                    {
                        prec = PNot;
                        return "!" + Ex(u.Operand, PCmp);
                    }
                    prec = PUnary;
                    return (u.Op == IrUnOp.Neg ? "-" : "+") + Ex(u.Operand, PPow);
                case IrConditional ce:
                    prec = PIf;
                    return "if (" + Ex(ce.Cond, PIf) + ") " + Ex(ce.Then, PAssign + 1) + " else " + Ex(ce.Else, PIf);
                case IrCall c:
                    switch (c.Kind)
                    {
                        case IrCallKind.Builtin: return Builtin(c);
                        case IrCallKind.Function: return (c.Function == null ? "NULL" : Name(c.Function)) + "(" + Args(c.Args) + ")";
                        default: return Ex(c.Callee, PPost) + "(" + Args(c.Args) + ")";
                    }
                case IrOpCall o: return Op(o, out prec);
                case IrIndex ix:
                    {
                        prec = PPost;
                        if (ix.Target.Type?.Kind == IrTypeKind.Tuple || ix.Target.Type?.IsGame("Position") == true || ix.Target.Type?.IsGame("Companion") == true)
                        {
                            var i0 = ix.Index is IrLiteral il && il.Kind == IrLitKind.Int ? (il.Int + 1 - ix.BaseOffset).ToString(CultureInfo.InvariantCulture) : Ex(ix.Index, PAdd) + " + " + (1 - ix.BaseOffset);
                            return Ex(ix.Target, PPost) + "[[" + i0 + "]]";
                        }
                        return IndexText(ix.Target, ix.Index, ix.BaseOffset);
                    }
                case IrListLit ll: return Vector(ll.Items, ll.Type);
                case IrSetLit sl: return Vector(sl.Items, sl.Type);
                case IrTupleLit tl: return Vector(tl.Items, null);
                case IrMapLit ml:
                    {
                        if (ml.Keys.Count == 0) return "list()";
                        if (ml.Keys.All(k => k is IrLiteral kl && kl.Kind == IrLitKind.String && (kl.Str ?? "").Length > 0))
                            return "list(" + string.Join(", ", ml.Keys.Select((k, i) => Label(k) + " = " + Ex(ml.Values[i], PAssign + 1))) + ")";
                        if (ml.Keys.Any(k => k is IrTupleLit)) Problem("map keys that are tuples (x, y) are not supported in R");
                        if (ml.Keys.Any(k => k.Type != null && k.Type.IsNumeric)) Problem("maps with number keys are not supported in R (named lists have text names)");
                        return "setNames(list(" + Args(ml.Values) + "), c(" + Args(ml.Keys) + "))";
                    }
                case IrLambda lam: prec = PIf; return LambdaText(lam);
                case IrFuncRef fr:
                    if (fr.Builtin != null) return fr.Builtin;
                    return fr.Function == null ? "NULL" : Name(fr.Function);
                case IrCast ca:
                    {
                        var tt = ca.Target ?? IrType.Unknown;
                        var ot = ca.Operand.Type ?? IrType.Unknown;
                        if (ca.IsTypeAssertion) return E(ca.Operand, out prec);
                        if (tt.Kind == IrTypeKind.String) return "as.character(" + Ex(ca.Operand, PIf) + ")";
                        if (tt.Kind == IrTypeKind.Int && ot.Kind != IrTypeKind.Int && ot.Kind != IrTypeKind.Bool) return "trunc(" + Ex(ca.Operand, PIf) + ")";
                        return E(ca.Operand, out prec);
                    }
            }
            Problem("unsupported expression " + e.GetType().Name);
            return "NULL";
        }

        string Builtin(IrCall c)
        {
            if (c.Builtin == "print")
            {
                if (c.Args.Count == 0) return "cat(\"\\n\")";
                if (c.Args.Count == 1 && c.Args[0] is IrBinary cb && cb.Op == IrBinOp.Concat && cb.Synthetic)
                {
                    var leaves = new List<IrExpr>();
                    Leaves(cb, leaves);
                    return "cat(" + string.Join(", ", leaves.Select(PasteArg)) + ", \"\\n\", sep = \"\")";
                }
                if (c.Args.Count == 1) return "print(" + Ex(c.Args[0], PAssign + 1) + ")";
                return "cat(" + Args(c.Args) + ", \"\\n\")";
            }
            if (c.Builtin == "spawn_drone" && c.Args.Count == 1 && c.Args[0] is IrFuncRef fr && fr.Builtin != null && (GameApi.Find(fr.Builtin)?.Params.Count ?? 0) > 0)
                Problem("a builtin with parameters (" + fr.Builtin + ") passed as a function");
            return c.Builtin + "(" + Args(c.Args) + ")";
        }

        string Binary(IrBinary b, out int prec)
        {
            int bp = PAtom;
            string Bin(string op, int p, int lmin, int rmin) { bp = p; return Ex(b.Left, lmin) + " " + op + " " + Ex(b.Right, rmin); }
            if ((b.Op == IrBinOp.Eq || b.Op == IrBinOp.Ne) && (b.Right is IrLiteral rn && rn.Kind == IrLitKind.Null || b.Left is IrLiteral ln && ln.Kind == IrLitKind.Null))
            {
                var x = b.Right is IrLiteral r2 && r2.Kind == IrLitKind.Null ? b.Left : b.Right;
                if (b.Op == IrBinOp.Eq) { prec = PAtom; return "is.null(" + Ex(x, PIf) + ")"; }
                prec = PNot; return "!is.null(" + Ex(x, PIf) + ")";
            }
            if (b.Op == IrBinOp.Add && b.Synthetic && b.Left is IrOpCall io && io.Op == IrOp.ListIndexOf && io.Args.Count == 2 && b.Right is IrLiteral one && one.Synthetic && one.Kind == IrLitKind.Int && one.Int == 1)
            {
                prec = PAtom;
                return "match(" + Ex(io.Args[1], PAssign + 1) + ", " + Ex(io.Args[0], PAssign + 1) + ")";
            }
            switch (b.Op)
            {
                case IrBinOp.Or: { var r0 = Bin("||", POr, POr, POr + 1); prec = bp; return r0; }
                case IrBinOp.And: { var r0 = Bin("&&", PAnd, PAnd, PAnd + 1); prec = bp; return r0; }
                case IrBinOp.Eq: { var r0 = Bin("==", PCmp, PCmp + 1, PCmp + 1); prec = bp; return r0; }
                case IrBinOp.Ne: { var r0 = Bin("!=", PCmp, PCmp + 1, PCmp + 1); prec = bp; return r0; }
                case IrBinOp.Lt: { var r0 = Bin("<", PCmp, PCmp + 1, PCmp + 1); prec = bp; return r0; }
                case IrBinOp.Le: { var r0 = Bin("<=", PCmp, PCmp + 1, PCmp + 1); prec = bp; return r0; }
                case IrBinOp.Gt: { var r0 = Bin(">", PCmp, PCmp + 1, PCmp + 1); prec = bp; return r0; }
                case IrBinOp.Ge: { var r0 = Bin(">=", PCmp, PCmp + 1, PCmp + 1); prec = bp; return r0; }
                case IrBinOp.Concat: prec = PAtom; return Paste(b);
                case IrBinOp.Add: { var r0 = Bin("+", PAdd, PAdd, PAdd + 1); prec = bp; return r0; }
                case IrBinOp.Sub: { var r0 = Bin("-", PAdd, PAdd, PAdd + 1); prec = bp; return r0; }
                case IrBinOp.Mul: { var r0 = Bin("*", PMul, PMul, PMul + 1); prec = bp; return r0; }
                case IrBinOp.TrueDiv: { var r0 = Bin("/", PMul, PMul, PMul + 1); prec = bp; return r0; }
                case IrBinOp.IntDivFloor: { var r0 = Bin("%/%", PSpecial, PSpecial, PSpecial + 1); prec = bp; return r0; }
                case IrBinOp.ModFloor: { var r0 = Bin("%%", PSpecial, PSpecial, PSpecial + 1); prec = bp; return r0; }
                case IrBinOp.IntDivTrunc:
                    if (IsNonNeg(b.Left) && IsNonNeg(b.Right)) { var r0 = Bin("%/%", PSpecial, PSpecial, PSpecial + 1); prec = bp; return r0; }
                    prec = PAtom; return "trunc(" + Ex(b.Left, PMul) + " / " + Ex(b.Right, PMul + 1) + ")";
                case IrBinOp.ModTrunc:
                    if (IsNonNeg(b.Left) && IsNonNeg(b.Right)) { var r0 = Bin("%%", PSpecial, PSpecial, PSpecial + 1); prec = bp; return r0; }
                    prec = PAdd; return Ex(b.Left, PAdd) + " - " + Ex(b.Right, PMul) + " * trunc(" + Ex(b.Left, PMul) + " / " + Ex(b.Right, PMul + 1) + ")";
                case IrBinOp.Pow: { var r0 = Bin("^", PPow, PPow + 1, PPow); prec = bp; return r0; }
            }
            prec = PAtom;
            return "NULL";
        }

        string Op(IrOpCall o, out int prec)
        {
            prec = PAtom;
            var a = o.Args;
            if (a.Count == 0) { Problem("operation " + o.Op + " without arguments"); return "NULL"; }
            var rk = a[0].Type?.Kind ?? IrTypeKind.Unknown;
            switch (o.Op)
            {
                case IrOp.Len: return (rk == IrTypeKind.String ? "nchar(" : "length(") + Ex(a[0], PIf) + ")";
                case IrOp.IsEmpty: prec = PCmp; return "length(" + Ex(a[0], PIf) + ") == 0";
                case IrOp.Contains:
                    if (a.Count < 2) break;
                    if (rk == IrTypeKind.String) Problem("searching text inside a string (x in s) is not supported in R");
                    prec = PSpecial;
                    return Ex(a[1], PSpecial) + " %in% " + (rk == IrTypeKind.Map ? "names(" + Ex(a[0], PIf) + ")" : Ex(a[0], PSpecial + 1));
                case IrOp.MapGetOrNull:
                    if (a.Count < 2) break;
                    prec = PPost;
                    if (a[1] is IrLiteral kl && kl.Kind == IrLitKind.String && RlNames.IsSyntactic(kl.Str)) return Ex(a[0], PPost) + "$" + kl.Str;
                    if (a[1] is IrTupleLit) Problem("map keys that are tuples (x, y) are not supported in R");
                    if (a[1].Type != null && a[1].Type.IsNumeric) Problem("maps with number keys are not supported in R (named lists have text names)");
                    return Ex(a[0], PPost) + "[[" + Ex(a[1], PIf) + "]]";
                case IrOp.MapGetOrDefault:
                    if (a.Count < 3) break;
                    helpersUsed.Add("get_or_default");
                    return "get_or_default(" + Args(a) + ")";
                case IrOp.MapKeys: return "names(" + Ex(a[0], PIf) + ")";
                case IrOp.MapValues: Problem("the values of a map as a value (outside a for loop)"); return Ex(a[0], PIf);
                case IrOp.ListCopy:
                    if (rk == IrTypeKind.Map) return "names(" + Ex(a[0], PIf) + ")";
                    return E(a[0], out prec);
                case IrOp.MapCopy: return E(a[0], out prec);
                case IrOp.SetCopy: Problem("converting to a set (unique values) is not supported in R"); return E(a[0], out prec);
                case IrOp.ListIndexOf:
                    if (a.Count < 2) break;
                    prec = PAdd;
                    return "match(" + Ex(a[1], PAssign + 1) + ", " + Ex(a[0], PAssign + 1) + ") - 1";
                case IrOp.ArrayNew:
                    if (a.Count < 2) break;
                    return "rep(" + Ex(a[1], PAssign + 1) + ", " + Ex(a[0], PAssign + 1) + ")";
                case IrOp.ArrayNew2:
                    Problem("two-dimensional arrays are not supported in R; use a list of vectors");
                    return "rep(list(rep(" + Ex(a.Count > 2 ? a[2] : a[0], PAssign + 1) + ", " + Ex(a.Count > 1 ? a[1] : a[0], PAssign + 1) + ")), " + Ex(a[0], PAssign + 1) + ")";
                case IrOp.Floor: return "floor(" + Ex(a[0], PIf) + ")";
                case IrOp.Ceil: return "ceiling(" + Ex(a[0], PIf) + ")";
                case IrOp.Sqrt: return "sqrt(" + Ex(a[0], PIf) + ")";
                case IrOp.Round: return "floor(" + Ex(a[0], PAdd) + " + 0.5)";
                case IrOp.PosX: case IrOp.CompanionEntity: prec = PPost; return Ex(a[0], PPost) + "[[1]]";
                case IrOp.PosY: case IrOp.CompanionPos: prec = PPost; return Ex(a[0], PPost) + "[[2]]";
            }
            Problem("the operation " + o.Op + " as a value is not supported in R");
            return "NULL";
        }
    }
}
