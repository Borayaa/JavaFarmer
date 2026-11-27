using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>A "global x" line needed by a function, with the Java span of the assignment that caused it.</summary>
    internal sealed class GlobalUse { public string PyName; public int Start, End; }

    internal sealed class FunctionCtx
    {
        public FunctionCtx Parent;
        public MethodSymbol Method;
        public LambdaExpr Lambda;
        public bool IsFieldInit;
        public JType ReturnType;          // null: infer (lambda passed to spawnDrone)
        public List<JType> InferredReturns;
        public string Display;            // "method main()"
        public List<BreakTarget> Targets = new List<BreakTarget>();
        public List<GlobalUse> Globals = new List<GlobalUse>();
        public List<(LocalSymbol Sym, Node At)> Captures;   // root only
        public FunctionCtx Root => Parent == null ? this : Parent.Root;
    }

    internal sealed class BreakTarget { public Stmt Node; public bool IsSwitch; public bool HasBreak; }

    internal sealed class Scope
    {
        public readonly Scope Parent;
        public readonly Dictionary<string, LocalSymbol> Vars = new Dictionary<string, LocalSymbol>();
        public Scope(Scope parent) { Parent = parent; }
        public LocalSymbol Lookup(string name)
        {
            for (var s = this; s != null; s = s.Parent)
                if (s.Vars.TryGetValue(name, out var v)) return v;
            return null;
        }
    }

    /// <summary>Symbol resolution, type checking and simple flow analysis (definite assignment, reachability).</summary>
    internal sealed partial class Checker
    {
        readonly CompilationUnit cu;
        readonly string fileName;
        readonly IReadOnlyDictionary<string, string> otherSources;
        public readonly List<Diagnostic> Errors = new List<Diagnostic>();
        public FileSymbols Own;
        public MethodDecl Main;
        /// <summary>Other files used by this file → Java span of the first use.</summary>
        public readonly Dictionary<string, (int Start, int End)> ModuleUses = new Dictionary<string, (int, int)>();
        /// <summary>Global lines per function (key: MethodDecl or LambdaExpr).</summary>
        public readonly Dictionary<Node, List<GlobalUse>> Globals = new Dictionary<Node, List<GlobalUse>>();
        /// <summary>Module-level Python names (fields, methods, file names); locals with these names get a "_l" suffix.</summary>
        public readonly HashSet<string> FileLevelPyNames = new HashSet<string>();
        /// <summary>Python identifier -> Java name, for every declared name that the lowering renamed (see CompileResult.Names).</summary>
        public readonly Dictionary<string, string> RenamedNames = new Dictionary<string, string>();
        /// <summary>Declarations of the other windows referenced so far.</summary>
        public readonly Dictionary<string, FileSymbols> OtherFiles = new Dictionary<string, FileSymbols>();

        FunctionCtx fn;
        Scope scope;
        HashSet<LocalSymbol> da = new HashSet<LocalSymbol>();
        bool reachable = true;

        readonly CompileOptions options;

        public Checker(CompilationUnit cu, string fileName, IReadOnlyDictionary<string, string> others, CompileOptions options = null)
        {
            this.cu = cu; this.fileName = fileName; otherSources = others; this.options = options;
        }

        string ClassName => cu.IsClass ? cu.ClassName : fileName;

        void Error(string msg, int s, int e) => Errors.Add(new Diagnostic(msg, s, e));
        void Error(string msg, Node n) => Error(msg, n.Start, n.End);

        // ================================================================== entry
        public void Run()
        {
            DeclareOwn();
            CheckBodies();
        }

        // ================================================================== types
        static readonly Dictionary<string, string> UnsupportedPrimitive = new Dictionary<string, string>
        {
            { "long", "long" }, { "float", "float" }, { "char", "char" }, { "byte", "byte" }, { "short", "short" }
        };

        internal JType ResolveType(TypeRef t, bool allowVoid = false, bool silent = false)
        {
            var baseT = ResolveBase(t, allowVoid, silent);
            for (int i = 0; i < t.Dims; i++) baseT = JType.ArrayOf(baseT);
            t.Resolved = baseT;
            return baseT;
        }

        JType ResolveBase(TypeRef t, bool allowVoid, bool silent)
        {
            void Err(string m, int s, int e) { if (!silent) Error(m, s, e); }
            JType Arg(int i)
            {
                var a = ResolveType(t.Args[i], false, silent);
                if (a.IsPrimitive) { Err("unexpected type: required reference, found " + a + " (use " + a.BoxedVersion + ")", t.Args[i].Start, t.Args[i].End); return a.BoxedVersion; }
                return a;
            }
            bool NeedArgs(int n)
            {
                if (t.Args == null)
                {
                    if (!t.Diamond) Err("raw types are not supported in JavaFarmer: write " + t.Name + (n == 1 ? "<Integer>" : "<String, Integer>"), t.Start, t.End);
                    return false;
                }
                if (t.Args.Count != n) { Err("wrong number of type arguments; required " + n, t.Start, t.End); return false; }
                return true;
            }
            if (UnsupportedPrimitive.ContainsKey(t.Name))
            {
                Err(t.Name + " is not supported in JavaFarmer; use int or double", t.NameStart, t.NameEnd);
                return t.Name == "char" ? JType.String : t.Name == "float" ? JType.Double : JType.Int;
            }
            switch (t.Name)
            {
                case "int": return JType.Int;
                case "double": return JType.Double;
                case "boolean": return JType.Boolean;
                case "void":
                    if (!allowVoid) Err("'void' type not allowed here", t.Start, t.End);
                    return JType.Void;
                case "Integer": return JType.Integer;
                case "Double": return JType.DoubleBox;
                case "Boolean": return JType.BooleanBox;
                case "String": return JType.String;
                case "Object": return JType.Object;
                case "Runnable": return JType.Runnable;
                case "Supplier": return NeedArgs(1) ? JType.SupplierOf(Arg(0)) : JType.SupplierOf(JType.Object);
                case "List": case "ArrayList": return NeedArgs(1) ? JType.ListOf(Arg(0), t.Name) : JType.ListOf(JType.Object, t.Name);
                case "Set": case "HashSet": return NeedArgs(1) ? JType.SetOf(Arg(0), t.Name) : JType.SetOf(JType.Object, t.Name);
                case "Collection": return NeedArgs(1) ? JType.CollectionOf(Arg(0)) : JType.CollectionOf(JType.Object);
                case "Map": case "HashMap": return NeedArgs(2) ? JType.MapOf(Arg(0), Arg(1), t.Name) : JType.MapOf(JType.Object, JType.Object, t.Name);
                case "LinkedList": case "TreeMap": case "TreeSet": case "LinkedHashMap": case "LinkedHashSet": case "Deque": case "ArrayDeque": case "Queue": case "Stack": case "Vector":
                    Err(t.Name + " is not supported in JavaFarmer; use List/ArrayList, Set/HashSet or Map/HashMap", t.NameStart, t.NameEnd);
                    return JType.Error;
                case "var":
                    Err("'var' is not allowed here", t.Start, t.End);
                    return JType.Error;
            }
            if (JType.GameTypeNames.Contains(t.Name))
            {
                if (t.Args != null) Err("type " + t.Name + " does not take parameters", t.Start, t.End);
                return JType.GameType(t.Name);
            }
            if (t.Name == ClassName || (otherSources != null && otherSources.ContainsKey(t.Name)))
            {
                Err("objects of class " + t.Name + " are not supported in JavaFarmer (only static members)", t.NameStart, t.NameEnd);
                return JType.Error;
            }
            Err("cannot find symbol: class " + t.Name, t.NameStart, t.NameEnd);
            return JType.Error;
        }

        // ================================================================== declarations
        void DeclareOwn()
        {
            Own = new FileSymbols { FileName = fileName, IsClass = cu.IsClass };
            if (cu.IsClass && cu.ClassName != fileName)
                Error("class " + cu.ClassName + " should be declared in a file named " + cu.ClassName + " (this window is named " + fileName + ")", cu.ClassNameStart, cu.ClassNameEnd);

            foreach (var imp in cu.Imports) CheckImport(imp);

            foreach (var f in cu.Fields)
            {
                if (cu.IsClass && (f.Mods & Mods.Static) == 0)
                    Error("instance fields are not supported in JavaFarmer: declare it static", f.Vars[0].NameStart, f.Vars[0].NameEnd);
                var ft = f.Type.Name == "var" ? null : ResolveType(f.Type);
                if (f.Type.Name == "var") Error("'var' is not allowed here", f.Type.Start, f.Type.End);
                foreach (var v in f.Vars)
                {
                    CheckIdentifier(v.Name, v.NameStart, v.NameEnd);
                    var t = ft ?? JType.Error;
                    for (int i = 0; i < v.ExtraDims; i++) t = JType.ArrayOf(t);
                    if (Own.Fields.ContainsKey(v.Name)) { Error("variable " + v.Name + " is already defined in class " + ClassName, v.NameStart, v.NameEnd); continue; }
                    var fs = new FieldSymbol { Name = v.Name, Type = t, IsFinal = (f.Mods & Mods.Final) != 0, IsPrivate = (f.Mods & Mods.Private) != 0, IsStatic = true, FileName = fileName, Declarator = v };
                    v.Field = fs;
                    Own.Fields[v.Name] = fs;
                    if (fs.IsFinal && v.Init == null) Error("variable " + v.Name + " not initialized in the default constructor", v.NameStart, v.NameEnd);
                }
            }
            foreach (var m in cu.Methods)
            {
                CheckIdentifier(m.Name, m.NameStart, m.NameEnd);
                if (cu.IsClass && (m.Mods & Mods.Static) == 0)
                    Error("instance methods are not supported in JavaFarmer: declare it static", m.NameStart, m.NameEnd);
                var ms = new MethodSymbol { Name = m.Name, ReturnType = ResolveType(m.ReturnType, allowVoid: true), IsPrivate = (m.Mods & Mods.Private) != 0, IsStatic = true, FileName = fileName, Decl = m };
                foreach (var p in m.Params)
                {
                    ms.ParamTypes.Add(ResolveType(p.Type));
                    ms.ParamNames.Add(p.Name);
                }
                m.Symbol = ms;
                if (Own.Methods.TryGetValue(m.Name, out var prev))
                {
                    if (prev.Display == ms.Display) Error("method " + ms.Display + " is already defined in class " + ClassName, m.NameStart, m.NameEnd);
                    else Error("method overloading is not supported in JavaFarmer: there is already a method named " + m.Name, m.NameStart, m.NameEnd);
                    continue;
                }
                Own.Methods[m.Name] = ms;
                if (m.Name == "main")
                {
                    bool okParams = m.Params.Count == 0 || (m.Params.Count == 1 && ms.ParamTypes[0].Kind == TK.Array && ms.ParamTypes[0].Elem.Kind == TK.String);
                    if (!okParams || ms.ReturnType.Kind != TK.Void)
                        Error(cu.IsClass ? "main must be declared as public static void main(String[] args)" : "main must be declared as void main()", m.HeaderStart, m.HeaderEnd);
                    else { Main = m; m.IsMain = true; Own.HasMain = true; }
                }
            }

            // Python names (fields that clash with a method name get a suffix)
            foreach (var ms in Own.Methods.Values) ms.PyName = PyNames.Ident(ms.Name);
            foreach (var fs in Own.Fields.Values)
                fs.PyName = PyNames.Ident(fs.Name) + (Own.Methods.ContainsKey(fs.Name) ? "_f" : "");
            foreach (var ms in Own.Methods.Values) { FileLevelPyNames.Add(ms.PyName); NoteRename(ms.PyName, ms.Name); }
            foreach (var fs in Own.Fields.Values) { FileLevelPyNames.Add(fs.PyName); NoteRename(fs.PyName, fs.Name); }
            FileLevelPyNames.Add(fileName);
            FileLevelPyNames.Add("main");
            if (otherSources != null) foreach (var k in otherSources.Keys) FileLevelPyNames.Add(k);
        }

        void NoteRename(string py, string java)
        {
            if (py != null && java != null && py != java) RenamedNames[py] = java;
        }

        void CheckImport(ImportDecl imp)
        {
            if (imp.IsStatic) { Error("static imports are not supported in JavaFarmer", imp); return; }
            var n = imp.Name;
            if (n == "java.util.*" || n == "java.util.function.*" || n == "java.lang.*") return;
            string[] ok = { "List", "ArrayList", "Map", "HashMap", "Set", "HashSet", "Collection" };
            if (n.StartsWith("java.util.") && ok.Contains(n.Substring("java.util.".Length))) return;
            if (n == "java.util.function.Supplier") return;
            if (n.StartsWith("java.lang.")) return;
            Error("import " + n + " is not supported in JavaFarmer (only java.util collections are available)", imp);
        }

        void CheckIdentifier(string name, int s, int e)
        {
            if (name.StartsWith("__j")) Error("identifiers starting with __j are reserved by JavaFarmer", s, e);
            else if (name.IndexOf('$') >= 0) Error("'$' in names is not supported in JavaFarmer", s, e);
        }

        /// <summary>Declarations of another window (parsed and cached; errors there are ignored here).</summary>
        internal FileSymbols GetOther(string name)
        {
            if (name == fileName || otherSources == null) return null;
            if (OtherFiles.TryGetValue(name, out var cached)) return cached;
            if (!otherSources.TryGetValue(name, out var source) || source == null) return null;
            // a file written in another language (its extension): read its declarations with that language's frontend
            var fs = ForeignModules.IsForeign(options, name, "java") ? ForeignSymbols.From(ForeignModules.Resolve(name, options), name) : DeclCache.Get(name, source);
            OtherFiles[name] = fs;
            return fs;
        }

        void NoteModuleUse(string module, int s, int e)
        {
            if (!ModuleUses.TryGetValue(module, out var prev) || prev.Start > s) ModuleUses[module] = (s, e);
        }

        // ================================================================== bodies
        void CheckBodies()
        {
            foreach (var f in cu.Fields)
            {
                foreach (var v in f.Vars)
                {
                    if (v.Init == null || v.Field == null) continue;
                    fn = new FunctionCtx { IsFieldInit = true, Display = "class " + ClassName, Captures = new List<(LocalSymbol, Node)>() };
                    scope = new Scope(null);
                    da = new HashSet<LocalSymbol>();
                    reachable = true;
                    CheckInitializer(v.Init, v.Field.Type);
                }
            }
            foreach (var m in cu.Methods)
            {
                if (m.Symbol == null) continue;
                CheckMethod(m);
            }
        }

        void CheckMethod(MethodDecl m)
        {
            var ms = m.Symbol;
            fn = new FunctionCtx { Method = ms, ReturnType = ms.ReturnType, Display = "method " + ms.Display, Captures = new List<(LocalSymbol, Node)>() };
            scope = new Scope(null);
            da = new HashSet<LocalSymbol>();
            reachable = true;
            for (int i = 0; i < m.Params.Count; i++)
            {
                var p = m.Params[i];
                CheckIdentifier(p.Name, p.NameStart, p.NameEnd);
                if (scope.Vars.ContainsKey(p.Name)) { Error("variable " + p.Name + " is already defined in " + fn.Display, p.NameStart, p.NameEnd); continue; }
                var sym = DeclareLocal(p.Name, ms.ParamTypes[i], p.IsFinal, true, p.NameStart, p.NameEnd);
                sym.IsParam = true;
                p.Symbol = sym;
                da.Add(sym);
            }
            foreach (var s in m.Body.Stmts) CheckStmtInBlock(s);
            if (reachable && ms.ReturnType.Kind != TK.Void)
                Error("missing return statement", m.Body.End - 1, m.Body.End);
            FinishFunction(m);
        }

        void FinishFunction(Node key)
        {
            Globals[key] = fn.Globals;
            var seen = new HashSet<LocalSymbol>();
            foreach (var (sym, at) in fn.Captures)
            {
                if (!sym.EffectivelyFinal && seen.Add(sym))
                    Error("local variables referenced from a lambda expression must be final or effectively final", at);
            }
        }

        LocalSymbol DeclareLocal(string name, JType type, bool isFinal, bool hasInit, int s, int e)
        {
            var py = PyNames.Ident(name);
            if (FileLevelPyNames.Contains(py)) py += "_l";
            NoteRename(py, name);
            var sym = new LocalSymbol { Name = name, PyName = py, Type = type, IsFinal = isFinal, HasInit = hasInit, DeclStart = s, DeclEnd = e, Owner = fn };
            scope.Vars[name] = sym;
            return sym;
        }

        bool CheckRedeclare(string name, int s, int e)
        {
            var existing = scope.Lookup(name);
            if (existing != null)
            {
                Error("variable " + name + " is already defined in " + ((FunctionCtx)existing.Owner).Display, s, e);
                return true;
            }
            return false;
        }

        // ================================================================== statements
        void CheckStmtInBlock(Stmt s)
        {
            if (!reachable)
            {
                Error("unreachable statement", s);
                reachable = true;
            }
            CheckStmt(s);
        }

        void PushScope() => scope = new Scope(scope);
        void PopScope() => scope = scope.Parent;

        static bool IsTrueLiteral(Expr e)
        {
            while (e is ParenExpr p) e = p.Inner;
            return e is LiteralExpr l && l.Kind == LitKind.Bool && l.BoolValue;
        }

        static HashSet<LocalSymbol> Meet(HashSet<LocalSymbol> a, bool aReach, HashSet<LocalSymbol> b, bool bReach)
        {
            if (!aReach && !bReach) return new HashSet<LocalSymbol>(a.Intersect(b));
            if (!aReach) return b;
            if (!bReach) return a;
            return new HashSet<LocalSymbol>(a.Intersect(b));
        }

        void CheckStmt(Stmt s)
        {
            switch (s)
            {
                case BlockStmt b:
                    PushScope();
                    foreach (var st in b.Stmts) CheckStmtInBlock(st);
                    PopScope();
                    break;

                case EmptyStmt _:
                    break;

                case LocalVarStmt lv:
                    CheckLocalVar(lv);
                    break;

                case ExprStmt es:
                    CheckExprStatement(es.E);
                    break;

                case IfStmt ifs:
                    {
                        CheckCond(ifs.Cond);
                        var da0 = new HashSet<LocalSymbol>(da);
                        PushScope(); CheckStmt(ifs.Then); PopScope();
                        var daThen = da; bool rThen = reachable;
                        da = new HashSet<LocalSymbol>(da0); reachable = true;
                        if (ifs.Else != null) { PushScope(); CheckStmt(ifs.Else); PopScope(); }
                        var daElse = da; bool rElse = reachable;
                        da = Meet(daThen, rThen, daElse, rElse);
                        reachable = rThen || rElse;
                        break;
                    }

                case WhileStmt w:
                    {
                        CheckCond(w.Cond);
                        var da0 = new HashSet<LocalSymbol>(da);
                        var target = new BreakTarget { Node = w };
                        fn.Targets.Add(target);
                        PushScope(); CheckStmt(w.Body); PopScope();
                        fn.Targets.RemoveAt(fn.Targets.Count - 1);
                        da = da0;
                        reachable = !IsTrueLiteral(w.Cond) || target.HasBreak;
                        break;
                    }

                case DoWhileStmt dw:
                    {
                        var target = new BreakTarget { Node = dw };
                        fn.Targets.Add(target);
                        PushScope(); CheckStmt(dw.Body); PopScope();
                        fn.Targets.RemoveAt(fn.Targets.Count - 1);
                        reachable = true;
                        CheckCond(dw.Cond);
                        reachable = !IsTrueLiteral(dw.Cond) || target.HasBreak;
                        break;
                    }

                case ForStmt f:
                    {
                        PushScope();
                        foreach (var init in f.Init)
                        {
                            if (init is LocalVarStmt lv) CheckLocalVar(lv);
                            else if (init is ExprStmt es) CheckExprStatement(es.E);
                        }
                        if (f.Cond != null) CheckCond(f.Cond);
                        var da0 = new HashSet<LocalSymbol>(da);
                        var target = new BreakTarget { Node = f };
                        fn.Targets.Add(target);
                        PushScope(); CheckStmt(f.Body); PopScope();
                        reachable = true;
                        foreach (var u in f.Update) CheckExprStatement(u);
                        fn.Targets.RemoveAt(fn.Targets.Count - 1);
                        PopScope();
                        da = da0;
                        reachable = (f.Cond != null && !IsTrueLiteral(f.Cond)) || target.HasBreak;
                        break;
                    }

                case ForEachStmt fe:
                    {
                        var it = CheckExpr(fe.Iterable);
                        JType elem = JType.Error;
                        if (it.Kind == TK.Array || it.Kind == TK.List || it.Kind == TK.Set || it.Kind == TK.Collection) elem = it.Elem;
                        else if (it.Kind == TK.Map) Error("for-each not applicable to expression type " + it + " (iterate over map.keySet() or map.values())", fe.Iterable);
                        else if (it.Kind == TK.String) Error("for-each not applicable to expression type String", fe.Iterable);
                        else if (!it.IsError) Error("for-each not applicable to expression type " + it, fe.Iterable);
                        PushScope();
                        CheckIdentifier(fe.Name, fe.NameStart, fe.NameEnd);
                        JType vt;
                        if (fe.VarType.Name == "var") vt = elem;
                        else
                        {
                            vt = ResolveType(fe.VarType);
                            if (!elem.IsError && !JType.Assignable(elem, vt))
                                Error(IncompatibleMsg(elem, vt), fe.VarType);
                        }
                        CheckRedeclare(fe.Name, fe.NameStart, fe.NameEnd);
                        var sym = DeclareLocal(fe.Name, vt, fe.IsFinal, true, fe.NameStart, fe.NameEnd);
                        fe.Local = sym;
                        da.Add(sym);
                        var da0 = new HashSet<LocalSymbol>(da);
                        var target = new BreakTarget { Node = fe };
                        fn.Targets.Add(target);
                        PushScope(); CheckStmt(fe.Body); PopScope();
                        fn.Targets.RemoveAt(fn.Targets.Count - 1);
                        PopScope();
                        da = da0;
                        da.Remove(sym);
                        reachable = true;
                        break;
                    }

                case BreakStmt br:
                    {
                        if (fn.Targets.Count == 0) { Error("break outside switch or loop", br); reachable = false; break; }
                        var t = fn.Targets[fn.Targets.Count - 1];
                        t.HasBreak = true;
                        if (t.IsSwitch)
                        {
                            br.TargetsSwitch = true;
                            if (!br.Strip) Error("break inside a switch case is only supported as the last statement of the case in JavaFarmer", br);
                        }
                        reachable = false;
                        break;
                    }

                case ContinueStmt co:
                    if (!fn.Targets.Any(t => !t.IsSwitch)) Error("continue outside of loop", co);
                    reachable = false;
                    break;

                case ReturnStmt r:
                    CheckReturn(r);
                    reachable = false;
                    break;

                case SwitchStmt sw:
                    CheckSwitch(sw);
                    break;

                default:
                    Error("unsupported statement", s);
                    break;
            }
        }

        void CheckLocalVar(LocalVarStmt lv)
        {
            bool isVar = lv.Type.Name == "var";
            JType declared = isVar ? null : ResolveType(lv.Type);
            foreach (var v in lv.Vars)
            {
                CheckIdentifier(v.Name, v.NameStart, v.NameEnd);
                JType t = declared;
                if (t != null) for (int i = 0; i < v.ExtraDims; i++) t = JType.ArrayOf(t);
                if (isVar)
                {
                    if (lv.Vars.Count > 1) Error("'var' is not allowed in a compound declaration", v.NameStart, v.NameEnd);
                    if (v.Init == null) { Error("cannot infer type for local variable " + v.Name + " (cannot use 'var' on variable without initializer)", v.NameStart, v.NameEnd); t = JType.Error; }
                    else if (v.Init is LambdaExpr) { Error("cannot infer type for local variable " + v.Name + " (lambda expression needs an explicit target-type)", v.NameStart, v.NameEnd); t = JType.Error; }
                    else if (v.Init is ArrayInitExpr) { Error("cannot infer type for local variable " + v.Name + " (array initializer needs an explicit target-type)", v.NameStart, v.NameEnd); t = JType.Error; }
                }
                bool dup = CheckRedeclare(v.Name, v.NameStart, v.NameEnd);
                if (v.Init != null)
                {
                    var it = CheckInitializer(v.Init, t);
                    if (isVar && t == null)
                    {
                        t = it;
                        if (it.Kind == TK.Null) { Error("cannot infer type for local variable " + v.Name + " (variable initializer is 'null')", v.NameStart, v.NameEnd); t = JType.Error; }
                        else if (it.Kind == TK.Void) t = JType.Error;
                    }
                }
                var sym = DeclareLocal(v.Name, t ?? JType.Error, lv.IsFinal, v.Init != null, v.NameStart, v.NameEnd);
                v.Local = sym;
                if (v.Init != null) da.Add(sym);
            }
        }

        /// <summary>Checks an initializer / assigned value against a target type (null target = infer).</summary>
        JType CheckInitializer(Expr init, JType target)
        {
            if (init is ArrayInitExpr ai)
            {
                if (target == null) return JType.Error;
                CheckArrayInit(ai, target);
                return target;
            }
            var t = CheckExpr(init, target);
            if (target != null) RequireAssignable(t, target, init);
            return t;
        }

        void RequireAssignable(JType from, JType to, Node at)
        {
            if (from.Kind == TK.Void) { Error("'void' type not allowed here", at); return; }
            if (!JType.Assignable(from, to)) Error(IncompatibleMsg(from, to), at);
        }

        static string IncompatibleMsg(JType from, JType to)
        {
            if (from.IsNumeric && to.IsNumeric && from.Kind == TK.Double && to.Kind == TK.Int && to.IsPrimitive)
                return "incompatible types: possible lossy conversion from " + from.Unboxed + " to " + to;
            return "incompatible types: " + from + " cannot be converted to " + to;
        }

        void CheckCond(Expr e)
        {
            var t = CheckExpr(e, JType.Boolean);
            if (!t.IsError && t.Kind != TK.Boolean)
            {
                if (t.Kind == TK.Void) Error("'void' type not allowed here", e);
                else Error("incompatible types: " + t + " cannot be converted to boolean", e);
            }
        }

        void CheckReturn(ReturnStmt r)
        {
            if (fn.IsFieldInit) { Error("return outside method", r); return; }
            if (fn.Lambda != null && fn.ReturnType == null)
            {
                // inferring (lambda passed to spawnDrone): collect
                if (r.Value != null)
                {
                    var t = CheckExpr(r.Value);
                    if (t.Kind == TK.Void) Error("'void' type not allowed here", r.Value);
                    fn.InferredReturns.Add(t);
                }
                return;
            }
            var rt = fn.ReturnType;
            if (rt.Kind == TK.Void)
            {
                if (r.Value != null)
                {
                    CheckExpr(r.Value);
                    Error(fn.Lambda != null ? "bad return type in lambda expression: unexpected return value" : "incompatible types: unexpected return value", r.Value);
                }
                return;
            }
            if (r.Value == null) { Error("incompatible types: missing return value", r); return; }
            var vt = CheckExpr(r.Value, rt);
            RequireAssignable(vt, rt, r.Value);
        }

        // ------------------------------------------------------------------ switch
        void CheckSwitch(SwitchStmt sw)
        {
            var st = CheckExpr(sw.Selector);
            var sel = st.Unboxed;
            bool selOk = st.IsError || sel.Kind == TK.Int || sel.Kind == TK.String || sel.Kind == TK.Game;
            if (!selOk) Error("switch on " + st + " is not supported in JavaFarmer (use int, String or a game type)", sw.Selector);

            // mark tail breaks before checking bodies
            foreach (var c in sw.Cases) MarkTailBreak(c.Body);

            var labels = new HashSet<string>();
            bool hasDefault = false;
            var da0 = new HashSet<LocalSymbol>(da);
            var target = new BreakTarget { Node = sw, IsSwitch = true };
            fn.Targets.Add(target);
            HashSet<LocalSymbol> daOut = null;
            bool lastCompletes = true;
            PushScope();   // colon-form cases share one block scope
            for (int i = 0; i < sw.Cases.Count; i++)
            {
                var c = sw.Cases[i];
                if (c.IsDefault)
                {
                    if (hasDefault) Error("duplicate default label", c.Start, c.LabelEnd);
                    hasDefault = true;
                }
                foreach (var l in c.Labels)
                {
                    var lt = CheckExpr(l, st);
                    if (!IsConstantLabel(l)) Error("constant expression required", l);
                    else if (!lt.IsError && !st.IsError && !(JType.Assignable(lt, st) || (lt.IsNumeric && sel.IsNumeric)))
                        Error("constant label of type " + lt + " is not compatible with switch selector type " + st, l);
                    var key = LabelKey(l);
                    if (key != null && !labels.Add(key)) Error("duplicate case label", l);
                }
                da = new HashSet<LocalSymbol>(da0);
                reachable = true;
                if (sw.Arrow) PushScope();
                foreach (var s in c.Body) CheckStmtInBlock(s);
                if (sw.Arrow) PopScope();
                bool endsWithBreak = c.Body.Count > 0 && LastIsStrippedBreak(c.Body);
                bool completes = reachable;
                if (!sw.Arrow && completes && c.Body.Count > 0 && i < sw.Cases.Count - 1)
                    Error("falling through to the next case is not supported in JavaFarmer; end the case with break", sw.Cases[i + 1].Start, sw.Cases[i + 1].LabelEnd);
                if (completes || endsWithBreak)
                    daOut = daOut == null ? new HashSet<LocalSymbol>(da) : new HashSet<LocalSymbol>(daOut.Intersect(da));
                if (i == sw.Cases.Count - 1) lastCompletes = completes;
            }
            PopScope();
            fn.Targets.RemoveAt(fn.Targets.Count - 1);
            if (sw.Arrow) lastCompletes = sw.Cases.Any(c => CaseCompletes(c));
            reachable = !hasDefault || target.HasBreak || lastCompletes || sw.Cases.Count == 0;
            da = hasDefault && daOut != null ? daOut : da0;
        }

        bool CaseCompletes(SwitchCase c)
        {
            // arrow cases: approximated by "the body does not end in return/continue"
            if (c.Body.Count == 0) return true;
            var last = c.Body[c.Body.Count - 1];
            while (last is BlockStmt b && b.Stmts.Count > 0) last = b.Stmts[b.Stmts.Count - 1];
            return !(last is ReturnStmt || last is ContinueStmt);
        }

        static void MarkTailBreak(List<Stmt> body)
        {
            if (body.Count == 0) return;
            var last = body[body.Count - 1];
            if (last is BreakStmt br) br.Strip = true;
            else if (last is BlockStmt b) MarkTailBreak(b.Stmts);
        }

        static bool LastIsStrippedBreak(List<Stmt> body)
        {
            if (body.Count == 0) return false;
            var last = body[body.Count - 1];
            if (last is BreakStmt br) return br.Strip;
            if (last is BlockStmt b) return LastIsStrippedBreak(b.Stmts);
            return false;
        }

        bool IsConstantLabel(Expr e)
        {
            while (e is ParenExpr p) e = p.Inner;
            switch (e)
            {
                case LiteralExpr l: return l.Kind != LitKind.Null;
                case UnaryExpr u: return (u.Op == "-" || u.Op == "+") && IsConstantLabel(u.Operand);
                case FieldAccessExpr fa: return fa.IsConstMember || fa.ConstText != null || (fa.Field != null && fa.Field.IsFinal);
                case NameExpr n: return n.Kind == NameKind.Direction || (n.Kind == NameKind.Field && n.Field.IsFinal) || (n.Kind == NameKind.Local && n.Local.IsFinal);
            }
            return false;
        }

        static string LabelKey(Expr e)
        {
            while (e is ParenExpr p) e = p.Inner;
            switch (e)
            {
                case LiteralExpr l:
                    switch (l.Kind)
                    {
                        case LitKind.Int: return "n" + l.IntValue;
                        case LitKind.Double: return "n" + l.DoubleValue;
                        case LitKind.String: return "s" + l.StrValue;
                        case LitKind.Bool: return "b" + l.BoolValue;
                    }
                    return null;
                case UnaryExpr u when u.Op == "-" && u.Operand is LiteralExpr ul && ul.Kind == LitKind.Int: return "n-" + ul.IntValue;
                case FieldAccessExpr fa when fa.IsConstMember && fa.Target is NameExpr tn: return "c" + tn.Name + "." + fa.Name;
                case NameExpr n: return "v" + n.Name;
            }
            return null;
        }
    }
}
