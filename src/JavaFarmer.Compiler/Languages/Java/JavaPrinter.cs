using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Farm IR → idiomatic JavaFarmer Java (SPEC §3-§5): a compact source file (Java 25) with fields and methods at
    /// top level and the entry point / script statements in "void main() { ... }". Snippet mode prints bare statements
    /// the way the docs show them. The output compiles again with JavaCompiler.Compile whenever the IR only uses
    /// constructs Java can express; everything else is reported in Problems (and printed as a comment or a
    /// compile-failing placeholder, never silently dropped).
    /// Untyped sources (Python, JS, R) are typed by a small inference pass (JavaTypeGuess) without touching the IR.
    /// </summary>
    public sealed class JavaPrinter : PrinterBase
    {
        /// <summary>Constructs the IR uses that have no Java equivalent (empty when the output is faithful).</summary>
        public readonly List<string> Problems = new List<string>();

        public static string Print(IrModule module, PrintOptions options) => Print(module, options, out _);

        public static string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            var p = new JavaPrinter(module, options);
            try { p.Module(); }
            catch (Exception ex) { p.Problems.Add("internal printer error: " + ex.Message); problems = p.Problems; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
            problems = p.Problems;
            return p.Result;
        }

        readonly IrModule mod;
        readonly bool fromJava;           // Java source: keep names and literal forms exactly (round trip)
        JavaTypeGuess types;

        JavaPrinter(IrModule module, PrintOptions options) : base(options)
        {
            mod = module ?? new IrModule();
            fromJava = mod.SourceLanguage == null || mod.SourceLanguage == "java";
        }

        void Problem(string what)
        {
            if (!Problems.Contains(what)) Problems.Add(what);
        }

        // ================================================================== names

        static readonly HashSet<string> Reserved = new HashSet<string>(Lexer.Keywords)
        {
            "_", "String", "Object", "Integer", "Double", "Boolean", "List", "ArrayList", "Map", "HashMap", "Set", "HashSet",
            "Collection", "Runnable", "Supplier", "Math", "System", "Arrays", "Collections",
            "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion",
            "Entities", "Grounds", "Items", "Hats", "Unlocks", "Leaderboards", "Directions",
            "North", "East", "South", "West",
        };

        /// <summary>Source identifier → legal Java identifier (camelCase for snake_case sources, reserved words get "_").</summary>
        internal static string Ident(string name, bool camel)
        {
            if (string.IsNullOrEmpty(name)) return "x";
            if (name == "_") return "ignored";
            var n = name;
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = "j" + n.Substring(3);
            if (camel && n.IndexOf('_') >= 0 && n.Any(char.IsLower))
            {
                var parts = n.Split('_').Where(p => p.Length > 0).ToList();
                if (parts.Count == 0) return "ignored";
                var sb = new StringBuilder(parts[0]);
                for (int i = 1; i < parts.Count; i++) sb.Append(char.ToUpperInvariant(parts[i][0])).Append(parts[i].Substring(1));
                n = sb.ToString();
            }
            var clean = new StringBuilder();
            foreach (var ch in n) clean.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            n = clean.ToString();
            if (n.Length == 0) n = "x";
            if (char.IsDigit(n[0])) n = "_" + n;
            if (Reserved.Contains(n)) n += "_";
            return n;
        }

        readonly Dictionary<IrSymbol, string> names = new Dictionary<IrSymbol, string>();
        readonly List<Dictionary<string, IrSymbol>> scopes = new List<Dictionary<string, IrSymbol>>();
        readonly HashSet<string> methodNames = new HashSet<string>();
        readonly HashSet<string> fieldNames = new HashSet<string>();

        bool CamelNames => !fromJava;

        void PushScope() => scopes.Add(new Dictionary<string, IrSymbol>());
        void PopScope() { if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1); }

        IrSymbol LiveLocal(string n)
        {
            for (int i = scopes.Count - 1; i >= 0; i--) if (scopes[i].TryGetValue(n, out var s)) return s;
            return null;
        }

        /// <summary>Declares a local / parameter in the current scope (renames on a clash with a live local).</summary>
        string Declare(IrSymbol s)
        {
            if (s == null) return "x";
            var baseName = Ident(s.Name, CamelNames);
            var n = baseName;
            int k = 2;
            while (true)
            {
                var live = LiveLocal(n);
                if (live == null || live == s) break;
                n = baseName + k++;
            }
            names[s] = n;
            if (scopes.Count == 0) PushScope();
            scopes[scopes.Count - 1][n] = s;
            return n;
        }

        string Name(IrSymbol s)
        {
            if (s == null) return "null";
            if (names.TryGetValue(s, out var n)) return n;
            n = Ident(s.Name, CamelNames);
            names[s] = n;
            return n;
        }

        void AssignModuleNames()
        {
            // methods first (they keep their names), then fields; API names and "main" are avoided
            foreach (var f in mod.Functions.Concat(undefinedFns.Select(s => new IrFunction { Symbol = s })))
            {
                if (f.Symbol == null) continue;
                string n;
                if (f.IsEntry) n = "main";
                else
                {
                    var b = Ident(f.Symbol.Name, CamelNames);
                    if (GameApi.Overloads(b).Count > 0 || b == "main") b += "_";
                    n = b;
                    int k = 2;
                    while (methodNames.Contains(n)) n = b + k++;
                }
                names[f.Symbol] = n;
                methodNames.Add(n);
            }
            foreach (var s in FieldSymbols())
            {
                var b = Ident(s.Name, CamelNames);
                var n = b;
                int k = 2;
                while (fieldNames.Contains(n)) n = b + k++;
                names[s] = n;
                fieldNames.Add(n);
            }
        }

        IEnumerable<IrSymbol> FieldSymbols()
        {
            foreach (var g in mod.Globals) if (g.Symbol != null && !localized.Contains(g.Symbol)) yield return g.Symbol;
            foreach (var s in extraFields) yield return s;
            if (!Options.Snippet) foreach (var s in undefinedVars) yield return s;
        }

        /// <summary>
        /// Names a snippet uses without defining them (docs fragments: "if condition:", "print(tuple[1])"). A full
        /// print declares them (fields / empty methods marked "defined elsewhere") so the fragment compiles; snippets don't.
        /// </summary>
        readonly List<IrSymbol> undefinedVars = new List<IrSymbol>();
        readonly List<IrSymbol> undefinedFns = new List<IrSymbol>();

        static bool IsUndefined(IrSymbol s) => s != null && Equals(s.Tag, PyLowering.UndefinedTag);

        void FindUndefined()
        {
            foreach (var n in IrWalk.Descendants(mod))
            {
                IrSymbol s = null;
                switch (n)
                {
                    case IrName nm: s = nm.Symbol; break;
                    case IrCall c when c.Kind == IrCallKind.Function: s = c.Function; break;
                    case IrFuncRef fr: s = fr.Function; break;
                }
                if (!IsUndefined(s)) continue;
                if (s.Kind == IrSymbolKind.Function) { if (s.Function == null && !undefinedFns.Contains(s)) undefinedFns.Add(s); }
                else if (s.Kind == IrSymbolKind.Global && !undefinedVars.Contains(s)) undefinedVars.Add(s);
            }
        }

        void UndefinedFunctions()
        {
            foreach (var s in undefinedFns)
            {
                var calls = IrWalk.Descendants(mod).OfType<IrCall>().Where(c => c.Kind == IrCallKind.Function && c.Function == s).ToList();
                var ps = new List<string>();
                int arity = calls.Count == 0 ? 0 : calls.Max(c => c.Args.Count);
                for (int i = 0; i < arity; i++)
                {
                    IrType t = IrType.Unknown;
                    foreach (var c in calls) if (i < c.Args.Count) t = IrType.Join(t, TypeOf(c.Args[i]));
                    ps.Add(TypeText(t.IsKnown && t.Kind != IrTypeKind.Null ? t : IrType.Any) + " " + (char)('a' + Math.Min(i, 25)));
                }
                var rt = types.Ret(s);
                if ((!rt.IsKnown || rt.Kind == IrTypeKind.Void) && IrWalk.Descendants(mod).Any(x => !(x is IrExprStmt) && IrWalk.Children(x).Any(ch => calls.Contains(ch)))) rt = IrType.Any;
                Line();
                Line("// defined elsewhere");
                Line(TypeText(rt.IsKnown && rt.Kind != IrTypeKind.Null ? rt : IrType.Void) + " " + Name(s) + "(" + string.Join(", ", ps) + ") {");
                if (rt.IsKnown && rt.Kind != IrTypeKind.Void && rt.Kind != IrTypeKind.Null) { Indent(); Line("return " + DefaultValue(rt) + ";"); Dedent(); }
                Line("}");
            }
        }

        // ================================================================== module analysis

        /// <summary>Statements of the generated main() (script modules: top-level statements and localized globals).</summary>
        IrBlock mainBlock;
        IrFunction entry;
        /// <summary>Globals used only by top-level statements: printed as locals of main().</summary>
        readonly HashSet<IrSymbol> localized = new HashSet<IrSymbol>();
        /// <summary>Module-level variables assigned by top-level statements that have no IrGlobal: printed as fields.</summary>
        readonly List<IrSymbol> extraFields = new List<IrSymbol>();
        readonly Dictionary<IrSymbol, IrExpr> extraFieldInit = new Dictionary<IrSymbol, IrExpr>();
        readonly HashSet<IrStmt> skipped = new HashSet<IrStmt>();
        // declaration plan for locals the IR declares implicitly (Python style: first assignment declares)
        readonly Dictionary<IrStmt, List<IrSymbol>> declareBefore = new Dictionary<IrStmt, List<IrSymbol>>();
        readonly HashSet<IrAssign> declaringAssign = new HashSet<IrAssign>();
        readonly HashSet<IrSymbol> headerDeclared = new HashSet<IrSymbol>();
        readonly HashSet<IrSymbol> needsDecl = new HashSet<IrSymbol>();

        static IrSymbol NameSym(IrExpr e) => e is IrName n ? n.Symbol : null;

        void Analyse()
        {
            types = new JavaTypeGuess(mod);
            entry = mod.Entry;
            if (!fromJava) FindUndefined();
            if (mod.IsScript)
            {
                var stmts = new List<IrStmt>();
                var fnRefs = new HashSet<IrSymbol>();
                foreach (var f in mod.Functions)
                    foreach (var n in IrWalk.Descendants(f).OfType<IrName>()) if (n.Symbol != null) fnRefs.Add(n.Symbol);
                foreach (var f in mod.Functions)
                    foreach (var n in IrWalk.Descendants(f)) { if (n is IrRangeFor r && r.Var != null) fnRefs.Add(r.Var); if (n is IrForEach fe && fe.Var != null) fnRefs.Add(fe.Var); }
                foreach (var g in mod.Globals) if (g.Symbol != null && !fnRefs.Contains(g.Symbol)) localized.Add(g.Symbol);
                var declaredGlobals = new HashSet<IrSymbol>(mod.Globals.Select(g => g.Symbol));
                foreach (var item in mod.Items)
                {
                    if (item is IrGlobal g && g.Symbol != null && localized.Contains(g.Symbol))
                    {
                        var v = new IrVarDecl { Symbol = g.Symbol, Init = g.ImplicitInit ? null : g.Init, TypeExplicit = g.TypeExplicit, Span = g.Span, Leading = g.Leading, Trailing = g.Trailing };
                        stmts.Add(v);
                    }
                    else if (item is IrStmt s) stmts.Add(s);
                }
                mainBlock = new IrBlock(stmts);
                // module-level variables without a declaration: fields when functions use them, else main locals
                var seen = new HashSet<IrSymbol>();
                foreach (var s in stmts)
                    foreach (var sym in IrWalk.AssignedSymbols(s))
                    {
                        if (sym == null || sym.Kind != IrSymbolKind.Global || declaredGlobals.Contains(sym) || !seen.Add(sym)) continue;
                        if (fnRefs.Contains(sym)) extraFields.Add(sym);
                        else localized.Add(sym);
                    }
                foreach (var f in extraFields)
                {
                    // hoist a constant first assignment into the field initialiser
                    var first = stmts.FirstOrDefault(s => Mentions(s, f));
                    if (first is IrAssign a && a.Op == null && NameSym(a.Target) == f && IsConstantInit(a.Value))
                    {
                        extraFieldInit[f] = a.Value;
                        skipped.Add(a);
                        if (a.Leading != null) leadingOfSkipped[a] = a.Leading;
                    }
                }
            }
            if (fromJava && mainBlock == null)
            {
                // typed Java IR declares every local; only loop variables are declared by their loop header
                foreach (var n in IrWalk.Descendants(mod))
                {
                    if (n is IrForEach fe) { if (fe.Var != null) headerDeclared.Add(fe.Var); if (fe.ValueVar != null) headerDeclared.Add(fe.ValueVar); }
                    else if (n is IrRangeFor rf && rf.Var != null) headerDeclared.Add(rf.Var);
                }
                return;
            }
            MergeBareDecls();
            foreach (var f in mod.Functions) PlanDecls(f.Body, f.Params.Select(p => p.Symbol));
            if (mainBlock != null)
            {
                PlanDecls(mainBlock, Enumerable.Empty<IrSymbol>());
                PromoteCapturedLocals();
            }
        }

        readonly Dictionary<IrStmt, List<IrComment>> leadingOfSkipped = new Dictionary<IrStmt, List<IrComment>>();

        static bool Mentions(IrNode n, IrSymbol s)
        {
            foreach (var d in IrWalk.Descendants(n))
            {
                if (d is IrName nm && nm.Symbol == s) return true;
                if (d is IrRangeFor r && r.Var == s) return true;
                if (d is IrForEach fe && (fe.Var == s || fe.ValueVar == s)) return true;
            }
            return false;
        }

        static bool IsConstantInit(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral _: case IrConst _: return true;
                case IrUnary u: return IsConstantInit(u.Operand);
                case IrBinary b: return IsConstantInit(b.Left) && IsConstantInit(b.Right);
                case IrListLit l: return l.Items.All(IsConstantInit);
                case IrSetLit s: return s.Items.All(IsConstantInit);
                case IrMapLit m: return m.Keys.All(IsConstantInit) && m.Values.All(IsConstantInit);
            }
            return false;
        }

        /// <summary>
        /// "T x;" followed by the first assignment of x in the same block (Python frontends declare unpacking targets
        /// up front): the assignment becomes the declaration.
        /// </summary>
        void MergeBareDecls()
        {
            var blocks = mod.Functions.Select(f => f.Body).Concat(mainBlock != null ? new[] { mainBlock } : new IrBlock[0])
                .SelectMany(b => IrWalk.Descendants(b).OfType<IrBlock>()).Distinct().ToList();
            foreach (var b in blocks)
                for (int i = 0; i < b.Stmts.Count; i++)
                {
                    if (!(b.Stmts[i] is IrVarDecl v) || v.Init != null || v.Symbol == null || v.Leading != null || v.Trailing != null) continue;
                    var first = b.Stmts.Skip(i + 1).FirstOrDefault(s => Mentions(s, v.Symbol));
                    if (!(first is IrAssign a) || a.Op != null || Mentions(a.Value, v.Symbol)) continue;
                    if (NameSym(a.Target) == v.Symbol) { declaringAssign.Add(a); skipped.Add(v); }
                    else if (a.Target is IrTupleLit tl && tl.Items.Concat(tl.Items.OfType<IrTupleLit>().SelectMany(x => x.Items)).Any(x => NameSym(x) == v.Symbol))
                    { unpackDeclared.Add(v.Symbol); skipped.Add(v); }
                }
        }

        /// <summary>Main locals captured by a lambda and assigned more than once cannot be Java locals: make them fields.</summary>
        void PromoteCapturedLocals()
        {
            var inLambda = new HashSet<IrSymbol>();
            foreach (var l in IrWalk.Descendants(mainBlock).OfType<IrLambda>())
                foreach (var s in IrWalk.Captures(l)) inLambda.Add(s);
            foreach (var s in inLambda.ToList())
            {
                if (!(localized.Contains(s) || (s.Kind == IrSymbolKind.Local && needsDecl.Contains(s)))) continue;
                int writes = IrWalk.AssignedSymbols(mainBlock).Count(x => x == s);
                if (writes <= 1) continue;
                if (IrWalk.Descendants(mainBlock).OfType<IrVarDecl>().Any(v => v.Symbol == s && v.Init != null && !localized.Contains(s))) continue;
                localized.Remove(s);
                needsDecl.Remove(s);
                headerDeclared.Remove(s);
                foreach (var kv in declareBefore) kv.Value.Remove(s);
                foreach (var a in declaringAssign.Where(a => NameSym(a.Target) == s).ToList()) declaringAssign.Remove(a);
                if (!extraFields.Contains(s)) extraFields.Add(s);
                promotedDecls.Add(s);
            }
        }

        /// <summary>Main locals turned into fields: their pseudo declarations become plain assignments.</summary>
        readonly HashSet<IrSymbol> promotedDecls = new HashSet<IrSymbol>();

        // ------------------------------------------------------------------ implicit declarations

        sealed class Ref { public List<(IrBlock B, int I)> Path; public bool Binding; }

        /// <summary>
        /// Finds where to declare the locals that have no IrVarDecl: at their first plain assignment when every use
        /// follows it in the same block, in the loop header when every use is inside loops binding them, otherwise
        /// "T x = default;" before the first statement of the innermost block containing every use.
        /// </summary>
        void PlanDecls(IrBlock body, IEnumerable<IrSymbol> parms)
        {
            var declared = new HashSet<IrSymbol>(parms.Where(p => p != null));
            foreach (var n in IrWalk.Descendants(body))
            {
                if (n is IrVarDecl v && v.Symbol != null) declared.Add(v.Symbol);
                if (n is IrParam p && p.Symbol != null) declared.Add(p.Symbol);
            }
            var refs = new Dictionary<IrSymbol, List<Ref>>();
            var path = new List<(IrBlock, int)>();
            bool Candidate(IrSymbol s) => s != null && !declared.Contains(s) &&
                (s.Kind == IrSymbolKind.Local || (s.Kind == IrSymbolKind.Global && localized.Contains(s)));
            void Add(IrSymbol s, bool binding)
            {
                if (!Candidate(s)) return;
                if (!refs.TryGetValue(s, out var l)) refs[s] = l = new List<Ref>();
                l.Add(new Ref { Path = path.ToList(), Binding = binding });
            }
            void Expr(IrNode e)
            {
                if (e == null) return;
                foreach (var d in IrWalk.Descendants(e, false))
                {
                    if (d is IrName nm) Add(nm.Symbol, false);
                    if (d is IrLambda lam && lam != e) { if (lam.Body != null) Block(lam.Body); else Expr(lam.BodyExpr); }
                }
                if (e is IrLambda self) { if (self.Body != null) Block(self.Body); else Expr(self.BodyExpr); }
            }
            void Block(IrBlock b)
            {
                for (int i = 0; i < b.Stmts.Count; i++)
                {
                    path.Add((b, i));
                    Stmt(b.Stmts[i]);
                    path.RemoveAt(path.Count - 1);
                }
            }
            void Stmt(IrStmt s)
            {
                switch (s)
                {
                    case null: return;
                    case IrBlock b: Block(b); return;
                    case IrIf f:
                        Expr(f.Cond); Block(f.Then);
                        if (f.Else is IrIf ei) Stmt(ei); else if (f.Else is IrBlock eb) Block(eb);
                        return;
                    case IrWhile w: Expr(w.Cond); Block(w.Body); return;
                    case IrDoWhile dw: Block(dw.Body); Expr(dw.Cond); return;
                    case IrLoop lp: Block(lp.Body); return;
                    case IrClassicFor cf:
                        foreach (var x in cf.Init) Stmt(x);
                        Expr(cf.Cond);
                        foreach (var x in cf.Update) Stmt(x);
                        Block(cf.Body);
                        return;
                    case IrRangeFor rf: Add(rf.Var, true); Expr(rf.Start); Expr(rf.End); Expr(rf.Step); Block(rf.Body); return;
                    case IrForEach fe: Add(fe.Var, true); Add(fe.ValueVar, true); Expr(fe.Iterable); Block(fe.Body); return;
                    case IrSwitch sw: Expr(sw.Selector); foreach (var c in sw.Cases) { foreach (var l in c.Labels) Expr(l); Block(c.Body); } return;
                    case IrAssign a:
                        if (a.Target is IrTupleLit t) foreach (var it in t.Items) Expr(it); else Expr(a.Target);
                        Expr(a.Value);
                        return;
                }
                foreach (var c in IrWalk.Children(s)) Expr(c);
            }
            Block(body);

            foreach (var kv in refs)
            {
                var s = kv.Key;
                var rs = kv.Value;
                needsDecl.Add(s);
                // 1. every use inside a loop that binds the symbol: each loop declares its own variable
                bool allInLoops = rs.All(r => r.Path.Any(p => Binds(p.B.Stmts[p.I], s)));
                if (allInLoops) { headerDeclared.Add(s); continue; }
                // 2. innermost block holding every use
                int depth = 0;
                while (true)
                {
                    if (rs.Any(r => r.Path.Count <= depth)) break;
                    var blk = rs[0].Path[depth].B;
                    if (rs.Any(r => r.Path[depth].B != blk)) break;
                    if (rs.Any(r => r.Path[depth].I != rs[0].Path[depth].I)) { depth++; break; }
                    depth++;
                }
                int level = Math.Max(0, depth - 1);
                var block = rs[0].Path.Count > level ? rs[0].Path[level].B : body;
                int first = rs.Where(r => r.Path.Count > level).Min(r => r.Path[level].I);
                var st = block.Stmts[first];
                bool allAfter = rs.All(r => r.Path.Count > level && r.Path[level].B == block);
                if (st is IrAssign a && a.Op == null && NameSym(a.Target) == s && !Mentions(a.Value, s) && allAfter
                    && rs.Count(r => r.Path.Count == level + 1 && r.Path[level].I == first) <= 1)
                {
                    declaringAssign.Add(a);
                    continue;
                }
                if (st is IrAssign ua && ua.Op == null && ua.Target is IrTupleLit utl && allAfter && !Mentions(ua.Value, s)
                    && utl.Items.Concat(utl.Items.OfType<IrTupleLit>().SelectMany(x => x.Items)).Any(x => NameSym(x) == s))
                {
                    unpackDeclared.Add(s);
                    continue;
                }
                if (!declareBefore.TryGetValue(st, out var l)) declareBefore[st] = l = new List<IrSymbol>();
                l.Add(s);
            }
        }

        /// <summary>Block comments keep a space before the closing marker when the source had one.</summary>
        protected override void Comment(IrComment c)
        {
            if (c != null && c.IsBlock && (c.Text ?? "").IndexOf('\n') >= 0 && (c.Text ?? "").Length > 0 && char.IsWhiteSpace(c.Text[c.Text.Length - 1]))
            {
                var lines = c.Text.Replace("\r\n", "\n").Split('\n');
                Line("/*" + lines[0].TrimEnd());
                for (int i = 1; i < lines.Length; i++)
                {
                    var l = lines[i].TrimEnd();
                    Out.Append(IndentText).Append(l).Append(i == lines.Length - 1 ? (l.Length > 0 ? " */" : "*/") : "").Append(Options.Newline);
                }
                return;
            }
            base.Comment(c);
        }

        /// <summary>Locals declared by their first (tuple-unpacking) assignment.</summary>
        readonly HashSet<IrSymbol> unpackDeclared = new HashSet<IrSymbol>();

        static bool Binds(IrStmt s, IrSymbol sym) =>
            (s is IrRangeFor r && r.Var == sym) || (s is IrForEach fe && (fe.Var == sym || fe.ValueVar == sym));

        // ================================================================== module

        void Module()
        {
            Analyse();
            AssignModuleNames();
            Comments(mod.Leading);
            bool hasDecls = mod.Functions.Any(f => !f.IsEntry) || mod.Globals.Any(g => g.Symbol != null && !localized.Contains(g.Symbol)) || extraFields.Count > 0;
            if (Options.Snippet && !hasDecls)
            {
                PushScope();
                // a lone expression (docs prose: `can_harvest()`, `True`) prints as the bare expression
                if (mainBlock != null && mainBlock.Stmts.Count == 1 && mainBlock.Stmts[0] is IrExprStmt lone && !IsStatementExpr(lone.Expr) && lone.Leading == null && lone.Trailing == null)
                {
                    Line(X(lone.Expr).T);
                    PopScope();
                    return;
                }
                if (mainBlock != null) Stmts(mainBlock.Stmts);
                else if (entry != null)
                {
                    foreach (var p in entry.Params) Declare(p.Symbol);
                    Stmts(entry.Body.Stmts);
                    Comments(entry.Body.TrailingComments);
                }
                PopScope();
                Comments(mod.TrailingComments);
                return;
            }
            int last = 0;   // 0 nothing yet, 1 field, 2 method
            foreach (var s in undefinedVars)
            {
                if (Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0 || s.Name == "Directions") Problem(s.Name + " cannot be used as a value in Java");
                Line("// defined elsewhere");
                var ut = SymType(s);
                Line(TypeText(ut.IsKnown && ut.Kind != IrTypeKind.Null ? ut : IrType.Any) + " " + Name(s) + ";");
                last = 1;
            }
            foreach (var s in extraFields)
            {
                var init = extraFieldInit.TryGetValue(s, out var ie) ? ie : null;
                if (init != null && skipped.FirstOrDefault(x => x is IrAssign a && NameSym(a.Target) == s) is IrStmt st && leadingOfSkipped.TryGetValue(st, out var lc))
                    Comments(lc);
                Line(TypeText(SymType(s), types.IsBoxed(s)) + " " + Name(s) + (init != null ? " = " + X(init, SymType(s)).T : "") + ";");
                last = 1;
            }
            bool mainDone = false;
            foreach (var item in mod.Items)
            {
                switch (item)
                {
                    case IrGlobal g:
                        if (g.Symbol == null || localized.Contains(g.Symbol)) break;
                        if (last == 2) Line();
                        Field(g);
                        last = 1;
                        break;
                    case IrFunction f:
                        if (last != 0) Line();
                        Method(f);
                        last = 2;
                        break;
                    case IrStmt st:
                        if (mainDone || mainBlock == null || skipped.Contains(st)) break;
                        mainDone = true;
                        if (last != 0) Line();
                        Line("void main() {");
                        Indent();
                        PushScope();
                        Stmts(mainBlock.Stmts);
                        PopScope();
                        Dedent();
                        Line("}");
                        last = 2;
                        break;
                }
            }
            if (!Options.Snippet) UndefinedFunctions();
            Comments(mod.TrailingComments);
        }

        void Field(IrGlobal g)
        {
            LeadingComments(g);
            var s = g.Symbol;
            var t = SymType(s);
            var mods = (g.IsPrivate ? "private " : "") + (s.IsConst ? "final " : "");
            var init = g.Init != null && !(g.ImplicitInit && !s.IsConst) ? " = " + X(g.Init, t).T : "";
            Line(mods + TypeText(t, types.IsBoxed(s)) + " " + Name(s) + init + ";", g);
        }

        IrType RetType(IrFunction f)
        {
            if (f.ReturnType != null && f.ReturnType.IsKnown) return f.ReturnType;
            return f.Symbol != null ? types.Ret(f.Symbol) : IrType.Void;
        }

        void Method(IrFunction f)
        {
            LeadingComments(f);
            PushScope();
            var ps = new List<string>();
            foreach (var p in f.Params)
            {
                var pt = SymType(p.Symbol);
                ps.Add(TypeText(pt, types.IsBoxed(p.Symbol)) + " " + Declare(p.Symbol));
            }
            var rt = RetType(f);
            var outerReturn = CurrentReturn;
            CurrentReturn = rt;
            string header;
            if (f.IsEntry) header = "void main(" + string.Join(", ", ps) + ")";
            else header = (f.IsPrivate ? "private " : "") + TypeText(rt) + " " + Name(f.Symbol) + "(" + string.Join(", ", ps) + ")";
            Line(header + " {", f);
            Indent();
            Stmts(f.Body.Stmts);
            if (!fromJava && !f.IsEntry && rt.Kind != IrTypeKind.Void && CanComplete(f.Body)) Line("return " + DefaultValue(rt) + ";");
            Comments(f.Body.TrailingComments);
            Dedent();
            PopScope();
            Line("}");
            CurrentReturn = outerReturn;
        }

        // ================================================================== statements

        void Stmts(IEnumerable<IrStmt> stmts)
        {
            bool dead = false;
            foreach (var s in stmts)
            {
                if (dead && !fromJava)
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

        void Block(IrBlock b)
        {
            Indent();
            PushScope();
            if (b != null) { Stmts(b.Stmts); Comments(b.TrailingComments); }
            PopScope();
            Dedent();
        }

        void Stmt(IrStmt s)
        {
            if (s == null) return;
            if (skipped.Contains(s)) return;
            if (declareBefore.TryGetValue(s, out var pre))
                foreach (var sym in pre)
                {
                    var t = SymType(sym);
                    Line(TypeText(t, types.IsBoxed(sym)) + " " + Declare(sym) + " = " + DefaultValue(t, types.IsBoxed(sym)) + ";");
                }
            LeadingComments(s);
            switch (s)
            {
                case IrBlock b:
                    Line("{");
                    Block(b);
                    Line("}", b);
                    return;
                case IrVarDecl v: VarDecl(v); return;
                case IrAssign a: Assign(a); return;
                case IrExprStmt es: ExprStmt(es); return;
                case IrIf f: If(f); return;
                case IrWhile w:
                    Line("while (" + Cond(w.Cond).T + ") {", w);
                    Block(w.Body);
                    Line("}");
                    return;
                case IrLoop lp:
                    Line("while (true) {", lp);
                    Block(lp.Body);
                    Line("}");
                    return;
                case IrDoWhile dw:
                    Line("do {", dw);
                    Block(dw.Body);
                    Line("} while (" + Cond(dw.Cond).T + ");");
                    return;
                case IrClassicFor cf: ClassicFor(cf); return;
                case IrRangeFor rf: RangeFor(rf); return;
                case IrForEach fe: ForEach(fe); return;
                case IrSwitch sw: Switch(sw); return;
                case IrBreak _: Line("break;", s); return;
                case IrContinue _: Line("continue;", s); return;
                case IrReturn r:
                    if (r.Value == null) { Line("return;", r); return; }
                    if (r.Value is IrTupleLit) Problem("returning several values (a tuple) is not supported in Java");
                    Line("return " + X(r.Value, CurrentReturn).T + ";", r);
                    return;
                case IrImport imp:
                    // Java files use other files by name (Utils.f()); there is no import statement for them
                    if (Options.Snippet) Problem("import " + imp.Module + " has no Java equivalent (other files are used by name)");
                    Line("// " + imp.Module + " is used as " + imp.Module + ".name()", imp);
                    return;
            }
            Problem("unsupported statement " + s.GetType().Name);
            Line("// unsupported statement: " + s.GetType().Name);
        }

        /// <summary>Return type of the method being printed (literals in return statements take it).</summary>
        IrType CurrentReturn;

        void VarDecl(IrVarDecl v)
        {
            var s = v.Symbol;
            if (s != null && promotedDecls.Contains(s))
            {
                if (v.Init != null) Line(Name(s) + " = " + X(v.Init, SymType(s)).T + ";", v);
                return;
            }
            var t = SymType(s);
            string type = DeclTypeText(s, v.Init, v.TypeExplicit);
            var fin = s != null && s.IsConst ? "final " : "";
            if (v.Init == null)
            {
                if (!fromJava) { Line(type + " " + Declare(s) + " = " + DefaultValue(t, s != null && types.IsBoxed(s)) + ";", v); return; }
                Line(fin + type + " " + Declare(s) + ";", v);
                return;
            }
            var init = X(v.Init, t).T;   // before Declare: the initialiser cannot see the new name
            Line(fin + type + " " + Declare(s) + " = " + init + ";", v);
        }

        /// <summary>Type of a declaration: var when the source wrote var (or the type is unknown), else the Java type.</summary>
        string DeclTypeText(IrSymbol s, IrExpr init, bool explicitType)
        {
            var t = SymType(s);
            bool varOk = init != null && !(init is IrLiteral l && l.Kind == IrLitKind.Null) && !(init is IrLambda) && !(init is IrFuncRef)
                         && !(init is IrConditional) && !IsEmptyCollection(init) && !(init is IrName n && n.Symbol != null && (n.Symbol.Kind == IrSymbolKind.Function || n.Symbol.Kind == IrSymbolKind.ExternalFunction));
            if (varOk && (!explicitType && fromJava || !t.IsKnown || t.Kind == IrTypeKind.Unknown)) return "var";
            return TypeText(t, s != null && types.IsBoxed(s));
        }

        static bool IsEmptyCollection(IrExpr e) =>
            (e is IrListLit l && l.Items.Count == 0) || (e is IrSetLit s && s.Items.Count == 0) || (e is IrMapLit m && m.Keys.Count == 0);

        void ExprStmt(IrExprStmt es)
        {
            var e = es.Expr;
            if (e is IrOpCall oc && oc.Op == IrOp.ListPop && oc.Args.Count == 1 && !IsSimple(oc.Args[0]))
            {
                Problem("pop() on a computed list");
            }
            var x = X(e);
            if (!IsStatementExpr(e))
            {
                Problem("an expression statement without effect has no Java equivalent");
                Line("// " + x.T + ";", es);
                return;
            }
            Line(x.T + ";", es);
        }

        bool IsStatementExpr(IrExpr e)
        {
            switch (e)
            {
                case IrCall _: return true;
                case IrOpCall o:
                    // everything prints as a method call except the array forms a.length / a.length == 0
                    return !((o.Op == IrOp.Len || o.Op == IrOp.IsEmpty) && o.Args.Count > 0 && TypeOf(o.Args[0]).Kind == IrTypeKind.Array);
            }
            return false;
        }

        void If(IrIf f)
        {
            Line("if (" + Cond(f.Cond).T + ") {", f);
            Block(f.Then);
            var tail = f.Else;
            while (tail is IrIf ei)
            {
                LeadingCommentsInside(ei);
                Line("} else if (" + Cond(ei.Cond).T + ") {", ei);
                Block(ei.Then);
                tail = ei.Else;
            }
            if (tail != null)
            {
                Line("} else {", tail);
                if (tail is IrBlock eb) Block(eb);
                else { Indent(); Stmt(tail); Dedent(); }
            }
            Line("}");
        }

        /// <summary>Comments before an else-if: printed inside the previous branch (Java has nowhere else to put them).</summary>
        void LeadingCommentsInside(IrNode n)
        {
            if (!Options.Comments || n?.Leading == null || n.Leading.Count == 0) return;
            Indent();
            Comments(n.Leading);
            Dedent();
        }

        string SimpleStmtText(IrStmt s)
        {
            switch (s)
            {
                case IrVarDecl v:
                    {
                        var t = SymType(v.Symbol);
                        var init = v.Init != null ? " = " + X(v.Init, t).T : "";
                        return DeclTypeText(v.Symbol, v.Init, v.TypeExplicit) + " " + Declare(v.Symbol) + init;
                    }
                case IrAssign a: return AssignText(a) ?? "/* unsupported assignment */";
                case IrExprStmt e: return X(e.Expr).T;
            }
            Problem("unsupported statement in a for header");
            return "/* unsupported */";
        }

        void ClassicFor(IrClassicFor f)
        {
            PushScope();
            string init;
            var decls = f.Init.OfType<IrVarDecl>().ToList();
            if (decls.Count == f.Init.Count && decls.Count > 1 && decls.All(d => DeclTypeText(d.Symbol, d.Init, d.TypeExplicit) == DeclTypeText(decls[0].Symbol, decls[0].Init, decls[0].TypeExplicit)))
            {
                var type = DeclTypeText(decls[0].Symbol, decls[0].Init, decls[0].TypeExplicit);
                init = type + " " + string.Join(", ", decls.Select(d => { var iv = d.Init != null ? " = " + X(d.Init, SymType(d.Symbol)).T : ""; return Declare(d.Symbol) + iv; }));
            }
            else init = string.Join(", ", f.Init.Select(SimpleStmtText));
            var cond = f.Cond != null ? Cond(f.Cond).T : "";
            var upd = string.Join(", ", f.Update.Select(SimpleStmtText));
            Line("for (" + init + "; " + cond + "; " + upd + ") {", f);
            Block(f.Body);
            PopScope();
            Line("}");
        }

        void RangeFor(IrRangeFor rf)
        {
            PushScope();
            int step = 1;
            bool literalStep = true;
            if (rf.Step != null)
            {
                if (rf.Step is IrLiteral sl && sl.Kind == IrLitKind.Int) step = (int)sl.Int;
                else if (rf.Step is IrUnary su && su.Op == IrUnOp.Neg && su.Operand is IrLiteral sl2 && sl2.Kind == IrLitKind.Int) step = -(int)sl2.Int;
                else literalStep = false;
            }
            var start = rf.Start != null ? X(rf.Start, IrType.Int).T : "0";
            var end = X(rf.End, IrType.Int);
            // the end of a Python range() is evaluated once: keep it in a variable when it calls something
            if (!fromJava && HasCall(rf.End))
            {
                var tmp = new IrSymbol("end", IrSymbolKind.Local, IrType.Int);
                var tn = Declare(tmp);
                // declared outside the loop scope so the loop can see it
                Line("int " + tn + " = " + end.T + ";");
                end = new Ex(tn, PrecAtom);
            }
            string v;
            bool declareHere = rf.Var == null || headerDeclared.Contains(rf.Var);
            if (declareHere) v = Declare(rf.Var);
            else v = Name(rf.Var);
            var decl = declareHere ? "int " + v : v;
            string cond, upd;
            if (!literalStep)
            {
                Problem("a range with a computed step is printed for positive steps only");
                cond = v + " < " + end.T;
                upd = v + " += " + X(rf.Step).T;
            }
            else if (step > 0)
            {
                cond = v + (rf.Inclusive ? " <= " : " < ") + Wrap(end.T, end.P, PrecRel + 1);
                upd = step == 1 ? v + "++" : v + " += " + step.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                cond = v + (rf.Inclusive ? " >= " : " > ") + Wrap(end.T, end.P, PrecRel + 1);
                upd = step == -1 ? v + "--" : v + " -= " + (-step).ToString(CultureInfo.InvariantCulture);
            }
            Line("for (" + decl + " = " + start + "; " + cond + "; " + upd + ") {", rf);
            Block(rf.Body);
            PopScope();
            Line("}");
        }

        static bool HasCall(IrExpr e) => IrWalk.Descendants(e).Any(n => (n is IrCall c && !(c.Kind == IrCallKind.Builtin && (GameApi.Find(c.Builtin)?.LoopInvariant ?? false))));

        void ForEach(IrForEach fe)
        {
            PushScope();
            var it = fe.Iterable;
            var itT = TypeOf(it);
            string src;
            IrType elem;
            if (it is IrOpCall mk && mk.Op == IrOp.MapKeys)
            {
                src = X(it).T;
                elem = TypeOf(mk.Args[0]).Key ?? IrType.Unknown;
            }
            else if (itT.Kind == IrTypeKind.Map)
            {
                src = Postfix(it) + ".keySet()";
                elem = itT.Key ?? IrType.Unknown;
            }
            else
            {
                if (itT.Kind == IrTypeKind.String) Problem("iterating over the characters of a String is not supported in JavaFarmer");
                src = X(it).T;
                elem = itT.Elem ?? IrType.Unknown;
            }
            if (fe.Var != null && SymType(fe.Var).IsKnown) elem = SymType(fe.Var);
            bool own = fe.Var == null || headerDeclared.Contains(fe.Var);
            string type = elem.IsKnown ? TypeText(elem) : "var";
            string v = own ? Declare(fe.Var) : Declare(new IrSymbol(fe.Var.Name + "Item", IrSymbolKind.Local, elem));
            Line("for (" + type + " " + v + " : " + src + ") {", fe);
            Indent();
            PushScope();
            if (!own) Line(Name(fe.Var) + " = " + v + ";");
            if (fe.ValueVar != null)
            {
                var mapExpr = it is IrOpCall mk2 && mk2.Op == IrOp.MapKeys ? mk2.Args[0] : it;
                var vt = SymType(fe.ValueVar);
                if (!vt.IsKnown) vt = TypeOf(mapExpr).Elem ?? IrType.Unknown;
                var mapText = Postfix(mapExpr);
                if (!IsSimple(mapExpr)) Problem("for-each with a value variable over a computed map");
                Line((vt.IsKnown ? TypeText(vt) : "var") + " " + Declare(fe.ValueVar) + " = " + mapText + ".get(" + v + ");");
            }
            Stmts(fe.Body.Stmts);
            Comments(fe.Body.TrailingComments);
            PopScope();
            Dedent();
            PopScope();
            Line("}");
        }

        // ------------------------------------------------------------------ switch

        void Switch(IrSwitch sw)
        {
            // a break inside an arrow case would leave the switch, not the loop: print an if chain instead
            bool loopBreak = sw.Cases.Any(c => BreaksOut(c.Body));
            if (loopBreak) { SwitchAsIf(sw); return; }
            Line("switch (" + X(sw.Selector).T + ") {", sw);
            Indent();
            var selT = TypeOf(sw.Selector);
            foreach (var c in sw.Cases)
            {
                LeadingComments(c);
                var label = c.IsDefault ? "default" : "case " + string.Join(", ", c.Labels.Select(l => X(l, selT).T));
                var caseBlock = c.Body;
                if (caseBlock != null && caseBlock.Stmts.Count == 1 && caseBlock.Stmts[0] is IrBlock only && only.Leading == null && only.Trailing == null
                    && (caseBlock.TrailingComments == null || caseBlock.TrailingComments.Count == 0)) caseBlock = only;
                var body = caseBlock?.Stmts ?? new List<IrStmt>();
                if (body.Count == 1 && (body[0] is IrExprStmt || (body[0] is IrAssign a1 && !(a1.Target is IrTupleLit) && !declaringAssign.Contains(a1)))
                    && body[0].Leading == null && !declareBefore.ContainsKey(body[0]) && (caseBlock.TrailingComments == null || caseBlock.TrailingComments.Count == 0))
                {
                    var one = body[0] is IrExprStmt es ? X(es.Expr).T : AssignText((IrAssign)body[0]);
                    if (one != null && one.IndexOf('\n') < 0) { Line(label + " -> " + one + ";", body[0]); continue; }
                }
                if (body.Count == 0 && (caseBlock?.TrailingComments == null || caseBlock.TrailingComments.Count == 0)) { Line(label + " -> {", c); Line("}"); continue; }
                Line(label + " -> {", c);
                Block(caseBlock);
                Line("}");
            }
            Dedent();
            Line("}");
        }

        /// <summary>True if the block contains a break that leaves an enclosing loop (not one of its own loops).</summary>
        static bool BreaksOut(IrNode n)
        {
            switch (n)
            {
                case IrBreak _: return true;
                case IrWhile _: case IrDoWhile _: case IrLoop _: case IrClassicFor _: case IrRangeFor _: case IrForEach _: case IrLambda _: return false;
            }
            foreach (var c in IrWalk.Children(n)) if (c is IrStmt || c is IrCase) if (BreaksOut(c)) return true;
            return false;
        }

        void SwitchAsIf(IrSwitch sw)
        {
            var selT = TypeOf(sw.Selector);
            string sel;
            if (IsSimple(sw.Selector)) sel = X(sw.Selector).T;
            else
            {
                var tmp = new IrSymbol("selector", IrSymbolKind.Local, selT);
                var init = X(sw.Selector).T;
                sel = Declare(tmp);
                Line((selT.IsKnown ? TypeText(selT) : "var") + " " + sel + " = " + init + ";");
            }
            var normal = sw.Cases.Where(c => !c.IsDefault).ToList();
            var dflt = sw.Cases.FirstOrDefault(c => c.IsDefault);
            bool first = true;
            foreach (var c in normal)
            {
                var conds = c.Labels.Select(l => EqText(sel, PrecAtom, selT, X(l, selT), TypeOf(l), false)).ToList();
                var cond = string.Join(" || ", conds.Select(x => conds.Count > 1 ? Wrap(x.T, x.P, PrecOr + 1) : x.T));
                LeadingComments(c);
                Line((first ? "if (" : "} else if (") + cond + ") {", sw);
                Block(c.Body);
                first = false;
            }
            if (dflt != null)
            {
                if (first) { Line("{"); Block(dflt.Body); Line("}"); return; }
                Line("} else {");
                Block(dflt.Body);
            }
            if (!first) Line("}");
        }

        // ------------------------------------------------------------------ assignments

        void Assign(IrAssign a)
        {
            if (a.Target is IrTupleLit tl) { Unpack(tl, a); return; }
            if (declaringAssign.Contains(a))
            {
                var s = NameSym(a.Target);
                var t = SymType(s);
                var val = X(a.Value, t).T;
                Line(DeclTypeText(s, a.Value, true) + " " + Declare(s) + " = " + val + ";", a);
                return;
            }
            var text = AssignText(a);
            if (text == null) { Line("// unsupported assignment", a); return; }
            Line(text + ";", a);
        }

        /// <summary>One assignment as a statement expression (no ';'), or null when it is not expressible.</summary>
        string AssignText(IrAssign a)
        {
            var target = a.Target;
            var tt = TypeOf(target);
            if (target is IrIndex ix)
            {
                var recvT = TypeOf(ix.Target);
                if (recvT.Kind == IrTypeKind.List || recvT.Kind == IrTypeKind.Map || recvT.Kind == IrTypeKind.Unknown && !fromJava)
                {
                    string method = recvT.Kind == IrTypeKind.Map ? "put" : "set";
                    var elemT = recvT.Elem;
                    var idx = recvT.Kind == IrTypeKind.Map ? X(ix.Index, recvT.Key).T : IndexText(ix);
                    string value;
                    if (a.Op == null) value = X(a.Value, elemT).T;
                    else
                    {
                        if (!IsSimple(ix.Target) || !IsSimple(ix.Index)) Problem("compound assignment to a computed list/map element");
                        var cur = new IrIndex { Target = ix.Target, Index = ix.Index, BaseOffset = ix.BaseOffset, Type = ix.Type };
                        value = BinText(a.Op.Value, cur, a.Value, a.Value.Type).T;
                    }
                    return Postfix(ix.Target) + "." + method + "(" + idx + ", " + value + ")";
                }
            }
            var tx = TargetText(target);
            if (a.Op == null) return tx + " = " + X(a.Value, tt).T;
            var op = a.Op.Value;
            bool lit1 = a.Value is IrLiteral l1 && l1.Kind == IrLitKind.Int && l1.Int == 1;
            if (a.IsIncDec && lit1 && (op == IrBinOp.Add || op == IrBinOp.Sub))
            {
                var pp = op == IrBinOp.Add ? "++" : "--";
                return a.Prefix ? pp + tx : tx + pp;
            }
            bool intTarget = tt.Kind == IrTypeKind.Int;
            switch (op)
            {
                case IrBinOp.Add: case IrBinOp.Concat: return tx + " += " + X(a.Value, op == IrBinOp.Concat ? null : tt).T;
                case IrBinOp.Sub: return tx + " -= " + X(a.Value, tt).T;
                case IrBinOp.Mul: return tx + " *= " + X(a.Value, tt).T;
                case IrBinOp.ModTrunc: return tx + " %= " + X(a.Value, tt).T;
                case IrBinOp.IntDivTrunc: return tx + " /= " + X(a.Value, tt).T;
                case IrBinOp.TrueDiv:
                    if (intTarget && !a.TruncateToInt && IsInt(a.Value)) return tx + " = " + BinText(op, target, a.Value, IrType.Float).T;
                    return tx + " /= " + X(a.Value, tt).T;
            }
            // floor division / modulo / power have no compound operator
            if (!(target is IrName) && !IsSimple(target)) Problem("compound assignment on a computed target");
            var rhs = BinText(op, target, a.Value, null);
            if (intTarget && op == IrBinOp.Pow) return tx + " = (int) " + Wrap(rhs.T, rhs.P, PrecUnary);
            return tx + " = " + rhs.T;
        }

        string TargetText(IrExpr t)
        {
            if (t is IrName n) return NameRef(n);
            return X(t).T;
        }

        /// <summary>a, b = ... : separate assignments (temporaries when the values read the targets).</summary>
        void Unpack(IrTupleLit targets, IrAssign a)
        {
            if (a.Op != null) { Problem("compound assignment to several variables"); Line("// unsupported compound tuple assignment", a); return; }
            if (a.Value is IrTupleLit vals && vals.Items.Count == targets.Items.Count)
            {
                var tsyms = targets.Items.Select(NameSym).ToList();
                bool reads = vals.Items.Any(v => tsyms.Any(s => s != null && Mentions(v, s)));
                if (!reads)
                {
                    for (int i = 0; i < targets.Items.Count; i++) AssignOne(targets.Items[i], vals.Items[i], a, i == 0);
                    return;
                }
                var temps = new List<string>();
                for (int i = 0; i < vals.Items.Count; i++)
                {
                    var t = TypeOf(targets.Items[i]);
                    var init = X(vals.Items[i], t).T;
                    var tmp = Declare(new IrSymbol((NameSym(targets.Items[i])?.Name ?? "value") + "New", IrSymbolKind.Local, t));
                    Line((t.IsKnown ? TypeText(t) : "var") + " " + tmp + " = " + init + ";", i == 0 ? a : null);
                    temps.Add(tmp);
                }
                for (int i = 0; i < targets.Items.Count; i++)
                    AssignOne(targets.Items[i], new IrName(new IrSymbol(temps[i], IrSymbolKind.Local)) { Type = TypeOf(vals.Items[i]) }, null, false, temps[i]);
                return;
            }
            var vt = TypeOf(a.Value);
            bool pos = vt.IsGame("Position") || IsPositionTuple(vt) || ((vt.Kind == IrTypeKind.Any || !vt.IsKnown) && targets.Items.Count == 2 && targets.Items.All(x => x is IrName));
            bool comp = vt.IsGame("Companion");
            if ((pos || comp) && targets.Items.Count == 2)
            {
                string holder;
                if (IsSimple(a.Value)) holder = X(a.Value).T;
                else
                {
                    var tn = pos ? "Position" : "Companion";
                    var init = X(a.Value).T;
                    if (TypeOf(a.Value).Kind == IrTypeKind.Any || TypeOf(a.Value).Kind == IrTypeKind.Unknown) init = "(" + tn + ") " + init;
                    holder = Declare(new IrSymbol(pos ? "pos" : "companion", IrSymbolKind.Local, IrType.Game(tn)));
                    Line(tn + " " + holder + " = " + init + ";", a);
                }
                if (pos)
                {
                    AssignOneText(targets.Items[0], holder + ".x()", IrType.Int);
                    AssignOneText(targets.Items[1], holder + ".y()", IrType.Int);
                }
                else
                {
                    AssignOneText(targets.Items[0], holder + ".entity()", IrType.Game("Entity"));
                    if (targets.Items[1] is IrTupleLit inner && inner.Items.Count == 2)
                    {
                        AssignOneText(inner.Items[0], holder + ".x()", IrType.Int);
                        AssignOneText(inner.Items[1], holder + ".y()", IrType.Int);
                    }
                    else AssignOneText(targets.Items[1], holder + ".position()", IrType.Game("Position"));
                }
                return;
            }
            Problem("unpacking a tuple value is not supported in Java");
            Line("// unsupported: " + string.Join(", ", targets.Items.Select(t => X(t).T)) + " = " + X(a.Value).T, a);
        }

        static bool IsPositionTuple(IrType t) => t.Kind == IrTypeKind.Tuple && t.Items != null && t.Items.Count == 2 && t.Items.All(i => i.IsNumeric);

        void AssignOne(IrExpr target, IrExpr value, IrNode commentsOf, bool first, string valueText = null)
        {
            var s = NameSym(target);
            var t = TypeOf(target);
            var v = valueText ?? X(value, t).T;
            AssignOneText(target, v, TypeOf(value), commentsOf);
        }

        void AssignOneText(IrExpr target, string valueText, IrType valueType, IrNode commentsOf = null)
        {
            var s = NameSym(target);
            if (s != null && unpackDeclared.Contains(s) && !declared.Contains(s))
            {
                declared.Add(s);
                var t = SymType(s);
                if (!t.IsKnown || (t.Kind == IrTypeKind.Any && valueType != null && valueType.IsKnown)) t = valueType;
                Line((t.IsKnown ? TypeText(t, types.IsBoxed(s)) : "var") + " " + Declare(s) + " = " + valueText + ";", commentsOf);
                return;
            }
            if (target is IrIndex ix)
            {
                var text = AssignText(new IrAssign { Target = target, Value = new IrName(new IrSymbol(valueText, IrSymbolKind.Local)) { Type = valueType } });
                Line(text + ";", commentsOf);
                return;
            }
            Line(TargetText(target) + " = " + valueText + ";", commentsOf);
        }

        /// <summary>Unpacking targets already declared by their first unpacking assignment.</summary>
        readonly HashSet<IrSymbol> declared = new HashSet<IrSymbol>();

        // ================================================================== expressions

        struct Ex
        {
            public string T; public int P;
            public Ex(string t, int p) { T = t; P = p; }
        }

        const int PrecOr = 1, PrecAnd = 2, PrecEq = 3, PrecRel = 4, PrecAdd = 5, PrecMul = 6;

        IrType TypeOf(IrExpr e) => types.TypeOf(e);
        IrType SymType(IrSymbol s) => s == null ? IrType.Unknown : types.Sym(s);
        bool IsInt(IrExpr e) => TypeOf(e).Kind == IrTypeKind.Int;
        bool IsStr(IrExpr e) => TypeOf(e).IsString || (e is IrBinary b && b.Op == IrBinOp.Concat) || (e is IrCast c && c.Target != null && c.Target.IsString);

        static bool IsSimple(IrExpr e) => e is IrName || e is IrLiteral || e is IrConst;

        string Postfix(IrExpr e) { var x = X(e); return Wrap(x.T, x.P, PrecPostfix); }
        static string W(Ex x, int min) => Wrap(x.T, x.P, min);

        string NameRef(IrName n)
        {
            var s = n.Symbol;
            if (s == null) { Problem("unresolved name"); return "null"; }
            switch (s.Kind)
            {
                case IrSymbolKind.ExternalGlobal: return (s.Module ?? "Other") + "." + Ident(s.Name, CamelNames);
                case IrSymbolKind.Function: return "this::" + Name(s);
                case IrSymbolKind.ExternalFunction: return (s.Module ?? "Other") + "::" + Ident(s.Name, CamelNames);
                case IrSymbolKind.Global:
                    {
                        var nm = Name(s);
                        var live = LiveLocal(nm);
                        return live != null && live != s ? "this." + nm : nm;
                    }
            }
            return Name(s);
        }

        Ex X(IrExpr e, IrType expected = null)
        {
            switch (e)
            {
                case null: return new Ex("null", PrecAtom);
                case IrLiteral l: return Literal(l, false);
                case IrName n: return new Ex(NameRef(n), PrecAtom);
                case IrConst c: return new Ex(c.Category == null || c.Category == "Directions" ? c.Name : c.Category + "." + c.Name, PrecPostfix);
                case IrUnary u:
                    {
                        if (u.Op == IrUnOp.Not)
                        {
                            var ot = TypeOf(u.Operand);
                            if (!fromJava && ot.Kind != IrTypeKind.Bool && ot.IsKnown && ot.Kind != IrTypeKind.Any)
                            {
                                var ox = X(u.Operand);
                                switch (ot.Kind)
                                {
                                    case IrTypeKind.Int: case IrTypeKind.Float: return new Ex(W(ox, PrecRel + 1) + " == 0", PrecEq);
                                    case IrTypeKind.String: case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Map: return new Ex(W(ox, PrecPostfix) + ".isEmpty()", PrecPostfix);
                                    case IrTypeKind.Array: return new Ex(W(ox, PrecPostfix) + ".length == 0", PrecEq);
                                    default: return new Ex(W(ox, PrecRel + 1) + " == null", PrecEq);
                                }
                            }
                            var o = Cond(u.Operand);
                            return new Ex("!" + W(o, PrecUnary), PrecUnary);
                        }
                        var x = X(u.Operand, expected);
                        var sign = u.Op == IrUnOp.Neg ? "-" : "+";
                        var t = W(x, PrecUnary);
                        if (t.StartsWith("-") || t.StartsWith("+")) t = "(" + t + ")";
                        return new Ex(sign + t, PrecUnary);
                    }
                case IrBinary b: return BinText(b.Op, b.Left, b.Right, b.Type);
                case IrConditional ce:
                    {
                        var c = Cond(ce.Cond);
                        var a = X(ce.Then, expected);
                        var z = X(ce.Else, expected);
                        return new Ex(W(c, PrecOr) + " ? " + W(a, PrecOr) + " : " + W(z, PrecTernary), PrecTernary);
                    }
                case IrCall call: return Call(call);
                case IrOpCall op: return Op(op);
                case IrIndex ix: return Index(ix);
                case IrListLit ll: return ListLit(ll, expected);
                case IrSetLit sl: return SetLit(sl, expected);
                case IrMapLit ml: return MapLit(ml, expected);
                case IrTupleLit tl:
                    Problem("tuples are not supported in Java");
                    return new Ex("null /* (" + string.Join(", ", tl.Items.Select(i => X(i).T)) + ") */", PrecAtom);
                case IrLambda lam: return Lambda(lam);
                case IrFuncRef fr: return FuncRef(fr);
                case IrCast ca: return Cast(ca);
            }
            Problem("unsupported expression " + e.GetType().Name);
            return new Ex("null /* unsupported */", PrecAtom);
        }

        Ex Literal(IrLiteral l, bool asDouble)
        {
            switch (l.Kind)
            {
                case IrLitKind.Int:
                    if (asDouble) return new Ex(FormatFloat(l.Int), l.Int < 0 ? PrecUnary : PrecAtom);
                    return new Ex(l.Int.ToString(CultureInfo.InvariantCulture), l.Int < 0 ? PrecUnary : PrecAtom);
                case IrLitKind.Float:
                    {
                        var t = FormatFloat(l.Float);
                        return new Ex(t, t.StartsWith("-") ? PrecUnary : PrecAtom);
                    }
                case IrLitKind.Bool: return new Ex(l.Bool ? "true" : "false", PrecAtom);
                case IrLitKind.Null: return new Ex("null", PrecAtom);
                default:
                    {
                        var s = l.Str ?? "";
                        if (s.IndexOf('\n') >= 0) Problem("line breaks inside strings are not supported by the game");
                        return new Ex("\"" + Escape(s) + "\"", PrecAtom);
                    }
            }
        }

        /// <summary>A value stored into a generic collection of elem: ints become doubles for Double elements (no auto widening when boxing).</summary>
        Ex Boxed(IrExpr e, IrType elem)
        {
            if (elem != null && elem.Kind == IrTypeKind.Float && IsInt(e))
            {
                if (e is IrLiteral l) return Literal(l, true);
                if (e is IrUnary u && u.Op == IrUnOp.Neg && u.Operand is IrLiteral ul) return new Ex("-" + Literal(ul, true).T, PrecUnary);
                return new Ex("(double) " + W(X(e), PrecUnary), PrecUnary);
            }
            return X(e, elem);
        }

        /// <summary>A condition (if / while / ! / ?:): non-boolean Python truthiness becomes an explicit test.</summary>
        Ex Cond(IrExpr e)
        {
            if (e is IrBinary b && (b.Op == IrBinOp.And || b.Op == IrBinOp.Or))
            {
                var prec = b.Op == IrBinOp.And ? PrecAnd : PrecOr;
                var l = Cond(b.Left); var r = Cond(b.Right);
                return new Ex(W(l, prec) + (b.Op == IrBinOp.And ? " && " : " || ") + W(r, prec + 1), prec);
            }
            var t = TypeOf(e);
            var x = X(e);
            switch (t.Kind)
            {
                case IrTypeKind.Int: case IrTypeKind.Float: return new Ex(W(x, PrecRel + 1) + " != 0", PrecEq);
                case IrTypeKind.String: case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Map:
                    return new Ex("!" + W(x, PrecPostfix) + ".isEmpty()", PrecUnary);
                case IrTypeKind.Array: return new Ex(W(x, PrecPostfix) + ".length != 0", PrecEq);
                case IrTypeKind.Game: case IrTypeKind.Tuple: case IrTypeKind.Func: return new Ex(W(x, PrecRel + 1) + " != null", PrecEq);
            }
            return x;
        }

        Ex Bin(string op, int prec, IrExpr l, IrExpr r, Ex? lx = null, Ex? rx = null)
        {
            var a = lx ?? X(l); var b = rx ?? X(r);
            return new Ex(W(a, prec) + " " + op + " " + W(b, prec + 1), prec);
        }

        /// <summary>An Object-typed operand (measure(), waitFor()) next to a number: cast it so Java can compare.</summary>
        Ex NumOperand(IrExpr e, IrExpr other)
        {
            var t = TypeOf(e);
            var x = X(e);
            if ((t.Kind == IrTypeKind.Any || t.Kind == IrTypeKind.Unknown && !fromJava) && TypeOf(other).IsNumeric && !(e is IrLiteral))
                return new Ex("(double) " + W(x, PrecUnary), PrecUnary);
            return x;
        }

        Ex BinText(IrBinOp op, IrExpr l, IrExpr r, IrType resultType)
        {
            bool ints = IsInt(l) && IsInt(r);
            switch (op)
            {
                case IrBinOp.Add:
                    if (IsStr(l) || IsStr(r)) return Concat(l, r);
                    return Bin("+", PrecAdd, l, r);
                case IrBinOp.Concat: return Concat(l, r);
                case IrBinOp.Sub: return Bin("-", PrecAdd, l, r);
                case IrBinOp.Mul:
                    if (IsStr(l) || IsStr(r) || TypeOf(l).Kind == IrTypeKind.List || TypeOf(r).Kind == IrTypeKind.List) Problem("repeating a string or list with * is not supported in Java");
                    return Bin("*", PrecMul, l, r);
                case IrBinOp.TrueDiv:
                    if (ints) return new Ex("(double) " + W(X(l), PrecUnary) + " / " + W(X(r), PrecMul + 1), PrecMul);
                    return Bin("/", PrecMul, l, r);
                case IrBinOp.IntDivTrunc:
                    if (ints || fromJava) return Bin("/", PrecMul, l, r);
                    return new Ex("(int) (" + Bin("/", PrecMul, l, r).T + ")", PrecUnary);
                case IrBinOp.IntDivFloor:
                    if (ints && IsNonNeg(l) && IsNonNeg(r)) return Bin("/", PrecMul, l, r);
                    if (ints) return new Ex("Math.floorDiv(" + X(l).T + ", " + X(r).T + ")", PrecPostfix);
                    return new Ex("Math.floor(" + Bin("/", PrecMul, l, r).T + ")", PrecPostfix);
                case IrBinOp.ModTrunc: return Bin("%", PrecMul, l, r);
                case IrBinOp.ModFloor:
                    if (ints && IsNonNeg(l) && IsNonNeg(r)) return Bin("%", PrecMul, l, r);
                    if (ints) return new Ex("Math.floorMod(" + X(l).T + ", " + X(r).T + ")", PrecPostfix);
                    return Bin("%", PrecMul, l, r);
                case IrBinOp.Pow:
                    {
                        var call = "Math.pow(" + X(l).T + ", " + X(r).T + ")";
                        bool intResult = resultType != null && resultType.Kind == IrTypeKind.Int || (resultType == null || !resultType.IsKnown) && ints && !fromJava;
                        return intResult ? new Ex("(int) " + call, PrecUnary) : new Ex(call, PrecPostfix);
                    }
                case IrBinOp.Eq: case IrBinOp.Ne:
                    return EqText(NumOperand(l, r), TypeOf(l), NumOperand(r, l), TypeOf(r), op == IrBinOp.Ne);
                case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge:
                    {
                        if (IsStr(l) && IsStr(r)) Problem("comparing strings with < > is not supported in JavaFarmer");
                        var s = op == IrBinOp.Lt ? "<" : op == IrBinOp.Le ? "<=" : op == IrBinOp.Gt ? ">" : ">=";
                        return Bin(s, PrecRel, l, r, NumOperand(l, r), NumOperand(r, l));
                    }
                case IrBinOp.And: case IrBinOp.Or:
                    {
                        var prec = op == IrBinOp.And ? PrecAnd : PrecOr;
                        var a = TypeOf(l).Kind == IrTypeKind.Bool ? X(l) : Cond(l);
                        var b = TypeOf(r).Kind == IrTypeKind.Bool ? X(r) : Cond(r);
                        return new Ex(W(a, prec) + (op == IrBinOp.And ? " && " : " || ") + W(b, prec + 1), prec);
                    }
            }
            Problem("unsupported operator " + op);
            return new Ex("null", PrecAtom);
        }

        Ex EqText(Ex a, IrType at, Ex b, IrType bt, bool ne)
        {
            if (at.IsString && bt.IsString && a.T != "null" && b.T != "null")
            {
                var eq = W(a, PrecPostfix) + ".equals(" + b.T + ")";
                return ne ? new Ex("!" + eq, PrecUnary) : new Ex(eq, PrecPostfix);
            }
            return new Ex(W(a, PrecEq) + (ne ? " != " : " == ") + W(b, PrecEq + 1), PrecEq);
        }

        Ex EqText(string a, int ap, IrType at, Ex b, IrType bt, bool ne) => EqText(new Ex(a, ap), at, b, bt, ne);

        Ex Concat(IrExpr l, IrExpr r)
        {
            var a = X(l); var b = X(r);
            if (!IsStr(l) && !IsStr(r))
                return new Ex("\"\" + " + W(a, PrecAdd + 1) + " + " + W(b, PrecAdd + 1), PrecAdd);
            return new Ex(W(a, PrecAdd) + " + " + W(b, PrecAdd + 1), PrecAdd);
        }

        /// <summary>Conservative "provably >= 0" (a subset of the emitter's analysis, so a / b prints the same Python).</summary>
        bool IsNonNeg(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l: return l.Kind == IrLitKind.Int && l.Int >= 0;
                case IrBinary b when b.Op == IrBinOp.Add || b.Op == IrBinOp.Mul || b.Op == IrBinOp.IntDivFloor || b.Op == IrBinOp.IntDivTrunc
                                  || b.Op == IrBinOp.ModFloor || b.Op == IrBinOp.ModTrunc:
                    return IsNonNeg(b.Left) && IsNonNeg(b.Right);
                case IrCall c when c.Kind == IrCallKind.Builtin: return GameApi.Find(c.Builtin)?.NonNegative ?? false;
                case IrOpCall o: return o.Op == IrOp.Len;
            }
            return false;
        }

        // ------------------------------------------------------------------ calls

        string Args(IEnumerable<string> a) => "(" + string.Join(", ", a) + ")";

        Ex Call(IrCall c)
        {
            switch (c.Kind)
            {
                case IrCallKind.Builtin: return BuiltinCall(c.Builtin, c.Args);
                case IrCallKind.Function:
                    {
                        var s = c.Function;
                        if (s == null) { Problem("call of an unknown function"); return new Ex("null", PrecAtom); }
                        var args = c.Args.Select((a, i) => ArgFor(s, i, a)).ToList();
                        var fn = s.Function;
                        if (fn != null && args.Count < fn.Params.Count)
                        {
                            // Java has no default arguments: pass the default value explicitly
                            for (int i = args.Count; i < fn.Params.Count; i++)
                            {
                                if (fn.Params[i].Default == null) { Problem("missing argument"); break; }
                                args.Add(X(fn.Params[i].Default, SymType(fn.Params[i].Symbol)).T);
                            }
                        }
                        var name = s.Kind == IrSymbolKind.ExternalFunction ? (s.Module ?? "Other") + "." + Ident(s.Name, CamelNames) : Name(s);
                        return new Ex(name + Args(args), PrecPostfix);
                    }
                case IrCallKind.Value:
                    {
                        if (c.Args.Count > 0) Problem("calling a function value with arguments is not supported in Java (Runnable / Supplier take none)");
                        var ft = TypeOf(c.Callee);
                        bool value = ft.Kind == IrTypeKind.Func && ft.Elem != null && ft.Elem.IsKnown && ft.Elem.Kind != IrTypeKind.Void;
                        return new Ex(Postfix(c.Callee) + (value ? ".get" : ".run") + Args(c.Args.Select(a => X(a).T)), PrecPostfix);
                    }
            }
            Problem("unsupported call");
            return new Ex("null", PrecAtom);
        }

        string ArgFor(IrSymbol fn, int i, IrExpr a)
        {
            IrType pt = null;
            if (fn.Function != null && i < fn.Function.Params.Count) pt = SymType(fn.Function.Params[i].Symbol);
            else if (fn.ParamTypes != null && i < fn.ParamTypes.Count) pt = fn.ParamTypes[i];
            return X(a, pt).T;
        }

        Ex BuiltinCall(string b, List<IrExpr> args)
        {
            switch (b)
            {
                case "print":
                    {
                        if (args.Count == 0) return new Ex("System.out.println()", PrecPostfix);
                        if (args.Count == 1) return new Ex("System.out.println(" + X(args[0]).T + ")", PrecPostfix);
                        var sb = new StringBuilder(W(X(args[0]), PrecAdd));
                        for (int i = 1; i < args.Count; i++) sb.Append(" + \" \" + ").Append(W(X(args[i]), PrecAdd + 1));
                        return new Ex("System.out.println(" + sb + ")", PrecPostfix);
                    }
                case "min": case "max":
                    {
                        if (args.Count < 2) { Problem(b + "() of a collection is not supported in Java"); return new Ex("Math." + b + Args(args.Select(a => X(a).T)), PrecPostfix); }
                        var acc = X(args[args.Count - 1]).T;
                        for (int i = args.Count - 2; i >= 0; i--) acc = "Math." + b + "(" + X(args[i]).T + ", " + acc + ")";
                        return new Ex(acc, PrecPostfix);
                    }
                case "abs": return new Ex("Math.abs" + Args(args.Select(a => X(a).T)), PrecPostfix);
                case "random": return new Ex("Math.random()", PrecPostfix);
                case "str": return new Ex("String.valueOf" + Args(args.Select(a => X(a).T)), PrecPostfix);
                case "len": if (args.Count == 1) return Op(new IrOpCall(IrOp.Len, args[0])); break;
                case "range":
                    Problem("range() outside a for loop is not supported in Java");
                    break;
            }
            string jn = null;
            try { jn = GameApi.ToJava(b); } catch { }
            if (jn == null) { jn = Camel(b); if (GameApi.Find(b) == null) Problem("unknown builtin " + b); }
            var api = GameApi.Find(b);
            var parts = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                IrType pt = api != null && api.Params.Count > 0 ? api.Params[Math.Min(i, api.Params.Count - 1)].Type : null;
                parts.Add(X(args[i], pt).T);
            }
            return new Ex(jn + Args(parts), PrecPostfix);
        }

        Ex FuncRef(IrFuncRef fr)
        {
            if (fr.Function != null)
            {
                if (fr.Function.Kind == IrSymbolKind.ExternalFunction) return new Ex((fr.Function.Module ?? "Other") + "::" + Ident(fr.Function.Name, CamelNames), PrecPostfix);
                return new Ex("this::" + Name(fr.Function), PrecPostfix);
            }
            if (fr.Builtin != null)
            {
                var api = GameApi.Find(fr.Builtin);
                if (api != null && api.MinArgs == 0) return new Ex("() -> " + BuiltinCall(fr.Builtin, new List<IrExpr>()).T, PrecTernary);
                Problem("a builtin with parameters (" + fr.Builtin + ") cannot be used as a value in Java");
                return new Ex("null /* " + fr.Builtin + " */", PrecAtom);
            }
            return new Ex("null", PrecAtom);
        }

        Ex Lambda(IrLambda lam)
        {
            if (lam.Params.Count > 0) Problem("lambdas with parameters are not supported in JavaFarmer (only Runnable / Supplier)");
            PushScope();
            var ps = lam.Params.Count == 1 ? Declare(lam.Params[0].Symbol) : "(" + string.Join(", ", lam.Params.Select(p => Declare(p.Symbol))) + ")";
            string text;
            if (lam.BodyExpr != null) text = ps + " -> " + W(X(lam.BodyExpr), PrecTernary);
            else
            {
                int mark = Out.Length;
                Indent();
                PushScope();
                if (lam.Body != null) { Stmts(lam.Body.Stmts); Comments(lam.Body.TrailingComments); }
                PopScope();
                Dedent();
                var inner = Out.ToString(mark, Out.Length - mark);
                Out.Length = mark;
                text = inner.Length == 0 ? ps + " -> {}" : ps + " -> {" + Options.Newline + inner + IndentText + "}";
            }
            PopScope();
            return new Ex(text, PrecTernary);
        }

        Ex Cast(IrCast ca)
        {
            var t = ca.Target ?? IrType.Unknown;
            var ot = TypeOf(ca.Operand);
            var x = X(ca.Operand);
            switch (t.Kind)
            {
                case IrTypeKind.String: return new Ex("String.valueOf(" + x.T + ")", PrecPostfix);
                case IrTypeKind.Int:
                    if (ot.IsString) Problem("converting a String to a number is not supported in JavaFarmer");
                    return new Ex("(int) " + W(x, PrecUnary), PrecUnary);
                case IrTypeKind.Float:
                    if (ot.IsString) Problem("converting a String to a number is not supported in JavaFarmer");
                    return new Ex("(double) " + W(x, PrecUnary), PrecUnary);
                case IrTypeKind.Bool: return new Ex("(boolean) " + W(x, PrecUnary), PrecUnary);
                case IrTypeKind.Unknown: case IrTypeKind.Any: return x;
            }
            return new Ex("(" + TypeText(t) + ") " + W(x, PrecUnary), PrecUnary);
        }

        // ------------------------------------------------------------------ collection operations

        string IndexText(IrIndex ix)
        {
            var it = TypeOf(ix.Index);
            Ex i;
            if (ix.BaseOffset != 0)
            {
                var raw = X(ix.Index);
                i = new Ex(W(raw, PrecAdd) + (ix.BaseOffset > 0 ? " - " + ix.BaseOffset : " + " + (-ix.BaseOffset)), PrecAdd);
            }
            else i = X(ix.Index);
            if (it.Kind == IrTypeKind.Float || it.Kind == IrTypeKind.Any) return "(int) " + W(i, PrecUnary);
            return i.T;
        }

        Ex Index(IrIndex ix)
        {
            var tt = TypeOf(ix.Target);
            var recv = Postfix(ix.Target);
            long lit = ix.Index is IrLiteral li && li.Kind == IrLitKind.Int && ix.BaseOffset == 0 ? li.Int :
                       ix.Index is IrUnary un && un.Op == IrUnOp.Neg && un.Operand is IrLiteral ul && ul.Kind == IrLitKind.Int && ix.BaseOffset == 0 ? -ul.Int : long.MinValue;
            switch (tt.Kind)
            {
                case IrTypeKind.Array:
                    if (lit < 0 && lit != long.MinValue) return new Ex(recv + "[" + recv + ".length - " + (-lit) + "]", PrecPostfix);
                    return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
                case IrTypeKind.String:
                    return new Ex(recv + ".charAt(" + IndexText(ix) + ")", PrecPostfix);
                case IrTypeKind.Map:
                    return new Ex(recv + ".get(" + X(ix.Index, tt.Key).T + ")", PrecPostfix);
                case IrTypeKind.Tuple: case IrTypeKind.Game:
                    {
                        bool comp = tt.IsGame("Companion") || (tt.Kind == IrTypeKind.Tuple && tt.Items != null && tt.Items.Count == 2 && !tt.Items[0].IsNumeric);
                        if (lit == 0) return new Ex(recv + (comp ? ".entity()" : ".x()"), PrecPostfix);
                        if (lit == 1) return new Ex(recv + (comp ? ".position()" : ".y()"), PrecPostfix);
                        Problem("indexing a tuple is not supported in Java");
                        return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
                    }
                case IrTypeKind.List:
                    if (lit < 0 && lit != long.MinValue)
                    {
                        if (!IsSimple(ix.Target)) Problem("negative index on a computed list");
                        return new Ex(recv + ".get(" + recv + ".size() - " + (-lit) + ")", PrecPostfix);
                    }
                    return new Ex(recv + ".get(" + IndexText(ix) + ")", PrecPostfix);
            }
            if (fromJava) return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
            return new Ex(recv + ".get(" + IndexText(ix) + ")", PrecPostfix);
        }

        Ex Op(IrOpCall o)
        {
            var a = o.Args;
            if (a.Count == 0) { Problem("operation " + o.Op + " without arguments"); return new Ex("null", PrecAtom); }
            var rt = TypeOf(a[0]);
            string R() => Postfix(a[0]);
            string A(int i) => i < a.Count ? X(a[i]).T : "null";
            string B(int i) => i < a.Count ? Boxed(a[i], rt.Elem).T : "null";
            Ex M(string text) => new Ex(text, PrecPostfix);
            switch (o.Op)
            {
                case IrOp.Len:
                    if (rt.Kind == IrTypeKind.Array) return M(R() + ".length");
                    if (rt.Kind == IrTypeKind.String) return M(R() + ".length()");
                    return M(R() + ".size()");
                case IrOp.IsEmpty:
                    if (rt.Kind == IrTypeKind.Array) return new Ex(R() + ".length == 0", PrecEq);
                    return M(R() + ".isEmpty()");
                case IrOp.Contains:
                    if (rt.Kind == IrTypeKind.Map) return M(R() + ".containsKey(" + A(1) + ")");
                    if (rt.Kind == IrTypeKind.Array) Problem("contains on an array is not supported in Java");
                    return M(R() + ".contains(" + A(1) + ")");
                case IrOp.ListAppend: return M(R() + ".add(" + B(1) + ")");
                case IrOp.SetAdd: return M(R() + ".add(" + B(1) + ")");
                case IrOp.ListInsert: return M(R() + ".add(" + A(1) + ", " + B(2) + ")");
                case IrOp.ListPop:
                    if (a.Count >= 2) return M(R() + ".remove(" + IndexOf(a[1]) + ")");
                    if (!IsSimple(a[0])) Problem("pop() on a computed list");
                    return M(R() + ".remove(" + R() + ".size() - 1)");
                case IrOp.ListSet: return M(R() + ".set(" + IndexOf(a[1]) + ", " + B(2) + ")");
                case IrOp.ListIndexOf: return M(R() + ".indexOf(" + A(1) + ")");
                case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent:
                    {
                        var v = A(1);
                        // List<Integer>.remove(int) removes by index: box the value so it removes the element
                        if (rt.Kind == IrTypeKind.List && (rt.Elem?.Kind == IrTypeKind.Int) && TypeOf(a[1]).Kind == IrTypeKind.Int)
                            v = "(Integer) " + W(X(a[1]), PrecUnary);
                        return M(R() + ".remove(" + v + ")");
                    }
                case IrOp.Clear: return M(R() + ".clear()");
                case IrOp.MapPut: return M(R() + ".put(" + X(a.Count > 1 ? a[1] : null, rt.Key).T + ", " + (a.Count > 2 ? Boxed(a[2], rt.Elem).T : "null") + ")");
                case IrOp.MapGetOrNull: return M(R() + ".get(" + A(1) + ")");
                case IrOp.MapGetOrDefault: return M(R() + ".getOrDefault(" + A(1) + ", " + B(2) + ")");
                case IrOp.MapRemove: case IrOp.MapRemoveIfPresent: return M(R() + ".remove(" + A(1) + ")");
                case IrOp.MapKeys: return M(R() + ".keySet()");
                case IrOp.MapValues: return M(R() + ".values()");
                case IrOp.ListCopy: return M("new ArrayList<>(" + CopySource(a[0]) + ")");
                case IrOp.SetCopy: return M("new HashSet<>(" + CopySource(a[0]) + ")");
                case IrOp.MapCopy: return M("new HashMap<>(" + CopySource(a[0]) + ")");
                case IrOp.ArrayNew: case IrOp.ArrayNew2:
                    {
                        var t = TypeOf(o);
                        var elem = t.Kind == IrTypeKind.Array ? t.Elem : null;
                        if (o.Op == IrOp.ArrayNew2 && elem != null && elem.Kind == IrTypeKind.Array) elem = elem.Elem;
                        var d = o.Op == IrOp.ArrayNew ? (a.Count > 1 ? a[1] : null) : (a.Count > 2 ? a[2] : null);
                        if (elem == null || !elem.IsKnown) elem = d != null ? TypeOf(d) : IrType.Any;
                        if (d != null && !IsDefaultOf(d, elem)) Problem("an array filled with a value other than the default is not supported in Java");
                        var dims = o.Op == IrOp.ArrayNew ? "[" + A(0) + "]" : "[" + A(0) + "][" + A(1) + "]";
                        return M("new " + TypeText(elem) + dims);
                    }
                case IrOp.Floor: return M("Math.floor(" + A(0) + ")");
                case IrOp.Ceil: return M("Math.ceil(" + A(0) + ")");
                case IrOp.Round: return M("Math.round(" + A(0) + ")");
                case IrOp.Sqrt: return M("Math.sqrt(" + A(0) + ")");
                case IrOp.PosX: return M(R() + ".x()");
                case IrOp.PosY: return M(R() + ".y()");
                case IrOp.CompanionEntity: return M(R() + ".entity()");
                case IrOp.CompanionPos: return M(R() + ".position()");
            }
            Problem("unsupported operation " + o.Op);
            return new Ex("null", PrecAtom);
        }

        string IndexOf(IrExpr i)
        {
            var t = TypeOf(i);
            var x = X(i);
            return t.Kind == IrTypeKind.Float || t.Kind == IrTypeKind.Any ? "(int) " + W(x, PrecUnary) : x.T;
        }

        /// <summary>new ArrayList&lt;&gt;(List.of(...)): the inner literal stays immutable.</summary>
        string CopySource(IrExpr e)
        {
            switch (e)
            {
                case IrListLit l when !l.IsArray && l.Items.Count > 0: return CollectionOf("List", l.Items, TypeOf(l).Elem);
                case IrSetLit s when s.Items.Count > 0: return CollectionOf("Set", s.Items, TypeOf(s).Elem);
                case IrMapLit m when m.Keys.Count > 0: return MapOf(m);
            }
            return X(e).T;
        }

        bool IsDefaultOf(IrExpr d, IrType t)
        {
            if (!(d is IrLiteral l)) return false;
            switch (t.Kind)
            {
                case IrTypeKind.Int: case IrTypeKind.Float: return (l.Kind == IrLitKind.Int && l.Int == 0) || (l.Kind == IrLitKind.Float && l.Float == 0);
                case IrTypeKind.Bool: return l.Kind == IrLitKind.Bool && !l.Bool;
            }
            return l.Kind == IrLitKind.Null;
        }

        string CollectionOf(string kind, List<IrExpr> items, IrType elem)
        {
            if (items.Any(i => i is IrLiteral l && l.Kind == IrLitKind.Null)) Problem("null elements are not allowed in " + kind + ".of");
            return kind + ".of(" + string.Join(", ", items.Select(i => Boxed(i, elem).T)) + ")";
        }

        string MapOf(IrMapLit m, IrType t = null)
        {
            t = t ?? TypeOf(m);
            if (m.Keys.Count > 10) Problem("Map.of takes at most 10 entries");
            var parts = new List<string>();
            for (int i = 0; i < m.Keys.Count; i++) { parts.Add(Boxed(m.Keys[i], t.Key).T); parts.Add(Boxed(m.Values[i], t.Elem).T); }
            return "Map.of(" + string.Join(", ", parts) + ")";
        }

        bool Mutable(IrExpr lit) => !fromJava && !types.ImmutableLiterals.Contains(lit);

        /// <summary>The literal's type, or the declared one when it has the same shape (Map&lt;Item, Double&gt; m = {Items.Hay: 1}).</summary>
        IrType LitType(IrExpr lit, IrType expected)
        {
            var t = TypeOf(lit);
            if (expected != null && expected.Kind == t.Kind && !fromJava) return expected;
            return t;
        }

        Ex ListLit(IrListLit l, IrType expected = null)
        {
            var t = LitType(l, expected);
            if (l.IsArray)
            {
                var elem = t.Elem != null && t.Elem.IsKnown ? t.Elem : IrType.Any;
                return new Ex("new " + TypeText(elem) + "[]{" + string.Join(", ", l.Items.Select(i => X(i, elem).T)) + "}", PrecPostfix);
            }
            if (l.Items.Count == 0) return new Ex("new ArrayList<>()", PrecPostfix);
            var of = CollectionOf("List", l.Items, t.Elem);
            return new Ex(Mutable(l) ? "new ArrayList<>(" + of + ")" : of, PrecPostfix);
        }

        Ex SetLit(IrSetLit s, IrType expected = null)
        {
            if (s.Items.Count == 0) return new Ex("new HashSet<>()", PrecPostfix);
            var of = CollectionOf("Set", s.Items, LitType(s, expected).Elem);
            return new Ex(Mutable(s) ? "new HashSet<>(" + of + ")" : of, PrecPostfix);
        }

        Ex MapLit(IrMapLit m, IrType expected = null)
        {
            if (m.Keys.Count == 0) return new Ex("new HashMap<>()", PrecPostfix);
            var of = MapOf(m, LitType(m, expected));
            return new Ex(Mutable(m) ? "new HashMap<>(" + of + ")" : of, PrecPostfix);
        }

        // ================================================================== types

        /// <summary>Java spelling of a neutral type. box: generic argument position (Integer, Double, Boolean).</summary>
        internal static string TypeText(IrType t, bool box = false)
        {
            if (t == null) return "Object";
            switch (t.Kind)
            {
                case IrTypeKind.Int: return box ? "Integer" : "int";
                case IrTypeKind.Float: return box ? "Double" : "double";
                case IrTypeKind.Bool: return box ? "Boolean" : "boolean";
                case IrTypeKind.String: return "String";
                case IrTypeKind.Void: return box ? "Object" : "void";
                case IrTypeKind.List: return "List<" + TypeText(t.Elem, true) + ">";
                case IrTypeKind.Set: return "Set<" + TypeText(t.Elem, true) + ">";
                case IrTypeKind.Map: return "Map<" + TypeText(t.Key, true) + ", " + TypeText(t.Elem, true) + ">";
                case IrTypeKind.Array: return TypeText(t.Elem == null || !t.Elem.IsKnown ? IrType.Any : t.Elem) + "[]";
                case IrTypeKind.Game: return t.GameName ?? "Object";
                case IrTypeKind.Tuple:
                    if (IsPositionTuple(t)) return "Position";
                    if (t.Items != null && t.Items.Count == 2 && t.Items[0].IsGame("Entity")) return "Companion";
                    return "Object";
                case IrTypeKind.Func:
                    return t.Elem == null || t.Elem.Kind == IrTypeKind.Void || !t.Elem.IsKnown ? "Runnable" : "Supplier<" + TypeText(t.Elem, true) + ">";
            }
            return "Object";
        }

        static string DefaultValue(IrType t, bool boxed = false)
        {
            if (boxed) return "null";
            switch (t?.Kind ?? IrTypeKind.Unknown)
            {
                case IrTypeKind.Int: return "0";
                case IrTypeKind.Float: return "0.0";
                case IrTypeKind.Bool: return "false";
            }
            return "null";
        }

        /// <summary>Java "can complete normally" (approximation): false after return / an endless loop without break.</summary>
        static bool CanComplete(IrStmt s)
        {
            switch (s)
            {
                case null: return true;
                case IrReturn _: return false;
                case IrBlock b: return b.Stmts.Count == 0 || b.Stmts.All(CanCompleteOrNotLast(b));
                case IrIf f: return f.Else == null || CanComplete(f.Then) || CanComplete(f.Else);
                case IrLoop l: return BreaksOut(l.Body);
                case IrWhile w: return !(w.Cond is IrLiteral lit && lit.Kind == IrLitKind.Bool && lit.Bool) || BreaksOut(w.Body);
                case IrSwitch sw: return !sw.Cases.Any(c => c.IsDefault) || sw.Cases.Any(c => CanComplete(c.Body));
            }
            return true;
        }

        static Func<IrStmt, bool> CanCompleteOrNotLast(IrBlock b) => s => CanComplete(s);
    }

    /// <summary>
    /// Type inference for untyped IR (Python, JS, R sources) as the Java printer needs it: joins the values assigned to
    /// every symbol (declarations, assignments, call arguments, appended elements, loop variables) to a fixpoint.
    /// Typed IR (Java) passes straight through. Never mutates the IR.
    /// </summary>
    internal sealed class JavaTypeGuess
    {
        sealed class Src { public int Kind; public IrExpr E; public IrType T; public IrBinOp Op; }
        const int Value = 0, Fixed = 1, ElemOf = 2, KeyOf = 3, MapValOf = 4, Compound = 5, TupleItem = 6;

        readonly Dictionary<IrSymbol, IrType> sym = new Dictionary<IrSymbol, IrType>();
        readonly Dictionary<IrSymbol, List<Src>> src = new Dictionary<IrSymbol, List<Src>>();
        readonly Dictionary<IrSymbol, List<IrExpr>> elemSrc = new Dictionary<IrSymbol, List<IrExpr>>();
        readonly Dictionary<IrSymbol, List<IrExpr>> keySrc = new Dictionary<IrSymbol, List<IrExpr>>();
        readonly Dictionary<IrSymbol, IrTypeKind> kindHint = new Dictionary<IrSymbol, IrTypeKind>();
        readonly Dictionary<IrSymbol, List<IrExpr>> retSrc = new Dictionary<IrSymbol, List<IrExpr>>();
        readonly Dictionary<IrSymbol, IrType> ret = new Dictionary<IrSymbol, IrType>();
        readonly Dictionary<IrSymbol, IrFunction> funcs = new Dictionary<IrSymbol, IrFunction>();
        readonly HashSet<IrSymbol> sawNull = new HashSet<IrSymbol>();
        /// <summary>Collection literals whose variable is never modified (List.of is enough).</summary>
        public readonly HashSet<IrExpr> ImmutableLiterals = new HashSet<IrExpr>();

        public JavaTypeGuess(IrModule m)
        {
            foreach (var f in m.Functions) if (f.Symbol != null) funcs[f.Symbol] = f;
            bool typed = m.SourceLanguage == null || m.SourceLanguage == "java";
            typedModule = typed;
            var parent = typed ? null : new Dictionary<IrNode, IrNode>();
            Walk(m, null, parent);
            foreach (var f in m.Functions)
            {
                if (f.Symbol == null || !(f.ReturnType == null || !f.ReturnType.IsKnown)) continue;
                var l = retSrc[f.Symbol] = new List<IrExpr>();
                foreach (var r in IrWalk.Descendants(f.Body, false).OfType<IrReturn>()) if (r.Value != null) l.Add(r.Value);
            }
            Solve();
            solved = true;
            if (!typed) Literals(m, parent);
        }

        /// <summary>One pass over the module: sources for inference (+ parents for the literal analysis).</summary>
        void Walk(IrNode n, IrNode par, Dictionary<IrNode, IrNode> parent)
        {
            if (n == null) return;
            if (parent != null && par != null && !parent.ContainsKey(n)) parent[n] = par;
            Collect(n);
            foreach (var c in IrWalk.Children(n)) Walk(c, n, parent);
        }

        void AddSrc(IrSymbol s, Src x)
        {
            if (s == null) return;
            if (!src.TryGetValue(s, out var l)) src[s] = l = new List<Src>();
            l.Add(x);
        }

        void AddElem(IrExpr recv, IrExpr elem, IrExpr key, IrTypeKind hint)
        {
            if (!(recv is IrName n) || n.Symbol == null) return;
            var s = n.Symbol;
            if (elem != null) { if (!elemSrc.TryGetValue(s, out var l)) elemSrc[s] = l = new List<IrExpr>(); l.Add(elem); }
            if (key != null) { if (!keySrc.TryGetValue(s, out var l)) keySrc[s] = l = new List<IrExpr>(); l.Add(key); }
            if (!kindHint.ContainsKey(s)) kindHint[s] = hint;
        }

        // ------------------------------------------------------------------ use-based expectations
        // A symbol with no value of its own (an uncalled parameter, a name a docs snippet never defines) is typed from
        // how it is used: a condition is boolean, move(d) makes d a Direction, range(n) / n % 2 make n an int ...
        sealed class Use { public IrType T; public IrExpr Like; public IrExpr IndexBy; public IrType Elem; }
        readonly Dictionary<IrSymbol, List<Use>> uses = new Dictionary<IrSymbol, List<Use>>();
        readonly Dictionary<IrSymbol, List<Use>> retUses = new Dictionary<IrSymbol, List<Use>>();

        void Expect(IrExpr e, Use u)
        {
            Dictionary<IrSymbol, List<Use>> d; IrSymbol s;
            // xs[i] used as a T: xs holds Ts
            if (e is IrIndex ix && ix.Target is IrName && u.T != null && u.IndexBy == null) { Expect(ix.Target, new Use { IndexBy = ix.Index, Elem = u.T }); return; }
            if (e is IrName n && n.Symbol != null && n.Symbol.Kind != IrSymbolKind.Function) { d = uses; s = n.Symbol; }
            else if (e is IrCall c && c.Kind == IrCallKind.Function && c.Function != null && c.Function.Function == null) { d = retUses; s = c.Function; }
            else return;
            if (!d.TryGetValue(s, out var l)) d[s] = l = new List<Use>();
            l.Add(u);
        }

        void ExpectT(IrExpr e, IrType t) { if (t != null && t.IsKnown && t.Kind != IrTypeKind.Any) Expect(e, new Use { T = t }); }

        static bool Arith(IrBinOp op) => op == IrBinOp.Sub || op == IrBinOp.Mul || op == IrBinOp.TrueDiv || op == IrBinOp.IntDivFloor || op == IrBinOp.IntDivTrunc
                                          || op == IrBinOp.ModFloor || op == IrBinOp.ModTrunc || op == IrBinOp.Pow || op == IrBinOp.Lt || op == IrBinOp.Le || op == IrBinOp.Gt || op == IrBinOp.Ge || op == IrBinOp.Add;

        void CollectUses(IrNode n)
        {
            switch (n)
            {
                case IrIf f: ExpectT(f.Cond, IrType.Bool); break;
                case IrWhile w: ExpectT(w.Cond, IrType.Bool); break;
                case IrDoWhile dw: ExpectT(dw.Cond, IrType.Bool); break;
                case IrConditional ce: ExpectT(ce.Cond, IrType.Bool); break;
                case IrUnary u: if (u.Op == IrUnOp.Not) ExpectT(u.Operand, IrType.Bool); else ExpectT(u.Operand, IrType.Int); break;
                case IrRangeFor r: ExpectT(r.Start, IrType.Int); ExpectT(r.End, IrType.Int); ExpectT(r.Step, IrType.Int); break;
                case IrForEach fe: Expect(fe.Iterable, new Use { T = IrType.List(IrType.Unknown) }); break;
                case IrIndex ix: Expect(ix.Target, new Use { IndexBy = ix.Index }); ExpectT(ix.Index, null); break;
                case IrBinary b:
                    if (b.Op == IrBinOp.And || b.Op == IrBinOp.Or) { ExpectT(b.Left, IrType.Bool); ExpectT(b.Right, IrType.Bool); }
                    else if (Arith(b.Op)) { Expect(b.Left, new Use { Like = b.Right }); Expect(b.Right, new Use { Like = b.Left }); }
                    break;
                case IrCall c:
                    if (c.Kind == IrCallKind.Builtin)
                    {
                        var api = GameApi.Find(c.Builtin);
                        if (api != null && api.Params.Count > 0)
                            for (int i = 0; i < c.Args.Count; i++)
                            {
                                var p = api.Params[Math.Min(i, api.Params.Count - 1)];
                                if (p.Variadic || p.TypeText == null) continue;
                                if (p.TypeText == "number") { Expect(c.Args[i], new Use { T = IrType.Int }); continue; }
                                // Entity|Item|Unlock: any member type will do
                                if (p.TypeText.Contains("|")) { ExpectT(c.Args[i], IrType.Parse(p.TypeText.Split('|')[0])); continue; }
                                ExpectT(c.Args[i], p.Type);
                            }
                    }
                    else if (c.Kind == IrCallKind.Function && c.Function?.Function != null)
                        for (int i = 0; i < c.Args.Count && i < c.Function.Function.Params.Count; i++) ExpectT(c.Args[i], c.Function.Function.Params[i].Symbol?.Type);
                    else if (c.Kind == IrCallKind.Value) Expect(c.Callee, new Use { T = IrType.Func(IrType.Void) });
                    break;
                case IrOpCall o when o.Args.Count > 0:
                    switch (o.Op)
                    {
                        case IrOp.Len: case IrOp.ListPop: case IrOp.ListIndexOf: Expect(o.Args[0], new Use { T = IrType.List(IrType.Unknown) }); break;
                    }
                    break;
            }
        }

        IrType FromUses(List<Use> l)
        {
            IrType t = IrType.Unknown;
            foreach (var u in l)
            {
                IrType ut;
                if (u.T != null) ut = u.T;
                else if (u.Like != null) { ut = TypeOf(u.Like); if (!ut.IsNumeric) ut = IrType.Unknown; }
                else if (u.IndexBy != null)
                {
                    var it = TypeOf(u.IndexBy);
                    ut = it.Kind == IrTypeKind.Int || !it.IsKnown ? IrType.List(u.Elem ?? IrType.Unknown) : IrType.Map(it, u.Elem ?? IrType.Unknown);
                }
                else continue;
                t = IrType.Join(t, ut);
            }
            return t;
        }

        bool typedModule;

        void Collect(IrNode n)
        {
            if (!typedModule) CollectUses(n);
            switch (n)
            {
                case IrVarDecl v: if (v.Init != null) AddSrc(v.Symbol, new Src { Kind = Value, E = v.Init }); break;
                case IrGlobal g: if (g.Init != null) AddSrc(g.Symbol, new Src { Kind = Value, E = g.Init }); break;
                case IrParam p: if (p.Default != null) AddSrc(p.Symbol, new Src { Kind = Value, E = p.Default }); break;
                case IrRangeFor r: AddSrc(r.Var, new Src { Kind = Fixed, T = IrType.Int }); break;
                case IrForEach fe:
                    AddSrc(fe.Var, new Src { Kind = ElemOf, E = fe.Iterable });
                    if (fe.ValueVar != null) AddSrc(fe.ValueVar, new Src { Kind = MapValOf, E = fe.Iterable });
                    break;
                case IrAssign a:
                    if (a.Target is IrName tn)
                    {
                        if (a.Op == null) AddSrc(tn.Symbol, new Src { Kind = Value, E = a.Value });
                        else AddSrc(tn.Symbol, new Src { Kind = Compound, E = a.Value, Op = a.Op.Value });
                    }
                    else if (a.Target is IrTupleLit tl)
                    {
                        for (int i = 0; i < tl.Items.Count; i++)
                        {
                            if (!(tl.Items[i] is IrName itn)) continue;
                            if (a.Value is IrTupleLit vl && i < vl.Items.Count) AddSrc(itn.Symbol, new Src { Kind = Value, E = vl.Items[i] });
                            else AddSrc(itn.Symbol, new Src { Kind = TupleItem, E = a.Value, Op = (IrBinOp)i });
                        }
                    }
                    else if (a.Target is IrIndex ix && a.Op == null) AddElem(ix.Target, a.Value, ix.Index, IrTypeKind.Unknown);
                    break;
                case IrOpCall o when o.Args.Count > 0:
                    switch (o.Op)
                    {
                        case IrOp.ListAppend: if (o.Args.Count > 1) AddElem(o.Args[0], o.Args[1], null, IrTypeKind.List); break;
                        case IrOp.ListInsert: if (o.Args.Count > 2) AddElem(o.Args[0], o.Args[2], null, IrTypeKind.List); break;
                        case IrOp.ListSet: if (o.Args.Count > 2) AddElem(o.Args[0], o.Args[2], null, IrTypeKind.List); break;
                        case IrOp.SetAdd: if (o.Args.Count > 1) AddElem(o.Args[0], o.Args[1], null, IrTypeKind.Set); break;
                        case IrOp.MapPut: if (o.Args.Count > 2) AddElem(o.Args[0], o.Args[2], o.Args[1], IrTypeKind.Map); break;
                    }
                    break;
                case IrCall c when c.Kind == IrCallKind.Function && c.Function != null && c.Function.Function != null:
                    for (int i = 0; i < c.Args.Count && i < c.Function.Function.Params.Count; i++)
                        AddSrc(c.Function.Function.Params[i].Symbol, new Src { Kind = Value, E = c.Args[i] });
                    break;
            }
        }

        static bool IsColl(IrType t) => t != null && (t.Kind == IrTypeKind.Map || t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Set);

        static bool Complete(IrType t)
        {
            if (t == null || !t.IsKnown) return false;
            switch (t.Kind)
            {
                case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Array: return Complete(t.Elem);
                case IrTypeKind.Map: return Complete(t.Key) && Complete(t.Elem);
                case IrTypeKind.Tuple: return t.Items != null && t.Items.All(Complete);
            }
            return true;
        }

        void Solve()
        {
            var cands = src.Keys.Concat(elemSrc.Keys).Concat(keySrc.Keys).Concat(uses.Keys).Distinct()
                .Where(s => !Complete(s.Type) || (uses.ContainsKey(s) && IsColl(s.Type))).ToList();
            var fns = retSrc.Keys.ToList();
            if (cands.Count == 0 && fns.Count == 0) return;
            for (int iter = 0; iter < 8; iter++)
            {
                bool changed = false;
                foreach (var s in cands)
                {
                    var t = Infer(s);
                    if (!sym.TryGetValue(s, out var old) || old.ToString() != t.ToString()) { sym[s] = t; changed = true; }
                }
                foreach (var f in fns)
                {
                    IrType t = IrType.Unknown;
                    foreach (var e in retSrc[f]) t = IrType.Join(t, TypeOf(e));
                    if (retSrc[f].Count == 0) t = IrType.Void;
                    if (!ret.TryGetValue(f, out var old) || old.ToString() != t.ToString()) { ret[f] = t; changed = true; }
                }
                foreach (var kv in retUses)
                {
                    var t = FromUses(kv.Value);
                    if (!ret.TryGetValue(kv.Key, out var old) || old.ToString() != t.ToString()) { ret[kv.Key] = t; changed = true; }
                }
                if (!changed) break;
            }
        }

        IrType Infer(IrSymbol s)
        {
            IrType t = s.Type != null && s.Type.IsKnown ? s.Type : IrType.Unknown;
            if (src.TryGetValue(s, out var l))
                foreach (var x in l)
                {
                    IrType st;
                    switch (x.Kind)
                    {
                        case Fixed: st = x.T; break;
                        case ElemOf:
                            {
                                var it = TypeOf(x.E);
                                st = it.Kind == IrTypeKind.Map ? it.Key : it.Kind == IrTypeKind.String ? IrType.String : it.Elem ?? IrType.Unknown;
                                break;
                            }
                        case MapValOf: st = TypeOf(x.E).Elem ?? IrType.Unknown; break;
                        case Compound: st = BinType(x.Op, sym.TryGetValue(s, out var cur) ? cur : IrType.Unknown, TypeOf(x.E)); break;
                        case TupleItem:
                            {
                                var vt = TypeOf(x.E);
                                int i = (int)x.Op;
                                if (vt.IsGame("Position")) st = IrType.Int;
                                else if (vt.IsGame("Companion")) st = i == 0 ? IrType.Game("Entity") : IrType.Game("Position");
                                else if (vt.Kind == IrTypeKind.Tuple && vt.Items != null && i < vt.Items.Count) st = vt.Items[i];
                                else st = IrType.Unknown;
                                break;
                            }
                        default: st = TypeOf(x.E); break;
                    }
                    if (st == null) continue;
                    if (st.Kind == IrTypeKind.Null) sawNull.Add(s);
                    t = IrType.Join(t, st);
                }
            if (uses.TryGetValue(s, out var ul))
            {
                // unknown: typed by its uses; a collection passed where Double elements are expected widens (Map<Item, Double>)
                bool coll = t.Kind == IrTypeKind.Map || t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Set;
                if (!Complete(t) || coll)
                {
                    var ut = FromUses(ul);
                    if (!t.IsKnown) t = ut;
                    else if (t.Kind == ut.Kind) { var j = IrType.Join(t, ut); if (Complete(j) || !Complete(t)) t = j; }
                }
            }
            bool hasElems = elemSrc.ContainsKey(s) || keySrc.ContainsKey(s);
            if (hasElems)
            {
                if (!t.IsKnown && kindHint.TryGetValue(s, out var hk))
                    t = hk == IrTypeKind.Set ? IrType.Set(IrType.Unknown) : hk == IrTypeKind.Map ? IrType.Map(IrType.Unknown, IrType.Unknown) : hk == IrTypeKind.List ? IrType.List(IrType.Unknown) : t;
                if (t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Set || t.Kind == IrTypeKind.Array || t.Kind == IrTypeKind.Map)
                {
                    var elem = t.Elem ?? IrType.Unknown;
                    var key = t.Key ?? IrType.Unknown;
                    if (!Complete(elem) && elemSrc.TryGetValue(s, out var es)) foreach (var e in es) elem = IrType.Join(elem, TypeOf(e));
                    if (t.Kind == IrTypeKind.Map && !Complete(key) && keySrc.TryGetValue(s, out var ks)) foreach (var k in ks) key = IrType.Join(key, TypeOf(k));
                    t = t.Kind == IrTypeKind.Map ? IrType.Map(key, elem) : new IrType { Kind = t.Kind, Elem = elem, Nullable = t.Nullable };
                }
            }
            return t;
        }

        public IrType Sym(IrSymbol s)
        {
            if (s == null) return IrType.Unknown;
            if (sym.TryGetValue(s, out var t) && t.IsKnown && (!Complete(s.Type) || IsColl(s.Type))) return t;
            if (Complete(s.Type)) return s.Type;
            if (s.Kind == IrSymbolKind.Function && funcs.ContainsKey(s)) return IrType.Func(Ret(s));
            return s.Type ?? IrType.Unknown;
        }

        public bool IsBoxed(IrSymbol s)
        {
            if (s == null || !sawNull.Contains(s)) return false;
            var t = Sym(s);
            return t.Kind == IrTypeKind.Int || t.Kind == IrTypeKind.Float || t.Kind == IrTypeKind.Bool;
        }

        public IrType Ret(IrSymbol f)
        {
            if (f == null) return IrType.Unknown;
            if (funcs.TryGetValue(f, out var fn) && fn.ReturnType != null && fn.ReturnType.IsKnown) return fn.ReturnType;
            if (ret.TryGetValue(f, out var t)) return t;
            if (funcs.ContainsKey(f) && retSrc.TryGetValue(f, out var l) && l.Count == 0) return IrType.Void;
            if (f.Type != null && f.Type.Kind == IrTypeKind.Func) return f.Type.Elem ?? IrType.Unknown;
            return f.Type ?? IrType.Unknown;
        }

        static IrType BinType(IrBinOp op, IrType l, IrType r)
        {
            switch (op)
            {
                case IrBinOp.Eq: case IrBinOp.Ne: case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge:
                case IrBinOp.And: case IrBinOp.Or:
                    return IrType.Bool;
                case IrBinOp.Concat: return IrType.String;
                case IrBinOp.TrueDiv: return IrType.Float;
                case IrBinOp.Add:
                    if (l.IsString || r.IsString) return IrType.String;
                    if (l.Kind == IrTypeKind.List && r.Kind == IrTypeKind.List) return IrType.Join(l, r);
                    break;
            }
            if (l.IsNumeric && r.IsNumeric) return l.IsInt && r.IsInt ? IrType.Int : IrType.Float;
            if (!l.IsKnown && r.IsNumeric) return r;
            if (!r.IsKnown && l.IsNumeric) return l;
            return IrType.Join(l, r).Kind == IrTypeKind.Any ? IrType.Unknown : IrType.Join(l, r);
        }

        readonly Dictionary<IrExpr, IrType> memo = new Dictionary<IrExpr, IrType>();
        bool solved;

        public IrType TypeOf(IrExpr e)
        {
            if (e == null) return IrType.Unknown;
            if (solved && memo.TryGetValue(e, out var m)) return m;
            var t = TypeOfCore(e);
            if (solved) memo[e] = t;
            return t;
        }

        IrType TypeOfCore(IrExpr e)
        {
            switch (e)
            {
                case null: return IrType.Unknown;
                case IrName n:
                    if (Complete(n.Type)) return n.Type;
                    return Sym(n.Symbol);
            }
            if (Complete(e.Type)) return e.Type;
            var t = Compute(e);
            if (!t.IsKnown && e.Type != null && e.Type.IsKnown) return e.Type;
            return t;
        }

        IrType Compute(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l:
                    switch (l.Kind)
                    {
                        case IrLitKind.Int: return IrType.Int;
                        case IrLitKind.Float: return IrType.Float;
                        case IrLitKind.Bool: return IrType.Bool;
                        case IrLitKind.String: return IrType.String;
                    }
                    return IrType.Null;
                case IrConst c: return IrType.Game(GameApi.GameTypeOfCategory(c.Category) ?? "Entity");
                case IrUnary u: return u.Op == IrUnOp.Not ? IrType.Bool : TypeOf(u.Operand);
                case IrBinary b: return BinType(b.Op, TypeOf(b.Left), TypeOf(b.Right));
                case IrConditional ce: return IrType.Join(TypeOf(ce.Then), TypeOf(ce.Else));
                case IrCast c: return c.Target ?? IrType.Unknown;
                case IrCall c:
                    switch (c.Kind)
                    {
                        case IrCallKind.Function: return Ret(c.Function);
                        case IrCallKind.Value: { var ft = TypeOf(c.Callee); return ft.Kind == IrTypeKind.Func ? ft.Elem ?? IrType.Unknown : IrType.Unknown; }
                    }
                    if (c.Builtin == "str") return IrType.String;
                    if (c.Builtin == "len") return IrType.Int;
                    var api = GameApi.Find(c.Builtin);
                    if (api == null) return IrType.Unknown;
                    if (api.NumericLikeArgs)
                    {
                        IrType r = IrType.Unknown;
                        foreach (var a in c.Args) r = IrType.Join(r, TypeOf(a));
                        return r.IsNumeric ? r : IrType.Float;
                    }
                    return api.ReturnType ?? IrType.Unknown;
                case IrOpCall o:
                    {
                        var rt = o.Args.Count > 0 ? TypeOf(o.Args[0]) : IrType.Unknown;
                        switch (o.Op)
                        {
                            case IrOp.Len: case IrOp.ListIndexOf: case IrOp.Round: case IrOp.PosX: case IrOp.PosY: return IrType.Int;
                            case IrOp.IsEmpty: case IrOp.Contains: case IrOp.RemoveValueIfPresent: case IrOp.SetAdd: case IrOp.ListAppend: return IrType.Bool;
                            case IrOp.ListPop: case IrOp.MapGetOrNull: case IrOp.MapRemove: case IrOp.MapRemoveIfPresent: return rt.Elem ?? IrType.Unknown;
                            case IrOp.MapGetOrDefault: return IrType.Join(rt.Elem ?? IrType.Unknown, o.Args.Count > 2 ? TypeOf(o.Args[2]) : IrType.Unknown);
                            case IrOp.MapKeys: return IrType.Set(rt.Key ?? IrType.Unknown);
                            case IrOp.MapValues: return IrType.List(rt.Elem ?? IrType.Unknown);
                            case IrOp.ListCopy: return IrType.List(rt.Elem ?? (rt.Kind == IrTypeKind.Map ? rt.Key : IrType.Unknown));
                            case IrOp.SetCopy: return IrType.Set(rt.Elem ?? (rt.Kind == IrTypeKind.Map ? rt.Key : IrType.Unknown));
                            case IrOp.MapCopy: return rt.Kind == IrTypeKind.Map ? rt : IrType.Map(IrType.Unknown, IrType.Unknown);
                            case IrOp.ArrayNew: return IrType.Array(o.Args.Count > 1 ? TypeOf(o.Args[1]) : IrType.Unknown);
                            case IrOp.ArrayNew2: return IrType.Array(IrType.Array(o.Args.Count > 2 ? TypeOf(o.Args[2]) : IrType.Unknown));
                            case IrOp.Floor: case IrOp.Ceil: case IrOp.Sqrt: return IrType.Float;
                            case IrOp.CompanionEntity: return IrType.Game("Entity");
                            case IrOp.CompanionPos: return IrType.Game("Position");
                        }
                        return IrType.Void;
                    }
                case IrIndex ix:
                    {
                        var tt = TypeOf(ix.Target);
                        switch (tt.Kind)
                        {
                            case IrTypeKind.List: case IrTypeKind.Array: case IrTypeKind.Map: return tt.Elem ?? IrType.Unknown;
                            case IrTypeKind.String: return IrType.String;
                            case IrTypeKind.Tuple:
                                if (ix.Index is IrLiteral li && li.Kind == IrLitKind.Int && tt.Items != null && li.Int >= 0 && li.Int < tt.Items.Count) return tt.Items[(int)li.Int];
                                return IrType.Unknown;
                            case IrTypeKind.Game:
                                if (tt.GameName == "Position") return IrType.Int;
                                if (tt.GameName == "Companion" && ix.Index is IrLiteral ci && ci.Kind == IrLitKind.Int) return ci.Int == 0 ? IrType.Game("Entity") : IrType.Game("Position");
                                return IrType.Unknown;
                        }
                        return IrType.Unknown;
                    }
                case IrListLit l:
                    {
                        IrType el = IrType.Unknown;
                        foreach (var i in l.Items) el = IrType.Join(el, TypeOf(i));
                        if (el.Kind == IrTypeKind.Null) el = IrType.Any;
                        return l.IsArray ? IrType.Array(el) : IrType.List(el);
                    }
                case IrSetLit s:
                    {
                        IrType el = IrType.Unknown;
                        foreach (var i in s.Items) el = IrType.Join(el, TypeOf(i));
                        return IrType.Set(el);
                    }
                case IrMapLit m:
                    {
                        IrType k = IrType.Unknown, v = IrType.Unknown;
                        foreach (var i in m.Keys) k = IrType.Join(k, TypeOf(i));
                        foreach (var i in m.Values) v = IrType.Join(v, TypeOf(i));
                        return IrType.Map(k, v);
                    }
                case IrTupleLit t: return IrType.Tuple(t.Items.Select(TypeOf).ToArray());
                case IrLambda lam:
                    {
                        if (lam.ReturnType != null && lam.ReturnType.IsKnown) return IrType.Func(lam.ReturnType);
                        if (lam.BodyExpr != null) return IrType.Func(lam.ReturnsValue ? TypeOf(lam.BodyExpr) : IrType.Void);
                        IrType r = IrType.Void;
                        bool any = false;
                        if (lam.Body != null)
                            foreach (var ret in IrWalk.Descendants(lam.Body, false).OfType<IrReturn>())
                                if (ret.Value != null) { r = any ? IrType.Join(r, TypeOf(ret.Value)) : TypeOf(ret.Value); any = true; }
                        return IrType.Func(r);
                    }
                case IrFuncRef fr:
                    if (fr.Function != null) return IrType.Func(Ret(fr.Function));
                    return IrType.Func(GameApi.Find(fr.Builtin)?.ReturnType ?? IrType.Void);
            }
            return IrType.Unknown;
        }

        // ------------------------------------------------------------------ literal mutability

        static readonly HashSet<IrOp> ReadOps = new HashSet<IrOp>
        {
            IrOp.Len, IrOp.IsEmpty, IrOp.Contains, IrOp.ListIndexOf, IrOp.MapGetOrNull, IrOp.MapGetOrDefault, IrOp.MapKeys,
            IrOp.MapValues, IrOp.ListCopy, IrOp.SetCopy, IrOp.MapCopy,
        };

        void Literals(IrModule m, Dictionary<IrNode, IrNode> parent)
        {
            var unsafeSyms = new HashSet<IrSymbol>();
            foreach (var kvp in parent)
            {
                if (!(kvp.Key is IrName n) || n.Symbol == null) continue;
                var p = kvp.Value;
                bool ok;
                switch (p)
                {
                    case IrOpCall o: ok = o.Args.Count > 0 && o.Args[0] == n ? ReadOps.Contains(o.Op) : true; break;
                    case IrIndex ix: ok = ix.Target == n ? !(parent.TryGetValue(ix, out var pp) && pp is IrAssign pa && pa.Target == ix) : true; break;
                    case IrForEach fe: ok = true; break;
                    case IrBinary _: case IrUnary _: case IrConditional _: case IrCast _: case IrIf _: case IrWhile _: ok = true; break;
                    case IrCall c: ok = c.Kind == IrCallKind.Builtin && (c.Builtin == "print" || c.Builtin == "quick_print" || c.Builtin == "len" || c.Builtin == "str"); break;
                    case IrAssign a: ok = a.Target == n; break;
                    default: ok = false; break;
                }
                if (!ok) unsafeSyms.Add(n.Symbol);
            }
            void Lit(IrSymbol s, IrExpr v)
            {
                if (s != null && !unsafeSyms.Contains(s) && (v is IrListLit || v is IrSetLit || v is IrMapLit)) ImmutableLiterals.Add(v);
            }
            foreach (var n in parent.Keys)
            {
                switch (n)
                {
                    case IrVarDecl v: Lit(v.Symbol, v.Init); break;
                    case IrGlobal g: Lit(g.Symbol, g.Init); break;
                    case IrAssign a when a.Op == null && a.Target is IrName tn: Lit(tn.Symbol, a.Value); break;
                    case IrOpCall o when (o.Op == IrOp.ListCopy || o.Op == IrOp.SetCopy || o.Op == IrOp.MapCopy) && o.Args.Count > 0: ImmutableLiterals.Add(o.Args[0]); break;
                    case IrForEach fe: if (fe.Iterable is IrListLit || fe.Iterable is IrSetLit) ImmutableLiterals.Add(fe.Iterable); break;
                }
            }
        }
    }
}
