using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Checked Rust AST → Farm IR. Every construct emits what its Python twin would produce (SPEC-v2 §4): fn main is the
    /// entry (scaffolding), use / mod lines have no IR node, println!/format! become print with Synthetic Concat nodes,
    /// if-let / match on Option become a binding plus a != None test, ranges become IrRangeFor, .rev() / .step_by() steps.
    /// </summary>
    internal sealed class RustLowering
    {
        readonly RustChecker chk;
        readonly string fileName;
        readonly Dictionary<RVar, IrSymbol> syms = new Dictionary<RVar, IrSymbol>();
        readonly Dictionary<RFnSym, IrSymbol> fns = new Dictionary<RFnSym, IrSymbol>();

        RustLowering(RustChecker chk, string fileName) { this.chk = chk; this.fileName = fileName; }

        public static IrModule Lower(RustChecker chk, RFile file, string fileName, string source, bool comments)
        {
            var l = new RustLowering(chk, fileName);
            var m = l.Module(file);
            if (comments && source != null) IrComments.Attach(m, Comments(file), source);
            return m;
        }

        static List<IrComment> Comments(RFile f)
        {
            var res = new List<IrComment>();
            foreach (var t in f.Comments)
            {
                var text = t.Text ?? "";
                if (!t.Block)
                {
                    bool doc = text.StartsWith("///") || text.StartsWith("//!");
                    res.Add(new IrComment { Text = text.Substring(doc ? 3 : 2), IsDoc = doc, Span = new Span(t.Start, t.End) });
                }
                else
                {
                    bool doc = t.Doc;
                    int body = doc ? 3 : 2;
                    int end = text.EndsWith("*/") && text.Length >= body + 2 ? text.Length - 2 : text.Length;
                    res.Add(new IrComment { Text = text.Substring(body, Math.Max(0, end - body)), IsBlock = true, IsDoc = doc, Span = new Span(t.Start, t.End) });
                }
            }
            return res;
        }

        static Span Sp(RNode n) => n == null ? Span.None : new Span(n.Start, n.End);
        static Span Sp(int s, int e) => s < 0 ? Span.None : new Span(s, e);
        static RustAbort NotSupported(string what, RNode n) => new RustAbort(what + " is not supported in JavaFarmer", n.Start, n.End);

        // ------------------------------------------------------------------ symbols
        IrSymbol Sym(RVar v)
        {
            if (v == null) return null;
            if (v.Ir != null && v.Kind == RVarKind.External) return v.Ir;
            if (syms.TryGetValue(v, out var s)) return s;
            IrSymbolKind kind;
            switch (v.Kind)
            {
                case RVarKind.Param: kind = IrSymbolKind.Param; break;
                case RVarKind.Static: case RVarKind.Const: kind = IrSymbolKind.Global; break;
                default: kind = IrSymbolKind.Local; break;
            }
            s = new IrSymbol(v.Name, kind, RTy.ToIr(v.Type)) { IsConst = !v.Mut, DeclSpan = Sp(v.DeclStart, v.DeclEnd), Tag = v.Type };
            syms[v] = s;
            return s;
        }

        IrSymbol FnSym(RFnSym f)
        {
            if (fns.TryGetValue(f, out var s)) return s;
            bool own = f.Module == null;
            s = new IrSymbol(f.Name, own ? IrSymbolKind.Function : IrSymbolKind.ExternalFunction, IrType.Func(RTy.ToIr(f.Ret)))
            {
                ParamTypes = f.Params.Select(RTy.ToIr).ToList()
            };
            if (!own) { s.Module = f.Module; s.PyName = f.PyName; }
            fns[f] = s;
            return s;
        }

        IrSymbol Temp(string name, IrType t, RNode at) => new IrSymbol(name, IrSymbolKind.Local, t) { DeclSpan = Sp(at) };

        // ------------------------------------------------------------------ module
        IrModule Module(RFile file)
        {
            var m = new IrModule { Name = fileName, SourceLanguage = "rust", Span = Sp(file) };
            foreach (var it in file.Items)
            {
                switch (it)
                {
                    case RConst c:
                        {
                            var g = new IrGlobal { Symbol = Sym(c.Var), Span = Sp(c), NameSpan = Sp(c.NameStart, c.NameEnd), TypeExplicit = true, IsPrivate = !c.Pub };
                            g.Symbol.DeclSpan = g.NameSpan;
                            g.Init = Expr(c.Init);
                            m.Items.Add(g);
                            break;
                        }
                    case RFn f: m.Items.Add(Function(f)); break;
                }
            }
            return m;
        }

        RFn current;

        IrFunction Function(RFn f)
        {
            current = f;
            var sym = FnSym(f.Sym);
            var fn = new IrFunction
            {
                Symbol = sym, ReturnType = RTy.ToIr(f.Sym.Ret), IsEntry = f == chk.Main, IsPrivate = !f.Pub, Span = Sp(f),
                HeaderSpan = Sp(f.Start, f.HeaderEnd), NameSpan = Sp(f.NameStart, f.NameEnd)
            };
            sym.Function = fn;
            sym.DeclSpan = fn.NameSpan;
            foreach (var p in f.Params)
            {
                IrSymbol ps;
                if (p.Pat.Kind == RPatKind.Ident) ps = Sym(p.Pat.Var);
                else if (p.Pat.Kind == RPatKind.Wild) ps = new IrSymbol("_", IrSymbolKind.Param, RTy.ToIr(p.Pat.Type));
                else throw NotSupported("a pattern as a function parameter", p.Pat);
                fn.Params.Add(new IrParam { Symbol = ps, Span = Sp(p), NameSpan = Sp(p.Pat) });
            }
            bool returns = RTy.Prune(f.Sym.Ret).Kind != RK.Unit && RTy.Prune(f.Sym.Ret).Kind != RK.Never;
            fn.Body = Block(f.Body, returns ? Sink.Return : Sink.Discard);
            return fn;
        }

        // ------------------------------------------------------------------ value sinks (tail expressions)
        sealed class Sink
        {
            public static readonly Sink Discard = new Sink();
            public static readonly Sink Return = new Sink { IsReturn = true };
            public bool IsReturn;
            public Func<IrExpr> Target;   // assignment target factory
            public bool IsDiscard => !IsReturn && Target == null;
        }

        IrBlock Block(RBlock b, Sink tailSink)
        {
            var blk = new IrBlock { Span = Sp(b) };
            foreach (var s in b.Stmts) Stmt(s, blk.Stmts);
            if (b.Tail != null) Into(b.Tail, tailSink, blk.Stmts);
            return blk;
        }

        static bool IsBlockLike(RExpr e) => e is RIf || e is RMatch || e is RBlockExpr || e is RLoop || e is RWhile || e is RFor;

        /// <summary>Lowers a value-producing expression whose value goes to the sink (return / assignment / nothing).</summary>
        void Into(RExpr e, Sink sink, List<IrStmt> outList)
        {
            if (sink.IsDiscard) { ExprStmt(e, outList, Sp(e)); return; }
            var ty = RTy.Prune(e.Type);
            if (ty != null && ty.Kind == RK.Never && !(e is RIf) && !(e is RMatch) && !(e is RBlockExpr)) { ExprStmt(e, outList, Sp(e)); return; }
            switch (e)
            {
                case RIf i when !SimpleIf(i):
                    outList.Add(IfStmt(i, sink));
                    return;
                case RMatch m:
                    MatchStmt(m, sink, outList);
                    return;
                case RBlockExpr be:
                    if (be.Block.Stmts.Count == 0 && be.Block.Tail != null) { Into(be.Block.Tail, sink, outList); return; }
                    outList.Add(Block(be.Block, sink));
                    return;
                case RLoop _: case RWhile _: case RFor _:
                    ExprStmt(e, outList, Sp(e));
                    return;
                case RReturn _: case RBreak _: case RContinue _:
                    ExprStmt(e, outList, Sp(e));
                    return;
            }
            var v = Expr(e);
            if (sink.IsReturn) outList.Add(new IrReturn { Value = v, Span = Sp(e) });
            else outList.Add(new IrAssign { Target = sink.Target(), Value = v, Span = Sp(e), OpSpan = Span.None });
        }

        /// <summary>if/else chain whose branches are single expressions: an IrConditional value.</summary>
        static bool SimpleIf(RIf i)
        {
            if (i.LetPat != null || i.Else == null) return false;
            if (i.Then.Stmts.Count > 0 || i.Then.Tail == null || IsBlockLike(i.Then.Tail) || IsJump(i.Then.Tail)) return false;
            if (i.Else is RIf ei) return SimpleIf(ei);
            if (i.Else is RBlockExpr be) return be.Block.Stmts.Count == 0 && be.Block.Tail != null && !IsBlockLike(be.Block.Tail) && !IsJump(be.Block.Tail);
            return false;
        }

        static bool IsJump(RExpr e) => e is RReturn || e is RBreak || e is RContinue;

        // ------------------------------------------------------------------ statements
        void Stmt(RStmt s, List<IrStmt> outList)
        {
            switch (s)
            {
                case REmptyStmt _: return;
                case RLet l: Let(l, outList); return;
                case RItemStmt it:
                    if (it.Item is RConst c)
                    {
                        var d = new IrVarDecl { Symbol = Sym(c.Var), Init = Expr(c.Init), NameSpan = Sp(c.NameStart, c.NameEnd), Span = Sp(c), TypeExplicit = true };
                        d.Symbol.IsConst = true;
                        outList.Add(d);
                    }
                    return;
                case RExprStmt es: ExprStmt(es.E, outList, Sp(es)); return;
            }
            throw NotSupported("this statement", s);
        }

        void Let(RLet l, List<IrStmt> outList)
        {
            var p = l.Pat;
            switch (p.Kind)
            {
                case RPatKind.Ident:
                    {
                        var sym = Sym(p.Var);
                        var d = new IrVarDecl { Symbol = sym, NameSpan = Sp(p), Span = Sp(l), TypeExplicit = l.Type != null };
                        sym.DeclSpan = d.NameSpan;
                        if (l.Init == null) { outList.Add(d); return; }
                        if (IsBlockLike(l.Init) && !(l.Init is RIf ri && SimpleIf(ri)) && !(l.Init is RBlockExpr be0 && be0.Block.Stmts.Count == 0))
                        {
                            outList.Add(d);
                            Into(l.Init, new Sink { Target = () => new IrName(sym) { Span = Sp(p), Type = sym.Type } }, outList);
                            return;
                        }
                        d.Init = Expr(l.Init);
                        outList.Add(d);
                        return;
                    }
                case RPatKind.Wild:
                    if (l.Init != null) ExprStmt(l.Init, outList, Sp(l));
                    return;
                case RPatKind.Tuple:
                    {
                        if (l.Init == null) throw NotSupported("a tuple pattern without a value", l);
                        foreach (var v in Bindings(p))
                            outList.Add(new IrVarDecl { Symbol = Sym(v.Var), NameSpan = Sp(v), Span = Sp(v), TypeExplicit = false });
                        outList.Add(new IrAssign { Target = TupleTarget(p), Value = Expr(l.Init), Span = Sp(l), OpSpan = Span.None });
                        return;
                    }
                case RPatKind.Ref:
                    {
                        if (p.Inner.Kind != RPatKind.Ident) throw NotSupported("this pattern", p);
                        var sym = Sym(p.Inner.Var);
                        outList.Add(new IrVarDecl { Symbol = sym, NameSpan = Sp(p.Inner), Span = Sp(l), TypeExplicit = l.Type != null, Init = l.Init == null ? null : Expr(l.Init) });
                        return;
                    }
            }
            throw NotSupported("this pattern", p);
        }

        static IEnumerable<RPat> Bindings(RPat p)
        {
            switch (p.Kind)
            {
                case RPatKind.Ident: if (p.Var != null) yield return p; break;
                case RPatKind.Ref: foreach (var x in Bindings(p.Inner)) yield return x; break;
                case RPatKind.Tuple: case RPatKind.TupleStruct: case RPatKind.Or:
                    foreach (var i in p.Items) foreach (var x in Bindings(i)) yield return x;
                    break;
            }
        }

        IrExpr TupleTarget(RPat p)
        {
            switch (p.Kind)
            {
                case RPatKind.Ident: return new IrName(Sym(p.Var)) { Span = Sp(p) };
                case RPatKind.Ref: return TupleTarget(p.Inner);
                case RPatKind.Wild:
                    {
                        var s = Temp("_", RTy.ToIr(p.Type), p);
                        return new IrName(s) { Span = Sp(p) };
                    }
                case RPatKind.Tuple:
                    {
                        var t = new IrTupleLit { Span = Sp(p) };
                        foreach (var i in p.Items) t.Items.Add(TupleTarget(i));
                        return t;
                    }
            }
            throw NotSupported("this pattern", p);
        }

        void ExprStmt(RExpr e, List<IrStmt> outList, Span span)
        {
            switch (e)
            {
                case RAssign a: outList.Add(Assign(a, span)); return;
                case RIf i: outList.Add(IfStmt(i, Sink.Discard)); return;
                case RMatch m: MatchStmt(m, Sink.Discard, outList); return;
                case RBlockExpr be: outList.Add(Block(be.Block, Sink.Discard)); return;
                case RLoop lp: outList.Add(new IrLoop { Body = Block(lp.Body, Sink.Discard), KwSpan = Sp(lp.Start, lp.KwEnd), Span = Sp(lp) }); return;
                case RWhile w: outList.Add(While(w)); return;
                case RFor f: outList.Add(For(f)); return;
                case RBreak br: outList.Add(new IrBreak { Span = Sp(br) }); return;
                case RContinue co: outList.Add(new IrContinue { Span = Sp(co) }); return;
                case RReturn r:
                    if (r.Value != null && IsBlockLike(r.Value) && !(r.Value is RIf ri && SimpleIf(ri))) { Into(r.Value, Sink.Return, outList); return; }
                    outList.Add(new IrReturn { Value = r.Value == null ? null : Expr(r.Value), Span = Sp(r) });
                    return;
                case RMacro m when m.Name == "format": return;
                case RMethodCall mc:
                    switch (mc.Resolved)
                    {
                        case "map.insert":
                            outList.Add(new IrExprStmt { Expr = new IrOpCall(IrOp.MapPut, Expr(mc.Recv), Expr(mc.Args[0]), Expr(mc.Args[1])) { NameSpan = Sp(mc.NameStart, mc.NameEnd), Span = Sp(mc), Type = IrType.Void }, Span = span });
                            return;
                        case "vec.swap":
                            {
                                if (!Pure(mc.Recv) || !Pure(mc.Args[0]) || !Pure(mc.Args[1])) throw NotSupported("`swap` with calls in its arguments (store them in variables first)", mc);
                                IrExpr El(RExpr i) => new IrIndex { Target = Expr(mc.Recv), Index = Expr(i), Span = Sp(mc), Type = RTy.ToIr(RTy.Deref(mc.Recv.Type).A) };
                                var t = new IrTupleLit { Span = Sp(mc) }; t.Items.Add(El(mc.Args[0])); t.Items.Add(El(mc.Args[1]));
                                var v = new IrTupleLit { Span = Sp(mc) }; v.Items.Add(El(mc.Args[1])); v.Items.Add(El(mc.Args[0]));
                                outList.Add(new IrAssign { Target = t, Value = v, Span = span, OpSpan = Sp(mc.NameStart, mc.NameEnd) });
                                return;
                            }
                        case "str.push_str":
                            outList.Add(new IrAssign { Target = Expr(mc.Recv), Op = IrBinOp.Concat, Value = Expr(mc.Args[0]), Span = span, OpSpan = Sp(mc.NameStart, mc.NameEnd) });
                            return;
                    }
                    break;
            }
            var x = Expr(e, stmt: true);
            if (x == null) return;
            if (!(x is IrCall) && !(x is IrOpCall) && !HasCall(x)) return;   // a value without effect
            outList.Add(new IrExprStmt { Expr = x, Span = span });
        }

        static bool HasCall(IrExpr e) => IrWalk.Descendants(e, false).Any(n => n is IrCall || n is IrOpCall);

        static bool Pure(RExpr e)
        {
            switch (e)
            {
                case RLit _: case RPathExpr _: return true;
                case RUnary u: return Pure(u.Operand);
                case RBinary b: return Pure(b.Left) && Pure(b.Right);
                case RCast c: return Pure(c.Operand);
                case RField f: return Pure(f.Recv);
                case RIndex ix: return Pure(ix.Target) && Pure(ix.Index);
                case RMethodCall mc: return mc.Resolved == "len" && Pure(mc.Recv);
            }
            return false;
        }

        IrStmt Assign(RAssign a, Span span)
        {
            var opSpan = Sp(a.OpStart, a.OpEnd);
            if (a.Op == "=")
            {
                if (a.Target is RTuple tt)
                {
                    var t = new IrTupleLit { Span = Sp(tt) };
                    foreach (var i in tt.Items) t.Items.Add(Place(i));
                    return new IrAssign { Target = t, Value = Expr(a.Value), Span = span, OpSpan = opSpan };
                }
                if (a.Target is RPathExpr pe && pe.Segs.Count == 1 && pe.Segs[0].Name == "_")
                {
                    var l = new List<IrStmt>();
                    ExprStmt(a.Value, l, span);
                    return l.Count == 1 ? l[0] : new IrBlock(l) { Span = span };
                }
                if (IsBlockLike(a.Value) && !(a.Value is RIf ri && SimpleIf(ri)))
                {
                    var list = new List<IrStmt>();
                    Into(a.Value, new Sink { Target = () => Place(a.Target) }, list);
                    return list.Count == 1 ? list[0] : new IrBlock(list) { Span = span };
                }
                return new IrAssign { Target = Place(a.Target), Value = Expr(a.Value), Span = span, OpSpan = opSpan };
            }
            // *m.entry(k).or_insert(d) op= v
            if (a.Target is RUnary u && u.Op == "*" && u.Operand is RMethodCall oi && oi.Resolved == "map.entry")
            {
                var en = (RMethodCall)oi.Recv;
                if (!Pure(en.Recv) || !Pure(en.Args[0])) throw NotSupported("`entry` with calls in the key (store the key in a variable first)", en);
                var target = new IrIndex { Target = Expr(en.Recv), Index = Expr(en.Args[0]), Span = Sp(a.Target), Type = RTy.ToIr(a.Target.Type) };
                var cur = new IrOpCall(IrOp.MapGetOrDefault, Expr(en.Recv), Expr(en.Args[0]), Expr(oi.Args[0])) { NameSpan = Sp(oi.NameStart, oi.NameEnd), Span = Sp(oi), Type = RTy.ToIr(a.Target.Type) };
                var bop = CompoundOp(a);
                return new IrAssign { Target = target, Value = new IrBinary { Op = bop, Left = cur, Right = Expr(a.Value), OpSpan = opSpan, Span = Sp(a), Type = RTy.ToIr(a.Target.Type) }, Span = span, OpSpan = opSpan };
            }
            return new IrAssign { Target = Place(a.Target), Op = CompoundOp(a), Value = Expr(a.Value), Span = span, OpSpan = opSpan };
        }

        IrBinOp CompoundOp(RAssign a)
        {
            var tt = RTy.Deref(a.Target.Type);
            bool isInt = tt.IsIntLike;
            switch (a.Op)
            {
                case "+=": return tt.Kind == RK.String ? IrBinOp.Concat : IrBinOp.Add;
                case "-=": return IrBinOp.Sub;
                case "*=": return IrBinOp.Mul;
                case "/=": return isInt ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv;
                case "%=": return IrBinOp.ModTrunc;
            }
            throw NotSupported("the operator `" + a.Op + "`", a);
        }

        IrExpr Place(RExpr t)
        {
            switch (t)
            {
                case RPathExpr _: case RIndex _: return Expr(t);
            }
            throw NotSupported("this assignment target", t);
        }

        // ------------------------------------------------------------------ if / match
        IrStmt IfStmt(RIf i, Sink sink)
        {
            IrBlock Branch(RBlock b) => Block(b, sink);
            IrStmt ElsePart()
            {
                if (i.Else == null) return null;
                if (i.Else is RIf ei) return IfStmt(ei, sink);
                var be = (RBlockExpr)i.Else;
                return Branch(be.Block);
            }
            var kw = Sp(i.Start, i.KwEnd);
            if (i.LetPat == null)
            {
                var r = new IrIf { Cond = Expr(i.Cond), Then = Branch(i.Then), KwSpan = kw, Span = Sp(i) };
                r.Else = ElsePart();
                return r;
            }
            return IfLet(i.LetPat, i.LetExpr, () => Branch(i.Then), ElsePart, kw, Sp(i));
        }

        /// <summary>if let P = E { then } else { else }: Some(x) binds x = E and tests x != None; constants compare with ==.</summary>
        IrStmt IfLet(RPat pat, RExpr scrut, Func<IrBlock> then, Func<IrStmt> els, Span kw, Span span)
        {
            bool some = pat.Kind == RPatKind.TupleStruct;
            if (some)
            {
                var inner = pat.Items[0];
                // if let Some(x) = v.pop()
                if (scrut is RMethodCall pop && pop.Resolved == "vec.pop")
                {
                    var cond = new IrUnary { Op = IrUnOp.Not, Operand = new IrOpCall(IrOp.IsEmpty, Expr(pop.Recv)) { Span = Sp(pop), Synthetic = true, Type = IrType.Bool }, Span = Sp(pop), Synthetic = true, Type = IrType.Bool };
                    var thenB = then();
                    if (inner.Kind == RPatKind.Ident || inner.Kind == RPatKind.Ref)
                    {
                        var bind = inner.Kind == RPatKind.Ref ? inner.Inner : inner;
                        thenB.Stmts.Insert(0, new IrVarDecl { Symbol = Sym(bind.Var), Init = new IrOpCall(IrOp.ListPop, Expr(pop.Recv)) { NameSpan = Sp(pop.NameStart, pop.NameEnd), Span = Sp(pop), Type = RTy.ToIr(bind.Var.Type) }, NameSpan = Sp(bind), Span = Sp(pat), TypeExplicit = false });
                    }
                    else if (inner.Kind == RPatKind.Wild)
                        thenB.Stmts.Insert(0, new IrExprStmt(new IrOpCall(IrOp.ListPop, Expr(pop.Recv)) { NameSpan = Sp(pop.NameStart, pop.NameEnd), Span = Sp(pop) }));
                    else throw NotSupported("this pattern in `if let`", inner);
                    return new IrIf { Cond = cond, Then = thenB, Else = els(), KwSpan = kw, Span = span };
                }
                var value = Expr(scrut);
                if (inner.Kind == RPatKind.Wild)
                    return new IrIf { Cond = NullTest(value, false, pat), Then = then(), Else = els(), KwSpan = kw, Span = span };
                var bindPat = inner.Kind == RPatKind.Ref ? inner.Inner : inner;
                if (bindPat.Kind == RPatKind.Ident && bindPat.Var != null)
                {
                    var sym = Sym(bindPat.Var);
                    var blk = new IrBlock { Span = span };
                    blk.Stmts.Add(new IrVarDecl { Symbol = sym, Init = value, NameSpan = Sp(bindPat), Span = Sp(pat), TypeExplicit = false });
                    blk.Stmts.Add(new IrIf { Cond = NullTest(new IrName(sym) { Span = Sp(bindPat) }, false, pat), Then = then(), Else = els(), KwSpan = kw, Span = span });
                    return blk;
                }
                if (IsConstPattern(inner))
                    return new IrIf { Cond = Compare(value, inner), Then = then(), Else = els(), KwSpan = kw, Span = span };
                if (bindPat.Kind == RPatKind.Tuple)
                {
                    // if let Some((a, (x, y))) = e: value = e; if value != None { (a, (x, y)) = value; ... }
                    var tmp = Temp("value", value.Type, inner);
                    var blk = new IrBlock { Span = span };
                    blk.Stmts.Add(new IrVarDecl { Symbol = tmp, Init = value, NameSpan = Sp(inner), Span = Sp(pat), TypeExplicit = false });
                    var thenB = then();
                    var pro = new List<IrStmt>();
                    foreach (var v in Bindings(bindPat)) pro.Add(new IrVarDecl { Symbol = Sym(v.Var), NameSpan = Sp(v), Span = Sp(v), TypeExplicit = false });
                    pro.Add(new IrAssign { Target = TupleTarget(bindPat), Value = new IrName(tmp) { Span = Sp(inner) }, Span = Sp(inner), OpSpan = Span.None });
                    thenB.Stmts.InsertRange(0, pro);
                    blk.Stmts.Add(new IrIf { Cond = NullTest(new IrName(tmp) { Span = Sp(inner) }, false, pat), Then = thenB, Else = els(), KwSpan = kw, Span = span });
                    return blk;
                }
                throw NotSupported("this pattern in `if let`", inner);
            }
            if (IsConstPattern(pat))
                return new IrIf { Cond = Compare(Expr(scrut), pat), Then = then(), Else = els(), KwSpan = kw, Span = span };
            if (pat.Kind == RPatKind.Wild || (pat.Kind == RPatKind.Ident && pat.Var != null))
            {
                var blk = new IrBlock { Span = span };
                if (pat.Var != null) blk.Stmts.Add(new IrVarDecl { Symbol = Sym(pat.Var), Init = Expr(scrut), NameSpan = Sp(pat), Span = Sp(pat) });
                var tb = then();
                foreach (var s in tb.Stmts) blk.Stmts.Add(s);
                return blk;
            }
            throw NotSupported("this pattern in `if let`", pat);
        }

        IrExpr NullTest(IrExpr value, bool isNull, RNode at) =>
            new IrBinary { Op = isNull ? IrBinOp.Eq : IrBinOp.Ne, Left = value, Right = new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Synthetic = true, Span = Sp(at) }, Span = Sp(at), Synthetic = true, Type = IrType.Bool };

        bool IsConstPattern(RPat p)
        {
            switch (p.Kind)
            {
                case RPatKind.Lit: case RPatKind.Path: return true;
                case RPatKind.Ident: return p.Var == null;
                case RPatKind.TupleStruct: return p.Items.Count == 1 && IsConstPattern(p.Items[0]);
                case RPatKind.Or: return p.Items.All(IsConstPattern);
            }
            return false;
        }

        /// <summary>Constant pattern labels (or-patterns give several).</summary>
        IEnumerable<IrExpr> Labels(RPat p)
        {
            switch (p.Kind)
            {
                case RPatKind.Lit: yield return Expr(p.Lit); break;
                case RPatKind.TupleStruct: foreach (var l in Labels(p.Items[0])) yield return l; break;
                case RPatKind.Or: foreach (var a in p.Items) foreach (var l in Labels(a)) yield return l; break;
                case RPatKind.Path:
                case RPatKind.Ident:
                    {
                        var path = p.Path ?? new List<string> { p.Name };
                        int k = path[0] == "farm" ? 1 : 0;
                        if ((path.Count == 1 && path[0] == "None") || (path.Count == 2 && path[1] == "None"))
                            yield return new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = Sp(p) };
                        else if (path.Count - k == 2)
                        {
                            var cat = path[k] == "Direction" ? "Directions" : RustApi.CategoryOfEnum(path[k]);
                            yield return new IrConst(cat, path[k + 1]) { Span = Sp(p) };
                        }
                        else if (path.Count == 1 && Array.IndexOf(GameApi.DirectionNames, path[0]) >= 0 && !chk.Globals.ContainsKey(path[0]))
                            yield return new IrConst("Directions", path[0]) { Span = Sp(p) };
                        else if (path.Count == 1 && chk.Globals.TryGetValue(path[0], out var g))
                            yield return new IrName(Sym(g)) { Span = Sp(p) };
                        else throw NotSupported("this pattern", p);
                        break;
                    }
                default: throw NotSupported("this pattern", p);
            }
        }

        IrExpr Compare(IrExpr value, RPat p)
        {
            var labels = Labels(p).ToList();
            IrExpr res = null;
            foreach (var l in labels)
            {
                var eq = new IrBinary { Op = IrBinOp.Eq, Left = res == null ? value : CloneSimple(value), Right = l, Span = Sp(p), OpSpan = Sp(p), Type = IrType.Bool };
                res = res == null ? eq : new IrBinary { Op = IrBinOp.Or, Left = res, Right = eq, Span = Sp(p), Synthetic = true, Type = IrType.Bool };
            }
            return res;
        }

        static IrExpr CloneSimple(IrExpr e)
        {
            if (e is IrName n) return new IrName(n.Symbol) { Span = n.Span, Type = n.Type, QualifierSpan = n.QualifierSpan, NameSpan = n.NameSpan };
            throw new RustAbort("an or-pattern in `if let` on a call is not supported in JavaFarmer (store the value in a variable first)", e.Span.Start, e.Span.End);
        }

        void MatchStmt(RMatch m, Sink sink, List<IrStmt> outList)
        {
            var kw = Sp(m.Start, m.KwEnd);
            IrBlock Body(RArm arm)
            {
                var b = new IrBlock { Span = Sp(arm.Body) };
                if (arm.Body is RBlockExpr be) { var inner = Block(be.Block, sink); inner.Span = Sp(be); return inner; }
                Into(arm.Body, sink, b.Stmts);
                return b;
            }
            var sd = RTy.Deref(m.Scrutinee.Type);
            // Option match: Some(x) / None (or _)
            var someArm = m.Arms.FirstOrDefault(a => a.Pat.Kind == RPatKind.TupleStruct && !IsConstPattern(a.Pat.Items[0]));
            if (sd.Kind == RK.Option && someArm != null)
            {
                var other = m.Arms.Where(a => a != someArm).ToList();
                if (other.Count > 1 || (other.Count == 1 && !(IsNonePat(other[0].Pat) || other[0].Pat.Kind == RPatKind.Wild)))
                    throw NotSupported("this `match` on an Option (use `Some(x) => ..., None => ...`)", m);
                var elseArm = other.Count == 1 ? other[0] : null;
                var res = IfLet(someArm.Pat, m.Scrutinee, () => Body(someArm), () => elseArm == null ? null : Body(elseArm), kw, Sp(m));
                outList.Add(res);
                return;
            }
            var sw = new IrSwitch { KwSpan = kw, Span = Sp(m), Style = IrSwitchStyle.Match };
            IrSymbol bound = null;
            var last = m.Arms.Count > 0 ? m.Arms[m.Arms.Count - 1] : null;
            foreach (var arm in m.Arms)
            {
                var p = arm.Pat;
                var c = new IrCase { LabelSpan = Sp(p.Start, arm.ArrowEnd), Span = Sp(arm) };
                if (p.Kind == RPatKind.Wild) c.IsDefault = true;
                else if (p.Kind == RPatKind.Ident && p.Var != null)
                {
                    if (arm != last) throw NotSupported("a binding pattern before the last `match` arm", p);
                    c.IsDefault = true;
                    bound = Sym(p.Var);
                }
                else if (IsConstPattern(p)) c.Labels.AddRange(Labels(p));
                else throw NotSupported("this `match` pattern", p);
                c.Body = Body(arm);
                sw.Cases.Add(c);
                if (c.IsDefault) break;
            }
            if (bound != null)
            {
                outList.Add(new IrVarDecl { Symbol = bound, Init = Expr(m.Scrutinee), NameSpan = Sp(last.Pat), Span = Sp(last.Pat) });
                sw.Selector = new IrName(bound) { Span = Sp(m.Scrutinee) };
            }
            else sw.Selector = Expr(m.Scrutinee);
            outList.Add(sw);
        }

        static bool IsNonePat(RPat p) =>
            (p.Kind == RPatKind.Path && p.Path.Count >= 1 && p.Path[p.Path.Count - 1] == "None") || (p.Kind == RPatKind.Ident && p.Name == "None" && p.Var == null);

        // ------------------------------------------------------------------ loops
        IrStmt While(RWhile w)
        {
            var kw = Sp(w.Start, w.KwEnd);
            if (w.LetPat != null)
            {
                if (w.LetPat.Kind == RPatKind.TupleStruct && w.LetExpr is RMethodCall pop && pop.Resolved == "vec.pop")
                {
                    var inner = w.LetPat.Items[0];
                    var cond = new IrUnary { Op = IrUnOp.Not, Operand = new IrOpCall(IrOp.IsEmpty, Expr(pop.Recv)) { Span = Sp(pop), Synthetic = true, Type = IrType.Bool }, Span = Sp(pop), Synthetic = true, Type = IrType.Bool };
                    var body = Block(w.Body, Sink.Discard);
                    var bind = inner.Kind == RPatKind.Ref ? inner.Inner : inner;
                    var popCall = new IrOpCall(IrOp.ListPop, Expr(pop.Recv)) { NameSpan = Sp(pop.NameStart, pop.NameEnd), Span = Sp(pop), Type = RTy.ToIr(RTy.Prune(pop.Type).A) };
                    if (bind.Kind == RPatKind.Ident && bind.Var != null)
                        body.Stmts.Insert(0, new IrVarDecl { Symbol = Sym(bind.Var), Init = popCall, NameSpan = Sp(bind), Span = Sp(w.LetPat), TypeExplicit = false });
                    else if (bind.Kind == RPatKind.Wild) body.Stmts.Insert(0, new IrExprStmt(popCall));
                    else throw NotSupported("this pattern in `while let`", inner);
                    return new IrWhile { Cond = cond, Body = body, KwSpan = kw, Span = Sp(w) };
                }
                throw NotSupported("this `while let` (only `while let Some(x) = v.pop()` is)", Sp(w.Start, w.LetExpr.End).Start == w.Start ? (RNode)w.LetExpr : w);
            }
            return new IrWhile { Cond = Expr(w.Cond), Body = Block(w.Body, Sink.Discard), KwSpan = kw, Span = Sp(w) };
        }

        IrSymbol LoopVar(RPat p, RTy t, out Span span, List<IrStmt> prologue)
        {
            span = Sp(p);
            switch (p.Kind)
            {
                case RPatKind.Ident: return Sym(p.Var);
                case RPatKind.Ref: return LoopVar(p.Inner, t, out span, prologue);
                case RPatKind.Wild: return Temp("_", RTy.ToIr(t), p);
                case RPatKind.Tuple:
                    {
                        var tmp = Temp("item", RTy.ToIr(t), p);
                        foreach (var v in Bindings(p)) prologue.Add(new IrVarDecl { Symbol = Sym(v.Var), NameSpan = Sp(v), Span = Sp(v), TypeExplicit = false });
                        prologue.Add(new IrAssign { Target = TupleTarget(p), Value = new IrName(tmp) { Span = Sp(p) }, Span = Sp(p), OpSpan = Span.None });
                        return tmp;
                    }
            }
            throw NotSupported("this loop pattern", p);
        }

        IrStmt For(RFor f)
        {
            var kw = Sp(f.Start, f.KwEnd);
            var hdr = Sp(f.Start, f.HeaderEnd);
            var prologue = new List<IrStmt>();
            switch (f.Shape)
            {
                case "range":
                    {
                        var r = f.Range;
                        var v = LoopVar(f.Pat, RTy.Prune(f.Range.Type).A, out var vs, prologue);
                        var rf = new IrRangeFor { Var = v, KwSpan = kw, HeaderSpan = hdr, VarSpan = vs, Span = Sp(f) };
                        var from = Expr(r.From); var to = Expr(r.To);
                        if (!f.Rev)
                        {
                            rf.Start = IsZero(from) && r.From is RLit ? null : from;
                            if (rf.Start == null) rf.Start = from;
                            rf.End = to; rf.Inclusive = r.Inclusive;
                            if (f.Step != null) rf.Step = Expr(f.Step);
                        }
                        else
                        {
                            long step = f.Step is RLit sl ? (long)sl.I : 1;
                            if (from is IrLiteral fl && fl.Kind == IrLitKind.Int && to is IrLiteral tl && tl.Kind == IrLitKind.Int)
                            {
                                // (a..b).rev() with literal bounds: range(b - 1, a - 1, -1) folded
                                rf.Start = new IrLiteral { Kind = IrLitKind.Int, Int = r.Inclusive ? tl.Int : tl.Int - 1, Type = IrType.Int, Synthetic = true, Span = Sp(r.To) };
                                rf.End = new IrLiteral { Kind = IrLitKind.Int, Int = fl.Int - 1, Type = IrType.Int, Synthetic = true, Span = Sp(r.From) };
                                rf.Step = new IrLiteral { Kind = IrLitKind.Int, Int = -step, Type = IrType.Int, Synthetic = true, Span = Sp(f.Iter) };
                                rf.Body = Block(f.Body, Sink.Discard);
                                rf.Body.Stmts.InsertRange(0, prologue);
                                return rf;
                            }
                            rf.Inclusive = true;
                            rf.End = from;
                            rf.Start = r.Inclusive ? to : new IrBinary { Op = IrBinOp.Sub, Left = to, Right = new IrLiteral { Kind = IrLitKind.Int, Int = 1, Type = IrType.Int, Synthetic = true, Span = Sp(r.To) }, Span = Sp(r.To), Synthetic = true, Type = IrType.Int };
                            rf.Step = new IrLiteral { Kind = IrLitKind.Int, Int = -step, Type = IrType.Int, Synthetic = true, Span = Sp(f.Iter) };
                        }
                        rf.Body = Block(f.Body, Sink.Discard);
                        rf.Body.Stmts.InsertRange(0, prologue);
                        return rf;
                    }
                case "enumerate":
                    {
                        if (f.Pat.Kind != RPatKind.Tuple || f.Pat.Items.Count != 2) throw NotSupported("this pattern with `.enumerate()` (write `for (i, x) in v.iter().enumerate()`)", f.Pat);
                        if (!Pure(f.Source)) throw NotSupported("`.enumerate()` on a call (store the list in a variable first)", f.Source);
                        var ip = f.Pat.Items[0]; var xp = f.Pat.Items[1];
                        var iv = LoopVar(ip, RTy.USize, out var ispan, prologue);
                        var len = new IrOpCall(IrOp.Len, Expr(f.Source)) { Span = Sp(f.Iter), NameSpan = Sp(f.Iter), Synthetic = true, Type = IrType.Int };
                        var rf = new IrRangeFor { Var = iv, End = len, KwSpan = kw, HeaderSpan = hdr, VarSpan = ispan, Span = Sp(f) };
                        var bind = xp.Kind == RPatKind.Ref ? xp.Inner : xp;
                        if (bind.Kind == RPatKind.Ident && bind.Var != null)
                        {
                            var el = new IrIndex { Target = Expr(f.Source), Index = new IrName(iv) { Span = ispan }, Span = Sp(xp), Synthetic = true, Type = Sym(bind.Var).Type };
                            prologue.Add(new IrVarDecl { Symbol = Sym(bind.Var), Init = el, NameSpan = Sp(bind), Span = Sp(xp), Synthetic = true, TypeExplicit = false });
                        }
                        else if (bind.Kind != RPatKind.Wild) throw NotSupported("this pattern with `.enumerate()`", xp);
                        rf.Body = Block(f.Body, Sink.Discard);
                        rf.Body.Stmts.InsertRange(0, prologue);
                        return rf;
                    }
                case "map":
                    {
                        if (f.Pat.Kind != RPatKind.Tuple || f.Pat.Items.Count != 2) throw NotSupported("this pattern over a map (write `for (k, v) in &m`)", f.Pat);
                        var kp = f.Pat.Items[0]; var vp = f.Pat.Items[1];
                        var k = LoopVar(kp, null, out var kspan, prologue);
                        IrSymbol v = null; Span vspan = Span.None;
                        var vb = vp.Kind == RPatKind.Ref ? vp.Inner : vp;
                        if (vb.Kind != RPatKind.Wild) v = LoopVar(vp, null, out vspan, prologue);
                        var fe = new IrForEach { Var = k, ValueVar = v, Iterable = Expr(f.Source), KwSpan = kw, VarSpan = kspan, ValueVarSpan = vspan, Span = Sp(f) };
                        fe.Body = Block(f.Body, Sink.Discard);
                        fe.Body.Stmts.InsertRange(0, prologue);
                        return fe;
                    }
                case "keys":
                case "values":
                case "elems":
                    {
                        var v = LoopVar(f.Pat, null, out var vspan, prologue);
                        IrExpr it = Expr(f.Source);
                        if (f.Shape == "keys") it = new IrOpCall(IrOp.MapKeys, it) { Span = Sp(f.Iter), NameSpan = Sp(f.Iter), Synthetic = true, Type = IrType.Set(it.Type.Key ?? IrType.Unknown) };
                        else if (f.Shape == "values") it = new IrOpCall(IrOp.MapValues, it) { Span = Sp(f.Iter), NameSpan = MethodSpan(f.Iter), Type = IrType.List(it.Type.Elem ?? IrType.Unknown) };
                        var fe = new IrForEach { Var = v, Iterable = it, KwSpan = kw, VarSpan = vspan, Span = Sp(f) };
                        fe.Body = Block(f.Body, Sink.Discard);
                        fe.Body.Stmts.InsertRange(0, prologue);
                        return fe;
                    }
            }
            throw NotSupported("this `for` loop", f);
        }

        static Span MethodSpan(RExpr e)
        {
            while (e is RMethodCall m) { if (m.Name == "values" || m.Name == "keys") return Sp(m.NameStart, m.NameEnd); e = m.Recv; }
            return Sp(e);
        }

        static bool IsZero(IrExpr e) => e is IrLiteral l && l.Kind == IrLitKind.Int && l.Int == 0;

        // ------------------------------------------------------------------ expressions
        IrExpr Expr(RExpr e, bool stmt = false)
        {
            var r = ExprCore(e, stmt);
            if (r == null) return null;
            if (r.Span.IsNone) r.Span = Sp(e);
            if (e.ParenStart >= 0 && r.ParenSpan.IsNone) r.ParenSpan = Sp(e.ParenStart, e.ParenEnd);
            if (!r.Type.IsKnown && e.Type != null) r.Type = RTy.ToIr(e.Type);
            return r;
        }

        IrLiteral Lit(RLit l)
        {
            switch (l.Kind)
            {
                case RLitKind.Int:
                    if (RTy.Prune(l.Type).Kind == RK.Float) return IrLiteral.OfFloat(l.I);
                    return IrLiteral.OfInt((long)l.I);
                case RLitKind.Float: return IrLiteral.OfFloat(l.F);
                case RLitKind.Bool: return IrLiteral.OfBool(l.B);
                case RLitKind.Str:
                    if ((l.S ?? "").IndexOf('\n') >= 0 || (l.S ?? "").IndexOf('\r') >= 0)
                        throw new RustAbort("a string with a line break is not supported in JavaFarmer (the game's strings are one line)", l.Start, l.End);
                    return IrLiteral.OfString(l.S ?? "");
            }
            throw NotSupported("this literal", l);
        }

        IrExpr ExprCore(RExpr e, bool stmt)
        {
            switch (e)
            {
                case RLit l: return Lit(l);
                case RPathExpr pe: return Path(pe);
                case RUnary u:
                    switch (u.Op)
                    {
                        case "-": return new IrUnary { Op = IrUnOp.Neg, Operand = Expr(u.Operand), OpSpan = Sp(u.Start, u.OpEnd) };
                        case "!": return new IrUnary { Op = IrUnOp.Not, Operand = Expr(u.Operand), OpSpan = Sp(u.Start, u.OpEnd) };
                        default: return Expr(u.Operand);
                    }
                case RBinary b: return Binary(b);
                case RCast c:
                    {
                        var operand = Expr(c.Operand);
                        var to = RTy.Prune(c.Type);
                        var from = RTy.Deref(c.Operand.Type);
                        var opSpan = Sp(c.AsStart, c.End);
                        if (to.Kind == RK.Int && from.Kind == RK.Float) return new IrCast { Target = IrType.Int, Operand = operand, OpSpan = opSpan, Type = IrType.Int };
                        if (to.Kind == RK.Float && from.Kind == RK.Int) return new IrCast { Target = IrType.Float, Operand = operand, OpSpan = opSpan, Type = IrType.Float };
                        return operand;
                    }
                case RField f:
                    {
                        var idx = IrLiteral.OfInt(f.TupleIndex);
                        idx.Span = Sp(f.NameStart, f.NameEnd);
                        return new IrIndex { Target = Expr(f.Recv), Index = idx };
                    }
                case RIndex ix: return new IrIndex { Target = Expr(ix.Target), Index = Expr(ix.Index) };
                case RTuple t:
                    {
                        if (t.Items.Count == 0) throw NotSupported("the unit value `()` here", t);
                        var tl = new IrTupleLit();
                        foreach (var i in t.Items) tl.Items.Add(Expr(i));
                        return tl;
                    }
                case RArray arr: return ArrayLit(arr);
                case RBlockExpr be:
                    if (be.Block.Stmts.Count == 0 && be.Block.Tail != null) return Expr(be.Block.Tail, stmt);
                    throw NotSupported("a block with statements in this position", be);
                case RIf i:
                    if (SimpleIf(i)) return Conditional(i);
                    throw NotSupported("an `if` expression in this position (use it as a whole `let` value, assignment or return value)", i);
                case RMatch m: throw NotSupported("a `match` expression in this position (use it as a whole `let` value, assignment or return value)", m);
                case RClosure c: return Lambda(c);
                case RCall call: return Call(call);
                case RMethodCall mc: return Method(mc, stmt);
                case RMacro m: return MacroCall(m);
                case RAssign a: throw NotSupported("an assignment used as a value", a);
                case RLoop _: case RWhile _: case RFor _: throw NotSupported("a loop used as a value", e);
                case RReturn _: case RBreak _: case RContinue _: throw NotSupported("`" + (e is RReturn ? "return" : e is RBreak ? "break" : "continue") + "` inside an expression", e);
                case RRange r: throw NotSupported("a range outside a `for` loop", r);
            }
            throw NotSupported("this expression", e);
        }

        IrExpr Conditional(RIf i)
        {
            var then = Expr(i.Then.Tail);
            IrExpr els = i.Else is RIf ei ? Conditional(ei) : Expr(((RBlockExpr)i.Else).Block.Tail);
            return new IrConditional { Cond = Expr(i.Cond), Then = then, Else = els, QSpan = Sp(i.Start, i.KwEnd) };
        }

        IrExpr Path(RPathExpr pe)
        {
            var nameSeg = pe.Segs[pe.Segs.Count - 1];
            if (pe.Var != null)
            {
                var s = Sym(pe.Var);
                var n = new IrName(s);
                if (s.Kind == IrSymbolKind.ExternalGlobal) { n.QualifierSpan = Sp(pe.Segs[0].Start, pe.Segs[pe.Segs.Count - 2].End); n.NameSpan = Sp(nameSeg.Start, nameSeg.End); }
                return n;
            }
            if (pe.Fn != null)
            {
                var fr = new IrFuncRef { Function = FnSym(pe.Fn), NameSpan = Sp(nameSeg.Start, nameSeg.End), Type = IrType.Func(RTy.ToIr(pe.Fn.Ret)) };
                if (pe.Fn.Module != null && pe.Segs.Count > 1) fr.QualifierSpan = Sp(pe.Segs[0].Start, pe.Segs[pe.Segs.Count - 2].End);
                return fr;
            }
            if (pe.ConstCategory != null) return new IrConst(pe.ConstCategory, pe.ConstName);
            if (pe.IsNone) return IrLiteral.Null();
            if (pe.Builtin != null)
            {
                if (pe.Builtin.StartsWith("const:"))
                {
                    switch (pe.Builtin.Substring(6))
                    {
                        case "i32::MAX": return IrLiteral.OfInt(int.MaxValue);
                        case "i32::MIN": return IrLiteral.OfInt(int.MinValue);
                        case "i64::MAX": return IrLiteral.OfInt(long.MaxValue);
                        case "i64::MIN": return IrLiteral.OfInt(long.MinValue);
                        case "u32::MAX": return IrLiteral.OfInt(uint.MaxValue);
                        case "u8::MAX": return IrLiteral.OfInt(255);
                        case "i8::MAX": return IrLiteral.OfInt(127);
                        case "i8::MIN": return IrLiteral.OfInt(-128);
                        case "i16::MAX": return IrLiteral.OfInt(short.MaxValue);
                        case "i16::MIN": return IrLiteral.OfInt(short.MinValue);
                        case "u16::MAX": return IrLiteral.OfInt(ushort.MaxValue);
                    }
                    if (pe.Builtin.EndsWith("::MIN")) return IrLiteral.OfInt(0);
                    return IrLiteral.OfInt(long.MaxValue);
                }
                return new IrFuncRef { Builtin = pe.Builtin, NameSpan = Sp(nameSeg.Start, nameSeg.End) };
            }
            throw NotSupported("this name", pe);
        }

        IrExpr Binary(RBinary b)
        {
            IrBinOp op;
            var lt = RTy.Deref(b.Left.Type);
            switch (b.Op)
            {
                case "+": op = RTy.Deref(b.Type).Kind == RK.String ? IrBinOp.Concat : IrBinOp.Add; break;
                case "-": op = IrBinOp.Sub; break;
                case "*": op = IrBinOp.Mul; break;
                case "/": op = lt.IsIntLike ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv; break;
                case "%": op = IrBinOp.ModTrunc; break;
                case "==": op = IrBinOp.Eq; break;
                case "!=": op = IrBinOp.Ne; break;
                case "<": op = IrBinOp.Lt; break;
                case ">": op = IrBinOp.Gt; break;
                case "<=": op = IrBinOp.Le; break;
                case ">=": op = IrBinOp.Ge; break;
                case "&&": op = IrBinOp.And; break;
                case "||": op = IrBinOp.Or; break;
                default: throw NotSupported("the operator `" + b.Op + "`", b);
            }
            return new IrBinary { Op = op, Left = Expr(b.Left), Right = Expr(b.Right), OpSpan = Sp(b.OpStart, b.OpEnd) };
        }

        IrExpr ArrayLit(RArray arr)
        {
            if (arr.RepeatValue != null)
            {
                // vec![vec![d; h]; w] → w independent rows
                if (arr.RepeatValue is RArray inner && inner.RepeatValue != null)
                    return new IrOpCall(IrOp.ArrayNew2, Expr(arr.RepeatCount), Expr(inner.RepeatCount), Expr(inner.RepeatValue)) { NameSpan = Sp(arr.Start, arr.Start + 1) };
                if (RTy.Prune(arr.RepeatValue.Type).Kind == RK.Vec && !(arr.RepeatValue is RArray))
                    throw NotSupported("repeating a list value (use `vec![vec![x; h]; w]`)", arr.RepeatValue);
                return new IrOpCall(IrOp.ArrayNew, Expr(arr.RepeatCount), Expr(arr.RepeatValue)) { NameSpan = Sp(arr.Start, arr.Start + 1) };
            }
            var l = new IrListLit { IsArray = !arr.IsVecMacro };
            foreach (var i in arr.Items) l.Items.Add(Expr(i));
            return l;
        }

        IrExpr Lambda(RClosure c)
        {
            var ct = RTy.Prune(c.Type);
            var ret = RTy.Prune(ct.Ret);
            bool returns = ret.Kind != RK.Unit && ret.Kind != RK.Never;
            var l = new IrLambda { ReturnsValue = returns, ReturnType = RTy.ToIr(ret), HeaderSpan = Sp(c.Start, c.HeaderEnd) };
            foreach (var p in c.Params)
            {
                IrSymbol ps;
                if (p.Pat.Kind == RPatKind.Ident) ps = Sym(p.Pat.Var);
                else if (p.Pat.Kind == RPatKind.Wild) ps = new IrSymbol("_", IrSymbolKind.Param, RTy.ToIr(p.Pat.Type));
                else throw NotSupported("a pattern as a closure parameter", p.Pat);
                l.Params.Add(new IrParam { Symbol = ps, Span = Sp(p), NameSpan = Sp(p.Pat) });
            }
            if (c.Body is RBlockExpr be) l.Body = Block(be.Block, returns ? Sink.Return : Sink.Discard);
            else if (IsBlockLike(c.Body) || c.Body is RAssign || c.Body is RReturn)
            {
                l.Body = new IrBlock { Span = Sp(c.Body) };
                Into(c.Body, returns ? Sink.Return : Sink.Discard, l.Body.Stmts);
            }
            else l.BodyExpr = Expr(c.Body);
            l.Type = RTy.ToIr(ct);
            return l;
        }

        IrExpr Call(RCall c)
        {
            var nameSpan = Sp(c.Callee);
            if (c.Callee is RPathExpr cp) { var last = cp.Segs[cp.Segs.Count - 1]; nameSpan = Sp(last.Start, last.End); }
            List<IrExpr> Args() => c.Args.Select(a => Expr(a)).ToList();
            switch (c.Special)
            {
                case "builtin":
                    {
                        var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = c.Api.B.PythonName, NameSpan = nameSpan };
                        if (c.SpreadArgs != null)
                        {
                            for (int i = 0; i < c.Args.Count; i++)
                            {
                                if (c.Api.Params[i].Variadic) foreach (var s in c.SpreadArgs) call.Args.Add(Expr(s));
                                else call.Args.Add(Expr(c.Args[i]));
                            }
                        }
                        else call.Args.AddRange(Args());
                        return call;
                    }
                case "min": case "max":
                    {
                        var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = c.Special, NameSpan = nameSpan };
                        call.Args.AddRange(Args());
                        return call;
                    }
                case "fn":
                    {
                        var call = new IrCall { Kind = IrCallKind.Function, Function = FnSym(c.FnSym), NameSpan = nameSpan };
                        if (c.FnSym.Module != null && c.Callee is RPathExpr pe && pe.Segs.Count > 1) call.QualifierSpan = Sp(pe.Segs[0].Start, pe.Segs[pe.Segs.Count - 2].End);
                        call.Args.AddRange(Args());
                        return call;
                    }
                case "value":
                    {
                        var call = new IrCall { Kind = IrCallKind.Value, Callee = Expr(c.Callee), NameSpan = nameSpan };
                        call.Args.AddRange(Args());
                        return call;
                    }
                case "some": return Expr(c.Args[0]);
                case "vec.new": return new IrListLit();
                case "map.new": return new IrMapLit();
                case "set.new": return new IrSetLit();
                case "string.new": return IrLiteral.OfString("");
                case "string.from": return Expr(c.Args[0]);
                case "vec.from":
                    {
                        var l = new IrListLit();
                        foreach (var i in ((RArray)c.Args[0]).Items) l.Items.Add(Expr(i));
                        return l;
                    }
                case "set.from":
                    {
                        var s = new IrSetLit();
                        foreach (var i in ((RArray)c.Args[0]).Items) s.Items.Add(Expr(i));
                        return s;
                    }
                case "map.from":
                    {
                        var m = new IrMapLit();
                        foreach (var i in ((RArray)c.Args[0]).Items)
                        {
                            var t = (RTuple)i;
                            m.Keys.Add(Expr(t.Items[0]));
                            m.Values.Add(Expr(t.Items[1]));
                        }
                        return m;
                    }
            }
            throw NotSupported("this call", c);
        }

        static RExpr StripCopied(RExpr e)
        {
            while (e is RMethodCall m && m.Resolved == "opt.copied") e = m.Recv;
            return e;
        }

        IrExpr Method(RMethodCall mc, bool stmt)
        {
            var ns = Sp(mc.NameStart, mc.NameEnd);
            IrOpCall Op(IrOp op, params IrExpr[] args) => new IrOpCall(op, args) { NameSpan = ns };
            IrExpr R() => Expr(mc.Recv);
            IrExpr A(int i) => Expr(mc.Args[i]);
            var rd = RTy.Deref(mc.Recv.Type);
            switch (mc.Resolved)
            {
                case "vec.push": return Op(IrOp.ListAppend, R(), A(0));
                case "vec.pop": return Op(IrOp.ListPop, R());
                case "len": return Op(IrOp.Len, mc.Recv is RMethodCall chs && chs.Name == "chars" ? Expr(chs.Recv) : R());
                case "is_empty": return Op(IrOp.IsEmpty, R());
                case "contains": return Op(IrOp.Contains, R(), A(0));
                case "clear": return Op(IrOp.Clear, R());
                case "copy.list": return Op(IrOp.ListCopy, R());
                case "copy.set": return Op(IrOp.SetCopy, R());
                case "copy.map": return Op(IrOp.MapCopy, R());
                case "vec.insert": return Op(IrOp.ListInsert, R(), A(0), A(1));
                case "vec.remove": return Op(IrOp.ListPop, R(), A(0));
                case "vec.remove_value": return Op(IrOp.RemoveValue, R(), A(0));
                case "vec.index_of": return Op(IrOp.ListIndexOf, R(), A(0));
                case "map.insert":
                    if (!stmt) throw NotSupported("using the value of `insert` on a HashMap", mc);
                    return Op(IrOp.MapPut, R(), A(0), A(1));
                case "map.get": return Op(IrOp.MapGetOrNull, R(), A(0));
                case "map.remove": return Op(IrOp.MapRemoveIfPresent, R(), A(0));
                case "set.insert":
                    if (!stmt) throw NotSupported("using the value of `insert` on a HashSet", mc);
                    return Op(IrOp.SetAdd, R(), A(0));
                case "set.remove":
                    if (!stmt) throw NotSupported("using the value of `remove` on a HashSet", mc);
                    return Op(IrOp.RemoveValueIfPresent, R(), A(0));
                case "opt.unwrap":
                    {
                        var inner = StripCopied(mc.Recv);
                        if (inner is RMethodCall g && g.Resolved == "map.get")
                            return new IrIndex { Target = Expr(g.Recv), Index = Expr(g.Args[0]) };
                        if (inner is RMethodCall mr && mr.Resolved == "map.remove")
                            return new IrOpCall(IrOp.MapRemove, Expr(mr.Recv), Expr(mr.Args[0])) { NameSpan = Sp(mr.NameStart, mr.NameEnd) };
                        return Expr(inner, stmt);
                    }
                case "opt.unwrap_or":
                    {
                        var inner = StripCopied(mc.Recv);
                        if (inner is RMethodCall g && g.Resolved == "map.get")
                            return new IrOpCall(IrOp.MapGetOrDefault, Expr(g.Recv), Expr(g.Args[0]), A(0)) { NameSpan = ns };
                        if (!(inner is RPathExpr)) throw NotSupported("`unwrap_or` on a call (store the value in a variable first)", mc);
                        var v = Expr(inner);
                        return new IrConditional { Cond = NullTest(Expr(inner), false, mc), Then = v, Else = A(0), QSpan = ns };
                    }
                case "opt.is_some": return new IrBinary { Op = IrBinOp.Ne, Left = R(), Right = new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = ns }, OpSpan = ns, Type = IrType.Bool };
                case "opt.is_none": return new IrBinary { Op = IrBinOp.Eq, Left = R(), Right = new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = ns }, OpSpan = ns, Type = IrType.Bool };
                case "opt.copied": return Expr(mc.Recv, stmt);
                case "same": return Expr(mc.Recv, stmt);
                case "num.to_string": return new IrCast { Target = IrType.String, Operand = R(), OpSpan = ns, Type = IrType.String };
                case "num.abs": { var c = new IrCall { Kind = IrCallKind.Builtin, Builtin = "abs", NameSpan = ns }; c.Args.Add(R()); return c; }
                case "num.min": case "num.max":
                    {
                        var c = new IrCall { Kind = IrCallKind.Builtin, Builtin = mc.Resolved.Substring(4), NameSpan = ns };
                        c.Args.Add(R()); c.Args.Add(A(0));
                        return c;
                    }
                case "num.pow": return new IrBinary { Op = IrBinOp.Pow, Left = R(), Right = A(0), OpSpan = ns };
                case "num.sqrt": return Op(IrOp.Sqrt, R());
                case "num.floor": return Op(IrOp.Floor, R());
                case "num.ceil": return Op(IrOp.Ceil, R());
                case "num.round": return Op(IrOp.Round, R());
                case "num.div_euclid": return new IrBinary { Op = IrBinOp.IntDivFloor, Left = R(), Right = A(0), OpSpan = ns };
                case "num.rem_euclid": return new IrBinary { Op = IrBinOp.ModFloor, Left = R(), Right = A(0), OpSpan = ns };
                case "range.contains":
                    {
                        var r = (RRange)mc.Recv;
                        var x = A(0);
                        if (!Pure(mc.Args[0])) throw NotSupported("`contains` with a call as its argument (store it in a variable first)", mc.Args[0]);
                        var lo = new IrBinary { Op = IrBinOp.Le, Left = Expr(r.From), Right = x, OpSpan = ns, Type = IrType.Bool };
                        var hi = new IrBinary { Op = r.Inclusive ? IrBinOp.Le : IrBinOp.Lt, Left = Expr(mc.Args[0]), Right = Expr(r.To), OpSpan = ns, Type = IrType.Bool };
                        return new IrBinary { Op = IrBinOp.And, Left = lo, Right = hi, OpSpan = ns, Synthetic = true, Type = IrType.Bool };
                    }
                case "str.push_str": throw NotSupported("`push_str` used as a value", mc);
                case "vec.swap": throw NotSupported("`swap` used as a value", mc);
                case "map.entry": throw NotSupported("`entry(..).or_insert(..)` outside `*m.entry(k).or_insert(0) += x;`", mc);
            }
            throw NotSupported("the method `." + mc.Name + "()`", mc);
        }

        IrExpr MacroCall(RMacro m)
        {
            var parts = new List<IrExpr>();
            foreach (var pc in m.Pieces)
            {
                if (pc.Arg == null)
                {
                    if (pc.Text.IndexOf('\n') >= 0 || pc.Text.IndexOf('\r') >= 0)
                        throw new RustAbort("a line break inside a format string is not supported in JavaFarmer (use one println! per line)", m.Format.Start, m.Format.End);
                    var lit = IrLiteral.OfString(pc.Text);
                    lit.Span = Sp(m.Format);
                    parts.Add(lit);
                }
                else parts.Add(Expr(pc.Arg));
            }
            IrExpr joined = null;
            foreach (var p in parts)
                joined = joined == null ? p : new IrBinary { Op = IrBinOp.Concat, Left = joined, Right = p, Synthetic = true, Span = Sp(m), OpSpan = Sp(m.NameStart, m.BangEnd), Type = IrType.String };
            if (m.Name == "format")
            {
                if (joined == null) { var e = IrLiteral.OfString(""); e.Span = Sp(m); return e; }
                if (parts.Count == 1 && !RTy.ToIr(m.Pieces[0].Arg?.Type ?? RTy.String).IsString)
                    return new IrCast { Target = IrType.String, Operand = joined, Synthetic = true, OpSpan = Sp(m.NameStart, m.BangEnd), Type = IrType.String };
                return joined;
            }
            var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = m.Name == "quick_print" ? "quick_print" : "print", NameSpan = Sp(m.NameStart, m.BangEnd), Type = IrType.Void };
            if (joined != null) call.Args.Add(joined);
            else if (m.Name == "print") { var e = IrLiteral.OfString(""); e.Span = Sp(m); call.Args.Add(e); }
            return call;
        }
    }
}
