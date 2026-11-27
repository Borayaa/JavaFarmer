using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.CFamily
{
    /// <summary>
    /// Farm IR → idiomatic C (farm.h) or C++ (farm.hpp). Prints the program a beginner would write: includes, globals,
    /// functions (prototypes for the ones used before their definition) and int main. Snippet mode prints bare statements
    /// like the docs. Constructs the dialect cannot express are reported in Problems (DocsTranslator keeps the Python).
    /// The declaration planning for untyped IR (Python docs) is the Java printer's, copied (see TypeGuess.cs).
    /// </summary>
    internal sealed class CPrinter : PrinterBase
    {
        public readonly List<string> Problems = new List<string>();

        public static string Print(IrModule module, PrintOptions options, Dialect d, out IReadOnlyList<string> problems)
        {
            var p = new CPrinter(module, options, d);
            try { p.Module(); }
            catch (Exception ex) { p.Problems.Add("internal printer error: " + ex.Message); problems = p.Problems; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
            problems = p.Problems;
            return p.Result;
        }

        readonly IrModule mod;
        readonly Dialect d;
        readonly bool typed;      // a statically typed source (C, C++, Java ...): keep names and declarations as written
        CTypeGuess types;
        bool C => d == Dialect.C;
        bool Cpp => d == Dialect.Cpp;
        string Lang => C ? "C" : "C++";

        CPrinter(IrModule module, PrintOptions options, Dialect dialect) : base(options)
        {
            mod = module ?? new IrModule();
            d = dialect;
            var sl = mod.SourceLanguage;
            typed = sl == null || sl == "c" || sl == "cpp" || sl == "java" || sl == "csharp" || sl == "rust" || sl == "zig";
        }

        void Problem(string what) { if (!Problems.Contains(what)) Problems.Add(what); }

        static readonly string[] LibraryNames =
        {
            "main", "NULL", "printf", "puts", "strlen", "strcmp", "floor", "ceil", "round", "sqrt", "pow", "fabs", "fmin", "fmax", "fmod",
            "exp", "log", "sin", "cos", "tan", "abs", "min", "max", "time", "index", "exit", "malloc", "free", "rand", "random", "y0", "y1", "yn", "j0", "j1", "jn",
            "for_each", "List", "Map", "Set", "string", "std", "farm", "Position", "Companion", "quick_print", "str_of", "str_concat", "str_eq", "str_contains",
            "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Entities", "Grounds", "Items", "Hats", "Unlocks", "Leaderboards", "Directions",
            "North", "East", "South", "West",
        };
        static readonly HashSet<string> CReserved = new HashSet<string>(Lexer.CKeywords.Concat(LibraryNames));
        static readonly HashSet<string> CppReserved = new HashSet<string>(Lexer.CppKeywords.Concat(Lexer.CKeywords).Concat(LibraryNames));

        static bool IsApiName(string n) => GameApi.Find(n) != null || n.StartsWith("list_") || n.StartsWith("map_") || n.StartsWith("set_");

        /// <summary>Source identifier → legal C / C++ identifier (keywords and library names get "_").</summary>
        internal string Ident(string name)
        {
            if (string.IsNullOrEmpty(name)) return "x";
            if (name == "_") return "unused";
            var n = name;
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = "j" + n.Substring(3);
            var clean = new StringBuilder();
            foreach (var ch in n) clean.Append(char.IsLetterOrDigit(ch) && ch < 128 || ch == '_' ? ch : '_');
            n = clean.ToString();
            if (n.Length == 0) n = "x";
            if (char.IsDigit(n[0])) n = "_" + n;
            if ((C ? CReserved : CppReserved).Contains(n) || IsApiName(n)) n += "_";
            return n;
        }

        readonly Dictionary<IrSymbol, string> names = new Dictionary<IrSymbol, string>();
        readonly List<Dictionary<string, IrSymbol>> scopes = new List<Dictionary<string, IrSymbol>>();
        readonly HashSet<string> methodNames = new HashSet<string>();
        readonly HashSet<string> fieldNames = new HashSet<string>();

        bool CamelNames => !typed;

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
            var baseName = Ident(s.Name);
            var n = baseName;
            int k = 2;
            while (true)
            {
                var live = LiveLocal(n);
                if ((live == null || live == s) && !methodNames.Contains(n)) break;
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
            n = Ident(s.Name);
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
                    var b = Ident(f.Symbol.Name);
                    if (IsApiName(b) || b == "main") b += "_";
                    n = b;
                    int k = 2;
                    while (methodNames.Contains(n)) n = b + k++;
                }
                names[f.Symbol] = n;
                methodNames.Add(n);
            }
            foreach (var s in FieldSymbols())
            {
                var b = Ident(s.Name);
                var n = b;
                int k = 2;
                while (fieldNames.Contains(n) || methodNames.Contains(n)) n = b + k++;
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
                Line(TypeText(rt.IsKnown && rt.Kind != IrTypeKind.Null ? rt : IrType.Void) + " " + Name(s) + "(" + ParamsText(ps) + ") {");
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
            types = new CTypeGuess(mod);
            entry = mod.Entry;
            if (!typed) FindUndefined();
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
            if (typed && mainBlock == null)
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
            undefinedVars.RemoveAll(u => u.Name == "__name__");
            AssignModuleNames();
            Comments(mod.Leading);
            bool hasDecls = mod.Functions.Any(f => !f.IsEntry) || mod.Globals.Any(g => g.Symbol != null && !localized.Contains(g.Symbol) && !IsBuffer(g.Symbol)) || extraFields.Count > 0;
            if (Options.Snippet && !hasDecls)
            {
                PushScope();
                if (mainBlock != null && mainBlock.Stmts.Count == 1 && mainBlock.Stmts[0] is IrExprStmt lone && !IsStatementExpr(lone.Expr) && lone.Leading == null && lone.Trailing == null)
                {
                    Line(X(lone.Expr).T);
                    PopScope();
                    return;
                }
                if (mainBlock != null) Stmts(mainBlock.Stmts);
                else if (entry != null) { Stmts(entry.Body.Stmts); Comments(entry.Body.TrailingComments); }
                PopScope();
                Comments(mod.TrailingComments);
                return;
            }
            // the body is printed first (it decides which headers are needed), the head is put in front of it
            var head = Out.ToString();
            Out.Length = 0;
            int last = 0;   // 0 nothing yet, 1 variable, 2 function
            foreach (var s in undefinedVars)
            {
                if (Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0 || s.Name == "Directions") Problem(s.Name + " cannot be used as a value in " + Lang);
                Line("// defined elsewhere");
                var ut = SymType(s);
                Line(Decl(ut.IsKnown && ut.Kind != IrTypeKind.Null && ut.Kind != IrTypeKind.Any ? ut : IrType.Int, Name(s)) + ";");
                last = 1;
            }
            foreach (var s in extraFields)
            {
                var init = extraFieldInit.TryGetValue(s, out var ie) ? ie : null;
                if (init != null && skipped.FirstOrDefault(x => x is IrAssign a && NameSym(a.Target) == s) is IrStmt st && leadingOfSkipped.TryGetValue(st, out var lc))
                    Comments(lc);
                var t = SymType(s);
                Line(Decl(t, Name(s)) + (init != null ? " = " + X(init, t).T : GlobalDefault(t)) + ";");
                last = 1;
            }
            var order = new List<IrNode>();
            bool mainDone = false;
            foreach (var item in mod.Items)
            {
                if (item is IrStmt st)
                {
                    if (mainDone || mainBlock == null || skipped.Contains(st)) continue;
                    mainDone = true;
                    order.Add(mainBlock);
                }
                else order.Add(item);
            }
            // script sources (Python, JS ...): C / C++ need globals declared before the functions that use them
            if (!typed) order = order.Where(n => n is IrGlobal).Concat(order.Where(n => !(n is IrGlobal))).ToList();
            // functions used before their definition get a prototype
            var printedFns = new HashSet<IrSymbol>();
            var needProto = new List<IrSymbol>();
            foreach (var node in order)
            {
                foreach (var n in IrWalk.Descendants(node))
                {
                    IrSymbol fs = null;
                    if (n is IrCall c && c.Kind == IrCallKind.Function) fs = c.Function;
                    else if (n is IrFuncRef fr) fs = fr.Function;
                    else if (n is IrName nm && nm.Symbol != null && nm.Symbol.Kind == IrSymbolKind.Function) fs = nm.Symbol;
                    if (fs == null || printedFns.Contains(fs) || needProto.Contains(fs)) continue;
                    if (fs.Kind == IrSymbolKind.Function && (mod.Functions.Any(f => f.Symbol == fs && !f.IsEntry) || undefinedFns.Contains(fs))) needProto.Add(fs);
                }
                if (node is IrFunction fn && fn.Symbol != null) printedFns.Add(fn.Symbol);
            }
            var externals = ExternalFunctions();
            var externGlobals = IrWalk.Descendants(mod).OfType<IrName>().Select(nm => nm.Symbol).Where(sy => sy != null && sy.Kind == IrSymbolKind.ExternalGlobal).Distinct().ToList();
            foreach (var eg in externGlobals) { Line("extern " + Decl(SymType(eg), Ident(eg.Name), eg) + ";" + (eg.Module != null ? "   // defined in " + eg.Module : "")); last = 1; }
            if (needProto.Count > 0 || externals.Count > 0)
            {
                foreach (var s in externals) Line(Prototype(s, null) + ";" + (s.Module != null ? "   // defined in " + s.Module : ""));
                foreach (var s in needProto) Line(Prototype(s, mod.Functions.FirstOrDefault(f => f.Symbol == s)) + ";");
                last = 1;
            }
            bool printedMainBlock = false;
            foreach (var node in order)
            {
                switch (node)
                {
                    case IrGlobal g:
                        if (g.Symbol == null || localized.Contains(g.Symbol) || IsBuffer(g.Symbol)) break;
                        if (last == 2) Line();
                        Global(g);
                        last = 1;
                        break;
                    case IrFunction f:
                        if (last != 0) Line();
                        Function(f);
                        last = 2;
                        break;
                    case IrBlock mb when mb == mainBlock:
                        if (printedMainBlock) break;
                        printedMainBlock = true;
                        if (last != 0) Line();
                        Line(C ? "int main(void) {" : "int main() {");
                        Indent();
                        PushScope();
                        LateInits();
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
            var body = Out.ToString();
            Out.Length = 0;
            Out.Append(head);
            if (!Options.Snippet)
            {
                Line("#include \"" + (C ? "farm.h" : "farm.hpp") + "\"");
                foreach (var h in includes.OrderBy(x => x, StringComparer.Ordinal)) Line("#include <" + h + ">");
                Line();
            }
            Out.Append(body);
            if (lateInits.Count > 0 && entry == null && mainBlock == null) Problem("a global variable needs a value that C cannot compute outside a function");
        }

        readonly SortedSet<string> includes = new SortedSet<string>(StringComparer.Ordinal);
        void Need(string header) { if (!Options.Snippet) includes.Add(header); }

        /// <summary>A parameter: C++ reference parameters keep their &amp;; Python lists (passed by reference) print as T&amp;.</summary>
        string ParamDecl(IrType t, string name, IrSymbol s)
        {
            if (Cpp && (Equals(s?.Tag, Binder.RefTag) || (!typed && (t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Set || t.Kind == IrTypeKind.Map))))
            {
                var text = TypeText(t);
                if (text != "auto") return text + "& " + name;
            }
            return Decl(t, name, s);
        }

        /// <summary>C++ std::optional value (the IR shares the name node between "*opt" and "opt", so look at the symbol too).</summary>
        bool IsOptional(IrExpr e) => TypeOf(e).Nullable || (e is IrName n && n.Symbol?.Type != null && n.Symbol.Type.Nullable && n.Symbol.Type.IsCollection);

        static bool IsBuffer(IrSymbol s) => s != null && Equals(s.Tag, Binder.BufferTag);
        bool IsField(IrSymbol s) => s != null && (s.Kind == IrSymbolKind.Global);

        string ParamsText(List<string> ps) => ps.Count == 0 && C ? "void" : string.Join(", ", ps);

        List<IrSymbol> ExternalFunctions()
        {
            var res = new List<IrSymbol>();
            foreach (var n in IrWalk.Descendants(mod))
            {
                IrSymbol s = n is IrCall c && c.Kind == IrCallKind.Function ? c.Function : n is IrFuncRef fr ? fr.Function : null;
                if (s != null && s.Kind == IrSymbolKind.ExternalFunction && !res.Contains(s)) res.Add(s);
            }
            return res;
        }

        string Prototype(IrSymbol s, IrFunction f)
        {
            var ps = new List<string>();
            if (f != null)
            {
                PushScope();
                foreach (var p in f.Params) ps.Add(Decl(SymType(p.Symbol), Ident(p.Symbol.Name), p.Symbol) + (Cpp && p.Default != null ? " = " + X(p.Default, SymType(p.Symbol)).T : ""));
                PopScope();
                return Decl(RetType(f), Name(s), null, true) + "(" + ParamsText(ps) + ")";
            }
            if (s.ParamTypes != null) for (int i = 0; i < s.ParamTypes.Count; i++) ps.Add(Decl(s.ParamTypes[i], ((char)('a' + Math.Min(i, 25))).ToString()));
            else
            {
                var calls = IrWalk.Descendants(mod).OfType<IrCall>().Where(c => c.Function == s).ToList();
                int n = calls.Count == 0 ? 0 : calls.Max(c => c.Args.Count);
                for (int i = 0; i < n; i++) ps.Add(Decl(TypeOf(calls.First(c => c.Args.Count > i).Args[i]), ((char)('a' + Math.Min(i, 25))).ToString()));
                if (!(s.Type != null && s.Type.Kind == IrTypeKind.Func)) Problem("the types of " + s.Name + " (another code window) are unknown");
            }
            var rt = s.Type != null && s.Type.Kind == IrTypeKind.Func && s.Type.Elem != null && s.Type.Elem.IsKnown ? s.Type.Elem : IrType.Void;
            return Decl(rt, s.PyName != null && s.Kind == IrSymbolKind.ExternalFunction ? Ident(s.Name) : Name(s), null, true) + "(" + ParamsText(ps) + ")";
        }

        /// <summary>C: global initialisers must be constants; other values are assigned at the start of main.</summary>
        readonly List<(IrSymbol S, IrExpr Init)> lateInits = new List<(IrSymbol, IrExpr)>();

        void LateInits()
        {
            foreach (var (s, init) in lateInits) Line(Name(s) + " = " + X(init, SymType(s)).T + ";");
            lateInits.Clear();
        }

        bool CConstant(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l: return true;
                case IrConst c: return c.Category == "Directions";
                case IrUnary u: return CConstant(u.Operand);
                case IrBinary b: return b.Op != IrBinOp.Pow && b.Op != IrBinOp.Concat && CConstant(b.Left) && CConstant(b.Right);
                case IrName n: return n.Symbol != null && n.Symbol.IsConst && n.Symbol.Kind == IrSymbolKind.Global && false;
                case IrListLit ll: return ll.IsArray && ll.Items.All(CConstant);
                case IrTupleLit tl: return false;
            }
            return false;
        }

        void Global(IrGlobal g)
        {
            LeadingComments(g);
            var s = g.Symbol;
            if (Equals(s.Tag, Binder.DefineTag) && C && g.Init != null)
            {
                Line("#define " + Name(s) + " " + X(g.Init, SymType(s)).T, g);
                return;
            }
            var t = SymType(s);
            var mods = (g.IsPrivate && typed ? "static " : "") + (s.IsConst ? (Cpp ? "const " : "const ") : "");
            string init = "";
            if (g.Init != null && !(g.ImplicitInit && !s.IsConst))
            {
                if (C && !CConstant(g.Init) && !(t.Kind == IrTypeKind.Array && g.Init is IrOpCall))
                {
                    if (s.IsConst) mods = mods.Replace("const ", "");
                    lateInits.Add((s, g.Init));
                }
                else if (t.Kind == IrTypeKind.Array && g.Init is IrOpCall)
                {
                    // int grid[8][8]; (zero-filled)
                }
                else init = " = " + X(g.Init, t).T;
            }
            else init = GlobalDefault(t);
            if (t.Kind == IrTypeKind.Array) { Line(mods + ArrayDecl(t, Name(s), s, g.Init) + init + ";", g); return; }
            Line(mods + Decl(t, Name(s), s) + init + ";", g);
        }

        /// <summary>C++ globals of class type need no initialiser; C pointers start as NULL anyway.</summary>
        string GlobalDefault(IrType t) => "";

        IrType RetType(IrFunction f)
        {
            if (f.ReturnType != null && f.ReturnType.IsKnown) return f.ReturnType;
            return f.Symbol != null ? types.Ret(f.Symbol) : IrType.Void;
        }

        void Function(IrFunction f)
        {
            LeadingComments(f);
            PushScope();
            var ps = new List<string>();
            if (!f.IsEntry)
                foreach (var p in f.Params)
                {
                    var pt = SymType(p.Symbol);
                    if (!pt.IsKnown || pt.Kind == IrTypeKind.Any || pt.Kind == IrTypeKind.Null) pt = IrType.Int;
                    var nm = Declare(p.Symbol);
                    var dflt = "";
                    if (p.Default != null)
                    {
                        if (Cpp) dflt = " = " + X(p.Default, pt).T;
                        else if (!typed) { /* callers pass the default explicitly */ }
                    }
                    ps.Add(ParamDecl(pt, nm, p.Symbol) + dflt);
                }
            var rt = RetType(f);
            var outerReturn = CurrentReturn;
            CurrentReturn = rt;
            inEntry = f.IsEntry;
            string header = f.IsEntry ? (C ? "int main(void)" : "int main()") : (f.IsPrivate && typed ? "static " : "") + Decl(rt, Name(f.Symbol), null, true) + "(" + ParamsText(ps) + ")";
            Line(header + " {", f);
            Indent();
            if (f.IsEntry) LateInits();
            Stmts(f.Body.Stmts);
            if (!typed && !f.IsEntry && rt.Kind != IrTypeKind.Void && CanComplete(f.Body)) Line("return " + DefaultValue(rt) + ";");
            Comments(f.Body.TrailingComments);
            Dedent();
            PopScope();
            Line("}");
            inEntry = false;
            CurrentReturn = outerReturn;
        }

        bool inEntry;
        IrType CurrentReturn;

        // ================================================================== statements

        void Stmts(IEnumerable<IrStmt> stmts)
        {
            var list = stmts as IList<IrStmt> ?? stmts.ToList();
            bool dead = false;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (dead && !typed)
                {
                    int mark = Out.Length;
                    Stmt(s);
                    var text = Out.ToString(mark, Out.Length - mark);
                    Out.Length = mark;
                    foreach (var line in text.Split(new[] { Options.Newline }, StringSplitOptions.None))
                        if (line.Length > 0) Line("// " + line.TrimStart());
                    continue;
                }
                // printf lowering pairs: print(__jout + x) followed by __jout = ""
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
                    Line(Decl(t, Declare(sym), sym) + " = " + DefaultValue(t) + ";");
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
                    if (r.Value == null) { Line(inEntry ? "return 0;" : "return;", r); return; }
                    if (r.Value is IrTupleLit tl && !(tl.Items.Count == 2 && tl.Items.All(i => TypeOf(i).IsNumeric))) Problem("returning several values (a tuple) is not supported in " + Lang);
                    if (inEntry) { Line("return 0;", r); return; }
                    Line("return " + X(r.Value, CurrentReturn).T + ";", r);
                    return;
                case IrImport imp:
                    if (Options.Snippet) Problem("import " + imp.Module + " has no " + Lang + " equivalent (declare the other window's functions with prototypes)");
                    Line("// the functions of " + imp.Module + " are used through prototypes", imp);
                    return;
            }
            Problem("unsupported statement " + s.GetType().Name);
            Line("// unsupported statement: " + s.GetType().Name);
        }

        void VarDecl(IrVarDecl v)
        {
            var s = v.Symbol;
            if (s != null && promotedDecls.Contains(s))
            {
                if (v.Init != null) Line(Name(s) + " = " + X(v.Init, SymType(s)).T + ";", v);
                return;
            }
            var t = SymType(s);
            if (v.Init is IrLambda lam && (Cpp || C))
            {
                if (C) { Problem("closures (a function defined inside a function) are not supported in C"); Line("// unsupported: nested function " + s?.Name, v); return; }
                var lx = X(lam).T;
                Line("auto " + Declare(s) + " = " + lx + ";", v);
                return;
            }
            var cst = s != null && s.IsConst && typed ? "const " : "";
            if (v.Init == null)
            {
                Line(cst + DeclFor(s, t, null) + (typed ? "" : " = " + DefaultValue(t)) + ";", v);
                return;
            }
            if (t.Kind == IrTypeKind.Array)
            {
                var nm0 = Declare(s);
                if (v.Init is IrOpCall ao && (ao.Op == IrOp.ArrayNew || ao.Op == IrOp.ArrayNew2) && C)
                {
                    Line(cst + ArrayDecl(t, nm0, s, ao) + ";", v);
                    return;
                }
                Line(cst + ArrayDecl(t, nm0, s, v.Init) + " = " + X(v.Init, t).T + ";", v);
                return;
            }
            var declText = DeclFor(s, t, v.Init, v.TypeExplicit);
            var init = Cpp && !declText.StartsWith("auto") && (v.Init is IrListLit || v.Init is IrSetLit || v.Init is IrMapLit) ? BraceInit(v.Init, t) : X(v.Init, t).T;
            Line(cst + declText + " = " + init + ";", v);
        }

        /// <summary>"T name" for a local declaration (auto in C++ when the source wrote auto or the type is long).</summary>
        string DeclFor(IrSymbol s, IrType t, IrExpr init, bool typeExplicit = true)
        {
            var nm = Declare(s);
            if (Cpp && init != null && (!t.IsKnown || t.Kind == IrTypeKind.Any || (!typeExplicit && typed) || (t.Nullable && (t.Kind == IrTypeKind.Map || t.IsGame("Companion"))) || t.Kind == IrTypeKind.Func) && !(init is IrLiteral lit && lit.Kind == IrLitKind.Null))
                return "auto " + nm;
            return Decl(t, nm, s);
        }

        void ExprStmt(IrExprStmt es)
        {
            var e = es.Expr;
            if (e is IrCall pc && pc.Kind == IrCallKind.Builtin && (pc.Builtin == "print" || pc.Builtin == "quick_print"))
            {
                PrintStmt(pc, es, true);
                return;
            }
            if (Cpp && e is IrOpCall pop && pop.Op == IrOp.ListPop && pop.Args.Count >= 1)
            {
                var r = Postfix(pop.Args[0]);
                if (pop.Args.Count == 1) Line(r + ".pop_back();", es);
                else Line(r + ".erase(" + r + ".begin() + " + W(X(pop.Args[1], IrType.Int), PrecAdd + 1) + ");", es);
                return;
            }
            stmtCall = e;
            var x = X(e);
            stmtCall = null;
            if (!IsStatementExpr(e))
            {
                Problem("an expression statement without effect has no " + Lang + " equivalent");
                Line("// " + x.T + ";", es);
                return;
            }
            Line(x.T + ";", es);
        }

        bool IsStatementExpr(IrExpr e) => e is IrCall || e is IrOpCall o && o.Op != IrOp.Len && o.Op != IrOp.IsEmpty && o.Op != IrOp.Contains;

        /// <summary>Python's  if __name__ == "__main__":  (a C/C++ program always starts in main).</summary>
        static bool IsNameGuard(IrExpr c) =>
            c is IrBinary b && b.Op == IrBinOp.Eq && b.Left is IrName n && n.Symbol != null && n.Symbol.Name == "__name__" && b.Right is IrLiteral l && l.Kind == IrLitKind.String && l.Str == "__main__";

        void If(IrIf f)
        {
            if (IsNameGuard(f.Cond) && f.Else == null && !typed)
            {
                Stmts(f.Then.Stmts);
                return;
            }
            Line("if (" + Cond(f.Cond).T + ") {", f);
            Block(f.Then);
            var tail = f.Else;
            while (tail is IrIf ei)
            {
                if (Options.Comments && ei.Leading != null && ei.Leading.Count > 0) { Indent(); Comments(ei.Leading); Dedent(); }
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

        string SimpleStmtText(IrStmt s)
        {
            switch (s)
            {
                case IrVarDecl v:
                    {
                        var t = SymType(v.Symbol);
                        var init = v.Init != null ? " = " + X(v.Init, t).T : "";
                        return DeclFor(v.Symbol, t, v.Init, v.TypeExplicit) + init;
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
            if (decls.Count == f.Init.Count && decls.Count > 1 && decls.All(dd => TypeText(SymType(dd.Symbol)) == TypeText(SymType(decls[0].Symbol))))
                init = TypeText(SymType(decls[0].Symbol)) + " " + string.Join(", ", decls.Select(dd => { var iv = dd.Init != null ? " = " + X(dd.Init, SymType(dd.Symbol)).T : ""; return Declare(dd.Symbol) + iv; }));
            else init = string.Join(", ", f.Init.Select(SimpleStmtText));
            if (C && f.Init.Count > 1 && !(decls.Count == f.Init.Count)) Problem("a for header with several different statements");
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
            if (!typed && HasCall(rf.End))
            {
                var tmp = new IrSymbol("end", IrSymbolKind.Local, IrType.Int);
                var tn = Declare(tmp);
                Line("int " + tn + " = " + end.T + ";");
                end = new Ex(tn, PrecAtom);
            }
            string v;
            bool declareHere = rf.Var == null || headerDeclared.Contains(rf.Var) || typed;
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
            IrExpr coll = it;
            bool overMap = false;
            if (it is IrOpCall mk && mk.Op == IrOp.MapKeys) { coll = mk.Args[0]; overMap = true; }
            else if (itT.Kind == IrTypeKind.Map) overMap = true;
            var collT = TypeOf(coll);
            IrType elem = overMap ? collT.Key ?? IrType.Unknown : collT.Elem ?? IrType.Unknown;
            if (collT.Kind == IrTypeKind.String) Problem("iterating over the characters of a string is not supported in JavaFarmer");
            if (fe.Var != null && SymType(fe.Var).IsKnown && SymType(fe.Var).Kind != IrTypeKind.Any) elem = SymType(fe.Var);
            bool own = fe.Var == null || headerDeclared.Contains(fe.Var) || typed;
            string v = own ? Declare(fe.Var) : Declare(new IrSymbol(fe.Var.Name + "_item", IrSymbolKind.Local, elem));
            var src = coll is IrListLit || coll is IrSetLit || coll is IrMapLit ? X(coll, collT).T : Postfix(coll);
            if (Cpp && IsOptional(coll) && collT.IsCollection) src = "*" + src;
            if (C)
            {
                if (collT.Kind == IrTypeKind.Array)
                {
                    // arrays: an index loop
                    var idx = Declare(new IrSymbol("i", IrSymbolKind.Local, IrType.Int));
                    Line("for (int " + idx + " = 0; " + idx + " < (int) (sizeof " + src + " / sizeof " + src + "[0]); " + idx + "++) {", fe);
                    Indent();
                    Line(Decl(elem, v) + " = " + src + "[" + idx + "];");
                    Dedent();
                }
                else
                {
                    if (!(collT.Kind == IrTypeKind.List || collT.Kind == IrTypeKind.Set || collT.Kind == IrTypeKind.Map || collT.Kind == IrTypeKind.Unknown)) Problem("for_each over " + collT + " is not supported in C");
                    Line("for_each (" + Decl(elem.IsKnown ? elem : IrType.Int, v) + ", " + src + ") {", fe);
                }
            }
            else
            {
                if (overMap)
                {
                    var vt = fe.ValueVar != null ? SymType(fe.ValueVar) : collT.Elem;
                    string valueName = fe.ValueVar != null ? Declare(fe.ValueVar) : Declare(new IrSymbol("value", IrSymbolKind.Local, vt));
                    Line("for (" + (typed ? "auto& " : "const auto& ") + "[" + v + ", " + valueName + "] : " + src + ") {", fe);
                }
                else
                {
                    var type = elem.IsKnown && elem.Kind != IrTypeKind.Any ? TypeText(elem) : "auto";
                    Line("for (" + type + " " + v + " : " + src + ") {", fe);
                }
            }
            Indent();
            PushScope();
            if (!own) Line(Name(fe.Var) + " = " + v + ";");
            if (fe.ValueVar != null && C)
            {
                var vt = SymType(fe.ValueVar);
                if (!vt.IsKnown) vt = collT.Elem ?? IrType.Unknown;
                Line(Decl(vt, Declare(fe.ValueVar)) + " = map_get(" + src + ", " + v + ");");
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
            var selT = TypeOf(sw.Selector);
            bool intLabels = sw.Cases.All(c => c.Labels.All(l => l is IrLiteral li && li.Kind == IrLitKind.Int || l is IrUnary u && u.Operand is IrLiteral));
            bool loopBreak = sw.Cases.Any(c => BreaksOut(c.Body));
            if (!intLabels || loopBreak || !(selT.Kind == IrTypeKind.Int || selT.Kind == IrTypeKind.Unknown && intLabels)) { SwitchAsIf(sw); return; }
            Line("switch (" + X(sw.Selector).T + ") {", sw);
            foreach (var c in sw.Cases)
            {
                LeadingComments(c);
                if (c.IsDefault) Line("default:");
                else foreach (var l in c.Labels) Line("case " + X(l, selT).T + ":");
                Indent();
                PushScope();
                var body = c.Body?.Stmts ?? new List<IrStmt>();
                bool needsBraces = body.Any(x => x is IrVarDecl) || (!typed && declareBefore.Keys.Any(k => body.Contains(k)));
                if (needsBraces) { Line("{"); Indent(); }
                Stmts(body);
                Comments(c.Body?.TrailingComments);
                if (needsBraces) { Dedent(); Line("}"); }
                if (body.Count == 0 || CanComplete(new IrBlock(body))) Line("break;");
                PopScope();
                Dedent();
            }
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
                Line(Decl(selT.IsKnown ? selT : IrType.Int, sel) + " = " + init + ";");
            }
            var normal = sw.Cases.Where(c => !c.IsDefault).ToList();
            var dflt = sw.Cases.FirstOrDefault(c => c.IsDefault);
            bool first = true;
            foreach (var c in normal)
            {
                var conds = c.Labels.Select(l => EqText(new Ex(sel, PrecAtom), selT, X(l, selT), TypeOf(l), false)).ToList();
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
            // printf buffer (text without a trailing \n): the assignments belong to the printf that made them
            if (a.Target is IrName bn && IsBuffer(bn.Symbol))
            {
                if (a.Op == IrBinOp.Concat) { PrintText(new List<IrExpr> { a.Value }, false, false, a); return; }
                return;   // __jout = "" after a printed line
            }
            if (a.Target is IrTupleLit tl) { Unpack(tl, a); return; }
            // the copy a C++ by-value parameter makes (the binder's synthetic xs = copy(xs)) is implicit in C++
            if (a.Synthetic && a.Value is IrOpCall sc && sc.Synthetic && (sc.Op == IrOp.ListCopy || sc.Op == IrOp.SetCopy || sc.Op == IrOp.MapCopy) && Cpp) return;
            if (declaringAssign.Contains(a))
            {
                var s = NameSym(a.Target);
                var t = SymType(s);
                if (a.Value is IrLambda lam)
                {
                    if (C) { Problem("closures (a function defined inside a function) are not supported in C"); Line("// unsupported: nested function " + s.Name, a); return; }
                    var lx = X(lam).T;
                    Line("auto " + Declare(s) + " = " + lx + ";", a);
                    return;
                }
                var declText = DeclFor(s, t, a.Value, true);
                var val = Cpp && !declText.StartsWith("auto") && (a.Value is IrListLit || a.Value is IrSetLit || a.Value is IrMapLit) ? BraceInit(a.Value, t) : X(a.Value, t).T;
                Line(declText + " = " + val + ";", a);
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
            if (target is IrIndex ix)
            {
                var recvT = TypeOf(ix.Target);
                if (C && (recvT.Kind == IrTypeKind.List || recvT.Kind == IrTypeKind.Map || (recvT.Kind == IrTypeKind.Unknown && !typed)))
                {
                    var fn = recvT.Kind == IrTypeKind.Map ? "map_put" : "list_set";
                    var key = recvT.Kind == IrTypeKind.Map ? X(ix.Index, recvT.Key).T : IndexText(ix);
                    string value;
                    if (a.Op == null) value = X(a.Value, recvT.Elem).T;
                    else
                    {
                        if (!IsSimple(ix.Target) || !IsSimple(ix.Index)) Problem("compound assignment to a computed list/map element");
                        var cur = new IrIndex { Target = ix.Target, Index = ix.Index, BaseOffset = ix.BaseOffset, Type = ix.Type };
                        value = BinText(a.Op.Value, cur, a.Value, a.Value.Type).T;
                    }
                    return fn + "(" + X(ix.Target).T + ", " + key + ", " + value + ")";
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
                case IrBinOp.Concat:
                    if (C) return tx + " = " + Concat(target, a.Value).T;
                    return tx + " += " + StrOperand(a.Value).T;
                case IrBinOp.Add:
                    if (IsStr(target) || IsStr(a.Value)) goto case IrBinOp.Concat;
                    return tx + " += " + X(a.Value, tt).T;
                case IrBinOp.Sub: return tx + " -= " + X(a.Value, tt).T;
                case IrBinOp.Mul: return tx + " *= " + X(a.Value, tt).T;
                case IrBinOp.ModTrunc:
                    if (intTarget && IsInt(a.Value)) return tx + " %= " + X(a.Value, tt).T;
                    break;
                case IrBinOp.IntDivTrunc: if (intTarget && IsInt(a.Value)) return tx + " /= " + X(a.Value, tt).T; break;
                case IrBinOp.TrueDiv:
                    if (intTarget && !a.TruncateToInt && IsInt(a.Value)) return tx + " = " + BinText(op, target, a.Value, IrType.Float).T;
                    return tx + " /= " + X(a.Value, tt).T;
            }
            if (!(target is IrName) && !IsSimple(target)) Problem("compound assignment on a computed target");
            var rhs = BinText(op, target, a.Value, null);
            if (intTarget && (op == IrBinOp.Pow || !IsInt(a.Value) && op != IrBinOp.IntDivFloor)) return tx + " = (int) " + Wrap(rhs.T, rhs.P, PrecUnary);
            return tx + " = " + rhs.T;
        }

        string TargetText(IrExpr t)
        {
            if (t is IrName n) return NameRef(n);
            if (t is IrIndex ix && Cpp && TypeOf(ix.Target).Kind == IrTypeKind.Map) return Postfix(ix.Target) + "[" + X(ix.Index, TypeOf(ix.Target).Key).T + "]";
            return X(t).T;
        }

        void Unpack(IrTupleLit targets, IrAssign a)
        {
            if (a.Op != null) { Problem("compound assignment to several variables"); Line("// unsupported compound tuple assignment", a); return; }
            if (a.Value is IrTupleLit vals && vals.Items.Count == targets.Items.Count)
            {
                var tsyms = targets.Items.Select(NameSym).ToList();
                bool reads = vals.Items.Any(v => tsyms.Any(s => s != null && Mentions(v, s)));
                if (!reads)
                {
                    for (int i = 0; i < targets.Items.Count; i++) AssignOne(targets.Items[i], vals.Items[i], i == 0 ? a : null);
                    return;
                }
                var temps = new List<string>();
                for (int i = 0; i < vals.Items.Count; i++)
                {
                    var t = TypeOf(targets.Items[i]);
                    var init = X(vals.Items[i], t).T;
                    var tmp = Declare(new IrSymbol((NameSym(targets.Items[i])?.Name ?? "value") + "_new", IrSymbolKind.Local, t));
                    Line(Decl(t.IsKnown ? t : TypeOf(vals.Items[i]), tmp) + " = " + init + ";", i == 0 ? a : null);
                    temps.Add(tmp);
                }
                for (int i = 0; i < targets.Items.Count; i++) AssignOneText(targets.Items[i], temps[i], TypeOf(vals.Items[i]));
                return;
            }
            var vt = TypeOf(a.Value);
            bool pos = vt.IsGame("Position") || IsPositionTuple(vt) || ((vt.Kind == IrTypeKind.Any || !vt.IsKnown) && targets.Items.Count == 2 && targets.Items.All(x => x is IrName));
            bool comp = vt.IsGame("Companion");
            if ((pos || comp) && targets.Items.Count == 2)
            {
                if (pos && C && (vt.Kind == IrTypeKind.Any || !vt.IsKnown) && !(a.Value is IrCall mcc && mcc.Builtin == "measure")) Problem("unpacking a value of unknown type (measure() returns a number in C; use measure_pos())");
                string holder;
                if (IsSimple(a.Value)) holder = X(a.Value).T;
                else
                {
                    var tn = pos ? IrType.Game("Position") : IrType.Game("Companion");
                    var init = C && pos && a.Value is IrCall mc && mc.Kind == IrCallKind.Builtin && mc.Builtin == "measure"
                        ? "measure_pos(" + string.Join(", ", mc.Args.Select(x => X(x).T)) + ")" : X(a.Value).T;
                    holder = Declare(new IrSymbol(pos ? "pos" : "companion", IrSymbolKind.Local, tn));
                    Line((Cpp && comp ? "auto" : TypeText(tn)) + " " + holder + " = " + init + ";", a);
                }
                var acc = comp && (Cpp || C) ? "->" : ".";
                if (pos)
                {
                    AssignOneText(targets.Items[0], holder + ".x", IrType.Int);
                    AssignOneText(targets.Items[1], holder + ".y", IrType.Int);
                }
                else
                {
                    AssignOneText(targets.Items[0], holder + acc + "entity", IrType.Game("Entity"));
                    if (targets.Items[1] is IrTupleLit inner && inner.Items.Count == 2)
                    {
                        AssignOneText(inner.Items[0], holder + acc + "position.x", IrType.Int);
                        AssignOneText(inner.Items[1], holder + acc + "position.y", IrType.Int);
                    }
                    else AssignOneText(targets.Items[1], holder + acc + "position", IrType.Game("Position"));
                }
                return;
            }
            Problem("unpacking a tuple value is not supported in " + Lang);
            Line("// unsupported: " + string.Join(", ", targets.Items.Select(t => X(t).T)) + " = " + X(a.Value).T, a);
        }

        static bool IsPositionTuple(IrType t) => t.Kind == IrTypeKind.Tuple && t.Items != null && t.Items.Count == 2 && t.Items.All(i => i.IsNumeric);

        void AssignOne(IrExpr target, IrExpr value, IrNode commentsOf)
        {
            var t = TypeOf(target);
            AssignOneText(target, X(value, t).T, TypeOf(value), commentsOf);
        }

        void AssignOneText(IrExpr target, string valueText, IrType valueType, IrNode commentsOf = null)
        {
            var s = NameSym(target);
            if (s != null && unpackDeclared.Contains(s) && !declared.Contains(s))
            {
                declared.Add(s);
                var t = SymType(s);
                if (!t.IsKnown || (t.Kind == IrTypeKind.Any && valueType != null && valueType.IsKnown)) t = valueType;
                Line((t != null && t.IsKnown ? Decl(t, Declare(s)) : (Cpp ? "auto " + Declare(s) : Decl(IrType.Int, Declare(s)))) + " = " + valueText + ";", commentsOf);
                return;
            }
            if (target is IrIndex)
            {
                var text = AssignText(new IrAssign { Target = target, Value = new IrName(new IrSymbol(valueText, IrSymbolKind.Local)) { Type = valueType } });
                Line(text + ";", commentsOf);
                return;
            }
            Line(TargetText(target) + " = " + valueText + ";", commentsOf);
        }

        readonly HashSet<IrSymbol> declared = new HashSet<IrSymbol>();

        // ================================================================== printing statements

        void PrintStmt(IrCall pc, IrNode commentsOf, bool newline)
        {
            var args = pc.Args.ToList();
            // print(__jout + text): the buffered start of the line was printed by earlier printf calls
            if (args.Count == 1 && args[0] is IrBinary cb && cb.Op == IrBinOp.Concat && cb.Synthetic && FirstIsBuffer(cb, out var rest))
                args = rest == null ? new List<IrExpr>() : new List<IrExpr> { rest };
            else if (args.Count == 1 && args[0] is IrName bn && IsBuffer(bn.Symbol)) args = new List<IrExpr>();
            if (pc.Builtin == "quick_print") { PrintText(args, true, true, commentsOf); return; }
            PrintText(args, newline, false, commentsOf);
        }

        static bool FirstIsBuffer(IrBinary b, out IrExpr rest)
        {
            rest = null;
            if (b.Left is IrName n && IsBuffer(n.Symbol)) { rest = b.Right; return true; }
            if (b.Left is IrBinary lb && lb.Op == IrBinOp.Concat && FirstIsBuffer(lb, out var r))
            {
                rest = r == null ? b.Right : new IrBinary { Op = IrBinOp.Concat, Left = r, Right = b.Right, Synthetic = b.Synthetic, Type = IrType.String };
                return true;
            }
            return false;
        }

        sealed class PSeg { public string Text; public IrExpr Value; }

        /// <summary>The printed text as literal pieces and values (Python print(a, b) joins with a space).</summary>
        List<PSeg> Segments(List<IrExpr> args)
        {
            var segs = new List<PSeg>();
            void Add(IrExpr e)
            {
                switch (e)
                {
                    case IrLiteral l when l.Kind == IrLitKind.String: segs.Add(new PSeg { Text = l.Str }); return;
                    case IrBinary b when b.Op == IrBinOp.Concat || b.Op == IrBinOp.Add && (IsStr(b.Left) || IsStr(b.Right)):
                        Add(b.Left); Add(b.Right); return;
                    case IrCast c when c.Target != null && c.Target.IsString && !c.IsTypeAssertion && !IsStr(c.Operand):
                        segs.Add(new PSeg { Value = c.Operand }); return;
                }
                segs.Add(new PSeg { Value = e });
            }
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) segs.Add(new PSeg { Text = " " });
                Add(args[i]);
            }
            // adjacent text pieces print as one literal
            var merged = new List<PSeg>();
            foreach (var sg in segs)
            {
                if (sg.Text != null && merged.Count > 0 && merged[merged.Count - 1].Text != null) merged[merged.Count - 1].Text += sg.Text;
                else merged.Add(sg);
            }
            return merged;
        }

        void PrintText(List<IrExpr> args, bool newline, bool quick, IrNode commentsOf)
        {
            var segs = Segments(args);
            if (C)
            {
                var fmt = new StringBuilder();
                var vals = new List<string>();
                foreach (var s in segs)
                {
                    if (s.Text != null) { fmt.Append(EscapeFmt(s.Text, true)); continue; }
                    var t = TypeOf(s.Value);
                    if (t.Kind == IrTypeKind.Int) { fmt.Append("%d"); vals.Add(X(s.Value).T); }
                    else if (t.Kind == IrTypeKind.Float) { fmt.Append("%g"); vals.Add(X(s.Value).T); }
                    else if (t.Kind == IrTypeKind.String) { fmt.Append("%s"); vals.Add(X(s.Value).T); }
                    else if (t.Kind == IrTypeKind.Bool && (s.Value is IrBinary || s.Value is IrUnary)) { fmt.Append("%s"); vals.Add("str_of((bool) (" + X(s.Value).T + "))"); }
                    else { fmt.Append("%s"); vals.Add("str_of(" + X(s.Value).T + ")"); if (!t.IsKnown) Problem("printing a value of unknown type"); }
                }
                if (newline && !quick) fmt.Append("\\n");
                if (!quick) Need("stdio.h");
                Line((quick ? "quick_print" : "printf") + "(\"" + fmt + "\"" + string.Concat(vals.Select(v => ", " + v)) + ");", commentsOf);
                return;
            }
            // C++: std::cout for text and numbers, std::println when a value needs std::format (bools, collections)
            bool needFormat = quick || segs.Any(s => s.Value != null && NeedsFormat(TypeOf(s.Value)));
            if (needFormat)
            {
                var fmt = new StringBuilder();
                var vals = new List<string>();
                foreach (var s in segs)
                {
                    if (s.Text != null) { fmt.Append(EscapeFmt(s.Text, false).Replace("{", "{{").Replace("}", "}}")); continue; }
                    fmt.Append("{}");
                    vals.Add(X(s.Value).T);
                }
                if (!quick) Need("print");
                var fn = quick ? "quick_print" : newline ? "std::println" : "std::print";
                Line(fn + "(\"" + fmt + "\"" + string.Concat(vals.Select(v => ", " + v)) + ");", commentsOf);
                return;
            }
            Need("iostream");
            var sb = new StringBuilder("std::cout");
            foreach (var s in segs)
            {
                if (s.Text != null) sb.Append(" << \"").Append(EscapeFmt(s.Text, false)).Append('"');
                else sb.Append(" << ").Append(W(X(s.Value), PrecAdd));
            }
            if (newline) sb.Append(" << std::endl");
            Line(sb + ";", commentsOf);
        }

        static bool NeedsFormat(IrType t) => t.Kind == IrTypeKind.Bool || t.IsCollection || t.Kind == IrTypeKind.Tuple || t.IsGame("Companion");

        static string EscapeFmt(string s, bool percent)
        {
            var e = Escape(s, '"');
            return percent ? e.Replace("%", "%%") : e;
        }

        // ================================================================== expressions

        struct Ex
        {
            public string T; public int P;
            public Ex(string t, int p) { T = t; P = p; }
        }

        const int PrecOr = 1, PrecAnd = 2, PrecEq = 3, PrecRel = 4, PrecAdd = 5, PrecMul = 6;

        IrType TypeOf(IrExpr e) => Vec(types.TypeOf(e));
        IrType SymType(IrSymbol s) => s == null ? IrType.Unknown : Vec(types.Sym(s));
        /// <summary>C++: fixed arrays of other languages (Java int[], Python lists made arrays) are std::vector.</summary>
        bool vectorArrays => Cpp && mod.SourceLanguage != "cpp" && mod.SourceLanguage != "c";
        IrType Vec(IrType t)
        {
            if (!vectorArrays || t == null || t.Kind != IrTypeKind.Array) return t;
            return IrType.List(Vec(t.Elem));
        }
        bool IsInt(IrExpr e) => TypeOf(e).Kind == IrTypeKind.Int;
        bool IsStr(IrExpr e) => TypeOf(e).IsString || (e is IrBinary b && b.Op == IrBinOp.Concat) || (e is IrCast c && c.Target != null && c.Target.IsString && !c.IsTypeAssertion);

        static bool IsSimple(IrExpr e) => e is IrName || e is IrLiteral || e is IrConst;

        string Postfix(IrExpr e) { var x = X(e); return Wrap(x.T, x.P, PrecPostfix); }
        static string W(Ex x, int min) => Wrap(x.T, x.P, min);

        string NameRef(IrName n)
        {
            var s = n.Symbol;
            if (s == null) { Problem("unresolved name"); return Null; }
            switch (s.Kind)
            {
                case IrSymbolKind.ExternalGlobal:
                    if (!typed) Problem("the global variable " + s.Name + " of another code window needs an extern declaration");
                    return Ident(s.Name);
                case IrSymbolKind.Function:
                case IrSymbolKind.ExternalFunction:
                    return s.Kind == IrSymbolKind.Function ? Name(s) : Ident(s.Name);
            }
            return Name(s);
        }

        string Null => C ? "NULL" : "nullptr";

        Ex X(IrExpr e, IrType expected = null)
        {
            switch (e)
            {
                case null: return new Ex(Null, PrecAtom);
                case IrLiteral l: return Literal(l, expected);
                case IrName n:
                    if (IsBuffer(n.Symbol)) { Problem("the printf buffer used as a value"); return new Ex("\"\"", PrecAtom); }
                    return new Ex(NameRef(n), PrecAtom);
                case IrConst c: return new Ex(c.Category == null || c.Category == "Directions" ? c.Name : c.Category + (C ? "." : "::") + c.Name, PrecPostfix);
                case IrUnary u:
                    {
                        if (u.Op == IrUnOp.Not)
                        {
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
                    if (tl.Items.Count == 2)
                    {
                        var parts = tl.Items.Select(i => X(i, IrType.Int).T);
                        return new Ex(C ? "(Position){" + string.Join(", ", parts) + "}" : "Position{" + string.Join(", ", parts) + "}", PrecPostfix);
                    }
                    Problem("tuples are not supported in " + Lang + " (only (x, y) positions)");
                    return new Ex(Null, PrecAtom);
                case IrLambda lam: return Lambda(lam);
                case IrFuncRef fr: return FuncRef(fr);
                case IrCast ca: return Cast(ca);
            }
            Problem("unsupported expression " + e.GetType().Name);
            return new Ex(Null, PrecAtom);
        }

        Ex Literal(IrLiteral l, IrType expected)
        {
            switch (l.Kind)
            {
                case IrLitKind.Int:
                    return new Ex(l.Int.ToString(CultureInfo.InvariantCulture), l.Int < 0 ? PrecUnary : PrecAtom);
                case IrLitKind.Float:
                    {
                        var t = FormatFloat(l.Float);
                        return new Ex(t, t.StartsWith("-") ? PrecUnary : PrecAtom);
                    }
                case IrLitKind.Bool: return new Ex(l.Bool ? "true" : "false", PrecAtom);
                case IrLitKind.Null:
                    if (Cpp && expected != null && expected.Kind == IrTypeKind.String) Problem("a std::string cannot be nullptr");
                    return new Ex(Null, PrecAtom);
                default:
                    {
                        var s = l.Str ?? "";
                        if (s.IndexOf('\n') >= 0) Problem("line breaks inside strings are not supported by the game");
                        return new Ex("\"" + Escape(s) + "\"", PrecAtom);
                    }
            }
        }

        /// <summary>A condition: Python truthiness made explicit.</summary>
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
                case IrTypeKind.Int: case IrTypeKind.Float: return typed ? x : new Ex(W(x, PrecRel + 1) + " != 0", PrecEq);
                case IrTypeKind.String:
                    if (C) return new Ex("strlen(" + x.T + ") > 0", PrecRel);
                    return new Ex("!" + W(x, PrecPostfix) + ".empty()", PrecUnary);
                case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Map:
                    if (t.Nullable && Cpp) return x;
                    if (C) return new Ex(OpName(t, "len") + "(" + x.T + ") > 0", PrecRel);
                    return new Ex("!" + W(x, PrecPostfix) + ".empty()", PrecUnary);
                case IrTypeKind.Game: case IrTypeKind.Func:
                    if (typed) return x;
                    if (Cpp && t.IsGame("Companion")) return x;
                    return new Ex(W(x, PrecRel + 1) + " != " + Null, PrecEq);
            }
            return x;
        }

        Ex Bin(string op, int prec, IrExpr l, IrExpr r)
        {
            var a = X(l); var b = X(r);
            return new Ex(W(a, prec) + " " + op + " " + W(b, prec + 1), prec);
        }

        Ex BinText(IrBinOp op, IrExpr l, IrExpr r, IrType resultType)
        {
            bool ints = IsInt(l) && IsInt(r);
            switch (op)
            {
                case IrBinOp.Add:
                    if (IsStr(l) || IsStr(r)) return Concat(l, r);
                    if (TypeOf(l).Kind == IrTypeKind.List || TypeOf(r).Kind == IrTypeKind.List) Problem("joining lists with + is not supported in " + Lang);
                    return Bin("+", PrecAdd, l, r);
                case IrBinOp.Concat: return Concat(l, r);
                case IrBinOp.Sub: return Bin("-", PrecAdd, l, r);
                case IrBinOp.Mul:
                    if (IsStr(l) || IsStr(r) || TypeOf(l).Kind == IrTypeKind.List || TypeOf(r).Kind == IrTypeKind.List) Problem("repeating a string or list with * is not supported in " + Lang);
                    return Bin("*", PrecMul, l, r);
                case IrBinOp.TrueDiv:
                    if (ints) return new Ex("(double) " + W(X(l), PrecUnary) + " / " + W(X(r), PrecMul + 1), PrecMul);
                    return Bin("/", PrecMul, l, r);
                case IrBinOp.IntDivTrunc:
                    if (ints || typed) return Bin("/", PrecMul, l, r);
                    return new Ex("(int) (" + Bin("/", PrecMul, l, r).T + ")", PrecUnary);
                case IrBinOp.IntDivFloor:
                    if (ints && IsNonNeg(l) && IsNonNeg(r)) return Bin("/", PrecMul, l, r);
                    NeedMath();
                    if (ints) return new Ex("(int) " + MathFn("floor") + "((double) " + W(X(l), PrecUnary) + " / " + W(X(r), PrecMul + 1) + ")", PrecUnary);
                    return new Ex(MathFn("floor") + "(" + Bin("/", PrecMul, l, r).T + ")", PrecPostfix);
                case IrBinOp.ModTrunc:
                    if (ints || typed && !(TypeOf(l).Kind == IrTypeKind.Float || TypeOf(r).Kind == IrTypeKind.Float)) return Bin("%", PrecMul, l, r);
                    NeedMath();
                    return new Ex(MathFn("fmod") + "(" + X(l).T + ", " + X(r).T + ")", PrecPostfix);
                case IrBinOp.ModFloor:
                    if (ints && IsNonNeg(l) && IsNonNeg(r)) return Bin("%", PrecMul, l, r);
                    if (ints)
                    {
                        if (!IsSimple(r) || !IsPureForDup(l)) Problem("Python's % of a computed negative value");
                        var bx = W(X(r), PrecMul + 1);
                        return new Ex("(" + W(X(l), PrecMul) + " % " + bx + " + " + bx + ") % " + bx, PrecMul);
                    }
                    NeedMath();
                    return new Ex(MathFn("fmod") + "(" + MathFn("fmod") + "(" + X(l).T + ", " + X(r).T + ") + " + W(X(r), PrecAdd + 1) + ", " + X(r).T + ")", PrecPostfix);
                case IrBinOp.Pow:
                    {
                        NeedMath();
                        var call = MathFn("pow") + "(" + X(l).T + ", " + X(r).T + ")";
                        bool intResult = resultType != null && resultType.Kind == IrTypeKind.Int || (resultType == null || !resultType.IsKnown) && ints && !typed;
                        return intResult ? new Ex("(int) " + call, PrecUnary) : new Ex(call, PrecPostfix);
                    }
                case IrBinOp.Eq: case IrBinOp.Ne:
                    return EqText(X(l), TypeOf(l), X(r), TypeOf(r), op == IrBinOp.Ne);
                case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge:
                    {
                        if (IsStr(l) && IsStr(r)) Problem("comparing strings with < > is not supported in JavaFarmer");
                        var s = op == IrBinOp.Lt ? "<" : op == IrBinOp.Le ? "<=" : op == IrBinOp.Gt ? ">" : ">=";
                        return Bin(s, PrecRel, l, r);
                    }
                case IrBinOp.And: case IrBinOp.Or:
                    {
                        var prec = op == IrBinOp.And ? PrecAnd : PrecOr;
                        var a = Cond(l);
                        var b = Cond(r);
                        return new Ex(W(a, prec) + (op == IrBinOp.And ? " && " : " || ") + W(b, prec + 1), prec);
                    }
            }
            Problem("unsupported operator " + op);
            return new Ex(Null, PrecAtom);
        }

        static bool IsPureForDup(IrExpr e) => !IrWalk.Descendants(e).Any(n => n is IrCall);

        void NeedMath() => Need(C ? "math.h" : "cmath");
        string MathFn(string f) => C ? f : "std::" + f;

        Ex EqText(Ex a, IrType at, Ex b, IrType bt, bool ne)
        {
            if ((a.T == Null && (bt.Kind == IrTypeKind.Any || !bt.IsKnown)) || (b.T == Null && (at.Kind == IrTypeKind.Any || !at.IsKnown)))
                Problem("comparing a value of unknown type (measure(), wait_for()) with " + Null);
            if (at.IsString && bt.IsString && a.T != Null && b.T != Null)
            {
                if (C)
                {
                    if (ne) { Need("string.h"); return new Ex("strcmp(" + a.T + ", " + b.T + ") != 0", PrecEq); }
                    return new Ex("str_eq(" + a.T + ", " + b.T + ")", PrecPostfix);
                }
                if (a.T.StartsWith("\"") && b.T.StartsWith("\"")) a = new Ex("std::string(" + a.T + ")", PrecPostfix);
            }
            if (C && (at.IsCollection || IsPositionTuple(at) || at.IsGame("Position")) && b.T != Null) Problem("comparing collections or positions with == is not supported in C");
            return new Ex(W(a, PrecEq) + (ne ? " != " : " == ") + W(b, PrecEq + 1), PrecEq);
        }

        /// <summary>A value as text: C str_of(x), C++ std::to_string(x) / std::format("{}", x).</summary>
        Ex StrOperand(IrExpr e)
        {
            if (C && TypeOf(e).Kind == IrTypeKind.Bool && (e is IrBinary || e is IrUnary)) return new Ex("str_of((bool) (" + X(e).T + "))", PrecPostfix);
            if (IsStr(e))
            {
                var x = X(e);
                return x;
            }
            var t = TypeOf(e);
            if (C) return new Ex("str_of(" + X(e).T + ")", PrecPostfix);
            if (t.IsNumeric) return new Ex("std::to_string(" + X(e).T + ")", PrecPostfix);
            Need("format");
            return new Ex("std::format(\"{}\", " + X(e).T + ")", PrecPostfix);
        }

        Ex Concat(IrExpr l, IrExpr r)
        {
            var a = StrOperand(l); var b = StrOperand(r);
            if (C) return new Ex("str_concat(" + a.T + ", " + b.T + ")", PrecPostfix);
            if (a.T.StartsWith("\"") && (b.T.StartsWith("\"") || !IsStdStringExpr(r))) a = new Ex("std::string(" + a.T + ")", PrecPostfix);
            return new Ex(W(a, PrecAdd) + " + " + W(b, PrecAdd + 1), PrecAdd);
        }

        bool IsStdStringExpr(IrExpr e) => !(e is IrLiteral);

        bool IsNonNeg(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l: return l.Kind == IrLitKind.Int && l.Int >= 0;
                case IrBinary b when b.Op == IrBinOp.Add || b.Op == IrBinOp.Mul || b.Op == IrBinOp.IntDivFloor || b.Op == IrBinOp.IntDivTrunc || b.Op == IrBinOp.ModFloor || b.Op == IrBinOp.ModTrunc:
                    return IsNonNeg(b.Left) && IsNonNeg(b.Right);
                case IrCall c when c.Kind == IrCallKind.Builtin: return GameApi.Find(c.Builtin)?.NonNegative ?? false;
                case IrOpCall o: return o.Op == IrOp.Len;
            }
            return false;
        }

        // ------------------------------------------------------------------ calls

        string ArgsText(IEnumerable<string> a) => "(" + string.Join(", ", a) + ")";

        /// <summary>The call printed as a whole expression statement (its value is not used).</summary>
        IrExpr stmtCall;

        Ex Call(IrCall c)
        {
            switch (c.Kind)
            {
                case IrCallKind.Builtin:
                    if (c.Builtin == "wait_for" && c != stmtCall) Problem("the result of wait_for (a drone function's return value) cannot be used in " + Lang);
                    return BuiltinCall(c.Builtin, c.Args);
                case IrCallKind.Function:
                    {
                        var s = c.Function;
                        if (s == null) { Problem("call of an unknown function"); return new Ex(Null, PrecAtom); }
                        var args = c.Args.Select((a, i) => ArgFor(s, i, a)).ToList();
                        var fn = s.Function;
                        if (fn != null && args.Count < fn.Params.Count && !Cpp)
                        {
                            // C has no default arguments: pass the default value explicitly
                            for (int i = args.Count; i < fn.Params.Count; i++)
                            {
                                if (fn.Params[i].Default == null) { Problem("missing argument"); break; }
                                args.Add(X(fn.Params[i].Default, SymType(fn.Params[i].Symbol)).T);
                            }
                        }
                        var name = s.Kind == IrSymbolKind.ExternalFunction ? Ident(s.Name) : Name(s);
                        return new Ex(name + ArgsText(args), PrecPostfix);
                    }
                case IrCallKind.Value:
                    {
                        var ft = TypeOf(c.Callee);
                        if (c.Args.Count > 0 && C) Problem("calling a function value with arguments is not supported in C");
                        var callee = c.Callee is IrCall || c.Callee is IrName || c.Callee is IrFuncRef ? X(c.Callee).T : "(" + X(c.Callee).T + ")";
                        return new Ex(callee + ArgsText(c.Args.Select(a => X(a).T)), PrecPostfix);
                    }
            }
            Problem("unsupported call");
            return new Ex(Null, PrecAtom);
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
                case "print": case "quick_print":
                    Problem(b + "() used as a value");
                    return new Ex("0", PrecAtom);
                case "min": case "max":
                    {
                        if (args.Count < 2) { Problem(b + "() of a collection is not supported in " + Lang); return new Ex(ApiN(b) + ArgsText(args.Select(a => X(a).T)), PrecPostfix); }
                        if (Cpp) Need("algorithm");
                        bool mixed = Cpp && args.Any(IsInt) && !args.All(IsInt);
                        string Arg(IrExpr a) => mixed && IsInt(a) ? "(double) " + W(X(a), PrecUnary) : X(a).T;
                        var acc = Arg(args[args.Count - 1]);
                        for (int i = args.Count - 2; i >= 0; i--) acc = ApiN(b) + "(" + Arg(args[i]) + ", " + acc + ")";
                        return new Ex(acc, PrecPostfix);
                    }
                case "abs": if (Cpp) Need("cmath"); break;
            }
            var api = GameApi.Find(b);
            if (api == null) Problem("unknown builtin " + b);
            if (b == "wait_for" || b == "measure" && C) { }
            var parts = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                IrType pt = api != null && api.Params.Count > 0 ? api.Params[Math.Min(i, api.Params.Count - 1)].Type : null;
                if (api != null && api.Params.Count > i && api.Params[i].TypeText == "func") parts.Add(FuncArg(args[i]));
                else parts.Add(X(args[i], pt).T);
            }
            return new Ex(ApiN(b) + ArgsText(parts), PrecPostfix);
        }

        string FuncArg(IrExpr a)
        {
            if (a is IrFuncRef fr && fr.Builtin != null && C) Problem("a builtin passed as a function value is not supported in C (write a void function that calls it)");
            return X(a).T;
        }

        string ApiN(string py) => CFamilyLanguage.ApiNameOf(py, d);

        Ex FuncRef(IrFuncRef fr)
        {
            if (fr.Function != null) return new Ex(fr.Function.Kind == IrSymbolKind.ExternalFunction ? Ident(fr.Function.Name) : Name(fr.Function), PrecAtom);
            if (fr.Builtin != null)
            {
                if (Cpp) return new Ex(ApiN(fr.Builtin), PrecAtom);
                return new Ex(ApiN(fr.Builtin), PrecAtom);
            }
            return new Ex(Null, PrecAtom);
        }

        Ex Lambda(IrLambda lam)
        {
            if (C) { Problem("closures (lambdas / nested functions) are not supported in C"); return new Ex(Null, PrecAtom); }
            PushScope();
            var ps = string.Join(", ", lam.Params.Select(p => Decl(SymType(p.Symbol).IsKnown ? SymType(p.Symbol) : IrType.Int, Declare(p.Symbol))));
            string text;
            var outerRet = CurrentReturn; bool outerEntry = inEntry;
            inEntry = false;
            CurrentReturn = lam.ReturnType;
            if (lam.BodyExpr != null)
            {
                var bx = X(lam.BodyExpr).T;
                text = "[&](" + ps + ") { " + (lam.ReturnsValue ? "return " : "") + bx + "; }";
            }
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
                text = inner.Length == 0 ? "[&](" + ps + ") {}" : "[&](" + ps + ") {" + Options.Newline + inner + IndentText + "}";
            }
            CurrentReturn = outerRet; inEntry = outerEntry;
            PopScope();
            return new Ex(text, PrecAtom);
        }

        Ex Cast(IrCast ca)
        {
            var t = ca.Target ?? IrType.Unknown;
            var ot = TypeOf(ca.Operand);
            switch (t.Kind)
            {
                case IrTypeKind.String:
                    if (ca.IsTypeAssertion) return X(ca.Operand);
                    return StrOperand(ca.Operand);
                case IrTypeKind.Int:
                    if (ot.IsString) Problem("converting a string to a number is not supported in JavaFarmer");
                    if (ot.Kind == IrTypeKind.Int && ca.Synthetic) return X(ca.Operand);
                    if (ca.Synthetic && typed) return X(ca.Operand);
                    { var ox = X(ca.Operand); if (ox.T.StartsWith("(int) ")) return ox; return new Ex("(int) " + W(ox, PrecUnary), PrecUnary); }
                case IrTypeKind.Float:
                    if (ot.IsString) Problem("converting a string to a number is not supported in JavaFarmer");
                    if (ca.Synthetic || ca.IsTypeAssertion) return X(ca.Operand);
                    return new Ex("(double) " + W(X(ca.Operand), PrecUnary), PrecUnary);
                case IrTypeKind.Bool: return Cond(ca.Operand);
                case IrTypeKind.Unknown: case IrTypeKind.Any: return X(ca.Operand);
            }
            return X(ca.Operand);
        }

        // ------------------------------------------------------------------ collection operations

        string OpName(IrType recv, string op)
        {
            switch (recv.Kind)
            {
                case IrTypeKind.Set: return "set_" + op;
                case IrTypeKind.Map: return "map_" + op;
            }
            return "list_" + op;
        }

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
                    if (lit < 0 && lit != long.MinValue) { Problem("a negative index on an array"); }
                    return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
                case IrTypeKind.String:
                    Problem("indexing a string is not supported in JavaFarmer");
                    return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
                case IrTypeKind.Map:
                    if (C) return new Ex("map_get(" + X(ix.Target).T + ", " + X(ix.Index, tt.Key).T + ")", PrecPostfix);
                    return new Ex(recv + (tt.Nullable ? "->" : ".") + "at(" + X(ix.Index, tt.Key).T + ")", PrecPostfix);
                case IrTypeKind.Tuple: case IrTypeKind.Game:
                    {
                        bool comp = tt.IsGame("Companion") || (tt.Kind == IrTypeKind.Tuple && tt.Items != null && tt.Items.Count == 2 && !tt.Items[0].IsNumeric);
                        var acc = comp ? "->" : ".";
                        if (lit == 0) return new Ex(recv + (comp ? acc + "entity" : ".x"), PrecPostfix);
                        if (lit == 1) return new Ex(recv + (comp ? acc + "position" : ".y"), PrecPostfix);
                        Problem("indexing a tuple is not supported in " + Lang);
                        return new Ex(recv, PrecPostfix);
                    }
                case IrTypeKind.List:
                case IrTypeKind.Unknown:
                    if (lit < 0 && lit != long.MinValue)
                    {
                        if (!IsSimple(ix.Target)) Problem("negative index on a computed list");
                        if (C) return new Ex("list_get(" + X(ix.Target).T + ", list_len(" + X(ix.Target).T + ") - " + (-lit) + ")", PrecPostfix);
                        return new Ex(recv + "[" + recv + ".size() - " + (-lit) + "]", PrecPostfix);
                    }
                    if (C) return new Ex("list_get(" + X(ix.Target).T + ", " + IndexText(ix) + ")", PrecPostfix);
                    return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
            }
            Problem("indexing " + tt + " is not supported in " + Lang);
            return new Ex(recv + "[" + IndexText(ix) + "]", PrecPostfix);
        }

        Ex Op(IrOpCall o)
        {
            var a = o.Args;
            if (a.Count == 0) { Problem("operation " + o.Op + " without arguments"); return new Ex(Null, PrecAtom); }
            var rt = TypeOf(a[0]);
            if (rt.Kind == IrTypeKind.Unknown && !typed)
            {
                if (o.Op == IrOp.MapPut || o.Op == IrOp.MapGetOrNull || o.Op == IrOp.MapGetOrDefault || o.Op == IrOp.MapRemove || o.Op == IrOp.MapRemoveIfPresent || o.Op == IrOp.MapKeys || o.Op == IrOp.MapValues) rt = IrType.Map(IrType.Unknown, IrType.Unknown);
                else if (o.Op == IrOp.SetAdd) rt = IrType.Set(IrType.Unknown);
                else rt = IrType.List(IrType.Unknown);
            }
            string R() => X(a[0]).T;
            string RP() => Postfix(a[0]);
            string A(int i, IrType t = null) => i < a.Count ? X(a[i], t).T : Null;
            Ex M(string text) => new Ex(text, PrecPostfix);
            bool opt = Cpp && rt.Nullable && (rt.Kind == IrTypeKind.Map);
            string dot = opt ? "->" : ".";
            switch (o.Op)
            {
                case IrOp.Len:
                    if (rt.Kind == IrTypeKind.Array) return new Ex("(int) (sizeof(" + R() + ") / sizeof(" + RP() + "[0]))", PrecUnary);
                    if (rt.Kind == IrTypeKind.String) return C ? M("(int) strlen(" + R() + ")") : new Ex("(int) " + RP() + ".size()", PrecUnary);
                    if (C) return M(OpName(rt, "len") + "(" + R() + ")");
                    return new Ex("(int) " + RP() + dot + "size()", PrecUnary);
                case IrOp.IsEmpty:
                    if (rt.Kind == IrTypeKind.String) return C ? new Ex("strlen(" + R() + ") == 0", PrecEq) : M(RP() + ".empty()");
                    if (C) return M(OpName(rt, "is_empty") + "(" + R() + ")");
                    return M(RP() + dot + "empty()");
                case IrOp.Contains:
                    if (rt.Kind == IrTypeKind.String) return C ? M("str_contains(" + R() + ", " + A(1) + ")") : M(RP() + ".contains(" + A(1) + ")");
                    if (C)
                    {
                        if (rt.Kind == IrTypeKind.Array) Problem("contains on an array is not supported in C");
                        return M((rt.Kind == IrTypeKind.Map ? "map_has" : OpName(rt, "contains")) + "(" + R() + ", " + A(1, rt.Kind == IrTypeKind.Map ? rt.Key : rt.Elem) + ")");
                    }
                    if (rt.Kind == IrTypeKind.List || rt.Kind == IrTypeKind.Array) { Need("algorithm"); return M("std::ranges::contains(" + R() + ", " + A(1, rt.Elem) + ")"); }
                    return M(RP() + dot + "contains(" + A(1, rt.Kind == IrTypeKind.Map ? rt.Key : rt.Elem) + ")");
                case IrOp.ListAppend: return C ? M("list_add(" + R() + ", " + A(1, rt.Elem) + ")") : M(RP() + ".push_back(" + A(1, rt.Elem) + ")");
                case IrOp.SetAdd: return C ? M("set_add(" + R() + ", " + A(1, rt.Elem) + ")") : M(RP() + ".insert(" + A(1, rt.Elem) + ")");
                case IrOp.ListInsert: return C ? M("list_insert(" + R() + ", " + A(1, IrType.Int) + ", " + A(2, rt.Elem) + ")") : M(RP() + ".insert(" + RP() + ".begin() + " + W(X(a[1], IrType.Int), PrecAdd + 1) + ", " + A(2, rt.Elem) + ")");
                case IrOp.ListPop:
                    if (C) return a.Count >= 2 ? M("list_remove_at(" + R() + ", " + A(1, IrType.Int) + ")") : M("list_pop(" + R() + ")");
                    Problem("pop() used as a value has no single C++ expression (use back() then pop_back())");
                    return M(RP() + ".back()");
                case IrOp.ListSet: return C ? M("list_set(" + R() + ", " + A(1, IrType.Int) + ", " + A(2, rt.Elem) + ")") : new Ex(RP() + "[" + A(1, IrType.Int) + "] = " + A(2, rt.Elem), PrecTernary);
                case IrOp.ListIndexOf:
                    if (C) return M("list_index_of(" + R() + ", " + A(1, rt.Elem) + ")");
                    Problem("indexOf has no simple C++ equivalent in JavaFarmer");
                    return M("0");
                case IrOp.RemoveValue:
                    if (C) return M(OpName(rt, "remove") + "(" + R() + ", " + A(1, rt.Elem) + ")");
                    if (rt.Kind == IrTypeKind.Set) return M(RP() + ".erase(" + A(1, rt.Elem) + ")");
                    Need("algorithm");
                    if (!IsSimple(a[0])) Problem("remove() on a computed vector");
                    return M(RP() + ".erase(std::find(" + RP() + ".begin(), " + RP() + ".end(), " + A(1, rt.Elem) + "))");
                case IrOp.RemoveValueIfPresent:
                    if (C)
                    {
                        Problem("remove-if-present has no single C expression");
                        return M(OpName(rt, "remove") + "(" + R() + ", " + A(1, rt.Elem) + ")");
                    }
                    if (rt.Kind == IrTypeKind.Set) return M(RP() + ".erase(" + A(1, rt.Elem) + ")");
                    Problem("removing a value that may be missing from a vector");
                    return M(RP() + ".erase(std::find(" + RP() + ".begin(), " + RP() + ".end(), " + A(1, rt.Elem) + "))");
                case IrOp.Clear: return C ? M(OpName(rt, "clear") + "(" + R() + ")") : M(RP() + dot + "clear()");
                case IrOp.MapPut: return C ? M("map_put(" + R() + ", " + A(1, rt.Key) + ", " + A(2, rt.Elem) + ")") : new Ex(RP() + "[" + A(1, rt.Key) + "] = " + A(2, rt.Elem), PrecTernary);
                case IrOp.MapGetOrNull:
                    Problem("a map lookup that may find nothing");
                    return C ? M("map_get(" + R() + ", " + A(1, rt.Key) + ")") : M(RP() + ".at(" + A(1, rt.Key) + ")");
                case IrOp.MapGetOrDefault:
                    if (C) return M("map_get_or(" + R() + ", " + A(1, rt.Key) + ", " + A(2, rt.Elem) + ")");
                    if (!IsSimple(a[0]) || !IsSimple(a[1])) Problem("get-or-default on computed values");
                    return new Ex(RP() + ".contains(" + A(1, rt.Key) + ") ? " + RP() + ".at(" + A(1, rt.Key) + ") : " + W(X(a[2], rt.Elem), PrecTernary), PrecTernary);
                case IrOp.MapRemove:
                case IrOp.MapRemoveIfPresent:
                    if (C) return M("map_remove(" + R() + ", " + A(1, rt.Key) + ")");
                    return M(RP() + ".erase(" + A(1, rt.Key) + ")");
                case IrOp.MapKeys:
                    if (C) return M("map_keys(" + R() + ")");
                    Problem("the keys of a map as a value (iterate the map instead)");
                    return M(R());
                case IrOp.MapValues:
                    if (C) return M("map_values(" + R() + ")");
                    Problem("the values of a map as a value (iterate the map instead)");
                    return M(R());
                case IrOp.ListCopy:
                    if (Cpp && o.Synthetic && typed) return X(a[0]);
                    if (Cpp && (rt.Kind == IrTypeKind.List || rt.Kind == IrTypeKind.Set || rt.Kind == IrTypeKind.Array)) return M(TypeText(IrType.List(rt.Elem)) + "(" + R() + ")");
                    if (C) { if (rt.Kind == IrTypeKind.Map) return M("map_keys(" + R() + ")"); if (rt.Kind == IrTypeKind.List) return M("list_copy(" + R() + ")"); Problem("converting " + rt + " to a list"); return M(R()); }
                    return M(TypeText(IrType.List(rt.Kind == IrTypeKind.Map ? rt.Key : rt.Elem)) + "(" + RP() + ".begin(), " + RP() + ".end())");
                case IrOp.SetCopy:
                    if (Cpp && o.Synthetic && typed) return X(a[0]);
                    if (Cpp && (rt.Kind == IrTypeKind.List || rt.Kind == IrTypeKind.Set)) return M(TypeText(IrType.Set(rt.Elem)) + "(" + R() + ")");
                    if (C) { if (rt.Kind == IrTypeKind.Set) return M("set_copy(" + R() + ")"); Problem("converting " + rt + " to a set"); return M(R()); }
                    return M(TypeText(IrType.Set(rt.Elem)) + "(" + RP() + ".begin(), " + RP() + ".end())");
                case IrOp.MapCopy:
                    if (Cpp && o.Synthetic && typed) return X(a[0]);
                    if (C) return M("map_copy(" + R() + ")");
                    return M(TypeText(rt) + "(" + R() + ")");
                case IrOp.ArrayNew: case IrOp.ArrayNew2:
                    {
                        var t = TypeOf(o);
                        if (C && t.Kind == IrTypeKind.Array) { Problem("an array as a value"); return M(Null); }
                        if (C) { Problem("a list of n copies (use a loop with list_add)"); return M(Null); }
                        if (o.Op == IrOp.ArrayNew)
                        {
                            var et = t.Elem ?? TypeOf(a[1]);
                            return M(TypeText(IrType.List(et)) + "(" + A(0, IrType.Int) + ", " + A(1, et) + ")");
                        }
                        var inner = t.Elem != null && (t.Elem.Kind == IrTypeKind.List || t.Elem.Kind == IrTypeKind.Array) ? t.Elem.Elem : TypeOf(a[2]);
                        var it = TypeText(IrType.List(inner));
                        return M("std::vector<" + it + ">(" + A(0, IrType.Int) + ", " + it + "(" + A(1, IrType.Int) + ", " + A(2, inner) + "))");
                    }
                case IrOp.Floor: case IrOp.Ceil: case IrOp.Round:
                    {
                        NeedMath();
                        var fn = MathFn(o.Op == IrOp.Floor ? "floor" : o.Op == IrOp.Ceil ? "ceil" : "round") + "(" + A(0) + ")";
                        // Java Math.round / floorDiv results are ints: real C / C++ return double
                        return TypeOf(o).Kind == IrTypeKind.Int ? new Ex("(int) " + fn, PrecUnary) : M(fn);
                    }
                case IrOp.Sqrt: NeedMath(); return M(MathFn("sqrt") + "(" + A(0) + ")");
                case IrOp.PosX: return M(RP() + ".x");
                case IrOp.PosY: return M(RP() + ".y");
                case IrOp.CompanionEntity: return M(RP() + "->entity");
                case IrOp.CompanionPos: return M(RP() + "->position");
            }
            Problem("unsupported operation " + o.Op);
            return new Ex(Null, PrecAtom);
        }

        // ------------------------------------------------------------------ literals of collections

        IrType LitType(IrExpr lit, IrType expected)
        {
            var t = TypeOf(lit);
            if (expected != null && expected.Kind == t.Kind && (!typed || !t.IsKnown)) return expected;
            return t;
        }

        string ElemName(IrType t)
        {
            if (t == null || !t.IsKnown || t.Kind == IrTypeKind.Any) { Problem("a collection whose element type is unknown"); return "int"; }
            switch (t.Kind)
            {
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "bool";
                case IrTypeKind.String: return "string";
                case IrTypeKind.Game: if (t.GameName == "Companion") Problem("a collection of companions is not supported in C"); return t.GameName;
                case IrTypeKind.Tuple: if (IsPositionTuple(t)) return "Position"; break;
            }
            Problem("farm.h has no collection of " + t);
            return "int";
        }

        Ex ListLit(IrListLit l, IrType expected = null)
        {
            var t = LitType(l, expected);
            var elem = t.Elem;
            if (l.IsArray)
                return new Ex("{" + string.Join(", ", l.Items.Select(i => X(i, elem).T)) + "}", PrecAtom);
            if (C)
            {
                if (l.Items.Count == 0) return new Ex("list_new(" + ElemName(elem) + ")", PrecPostfix);
                return new Ex("list_of(" + ElemName(elem) + ", " + string.Join(", ", l.Items.Select(i => X(i, elem).T)) + ")", PrecPostfix);
            }
            return new Ex(TypeText(IrType.List(elem)) + "{" + string.Join(", ", l.Items.Select(i => X(i, elem).T)) + "}", PrecPostfix);
        }

        Ex SetLit(IrSetLit s, IrType expected = null)
        {
            var t = LitType(s, expected);
            if (C)
            {
                if (s.Items.Count == 0) return new Ex("set_new(" + ElemName(t.Elem) + ")", PrecPostfix);
                return new Ex("set_of(" + ElemName(t.Elem) + ", " + string.Join(", ", s.Items.Select(i => X(i, t.Elem).T)) + ")", PrecPostfix);
            }
            return new Ex(TypeText(IrType.Set(t.Elem)) + "{" + string.Join(", ", s.Items.Select(i => X(i, t.Elem).T)) + "}", PrecPostfix);
        }

        Ex MapLit(IrMapLit m, IrType expected = null)
        {
            var t = LitType(m, expected);
            if (C)
            {
                if (m.Keys.Count == 0) return new Ex("map_new(" + ElemName(t.Key) + ", " + ElemName(t.Elem) + ")", PrecPostfix);
                var pairs = new List<string>();
                for (int i = 0; i < m.Keys.Count; i++) pairs.Add("{" + X(m.Keys[i], t.Key).T + ", " + X(m.Values[i], t.Elem).T + "}");
                return new Ex("map_of(" + ElemName(t.Key) + ", " + ElemName(t.Elem) + ", " + string.Join(", ", pairs) + ")", PrecPostfix);
            }
            var ps = new List<string>();
            for (int i = 0; i < m.Keys.Count; i++) ps.Add("{" + X(m.Keys[i], t.Key).T + ", " + X(m.Values[i], t.Elem).T + "}");
            return new Ex(TypeText(IrType.Map(t.Key, t.Elem)) + "{" + string.Join(", ", ps) + "}", PrecPostfix);
        }

        /// <summary>"{1, 2}" for a declaration whose type is written (C++).</summary>
        string BraceInit(IrExpr lit, IrType t)
        {
            var full = X(lit, t).T;
            int i = full.IndexOf('{');
            return i > 0 && full.EndsWith("}") ? full.Substring(i) : full;
        }

        // ================================================================== types

        /// <summary>Spelling of a type (no declarator).</summary>
        string TypeText(IrType t)
        {
            if (t == null) return C ? "double" : "auto";
            switch (t.Kind)
            {
                case IrTypeKind.Int: return "int";
                case IrTypeKind.Float: return "double";
                case IrTypeKind.Bool: return "bool";
                case IrTypeKind.String: return C ? "const char *" : "std::string";
                case IrTypeKind.Void: return "void";
                case IrTypeKind.List: case IrTypeKind.Array:
                    if (C)
                    {
                        if (t.Elem != null && (t.Elem.Kind == IrTypeKind.List || t.Elem.Kind == IrTypeKind.Set || t.Elem.Kind == IrTypeKind.Map)) Problem("lists of lists are not supported in C (use a 2D array)");
                        return "List(" + ElemName(t.Elem) + ")";
                    }
                    return "std::vector<" + ElemCpp(t.Elem) + ">";
                case IrTypeKind.Set: return C ? "Set(" + ElemName(t.Elem) + ")" : "std::set<" + ElemCpp(t.Elem) + ">";
                case IrTypeKind.Map:
                    {
                        var s = C ? "Map(" + ElemName(t.Key) + ", " + ElemName(t.Elem) + ")" : "std::map<" + ElemCpp(t.Key) + ", " + ElemCpp(t.Elem) + ">";
                        return Cpp && t.Nullable ? "std::optional<" + s + ">" : s;
                    }
                case IrTypeKind.Game:
                    if (t.GameName == "Companion" && Cpp) return "std::optional<Companion>";
                    return t.GameName ?? "double";
                case IrTypeKind.Tuple:
                    if (IsPositionTuple(t)) return "Position";
                    if (t.Items != null && t.Items.Count == 2 && t.Items[0].IsGame("Entity")) return Cpp ? "std::optional<Companion>" : "Companion";
                    Problem("tuples are not supported in " + Lang);
                    return "Position";
                case IrTypeKind.Func:
                    if (C) return "void (*)(void)";
                    return "std::function<" + (t.Elem == null || !t.Elem.IsKnown ? "void" : TypeText(t.Elem)) + "()>";
                case IrTypeKind.Any:
                    if (C) return "double";
                    return "auto";
                case IrTypeKind.Unknown:
                    if (C) return "int";
                    return "auto";
                case IrTypeKind.Null:
                    return C ? "double" : "auto";
            }
            return C ? "double" : "auto";
        }

        string ElemCpp(IrType t)
        {
            if (t == null || !t.IsKnown || t.Kind == IrTypeKind.Any) { Problem("a collection whose element type is unknown"); return "int"; }
            var s = TypeText(t);
            if (s == "auto") { Problem("a collection whose element type is unknown"); return "int"; }
            return s;
        }

        /// <summary>A declaration "T name" (C function pointers and arrays put the name inside the type).</summary>
        string Decl(IrType t, string name, IrSymbol sym = null, bool function = false)
        {
            if (t == null) t = IrType.Unknown;
            if (function)
            {
                if (t.Kind == IrTypeKind.Func && C) { Problem("functions returning functions are not supported in C"); return "void " + name; }
                var tt = TypeText(t);
                if (tt == "auto") { tt = C ? "double" : "auto"; if (Cpp) Problem("the return type of " + name + " is unknown"); }
                return tt + (tt.EndsWith("*") ? "" : " ") + name;
            }
            if (t.Kind == IrTypeKind.Func && C)
            {
                var ret = t.Elem == null || !t.Elem.IsKnown ? "void" : TypeText(t.Elem);
                var ps = t.Items != null && t.Items.Count > 0 ? string.Join(", ", t.Items.Select(TypeText)) : "void";
                return ret + " (*" + name + ")(" + ps + ")";
            }
            if (t.Kind == IrTypeKind.Array) return ArrayDecl(t, name, sym, null);
            var text = TypeText(t);
            return text + (text.EndsWith("*") ? "" : " ") + name;
        }

        /// <summary>"int a[3]", "int g[w][h]", "int a[]" (parameters keep the dimensions the source wrote).</summary>
        string ArrayDecl(IrType t, string name, IrSymbol sym, IrExpr init)
        {
            var leaf = t;
            int dims = 0;
            while (leaf.Kind == IrTypeKind.Array) { leaf = leaf.Elem ?? IrType.Int; dims++; }
            string dimText;
            if (sym != null && sym.Tag is string tag && tag.StartsWith("[")) dimText = tag;
            else if (init is IrOpCall o && o.Op == IrOp.ArrayNew) dimText = "[" + X(o.Args[0], IrType.Int).T + "]";
            else if (init is IrOpCall o2 && o2.Op == IrOp.ArrayNew2) dimText = "[" + X(o2.Args[0], IrType.Int).T + "][" + X(o2.Args[1], IrType.Int).T + "]";
            else if (init is IrListLit ll)
            {
                dimText = "[" + ll.Items.Count + "]";
                if (dims == 2) dimText += "[" + (ll.Items.Count > 0 && ll.Items[0] is IrListLit inner ? inner.Items.Count : 0) + "]";
            }
            else
            {
                dimText = "[]";
                for (int i = 1; i < dims; i++) { dimText += "[]"; Problem("a 2D array parameter without its size"); }
            }
            if (Cpp && sym == null && init == null && false) { }
            var lt = TypeText(leaf);
            return lt + (lt.EndsWith("*") ? "" : " ") + name + dimText;
        }

        string DefaultValue(IrType t)
        {
            switch (t?.Kind ?? IrTypeKind.Unknown)
            {
                case IrTypeKind.Int: return "0";
                case IrTypeKind.Float: return "0.0";
                case IrTypeKind.Bool: return "false";
                case IrTypeKind.String: return C ? "\"\"" : "\"\"";
                case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Map:
                    if (C) return t.Kind == IrTypeKind.List ? "list_new(" + ElemName(t.Elem) + ")" : t.Kind == IrTypeKind.Set ? "set_new(" + ElemName(t.Elem) + ")" : "map_new(" + ElemName(t.Key) + ", " + ElemName(t.Elem) + ")";
                    return "{}";
                case IrTypeKind.Tuple: return C ? "(Position){0, 0}" : "Position{0, 0}";
                case IrTypeKind.Game: if (t.GameName == "Position") return C ? "(Position){0, 0}" : "Position{0, 0}"; break;
            }
            return Null;
        }

        static bool CanComplete(IrStmt s)
        {
            switch (s)
            {
                case null: return true;
                case IrReturn _: return false;
                case IrBlock b: return b.Stmts.Count == 0 || b.Stmts.All(x => CanComplete(x));
                case IrIf f: return f.Else == null || CanComplete(f.Then) || CanComplete(f.Else);
                case IrLoop l: return BreaksOut(l.Body);
                case IrWhile w: return !(w.Cond is IrLiteral lit && lit.Kind == IrLitKind.Bool && lit.Bool) || BreaksOut(w.Body);
                case IrSwitch sw: return !sw.Cases.Any(c => c.IsDefault) || sw.Cases.Any(c => CanComplete(c.Body));
                case IrBreak _: case IrContinue _: return false;
            }
            return true;
        }
    }
}
