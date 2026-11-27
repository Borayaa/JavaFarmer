using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>Declarations another code window (module) exposes: functions and consts/statics with their types.</summary>
    internal sealed class RModuleDecls
    {
        static int LineOf(string src, int offset)
        {
            int line = 1;
            for (int i = 0; i < Math.Min(offset, (src ?? "").Length); i++) if (src[i] == '\n') line++;
            return line;
        }

        public string Name;
        public readonly Dictionary<string, RFnSym> Fns = new Dictionary<string, RFnSym>();
        public readonly Dictionary<string, RVar> Vars = new Dictionary<string, RVar>();
        public bool Parsed;
        /// <summary>The file could not be read (a foreign file that does not compile, a Rust file with a syntax error): shown at the use site.</summary>
        public string Error;
        public Diagnostic ParseError;
        /// <summary>Written in another language (read through ForeignModules): no privacy, untyped values are {error} (accept anything).</summary>
        public bool Foreign;

        /// <summary>Declarations of a file in another language (docs/LANGUAGE-GUIDE.md §9).</summary>
        public static RModuleDecls FromSignature(string name, ModuleSignature sig)
        {
            var d = new RModuleDecls { Name = name, Foreign = true, Parsed = sig != null && sig.Ok };
            if (sig == null || !sig.Ok) { d.Error = sig?.Error ?? ("could not read " + name); return d; }
            foreach (var f in sig.Functions)
            {
                if (f.Name == null || d.Fns.ContainsKey(f.Name)) continue;
                var s = new RFnSym { Name = f.Name, Pub = true, Module = name, PyName = f.PyName ?? f.Name, Ret = FromIr(f.ReturnType, false, true) };
                foreach (var p in f.Params) { s.Params.Add(FromIr(p.Type, true, false)); s.ParamNames.Add(p.Name ?? "_"); }
                d.Fns[f.Name] = s;
            }
            foreach (var g in sig.Globals)
            {
                if (g.Name == null || d.Vars.ContainsKey(g.Name) || d.Fns.ContainsKey(g.Name)) continue;
                var v = new RVar { Name = g.Name, Kind = RVarKind.External, Mut = false, Type = FromIr(g.Type, false, false), Module = name, Pub = true };
                v.Ir = new IrSymbol(g.Name, IrSymbolKind.ExternalGlobal) { Module = name, PyName = g.PyName ?? g.Name, IsConst = g.IsConst, Type = g.Type ?? IrType.Unknown };
                d.Vars[g.Name] = v;
            }
            return d;
        }

        /// <summary>A neutral IR type as a Rust type: numbers, bool, the game enums and Option of them are checked; strings, collections and untyped values accept anything.</summary>
        static RTy FromIr(IrType t, bool param, bool ret)
        {
            if (t == null || !t.IsKnown) return ret && t != null && t.Kind == IrTypeKind.Void ? RTy.Unit : RTy.Error;
            if (t.Kind == IrTypeKind.Void) return RTy.Unit;
            if (t.Nullable && t.Kind != IrTypeKind.Null)
            {
                var inner = FromIr(t.NonNull(), param, false);
                return inner.Kind == RK.Error ? RTy.Error : RTy.Option(inner);
            }
            switch (t.Kind)
            {
                case IrTypeKind.Int: return RTy.Int("i32");
                case IrTypeKind.Float: return RTy.F64;
                case IrTypeKind.Bool: return RTy.Bool;
                case IrTypeKind.String: return param ? RTy.Error : RTy.String;
                case IrTypeKind.Game:
                    if (Array.IndexOf(RustApi.Enums, t.GameName) >= 0) return RTy.Game(t.GameName);
                    return RTy.Error;
            }
            return RTy.Error;
        }

        static readonly Dictionary<string, RModuleDecls> cache = new Dictionary<string, RModuleDecls>();
        static readonly object gate = new object();

        /// <summary>Declarations of another file (parsed for its signatures only; cached by name + source text).</summary>
        public static RModuleDecls Get(string name, string source)
        {
            var key = name + "\n" + GameApi.Version + "\n" + (source ?? "");
            lock (gate) { if (cache.TryGetValue(key, out var hit)) return hit; }
            var d = new RModuleDecls { Name = name };
            try
            {
                var f = RustParser.Parse(source ?? "");
                d.Parsed = true;
                var fnNames = new List<string>();
                var varNames = new List<string>();
                foreach (var it in f.Items)
                {
                    if (it is RFn fn) fnNames.Add(fn.Name);
                    else if (it is RConst c) varNames.Add(c.Name);
                }
                var pyFns = PyNaming.ModuleLevel(fnNames.Distinct(), varNames.Distinct(), out var pyVars);
                foreach (var it in f.Items)
                {
                    if (it is RFn fn && !d.Fns.ContainsKey(fn.Name))
                    {
                        var s = new RFnSym { Name = fn.Name, Pub = fn.Pub, Module = name, PyName = pyFns[fn.Name] };
                        foreach (var p in fn.Params)
                        {
                            s.Params.Add(RustChecker.TypeOfRef(p.Type, true));
                            s.ParamNames.Add(p.Pat?.Name ?? "_");
                        }
                        s.Ret = fn.Ret == null ? RTy.Unit : RustChecker.TypeOfRef(fn.Ret, false);
                        d.Fns[fn.Name] = s;
                    }
                    else if (it is RConst c && !d.Vars.ContainsKey(c.Name))
                    {
                        var v = new RVar
                        {
                            Name = c.Name, Kind = RVarKind.External, Mut = c.Mut, Type = RustChecker.TypeOfRef(c.Type, false), Module = name, Pub = c.Pub,
                        };
                        v.Ir = new IrSymbol(c.Name, IrSymbolKind.ExternalGlobal) { Module = name, PyName = pyVars[c.Name], IsConst = !c.Mut, Type = RTy.ToIr(v.Type) };
                        d.Vars[c.Name] = v;
                    }
                }
            }
            catch (RustAbort a) { d.Parsed = false; d.ParseError = a.Diag; }
            catch (Exception) { d.Parsed = false; }
            if (!d.Parsed) d.Error = name + " (Rust) does not compile" + (d.ParseError != null ? " (line " + LineOf(source, d.ParseError.Start) + ": " + d.ParseError.Message + ")" : "");
            lock (gate)
            {
                if (cache.Count > 64) cache.Clear();
                cache[key] = d;
            }
            return d;
        }
    }

    /// <summary>
    /// Name resolution and type checking for the JavaFarmer Rust subset (rustc wording). Annotates the AST with types and
    /// resolutions for RustLowering. Stops at the first error (RustAbort).
    /// </summary>
    internal sealed partial class RustChecker
    {
        readonly RFile file;
        public readonly string FileName;
        readonly IReadOnlyDictionary<string, string> others;
        /// <summary>The compile options (OtherFileLanguages: files of other languages are read through ForeignModules).</summary>
        public CompileOptions Options;

        public readonly Dictionary<string, RFnSym> Fns = new Dictionary<string, RFnSym>();
        public readonly Dictionary<string, RVar> Globals = new Dictionary<string, RVar>();
        public RFn Main;

        // imports
        bool farmGlob;
        readonly HashSet<string> farmNames = new HashSet<string>();
        bool hashMapImported, hashSetImported, stdCmpMin, stdCmpMax;
        public readonly Dictionary<string, string> ModuleAliases = new Dictionary<string, string>();   // name in scope → module
        readonly Dictionary<string, (string Module, string Name)> importedItems = new Dictionary<string, (string, string)>();
        readonly Dictionary<string, RModuleDecls> moduleDecls = new Dictionary<string, RModuleDecls>();

        // per function state
        readonly List<Dictionary<string, RVar>> scopes = new List<Dictionary<string, RVar>>();
        sealed class FnCtx
        {
            public RTy Ret; public int LoopDepth; public int Id; public int BaseScope; public RClosure Closure;
        }
        readonly List<FnCtx> fnStack = new List<FnCtx>();
        FnCtx Fn => fnStack[fnStack.Count - 1];
        int fnIds;
        int unsafeDepth;
        bool inConstInit;
        bool diverged;

        sealed class Flow
        {
            public HashSet<RVar> Init = new HashSet<RVar>();
            public HashSet<RVar> Maybe = new HashSet<RVar>();
            public Flow Clone() => new Flow { Init = new HashSet<RVar>(Init), Maybe = new HashSet<RVar>(Maybe) };
        }
        Flow flow = new Flow();

        readonly List<RTy> allVars = new List<RTy>();
        readonly List<RLit> intLits = new List<RLit>();
        readonly List<(RTy T, RExpr At, string Trait)> pendingTraits = new List<(RTy, RExpr, string)>();

        public RustChecker(RFile file, string fileName, IReadOnlyDictionary<string, string> others)
        {
            this.file = file;
            FileName = string.IsNullOrEmpty(fileName) ? "main" : fileName;
            this.others = others;
        }

        // ================================================================== errors
        static RustAbort Err(string msg, int s, int e) => new RustAbort(msg, s, Math.Max(e, s));
        static RustAbort Err(string msg, RNode n) => new RustAbort(msg, n.Start, n.End);
        static RustAbort NotSupported(string what, RNode n) => new RustAbort(what + " is not supported in JavaFarmer", n.Start, n.End);
        static RustAbort NotSupported(string what, int s, int e) => new RustAbort(what + " is not supported in JavaFarmer", s, e);

        static string Q(RTy t) => "`" + RTy.Show(t) + "`";
        static RustAbort Mismatch(RTy expected, RTy found, RNode at, string note = null) =>
            Err("mismatched types: expected " + Q(expected) + ", found " + Q(found) + (note ?? "") + " (E0308)", at);

        RTy NewVar(int cls = 0, RNode origin = null, string what = null)
        {
            var v = RTy.NewVar(cls, origin?.Start ?? -1, origin?.End ?? -1, what);
            allVars.Add(v);
            return v;
        }

        // ================================================================== unification / coercion
        public static bool Unify(RTy a, RTy b)
        {
            a = RTy.Prune(a); b = RTy.Prune(b);
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Kind == RK.Error || b.Kind == RK.Error) return true;
            if (a.Kind == RK.Var) return Bind(a, b);
            if (b.Kind == RK.Var) return Bind(b, a);
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case RK.Int: case RK.Float: case RK.Game: return a.Name == b.Name;
                case RK.Ref: return a.Mut == b.Mut && Unify(a.A, b.A);
                case RK.Vec: case RK.Slice: case RK.Set: case RK.Option: case RK.Drone: case RK.Range: case RK.Iter: return Unify(a.A, b.A);
                case RK.Array: return Unify(a.A, b.A) && (a.Len < 0 || b.Len < 0 || a.Len == b.Len);
                case RK.Map: return Unify(a.A, b.A) && Unify(a.B, b.B);
                case RK.Tuple:
                    if (a.Items.Count != b.Items.Count) return false;
                    for (int i = 0; i < a.Items.Count; i++) if (!Unify(a.Items[i], b.Items[i])) return false;
                    return true;
                case RK.Fn:
                    if ((a.Items?.Count ?? 0) != (b.Items?.Count ?? 0)) return false;
                    for (int i = 0; i < (a.Items?.Count ?? 0); i++) if (!Unify(a.Items[i], b.Items[i])) return false;
                    return Unify(a.Ret ?? RTy.Unit, b.Ret ?? RTy.Unit);
                case RK.Closure: return a.Closure == b.Closure;
            }
            return true;
        }

        static bool Bind(RTy v, RTy t)
        {
            if (t.Kind == RK.Var)
            {
                if (v.Class != 0 && t.Class != 0 && v.Class != t.Class) return false;
                var into = t.Class >= v.Class ? t : v;
                var from = into == t ? v : t;
                if (into.Constraints == null && from.Constraints != null) into.Constraints = from.Constraints;
                else if (from.Constraints != null) into.Constraints.AddRange(from.Constraints);
                if (into.OriginStart < 0) { into.OriginStart = from.OriginStart; into.OriginEnd = from.OriginEnd; into.OriginWhat = from.OriginWhat; }
                from.Link = into;
                return true;
            }
            if (v.Class == 1 && t.Kind != RK.Int) return false;
            if (v.Class == 2 && t.Kind != RK.Float) return false;
            if (Occurs(v, t)) return false;
            v.Link = t;
            return true;
        }

        static bool Occurs(RTy v, RTy t)
        {
            t = RTy.Prune(t);
            if (t == null) return false;
            if (ReferenceEquals(v, t)) return true;
            if (t.Kind == RK.Closure) return false;
            if (t.A != null && Occurs(v, t.A)) return true;
            if (t.B != null && Occurs(v, t.B)) return true;
            if (t.Ret != null && Occurs(v, t.Ret)) return true;
            if (t.Items != null) foreach (var i in t.Items) if (Occurs(v, i)) return true;
            return false;
        }

        /// <summary>Coercion site (let with a type, argument, return): never, &amp;mut → &amp;, &amp;String → &amp;str, &amp;Vec → &amp;[T], &amp;&amp;T → &amp;T.</summary>
        public static bool Coerces(RTy expected, RTy actual)
        {
            var e = RTy.Prune(expected); var a = RTy.Prune(actual);
            if (a == null || e == null) return false;
            if (a.Kind == RK.Never) return true;
            if (e.Kind == RK.Ref && a.Kind == RK.Ref)
            {
                var ea = RTy.Prune(e.A); var aa = RTy.Prune(a.A);
                if (e.Mut && !a.Mut) return false;
                if (ea.Kind == RK.Str && aa.Kind == RK.String) return true;
                if (ea.Kind == RK.Slice && (aa.Kind == RK.Vec || aa.Kind == RK.Array)) return Unify(ea.A, aa.A);
                if (aa.Kind == RK.Ref && ea.Kind != RK.Ref && ea.Kind != RK.Var) return Coerces(e, RTy.Ref(aa.A, aa.Mut && a.Mut));
                return Unify(ea, aa);
            }
            if (e.Kind == RK.Fn && a.Kind == RK.Fn) return Unify(e, a);
            if (e.Kind == RK.Fn && a.Kind == RK.Closure)
            {
                if (a.Closure != null && a.Closure.Captures.Count > 0) return false;
                if ((e.Items?.Count ?? 0) != (a.Items?.Count ?? 0)) return false;
                for (int i = 0; i < (e.Items?.Count ?? 0); i++) if (!Unify(e.Items[i], a.Items[i])) return false;
                return Unify(e.Ret ?? RTy.Unit, a.Ret ?? RTy.Unit);
            }
            return Unify(e, a);
        }

        void Coerce(RTy expected, RTy actual, RNode at, string note = null)
        {
            if (Coerces(expected, actual)) return;
            var e = RTy.Prune(expected); var a = RTy.Prune(actual);
            string hint = note;
            if (hint == null)
            {
                if (e.Kind == RK.Ref && RTy.Prune(e.A).Kind == RK.Str && a.Kind == RK.String) hint = " (consider borrowing here: `&`)";
                else if (e.Kind == RK.String && a.Kind == RK.Ref && RTy.Prune(a.A).Kind == RK.Str) hint = " (try using a conversion method: `.to_string()`)";
                else if (e.Kind == RK.Ref && a.Kind != RK.Ref && a.Kind != RK.Var) hint = " (consider borrowing here: `&`)";
                else if (e.IsNumeric && a.IsNumeric) hint = " (you can convert with `as " + RTy.Show(e) + "`)";
                else if (e.Kind == RK.Option && a.Kind != RK.Option && a.Kind != RK.Var) hint = " (try wrapping the expression in `Some`)";
            }
            throw Mismatch(expected, actual, at, hint);
        }

        void UnifyOrMismatch(RTy expected, RTy actual, RNode at)
        {
            if (!Unify(expected, actual)) throw Mismatch(expected, actual, at);
        }

        // ================================================================== scopes
        void Push() => scopes.Add(new Dictionary<string, RVar>());
        void Pop() => scopes.RemoveAt(scopes.Count - 1);

        void Declare(RVar v)
        {
            CheckName(v.Name, v.DeclStart, v.DeclEnd);
            v.ScopeLevel = scopes.Count - 1;
            v.FnId = fnStack.Count > 0 ? Fn.Id : 0;
            v.LoopDepth = fnStack.Count > 0 ? Fn.LoopDepth : 0;
            scopes[scopes.Count - 1][v.Name] = v;
        }

        RVar Lookup(string name, out int level)
        {
            for (int i = scopes.Count - 1; i >= 0; i--)
                if (scopes[i].TryGetValue(name, out var v)) { level = i; return v; }
            level = -1;
            return null;
        }

        // ================================================================== driver
        public void Run()
        {
            foreach (var it in file.Items)
            {
                switch (it)
                {
                    case RUse u: Use(u); break;
                    case RMod m: ModDecl(m); break;
                }
            }
            foreach (var it in file.Items)
            {
                if (it is RFn f)
                {
                    if (Fns.ContainsKey(f.Name) || Globals.ContainsKey(f.Name))
                        throw Err("the name `" + f.Name + "` is defined multiple times (E0428)", f.NameStart, f.NameEnd);
                    CheckName(f.Name, f.NameStart, f.NameEnd);
                    var s = new RFnSym { Name = f.Name, Pub = f.Pub, Decl = f, NameStart = f.NameStart, NameEnd = f.NameEnd };
                    foreach (var p in f.Params)
                    {
                        s.Params.Add(ResolveType(p.Type, false, param: true));
                        s.ParamNames.Add(p.Pat?.Name ?? "_");
                    }
                    s.Ret = f.Ret == null ? RTy.Unit : ResolveType(f.Ret, false);
                    f.Sym = s;
                    Fns[f.Name] = s;
                    if (f.Name == "main") Main = f;
                }
                else if (it is RConst c)
                {
                    if (Fns.ContainsKey(c.Name) || Globals.ContainsKey(c.Name))
                        throw Err("the name `" + c.Name + "` is defined multiple times (E0428)", c.NameStart, c.NameEnd);
                    CheckName(c.Name, c.NameStart, c.NameEnd);
                    var t = ResolveType(c.Type, false);
                    if (c.Mut && !RTy.IsCopy(t))
                        throw Err("a `static mut` of type " + Q(t) + " is not supported in JavaFarmer (Rust 2024 forbids references to it): use a local variable and pass it as a parameter", c.NameStart, c.Type.End);
                    var v = new RVar { Name = c.Name, Kind = c.IsStatic ? RVarKind.Static : RVarKind.Const, Mut = c.Mut, Type = t, DeclStart = c.NameStart, DeclEnd = c.NameEnd, Pub = c.Pub };
                    c.Var = v;
                    Globals[c.Name] = v;
                }
            }
            if (Main != null)
            {
                if (Main.Params.Count > 0) throw Err("`main` function has wrong type: it takes no parameters (E0580)", Main.NameStart, Main.HeaderEnd);
                if (Main.Ret != null && ResolveType(Main.Ret, false).P.Kind != RK.Unit)
                    throw NotSupported("a `main` that returns a value", Main.Ret);
            }
            Push();
            foreach (var it in file.Items)
            {
                if (it is RConst c)
                {
                    fnStack.Add(new FnCtx { Ret = RTy.Unit, Id = ++fnIds, BaseScope = scopes.Count });
                    inConstInit = true;
                    var t = CheckExpr(c.Init);
                    inConstInit = false;
                    Coerce(c.Var.Type, t, c.Init);
                    fnStack.RemoveAt(fnStack.Count - 1);
                }
            }
            foreach (var it in file.Items) if (it is RFn f) CheckFn(f);
            Pop();
            Finish();
        }

        static void CheckName(string name, int s, int e)
        {
            if (name != null && name.StartsWith("__j", StringComparison.Ordinal))
                throw Err("identifiers starting with `__j` are reserved by JavaFarmer", s, e);
        }

        void Use(RUse u)
        {
            var path = u.Path;
            if (path.Count == 0) throw NotSupported("this `use`", u);
            string root = path[0];
            if (root == "farm")
            {
                if (path.Count == 1 && u.Glob) { farmGlob = true; return; }
                if (path.Count == 1 && u.Group != null) { foreach (var g in u.Group) { if (g.Name == "*") farmGlob = true; else farmNames.Add(g.Name); } return; }
                if (path.Count == 2 && !u.Glob && u.Group == null) { farmNames.Add(path[1]); return; }
                if (path.Count == 2 && path[1] == "Direction" && u.Glob) { foreach (var d in GameApi.DirectionNames) farmNames.Add(d); return; }
                throw NotSupported("this `use farm::...` path (write `use farm::*;`)", u);
            }
            if (root == "std")
            {
                if (path.Count == 2 && path[1] == "collections")
                {
                    if (u.Glob) { hashMapImported = hashSetImported = true; return; }
                    if (u.Group != null)
                    {
                        foreach (var g in u.Group)
                        {
                            if (g.Name == "HashMap") hashMapImported = true;
                            else if (g.Name == "HashSet") hashSetImported = true;
                            else if (g.Name == "*") hashMapImported = hashSetImported = true;
                            else throw NotSupported("`std::collections::" + g.Name + "` (use Vec, HashMap or HashSet)", g.Start, g.End);
                        }
                        return;
                    }
                }
                if (path.Count == 3 && path[1] == "collections" && !u.Glob && u.Group == null)
                {
                    if (path[2] == "HashMap") { hashMapImported = true; return; }
                    if (path[2] == "HashSet") { hashSetImported = true; return; }
                    throw NotSupported("`std::collections::" + path[2] + "` (use Vec, HashMap or HashSet)", u);
                }
                if (path.Count == 2 && path[1] == "cmp" && u.Group != null)
                {
                    foreach (var g in u.Group)
                    {
                        if (g.Name == "min") stdCmpMin = true;
                        else if (g.Name == "max") stdCmpMax = true;
                        else throw NotSupported("`std::cmp::" + g.Name + "`", g.Start, g.End);
                    }
                    return;
                }
                if (path.Count == 3 && path[1] == "cmp" && !u.Glob && u.Group == null && (path[2] == "min" || path[2] == "max"))
                {
                    if (path[2] == "min") stdCmpMin = true; else stdCmpMax = true;
                    return;
                }
                throw NotSupported("`use " + string.Join("::", path) + (u.Glob ? "::*" : "") + "`", u);
            }
            int k = 0;
            if (root == "crate") k = 1;
            else if (root == "self" || root == "super") throw NotSupported("`use " + root + "::...`", u);
            if (path.Count <= k) throw NotSupported("this `use`", u);
            var module = path[k];
            if (!ModuleExists(module, out var actual))
                throw Err("unresolved import `" + string.Join("::", path) + "`: there is no code window named `" + module + "` (E0432)", u.Start, u.PathEnd);
            if (path.Count == k + 1 && !u.Glob && u.Group == null) { ModuleAliases[module] = actual; return; }
            var decls = Decls(actual);
            if (decls.Error != null) throw Err("cannot use `" + actual + "`: " + decls.Error, u.Start, u.End);
            if (u.Glob && path.Count == k + 1)
            {
                foreach (var f in decls.Fns.Values) if (f.Pub) importedItems[f.Name] = (actual, f.Name);
                foreach (var v in decls.Vars.Values) if (v.Pub) importedItems[v.Name] = (actual, v.Name);
                ModuleAliases[module] = actual;
                return;
            }
            if (u.Group != null && path.Count == k + 1)
            {
                foreach (var g in u.Group)
                {
                    if (g.Name == "self") { ModuleAliases[module] = actual; continue; }
                    ImportItem(decls, actual, g.Name, g.Start, g.End);
                }
                return;
            }
            if (path.Count == k + 2 && !u.Glob && u.Group == null) { ImportItem(decls, actual, path[k + 1], u.Start, u.PathEnd); return; }
            throw NotSupported("this `use` path", u);
        }

        void ImportItem(RModuleDecls d, string module, string name, int s, int e)
        {
            if (d.Fns.TryGetValue(name, out var f))
            {
                if (!f.Pub) throw Err("function `" + name + "` is private (E0603)", s, e);
                importedItems[name] = (module, name);
                return;
            }
            if (d.Vars.TryGetValue(name, out var v))
            {
                if (!v.Pub) throw Err((v.Mut ? "static" : "constant") + " `" + name + "` is private (E0603)", s, e);
                importedItems[name] = (module, name);
                return;
            }
            throw Err("unresolved import: no `" + name + "` in `" + module + "` (E0432)", s, e);
        }

        bool ModuleExists(string name, out string actual)
        {
            actual = name;
            if (others == null) return false;
            return others.ContainsKey(name);
        }

        RModuleDecls Decls(string module)
        {
            if (moduleDecls.TryGetValue(module, out var d)) return d;
            string src = null;
            others?.TryGetValue(module, out src);
            d = ForeignModules.IsForeign(Options, module, "rust") ? RModuleDecls.FromSignature(module, ForeignModules.Resolve(module, Options)) : RModuleDecls.Get(module, src ?? "");
            moduleDecls[module] = d;
            return d;
        }

        void ModDecl(RMod m)
        {
            if (m.Name == FileName) throw Err("a file cannot be a module of itself", m.NameStart, m.NameEnd);
            if (!ModuleExists(m.Name, out var actual))
                throw Err("file not found for module `" + m.Name + "` (create a code window named \"" + m.Name + "\") (E0583)", m.Start, m.End);
            ModuleAliases[m.Name] = actual;
        }

        // ================================================================== types
        /// <summary>Type of a signature in another file (no import checks).</summary>
        internal static RTy TypeOfRef(RTypeRef t, bool param)
        {
            try { return new RustChecker(new RFile(), "_", null) { lenient = true }.ResolveType(t, false, param); }
            catch (RustAbort) { return RTy.Error; }
        }
        bool lenient;

        internal RTy ResolveType(RTypeRef t, bool allowInfer, bool param = false)
        {
            if (t == null) return allowInfer ? NewVar() : RTy.Unit;
            switch (t.Kind)
            {
                case RTypeKind.Ref: return RTy.Ref(ResolveType(t.Elem, allowInfer), t.Mut);
                case RTypeKind.Tuple:
                    if (t.Args.Count == 0) return RTy.Unit;
                    return RTy.Tuple(t.Args.Select(a => ResolveType(a, allowInfer)).ToArray());
                case RTypeKind.Array:
                    {
                        long len = -1;
                        if (t.Len is RLit l && l.Kind == RLitKind.Int) len = (long)l.I;
                        else if (t.Len is RPathExpr pe && pe.Segs.Count == 1 && Globals.TryGetValue(pe.Last, out var gv) && gv.Kind == RVarKind.Const) len = -1;
                        else throw NotSupported("an array length that is not a number", t.Len ?? (RNode)t);
                        return RTy.Array(ResolveType(t.Elem, allowInfer), len);
                    }
                case RTypeKind.Slice: return RTy.Slice(ResolveType(t.Elem, allowInfer));
                case RTypeKind.Infer:
                    if (!allowInfer) throw Err("the placeholder `_` is not allowed within types on item signatures (E0121)", t);
                    return NewVar(0, t);
                case RTypeKind.Never: return RTy.Never;
                case RTypeKind.Fn:
                    return RTy.Fn(t.Args.Select(a => ResolveType(a, allowInfer)).ToList(), t.Elem == null ? RTy.Unit : ResolveType(t.Elem, allowInfer));
                case RTypeKind.ImplTrait:
                    {
                        var tr = t.Elem;
                        if (!param) throw NotSupported("`impl Trait` in this position", t);
                        if (tr != null && tr.Kind == RTypeKind.Path && (tr.Name == "Fn" || tr.Name == "FnMut" || tr.Name == "FnOnce"))
                            return RTy.Fn((tr.Args ?? new List<RTypeRef>()).Select(a => ResolveType(a, allowInfer)).ToList(), tr.Elem == null ? RTy.Unit : ResolveType(tr.Elem, allowInfer));
                        throw NotSupported("`impl " + (tr?.Name ?? "Trait") + "`", t);
                    }
            }
            var path = t.Path;
            int k = 0;
            if (path.Count >= 3 && path[0] == "std" && path[1] == "collections") k = 2;
            else if (path.Count == 2 && path[0] == "farm") k = 1;
            if (k != path.Count - 1 && path.Count > 1) throw NotSupported("the type path `" + string.Join("::", path) + "`", t);
            var name = path[k];
            bool qualified = k > 0;
            RTy Arg(int i)
            {
                if (t.Args == null || i >= t.Args.Count)
                {
                    if (allowInfer) return NewVar(0, t);
                    throw Err("missing generics for `" + name + "` (E0107)", t);
                }
                return ResolveType(t.Args[i], allowInfer);
            }
            void NoArgs() { if (t.Args != null && t.Args.Count > 0) throw Err("type arguments are not allowed on `" + name + "` (E0109)", t); }
            switch (name)
            {
                case "i8": case "i16": case "i32": case "i64": case "i128": case "isize":
                case "u8": case "u16": case "u32": case "u64": case "u128": case "usize":
                    NoArgs(); return RTy.Int(name);
                case "f32": case "f64": NoArgs(); return RTy.Float(name);
                case "bool": NoArgs(); return RTy.Bool;
                case "char": throw NotSupported("the `char` type (use String / &str)", t);
                case "str": NoArgs(); return RTy.Str;
                case "String": NoArgs(); return RTy.String;
                case "Vec": return RTy.Vec(Arg(0));
                case "Option": return RTy.Option(Arg(0));
                case "HashMap":
                    if (!hashMapImported && !qualified && !lenient) throw Err("cannot find type `HashMap` in this scope (add `use std::collections::HashMap;`) (E0412)", t);
                    return RTy.Map(Arg(0), Arg(1));
                case "HashSet":
                    if (!hashSetImported && !qualified && !lenient) throw Err("cannot find type `HashSet` in this scope (add `use std::collections::HashSet;`) (E0412)", t);
                    return RTy.Set(Arg(0));
                case "Drone": RequireFarm(name, t); return RTy.Drone(t.Args != null && t.Args.Count > 0 ? ResolveType(t.Args[0], allowInfer) : RTy.Unit);
                case "Position": RequireFarm(name, t); NoArgs(); return RTy.Position;
                case "Companion": RequireFarm(name, t); NoArgs(); return RTy.Companion;
                case "Box": case "Rc": case "RefCell": case "Cell": case "Arc": case "Mutex": case "BTreeMap": case "BTreeSet": case "VecDeque": case "Result":
                    throw NotSupported("the type `" + name + "`", t);
                case "Fn": case "FnMut": case "FnOnce":
                    throw Err("expected a type, found trait `" + name + "` (write `fn()` or `impl Fn()`) (E0782)", t);
                case "int": case "integer": throw Err("cannot find type `" + name + "` in this scope (use `i32`) (E0412)", t);
                case "float": case "double": throw Err("cannot find type `" + name + "` in this scope (use `f64`) (E0412)", t);
                case "string": throw Err("cannot find type `string` in this scope (use `String` or `&str`) (E0412)", t);
                case "boolean": throw Err("cannot find type `boolean` in this scope (use `bool`) (E0412)", t);
            }
            if (Array.IndexOf(RustApi.Enums, name) >= 0) { RequireFarm(name, t); NoArgs(); return RTy.Game(name); }
            if (GameApi.GameTypeOfCategory(name) != null)
                throw Err("cannot find type `" + name + "` in this scope (the game's " + name + " are the Rust enum `" + GameApi.GameTypeOfCategory(name) + "`) (E0412)", t);
            throw Err("cannot find type `" + name + "` in this scope (E0412)", t);
        }

        void RequireFarm(string name, RNode at)
        {
            if (lenient || farmGlob || farmNames.Contains(name)) return;
            throw Err("cannot find type `" + name + "` in this scope (add `use farm::*;`) (E0412)", at);
        }

        bool FarmHas(string name) => lenient || farmGlob || farmNames.Contains(name);

        // ================================================================== functions
        void CheckFn(RFn f)
        {
            var s = f.Sym;
            fnStack.Add(new FnCtx { Ret = s.Ret, Id = ++fnIds, BaseScope = scopes.Count });
            Push();
            var saveFlow = flow;
            flow = new Flow();
            diverged = false;
            for (int i = 0; i < f.Params.Count; i++)
                BindPattern(f.Params[i].Pat, s.Params[i], initialized: true, kind: RVarKind.Param);
            var bt = CheckBlock(f.Body);
            if (f.Body.Tail == null)
            {
                var rt = RTy.Prune(s.Ret);
                if (!diverged && rt.Kind != RK.Unit && rt.Kind != RK.Never)
                {
                    var lastStmt = f.Body.Stmts.Count > 0 ? f.Body.Stmts[f.Body.Stmts.Count - 1] as RExprStmt : null;
                    string hint = lastStmt != null && lastStmt.Semi && !(lastStmt.E is RReturn) ? " (remove the `;` after the last expression to return it)" : " (the function body has no tail or `return` expression)";
                    throw Err("mismatched types: expected " + Q(rt) + ", found `()`" + hint + " (E0308)", f.Ret);
                }
            }
            else Coerce(s.Ret, bt, f.Body.Tail);
            flow = saveFlow;
            Pop();
            fnStack.RemoveAt(fnStack.Count - 1);
            diverged = false;
        }

        // ================================================================== patterns (bindings)
        /// <summary>Irrefutable pattern of let / params / for: binds variables.</summary>
        void BindPattern(RPat p, RTy t, bool initialized, RVarKind kind = RVarKind.Local)
        {
            switch (p.Kind)
            {
                case RPatKind.Ident:
                    {
                        if (IsConstPatternName(p.Name)) throw Err("refutable pattern in local binding: `" + p.Name + "` is a constant (E0005)", p);
                        var v = new RVar { Name = p.Name, Kind = kind, Mut = p.Mut, Type = t, DeclStart = p.Start, DeclEnd = p.End };
                        Declare(v);
                        p.Var = v;
                        p.Type = t;
                        if (initialized) { flow.Init.Add(v); flow.Maybe.Add(v); }
                        return;
                    }
                case RPatKind.Wild: p.Type = t; return;
                case RPatKind.Tuple:
                    {
                        var tt = RTy.Prune(t);
                        if (tt.Kind == RK.Ref && RTy.Prune(tt.A).Kind == RK.Tuple)
                        {
                            var inner = RTy.Prune(tt.A);
                            if (inner.Items.Count != p.Items.Count) throw Err("mismatched types: expected a tuple with " + inner.Items.Count + " elements, found one with " + p.Items.Count + " elements (E0308)", p);
                            for (int i = 0; i < p.Items.Count; i++) BindPattern(p.Items[i], RTy.Ref(inner.Items[i], tt.Mut), initialized, kind);
                            p.Type = t;
                            return;
                        }
                        var items = p.Items.Select(_ => NewVar()).ToArray();
                        if (!Unify(t, RTy.Tuple(items)))
                        {
                            if (tt.Kind == RK.Tuple) throw Err("mismatched types: expected a tuple with " + tt.Items.Count + " elements, found one with " + p.Items.Count + " elements (E0308)", p);
                            throw Err("mismatched types: expected " + Q(t) + ", found tuple (E0308)", p);
                        }
                        for (int i = 0; i < p.Items.Count; i++) BindPattern(p.Items[i], items[i], initialized, kind);
                        p.Type = t;
                        return;
                    }
                case RPatKind.Ref:
                    {
                        var tt = RTy.Prune(t);
                        RTy inner;
                        if (tt.Kind == RK.Ref) inner = tt.A;
                        else
                        {
                            inner = NewVar();
                            if (!Unify(t, RTy.Ref(inner, false))) throw Err("mismatched types: expected " + Q(t) + ", found `&_` (E0308)", p);
                        }
                        BindPattern(p.Inner, inner, initialized, kind);
                        p.Type = t;
                        return;
                    }
                case RPatKind.TupleStruct:
                case RPatKind.Path:
                case RPatKind.Lit:
                    throw Err("refutable pattern in local binding (use `if let` or `match`) (E0005)", p);
                case RPatKind.Or:
                    throw NotSupported("an or-pattern here", p);
                case RPatKind.Rest:
                    throw NotSupported("`..` in this pattern", p);
            }
            throw NotSupported("this pattern", p);
        }

        bool IsConstPatternName(string name)
        {
            if (name == "None") return true;
            if (Lookup(name, out _) != null) return false;
            if (Globals.TryGetValue(name, out var g) && g.Kind == RVarKind.Const) return true;
            if (Array.IndexOf(GameApi.DirectionNames, name) >= 0 && FarmHas(name)) return true;
            return false;
        }

        // ================================================================== blocks / statements
        RTy CheckBlock(RBlock b)
        {
            Push();
            if (b.Unsafe) unsafeDepth++;
            try
            {
                foreach (var s in b.Stmts) CheckStmt(s);
                if (b.Tail != null)
                {
                    var t = CheckExpr(b.Tail);
                    if (RTy.Prune(t).Kind == RK.Never) diverged = true;
                    return t;
                }
                return diverged ? RTy.Never : RTy.Unit;
            }
            finally
            {
                if (b.Unsafe) unsafeDepth--;
                Pop();
            }
        }

        void CheckStmt(RStmt s)
        {
            switch (s)
            {
                case REmptyStmt _: return;
                case RLet l: CheckLet(l); return;
                case RItemStmt it:
                    switch (it.Item)
                    {
                        case RFn f: throw NotSupported("a function inside a function (move it to the top level, or use a closure)", f.Start, f.HeaderEnd);
                        case RUse u: Use(u); return;
                        case RConst c:
                            {
                                if (c.IsStatic) throw NotSupported("a `static` inside a function", c);
                                var t = ResolveType(c.Type, false);
                                bool old = inConstInit; inConstInit = true;
                                var it2 = CheckExpr(c.Init);
                                inConstInit = old;
                                Coerce(t, it2, c.Init);
                                var v = new RVar { Name = c.Name, Kind = RVarKind.Local, Mut = false, Type = t, DeclStart = c.NameStart, DeclEnd = c.NameEnd };
                                Declare(v);
                                c.Var = v;
                                flow.Init.Add(v); flow.Maybe.Add(v);
                                return;
                            }
                        case RMod m: throw NotSupported("`mod` inside a function", m);
                    }
                    throw NotSupported("this item inside a function", it);
                case RExprStmt es:
                    {
                        var t = CheckExpr(es.E);
                        if (!es.Semi && !diverged)
                        {
                            var pt = RTy.Prune(t);
                            if (pt.Kind != RK.Unit && pt.Kind != RK.Never && !Unify(RTy.Unit, t))
                                throw Err("mismatched types: expected `()`, found " + Q(t) + " (add a `;` after it) (E0308)", es.E);
                        }
                        if (RTy.Prune(t).Kind == RK.Never) diverged = true;
                        return;
                    }
            }
            throw NotSupported("this statement", s);
        }

        void CheckLet(RLet l)
        {
            RTy declared = l.Type != null ? ResolveType(l.Type, true) : null;
            if (l.Init != null)
            {
                var it = CheckExpr(l.Init);
                if (declared != null) Coerce(declared, it, l.Init);
                else declared = it;
            }
            else if (declared == null) declared = NewVar(0, l.Pat, null);
            BindPattern(l.Pat, declared, initialized: l.Init != null);
        }

        // ================================================================== finish: defaults, E0282, trait bounds, literal ranges
        void Finish()
        {
            foreach (var v in allVars)
            {
                var r = RTy.Prune(v);
                if (r.Kind != RK.Var) continue;
                if (r.Class == 1) r.Link = RTy.I32;
                else if (r.Class == 2) r.Link = RTy.F64;
            }
            foreach (var (t, at, trait) in pendingTraits) CheckTrait(t, at, trait);
            foreach (var v in allVars)
            {
                var r = RTy.Prune(v);
                if (r.Kind == RK.Var && r.OriginStart >= 0)
                    throw Err("type annotations needed" + (r.OriginWhat != null ? " for `" + r.OriginWhat + "`" : "") + " (E0282)", r.OriginStart, r.OriginEnd);
            }
            foreach (var l in intLits) CheckLiteralRange(l);
        }

        void CheckTrait(RTy t, RExpr at, string trait)
        {
            var r = RTy.Prune(t);
            if (r.Kind == RK.Var) { if (r.Class == 0 && at != null) throw Err("type annotations needed (E0282)", at); return; }
            if (trait == "Measurement")
            {
                var inner = r.Kind == RK.Option ? RTy.Prune(r.A) : r;
                bool ok = inner.Kind == RK.Float && inner.Name == "f64" || inner.Kind == RK.Int && inner.Name == "i32"
                          || (inner.Kind == RK.Tuple && inner.Items.Count == 2 && inner.Items.All(x => RTy.Prune(x).Kind == RK.Int && RTy.Prune(x).Name == "i32"));
                if (!ok) throw Err("the trait bound `" + RTy.Show(r) + ": Measurement` is not satisfied (measure() gives f64, i32, Position or an Option of them) (E0277)", at);
                return;
            }
            if (trait.StartsWith("union:"))
            {
                var members = RustApi.UnionMembers(trait.Substring(6));
                bool ok = r.Kind == RK.Game && members.Contains(r.Name);
                if (!ok) throw Err("the trait bound `" + RTy.Show(r) + ": " + RustApi.UnionTrait(trait.Substring(6)) + "` is not satisfied (expected one of " + string.Join(", ", members.Select(m => "`" + m + "`")) + ") (E0277)", at);
            }
        }

        void CheckLiteralRange(RLit l)
        {
            var t = RTy.Prune(l.Type);
            if (t == null || t.Kind != RK.Int) return;
            ulong max;
            switch (t.Name)
            {
                case "i8": max = 128; break;
                case "u8": max = 255; break;
                case "i16": max = 32768; break;
                case "u16": max = 65535; break;
                case "i32": max = 2147483648UL; break;
                case "u32": max = 4294967295UL; break;
                case "i64": case "isize": max = 9223372036854775808UL; break;
                default: max = ulong.MaxValue; break;
            }
            bool negated = negatedLits.Contains(l);
            bool signed = t.Name.StartsWith("i");
            ulong lim = signed && !negated ? max - 1 : max;
            if (l.I > lim) throw Err("literal out of range for `" + t.Name + "` (the literal `" + l.Tok?.Text + "` does not fit into the type `" + t.Name + "`)", l);
        }

        readonly HashSet<RLit> negatedLits = new HashSet<RLit>();
    }
}
