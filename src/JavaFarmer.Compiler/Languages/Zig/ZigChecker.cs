using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JavaFarmer.Compiler.ZigImpl
{
    /// <summary>Result of checking an expression: its IR (null for types / namespaces), Zig type, comptime value.</summary>
    internal sealed class ZR
    {
        public IrExpr Ir;
        public ZType T;
        public bool Known;           // comptime-known number / string / bool
        public long IntVal;
        public double FloatVal;
        public string StrVal;
        public bool BoolVal;
        public ZSym Sym;             // the variable this expression names
        public ZApiFn Api;           // a farm function used as a value (callee)
        public string Special;       // std.debug.print, std.mem.eql ... used as a callee
        public static ZR Error => new ZR { T = ZType.Err };
    }

    sealed class ZCheckAbort : Exception { }

    /// <summary>
    /// Checks a parsed Zig file like AstGen + Sema would (names, unused / never-mutated locals, types, coercions)
    /// and lowers it to Farm IR in the same pass. AstGen-class errors win over Sema-class errors (the real compiler
    /// stops after AstGen errors).
    /// </summary>
    internal sealed partial class ZigChecker
    {
        readonly ZFile file;
        readonly string fileName;
        readonly IReadOnlyDictionary<string, string> others;
        readonly bool declsOnly;
        public readonly List<Diagnostic> AstErrors = new List<Diagnostic>();
        public readonly List<Diagnostic> SemaErrors = new List<Diagnostic>();
        readonly Dictionary<string, ZSym> container = new Dictionary<string, ZSym>(StringComparer.Ordinal);
        readonly List<ZSym> containerOrder = new List<ZSym>();
        readonly List<Dictionary<string, ZSym>> scopes = new List<Dictionary<string, ZSym>>();
        readonly List<List<ZSym>> scopeOrder = new List<List<ZSym>>();
        public IrModule Module;
        static readonly HashSet<string> resolvingFiles = new HashSet<string>();

        /// <summary>The compile options (OtherFileLanguages: files written in other languages).</summary>
        CompileOptions options;

        ZigChecker(ZFile f, string name, IReadOnlyDictionary<string, string> otherFiles, bool declsOnly)
        {
            file = f; fileName = name; others = otherFiles; this.declsOnly = declsOnly;
        }

        public static IrModule Check(ZFile f, string fileName, IReadOnlyDictionary<string, string> otherFiles, List<Diagnostic> errors) =>
            Check(f, fileName, new CompileOptions { FileName = fileName, OtherFiles = otherFiles }, errors);

        public static IrModule Check(ZFile f, string fileName, CompileOptions options, List<Diagnostic> errors)
        {
            var c = new ZigChecker(f, fileName, options?.OtherFiles, false) { options = options };
            try { c.Run(); }
            catch (ZCheckAbort) { }
            // a Sema error can stop the checker before it sees a use: drop "unused" / "never mutated" errors of names
            // that are referenced in the text (AstGen decides syntactically, so the real compiler would not report them)
            if (c.SemaErrors.Count > 0 && c.usageErrors.Count > 0)
                foreach (var (d, n) in c.usageErrors)
                    if (f.IdentCount.TryGetValue(n, out var cnt) && cnt > 1) c.AstErrors.Remove(d);
            if (c.AstErrors.Count > 0) { errors.AddRange(c.AstErrors); return null; }
            if (c.SemaErrors.Count > 0) { errors.AddRange(c.SemaErrors); return null; }
            return c.Module;
        }

        // ================================================================== diagnostics
        void ErrA(string msg, int start, int end) { AstErrors.Add(new Diagnostic(msg, start, end)); }
        void ErrA(string msg, ZNode n) => ErrA(msg, n.Start, n.End);
        void ErrS(string msg, int start, int end) { SemaErrors.Add(new Diagnostic(msg, start, end)); if (SemaErrors.Count > 50) throw new ZCheckAbort(); }
        void ErrS(string msg, ZNode n) => ErrS(msg, n.Start, n.End);
        void NotSupported(string what, ZNode n) => ErrS(what + " not supported in JavaFarmer", n);
        static string Q(object t) => "'" + t + "'";

        // ================================================================== other files
        sealed class OtherModule
        {
            public string Name;
            /// <summary>A file in another language that could not be read (ForeignModules signature error).</summary>
            public string Error;
            public Dictionary<string, ZSym> Decls = new Dictionary<string, ZSym>(StringComparer.Ordinal);
        }

        static readonly Dictionary<string, (string Src, int Ver, OtherModule M)> otherCache = new Dictionary<string, (string, int, OtherModule)>();
        static readonly object otherGate = new object();
        readonly Dictionary<string, OtherModule> otherMods = new Dictionary<string, OtherModule>();

        OtherModule Other(string module)
        {
            if (otherMods.TryGetValue(module, out var m)) return m;
            if (others == null || !others.TryGetValue(module, out var src)) return null;
            if (ForeignModules.IsForeign(options, module, "zig")) return otherMods[module] = ForeignOther(module);
            lock (otherGate)
            {
                if (otherCache.TryGetValue(module, out var c) && c.Src == src && c.Ver == GameApi.Version) { otherMods[module] = c.M; return c.M; }
                if (resolvingFiles.Contains(module)) return null;
                resolvingFiles.Add(module);
            }
            try
            {
                var f = ZigParser.Parse(src, out var perr);
                var om = new OtherModule { Name = module };
                if (perr == null)
                {
                    var chk = new ZigChecker(f, module, others, true) { options = options };
                    try { chk.Run(); } catch (ZCheckAbort) { } catch (Exception) { }
                    var fnNames = chk.containerOrder.Where(s => s.Kind == ZSymKind.Fn).Select(s => s.Name).ToList();
                    var gNames = chk.containerOrder.Where(s => s.Kind == ZSymKind.Global).Select(s => s.Name).ToList();
                    var fmap = PyNaming.ModuleLevel(fnNames, gNames, out var gmap);
                    foreach (var s in chk.containerOrder)
                    {
                        var ext = new ZSym { Name = s.Name, Kind = s.Kind, Type = s.Type, IsVar = s.IsVar, Sig = s.Sig, IsPub = s.IsPub, Module = module, HasValue = s.HasValue, IntValue = s.IntValue, FloatValue = s.FloatValue, StrValue = s.StrValue, Resolved = true };
                        if (s.Kind == ZSymKind.Fn)
                        {
                            ext.Ir = new IrSymbol(s.Name, IrSymbolKind.ExternalFunction, IrType.Func(s.Sig?.Ret?.Ir() ?? IrType.Void)) { Module = module, PyName = fmap.TryGetValue(s.Name, out var pn) ? pn : null };
                            ext.Ir.ParamTypes = s.Sig?.Params.Select(p => p.Ir()).ToList();
                            if (ext.Sig != null) ext.Sig = new ZFnSig { Params = s.Sig.Params, ParamNames = s.Sig.ParamNames, Ret = s.Sig.Ret, RetError = s.Sig.RetError, IsPub = s.Sig.IsPub, Module = module };
                        }
                        else if (s.Kind == ZSymKind.Global)
                            ext.Ir = new IrSymbol(s.Name, IrSymbolKind.ExternalGlobal, s.Type?.Ir() ?? IrType.Unknown) { Module = module, PyName = gmap.TryGetValue(s.Name, out var gn) ? gn : null, IsConst = !s.IsVar };
                        om.Decls[s.Name] = ext;
                    }
                }
                lock (otherGate)
                {
                    if (otherCache.Count > 64) otherCache.Clear();
                    otherCache[module] = (src, GameApi.Version, om);
                }
                otherMods[module] = om;
                return om;
            }
            finally { lock (otherGate) resolvingFiles.Remove(module); }
        }

        /// <summary>
        /// A file written in another language (docs/LANGUAGE-GUIDE.md §9): its declarations come from that language's own
        /// frontend (ForeignModules); types it does not know (Python, JS, R) become ZK.Foreign values.
        /// </summary>
        OtherModule ForeignOther(string module)
        {
            var sig = ForeignModules.Resolve(module, options);
            var om = new OtherModule { Name = module };
            if (sig == null || !sig.Ok) { om.Error = sig?.Error ?? "cannot read " + module; return om; }
            foreach (var f in sig.Functions)
            {
                var zs = new ZFnSig { IsPub = true, Module = module, Ret = ForeignType(f.ReturnType, true) };
                foreach (var p in f.Params) { zs.Params.Add(p.Type != null && p.Type.IsKnown ? ZigApi.FromIr(p.Type, false) : null); zs.ParamNames.Add(p.Name); }
                if (f.Params.Any(p => p.Optional)) zs.MinParams = f.MinArgs;
                var s = new ZSym { Name = f.Name, Kind = ZSymKind.Fn, Sig = zs, IsPub = true, Module = module, Resolved = true };
                s.Ir = new IrSymbol(f.Name, IrSymbolKind.ExternalFunction, IrType.Func(f.ReturnType ?? IrType.Unknown, f.Params.Select(p => p.Type ?? IrType.Unknown).ToArray()))
                {
                    Module = module, PyName = f.PyName, ParamTypes = f.Params.Select(p => p.Type ?? IrType.Unknown).ToList(),
                };
                om.Decls[f.Name] = s;
            }
            foreach (var g in sig.Globals)
            {
                if (om.Decls.ContainsKey(g.Name)) continue;
                var s = new ZSym { Name = g.Name, Kind = ZSymKind.Global, Type = ForeignType(g.Type, false), IsVar = !g.IsConst, IsPub = true, Module = module, Resolved = true };
                s.Ir = new IrSymbol(g.Name, IrSymbolKind.ExternalGlobal, g.Type ?? IrType.Unknown) { Module = module, PyName = g.PyName, IsConst = g.IsConst };
                om.Decls[g.Name] = s;
            }
            return om;
        }

        static ZType ForeignType(IrType t, bool result)
        {
            if (t == null || !t.IsKnown || t.Kind == IrTypeKind.Any) return ZType.Foreign;
            if (t.Kind == IrTypeKind.Void) return result ? ZType.Void : ZType.Foreign;
            return ZigApi.FromIr(t, false) ?? ZType.Foreign;
        }

        // ================================================================== module
        void Run()
        {
            Module = new IrModule { Name = fileName, SourceLanguage = "zig" };
            // pass 1: names
            foreach (var d in file.Decls)
            {
                switch (d)
                {
                    case ZFnDecl f:
                        {
                            var s = new ZSym { Name = f.Name, Kind = ZSymKind.Fn, Decl = f, IsPub = f.IsPub, DeclStart = f.NameStart, DeclEnd = f.NameEnd };
                            AddContainer(s, f.NameStart, f.NameEnd);
                            break;
                        }
                    case ZVarDecl v:
                        {
                            var s = new ZSym { Name = v.Name, Kind = ZSymKind.Global, Decl = v, IsPub = v.IsPub, IsVar = !v.IsConst, DeclStart = v.NameStart, DeclEnd = v.NameEnd };
                            AddContainer(s, v.NameStart, v.NameEnd);
                            break;
                        }
                    case ZTopUnsupported u:
                        if (!declsOnly) NotSupported(u.What, u);
                        break;
                }
            }
            DocComments();
            // pass 2: globals and aliases, then function signatures
            foreach (var s in containerOrder) if (s.Kind == ZSymKind.Global) ResolveGlobal(s);
            foreach (var s in containerOrder) if (s.Kind == ZSymKind.Fn) ResolveFn(s);
            if (declsOnly) return;
            // the entry point
            if (container.TryGetValue("main", out var main) && main.Kind == ZSymKind.Fn)
            {
                var md = (ZFnDecl)main.Decl;
                if (!md.IsPub) ErrS("'main' is not marked 'pub'", md.NameStart, md.NameEnd);
                if (md.Params.Count > 0) NotSupported("parameters of main are", new ZParam2Node(md.Params[0]));
                if (main.Sig != null && !(main.Sig.Ret.K == ZK.Void || main.Sig.Ret.K == ZK.Error))
                    ErrS("expected return type of main to be 'void', '!void', 'noreturn', 'u8', or '!u8'", md.Ret.Start, md.Ret.End);
            }
            // pass 3: bodies
            foreach (var d in file.Decls)
            {
                if (d is ZFnDecl f && container.TryGetValue(f.Name, out var s) && s.Decl == f) Module.Items.Add(Function(s, f));
                else if (d is ZVarDecl v && container.TryGetValue(v.Name, out var g) && g.Decl == v && g.Kind == ZSymKind.Global && g.Ir != null)
                {
                    // Zig's container-level declarations are order-independent, Python's assignments are not: a global
                    // whose initializer reads a later one comes after it (resolution order)
                    int at = resolvedGlobals.IndexOf(g);
                    for (int k = 0; k < at; k++)
                    {
                        var dep = resolvedGlobals[k];
                        if (!emittedGlobals.Contains(dep) && dep.Kind == ZSymKind.Global && dep.Ir != null && globalItems.TryGetValue(dep, out var di) && dep.DeclStart > v.NameStart)
                        { Module.Items.Add(di); emittedGlobals.Add(dep); }
                    }
                    if (emittedGlobals.Add(g)) Module.Items.Add(globalItems.TryGetValue(g, out var item) ? item : new IrGlobal { Symbol = g.Ir, Span = v.Span, NameSpan = new Span(v.NameStart, v.NameEnd) });
                }
            }
            var comments = new List<IrComment>();
            foreach (var t in file.Comments)
            {
                var text = t.Text;
                bool doc = text.StartsWith("///") && !text.StartsWith("////") || text.StartsWith("//!");
                comments.Add(new IrComment { Text = text.Substring(doc ? 3 : 2), IsDoc = doc, Span = new Span(t.Start, t.End) });
            }
            commentList = comments;
        }

        readonly HashSet<ZSym> emittedGlobals = new HashSet<ZSym>();
        List<IrComment> commentList;

        /// <summary>"///" documents the declaration after it (AstGen/parser): not a statement, and not the end of the file.</summary>
        void DocComments()
        {
            if (declsOnly) return;
            foreach (var t in file.Comments)
            {
                if (!t.Text.StartsWith("///") || t.Text.StartsWith("////")) continue;
                if (file.Decls.OfType<ZFnDecl>().Any(f => f.Body != null && t.Start > f.Body.Start && t.Start < f.Body.End))
                { ErrA("expected statement, found 'a document comment'", t.Start, t.End); continue; }
                if (file.Decls.Any(d => d.Start <= t.Start && t.Start < d.End)) continue;
                if (!file.Decls.Any(d => d.Start > t.Start)) ErrA("unattached documentation comment", t.Start, t.End);
            }
        }
        public List<IrComment> Comments => commentList ?? new List<IrComment>();

        /// <summary>A ZNode wrapper for a parameter (for spans).</summary>
        sealed class ZParam2Node : ZNode { public ZParam2Node(ZParam p) { Start = p.Start; End = p.End; } }

        void AddContainer(ZSym s, int start, int end)
        {
            if (s.Name.StartsWith("__j", StringComparison.Ordinal)) { ErrS("identifiers starting with '__j' are reserved by JavaFarmer", start, end); }
            if (IsPrimitiveName(s.Name)) { ErrA("name shadows primitive " + Q(s.Name), start, end); return; }
            if (container.ContainsKey(s.Name)) { ErrA("duplicate struct member name " + Q(s.Name), start, end); return; }
            container[s.Name] = s;
            containerOrder.Add(s);
        }

        static readonly HashSet<string> PrimitiveNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "anyerror", "anyframe", "anyopaque", "bool", "c_int", "c_long", "c_longdouble", "c_longlong", "c_char", "c_short",
            "c_uint", "c_ulong", "c_ulonglong", "c_ushort", "comptime_float", "comptime_int", "f128", "f16", "f32", "f64", "f80",
            "false", "isize", "noreturn", "null", "true", "type", "undefined", "usize", "void",
        };

        public static bool IsPrimitiveName(string n)
        {
            if (PrimitiveNames.Contains(n)) return true;
            if (n.Length < 2 || (n[0] != 'i' && n[0] != 'u')) return false;
            for (int i = 1; i < n.Length; i++) if (!char.IsDigit(n[i])) return false;
            return true;
        }

        readonly Dictionary<ZSym, IrGlobal> globalItems = new Dictionary<ZSym, IrGlobal>();

        void ResolveGlobal(ZSym s)
        {
            if (s.Resolved) return;
            var v = (ZVarDecl)s.Decl;
            if (s.Resolving)
            {
                ErrS("dependency loop detected", v.NameStart, v.NameEnd);
                s.Type = ZType.Err; s.Resolved = true;
                return;
            }
            s.Resolving = true;
            try
            {
                if (v.IsExtern) { NotSupported("extern and export variables are", v); s.Type = ZType.Err; return; }
                if (v.Init == null) { ErrA("variables must be initialized", v.NameStart, v.NameEnd); s.Type = ZType.Err; return; }
                // aliases: @import, namespaces, types, functions
                if (v.IsConst && v.Type == null)
                {
                    var alias = TryNamespaceValue(v.Init);
                    if (alias != null)
                    {
                        s.Kind = ZSymKind.Alias;
                        s.Type = alias.T;
                        s.Sig = alias.T.Sig;
                        aliasValues[s] = alias;
                        return;
                    }
                }
                ZType declT = v.Type != null ? ResolveType(v.Type) : null;
                var r = Whole(v.Init, declT);
                ZType t = declT ?? r.T;
                if (declT == null)
                {
                    if (!v.IsConst && (t.K == ZK.CInt || t.K == ZK.CFloat))
                        ErrS("variable of type " + Q(t) + " must be const or comptime", v.Init.Start, v.Init.End);
                    if (t.K == ZK.EnumLit) NotSupported("an untyped enum literal is", v.Init);
                    if (t.K == ZK.Null) ErrS("variable of type '@TypeOf(null)' must be const or comptime", v.Init.Start, v.Init.End);
                    if (t.K == ZK.Type || t.K == ZK.Namespace || t.K == ZK.Fn) { NotSupported("this declaration is", v); t = ZType.Err; }
                }
                var init = declT != null ? Coerce(r, declT, v.Init, v.IsConst) : CopyArray(r.Ir, t, v.Init, v.IsConst);
                if (!declsOnly && !t.IsErr && !r.T.IsErr) CheckComptimeInit(v.Init, init);
                bool undef = r.T.K == ZK.Undefined;
                // var grid: [3][3]i32 = undefined; still needs the lists; other undefined globals get no assignment (see ZigLanguage.Compile)
                if (undef && t.K == ZK.Array) { init = ArrayFill(t, null, v.Init); undef = false; }
                s.Type = t;
                if (v.IsConst && r.Known && (t.IsNumeric || t.K == ZK.Bool || t.IsString))
                {
                    s.HasValue = true; s.IntValue = r.IntVal; s.FloatValue = r.FloatVal; s.StrValue = r.StrVal;
                    if (t.K == ZK.Float && r.T.K == ZK.CInt) s.FloatValue = r.IntVal;
                }
                s.Ir = new IrSymbol(s.Name, IrSymbolKind.Global, t.Ir()) { IsConst = v.IsConst, DeclSpan = new Span(v.NameStart, v.NameEnd) };
                if (!declsOnly)
                {
                    if (undef || r.T.K == ZK.Undefined || t.K == ZK.Array) s.Ir.Tag = new ZigTag { Type = t, Undefined = r.T.K == ZK.Undefined };
                    var g = new IrGlobal { Symbol = s.Ir, Init = r.T.K == ZK.Undefined && undef ? null : init, Span = v.Span, NameSpan = new Span(v.NameStart, v.NameEnd), TypeExplicit = v.Type != null, IsPrivate = !v.IsPub };
                    if (g.Init != null && g.Init.Type.Kind == IrTypeKind.Unknown) g.Init.Type = t.Ir();
                    globalItems[s] = g;
                }
            }
            finally { s.Resolving = false; s.Resolved = true; resolvedGlobals.Add(s); }
        }

        /// <summary>Globals in the order their initializers were resolved (a global after the ones it reads).</summary>
        readonly List<ZSym> resolvedGlobals = new List<ZSym>();

        readonly Dictionary<ZSym, ZR> aliasValues = new Dictionary<ZSym, ZR>();

        void ResolveFn(ZSym s)
        {
            if (s.Resolved) return;
            s.Resolved = true;
            var f = (ZFnDecl)s.Decl;
            var sig = new ZFnSig { Decl = f, IsPub = f.IsPub };
            if (f.IsExport || f.IsExtern) NotSupported("export and extern functions are", new ZSpanNode(f.Start, f.NameEnd));
            if (f.IsInline) NotSupported("inline functions are", new ZSpanNode(f.Start, f.NameEnd));
            if (f.CallConv != null) NotSupported("callconv is", new ZSpanNode(f.Start, f.HeaderEnd));
            foreach (var p in f.Params)
            {
                ZType pt;
                if (p.IsComptime) { NotSupported("comptime parameters are", new ZParam2Node(p)); pt = ZType.Err; }
                else if (p.IsAnytype) { NotSupported("anytype parameters are", new ZParam2Node(p)); pt = ZType.Err; }
                else pt = ResolveType(p.Type);
                if (pt.K == ZK.Void) { }
                sig.Params.Add(pt);
                sig.ParamNames.Add(p.Name);
            }
            if (f.Ret is ZErrorUnionType eu) { NotSupported("named error sets are", eu.Error); sig.Ret = ResolveType(eu.Payload); sig.RetError = true; }
            else { sig.Ret = ResolveType(f.Ret); sig.RetError = f.RetBang; }
            s.Sig = sig;
            s.Type = new ZType { K = ZK.Fn, Sig = sig, Sym = s };
            s.Ir = new IrSymbol(s.Name, IrSymbolKind.Function, IrType.Func(sig.Ret.Ir())) { DeclSpan = new Span(f.NameStart, f.NameEnd), ParamTypes = sig.Params.Select(p => p.Ir()).ToList() };
        }

        sealed class ZSpanNode : ZNode { public ZSpanNode(int s, int e) { Start = s; End = e; } }

        // ================================================================== scopes
        void PushScope() { scopes.Add(new Dictionary<string, ZSym>(StringComparer.Ordinal)); scopeOrder.Add(new List<ZSym>()); }

        void PopScope()
        {
            var order = scopeOrder[scopeOrder.Count - 1];
            foreach (var s in order) CheckUsed(s);
            scopes.RemoveAt(scopes.Count - 1);
            scopeOrder.RemoveAt(scopeOrder.Count - 1);
        }

        readonly List<(Diagnostic, string)> usageErrors = new List<(Diagnostic, string)>();

        void CheckUsed(ZSym s)
        {
            if (s.Name == "_") return;
            if (!s.Used && !s.Discarded) { ErrA("unused " + s.IdCat, s.DeclStart, s.DeclEnd); usageErrors.Add((AstErrors[AstErrors.Count - 1], s.Name)); }
            else
            {
                if (s.Used && s.Discarded) ErrA("pointless discard of " + s.IdCat, s.DiscardStart, s.DiscardEnd);
                if (s.IdCat == "local variable" && !s.Mutated) { ErrA("local variable is never mutated", s.DeclStart, s.DeclEnd); usageErrors.Add((AstErrors[AstErrors.Count - 1], s.Name)); }
            }
        }

        /// <summary>Declares a local / parameter / capture (AstGen shadowing rules: no shadowing at all).</summary>
        ZSym Declare(string name, int start, int end, string idCat, ZType t, bool isVar, IrSymbolKind kind = IrSymbolKind.Local)
        {
            var s = new ZSym { Name = name, Kind = idCat == "function parameter" ? ZSymKind.Param : idCat == "capture" ? ZSymKind.Capture : ZSymKind.Local, Type = t, IsVar = isVar, DeclStart = start, DeclEnd = end, IdCat = idCat };
            s.Ir = new IrSymbol(name, kind, t?.Ir() ?? IrType.Unknown) { IsConst = !isVar, DeclSpan = new Span(start, end) };
            if (name == "_") return s;
            if (name.StartsWith("__j", StringComparison.Ordinal)) ErrS("identifiers starting with '__j' are reserved by JavaFarmer", start, end);
            if (IsPrimitiveName(name)) { ErrA("name shadows primitive " + Q(name), start, end); return s; }
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                if (scopes[i].TryGetValue(name, out var prev))
                {
                    if (i == scopes.Count - 1) ErrA("redeclaration of " + prev.IdCat + " " + Q(name), start, end);
                    else ErrA(idCat + " " + Q(name) + " shadows " + prev.IdCat + " from outer scope", start, end);
                    return s;
                }
            }
            if (container.ContainsKey(name)) { ErrA(idCat + " " + Q(name) + " shadows declaration of " + Q(name), start, end); return s; }
            scopes[scopes.Count - 1][name] = s;
            scopeOrder[scopeOrder.Count - 1].Add(s);
            return s;
        }

        ZSym Lookup(string name)
        {
            for (int i = scopes.Count - 1; i >= 0; i--) if (scopes[i].TryGetValue(name, out var s)) return s;
            if (container.TryGetValue(name, out var g))
            {
                if (g.Kind == ZSymKind.Global && !g.Resolved) ResolveGlobal(g);
                if (g.Kind == ZSymKind.Fn && !g.Resolved) ResolveFn(g);
                return g;
            }
            return null;
        }

        // ================================================================== types
        /// <summary>Resolves a type expression; reports and returns Err when it is not a (supported) type.</summary>
        ZType ResolveType(ZExpr e)
        {
            if (e == null) return ZType.Err;
            switch (e)
            {
                case ZIdent id:
                    {
                        var t = PrimitiveType(id.Name);
                        if (t != null) return t;
                        if (id.Name == "anyerror" || id.Name.StartsWith("c_") || id.Name == "anyopaque" || id.Name == "f16" || id.Name == "f80" || id.Name == "f128" || id.Name == "comptime_int" || id.Name == "comptime_float")
                        {
                            NotSupported("type " + Q(id.Name) + " is", e);
                            return ZType.Err;
                        }
                        break;
                    }
                case ZOptionalType o:
                    {
                        var c = ResolveType(o.Child);
                        if (c.K == ZK.Optional) return c;
                        return c.IsErr ? c : ZType.Opt(c);
                    }
                case ZSliceType st:
                    {
                        if (st.Sentinel) { NotSupported("sentinel-terminated slices are", e); return ZType.Err; }
                        var c = ResolveType(st.Child);
                        return c.IsErr ? c : ZType.SliceOf(c, st.Const);
                    }
                case ZArrayType at:
                    {
                        if (at.Sentinel) { NotSupported("sentinel-terminated arrays are", e); return ZType.Err; }
                        var c = ResolveType(at.Child);
                        if (at.Inferred) { ErrS("unable to infer array size", e); return ZType.Err; }
                        var n = Expr(at.Len, ZType.Usize);
                        if (n.T.IsErr) return ZType.Err;
                        if (!n.Known || !n.T.IsIntLike) { ErrS("unable to resolve comptime value", at.Len); return ZType.Err; }
                        if (n.IntVal < 0) { ErrS("type 'usize' cannot represent integer value " + Q(n.IntVal), at.Len); return ZType.Err; }
                        return c.IsErr ? c : ZType.ArrayOf(c, n.IntVal);
                    }
                case ZPointerType pt:
                    {
                        if (pt.Many) { NotSupported("many-item pointers are", e); return ZType.Err; }
                        if (pt.Child is ZFnType) { NotSupported("function pointers are", e); return ZType.Err; }
                        var c = ResolveType(pt.Child);
                        if (c.IsErr) return c;
                        if (c.K == ZK.List || c.K == ZK.Map || c.K == ZK.Array) return new ZType { K = ZK.Ptr, Elem = c, Const = pt.Const };
                        NotSupported("pointers to " + Q(c) + " are", e);
                        return ZType.Err;
                    }
                case ZTupleType tt:
                    {
                        var items = tt.Fields.Select(ResolveType).ToList();
                        if (items.Any(i => i.IsErr)) return ZType.Err;
                        return ZType.TupleT(items);
                    }
                case ZErrorUnionType _:
                    NotSupported("error union types are only supported as return types (!T) and", e);
                    return ZType.Err;
                case ZFnType _:
                    NotSupported("function types are", e);
                    return ZType.Err;
                case ZUnsupportedExpr u:
                    NotSupported(u.What, e);
                    return ZType.Err;
                case ZGrouped g:
                    return ResolveType(g.Inner);
            }
            var r = Expr(e, ZType.TypeOfType);
            if (r.T.IsErr) return ZType.Err;
            if (r.T.K == ZK.Type) return r.T.Inner;
            ErrS("expected type 'type', found " + Q(r.T), e);
            return ZType.Err;
        }

        static ZType PrimitiveType(string n)
        {
            switch (n)
            {
                case "bool": return ZType.Bool;
                case "void": return ZType.Void;
                case "noreturn": return ZType.NoReturn;
                case "f64": return ZType.F64;
                case "f32": return ZType.F32;
                case "type": return ZType.TypeOfType;
            }
            var it = ZType.IntT(n);
            if (it != null && it.Bits > 0 && it.Bits <= 128) return it;
            return null;
        }

        // ================================================================== coercion
        static bool IntFits(ZType t, long v)
        {
            if (t.Bits >= 64) return t.Signed || v >= 0;
            if (t.Signed) { long lim = 1L << (t.Bits - 1); return v >= -lim && v < lim; }
            return v >= 0 && (t.Bits >= 63 || v < (1L << t.Bits));
        }

        static bool IntCoerces(ZType from, ZType to)
        {
            if (from.Name == to.Name) return true;
            if (from.Signed == to.Signed) return to.Bits >= from.Bits && !(from.Name == "usize" && to.Name != "usize" && to.Bits < 64) && !(from.Name == "isize" && to.Bits < 64);
            if (!from.Signed && to.Signed) return to.Bits > from.Bits;
            return false;
        }

        static int Precision(ZType t) => t.Signed ? t.Bits - 1 : t.Bits;
        static int Mantissa(ZType f) => f.Bits == 32 ? 24 : 53;

        /// <summary>Can a value of type 'from' (comptime value r) coerce to 'to'? Returns the error message or null.</summary>
        string CoerceError(ZR r, ZType to)
        {
            var from = r.T;
            if (from.IsErr || to.IsErr) return null;
            if (from.K == ZK.Foreign) return null;   // a value of a Python / JS / R file takes the type it is used as
            if (to.K == ZK.Type && (from.K == ZK.Type || from.K == ZK.Namespace)) return null;
            if (from.Same(to)) return null;
            if (from.K == ZK.Undefined || from.K == ZK.NoReturn) return null;
            switch (to.K)
            {
                case ZK.Optional:
                    if (from.K == ZK.Null) return null;
                    if (from.K == ZK.Optional) return from.Elem.Same(to.Elem) ? null : Mismatch(from, to);
                    return CoerceError(r, to.Elem) == null ? null : Mismatch(from, to);
                case ZK.ErrorUnion:
                    return CoerceError(r, to.Elem) == null ? null : Mismatch(from, to);
                case ZK.Int:
                    if (from.K == ZK.CInt)
                    {
                        if (r.Known && !IntFits(to, r.IntVal)) return "type " + Q(to) + " cannot represent integer value " + Q(r.IntVal);
                        return null;
                    }
                    if (from.K == ZK.CFloat)
                    {
                        if (r.Known && r.FloatVal != Math.Floor(r.FloatVal)) return "fractional component prevents float value " + Q(FloatText(r.FloatVal)) + " from coercion to type " + Q(to);
                        return null;
                    }
                    if (from.K == ZK.Int && IntCoerces(from, to)) return null;
                    return Mismatch(from, to);
                case ZK.Float:
                    if (from.K == ZK.CInt || from.K == ZK.CFloat) return null;
                    if (from.K == ZK.Float && to.Bits >= from.Bits) return null;
                    if (from.K == ZK.Int && Precision(from) <= Mantissa(to)) return null;
                    return Mismatch(from, to);
                case ZK.CInt:
                    return from.K == ZK.CInt ? null : Mismatch(from, to);
                case ZK.Slice:
                    if (to.IsString && from.K == ZK.StrLit) return null;
                    if (from.K == ZK.Ptr && from.Elem.K == ZK.Array && from.Elem.Elem.Same(to.Elem) && (to.Const || !from.Const)) return null;
                    if (from.K == ZK.Slice && from.Elem.Same(to.Elem) && to.Const) return null;
                    if (from.K == ZK.Tuple && from.Items.Count == 0) return null;
                    return Mismatch(from, to);
                case ZK.Game:
                    if (from.K == ZK.EnumLit) return null;
                    return Mismatch(from, to);
                case ZK.Ptr:
                    if (from.K == ZK.Ptr && from.Elem.Same(to.Elem) && (to.Const || !from.Const)) return null;
                    return Mismatch(from, to);
                case ZK.Tuple:
                    if (from.K == ZK.Tuple && from.Items.Count == to.Items.Count) return null;
                    return Mismatch(from, to);
                case ZK.Void:
                    return Mismatch(from, to);
            }
            return Mismatch(from, to);
        }

        static string Mismatch(ZType from, ZType to) => "expected type " + Q(to) + ", found " + Q(from);

        public static string FloatText(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Coerces (reports at n); returns the IR with its type adjusted to the target.</summary>
        IrExpr Coerce(ZR r, ZType to, ZNode n, bool destConst = false)
        {
            if (r == null) return null;
            if (to == null) return r.Ir;
            var msg = CoerceError(r, to);
            if (msg != null) { ErrS(msg, n); return r.Ir; }
            var ir = r.Ir;
            if (ir == null) return null;
            if ((r.T.K == ZK.CInt || r.T.K == ZK.Int) && to.IsFloatLike) ir.Type = IrType.Float;
            if (to.K == ZK.Optional && r.T.K != ZK.Optional && r.T.K != ZK.Null && ir.Type.IsKnown) ir.Type = ir.Type.AsNullable();
            if (r.T.K == ZK.EnumLit && to.K == ZK.Game) return ConstFor(r, to, n);
            if (r.T.K == ZK.EnumLit && to.K == ZK.Optional && to.Elem.K == ZK.Game) return ConstFor(r, to.Elem, n);
            if (ir is IrListLit ll && to.K == ZK.Array && ll.Items.Count != to.Len && r.T.K != ZK.Undefined)
                ErrS("expected " + to.Len + " array elements; found " + ll.Items.Count, n);
            // arrays are values: the destination gets a copy (list(a)), not the same Python list
            if (r.T.K == ZK.Array) ir = CopyArray(ir, to, n, destConst);
            return ir;
        }

        IrExpr ConstFor(ZR r, ZType enumT, ZNode n)
        {
            var cat = GameApi.CategoryOfGameType(enumT.Name);
            var c = new IrConst(cat, r.T.Name) { Span = r.Ir?.Span ?? n.Span };
            if (r.Ir != null) { c.Leading = r.Ir.Leading; c.ParenSpan = r.Ir.ParenSpan; }
            return c;
        }

        /// <summary>Peer type resolution for binary operators / if-else expressions; null when the types are incompatible.</summary>
        static ZType Peer(ZR a, ZR b)
        {
            var x = a.T; var y = b.T;
            if (x.IsErr || y.IsErr) return ZType.Err;
            if (x.K == ZK.Foreign && y.K != ZK.Foreign) return y.K == ZK.CInt ? ZType.I32 : y.K == ZK.CFloat ? ZType.F64 : y;
            if (y.K == ZK.Foreign && x.K != ZK.Foreign) return x.K == ZK.CInt ? ZType.I32 : x.K == ZK.CFloat ? ZType.F64 : x;
            if (x.Same(y)) return x;
            if (x.K == ZK.NoReturn) return y;
            if (y.K == ZK.NoReturn) return x;
            if (x.K == ZK.CInt && y.K == ZK.Int) return y;
            if (y.K == ZK.CInt && x.K == ZK.Int) return x;
            if (x.K == ZK.CInt && y.K == ZK.CFloat || x.K == ZK.CFloat && y.K == ZK.CInt) return ZType.CFloat;
            if (x.IsComptimeNum && y.K == ZK.Float) return y;
            if (y.IsComptimeNum && x.K == ZK.Float) return x;
            if (x.K == ZK.CFloat && y.K == ZK.Int) return y;   // reported by coercion (fractional values)
            if (y.K == ZK.CFloat && x.K == ZK.Int) return x;
            if (x.K == ZK.Int && y.K == ZK.Int)
            {
                if (IntCoerces(x, y)) return y;
                if (IntCoerces(y, x)) return x;
                return null;
            }
            if (x.K == ZK.Int && y.K == ZK.Float) return Precision(x) <= Mantissa(y) ? y : null;
            if (y.K == ZK.Int && x.K == ZK.Float) return Precision(y) <= Mantissa(x) ? x : null;
            if (x.K == ZK.Float && y.K == ZK.Float) return x.Bits >= y.Bits ? x : y;
            if (x.K == ZK.Null && y.K == ZK.Optional) return y;
            if (y.K == ZK.Null && x.K == ZK.Optional) return x;
            if (x.K == ZK.Null) return ZType.Opt(y);
            if (y.K == ZK.Null) return ZType.Opt(x);
            if (x.K == ZK.Optional && x.Elem.Same(y)) return x;
            if (y.K == ZK.Optional && y.Elem.Same(x)) return y;
            if (x.K == ZK.EnumLit && y.K == ZK.Game) return y;
            if (y.K == ZK.EnumLit && x.K == ZK.Game) return x;
            if (x.K == ZK.EnumLit && y.K == ZK.Optional && y.Elem.K == ZK.Game) return y;
            if (y.K == ZK.EnumLit && x.K == ZK.Optional && x.Elem.K == ZK.Game) return x;
            if (x.IsString && y.IsString) return ZType.Str;
            if (x.K == ZK.Undefined) return y;
            if (y.K == ZK.Undefined) return x;
            return null;
        }
    }
}
