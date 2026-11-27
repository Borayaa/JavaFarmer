using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Farm IR → idiomatic JavaFarmer Rust (edition 2024): `use farm::*;`, consts / static mut (inside unsafe) for module
    /// state, fn items, fn main for the entry / script statements, println! with format strings, Vec / HashMap / HashSet,
    /// Option for nullable values. Accepts typed IR (Rust, Java) and inferred IR (Python docs: JavaTypeGuess types it).
    /// Integers print as i32 (len() as i32, v[i as usize]); constructs Rust cannot express are reported in Problems.
    /// </summary>
    public sealed class RustPrinter : PrinterBase
    {
        public readonly List<string> Problems = new List<string>();

        public static string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            var p = new RustPrinter(module, options);
            try { p.Module(); }
            catch (Exception ex) { p.Problems.Add("internal printer error: " + ex.Message); problems = p.Problems; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
            problems = p.Problems;
            return p.Result;
        }

        readonly IrModule mod;
        readonly bool fromRust;
        JavaTypeGuess types;

        RustPrinter(IrModule module, PrintOptions options) : base(options)
        {
            mod = module ?? new IrModule();
            fromRust = mod.SourceLanguage == "rust";
        }

        void Problem(string what) { if (!Problems.Contains(what)) Problems.Add(what); }

        // ================================================================== names
        static readonly HashSet<string> Reserved = new HashSet<string>(RustLexer.Keywords)
        {
            "Vec", "String", "Option", "Some", "None", "HashMap", "HashSet", "Box", "Result", "Ok", "Err", "Self", "self", "str",
            "i8", "i16", "i32", "i64", "u8", "u16", "u32", "u64", "usize", "isize", "f32", "f64", "bool", "char",
            "Entity", "Item", "Ground", "Unlock", "Hat", "Leaderboard", "Direction", "Drone", "Position", "Companion",
            "North", "East", "South", "West", "main", "farm", "std", "crate", "println", "format",
        };

        static string Ident(string name)
        {
            if (string.IsNullOrEmpty(name)) return "x";
            if (name == "_") return "_";
            var n = name;
            if (n.StartsWith("__j", StringComparison.Ordinal)) n = "j" + n.Substring(3);
            var clean = new StringBuilder();
            foreach (var ch in n) clean.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            n = clean.ToString();
            if (n.Length == 0) n = "x";
            if (char.IsDigit(n[0])) n = "_" + n;
            if (Reserved.Contains(n)) n += "_";
            return n;
        }

        static string Upper(string name)
        {
            var s = PrinterBase.Snake(Ident(name)).ToUpperInvariant();
            return s.Length == 0 ? "X" : s;
        }

        readonly Dictionary<IrSymbol, string> names = new Dictionary<IrSymbol, string>();
        readonly HashSet<string> moduleNames = new HashSet<string>();

        string Name(IrSymbol s)
        {
            if (s == null) return "x";
            if (names.TryGetValue(s, out var n)) return n;
            n = Ident(s.Name);
            names[s] = n;
            return n;
        }

        // ================================================================== analysis
        IrBlock mainBlock;
        IrFunction entry;
        readonly HashSet<IrSymbol> localized = new HashSet<IrSymbol>();
        readonly HashSet<IrSymbol> statics = new HashSet<IrSymbol>();          // static mut
        readonly HashSet<IrSymbol> consts = new HashSet<IrSymbol>();
        readonly Dictionary<IrSymbol, IrExpr> lateInit = new Dictionary<IrSymbol, IrExpr>();
        readonly HashSet<IrSymbol> mutated = new HashSet<IrSymbol>();          // needs let mut
        readonly HashSet<IrSymbol> mutParams = new HashSet<IrSymbol>();        // &mut collection params
        readonly HashSet<IrSymbol> refParams = new HashSet<IrSymbol>();        // & collection / &str params
        readonly HashSet<IrSymbol> refVars = new HashSet<IrSymbol>();          // loop variables bound to references
        readonly HashSet<IrSymbol> strRefVars = new HashSet<IrSymbol>();       // &str locals
        readonly List<IrSymbol> undefinedVars = new List<IrSymbol>();
        readonly List<IrSymbol> undefinedFns = new List<IrSymbol>();
        readonly HashSet<IrStmt> skipped = new HashSet<IrStmt>();
        readonly HashSet<IrAssign> letAssign = new HashSet<IrAssign>();      // tuple unpack that declares its names
        readonly Dictionary<IrSymbol, IrFunction> fnDecl = new Dictionary<IrSymbol, IrFunction>();
        readonly HashSet<string> usedModules = new HashSet<string>();
        bool usesMap, usesSet;

        static bool IsUndefined(IrSymbol s) => s != null && Equals(s.Tag, PyLowering.UndefinedTag);

        IrType SymType(IrSymbol s)
        {
            if (s != null && fromRust && s.Type != null && Complete(s.Type)) return s.Type;
            if (s != null && optionalFloat.Contains(s)) return new IrType { Kind = IrTypeKind.Float, Nullable = true };
            if (s != null && unwrapped.Contains(s)) { var u = types.Sym(s); if (u == null || !u.IsKnown) u = s.Type ?? IrType.Unknown; return u.NonNull(); }
            var t = types.Sym(s);
            if (t == null || !t.IsKnown) t = s?.Type ?? IrType.Unknown;
            return t;
        }

        IrType TypeOf(IrExpr e)
        {
            if (e == null) return IrType.Unknown;
            if (e is IrName n && n.Symbol != null) { var st = SymType(n.Symbol); if (st.IsKnown) return st; }
            var t = types.TypeOf(e);
            return t ?? IrType.Unknown;
        }

        static IrSymbol NameSym(IrExpr e) => e is IrName n ? n.Symbol : null;

        void Analyse()
        {
            types = new JavaTypeGuess(mod);
            entry = mod.Entry;
            foreach (var f in mod.Functions) if (f.Symbol != null) fnDecl[f.Symbol] = f;
            // undefined names of docs snippets
            foreach (var n in IrWalk.Descendants(mod))
            {
                IrSymbol s = null;
                switch (n)
                {
                    case IrName nm: s = nm.Symbol; break;
                    case IrCall c when c.Kind == IrCallKind.Function: s = c.Function; if (s != null && s.IsExternal) usedModules.Add(s.Module); break;
                    case IrFuncRef fr: s = fr.Function; if (s != null && s.IsExternal) usedModules.Add(s.Module); break;
                }
                if (n is IrName en && en.Symbol != null && en.Symbol.IsExternal) usedModules.Add(en.Symbol.Module);
                if (!IsUndefined(s)) continue;
                if (s.Kind == IrSymbolKind.Function) { if (s.Function == null && !undefinedFns.Contains(s)) undefinedFns.Add(s); }
                else if (s.Kind == IrSymbolKind.Global && !undefinedVars.Contains(s)) undefinedVars.Add(s);
            }
            foreach (var im in IrWalk.Descendants(mod).OfType<IrImport>()) Problem("Python imports (" + im.Module + ") have no Rust form here: other code windows are modules (mod " + im.Module + ";)");
            // script modules: top-level statements form main; globals only they use become main locals
            var fnRefs = fnUsed;
            foreach (var f in mod.Functions.Where(f => !f.IsEntry))
                foreach (var d in IrWalk.Descendants(f))
                {
                    if (d is IrName nm && nm.Symbol != null) fnRefs.Add(nm.Symbol);
                    if (d is IrAssign a) foreach (var s in IrWalk.AssignedSymbols(a)) fnRefs.Add(s);
                }
            if (mod.IsScript)
            {
                var stmts = new List<IrStmt>();
                foreach (var g in mod.Globals) if (g.Symbol != null && !fnRefs.Contains(g.Symbol)) localized.Add(g.Symbol);
                foreach (var item in mod.Items)
                {
                    if (item is IrGlobal g && g.Symbol != null && localized.Contains(g.Symbol))
                        stmts.Add(new IrVarDecl { Symbol = g.Symbol, Init = g.ImplicitInit ? null : g.Init, TypeExplicit = g.TypeExplicit, Span = g.Span, Leading = g.Leading, Trailing = g.Trailing });
                    else if (item is IrStmt s) stmts.Add(s);
                }
                mainBlock = new IrBlock(stmts);
                // module-level variables assigned by top-level statements without an IrGlobal
                var declared = new HashSet<IrSymbol>(mod.Globals.Select(g => g.Symbol));
                foreach (var s in stmts)
                    foreach (var sym in IrWalk.AssignedSymbols(s))
                        if (sym != null && sym.Kind == IrSymbolKind.Global && !declared.Contains(sym) && !IsUndefined(sym))
                        {
                            declared.Add(sym);
                            if (fnRefs.Contains(sym)) { statics.Add(sym); extraStatics.Add(sym); }
                            else localized.Add(sym);
                        }
            }
            else if (entry != null) mainBlock = entry.Body;
            // module state
            var assignedAnywhere = new HashSet<IrSymbol>();
            foreach (var item in mod.Items)
            {
                if (item is IrGlobal) continue;
                foreach (var s in IrWalk.AssignedSymbols(item)) assignedAnywhere.Add(s);
            }
            foreach (var g in mod.Globals)
            {
                var s = g.Symbol;
                if (s == null || localized.Contains(s)) continue;
                var t = SymType(s);
                bool assigned = assignedAnywhere.Contains(s) || (fromRust ? !s.IsConst : false);
                bool constInit = g.Init == null || IsConstantInit(g.Init);
                if (!assigned && constInit && IsConstType(t, g.Init)) { consts.Add(s); continue; }
                if (IsCopyType(t))
                {
                    statics.Add(s);
                    if (!constInit) lateInit[s] = g.Init;
                    continue;
                }
                if (!assigned && constInit && t.IsString) { consts.Add(s); continue; }
                Problem("the module-level " + KindWord(t) + " `" + s.Name + "` is shared by functions: Rust has no safe mutable globals of that type (pass it as a parameter)");
                statics.Add(s);
            }
            // mutability, references
            foreach (var n in IrWalk.Descendants(mod)) Mutations(n);
            foreach (var f in mod.Functions)
                foreach (var p in f.Params)
                {
                    var t = SymType(p.Symbol);
                    if (IsCollection(t)) { if (mutated.Contains(p.Symbol)) mutParams.Add(p.Symbol); else refParams.Add(p.Symbol); }
                    else if (t.IsString && !mutated.Contains(p.Symbol)) refParams.Add(p.Symbol);
                }
            // locals passed to &mut params must be mut
            foreach (var c in IrWalk.Descendants(mod).OfType<IrCall>())
            {
                if (c.Kind != IrCallKind.Function || c.Function?.Function == null) continue;
                var ps = c.Function.Function.Params;
                for (int i = 0; i < c.Args.Count && i < ps.Count; i++)
                    if (mutParams.Contains(ps[i].Symbol) && NameSym(c.Args[i]) is IrSymbol a) mutated.Add(a);
            }
            MergeBareDecls();
            // numeric receivers (x.div_euclid(..), x.pow(..)): a literal-initialised local needs its type written
            foreach (var n in IrWalk.Descendants(mod))
            {
                IrExpr recv = null;
                if (n is IrBinary rb && (rb.Op == IrBinOp.IntDivFloor || rb.Op == IrBinOp.ModFloor || rb.Op == IrBinOp.Pow)) recv = rb.Left;
                else if (n is IrAssign ra && ra.Op != null && (ra.Op == IrBinOp.IntDivFloor || ra.Op == IrBinOp.ModFloor || ra.Op == IrBinOp.Pow)) recv = ra.Target;
                else if (n is IrOpCall ro && (ro.Op == IrOp.Floor || ro.Op == IrOp.Ceil || ro.Op == IrOp.Round || ro.Op == IrOp.Sqrt) && ro.Args.Count > 0) recv = ro.Args[0];
                if (recv == null) continue;
                if (recv is IrName rn && rn.Symbol != null) numRecv.Add(rn.Symbol);
                foreach (var d in IrWalk.Descendants(recv, false)) if (d is IrName dn && dn.Symbol != null) numRecv.Add(dn.Symbol);
            }
            foreach (var c in IrWalk.Descendants(mod).OfType<IrCall>())
            {
                if (c.Kind != IrCallKind.Builtin) continue;
                var api = GameApi.Find(c.Builtin);
                if (api == null) continue;
                for (int i = 0; i < c.Args.Count && i < api.Params.Count; i++)
                    if ((api.Params[i].TypeText ?? "").Replace(" ", "").StartsWith("map<string") && NameSym(c.Args[i]) is IrSymbol ms) strKeyMaps.Add(ms);
            }
            // a loop variable read after its loop before a fresh binding (Python keeps it; Rust's exists only inside the loop)
            var loopBlocks = IrWalk.Descendants(mod).OfType<IrBlock>().ToList();
            if (mainBlock != null) loopBlocks.Add(mainBlock);
            foreach (var blk in loopBlocks.Distinct())
                for (int k = 0; k < blk.Stmts.Count; k++)
                {
                    var ls = blk.Stmts[k];
                    var lvs = ls is IrRangeFor lrf ? new[] { lrf.Var } : ls is IrForEach lfe ? new[] { lfe.Var, lfe.ValueVar } : null;
                    if (lvs == null) continue;
                    foreach (var lv in lvs)
                        if (lv != null && ReadsBeforeRebind(blk.Stmts, k + 1, lv))
                            Problem("the loop variable `" + lv.Name + "` is used after its loop (a Rust loop variable only exists inside the loop)");
                }
            // Python locals holding an Option of a collection (get_cost) that are never tested for None: unwrap at the declaration
            if (!fromRust)
            {
                var tested = new HashSet<IrSymbol>();
                void Test(IrExpr x) { if (x is IrName tn && tn.Symbol != null) tested.Add(tn.Symbol); else if (x is IrUnary tu && tu.Op == IrUnOp.Not) Test(tu.Operand); }
                foreach (var n in IrWalk.Descendants(mod))
                    switch (n)
                    {
                        case IrBinary b when (b.Op == IrBinOp.Eq || b.Op == IrBinOp.Ne) && (IsNull(b.Left) || IsNull(b.Right)): Test(b.Left); Test(b.Right); break;
                        case IrBinary b when b.Op == IrBinOp.And || b.Op == IrBinOp.Or: Test(b.Left); Test(b.Right); break;
                        case IrUnary u when u.Op == IrUnOp.Not: Test(u.Operand); break;
                        case IrIf f: Test(f.Cond); break;
                        case IrWhile w: Test(w.Cond); break;
                        case IrConditional ce: Test(ce.Cond); break;
                        case IrAssign a when IsNull(a.Value): foreach (var s in IrWalk.AssignedSymbols(a)) tested.Add(s); break;
                        case IrReturn r: Test(r.Value); break;
                    }
                foreach (var v in IrWalk.Descendants(mod).OfType<IrVarDecl>())
                    if (v.Symbol != null && IsMeasure(v.Init) && tested.Contains(v.Symbol)) optionalFloat.Add(v.Symbol);
                foreach (var a in IrWalk.Descendants(mod).OfType<IrAssign>())
                    if (a.Op == null && NameSym(a.Target) is IrSymbol ms && IsMeasure(a.Value) && tested.Contains(ms)) optionalFloat.Add(ms);
                foreach (var v in IrWalk.Descendants(mod).OfType<IrVarDecl>().Concat(mainBlock == null ? Enumerable.Empty<IrVarDecl>() : IrWalk.Descendants(mainBlock).OfType<IrVarDecl>()))
                {
                    if (v.Init == null || v.Symbol == null || tested.Contains(v.Symbol) || statics.Contains(v.Symbol)) continue;
                    var it = TypeOf(v.Init);
                    if (it.Nullable && IsCollection(it.NonNull()) && !IsNull(v.Init)) unwrapped.Add(v.Symbol);
                }
            }
        }

        /// <summary>Is lv read by stmts[from..] before something binds it afresh (an assignment, a declaration, a loop over it)?</summary>
        static bool ReadsBeforeRebind(IReadOnlyList<IrStmt> stmts, int from, IrSymbol lv)
        {
            for (int j = from; j < stmts.Count; j++)
            {
                var s = stmts[j];
                switch (s)
                {
                    case IrRangeFor r when r.Var == lv: return Reads(r.Start, lv) || Reads(r.End, lv) || Reads(r.Step, lv);
                    case IrForEach f when f.Var == lv || f.ValueVar == lv: return Reads(f.Iterable, lv);
                    case IrVarDecl d when d.Symbol == lv: return Reads(d.Init, lv);
                    case IrAssign a when a.Op == null && NameSym(a.Target) == lv: return Reads(a.Value, lv);
                }
                if (Reads(s, lv)) return true;
            }
            return false;
        }

        static bool Reads(IrNode n, IrSymbol lv)
        {
            switch (n)
            {
                case null: return false;
                case IrName nm: return nm.Symbol == lv;
                case IrBlock b: return ReadsBeforeRebind(b.Stmts, 0, lv);
                case IrRangeFor r when r.Var == lv: return Reads(r.Start, lv) || Reads(r.End, lv) || Reads(r.Step, lv);
                case IrForEach f when f.Var == lv || f.ValueVar == lv: return Reads(f.Iterable, lv);
                case IrLambda _: return false;
            }
            foreach (var c in IrWalk.Children(n)) if (Reads(c, lv)) return true;
            return false;
        }

        readonly HashSet<IrSymbol> fnUsed = new HashSet<IrSymbol>();       // symbols read or written by non-entry functions
        readonly HashSet<IrSymbol> numRecv = new HashSet<IrSymbol>();      // receivers of numeric methods
        readonly HashSet<IrSymbol> optionalFloat = new HashSet<IrSymbol>();  // measure() results the code tests for None: Option<f64>
        readonly HashSet<IrSymbol> strKeyMaps = new HashSet<IrSymbol>();   // maps passed to a map<string, ..> API parameter: HashMap<&str, ..>

        string LocalTypeText(IrSymbol s, string text) => strKeyMaps.Contains(s) ? text.Replace("HashMap<String,", "HashMap<&str,") : text;

        readonly List<IrSymbol> extraStatics = new List<IrSymbol>();
        readonly HashSet<IrSymbol> unwrapped = new HashSet<IrSymbol>();       // Option locals printed as their value (if let / .unwrap() at the declaration)

        string RetValue(IrExpr v) => currentReturn != null && currentReturn.IsString && !currentReturn.Nullable ? Owned(v, currentReturn) : X(v, currentReturn).T;

        static string KindWord(IrType t) => t.Kind == IrTypeKind.Map ? "map" : t.Kind == IrTypeKind.Set ? "set" : t.IsListLike ? "list" : t.IsString ? "string" : "value";

        static bool IsCollection(IrType t) => t != null && (t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Array || t.Kind == IrTypeKind.Map || t.Kind == IrTypeKind.Set);

        static bool IsCopyType(IrType t)
        {
            if (t == null) return false;
            switch (t.Kind)
            {
                case IrTypeKind.Int: case IrTypeKind.Float: case IrTypeKind.Bool: return true;
                case IrTypeKind.Game: return t.GameName != "Drone";
                case IrTypeKind.Tuple: return t.Items != null && t.Items.All(IsCopyType);
            }
            return false;
        }

        static bool IsConstType(IrType t, IrExpr init)
        {
            if (IsCopyType(t)) return true;
            if (t.IsString) return true;
            if ((t.Kind == IrTypeKind.Array || t.Kind == IrTypeKind.List) && init is IrListLit l && l.Items.All(i => IsConstantInit(i))) return IsCopyType(t.Elem) || t.Elem.IsString || !t.Elem.IsKnown;
            return false;
        }

        static bool IsConstantInit(IrExpr e)
        {
            switch (e)
            {
                case null: return true;
                case IrLiteral _: case IrConst _: return true;
                case IrUnary u: return IsConstantInit(u.Operand);
                case IrBinary b: return b.Op != IrBinOp.Pow && b.Op != IrBinOp.Concat && b.Op != IrBinOp.IntDivFloor && b.Op != IrBinOp.ModFloor && IsConstantInit(b.Left) && IsConstantInit(b.Right);
                case IrListLit l: return l.Items.All(IsConstantInit);
                case IrTupleLit t: return t.Items.All(IsConstantInit);
                case IrName n: return n.Symbol != null && n.Symbol.Kind == IrSymbolKind.Global && n.Symbol.IsConst;
            }
            return false;
        }

        void MarkMut(IrExpr target)
        {
            switch (target)
            {
                case IrName n: if (n.Symbol != null) mutated.Add(n.Symbol); break;
                case IrIndex ix: MarkMut(ix.Target); break;
                case IrTupleLit t: foreach (var i in t.Items) MarkMut(i); break;
            }
        }

        readonly Dictionary<IrSymbol, int> assignCount = new Dictionary<IrSymbol, int>();

        void Mutations(IrNode n)
        {
            switch (n)
            {
                case IrAssign a:
                    MarkMut(a.Target);
                    break;
                case IrOpCall o when o.Args.Count > 0:
                    switch (o.Op)
                    {
                        case IrOp.ListAppend: case IrOp.ListInsert: case IrOp.ListPop: case IrOp.ListSet: case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent:
                        case IrOp.Clear: case IrOp.SetAdd: case IrOp.MapPut: case IrOp.MapRemove: case IrOp.MapRemoveIfPresent:
                            MarkMut(o.Args[0]);
                            break;
                    }
                    break;
            }
        }

        /// <summary>"let x; let y; (x, y) = v;" → "let (x, y) = v;"</summary>
        void MergeBareDecls()
        {
            var blocks = new List<IrBlock>();
            foreach (var f in mod.Functions) blocks.AddRange(IrWalk.Descendants(f.Body).OfType<IrBlock>());
            if (mainBlock != null && entry == null) blocks.AddRange(IrWalk.Descendants(mainBlock).OfType<IrBlock>());
            foreach (var b in blocks.Distinct())
                for (int i = 0; i < b.Stmts.Count; i++)
                {
                    if (!(b.Stmts[i] is IrAssign a) || a.Op != null || !(a.Target is IrTupleLit tl)) continue;
                    var tnames = Flatten(tl).ToList();
                    if (tnames.Any(x => x == null)) continue;
                    var decls = new List<IrVarDecl>();
                    for (int j = i - 1; j >= 0 && b.Stmts[j] is IrVarDecl v && v.Init == null && tnames.Contains(v.Symbol); j--) decls.Add(v);
                    if (decls.Count != tnames.Count) continue;
                    foreach (var d in decls) skipped.Add(d);
                    letAssign.Add(a);
                }
        }

        static IEnumerable<IrSymbol> Flatten(IrTupleLit t)
        {
            foreach (var i in t.Items)
            {
                if (i is IrTupleLit inner) { foreach (var s in Flatten(inner)) yield return s; }
                else yield return NameSym(i);
            }
        }

        // ================================================================== module
        void Module()
        {
            Analyse();
            undefinedVars.RemoveAll(u => u.Name == "__name__");
            AssignModuleNames();
            Comments(mod.Leading);
            bool hasDecls = mod.Functions.Any(f => !f.IsEntry) || statics.Count > 0 || consts.Count > 0;
            if (Options.Snippet)
            {
                if (!hasDecls && mainBlock != null && mainBlock.Stmts.Count == 1 && mainBlock.Stmts[0] is IrExprStmt lone && !IsStatementExpr(lone.Expr) && lone.Leading == null && lone.Trailing == null)
                {
                    Line(X(lone.Expr).T);
                    return;
                }
            }
            else
            {
                Line("use farm::*;");
                if (usesMap || usesSet) Line(usesMap && usesSet ? "use std::collections::{HashMap, HashSet};" : usesMap ? "use std::collections::HashMap;" : "use std::collections::HashSet;");
                foreach (var m in usedModules.OrderBy(x => x, StringComparer.Ordinal)) Line("mod " + m + ";");
                Line();
            }
            int last = 0;
            if (!Options.Snippet)
                foreach (var s in undefinedVars)
                {
                    if (UndefinedAsLocal(s)) continue;
                    Line("// defined elsewhere");
                    var t = SymType(s);
                    Line("const " + Name(s) + ": " + ConstTypeText(t, null) + " = " + (t.IsString ? "\"\"" : DefaultValue(t)) + ";");
                    last = 1;
                }
            foreach (var s in extraStatics)
            {
                Line("static mut " + Name(s) + ": " + TypeText(SymType(s)) + " = " + DefaultValue(SymType(s)) + ";");
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
                        Global(g);
                        last = 1;
                        break;
                    case IrFunction f:
                        if (f.IsEntry && Options.Snippet)
                        {
                            if (last != 0) Line();
                            LeadingComments(f);
                            Stmts(f.Body.Stmts);
                            Comments(f.Body.TrailingComments);
                            mainDone = true;
                            last = 2;
                            break;
                        }
                        if (last != 0) Line();
                        Function(f);
                        last = 2;
                        break;
                    case IrStmt st:
                        if (mainDone || mainBlock == null || entry != null) break;
                        mainDone = true;
                        if (Options.Snippet)
                        {
                            if (last == 2) Line();
                            Stmts(mainBlock.Stmts);
                            last = 2;
                            break;
                        }
                        if (last != 0) Line();
                        Line("fn main() {");
                        Indent();
                        LateInits();
                        UndefinedLocals();
                        Stmts(mainBlock.Stmts);
                        Dedent();
                        Line("}");
                        last = 2;
                        break;
                }
            }
            if (!Options.Snippet) UndefinedFunctions();
            Comments(mod.TrailingComments);
            // the use line goes first only when needed: re-scan for collections used by the body
            if (!Options.Snippet && (needMap || needSet) && !(usesMap || usesSet))
            {
                var text = Out.ToString();
                var line = needMap && needSet ? "use std::collections::{HashMap, HashSet};" : needMap ? "use std::collections::HashMap;" : "use std::collections::HashSet;";
                Out.Clear();
                Out.Append(text.Replace("use farm::*;" + Options.Newline, "use farm::*;" + Options.Newline + line + Options.Newline));
            }
        }

        bool needMap, needSet;

        /// <summary>A docs snippet's variable from an earlier block, used only by the main code: a main local.</summary>
        bool UndefinedAsLocal(IrSymbol s) => !Options.Snippet && !fnUsed.Contains(s) && mainBlock != null;

        void UndefinedLocals()
        {
            if (Options.Snippet) return;
            foreach (var s in undefinedVars)
            {
                if (!UndefinedAsLocal(s)) continue;
                var t = SymType(s);
                Line("// defined elsewhere");
                Line("let " + (mutated.Contains(s) ? "mut " : "") + Name(s) + ": " + LocalTypeText(s, QuietTypeText(t)) + " = " + DefaultValue(t) + ";");
            }
        }

        /// <summary>TypeText with unknown parts as i32 and Drone<_>, without a problem (an unused empty list, a docs placeholder).</summary>
        string QuietTypeText(IrType t)
        {
            int n = Problems.Count;
            var s = TypeText(t);
            while (Problems.Count > n) Problems.RemoveAt(Problems.Count - 1);
            return s.Replace("Drone<()>", "Drone<_>");
        }

        void LateInits()
        {
            foreach (var kv in lateInit) Line("unsafe { " + Name(kv.Key) + " = " + X(kv.Value, SymType(kv.Key)).T + "; }");
        }

        void AssignModuleNames()
        {
            var builtinNames = new HashSet<string>(RustApi.All().Select(e => e.RustName.Replace("r#", "")));
            foreach (var f in mod.Functions)
            {
                if (f.Symbol == null) continue;
                string n;
                if (f.IsEntry) n = "main";
                else
                {
                    n = Ident(f.Symbol.Name);
                    if (n == "main_" && !fromRust) n = "main_";
                    if (builtinNames.Contains(n) && !fromRust) n += "_fn";
                    var b = n; int k = 2;
                    while (moduleNames.Contains(n)) n = b + k++;
                }
                names[f.Symbol] = n;
                moduleNames.Add(n);
            }
            foreach (var s in undefinedFns) { var n = Ident(s.Name); names[s] = n; moduleNames.Add(n); }
            foreach (var g in mod.Globals.Select(g => g.Symbol).Concat(extraStatics))
            {
                if (g == null || localized.Contains(g)) continue;
                var b = fromRust ? Ident(g.Name) : Upper(g.Name);
                var n = b; int k = 2;
                while (moduleNames.Contains(n)) n = b + k++;
                names[g] = n;
                moduleNames.Add(n);
            }
            foreach (var s in undefinedVars) { names[s] = Ident(s.Name); }
        }

        void Global(IrGlobal g)
        {
            LeadingComments(g);
            var s = g.Symbol;
            var t = SymType(s);
            var pub = g.IsPrivate || !fromRust ? "" : "pub ";
            if (consts.Contains(s))
            {
                Line(pub + (fromRust && s.IsConst && IsStaticSource(g) ? "static " : "const ") + Name(s) + ": " + ConstTypeText(t, g.Init) + " = " + ConstValue(g.Init, t) + ";", g);
                return;
            }
            var init = lateInit.ContainsKey(s) || g.Init == null ? DefaultValue(t) : X(g.Init, t).T;
            Line(pub + "static mut " + Name(s) + ": " + TypeText(t) + " = " + init + ";", g);
        }

        static bool IsStaticSource(IrGlobal g) => false;

        string ConstValue(IrExpr init, IrType t)
        {
            if (init == null) return DefaultValue(t);
            if (init is IrListLit l) return "[" + string.Join(", ", l.Items.Select(i => X(i, t.Elem).T)) + "]";
            if (t.IsString && init is IrLiteral sl && sl.Kind == IrLitKind.String) return Str(sl.Str);
            return X(init, t).T;
        }

        string ConstTypeText(IrType t, IrExpr init)
        {
            if (t.IsString) return "&str";
            if ((t.Kind == IrTypeKind.Array || t.Kind == IrTypeKind.List) && init is IrListLit)
            {
                int n = init is IrListLit l ? l.Items.Count : 0;
                return "[" + (t.Elem.IsString ? "&str" : TypeText(t.Elem)) + "; " + n + "]";
            }
            return TypeText(t);
        }

        IrType RetType(IrFunction f)
        {
            if (f.ReturnType != null && f.ReturnType.IsKnown) return f.ReturnType;
            return f.Symbol != null ? types.Ret(f.Symbol) ?? IrType.Void : IrType.Void;
        }

        IrType currentReturn = IrType.Void;
        IrFunction currentFn;

        void Function(IrFunction f)
        {
            LeadingComments(f);
            currentFn = f;
            var ps = new List<string>();
            foreach (var p in f.Params)
            {
                if (p.Default != null) defaults[f.Symbol] = f;
                var pt = SymType(p.Symbol);
                ps.Add((mutated.Contains(p.Symbol) && !mutParams.Contains(p.Symbol) && !refParams.Contains(p.Symbol) ? "mut " : "") + Name(p.Symbol) + ": " + ParamType(p.Symbol, pt));
            }
            var rt = RetType(f);
            currentReturn = rt;
            string header;
            if (f.IsEntry) header = "fn main()";
            else header = (f.IsPrivate || !fromRust ? "" : "pub ") + "fn " + Name(f.Symbol) + "(" + string.Join(", ", ps) + ")" + (rt.Kind == IrTypeKind.Void || !rt.IsKnown || rt.Kind == IrTypeKind.Null ? "" : " -> " + TypeText(rt));
            if (!rt.IsKnown && !f.IsEntry && IrWalk.Descendants(f.Body, false).OfType<IrReturn>().Any(r => r.Value != null)) Problem("the return type of " + f.Name + " is unknown");
            Line(header + " {", f);
            Indent();
            if (f.IsEntry) { LateInits(); UndefinedLocals(); }
            var stmts = f.Body.Stmts;
            bool returnsValue = rt.Kind != IrTypeKind.Void && rt.IsKnown && rt.Kind != IrTypeKind.Null;
            if (returnsValue && stmts.Count > 0 && stmts[stmts.Count - 1] is IrReturn last && last.Value != null && last.Leading == null && !(last.Value is IrConditional))
            {
                Stmts(stmts.Take(stmts.Count - 1));
                Line(RetValue(last.Value), last);
            }
            else
            {
                Stmts(stmts);
                if (returnsValue && CanComplete(f.Body)) Line(DefaultValue(rt));
            }
            Comments(f.Body.TrailingComments);
            Dedent();
            Line("}");
            currentReturn = IrType.Void;
            currentFn = null;
        }

        readonly Dictionary<IrSymbol, IrFunction> defaults = new Dictionary<IrSymbol, IrFunction>();

        string ParamType(IrSymbol p, IrType t)
        {
            if (mutParams.Contains(p)) return "&mut " + TypeText(t);
            if (refParams.Contains(p))
            {
                if (t.IsString) return "&str";
                if (t.IsListLike) return "&[" + TypeText(t.Elem) + "]";
                return "&" + TypeText(t);
            }
            if (t.Kind == IrTypeKind.Func) return FnTypeText(t);
            if (!t.IsKnown || t.Kind == IrTypeKind.Any) { Problem("the type of parameter `" + p.Name + "` is unknown"); return "i32"; }
            return TypeText(t);
        }

        string FnTypeText(IrType t)
        {
            var ret = t.Elem;
            var ps = t.Items ?? new List<IrType>();
            return "fn(" + string.Join(", ", ps.Select(TypeText)) + ")" + (ret == null || ret.Kind == IrTypeKind.Void || !ret.IsKnown ? "" : " -> " + TypeText(ret));
        }

        void UndefinedFunctions()
        {
            foreach (var s in undefinedFns)
            {
                var calls = IrWalk.Descendants(mod).OfType<IrCall>().Where(c => c.Kind == IrCallKind.Function && c.Function == s).ToList();
                int arity = calls.Count == 0 ? 0 : calls.Max(c => c.Args.Count);
                var ps = new List<string>();
                for (int i = 0; i < arity; i++)
                {
                    IrType t = IrType.Unknown;
                    foreach (var c in calls) if (i < c.Args.Count) t = IrType.Join(t, TypeOf(c.Args[i]));
                    ps.Add("_" + (char)('a' + Math.Min(i, 25)) + ": " + (t.IsKnown && t.Kind != IrTypeKind.Null && t.Kind != IrTypeKind.Any ? TypeText(t) : "i32"));
                }
                var rt = types.Ret(s);
                bool used = IrWalk.Descendants(mod).Any(x => !(x is IrExprStmt) && IrWalk.Children(x).Any(ch => calls.Contains(ch)));
                Line();
                Line("// defined elsewhere");
                if (used && (!rt.IsKnown || rt.Kind == IrTypeKind.Void || rt.Kind == IrTypeKind.Any)) { Problem("the result type of " + s.Name + "() is unknown"); rt = IrType.Int; }
                bool ret = rt.IsKnown && rt.Kind != IrTypeKind.Void && rt.Kind != IrTypeKind.Null && rt.Kind != IrTypeKind.Any;
                Line("fn " + Name(s) + "(" + string.Join(", ", ps) + ")" + (ret ? " -> " + TypeText(rt) : "") + " {");
                if (ret) { Indent(); Line(DefaultValue(rt)); Dedent(); }
                Line("}");
            }
        }

        // ================================================================== types
        string TypeText(IrType t)
        {
            if (t == null) return "i32";
            string core;
            switch (t.Kind)
            {
                case IrTypeKind.Int: core = "i32"; break;
                case IrTypeKind.Float: core = "f64"; break;
                case IrTypeKind.Bool: core = "bool"; break;
                case IrTypeKind.String: core = "String"; break;
                case IrTypeKind.List: case IrTypeKind.Array: core = "Vec<" + TypeText(t.Elem) + ">"; break;
                case IrTypeKind.Set: needSet = true; core = "HashSet<" + TypeText(t.Elem) + ">"; break;
                case IrTypeKind.Map: needMap = true; core = "HashMap<" + TypeText(t.Key) + ", " + TypeText(t.Elem) + ">"; break;
                case IrTypeKind.Tuple:
                    if (t.Items != null && t.Items.Count == 2 && t.Items.All(i => i.Kind == IrTypeKind.Int)) core = "Position";
                    else core = "(" + string.Join(", ", (t.Items ?? new List<IrType>()).Select(TypeText)) + ((t.Items?.Count ?? 0) == 1 ? ",)" : ")");
                    break;
                case IrTypeKind.Game: core = t.GameName == "Drone" ? "Drone<()>" : t.GameName; break;
                case IrTypeKind.Func: core = FnTypeText(t); break;
                case IrTypeKind.Void: core = "()"; break;
                default:
                    Problem("a value of unknown type");
                    core = "i32";
                    break;
            }
            return t.Nullable && t.Kind != IrTypeKind.Null ? "Option<" + core + ">" : core;
        }

        string DefaultValue(IrType t)
        {
            if (t == null) return "0";
            if (t.Nullable) return "None";
            switch (t.Kind)
            {
                case IrTypeKind.Int: return "0";
                case IrTypeKind.Float: return "0.0";
                case IrTypeKind.Bool: return "false";
                case IrTypeKind.String: return "String::new()";
                case IrTypeKind.List: case IrTypeKind.Array: return "Vec::new()";
                case IrTypeKind.Map: needMap = true; return "HashMap::new()";
                case IrTypeKind.Set: needSet = true; return "HashSet::new()";
                case IrTypeKind.Tuple: return "(" + string.Join(", ", (t.Items ?? new List<IrType>()).Select(DefaultValue)) + ((t.Items?.Count ?? 0) == 1 ? ",)" : ")");
                case IrTypeKind.Game:
                    switch (t.GameName)
                    {
                        case "Entity": return "Entity::Grass";
                        case "Item": return "Item::Hay";
                        case "Ground": return "Ground::Grassland";
                        case "Unlock": return "Unlock::Speed";
                        case "Hat": return "Hat::Straw_Hat";
                        case "Leaderboard": return "Leaderboard::Hay";
                        case "Direction": return "Direction::North";
                        case "Position": return "(0, 0)";
                        case "Companion": return "(Entity::Grass, (0, 0))";
                    }
                    break;
            }
            Problem("no default value for a value of unknown type");
            return "0";
        }

        // ================================================================== statements
        void Stmts(IEnumerable<IrStmt> stmts)
        {
            foreach (var s in stmts) Stmt(s);
        }

        void Block(IrBlock b)
        {
            Indent();
            if (b != null) { Stmts(b.Stmts); Comments(b.TrailingComments); }
            Dedent();
        }

        bool OutEndsWith(string s)
        {
            if (Out.Length < s.Length) return false;
            for (int i = 0; i < s.Length; i++) if (Out[Out.Length - s.Length + i] != s[i]) return false;
            return true;
        }

        static bool IsJump(IrStmt s) => s is IrBreak || s is IrContinue || s is IrReturn;

        void Stmt(IrStmt s)
        {
            if (s == null || skipped.Contains(s)) return;
            if (s.BlankLinesBefore > 0 && Out.Length > 0 && !OutEndsWith("{" + Options.Newline) && !OutEndsWith(Options.Newline + Options.Newline)) Line();
            LeadingComments(s);
            switch (s)
            {
                case IrBlock b:
                    if (IfLetBlock(b)) return;
                    Line("{"); Block(b); Line("}"); return;
                case IrVarDecl v: VarDecl(v); return;
                case IrAssign a: Assign(a); return;
                case IrExprStmt es: ExprStmt(es); return;
                case IrIf f: If(f, false); return;
                case IrWhile w:
                    if (w.Cond is IrLiteral wl && wl.Kind == IrLitKind.Bool && wl.Bool) Line("loop {", w);
                    else Line("while " + Cond(w.Cond) + " {", w);
                    Block(w.Body); Line("}");
                    return;
                case IrLoop lp: Line("loop {", lp); Block(lp.Body); Line("}"); return;
                case IrDoWhile dw:
                    if (IrWalk.Descendants(dw.Body, false).Any(x => x is IrContinue)) Problem("a do-while loop with continue");
                    Line("loop {", dw);
                    Indent();
                    Stmts(dw.Body.Stmts);
                    Comments(dw.Body.TrailingComments);
                    Line("if !(" + Cond(dw.Cond) + ") {");
                    Indent(); Line("break;"); Dedent();
                    Line("}");
                    Dedent();
                    Line("}");
                    return;
                case IrClassicFor cf: ClassicFor(cf); return;
                case IrRangeFor rf: RangeFor(rf); return;
                case IrForEach fe: ForEach(fe); return;
                case IrSwitch sw: Switch(sw); return;
                case IrBreak _: Line("break;", s); return;
                case IrContinue _: Line("continue;", s); return;
                case IrReturn r:
                    if (r.Value == null) Line("return;", r);
                    else if (r.Value is IrConditional rc) Line("return " + CondExpr(rc, currentReturn) + ";", r);
                    else Line("return " + RetValue(r.Value) + ";", r);
                    return;
                case IrImport imp: Line("// import " + imp.Module + " (use mod " + imp.Module + "; at the top)", imp); return;
            }
            Problem("statement " + s.GetType().Name);
            Line("// " + s.GetType().Name);
        }

        bool IsRefLocal(IrSymbol s) => refVars.Contains(s) || refParams.Contains(s) || mutParams.Contains(s);

        void VarDecl(IrVarDecl v)
        {
            var s = v.Symbol;
            var t = SymType(s);
            var mut = (fromRust ? !s.IsConst : mutated.Contains(s)) ? "mut " : "";
            if (v.Init == null)
            {
                Line("let " + mut + Name(s) + (t.IsKnown && t.Kind != IrTypeKind.Any ? ": " + TypeText(t) : "") + ";", v);
                return;
            }
            string ann = "";
            var init = v.Init;
            bool emptyColl = (init is IrListLit l0 && l0.Items.Count == 0) || (init is IrMapLit m0 && m0.Keys.Count == 0) || (init is IrSetLit s0 && s0.Items.Count == 0) || (init is IrLiteral nl && nl.Kind == IrLitKind.Null);
            if (emptyColl || (v.TypeExplicit && fromRust) || IsMeasure(init))
            {
                if (t.IsKnown && t.Kind != IrTypeKind.Any && Complete(t)) ann = ": " + TypeText(t).Replace("Drone<()>", "Drone<_>");
                else if (IsMeasure(init)) ann = ": f64";
                else if (emptyColl && IsCollection(t)) ann = ": " + QuietTypeText(t);
            }
            ann = LocalTypeText(s, ann);
            if (ann == "" && numRecv.Contains(s) && IsNumLit(init) && (t.Kind == IrTypeKind.Int || t.Kind == IrTypeKind.Float)) ann = t.Kind == IrTypeKind.Int ? ": i32" : ": f64";
            if (ann == "" && !fromRust && unwrapped.Contains(s)) { Line("let " + mut + Name(s) + " = " + Wrap(X(init).T, X(init).Prec, PrecPostfix) + ".unwrap();", v); return; }
            string val = init is IrConditional ce ? CondExpr(ce, t) : X(init, t).T;
            if (t.IsString && !(init is IrConditional) && IsStrRef(init) && !mutated.Contains(s) && !v.TypeExplicit) strRefVars.Add(s);
            else if (t.IsString && IsStrRef(init)) val = ToOwned(val, init);
            Line("let " + mut + Name(s) + ann + " = " + val + ";", v);
        }

        static bool IsNumLit(IrExpr e) => (e is IrLiteral l && (l.Kind == IrLitKind.Int || l.Kind == IrLitKind.Float)) || (e is IrUnary u && u.Op == IrUnOp.Neg && IsNumLit(u.Operand));

        static bool Complete(IrType t)
        {
            if (t == null || !t.IsKnown || t.Kind == IrTypeKind.Any) return false;
            switch (t.Kind)
            {
                case IrTypeKind.List: case IrTypeKind.Set: case IrTypeKind.Array: return Complete(t.Elem);
                case IrTypeKind.Map: return Complete(t.Key) && Complete(t.Elem);
                case IrTypeKind.Tuple: return t.Items != null && t.Items.All(Complete);
            }
            return true;
        }

        static bool IsMeasure(IrExpr e) => e is IrCall c && c.Kind == IrCallKind.Builtin && c.Builtin == "measure";

        string ToOwned(string text, IrExpr e)
        {
            if (e is IrLiteral) return text + ".to_string()";
            return text + ".to_string()";
        }

        bool IsStrRef(IrExpr e)
        {
            if (e is IrLiteral l && l.Kind == IrLitKind.String) return true;
            if (e is IrName n && n.Symbol != null && (strRefVars.Contains(n.Symbol) || (refParams.Contains(n.Symbol) && SymType(n.Symbol).IsString))) return true;
            if (e is IrName gn && gn.Symbol != null && consts.Contains(gn.Symbol) && SymType(gn.Symbol).IsString) return true;
            return false;
        }

        void Assign(IrAssign a)
        {
            if (a.Target is IrTupleLit tl)
            {
                if (letAssign.Contains(a))
                {
                    var pat = TuplePattern(tl);
                    string ann = "";
                    if (IsMeasure(a.Value)) ann = ": Position";
                    var mutAny = Flatten(tl).Any(x => mutated.Contains(x) && !IsTupleOnlyAssign(x, a));
                    Line("let " + pat + ann + " = " + TupleValue(a.Value, tl) + ";", a);
                    return;
                }
                Line(TuplePattern(tl, assign: true) + " = " + TupleValue(a.Value, tl) + ";", a);
                return;
            }
            var tt = TypeOf(a.Target);
            if (a.Target is IrIndex ix && TypeOf(ix.Target).Kind == IrTypeKind.Map && a.Op == null)
            {
                var mt = TypeOf(ix.Target);
                Line(Recv(ix.Target) + ".insert(" + X(ix.Index, mt.Key).T + ", " + Owned(a.Value, mt.Elem) + ");", a);
                return;
            }
            if (a.Target is IrIndex ix2 && TypeOf(ix2.Target).Kind == IrTypeKind.Tuple) Problem("assigning to a tuple item (tuples cannot be changed)");
            var target = Target(a.Target);
            bool isStatic = a.Target is IrName sn && statics.Contains(sn.Symbol);
            string text;
            if (a.Op == null)
            {
                if (a.Value is IrConditional ce) text = target + " = " + CondExpr(ce, tt);
                else text = target + " = " + Owned(a.Value, tt);
            }
            else
            {
                var op = a.Op.Value;
                var vt = TypeOf(a.Value);
                if (a.TruncateToInt)
                    text = target + " = (" + target + " as f64 " + OpText(op) + " " + X(a.Value, IrType.Float).T + ") as i32";
                else switch (op)
                    {
                        case IrBinOp.Add: case IrBinOp.Sub: case IrBinOp.Mul: case IrBinOp.ModTrunc:
                            text = target + " " + OpText(op) + "= " + X(a.Value, tt.IsKnown ? tt : vt).T; break;
                        case IrBinOp.TrueDiv:
                            if (tt.Kind == IrTypeKind.Int) text = target + " = (" + target + " as f64 / " + X(a.Value, IrType.Float).T + ") as i32";
                            else text = target + " /= " + X(a.Value, IrType.Float).T;
                            break;
                        case IrBinOp.IntDivTrunc: text = target + " /= " + X(a.Value, tt).T; break;
                        case IrBinOp.IntDivFloor: text = target + " = " + target + ".div_euclid(" + X(a.Value, tt).T + ")"; break;
                        case IrBinOp.ModFloor: text = target + " = " + target + ".rem_euclid(" + X(a.Value, tt).T + ")"; break;
                        case IrBinOp.Pow: text = target + " = " + PowText(new P(target, PrecPostfix + 1), tt, a.Value); break;
                        case IrBinOp.Concat:
                            {
                                var vtx = TypeOf(a.Value);
                                if (a.Value is IrLiteral sl && sl.Kind == IrLitKind.String) text = target + " += " + Str(sl.Str);
                                else if (vtx.IsString) text = target + " += &" + Wrap(X(a.Value).T, X(a.Value).Prec, PrecUnary);
                                else text = target + " += &" + Wrap(X(a.Value).T, X(a.Value).Prec, PrecPostfix) + ".to_string()";
                                break;
                            }
                        default: text = target + " " + OpText(op) + "= " + X(a.Value, tt).T; break;
                    }
            }
            Line((isStatic ? "unsafe { " : "") + text + ";" + (isStatic ? " }" : ""), a);
        }

        bool IsTupleOnlyAssign(IrSymbol s, IrAssign a) => true;

        string TuplePattern(IrTupleLit t, bool assign = false)
        {
            var parts = new List<string>();
            foreach (var i in t.Items)
            {
                if (i is IrTupleLit inner) parts.Add(TuplePattern(inner, assign));
                else if (i is IrName n && n.Symbol != null) parts.Add((!assign && mutated.Contains(n.Symbol) && CountAssigns(n.Symbol) > 1 ? "mut " : "") + Name(n.Symbol));
                else parts.Add(assign ? Target(i) : "_");
            }
            return "(" + string.Join(", ", parts) + ")";
        }

        int CountAssigns(IrSymbol s) => IrWalk.Descendants(mod).OfType<IrAssign>().Count(a => IrWalk.AssignedSymbols(a).Contains(s)) + IrWalk.Descendants(mod).OfType<IrOpCall>().Count(o => o.Args.Count > 0 && NameSym(o.Args[0]) == s);

        string TupleValue(IrExpr v, IrTupleLit target)
        {
            if (v is IrTupleLit vl && vl.Items.Count == target.Items.Count)
            {
                var parts = new List<string>();
                for (int i = 0; i < vl.Items.Count; i++) parts.Add(X(vl.Items[i], target.Items[i] is IrName n ? SymType(n.Symbol) : null).T);
                return "(" + string.Join(", ", parts) + ")";
            }
            var vt = TypeOf(v);
            var x = X(v);
            if (vt.Nullable) return Wrap(x.T, x.Prec, PrecPostfix) + ".unwrap()";
            return x.T;
        }

        string Target(IrExpr t)
        {
            switch (t)
            {
                case IrName n: return NameRef(n, write: true);
                case IrIndex ix: return IndexText(ix, write: true);
            }
            return X(t).T;
        }

        void ExprStmt(IrExprStmt es)
        {
            var e = es.Expr;
            if (e is IrOpCall o)
            {
                switch (o.Op)
                {
                    case IrOp.ListSet:
                        Line(Recv(o.Args[0]) + "[" + IndexArg(o.Args[1]) + "] = " + Owned(o.Args[2], TypeOf(o.Args[0]).Elem) + ";", es);
                        return;
                    case IrOp.MapPut:
                        {
                            var mt = TypeOf(o.Args[0]);
                            Line(Recv(o.Args[0]) + ".insert(" + X(o.Args[1], mt.Key).T + ", " + Owned(o.Args[2], mt.Elem) + ");", es);
                            return;
                        }
                }
            }
            if (e is IrConditional) { Problem("a ?: expression used as a statement"); }
            var x = X(e);
            bool isStatic = IrWalk.Descendants(e, false).OfType<IrName>().Any(n => statics.Contains(n.Symbol));
            Line(x.T + ";", es);
        }

        bool IsStatementExpr(IrExpr e) => e is IrCall || e is IrOpCall;

        string Cond(IrExpr c)
        {
            var t = TypeOf(c);
            if (t.Nullable && t.Kind != IrTypeKind.Bool) { var o = X(c); return Wrap(o.T, o.Prec, PrecPostfix) + ".is_some()"; }
            var x = X(c, IrType.Bool);
            if (t.Kind == IrTypeKind.Bool || !t.IsKnown) return x.T;
            if (t.Nullable) return Wrap(x.T, x.Prec, PrecPostfix) + ".is_some()";
            if (t.Kind == IrTypeKind.Int) return Wrap(x.T, x.Prec, 8) + " != 0";
            if (t.Kind == IrTypeKind.Float) return Wrap(x.T, x.Prec, 8) + " != 0.0";
            if (IsCollection(t) || t.IsString) return "!" + Wrap(x.T, x.Prec, PrecPostfix) + ".is_empty()";
            return x.T;
        }

        /// <summary>Python's  if __name__ == "__main__":  (a Rust program always starts in main).</summary>
        static bool IsNameGuard(IrExpr c) =>
            c is IrBinary b && b.Op == IrBinOp.Eq && b.Left is IrName n && n.Symbol != null && n.Symbol.Name == "__name__" && b.Right is IrLiteral l && l.Kind == IrLitKind.String && l.Str == "__main__";

        /// <summary>The Rust lowering's  { x = e; if x != None { .. } }  (and the tuple form) → if let Some(x) = e { .. }.</summary>
        bool IfLetBlock(IrBlock b)
        {
            if (b.Stmts.Count != 2 || !(b.Stmts[0] is IrVarDecl d) || d.Init == null || !(b.Stmts[1] is IrIf f) || b.TrailingComments != null) return false;
            if (!(f.Cond is IrBinary c) || c.Op != IrBinOp.Ne || !IsNull(c.Right) || NameSym(c.Left) != d.Symbol) return false;
            if (!TypeOf(d.Init).Nullable && !IsMeasure(d.Init)) return false;
            // the bound name is not used after the if (it is a block-local)
            string pat = Name(d.Symbol);
            var then = f.Then;
            var skip = new List<IrStmt>();
            if (then != null && then.Stmts.Count > 0)
            {
                // tuple form: let a; let x; let y; (a, (x, y)) = tmp;
                int k = 0;
                while (k < then.Stmts.Count && then.Stmts[k] is IrVarDecl vd && vd.Init == null) k++;
                if (k < then.Stmts.Count && then.Stmts[k] is IrAssign ta && ta.Op == null && ta.Target is IrTupleLit tl && NameSym(ta.Value) == d.Symbol)
                {
                    var names = Flatten(tl).ToList();
                    if (names.All(x => x != null) && names.Count == k && then.Stmts.Take(k).All(s => names.Contains(((IrVarDecl)s).Symbol))
                        && !IrWalk.Descendants(then, false).OfType<IrName>().Any(n => n.Symbol == d.Symbol && n != ta.Value))
                    {
                        pat = TuplePattern(tl);
                        for (int i = 0; i <= k; i++) skip.Add(then.Stmts[i]);
                    }
                }
            }
            bool mut = pat == Name(d.Symbol) && mutated.Contains(d.Symbol);
            LeadingComments(d);
            var head = "if let Some(" + (mut ? "mut " : "") + pat + ") = " + X(d.Init).T + " {";
            Line(head, f);
            foreach (var s in skip) skipped.Add(s);
            unwrapped.Add(d.Symbol);
            Block(then);
            if (f.Else is IrIf ei && ei.Leading == null) { Line("}"); If(ei, true); return true; }
            if (f.Else != null)
            {
                Line("} else {");
                if (f.Else is IrBlock eb) Block(eb);
                else { Indent(); Stmt(f.Else); Dedent(); }
            }
            Line("}");
            return true;
        }

        void If(IrIf f, bool elseIf)
        {
            if (!elseIf && IsNameGuard(f.Cond) && f.Else == null && !fromRust)
            {
                Stmts(f.Then.Stmts);
                return;
            }
            var head = (elseIf ? "} else if " : "if ") + Cond(f.Cond) + " {";
            if (elseIf) { TrimLastNewline(); var lineStart = Out.ToString().LastIndexOf('\n') + 1; Out.Length = lineStart; Out.Append(IndentText); Out.Append(head + TrailingComment(f)).Append(Options.Newline); }
            else Line(head, f);
            Block(f.Then);
            if (f.Else is IrIf ei && ei.Leading == null) { Line("}"); If(ei, true); return; }
            if (f.Else != null)
            {
                Line("} else {");
                if (f.Else is IrBlock eb) Block(eb);
                else { Indent(); Stmt(f.Else); Dedent(); }
            }
            Line("}");
        }

        void ClassicFor(IrClassicFor f)
        {
            // for (int i = a; i < b; i++) → for i in a..b
            if (f.Init.Count == 1 && f.Init[0] is IrVarDecl d && d.Init != null && f.Cond is IrBinary c && NameSym(c.Left) == d.Symbol
                && f.Update.Count == 1 && f.Update[0] is IrAssign u && NameSym(u.Target) == d.Symbol && u.Op != null && u.Value is IrLiteral ul && ul.Kind == IrLitKind.Int && ul.Int > 0
                && !IrWalk.Assigns(f.Body, d.Symbol) && SymType(d.Symbol).Kind == IrTypeKind.Int)
            {
                bool up = u.Op == IrBinOp.Add && (c.Op == IrBinOp.Lt || c.Op == IrBinOp.Le);
                bool down = u.Op == IrBinOp.Sub && (c.Op == IrBinOp.Gt || c.Op == IrBinOp.Ge);
                if (up || down)
                {
                    var rf = new IrRangeFor { Var = d.Symbol, Body = f.Body, Leading = null, Trailing = f.Trailing };
                    if (up) { rf.Start = d.Init; rf.End = c.Right; rf.Inclusive = c.Op == IrBinOp.Le; rf.Step = ul.Int == 1 ? null : u.Value; }
                    else
                    {
                        rf.Start = d.Init; rf.End = c.Right; rf.Inclusive = c.Op == IrBinOp.Ge;
                        rf.Step = IrLiteral.OfInt(-ul.Int);
                    }
                    RangeFor(rf);
                    return;
                }
            }
            if (IrWalk.Descendants(f.Body, false).Any(x => x is IrContinue)) Problem("a C-style for loop with continue");
            Line("{");
            Indent();
            foreach (var i in f.Init) Stmt(i);
            Line("while " + (f.Cond == null ? "true" : Cond(f.Cond)) + " {");
            Indent();
            Stmts(f.Body.Stmts);
            Comments(f.Body.TrailingComments);
            foreach (var u2 in f.Update) Stmt(u2);
            Dedent();
            Line("}");
            Dedent();
            Line("}");
        }

        static bool IsLit(IrExpr e, out long v)
        {
            v = 0;
            if (e is IrLiteral l && l.Kind == IrLitKind.Int) { v = l.Int; return true; }
            if (e is IrUnary u && u.Op == IrUnOp.Neg && u.Operand is IrLiteral ul && ul.Kind == IrLitKind.Int) { v = -ul.Int; return true; }
            return false;
        }

        string Bound(IrExpr e, long delta)
        {
            if (e == null) return (delta).ToString(CultureInfo.InvariantCulture);
            if (IsLit(e, out var v)) return (v + delta).ToString(CultureInfo.InvariantCulture);
            if (delta == 0) return X(e, IrType.Int).T;
            if (e is IrBinary b && b.Op == IrBinOp.Sub && IsLit(b.Right, out var r) && r == -delta) return X(b.Left, IrType.Int).T;
            var x = X(e, IrType.Int);
            return Wrap(x.T, x.Prec, 10) + (delta > 0 ? " + " + delta : " - " + (-delta));
        }

        void RangeFor(IrRangeFor rf)
        {
            var v = rf.Var == null ? "_" : Name(rf.Var);
            string start = rf.Start == null ? "0" : null;
            long step = 1;
            bool stepLit = true;
            if (rf.Step != null) { if (IsLit(rf.Step, out var s)) step = s; else stepLit = false; }
            string iter;
            if (!stepLit)
            {
                iter = "(" + RangeText(rf.Start, rf.End, rf.Inclusive) + ").step_by(" + Wrap(X(rf.Step, IrType.Int).T, X(rf.Step).Prec, 12) + " as usize)";
            }
            else if (step > 0)
            {
                var r = RangeText(rf.Start, rf.End, rf.Inclusive);
                iter = step == 1 ? r : "(" + r + ").step_by(" + step + ")";
            }
            else
            {
                // range(start, end, -k): start, start-k, ... > end  →  ((end + 1)..=start).rev()
                string lo = rf.Inclusive ? Bound(rf.End, 0) : Bound(rf.End, 1);
                string r;
                if (rf.Start is IrBinary sb && sb.Op == IrBinOp.Sub && IsLit(sb.Right, out var one) && one == 1)
                    r = lo + ".." + Bound(sb.Left, 0);
                else if (IsLit(rf.Start, out var sv)) r = lo + ".." + (sv + 1).ToString(CultureInfo.InvariantCulture);
                else r = lo + "..=" + Bound(rf.Start, 0);
                iter = "(" + r + ").rev()" + (step == -1 ? "" : ".step_by(" + (-step) + ")");
            }
            Line("for " + v + " in " + iter + " {", rf);
            Block(rf.Body);
            Line("}");
        }

        string RangeText(IrExpr start, IrExpr end, bool inclusive)
        {
            var s = start == null ? "0" : Wrap(X(start, IrType.Int).T, X(start).Prec, 5);
            var e = Wrap(X(end, IrType.Int).T, X(end).Prec, 5);
            return s + (inclusive ? "..=" : "..") + e;
        }

        void ForEach(IrForEach fe)
        {
            var it = fe.Iterable;
            var itT = TypeOf(it);
            string src;
            string pat;
            string Bind(IrSymbol s, IrType elem)
            {
                if (s == null) return "_";
                if (IsCopyType(elem) || !elem.IsKnown || elem.IsGame("Drone")) return "&" + Name(s);
                refVars.Add(s);
                return Name(s);
            }
            if (it is IrOpCall kc && kc.Op == IrOp.MapKeys) { it = kc.Args[0]; itT = TypeOf(it); }
            if (itT.Nullable) Problem("iterating over a value that may be None");
            if (it is IrOpCall vc && vc.Op == IrOp.MapValues)
            {
                var mt = TypeOf(vc.Args[0]);
                src = Recv(vc.Args[0]) + ".values()";
                pat = Bind(fe.Var, mt.Elem ?? IrType.Unknown);
            }
            else if (itT.Kind == IrTypeKind.Map)
            {
                if (fe.ValueVar != null)
                {
                    src = "&" + Wrap(X(it).T, X(it).Prec, PrecUnary);
                    if (IsRefLocal(NameSym(it))) src = X(it).T;
                    pat = "(" + Bind(fe.Var, itT.Key) + ", " + Bind(fe.ValueVar, itT.Elem) + ")";
                }
                else
                {
                    src = Recv(it) + ".keys()";
                    pat = Bind(fe.Var, itT.Key);
                }
            }
            else if (itT.IsString)
            {
                Problem("iterating over the characters of a string");
                src = Recv(it) + ".chars()";
                pat = Name(fe.Var);
            }
            else if (it is IrListLit ll)
            {
                src = "[" + string.Join(", ", ll.Items.Select(i => X(i, itT.Elem).T)) + "]";
                pat = fe.Var == null ? "_" : Name(fe.Var);
            }
            else if (it is IrName || it is IrIndex)
            {
                var elem = itT.Elem ?? IrType.Unknown;
                var x = X(it);
                src = IsRefLocal(NameSym(it)) ? x.T : "&" + Wrap(x.T, x.Prec, PrecUnary);
                pat = Bind(fe.Var, elem);
            }
            else
            {
                src = X(it).T;
                pat = fe.Var == null ? "_" : Name(fe.Var);
            }
            Line("for " + pat + " in " + src + " {", fe);
            Block(fe.Body);
            Line("}");
        }

        void Switch(IrSwitch sw)
        {
            var st = TypeOf(sw.Selector);
            var sel = X(sw.Selector);
            var selText = st.IsString && !st.Nullable && !IsStrRef(sw.Selector) ? Wrap(sel.T, sel.Prec, PrecPostfix) + ".as_str()" : sel.T;
            bool boolTrue = false, boolFalse = false;
            foreach (var c in sw.Cases) foreach (var l in c.Labels) if (l is IrLiteral bl && bl.Kind == IrLitKind.Bool) { if (bl.Bool) boolTrue = true; else boolFalse = true; }
            Line("match " + selText + " {", sw);
            Indent();
            bool hasDefault = false;
            foreach (var c in sw.Cases)
            {
                LeadingComments(c);
                string label;
                if (c.IsDefault) { label = "_"; hasDefault = true; }
                else
                {
                    var ls = new List<string>();
                    foreach (var l in c.Labels)
                    {
                        if (!(l is IrLiteral || l is IrConst || (l is IrUnary u && u.Operand is IrLiteral))) Problem("a match label that is not a constant");
                        var lt = X(l, st.NonNull());
                        var txt = l is IrLiteral sl && sl.Kind == IrLitKind.String ? Str(sl.Str) : lt.T;
                        if (st.Nullable && !(l is IrLiteral nl && nl.Kind == IrLitKind.Null)) txt = "Some(" + txt + ")";
                        ls.Add(txt);
                    }
                    label = string.Join(" | ", ls);
                }
                var body = c.Body;
                if (body.Stmts.Count == 1 && body.TrailingComments == null && body.Stmts[0] is IrExprStmt es && es.Leading == null && es.Trailing == null && IsStatementExpr(es.Expr) && IsUnitCall(es.Expr) && !(es.Expr is IrOpCall op && (op.Op == IrOp.MapPut || op.Op == IrOp.ListSet)))
                    Line(label + " => " + X(es.Expr).T + ",");
                else if (body.Stmts.Count == 0 && body.TrailingComments == null)
                    Line(label + " => {}");
                else
                {
                    Line(label + " => {");
                    Block(body);
                    Line("}");
                }
            }
            if (!hasDefault && !(boolTrue && boolFalse)) Line("_ => {}");
            Dedent();
            Line("}");
        }

        /// <summary>A call whose value is () in Rust (so it can be a match arm next to other unit arms).</summary>
        bool IsUnitCall(IrExpr e)
        {
            if (e is IrCall c && c.Kind == IrCallKind.Builtin)
            {
                if (c.Builtin == "print" || c.Builtin == "quick_print") return true;
                var api = GameApi.Find(c.Builtin);
                var rt = (api?.ReturnText ?? "").Replace(" ", "");
                return rt == "" || rt == "void" || rt == "None";
            }
            var t = TypeOf(e);
            return t.Kind == IrTypeKind.Void;
        }

        static bool CanComplete(IrStmt s)
        {
            switch (s)
            {
                case null: return true;
                case IrReturn _: return false;
                case IrBlock b: return b.Stmts.Count == 0 || CanComplete(b.Stmts[b.Stmts.Count - 1]);
                case IrIf f: return f.Else == null || CanComplete(f.Then) || CanComplete(f.Else);
                case IrLoop lp: return IrWalk.Descendants(lp.Body, false).Any(x => x is IrBreak);
                case IrWhile w: return !(w.Cond is IrLiteral l && l.Kind == IrLitKind.Bool && l.Bool) || IrWalk.Descendants(w.Body, false).Any(x => x is IrBreak);
                case IrSwitch sw: return !sw.Cases.Any(c => c.IsDefault) || sw.Cases.Any(c => CanComplete(c.Body));
            }
            return true;
        }

        // ================================================================== expressions
        struct P
        {
            public string T; public int Prec;
            public P(string t, int prec) { T = t; Prec = prec; }
        }

        // Rust precedence (higher binds tighter)
        const int PrecOr = 5, PrecAnd = 6, PrecCmp = 7, PrecAdd = 10, PrecMul = 11, PrecCast = 12;
        new const int PrecUnary = 13, PrecPostfix = 14, PrecAtom = 15;

        static int RPrec(IrBinOp op)
        {
            switch (op)
            {
                case IrBinOp.Or: return PrecOr;
                case IrBinOp.And: return PrecAnd;
                case IrBinOp.Eq: case IrBinOp.Ne: case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge: return PrecCmp;
                case IrBinOp.Add: case IrBinOp.Sub: case IrBinOp.Concat: return PrecAdd;
                default: return PrecMul;
            }
        }

        static string OpText(IrBinOp op)
        {
            switch (op)
            {
                case IrBinOp.Add: case IrBinOp.Concat: return "+";
                case IrBinOp.Sub: return "-";
                case IrBinOp.Mul: return "*";
                case IrBinOp.TrueDiv: case IrBinOp.IntDivTrunc: case IrBinOp.IntDivFloor: return "/";
                case IrBinOp.ModTrunc: case IrBinOp.ModFloor: return "%";
                case IrBinOp.Eq: return "==";
                case IrBinOp.Ne: return "!=";
                case IrBinOp.Lt: return "<";
                case IrBinOp.Le: return "<=";
                case IrBinOp.Gt: return ">";
                case IrBinOp.Ge: return ">=";
                case IrBinOp.And: return "&&";
                case IrBinOp.Or: return "||";
            }
            return "?";
        }

        static string Str(string s) => "\"" + RustEscape(s ?? "") + "\"";

        static string RustEscape(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        static string FormatEscape(string s) => RustEscape(s).Replace("{", "{{").Replace("}", "}}");

        string NumLit(IrLiteral l, IrType want)
        {
            if (l.Kind == IrLitKind.Int)
            {
                if (want != null && want.Kind == IrTypeKind.Float) return l.Int.ToString(CultureInfo.InvariantCulture) + ".0";
                return l.Int.ToString(CultureInfo.InvariantCulture);
            }
            var f = PyFormat.Double(l.Float);
            if (want != null && want.Kind == IrTypeKind.Int && l.Float == Math.Floor(l.Float)) return ((long)l.Float).ToString(CultureInfo.InvariantCulture);
            return f;
        }

        /// <summary>Expression text converted to the wanted type (numbers, Option, String / &amp;str).</summary>
        P X(IrExpr e, IrType want = null)
        {
            var p = XCore(e, want);
            if (want == null || !want.IsKnown) return p;
            var t = TypeOf(e);
            if (!t.IsKnown) return p;
            if (e is IrLiteral lit && lit.Kind == IrLitKind.Null) return p;
            if (want.Kind == IrTypeKind.Float && t.Kind == IrTypeKind.Int && !(e is IrLiteral)) return new P(Wrap(p.T, p.Prec, PrecCast) + " as f64", PrecCast);
            if (want.Kind == IrTypeKind.Int && t.Kind == IrTypeKind.Float && !(e is IrLiteral) && !fromRust) return new P(Wrap(p.T, p.Prec, PrecCast) + " as i32", PrecCast);
            if (!want.Nullable && t.Nullable && want.Kind != IrTypeKind.Any && !(e is IrLiteral)) return new P(Wrap(p.T, p.Prec, PrecPostfix) + ".unwrap()", PrecPostfix);
            if (want.Nullable && !t.Nullable && t.Kind != IrTypeKind.Null && want.Kind != IrTypeKind.Unknown && !IsMeasure(e)) return new P("Some(" + p.T + ")", PrecAtom);
            return p;
        }

        /// <summary>A value stored in an owned place (String var, collection element): &amp;str → .to_string().</summary>
        string Owned(IrExpr e, IrType want)
        {
            if (want != null && want.IsString && IsStrRef(e)) { var x = X(e); return Wrap(x.T, x.Prec, PrecPostfix) + ".to_string()"; }
            if (e is IrName n && n.Symbol != null && refVars.Contains(n.Symbol)) { var x = X(e, want); return Wrap(x.T, x.Prec, PrecPostfix) + ".clone()"; }
            if (e is IrConditional ce) return CondExpr(ce, want);
            return X(e, want).T;
        }

        string Recv(IrExpr e)
        {
            var x = X(e);
            return Wrap(x.T, x.Prec, PrecPostfix);
        }

        string CondExpr(IrConditional c, IrType want)
        {
            var t = want != null && want.IsKnown ? want : TypeOf(c);
            string Branch(IrExpr b) => b is IrConditional inner ? "{ " + CondExpr(inner, t) + " }" : (t.IsString ? Owned(b, t) : X(b, t).T);
            var els = c.Else is IrConditional ec ? CondExpr(ec, t) : null;
            return "if " + Cond(c.Cond) + " { " + (t.IsString ? Owned(c.Then, t) : X(c.Then, t).T) + " } else " + (els != null ? els : "{ " + Branch(c.Else) + " }");
        }

        string NameRef(IrName n, bool write = false)
        {
            var s = n.Symbol;
            if (s == null) return "x";
            if (s.IsExternal) return s.Module + "::" + Ident(s.Name);
            var nm = Name(s);
            if (statics.Contains(s) && !write) return "unsafe { " + nm + " }";
            if (write && refVars.Contains(s)) return "*" + nm;
            return nm;
        }

        P XCore(IrExpr e, IrType want)
        {
            switch (e)
            {
                case null: return new P("()", PrecAtom);
                case IrLiteral l:
                    switch (l.Kind)
                    {
                        case IrLitKind.Int: return new P(NumLit(l, want), l.Int < 0 ? PrecUnary : PrecAtom);
                        case IrLitKind.Float: return new P(NumLit(l, want), l.Float < 0 ? PrecUnary : PrecAtom);
                        case IrLitKind.Bool: return new P(l.Bool ? "true" : "false", PrecAtom);
                        case IrLitKind.String: return new P(Str(l.Str), PrecAtom);
                        default: return new P("None", PrecAtom);
                    }
                case IrName n:
                    {
                        if (n.Symbol != null && (Array.IndexOf(GameApi.ConstantCategories, n.Symbol.Name) >= 0) && IsUndefined(n.Symbol))
                            Problem(n.Symbol.Name + " (the whole category) cannot be used as a value in Rust");
                        var t = NameRef(n);
                        if (n.Symbol != null && statics.Contains(n.Symbol)) return new P(t, PrecAtom);
                        if (n.Symbol != null && refVars.Contains(n.Symbol) && IsCopyType(SymType(n.Symbol))) return new P("*" + t, PrecUnary);
                        return new P(t, PrecAtom);
                    }
                case IrConst c: return new P(ConstText(c), PrecAtom);
                case IrUnary u:
                    {
                        if (u.Op == IrUnOp.Not)
                        {
                            var ot = TypeOf(u.Operand);
                            if (ot.Nullable) { var o = X(u.Operand); return new P(Wrap(o.T, o.Prec, PrecPostfix) + ".is_none()", PrecPostfix); }
                            if (ot.IsKnown && ot.Kind != IrTypeKind.Bool) { var cnd = Cond(u.Operand); return new P("!(" + cnd + ")", PrecUnary); }
                            var x = X(u.Operand, IrType.Bool);
                            return new P("!" + Wrap(x.T, x.Prec, PrecUnary), PrecUnary);
                        }
                        if (u.Op == IrUnOp.Neg)
                        {
                            if (u.Operand is IrLiteral nl && (nl.Kind == IrLitKind.Int || nl.Kind == IrLitKind.Float)) return new P("-" + NumLit(nl, want), PrecUnary);
                            var x = X(u.Operand, want);
                            return new P("-" + Wrap(x.T, x.Prec, PrecUnary), PrecUnary);
                        }
                        return X(u.Operand, want);
                    }
                case IrBinary b: return Binary(b, want);
                case IrConditional ce: return new P(CondExpr(ce, want), PrecAtom);
                case IrCall c: return Call(c);
                case IrOpCall o: return OpCall(o);
                case IrIndex ix: return new P(IndexText(ix), PrecPostfix);
                case IrListLit ll:
                    {
                        IrType seen = null;
                        foreach (var it in ll.Items)
                        {
                            var ity = TypeOf(it);
                            if (!ity.IsKnown || ity.Kind == IrTypeKind.Any || ity.Kind == IrTypeKind.Null) continue;
                            if (seen == null) { seen = ity; continue; }
                            bool same = seen.Kind == ity.Kind && (seen.Kind != IrTypeKind.Game || seen.GameName == ity.GameName);
                            if (!same && !(seen.IsNumeric && ity.IsNumeric)) { Problem("a list holding values of different types (a Rust Vec holds one element type)"); break; }
                        }
                        var et = (want != null && (want.Kind == IrTypeKind.List || want.Kind == IrTypeKind.Array) ? want.Elem : null) ?? TypeOf(ll).Elem;
                        if (et != null && !et.IsKnown) et = null;
                        var items = ll.Items.Select(i => et != null && et.IsString ? Owned(i, et) : X(i, et).T);
                        return new P("vec![" + string.Join(", ", items) + "]", PrecAtom);
                    }
                case IrSetLit sl:
                    {
                        needSet = true;
                        if (sl.Items.Count == 0) return new P("HashSet::new()", PrecPostfix);
                        var et = (want != null && want.Kind == IrTypeKind.Set ? want.Elem : null) ?? TypeOf(sl).Elem;
                        return new P("HashSet::from([" + string.Join(", ", sl.Items.Select(i => et != null && et.IsString ? Owned(i, et) : X(i, et).T)) + "])", PrecPostfix);
                    }
                case IrMapLit ml:
                    {
                        needMap = true;
                        if (ml.Keys.Count == 0) return new P("HashMap::new()", PrecPostfix);
                        var mt = want != null && want.Kind == IrTypeKind.Map ? want : TypeOf(ml);
                        var parts = new List<string>();
                        for (int i = 0; i < ml.Keys.Count; i++)
                            parts.Add("(" + Owned(ml.Keys[i], mt.Key) + ", " + Owned(ml.Values[i], mt.Elem) + ")");
                        return new P("HashMap::from([" + string.Join(", ", parts) + "])", PrecPostfix);
                    }
                case IrTupleLit tl:
                    {
                        var wt = want != null && want.Kind == IrTypeKind.Tuple && want.Items != null && want.Items.Count == tl.Items.Count ? want.Items : null;
                        var parts = tl.Items.Select((i, k) => X(i, wt?[k]).T).ToList();
                        return new P("(" + string.Join(", ", parts) + (parts.Count == 1 ? ",)" : ")"), PrecAtom);
                    }
                case IrLambda lam: return new P(Lambda(lam), PrecOr - 1);
                case IrFuncRef fr:
                    {
                        if (fr.Function != null)
                        {
                            if (fr.Function.IsExternal) return new P(fr.Function.Module + "::" + Ident(fr.Function.Name), PrecAtom);
                            return new P(Name(fr.Function), PrecAtom);
                        }
                        var an = RustApi.ApiName(fr.Builtin) ?? fr.Builtin;
                        if (an.EndsWith("!")) Problem("the builtin " + fr.Builtin + " used as a value");
                        var api = GameApi.Find(fr.Builtin);
                        if (api != null && api.Params.Count > 0) Problem("the builtin " + fr.Builtin + " with parameters used as a value");
                        return new P(an, PrecAtom);
                    }
                case IrCast c:
                    {
                        var x = X(c.Operand);
                        var ot = TypeOf(c.Operand);
                        if (c.IsTypeAssertion) return X(c.Operand, c.Target);
                        switch (c.Target.Kind)
                        {
                            case IrTypeKind.Int:
                                if (ot.Kind == IrTypeKind.Int) return x;
                                return new P(Wrap(x.T, x.Prec, PrecCast) + " as i32", PrecCast);
                            case IrTypeKind.Float:
                                if (ot.Kind == IrTypeKind.Float) return x;
                                return new P(Wrap(x.T, x.Prec, PrecCast) + " as f64", PrecCast);
                            case IrTypeKind.String:
                                if (ot.IsString && !(c.Operand is IrLiteral)) return x;
                                return new P(Wrap(x.T, x.Prec, PrecPostfix) + ".to_string()", PrecPostfix);
                        }
                        return x;
                    }
            }
            Problem("expression " + e.GetType().Name);
            return new P("()", PrecAtom);
        }

        string ConstText(IrConst c)
        {
            if (c.Category == "Directions") return "Direction::" + c.Name;
            var en = GameApi.GameTypeOfCategory(c.Category) ?? c.Category;
            return en + "::" + (RustApi.IsRustKeyword(c.Name) ? "r#" + c.Name : c.Name);
        }

        P Binary(IrBinary b, IrType want)
        {
            var lt = TypeOf(b.Left); var rt = TypeOf(b.Right);
            if (b.Op == IrBinOp.Concat || (b.Op == IrBinOp.Add && (lt.IsString || rt.IsString)))
                return new P(FormatMacro("format", FlattenConcat(b)), PrecPostfix);
            int prec = RPrec(b.Op);
            bool cmp = prec == PrecCmp;
            // None comparisons: x == None → x.is_none()
            if ((b.Op == IrBinOp.Eq || b.Op == IrBinOp.Ne) && (IsNull(b.Right) || IsNull(b.Left)))
            {
                var other = IsNull(b.Right) ? b.Left : b.Right;
                var o = X(other);
                if (!TypeOf(other).Nullable && TypeOf(other).IsKnown && !IsCollection(TypeOf(other))) Problem("comparing a value that is never None with None");
                return new P(Wrap(o.T, o.Prec, PrecPostfix) + (b.Op == IrBinOp.Eq ? ".is_none()" : ".is_some()"), PrecPostfix);
            }
            if (b.Op == IrBinOp.Pow)
            {
                var x = X(b.Left, lt);
                return new P(PowText(new P(ReceiverText(x, b.Left, lt), PrecPostfix), lt, b.Right), PrecPostfix);
            }
            IrType operand = null;
            if (lt.IsNumeric && rt.IsNumeric) operand = lt.Kind == IrTypeKind.Float || rt.Kind == IrTypeKind.Float ? IrType.Float : IrType.Int;
            if (b.Op == IrBinOp.TrueDiv && lt.IsNumeric && rt.IsNumeric) operand = IrType.Float;
            if (b.Op == IrBinOp.TrueDiv && want != null && want.Kind == IrTypeKind.Float) operand = IrType.Float;
            if (cmp && (lt.Nullable != rt.Nullable) && (lt.IsKnown && rt.IsKnown))
            {
                if (b.Op == IrBinOp.Lt || b.Op == IrBinOp.Le || b.Op == IrBinOp.Gt || b.Op == IrBinOp.Ge)
                {
                    // Option < value (after a None test): compare the unwrapped value
                    var lw = lt.NonNull(); var rw = rt.NonNull();
                    IrType num = lw.IsNumeric && rw.IsNumeric ? (lw.Kind == IrTypeKind.Float || rw.Kind == IrTypeKind.Float ? IrType.Float : IrType.Int) : null;
                    var ul = X(b.Left, num ?? lw); var ur = X(b.Right, num ?? rw);
                    return new P(Wrap(ul.T, ul.Prec, prec + 1) + " " + OpText(b.Op) + " " + Wrap(ur.T, ur.Prec, prec + 1), prec);
                }
                // Option == value: compare with Some(value)
                var nl = lt.Nullable ? X(b.Left) : new P("Some(" + X(b.Left, lt.NonNull()).T + ")", PrecAtom);
                var nr = rt.Nullable ? X(b.Right) : new P("Some(" + X(b.Right, rt.NonNull()).T + ")", PrecAtom);
                return new P(Wrap(nl.T, nl.Prec, prec + 1) + " " + OpText(b.Op) + " " + Wrap(nr.T, nr.Prec, prec + 1), prec);
            }
            if (b.Op == IrBinOp.IntDivFloor || b.Op == IrBinOp.ModFloor)
            {
                var l = X(b.Left, operand);
                var recv = ReceiverText(l, b.Left, operand ?? lt);
                return new P(recv + (b.Op == IrBinOp.IntDivFloor ? ".div_euclid(" : ".rem_euclid(") + X(b.Right, operand ?? lt).T + ")", PrecPostfix);
            }
            var L = X(b.Left, cmp || b.Op == IrBinOp.And || b.Op == IrBinOp.Or ? (operand ?? (b.Op == IrBinOp.And || b.Op == IrBinOp.Or ? IrType.Bool : null)) : operand);
            var R = X(b.Right, cmp || b.Op == IrBinOp.And || b.Op == IrBinOp.Or ? (operand ?? (b.Op == IrBinOp.And || b.Op == IrBinOp.Or ? IrType.Bool : null)) : operand);
            if (b.Op == IrBinOp.And || b.Op == IrBinOp.Or)
            {
                if (lt.IsKnown && lt.Kind != IrTypeKind.Bool) L = new P(Cond(b.Left), PrecCmp);
                if (rt.IsKnown && rt.Kind != IrTypeKind.Bool) R = new P(Cond(b.Right), PrecCmp);
            }
            // comparisons are not associative in Rust: parenthesise a comparison operand
            int lmin = cmp ? prec + 1 : prec, rmin = prec + 1;
            if (!cmp && (b.Op == IrBinOp.And || b.Op == IrBinOp.Or || b.Op == IrBinOp.Add || b.Op == IrBinOp.Mul)) lmin = prec;
            return new P(Wrap(L.T, L.Prec, lmin) + " " + OpText(b.Op) + " " + Wrap(R.T, R.Prec, rmin), prec);
        }

        static bool IsNull(IrExpr e) => e is IrLiteral l && l.Kind == IrLitKind.Null;

        /// <summary>A receiver for a numeric method: literals get a type suffix (2i32.pow, 2.0f64.sqrt), others parentheses.</summary>
        string ReceiverText(P x, IrExpr e, IrType t)
        {
            if (e is IrLiteral l && (l.Kind == IrLitKind.Int || l.Kind == IrLitKind.Float))
            {
                var txt = x.T;
                bool f = (t != null && t.Kind == IrTypeKind.Float) || l.Kind == IrLitKind.Float || txt.Contains(".");
                var s = txt + (f ? (txt.Contains(".") ? "" : ".0") + "_f64" : "_i32");
                return s.StartsWith("-") ? "(" + s + ")" : s;
            }
            if (e is IrUnary u && u.Op == IrUnOp.Neg && u.Operand is IrLiteral) return "(" + x.T + (t != null && t.Kind == IrTypeKind.Float ? "_f64" : "_i32") + ")";
            return Wrap(x.T, x.Prec, PrecPostfix);
        }

        string PowText(P baseText, IrType baseType, IrExpr exp)
        {
            var et = TypeOf(exp);
            var recvExpr = baseType;
            string recv = baseText.T;
            if (baseType.Kind == IrTypeKind.Float)
            {
                if (et.Kind == IrTypeKind.Int) { var x = X(exp); return recv + ".powi(" + (exp is IrLiteral ? x.T : Wrap(x.T, x.Prec, PrecCast) ) + ")"; }
                return recv + ".powf(" + X(exp, IrType.Float).T + ")";
            }
            var ex = X(exp, IrType.Int);
            return recv + ".pow(" + (exp is IrLiteral el && el.Int >= 0 ? ex.T : Wrap(ex.T, ex.Prec, PrecCast) + " as u32") + ")";
        }

        string IndexArg(IrExpr i)
        {
            if (i is IrLiteral l && l.Kind == IrLitKind.Int && l.Int >= 0) return l.Int.ToString(CultureInfo.InvariantCulture);
            var x = X(i, IrType.Int);
            return Wrap(x.T, x.Prec, PrecCast) + " as usize";
        }

        string IndexText(IrIndex ix, bool write = false)
        {
            var tt = TypeOf(ix.Target);
            var target = Recv(ix.Target);
            if (tt.Kind == IrTypeKind.Map) return target + "[&" + Wrap(X(ix.Index, tt.Key).T, X(ix.Index).Prec, PrecUnary) + "]";
            if (tt.Kind == IrTypeKind.Tuple || tt.IsGame("Position") || tt.IsGame("Companion"))
            {
                if (ix.Index is IrLiteral l && l.Kind == IrLitKind.Int) return target + "." + l.Int;
                Problem("indexing a tuple with a variable");
                return target + ".0";
            }
            if (tt.IsString) Problem("indexing a string");
            if (ix.Index is IrLiteral nl && nl.Kind == IrLitKind.Int && nl.Int < 0)
                return target + "[" + target + ".len() - " + (-nl.Int) + "]";
            if (ix.Index is IrUnary nu && nu.Op == IrUnOp.Neg && nu.Operand is IrLiteral nul && nul.Kind == IrLitKind.Int)
                return target + "[" + target + ".len() - " + nul.Int + "]";
            return target + "[" + IndexArg(ix.Index) + "]";
        }

        List<IrExpr> FlattenConcat(IrExpr e)
        {
            var res = new List<IrExpr>();
            void Walk(IrExpr x)
            {
                if (x is IrBinary b && (b.Op == IrBinOp.Concat || (b.Op == IrBinOp.Add && (TypeOf(b).IsString || TypeOf(b.Left).IsString || TypeOf(b.Right).IsString)))) { Walk(b.Left); Walk(b.Right); return; }
                if (x is IrCast c && c.Target.Kind == IrTypeKind.String && !c.IsTypeAssertion && c.Synthetic) { Walk(c.Operand); return; }
                res.Add(x);
            }
            Walk(e);
            return res;
        }

        /// <summary>format!/println!/quick_print! from parts: string literals become text, the rest {} / {:?} placeholders.</summary>
        string FormatMacro(string name, List<IrExpr> parts, string sep = "")
        {
            var fmt = new StringBuilder();
            var args = new List<string>();
            bool first = true, prevLit = false;
            foreach (var p in parts)
            {
                if (!first) fmt.Append(sep);
                first = false;
                if (p is IrLiteral l && l.Kind == IrLitKind.String && !prevLit) { fmt.Append(FormatEscape(l.Str)); prevLit = fromRust; continue; }
                prevLit = fromRust && p is IrLiteral pl && pl.Kind == IrLitKind.String;
                if (p is IrCast c && c.Target.Kind == IrTypeKind.String && !c.IsTypeAssertion && !(c.Operand is IrLiteral)) { AddArg(c.Operand); continue; }
                AddArg(p);
            }
            void AddArg(IrExpr a)
            {
                var t = TypeOf(a);
                bool debug = !DisplayType(t);
                if (a is IrName n && n.Symbol != null && !statics.Contains(n.Symbol) && !n.Symbol.IsExternal && n.Symbol.Kind != IrSymbolKind.Global && !refVarsDeref(n.Symbol))
                {
                    fmt.Append('{').Append(Name(n.Symbol)).Append(debug ? ":?}" : "}");
                    return;
                }
                fmt.Append(debug ? "{:?}" : "{}");
                var x = X(a);
                args.Add(x.T);
            }
            if (name == "format" && args.Count == 0 && parts.Count == 1 && parts[0] is IrLiteral only) return Str(only.Str) + ".to_string()";
            return name + "!(\"" + fmt + "\"" + string.Concat(args.Select(a => ", " + a)) + ")";
        }

        bool refVarsDeref(IrSymbol s) => false;

        static bool DisplayType(IrType t)
        {
            if (t == null || !t.IsKnown) return true;
            if (t.Nullable) return false;
            switch (t.Kind)
            {
                case IrTypeKind.Int: case IrTypeKind.Float: case IrTypeKind.Bool: case IrTypeKind.String: return true;
                case IrTypeKind.Game: return t.GameName != "Drone" && t.GameName != "Position" && t.GameName != "Companion";
                case IrTypeKind.Any: return true;
            }
            return false;
        }

        string Lambda(IrLambda lam)
        {
            var caps = IrWalk.Captures(lam);
            var ps = string.Join(", ", lam.Params.Select(p => Name(p.Symbol) + (SymType(p.Symbol).IsKnown ? ": " + TypeText(SymType(p.Symbol)) : "")));
            var head = (caps.Count > 0 ? "move " : "") + "|" + ps + "|";
            if (lam.BodyExpr != null) return head + " " + X(lam.BodyExpr).T;
            // block body: printed into a temporary buffer at one deeper indentation
            int mark = Out.Length;
            var oldRet = currentReturn;
            currentReturn = lam.ReturnType != null && lam.ReturnType.IsKnown ? lam.ReturnType : IrType.Unknown;
            Indent();
            Stmts(lam.Body.Stmts);
            Comments(lam.Body.TrailingComments);
            Dedent();
            currentReturn = oldRet;
            var body = Out.ToString(mark, Out.Length - mark);
            Out.Length = mark;
            return head + " {" + Options.Newline + body + IndentText + "}";
        }

        P Call(IrCall c)
        {
            switch (c.Kind)
            {
                case IrCallKind.Builtin: return Builtin(c);
                case IrCallKind.Function:
                    {
                        var f = c.Function;
                        string name = f == null ? "f" : f.IsExternal ? f.Module + "::" + Ident(f.Name) : Name(f);
                        var decl = f?.Function;
                        var args = new List<string>();
                        for (int i = 0; i < c.Args.Count; i++) args.Add(UserArg(decl, i, c.Args[i]));
                        if (decl != null)
                            for (int i = c.Args.Count; i < decl.Params.Count; i++)
                                args.Add(decl.Params[i].Default != null ? X(decl.Params[i].Default, SymType(decl.Params[i].Symbol)).T : DefaultValue(SymType(decl.Params[i].Symbol)));
                        else if (f != null && f.IsExternal && f.ParamTypes != null)
                        {
                            args.Clear();
                            for (int i = 0; i < c.Args.Count; i++)
                            {
                                var pt = i < f.ParamTypes.Count ? f.ParamTypes[i] : null;
                                args.Add(ExternalArg(pt, c.Args[i]));
                            }
                        }
                        return new P(name + "(" + string.Join(", ", args) + ")", PrecPostfix);
                    }
                default:
                    {
                        var callee = X(c.Callee);
                        var ct = TypeOf(c.Callee);
                        var args = c.Args.Select((a, i) => X(a, ct.Items != null && i < ct.Items.Count ? ct.Items[i] : null).T);
                        return new P(Wrap(callee.T, callee.Prec, PrecPostfix) + "(" + string.Join(", ", args) + ")", PrecPostfix);
                    }
            }
        }

        string ExternalArg(IrType pt, IrExpr a)
        {
            if (pt != null && (IsCollection(pt) || pt.IsString) && !(a is IrLiteral))
            {
                var x = X(a, pt);
                if (NameSym(a) is IrSymbol s && IsRefLocal(s)) return x.T;
                return "&" + Wrap(x.T, x.Prec, PrecUnary);
            }
            return X(a, pt).T;
        }

        string UserArg(IrFunction decl, int i, IrExpr a)
        {
            if (decl == null || i >= decl.Params.Count) return X(a).T;
            var p = decl.Params[i].Symbol;
            var pt = SymType(p);
            var x = X(a, pt);
            var asym = NameSym(a);
            if (mutParams.Contains(p))
            {
                if (asym != null && IsRefLocal(asym)) return x.T;
                return "&mut " + Wrap(x.T, x.Prec, PrecUnary);
            }
            if (refParams.Contains(p))
            {
                if (pt.IsString && IsStrRef(a)) return x.T;
                if (asym != null && IsRefLocal(asym)) return x.T;
                return "&" + Wrap(x.T, x.Prec, PrecUnary);
            }
            if (pt.IsString) return Owned(a, pt);
            return x.T;
        }

        P Builtin(IrCall c)
        {
            var name = c.Builtin;
            var api = GameApi.Find(name);
            switch (name)
            {
                case "print":
                case "quick_print":
                    {
                        var mac = name == "print" ? "println" : "quick_print";
                        if (c.Args.Count == 0) return new P(mac + "!()", PrecPostfix);
                        var parts = new List<IrExpr>();
                        if (c.Args.Count == 1) parts.AddRange(FlattenConcat(c.Args[0]));
                        else
                        {
                            // print(a, b): the game joins the values with spaces
                            for (int i = 0; i < c.Args.Count; i++)
                            {
                                if (i > 0) parts.Add(IrLiteral.OfString(" "));
                                parts.AddRange(FlattenConcat(c.Args[i]));
                            }
                        }
                        return new P(FormatMacro(mac, parts), PrecPostfix);
                    }
                case "min": case "max":
                    {
                        IrType t = IrType.Unknown;
                        foreach (var a in c.Args) t = IrType.Join(t, TypeOf(a));
                        if (!t.IsNumeric) t = null;
                        string acc = X(c.Args[0], t).T;
                        for (int i = 1; i < c.Args.Count; i++) acc = name + "(" + acc + ", " + X(c.Args[i], t).T + ")";
                        return new P(acc, PrecPostfix);
                    }
                case "abs":
                    if (c.Args.Count == 1) return new P("abs(" + X(c.Args[0]).T + ")", PrecPostfix);
                    break;
                case "measure":
                    {
                        var fn = c.Args.Count == 0 ? "measure" : "measure_with";
                        return new P(fn + "(" + string.Join(", ", c.Args.Select(a => X(a, IrType.Game("Direction")).T)) + ")", PrecPostfix);
                    }
            }
            if (api == null) { Problem("unknown builtin " + name); return new P(name + "()", PrecPostfix); }
            int required = api.Params.Count(p => !p.Optional || p.Variadic);
            bool with = RustApi.HasOptional(api) && c.Args.Count > required;
            var fname = with ? RustApi.BaseName(name).Replace("r#", "") + "_with" : RustApi.BaseName(name);
            var args = new List<string>();
            bool spread = false;
            for (int i = 0; i < c.Args.Count; i++)
            {
                var p = api.Params[Math.Min(i, api.Params.Count - 1)];
                if (p.Variadic) { spread = true; break; }
                args.Add(ApiArg(p, c.Args[i]));
            }
            if (spread)
            {
                var p = api.Params[api.Params.Count - 1];
                var rest = c.Args.Skip(api.Params.Count - 1).Select(a => X(a, p.Type).T);
                args.Add("&[" + string.Join(", ", rest) + "]");
            }
            if (!with && RustApi.HasOptional(api) && c.Args.Count < required) Problem("missing arguments for " + name);
            return new P(fname + "(" + string.Join(", ", args) + ")", PrecPostfix);
        }

        string ApiArg(ApiParam p, IrExpr a)
        {
            var text = (p.TypeText ?? "").Replace(" ", "");
            var at = TypeOf(a);
            if (text == "func")
            {
                if (a is IrFuncRef fr && fr.Builtin != null)
                {
                    var bp = GameApi.Find(fr.Builtin);
                    if (bp != null && bp.Params.Count > 0) Problem("passing " + fr.Builtin + " (it takes parameters) as a drone task");
                }
                return X(a).T;
            }
            if (text == "string")
            {
                var x = X(a, IrType.String);
                if (IsStrRef(a)) return x.T;
                return "&" + Wrap(x.T, x.Prec, PrecUnary);
            }
            if (text.Contains("|") || text == "any") return X(a, at.Nullable ? at.NonNull() : null).T;
            if (text.StartsWith("map<"))
            {
                var x = X(a, p.Type);
                return NameSym(a) is IrSymbol s && IsRefLocal(s) ? x.T + ".clone()" : x.T;
            }
            if (text.StartsWith("list<"))
            {
                var x = X(a, p.Type);
                return "&" + Wrap(x.T, x.Prec, PrecUnary);
            }
            return X(a, p.Type).T;
        }

        P OpCall(IrOpCall o)
        {
            IrExpr A(int i) => o.Args[i];
            var t0 = o.Args.Count > 0 ? TypeOf(A(0)) : IrType.Unknown;
            string R0() => Recv(A(0));
            string RefArg(int i, IrType want)
            {
                var a = A(i);
                if (a is IrLiteral sl && sl.Kind == IrLitKind.String && want != null && want.IsString && t0.IsString) return Str(sl.Str);
                var x = X(a, want);
                if (NameSym(a) is IrSymbol s && refVars.Contains(s)) return x.T.StartsWith("*") ? x.T.Substring(1) : x.T;
                return "&" + Wrap(x.T, x.Prec, PrecUnary);
            }
            switch (o.Op)
            {
                case IrOp.Len: return new P(R0() + ".len() as i32", PrecCast);
                case IrOp.IsEmpty: return new P(R0() + ".is_empty()", PrecPostfix);
                case IrOp.Contains:
                    if (t0.Kind == IrTypeKind.Map) return new P(R0() + ".contains_key(" + RefArg(1, t0.Key) + ")", PrecPostfix);
                    if (t0.IsString) { var x = X(A(1)); return new P(R0() + ".contains(" + (IsStrRef(A(1)) ? x.T : "&" + Wrap(x.T, x.Prec, PrecUnary)) + ")", PrecPostfix); }
                    return new P(R0() + ".contains(" + RefArg(1, t0.Elem) + ")", PrecPostfix);
                case IrOp.ListAppend: return new P(R0() + ".push(" + Owned(A(1), t0.Elem) + ")", PrecPostfix);
                case IrOp.ListInsert: return new P(R0() + ".insert(" + IndexArg(A(1)) + ", " + Owned(A(2), t0.Elem) + ")", PrecPostfix);
                case IrOp.ListPop:
                    if (o.Args.Count == 1) return new P(R0() + ".pop().unwrap()", PrecPostfix);
                    return new P(R0() + ".remove(" + IndexArg(A(1)) + ")", PrecPostfix);
                case IrOp.ListSet: return new P(R0() + "[" + IndexArg(A(1)) + "] = " + Owned(A(2), t0.Elem), 3);
                case IrOp.ListIndexOf: return new P(R0() + ".index_of(" + RefArg(1, t0.Elem) + ")", PrecPostfix);
                case IrOp.RemoveValue:
                    if (t0.Kind == IrTypeKind.Set) return new P(R0() + ".remove(" + RefArg(1, t0.Elem) + ")", PrecPostfix);
                    return new P(R0() + ".remove_value(" + RefArg(1, t0.Elem) + ")", PrecPostfix);
                case IrOp.RemoveValueIfPresent:
                    if (t0.Kind == IrTypeKind.Set) return new P(R0() + ".remove(" + RefArg(1, t0.Elem) + ")", PrecPostfix);
                    Problem("removing a list value that may be missing");
                    return new P(R0() + ".remove_value(" + RefArg(1, t0.Elem) + ")", PrecPostfix);
                case IrOp.Clear: return new P(R0() + ".clear()", PrecPostfix);
                case IrOp.SetAdd: return new P(R0() + ".insert(" + Owned(A(1), t0.Elem) + ")", PrecPostfix);
                case IrOp.MapPut: return new P(R0() + ".insert(" + X(A(1), t0.Key).T + ", " + Owned(A(2), t0.Elem) + ")", PrecPostfix);
                case IrOp.MapGetOrNull: return new P(R0() + ".get(" + RefArg(1, t0.Key) + ")" + (IsCopyType(t0.Elem) ? ".copied()" : ".cloned()"), PrecPostfix);
                case IrOp.MapGetOrDefault:
                    if (IsCopyType(t0.Elem)) return new P("*" + R0() + ".get(" + RefArg(1, t0.Key) + ").unwrap_or(&" + Wrap(X(A(2), t0.Elem).T, X(A(2)).Prec, PrecUnary) + ")", PrecUnary);
                    return new P(R0() + ".get(" + RefArg(1, t0.Key) + ").cloned().unwrap_or(" + Owned(A(2), t0.Elem) + ")", PrecPostfix);
                case IrOp.MapRemove: return new P(R0() + ".remove(" + RefArg(1, t0.Key) + ").unwrap()", PrecPostfix);
                case IrOp.MapRemoveIfPresent: return new P(R0() + ".remove(" + RefArg(1, t0.Key) + ")", PrecPostfix);
                case IrOp.MapKeys: Problem("the keys of a map as a value"); return new P(R0() + ".keys()", PrecPostfix);
                case IrOp.MapValues: Problem("the values of a map as a value"); return new P(R0() + ".values()", PrecPostfix);
                case IrOp.ListCopy:
                    if (t0.Kind == IrTypeKind.List || t0.Kind == IrTypeKind.Array) return new P(R0() + ".clone()", PrecPostfix);
                    Problem("converting a set or map to a list");
                    return new P(R0() + ".clone()", PrecPostfix);
                case IrOp.SetCopy:
                    if (t0.Kind == IrTypeKind.Set) return new P(R0() + ".clone()", PrecPostfix);
                    Problem("converting a list to a set");
                    return new P(R0() + ".clone()", PrecPostfix);
                case IrOp.MapCopy: return new P(R0() + ".clone()", PrecPostfix);
                case IrOp.ArrayNew: return new P("vec![" + X(A(1), TypeOf(o).Elem).T + "; " + IndexArg(A(0)) + "]", PrecAtom);
                case IrOp.ArrayNew2: return new P("vec![vec![" + X(A(2)).T + "; " + IndexArg(A(1)) + "]; " + IndexArg(A(0)) + "]", PrecAtom);
                case IrOp.Floor: case IrOp.Ceil: case IrOp.Round: case IrOp.Sqrt:
                    {
                        var x = X(A(0), IrType.Float);
                        var m = o.Op == IrOp.Floor ? "floor" : o.Op == IrOp.Ceil ? "ceil" : o.Op == IrOp.Round ? "round" : "sqrt";
                        var recv = ReceiverText(x, A(0), IrType.Float);
                        if (TypeOf(A(0)).Kind == IrTypeKind.Int && !(A(0) is IrLiteral)) recv = "(" + x.T + ")";
                        return new P(recv + "." + m + "()", PrecPostfix);
                    }
                case IrOp.PosX: return new P(R0() + ".0", PrecPostfix);
                case IrOp.PosY: return new P(R0() + ".1", PrecPostfix);
                case IrOp.CompanionEntity: return new P(R0() + ".0", PrecPostfix);
                case IrOp.CompanionPos: return new P(R0() + ".1", PrecPostfix);
            }
            Problem("operation " + o.Op);
            return new P("()", PrecAtom);
        }
    }
}
