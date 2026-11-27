using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.CSharp
{
    /// <summary>
    /// Type inference for untyped IR (Python, JS, R sources) as the Java printer needs it: joins the values assigned to
    /// every symbol (declarations, assignments, call arguments, appended elements, loop variables) to a fixpoint.
    /// Typed IR (Java) passes straight through. Never mutates the IR.
    /// </summary>
    internal sealed class CsTypeGuess
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

        public CsTypeGuess(IrModule m)
        {
            foreach (var f in m.Functions) if (f.Symbol != null) funcs[f.Symbol] = f;
            bool typed = m.SourceLanguage == null || m.SourceLanguage == "java" || m.SourceLanguage == "csharp";
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
