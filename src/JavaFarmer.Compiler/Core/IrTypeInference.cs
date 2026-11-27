using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Fills in static types for IR built from dynamic sources (Python, JS, R), so printers of static languages can
    /// declare variables.
    /// Contract:
    /// - Only fills what is Unknown: an IrExpr.Type, IrSymbol.Type, IrFunction.ReturnType or IrLambda.ReturnType that is
    ///   fully known before Infer runs is never changed; a partially known type (list&lt;unknown&gt;) only gets its
    ///   unknown parts filled. Unknown stays Unknown when nothing is known.
    /// - Flow-insensitive fixpoint over the whole module (a symbol has one type: the IrType.Join of everything assigned
    ///   to it anywhere): literals; arithmetic (int op int → int except / → float; any float → float; Concat or + with
    ///   a string side → string; list + list → list); comparisons, Contains, IsEmpty, and / or / not → bool; builtin
    ///   calls → GameApi.Find(name).ReturnType (min / max / abs: int when every argument is int); user calls → the
    ///   callee's return type (join of its returned values, nullable when a bare return also exists, void when it
    ///   returns nothing); parameters from the argument types at every call site (and defaults); list / set / map /
    ///   tuple literals → joins of their items; empty literals are refined by later ListAppend / ListInsert / SetAdd /
    ///   MapPut / index assignment on the same symbol (the literal gets the symbol's type too); indexing a list / array
    ///   → element, a map → value, a string → string, a tuple with a literal index → that item, a Position → int;
    ///   for-each variables → element type (a map iterates its keys); range-for variables → int; Len → int;
    ///   MapGet* → value type; constants → their game type; casts → the cast's target; functions used as values → func.
    /// - Types nest at most 5 levels (deeper parts become any), so recursive structures terminate.
    /// - Never throws.
    /// </summary>
    public static class IrTypeInference
    {
        public static void Infer(IrModule m)
        {
            if (m == null) return;
            try { new Inferer(m).Run(); }
            catch (Exception) { /* inference is best effort: keep whatever was inferred so far */ }
        }

        /// <summary>True if the type has no Unknown part.</summary>
        public static bool IsFullyKnown(IrType t)
        {
            if (t == null || t.Kind == IrTypeKind.Unknown) return false;
            switch (t.Kind)
            {
                case IrTypeKind.List: case IrTypeKind.Array: case IrTypeKind.Set: return IsFullyKnown(t.Elem);
                case IrTypeKind.Map: return IsFullyKnown(t.Key) && IsFullyKnown(t.Elem);
                case IrTypeKind.Tuple: return t.Items == null || t.Items.All(IsFullyKnown);
                case IrTypeKind.Func: return t.Elem == null || IsFullyKnown(t.Elem);
            }
            return true;
        }

        /// <summary>Fills the Unknown parts of orig from inferred; a known part (or a different kind) of orig wins.</summary>
        public static IrType Refine(IrType orig, IrType inferred)
        {
            if (inferred == null || inferred.Kind == IrTypeKind.Unknown) return orig ?? IrType.Unknown;
            if (orig == null || orig.Kind == IrTypeKind.Unknown) return inferred;
            if (orig.Kind != inferred.Kind) return orig;
            IrType r;
            switch (orig.Kind)
            {
                case IrTypeKind.List: r = IrType.List(Refine(orig.Elem, inferred.Elem)); break;
                case IrTypeKind.Array: r = IrType.Array(Refine(orig.Elem, inferred.Elem)); break;
                case IrTypeKind.Set: r = IrType.Set(Refine(orig.Elem, inferred.Elem)); break;
                case IrTypeKind.Map: r = IrType.Map(Refine(orig.Key, inferred.Key), Refine(orig.Elem, inferred.Elem)); break;
                case IrTypeKind.Tuple:
                    if (orig.Items == null || inferred.Items == null || orig.Items.Count != inferred.Items.Count) return orig;
                    r = IrType.Tuple(orig.Items.Zip(inferred.Items, Refine).ToArray());
                    break;
                case IrTypeKind.Func:
                    r = IrType.Func(Refine(orig.Elem, inferred.Elem));
                    r.Items = orig.Items != null && orig.Items.Count > 0 ? orig.Items : inferred.Items;
                    break;
                default: return orig;
            }
            return orig.Nullable ? r.AsNullable() : r;
        }

        /// <summary>Structural equality including nullability (IrType.SameAs ignores it).</summary>
        public static bool Same(IrType a, IrType b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Kind != b.Kind || a.Nullable != b.Nullable || a.GameName != b.GameName) return false;
            if (!Same(a.Elem, b.Elem) || !Same(a.Key, b.Key)) return false;
            int ac = a.Items?.Count ?? 0, bc = b.Items?.Count ?? 0;
            if (ac != bc) return false;
            for (int i = 0; i < ac; i++) if (!Same(a.Items[i], b.Items[i])) return false;
            return true;
        }

        /// <summary>Rounds the last Infer call needed (diagnostics for tests).</summary>
        internal static int LastRounds;

        sealed class Inferer
        {
            const int MaxDepth = 5, MaxRounds = 24;
            readonly IrModule mod;
            List<IrNode> nodes;
            List<IrExpr> exprsBottomUp;
            readonly Dictionary<IrExpr, IrType> exprInit = new Dictionary<IrExpr, IrType>();
            readonly Dictionary<IrExpr, IrType> hints = new Dictionary<IrExpr, IrType>();
            readonly Dictionary<IrSymbol, IrType> symInit = new Dictionary<IrSymbol, IrType>();
            readonly Dictionary<IrFunction, IrType> fnInit = new Dictionary<IrFunction, IrType>();
            readonly Dictionary<IrLambda, IrType> lamInit = new Dictionary<IrLambda, IrType>();
            readonly Dictionary<IrSymbol, IrLambda> lambdaOf = new Dictionary<IrSymbol, IrLambda>();
            bool changed;

            public Inferer(IrModule m) { mod = m; }

            public void Run()
            {
                nodes = IrWalk.Descendants(mod).ToList();
                exprsBottomUp = nodes.OfType<IrExpr>().Reverse().ToList();
                foreach (var e in exprsBottomUp) if (!IsFullyKnown(e.Type)) exprInit[e] = e.Type ?? IrType.Unknown;
                foreach (var s in AllSymbols()) if (!symInit.ContainsKey(s) && !IsFullyKnown(s.Type)) symInit[s] = s.Type ?? IrType.Unknown;
                foreach (var f in nodes.OfType<IrFunction>()) if (!IsFullyKnown(f.ReturnType)) fnInit[f] = f.ReturnType ?? IrType.Unknown;
                foreach (var l in nodes.OfType<IrLambda>()) if (!IsFullyKnown(l.ReturnType)) lamInit[l] = l.ReturnType ?? IrType.Unknown;
                foreach (var v in nodes.OfType<IrVarDecl>()) if (v.Symbol != null && v.Init is IrLambda lam) lambdaOf[v.Symbol] = lam;
                if (exprInit.Count == 0 && symInit.Count == 0 && fnInit.Count == 0 && lamInit.Count == 0) return;

                for (int round = 0; round < MaxRounds; round++)
                {
                    LastRounds = round + 1;
                    changed = false;
                    Expressions();
                    Symbols();
                    Returns();
                    if (!changed) break;
                }
            }

            IEnumerable<IrSymbol> AllSymbols()
            {
                foreach (var n in nodes)
                {
                    switch (n)
                    {
                        case IrName nm: if (nm.Symbol != null) yield return nm.Symbol; break;
                        case IrVarDecl v: if (v.Symbol != null) yield return v.Symbol; break;
                        case IrGlobal g: if (g.Symbol != null) yield return g.Symbol; break;
                        case IrParam p: if (p.Symbol != null) yield return p.Symbol; break;
                        case IrFunction f: if (f.Symbol != null) yield return f.Symbol; break;
                        case IrRangeFor r: if (r.Var != null) yield return r.Var; break;
                        case IrForEach fe: if (fe.Var != null) yield return fe.Var; if (fe.ValueVar != null) yield return fe.ValueVar; break;
                        case IrCall c: if (c.Function != null) yield return c.Function; break;
                        case IrFuncRef fr: if (fr.Function != null) yield return fr.Function; break;
                    }
                }
            }

            // ------------------------------------------------------------ expressions
            void Expressions()
            {
                foreach (var e in exprsBottomUp)
                {
                    if (!exprInit.TryGetValue(e, out var init)) continue;
                    var t = Refine(init, Cap(Compute(e), 0));
                    if (hints.TryGetValue(e, out var h)) t = Refine(t, h);
                    if (!Same(t, e.Type)) { e.Type = t; changed = true; }
                }
            }

            static IrType Cap(IrType t, int depth)
            {
                if (t == null) return IrType.Unknown;
                if (depth >= MaxDepth) return IrType.Any;
                IrType r;
                switch (t.Kind)
                {
                    case IrTypeKind.List: r = IrType.List(Cap(t.Elem, depth + 1)); break;
                    case IrTypeKind.Array: r = IrType.Array(Cap(t.Elem, depth + 1)); break;
                    case IrTypeKind.Set: r = IrType.Set(Cap(t.Elem, depth + 1)); break;
                    case IrTypeKind.Map: r = IrType.Map(Cap(t.Key, depth + 1), Cap(t.Elem, depth + 1)); break;
                    case IrTypeKind.Tuple: r = IrType.Tuple((t.Items ?? new List<IrType>()).Select(i => Cap(i, depth + 1)).ToArray()); break;
                    default: return t;
                }
                return t.Nullable ? r.AsNullable() : r;
            }

            static IrType T(IrExpr e) => e?.Type ?? IrType.Unknown;

            static IrType JoinAll(IEnumerable<IrType> ts)
            {
                IrType r = IrType.Unknown;
                foreach (var t in ts) r = IrType.Join(r, t);
                return r;
            }

            static bool IsNum(IrType t) => t.Kind == IrTypeKind.Int || t.Kind == IrTypeKind.Float || t.Kind == IrTypeKind.Bool;

            static IrType Arith(IrType l, IrType r)
            {
                if (!IsNum(l) || !IsNum(r)) return l.Kind == IrTypeKind.Float || r.Kind == IrTypeKind.Float ? IrType.Float : IrType.Unknown;
                return l.Kind == IrTypeKind.Float || r.Kind == IrTypeKind.Float ? IrType.Float : IrType.Int;
            }

            public static IrType BinType(IrBinOp op, IrType l, IrType r)
            {
                l = l ?? IrType.Unknown; r = r ?? IrType.Unknown;
                switch (op)
                {
                    case IrBinOp.Eq: case IrBinOp.Ne: case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge:
                    case IrBinOp.And: case IrBinOp.Or:
                        return IrType.Bool;
                    case IrBinOp.Concat: return IrType.String;
                    case IrBinOp.TrueDiv: return IrType.Float;
                    case IrBinOp.Add:
                        if (l.IsString || r.IsString) return IrType.String;
                        if ((l.Kind == IrTypeKind.List || l.Kind == IrTypeKind.Array) && (r.Kind == IrTypeKind.List || r.Kind == IrTypeKind.Array))
                            return IrType.List(IrType.Join(l.Elem, r.Elem));
                        if (l.Kind == IrTypeKind.Tuple && r.Kind == IrTypeKind.Tuple)
                            return IrType.Tuple((l.Items ?? new List<IrType>()).Concat(r.Items ?? new List<IrType>()).ToArray());
                        return Arith(l, r);
                    case IrBinOp.Mul:
                        if ((l.IsString && IsNum(r)) || (r.IsString && IsNum(l))) return IrType.String;
                        if (l.Kind == IrTypeKind.List && IsNum(r)) return l;
                        if (r.Kind == IrTypeKind.List && IsNum(l)) return r;
                        return Arith(l, r);
                    default:
                        return Arith(l, r);
                }
            }

            IrType FunctionReturn(IrSymbol s)
            {
                if (s == null) return IrType.Unknown;
                if (s.Function != null) return s.Function.ReturnType ?? IrType.Unknown;
                if (s.Type != null && s.Type.Kind == IrTypeKind.Func) return s.Type.Elem ?? IrType.Unknown;
                return IrType.Unknown;
            }

            IrType FuncTypeOf(IrSymbol s)
            {
                if (s?.Function != null) return IrType.Func(s.Function.ReturnType ?? IrType.Unknown, s.Function.Params.Select(p => p.Symbol?.Type ?? IrType.Unknown).ToArray());
                if (s?.Type != null && s.Type.Kind == IrTypeKind.Func) return s.Type;
                return IrType.Unknown;
            }

            static IrType ElemOf(IrType t)
            {
                switch (t?.Kind ?? IrTypeKind.Unknown)
                {
                    case IrTypeKind.List: case IrTypeKind.Array: case IrTypeKind.Set: return t.Elem ?? IrType.Unknown;
                    case IrTypeKind.Map: return t.Key ?? IrType.Unknown;
                    case IrTypeKind.String: return IrType.String;
                    case IrTypeKind.Tuple: return JoinAll(t.Items ?? new List<IrType>());
                }
                return IrType.Unknown;
            }

            static IrType ValueOf(IrType t)
            {
                switch (t?.Kind ?? IrTypeKind.Unknown)
                {
                    case IrTypeKind.Map: case IrTypeKind.List: case IrTypeKind.Array: return t.Elem ?? IrType.Unknown;
                }
                return IrType.Unknown;
            }

            static long? LiteralIndex(IrExpr e)
            {
                if (e is IrLiteral l && l.Kind == IrLitKind.Int) return l.Int;
                if (e is IrUnary u && u.Op == IrUnOp.Neg && u.Operand is IrLiteral l2 && l2.Kind == IrLitKind.Int) return -l2.Int;
                return null;
            }

            static IrType ItemOf(IrType t, IrExpr index)
            {
                switch (t?.Kind ?? IrTypeKind.Unknown)
                {
                    case IrTypeKind.List: case IrTypeKind.Array: case IrTypeKind.Map: return t.Elem ?? IrType.Unknown;
                    case IrTypeKind.String: return IrType.String;
                    case IrTypeKind.Tuple:
                        {
                            var items = t.Items ?? new List<IrType>();
                            var i = LiteralIndex(index);
                            if (i != null)
                            {
                                long k = i.Value < 0 ? items.Count + i.Value : i.Value;
                                return k >= 0 && k < items.Count ? items[(int)k] : IrType.Unknown;
                            }
                            return JoinAll(items);
                        }
                    case IrTypeKind.Game:
                        if (t.GameName == "Position") return IrType.Int;
                        if (t.GameName == "Companion")
                        {
                            var i = LiteralIndex(index);
                            return i == 0 ? IrType.Game("Entity") : i == 1 ? IrType.Game("Position") : IrType.Unknown;
                        }
                        break;
                }
                return IrType.Unknown;
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
                            default: return IrType.Null;
                        }
                    case IrName n:
                        if (n.Symbol == null) return IrType.Unknown;
                        if (n.Symbol.Kind == IrSymbolKind.Function) return FuncTypeOf(n.Symbol);
                        return n.Symbol.Type ?? IrType.Unknown;
                    case IrConst c:
                        {
                            var g = GameApi.GameTypeOfCategory(c.Category);
                            return g != null ? IrType.Game(g) : IrType.Unknown;
                        }
                    case IrBinary b: return BinType(b.Op, T(b.Left), T(b.Right));
                    case IrUnary u:
                        if (u.Op == IrUnOp.Not) return IrType.Bool;
                        {
                            var ot = T(u.Operand);
                            return ot.Kind == IrTypeKind.Bool ? IrType.Int : IsNum(ot) ? ot : IrType.Unknown;
                        }
                    case IrConditional c: return IrType.Join(T(c.Then), T(c.Else));
                    case IrCall c:
                        switch (c.Kind)
                        {
                            case IrCallKind.Builtin:
                                {
                                    var b = GameApi.Find(c.Builtin);
                                    if (b == null) return IrType.Unknown;
                                    if (b.NumericLikeArgs)
                                    {
                                        var ts = c.Args.Select(T).ToList();
                                        if (ts.Any(t => t.Kind == IrTypeKind.Float)) return IrType.Float;
                                        if (ts.Count > 0 && ts.All(t => t.Kind == IrTypeKind.Int || t.Kind == IrTypeKind.Bool)) return IrType.Int;
                                        return ts.All(t => t.IsKnown) ? IrType.Float : IrType.Unknown;
                                    }
                                    return b.ReturnType ?? IrType.Unknown;
                                }
                            case IrCallKind.Function: return FunctionReturn(c.Function);
                            default:
                                {
                                    if (c.Callee is IrName cn && cn.Symbol != null && lambdaOf.TryGetValue(cn.Symbol, out var lam)) return lam.ReturnType ?? IrType.Unknown;
                                    if (c.Callee is IrFuncRef fr) return fr.Function != null ? FunctionReturn(fr.Function) : (GameApi.Find(fr.Builtin)?.ReturnType ?? IrType.Unknown);
                                    var ct = T(c.Callee);
                                    return ct.Kind == IrTypeKind.Func ? ct.Elem ?? IrType.Unknown : IrType.Unknown;
                                }
                        }
                    case IrOpCall o: return OpType(o);
                    case IrIndex ix: return ItemOf(T(ix.Target), ix.Index);
                    case IrListLit l: { var el = JoinAll(l.Items.Select(T)); return l.IsArray ? IrType.Array(el) : IrType.List(el); }
                    case IrSetLit s: return IrType.Set(JoinAll(s.Items.Select(T)));
                    case IrMapLit m: return IrType.Map(JoinAll(m.Keys.Select(T)), JoinAll(m.Values.Select(T)));
                    case IrTupleLit t: return IrType.Tuple(t.Items.Select(T).ToArray());
                    case IrLambda lam: return IrType.Func(lam.ReturnType ?? IrType.Unknown, lam.Params.Select(p => p.Symbol?.Type ?? IrType.Unknown).ToArray());
                    case IrFuncRef f:
                        if (f.Function != null) return FuncTypeOf(f.Function).IsKnown ? FuncTypeOf(f.Function) : IrType.Func(FunctionReturn(f.Function));
                        {
                            var b = GameApi.Find(f.Builtin);
                            return b != null ? IrType.Func(b.ReturnType ?? IrType.Void, b.Params.Select(p => p.Type ?? IrType.Any).ToArray()) : IrType.Func(IrType.Unknown);
                        }
                    case IrCast c: return c.Target ?? IrType.Unknown;
                }
                return IrType.Unknown;
            }

            static IrType OpType(IrOpCall o)
            {
                var a = o.Args;
                var r = a.Count > 0 ? T(a[0]) : IrType.Unknown;
                switch (o.Op)
                {
                    case IrOp.Len: case IrOp.ListIndexOf: case IrOp.PosX: case IrOp.PosY: case IrOp.Round: return IrType.Int;
                    case IrOp.IsEmpty: case IrOp.Contains: return IrType.Bool;
                    case IrOp.ListAppend: case IrOp.ListInsert: case IrOp.ListSet: case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent:
                    case IrOp.Clear: case IrOp.SetAdd: case IrOp.MapPut:
                        return IrType.Void;
                    case IrOp.ListPop: case IrOp.MapRemove: return ValueOf(r);
                    case IrOp.MapGetOrNull: case IrOp.MapRemoveIfPresent: { var v = ValueOf(r); return v.IsKnown ? v.AsNullable() : v; }
                    case IrOp.MapGetOrDefault: return IrType.Join(ValueOf(r), a.Count > 2 ? T(a[2]) : IrType.Unknown);
                    case IrOp.MapKeys: return r.Kind == IrTypeKind.Map ? IrType.Set(r.Key) : IrType.Set(IrType.Unknown);
                    case IrOp.MapValues: return r.Kind == IrTypeKind.Map ? IrType.List(r.Elem) : IrType.List(IrType.Unknown);
                    case IrOp.ListCopy: return IrType.List(ElemOf(r));
                    case IrOp.SetCopy: return IrType.Set(ElemOf(r));
                    case IrOp.MapCopy: return r.Kind == IrTypeKind.Map ? r : IrType.Map(IrType.Unknown, IrType.Unknown);
                    case IrOp.ArrayNew: return IrType.Array(a.Count > 1 ? T(a[1]) : IrType.Unknown);
                    case IrOp.ArrayNew2: return IrType.Array(IrType.Array(a.Count > 2 ? T(a[2]) : IrType.Unknown));
                    case IrOp.Floor: case IrOp.Ceil: case IrOp.Sqrt: return IrType.Float;
                    case IrOp.CompanionEntity: return IrType.Game("Entity");
                    case IrOp.CompanionPos: return IrType.Game("Position");
                }
                return IrType.Unknown;
            }

            // ------------------------------------------------------------ symbols
            Dictionary<IrSymbol, IrType> acc;

            void Contribute(IrSymbol s, IrType t)
            {
                if (s == null || t == null || !t.IsKnown) return;
                acc[s] = acc.TryGetValue(s, out var cur) ? IrType.Join(cur, t) : t;
            }

            static IrSymbol Sym(IrExpr e) => e is IrName n ? n.Symbol : null;

            void AssignTo(IrExpr target, IrType t, IrExpr value)
            {
                switch (target)
                {
                    case IrName n: Contribute(n.Symbol, t); break;
                    case IrTupleLit tl:
                        for (int i = 0; i < tl.Items.Count; i++)
                        {
                            IrType it = IrType.Unknown;
                            if (value is IrTupleLit vt && i < vt.Items.Count) it = T(vt.Items[i]);
                            else if (t != null && t.Kind == IrTypeKind.Tuple && t.Items != null && i < t.Items.Count) it = t.Items[i];
                            else if (t != null && t.IsGame("Position")) it = IrType.Int;
                            else if (t != null && t.IsGame("Companion")) it = i == 0 ? IrType.Game("Entity") : IrType.Game("Position");
                            else if (t != null && (t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Array)) it = t.Elem;
                            AssignTo(tl.Items[i], it, null);
                        }
                        break;
                    case IrIndex ix:
                        {
                            var s = Sym(ix.Target);
                            if (s == null) break;
                            var ct = s.Type ?? IrType.Unknown;
                            if (ct.Kind == IrTypeKind.Map) Contribute(s, IrType.Map(T(ix.Index), t));
                            else if (ct.Kind == IrTypeKind.List) Contribute(s, IrType.List(t));
                            else if (ct.Kind == IrTypeKind.Array) Contribute(s, IrType.Array(t));
                            break;
                        }
                }
            }

            void Symbols()
            {
                acc = new Dictionary<IrSymbol, IrType>();
                var initOf = new List<(IrSymbol, IrExpr)>();
                foreach (var n in nodes)
                {
                    switch (n)
                    {
                        case IrVarDecl v: if (v.Init != null) { Contribute(v.Symbol, T(v.Init)); initOf.Add((v.Symbol, v.Init)); } break;
                        case IrGlobal g: if (g.Init != null) { Contribute(g.Symbol, T(g.Init)); initOf.Add((g.Symbol, g.Init)); } break;
                        case IrAssign a:
                            if (a.Op == null) { AssignTo(a.Target, T(a.Value), a.Value); if (Sym(a.Target) != null) initOf.Add((Sym(a.Target), a.Value)); }
                            else if (a.Target is IrName tn) Contribute(tn.Symbol, a.TruncateToInt ? IrType.Int : BinType(a.Op.Value, T(a.Target), T(a.Value)));
                            break;
                        case IrRangeFor r: Contribute(r.Var, IrType.Int); break;
                        case IrForEach fe:
                            {
                                var it = T(fe.Iterable);
                                Contribute(fe.Var, ElemOf(it));
                                if (fe.ValueVar != null && it.Kind == IrTypeKind.Map) Contribute(fe.ValueVar, it.Elem);
                                break;
                            }
                        case IrOpCall o when o.Args.Count > 0 && Sym(o.Args[0]) is IrSymbol rs:
                            switch (o.Op)
                            {
                                case IrOp.ListAppend: if (o.Args.Count > 1) Contribute(rs, Container(rs, T(o.Args[1]))); break;
                                case IrOp.ListInsert: if (o.Args.Count > 2) Contribute(rs, Container(rs, T(o.Args[2]))); break;
                                case IrOp.ListSet: if (o.Args.Count > 2) Contribute(rs, Container(rs, T(o.Args[2]))); break;
                                case IrOp.SetAdd: if (o.Args.Count > 1) Contribute(rs, IrType.Set(T(o.Args[1]))); break;
                                case IrOp.MapPut: if (o.Args.Count > 2) Contribute(rs, IrType.Map(T(o.Args[1]), T(o.Args[2]))); break;
                            }
                            break;
                        case IrCall c:
                            {
                                List<IrParam> ps = null;
                                if (c.Kind == IrCallKind.Function && c.Function?.Function != null) ps = c.Function.Function.Params;
                                else if (c.Kind == IrCallKind.Value && c.Callee is IrName cn && cn.Symbol != null && lambdaOf.TryGetValue(cn.Symbol, out var lam)) ps = lam.Params;
                                if (ps != null)
                                    for (int i = 0; i < ps.Count && i < c.Args.Count; i++) Contribute(ps[i].Symbol, T(c.Args[i]));
                                break;
                            }
                        case IrParam p: if (p.Default != null) Contribute(p.Symbol, T(p.Default)); break;
                    }
                }
                foreach (var kv in acc)
                {
                    if (!symInit.TryGetValue(kv.Key, out var init)) continue;
                    var t = Refine(init, Cap(kv.Value, 0));
                    if (!Same(t, kv.Key.Type)) { kv.Key.Type = t; changed = true; }
                }
                // function symbols: func(return type)
                foreach (var f in nodes.OfType<IrFunction>())
                    if (f.Symbol != null && symInit.TryGetValue(f.Symbol, out var fi))
                    {
                        var t = Refine(fi, FuncTypeOf(f.Symbol));
                        if (!Same(t, f.Symbol.Type)) { f.Symbol.Type = t; changed = true; }
                    }
                // empty / partial literals take the type of the symbol they initialise
                hints.Clear();
                foreach (var (s, e) in initOf)
                    if (s != null && exprInit.TryGetValue(e, out var einit) && !IsFullyKnown(einit) && s.Type != null && s.Type.Kind == (e.Type?.Kind ?? IrTypeKind.Unknown))
                    {
                        hints[e] = s.Type;
                        if (!Same(Refine(e.Type, s.Type), e.Type)) changed = true;
                    }
            }

            static IrType Container(IrSymbol s, IrType elem) => (s.Type?.Kind ?? IrTypeKind.Unknown) == IrTypeKind.Array ? IrType.Array(elem) : IrType.List(elem);

            // ------------------------------------------------------------ returns
            static IEnumerable<IrReturn> ReturnsOf(IrNode body)
            {
                if (body == null) yield break;
                foreach (var n in IrWalk.Descendants(body, intoLambdas: false))
                    if (n is IrReturn r) yield return r;
            }

            readonly Dictionary<IrNode, List<IrReturn>> returnsCache = new Dictionary<IrNode, List<IrReturn>>();

            IrType ReturnTypeOf(IrNode body)
            {
                if (body == null) return IrType.Void;
                if (!returnsCache.TryGetValue(body, out var rs)) returnsCache[body] = rs = ReturnsOf(body).ToList();
                var vals = rs.Where(r => r.Value != null).ToList();
                if (vals.Count == 0) return IrType.Void;
                var t = JoinAll(vals.Select(r => T(r.Value)));
                if (t.IsKnown && rs.Count > vals.Count) t = t.AsNullable();
                return t;
            }

            void Returns()
            {
                foreach (var kv in fnInit)
                {
                    var t = Refine(kv.Value, Cap(ReturnTypeOf(kv.Key.Body), 0));
                    if (!Same(t, kv.Key.ReturnType)) { kv.Key.ReturnType = t; changed = true; }
                }
                foreach (var kv in lamInit)
                {
                    var l = kv.Key;
                    IrType rt = l.BodyExpr != null ? (l.ReturnsValue ? T(l.BodyExpr) : IrType.Void) : ReturnTypeOf(l.Body);
                    var t = Refine(kv.Value, Cap(rt, 0));
                    if (!Same(t, l.ReturnType)) { l.ReturnType = t; changed = true; }
                }
            }
        }
    }
}
