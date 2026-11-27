using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// JavaScript AST → Farm IR (docs/IR.md) with JavaScript's static semantics: lexical (block) scoping, hoisted
    /// function declarations, const, strict-mode ReferenceErrors at compile time, closures. Module-scope variables used
    /// by a function declaration (or assigned by any closure, or exported) become Global symbols; other top-level
    /// variables are script Locals (the Python frontend's rule). Collection methods are lowered by name, then
    /// JsFrontend fixes up the choices that depend on types after IrTypeInference.
    /// </summary>
    internal sealed partial class JsLowering
    {
        public const string ObjectTag = "js:object", MapTag = "js:map", SetTag = "js:set";

        static readonly ConditionalWeakTable<IrMapLit, object> objectLiterals = new ConditionalWeakTable<IrMapLit, object>();
        static readonly object marker = new object();

        /// <summary>True for a map literal the JS frontend built from a plain object literal ({ a: 1 }).</summary>
        public static bool IsObjectLiteral(IrMapLit m) => m != null && objectLiterals.TryGetValue(m, out _);
        static void MarkObject(IrMapLit m) { try { objectLiterals.Add(m, marker); } catch (ArgumentException) { } }

        static readonly ConditionalWeakTable<IrVarDecl, object> functionDeclarations = new ConditionalWeakTable<IrVarDecl, object>();
        /// <summary>True for the IrVarDecl the JS frontend built from a nested function declaration (function f() {}).</summary>
        public static bool IsFunctionDeclaration(IrVarDecl v) => v != null && functionDeclarations.TryGetValue(v, out _);

        // ================================================================== scopes
        sealed class Fn
        {
            public Fn Parent;
            public bool IsDecl;      // a top-level function declaration (IrFunction); false: closure (IrLambda)
            public bool InsideDecl => IsDecl || (Parent != null && Parent.InsideDecl);
        }

        sealed class Binding
        {
            public IrSymbol Sym;
            public string Kind;      // let const var function param import namespace farm
            public Fn Owner;         // null: module code
            public bool ModuleScope;
            public int DeclPos;
            public bool NeedsGlobal, Exported;
            public JsFunctionDecl Decl;      // function declarations (arity checks)
            public string Module;            // namespace / named imports: the other module
            public JsModuleInfo Info;        // namespace imports: the other module's declarations (null: unknown)
            public string Imported;          // named imports: the exported name
            public bool FromFunctionDecl;    // nested function declaration (hoisted inside its block)
        }

        sealed class Scope
        {
            public Scope Parent;
            public bool IsFunction;          // function body / module: var declarations land here
            public readonly Dictionary<string, Binding> Names = new Dictionary<string, Binding>();
        }

        readonly List<Diagnostic> errors;
        readonly IrModule mod;
        readonly string src;
        readonly string fileName;
        readonly IReadOnlyDictionary<string, string> otherFiles;
        /// <summary>The compile options (OtherFileLanguages: windows written in other languages); null in tests that lower directly.</summary>
        internal CompileOptions options;
        Scope scope, moduleScope;
        Fn fn;
        readonly List<string> breakables = new List<string>();   // "loop" / "switch" (innermost last)
        readonly List<Binding> moduleBindings = new List<Binding>();
        readonly HashSet<IrVarDecl> functionDecls = new HashSet<IrVarDecl>();
        readonly Dictionary<IrOpCall, string> lenSource = new Dictionary<IrOpCall, string>();
        readonly List<IrForEach> tupleLoops = new List<IrForEach>();
        /// <summary>Nodes FixUps may change after type inference (+ / += and Map-or-Set delete): no tree walk needed.</summary>
        readonly List<IrNode> fixupCandidates = new List<IrNode>();
        /// <summary>const p = [a, b]: array literals that may become game tuples (see ConstTuples).</summary>
        readonly Dictionary<IrListLit, Binding> tupleCandidates = new Dictionary<IrListLit, Binding>();
        readonly List<(IrForEach Loop, bool IsIn, Span At)> forChecks = new List<(IrForEach, bool, Span)>();
        readonly Dictionary<string, IrSymbol> externals = new Dictionary<string, IrSymbol>();
        readonly Dictionary<string, IrSymbol> bags = new Dictionary<string, IrSymbol>();

        JsLowering(string source, string fileName, IReadOnlyDictionary<string, string> others, List<Diagnostic> errors)
        {
            src = source ?? "";
            this.fileName = string.IsNullOrEmpty(fileName) ? "main" : fileName;
            otherFiles = others;
            this.errors = errors;
            mod = new IrModule { Name = this.fileName, SourceLanguage = "js" };
        }

        public static IrModule Lower(List<JsStmt> program, string source, string fileName, IReadOnlyDictionary<string, string> others, List<Diagnostic> errors)
        {
            var l = new JsLowering(source, fileName, others, errors);
            l.LowerModule(program);
            return l.mod;
        }

        void Error(string msg, Span s)
        {
            int st = Math.Max(0, s.Start), en = Math.Max(st, s.End);
            foreach (var d in errors) if (d.Start == st && d.End == en && d.Message == msg) return;
            errors.Add(new Diagnostic(msg, st, en));
        }

        void PushScope(bool isFunction = false) => scope = new Scope { Parent = scope, IsFunction = isFunction };
        void PopScope() => scope = scope.Parent;

        Scope FunctionScope()
        {
            for (var s = scope; s != null; s = s.Parent) if (s.IsFunction) return s;
            return moduleScope;
        }

        static bool Reserved(string name) => name != null && name.StartsWith("__j", StringComparison.Ordinal);

        Binding Declare(string name, Span at, string kind, Scope target = null)
        {
            target = target ?? (kind == "var" ? FunctionScope() : scope);
            if (Reserved(name)) Error("identifiers starting with __j are reserved by JavaFarmer", at);
            if (target.Names.TryGetValue(name, out var old))
            {
                bool ok = (kind == "var" && (old.Kind == "var" || old.Kind == "param")) || (kind == "param" && old.Kind == "param" && false);
                if (!ok) { Error("Identifier '" + name + "' has already been declared", at); return old; }
                return old;
            }
            // a var may not hoist over a let/const of an inner block with the same name
            if (kind == "var")
                for (var s = scope; s != null && s != target; s = s.Parent)
                    if (s.Names.TryGetValue(name, out var inner) && inner.Kind != "var") { Error("Identifier '" + name + "' has already been declared", at); return inner; }
            bool moduleScope = target == this.moduleScope;
            IrSymbolKind sk = kind == "param" ? IrSymbolKind.Param : kind == "function" && moduleScope ? IrSymbolKind.Function : IrSymbolKind.Local;
            var b = new Binding
            {
                Sym = new IrSymbol(name, sk) { DeclSpan = at, IsConst = kind == "const" },
                Kind = kind, Owner = fn, ModuleScope = moduleScope, DeclPos = at.Start,
            };
            target.Names[name] = b;
            if (moduleScope) moduleBindings.Add(b);
            return b;
        }

        Binding Lookup(string name)
        {
            for (var s = scope; s != null; s = s.Parent)
                if (s.Names.TryGetValue(name, out var b)) return b;
            return null;
        }

        /// <summary>Declares the names a statement list binds in the current scope (and vars in the function scope).</summary>
        void Predeclare(List<JsStmt> stmts)
        {
            foreach (var s in stmts)
            {
                switch (s)
                {
                    case JsVarDecl v when v.Kind != "var":
                        foreach (var d in v.Decls) foreach (var (n, sp) in PatternNames(d.Target)) { var b = Declare(n, sp, v.Kind); if (v.IsExport) b.Exported = true; }
                        break;
                    case JsFunctionDecl f:
                        {
                            var b = Declare(f.Name, f.NameSpan, "function");
                            b.Decl = f;
                            b.FromFunctionDecl = true;
                            if (f.IsExport) b.Exported = true;
                            if (b.Sym.Kind == IrSymbolKind.Function) b.Sym.IsConst = true;
                            break;
                        }
                }
            }
        }

        /// <summary>Hoists the var declarations of a function body / the module (not into nested functions).</summary>
        void PredeclareVars(IEnumerable<JsStmt> stmts)
        {
            foreach (var s in stmts) VarsOf(s);
        }

        void VarsOf(JsStmt s)
        {
            switch (s)
            {
                case JsVarDecl v when v.Kind == "var":
                    foreach (var d in v.Decls) foreach (var (n, sp) in PatternNames(d.Target)) { var b = Declare(n, sp, "var", FunctionScope()); if (v.IsExport) b.Exported = true; }
                    break;
                case JsBlock b: foreach (var x in b.Stmts) VarsOf(x); break;
                case JsIf i: VarsOf(i.Then); if (i.Else != null) VarsOf(i.Else); break;
                case JsWhile w: VarsOf(w.Body); break;
                case JsDoWhile dw: VarsOf(dw.Body); break;
                case JsFor f: if (f.InitDecl != null) VarsOf(f.InitDecl); VarsOf(f.Body); break;
                case JsForOf fo:
                    if (fo.DeclKind == "var") foreach (var (n, sp) in PatternNames(fo.Target)) Declare(n, sp, "var", FunctionScope());
                    VarsOf(fo.Body);
                    break;
                case JsSwitch sw: foreach (var c in sw.Cases) foreach (var x in c.Body) VarsOf(x); break;
            }
        }

        static IEnumerable<(string, Span)> PatternNames(JsExpr target)
        {
            if (target is JsIdent id) yield return (id.Name, id.Span);
            else if (target is JsArrayLit a) foreach (var it in a.Items) foreach (var x in PatternNames(it)) yield return x;
        }

        // ================================================================== module
        void LowerModule(List<JsStmt> program)
        {
            moduleScope = scope = new Scope { IsFunction = true };
            fn = null;
            // imports and exports first (they bind names for the whole module)
            foreach (var s in program) if (s is JsImport im) DeclareImport(im);
            PredeclareVars(program);
            Predeclare(program);
            foreach (var s in program) if (s is JsExportList ex) foreach (var n in ex.Names)
                {
                    if (moduleScope.Names.TryGetValue(n.Local, out var b) && b.Kind != "namespace" && b.Kind != "import" && b.Kind != "farm") b.Exported = true;
                    else Error("Export '" + n.Local + "' is not defined in module", n.LocalSpan);
                }

            foreach (var s in program)
            {
                int before = mod.Items.Count;
                if (s is JsFunctionDecl f)
                {
                    mod.Items.Add(LowerFunction(f, moduleScope.Names[f.Name]));
                }
                else
                {
                    var list = new List<IrStmt>();
                    Stmt(s, list);
                    mod.Items.AddRange(list);
                }
                if (mod.Items.Count > before) mod.Items[before].BlankLinesBefore = s.BlankLines;
            }
            foreach (var b in moduleBindings) if (b.Exported) b.NeedsGlobal = true;
            MakeGlobals();
            HoistModuleFunctions();
            mod.Span = new Span(0, src.Length);
        }

        IrFunction LowerFunction(JsFunctionDecl f, Binding b)
        {
            var sym = b.Sym;
            var fun = new IrFunction { Symbol = sym, Span = f.Span, HeaderSpan = f.HeaderSpan, NameSpan = f.NameSpan, IsPrivate = !b.Exported };
            sym.Function = fun;
            var saveFn = fn;
            var saveBreak = new List<string>(breakables);
            breakables.Clear();
            fn = new Fn { Parent = fn, IsDecl = true };
            PushScope(true);
            foreach (var p in f.Params)
            {
                var pb = Declare(p.Name, p.NameSpan, "param");
                fun.Params.Add(new IrParam { Symbol = pb.Sym, NameSpan = p.NameSpan, Span = p.Span });
            }
            // defaults see the parameters
            for (int k = 0; k < f.Params.Count; k++)
                if (f.Params[k].Default != null) fun.Params[k].Default = Expr(f.Params[k].Default);
            fun.Body = BodyBlock(f.Body);
            PopScope();
            fn = saveFn;
            breakables.Clear(); breakables.AddRange(saveBreak);
            return fun;
        }

        /// <summary>A function body: the parameter scope is the body's top-level scope (let x clashes with a param x).</summary>
        IrBlock BodyBlock(JsBlock body)
        {
            PredeclareVars(body.Stmts);
            Predeclare(body.Stmts);
            var blk = new IrBlock { Span = body.Span };
            StmtList(body.Stmts, blk.Stmts);
            return blk;
        }

        IrLambda LowerLambda(JsFunctionExpr f, string ownName = null)
        {
            var lam = new IrLambda { Span = f.Span, HeaderSpan = f.HeaderSpan };
            var saveFn = fn;
            var saveBreak = new List<string>(breakables);
            breakables.Clear();
            fn = new Fn { Parent = fn, IsDecl = false };
            PushScope(true);
            if (f.Name != null && ownName == null)
            {
                var nb = Declare(f.Name, f.HeaderSpan, "const");
                nb.Kind = "fnexpr";
            }
            foreach (var p in f.Params)
            {
                var pb = Declare(p.Name, p.NameSpan, "param");
                lam.Params.Add(new IrParam { Symbol = pb.Sym, NameSpan = p.NameSpan, Span = p.Span });
            }
            for (int k = 0; k < f.Params.Count; k++)
                if (f.Params[k].Default != null) lam.Params[k].Default = Expr(f.Params[k].Default);
            if (f.ExprBody != null)
            {
                lam.BodyExpr = Expr(f.ExprBody);
                lam.ReturnsValue = true;
            }
            else lam.Body = BodyBlock(f.Body);
            PopScope();
            fn = saveFn;
            breakables.Clear(); breakables.AddRange(saveBreak);
            return lam;
        }

        // ------------------------------------------------------------------ globals
        /// <summary>Module-scope variables that functions use become Global symbols: their top-level declaration becomes an IrGlobal.</summary>
        void MakeGlobals()
        {
            var globals = new HashSet<IrSymbol>();
            foreach (var b in moduleBindings)
            {
                if (!b.NeedsGlobal || b.Sym.Kind != IrSymbolKind.Local) continue;
                if (b.Kind != "let" && b.Kind != "const" && b.Kind != "var") continue;
                b.Sym.Kind = IrSymbolKind.Global;
                globals.Add(b.Sym);
            }
            if (globals.Count == 0) return;
            var placed = new HashSet<IrSymbol>();
            for (int k = 0; k < mod.Items.Count; k++)
            {
                var item = mod.Items[k];
                if (item is IrVarDecl rv && rv.Symbol != null && globals.Contains(rv.Symbol) && placed.Contains(rv.Symbol))
                {
                    // var x again at top level: a plain assignment
                    if (rv.Init == null || (rv.Init is IrLiteral rl && rl.Synthetic)) { mod.Items.RemoveAt(k); k--; continue; }
                    mod.Items[k] = new IrAssign { Target = new IrName(rv.Symbol) { Span = rv.NameSpan, NameSpan = rv.NameSpan }, Value = rv.Init, Span = rv.Span, OpSpan = rv.NameSpan, Leading = rv.Leading, Trailing = rv.Trailing, BlankLinesBefore = rv.BlankLinesBefore };
                    continue;
                }
                if (item is IrVarDecl v && v.Symbol != null && globals.Contains(v.Symbol) && placed.Add(v.Symbol))
                {
                    bool implicitNull = v.Init is IrLiteral l && l.Kind == IrLitKind.Null && l.Synthetic;
                    mod.Items[k] = new IrGlobal
                    {
                        Symbol = v.Symbol, Init = v.Init, NameSpan = v.NameSpan, Span = v.Span, TypeExplicit = false,
                        ImplicitInit = implicitNull, Leading = v.Leading, Trailing = v.Trailing, BlankLinesBefore = v.BlankLinesBefore,
                        Synthetic = v.Synthetic && v.Init == null, IsPrivate = !IsExported(v.Symbol),
                    };
                    if (v.Init == null) { ((IrGlobal)mod.Items[k]).ImplicitInit = true; ((IrGlobal)mod.Items[k]).Synthetic = true; }
                    continue;
                }
                if (item is IrStmt st)
                {
                    // globals declared inside a top-level statement (var in a block, destructuring): declare before it
                    var inner = new List<IrSymbol>();
                    RewriteInnerDecls(st, globals, placed, inner);
                    foreach (var g in inner)
                    {
                        mod.Items.Insert(k, new IrGlobal { Symbol = g, Init = null, ImplicitInit = true, Synthetic = true, TypeExplicit = false, IsPrivate = !IsExported(g) });
                        k++;
                    }
                }
            }
        }

        bool IsExported(IrSymbol s) => moduleBindings.Any(b => b.Sym == s && b.Exported);

        /// <summary>IrVarDecl of a global nested in a top-level statement → assignment (or nothing); collects the symbols.</summary>
        void RewriteInnerDecls(IrNode n, HashSet<IrSymbol> globals, HashSet<IrSymbol> placed, List<IrSymbol> found)
        {
            foreach (var blk in IrWalk.Descendants(n).OfType<IrBlock>().ToList())
            {
                for (int i = 0; i < blk.Stmts.Count; i++)
                {
                    if (!(blk.Stmts[i] is IrVarDecl v) || v.Symbol == null || !globals.Contains(v.Symbol)) continue;
                    if (placed.Add(v.Symbol)) found.Add(v.Symbol);
                    if (v.Init == null || (v.Init is IrLiteral l && l.Synthetic)) { blk.Stmts.RemoveAt(i); i--; continue; }
                    blk.Stmts[i] = new IrAssign
                    {
                        Target = new IrName(v.Symbol) { Span = v.NameSpan, NameSpan = v.NameSpan }, Value = v.Init, Span = v.Span,
                        OpSpan = v.NameSpan, Leading = v.Leading, Trailing = v.Trailing, BlankLinesBefore = v.BlankLinesBefore,
                    };
                }
            }
        }

        /// <summary>Top-level code that (transitively) calls a function declared further down: hoist all function declarations.</summary>
        void HoistModuleFunctions()
        {
            var fnIndex = new Dictionary<IrSymbol, int>();
            for (int k = 0; k < mod.Items.Count; k++) if (mod.Items[k] is IrFunction f && f.Symbol != null) fnIndex[f.Symbol] = k;
            if (fnIndex.Count == 0) return;
            // common case: every function is declared before the first top-level statement that could call one
            int lastFn = fnIndex.Values.Max(), firstStmt = -1;
            for (int k = 0; k < mod.Items.Count; k++) if (!(mod.Items[k] is IrFunction) && !(mod.Items[k] is IrImport)) { firstStmt = k; break; }
            if (firstStmt < 0 || firstStmt > lastFn) return;
            var deps = new Dictionary<IrSymbol, HashSet<IrSymbol>>();
            foreach (var f in mod.Items.OfType<IrFunction>()) deps[f.Symbol] = FunctionRefs(f.Body);
            bool need = false;
            for (int k = 0; k < mod.Items.Count && !need; k++)
            {
                if (mod.Items[k] is IrFunction) continue;
                var reach = new HashSet<IrSymbol>();
                var stack = new Stack<IrSymbol>(FunctionRefs(mod.Items[k]));
                while (stack.Count > 0)
                {
                    var s = stack.Pop();
                    if (!reach.Add(s)) continue;
                    if (deps.TryGetValue(s, out var more)) foreach (var m in more) stack.Push(m);
                }
                foreach (var s in reach) if (fnIndex.TryGetValue(s, out var at) && at > k) { need = true; break; }
            }
            if (!need) return;
            var imports = mod.Items.Where(i => i is IrImport).ToList();
            var fns = mod.Items.Where(i => i is IrFunction).ToList();
            var rest = mod.Items.Where(i => !(i is IrImport) && !(i is IrFunction)).ToList();
            mod.Items.Clear();
            mod.Items.AddRange(imports);
            mod.Items.AddRange(fns);
            mod.Items.AddRange(rest);
        }

        static HashSet<IrSymbol> FunctionRefs(IrNode n)
        {
            var res = new HashSet<IrSymbol>();
            foreach (var d in IrWalk.Descendants(n))
            {
                if (d is IrCall c && c.Kind == IrCallKind.Function && c.Function != null && c.Function.Kind == IrSymbolKind.Function) res.Add(c.Function);
                else if (d is IrFuncRef r && r.Function != null && r.Function.Kind == IrSymbolKind.Function) res.Add(r.Function);
            }
            return res;
        }

        // ================================================================== statements
        void StmtList(List<JsStmt> stmts, List<IrStmt> into)
        {
            int start = into.Count;
            bool hasFnDecl = false;
            foreach (var s in stmts)
            {
                int before = into.Count;
                Stmt(s, into);
                if (into.Count > before) into[before].BlankLinesBefore = s.BlankLines;
                if (s is JsFunctionDecl) hasFnDecl = true;
            }
            if (hasFnDecl) HoistBlockFunctions(into, start);
        }

        /// <summary>Nested function declarations are callable before their definition: move them up when that is needed.</summary>
        void HoistBlockFunctions(List<IrStmt> list, int start)
        {
            bool need = false;
            for (int k = start; k < list.Count && !need; k++)
            {
                if (!(list[k] is IrVarDecl v) || !functionDecls.Contains(v)) continue;
                for (int j = start; j < k && !need; j++)
                    if (IrWalk.Descendants(list[j]).Any(n => n is IrName nm && nm.Symbol == v.Symbol)) need = true;
            }
            if (!need) return;
            var fns = list.Skip(start).Where(x => x is IrVarDecl v && functionDecls.Contains(v)).ToList();
            var rest = list.Skip(start).Where(x => !(x is IrVarDecl v && functionDecls.Contains(v))).ToList();
            list.RemoveRange(start, list.Count - start);
            list.AddRange(fns);
            list.AddRange(rest);
        }

        IrBlock Block(JsStmt s, bool newScope = true)
        {
            if (s is JsBlock b)
            {
                if (newScope) PushScope();
                Predeclare(b.Stmts);
                var blk = new IrBlock { Span = b.Span };
                StmtList(b.Stmts, blk.Stmts);
                if (newScope) PopScope();
                return blk;
            }
            var single = new IrBlock { Span = s.Span };
            if (newScope) PushScope();
            Stmt(s, single.Stmts);
            if (newScope) PopScope();
            return single;
        }

        void Stmt(JsStmt s, List<IrStmt> outList)
        {
            switch (s)
            {
                case JsEmpty _: return;
                case JsBlock b: outList.Add(Block(b)); return;
                case JsVarDecl v: VarDecl(v, outList); return;
                case JsFunctionDecl f:
                    {
                        var b = Lookup(f.Name);
                        if (b == null || b.Decl != f) b = Declare(f.Name, f.NameSpan, "function");
                        var lam = LowerFunctionAsLambda(f);
                        var decl = new IrVarDecl { Symbol = b.Sym, Init = lam, NameSpan = f.NameSpan, TypeExplicit = false, Span = f.Span };
                        functionDecls.Add(decl);
                        try { functionDeclarations.Add(decl, marker); } catch (ArgumentException) { }
                        outList.Add(decl);
                        return;
                    }
                case JsExprStmt es: StmtExpr(es.Expr, es.Span, outList); return;
                case JsIf i: outList.Add(If(i)); return;
                case JsWhile w:
                    {
                        var cond = Expr(w.Cond);
                        breakables.Add("loop");
                        var body = Block(w.Body);
                        breakables.RemoveAt(breakables.Count - 1);
                        outList.Add(new IrWhile { Cond = cond, Body = body, KwSpan = w.KwSpan, Span = w.Span });
                        return;
                    }
                case JsDoWhile dw:
                    {
                        breakables.Add("loop");
                        var body = Block(dw.Body);
                        breakables.RemoveAt(breakables.Count - 1);
                        var cond = Expr(dw.Cond);
                        outList.Add(new IrDoWhile { Body = body, Cond = cond, KwSpan = dw.KwSpan, WhileSpan = dw.WhileSpan, Span = dw.Span });
                        return;
                    }
                case JsFor f: outList.Add(For(f)); return;
                case JsForOf fo: outList.Add(ForOf(fo)); return;
                case JsSwitch sw: outList.Add(Switch(sw)); return;
                case JsBreak br:
                    if (breakables.Count > 0 && breakables[breakables.Count - 1] == "switch")
                        Error("break inside a switch case is only supported as the last statement of the case", br.Span);
                    outList.Add(new IrBreak { Span = br.Span });
                    return;
                case JsContinue c: outList.Add(new IrContinue { Span = c.Span }); return;
                case JsReturn r: outList.Add(new IrReturn { Value = r.Value != null ? Expr(r.Value) : null, Span = r.Span }); return;
                case JsImport im: LowerImport(im, outList); return;
                case JsExportList _: return;
            }
            Error("this statement is not supported in JavaFarmer", s.Span);
        }

        IrLambda LowerFunctionAsLambda(JsFunctionDecl f) =>
            LowerLambda(new JsFunctionExpr { Name = f.Name, Params = f.Params, Body = f.Body, HeaderSpan = f.HeaderSpan, Span = f.Span }, f.Name);

        void VarDecl(JsVarDecl v, List<IrStmt> outList)
        {
            foreach (var d in v.Decls)
            {
                if (d.Target is JsIdent id)
                {
                    var b = Lookup(id.Name);
                    if (b == null) b = Declare(id.Name, id.Span, v.Kind);
                    IrExpr init;
                    if (d.Init != null) init = DeclInit(d.Init, b);
                    else if (v.Kind == "var" && b.Kind == "var" && b.DeclPos != id.Span.Start) continue;   // var x; again: no-op
                    else init = new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Synthetic = true, Span = id.Span };
                    outList.Add(new IrVarDecl { Symbol = b.Sym, Init = init, NameSpan = id.Span, TypeExplicit = false, Span = d.Span.IsNone ? v.Span : DeclSpan(v, d) });
                    continue;
                }
                if (d.Target is JsArrayLit pat)
                {
                    var value = DestructureValue(d.Init);
                    var tuple = PatternTuple(pat, n =>
                    {
                        var b = Lookup(n.Name) ?? Declare(n.Name, n.Span, v.Kind);
                        outList.Add(new IrVarDecl { Symbol = b.Sym, Init = null, NameSpan = n.Span, TypeExplicit = false, Synthetic = true });
                        return b.Sym;
                    });
                    outList.Add(new IrAssign { Target = tuple, Value = value, Span = DeclSpan(v, d), OpSpan = pat.Span });
                }
            }
        }

        /// <summary>[a, [b, c]] → nested IrTupleLit of the names (bind: the symbol of each name).</summary>
        IrTupleLit PatternTuple(JsArrayLit pat, Func<JsIdent, IrSymbol> bind)
        {
            var tuple = new IrTupleLit { Span = pat.Span };
            foreach (var it in pat.Items)
            {
                if (it is JsIdent pid) tuple.Items.Add(new IrName(bind(pid)) { Span = pid.Span, NameSpan = pid.Span });
                else if (it is JsArrayLit inner) tuple.Items.Add(PatternTuple(inner, bind));
                else Error("holes in destructuring are not supported in JavaFarmer", pat.Span);
            }
            return tuple;
        }

        /// <summary>A declaration's span: "let x = 1" for a single declarator, else the declarator.</summary>
        static Span DeclSpan(JsVarDecl v, JsDeclarator d) => v.Decls.Count == 1 ? v.Span : d.Span;

        /// <summary>An initialiser; object / Map / Set literals tag the variable so printers keep the source's style.</summary>
        IrExpr DeclInit(JsExpr init, Binding b)
        {
            var e = Expr(init);
            if (b.Kind == "const" && e is IrListLit ll && init is JsArrayLit al && al.Paren.IsNone && al.Items.Count >= 2
                && al.Items.All(x => x != null && !(x is JsSpread)))
                tupleCandidates[ll] = b;
            if (b.Sym.Tag == null)
            {
                if (init is JsObjectLit) b.Sym.Tag = ObjectTag;
                else if (init is JsNew n && n.Callee is JsIdent ni) { if (ni.Name == "Map") b.Sym.Tag = MapTag; else if (ni.Name == "Set") b.Sym.Tag = SetTag; }
            }
            return e;
        }

        /// <summary>The value of [a, b] = value: an array literal becomes a tuple (Python "a, b = 1, 2").</summary>
        IrExpr DestructureValue(JsExpr init)
        {
            if (init is JsArrayLit al && al.Items.All(x => x != null && !(x is JsSpread)))
            {
                var t = new IrTupleLit { Span = al.Span, ParenSpan = al.Paren };
                foreach (var x in al.Items) t.Items.Add(Expr(x));
                return t;
            }
            return Expr(init);
        }

        IrIf If(JsIf i)
        {
            var r = new IrIf { Cond = Expr(i.Cond), Then = Block(i.Then), KwSpan = i.KwSpan, Span = i.Span };
            if (i.Else is JsIf ei) r.Else = If(ei);
            else if (i.Else != null)
            {
                var eb = Block(i.Else);
                eb.Span = Span.Cover(i.ElseSpan, eb.Span);
                r.Else = eb;
            }
            return r;
        }

        IrStmt For(JsFor f)
        {
            PushScope();
            var cf = new IrClassicFor { KwSpan = f.KwSpan, HeaderSpan = f.HeaderSpan, Span = f.Span };
            if (f.InitDecl != null)
            {
                if (f.InitDecl.Kind != "var") foreach (var d in f.InitDecl.Decls) foreach (var (n, sp) in PatternNames(d.Target)) Declare(n, sp, f.InitDecl.Kind);
                VarDecl(f.InitDecl, cf.Init);
            }
            else if (f.InitExprs != null) foreach (var e in f.InitExprs) StmtExpr(e, e.Span, cf.Init);
            cf.Cond = f.Cond != null ? Expr(f.Cond) : null;
            foreach (var u in f.Update) StmtExpr(u, u.Span, cf.Update);
            breakables.Add("loop");
            cf.Body = Block(f.Body);
            breakables.RemoveAt(breakables.Count - 1);
            PopScope();
            return cf;
        }

        IrStmt ForOf(JsForOf f)
        {
            PushScope();
            var fe = new IrForEach { KwSpan = f.KwSpan, Span = f.Span, VarSpan = f.Target.Span };
            // for (const [k, v] of map.entries()) / Object.entries(o): key and value variables of a map
            JsExpr mapOf = null;
            if (!f.IsIn && f.Target is JsArrayLit && f.Iter is JsCall ec && ec.Args.Count == 0 && ec.Callee is JsMember em && em.Name == "entries") mapOf = em.Object;
            if (!f.IsIn && f.Target is JsArrayLit && f.Iter is JsCall oc && oc.Args.Count == 1 && oc.Callee is JsMember om && om.Name == "entries" && om.Object is JsIdent oi && oi.Name == "Object" && Lookup("Object") == null) mapOf = oc.Args[0];
            fe.Iterable = Expr(mapOf ?? f.Iter);
            IrSymbol LoopVar(JsIdent id)
            {
                if (f.DeclKind != null)
                {
                    var b = Declare(id.Name, id.Span, f.DeclKind == "var" ? "var" : f.DeclKind == "const" ? "const" : "let");
                    if (f.DeclKind == "const") b.Sym.IsConst = false;   // rebound every iteration
                    return b.Sym;
                }
                return AssignTargetSymbol(id);
            }
            if (f.Target is JsIdent tid)
            {
                fe.Var = LoopVar(tid);
            }
            else if (f.Target is JsArrayLit pat)
            {
                if (f.IsIn) Error("destructuring in for...in is not supported in JavaFarmer", pat.Span);
                var names = pat.Items.Select(x => x as JsIdent).ToList();
                if (mapOf != null && names.Any(x => x == null)) Error("use for (const [key, value] of map.entries()) with exactly two names in JavaFarmer", pat.Span);
                names = names.Where(x => x != null).ToList();
                if (mapOf != null)
                {
                    if (names.Count != 2) Error("use for (const [key, value] of map.entries()) with exactly two names in JavaFarmer", pat.Span);
                    if (names.Count > 0) fe.Var = LoopVar(names[0]);
                    if (names.Count > 1) { fe.ValueVar = LoopVar(names[1]); fe.ValueVarSpan = names[1].Span; }
                    fe.VarSpan = names.Count > 0 ? names[0].Span : pat.Span;
                }
                else
                {
                    // for (const [a, b] of pairs): hidden loop variable + "a, b = it" (a Map iterable is fixed up after inference)
                    fe.Var = new IrSymbol(string.Join("_", PatternNames(pat).Select(x => x.Item1)), IrSymbolKind.Local) { Tag = PyLowering.TupleForTag };
                    var decls = new List<IrStmt>();
                    var tuple = PatternTuple(pat, n =>
                    {
                        var s = LoopVar(n);
                        if (f.DeclKind != null) decls.Add(new IrVarDecl { Symbol = s, NameSpan = n.Span, TypeExplicit = false, Synthetic = true });
                        return s;
                    });
                    fe.Body = new IrBlock();
                    fe.Body.Stmts.AddRange(decls);
                    fe.Body.Stmts.Add(new IrAssign { Target = tuple, Value = new IrName(fe.Var) { Span = pat.Span, NameSpan = pat.Span, Synthetic = true }, Synthetic = true, Span = pat.Span });
                    tupleLoops.Add(fe);
                }
            }
            breakables.Add("loop");
            var body = Block(f.Body);
            breakables.RemoveAt(breakables.Count - 1);
            if (fe.Body != null && fe.Body.Stmts.Count > 0) { body.Stmts.InsertRange(0, fe.Body.Stmts); }
            fe.Body = body;
            PopScope();
            forChecks.Add((fe, f.IsIn, f.Iter.Span));
            return fe;
        }

        static bool EndsCase(JsStmt s)
        {
            switch (s)
            {
                case JsBreak _: case JsReturn _: case JsContinue _: return true;
                case JsBlock b: return b.Stmts.Count > 0 && EndsCase(b.Stmts[b.Stmts.Count - 1]);
                case JsIf i: return i.Else != null && EndsCase(i.Then) && EndsCase(i.Else);
            }
            return false;
        }

        /// <summary>Drops the break that ends a case (possibly the last statement of a block case body).</summary>
        static List<JsStmt> WithoutFinalBreak(List<JsStmt> body)
        {
            if (body.Count == 0) return body;
            var last = body[body.Count - 1];
            if (last is JsBreak) return body.Take(body.Count - 1).ToList();
            if (last is JsBlock b && body.Count == 1)
            {
                var inner = WithoutFinalBreak(b.Stmts);
                if (inner.Count != b.Stmts.Count) return new List<JsStmt> { new JsBlock { Stmts = inner, Span = b.Span } };
            }
            return body;
        }

        IrSwitch Switch(JsSwitch sw)
        {
            var r = new IrSwitch { Selector = Expr(sw.Disc), KwSpan = sw.KwSpan, Span = sw.Span, Style = IrSwitchStyle.Colon };
            PushScope();
            foreach (var c in sw.Cases) Predeclare(c.Body);
            IrCase cur = null;
            breakables.Add("switch");
            for (int k = 0; k < sw.Cases.Count; k++)
            {
                var c = sw.Cases[k];
                if (cur == null) cur = new IrCase { LabelSpan = c.LabelSpan, Span = c.Span };
                else { cur.LabelSpan = Span.Cover(cur.LabelSpan, c.LabelSpan); }
                if (c.Test == null) cur.IsDefault = true;
                else cur.Labels.Add(Expr(c.Test));
                if (c.Body.Count == 0 && k < sw.Cases.Count - 1) continue;   // empty case: labels stack
                bool last = k == sw.Cases.Count - 1;
                if (!last && !EndsCase(c.Body[c.Body.Count - 1]))
                    Error("falling through to the next case is not supported in JavaFarmer", sw.Cases[k + 1].LabelSpan);
                var body = WithoutFinalBreak(c.Body);
                var blk = new IrBlock { Span = c.Span };
                StmtList(body, blk.Stmts);
                cur.Body = blk;
                cur.Span = Span.Cover(cur.Span, c.Span);
                r.Cases.Add(cur);
                cur = null;
            }
            breakables.RemoveAt(breakables.Count - 1);
            PopScope();
            return r;
        }

        // ------------------------------------------------------------------ imports
        /// <summary>"./Utils.js" → "Utils"; null when the path is not another code window.</summary>
        static string ModuleOfPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var p = path;
            if (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            else return null;
            if (p.EndsWith(".js", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 3);
            else if (p.EndsWith(".mjs", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 4);
            else
            {
                // a window in another language may be named with its own extension: "./Utils.java", "./helper.py"
                int dot = p.LastIndexOf('.');
                if (dot > 0)
                {
                    var ext = p.Substring(dot);
                    foreach (var l in Languages.All)
                        if (l != null && string.Equals(l.FileExtension, ext, StringComparison.OrdinalIgnoreCase)) { p = p.Substring(0, dot); break; }
                }
            }
            if (p.Length == 0 || p.IndexOf('/') >= 0 || p.IndexOf('\\') >= 0 || p.IndexOf('.') >= 0) return null;
            return p;
        }

        static bool IsFarmPath(string path) => path == "./farm.js" || path == "./farm" || path == "farm" || path == "farm.js";

        JsModuleInfo InfoOf(string module, Span at, out bool known)
        {
            known = false;
            if (otherFiles == null || otherFiles.Count == 0) return null;
            if (otherFiles.TryGetValue(module, out var text))
            {
                known = true;
                // a window written in another language: read it with that language's frontend (docs/LANGUAGE-GUIDE.md §9)
                if (options != null && ForeignModules.IsForeign(options, module, "js"))
                {
                    var sig = ForeignModules.Resolve(module, options);
                    if (sig == null || !sig.Ok) { Error("cannot use " + module + ": " + (sig?.Error ?? "it could not be read"), at); return null; }
                    return JsModuleInfo.FromSignature(sig);
                }
                return JsModuleInfo.Get(text);
            }
            Error("Cannot find module './" + module + ".js' (there is no code window named " + module + ")", at);
            return null;
        }

        void DeclareImport(JsImport im)
        {
            if (IsFarmPath(im.Path))
            {
                if (im.Namespace != null) { var b = Declare(im.Namespace, im.NamespaceSpan, "farm"); b.Sym.IsConst = true; }
                if (im.Names != null)
                    foreach (var n in im.Names)
                    {
                        var api = JsNames.FromApiName(n.Imported);
                        bool constant = Array.IndexOf(GameApi.ConstantCategories, n.Imported) >= 0 || Array.IndexOf(GameApi.DirectionNames, n.Imported) >= 0 || n.Imported == "quickPrint";
                        if (api == null && !constant) Error("The requested module '" + im.Path + "' does not provide an export named '" + n.Imported + "'", n.ImportedSpan);
                        else if (n.Local != n.Imported) Error("renaming game functions or constants (as " + n.Local + ") is not supported in JavaFarmer", n.LocalSpan);
                    }
                return;
            }
            var module = ModuleOfPath(im.Path);
            if (module == null)
            {
                Error("Cannot find module '" + im.Path + "': only other code windows can be imported, like import * as Utils from \"./Utils.js\"", im.PathSpan);
                return;
            }
            if (module == fileName) { Error("a module cannot import itself", im.PathSpan); return; }
            var info = InfoOf(module, im.PathSpan, out var known);
            if (im.Namespace != null)
            {
                var b = Declare(im.Namespace, im.NamespaceSpan, "namespace");
                b.Module = module; b.Info = info; b.Sym.IsConst = true;
            }
            if (im.Names != null)
                foreach (var n in im.Names)
                {
                    bool isFn = false;
                    string pyName = null;
                    if (info != null)
                    {
                        if (!info.Exports.TryGetValue(n.Imported, out var d))
                        {
                            Error("The requested module '" + im.Path + "' does not provide an export named '" + n.Imported + "'", n.ImportedSpan);
                            continue;
                        }
                        isFn = d.IsFunction;
                        pyName = d.PyName;
                    }
                    var b = Declare(n.Local, n.LocalSpan, "import");
                    b.Module = module; b.Info = info; b.Imported = n.Imported;
                    b.Sym = External(module, n.Imported, isFn, pyName);
                }
        }

        void LowerImport(JsImport im, List<IrStmt> outList)
        {
            if (IsFarmPath(im.Path)) return;
            var module = ModuleOfPath(im.Path);
            if (module == null || module == fileName) return;
            var imp = new IrImport { Module = module, NameSpan = im.PathSpan, Span = im.Span };
            if (im.Names != null) imp.Names = im.Names.Select(n => n.Imported).ToList();
            outList.Add(imp);
        }

        /// <summary>The symbol of another module's function / variable (PyName as that module's emitter names it).</summary>
        IrSymbol External(string module, string name, bool function, string pyName = null)
        {
            var key = module + "|" + name + "|" + (function ? "f" : "g");
            if (externals.TryGetValue(key, out var s)) return s;
            s = new IrSymbol(name, function ? IrSymbolKind.ExternalFunction : IrSymbolKind.ExternalGlobal) { Module = module, PyName = pyName ?? PyNaming.Ident(name) };
            externals[key] = s;
            return s;
        }
    }

    /// <summary>Declarations of another code window (parsed once per source text): exported names and function arities.</summary>
    internal sealed class JsModuleInfo
    {
        public sealed class Decl { public bool IsFunction, IsConst; public int MinArgs, MaxArgs; public string PyName; }

        /// <summary>A window in another language (ForeignModules): every module-level name is visible (no export needed).</summary>
        public static JsModuleInfo FromSignature(ModuleSignature sig)
        {
            var info = new JsModuleInfo();
            if (sig == null) return info;
            foreach (var g in sig.Globals)
                if (g?.Name != null) info.All[g.Name] = info.Exports[g.Name] = new Decl { IsConst = g.IsConst, PyName = g.PyName };
            foreach (var f in sig.Functions)
                if (f?.Name != null) info.All[f.Name] = info.Exports[f.Name] = new Decl { IsFunction = true, MinArgs = f.MinArgs, MaxArgs = f.Params.Count, PyName = f.PyName };
            return info;
        }

        /// <summary>
        /// ISignatureProvider for JavaScript windows: top-level function declarations and variables (bodies not checked,
        /// imports not needed). Types are Unknown (JavaScript is dynamic); IsPrivate = not exported.
        /// </summary>
        public static ModuleSignature Signatures(string source, CompileOptions options)
        {
            var module = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            source = source ?? "";
            List<JsStmt> prog;
            try { prog = JsParser.Parse(source, null); }
            catch (JsSyntaxError e)
            {
                int line = 1;
                for (int i = 0; i < Math.Min(Math.Max(0, e.Span.Start), source.Length); i++) if (source[i] == '\n') line++;
                return ModuleSignature.Failed(module, "js", module + " (JavaScript) does not compile (line " + line + ": " + e.Message + ")");
            }
            var exported = new HashSet<string>();
            var fns = new List<JsFunctionDecl>();
            var globals = new List<(string Name, bool IsConst)>();
            foreach (var s in prog)
            {
                switch (s)
                {
                    case JsFunctionDecl f:
                        if (f.Name != null && !fns.Any(x => x.Name == f.Name)) fns.Add(f);
                        if (f.IsExport) exported.Add(f.Name);
                        break;
                    case JsVarDecl v:
                        foreach (var dc in v.Decls)
                            foreach (var n in PatternNames(dc.Target))
                            {
                                if (!globals.Any(g => g.Name == n)) globals.Add((n, v.Kind == "const"));
                                if (v.IsExport) exported.Add(n);
                            }
                        break;
                    case JsExportList ex: foreach (var n in ex.Names) exported.Add(n.Local); break;
                }
            }
            var fnPy = PyNaming.ModuleLevel(fns.Select(f => f.Name), globals.Select(g => g.Name), out var globalPy);
            var sig = new ModuleSignature { Module = module, LanguageId = "js", LanguageName = "JavaScript", Ok = true };
            foreach (var f in fns)
            {
                var fs = new FunctionSignature { Name = f.Name, PyName = fnPy[f.Name], IsPrivate = !exported.Contains(f.Name) };
                foreach (var p in f.Params)
                    fs.Params.Add(new ParamSignature { Name = p.Name, Optional = p.Default != null });
                sig.Functions.Add(fs);
            }
            foreach (var g in globals)
                sig.Globals.Add(new GlobalSignature { Name = g.Name, PyName = globalPy[g.Name], IsConst = g.IsConst, IsPrivate = !exported.Contains(g.Name) });
            return sig;
        }

        static IEnumerable<string> PatternNames(JsExpr target)
        {
            if (target is JsIdent id) { yield return id.Name; yield break; }
            if (target is JsArrayLit a) foreach (var item in a.Items) foreach (var n in PatternNames(item)) yield return n;
        }
        public readonly Dictionary<string, Decl> Exports = new Dictionary<string, Decl>();
        public readonly Dictionary<string, Decl> All = new Dictionary<string, Decl>();

        static readonly Dictionary<string, JsModuleInfo> cache = new Dictionary<string, JsModuleInfo>();
        static readonly object gate = new object();

        public static JsModuleInfo Get(string source)
        {
            source = source ?? "";
            lock (gate) { if (cache.TryGetValue(source, out var hit)) return hit; }
            var info = new JsModuleInfo();
            try
            {
                var prog = JsParser.Parse(source, null);
                var exported = new HashSet<string>();
                foreach (var s in prog)
                {
                    switch (s)
                    {
                        case JsFunctionDecl f:
                            {
                                var d = new Decl { IsFunction = true, MaxArgs = f.Params.Count, MinArgs = f.Params.Count(p => p.Default == null) };
                                for (int k = f.Params.Count - 1; k >= 0 && f.Params[k].Default != null; k--) { }
                                info.All[f.Name] = d;
                                if (f.IsExport) exported.Add(f.Name);
                                break;
                            }
                        case JsVarDecl v:
                            foreach (var dc in v.Decls)
                            {
                                var names = dc.Target is JsIdent id ? new[] { id.Name } : dc.Target is JsArrayLit a ? a.Items.OfType<JsIdent>().Select(x => x.Name).ToArray() : new string[0];
                                foreach (var n in names)
                                {
                                    info.All[n] = new Decl { IsConst = v.Kind == "const" };
                                    if (v.IsExport) exported.Add(n);
                                }
                            }
                            break;
                        case JsExportList ex: foreach (var n in ex.Names) exported.Add(n.Local); break;
                    }
                }
                foreach (var n in exported) if (info.All.TryGetValue(n, out var d)) info.Exports[n] = d;
            }
            catch (JsSyntaxError) { }
            catch (Exception) { }
            lock (gate)
            {
                if (cache.Count > 64) cache.Clear();
                cache[source] = info;
            }
            return info;
        }
    }
}
