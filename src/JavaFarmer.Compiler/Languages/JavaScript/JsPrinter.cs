using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Farm IR (from any frontend) → idiomatic JavaScript that compiles again with the JS frontend:
    /// top-level statements, function declarations, let / const, ===, for...of, classic for for counting loops,
    /// Map / Set / Array methods, console.log, Entities.X / North. Python dicts print as Map, sets as Set, tuples as
    /// arrays. Plain objects (JS source) stay objects. Names that are JS reserved words get a "_" suffix; snake_case names
    /// of other languages become camelCase. Constructs JavaScript cannot express are reported in Problems.
    /// </summary>
    internal sealed class JsPrinter : PrinterBase
    {
        public readonly List<string> Problems = new List<string>();

        public static string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            var p = new JsPrinter(module ?? new IrModule(), options ?? new PrintOptions());
            try { p.Run(); }
            catch (Exception ex) { p.Problems.Add("internal printer error: " + ex.Message); problems = p.Problems; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
            problems = p.Problems;
            return p.Result;
        }

        readonly IrModule mod;
        readonly bool fromJs;
        bool layout;

        JsPrinter(IrModule m, PrintOptions o) : base(o)
        {
            mod = m;
            fromJs = m.SourceLanguage == "js";
        }

        void Problem(string what) { if (!Problems.Contains(what)) Problems.Add(what); }

        // ================================================================== names
        static readonly HashSet<string> ReservedBase = new HashSet<string>(StringComparer.Ordinal)
        {
            "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export",
            "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return",
            "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "enum", "let", "static",
            "yield", "await", "implements", "interface", "package", "private", "protected", "public", "arguments", "eval",
            "undefined", "NaN", "Infinity", "async", "of",
            "Math", "console", "Map", "Set", "Array", "Object", "String", "Number", "JSON", "globalThis", "Boolean", "Symbol",
            "Date", "Promise", "Error", "RegExp", "BigInt", "parseInt", "parseFloat", "isNaN", "isFinite", "Proxy", "Reflect",
            "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards", "Directions", "North", "East", "South", "West",
        };

        HashSet<string> reserved;
        readonly Dictionary<IrSymbol, string> names = new Dictionary<IrSymbol, string>();
        readonly HashSet<string> moduleNames = new HashSet<string>(StringComparer.Ordinal);
        readonly List<Dictionary<string, IrSymbol>> scopes = new List<Dictionary<string, IrSymbol>>();
        readonly HashSet<string> allUsed = new HashSet<string>(StringComparer.Ordinal);

        string Ident(string name)
        {
            var n = string.IsNullOrEmpty(name) ? "x" : name;
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = n.Substring(3).TrimStart('_');
            if (n.Length == 0) n = "v";
            if (!fromJs && n.IndexOf('_') >= 0 && n.Any(char.IsLower))
            {
                var parts = n.Split('_').Where(x => x.Length > 0).ToList();
                if (parts.Count > 0)
                {
                    var sb = new StringBuilder(parts[0]);
                    for (int k = 1; k < parts.Count; k++) sb.Append(char.ToUpperInvariant(parts[k][0])).Append(parts[k].Substring(1));
                    n = sb.ToString();
                }
            }
            var clean = new StringBuilder();
            foreach (var ch in n) clean.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '$' ? ch : '_');
            n = clean.ToString();
            if (n.Length == 0) n = "x";
            if (char.IsDigit(n[0])) n = "_" + n;
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = "j" + n.Substring(3);
            while (reserved.Contains(n)) n += "_";
            return n;
        }

        void PushScope() => scopes.Add(new Dictionary<string, IrSymbol>());
        void PopScope() { if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1); }

        bool LiveOther(string n, IrSymbol s)
        {
            for (int i = scopes.Count - 1; i >= 0; i--) if (scopes[i].TryGetValue(n, out var o) && o != s) return true;
            return false;
        }

        /// <summary>Names a local / parameter / loop variable in the current scope (renamed on a clash).</summary>
        string Declare(IrSymbol s)
        {
            if (s == null) return "x";
            if (names.TryGetValue(s, out var have) && (s.IsModuleLevel || s.IsExternal)) return have;
            var b = Ident(s.Name);
            var n = b;
            int k = 2;
            while (moduleNames.Contains(n) || LiveOther(n, s)) n = b + k++;
            names[s] = n;
            allUsed.Add(n);
            if (scopes.Count == 0) PushScope();
            scopes[scopes.Count - 1][n] = s;
            return n;
        }

        bool IsDeclared(IrSymbol s) => s != null && names.ContainsKey(s);

        string Name(IrSymbol s)
        {
            if (s == null) return "null";
            if (names.TryGetValue(s, out var n)) return n;
            if (s.Tag as string == PyLowering.UndefinedTag && Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0) return names[s] = s.Name;
            if (s.Kind == IrSymbolKind.Local || s.Kind == IrSymbolKind.Param) return Declare(s);
            n = Ident(s.Name);
            names[s] = n;
            return n;
        }

        string Fresh(string b)
        {
            var n = b;
            int k = 2;
            while (moduleNames.Contains(n) || allUsed.Contains(n) || reserved.Contains(n) || LiveOther(n, null)) n = b + k++;
            allUsed.Add(n);
            if (scopes.Count == 0) PushScope();
            scopes[scopes.Count - 1][n] = new IrSymbol(n, IrSymbolKind.Local);
            return n;
        }

        // ================================================================== analysis
        readonly HashSet<IrSymbol> reassigned = new HashSet<IrSymbol>();
        readonly List<IrSymbol> undefinedFns = new List<IrSymbol>();
        readonly List<IrSymbol> undefinedVars = new List<IrSymbol>();
        readonly Dictionary<string, List<string>> namedImports = new Dictionary<string, List<string>>();   // module → names
        readonly HashSet<string> fromNamed = new HashSet<string>();     // "Module.name" printed bare
        readonly List<string> namespaceImports = new List<string>();

        static bool IsUndefined(IrSymbol s) => s != null && s.Tag as string == PyLowering.UndefinedTag;
        static bool IsBag(IrSymbol s) => IsUndefined(s) && Array.IndexOf(GameApi.ConstantCategories, s.Name) >= 0;

        void Analyse()
        {
            reserved = new HashSet<string>(ReservedBase, StringComparer.Ordinal);
            foreach (var b in GameApi.Builtins)
            {
                var js = JsNames.ApiName(b.PythonName);
                if (js != null && js.IndexOf('.') < 0) reserved.Add(js);
            }
            layout = IrWalk.Descendants(mod).Any(n => n.BlankLinesBefore > 0);
            foreach (var n in IrWalk.Descendants(mod))
            {
                switch (n)
                {
                    case IrAssign a:
                        if (a.Target is IrTupleLit tl) { foreach (var it in tl.Items) if (it is IrName tn && tn.Symbol != null) reassigned.Add(tn.Symbol); }
                        else if (a.Target is IrName nm && nm.Symbol != null) reassigned.Add(nm.Symbol);
                        break;
                    case IrRangeFor rf: if (rf.Var != null) reassigned.Add(rf.Var); break;
                    case IrImport im:
                        if (im.Module == null) break;
                        if (im.Names != null && !im.ImportAll)
                        {
                            if (!namedImports.TryGetValue(im.Module, out var l)) namedImports[im.Module] = l = new List<string>();
                            foreach (var x in im.Names) { if (!l.Contains(x)) l.Add(x); fromNamed.Add(im.Module + "." + x); }
                        }
                        else if (!namespaceImports.Contains(im.Module)) namespaceImports.Add(im.Module);
                        break;
                }
                IrSymbol s = (n as IrName)?.Symbol ?? (n as IrCall)?.Function ?? (n as IrFuncRef)?.Function;
                if (!IsUndefined(s) || IsBag(s)) continue;
                if (s.Kind == IrSymbolKind.Function) { if (s.Function == null && !undefinedFns.Contains(s)) undefinedFns.Add(s); }
                else if (s.Kind == IrSymbolKind.Global && !undefinedVars.Contains(s)) undefinedVars.Add(s);
            }
            foreach (var m in IrWalk.ReferencedModules(mod))
                if (!namespaceImports.Contains(m) && !(namedImports.ContainsKey(m) && OnlyNamedUses(m))) namespaceImports.Add(m);
            foreach (var m in namespaceImports.Concat(namedImports.Keys)) moduleNames.Add(Ident(m));
            // module-level names: functions, then globals, then names defined elsewhere
            foreach (var f in mod.Functions)
                if (f.Symbol != null && !names.ContainsKey(f.Symbol)) names[f.Symbol] = ModuleName(f.IsEntry && !EntryAtTopLevel(f) ? "main" : f.Symbol.Name);
            foreach (var g in mod.Globals) if (g.Symbol != null && !names.ContainsKey(g.Symbol)) names[g.Symbol] = ModuleName(g.Symbol.Name);
            foreach (var s in undefinedFns.Concat(undefinedVars)) if (!names.ContainsKey(s)) names[s] = ModuleName(s.PyName ?? s.Name);
            foreach (var kv in namedImports) foreach (var x in kv.Value) moduleNames.Add(Ident(x));
        }

        bool OnlyNamedUses(string module)
        {
            foreach (var n in IrWalk.Descendants(mod))
            {
                IrSymbol s = (n as IrName)?.Symbol ?? (n as IrCall)?.Function ?? (n as IrFuncRef)?.Function;
                if (s != null && s.IsExternal && s.Module == module && !fromNamed.Contains(module + "." + s.Name)) return false;
            }
            return true;
        }

        string ModuleName(string source)
        {
            var b = Ident(source);
            var n = b;
            int k = 2;
            while (moduleNames.Contains(n)) n = b + k++;
            moduleNames.Add(n);
            allUsed.Add(n);
            return n;
        }

        bool EntryAtTopLevel(IrFunction e)
        {
            if (e == null) return false;
            foreach (var n in IrWalk.Descendants(e.Body, false))
            {
                if (n is IrReturn) return false;
                if (n is IrName nm && nm.Symbol != null && nm.Symbol.Kind == IrSymbolKind.Param) return false;
            }
            return true;
        }

        bool Exported(bool isPrivate) => fromJs ? !isPrivate : Options.OtherFiles != null && Options.OtherFiles.Count > 0;

        // ================================================================== module
        void Run()
        {
            Analyse();
            PushScope();
            var entry = mod.Entry;
            var items = mod.Items.ToList();
            bool topEntry = entry != null && (Options.Snippet || EntryAtTopLevel(entry));
            if (entry != null) items.Remove(entry);
            if (!Options.Snippet)
                foreach (var s in undefinedVars)
                {
                    Line("let " + Name(s) + "; // defined elsewhere");
                }
            if (!Options.Snippet && undefinedVars.Count > 0 && items.Count > 0) Line();
            Items(items, true);
            if (entry != null)
            {
                if (topEntry)
                {
                    if (items.Count > 0 && !layout) Line();
                    LeadingComments(entry);
                    PushScope();
                    Stmts(entry.Body.Stmts);
                    Comments(entry.Body.TrailingComments);
                    PopScope();
                }
                else
                {
                    if (items.Count > 0) Line();
                    Function(entry, "main", false);
                    Line();
                    Line("main();");
                }
            }
            Comments(mod.TrailingComments);
            if (!Options.Snippet && undefinedFns.Count > 0)
                foreach (var s in undefinedFns)
                {
                    int arity = IrWalk.Descendants(mod).OfType<IrCall>().Where(c => c.Kind == IrCallKind.Function && c.Function == s).Select(c => c.Args.Count).DefaultIfEmpty(0).Max();
                    var ps = Enumerable.Range(0, arity).Select(i => ((char)('a' + Math.Min(i, 25))).ToString()).ToList();
                    Line();
                    Line("// defined elsewhere");
                    Line("function " + Name(s) + "(" + string.Join(", ", ps) + ") {");
                    Line("}");
                }
            // imports go first
            var body = Out.ToString();
            Out.Clear();
            foreach (var m in namespaceImports) Line("import * as " + Ident(m) + " from \"./" + m + ".js\";");
            foreach (var kv in namedImports) Line("import { " + string.Join(", ", kv.Value.Select(Ident)) + " } from \"./" + kv.Key + ".js\";");
            if (Out.Length > 0 && body.Length > 0 && !body.StartsWith(Options.Newline)) Line();
            Out.Append(body);
        }

        /// <summary>Statements / declarations of a statement list (module items or block statements).</summary>
        bool atModule;

        void Items(List<IrNode> items, bool moduleLevel)
        {
            bool prevDef = false;
            bool saveAt = atModule;
            atModule = moduleLevel;
            try { ItemsCore(items, moduleLevel, ref prevDef); }
            finally { atModule = saveAt; }
        }

        void ItemsCore(List<IrNode> items, bool moduleLevel, ref bool prevDef)
        {
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k];
                if (it is IrImport) { LeadingComments(it); continue; }
                bool isDef = it is IrFunction;
                if (moduleLevel && !layout && k > 0 && (isDef || prevDef) && Out.Length > 0) Line();
                // let [a, b] = value: bare declarations followed by the destructuring assignment
                if (TryMergeDestructure(items, k, out int consumed)) { k += consumed - 1; prevDef = false; continue; }
                switch (it)
                {
                    case IrFunction f: Function(f, null, Exported(f.IsPrivate)); break;
                    case IrGlobal g: Global(g); break;
                    case IrStmt s: Stmt(s); break;
                }
                prevDef = isDef;
            }
        }

        void Stmts(List<IrStmt> stmts) => Items(stmts.Cast<IrNode>().ToList(), false);

        void Pre(IrNode n)
        {
            if (n == null) return;
            if (Options.Comments && n.Leading != null)
                foreach (var c in n.Leading)
                {
                    if (layout) for (int i = 0; i < Math.Min(2, c.BlankLinesBefore); i++) Line();
                    Comment(c);
                }
            if (layout && Out.Length > 0) for (int i = 0; i < Math.Min(2, n.BlankLinesBefore); i++) Line();
        }

        bool TryMergeDestructure(List<IrNode> items, int k, out int consumed)
        {
            consumed = 0;
            var decls = new List<IrSymbol>();
            int j = k;
            while (j < items.Count)
            {
                if (items[j] is IrVarDecl v && v.Init == null && v.Synthetic && v.Symbol != null && !IsDeclared(v.Symbol)) { decls.Add(v.Symbol); j++; continue; }
                if (items[j] is IrGlobal g && g.Init == null && g.ImplicitInit && g.Symbol != null && !IsDeclaredGlobal(g.Symbol)) { decls.Add(g.Symbol); j++; continue; }
                break;
            }
            if (decls.Count == 0 || j >= items.Count || !(items[j] is IrAssign a) || a.Op != null || !(a.Target is IrTupleLit tl)) return false;
            var flat = FlatNames(tl);
            if (flat == null || flat.Count != decls.Count || !flat.All(decls.Contains)) return false;
            if (IrWalk.Descendants(a.Value).Any(x => x is IrName vn && decls.Contains(vn.Symbol))) return false;
            for (int q = k; q <= j; q++) Pre(items[q]);
            var value = X(a.Value, PAssign);
            bool isConst = fromJs ? decls.All(s => s.IsConst) : decls.All(s => !reassigned.Contains(s) || CountAssigns(s) <= 1);
            var ns = new List<string>();
            foreach (var s in decls)
            {
                if (s.IsModuleLevel) { globalsDeclared.Add(s); ns.Add(Name(s)); }
                else ns.Add(Declare(s));
            }
            var target = PatternText(tl);
            bool export = decls.All(s => s.IsModuleLevel) && items[k] is IrGlobal eg && Exported(eg.IsPrivate);
            Line((export ? "export " : "") + (isConst ? "const " : "let ") + target + " = " + value + ";", a);
            consumed = j - k + 1;
            return true;
        }

        static List<IrSymbol> FlatNames(IrTupleLit t)
        {
            var res = new List<IrSymbol>();
            foreach (var x in t.Items)
            {
                if (x is IrName n && n.Symbol != null) res.Add(n.Symbol);
                else if (x is IrTupleLit inner) { var r = FlatNames(inner); if (r == null) return null; res.AddRange(r); }
                else return null;
            }
            return res;
        }

        string PatternText(IrTupleLit t) => "[" + string.Join(", ", t.Items.Select(x => x is IrTupleLit it ? PatternText(it) : Target(x))) + "]";

        readonly Dictionary<IrSymbol, int> assignCounts = new Dictionary<IrSymbol, int>();
        int CountAssigns(IrSymbol s)
        {
            if (assignCounts.Count == 0)
                foreach (var n in IrWalk.Descendants(mod))
                    if (n is IrAssign a)
                        foreach (var t in a.Target is IrTupleLit tl ? tl.Items : new List<IrExpr> { a.Target })
                            if (t is IrName nm && nm.Symbol != null) assignCounts[nm.Symbol] = (assignCounts.TryGetValue(nm.Symbol, out var c) ? c : 0) + 1;
            return assignCounts.TryGetValue(s, out var r) ? r : 0;
        }

        readonly HashSet<IrSymbol> globalsDeclared = new HashSet<IrSymbol>();
        bool IsDeclaredGlobal(IrSymbol s) => globalsDeclared.Contains(s);

        void Global(IrGlobal g)
        {
            Pre(g);
            if (g.Symbol == null) return;
            if (globalsDeclared.Contains(g.Symbol))
            {
                if (g.Init != null && !g.ImplicitInit) Line(Name(g.Symbol) + " = " + X(g.Init, PAssign) + ";", g);
                return;
            }
            globalsDeclared.Add(g.Symbol);
            var export = Exported(g.IsPrivate) ? "export " : "";
            bool bare = g.Init == null && g.ImplicitInit || (g.ImplicitInit && g.Init is IrLiteral l && l.Kind == IrLitKind.Null && l.Synthetic);
            if (bare) { Line(export + "let " + Name(g.Symbol) + ";", g); return; }
            string init;
            if (g.Init == null)
            {
                var t = g.Symbol.Type ?? IrType.Unknown;
                init = t.Kind == IrTypeKind.Bool ? "false" : t.IsNumeric ? "0" : "null";
            }
            else init = X(g.Init, PAssign);
            bool isConst = fromJs ? g.Symbol.IsConst : (g.Symbol.IsConst || !reassigned.Contains(g.Symbol)) && g.Init != null;
            Line(export + (isConst ? "const " : "let ") + Name(g.Symbol) + " = " + init + ";", g);
        }

        void Function(IrFunction f, string nameOverride, bool export)
        {
            Pre(f);
            PushScope();
            var ps = Params(f.Params);
            Line((export ? "export " : "") + "function " + (nameOverride ?? Name(f.Symbol)) + "(" + ps + ") {", f);
            Indent();
            Stmts(f.Body.Stmts);
            Comments(f.Body.TrailingComments);
            Dedent();
            Line("}");
            PopScope();
        }

        string Params(List<IrParam> ps)
        {
            var parts = new List<string>();
            foreach (var p in ps)
            {
                var n = Declare(p.Symbol);
                parts.Add(p.Default != null ? n + " = " + X(p.Default, PAssign) : n);
            }
            return string.Join(", ", parts);
        }

        // ================================================================== statements
        void Block(IrBlock b)
        {
            PushScope();
            Indent();
            if (b != null)
            {
                Stmts(b.Stmts);
                Comments(b.TrailingComments);
            }
            Dedent();
            PopScope();
        }

        static IrSymbol NameSym(IrExpr e) => e is IrName n ? n.Symbol : null;

        bool IsConstDecl(IrVarDecl v)
        {
            if (v.Init == null || IsSyntheticNull(v.Init)) return false;
            if (fromJs) return v.Symbol.IsConst;
            return v.Symbol.IsConst || !reassigned.Contains(v.Symbol);
        }

        static bool IsSyntheticNull(IrExpr e) => e is IrLiteral l && l.Kind == IrLitKind.Null && l.Synthetic;

        void Stmt(IrStmt s)
        {
            switch (s)
            {
                case null: return;
                case IrBlock b:
                    Pre(b);
                    Line("{");
                    Block(b);
                    Line("}");
                    return;
                case IrVarDecl v:
                    {
                        Pre(v);
                        if (v.Symbol == null) return;
                        if (v.Init is IrLambda fl && fl.Body != null && !reassigned.Contains(v.Symbol) && !IsDeclared(v.Symbol)
                            && (fromJs ? JsLowering.IsFunctionDeclaration(v) : !atModule))
                        {
                            var fname = Declare(v.Symbol);
                            PushScope();
                            var ps = Params(fl.Params);
                            Line("function " + fname + "(" + ps + ") {", v);
                            Indent();
                            Stmts(fl.Body.Stmts);
                            Comments(fl.Body.TrailingComments);
                            Dedent();
                            Line("}");
                            PopScope();
                            return;
                        }
                        if (IsDeclared(v.Symbol) && !(v.Symbol.Kind == IrSymbolKind.Local && !LiveSymbol(v.Symbol)))
                        {
                            // declared already (e.g. a var declared twice): an assignment
                            if (v.Init != null && !IsSyntheticNull(v.Init)) Line(Name(v.Symbol) + " = " + X(v.Init, PAssign) + ";", v);
                            return;
                        }
                        if (v.Init == null || IsSyntheticNull(v.Init)) { Line("let " + Declare(v.Symbol) + ";", v); return; }
                        var init = X(v.Init, PAssign);
                        Line((IsConstDecl(v) ? "const " : "let ") + Declare(v.Symbol) + " = " + init + ";", v);
                        return;
                    }
                case IrAssign a: Pre(a); Line(Assign(a) + ";", a); return;
                case IrExprStmt es: Pre(es); Line(ExprStatement(es.Expr) + ";", es); return;
                case IrIf i: Pre(i); If(i, false); return;
                case IrWhile w:
                    Pre(w);
                    Line("while (" + X(w.Cond, 0) + ") {", w);
                    Block(w.Body);
                    Line("}");
                    return;
                case IrLoop lp:
                    Pre(lp);
                    Line("while (true) {", lp);
                    Block(lp.Body);
                    Line("}");
                    return;
                case IrDoWhile dw:
                    Pre(dw);
                    Line("do {", dw);
                    Block(dw.Body);
                    Line("} while (" + X(dw.Cond, 0) + ");");
                    return;
                case IrClassicFor cf: Pre(cf); ClassicFor(cf); return;
                case IrRangeFor rf: Pre(rf); RangeFor(rf); return;
                case IrForEach fe: Pre(fe); ForEach(fe); return;
                case IrSwitch sw: Pre(sw); Switch(sw); return;
                case IrBreak br: Pre(br); Line("break;", br); return;
                case IrContinue co: Pre(co); Line("continue;", co); return;
                case IrReturn r: Pre(r); Line(r.Value == null ? "return;" : "return " + X(r.Value, PAssign) + ";", r); return;
                case IrImport im: Pre(im); return;
            }
            Problem("statement " + s.GetType().Name + " cannot be printed");
            Line("// " + s.GetType().Name);
        }

        bool LiveSymbol(IrSymbol s)
        {
            for (int i = scopes.Count - 1; i >= 0; i--) if (scopes[i].ContainsValue(s)) return true;
            return false;
        }

        void If(IrIf i, bool elseIf)
        {
            var head = "if (" + X(i.Cond, 0) + ") {";
            if (elseIf)
            {
                TrimLastNewline();
                Out.Append(" else ").Append(head).Append(TrailingComment(i)).Append(Options.Newline);
            }
            else Line(head, i);
            Block(i.Then);
            Line("}");
            if (i.Else is IrIf ei) { LeadingComments(ei); If(ei, true); }
            else if (i.Else is IrBlock eb)
            {
                TrimLastNewline();
                Out.Append(" else {").Append(TrailingComment(eb)).Append(Options.Newline);
                Block(eb);
                Line("}");
            }
        }

        /// <summary>A statement-level assignment without the semicolon (also used in for headers).</summary>
        string Assign(IrAssign a)
        {
            if (a.Target is IrTupleLit tl)
                return PatternText(tl) + " = " + X(a.Value, PAssign);
            // first assignment of an undeclared local (IR without declarations): declare it here
            if (a.Op == null && a.Target is IrName tn && tn.Symbol != null && tn.Symbol.Kind == IrSymbolKind.Local && !IsDeclared(tn.Symbol))
                return "let " + Declare(tn.Symbol) + " = " + X(a.Value, PAssign);
            if (a.Target is IrIndex ix && MapStyleIndex(ix))
            {
                var m = X(ix.Target, PPost);
                var k = X(ix.Index, PAssign);
                if (a.Op == null) return m + ".set(" + k + ", " + X(a.Value, PAssign) + ")";
                var cur = new IrOpCall(IrOp.MapGetOrNull, ix.Target, ix.Index) { Type = ix.Type };
                return m + ".set(" + k + ", " + BinText(a.Op.Value, cur, a.Value, a.TruncateToInt) + ")";
            }
            var t = Target(a.Target);
            if (a.Op == null) return t + " = " + X(a.Value, PAssign);
            var op = a.Op.Value;
            if (a.TruncateToInt) return t + " = Math.trunc(" + BinText(op, a.Target, a.Value, false) + ")";
            if (a.IsIncDec && a.Value is IrLiteral one && one.Kind == IrLitKind.Int && one.Int == 1 && (op == IrBinOp.Add || op == IrBinOp.Sub))
                return a.Prefix ? (op == IrBinOp.Add ? "++" : "--") + t : t + (op == IrBinOp.Add ? "++" : "--");
            switch (op)
            {
                case IrBinOp.Add: case IrBinOp.Concat: return t + " += " + X(a.Value, PAssign);
                case IrBinOp.Sub: return t + " -= " + X(a.Value, PAssign);
                case IrBinOp.Mul: return t + " *= " + X(a.Value, PAssign);
                case IrBinOp.TrueDiv: return t + " /= " + X(a.Value, PAssign);
                case IrBinOp.ModTrunc: return t + " %= " + X(a.Value, PAssign);
                case IrBinOp.Pow: return t + " **= " + X(a.Value, PAssign);
                case IrBinOp.ModFloor:
                    if (NonNeg(a.Target) && PositiveLiteral(a.Value)) return t + " %= " + X(a.Value, PAssign);
                    return t + " = " + BinText(op, a.Target, a.Value, false);
                default: return t + " = " + BinText(op, a.Target, a.Value, false);
            }
        }

        string Target(IrExpr t)
        {
            if (t is IrName n) return NameRef(n);
            return X(t, PPost);
        }

        string BinText(IrBinOp op, IrExpr l, IrExpr r, bool trunc)
        {
            var e = new IrBinary { Op = op, Left = l, Right = r, Type = op == IrBinOp.Concat ? IrType.String : IrType.Unknown };
            var s = X(e, PAssign);
            return trunc ? "Math.trunc(" + s + ")" : s;
        }

        string ForInit(IrStmt s)
        {
            switch (s)
            {
                case IrVarDecl v:
                    return (IsDeclared(v.Symbol) ? "" : "let ") + Declare(v.Symbol) + (v.Init != null && !IsSyntheticNull(v.Init) ? " = " + X(v.Init, PAssign) : "");
                case IrAssign a: return Assign(a);
                case IrExprStmt e: return ExprStatement(e.Expr);
            }
            Problem("for-loop header part " + s?.GetType().Name);
            return "";
        }

        void ClassicFor(IrClassicFor f)
        {
            PushScope();
            string init;
            if (f.Init.Count > 1 && f.Init.All(x => x is IrVarDecl vd && !IsDeclared(vd.Symbol)))
                init = "let " + string.Join(", ", f.Init.Cast<IrVarDecl>().Select(v => Declare(v.Symbol) + (v.Init != null && !IsSyntheticNull(v.Init) ? " = " + X(v.Init, PAssign) : "")));
            else init = string.Join(", ", f.Init.Select(ForInit));
            var cond = f.Cond != null ? X(f.Cond, 0) : "";
            var upd = string.Join(", ", f.Update.Select(u => u is IrAssign ua ? Assign(ua) : u is IrExprStmt ue ? ExprStatement(ue.Expr) : ""));
            Line("for (" + init + "; " + cond + "; " + upd + ") {", f);
            Block(f.Body);
            Line("}");
            PopScope();
        }

        bool Invariant(IrExpr e, IrNode body)
        {
            switch (e)
            {
                case null: return true;
                case IrLiteral _: case IrConst _: return true;
                case IrName n:
                    if (n.Symbol == null) return false;
                    if (IrWalk.Assigns(body, n.Symbol)) return false;
                    if (n.Symbol.Kind == IrSymbolKind.Global || n.Symbol.Kind == IrSymbolKind.ExternalGlobal)
                        return n.Symbol.IsConst || !IrWalk.Descendants(body).Any(x => x is IrCall c && c.Kind != IrCallKind.Builtin);
                    return true;
                case IrUnary u: return Invariant(u.Operand, body);
                case IrBinary b: return b.Op != IrBinOp.And && b.Op != IrBinOp.Or && Invariant(b.Left, body) && Invariant(b.Right, body);
                case IrCall c when c.Kind == IrCallKind.Builtin:
                    {
                        var api = GameApi.Find(c.Builtin);
                        if (api == null || !(api.LoopInvariant || api.Pure)) return false;
                        return c.Args.All(a => Invariant(a, body));
                    }
                case IrOpCall o when o.Op == IrOp.Len && o.Args.Count == 1 && o.Args[0] is IrName ln && ln.Symbol != null:
                    {
                        if (!Invariant(ln, body)) return false;
                        foreach (var x in IrWalk.Descendants(body))
                            if (x is IrOpCall mo && mo.Args.Count > 0 && mo.Args[0] is IrName mn && mn.Symbol == ln.Symbol && Mutates(mo.Op)) return false;
                        if (ln.Symbol.Kind == IrSymbolKind.Global && IrWalk.Descendants(body).Any(x => x is IrCall c2 && c2.Kind != IrCallKind.Builtin)) return false;
                        return true;
                    }
                case IrOpCall o2 when (o2.Op == IrOp.Floor || o2.Op == IrOp.Ceil || o2.Op == IrOp.Round || o2.Op == IrOp.Sqrt):
                    return Invariant(o2.Args[0], body);
                case IrCast ca: return Invariant(ca.Operand, body);
            }
            return false;
        }

        static bool Mutates(IrOp op)
        {
            switch (op)
            {
                case IrOp.ListAppend: case IrOp.ListInsert: case IrOp.ListPop: case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent:
                case IrOp.Clear: case IrOp.SetAdd: case IrOp.MapPut: case IrOp.MapRemove: case IrOp.MapRemoveIfPresent:
                    return true;
            }
            return false;
        }

        void RangeFor(IrRangeFor r)
        {
            long step = 1;
            bool literalStep = true;
            if (r.Step != null)
            {
                if (r.Step is IrLiteral sl && sl.Kind == IrLitKind.Int) step = sl.Int;
                else if (r.Step is IrUnary su && su.Op == IrUnOp.Neg && su.Operand is IrLiteral sl2 && sl2.Kind == IrLitKind.Int) step = -sl2.Int;
                else literalStep = false;
            }
            string end = X(r.End, PRel + 1);
            if (!Invariant(r.End, r.Body))
            {
                // Python evaluates range() once: keep the end in a constant
                var tmp = Fresh("end");
                Line("const " + tmp + " = " + X(r.End, PAssign) + ";");
                end = tmp;
            }
            PushScope();
            var v = IsDeclared(r.Var) && (r.Var.IsModuleLevel || LiveSymbol(r.Var)) ? Name(r.Var) : null;
            string decl = v == null ? "let " + Declare(r.Var) : v;
            v = Name(r.Var);
            var start = r.Start != null ? X(r.Start, PAssign) : "0";
            string cmp, upd;
            if (!literalStep)
            {
                Problem("a range with a variable step");
                cmp = v + " < " + end;
                upd = v + " += " + X(r.Step, PAssign);
            }
            else if (step >= 0)
            {
                cmp = v + (r.Inclusive ? " <= " : " < ") + end;
                upd = step == 1 ? v + "++" : v + " += " + step.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                cmp = v + (r.Inclusive ? " >= " : " > ") + end;
                upd = step == -1 ? v + "--" : v + " -= " + (-step).ToString(CultureInfo.InvariantCulture);
            }
            if (IrWalk.Assigns(r.Body, r.Var)) Problem("a range loop whose body changes the loop variable");
            if (literalStep && step > 0 && (r.Start == null || NonNeg(r.Start))) rangeVars.Add(r.Var);
            Line("for (" + decl + " = " + start + "; " + cmp + "; " + upd + ") {", r);
            Block(r.Body);
            Line("}");
            PopScope();
        }

        void ForEach(IrForEach f)
        {
            var it = f.Iterable;
            var body = f.Body?.Stmts ?? new List<IrStmt>();
            int skip = 0;
            string targetText = null;
            PushScope();
            var iterText = X(it, PAssign);
            // for (const [a, b] of pairs): hidden variable + unpacking as the first statement
            if (f.ValueVar == null && f.Var != null)
            {
                int k = 0;
                var bare = new List<IrSymbol>();
                while (k < body.Count && body[k] is IrVarDecl d && d.Init == null && d.Synthetic) { bare.Add(d.Symbol); k++; }
                if (k < body.Count && body[k] is IrAssign un && un.Op == null && un.Target is IrTupleLit tl && NameSym(un.Value) == f.Var
                    && FlatNames(tl) != null && !IrWalk.Descendants(f.Body).Any(x => x is IrName xn && xn.Symbol == f.Var && x != un.Value))
                {
                    var syms = FlatNames(tl);
                    bool fresh = syms.All(s => !IsDeclared(s));
                    bool cst = fresh && syms.All(s => !IrWalk.Assigns(new IrBlock(body.Skip(k + 1)), s));
                    if (fresh) foreach (var s in syms) Declare(s);
                    targetText = (fresh ? (cst ? "const " : "let ") : "") + PatternText(tl);
                    skip = k + 1;
                }
            }
            if (targetText == null)
            {
                bool fresh = !IsDeclared(f.Var) || (f.Var.Kind == IrSymbolKind.Local && !LiveSymbol(f.Var) && !f.Var.IsModuleLevel);
                if (f.ValueVar != null)
                {
                    bool cst = !IrWalk.Assigns(f.Body, f.Var) && !IrWalk.Assigns(f.Body, f.ValueVar);
                    var kn = fresh ? Declare(f.Var) : Name(f.Var);
                    var vn = !IsDeclared(f.ValueVar) ? Declare(f.ValueVar) : Name(f.ValueVar);
                    targetText = (fresh ? (cst ? "const " : "let ") : "") + "[" + kn + ", " + vn + "]";
                    if (IsObjectStyle(it)) iterText = "Object.entries(" + X(it, PAssign) + ")";
                    else if (TypeOf(it).Kind != IrTypeKind.Map) iterText = X(it, PPost) + ".entries()";
                }
                else
                {
                    bool cst = !IrWalk.Assigns(f.Body, f.Var);
                    targetText = (fresh ? (cst ? "const " : "let ") : "") + (fresh ? Declare(f.Var) : Name(f.Var));
                    if (TypeOf(it).Kind == IrTypeKind.Map && !(it is IrOpCall)) 
                    {
                        if (IsObjectStyle(it)) { Line("for (" + targetText + " in " + X(it, PAssign) + ") {", f); BodyFrom(f.Body, 0); PopScope(); return; }
                        iterText = X(it, PPost) + ".keys()";
                    }
                }
            }
            Line("for (" + targetText + " of " + iterText + ") {", f);
            BodyFrom(f.Body, skip);
            PopScope();
        }

        void BodyFrom(IrBlock b, int skip)
        {
            PushScope();
            Indent();
            if (b != null)
            {
                Stmts(b.Stmts.Skip(skip).ToList());
                Comments(b.TrailingComments);
            }
            Dedent();
            PopScope();
            Line("}");
        }

        static bool HasLoopBreak(IrNode n)
        {
            foreach (var c in IrWalk.Children(n))
            {
                if (c is IrBreak) return true;
                if (c is IrWhile || c is IrDoWhile || c is IrLoop || c is IrClassicFor || c is IrRangeFor || c is IrForEach || c is IrLambda) continue;
                if (HasLoopBreak(c)) return true;
            }
            return false;
        }

        static bool Terminal(IrBlock b)
        {
            if (b == null || b.Stmts.Count == 0) return false;
            var last = b.Stmts[b.Stmts.Count - 1];
            if (last is IrReturn || last is IrContinue || last is IrBreak) return true;
            if (last is IrBlock ib) return Terminal(ib);
            if (last is IrIf i && i.Else != null) return Terminal(i.Then) && (i.Else is IrBlock eb ? Terminal(eb) : Terminal(new IrBlock(new[] { i.Else })));
            return false;
        }

        void Switch(IrSwitch sw)
        {
            if (sw.Cases.Any(c => HasLoopBreak(c.Body)))
            {
                // a break that leaves a loop cannot be written inside a JS switch: an if / else if chain
                var sel = X(sw.Selector, PEq + 1);
                if (!(sw.Selector is IrName || sw.Selector is IrLiteral || sw.Selector is IrConst))
                {
                    var tmp = Fresh("value");
                    Line("const " + tmp + " = " + X(sw.Selector, PAssign) + ";");
                    sel = tmp;
                }
                bool first = true;
                IrCase def = null;
                foreach (var c in sw.Cases)
                {
                    if (c.IsDefault) { def = c; continue; }
                    var cond = string.Join(" || ", c.Labels.Select(l => sel + " === " + X(l, PEq + 1)));
                    if (first) Line("if (" + cond + ") {");
                    else { TrimLastNewline(); Out.Append(" else if (" + cond + ") {").Append(Options.Newline); }
                    Block(c.Body);
                    Line("}");
                    first = false;
                }
                if (def != null)
                {
                    if (first) { Line("{"); Block(def.Body); Line("}"); }
                    else { TrimLastNewline(); Out.Append(" else {").Append(Options.Newline); Block(def.Body); Line("}"); }
                }
                return;
            }
            Line("switch (" + X(sw.Selector, 0) + ") {", sw);
            Indent();
            foreach (var c in sw.Cases)
            {
                LeadingComments(c);
                if (!c.IsDefault || c.Labels.Count > 0) foreach (var l in c.Labels) Line("case " + X(l, 0) + ":");
                if (c.IsDefault) Line("default:");
                bool declares = c.Body.Stmts.Any(x => x is IrVarDecl);
                if (declares)
                {
                    TrimLastNewline();
                    Out.Append(" {").Append(Options.Newline);
                }
                PushScope();
                Indent();
                Stmts(c.Body.Stmts);
                Comments(c.Body.TrailingComments);
                if (!Terminal(c.Body)) Line("break;");
                Dedent();
                PopScope();
                if (declares) Line("}");
            }
            Dedent();
            Line("}");
        }

        // ================================================================== expressions
        const int PAssign = 1, POr = 2, PAnd = 3, PEq = 4, PRel = 5, PAdd = 6, PMul = 7, PPow = 8, PUn = 9, PPost = 10, PAtom = 11;

        static IrType TypeOf(IrExpr e)
        {
            if (e == null) return IrType.Unknown;
            if (e.Type != null && e.Type.IsKnown) return e.Type;
            if (e is IrName n && n.Symbol?.Type != null) return n.Symbol.Type;
            return IrType.Unknown;
        }

        static bool IsObjectStyle(IrExpr e)
        {
            if (e is IrName n && n.Symbol?.Tag as string == JsLowering.ObjectTag) return true;
            if (e is IrMapLit m && JsLowering.IsObjectLiteral(m)) return true;
            return false;
        }

        /// <summary>Index on a Map that prints as .get / .set (not brackets).</summary>
        bool MapStyleIndex(IrIndex ix)
        {
            if (TypeOf(ix.Target).Kind != IrTypeKind.Map) return false;
            if (IsObjectStyle(ix.Target)) return false;
            if (fromJs) return ix.Target is IrName n && n.Symbol?.Tag as string == JsLowering.MapTag;
            return true;
        }

        bool NonNeg(IrExpr e)
        {
            switch (e)
            {
                case IrLiteral l: return (l.Kind == IrLitKind.Int && l.Int >= 0) || (l.Kind == IrLitKind.Float && l.Float >= 0);
                case IrCall c when c.Kind == IrCallKind.Builtin: return GameApi.Find(c.Builtin)?.NonNegative ?? false;
                case IrOpCall o: return o.Op == IrOp.Len || o.Op == IrOp.Sqrt || ((o.Op == IrOp.Floor || o.Op == IrOp.Ceil || o.Op == IrOp.Round) && NonNeg(o.Args[0]));
                case IrBinary b:
                    switch (b.Op)
                    {
                        case IrBinOp.Add: case IrBinOp.Mul: case IrBinOp.TrueDiv: case IrBinOp.IntDivFloor: case IrBinOp.IntDivTrunc: case IrBinOp.ModTrunc: case IrBinOp.ModFloor:
                            return NonNeg(b.Left) && NonNeg(b.Right);
                    }
                    return false;
                case IrName n: return n.Symbol != null && rangeVars.Contains(n.Symbol);
                case IrCast c: return NonNeg(c.Operand);
            }
            return false;
        }

        readonly HashSet<IrSymbol> rangeVars = new HashSet<IrSymbol>();

        static bool PositiveLiteral(IrExpr e) => e is IrLiteral l && ((l.Kind == IrLitKind.Int && l.Int > 0) || (l.Kind == IrLitKind.Float && l.Float > 0));

        string NameRef(IrName n)
        {
            var s = n.Symbol;
            if (s == null) return "null";
            if (s.IsExternal) return Qualified(s);
            return Name(s);
        }

        string Qualified(IrSymbol s)
        {
            if (s.Module != null && fromNamed.Contains(s.Module + "." + s.Name)) return Ident(s.Name);
            return Ident(s.Module ?? "module") + "." + Ident(s.Name);
        }

        string Lit(IrLiteral l)
        {
            switch (l.Kind)
            {
                case IrLitKind.Int: return l.Int.ToString(CultureInfo.InvariantCulture);
                case IrLitKind.Float:
                    if (double.IsNaN(l.Float) || double.IsInfinity(l.Float)) { Problem("an infinite or NaN number"); return "0"; }
                    return FormatFloat(l.Float);
                case IrLitKind.Bool: return l.Bool ? "true" : "false";
                case IrLitKind.String: return JsFormat.Literal(l.Str, '"');
                default: return "null";
            }
        }

        string Args(IEnumerable<IrExpr> args) => string.Join(", ", args.Select(a => X(a, PAssign)));

        /// <summary>An expression statement: list.push(x), map.set(k, v), ... print as methods.</summary>
        string ExprStatement(IrExpr e) => X(e, 0, true);

        string X(IrExpr e, int min, bool stmt = false)
        {
            var (t, p) = Ex(e, stmt);
            return p < min ? "(" + t + ")" : t;
        }

        (string, int) Ex(IrExpr e, bool stmt)
        {
            switch (e)
            {
                case null: return ("null", PAtom);
                case IrLiteral l:
                    {
                        var s = Lit(l);
                        return (s, s.StartsWith("-") ? PUn : PAtom);
                    }
                case IrName n:
                    if (n.Symbol != null && IsBag(n.Symbol)) return (n.Symbol.Name, PAtom);
                    return (NameRef(n), PAtom);
                case IrConst c: return (c.Category == null || c.Category == "Directions" ? c.Name : c.Category + "." + c.Name, PPost);
                case IrBinary b: return Bin(b);
                case IrUnary u:
                    {
                        var opnd = X(u.Operand, PUn);
                        var op = u.Op == IrUnOp.Not ? "!" : u.Op == IrUnOp.Neg ? "-" : "+";
                        if ((op == "-" && opnd.StartsWith("-")) || (op == "+" && opnd.StartsWith("+"))) opnd = "(" + opnd + ")";
                        return (op + opnd, PUn);
                    }
                case IrConditional c:
                    return (X(c.Cond, POr) + " ? " + X(c.Then, PAssign) + " : " + X(c.Else, PAssign), PAssign);
                case IrCall c: return (Call(c), PPost);
                case IrOpCall o: return Op(o, stmt);
                case IrIndex ix:
                    {
                        var idx = ix.BaseOffset != 0 ? new IrBinary { Op = IrBinOp.Sub, Left = ix.Index, Right = IrLiteral.OfInt(ix.BaseOffset), Type = IrType.Int } : ix.Index;
                        if (MapStyleIndex(ix)) return (X(ix.Target, PPost) + ".get(" + X(idx, PAssign) + ")", PPost);
                        if (idx is IrLiteral sl && sl.Kind == IrLitKind.String && JsFormat.IsIdentifierName(sl.Str) && !JsLexer.Reserved.Contains(sl.Str)
                            && sl.Str != "length" && sl.Str != "size" && TypeOf(ix.Target).Kind == IrTypeKind.Map)
                            return (X(ix.Target, PPost) + "." + sl.Str, PPost);
                        return (X(ix.Target, PPost) + "[" + X(idx, PAssign) + "]", PPost);
                    }
                case IrListLit l: return ("[" + Args(l.Items) + "]", PAtom);
                case IrTupleLit t: return ("[" + Args(t.Items) + "]", PAtom);
                case IrSetLit s: return (s.Items.Count == 0 ? "new Set()" : "new Set([" + Args(s.Items) + "])", PPost);
                case IrMapLit m: return MapLit(m);
                case IrLambda lam: return (Lambda(lam), PAssign);
                case IrFuncRef fr:
                    if (fr.Builtin != null) return (JsNames.ApiName(fr.Builtin) ?? Ident(fr.Builtin), PPost);
                    if (fr.Function != null && fr.Function.IsExternal) return (Qualified(fr.Function), PPost);
                    return (Name(fr.Function), PAtom);
                case IrCast c:
                    if (c.IsTypeAssertion) return Ex(c.Operand, false);
                    switch (c.Target?.Kind ?? IrTypeKind.Unknown)
                    {
                        case IrTypeKind.String: return ("String(" + X(c.Operand, PAssign) + ")", PPost);
                        case IrTypeKind.Int: return ("Math.trunc(" + X(c.Operand, PAssign) + ")", PPost);
                    }
                    return Ex(c.Operand, false);
            }
            Problem("expression " + e.GetType().Name + " cannot be printed");
            return ("null", PAtom);
        }

        (string, int) MapLit(IrMapLit m)
        {
            if (JsLowering.IsObjectLiteral(m))
            {
                if (m.Keys.Count == 0) return ("{}", PAtom);
                var parts = new List<string>();
                for (int k = 0; k < m.Keys.Count; k++)
                {
                    var key = m.Keys[k] is IrLiteral kl && kl.Kind == IrLitKind.String
                        ? (JsFormat.IsIdentifierName(kl.Str) ? kl.Str : JsFormat.Literal(kl.Str, '"'))
                        : "[" + X(m.Keys[k], PAssign) + "]";
                    parts.Add(key + ": " + X(m.Values[k], PAssign));
                }
                return ("{ " + string.Join(", ", parts) + " }", PAtom);
            }
            if (m.Keys.Count == 0) return ("new Map()", PPost);
            var pairs = new List<string>();
            for (int k = 0; k < m.Keys.Count; k++) pairs.Add("[" + X(m.Keys[k], PAssign) + ", " + X(m.Values[k], PAssign) + "]");
            return ("new Map([" + string.Join(", ", pairs) + "])", PPost);
        }

        static int BinPrec(IrBinOp op)
        {
            switch (op)
            {
                case IrBinOp.Or: return POr;
                case IrBinOp.And: return PAnd;
                case IrBinOp.Eq: case IrBinOp.Ne: return PEq;
                case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge: return PRel;
                case IrBinOp.Add: case IrBinOp.Sub: case IrBinOp.Concat: return PAdd;
                case IrBinOp.Pow: return PPow;
                default: return PMul;
            }
        }

        static string OpText(IrBinOp op)
        {
            switch (op)
            {
                case IrBinOp.Or: return "||";
                case IrBinOp.And: return "&&";
                case IrBinOp.Eq: return "===";
                case IrBinOp.Ne: return "!==";
                case IrBinOp.Lt: return "<";
                case IrBinOp.Le: return "<=";
                case IrBinOp.Gt: return ">";
                case IrBinOp.Ge: return ">=";
                case IrBinOp.Add: case IrBinOp.Concat: return "+";
                case IrBinOp.Sub: return "-";
                case IrBinOp.Mul: return "*";
                case IrBinOp.TrueDiv: return "/";
                case IrBinOp.ModTrunc: return "%";
                case IrBinOp.Pow: return "**";
            }
            return "?";
        }

        static bool IsStringy(IrExpr e) =>
            (e is IrLiteral l && l.Kind == IrLitKind.String) || TypeOf(e).IsString && !(e is IrBinary bb && bb.Op == IrBinOp.Concat && !IsStringyConcat(bb));

        static bool IsStringyConcat(IrBinary b) => IsStringy(b.Left) || IsStringy(b.Right);

        (string, int) Bin(IrBinary b)
        {
            switch (b.Op)
            {
                case IrBinOp.IntDivTrunc: return ("Math.trunc(" + X(b.Left, PMul) + " / " + X(b.Right, PMul + 1) + ")", PPost);
                case IrBinOp.IntDivFloor: return ("Math.floor(" + X(b.Left, PMul) + " / " + X(b.Right, PMul + 1) + ")", PPost);
                case IrBinOp.ModFloor:
                    {
                        if (NonNeg(b.Left) && PositiveLiteral(b.Right)) return (X(b.Left, PMul) + " % " + X(b.Right, PMul + 1), PMul);
                        var r = X(b.Right, PMul + 1);
                        if (!(b.Right is IrLiteral || b.Right is IrName)) Problem("Python's % with a computed divisor (printed as ((a % b) + b) % b, which evaluates b twice)");
                        return ("((" + X(b.Left, PMul) + " % " + r + ") + " + X(b.Right, PAdd + 1) + ") % " + r, PMul);
                    }
                case IrBinOp.Concat: return Concat(b);
                case IrBinOp.Mul when b.Left is IrListLit ll1 && ll1.Items.Count == 1:
                    return ("new Array(" + X(b.Right, PAssign) + ").fill(" + X(ll1.Items[0], PAssign) + ")", PPost);
                case IrBinOp.Mul when b.Right is IrListLit rl1 && rl1.Items.Count == 1:
                    return ("new Array(" + X(b.Left, PAssign) + ").fill(" + X(rl1.Items[0], PAssign) + ")", PPost);
                case IrBinOp.Mul when TypeOf(b.Left).IsListLike || TypeOf(b.Right).IsListLike:
                    Problem("repeating a list with * (JavaScript has no list repetition)");
                    break;
                case IrBinOp.Add when (TypeOf(b.Left).IsListLike || b.Left is IrListLit) && (TypeOf(b.Right).IsListLike || b.Right is IrListLit):
                    {
                        var parts = new List<string>();
                        void Flat(IrExpr x)
                        {
                            if (x is IrBinary xb && xb.Op == IrBinOp.Add && (TypeOf(xb.Left).IsListLike || xb.Left is IrListLit) && (TypeOf(xb.Right).IsListLike || xb.Right is IrListLit)) { Flat(xb.Left); Flat(xb.Right); }
                            else if (x is IrListLit xl) foreach (var it in xl.Items) parts.Add(X(it, PAssign));
                            else parts.Add("..." + X(x, PAssign));
                        }
                        Flat(b);
                        return ("[" + string.Join(", ", parts) + "]", PAtom);
                    }
                case IrBinOp.Pow:
                    {
                        var l = X(b.Left, PPow + 1);
                        if (b.Left is IrUnary || (b.Left is IrLiteral ll && Lit(ll).StartsWith("-"))) l = "(" + X(b.Left, PAssign) + ")";
                        return (l + " ** " + X(b.Right, PPow), PPow);
                    }
            }
            int p = BinPrec(b.Op);
            // x % n === 0 means the same for Python's and JavaScript's %
            if ((b.Op == IrBinOp.Eq || b.Op == IrBinOp.Ne) && b.Right is IrLiteral z && z.Kind == IrLitKind.Int && z.Int == 0 && b.Left is IrBinary lm && lm.Op == IrBinOp.ModFloor)
                return (X(lm.Left, PMul) + " % " + X(lm.Right, PMul + 1) + " " + OpText(b.Op) + " 0", p);
            return (X(b.Left, p) + " " + OpText(b.Op) + " " + X(b.Right, p + 1), p);
        }

        /// <summary>String concatenation: "a" + x when a side is a string, else a template literal (+ of two numbers would add).</summary>
        (string, int) Concat(IrBinary b)
        {
            var parts = new List<IrExpr>();
            void Flat(IrExpr e)
            {
                if (e is IrBinary cb && cb.Op == IrBinOp.Concat) { Flat(cb.Left); parts.Add(cb.Right); }
                else parts.Add(e);
            }
            Flat(b);
            if (parts.Count >= 2 && (IsStringy(parts[0]) || IsStringy(parts[1])))
            {
                var sb = new StringBuilder(X(parts[0], PAdd));
                for (int k = 1; k < parts.Count; k++) sb.Append(" + ").Append(X(parts[k], PAdd + 1));
                return (sb.ToString(), PAdd);
            }
            var t = new StringBuilder("`");
            foreach (var x in parts)
            {
                if (x is IrLiteral sl && sl.Kind == IrLitKind.String)
                {
                    foreach (var ch in sl.Str)
                    {
                        if (ch == '`' || ch == '\\') t.Append('\\').Append(ch);
                        else if (ch == '$') t.Append("\\$");
                        else if (ch == '\t') t.Append("\\t");
                        else t.Append(ch);
                    }
                }
                else t.Append("$" + "{").Append(X(x, PAssign)).Append('}');
            }
            return (t.Append('`').ToString(), PAtom);
        }

        string Call(IrCall c)
        {
            switch (c.Kind)
            {
                case IrCallKind.Builtin:
                    return (JsNames.ApiName(c.Builtin) ?? Ident(c.Builtin)) + "(" + Args(c.Args) + ")";
                case IrCallKind.Function:
                    {
                        var f = c.Function;
                        var n = f == null ? "f" : f.IsExternal ? Qualified(f) : Name(f);
                        return n + "(" + Args(c.Args) + ")";
                    }
                default:
                    return X(c.Callee, PPost) + "(" + Args(c.Args) + ")";
            }
        }

        string Lambda(IrLambda l)
        {
            PushScope();
            var ps = "(" + Params(l.Params) + ")";
            string res;
            if (l.BodyExpr != null)
            {
                var body = X(l.BodyExpr, PAssign);
                if (body.StartsWith("{")) body = "(" + body + ")";
                res = ps + " => " + (l.ReturnsValue || IsCallish(l.BodyExpr) ? body : "{ " + body + "; }");
            }
            else
            {
                int mark = Out.Length;
                Indent();
                PushScope();
                if (l.Body != null) { Stmts(l.Body.Stmts); Comments(l.Body.TrailingComments); }
                PopScope();
                Dedent();
                var inner = Out.ToString(mark, Out.Length - mark);
                Out.Length = mark;
                res = inner.Length == 0 ? ps + " => {}" : ps + " => {" + Options.Newline + inner + IndentText + "}";
            }
            PopScope();
            return res;
        }

        static bool IsCallish(IrExpr e) => e is IrCall || e is IrOpCall;

        static bool IsSetLike(IrType t) => t.Kind == IrTypeKind.Set;
        static bool IsMapLike(IrType t) => t.Kind == IrTypeKind.Map;

        (string, int) Op(IrOpCall o, bool stmt)
        {
            var a = o.Args;
            string R() => a.Count > 0 ? X(a[0], PPost) : "x";
            string A(int i) => i < a.Count ? X(a[i], PAssign) : "null";
            var rt = a.Count > 0 ? TypeOf(a[0]) : IrType.Unknown;
            void NeedStmt(string what) { if (!stmt) Problem(what + " used as a value"); }
            switch (o.Op)
            {
                case IrOp.Len:
                    if (IsMapLike(rt) && IsObjectStyle(a[0])) return ("Object.keys(" + A(0) + ").length", PPost);
                    return (R() + (IsMapLike(rt) || IsSetLike(rt) ? ".size" : ".length"), PPost);
                case IrOp.IsEmpty:
                    return (R() + (IsMapLike(rt) || IsSetLike(rt) ? ".size" : ".length") + " === 0", PEq);
                case IrOp.Contains:
                    if (IsMapLike(rt) && IsObjectStyle(a[0])) return (X(a[1], PRel + 1) + " in " + X(a[0], PRel + 1), PRel);
                    return (R() + (IsMapLike(rt) || IsSetLike(rt) ? ".has(" : ".includes(") + A(1) + ")", PPost);
                case IrOp.ListAppend: NeedStmt("push"); return (R() + ".push(" + A(1) + ")", PPost);
                case IrOp.ListInsert:
                    NeedStmt("insert");
                    if (a.Count > 1 && a[1] is IrLiteral z && z.Kind == IrLitKind.Int && z.Int == 0) return (R() + ".unshift(" + A(2) + ")", PPost);
                    return (R() + ".splice(" + A(1) + ", 0, " + A(2) + ")", PPost);
                case IrOp.ListPop:
                    if (a.Count < 2) return (R() + ".pop()", PPost);
                    if (a[1] is IrLiteral z2 && z2.Kind == IrLitKind.Int && z2.Int == 0) return (R() + ".shift()", PPost);
                    if (a[1] is IrLiteral m1 && m1.Kind == IrLitKind.Int && m1.Int == -1) return (R() + ".pop()", PPost);
                    return (R() + ".splice(" + A(1) + ", 1)" + (stmt ? "" : "[0]"), PPost);
                case IrOp.ListSet: NeedStmt("list set"); return (R() + "[" + A(1) + "] = " + A(2), PAssign);
                case IrOp.ListIndexOf: return (R() + ".indexOf(" + A(1) + ")", PPost);
                case IrOp.RemoveValue:
                case IrOp.RemoveValueIfPresent:
                    NeedStmt("remove");
                    if (IsSetLike(rt)) return (R() + ".delete(" + A(1) + ")", PPost);
                    return (R() + ".splice(" + R() + ".indexOf(" + A(1) + "), 1)", PPost);
                case IrOp.Clear:
                    NeedStmt("clear");
                    if (IsMapLike(rt) || IsSetLike(rt) || !rt.IsKnown) return (R() + ".clear()", PPost);
                    return (R() + ".length = 0", PAssign);
                case IrOp.SetAdd: NeedStmt("add"); return (R() + ".add(" + A(1) + ")", PPost);
                case IrOp.MapPut:
                    NeedStmt("map put");
                    if (IsObjectStyle(a[0])) return (R() + "[" + A(1) + "] = " + A(2), PAssign);
                    return (R() + ".set(" + A(1) + ", " + A(2) + ")", PPost);
                case IrOp.MapGetOrNull:
                    if (IsObjectStyle(a[0])) return (R() + "[" + A(1) + "]", PPost);
                    return (R() + ".get(" + A(1) + ")", PPost);
                case IrOp.MapGetOrDefault: return (R() + ".get(" + A(1) + ") ?? " + X(a[2], PPost), PAssign);
                case IrOp.MapRemove:
                case IrOp.MapRemoveIfPresent:
                    if (!stmt) Problem("removing from a map as a value (JavaScript's map.delete returns true / false)");
                    if (IsObjectStyle(a[0])) Problem("delete on a plain object");
                    return (R() + ".delete(" + A(1) + ")", PPost);
                case IrOp.MapKeys:
                    if (IsObjectStyle(a[0])) return ("Object.keys(" + A(0) + ")", PPost);
                    return (R() + ".keys()", PPost);
                case IrOp.MapValues:
                    if (IsObjectStyle(a[0])) return ("Object.values(" + A(0) + ")", PPost);
                    return (R() + ".values()", PPost);
                case IrOp.ListCopy: return ("[..." + A(0) + "]", PAtom);
                case IrOp.SetCopy: return ("new Set(" + A(0) + ")", PPost);
                case IrOp.MapCopy: return ("new Map(" + A(0) + ")", PPost);
                case IrOp.ArrayNew: return ("new Array(" + A(0) + ").fill(" + A(1) + ")", PPost);
                case IrOp.ArrayNew2: return ("Array.from({ length: " + A(0) + " }, () => new Array(" + A(1) + ").fill(" + A(2) + "))", PPost);
                case IrOp.Floor: return ("Math.floor(" + A(0) + ")", PPost);
                case IrOp.Ceil: return ("Math.ceil(" + A(0) + ")", PPost);
                case IrOp.Round: return ("Math.round(" + A(0) + ")", PPost);
                case IrOp.Sqrt: return ("Math.sqrt(" + A(0) + ")", PPost);
                case IrOp.PosX: case IrOp.CompanionEntity: return (R() + "[0]", PPost);
                case IrOp.PosY: case IrOp.CompanionPos: return (R() + "[1]", PPost);
            }
            Problem("operation " + o.Op);
            return ("null", PAtom);
        }
    }
}
