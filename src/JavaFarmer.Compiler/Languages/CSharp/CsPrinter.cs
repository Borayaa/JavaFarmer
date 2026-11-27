using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.CSharp
{
    /// <summary>
    /// Farm IR → idiomatic JavaFarmer C# (Allman braces, tabs). Script modules (Python, JS, R, C# top-level) print as
    /// top-level statements with local functions at the end; modules with an entry point (Java, C# class form) as
    /// "class Program { static ... static void Main() }"; modules with only declarations as "public static class File".
    /// Snippet mode prints bare statements and members (docs). Constructs C# cannot express are reported in Problems.
    /// </summary>
    internal sealed class CsPrinter : PrinterBase
    {
        public readonly List<string> Problems = new List<string>();

        public static string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            var p = new CsPrinter(module, options);
            try { p.Module(); }
            catch (Exception ex) { p.Problems.Add("internal printer error: " + ex.Message); problems = p.Problems; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
            problems = p.Problems;
            return p.Result;
        }

        readonly IrModule mod;
        readonly bool fromCs;       // C# source: keep names and literal forms (round trip)
        readonly bool typedSource;  // C# / Java: every local is declared
        readonly bool snakeSource;  // Python / R / C ...: snake_case names become camelCase / PascalCase
        CsTypeGuess types;

        CsPrinter(IrModule module, PrintOptions options) : base(options)
        {
            mod = module ?? new IrModule();
            fromCs = mod.SourceLanguage == "csharp";
            typedSource = fromCs || mod.SourceLanguage == null || mod.SourceLanguage == "java";
            snakeSource = !typedSource && mod.SourceLanguage != "js";
        }

        void Problem(string what) { if (!Problems.Contains(what)) Problems.Add(what); }

        // ================================================================== names
        static readonly HashSet<string> Reserved = new HashSet<string>(CsLexer.Keywords)
        {
            "var", "_", "Console", "Math", "List", "Dictionary", "HashSet", "KeyValuePair", "Action", "Func", "Farm", "String", "Object", "System",
            "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion",
            "Entities", "Grounds", "Items", "Hats", "Unlocks", "Leaderboards", "North", "East", "South", "West", "Random",
            "global", "record", "await", "async", "yield", "nameof", "when", "dynamic", "value",
        };

        /// <summary>Source identifier → legal C# identifier. pascal: functions; else camelCase locals (snake_case sources).</summary>
        string Ident(string name, bool pascal)
        {
            if (string.IsNullOrEmpty(name)) return "x";
            if (name == "_") return "ignored";
            var n = name;
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = "j" + n.Substring(3);
            if (!fromCs)
            {
                bool allCaps = n.Any(char.IsLetter) && !n.Any(char.IsLower);
                if (!allCaps && n.IndexOf('_') > 0)
                {
                    var parts = n.Split('_').Where(p => p.Length > 0).ToList();
                    if (parts.Count > 0)
                    {
                        var sb = new StringBuilder(parts[0]);
                        for (int i = 1; i < parts.Count; i++) sb.Append(char.ToUpperInvariant(parts[i][0])).Append(parts[i].Substring(1));
                        n = sb.ToString();
                    }
                }
                if (pascal && n.Length > 0 && char.IsLower(n[0])) n = char.ToUpperInvariant(n[0]) + n.Substring(1);
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
        readonly HashSet<string> moduleNames = new HashSet<string>();

        void PushScope() => scopes.Add(new Dictionary<string, IrSymbol>());
        void PopScope() { if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1); }

        IrSymbol LiveLocal(string n)
        {
            for (int i = scopes.Count - 1; i >= 0; i--) if (scopes[i].TryGetValue(n, out var s)) return s;
            return null;
        }

        /// <summary>Declares a local / parameter in the current scope (renames on a clash with a live local or a function).</summary>
        string Declare(IrSymbol s)
        {
            if (s == null) return "x";
            var baseName = Ident(s.Name, false);
            if (s.Name == "_")
            {
                // Python's "for _ in range(n)": a C# loop counter
                foreach (var c in new[] { "i", "j", "k", "n" })
                    if (LiveLocal(c) == null && !functionNames.Contains(c) && !moduleNames.Contains(c)) { baseName = c; break; }
                if (baseName == "ignored") baseName = "i";
            }
            var n = baseName;
            int k = 2;
            while (true)
            {
                var live = LiveLocal(n);
                if ((live == null || live == s) && !functionNames.Contains(n)) break;
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
            n = Ident(s.Name, s.Kind == IrSymbolKind.Function || s.Kind == IrSymbolKind.ExternalFunction);
            names[s] = n;
            return n;
        }

        readonly HashSet<string> functionNames = new HashSet<string>();

        /// <summary>
        /// Members of a library window keep their source names: other windows (in any language) call them by those names
        /// (Utils.plant_row() from Python must still find Utils.plant_row). Only made legal C# identifiers.
        /// </summary>
        bool KeepMemberNames => !mod.IsScript && mod.Entry == null && !Options.Snippet;

        string Verbatim(string name)
        {
            if (string.IsNullOrEmpty(name)) return "x";
            var sb = new StringBuilder();
            foreach (var ch in name) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            var n = sb.ToString();
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = "j" + n.Substring(3);
            if (char.IsDigit(n[0])) n = "_" + n;
            return Reserved.Contains(n) ? n + "_" : n;
        }

        void AssignModuleNames()
        {
            foreach (var f in mod.Functions.Concat(undefinedFns.Select(s => new IrFunction { Symbol = s })))
            {
                if (f.Symbol == null) continue;
                string n;
                if (f.IsEntry) n = "Main";
                else
                {
                    var b = KeepMemberNames ? Verbatim(f.Symbol.Name) : Ident(f.Symbol.Name, true);
                    if (CsApi.Find(b) != null || b == "Main" || b == ClassName) b += "_";
                    n = b;
                    int k = 2;
                    while (functionNames.Contains(n)) n = b + k++;
                }
                names[f.Symbol] = n;
                functionNames.Add(n);
            }
            foreach (var s in ModuleVars())
            {
                var b = KeepMemberNames ? Verbatim(s.Name) : Ident(s.Name, false);
                var n = b;
                int k = 2;
                while (moduleNames.Contains(n) || functionNames.Contains(n)) n = b + k++;
                names[s] = n;
                moduleNames.Add(n);
            }
        }

        IEnumerable<IrSymbol> ModuleVars()
        {
            foreach (var g in mod.Globals) if (g.Symbol != null && !IsBuffer(g.Symbol)) yield return g.Symbol;
            foreach (var s in extraFields) yield return s;
            if (!Options.Snippet) foreach (var s in undefinedVars) yield return s;
        }

        static bool IsBuffer(IrSymbol s) => s != null && s.Kind == IrSymbolKind.Global && s.Name == CsBinder.BufferName;

        /// <summary>A library class is named exactly like its window (other windows call it by that name).</summary>
        string ClassName => string.IsNullOrEmpty(Options.FileName) ? "Program" : ModuleName(Options.FileName);

        static string ModuleName(string module)
        {
            if (string.IsNullOrEmpty(module)) return "Other";
            var sb = new StringBuilder();
            foreach (var ch in module) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            var n = sb.ToString();
            if (char.IsDigit(n[0])) n = "_" + n;
            return Reserved.Contains(n) ? n + "_" : n;
        }

        // ------------------------------------------------------------------ undefined names (docs fragments)
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

        void UndefinedFunctions(bool asMembers)
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
                Line((asMembers ? "public static " : "") + TypeText(rt.IsKnown && rt.Kind != IrTypeKind.Null ? rt : IrType.Void) + " " + Name(s) + "(" + string.Join(", ", ps) + ")");
                Line("{");
                if (rt.IsKnown && rt.Kind != IrTypeKind.Void && rt.Kind != IrTypeKind.Null) { Indent(); Line("return " + DefaultValue(rt) + ";"); Dedent(); }
                Line("}");
            }
        }

        // ================================================================== module analysis
        IrBlock mainBlock;
        IrFunction entry;
        readonly List<IrSymbol> extraFields = new List<IrSymbol>();
        readonly HashSet<IrStmt> skipped = new HashSet<IrStmt>();
        readonly Dictionary<IrStmt, List<IrSymbol>> declareBefore = new Dictionary<IrStmt, List<IrSymbol>>();
        readonly HashSet<IrAssign> declaringAssign = new HashSet<IrAssign>();
        readonly HashSet<IrSymbol> headerDeclared = new HashSet<IrSymbol>();
        readonly HashSet<IrSymbol> needsDecl = new HashSet<IrSymbol>();
        readonly HashSet<IrSymbol> unpackDeclared = new HashSet<IrSymbol>();
        readonly HashSet<IrSymbol> declared = new HashSet<IrSymbol>();

        static IrSymbol NameSym(IrExpr e) => e is IrName n ? n.Symbol : null;

        void Analyse()
        {
            types = new CsTypeGuess(mod);
            entry = mod.Entry;
            if (!typedSource) FindUndefined();
            if (mod.IsScript)
            {
                var stmts = new List<IrStmt>();
                var declaredGlobals = new HashSet<IrSymbol>(mod.Globals.Select(g => g.Symbol));
                foreach (var item in mod.Items)
                {
                    if (item is IrGlobal g && g.Symbol != null)
                    {
                        if (IsBuffer(g.Symbol)) continue;
                        stmts.Add(new IrVarDecl { Symbol = g.Symbol, Init = g.ImplicitInit && !fromCs ? null : g.Init, TypeExplicit = g.TypeExplicit, Span = g.Span, Leading = g.Leading, Trailing = g.Trailing, BlankLinesBefore = g.BlankLinesBefore });
                    }
                    else if (item is IrStmt s) stmts.Add(s);
                }
                mainBlock = new IrBlock(stmts);
                // module variables first assigned inside a function (Python "global x"): declared at the top
                var inStmts = new HashSet<IrSymbol>();
                foreach (var s in stmts) foreach (var sym in IrWalk.AssignedSymbols(s)) inStmts.Add(sym);
                foreach (var f in mod.Functions)
                    foreach (var sym in IrWalk.AssignedSymbols(f.Body))
                        if (sym != null && sym.Kind == IrSymbolKind.Global && !declaredGlobals.Contains(sym) && !inStmts.Contains(sym) && !extraFields.Contains(sym) && !IsBuffer(sym))
                            extraFields.Add(sym);
            }
            else
            {
                foreach (var f in mod.Functions)
                    foreach (var sym in IrWalk.AssignedSymbols(f.Body))
                        if (sym != null && sym.Kind == IrSymbolKind.Global && !mod.Globals.Any(g => g.Symbol == sym) && !extraFields.Contains(sym) && !IsBuffer(sym))
                            extraFields.Add(sym);
            }
            if (typedSource)
            {
                foreach (var n in IrWalk.Descendants(mod))
                {
                    if (n is IrForEach fe) { if (fe.Var != null) headerDeclared.Add(fe.Var); if (fe.ValueVar != null) headerDeclared.Add(fe.ValueVar); }
                    else if (n is IrRangeFor rf && rf.Var != null) headerDeclared.Add(rf.Var);
                }
                if (mainBlock != null) MergeBareDecls();
                return;
            }
            MergeBareDecls();
            foreach (var f in mod.Functions) PlanDecls(f.Body, f.Params.Select(p => p.Symbol));
            if (mainBlock != null) PlanDecls(mainBlock, Enumerable.Empty<IrSymbol>());
        }

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

        /// <summary>"T x;" followed by its first (tuple) assignment in the same block: the assignment declares.</summary>
        void MergeBareDecls()
        {
            var roots = mod.Functions.Select(f => f.Body).Concat(mainBlock != null ? new[] { mainBlock } : new IrBlock[0]);
            var blocks = roots.SelectMany(b => new[] { b }.Concat(IrWalk.Descendants(b).OfType<IrBlock>())).Distinct().ToList();
            foreach (var b in blocks)
                for (int i = 0; i < b.Stmts.Count; i++)
                {
                    if (!(b.Stmts[i] is IrVarDecl v) || v.Init != null || v.Symbol == null || v.Trailing != null) continue;
                    var first = b.Stmts.Skip(i + 1).FirstOrDefault(s => Mentions(s, v.Symbol));
                    if (!(first is IrAssign a) || a.Op != null || Mentions(a.Value, v.Symbol)) continue;
                    if (NameSym(a.Target) == v.Symbol) { if (v.Leading == null) { declaringAssign.Add(a); skipped.Add(v); } }
                    else if (a.Target is IrTupleLit tl && Flat(tl).Any(x => NameSym(x) == v.Symbol))
                    { unpackDeclared.Add(v.Symbol); skipped.Add(v); }
                }
        }

        static IEnumerable<IrExpr> Flat(IrTupleLit t)
        {
            foreach (var i in t.Items)
            {
                if (i is IrTupleLit inner) foreach (var x in Flat(inner)) yield return x;
                else yield return i;
            }
        }

        sealed class Ref { public List<(IrBlock B, int I)> Path; public bool Binding; }

        void PlanDecls(IrBlock body, IEnumerable<IrSymbol> parms)
        {
            var decl = new HashSet<IrSymbol>(parms.Where(p => p != null));
            foreach (var n in IrWalk.Descendants(body))
            {
                if (n is IrVarDecl v && v.Symbol != null) decl.Add(v.Symbol);
                if (n is IrParam p && p.Symbol != null) decl.Add(p.Symbol);
            }
            var refs = new Dictionary<IrSymbol, List<Ref>>();
            var path = new List<(IrBlock, int)>();
            bool Candidate(IrSymbol s) => s != null && !decl.Contains(s) && (s.Kind == IrSymbolKind.Local || (s.Kind == IrSymbolKind.Global && body == mainBlock && !mod.Globals.Any(g => g.Symbol == s) && !extraFields.Contains(s) && !undefinedVars.Contains(s)));
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
                        if (a.Target is IrTupleLit t) foreach (var it in Flat(t)) Expr(it); else Expr(a.Target);
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
                bool allInLoops = rs.All(r => r.Path.Any(p => Binds(p.B.Stmts[p.I], s)));
                if (allInLoops) { headerDeclared.Add(s); continue; }
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
                if (st is IrAssign ua && ua.Op == null && ua.Target is IrTupleLit utl && allAfter && !Mentions(ua.Value, s) && Flat(utl).Any(x => NameSym(x) == s))
                {
                    unpackDeclared.Add(s);
                    continue;
                }
                if (!declareBefore.TryGetValue(st, out var l)) declareBefore[st] = l = new List<IrSymbol>();
                l.Add(s);
            }
        }

        static bool Binds(IrStmt s, IrSymbol sym) =>
            (s is IrRangeFor r && r.Var == sym) || (s is IrForEach fe && (fe.Var == sym || fe.ValueVar == sym));

        protected override void Comment(IrComment c)
        {
            if (c != null && c.IsDoc && !c.IsBlock) { Line("///" + (c.Text ?? "")); return; }
            base.Comment(c);
        }

        // ================================================================== module
        void Module()
        {
            Analyse();
            AssignModuleNames();
            Comments(mod.Leading);
            if (mod.IsScript || (Options.Snippet && entry == null)) { TopLevel(); return; }
            if (Options.Snippet)
            {
                // entry module as a snippet: the entry body bare, other members as top-level code
                PushScope();
                foreach (var g in mod.Globals) if (!IsBuffer(g.Symbol)) Field(g, false);
                if (entry != null) { foreach (var p in entry.Params) Declare(p.Symbol); Stmts(entry.Body.Stmts); }
                foreach (var f in mod.Functions) if (!f.IsEntry) { Line(); Method(f, false); }
                PopScope();
                Comments(mod.TrailingComments);
                return;
            }
            bool library = entry == null;
            Line(library ? "public static class " + ClassName : "class Program");
            Line("{");
            Indent();
            int last = 0;
            foreach (var s in undefinedVars)
            {
                Line("// defined elsewhere");
                var ut = SymType(s);
                Line("public static " + TypeText(ut.IsKnown && ut.Kind != IrTypeKind.Null ? ut : IrType.Any) + " " + Name(s) + ";");
                last = 1;
            }
            foreach (var s in extraFields)
            {
                Line((library ? "public " : "") + "static " + TypeText(SymType(s)) + " " + Name(s) + " = " + DefaultValue(SymType(s)) + ";");
                last = 1;
            }
            foreach (var item in mod.Items)
            {
                switch (item)
                {
                    case IrGlobal g:
                        if (g.Symbol == null || IsBuffer(g.Symbol)) break;
                        if (last == 2) Line();
                        Field(g, true, library);
                        last = 1;
                        break;
                    case IrFunction f:
                        if (last != 0) Line();
                        Method(f, true, library);
                        last = 2;
                        break;
                }
            }
            UndefinedFunctions(true);
            Comments(mod.TrailingComments);
            Dedent();
            Line("}");
        }

        void TopLevel()
        {
            PushScope();
            // a lone expression in docs prose (can_harvest(), True) prints as the bare expression
            if (Options.Snippet && mainBlock != null && mod.Functions.Count() == 0 && mainBlock.Stmts.Count == 1 && mainBlock.Stmts[0] is IrExprStmt lone
                && !IsStatementExpr(lone.Expr) && lone.Leading == null && lone.Trailing == null)
            {
                Line(X(lone.Expr).T);
                PopScope();
                return;
            }
            bool any = false;
            if (!Options.Snippet)
                foreach (var s in undefinedVars)
                {
                    if (Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0 || s.Name == "Directions") Problem(s.Name + " cannot be used as a value in C#");
                    Line("// defined elsewhere");
                    var ut = SymType(s);
                    var tt = ut.IsKnown && ut.Kind != IrTypeKind.Null ? ut : IrType.Any;
                    Line(TypeText(tt) + " " + Name(s) + " = " + DefaultValue(tt) + ";");
                    any = true;
                }
            foreach (var s in extraFields)
            {
                Line(TypeText(SymType(s)) + " " + Name(s) + " = " + DefaultValue(SymType(s)) + ";");
                any = true;
            }
            if (any) Line();
            // module variables are top-level locals: the names they got must stay visible to local functions
            foreach (var s in ModuleVars()) scopes[scopes.Count - 1][Name(s)] = s;
            if (mainBlock != null)
            {
                Stmts(mainBlock.Stmts);
                any |= mainBlock.Stmts.Count > 0;
            }
            if (entry != null && !mod.IsScript) { foreach (var p in entry.Params) Declare(p.Symbol); Stmts(entry.Body.Stmts); any = true; }
            foreach (var f in mod.Functions)
            {
                if (f.IsEntry && !mod.IsScript) continue;
                if (any) Line();
                Method(f, false);
                any = true;
            }
            if (!Options.Snippet) UndefinedFunctions(false);
            PopScope();
            Comments(mod.TrailingComments);
        }

        void Field(IrGlobal g, bool member, bool library = false)
        {
            LeadingComments(g);
            var s = g.Symbol;
            var t = SymType(s);
            string mods = "";
            bool isConst = s.IsConst && g.Init is IrLiteral cl && cl.Kind != IrLitKind.Null && (t.Kind == IrTypeKind.Int || t.Kind == IrTypeKind.Float || t.Kind == IrTypeKind.Bool || t.Kind == IrTypeKind.String);
            if (member)
            {
                mods = library && !g.IsPrivate ? "public " : g.IsPrivate && fromCs ? "" : "";
                mods += isConst ? "const " : s.IsConst ? "static readonly " : "static ";
            }
            var init = g.Init != null && !(g.ImplicitInit && !s.IsConst) ? " = " + X(g.Init, t, true).T : member ? "" : " = " + DefaultValue(t);
            Line(mods + TypeText(t) + " " + Name(s) + init + ";", g);
        }

        IrType RetType(IrFunction f)
        {
            if (f.ReturnType != null && f.ReturnType.IsKnown) return f.ReturnType;
            return f.Symbol != null ? types.Ret(f.Symbol) : IrType.Void;
        }

        void Method(IrFunction f, bool member, bool library = false)
        {
            LeadingComments(f);
            PushScope();
            var ps = new List<string>();
            foreach (var p in f.Params)
            {
                var pt = SymType(p.Symbol);
                var text = SymTypeText(p.Symbol, pt) + " " + Declare(p.Symbol);
                if (p.Default != null) text += " = " + X(p.Default, pt).T;
                ps.Add(text);
            }
            var rt = RetType(f);
            if (!rt.IsKnown) rt = IrType.Any;
            var outerReturn = CurrentReturn;
            CurrentReturn = rt;
            string header;
            if (f.IsEntry && member) header = "static void Main(" + string.Join(", ", ps) + ")";
            else
            {
                var mods = member ? (library && !f.IsPrivate ? "public static " : "static ") : "";
                header = mods + TypeText(rt) + " " + Name(f.Symbol) + "(" + string.Join(", ", ps) + ")";
            }
            Line(header, f);
            Line("{");
            Indent();
            Stmts(f.Body.Stmts);
            if (!typedSource && !f.IsEntry && rt.Kind != IrTypeKind.Void && CanComplete(f.Body)) Line("return " + DefaultValue(rt) + ";");
            Comments(f.Body.TrailingComments);
            Dedent();
            PopScope();
            Line("}");
            CurrentReturn = outerReturn;
        }

        // ================================================================== statements
        void Stmts(IList<IrStmt> stmts)
        {
            bool dead = false;
            for (int i = 0; i < stmts.Count; i++)
            {
                var s = stmts[i];
                if (s.BlankLinesBefore > 0 && i > 0 && Out.Length > 0) Line();
                // Console.Write lowering: "buffer += v; print(buffer)" is Console.WriteLine(v)
                if (s is IrAssign ba && ba.Op == IrBinOp.Concat && ba.Target is IrName bt && IsBuffer(bt.Symbol) && i + 1 < stmts.Count
                    && stmts[i + 1] is IrExprStmt pe && pe.Expr is IrCall pc && pc.Kind == IrCallKind.Builtin && pc.Builtin == "print" && pc.Args.Count == 1 && pc.Args[0] is IrName pn && IsBuffer(pn.Symbol))
                {
                    LeadingComments(ba);
                    Line("Console.WriteLine(" + X(ba.Value).T + ");", pe);
                    i++;
                    continue;
                }
                // "T x;" + "x = sel switch { ... }" printed as one declaration
                if (s is IrVarDecl v && v.Init == null && i + 1 < stmts.Count && stmts[i + 1] is IrSwitch nsw && SwitchExprTarget(nsw) is IrSymbol tgt && tgt == v.Symbol && !skipped.Contains(v))
                {
                    LeadingComments(v);
                    var t = SymType(v.Symbol);
                    var text = SwitchExprText(nsw, t);
                    Line((v.TypeExplicit || !typedSource ? TypeText(t) : "var") + " " + Declare(v.Symbol) + " = " + text + ";", nsw);
                    i++;
                    continue;
                }
                if (dead && !typedSource)
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
            Line("{");
            Indent();
            PushScope();
            if (b != null) { Stmts(b.Stmts); Comments(b.TrailingComments); }
            PopScope();
            Dedent();
            Line("}");
        }

        void Stmt(IrStmt s)
        {
            if (s == null || skipped.Contains(s)) return;
            if (declareBefore.TryGetValue(s, out var pre))
                foreach (var sym in pre)
                {
                    var t = SymType(sym);
                    if (types.IsBoxed(sym)) Problem("a number variable that can also be null (int?) is not supported in JavaFarmer");
                    Line(TypeText(t.IsKnown ? t : IrType.Any) + " " + Declare(sym) + " = " + DefaultValue(t) + ";");
                }
            LeadingComments(s);
            switch (s)
            {
                case IrBlock b:
                    Block(b);
                    return;
                case IrVarDecl v: VarDecl(v); return;
                case IrAssign a: Assign(a); return;
                case IrExprStmt es: ExprStmt(es); return;
                case IrIf f: If(f); return;
                case IrWhile w:
                    Line("while (" + Cond(w.Cond).T + ")", w);
                    Block(w.Body);
                    return;
                case IrLoop lp:
                    Line("while (true)", lp);
                    Block(lp.Body);
                    return;
                case IrDoWhile dw:
                    Line("do", dw);
                    Block(dw.Body);
                    TrimLastNewline();
                    Out.Append(" while (" + Cond(dw.Cond).T + ");").Append(Options.Newline);
                    return;
                case IrClassicFor cf: ClassicFor(cf); return;
                case IrRangeFor rf: RangeFor(rf); return;
                case IrForEach fe: ForEach(fe); return;
                case IrSwitch sw: Switch(sw); return;
                case IrBreak _: Line("break;", s); return;
                case IrContinue _: Line("continue;", s); return;
                case IrReturn r:
                    if (r.Value == null) { Line("return;", r); return; }
                    Line("return " + X(r.Value, CurrentReturn, true).T + ";", r);
                    return;
                case IrImport imp:
                    if (Options.Snippet) Problem("import " + imp.Module + " has no C# equivalent (other files are used by their class name)");
                    Line("// " + imp.Module + " is used as " + imp.Module + ".Name(...)", imp);
                    return;
            }
            Problem("unsupported statement " + s.GetType().Name);
            Line("// unsupported statement: " + s.GetType().Name);
        }

        IrType CurrentReturn;

        /// <summary>x = list.pop(i) / x = dict.remove(k): C# reads the element first, then removes it (two statements).</summary>
        bool TakeAndRemove(IrExpr value, Func<string, string> assign, IrNode commentsOf)
        {
            if (!(value is IrOpCall o) || o.Args.Count == 0 || !IsSimple(o.Args[0])) return false;
            var recv = Postfix(o.Args[0]);
            if (o.Op == IrOp.ListPop && (o.Args.Count == 1 || IsSimple(o.Args[1])))
            {
                var at = o.Args.Count == 1 ? "^1" : IndexOf(o.Args[1]);
                Line(assign(recv + "[" + at + "]"), commentsOf);
                Line((o.Args.Count == 1 ? recv + ".RemoveAt(" + recv + ".Count - 1)" : recv + ".RemoveAt(" + at + ")") + ";");
                return true;
            }
            if ((o.Op == IrOp.MapRemove || o.Op == IrOp.MapRemoveIfPresent) && o.Args.Count == 2 && IsSimple(o.Args[1]))
            {
                var key = X(o.Args[1], TypeOf(o.Args[0]).Key).T;
                Line(assign(o.Op == IrOp.MapRemove ? recv + "[" + key + "]" : recv + ".GetValueOrDefault(" + key + ")"), commentsOf);
                Line(recv + ".Remove(" + key + ");");
                return true;
            }
            return false;
        }

        void VarDecl(IrVarDecl v)
        {
            var s = v.Symbol;
            if (v.Init != null && s != null && TakeAndRemove(v.Init, x => DeclTypeText(s, v.Init, true) + " " + Declare(s) + " = " + x + ";", v)) return;
            var t = SymType(s);
            if (s != null && types.IsBoxed(s)) Problem("a number variable that can also be null (int?) is not supported in JavaFarmer");
            string type = DeclTypeText(s, v.Init, v.TypeExplicit);
            if (v.Init == null)
            {
                if (!typedSource || IsBuffer(s)) { Line(type + " " + Declare(s) + " = " + DefaultValue(t) + ";", v); return; }
                Line(type + " " + Declare(s) + ";", v);
                return;
            }
            charItems = CharCollection(s);
            var init = CharOrString(v.Init, IsCharSym(s), type != "var" ? t : null).T;   // before Declare: the initialiser cannot see the new name
            charItems = false;
            bool isConst = s != null && s.IsConst && fromCs && v.Init is IrLiteral && type != "var";
            Line((isConst ? "const " : "") + type + " " + Declare(s) + " = " + init + ";", v);
        }

        /// <summary>var when the source wrote var (or the type is unknown and var can infer it), else the C# type.</summary>
        string DeclTypeText(IrSymbol s, IrExpr init, bool explicitType)
        {
            var t = SymType(s);
            bool collection = init is IrListLit || init is IrSetLit || init is IrMapLit;
            bool varOk = init != null && !(init is IrLiteral l && l.Kind == IrLitKind.Null) && !(init is IrFuncRef)
                         && !(init is IrConditional) && !(init is IrLambda lam && lam.Params.Count > 0) && !(collection && !fromCs)
                         && !(init is IrName n && n.Symbol != null && (n.Symbol.Kind == IrSymbolKind.Function || n.Symbol.Kind == IrSymbolKind.ExternalFunction));
            if (varOk && ((!explicitType && fromCs) || !t.IsKnown || t.Kind == IrTypeKind.Unknown)) return "var";
            return SymTypeText(s, t);
        }

        static bool IsEmptyCollection(IrExpr e) =>
            (e is IrListLit l && l.Items.Count == 0) || (e is IrSetLit s && s.Items.Count == 0) || (e is IrMapLit m && m.Keys.Count == 0);

        void ExprStmt(IrExprStmt es)
        {
            var e = es.Expr;
            if (e is IrOpCall pop && pop.Op == IrOp.ListPop)
            {
                // statement pop(): RemoveAt
                Line(PopStatement(pop) + ";", es);
                return;
            }
            statementOp = e as IrOpCall;
            var x = X(e);
            statementOp = null;
            if (!IsStatementExpr(e))
            {
                Problem("an expression statement without effect has no C# equivalent");
                Line("// " + x.T + ";", es);
                return;
            }
            Line(x.T + ";", es);
        }

        string PopStatement(IrOpCall o)
        {
            var recv = Postfix(o.Args[0]);
            if (o.Args.Count >= 2) return recv + ".RemoveAt(" + IndexOf(o.Args[1]) + ")";
            if (!IsSimple(o.Args[0])) Problem("pop() on a computed list");
            return recv + ".RemoveAt(" + recv + ".Count - 1)";
        }

        bool IsStatementExpr(IrExpr e)
        {
            switch (e)
            {
                case IrCall _: return true;
                case IrOpCall o:
                    switch (o.Op)
                    {
                        case IrOp.ListAppend: case IrOp.ListInsert: case IrOp.ListPop: case IrOp.ListSet: case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent:
                        case IrOp.Clear: case IrOp.SetAdd: case IrOp.MapPut: case IrOp.MapRemove: case IrOp.MapRemoveIfPresent:
                            return true;
                    }
                    return false;
            }
            return false;
        }

        void If(IrIf f)
        {
            Line("if (" + Cond(f.Cond).T + ")", f);
            Block(f.Then);
            var tail = f.Else;
            while (tail is IrIf ei)
            {
                LeadingComments(ei);
                Line("else if (" + Cond(ei.Cond).T + ")", ei);
                Block(ei.Then);
                tail = ei.Else;
            }
            if (tail != null)
            {
                Line("else", tail);
                if (tail is IrBlock eb) Block(eb);
                else { Line("{"); Indent(); Stmt(tail); Dedent(); Line("}"); }
            }
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
                var type = DeclTypeText(decls[0].Symbol, decls[0].Init, true);
                init = type + " " + string.Join(", ", decls.Select(d => { var iv = d.Init != null ? " = " + X(d.Init, SymType(d.Symbol)).T : ""; return Declare(d.Symbol) + iv; }));
            }
            else init = string.Join(", ", f.Init.Select(SimpleStmtText));
            var cond = f.Cond != null ? Cond(f.Cond).T : "";
            var upd = string.Join(", ", f.Update.Select(SimpleStmtText));
            Line("for (" + init + "; " + cond + "; " + upd + ")", f);
            Block(f.Body);
            PopScope();
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
            if (!typedSource && HasCall(rf.End))
            {
                var tmp = new IrSymbol("end", IrSymbolKind.Local, IrType.Int);
                var tn = Declare(tmp);
                Line("int " + tn + " = " + end.T + ";");
                end = new Ex(tn, PrecAtom);
            }
            string v;
            bool declareHere = rf.Var == null || headerDeclared.Contains(rf.Var);
            v = declareHere ? Declare(rf.Var) : Name(rf.Var);
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
            Line("for (" + decl + " = " + start + "; " + cond + "; " + upd + ")", rf);
            Block(rf.Body);
            PopScope();
        }

        static bool HasCall(IrExpr e) => IrWalk.Descendants(e).Any(n => (n is IrCall c && !(c.Kind == IrCallKind.Builtin && (GameApi.Find(c.Builtin)?.LoopInvariant ?? false))));

        void ForEach(IrForEach fe)
        {
            PushScope();
            var it = fe.Iterable;
            var itT = TypeOf(it);
            string src;
            IrType elem;
            bool overMap = false;
            IrExpr mapExpr = null;
            if (it is IrOpCall mk && mk.Op == IrOp.MapKeys)
            {
                mapExpr = mk.Args[0];
                src = fe.ValueVar != null ? X(mapExpr).T : X(it).T;
                elem = TypeOf(mk.Args[0]).Key ?? IrType.Unknown;
                overMap = true;
            }
            else if (itT.Kind == IrTypeKind.Map)
            {
                mapExpr = it;
                src = fe.ValueVar != null ? X(it).T : Postfix(it) + ".Keys";
                elem = itT.Key ?? IrType.Unknown;
                overMap = true;
            }
            else
            {
                src = X(it).T;
                elem = itT.Kind == IrTypeKind.String ? IrType.String : itT.Elem ?? IrType.Unknown;
            }
            if (fe.Var != null && SymType(fe.Var).IsKnown && !(itT.Kind == IrTypeKind.String)) elem = SymType(fe.Var);
            bool own = fe.Var == null || headerDeclared.Contains(fe.Var) || typedSource;
            // for a, b in pairs (Python) / foreach (var (a, b) in pairs): the body starts with "a, b = <it>"
            if (fe.Var != null && Equals(fe.Var.Tag, PyLowering.TupleForTag) && fe.Body.Stmts.Count > 0 && fe.Body.Stmts[0] is IrAssign ua && ua.Target is IrTupleLit utl
                && ua.Value is IrName un && un.Symbol == fe.Var && Flat(utl).All(x => x is IrName))
            {
                foreach (var x in Flat(utl)) declared.Add(NameSym(x));
                Line("foreach (var " + DeclTupleText(utl) + " in " + src + ")", fe);
                Line("{");
                Indent();
                PushScope();
                Stmts(fe.Body.Stmts.Skip(1).ToList());
                Comments(fe.Body.TrailingComments);
                PopScope();
                Dedent();
                Line("}");
                PopScope();
                return;
            }
            if (overMap && fe.ValueVar != null)
            {
                if (!own || !(headerDeclared.Contains(fe.ValueVar) || typedSource)) Problem("for-each over a map with outer loop variables");
                var k = Declare(fe.Var);
                var vv = Declare(fe.ValueVar);
                Line("foreach (var (" + k + ", " + vv + ") in " + src + ")", fe);
                Block(fe.Body);
                PopScope();
                return;
            }
            string type = itT.Kind == IrTypeKind.String ? "char" : elem.IsKnown && !(fromCs) ? TypeText(elem) : "var";
            if (itT.Kind == IrTypeKind.String && !IsCharSym(fe.Var)) Problem("iterating over the characters of a string gives char values in C#");
            string v = own ? Declare(fe.Var) : Declare(new IrSymbol(fe.Var.Name + "Item", IrSymbolKind.Local, elem));
            Line("foreach (" + type + " " + v + " in " + src + ")", fe);
            Line("{");
            Indent();
            PushScope();
            if (!own) Line(Name(fe.Var) + " = " + v + ";");
            Stmts(fe.Body.Stmts);
            Comments(fe.Body.TrailingComments);
            PopScope();
            Dedent();
            Line("}");
            PopScope();
        }

        // ------------------------------------------------------------------ switch
        /// <summary>The symbol every case of the switch assigns (switch expression form), or null.</summary>
        IrSymbol SwitchExprTarget(IrSwitch sw)
        {
            if (!SwitchLabelsOk(sw) || sw.Cases.Count == 0) return null;
            IrSymbol target = null;
            foreach (var c in sw.Cases)
            {
                if (c.Body == null || c.Body.Stmts.Count != 1 || c.Leading != null) return null;
                if (!(c.Body.Stmts[0] is IrAssign a) || a.Op != null || !(a.Target is IrName n) || n.Symbol == null || a.Leading != null || a.Trailing != null) return null;
                if (target == null) target = n.Symbol;
                else if (target != n.Symbol) return null;
                if (Mentions(a.Value, target)) return null;
            }
            return target;
        }

        bool SwitchReturns(IrSwitch sw)
        {
            if (!SwitchLabelsOk(sw) || sw.Cases.Count == 0) return false;
            return sw.Cases.All(c => c.Body != null && c.Body.Stmts.Count == 1 && c.Leading == null && c.Body.Stmts[0] is IrReturn r && r.Value != null && r.Leading == null && r.Trailing == null);
        }

        /// <summary>C# case labels must be constants: ints, strings, bools, Direction members (not Entities.X).</summary>
        bool SwitchLabelsOk(IrSwitch sw)
        {
            foreach (var c in sw.Cases)
                foreach (var l in c.Labels)
                {
                    var x = l is IrUnary u && u.Op == IrUnOp.Neg ? u.Operand : l;
                    if (x is IrLiteral lit && lit.Kind != IrLitKind.Null && lit.Kind != IrLitKind.Float) continue;
                    if (x is IrConst k && k.Category == "Directions") continue;
                    if (x is IrName n && n.Symbol != null && n.Symbol.IsConst && fromCs) continue;
                    return false;
                }
            var st = TypeOf(sw.Selector);
            return !(st.Kind == IrTypeKind.Float);
        }

        string SwitchExprText(IrSwitch sw, IrType valueType)
        {
            var selT = TypeOf(sw.Selector);
            var sb = new StringBuilder();
            sb.Append(Postfix(sw.Selector)).Append(" switch").Append(Options.Newline);
            sb.Append(IndentText).Append("{").Append(Options.Newline);
            Depth++;
            bool hasDefault = false;
            foreach (var c in sw.Cases)
            {
                var stmt = c.Body.Stmts[0];
                var value = stmt is IrAssign a ? a.Value : ((IrReturn)stmt).Value;
                string label = c.IsDefault ? "_" : string.Join(" or ", c.Labels.Select(l => CharOrString(l, IsCharExpr(sw.Selector), selT).T));
                if (c.IsDefault) hasDefault = true;
                sb.Append(IndentText).Append(label).Append(" => ").Append(X(value, valueType).T).Append(",").Append(Options.Newline);
            }
            Depth--;
            sb.Append(IndentText).Append("}");
            if (!hasDefault && !fromCs) Problem("a switch without a default case");
            return sb.ToString();
        }

        void Switch(IrSwitch sw)
        {
            if (SwitchExprTarget(sw) is IrSymbol target && (fromCs || sw.Cases.Any(c => c.IsDefault)) && sw.Style == IrSwitchStyle.Arrow)
            {
                Line(Name(target) + " = " + SwitchExprText(sw, SymType(target)) + ";", sw);
                return;
            }
            if (SwitchReturns(sw) && sw.Style == IrSwitchStyle.Arrow && fromCs)
            {
                Line("return " + SwitchExprText(sw, CurrentReturn) + ";", sw);
                return;
            }
            bool loopBreak = sw.Cases.Any(c => BreaksOut(c.Body));
            if (loopBreak || !SwitchLabelsOk(sw)) { SwitchAsIf(sw); return; }
            var selT = TypeOf(sw.Selector);
            Line("switch (" + X(sw.Selector).T + ")", sw);
            Line("{");
            Indent();
            foreach (var c in sw.Cases)
            {
                LeadingComments(c);
                if (c.IsDefault) Line("default:");
                else foreach (var l in c.Labels) Line("case " + CharOrString(l, IsCharExpr(sw.Selector), selT).T + ":");
                Indent();
                var body = c.Body;
                PushScope();
                if (body != null)
                {
                    var stmts = body.Stmts.Count == 1 && body.Stmts[0] is IrBlock only && only.Leading == null ? only.Stmts : body.Stmts;
                    Stmts(stmts);
                    Comments(body.TrailingComments);
                    if (CanComplete(new IrBlock(stmts))) Line("break;");
                }
                else Line("break;");
                PopScope();
                Dedent();
            }
            Dedent();
            Line("}");
        }

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
                var selExpr = IsSimple(sw.Selector) ? sw.Selector : null;
                var conds = c.Labels.Select(l => EqText(new Ex(sel, PrecAtom), selT, X(l, selT), TypeOf(l), false, selExpr, l)).ToList();
                var cond = string.Join(" || ", conds.Select(x => conds.Count > 1 ? Wrap(x.T, x.P, PrecOr + 1) : x.T));
                LeadingComments(c);
                Line((first ? "if (" : "else if (") + cond + ")", sw);
                Block(c.Body);
                first = false;
            }
            if (dflt != null)
            {
                if (first) { Block(dflt.Body); return; }
                Line("else");
                Block(dflt.Body);
            }
        }

        // ------------------------------------------------------------------ assignments
        void Assign(IrAssign a)
        {
            if (a.Target is IrName bn && IsBuffer(bn.Symbol))
            {
                // Console.Write lowering: buffer += x / buffer = ""
                if (a.Op == IrBinOp.Concat) Line("Console.Write(" + X(a.Value).T + ");", a);
                return;
            }
            if (a.Target is IrTupleLit tl) { Unpack(tl, a); return; }
            if (a.Op == null && !(a.Target is IrTupleLit) && TakeAndRemove(a.Value, x => (declaringAssign.Contains(a) ? DeclTypeText(NameSym(a.Target), a.Value, true) + " " + Declare(NameSym(a.Target)) : TargetText(a.Target)) + " = " + x + ";", a)) return;
            if (declaringAssign.Contains(a))
            {
                var s = NameSym(a.Target);
                var t = SymType(s);
                var type = DeclTypeText(s, a.Value, true);
                var val = X(a.Value, t, type != "var").T;
                Line(type + " " + Declare(s) + " = " + val + ";", a);
                return;
            }
            var text = AssignText(a);
            if (text == null) { Line("// unsupported assignment", a); return; }
            Line(text + ";", a);
        }

        string AssignText(IrAssign a)
        {
            var target = a.Target;
            var tt = TypeOf(target);
            var tx = TargetText(target);
            if (a.Op == null)
            {
                charItems = target is IrName cn && CharCollection(cn.Symbol);
                var v = CharOrString(a.Value, IsCharExpr(target), tt).T;
                charItems = false;
                return tx + " = " + v;
            }
            var op = a.Op.Value;
            bool lit1 = a.Value is IrLiteral l1 && l1.Kind == IrLitKind.Int && l1.Int == 1;
            if (a.IsIncDec && lit1 && (op == IrBinOp.Add || op == IrBinOp.Sub))
            {
                var pp = op == IrBinOp.Add ? "++" : "--";
                return a.Prefix ? pp + tx : tx + pp;
            }
            bool intTarget = tt.Kind == IrTypeKind.Int;
            if (a.TruncateToInt || (intTarget && !IsInt(a.Value) && TypeOf(a.Value).IsNumeric && (op == IrBinOp.Add || op == IrBinOp.Sub || op == IrBinOp.Mul || op == IrBinOp.TrueDiv || op == IrBinOp.ModTrunc)))
            {
                if (!(target is IrName) && !IsSimpleTarget(target)) Problem("compound assignment on a computed target");
                var r = BinText(op, target, a.Value, IrType.Float);
                return tx + " = (int)" + Wrap(r.T, r.P, PrecAtom);
            }
            switch (op)
            {
                case IrBinOp.Add: case IrBinOp.Concat: return tx + " += " + X(a.Value, op == IrBinOp.Concat ? null : tt).T;
                case IrBinOp.Sub: return tx + " -= " + X(a.Value, tt).T;
                case IrBinOp.Mul: return tx + " *= " + X(a.Value, tt).T;
                case IrBinOp.ModTrunc: return tx + " %= " + X(a.Value, tt).T;
                case IrBinOp.IntDivTrunc:
                    if (intTarget && IsInt(a.Value)) return tx + " /= " + X(a.Value, tt).T;
                    break;
                case IrBinOp.TrueDiv:
                    if (intTarget && IsInt(a.Value)) Problem("x /= y on an int computes a fraction in Python but truncates in C#");
                    if (!intTarget) return tx + " /= " + X(a.Value, tt).T;
                    break;
            }
            if (!(target is IrName) && !IsSimpleTarget(target)) Problem("compound assignment on a computed target");
            var rhs = BinText(op, target, a.Value, tt);
            return tx + " = " + rhs.T;
        }

        static bool IsSimpleTarget(IrExpr t) => t is IrName || (t is IrIndex ix && IsSimple(ix.Target) && IsSimple(ix.Index));

        string TargetText(IrExpr t)
        {
            if (t is IrName n) return NameRef(n);
            return X(t).T;
        }

        void Unpack(IrTupleLit targets, IrAssign a)
        {
            if (a.Op != null) { Problem("compound assignment to several variables"); Line("// unsupported compound tuple assignment", a); return; }
            var flat = Flat(targets).ToList();
            bool declares = flat.Any(t => NameSym(t) is IrSymbol s && unpackDeclared.Contains(s) && !declared.Contains(s));
            if (declares)
            {
                if (!flat.All(t => NameSym(t) is IrSymbol s && unpackDeclared.Contains(s) && !declared.Contains(s)))
                {
                    // mixed: declare the new ones first
                    foreach (var t in flat)
                        if (NameSym(t) is IrSymbol s && unpackDeclared.Contains(s) && !declared.Contains(s))
                        {
                            declared.Add(s);
                            var st = SymType(s);
                            Line(TypeText(st.IsKnown ? st : IrType.Any) + " " + Declare(s) + " = " + DefaultValue(st) + ";");
                        }
                    Line(TupleTargetText(targets) + " = " + X(a.Value).T + ";", a);
                    return;
                }
                var value = DeconstructSource(a.Value, targets);
                foreach (var t in flat) declared.Add(NameSym(t));
                Line("var " + DeclTupleText(targets) + " = " + value + ";", a);
                return;
            }
            Line(TupleTargetText(targets) + " = " + DeconstructSource(a.Value, targets) + ";", a);
        }

        /// <summary>The value of a deconstruction; an untyped value (Measure()) of two parts is cast to Position.</summary>
        string DeconstructSource(IrExpr value, IrTupleLit targets)
        {
            var vt = TypeOf(value);
            if (value is IrTupleLit || vt.Kind == IrTypeKind.Tuple || vt.IsGame("Position") || vt.IsGame("Companion")) return X(value).T;
            if ((vt.Kind == IrTypeKind.Any || !vt.IsKnown) && targets.Items.Count == 2)
            {
                bool companion = targets.Items[1] is IrTupleLit;
                return "(" + (companion ? "Companion" : "Position") + ")" + W(X(value), PrecUnary);
            }
            Problem("unpacking a value that is not a tuple");
            return X(value).T;
        }

        string DeclTupleText(IrTupleLit t) => "(" + string.Join(", ", t.Items.Select(i => i is IrTupleLit inner ? DeclTupleText(inner) : Declare(NameSym(i)))) + ")";
        string TupleTargetText(IrTupleLit t) => "(" + string.Join(", ", t.Items.Select(i => i is IrTupleLit inner ? TupleTargetText(inner) : TargetText(i))) + ")";

        // ================================================================== expressions
        struct Ex
        {
            public string T; public int P;
            public Ex(string t, int p) { T = t; P = p; }
        }

        const int PrecOr = 1, PrecAnd = 2, PrecEq = 3, PrecRel = 4, PrecAdd = 5, PrecMul = 6;

        IrType TypeOf(IrExpr e) => types.TypeOf(e);
        static string TagType(IrSymbol s) => s?.Tag is string t && t.StartsWith("csharp:type:", StringComparison.Ordinal) ? t.Substring(12) : null;
        static bool IsCharSym(IrSymbol s) => TagType(s) == "char";
        /// <summary>A C# char value: a char variable, a character of a string (s[i]), an element of a char collection.</summary>
        bool IsCharExpr(IrExpr e)
        {
            if (e is IrName n) return IsCharSym(n.Symbol);
            if (e is IrIndex ix)
            {
                if (TypeOf(ix.Target).IsString) return true;
                var tt = ix.Target is IrName tn ? TagType(tn.Symbol) : null;
                return tt == "char[]" || tt == "List<char>" || tt == "char[,]" && false;
            }
            return false;
        }
        static bool CharCollection(IrSymbol s) { var t = TagType(s); return t == "char[]" || t == "List<char>" || t == "HashSet<char>"; }
        /// <summary>Set while printing the initializer of a char collection: one-letter string literals print as char literals.</summary>
        bool charItems;
        Ex CharOrString(IrExpr value, bool wantChar, IrType expected)
        {
            if (wantChar && value is IrLiteral l && l.Kind == IrLitKind.String && (l.Str ?? "").Length == 1) return new Ex("'" + Escape(l.Str, '\'') + "'", PrecAtom);
            if (!wantChar && expected != null && expected.IsString && IsCharExpr(value)) return new Ex(Postfix(value) + ".ToString()", PrecPostfix);
            return X(value, expected, true);
        }
        string SymTypeText(IrSymbol s, IrType t) => TagType(s) ?? TypeText(t.IsKnown ? t : IrType.Any);
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
                case IrSymbolKind.ExternalGlobal: return ModuleName(s.Module) + "." + Verbatim(s.Name);
                case IrSymbolKind.Function: return Name(s);
                case IrSymbolKind.ExternalFunction: return ModuleName(s.Module) + "." + Verbatim(s.Name);
            }
            return Name(s);
        }

        Ex X(IrExpr e, IrType expected = null, bool targetTyped = false)
        {
            switch (e)
            {
                case null: return new Ex("null", PrecAtom);
                case IrLiteral l: return Literal(l, expected != null && expected.Kind == IrTypeKind.Float && !fromCs);
                case IrName n: return new Ex(NameRef(n), PrecAtom);
                case IrConst c: return new Ex(c.Category == null || c.Category == "Directions" ? c.Name : c.Category + "." + c.Name, PrecPostfix);
                case IrUnary u:
                    {
                        if (u.Op == IrUnOp.Not)
                        {
                            var ot = TypeOf(u.Operand);
                            if (!typedSource && ot.Kind != IrTypeKind.Bool && ot.IsKnown && ot.Kind != IrTypeKind.Any)
                            {
                                var ox = X(u.Operand);
                                switch (ot.Kind)
                                {
                                    case IrTypeKind.Int: case IrTypeKind.Float: return new Ex(W(ox, PrecRel + 1) + " == 0", PrecEq);
                                    case IrTypeKind.String: return new Ex(W(ox, PrecPostfix) + ".Length == 0", PrecEq);
                                    case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Map: return new Ex(W(ox, PrecPostfix) + ".Count == 0", PrecEq);
                                    case IrTypeKind.Array: return new Ex(W(ox, PrecPostfix) + ".Length == 0", PrecEq);
                                    default: return new Ex(W(ox, PrecRel + 1) + " == null", PrecEq);
                                }
                            }
                            if (u.Operand is IrOpCall empty && empty.Op == IrOp.IsEmpty && empty.Args.Count == 1)
                            {
                                var et = TypeOf(empty.Args[0]);
                                return new Ex(Postfix(empty.Args[0]) + (et.Kind == IrTypeKind.Array || et.Kind == IrTypeKind.String ? ".Length" : ".Count") + " > 0", PrecRel);
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
                        var a = X(ce.Then, expected, targetTyped);
                        var z = X(ce.Else, expected, targetTyped);
                        return new Ex(W(c, PrecOr) + " ? " + W(a, PrecOr) + " : " + W(z, PrecTernary), PrecTernary);
                    }
                case IrCall call: return Call(call);
                case IrOpCall op: return Op(op, expected);
                case IrIndex ix: return Index(ix);
                case IrListLit ll: return ListLit(ll, expected, targetTyped);
                case IrSetLit sl: return SetLit(sl, expected, targetTyped);
                case IrMapLit ml: return MapLit(ml, expected, targetTyped);
                case IrTupleLit tl:
                    {
                        var exp = expected != null && expected.Kind == IrTypeKind.Tuple ? expected.Items : null;
                        return new Ex("(" + string.Join(", ", tl.Items.Select((i, k) => X(i, exp != null && k < exp.Count ? exp[k] : null).T)) + ")", PrecAtom);
                    }
                case IrLambda lam: return Lambda(lam, expected);
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
                    if (asDouble || (l.Type != null && l.Type.Kind == IrTypeKind.Float)) return new Ex(FormatFloat(l.Int), l.Int < 0 ? PrecUnary : PrecAtom);
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
                        if (s.Length == 1 && (charLiterals.Contains(l) || charItems)) return new Ex("'" + Escape(s, '\'') + "'", PrecAtom);
                        return new Ex("\"" + Escape(s) + "\"", PrecAtom);
                    }
            }
        }

        /// <summary>String literals compared with a char (s[i] == 'a'): printed as char literals.</summary>
        readonly HashSet<IrLiteral> charLiterals = new HashSet<IrLiteral>();

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
            if (typedSource) return x;
            switch (t.Kind)
            {
                case IrTypeKind.Int: case IrTypeKind.Float: return new Ex(W(x, PrecRel + 1) + " != 0", PrecEq);
                case IrTypeKind.String: return new Ex(W(x, PrecPostfix) + ".Length > 0", PrecRel);
                case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Map: return new Ex(W(x, PrecPostfix) + ".Count > 0", PrecRel);
                case IrTypeKind.Array: return new Ex(W(x, PrecPostfix) + ".Length > 0", PrecRel);
                case IrTypeKind.Game: case IrTypeKind.Tuple: case IrTypeKind.Func: case IrTypeKind.Any: return new Ex(W(x, PrecRel + 1) + " != null", PrecEq);
            }
            return x;
        }

        Ex Bin(string op, int prec, IrExpr l, IrExpr r, Ex? lx = null, Ex? rx = null)
        {
            var a = lx ?? X(l); var b = rx ?? X(r);
            return new Ex(W(a, prec) + " " + op + " " + W(b, prec + 1), prec);
        }

        /// <summary>An object-typed operand (Measure(), WaitFor()) next to a number: cast it so C# can compare.</summary>
        Ex NumOperand(IrExpr e, IrExpr other)
        {
            var t = TypeOf(e);
            var x = X(e);
            if ((t.Kind == IrTypeKind.Any || (t.Kind == IrTypeKind.Unknown && !typedSource)) && TypeOf(other).IsNumeric && !(e is IrLiteral))
                return new Ex("(double)" + W(x, PrecUnary), PrecUnary);
            return x;
        }

        Ex BinText(IrBinOp op, IrExpr l, IrExpr r, IrType resultType)
        {
            bool ints = IsInt(l) && IsInt(r);
            switch (op)
            {
                case IrBinOp.Add:
                    if (IsStr(l) || IsStr(r)) return Concat(l, r);
                    if (TypeOf(l).Kind == IrTypeKind.List || TypeOf(r).Kind == IrTypeKind.List) Problem("adding lists with + is not supported in C#");
                    return Bin("+", PrecAdd, l, r, NumOperand(l, r), NumOperand(r, l));
                case IrBinOp.Concat: return Concat(l, r);
                case IrBinOp.Sub: return Bin("-", PrecAdd, l, r, NumOperand(l, r), NumOperand(r, l));
                case IrBinOp.Mul:
                    if (IsStr(l) || IsStr(r) || TypeOf(l).Kind == IrTypeKind.List || TypeOf(r).Kind == IrTypeKind.List) Problem("repeating a string or list with * is not supported in C#");
                    return Bin("*", PrecMul, l, r, NumOperand(l, r), NumOperand(r, l));
                case IrBinOp.TrueDiv:
                    if (ints) return new Ex("(double)" + W(X(l), PrecUnary) + " / " + W(X(r), PrecMul + 1), PrecMul);
                    return Bin("/", PrecMul, l, r, NumOperand(l, r), NumOperand(r, l));
                case IrBinOp.IntDivTrunc:
                    if (ints || typedSource) return Bin("/", PrecMul, l, r);
                    return new Ex("(int)(" + Bin("/", PrecMul, l, r).T + ")", PrecUnary);
                case IrBinOp.IntDivFloor:
                    if (ints && IsNonNeg(l) && IsNonNeg(r)) return Bin("/", PrecMul, l, r);
                    if (ints) return new Ex("(int)Math.Floor((double)" + W(X(l), PrecUnary) + " / " + W(X(r), PrecMul + 1) + ")", PrecUnary);
                    return new Ex("Math.Floor(" + Bin("/", PrecMul, l, r).T + ")", PrecPostfix);
                case IrBinOp.ModTrunc: return Bin("%", PrecMul, l, r);
                case IrBinOp.ModFloor:
                    if (IsNonNeg(l) || !ints && IsNonNeg(r) && false) return Bin("%", PrecMul, l, r);
                    if (r is IrLiteral rl && (rl.Kind == IrLitKind.Int && rl.Int > 0) && IsNonNegLoose(l)) return Bin("%", PrecMul, l, r);
                    {
                        var rx = X(r);
                        if (!IsSimple(r)) Problem("modulo of a possibly negative number by a computed divisor");
                        var inner = Bin("%", PrecMul, l, r);
                        return new Ex("(" + inner.T + " + " + W(rx, PrecAdd + 1) + ") % " + W(rx, PrecMul + 1), PrecMul);
                    }
                case IrBinOp.Pow:
                    {
                        var call = "Math.Pow(" + X(l).T + ", " + X(r).T + ")";
                        bool intResult = (resultType != null && resultType.Kind == IrTypeKind.Int) || ((resultType == null || !resultType.IsKnown) && ints && !typedSource);
                        return intResult ? new Ex("(int)" + call, PrecUnary) : new Ex(call, PrecPostfix);
                    }
                case IrBinOp.Eq: case IrBinOp.Ne:
                    return EqText(NumOperand(l, r), TypeOf(l), NumOperand(r, l), TypeOf(r), op == IrBinOp.Ne, l, r);
                case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge:
                    {
                        if (IsStr(l) && IsStr(r)) Problem("comparing strings with < > is not supported in C#");
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

        Ex EqText(Ex a, IrType at, Ex b, IrType bt, bool ne, IrExpr l = null, IrExpr r = null)
        {
            // s[i] == "a": a char compared with a one-letter string literal
            if (l != null && r != null)
            {
                bool lc = IsCharExpr(l), rc = IsCharExpr(r);
                if (lc && r is IrLiteral rl && rl.Kind == IrLitKind.String && (rl.Str ?? "").Length == 1) { charLiterals.Add(rl); b = X(r); }
                else if (rc && l is IrLiteral ll && ll.Kind == IrLitKind.String && (ll.Str ?? "").Length == 1) { charLiterals.Add(ll); a = X(l); }
                else if (lc && !rc && TypeOf(r).IsString) a = new Ex(W(a, PrecPostfix) + ".ToString()", PrecPostfix);
                else if (rc && !lc && TypeOf(l).IsString) b = new Ex(W(b, PrecPostfix) + ".ToString()", PrecPostfix);
            }
            if ((at.IsCollection && bt.Kind != IrTypeKind.Null) || (bt.IsCollection && at.Kind != IrTypeKind.Null)) Problem("comparing collections with == compares references in C#");
            return new Ex(W(a, PrecEq) + (ne ? " != " : " == ") + W(b, PrecEq + 1), PrecEq);
        }

        Ex Concat(IrExpr l, IrExpr r)
        {
            // str(x) is implicit next to a string: drop it on one side at most (x.ToString() + y.ToString() keeps the first)
            bool lDrop = Droppable(l), rDrop = Droppable(r);
            if (lDrop && rDrop) lDrop = false;
            else if (lDrop && !IsStr(r)) lDrop = false;
            else if (rDrop && !IsStr(l)) rDrop = false;
            var a = lDrop ? ConcatPart(l) : X(l); var b = rDrop ? ConcatPart(r) : X(r);
            if (TypeOf(l).Kind == IrTypeKind.Func || TypeOf(r).Kind == IrTypeKind.Func) Problem("printing a function is not supported in JavaFarmer");
            if (!IsStr(l) && !IsStr(r))
                return new Ex("\"\" + " + W(a, PrecAdd + 1) + " + " + W(b, PrecAdd + 1), PrecAdd);
            return new Ex(W(a, PrecAdd) + " + " + W(b, PrecAdd + 1), PrecAdd);
        }

        static bool Droppable(IrExpr e) => e is IrCast c && c.Target != null && c.Target.IsString && !c.IsTypeAssertion;

        /// <summary>Operand of a string concatenation: str(x) is implicit in C# ("a" + x).</summary>
        Ex ConcatPart(IrExpr e)
        {
            if (e is IrCast c && c.Target != null && c.Target.IsString && !c.IsTypeAssertion && !(TypeOf(c.Operand).Kind == IrTypeKind.Func)) return X(c.Operand);
            if (TypeOf(e).Kind == IrTypeKind.Func) Problem("printing a function is not supported in JavaFarmer");
            return X(e);
        }

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

        /// <summary>Non-negative or a counting loop variable (i % 2 in for i in range(n)).</summary>
        bool IsNonNegLoose(IrExpr e)
        {
            if (IsNonNeg(e)) return true;
            if (e is IrName n && n.Symbol != null && loopVars.Contains(n.Symbol)) return true;
            if (e is IrBinary b && (b.Op == IrBinOp.Add || b.Op == IrBinOp.Mul)) return IsNonNegLoose(b.Left) && IsNonNegLoose(b.Right);
            return false;
        }

        HashSet<IrSymbol> loopVarsCache;
        HashSet<IrSymbol> loopVars
        {
            get
            {
                if (loopVarsCache != null) return loopVarsCache;
                loopVarsCache = new HashSet<IrSymbol>();
                foreach (var rf in IrWalk.Descendants(mod).OfType<IrRangeFor>())
                    if (rf.Var != null && (rf.Start == null || IsNonNeg(rf.Start)) && (rf.Step == null || (rf.Step is IrLiteral sl && sl.Int > 0)) && !IrWalk.AssignedSymbols(rf.Body).Contains(rf.Var))
                        loopVarsCache.Add(rf.Var);
                return loopVarsCache;
            }
        }

        // ------------------------------------------------------------------ calls
        static string Args(IEnumerable<string> a) => "(" + string.Join(", ", a) + ")";

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
                        var name = s.Kind == IrSymbolKind.ExternalFunction ? ModuleName(s.Module) + "." + Verbatim(s.Name) : Name(s);
                        return new Ex(name + Args(args), PrecPostfix);
                    }
                case IrCallKind.Value:
                    {
                        var ft = TypeOf(c.Callee);
                        var args = c.Args.Select((a, i) => X(a, ft.Kind == IrTypeKind.Func && ft.Items != null && i < ft.Items.Count ? ft.Items[i] : null).T);
                        return new Ex(Postfix(c.Callee) + Args(args), PrecPostfix);
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
            return X(a, pt, true).T;
        }

        Ex BuiltinCall(string b, List<IrExpr> args)
        {
            switch (b)
            {
                case "print":
                    {
                        if (args.Count == 0) return new Ex("Console.WriteLine()", PrecPostfix);
                        if (args.Count == 1)
                        {
                            var a0 = args[0];
                            // Console.Write lowering: print(buffer [+ x])
                            if (a0 is IrName bn && IsBuffer(bn.Symbol)) return new Ex("Console.WriteLine()", PrecPostfix);
                            if (a0 is IrBinary cb && cb.Op == IrBinOp.Concat && cb.Left is IrName bl && IsBuffer(bl.Symbol)) return new Ex("Console.WriteLine(" + X(cb.Right).T + ")", PrecPostfix);
                            if (TypeOf(a0).Kind == IrTypeKind.Func) Problem("printing a function is not supported in JavaFarmer");
                            return new Ex("Console.WriteLine(" + X(a0).T + ")", PrecPostfix);
                        }
                        return new Ex("Console.WriteLine(" + Interpolated(args) + ")", PrecPostfix);
                    }
                case "min": case "max":
                    {
                        var m = b == "min" ? "Math.Min" : "Math.Max";
                        if (args.Count < 2) { Problem(b + "() of a collection is not supported in C# without LINQ"); return new Ex(m + Args(args.Select(a => X(a).T)), PrecPostfix); }
                        var acc = X(args[args.Count - 1]).T;
                        for (int i = args.Count - 2; i >= 0; i--) acc = m + "(" + X(args[i]).T + ", " + acc + ")";
                        return new Ex(acc, PrecPostfix);
                    }
                case "abs": return new Ex("Math.Abs" + Args(args.Select(a => X(a).T)), PrecPostfix);
                case "len": if (args.Count == 1) return Op(new IrOpCall(IrOp.Len, args[0]), null); break;
                case "range": Problem("range() outside a for loop is not supported in C#"); break;
            }
            var api = GameApi.Find(b);
            var name = api != null ? GameApi.ToStyle(b, NamingStyle.Pascal) : Ident(b, true);
            if (api == null) Problem("unknown builtin " + b);
            var parts = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                IrType pt = api != null && api.Params.Count > 0 ? api.Params[Math.Min(i, api.Params.Count - 1)].Type : null;
                if (pt != null && pt.Kind == IrTypeKind.Func && TypeOf(args[i]).Kind == IrTypeKind.Func && args[i] is IrLambda lam && lam.Params.Count > 0) Problem("spawn_drone with a function that takes arguments");
                parts.Add(X(args[i], pt).T);
            }
            return new Ex(name + Args(parts), PrecPostfix);
        }

        /// <summary>print(a, b) → $"{a} {b}"</summary>
        string Interpolated(List<IrExpr> args)
        {
            var sb = new StringBuilder("$\"");
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                var a = args[i];
                if (a is IrLiteral l && l.Kind == IrLitKind.String) { sb.Append(Escape(l.Str).Replace("{", "{{").Replace("}", "}}")); continue; }
                var x = X(a);
                var t = x.T;
                if (x.P <= PrecTernary || t.Contains(":")) t = "(" + t + ")";
                sb.Append('{').Append(t).Append('}');
            }
            return sb.Append('"').ToString();
        }

        Ex FuncRef(IrFuncRef fr)
        {
            if (fr.Function != null)
            {
                if (fr.Function.Kind == IrSymbolKind.ExternalFunction) return new Ex(ModuleName(fr.Function.Module) + "." + Verbatim(fr.Function.Name), PrecPostfix);
                return new Ex(Name(fr.Function), PrecAtom);
            }
            if (fr.Builtin != null)
            {
                var api = GameApi.Find(fr.Builtin);
                if (api != null && !CsApi.Bcl.ContainsKey(fr.Builtin)) return new Ex(GameApi.ToStyle(fr.Builtin, NamingStyle.Pascal), PrecAtom);
                Problem("the builtin " + fr.Builtin + " cannot be used as a value in C#");
                return new Ex("null", PrecAtom);
            }
            return new Ex("null", PrecAtom);
        }

        Ex Lambda(IrLambda lam, IrType expected)
        {
            PushScope();
            var ps = lam.Params.Count == 1 ? Declare(lam.Params[0].Symbol) : "(" + string.Join(", ", lam.Params.Select(p => Declare(p.Symbol))) + ")";
            var outerReturn = CurrentReturn;
            CurrentReturn = lam.ReturnType;
            string text;
            if (lam.BodyExpr != null) text = ps + " => " + W(X(lam.BodyExpr, lam.ReturnType), PrecTernary);
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
                text = inner.Length == 0 ? ps + " => { }" : ps + " =>" + Options.Newline + IndentText + "{" + Options.Newline + inner + IndentText + "}";
            }
            CurrentReturn = outerReturn;
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
                case IrTypeKind.String:
                    if (ca.IsTypeAssertion) return new Ex("(string)" + W(x, PrecUnary), PrecUnary);
                    if (ot.Kind == IrTypeKind.Func) Problem("printing a function is not supported in JavaFarmer");
                    if (ot.IsString) return x;
                    return new Ex(W(x, PrecPostfix) + ".ToString()", PrecPostfix);
                case IrTypeKind.Int:
                    if (ot.IsString) Problem("converting a string to a number is not supported in JavaFarmer");
                    if (ot.Kind == IrTypeKind.Int && !ca.IsTypeAssertion && !fromCs) return x;
                    return new Ex("(int)" + W(x, PrecUnary), PrecUnary);
                case IrTypeKind.Float:
                    if (ot.IsString) Problem("converting a string to a number is not supported in JavaFarmer");
                    return new Ex("(double)" + W(x, PrecUnary), PrecUnary);
                case IrTypeKind.Bool:
                    if (ot.Kind != IrTypeKind.Any) Problem("bool() of a value is not supported in C#");
                    return new Ex("(bool)" + W(x, PrecUnary), PrecUnary);
                case IrTypeKind.Unknown: case IrTypeKind.Any: return x;
            }
            return new Ex("(" + TypeText(t) + ")" + W(x, PrecUnary), PrecUnary);
        }

        // ------------------------------------------------------------------ indexing / collection operations
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
            if (it.Kind == IrTypeKind.Float || it.Kind == IrTypeKind.Any) return "(int)" + W(i, PrecUnary);
            return i.T;
        }

        static bool Is2D(IrType t) => t.Kind == IrTypeKind.Array && t.Elem != null && t.Elem.Kind == IrTypeKind.Array;

        Ex Index(IrIndex ix)
        {
            var tt = TypeOf(ix.Target);
            long lit = ix.Index is IrLiteral li && li.Kind == IrLitKind.Int && ix.BaseOffset == 0 ? li.Int :
                       ix.Index is IrUnary un && un.Op == IrUnOp.Neg && un.Operand is IrLiteral ul && ul.Kind == IrLitKind.Int && ix.BaseOffset == 0 ? -ul.Int : long.MinValue;
            // a[i][j] of a 2-dimensional array: a[i, j]
            if (ix.Target is IrIndex row && Is2D(TypeOf(row.Target)))
                return new Ex(Postfix(row.Target) + "[" + IndexText(row) + ", " + IndexText(ix) + "]", PrecPostfix);
            var recv = Postfix(ix.Target);
            if (Is2D(tt)) Problem("a row of a 2-dimensional array cannot be used as a value in C#");
            switch (tt.Kind)
            {
                case IrTypeKind.Array: case IrTypeKind.List: case IrTypeKind.String:
                    if (lit < 0 && lit != long.MinValue) return new Ex(recv + "[^" + (-lit) + "]", PrecPostfix);
                    if (ix.Index is IrUnary neg && neg.Op == IrUnOp.Neg && ix.BaseOffset == 0) return new Ex(recv + "[^" + W(X(neg.Operand), PrecUnary) + "]", PrecPostfix);
                    return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
                case IrTypeKind.Map:
                    return new Ex(recv + "[" + X(ix.Index, tt.Key).T + "]", PrecPostfix);
                case IrTypeKind.Tuple: case IrTypeKind.Game:
                    {
                        bool comp = tt.IsGame("Companion");
                        if (tt.IsGame("Position") || comp)
                        {
                            if (lit == 0) return new Ex(recv + (comp ? ".Entity" : ".X"), PrecPostfix);
                            if (lit == 1) return new Ex(recv + (comp ? ".Position" : ".Y"), PrecPostfix);
                        }
                        if (tt.Kind == IrTypeKind.Tuple && lit >= 0 && lit != long.MinValue) return new Ex(recv + ".Item" + (lit + 1), PrecPostfix);
                        Problem("indexing a tuple with a computed index is not supported in C#");
                        return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
                    }
            }
            return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
        }

        /// <summary>The operation printed as a whole statement (its C# result type does not matter).</summary>
        IrOpCall statementOp;

        Ex Op(IrOpCall o, IrType expected)
        {
            var a = o.Args;
            if (o != statementOp)
                switch (o.Op)
                {
                    case IrOp.MapRemove: case IrOp.MapRemoveIfPresent: Problem("the value of dict.pop() / Map.remove() inside an expression has no C# equivalent (Remove returns bool)"); break;
                    case IrOp.SetAdd: case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent: case IrOp.ListAppend: case IrOp.ListInsert: case IrOp.Clear:
                        Problem("the result of " + o.Op + " cannot be used as a value in JavaFarmer C#"); break;
                }
            if (a.Count == 0) { Problem("operation " + o.Op + " without arguments"); return new Ex("null", PrecAtom); }
            var rt = TypeOf(a[0]);
            string R() => Postfix(a[0]);
            string A(int i, IrType t = null) => i < a.Count ? X(a[i], t).T : "null";
            Ex M(string text) => new Ex(text, PrecPostfix);
            switch (o.Op)
            {
                case IrOp.Len:
                    if (a[0] is IrIndex row && row.Index is IrLiteral zl && zl.Kind == IrLitKind.Int && zl.Int == 0 && Is2D(TypeOf(row.Target))) return M(Postfix(row.Target) + ".GetLength(1)");
                    if (Is2D(rt)) return M(R() + ".GetLength(0)");
                    if (rt.Kind == IrTypeKind.Array || rt.Kind == IrTypeKind.String) return M(R() + ".Length");
                    return M(R() + ".Count");
                case IrOp.IsEmpty:
                    if (rt.Kind == IrTypeKind.Array || rt.Kind == IrTypeKind.String) return new Ex(R() + ".Length == 0", PrecEq);
                    return new Ex(R() + ".Count == 0", PrecEq);
                case IrOp.Contains:
                    if (rt.Kind == IrTypeKind.Map) return M(R() + ".ContainsKey(" + A(1, rt.Key) + ")");
                    if (a[0] is IrOpCall keys && keys.Op == IrOp.MapKeys) return M(Postfix(keys.Args[0]) + ".ContainsKey(" + A(1) + ")");
                    if (rt.Kind == IrTypeKind.Array) Problem("Contains on an array is not supported in JavaFarmer (use a List)");
                    if (rt.Kind == IrTypeKind.Tuple) Problem("Contains on a tuple is not supported in C#");
                    return M(R() + ".Contains(" + A(1, rt.Elem) + ")");
                case IrOp.ListAppend: case IrOp.SetAdd: return M(R() + ".Add(" + A(1, rt.Elem) + ")");
                case IrOp.ListInsert: return M(R() + ".Insert(" + IndexOf(a[1]) + ", " + A(2, rt.Elem) + ")");
                case IrOp.ListPop:
                    Problem("pop() as a value has no C# equivalent (read the element, then RemoveAt)");
                    return M(PopStatement(o));
                case IrOp.ListSet: return new Ex(R() + "[" + IndexOf(a[1]) + "] = " + A(2, rt.Elem), PrecTernary);
                case IrOp.ListIndexOf: return M(R() + ".IndexOf(" + A(1, rt.Elem) + ")");
                case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent: return M(R() + ".Remove(" + A(1, rt.Elem) + ")");
                case IrOp.Clear: return M(R() + ".Clear()");
                case IrOp.MapPut: return new Ex(R() + "[" + A(1, rt.Key) + "] = " + A(2, rt.Elem), PrecTernary);
                case IrOp.MapGetOrNull:
                    if (!typedSource && rt.Elem != null && (rt.Elem.IsNumeric || rt.Elem.Kind == IrTypeKind.Bool)) Problem("dict.get() of a missing number key returns None in Python but 0 in C#");
                    return M(R() + ".GetValueOrDefault(" + A(1, rt.Key) + ")");
                case IrOp.MapGetOrDefault: return M(R() + ".GetValueOrDefault(" + A(1, rt.Key) + ", " + A(2, rt.Elem) + ")");
                case IrOp.MapRemove: case IrOp.MapRemoveIfPresent: return M(R() + ".Remove(" + A(1, rt.Key) + ")");
                case IrOp.MapKeys: return M(R() + ".Keys");
                case IrOp.MapValues: return M(R() + ".Values");
                case IrOp.ListCopy:
                    {
                        var t = TypeOf(o);
                        var src = rt.Kind == IrTypeKind.Map ? R() + ".Keys" : CopySource(a[0]);
                        if (rt.Kind == IrTypeKind.String) Problem("a list of the characters of a string is not supported in JavaFarmer");
                        return M("new " + TypeText(IrType.List(t.Elem != null && t.Elem.IsKnown ? t.Elem : (rt.Kind == IrTypeKind.Map ? rt.Key : rt.Elem) ?? IrType.Any)) + "(" + src + ")");
                    }
                case IrOp.SetCopy:
                    {
                        var t = TypeOf(o);
                        var src = rt.Kind == IrTypeKind.Map ? R() + ".Keys" : CopySource(a[0]);
                        return M("new " + TypeText(IrType.Set(t.Elem != null && t.Elem.IsKnown ? t.Elem : (rt.Kind == IrTypeKind.Map ? rt.Key : rt.Elem) ?? IrType.Any)) + "(" + src + ")");
                    }
                case IrOp.MapCopy:
                    if (rt.Kind != IrTypeKind.Map) Problem("dict() of something that is not a dictionary");
                    return M("new " + TypeText(rt.Kind == IrTypeKind.Map ? rt : IrType.Map(IrType.Any, IrType.Any)) + "(" + CopySource(a[0]) + ")");
                case IrOp.ArrayNew: case IrOp.ArrayNew2:
                    {
                        var t = TypeOf(o);
                        var elem = t.Kind == IrTypeKind.Array ? t.Elem : null;
                        if (o.Op == IrOp.ArrayNew2 && elem != null && elem.Kind == IrTypeKind.Array) elem = elem.Elem;
                        var d = o.Op == IrOp.ArrayNew ? (a.Count > 1 ? a[1] : null) : (a.Count > 2 ? a[2] : null);
                        if (elem == null || !elem.IsKnown) elem = d != null ? TypeOf(d) : IrType.Any;
                        if (elem.Kind == IrTypeKind.Array) { Problem("jagged arrays are not supported in JavaFarmer (use T[,])"); elem = elem.Elem ?? IrType.Any; }
                        if (d != null && !IsDefaultOf(d, elem)) Problem("an array filled with a value other than the default is not supported in C#");
                        var dims = o.Op == IrOp.ArrayNew ? "[" + A(0, IrType.Int) + "]" : "[" + A(0, IrType.Int) + ", " + A(1, IrType.Int) + "]";
                        return M("new " + TypeText(elem) + dims);
                    }
                case IrOp.Floor: return MathOp("Math.Floor", a[0], expected);
                case IrOp.Ceil: return MathOp("Math.Ceiling", a[0], expected);
                case IrOp.Round: return MathOp("Math.Round", a[0], expected);
                case IrOp.Sqrt: return MathOp("Math.Sqrt", a[0], expected);
                case IrOp.PosX: return M(R() + ".X");
                case IrOp.PosY: return M(R() + ".Y");
                case IrOp.CompanionEntity: return M(R() + ".Entity");
                case IrOp.CompanionPos: return M(R() + ".Position");
            }
            Problem("unsupported operation " + o.Op);
            return new Ex("null", PrecAtom);
        }

        /// <summary>Math.Floor & co. return double in C#: an int context gets an explicit (int).</summary>
        Ex MathOp(string fn, IrExpr arg, IrType expected)
        {
            var call = fn + "(" + X(arg).T + ")";
            if (expected != null && expected.Kind == IrTypeKind.Int && !fromCs) return new Ex("(int)" + call, PrecUnary);
            return new Ex(call, PrecPostfix);
        }

        string IndexOf(IrExpr i)
        {
            var t = TypeOf(i);
            var x = X(i);
            return t.Kind == IrTypeKind.Float || t.Kind == IrTypeKind.Any ? "(int)" + W(x, PrecUnary) : x.T;
        }

        string CopySource(IrExpr e)
        {
            switch (e)
            {
                case IrListLit l when !l.IsArray && l.Items.Count > 0: return "[" + string.Join(", ", l.Items.Select(i => X(i, TypeOf(l).Elem).T)) + "]";
                case IrSetLit s when s.Items.Count > 0: return "[" + string.Join(", ", s.Items.Select(i => X(i, TypeOf(s).Elem).T)) + "]";
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

        /// <summary>The literal's type, or the expected one when it has the same shape.</summary>
        IrType LitType(IrExpr lit, IrType expected)
        {
            var t = TypeOf(lit);
            if (expected != null && expected.Kind == t.Kind && !fromCs) return expected;
            if (expected != null && expected.Kind == t.Kind && Complete(expected)) return expected;
            return t;
        }

        static bool Complete(IrType t)
        {
            if (t == null || !t.IsKnown) return false;
            switch (t.Kind)
            {
                case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Array: return Complete(t.Elem);
                case IrTypeKind.Map: return Complete(t.Key) && Complete(t.Elem);
            }
            return true;
        }

        Ex ListLit(IrListLit l, IrType expected, bool targetTyped)
        {
            var t = LitType(l, expected);
            var elem = t.Elem != null && t.Elem.IsKnown && t.Elem.Kind != IrTypeKind.Null ? t.Elem : IrType.Any;
            var items = string.Join(", ", l.Items.Select(i => X(i, elem).T));
            bool sameKind = expected != null && (expected.Kind == IrTypeKind.List || expected.Kind == IrTypeKind.Array) && (expected.Kind == IrTypeKind.Array) == l.IsArray;
            if (l.IsArray && Is2D(t))
            {
                // int[,]: nested initializer { { 1, 2 }, { 3, 4 } }
                var rows = l.Items.Select(row => row is IrListLit rl ? "{ " + string.Join(", ", rl.Items.Select(i => X(i, t.Elem.Elem).T)) + " }" : "{ /* row */ }").ToList();
                if (l.Items.Any(row => !(row is IrListLit))) Problem("a 2-dimensional array made of computed rows");
                else if (l.Items.Select(row => ((IrListLit)row).Items.Count).Distinct().Count() > 1) Problem("rows of different lengths (jagged arrays are not supported in JavaFarmer C#)");
                return new Ex("new " + TypeText(t) + " { " + string.Join(", ", rows) + " }", PrecPostfix);
            }
            if (targetTyped && sameKind) return new Ex("[" + items + "]", PrecAtom);
            if (l.IsArray) return new Ex("new " + TypeText(elem) + "[] { " + items + " }", PrecPostfix);
            if (l.Items.Count == 0) return new Ex("new " + TypeText(IrType.List(elem)) + "()", PrecPostfix);
            return new Ex("new " + TypeText(IrType.List(elem)) + " { " + items + " }", PrecPostfix);
        }

        Ex SetLit(IrSetLit s, IrType expected, bool targetTyped)
        {
            var t = LitType(s, expected);
            var elem = t.Elem != null && t.Elem.IsKnown ? t.Elem : IrType.Any;
            var items = string.Join(", ", s.Items.Select(i => X(i, elem).T));
            if (targetTyped && expected != null && expected.Kind == IrTypeKind.Set) return new Ex("[" + items + "]", PrecAtom);
            if (s.Items.Count == 0) return new Ex("new " + TypeText(IrType.Set(elem)) + "()", PrecPostfix);
            return new Ex("new " + TypeText(IrType.Set(elem)) + " { " + items + " }", PrecPostfix);
        }

        Ex MapLit(IrMapLit m, IrType expected, bool targetTyped)
        {
            var t = LitType(m, expected);
            var kt = t.Key != null && t.Key.IsKnown ? t.Key : IrType.Any;
            var vt = t.Elem != null && t.Elem.IsKnown ? t.Elem : IrType.Any;
            if (m.Keys.Count == 0)
            {
                if (targetTyped && expected != null && expected.Kind == IrTypeKind.Map) return new Ex("[]", PrecAtom);
                return new Ex("new " + TypeText(IrType.Map(kt, vt)) + "()", PrecPostfix);
            }
            var parts = new List<string>();
            for (int i = 0; i < m.Keys.Count; i++) parts.Add("[" + X(m.Keys[i], kt).T + "] = " + X(m.Values[i], vt).T);
            return new Ex("new " + TypeText(IrType.Map(kt, vt)) + " { " + string.Join(", ", parts) + " }", PrecPostfix);
        }

        // ================================================================== types
        internal static string TypeText(IrType t)
        {
            if (t == null) return "object";
            string Inner(IrType x) => x == null || !x.IsKnown || x.Kind == IrTypeKind.Null ? "object" : TypeText(x);
            string s;
            switch (t.Kind)
            {
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "bool";
                case IrTypeKind.String: return "string";
                case IrTypeKind.Void: return "void";
                case IrTypeKind.List: return "List<" + Inner(t.Elem) + ">";
                case IrTypeKind.Set: return "HashSet<" + Inner(t.Elem) + ">";
                case IrTypeKind.Map: return "Dictionary<" + Inner(t.Key) + ", " + Inner(t.Elem) + ">";
                case IrTypeKind.Array:
                    if (Is2D(t)) return Inner(t.Elem.Elem) + "[,]";
                    return Inner(t.Elem) + "[]";
                case IrTypeKind.Game: s = t.GameName ?? "object"; return t.Nullable && s != "Direction" ? s + "?" : s;
                case IrTypeKind.Tuple:
                    if (t.Items == null || t.Items.Count < 2) return "object";
                    return "(" + string.Join(", ", t.Items.Select(Inner)) + ")";
                case IrTypeKind.Func:
                    {
                        var ps = (t.Items ?? new List<IrType>()).Select(Inner).ToList();
                        if (t.Elem == null || t.Elem.Kind == IrTypeKind.Void || !t.Elem.IsKnown) return ps.Count == 0 ? "Action" : "Action<" + string.Join(", ", ps) + ">";
                        return "Func<" + string.Join(", ", ps.Concat(new[] { Inner(t.Elem) })) + ">";
                    }
            }
            return "object";
        }

        static string DefaultValue(IrType t)
        {
            switch (t?.Kind ?? IrTypeKind.Unknown)
            {
                case IrTypeKind.Int: return "0";
                case IrTypeKind.Float: return "0.0";
                case IrTypeKind.Bool: return "false";
                case IrTypeKind.Tuple: return "default";
                case IrTypeKind.Game: return t.GameName == "Direction" ? "North" : "null";
            }
            return "null";
        }

        static bool CanComplete(IrStmt s)
        {
            switch (s)
            {
                case null: return true;
                case IrReturn _: case IrContinue _: case IrBreak _: return false;
                case IrBlock b: return b.Stmts.Count == 0 || b.Stmts.All(CanComplete);
                case IrIf f: return f.Else == null || CanComplete(f.Then) || CanComplete(f.Else);
                case IrLoop l: return BreaksOut(l.Body);
                case IrWhile w: return !(w.Cond is IrLiteral lit && lit.Kind == IrLitKind.Bool && lit.Bool) || BreaksOut(w.Body);
                case IrSwitch sw: return !sw.Cases.Any(c => c.IsDefault) || sw.Cases.Any(c => CanComplete(c.Body));
            }
            return true;
        }
    }
}
