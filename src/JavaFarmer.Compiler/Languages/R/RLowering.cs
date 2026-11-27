using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// R syntax tree → Farm IR. R scoping: a name assigned with &lt;- / = inside a function is a local of that function
    /// from its first assignment on (textual order: a read before it refers to the enclosing / global variable, so
    /// "x &lt;- x + 1" makes a local x from the global x); &lt;&lt;- assigns the global. Top-level "f &lt;- function(..)" →
    /// IrFunction; every other function expression → IrLambda (closure). Top-level names that functions read or assign
    /// become Global symbols (IrGlobal at their first plain top-level assignment), other top-level names are script
    /// locals. Vectors / lists are 1-based (IrIndex.BaseOffset 1); named lists are maps (decided by a usage pre-pass).
    /// R's value semantics are approximated with Synthetic copies (see docs/languages/r.md).
    /// </summary>
    internal sealed partial class RlLowering
    {
        public const string Tag = "r";

        sealed class Abort : Exception { }

        readonly string src;
        readonly string modName;
        readonly CompileOptions options;
        readonly List<Diagnostic> errors;
        readonly IrModule mod;

        RlLowering(string src, string name, CompileOptions options, List<Diagnostic> errors)
        {
            this.src = src;
            modName = name;
            this.options = options;
            this.errors = errors;
            mod = new IrModule { Name = name, SourceLanguage = "r" };
        }

        void Fail(string msg, Span s)
        {
            errors.Add(new Diagnostic(msg, Math.Max(0, s.Start), Math.Max(0, s.End)));
            throw new Abort();
        }

        string Text(Span s) => s.IsNone || s.End > src.Length || s.Start < 0 || s.End < s.Start ? "" : src.Substring(s.Start, s.End - s.Start);

        string CallText(RlCall c)
        {
            var t = Text(c.Span).Replace("\n", " ").Replace("\r", "");
            if (t.Length > 40) t = (c.FnName ?? "f") + "(...)";
            return t;
        }

        static RlExpr Unparen(RlExpr e)
        {
            while (e is RlParen p) e = p.Inner;
            return e;
        }


        // ================================================================== analysis
        sealed class FnInfo
        {
            public int Id;
            public RlFunction Node;
            public FnInfo Parent;
            public readonly HashSet<string> Params = new HashSet<string>();
            public readonly HashSet<string> Assigned = new HashSet<string>();
            public bool Has(string n) => Params.Contains(n) || Assigned.Contains(n);
        }

        readonly Dictionary<RlFunction, FnInfo> fns = new Dictionary<RlFunction, FnInfo>();
        readonly HashSet<string> topAssigned = new HashSet<string>();
        readonly HashSet<string> superAssigned = new HashSet<string>();
        readonly Dictionary<string, int> topAssignCount = new Dictionary<string, int>();
        readonly Dictionary<string, RlAssign> topFnDefs = new Dictionary<string, RlAssign>();
        /// <summary>Names some assignment gives a function value (calls of them are calls of that value).</summary>
        readonly HashSet<string> fnValued = new HashSet<string>();
        readonly List<(string Name, FnInfo Fn, Span At)> superTargets = new List<(string, FnInfo, Span)>();
        readonly List<(string Module, Span At)> sourced = new List<(string, Span)>();

        /// <summary>Appends the children of e to buf (no allocation; the walkers use one shared stack).</summary>
        static void KidsInto(RlExpr e, List<RlExpr> buf)
        {
            switch (e)
            {
                case RlParen p: buf.Add(p.Inner); break;
                case RlCall c: buf.Add(c.Fn); foreach (var a in c.Args) if (a.Value != null) buf.Add(a.Value); break;
                case RlIndex ix: buf.Add(ix.Target); foreach (var a in ix.Args) if (a.Value != null) buf.Add(a.Value); break;
                case RlDollar d: buf.Add(d.Target); break;
                case RlBinary b: buf.Add(b.Left); buf.Add(b.Right); break;
                case RlUnary u: buf.Add(u.Operand); break;
                case RlAssign a: buf.Add(a.Target); buf.Add(a.Value); break;
                case RlFunction f: foreach (var p in f.Params) if (p.Default != null) buf.Add(p.Default); buf.Add(f.Body); break;
                case RlBlock b: buf.AddRange(b.Exprs); break;
                case RlIf i: buf.Add(i.Cond); buf.Add(i.Then); if (i.Else != null) buf.Add(i.Else); break;
                case RlFor f: buf.Add(f.Seq); buf.Add(f.Body); break;
                case RlWhile w: buf.Add(w.Cond); buf.Add(w.Body); break;
                case RlRepeat r: buf.Add(r.Body); break;
            }
        }

        readonly List<RlExpr> kidBuf = new List<RlExpr>();

        /// <summary>Calls visit for each child of e, using the shared stack.</summary>
        void EachKid<T>(RlExpr e, T arg, Action<RlExpr, T> visit)
        {
            int s0 = kidBuf.Count;
            KidsInto(e, kidBuf);
            int e0 = kidBuf.Count;
            for (int i = s0; i < e0; i++) visit(kidBuf[i], arg);
            kidBuf.RemoveRange(s0, e0 - s0);
        }

        static IEnumerable<RlExpr> Kids(RlExpr e)
        {
            switch (e)
            {
                case RlParen p: yield return p.Inner; break;
                case RlCall c: yield return c.Fn; foreach (var a in c.Args) if (a.Value != null) yield return a.Value; break;
                case RlIndex ix: yield return ix.Target; foreach (var a in ix.Args) if (a.Value != null) yield return a.Value; break;
                case RlDollar d: yield return d.Target; break;
                case RlBinary b: yield return b.Left; yield return b.Right; break;
                case RlUnary u: yield return u.Operand; break;
                case RlAssign a: yield return a.Target; yield return a.Value; break;
                case RlFunction f: foreach (var p in f.Params) if (p.Default != null) yield return p.Default; yield return f.Body; break;
                case RlBlock b: foreach (var x in b.Exprs) yield return x; break;
                case RlIf i: yield return i.Cond; yield return i.Then; if (i.Else != null) yield return i.Else; break;
                case RlFor f: yield return f.Seq; yield return f.Body; break;
                case RlWhile w: yield return w.Cond; yield return w.Body; break;
                case RlRepeat r: yield return r.Body; break;
            }
        }

        static RlIdent RootName(RlExpr e)
        {
            e = Unparen(e);
            while (true)
            {
                switch (e)
                {
                    case RlIdent id: return id;
                    case RlIndex ix: e = Unparen(ix.Target); continue;
                    case RlDollar d: e = Unparen(d.Target); continue;
                }
                return null;
            }
        }

        int fnIds;

        void Analyze(RlProgram prog)
        {
            // module level: bindings outside functions; functions anywhere
            void Top(RlExpr e, bool direct)
            {
                e = Unparen(e);
                switch (e)
                {
                    case RlAssign a:
                        {
                            var t = Unparen(a.Target);
                            var val = Unparen(a.Value);
                            if (t is RlIdent id)
                            {
                                topAssigned.Add(id.Name);
                                topAssignCount[id.Name] = topAssignCount.TryGetValue(id.Name, out var k) ? k + 1 : 1;
                                if (direct && val is RlFunction && !topFnDefs.ContainsKey(id.Name)) topFnDefs[id.Name] = a;
                                if (val is RlFunction) fnValued.Add(id.Name);
                            }
                            if (val is RlAssign) Top(val, false);   // chained x <- y <- 3
                            else Walk(a.Value, null);
                            if (!(t is RlIdent)) Walk(a.Target, null);
                            return;
                        }
                    case RlFor f:
                        topAssigned.Add(f.Var);
                        Walk(f.Seq, null);
                        Top(f.Body, false);
                        return;
                    case RlIf i: Walk(i.Cond, null); Top(i.Then, false); if (i.Else != null) Top(i.Else, false); return;
                    case RlWhile w: Walk(w.Cond, null); Top(w.Body, false); return;
                    case RlRepeat r: Top(r.Body, false); return;
                    case RlBlock b: foreach (var x in b.Exprs) Top(x, false); return;
                    case RlCall c when c.FnName == "source" && c.Args.Count >= 1 && Unparen(c.Args[0].Value) is RlStr s:
                        sourced.Add((ModuleOfFile(s.Value), s.Span));
                        return;
                }
                Walk(e, null);
            }
            foreach (var e in prog.Exprs) Top(e, true);
            foreach (var st in superTargets)
            {
                for (var f = st.Fn.Parent; f != null; f = f.Parent)
                    if (f.Has(st.Name))
                        Fail("assigning a variable of an enclosing function with <<- is not supported in JavaFarmer (only top-level variables); return the value instead", st.At);
                superAssigned.Add(st.Name);
            }
            foreach (var kv in topFnDefs.ToList())
                if (topAssignCount[kv.Key] != 1 || superAssigned.Contains(kv.Key)) topFnDefs.Remove(kv.Key);
            Kinds(prog);
            LoadImports();
        }

        /// <summary>Finds functions (any depth) and records their parameters and local bindings.</summary>
        void Walk(RlExpr e, FnInfo fn)
        {
            if (e == null) return;
            switch (e)
            {
                case RlFunction f:
                    {
                        var fi = new FnInfo { Id = ++fnIds, Node = f, Parent = fn };
                        foreach (var p in f.Params) fi.Params.Add(p.Name);
                        fns[f] = fi;
                        foreach (var p in f.Params) Walk(p.Default, fn);
                        Body(f.Body, fi);
                        return;
                    }
            }
            EachKid(e, fn, walkFn ?? (walkFn = Walk));
        }

        Action<RlExpr, FnInfo> walkFn, bodyFn;

        void Body(RlExpr e, FnInfo fi)
        {
            if (e == null) return;
            e = Unparen(e);
            switch (e)
            {
                case RlAssign a:
                    {
                        var t = Unparen(a.Target);
                        var root = RootName(t);
                        if (root != null)
                        {
                            if (a.Super) { if (t is RlIdent) superTargets.Add((root.Name, fi, a.OpSpan)); }
                            else fi.Assigned.Add(root.Name);
                            if (t is RlIdent && Unparen(a.Value) is RlFunction) fnValued.Add(root.Name);
                        }
                        Body(a.Value, fi);
                        if (!(t is RlIdent)) EachKid(t, fi, bodyFn ?? (bodyFn = Body));
                        return;
                    }
                case RlFor f:
                    fi.Assigned.Add(f.Var);
                    Body(f.Seq, fi);
                    Body(f.Body, fi);
                    return;
                case RlFunction nf:
                    Walk(nf, fi);
                    return;
            }
            EachKid(e, fi, bodyFn ?? (bodyFn = Body));
        }

        // ================================================================== usage kinds (list vs map, text vs number)
        [Flags] enum K { None = 0, Map = 1, List = 2, Str = 4, Num = 8 }

        readonly Dictionary<(int, string), K> kinds = new Dictionary<(int, string), K>();
        bool kindsChanged;

        static FnInfo Owner(string name, FnInfo fn)
        {
            for (var f = fn; f != null; f = f.Parent) if (f.Has(name)) return f;
            return null;
        }

        static (int, string) KeyOf(string name, FnInfo fn) { var o = Owner(name, fn); return (o == null ? 0 : o.Id, name); }

        K KindOfName(string name, FnInfo fn) => kinds.TryGetValue(KeyOf(name, fn), out var k) ? k : K.None;

        void Note(string name, FnInfo fn, K k)
        {
            if (k == K.None) return;
            var key = KeyOf(name, fn);
            var old = kinds.TryGetValue(key, out var o) ? o : K.None;
            if ((old | k) == old) return;
            kinds[key] = old | k;
            kindsChanged = true;
        }

        static bool IsCategory(string n) => Array.IndexOf(GameApi.ConstantCategories, n) >= 0;
        static bool IsDirection(string n) => Array.IndexOf(GameApi.DirectionNames, n) >= 0;

        K KindOf(RlExpr e, FnInfo fn)
        {
            e = Unparen(e);
            switch (e)
            {
                case RlStr _: return K.Str;
                case RlNum _: return K.Num;
                case RlIdent id:
                    if (IsDirection(id.Name) && Owner(id.Name, fn) == null && !topAssigned.Contains(id.Name)) return K.Str;
                    return KindOfName(id.Name, fn);
                case RlDollar d when Unparen(d.Target) is RlIdent cat && IsCategory(cat.Name) && Owner(cat.Name, fn) == null && !topAssigned.Contains(cat.Name):
                    return K.Str;
                case RlIndex ix when Unparen(ix.Target) is RlIdent cat2 && IsCategory(cat2.Name) && Owner(cat2.Name, fn) == null && !topAssigned.Contains(cat2.Name):
                    return K.Str;
                case RlUnary u when u.Op == "-" || u.Op == "+": return K.Num;
                case RlAssign ia when Unparen(ia.Target) is RlIdent iat: return KindOf(ia.Value, fn) | KindOfName(iat.Name, fn);
                case RlBinary b:
                    switch (b.Op)
                    {
                        case "+": case "-": case "*": case "/": case "^": case "%%": case "%/%": return K.Num;
                    }
                    return K.None;
                case RlCall c:
                    {
                        var n = c.FnName;
                        if (n == null) return K.None;
                        if (Owner(n, fn) != null || topFnDefs.ContainsKey(n)) return K.None;
                        switch (n)
                        {
                            case "paste": case "paste0": case "as.character": case "toString": case "sprintf": return K.Str;
                            case "length": case "nchar": case "floor": case "ceiling": case "sqrt": case "trunc": case "as.integer":
                            case "as.numeric": case "as.double": case "match": return K.Num;
                            case "c": case "list":
                                if (c.Args.Count == 0) return K.None;
                                if (c.Args.All(a => a.Name != null)) return K.Map;
                                return K.List;
                            case "setNames": return K.Map;
                            case "rep": return K.List;
                        }
                        var b = GameApi.Find(n);
                        if (b != null)
                        {
                            var rt = b.ReturnType ?? IrType.Unknown;
                            if (rt.Kind == IrTypeKind.Game || rt.Kind == IrTypeKind.String) return K.Str;
                            if (rt.IsNumeric) return K.Num;
                        }
                        return K.None;
                    }
            }
            return K.None;
        }

        static bool IsLengthPlusOne(RlExpr e, string name)
        {
            e = Unparen(e);
            return e is RlBinary b && b.Op == "+" && Unparen(b.Left) is RlCall c && c.FnName == "length" && c.Args.Count == 1
                   && Unparen(c.Args[0].Value) is RlIdent id && id.Name == name && Unparen(b.Right) is RlNum one && one.Value == 1;
        }

        void Kinds(RlProgram prog)
        {
            // round 1 walks the tree and keeps the nodes that tell something; later rounds (usage flows between names,
            // b <- a) only revisit those nodes, in the same order, until nothing changes (at most 3 rounds)
            var useful = new List<(RlExpr Node, FnInfo Fn)>();
            Action<RlExpr, FnInfo> wd = null;
            void W(RlExpr e, FnInfo fn)
            {
                if (e == null) return;
                e = Unparen(e);
                if (e is RlFunction f)
                {
                    var fi = fns.TryGetValue(f, out var x) ? x : fn;
                    foreach (var p in f.Params) W(p.Default, fn);
                    W(f.Body, fi);
                    return;
                }
                if (KindNode(e, fn)) useful.Add((e, fn));
                EachKid(e, fn, wd);
            }
            wd = W;
            kindsChanged = false;
            foreach (var e in prog.Exprs) W(e, null);
            for (int round = 1; round < 3 && kindsChanged; round++)
            {
                kindsChanged = false;
                foreach (var (node, fn) in useful) KindNode(node, fn);
            }
        }

        /// <summary>Notes what e tells about the kinds of names; false when e can never tell anything.</summary>
        bool KindNode(RlExpr e, FnInfo fn)
        {
            switch (e)
            {
                case RlAssign a:
                    if (Unparen(a.Target) is RlIdent t)
                    {
                        var vk = KindOf(a.Value, fn);
                        if (Unparen(a.Value) is RlIdent vi) vk |= KindOfName(vi.Name, fn);
                        Note(t.Name, fn, vk);
                        return true;
                    }
                    return false;
                case RlDollar d:
                    if (Unparen(d.Target) is RlIdent dt && !(IsCategory(dt.Name) && Owner(dt.Name, fn) == null && !topAssigned.Contains(dt.Name)))
                    {
                        Note(dt.Name, fn, K.Map);
                        return true;
                    }
                    return false;
                case RlIndex ix:
                    if (Unparen(ix.Target) is RlIdent it && ix.Args.Count == 1 && ix.Args[0].Value != null)
                    {
                        var a0 = ix.Args[0].Value;
                        if (IsLengthPlusOne(a0, it.Name)) Note(it.Name, fn, K.List);
                        else
                        {
                            var ak = KindOf(a0, fn);
                            if ((ak & K.Str) != 0) Note(it.Name, fn, K.Map);
                            else if ((ak & K.Num) != 0) Note(it.Name, fn, K.List);
                        }
                        return true;
                    }
                    return false;
                case RlCall c:
                    if (c.FnName == "names" && c.Args.Count == 1 && Unparen(c.Args[0].Value) is RlIdent nm) { Note(nm.Name, fn, K.Map); return true; }
                    return false;
                case RlFor f:
                    {
                        var seq = Unparen(f.Seq);
                        if (seq is RlCall sc && sc.FnName == "names") Note(f.Var, fn, K.Str);
                        else if ((seq is RlBinary sb && (sb.Op == ":" || sb.Op == "-")) || (seq is RlCall sc2 && (sc2.FnName == "seq_len" || sc2.FnName == "seq_along" || sc2.FnName == "seq")))
                            Note(f.Var, fn, K.Num);
                        return true;
                    }
            }
            return false;
        }

        /// <summary>True when the index of target[[index]] is a map key (decided from usage).</summary>
        bool IsMapIndex(RlExpr target, RlExpr index, IrExpr loweredIndex, Scope sc)
        {
            var fn = sc.Fn;
            switch (loweredIndex)
            {
                case IrConst _: return true;
                case IrLiteral l when l.Kind == IrLitKind.String: return true;
                case IrLiteral l2 when l2.Kind == IrLitKind.Int || l2.Kind == IrLitKind.Float: return false;
                case IrCast c when c.Target != null && c.Target.IsString: return true;
                case IrBinary b when b.Op == IrBinOp.Concat: return true;
            }
            var ik = KindOf(index, fn);
            if ((ik & K.Str) != 0 && (ik & K.Num) == 0) return true;
            if ((ik & K.Num) != 0 && (ik & K.Str) == 0) return false;
            if (Unparen(target) is RlIdent t)
            {
                var tk = KindOfName(t.Name, fn);
                if ((tk & K.Map) != 0 && (tk & K.List) == 0) return true;
            }
            return false;
        }

        bool IsMapValue(RlExpr target, Scope sc)
        {
            if (Unparen(target) is RlIdent t)
            {
                var tk = KindOfName(t.Name, sc.Fn);
                return (tk & K.Map) != 0 && (tk & K.List) == 0;
            }
            return false;
        }

        // ================================================================== other files (source("x.R"))
        internal sealed class ModuleDecls
        {
            public readonly Dictionary<string, List<RlParam>> Functions = new Dictionary<string, List<RlParam>>();
            public readonly HashSet<string> Vars = new HashSet<string>();
            public Dictionary<string, string> FnPy, VarPy;
            /// <summary>Foreign files (other languages): neutral types from their signatures (null for R files).</summary>
            public Dictionary<string, FunctionSignature> FnSigs;
            public Dictionary<string, IrType> VarTypes;
        }

        /// <summary>The declarations of a file in another language (ForeignModules signature) as R sees them.</summary>
        static ModuleDecls DeclsOf(ModuleSignature sig)
        {
            var d = new ModuleDecls
            {
                FnPy = new Dictionary<string, string>(), VarPy = new Dictionary<string, string>(),
                FnSigs = new Dictionary<string, FunctionSignature>(), VarTypes = new Dictionary<string, IrType>(),
            };
            foreach (var f in sig.Functions)
            {
                if (string.IsNullOrEmpty(f.Name) || d.Functions.ContainsKey(f.Name)) continue;
                var ps = new List<RlParam>();
                foreach (var p in f.Params)
                    ps.Add(new RlParam { Name = p.Name ?? "x", Default = p.Optional ? new RlConst { Kind = RlConstKind.Null, Text = "NULL" } : null });
                d.Functions[f.Name] = ps;
                d.FnSigs[f.Name] = f;
                d.FnPy[IrN(f.Name)] = f.PyName ?? PyNaming.Ident(IrN(f.Name));
            }
            foreach (var g in sig.Globals)
            {
                if (string.IsNullOrEmpty(g.Name) || d.Functions.ContainsKey(g.Name) || !d.Vars.Add(g.Name)) continue;
                d.VarTypes[g.Name] = g.Type ?? IrType.Unknown;
                d.VarPy[IrN(g.Name)] = g.PyName ?? PyNaming.Ident(IrN(g.Name));
            }
            return d;
        }

        static readonly Dictionary<string, ModuleDecls> declCache = new Dictionary<string, ModuleDecls>();
        static readonly object declGate = new object();

        internal static ModuleDecls DeclsOf(string source)
        {
            source = source ?? "";
            lock (declGate) if (declCache.TryGetValue(source, out var d0)) return d0;
            var d = new ModuleDecls();
            try
            {
                var prog = RlParser.Parse(source);
                var counts = new Dictionary<string, int>();
                void Top(RlExpr e, bool direct)
                {
                    e = Unparen(e);
                    switch (e)
                    {
                        case RlAssign a when Unparen(a.Target) is RlIdent id:
                            counts[id.Name] = counts.TryGetValue(id.Name, out var k) ? k + 1 : 1;
                            if (direct && Unparen(a.Value) is RlFunction f && !d.Functions.ContainsKey(id.Name)) d.Functions[id.Name] = f.Params;
                            else d.Vars.Add(id.Name);
                            return;
                        case RlFor f2: d.Vars.Add(f2.Var); Top(f2.Body, false); return;
                        case RlIf i: Top(i.Then, false); if (i.Else != null) Top(i.Else, false); return;
                        case RlWhile w: Top(w.Body, false); return;
                        case RlRepeat r: Top(r.Body, false); return;
                        case RlBlock b: foreach (var x in b.Exprs) Top(x, false); return;
                    }
                }
                foreach (var e in prog.Exprs) Top(e, true);
                foreach (var kv in counts) if (kv.Value > 1 && d.Functions.ContainsKey(kv.Key)) { d.Functions.Remove(kv.Key); d.Vars.Add(kv.Key); }
            }
            catch (Exception) { /* the other window reports its own errors */ }
            d.FnPy = PyNaming.ModuleLevel(d.Functions.Keys.Select(IrN), d.Vars.Select(IrN), out var globals);
            d.VarPy = globals;
            lock (declGate)
            {
                if (declCache.Count > 64) declCache.Clear();
                declCache[source] = d;
            }
            return d;
        }

        static string ModuleOfFile(string file)
        {
            var f = (file ?? "").Replace('\\', '/');
            int slash = f.LastIndexOf('/');
            if (slash >= 0) f = f.Substring(slash + 1);
            int dot = f.LastIndexOf('.');
            if (dot > 0) f = f.Substring(0, dot);   // "utils.R", "Utils.java", "bot.py": the window name
            return f;
        }

        readonly List<(string Module, ModuleDecls Decls)> imports = new List<(string, ModuleDecls)>();
        bool lenientImports;

        void LoadImports()
        {
            foreach (var (m, at) in sourced)
            {
                if (imports.Any(x => x.Module == m)) continue;
                if (m == modName) Fail("a file cannot source() itself", at);
                if (options?.OtherFiles == null) { lenientImports = true; imports.Add((m, null)); continue; }
                if (!options.OtherFiles.TryGetValue(m, out var other))
                    Fail("Error in file(filename, \"r\"): cannot open file '" + Text(at).Trim('"', '\'') + "': No such file or directory (there is no code window named " + m + ")", at);
                if (ForeignModules.IsForeign(options, m, "r"))
                {
                    // another language: read it with its own frontend (never with the R parser)
                    var sig = ForeignModules.Resolve(m, options);
                    if (sig == null || !sig.Ok) Fail("cannot use " + m + ": " + (sig?.Error ?? m + " does not compile"), at);
                    imports.Add((m, DeclsOf(sig)));
                    continue;
                }
                imports.Add((m, DeclsOf(other)));
            }
        }

        readonly Dictionary<string, IrSymbol> externals = new Dictionary<string, IrSymbol>();

        IrSymbol External(string module, string name, bool function, ModuleDecls d)
        {
            var key = module + "|" + name + "|" + (function ? "f" : "g");
            if (externals.TryGetValue(key, out var s)) return s;
            var irn = IrN(name);
            string py = null;
            if (d != null) (function ? d.FnPy : d.VarPy)?.TryGetValue(irn, out py);
            s = new IrSymbol(irn, function ? IrSymbolKind.ExternalFunction : IrSymbolKind.ExternalGlobal) { Module = module, PyName = py ?? PyNaming.Ident(irn), Tag = d != null && function && d.Functions.TryGetValue(name, out var ps) ? (object)ps : null };
            // a file in another language: its signature's neutral types (Unknown / Any stay dynamic)
            if (function && d?.FnSigs != null && d.FnSigs.TryGetValue(name, out var fs))
            {
                s.Type = IrType.Func(fs.ReturnType ?? IrType.Unknown, fs.Params.Select(p => p.Type ?? IrType.Unknown).ToArray());
                s.ParamTypes = fs.Params.Select(p => p.Type ?? IrType.Unknown).ToList();
            }
            else if (!function && d?.VarTypes != null && d.VarTypes.TryGetValue(name, out var vt)) s.Type = vt;
            externals[key] = s;
            return s;
        }

        IrSymbol ResolveImport(string name, bool call)
        {
            foreach (var (m, d) in imports)
            {
                if (d == null) continue;
                if (d.Functions.ContainsKey(name)) return External(m, name, true, d);
                if (d.Vars.Contains(name)) return External(m, name, false, d);
            }
            if (lenientImports)
            {
                var first = imports.FirstOrDefault(x => x.Decls == null);
                if (first.Module != null) return External(first.Module, name, call, null);
            }
            return null;
        }

        // ================================================================== scopes & symbols
        sealed class Scope
        {
            public Scope Parent;
            public FnInfo Fn;                                   // null = top level
            public readonly Dictionary<string, IrSymbol> Syms = new Dictionary<string, IrSymbol>();
            public readonly HashSet<string> Defined = new HashSet<string>();
            public int LoopDepth;
            public bool IsTop => Fn == null;
        }

        readonly Scope top = new Scope();
        readonly Dictionary<string, IrSymbol> moduleVars = new Dictionary<string, IrSymbol>();
        readonly Dictionary<string, IrSymbol> topFnSyms = new Dictionary<string, IrSymbol>();
        readonly HashSet<string> topFnDefined = new HashSet<string>();
        readonly Dictionary<string, string> irNames = new Dictionary<string, string>();

        static string IrN(string n) => n.IndexOf('.') < 0 ? n : n.Replace('.', '_');

        /// <summary>Letters, digits and _ (after '.' → '_'), not starting with a digit: a name the game's Python accepts.</summary>
        static bool IsPlainName(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsDigit(s[0])) return false;
            foreach (var ch in s) if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
            return true;
        }

        void CheckName(string n, Span at)
        {
            if (n == "...") Fail("... (a variable number of arguments) is not supported in JavaFarmer", at);
            var irn = IrN(n);
            if (n.StartsWith("__j", StringComparison.Ordinal) || irn.StartsWith("__j", StringComparison.Ordinal))
                Fail("names starting with __j are reserved for JavaFarmer", at);
            if (!IsPlainName(irn))
                Fail("the name `" + n + "` is not supported in JavaFarmer; use letters, digits, _ and . only (starting with a letter)", at);
            if (irNames.TryGetValue(irn, out var other))
            {
                if (other != n) Fail("'" + n + "' and '" + other + "' are the same name in JavaFarmer (dots in names become underscores); rename one of them", at);
            }
            else irNames[irn] = n;
        }

        IrSymbol ModuleVar(string n)
        {
            if (moduleVars.TryGetValue(n, out var s)) return s;
            s = new IrSymbol(IrN(n), IrSymbolKind.Local) { Tag = Tag };
            moduleVars[n] = s;
            return s;
        }

        IrSymbol Local(Scope sc, string n)
        {
            if (sc.Syms.TryGetValue(n, out var s)) return s;
            s = new IrSymbol(IrN(n), IrSymbolKind.Local);
            sc.Syms[n] = s;
            return s;
        }

        /// <summary>The variable a read of n refers to (null: not a user variable / function).</summary>
        IrSymbol ResolveVar(string n, Scope sc, Span at, bool forCall)
        {
            for (var s = sc; s != null && !s.IsTop; s = s.Parent)
            {
                if (s == sc)
                {
                    if (s.Defined.Contains(n)) return Local(s, n);
                    continue;
                }
                if (s.Fn.Has(n)) { var l = Local(s, n); return l; }
            }
            // current function: a local assigned later in a loop
            IrSymbol outer = ResolveModule(n, sc, forCall);
            if (outer != null) return outer;
            if (!sc.IsTop && sc.Fn.Assigned.Contains(n) && sc.LoopDepth > 0) return Local(sc, n);
            return null;
        }

        IrSymbol ResolveModule(string n, Scope sc, bool forCall)
        {
            if (topFnSyms.TryGetValue(n, out var f))
            {
                if (sc.IsTop && !topFnDefined.Contains(n)) return null;
                return f;
            }
            if (topAssigned.Contains(n) || superAssigned.Contains(n))
            {
                if (sc.IsTop && !top.Defined.Contains(n) && !superAssigned.Contains(n) && !(sc.LoopDepth > 0))
                {
                    var imp = ResolveImport(n, forCall);
                    if (imp != null) return imp;
                    return null;
                }
                var v = ModuleVar(n);
                if (!sc.IsTop) v.Kind = IrSymbolKind.Global;
                return v;
            }
            return ResolveImport(n, forCall);
        }

        /// <summary>Symbol written by "n &lt;- v" (call after lowering v: textual order).</summary>
        IrSymbol WriteVar(string n, Scope sc, bool super, Span at)
        {
            CheckName(n, at);
            if (sc.IsTop || super)
            {
                var v = ModuleVar(n);
                if (!sc.IsTop) v.Kind = IrSymbolKind.Global;
                else top.Defined.Add(n);
                return v;
            }
            var l = Local(sc, n);
            sc.Defined.Add(n);
            return l;
        }

        Scope FnScope(RlFunction f, Scope parent, List<IrParam> outParams)
        {
            var fi = fns[f];
            var sc = new Scope { Parent = parent, Fn = fi };
            foreach (var p in f.Params)
            {
                if (p.Dots) Fail("... (a variable number of arguments) is not supported in JavaFarmer", p.Span);
                CheckName(p.Name, p.Span);
                if (sc.Syms.ContainsKey(p.Name)) Fail("Error: repeated formal argument '" + p.Name + "'", p.Span);
                var ps = new IrSymbol(IrN(p.Name), IrSymbolKind.Param) { DeclSpan = p.Span };
                sc.Syms[p.Name] = ps;
                sc.Defined.Add(p.Name);
                var ip = new IrParam { Symbol = ps, NameSpan = p.Span, Span = p.Default != null ? Span.Cover(p.Span, OuterSpan(p.Default)) : p.Span };
                if (p.Default != null)
                {
                    foreach (var id in Idents(p.Default))
                        if (f.Params.Any(q => q.Name == id.Name)) Fail("a default value that uses another parameter (" + id.Name + ") is not supported in JavaFarmer; use a constant default", id.Span);
                    ip.Default = Expr(p.Default, parent);
                }
                outParams.Add(ip);
            }
            return sc;
        }

        static IEnumerable<RlIdent> Idents(RlExpr e)
        {
            if (e is RlIdent id) yield return id;
            if (e is RlFunction) yield break;
            foreach (var k in Kids(e)) foreach (var x in Idents(k)) yield return x;
        }

        static Span OuterSpan(RlExpr e) => e.ParenSpan.IsNone ? e.Span : e.ParenSpan;

        // ================================================================== module
        readonly Dictionary<IrAssign, IrGlobal> convertedToGlobal = new Dictionary<IrAssign, IrGlobal>();

        bool IsTopFnDef(RlExpr e, out RlAssign a, out RlIdent name, out RlFunction fn)
        {
            a = e as RlAssign; name = null; fn = null;
            if (a == null) return false;
            name = Unparen(a.Target) as RlIdent;
            fn = Unparen(a.Value) as RlFunction;
            return name != null && fn != null && topFnDefs.TryGetValue(name.Name, out var def) && def == a;
        }

        void LowerModule(RlProgram prog)
        {
            foreach (var kv in topFnDefs)
            {
                CheckName(kv.Key, Unparen(kv.Value.Target).Span);
                topFnSyms[kv.Key] = new IrSymbol(IrN(kv.Key), IrSymbolKind.Function) { DeclSpan = Unparen(kv.Value.Target).Span, Tag = Unparen(kv.Value.Value) };
            }
            foreach (var e in prog.Exprs)
            {
                if (IsTopFnDef(e, out var a, out var name, out var fn))
                {
                    var sym = topFnSyms[name.Name];
                    var f = new IrFunction { Symbol = sym, Span = a.Span, HeaderSpan = fn.HeaderSpan, NameSpan = name.Span };
                    sym.Function = f;
                    var sc = FnScope(fn, top, f.Params);
                    f.Body = FunctionBody(fn.Body, sc);
                    ParamCopies(f.Params, f.Body);
                    f.BlankLinesBefore = RlLexer.BlankLinesBefore(src, a.Span.Start);
                    mod.Items.Add(f);
                    topFnDefined.Add(name.Name);
                    continue;
                }
                var list = new List<IrStmt>();
                Stmt(e, top, list, false);
                mod.Items.AddRange(list);
            }
            PlaceGlobals();
            mod.Span = new Span(0, src.Length);
            if (prog.Comments.Count > 0)
            {
                IrComments.Attach(mod, prog.Comments, src);
                IrComments.Tidy(mod);   // only Attach creates empty comment lists
            }
        }

        void PlaceGlobals()
        {
            var placed = new HashSet<IrSymbol>();
            var items = new List<IrNode>();
            // once every global of this file is placed, the remaining items need no walk
            int globalCount = moduleVars.Values.Count(v => v.Kind == IrSymbolKind.Global && v.Tag as string == Tag);
            bool allPlaced = false;
            // functions assign globals only with <<- (superAssigned names); once those are placed, functions need no walk
            var superNames = new HashSet<string>(superAssigned.Select(IrN));
            var superGlobals = new HashSet<IrSymbol>(moduleVars.Values.Where(v => v.Kind == IrSymbolKind.Global && v.Tag as string == Tag && superNames.Contains(v.Name)));
            int superLeft = superGlobals.Count;
            foreach (var it in mod.Items)
            {
                superLeft = superGlobals.Count(g => !placed.Contains(g));
                if (!allPlaced && placed.Count >= globalCount)
                    allPlaced = moduleVars.Values.All(v => v.Kind != IrSymbolKind.Global || v.Tag as string != Tag || placed.Contains(v));
                if (allPlaced || (it is IrFunction && superLeft == 0))
                {
                    items.Add(it);
                    continue;
                }
                if (it is IrAssign a && a.Op == null && a.Target is IrName tn && tn.Symbol != null && tn.Symbol.Kind == IrSymbolKind.Global
                    && tn.Symbol.Tag as string == Tag && !placed.Contains(tn.Symbol) && !a.Synthetic)
                {
                    placed.Add(tn.Symbol);
                    var g = new IrGlobal { Symbol = tn.Symbol, Init = a.Value, NameSpan = tn.Span, TypeExplicit = false, Span = a.Span, Leading = a.Leading, Trailing = a.Trailing, BlankLinesBefore = a.BlankLinesBefore };
                    convertedToGlobal[a] = g;
                    items.Add(g);
                    continue;
                }
                foreach (var s in IrWalk.AssignedSymbols(it).ToList())
                    if (s.Kind == IrSymbolKind.Global && s.Tag as string == Tag && placed.Add(s)) items.Add(ImplicitGlobal(s));
                items.Add(it);
            }
            foreach (var g in moduleVars.Values.Where(v => v.Kind == IrSymbolKind.Global && !placed.Contains(v)).ToList())
            {
                placed.Add(g);
                int at = items.FindIndex(it => IrWalk.Descendants(it).Any(n => n is IrName nm && nm.Symbol == g));
                if (at < 0) at = items.Count;
                items.Insert(at, ImplicitGlobal(g));
            }
            mod.Items.Clear();
            mod.Items.AddRange(items);
        }

        static IrGlobal ImplicitGlobal(IrSymbol g) => new IrGlobal { Symbol = g, Init = null, ImplicitInit = true, Synthetic = true, TypeExplicit = false };

        IrBlock FunctionBody(RlExpr body, Scope sc)
        {
            body = Unparen(body);
            var blk = new IrBlock { Span = body.Span };
            if (body is RlBlock b)
            {
                for (int i = 0; i < b.Exprs.Count; i++) Stmt(b.Exprs[i], sc, blk.Stmts, i == b.Exprs.Count - 1);
            }
            else Stmt(body, sc, blk.Stmts, true);
            return blk;
        }

        IrBlock BlockOf(RlExpr body, Scope sc, bool tail)
        {
            body = Unparen(body);
            var blk = new IrBlock { Span = body.Span };
            if (body is RlBlock b)
            {
                for (int i = 0; i < b.Exprs.Count; i++) Stmt(b.Exprs[i], sc, blk.Stmts, tail && i == b.Exprs.Count - 1);
            }
            else Stmt(body, sc, blk.Stmts, tail);
            return blk;
        }

        // ------------------------------------------------------------------ copies (R value semantics)
        /// <summary>Nodes whose value is copied when it is a list / map read from a variable or element (after inference).</summary>
        readonly List<IrNode> copySites = new List<IrNode>();
        /// <summary>Synthetic copies whose op (ListCopy / MapCopy) is decided from the inferred type.</summary>
        readonly List<IrOpCall> copyOps = new List<IrOpCall>();
        /// <summary>Synthetic implicit returns of user / value calls: dropped (kept as a statement) when the call is void.</summary>
        readonly List<(List<IrStmt> Owner, IrReturn Ret)> tentativeReturns = new List<(List<IrStmt>, IrReturn)>();
        /// <summary>Values combined with c() / append(): must not be collections.</summary>
        readonly List<(IrExpr Value, Span At)> combineChecks = new List<(IrExpr, Span)>();
        readonly List<IrListLit> cLiterals = new List<IrListLit>();

        IrOpCall CopyOf(IrExpr e)
        {
            var c = new IrOpCall(IrOp.ListCopy, e) { Synthetic = true, Span = e.Span, NameSpan = e.Span };
            copyOps.Add(c);
            return c;
        }

        /// <summary>A function that changes a parameter in place starts with a Synthetic copy p = copy(p) (R arguments are values).</summary>
        void ParamCopies(List<IrParam> ps, IrBlock body)
        {
            if (ps.Count == 0) return;
            var changed = new HashSet<IrSymbol>();
            // changes in place are statements (index assignments, list / map operations): walk statements only
            void Check(IrStmt st)
            {
                switch (st)
                {
                    case IrBlock b: foreach (var x in b.Stmts) Check(x); break;
                    case IrAssign a when a.Target is IrIndex:
                        { var r = RootSym(a.Target); if (r != null && r.Kind == IrSymbolKind.Param) changed.Add(r); break; }
                    case IrExprStmt es when es.Expr is IrOpCall o && o.Args.Count > 0 && (o.Op == IrOp.ListAppend || o.Op == IrOp.ListInsert || o.Op == IrOp.ListPop || o.Op == IrOp.MapRemoveIfPresent || o.Op == IrOp.RemoveValue):
                        { var r = RootSym(o.Args[0]); if (r != null && r.Kind == IrSymbolKind.Param) changed.Add(r); break; }
                    case IrIf i: Check(i.Then); Check(i.Else); break;
                    case IrWhile w: Check(w.Body); break;
                    case IrDoWhile dw: Check(dw.Body); break;
                    case IrLoop l: Check(l.Body); break;
                    case IrClassicFor cf: Check(cf.Body); break;
                    case IrRangeFor rf: Check(rf.Body); break;
                    case IrForEach fe: Check(fe.Body); break;
                    case IrSwitch sw: foreach (var cs in sw.Cases) Check(cs.Body); break;
                }
            }
            Check(body);
            int at = 0;
            foreach (var p in ps)
            {
                if (!changed.Contains(p.Symbol)) continue;
                var copy = new IrAssign
                {
                    Target = new IrName(p.Symbol) { Synthetic = true, Span = p.NameSpan, NameSpan = p.NameSpan },
                    Value = CopyOf(new IrName(p.Symbol) { Synthetic = true, Span = p.NameSpan, NameSpan = p.NameSpan }),
                    Synthetic = true, Span = p.NameSpan, OpSpan = p.NameSpan,
                };
                body.Stmts.Insert(at++, copy);
            }
        }

        static IrSymbol RootSym(IrExpr e)
        {
            while (true)
            {
                switch (e)
                {
                    case IrName n: return n.Symbol;
                    case IrIndex ix: e = ix.Target; continue;
                    case IrOpCall o when o.Op == IrOp.MapGetOrNull && o.Args.Count > 0: e = o.Args[0]; continue;
                }
                return null;
            }
        }

        // ================================================================== statements
        void Stmt(RlExpr e, Scope sc, List<IrStmt> outList, bool tail)
        {
            int before = outList.Count;
            StmtCore(e, sc, outList, tail);
            if (outList.Count > before)
            {
                int blank = RlLexer.BlankLinesBefore(src, e.Span.Start);
                var first = outList.Skip(before).FirstOrDefault(x => !x.Synthetic) ?? outList[before];
                if (first.BlankLinesBefore == 0) first.BlankLinesBefore = blank;
            }
        }

        void StmtCore(RlExpr e, Scope sc, List<IrStmt> outList, bool tail)
        {
            var u = Unparen(e);
            switch (u)
            {
                case RlAssign a when Unparen(a.Value) is RlAssign inner && Unparen(inner.Target) is RlIdent iid:
                    // chained x <- y <- 3: y <- 3, then x <- y (Python's x = y = 3)
                    StmtCore(inner, sc, outList, false);
                    Assign(new RlAssign { Op = a.Op, Target = a.Target, Value = new RlIdent { Name = iid.Name, Backtick = iid.Backtick, Span = iid.Span }, OpSpan = a.OpSpan, Span = a.Span }, sc, outList);
                    return;
                case RlAssign a: Assign(a, sc, outList); return;
                case RlIf i: outList.Add(If(i, sc, tail)); return;
                case RlFor f: outList.Add(For(f, sc)); return;
                case RlWhile w:
                    {
                        var cond = Cond(w.Cond, sc);
                        sc.LoopDepth++;
                        var body = BlockOf(w.Body, sc, false);
                        sc.LoopDepth--;
                        outList.Add(new IrWhile { Cond = cond, Body = body, KwSpan = w.KwSpan, Span = w.Span });
                        return;
                    }
                case RlRepeat r:
                    {
                        sc.LoopDepth++;
                        var body = BlockOf(r.Body, sc, false);
                        sc.LoopDepth--;
                        outList.Add(new IrLoop { Body = body, KwSpan = r.KwSpan, Span = r.Span });
                        return;
                    }
                case RlBreak _:
                case RlNext _:
                    if (sc.LoopDepth == 0) Fail("Error: no loop for break/next, jumping to top level", u.Span);
                    outList.Add(u is RlBreak ? (IrStmt)new IrBreak { Span = u.Span } : new IrContinue { Span = u.Span });
                    return;
                case RlBlock b:
                    for (int k = 0; k < b.Exprs.Count; k++) Stmt(b.Exprs[k], sc, outList, tail && k == b.Exprs.Count - 1);
                    return;
                case RlCall c when c.Fn is RlIdent fnId && !IsUserCallable(fnId.Name, sc):
                    switch (fnId.Name)
                    {
                        case "return":
                            if (sc.IsTop) Fail("Error: no function to return from, jumping to top level", c.Span);
                            if (c.Args.Count > 1) Fail("Error in " + CallText(c) + ": multi-argument returns are not permitted", c.Span);
                            if (c.Args.Count == 1 && c.Args[0].Value != null)
                                outList.Add(new IrReturn { Value = Expr(c.Args[0].Value, sc), Span = c.Span });
                            else outList.Add(new IrReturn { Span = c.Span });
                            return;
                        case "source": outList.Add(Source(c, sc)); return;
                        case "switch": outList.Add(Switch(c, sc, (v, list) => Stmt(v, sc, list, tail), false, null)); return;
                        case "invisible":
                            if (c.Args.Count == 1 && c.Args[0].Value != null && c.Args[0].Name == null) { Stmt(c.Args[0].Value, sc, outList, tail); return; }
                            break;
                    }
                    break;
            }
            var v = Expr(e, sc);
            if (tail && !sc.IsTop)
            {
                if (v is IrCall bc && bc.Kind == IrCallKind.Builtin && (GameApi.Find(bc.Builtin)?.ReturnType?.Kind ?? IrTypeKind.Any) == IrTypeKind.Void)
                {
                    outList.Add(new IrExprStmt(v) { Span = u.Span });
                    return;
                }
                var r = new IrReturn { Value = v, Synthetic = true, Span = u.Span };
                if (v is IrCall uc && uc.Kind != IrCallKind.Builtin) tentativeReturns.Add((outList, r));
                outList.Add(r);
                return;
            }
            if (v is IrLiteral || v is IrName || v is IrConst)
            {
                // a bare value statement does nothing in the game (R would auto-print it at the top level)
            }
            outList.Add(new IrExprStmt(v) { Span = u.Span });
        }

        IrExpr Cond(RlExpr e, Scope sc)
        {
            var u = Unparen(e);
            if (u is RlAssign a) Fail("assignment inside a condition is not supported in JavaFarmer; did you mean == ?", a.OpSpan);
            return Expr(e, sc);
        }

        IrIf If(RlIf i, Scope sc, bool tail)
        {
            var r = new IrIf { Cond = Cond(i.Cond, sc), KwSpan = i.KwSpan, Span = i.Span };
            r.Then = BlockOf(i.Then, sc, tail);
            if (i.Else != null)
            {
                var eu = Unparen(i.Else);
                if (eu is RlIf ei) r.Else = If(ei, sc, tail);
                else
                {
                    var eb = BlockOf(i.Else, sc, tail);
                    eb.Span = Span.Cover(i.ElseSpan, eb.Span);
                    r.Else = eb;
                }
            }
            return r;
        }

        IrSymbol LoopVar(RlFor f, Scope sc) => WriteVar(f.Var, sc, false, f.VarSpan);

        static bool IsIntLit(RlExpr e, out long v)
        {
            e = Unparen(e);
            v = 0;
            if (e is RlNum n && n.LooksInt && Math.Abs(n.Value) < 1e15) { v = (long)n.Value; return true; }
            if (e is RlUnary u && u.Op == "-" && Unparen(u.Operand) is RlNum m && m.LooksInt && Math.Abs(m.Value) < 1e15) { v = -(long)m.Value; return true; }
            return false;
        }

        IrStmt For(RlFor f, Scope sc)
        {
            var seq = Unparen(f.Seq);
            IrStmt result;
            IrBlock Body()
            {
                sc.LoopDepth++;
                var b = BlockOf(f.Body, sc, false);
                sc.LoopDepth--;
                return b;
            }
            IrRangeFor Range(IrExpr start, IrExpr end, IrExpr step, bool inclusive)
            {
                var rf = new IrRangeFor { Start = start, End = end, Step = step, Inclusive = inclusive, KwSpan = f.KwSpan, HeaderSpan = f.HeaderSpan, VarSpan = f.VarSpan, Span = f.Span };
                rf.Var = LoopVar(f, sc);
                rf.Body = Body();
                return rf;
            }
            IrLiteral SynOne(Span at) => new IrLiteral { Kind = IrLitKind.Int, Int = 1, Type = IrType.Int, Synthetic = true, Span = at };
            if (seq is RlBinary colon && colon.Op == ":")
            {
                var a = Expr(colon.Left, sc);
                var b = Expr(colon.Right, sc);
                IrExpr step = null;
                if (IsIntLit(colon.Left, out var av) && IsIntLit(colon.Right, out var bv) && av > bv)
                    step = new IrUnary { Op = IrUnOp.Neg, Operand = SynOne(colon.OpSpan), Synthetic = true, Span = colon.OpSpan, OpSpan = colon.OpSpan };
                return Range(a, b, step, true);
            }
            if (seq is RlBinary minus && minus.Op == "-" && Unparen(minus.Left) is RlCall sl && sl.FnName == "seq_len" && !IsUserCallable("seq_len", sc)
                && IsIntLit(minus.Right, out var one) && one == 1 && sl.Args.Count == 1 && sl.Args[0].Value != null)
            {
                var n = Expr(sl.Args[0].Value, sc);
                return Range(null, n, null, false);
            }
            if (seq is RlCall c && c.Fn is RlIdent cid && !IsUserCallable(cid.Name, sc))
            {
                switch (cid.Name)
                {
                    case "seq_len":
                        {
                            var args = Positional(c, 1, 1, "length.out");
                            return Range(SynOne(cid.Span), Expr(args[0], sc), null, true);
                        }
                    case "seq_along":
                        {
                            var args = Positional(c, 1, 1, "along.with");
                            var v = Expr(args[0], sc);
                            var len = new IrOpCall(IrOp.Len, v) { Synthetic = true, NameSpan = cid.Span, Span = c.Span };
                            return Range(SynOne(cid.Span), len, null, true);
                        }
                    case "seq":
                        {
                            var args = Named(c, new[] { "from", "to", "by" });
                            if (args[0] == null) Fail("seq() needs a start value in JavaFarmer: seq(from, to)", c.Span);
                            if (args[1] == null)
                            {
                                if (args[2] != null) Fail("seq() needs from and to in JavaFarmer: seq(from, to, by = step)", c.Span);
                                return Range(SynOne(cid.Span), Expr(args[0], sc), null, true);
                            }
                            var a = Expr(args[0], sc);
                            var b = Expr(args[1], sc);
                            IrExpr step = null;
                            if (args[2] != null)
                            {
                                if (!IsIntLit(args[2], out var sv) || sv == 0) Fail("seq(by = ...) needs a constant whole number step in JavaFarmer", args[2].Span);
                                step = Expr(args[2], sc);
                            }
                            else if (IsIntLit(args[0], out var av) && IsIntLit(args[1], out var bv) && av > bv)
                                step = new IrUnary { Op = IrUnOp.Neg, Operand = SynOne(cid.Span), Synthetic = true, Span = cid.Span, OpSpan = cid.Span };
                            return Range(a, b, step, true);
                        }
                    case "names":
                        {
                            var args = Positional(c, 1, 1, "x");
                            var m = Expr(args[0], sc);
                            var keys = new IrOpCall(IrOp.MapKeys, m) { NameSpan = cid.Span, Span = c.Span };
                            var fe = new IrForEach { Iterable = keys, KwSpan = f.KwSpan, VarSpan = f.VarSpan, Span = f.Span };
                            fe.Var = LoopVar(f, sc);
                            fe.Body = Body();
                            return fe;
                        }
                }
            }
            {
                forSequence = true;
                IrExpr it;
                try { it = Expr(f.Seq, sc); }
                finally { forSequence = false; }
                if (IsMapValue(seq, sc)) it = new IrOpCall(IrOp.MapValues, it) { Synthetic = true, Span = it.Span, NameSpan = it.Span };
                var fe = new IrForEach { Iterable = it, KwSpan = f.KwSpan, VarSpan = f.VarSpan, Span = f.Span };
                fe.Var = LoopVar(f, sc);
                fe.Body = Body();
                result = fe;
            }
            return result;
        }

        IrStmt Source(RlCall c, Scope sc)
        {
            if (!sc.IsTop) Fail("source() is only supported at the top level of a file in JavaFarmer", c.Span);
            if (c.Args.Count != 1 || !(c.Args[0].Name == null || c.Args[0].Name == "file") || !(Unparen(c.Args[0].Value) is RlStr s))
                Fail("source() needs just the file name in JavaFarmer: source(\"utils.R\")", c.Span);
            var str = (RlStr)Unparen(c.Args[0].Value);
            var file = str.Value;
            int dot = file.LastIndexOf('.');
            if (dot <= 0 || dot == file.Length - 1 || file.IndexOf('/') >= 0 || file.IndexOf('\\') >= 0)
                Fail("source() takes the name of another code window with its extension in JavaFarmer, for example source(\"utils.R\") or source(\"Utils.java\")", str.Span);
            return new IrImport { Module = ModuleOfFile(file), NameSpan = str.Span, Span = c.Span, ImportAll = true };
        }

        // ------------------------------------------------------------------ switch
        /// <summary>
        /// switch(selector, a = {..}, "b c" = , d = {..}, {default}): string arms → IrSwitch; empty arms fall through to
        /// the next arm (several labels). into: the switch is an assignment value (each arm assigns; no default → NULL).
        /// </summary>
        IrSwitch Switch(RlCall c, Scope sc, Action<RlExpr, List<IrStmt>> arm, bool into, Func<IrExpr, IrStmt> sink)
        {
            if (c.Args.Count == 0 || c.Args[0].Value == null) Fail("Error in switch(): 'EXPR' is missing", c.Span);
            var selArg = c.Args[0];
            var arms = c.Args.Skip(1).ToList();
            bool anyNamed = arms.Any(a => a.Name != null);
            var selU = Unparen(selArg.Value);
            if (!anyNamed || selU is RlNum || (selU is RlUnary su && Unparen(su.Operand) is RlNum))
                Fail("numeric switch() is not supported in JavaFarmer; use if / else if", c.Fn.Span);
            var sw = new IrSwitch { Selector = Expr(selArg.Value, sc), KwSpan = c.Fn.Span, Span = c.Span, Style = IrSwitchStyle.Colon };
            var pending = new List<IrExpr>();
            var pendingSpan = Span.None;
            bool hasDefault = false;
            foreach (var a in arms)
            {
                if (a.Name == null)
                {
                    if (a.Value == null) continue;
                    if (hasDefault) Fail("Error in switch(): duplicate 'switch' defaults", a.Span);
                    hasDefault = true;
                    var d = new IrCase { IsDefault = true, LabelSpan = a.Span, Span = a.Span };
                    d.Body = ArmBlock(a.Value, arm);
                    sw.Cases.Add(d);
                    pending.Clear();
                    continue;
                }
                var lab = new IrLiteral { Kind = IrLitKind.String, Str = a.Name, Type = IrType.String, Span = a.NameSpan };
                pending.Add(lab);
                pendingSpan = pendingSpan.IsNone ? a.NameSpan : Span.Cover(pendingSpan, a.NameSpan);
                if (a.Value == null) continue;
                var cs = new IrCase { LabelSpan = pendingSpan, Span = a.Span };
                cs.Labels.AddRange(pending);
                cs.Body = ArmBlock(a.Value, arm);
                sw.Cases.Add(cs);
                pending.Clear();
                pendingSpan = Span.None;
            }
            if (pending.Count > 0)
            {
                var cs = new IrCase { LabelSpan = pendingSpan, Span = pendingSpan };
                cs.Labels.AddRange(pending);
                cs.Body = new IrBlock { Span = pendingSpan };
                if (into) cs.Body.Stmts.Add(Synth(sink(SynNull())));
                sw.Cases.Add(cs);
            }
            if (into && !hasDefault)
            {
                var d = new IrCase { IsDefault = true, Synthetic = true };
                d.Body = new IrBlock();
                d.Body.Stmts.Add(Synth(sink(SynNull())));
                sw.Cases.Add(d);
            }
            return sw;
        }

        /// <summary>Nodes Fix checks after type inference (text in arithmetic, text → number, x[i] on text).</summary>
        readonly List<IrExpr> fixChecks = new List<IrExpr>();
        T Track<T>(T e) where T : IrExpr { fixChecks.Add(e); return e; }

        static bool IsBoolOp(IrBinOp op) =>
            op == IrBinOp.Eq || op == IrBinOp.Ne || op == IrBinOp.Lt || op == IrBinOp.Le || op == IrBinOp.Gt || op == IrBinOp.Ge || op == IrBinOp.And || op == IrBinOp.Or;

        static IrStmt Synth(IrStmt s)
        {
            s.Synthetic = true;
            if (s is IrAssign a && a.Target is IrName tn) tn.Synthetic = true;   // the whole invented statement is scaffolding
            return s;
        }
        static IrLiteral SynNull() => new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Synthetic = true };

        IrBlock ArmBlock(RlExpr value, Action<RlExpr, List<IrStmt>> arm)
        {
            var u = Unparen(value);
            var blk = new IrBlock { Span = u.Span };
            arm(value, blk.Stmts);
            return blk;
        }

        // ------------------------------------------------------------------ assignment values computed by statements
        /// <summary>Lowers value so that its result goes through sink (if / switch / { } values of an assignment).</summary>
        void Into(RlExpr value, Scope sc, List<IrStmt> outList, Func<IrExpr, IrStmt> sink)
        {
            var u = Unparen(value);
            switch (u)
            {
                case RlBlock b:
                    if (b.Exprs.Count == 0) { outList.Add(Synth(sink(SynNull()))); return; }
                    for (int k = 0; k < b.Exprs.Count - 1; k++) Stmt(b.Exprs[k], sc, outList, false);
                    Into(b.Exprs[b.Exprs.Count - 1], sc, outList, sink);
                    return;
                case RlIf i:
                    {
                        var r = new IrIf { Cond = Cond(i.Cond, sc), KwSpan = i.KwSpan, Span = i.Span };
                        r.Then = new IrBlock { Span = Unparen(i.Then).Span };
                        Into(i.Then, sc, r.Then.Stmts, sink);
                        if (i.Else != null)
                        {
                            var eu = Unparen(i.Else);
                            var eb = new IrBlock { Span = Span.Cover(i.ElseSpan, eu.Span) };
                            Into(i.Else, sc, eb.Stmts, sink);
                            r.Else = eb.Stmts.Count == 1 && eb.Stmts[0] is IrIf nested && eu is RlIf ? (IrStmt)nested : eb;
                        }
                        else
                        {
                            var eb = new IrBlock { Synthetic = true };
                            eb.Stmts.Add(Synth(sink(SynNull())));
                            r.Else = eb;
                        }
                        outList.Add(r);
                        return;
                    }
                case RlCall c when c.FnName == "switch" && !IsUserCallable("switch", sc):
                    outList.Add(Switch(c, sc, (v, list) => Into(v, sc, list, sink), true, sink));
                    return;
            }
            outList.Add(sink(Expr(value, sc)));
        }

        static bool SimpleValue(RlExpr e)
        {
            e = Unparen(e);
            if (e is RlBlock b) return b.Exprs.Count == 1 && SimpleValue(b.Exprs[0]);
            if (e is RlIf || e is RlAssign || e is RlFor || e is RlWhile || e is RlRepeat) return false;
            if (e is RlCall c && c.FnName == "switch") return false;
            return true;
        }

        static bool NeedsInto(RlExpr value)
        {
            var u = Unparen(value);
            switch (u)
            {
                case RlBlock b: return !(b.Exprs.Count == 1 && SimpleValue(b.Exprs[0]));
                case RlIf i: return !SimpleChain(i);
                case RlCall c: return c.FnName == "switch";
            }
            return false;
        }

        // ------------------------------------------------------------------ assignments
        Scope emptyHintScope;
        string emptyHintName;

        void Assign(RlAssign a, Scope sc, List<IrStmt> outList)
        {
            var target = Unparen(a.Target);
            bool super = a.Super && !sc.IsTop;
            switch (target)
            {
                case RlIdent id:
                    AssignName(a, id, sc, outList, super);
                    return;
                case RlIndex _:
                case RlDollar _:
                    AssignIndex(a, target, sc, outList, super);
                    return;
                case RlCall c when c.FnName != null:
                    Fail("replacement functions like " + c.FnName + "(x) <- value are not supported in JavaFarmer", Span.Cover(target.Span, a.OpSpan));
                    return;
            }
            Fail("Error: invalid (do_set) left-hand side to assignment", target.Span);
        }

        void AssignName(RlAssign a, RlIdent id, Scope sc, List<IrStmt> outList, bool super)
        {
            var value = Unparen(a.Value);
            if (value is RlCall vc && TryCombine(a, id, vc, sc, outList, super)) return;
            if (value is RlIndex vix && TryNegIndex(a, id, vix, sc, outList, super)) return;
            if (NeedsInto(value))
            {
                var targets = new List<IrName>();
                Func<IrExpr, IrStmt> sink = v =>
                {
                    var nm = new IrName { Span = id.Span, NameSpan = id.Span };
                    targets.Add(nm);
                    var asg = new IrAssign { Target = nm, Value = v, OpSpan = a.OpSpan, Span = v.Span.IsNone ? a.Span : v.Span };
                    copySites.Add(asg);
                    return asg;
                };
                Into(value, sc, outList, sink);
                var sym = WriteVar(id.Name, sc, super, id.Span);
                foreach (var t in targets) { t.Symbol = sym; t.Type = sym.Type; }
                return;
            }
            var oldHint = emptyHintName; var oldScope = emptyHintScope;
            emptyHintName = id.Name; emptyHintScope = sc;
            IrExpr val;
            try { val = Expr(a.Value, sc); }
            finally { emptyHintName = oldHint; emptyHintScope = oldScope; }
            var s = WriteVar(id.Name, sc, super, id.Span);
            var asg2 = new IrAssign { Target = new IrName(s) { Span = id.Span, NameSpan = id.Span }, Value = val, OpSpan = a.OpSpan, Span = a.Span };
            copySites.Add(asg2);
            outList.Add(asg2);
        }

        /// <summary>
        /// The variable an in-place change of name (x[[i]] &lt;- v, x &lt;- c(x, v)) works on. Inside a function, R makes
        /// a local copy of an outer variable first: a Synthetic "x = copy(outer x)" is emitted before.
        /// </summary>
        IrSymbol ChangeTarget(RlIdent root, Scope sc, List<IrStmt> outList, bool super)
        {
            CheckName(root.Name, root.Span);
            if (sc.IsTop || super)
            {
                var s = ResolveVar(root.Name, sc, root.Span, false);
                if (s == null || s.Kind == IrSymbolKind.Function || s.Kind == IrSymbolKind.ExternalFunction) Fail("Error: object '" + root.Name + "' not found", root.Span);
                if (super && s.Kind == IrSymbolKind.Local && s.Tag as string == Tag) s.Kind = IrSymbolKind.Global;
                return s;
            }
            if (sc.Defined.Contains(root.Name)) return Local(sc, root.Name);
            var outer = ResolveVar(root.Name, sc, root.Span, false);
            if (outer == null || outer.Kind == IrSymbolKind.Function || outer.Kind == IrSymbolKind.ExternalFunction) Fail("Error: object '" + root.Name + "' not found", root.Span);
            var local = Local(sc, root.Name);
            sc.Defined.Add(root.Name);
            if (outer == local) return local;
            var copy = new IrAssign
            {
                Target = new IrName(local) { Span = root.Span, NameSpan = root.Span, Synthetic = true },
                Value = CopyOf(new IrName(outer) { Span = root.Span, NameSpan = root.Span, Synthetic = true }),
                Synthetic = true, Span = root.Span, OpSpan = root.Span,
            };
            outList.Add(copy);
            return local;
        }

        IrExpr NameRef(IrSymbol s, RlIdent id) => new IrName(s) { Span = id.Span, NameSpan = id.Span };

        /// <summary>v &lt;- c(v, x) / c(x, v) / append(v, x[, after = i]).</summary>
        bool TryCombine(RlAssign a, RlIdent id, RlCall c, Scope sc, List<IrStmt> outList, bool super)
        {
            var fn = c.FnName;
            if (fn != "c" && fn != "append") return false;
            if (IsUserCallable(fn, sc)) return false;
            if (fn == "c")
            {
                if (c.Args.Count != 2 || c.Args.Any(x => x.Name != null || x.Value == null)) return false;
                var first = Unparen(c.Args[0].Value) as RlIdent;
                var second = Unparen(c.Args[1].Value) as RlIdent;
                if (first != null && first.Name == id.Name)
                {
                    var x = Expr(c.Args[1].Value, sc);
                    var s = ChangeTarget(id, sc, outList, super);
                    combineChecks.Add((x, c.Args[1].Span));
                    var op = new IrOpCall(IrOp.ListAppend, NameRef(s, first), x) { NameSpan = c.Fn.Span, Span = a.Span };
                    copySites.Add(op);
                    outList.Add(new IrExprStmt(op) { Span = a.Span });
                    return true;
                }
                if (second != null && second.Name == id.Name)
                {
                    var x = Expr(c.Args[0].Value, sc);
                    var s = ChangeTarget(id, sc, outList, super);
                    combineChecks.Add((x, c.Args[0].Span));
                    var zero = new IrLiteral { Kind = IrLitKind.Int, Int = 0, Type = IrType.Int, Synthetic = true, Span = c.Fn.Span };
                    var op = new IrOpCall(IrOp.ListInsert, NameRef(s, second), zero, x) { NameSpan = c.Fn.Span, Span = a.Span };
                    copySites.Add(op);
                    outList.Add(new IrExprStmt(op) { Span = a.Span });
                    return true;
                }
                return false;
            }
            // append(x, values, after = length(x))
            var args = Named(c, new[] { "x", "values", "after" });
            if (args[0] == null || args[1] == null) Fail("Error in " + CallText(c) + ": argument \"" + (args[0] == null ? "x" : "values") + "\" is missing, with no default", c.Span);
            if (!(Unparen(args[0]) is RlIdent self) || self.Name != id.Name)
                Fail("append() is only supported as v <- append(v, x) in JavaFarmer", c.Span);
            var val = Expr(args[1], sc);
            IrExpr after = args[2] != null ? Expr(args[2], sc) : null;
            var tgt = ChangeTarget(id, sc, outList, super);
            combineChecks.Add((val, args[1].Span));
            IrOpCall res;
            if (after == null) res = new IrOpCall(IrOp.ListAppend, NameRef(tgt, (RlIdent)Unparen(args[0])), val) { NameSpan = c.Fn.Span, Span = a.Span };
            else res = new IrOpCall(IrOp.ListInsert, NameRef(tgt, (RlIdent)Unparen(args[0])), after, val) { NameSpan = c.Fn.Span, Span = a.Span };
            copySites.Add(res);
            outList.Add(new IrExprStmt(res) { Span = a.Span });
            return true;
        }

        /// <summary>v &lt;- v[-i] → v.pop(i - 1); v &lt;- v[-length(v)] → v.pop().</summary>
        bool TryNegIndex(RlAssign a, RlIdent id, RlIndex ix, Scope sc, List<IrStmt> outList, bool super)
        {
            if (!(Unparen(ix.Target) is RlIdent self) || self.Name != id.Name) return false;
            if (ix.Args.Count != 1 || ix.Args[0].Value == null || ix.Args[0].Name != null) return false;
            if (!(Unparen(ix.Args[0].Value) is RlUnary neg) || neg.Op != "-") return false;
            var e = Unparen(neg.Operand);
            var s0 = ChangeTarget(id, sc, outList, super);
            var recv = NameRef(s0, self);
            IrOpCall pop;
            if (e is RlCall lc && lc.FnName == "length" && lc.Args.Count == 1 && Unparen(lc.Args[0].Value) is RlIdent li && li.Name == id.Name && !IsUserCallable("length", sc))
                pop = new IrOpCall(IrOp.ListPop, recv) { NameSpan = ix.OpenSpan, Span = a.Span };
            else pop = new IrOpCall(IrOp.ListPop, recv, ZeroBased(e, sc)) { NameSpan = ix.OpenSpan, Span = a.Span };
            outList.Add(new IrExprStmt(pop) { Span = a.Span });
            return true;
        }

        /// <summary>A 1-based position as a 0-based index: literals are folded, otherwise a Synthetic "- 1".</summary>
        IrExpr ZeroBased(RlExpr e, Scope sc)
        {
            if (IsIntLit(e, out var v))
            {
                if (v < 1) Fail("Error: attempt to select less than one element (R positions start at 1)", Unparen(e).Span);
                return new IrLiteral { Kind = IrLitKind.Int, Int = v - 1, Type = IrType.Int, Span = Unparen(e).Span };
            }
            var x = Expr(e, sc);
            return new IrBinary { Op = IrBinOp.Sub, Left = x, Right = new IrLiteral { Kind = IrLitKind.Int, Int = 1, Type = IrType.Int, Synthetic = true, Span = x.Span }, Synthetic = true, Span = x.Span, OpSpan = x.Span };
        }

        void AssignIndex(RlAssign a, RlExpr target, Scope sc, List<IrStmt> outList, bool super)
        {
            var root = RootName(target);
            if (root == null) Fail("Error: invalid (do_set) left-hand side to assignment", target.Span);
            if (root.Name == "T" || root.Name == "F" || IsCategory(root.Name) && ResolveVar(root.Name, sc, root.Span, false) == null)
                Fail("Error: object '" + root.Name + "' not found", root.Span);
            var value = Unparen(a.Value);
            // removal: x[[i]] <- NULL / m[[k]] <- NULL / m$k <- NULL
            bool remove = value is RlConst nc && nc.Kind == RlConstKind.Null;
            IrExpr val = remove ? null : Expr(a.Value, sc);
            var s = ChangeTarget(root, sc, outList, super);
            if (target is RlIndex ix)
            {
                var key = SingleIndex(ix);
                var recv = TargetChain(ix.Target, s, sc);
                if (!remove && IsLengthPlusOne(key, (Unparen(ix.Target) as RlIdent)?.Name ?? "\0"))
                {
                    var app = new IrOpCall(IrOp.ListAppend, recv, val) { NameSpan = ix.OpenSpan, Span = a.Span };
                    copySites.Add(app);
                    outList.Add(new IrExprStmt(app) { Span = a.Span });
                    return;
                }
                var kx = KeyExpr(key, sc);
                bool map = IsMapIndex(ix.Target, key, kx, sc);
                if (remove)
                {
                    if (map) outList.Add(new IrExprStmt(new IrOpCall(IrOp.MapRemoveIfPresent, recv, kx) { NameSpan = ix.OpenSpan, Span = a.Span }) { Span = a.Span });
                    else outList.Add(new IrExprStmt(new IrOpCall(IrOp.ListPop, recv, ZeroBasedLowered(kx, key)) { NameSpan = ix.OpenSpan, Span = a.Span }) { Span = a.Span });
                    return;
                }
                var tgt = map ? Track(new IrIndex { Target = recv, Index = kx, BaseOffset = 0, Span = ix.Span }) : ListIndex(recv, kx, key, ix.Span);
                var asg = new IrAssign { Target = tgt, Value = val, OpSpan = a.OpSpan, Span = a.Span };
                copySites.Add(asg);
                outList.Add(asg);
                return;
            }
            var d = (RlDollar)target;
            if (d.At) Fail("slots (@) are not supported in JavaFarmer", d.OpSpan);
            var mrecv = TargetChain(d.Target, s, sc);
            var klit = new IrLiteral { Kind = IrLitKind.String, Str = d.Name, Type = IrType.String, Span = d.NameSpan };
            if (remove) { outList.Add(new IrExprStmt(new IrOpCall(IrOp.MapRemoveIfPresent, mrecv, klit) { NameSpan = d.OpSpan, Span = a.Span }) { Span = a.Span }); return; }
            var asg3 = new IrAssign { Target = Track(new IrIndex { Target = mrecv, Index = klit, BaseOffset = 0, Span = d.Span }), Value = val, OpSpan = a.OpSpan, Span = a.Span };
            copySites.Add(asg3);
            outList.Add(asg3);
        }

        IrExpr ZeroBasedLowered(IrExpr kx, RlExpr key)
        {
            if (kx is IrLiteral l && l.Kind == IrLitKind.Int)
            {
                if (l.Int < 1) Fail("Error: attempt to select less than one element (R positions start at 1)", l.Span);
                return new IrLiteral { Kind = IrLitKind.Int, Int = l.Int - 1, Type = IrType.Int, Span = l.Span };
            }
            return new IrBinary { Op = IrBinOp.Sub, Left = kx, Right = new IrLiteral { Kind = IrLitKind.Int, Int = 1, Type = IrType.Int, Synthetic = true, Span = kx.Span }, Synthetic = true, Span = kx.Span, OpSpan = kx.Span };
        }

        RlExpr SingleIndex(RlIndex ix)
        {
            if (ix.Args.Count == 0 || (ix.Args.Count == 1 && ix.Args[0].Value == null))
                Fail("x[] (all elements) is not supported in JavaFarmer; use one index: x[[i]]", ix.Span);
            if (ix.Args.Count > 1) Fail("matrix indexing x[i, j] is not supported in JavaFarmer; use a list of lists: x[[i]][[j]]", ix.Span);
            var arg = ix.Args[0];
            if (arg.Name != null) Fail("named index arguments are not supported in JavaFarmer", arg.Span);
            var k = Unparen(arg.Value);
            if (k is RlUnary u && u.Op == "-") Fail("negative indices (x[-i]) are only supported in JavaFarmer as x <- x[-i] (removes element i)", arg.Span);
            if (k is RlBinary b && b.Op == ":" || k is RlCall c && c.FnName == "c")
                Fail("indexing with a vector (x[c(1, 2)], x[1:3]) is not supported in JavaFarmer; use one index at a time", arg.Span);
            if (k is RlConst kc && (kc.Kind == RlConstKind.True || kc.Kind == RlConstKind.False))
                Fail("logical indices (x[TRUE]) are not supported in JavaFarmer", arg.Span);
            return arg.Value;
        }

        IrExpr KeyExpr(RlExpr key, Scope sc) => Expr(key, sc);

        IrExpr ListIndex(IrExpr recv, IrExpr kx, RlExpr key, Span span)
        {
            if (kx is IrLiteral l && (l.Kind == IrLitKind.Int || (l.Kind == IrLitKind.Float && l.Float == Math.Floor(l.Float))))
            {
                long v = l.Kind == IrLitKind.Int ? l.Int : (long)l.Float;
                if (v < 1) Fail("Error: attempt to select less than one element (R positions start at 1, so the first element is x[[1]])", l.Span);
                return Track(new IrIndex { Target = recv, Index = new IrLiteral { Kind = IrLitKind.Int, Int = v - 1, Type = IrType.Int, Span = l.Span }, BaseOffset = 0, Span = span });
            }
            return Track(new IrIndex { Target = recv, Index = kx, BaseOffset = 1, Span = span });
        }

        /// <summary>The receiver part of an assignment target (x, x[[i]], x$a ...) built on the resolved root symbol.</summary>
        IrExpr TargetChain(RlExpr e, IrSymbol root, Scope sc)
        {
            var u = Unparen(e);
            switch (u)
            {
                case RlIdent id: return NameRef(root, id);
                case RlIndex ix:
                    {
                        var key = SingleIndex(ix);
                        var recv = TargetChain(ix.Target, root, sc);
                        var kx = KeyExpr(key, sc);
                        if (IsMapIndex(ix.Target, key, kx, sc)) return Track(new IrIndex { Target = recv, Index = kx, BaseOffset = 0, Span = ix.Span });
                        return ListIndex(recv, kx, key, ix.Span);
                    }
                case RlDollar d:
                    {
                        if (d.At) Fail("slots (@) are not supported in JavaFarmer", d.OpSpan);
                        var recv = TargetChain(d.Target, root, sc);
                        return Track(new IrIndex { Target = recv, Index = new IrLiteral { Kind = IrLitKind.String, Str = d.Name, Type = IrType.String, Span = d.NameSpan }, BaseOffset = 0, Span = d.Span });
                    }
            }
            Fail("Error: invalid (do_set) left-hand side to assignment", u.Span);
            return null;
        }

        // ================================================================== expressions
        static bool SimpleChain(RlExpr e)
        {
            e = Unparen(e);
            if (e is RlIf i) return i.Else != null && SimpleValue(i.Then) && (SimpleValue(i.Else) || SimpleChain(i.Else));
            return false;
        }

        static RlExpr Single(RlExpr e)
        {
            var u = Unparen(e);
            if (u is RlBlock b && b.Exprs.Count == 1) return Single(b.Exprs[0]);
            return e;
        }

        static readonly HashSet<string> Specials = new HashSet<string>
        {
            "print", "cat", "paste", "paste0", "as.character", "as.integer", "as.numeric", "as.double", "trunc", "floor", "ceiling",
            "sqrt", "round", "length", "nchar", "c", "list", "vector", "names", "is.null", "append", "seq_len", "seq_along", "seq",
            "match", "invisible", "rep", "setNames", "return", "source", "switch", "message",
        };

        /// <summary>Common base-R functions JavaFarmer does not support (so the message says so instead of "could not find").</summary>
        static readonly HashSet<string> KnownBase = new HashSet<string>
        {
            "sum", "mean", "prod", "rev", "sort", "order", "which", "which.max", "which.min", "sapply", "lapply", "vapply", "mapply",
            "Map", "Filter", "Reduce", "do.call", "apply", "substr", "substring", "strsplit", "sprintf", "format", "formatC",
            "toupper", "tolower", "gsub", "sub", "grepl", "regmatches", "is.na", "is.numeric", "is.character", "numeric", "character",
            "logical", "integer", "double", "matrix", "array", "data.frame", "head", "tail", "exists", "get", "assign", "eval",
            "parse", "environment", "new.env", "Sys.time", "Sys.sleep", "set.seed", "readline", "readLines", "writeLines", "nrow",
            "ncol", "dim", "cumsum", "range", "identical", "all", "any", "xor", "isTRUE", "isFALSE", "unlist", "Recall", "unname",
            "toString", "warning", "on.exit", "missing", "local", "attr", "attributes", "structure", "class", "inherits", "typeof",
            "mode", "is.element", "duplicated", "table", "exp", "log", "log2", "log10", "sin", "cos", "tan", "signif", "as.logical",
            "as.vector", "as.list", "is.list", "is.function", "factor", "levels", "trimws", "startsWith", "endsWith", "nargs",
            "emptyenv", "globalenv", "sys.call", "Negate", "identity", "max.col", "pmin", "pmax", "rowSums", "colSums", "outer",
            "crossprod", "diag", "t", "rbind", "cbind", "names<-", "mapply", "rapply", "environmentName", "Sys.getenv", "nlevels",
            "chartr", "casefold", "strtoi", "bitwAnd", "bitwOr", "bitwXor", "utf8ToInt", "intToUtf8", "rev.default", "sample",
            "rnorm", "rbinom", "rpois", "runif", "unique", "union", "intersect", "setdiff", "stop", "tryCatch", "try", "library",
            "require", "requireNamespace", "ifelse", "repeat", "vapply", "Vectorize", "environment<-", "body", "formals", "args",
        };

        bool IsUserCallable(string name, Scope sc)
        {
            bool BuiltinLike() => Specials.Contains(name) || GameApi.Find(name) != null;
            for (var s = sc; s != null && !s.IsTop; s = s.Parent)
            {
                bool has = s == sc ? s.Defined.Contains(name) || s.Fn.Assigned.Contains(name) : s.Fn.Has(name);
                if (has) return fnValued.Contains(name) || !BuiltinLike();
            }
            if (topFnSyms.ContainsKey(name)) return true;
            if (topAssigned.Contains(name) || superAssigned.Contains(name)) return fnValued.Contains(name) || !BuiltinLike();
            foreach (var (_, d) in imports) if (d != null && d.Functions.ContainsKey(name)) return true;
            return false;
        }

        IrExpr Expr(RlExpr e, Scope sc)
        {
            switch (e)
            {
                case RlParen p:
                    {
                        var x = Expr(p.Inner, sc);
                        x.ParenSpan = p.Span;
                        return x;
                    }
                case RlNum n:
                    if (n.LooksInt && Math.Abs(n.Value) < 9e15) return new IrLiteral { Kind = IrLitKind.Int, Int = (long)n.Value, Type = IrType.Int, Span = n.Span };
                    return new IrLiteral { Kind = IrLitKind.Float, Float = n.Value, Type = IrType.Float, Span = n.Span };
                case RlStr s:
                    if (s.Raw) Fail("raw strings r\"(...)\" are not supported in JavaFarmer", s.Span);
                    return new IrLiteral { Kind = IrLitKind.String, Str = s.Value, Type = IrType.String, Span = s.Span };
                case RlConst c:
                    switch (c.Kind)
                    {
                        case RlConstKind.True: return new IrLiteral { Kind = IrLitKind.Bool, Bool = true, Type = IrType.Bool, Span = c.Span };
                        case RlConstKind.False: return new IrLiteral { Kind = IrLitKind.Bool, Bool = false, Type = IrType.Bool, Span = c.Span };
                        case RlConstKind.Null: return new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = c.Span };
                        case RlConstKind.NA: Fail(c.Text + " is not supported in JavaFarmer (the game has no missing values); use NULL", c.Span); break;
                        default: Fail(c.Text + " is not supported in JavaFarmer (game numbers are always finite)", c.Span); break;
                    }
                    break;
                case RlIdent id: return NameExpr(id, sc);
                case RlBinary b: return Binary(b, sc);
                case RlUnary u: return Unary(u, sc);
                case RlCall call: return Call(call, sc);
                case RlIndex ix: return IndexRead(ix, sc);
                case RlDollar d: return DollarRead(d, sc);
                case RlNamespace ns: Fail("the " + Text(ns.OpSpan) + " operator (package::name) is not supported in JavaFarmer; call the function directly", ns.OpSpan); break;
                case RlFunction f: return Lambda(f, sc);
                case RlIf i:
                    {
                        if (i.Else == null) Fail("if without else cannot be used as a value here in JavaFarmer; add an else branch", i.KwSpan);
                        if (!SimpleChain(i)) Fail("an if / else with several statements can only be the whole value of an assignment (y <- if (c) { ... } else { ... }) in JavaFarmer", i.KwSpan);
                        var cond = Cond(i.Cond, sc);
                        var th = Expr(Single(i.Then), sc);
                        var el = Expr(Single(i.Else), sc);
                        return new IrConditional { Cond = cond, Then = th, Else = el, QSpan = i.KwSpan, Span = i.Span };
                    }
                case RlBlock blk:
                    if (blk.Exprs.Count == 1) return Expr(blk.Exprs[0], sc);
                    Fail("a { } block with several statements cannot be used as a value here in JavaFarmer", blk.OpenSpan);
                    break;
                case RlAssign a:
                    Fail("an assignment cannot be used as a value in JavaFarmer; assign on its own line first", a.OpSpan);
                    break;
                case RlFor _: case RlWhile _: case RlRepeat _:
                    Fail("a loop cannot be used as a value in JavaFarmer", e.Span);
                    break;
                case RlBreak _: case RlNext _:
                    Fail("break / next cannot be used as a value", e.Span);
                    break;
            }
            Fail("this expression is not supported in JavaFarmer", e.Span);
            return null;
        }

        static string Example(string cat)
        {
            switch (cat)
            {
                case "Entities": return "Carrot";
                case "Items": return "Hay";
                case "Grounds": return "Soil";
                case "Unlocks": return "Speed";
                case "Hats": return "Straw_Hat";
                default: return "Hay";
            }
        }

        bool forSequence;
        readonly Dictionary<string, IrSymbol> bags = new Dictionary<string, IrSymbol>();

        /// <summary>A whole constant bag used as a value (simulate(file, Unlocks, ...)).</summary>
        IrSymbol ConstantBag(string name)
        {
            if (bags.TryGetValue(name, out var s)) return s;
            s = new IrSymbol(name, IrSymbolKind.Global, IrType.Any) { PyName = name, Tag = PyLowering.UndefinedTag, IsConst = true };
            bags[name] = s;
            return s;
        }

        IrExpr NameExpr(RlIdent id, Scope sc)
        {
            var n = id.Name;
            var s = ResolveVar(n, sc, id.Span, false);
            if (s != null)
            {
                if (s.Kind == IrSymbolKind.Function || s.Kind == IrSymbolKind.ExternalFunction) return new IrFuncRef { Function = s, Span = id.Span, NameSpan = id.Span };
                return new IrName(s) { Span = id.Span, NameSpan = id.Span };
            }
            if (n == "T" || n == "F") Fail("T and F are not supported in JavaFarmer; write TRUE / FALSE", id.Span);
            if (IsDirection(n)) return new IrConst("Directions", n) { Span = id.Span };
            if (IsCategory(n))
            {
                if (forSequence) Fail(n + " holds the game's constants and cannot be used as a value; use " + n + "$Name (for example " + n + "$" + Example(n) + ")", id.Span);
                return new IrName(ConstantBag(n)) { Span = id.Span, NameSpan = id.Span };
            }
            if (GameApi.Find(n) != null) return new IrFuncRef { Builtin = n, Span = id.Span, NameSpan = id.Span };
            if (Specials.Contains(n) || KnownBase.Contains(n)) Fail(n + " cannot be used as a value in JavaFarmer", id.Span);
            Fail("Error: object '" + n + "' not found", id.Span);
            return null;
        }

        IrExpr Binary(RlBinary b, Scope sc)
        {
            IrBinOp op;
            switch (b.Op)
            {
                case "+": op = IrBinOp.Add; break;
                case "-": op = IrBinOp.Sub; break;
                case "*": op = IrBinOp.Mul; break;
                case "/": op = IrBinOp.TrueDiv; break;
                case "^": op = IrBinOp.Pow; break;
                case "%/%": op = IrBinOp.IntDivFloor; break;
                case "%%": op = IrBinOp.ModFloor; break;
                case "==": op = IrBinOp.Eq; break;
                case "!=": op = IrBinOp.Ne; break;
                case "<": op = IrBinOp.Lt; break;
                case "<=": op = IrBinOp.Le; break;
                case ">": op = IrBinOp.Gt; break;
                case ">=": op = IrBinOp.Ge; break;
                case "&&": op = IrBinOp.And; break;
                case "||": op = IrBinOp.Or; break;
                case "&": Fail("the vectorised & is not supported in JavaFarmer; use && (and || instead of |)", b.OpSpan); return null;
                case "|": Fail("the vectorised | is not supported in JavaFarmer; use || (and && instead of &)", b.OpSpan); return null;
                case "%in%":
                    {
                        var item = Expr(b.Left, sc);
                        var rhs = Unparen(b.Right);
                        if (rhs is RlCall nc && nc.FnName == "names" && nc.Args.Count == 1 && nc.Args[0].Value != null && nc.Args[0].Name == null && !IsUserCallable("names", sc))
                            return new IrOpCall(IrOp.Contains, Expr(nc.Args[0].Value, sc), item) { NameSpan = b.OpSpan, Span = b.Span };
                        return new IrOpCall(IrOp.Contains, Expr(b.Right, sc), item) { NameSpan = b.OpSpan, Span = b.Span };
                    }
                case ":": Fail("a:b (a sequence) is only supported as the sequence of a for loop in JavaFarmer: for (i in a:b)", b.OpSpan); return null;
                case "|>": Fail("the pipe operator |> is not supported in JavaFarmer; call the function directly", b.OpSpan); return null;
                case "~": Fail("formulas (~) are not supported in JavaFarmer", b.OpSpan); return null;
                case "?": Fail("help (?) is not supported in JavaFarmer", b.OpSpan); return null;
                default: Fail("the " + b.Op + " operator is not supported in JavaFarmer", b.OpSpan); return null;
            }
            if ((op == IrBinOp.Eq || op == IrBinOp.Ne) && (IsNullLit(b.Left) || IsNullLit(b.Right)))
                Fail("comparing with NULL using " + b.Op + " does not work in R (the result is empty); use " + (op == IrBinOp.Eq ? "is.null(x)" : "!is.null(x)"), b.OpSpan);
            var l = Expr(b.Left, sc);
            var r = Expr(b.Right, sc);
            var res = new IrBinary { Op = op, Left = l, Right = r, OpSpan = b.OpSpan, Span = b.Span };
            if (IsBoolOp(op)) res.Type = IrType.Bool;   // known now: type inference skips it
            else Track(res);
            return res;
        }

        static bool IsNullLit(RlExpr e) => Unparen(e) is RlConst c && c.Kind == RlConstKind.Null;

        IrExpr Unary(RlUnary u, Scope sc)
        {
            switch (u.Op)
            {
                case "-": return new IrUnary { Op = IrUnOp.Neg, Operand = Expr(u.Operand, sc), OpSpan = u.OpSpan, Span = u.Span };
                case "+": return new IrUnary { Op = IrUnOp.Plus, Operand = Expr(u.Operand, sc), OpSpan = u.OpSpan, Span = u.Span };
                case "!":
                    {
                        var inner = Unparen(u.Operand);
                        if (inner is RlCall c && c.FnName == "is.null" && !IsUserCallable("is.null", sc) && c.Args.Count == 1 && c.Args[0].Value != null)
                        {
                            var x = Expr(c.Args[0].Value, sc);
                            return new IrBinary { Op = IrBinOp.Ne, Left = x, Right = SynNull(), OpSpan = Span.Cover(u.OpSpan, c.Fn.Span), Span = u.Span, Type = IrType.Bool };
                        }
                        return new IrUnary { Op = IrUnOp.Not, Operand = Expr(u.Operand, sc), OpSpan = u.OpSpan, Span = u.Span, Type = IrType.Bool };
                    }
                case "~": Fail("formulas (~) are not supported in JavaFarmer", u.OpSpan); break;
                default: Fail("help (?) is not supported in JavaFarmer", u.OpSpan); break;
            }
            return null;
        }

        IrExpr IndexRead(RlIndex ix, Scope sc)
        {
            var key = SingleIndex(ix);
            var tu = Unparen(ix.Target);
            if (tu is RlIdent cat && IsCategory(cat.Name) && ResolveVar(cat.Name, sc, cat.Span, false) == null && Unparen(key) is RlStr ks)
                return new IrConst(cat.Name, ks.Value) { Span = ix.Span };
            var recv = Expr(ix.Target, sc);
            var kx = Expr(key, sc);
            if (IsMapIndex(ix.Target, key, kx, sc))
                return new IrOpCall(IrOp.MapGetOrNull, recv, kx) { NameSpan = ix.OpenSpan, Span = ix.Span };
            return ListIndex(recv, kx, key, ix.Span);
        }

        IrExpr DollarRead(RlDollar d, Scope sc)
        {
            if (d.At) Fail("slots (@) are not supported in JavaFarmer", d.OpSpan);
            var tu = Unparen(d.Target);
            if (tu is RlIdent cat && IsCategory(cat.Name) && ResolveVar(cat.Name, sc, cat.Span, false) == null)
                return new IrConst(cat.Name, d.Name) { Span = d.Span };
            var recv = Expr(d.Target, sc);
            var key = new IrLiteral { Kind = IrLitKind.String, Str = d.Name, Type = IrType.String, Span = d.NameSpan };
            return new IrOpCall(IrOp.MapGetOrNull, recv, key) { NameSpan = d.OpSpan, Span = d.Span };
        }

        IrLambda Lambda(RlFunction f, Scope sc)
        {
            var lam = new IrLambda { HeaderSpan = f.HeaderSpan, Span = f.Span };
            var fsc = FnScope(f, sc, lam.Params);
            lam.Body = FunctionBody(f.Body, fsc);
            ParamCopies(lam.Params, lam.Body);
            return lam;
        }

        // ================================================================== calls
        string ArgText(RlArg a) => Text(a.Span);

        /// <summary>Positional arguments (the first may be named firstName); anything else is "unused argument".</summary>
        List<RlExpr> Positional(RlCall c, int min, int max, string firstName)
        {
            var res = new List<RlExpr>();
            for (int i = 0; i < c.Args.Count; i++)
            {
                var a = c.Args[i];
                if (a.Value == null) { if (c.Args.Count == 1 && i == 0) break; Fail("Error in " + CallText(c) + ": argument " + (i + 1) + " is empty", a.Span); }
                if (a.Name != null && !(i == 0 && a.Name == firstName)) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(a) + ")", a.Span);
                res.Add(a.Value);
            }
            if (res.Count > max) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(c.Args[max]) + ")", c.Args[max].Span);
            if (res.Count < min) Fail("Error in " + CallText(c) + ": argument \"" + (firstName ?? "x") + "\" is missing, with no default", c.Span);
            return res;
        }

        /// <summary>R argument matching against fixed formal names (exact names, then positions). Missing = null.</summary>
        RlExpr[] Named(RlCall c, string[] formals)
        {
            var res = new RlExpr[formals.Length];
            var filled = new bool[formals.Length];
            foreach (var a in c.Args)
            {
                if (a.Name == null) continue;
                int k = Array.IndexOf(formals, a.Name);
                if (k < 0) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(a) + ")", a.Span);
                if (filled[k]) Fail("Error in " + CallText(c) + ": formal argument \"" + a.Name + "\" matched by multiple actual arguments", a.Span);
                filled[k] = true;
                res[k] = a.Value;
            }
            int next = 0;
            foreach (var a in c.Args)
            {
                if (a.Name != null) continue;
                while (next < formals.Length && filled[next]) next++;
                if (next >= formals.Length) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(a) + ")", a.Span);
                filled[next] = true;
                res[next] = a.Value;
                next++;
            }
            return res;
        }

        IrExpr Call(RlCall c, Scope sc)
        {
            var fnE = Unparen(c.Fn);
            if (fnE is RlIdent id)
            {
                var n = id.Name;
                if (IsUserCallable(n, sc)) return UserCall(c, id, sc);
                if (Specials.Contains(n)) return Special(c, id, sc);
                var b = GameApi.Find(n);
                if (b != null) return BuiltinCall(c, id, b, sc);
                switch (n)
                {
                    case "runif": case "sample": case "rnorm":
                        Fail(n + "() is not supported in JavaFarmer; use random() (the game's random number in [0, 1))", id.Span); break;
                    case "unique": case "union": case "intersect": case "setdiff":
                        Fail("sets (" + n + "()) are not supported in JavaFarmer; use a vector and %in%", id.Span); break;
                    case "library": case "require": case "requireNamespace":
                        Fail("packages (" + n + "()) are not supported in JavaFarmer", id.Span); break;
                    case "ifelse":
                        Fail("ifelse() is not supported in JavaFarmer; use if (cond) a else b", id.Span); break;
                    case "stop": case "tryCatch": case "try": case "warning":
                        Fail(n + "() is not supported in JavaFarmer (the game has no errors you can catch)", id.Span); break;
                }
                if (KnownBase.Contains(n)) Fail(n + "() is not supported in JavaFarmer", id.Span);
                if (topFnSyms.ContainsKey(n) && sc.IsTop) Fail("Error in " + CallText(c) + ": could not find function \"" + n + "\" (it is defined further down; define functions before you call them)", id.Span);
                var ext = lenientImports ? ResolveImport(n, true) : null;
                if (ext != null) return new IrCall { Kind = IrCallKind.Function, Function = ext, Args = PositionalValues(c, sc), NameSpan = id.Span, Span = c.Span };
                Fail("Error in " + CallText(c) + ": could not find function \"" + n + "\"", id.Span);
            }
            if (fnE is RlNamespace ns) Fail("the " + Text(ns.OpSpan) + " operator (package::name) is not supported in JavaFarmer; call the function directly", ns.OpSpan);
            var callee = Expr(c.Fn, sc);
            return new IrCall { Kind = IrCallKind.Value, Callee = callee, Args = PositionalValues(c, sc), Span = c.Span, NameSpan = c.Fn.Span };
        }

        List<IrExpr> PositionalValues(RlCall c, Scope sc)
        {
            var res = new List<IrExpr>();
            foreach (var a in c.Args)
            {
                if (a.Name != null) Fail("named arguments are only supported for functions defined at the top level of a file in JavaFarmer", a.NameSpan);
                if (a.Value == null) { if (c.Args.Count == 1) break; Fail("Error in " + CallText(c) + ": empty argument", a.Span); }
                res.Add(Expr(a.Value, sc));
            }
            return res;
        }

        IrExpr UserCall(RlCall c, RlIdent id, Scope sc)
        {
            var s = ResolveVar(id.Name, sc, id.Span, true);
            if (s == null) Fail("Error in " + CallText(c) + ": could not find function \"" + id.Name + "\"", id.Span);
            if (s.Kind == IrSymbolKind.Function || s.Kind == IrSymbolKind.ExternalFunction)
            {
                List<RlParam> ps = s.Kind == IrSymbolKind.Function ? (s.Tag as RlFunction)?.Params : s.Tag as List<RlParam>;
                var call = new IrCall { Kind = IrCallKind.Function, Function = s, NameSpan = id.Span, Span = c.Span };
                if (ps == null) call.Args = PositionalValues(c, sc);
                else call.Args = MatchArgs(c, ps, sc, s.Kind == IrSymbolKind.ExternalFunction);
                return call;
            }
            return new IrCall { Kind = IrCallKind.Value, Callee = new IrName(s) { Span = id.Span, NameSpan = id.Span }, Args = PositionalValues(c, sc), NameSpan = id.Span, Span = c.Span };
        }

        List<IrExpr> MatchArgs(RlCall c, List<RlParam> ps, Scope sc, bool external)
        {
            var slots = new RlExpr[ps.Count];
            var filled = new bool[ps.Count];
            foreach (var a in c.Args)
            {
                if (a.Name == null) continue;
                int k = ps.FindIndex(p => p.Name == a.Name);
                if (k < 0) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(a) + ")", a.Span);
                if (filled[k]) Fail("Error in " + CallText(c) + ": formal argument \"" + a.Name + "\" matched by multiple actual arguments", a.Span);
                filled[k] = a.Value != null;
                slots[k] = a.Value;
            }
            int next = 0;
            for (int i = 0; i < c.Args.Count; i++)
            {
                var a = c.Args[i];
                if (a.Name != null) continue;
                if (a.Value == null && c.Args.Count == 1) break;
                while (next < ps.Count && (filled[next] || slots[next] != null)) next++;
                if (next >= ps.Count) Fail("Error in " + CallText(c) + ": unused argument" + (c.Args.Count - i > 1 ? "s" : "") + " (" + ArgText(a) + ")", a.Span);
                filled[next] = a.Value != null;
                slots[next] = a.Value;
                next++;
            }
            int last = -1;
            for (int i = 0; i < ps.Count; i++) if (filled[i]) last = i;
            var res = new List<IrExpr>();
            for (int i = 0; i < ps.Count; i++)
            {
                if (filled[i]) { res.Add(Expr(slots[i], sc)); continue; }
                if (ps[i].Default == null) Fail("Error in " + CallText(c) + ": argument \"" + ps[i].Name + "\" is missing, with no default", c.Span);
                if (i > last) break;
                if (external) Fail("skipping a parameter with a default value is not supported for functions of other files in JavaFarmer; pass every argument up to the last one", c.Span);
                var d = Expr(ps[i].Default, top);
                MarkSynthetic(d);
                res.Add(d);
            }
            return res;
        }

        static void MarkSynthetic(IrExpr e) { foreach (var n in IrWalk.Descendants(e)) n.Synthetic = true; }

        IrExpr BuiltinCall(RlCall c, RlIdent id, ApiBuiltin b, Scope sc)
        {
            var ps = b.Params;
            var slots = new List<RlExpr>[ps.Count];
            for (int i = 0; i < ps.Count; i++) slots[i] = new List<RlExpr>();
            foreach (var a in c.Args)
            {
                if (a.Name == null) continue;
                int k = ps.FindIndex(p => p.Name == a.Name);
                if (k < 0 || a.Value == null) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(a) + ")", a.Span);
                if (slots[k].Count > 0 && !ps[k].Variadic) Fail("Error in " + CallText(c) + ": formal argument \"" + a.Name + "\" matched by multiple actual arguments", a.Span);
                slots[k].Add(a.Value);
            }
            int next = 0;
            foreach (var a in c.Args)
            {
                if (a.Name != null) continue;
                if (a.Value == null) { if (c.Args.Count == 1) break; Fail("Error in " + CallText(c) + ": empty argument", a.Span); }
                while (next < ps.Count && !ps[next].Variadic && slots[next].Count > 0) next++;
                if (next >= ps.Count) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(a) + ")", a.Span);
                slots[next].Add(a.Value);
                if (!ps[next].Variadic) next++;
            }
            var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = b.PythonName, NameSpan = id.Span, Span = c.Span };
            // a fixed return type is known now (type inference then skips the call); min / max / abs depend on the arguments
            if (!b.NumericLikeArgs && IrTypeInference.IsFullyKnown(b.ReturnType)) call.Type = b.ReturnType;
            bool gap = false;
            for (int i = 0; i < ps.Count; i++)
            {
                if (slots[i].Count == 0)
                {
                    if (!ps[i].Optional && !ps[i].Variadic) Fail("Error in " + CallText(c) + ": argument \"" + ps[i].Name + "\" is missing, with no default", c.Span);
                    gap = true;
                    continue;
                }
                if (gap) Fail("Error in " + CallText(c) + ": argument \"" + ps[i - 1].Name + "\" is missing (the game needs the arguments in order)", c.Span);
                foreach (var v in slots[i]) call.Args.Add(Expr(v, sc));
            }
            return call;
        }

        IrExpr Special(RlCall c, RlIdent id, Scope sc)
        {
            var n = id.Name;
            Span ns = id.Span;
            switch (n)
            {
                case "print":
                    {
                        if (c.Args.Count == 0 || c.Args[0].Value == null) Fail("Error in print(): argument \"x\" is missing, with no default", c.Span);
                        if (c.Args.Count > 1) Fail("print() shows one value in JavaFarmer; use cat(a, b, \"\\n\") to print several values", c.Args[1].Span);
                        if (c.Args[0].Name != null && c.Args[0].Name != "x") Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(c.Args[0]) + ")", c.Args[0].Span);
                        return new IrCall { Kind = IrCallKind.Builtin, Builtin = "print", Args = { Expr(c.Args[0].Value, sc) }, NameSpan = ns, Span = c.Span, Type = IrType.Void };
                    }
                case "cat":
                    {
                        string sep = " ";
                        var parts = new List<RlArg>();
                        foreach (var a in c.Args)
                        {
                            if (a.Name == "sep")
                            {
                                if (!(Unparen(a.Value) is RlStr ss) || (ss.Value != " " && ss.Value != "")) Fail("cat() only supports sep = \" \" and sep = \"\" in JavaFarmer", a.Span);
                                sep = ((RlStr)Unparen(a.Value)).Value;
                                continue;
                            }
                            if (a.Name != null) Fail("cat(" + a.Name + " = ...) is not supported in JavaFarmer", a.Span);
                            if (a.Value == null) { if (c.Args.Count == 1) break; Fail("Error in " + CallText(c) + ": empty argument", a.Span); }
                            parts.Add(a);
                        }
                        var vals = new List<IrExpr>();
                        for (int i = 0; i < parts.Count; i++)
                        {
                            var pu = Unparen(parts[i].Value);
                            if (i == parts.Count - 1 && pu is RlStr ls && ls.Value.EndsWith("\n", StringComparison.Ordinal))
                            {
                                var rest = ls.Value.Substring(0, ls.Value.Length - 1);
                                if (rest.Length == 0) break;
                                vals.Add(new IrLiteral { Kind = IrLitKind.String, Str = rest, Type = IrType.String, Span = ls.Span });
                                break;
                            }
                            vals.Add(Expr(parts[i].Value, sc));
                        }
                        var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = "print", NameSpan = ns, Span = c.Span, Type = IrType.Void };
                        if (sep == "" && vals.Count > 1)
                        {
                            IrExpr acc = vals[0];
                            for (int i = 1; i < vals.Count; i++)
                                acc = new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = vals[i], Type = IrType.String, Synthetic = true, OpSpan = ns, Span = c.Span };
                            call.Args.Add(acc);
                        }
                        else call.Args.AddRange(vals);
                        return call;
                    }
                case "message":
                    Fail("message() is not supported in JavaFarmer; use print() or cat()", ns);
                    break;
                case "paste":
                case "paste0":
                    {
                        string sep = n == "paste0" ? "" : " ";
                        var parts = new List<RlExpr>();
                        foreach (var a in c.Args)
                        {
                            if (a.Name == "sep" && n == "paste")
                            {
                                if (!(Unparen(a.Value) is RlStr ss)) { Fail("paste(sep = ...) needs a text constant in JavaFarmer", a.Span); return null; }
                                if (ss.Value.IndexOf('\n') >= 0) Fail("line breaks inside strings are not supported by the game", a.Span);
                                sep = ss.Value;
                                continue;
                            }
                            if (a.Name == "collapse") Fail("paste(collapse = ...) is not supported in JavaFarmer; join the values in a loop", a.Span);
                            if (a.Name != null) Fail("Error in " + CallText(c) + ": unused argument (" + ArgText(a) + ")", a.Span);
                            if (a.Value == null) continue;
                            parts.Add(a.Value);
                        }
                        if (parts.Count == 0) Fail(n + "() needs at least one value in JavaFarmer", c.Span);
                        var vals = parts.Select(p => Expr(p, sc)).ToList();
                        if (vals.Count == 1) return new IrCast { Target = IrType.String, Operand = vals[0], OpSpan = ns, Span = c.Span, Type = IrType.String };
                        IrExpr acc = vals[0];
                        for (int i = 1; i < vals.Count; i++)
                        {
                            if (sep.Length > 0)
                                acc = new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = new IrLiteral { Kind = IrLitKind.String, Str = sep, Type = IrType.String, Synthetic = true, Span = ns }, Type = IrType.String, Synthetic = true, OpSpan = ns, Span = c.Span };
                            acc = new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = vals[i], Type = IrType.String, OpSpan = ns, Span = c.Span };
                        }
                        return acc;
                    }
                case "as.character":
                    {
                        var a = Positional(c, 1, 1, "x");
                        return new IrCast { Target = IrType.String, Operand = Expr(a[0], sc), OpSpan = ns, Span = c.Span, Type = IrType.String };
                    }
                case "as.integer":
                case "trunc":
                    {
                        var a = Positional(c, 1, 1, "x");
                        return Track(new IrCast { Target = IrType.Int, Operand = Expr(a[0], sc), OpSpan = ns, Span = c.Span, Type = IrType.Int });
                    }
                case "as.numeric":
                case "as.double":
                    {
                        var a = Positional(c, 1, 1, "x");
                        return Track(new IrCast { Target = IrType.Float, Operand = Expr(a[0], sc), OpSpan = ns, Span = c.Span, Type = IrType.Float });
                    }
                case "floor":
                case "ceiling":
                case "sqrt":
                    {
                        var a = Positional(c, 1, 1, "x");
                        var op = n == "floor" ? IrOp.Floor : n == "ceiling" ? IrOp.Ceil : IrOp.Sqrt;
                        return new IrOpCall(op, Expr(a[0], sc)) { NameSpan = ns, Span = c.Span };
                    }
                case "round":
                    Fail("round() is not supported in JavaFarmer (R rounds halves to even: round(2.5) is 2); use floor(x + 0.5)", ns);
                    break;
                case "length":
                case "nchar":
                    {
                        var a = Positional(c, 1, 1, "x");
                        return new IrOpCall(IrOp.Len, Expr(a[0], sc)) { NameSpan = ns, Span = c.Span };
                    }
                case "c":
                case "list":
                    return Literal(c, n, sc);
                case "vector":
                    if (c.Args.Count > 1 || (c.Args.Count == 1 && c.Args[0].Value != null && !(Unparen(c.Args[0].Value) is RlStr)))
                        Fail("vector(mode, length) is not supported in JavaFarmer; start with list() or c() and add elements", c.Span);
                    return EmptyLiteral(c.Span);
                case "names":
                    {
                        var a = Positional(c, 1, 1, "x");
                        return new IrOpCall(IrOp.MapKeys, Expr(a[0], sc)) { NameSpan = ns, Span = c.Span };
                    }
                case "is.null":
                    {
                        var a = Positional(c, 1, 1, "x");
                        return new IrBinary { Op = IrBinOp.Eq, Left = Expr(a[0], sc), Right = SynNull(), OpSpan = ns, Span = c.Span, Type = IrType.Bool };
                    }
                case "append":
                    Fail("append() is only supported as v <- append(v, x) in JavaFarmer", ns);
                    break;
                case "seq_len": case "seq_along": case "seq":
                    Fail(n + "() is only supported as the sequence of a for loop in JavaFarmer: for (i in " + n + "(...))", ns);
                    break;
                case "match":
                    {
                        var a = Named(c, new[] { "x", "table" });
                        if (a[0] == null || a[1] == null) Fail("Error in " + CallText(c) + ": argument \"" + (a[0] == null ? "x" : "table") + "\" is missing, with no default", c.Span);
                        var x = Expr(a[0], sc);
                        var t = Expr(a[1], sc);
                        var idx = new IrOpCall(IrOp.ListIndexOf, t, x) { NameSpan = ns, Span = c.Span };
                        return new IrBinary { Op = IrBinOp.Add, Left = idx, Right = new IrLiteral { Kind = IrLitKind.Int, Int = 1, Type = IrType.Int, Synthetic = true, Span = ns }, Synthetic = true, OpSpan = ns, Span = c.Span };
                    }
                case "invisible":
                    {
                        var a = Positional(c, 1, 1, "x");
                        return Expr(a[0], sc);
                    }
                case "rep":
                    {
                        var a = Named(c, new[] { "x", "times" });
                        if (a[0] == null || a[1] == null) Fail("rep() needs a value and a count in JavaFarmer: rep(x, n)", c.Span);
                        var x = Expr(a[0], sc);
                        var times = Expr(a[1], sc);
                        return new IrOpCall(IrOp.ArrayNew, times, x) { NameSpan = ns, Span = c.Span };
                    }
                case "setNames":
                    {
                        var a = Named(c, new[] { "object", "nm" });
                        var vals = a[0] != null ? Unparen(a[0]) as RlCall : null;
                        var keys = a[1] != null ? Unparen(a[1]) as RlCall : null;
                        if (vals == null || keys == null || (vals.FnName != "list" && vals.FnName != "c") || keys.FnName != "c" || vals.Args.Count != keys.Args.Count
                            || vals.Args.Any(x => x.Name != null || x.Value == null) || keys.Args.Any(x => x.Name != null || x.Value == null))
                            Fail("setNames() is only supported as setNames(list(v1, v2), c(k1, k2)) in JavaFarmer", c.Span);
                        var m = new IrMapLit { Span = c.Span };
                        for (int i = 0; i < vals.Args.Count; i++)
                        {
                            m.Keys.Add(Expr(keys.Args[i].Value, sc));
                            m.Values.Add(Expr(vals.Args[i].Value, sc));
                        }
                        copySites.Add(m);
                        return m;
                    }
                case "return":
                    Fail("return() can only be used as a statement in JavaFarmer", ns);
                    break;
                case "source":
                    Fail("source() is only supported as a statement at the top level of a file in JavaFarmer", ns);
                    break;
                case "switch":
                    Fail("switch() as a value is only supported as the whole value of an assignment (y <- switch(...)) in JavaFarmer", ns);
                    break;
            }
            Fail(n + "() is not supported in JavaFarmer", ns);
            return null;
        }

        IrExpr EmptyLiteral(Span span)
        {
            if (emptyHintName != null && emptyHintScope != null)
            {
                var k = KindOfName(emptyHintName, emptyHintScope.Fn);
                if ((k & K.Map) != 0 && (k & K.List) == 0) return new IrMapLit { Span = span };
            }
            return new IrListLit { Span = span };
        }

        IrExpr Literal(RlCall c, string n, Scope sc)
        {
            if (c.Args.Count == 0 || (c.Args.Count == 1 && c.Args[0].Value == null && c.Args[0].Name == null)) return EmptyLiteral(c.Span);
            bool named = c.Args.Any(a => a.Name != null), positional = c.Args.Any(a => a.Name == null);
            foreach (var a in c.Args) if (a.Value == null) Fail("Error in " + CallText(c) + ": argument is empty", a.Span);
            if (named && positional) Fail("a " + (n == "c" ? "vector" : "list") + " with some named and some unnamed elements is not supported in JavaFarmer", c.Span);
            if (named)
            {
                var m = new IrMapLit { Span = c.Span };
                foreach (var a in c.Args)
                {
                    m.Keys.Add(new IrLiteral { Kind = IrLitKind.String, Str = a.Name, Type = IrType.String, Span = a.NameSpan });
                    m.Values.Add(Expr(a.Value, sc));
                }
                copySites.Add(m);
                return m;
            }
            var l = new IrListLit { Span = c.Span };
            foreach (var a in c.Args) l.Items.Add(Expr(a.Value, sc));
            if (n == "c") cLiterals.Add(l);
            copySites.Add(l);
            return l;
        }

        // ================================================================== after type inference
        static bool IsCollection(IrType t) => t != null && (t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Array || t.Kind == IrTypeKind.Map || t.Kind == IrTypeKind.Set);

        static bool Copyable(IrExpr e) => e is IrName n && n.Symbol != null && n.Symbol.Kind != IrSymbolKind.Function && n.Symbol.Kind != IrSymbolKind.ExternalFunction
                                         || e is IrIndex || e is IrOpCall o && o.Op == IrOp.MapGetOrNull;

        IrExpr CopyValue(IrExpr v)
        {
            if (v == null || !Copyable(v)) return v;
            var t = v.Type ?? IrType.Unknown;
            IrOp op;
            if (t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Array) op = IrOp.ListCopy;
            else if (t.Kind == IrTypeKind.Map) op = IrOp.MapCopy;
            else return v;
            return new IrOpCall(op, v) { Synthetic = true, Span = v.Span, NameSpan = v.Span, Type = t };
        }

        /// <summary>Parse tree → checked, typed IR with declarations placed (null + errors on failure).</summary>
        public static IrModule Build(RlProgram prog, string name, CompileOptions options, List<Diagnostic> errors)
        {
            var l = new RlLowering(prog.Source ?? "", name, options, errors);
            try
            {
                l.Analyze(prog);
                l.LowerModule(prog);
            }
            catch (Abort) { return null; }
            if (errors.Count > 0) return null;
            IrTypeInference.Infer(l.mod);
            l.Fix(errors);
            if (errors.Count > 0) return null;
            IrDeclarations.Place(l.mod);
            return l.mod;
        }

        void Fix(List<Diagnostic> errs)
        {
            // the earliest problem in the source wins
            void Err(string msg, Span s)
            {
                var d = new Diagnostic(msg, Math.Max(0, s.Start), Math.Max(0, s.End));
                if (errs.Count == 0) errs.Add(d);
                else if (d.Start < errs[0].Start) errs[0] = d;
            }
            // implicit returns of calls that return nothing stay plain calls
            foreach (var (owner, ret) in tentativeReturns)
            {
                if (!(ret.Value is IrCall call) || (call.Type?.Kind ?? IrTypeKind.Unknown) != IrTypeKind.Void) continue;
                int i = owner.IndexOf(ret);
                if (i < 0) continue;
                owner[i] = new IrExprStmt(call) { Span = ret.Span, Leading = ret.Leading, Trailing = ret.Trailing, BlankLinesBefore = ret.BlankLinesBefore };
            }
            foreach (var o in copyOps)
            {
                var t = o.Args[0].Type ?? IrType.Unknown;
                if (t.Kind == IrTypeKind.Map) o.Op = IrOp.MapCopy;
                o.Type = t;
            }
            foreach (var site in copySites)
            {
                switch (site)
                {
                    case IrAssign a:
                        if (a.Op != null) break;
                        if (convertedToGlobal.TryGetValue(a, out var g)) g.Init = CopyValue(g.Init);
                        else a.Value = CopyValue(a.Value);
                        break;
                    case IrOpCall op when op.Op == IrOp.ListAppend && op.Args.Count == 2: op.Args[1] = CopyValue(op.Args[1]); break;
                    case IrOpCall op2 when op2.Op == IrOp.ListInsert && op2.Args.Count == 3: op2.Args[2] = CopyValue(op2.Args[2]); break;
                    case IrListLit ll: for (int i = 0; i < ll.Items.Count; i++) ll.Items[i] = CopyValue(ll.Items[i]); break;
                    case IrMapLit ml: for (int i = 0; i < ml.Values.Count; i++) ml.Values[i] = CopyValue(ml.Values[i]); break;
                }
            }
            foreach (var (v, at) in combineChecks)
                if (IsCollection(v.Type)) Err("combining vectors with c() / append() is not supported in JavaFarmer; add the elements one at a time", at);
            foreach (var cl in cLiterals)
                foreach (var it in cl.Items)
                    if (IsCollection(it.Type)) Err("combining vectors with c() is not supported in JavaFarmer; use list(...) for a list of lists", it.Span);
            foreach (var n in fixChecks)
            {
                switch (n)
                {
                    case IrBinary b when b.Op == IrBinOp.Add || b.Op == IrBinOp.Sub || b.Op == IrBinOp.Mul || b.Op == IrBinOp.TrueDiv
                                      || b.Op == IrBinOp.IntDivFloor || b.Op == IrBinOp.ModFloor || b.Op == IrBinOp.Pow:
                        if ((b.Left.Type?.IsString ?? false) || (b.Right.Type?.IsString ?? false))
                            Err("Error in " + Text(b.Span) + ": non-numeric argument to binary operator (use paste() or paste0() to join text)", b.OpSpan);
                        break;
                    case IrCast c when (c.Target?.IsNumeric ?? false) && (c.Operand.Type?.IsString ?? false):
                        Err("converting text to a number is not supported in JavaFarmer", c.Span);
                        break;
                    case IrIndex ix when ix.Target.Type?.IsString ?? false:
                        Err("x[i] on a string does not select a character in R (a string is one value); character access is not supported in JavaFarmer", ix.Span);
                        break;
                }
            }
        }
    }
}
