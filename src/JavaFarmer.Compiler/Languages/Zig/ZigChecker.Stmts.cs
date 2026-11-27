using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal sealed partial class ZigChecker
    {
        ZFnSig curFn;
        /// <summary>Enclosing loops (innermost last): the continue-expression statements to repeat before a continue.</summary>
        readonly List<Func<List<IrStmt>>> loops = new List<Func<List<IrStmt>>>();

        IrFunction Function(ZSym s, ZFnDecl f)
        {
            var sig = s.Sig;
            var fn = new IrFunction
            {
                Symbol = s.Ir, ReturnType = sig.Ret.Ir(), IsEntry = f.Name == "main" && f.IsPub && f.Params.Count == 0,
                IsPrivate = !f.IsPub, Span = f.Span, HeaderSpan = new Span(f.Start, f.HeaderEnd), NameSpan = new Span(f.NameStart, f.NameEnd)
            };
            s.Ir.Function = fn;
            s.Ir.Tag = new ZigTag { Sig = sig };
            PushScope();
            for (int i = 0; i < f.Params.Count; i++)
            {
                var p = f.Params[i];
                var pt = i < sig.Params.Count ? sig.Params[i] : ZType.Err;
                var ps = Declare(p.Name ?? "_", p.NameStart, p.NameEnd, "function parameter", pt, false, IrSymbolKind.Param);
                ps.Ir.Tag = new ZigTag { Type = pt };
                fn.Params.Add(new IrParam { Symbol = ps.Ir, Span = new Span(p.Start, p.End), NameSpan = new Span(p.NameStart, p.NameEnd) });
            }
            var outer = curFn;
            curFn = sig;
            fn.Body = Block(f.Body, true);
            if (sig.Ret.K != ZK.Void && sig.Ret.K != ZK.NoReturn && !sig.Ret.IsErr && CanComplete(f.Body))
                ErrS("function with non-void return type " + Q(sig.Ret) + " implicitly returns", f.Body.End - 1, f.Body.End);
            curFn = outer;
            PopScope();
            return fn;
        }

        // ------------------------------------------------------------------ reachability
        static bool CanComplete(ZStmt s)
        {
            switch (s)
            {
                case null: return true;
                case ZReturn _: case ZBreak _: case ZContinue _: return false;
                case ZExprStmt es when es.Expr is ZControlExpr ce && ce.Kw == "unreachable": return false;
                case ZBlock b:
                    foreach (var st in b.Stmts) if (!CanComplete(st)) return false;
                    return true;
                case ZIf i: return i.Else == null || CanComplete(i.Then) || CanComplete(i.Else);
                case ZWhile w:
                    // while (true) without a break never completes
                    if (w.Cond is ZIdent id && id.Name == "true" && w.Capture == null) return HasBreak(w.Body);
                    return true;
                case ZSwitch sw:
                    if (!sw.Prongs.Any(p => p.IsElse) && sw.Prongs.Count > 0 && false) return true;
                    foreach (var p in sw.Prongs) if (CanComplete(p.Body)) return true;
                    return sw.Prongs.Count == 0;
            }
            return true;
        }

        static bool HasBreak(ZStmt s)
        {
            switch (s)
            {
                case ZBreak _: return true;
                case ZWhile _: case ZFor _: return false;
                case ZBlock b: return b.Stmts.Any(HasBreak);
                case ZIf i: return HasBreak(i.Then) || HasBreak(i.Else);
                case ZSwitch sw: return sw.Prongs.Any(p => HasBreak(p.Body));
            }
            return false;
        }

        static bool IsNoReturnStmt(ZStmt s) =>
            s is ZReturn || s is ZBreak || s is ZContinue || (s is ZExprStmt es && es.Expr is ZControlExpr ce && ce.Kw == "unreachable");

        // ------------------------------------------------------------------ blocks
        IrBlock Block(ZStmt s, bool ownScope = true)
        {
            if (s is ZBlock b)
            {
                if (b.Label != null) NotSupported("labeled blocks are", new ZSpanNode(b.Start, b.Start + b.Label.Length));
                var blk = new IrBlock { Span = b.Span };
                if (ownScope) PushScope();
                Stmts(b.Stmts, blk.Stmts);
                if (ownScope) PopScope();
                return blk;
            }
            var one = new IrBlock { Span = s?.Span ?? Span.None };
            if (s != null)
            {
                PushScope();
                Stmt(s, one.Stmts);
                PopScope();
            }
            return one;
        }

        void Stmts(List<ZStmt> stmts, List<IrStmt> outList)
        {
            for (int i = 0; i < stmts.Count; i++)
            {
                if (i > 0 && IsNoReturnStmt(stmts[i - 1])) { ErrA("unreachable code", stmts[i]); }
                // var it = map.iterator(); while (it.next()) |entry| { ... }
                if (i + 1 < stmts.Count && stmts[i] is ZVarDecl vd && stmts[i + 1] is ZWhile w && TryMapLoop(vd, w, outList)) { i++; continue; }
                Stmt(stmts[i], outList);
            }
        }

        void Stmt(ZStmt s, List<IrStmt> outList)
        {
            switch (s)
            {
                case ZVarDecl v: LocalDecl(v, outList); return;
                case ZDestructure d: Destructure(d, outList); return;
                case ZAssign a: Assign(a, outList); return;
                case ZExprStmt es: ExprStmt(es, outList); return;
                case ZBlock b: outList.Add(Block(b)); return;
                case ZIf i: outList.AddRange(If(i)); return;
                case ZWhile w: While(w, outList); return;
                case ZFor f: For(f, outList); return;
                case ZSwitch sw: SwitchStmt(sw, outList, null); return;
                case ZBreak br:
                    if (br.Label != null) { NotSupported("labeled break is", br); return; }
                    if (br.Value != null) { NotSupported("break with a value is", br); return; }
                    if (loops.Count == 0) { ErrA("break expression outside loop", br); return; }
                    outList.Add(new IrBreak { Span = br.Span });
                    return;
                case ZContinue co:
                    if (co.Label != null) { NotSupported("labeled continue is", co); return; }
                    if (loops.Count == 0) { ErrA("continue expression outside loop", co); return; }
                    var update = loops[loops.Count - 1]?.Invoke();
                    if (update != null) outList.AddRange(update);
                    outList.Add(new IrContinue { Span = co.Span });
                    return;
                case ZReturn r: Return(r, outList); return;
                case ZDefer df: Defer(df); return;
                case ZUnsupportedStmt u: NotSupported(u.What, u); return;
            }
            NotSupported("this statement is", s);
        }

        // ------------------------------------------------------------------ declarations
        void LocalDecl(ZVarDecl v, List<IrStmt> outList)
        {
            if (v.Name == "_") { ErrA("'_' used as an identifier without @\"_\"", v.NameStart, v.NameEnd); return; }
            if (v.Init == null) { ErrA("variables must be initialized", v.NameStart, v.NameEnd); return; }
            ZType declT = v.Type != null ? ResolveType(v.Type) : null;
            // const x = expr orelse return; / orelse continue / orelse break / orelse unreachable
            if (v.Init is ZBinary ob && ob.Op == "orelse" && (ob.Right is ZControlExpr || ob.Right is ZBlockValue))
            {
                OrelseDecl(v, ob, declT, outList);
                return;
            }
            if (v.Init is ZSwitchExpr swe)
            {
                var t0 = declT ?? InferSwitchType(swe.Switch, v);
                if (t0 == null) { ErrS("a switch expression needs a declared type in JavaFarmer (const x: T = switch ...)", v.Init.Start, v.Init.Start + 6); t0 = ZType.Err; }
                var sym0 = Declare(v.Name, v.NameStart, v.NameEnd, v.IsConst ? "local constant" : "local variable", t0, !v.IsConst);
                sym0.Ir.Tag = new ZigTag { Type = t0 };
                sym0.Ir.IsConst = false;   // assigned by every branch
                outList.Add(new IrVarDecl { Symbol = sym0.Ir, NameSpan = new Span(v.NameStart, v.NameEnd), Span = v.Span, TypeExplicit = true });
                SwitchStmt(swe.Switch, outList, (val, at) => new IrAssign { Target = new IrName(sym0.Ir) { Span = new Span(v.NameStart, v.NameEnd) }, Value = Coerce(val, t0, at), Span = at.Span }, t0);
                return;
            }
            var r = Whole(v.Init, declT);
            ZType t = declT ?? r.T;
            if (declT == null)
            {
                if (!v.IsConst && (t.K == ZK.CInt || t.K == ZK.CFloat))
                    ErrS("variable of type " + Q(t) + " must be const or comptime", v.Init.Start, v.Init.End);
                else if (t.K == ZK.Null) ErrS("variable of type '@TypeOf(null)' must be const or comptime", v.Init.Start, v.Init.End);
                else if (t.K == ZK.EnumLit) { NotSupported("an untyped enum literal is", v.Init); t = ZType.Err; }
                else if (t.K == ZK.Undefined) { ErrS("unable to infer variable type", v.Init); t = ZType.Err; }
                else if (t.K == ZK.Foreign) { ErrS("this value comes from a file in another language and has no Zig type: give the variable one (const " + v.Name + ": i32 = ...)", v.Init); t = ZType.Err; }
                else if (t.K == ZK.Void && !t.IsErr) { }
                else if (t.K == ZK.Iter) { NotSupported("a map iterator outside 'while (it.next()) |entry| { ... }' is", v.Init); t = ZType.Err; }
                if (t.K == ZK.Type || t.K == ZK.Namespace || (t.K == ZK.Fn && r.Ir == null) || r.Special != null && r.Ir == null || r.Api != null)
                {
                    if (!v.IsConst) { ErrS("variable of type 'type' must be const or comptime", v.Init); return; }
                    // a local alias: const print = std.debug.print;
                    var al = Declare(v.Name, v.NameStart, v.NameEnd, "local constant", t, false);
                    al.Kind = ZSymKind.Alias;
                    aliasValues[al] = r;
                    return;
                }
            }
            IrExpr init = declT != null ? Coerce(r, declT, v.Init, v.IsConst) : CopyArray(r.Ir, t, v.Init, v.IsConst);
            if (r.T.K == ZK.Undefined)
            {
                init = null;
                if (t.K == ZK.Array) init = ArrayFill(t, null, v.Init);
            }
            var sym = Declare(v.Name, v.NameStart, v.NameEnd, v.IsConst ? "local constant" : "local variable", t, !v.IsConst);
            sym.Ir.Tag = new ZigTag { Type = t };
            if (v.IsConst && r.Known) { sym.HasValue = true; sym.IntValue = r.IntVal; sym.FloatValue = r.T.K == ZK.CInt ? r.IntVal : r.FloatVal; sym.StrValue = r.StrVal; }
            if (init != null && !init.Type.IsKnown) init.Type = t.Ir();
            outList.Add(new IrVarDecl { Symbol = sym.Ir, Init = init, NameSpan = new Span(v.NameStart, v.NameEnd), Span = v.Span, TypeExplicit = v.Type != null });
        }

        /// <summary>
        /// The peer type of a switch expression whose prong values are all literals of one kind (strings: []const u8,
        /// bools: bool); number literals are comptime-only (the Zig error). Null: the type must be declared.
        /// </summary>
        ZType InferSwitchType(ZSwitch sw, ZVarDecl v)
        {
            bool str = true, boolean = true, num = true; int values = 0;
            foreach (var pr in sw.Prongs)
            {
                var e = pr.Body is ZExprStmt es ? Unparen(es.Expr) : null;
                if (e is ZControlExpr) continue;   // => unreachable / return
                if (e == null) return null;
                values++;
                if (!(e is ZStrLit)) str = false;
                if (!(e is ZIdent id && (id.Name == "true" || id.Name == "false"))) boolean = false;
                if (!(e is ZIntLit || e is ZFloatLit || e is ZUnary u && u.Op == "-" && (u.Operand is ZIntLit || u.Operand is ZFloatLit))) num = false;
            }
            if (values == 0) return null;
            if (str) return ZType.Str;
            if (boolean) return ZType.Bool;
            if (num) { ErrS("value with comptime-only type 'comptime_int' depends on runtime control flow (give the variable a type)", sw.Start, sw.KwEnd); return ZType.Err; }
            return null;
        }

        /// <summary>const x = opt orelse return; → x = opt; if x == None: return</summary>
        void OrelseDecl(ZVarDecl v, ZBinary ob, ZType declT, List<IrStmt> outList)
        {
            var left = OptValue(Expr(ob.Left, declT != null ? ZType.Opt(declT) : null), ob.Left);
            ZType t;
            if (left.T.IsErr) t = ZType.Err;
            else if (left.T.K != ZK.Optional) { ErrS("expected optional type, found " + Q(left.T), ob.Left); t = ZType.Err; }
            else t = left.T.Elem;
            if (declT != null && !t.IsErr) { var m = CoerceError(new ZR { T = t }, declT); if (m != null) ErrS(m, v.Init); t = declT; }
            var sym = Declare(v.Name, v.NameStart, v.NameEnd, v.IsConst ? "local constant" : "local variable", t, !v.IsConst);
            sym.Ir.Tag = new ZigTag { Type = t };
            var leftIr = left.Ir;
            if (leftIr != null) leftIr.Type = t.Ir();
            outList.Add(new IrVarDecl { Symbol = sym.Ir, Init = leftIr, NameSpan = new Span(v.NameStart, v.NameEnd), Span = v.Span, TypeExplicit = v.Type != null });
            var then = new IrBlock { Span = ob.Right.Span };
            if (ob.Right is ZControlExpr ce)
            {
                switch (ce.Kw)
                {
                    case "unreachable": return;   // x.? : no check in the game
                    case "return": Return(new ZReturn { Value = ce.Value, Start = ce.Start, End = ce.End }, then.Stmts); break;
                    case "break": Stmt(new ZBreak { Label = ce.Label, Value = ce.Value, Start = ce.Start, End = ce.End }, then.Stmts); break;
                    case "continue": Stmt(new ZContinue { Label = ce.Label, Start = ce.Start, End = ce.End }, then.Stmts); break;
                }
            }
            else if (ob.Right is ZBlockValue bv)
            {
                NotSupported("'orelse { ... }' is", bv);
                return;
            }
            var cond = new IrBinary { Op = IrBinOp.Eq, Left = new IrName(sym.Ir) { Span = new Span(v.NameStart, v.NameEnd) }, Right = new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = new Span(ob.OpStart, ob.OpEnd), Synthetic = true }, OpSpan = new Span(ob.OpStart, ob.OpEnd), Type = IrType.Bool, Synthetic = true, Span = ob.Span };
            outList.Add(new IrIf { Cond = cond, Then = then, KwSpan = new Span(ob.OpStart, ob.OpEnd), Span = ob.Span });
        }

        void Destructure(ZDestructure d, List<IrStmt> outList)
        {
            ZR value;
            List<ZType> itemTypes;
            if (d.Value is ZInit init && init.Type == null && init.Fields == null)
            {
                var items = init.Items.Select(x => Expr(x, null)).ToList();
                var lit = new IrTupleLit { Span = init.Span };
                foreach (var (it, src) in items.Zip(init.Items, (a, b) => (a, b))) lit.Items.Add(it.Ir ?? IrLiteral.Null());
                itemTypes = items.Select(x => x.T).ToList();
                value = new ZR { Ir = lit, T = ZType.TupleT(itemTypes) };
                lit.Type = value.T.Ir();
                if (items.Count != d.Targets.Count) { ErrA("expected " + d.Targets.Count + " elements for destructure, found " + items.Count, d.Value); return; }
                for (int i = 0; i < items.Count; i++) if (items[i].T.K == ZK.EnumLit) { NotSupported("an untyped enum literal is", init.Items[i]); return; }
            }
            else
            {
                value = Expr(d.Value, null);
                if (value.T.IsErr) return;
                if (value.T.K == ZK.Struct) { ErrS("type " + Q(value.T) + " cannot be destructured", d.Value); return; }
                if (value.T.K != ZK.Tuple) { ErrS("type " + Q(value.T) + " cannot be destructured", d.Value); return; }
                itemTypes = value.T.Items;
                if (itemTypes.Count != d.Targets.Count) { ErrS("expected " + d.Targets.Count + " elements for destructure, found " + itemTypes.Count, d.Value); return; }
            }
            var targets = new IrTupleLit { Span = new Span(d.Start, d.EqStart) };
            var decls = new List<IrStmt>();
            for (int i = 0; i < d.Targets.Count; i++)
            {
                var (decl, target) = d.Targets[i];
                var it = itemTypes[i];
                if (decl != null)
                {
                    var t = decl.Type != null ? ResolveType(decl.Type) : it;
                    if (decl.Type != null) { var m = CoerceError(new ZR { T = it }, t); if (m != null) ErrS(m, d.Value); }
                    else if (!decl.IsConst && it.IsComptimeNum) ErrS("variable of type " + Q(it) + " must be const or comptime", d.Value);
                    var sym = Declare(decl.Name, decl.NameStart, decl.NameEnd, decl.IsConst ? "local constant" : "local variable", t, !decl.IsConst);
                    sym.Ir.Tag = new ZigTag { Type = t };
                    sym.Ir.IsConst = false;
                    decls.Add(new IrVarDecl { Symbol = sym.Ir, NameSpan = new Span(decl.NameStart, decl.NameEnd), Span = decl.Span, TypeExplicit = decl.Type != null });
                    targets.Items.Add(new IrName(sym.Ir) { Span = new Span(decl.NameStart, decl.NameEnd) });
                }
                else
                {
                    if (target is ZIdent di && di.Name == "_") { NotSupported("discarding a destructured value with _ is", target); return; }
                    var lv = LValue(target);
                    if (lv == null) return;
                    var m = CoerceError(new ZR { T = it }, lv.T);
                    if (m != null) ErrS(m, d.Value);
                    targets.Items.Add(lv.Ir);
                }
            }
            outList.AddRange(decls);
            outList.Add(new IrAssign { Target = targets, Value = value.Ir, OpSpan = new Span(d.EqStart, d.EqStart + 1), Span = d.Span });
        }

        // ------------------------------------------------------------------ assignments
        /// <summary>An assignable expression: marks the variable used and mutated; checks it is not a constant.</summary>
        ZR LValue(ZExpr target)
        {
            switch (target)
            {
                case ZGrouped g: return LValue(g.Inner);
                case ZIdent id:
                    {
                        if (id.Name == "_") { ErrA("'_' used as an identifier without @\"_\"", id); return null; }
                        var s = Lookup(id.Name);
                        if (s == null)
                        {
                            if (IsPrimitiveName(id.Name)) { ErrS("cannot assign to constant", id); return null; }
                            ErrA("use of undeclared identifier " + Q(id.Name), id); return null;
                        }
                        s.Used = true;
                        s.Mutated = true;
                        if (s.Kind == ZSymKind.Alias || s.Kind == ZSymKind.Fn) { ErrS("cannot assign to constant", id); return null; }
                        if (!s.IsVar) { ErrS("cannot assign to constant", id); return null; }
                        if (s.Module != null) { }
                        return new ZR { Ir = new IrName(s.Ir) { Span = id.Span }, T = s.Type, Sym = s };
                    }
                case ZIndex ix:
                    {
                        var baseR = IndexBase(ix.Target, true);
                        if (baseR == null) return null;
                        return Index(ix, baseR);
                    }
                case ZDeref dr:
                    {
                        var r = Expr(dr, null);
                        if (r.T.IsErr) return null;
                        if (r.Special == "value_ptr") return r;
                        if (r.Special == "key_ptr") { ErrS("cannot assign to constant", dr); return null; }
                        NotSupported("assigning through a pointer is", dr);
                        return null;
                    }
                case ZField f:
                    {
                        var r = Expr(f.Target, null);
                        if (r.T.IsErr) return null;
                        if (r.T.K == ZK.Namespace && r.T.Name != null && r.T.Name.StartsWith("file:"))
                        {
                            // other_file.global = value (a pub var of another file)
                            var m = Expr(f, null);
                            if (m.T.IsErr) return null;
                            if (m.Sym == null || m.Sym.Kind != ZSymKind.Global || !m.Sym.IsVar) { ErrS("cannot assign to constant", f); return null; }
                            return m;
                        }
                        var froot = Unparen(f.Target);
                        while (froot is ZIndex fri) froot = Unparen(fri.Target);
                        if (froot is ZIdent fid && LookupQuiet(fid.Name) is ZSym fs && !fs.IsVar && (fs.Kind == ZSymKind.Local || fs.Kind == ZSymKind.Global || fs.Kind == ZSymKind.Param || fs.Kind == ZSymKind.Capture)
                            && (r.T.K == ZK.Struct || r.T.K == ZK.Tuple || r.T.K == ZK.Array))
                        { ErrS("cannot assign to constant", target); return null; }
                        NotSupported("assigning to a field is", target);
                        return null;
                    }
            }
            ErrA("invalid left-hand side to assignment", target);
            return null;
        }

        /// <summary>The array / slice / list.items being indexed; marks its variable mutated when assigned through.</summary>
        ZR IndexBase(ZExpr target, bool assign)
        {
            if (assign)
            {
                var root = target;
                while (true)
                {
                    if (root is ZField rf) root = rf.Target;
                    else if (root is ZGrouped rg) root = rg.Inner;
                    else if (root is ZIndex ri) root = ri.Target;
                    else break;
                }
                if (root is ZIdent rid)
                {
                    var s = Lookup(rid.Name);
                    if (s != null && (s.Kind == ZSymKind.Local || s.Kind == ZSymKind.Global || s.Kind == ZSymKind.Param || s.Kind == ZSymKind.Capture))
                    {
                        bool viaItems = target is ZField tf && tf.Name == "items";
                        // a const array / tuple / struct is a constant value all the way down (Zig: "cannot assign to constant")
                        bool isArray = s.Type != null && (s.Type.K == ZK.Array || s.Type.K == ZK.Tuple || s.Type.K == ZK.Struct);
                        if (isArray && !s.IsVar) { s.Used = true; ErrS("cannot assign to constant", target); return null; }
                        if (s.Type != null && s.Type.K == ZK.Slice && s.Type.Const) { s.Used = true; ErrS("cannot assign to constant", target); return null; }
                        s.Mutated = true;
                        if (!viaItems && s.Type != null && s.Type.K == ZK.Ptr && s.Type.Const) { ErrS("cannot assign to constant", target); return null; }
                    }
                }
            }
            var r = Expr(target, null);
            if (r.T.IsErr) return null;
            return r;
        }

        static readonly Dictionary<string, IrBinOp> CompoundOps = new Dictionary<string, IrBinOp>
        {
            ["+="] = IrBinOp.Add, ["-="] = IrBinOp.Sub, ["*="] = IrBinOp.Mul, ["/="] = IrBinOp.TrueDiv, ["%="] = IrBinOp.ModTrunc,
        };

        void Assign(ZAssign a, List<IrStmt> outList)
        {
            // _ = value;
            if (a.Target is ZIdent di && di.Name == "_")
            {
                if (a.Op != "=") { ErrA("'_' used as an identifier without @\"_\"", di); return; }
                Discard(a.Value, outList, a);
                return;
            }
            if (a.Op == "=" && a.Value is ZSwitchExpr swe)
            {
                var lv0 = LValue(a.Target);
                if (lv0 == null) return;
                var tt = lv0.T;
                SwitchStmt(swe.Switch, outList, (val, at) => new IrAssign { Target = CloneTarget(lv0.Ir), Value = Coerce(val, tt, at), OpSpan = new Span(a.OpStart, a.OpEnd), Span = at.Span }, tt);
                return;
            }
            // t[0] = v on a tuple, p.x = v on a farm.Position: rebuild the (immutable) Python tuple
            if (TupleElementAssign(a, outList)) return;
            if (!(a.Op == "=" || CompoundOps.ContainsKey(a.Op)))
            {
                NotSupported("operator " + Q(a.Op) + " is", new ZSpanNode(a.OpStart, a.OpEnd));
                return;
            }
            var lv = LValue(a.Target);
            if (lv == null) { Expr(a.Value, null); return; }
            var t = lv.T;
            var val = Whole(a.Value, t);
            var res = new IrAssign { Target = lv.Ir, OpSpan = new Span(a.OpStart, a.OpEnd), Span = a.Span };
            if (a.Op == "=")
            {
                if (val.T.K == ZK.Undefined) { NotSupported("assigning undefined is", a.Value); return; }
                res.Value = Coerce(val, t, a.Value);
            }
            else
            {
                if (!t.IsNumeric && !t.IsErr) { ErrS("invalid operands to binary expression: " + Q(t) + " and " + Q(val.T), a); return; }
                var op = CompoundOps[a.Op];
                var lhs = new ZR { T = t, Ir = lv.Ir };
                var peer = Peer(lhs, val);
                if (peer == null) { ErrS("incompatible types: " + Q(t) + " and " + Q(val.T), a); return; }
                if (!peer.IsErr && !peer.Same(t) && !(val.T.IsComptimeNum)) { ErrS(Mismatch(val.T, t), a.Value); return; }
                if (op == IrBinOp.TrueDiv || op == IrBinOp.ModTrunc)
                {
                    var bop = DivOp(a.Op == "/=" ? "/" : "%", lhs, val, t, new ZSpanNode(a.OpStart, a.OpEnd));
                    if (bop == null) return;
                    op = bop.Value;
                }
                res.Op = op;
                res.Value = Coerce(val, t, a.Value);
                if (a.Value is ZIntLit il && il.Value == 1 && (op == IrBinOp.Add || op == IrBinOp.Sub)) { }
            }
            outList.Add(res);
        }

        IrExpr CloneTarget(IrExpr t)
        {
            switch (t)
            {
                case IrName n: return new IrName(n.Symbol) { Span = n.Span, Type = n.Type };
                case IrIndex ix: return new IrIndex { Target = CloneTarget(ix.Target), Index = CloneTarget(ix.Index), Span = ix.Span, Type = ix.Type };
                case IrLiteral l: return new IrLiteral { Kind = l.Kind, Int = l.Int, Float = l.Float, Bool = l.Bool, Str = l.Str, Type = l.Type, Span = l.Span };
            }
            return t;
        }

        /// <summary>_ = value: a plain name is discarded (AstGen), anything with effects is evaluated.</summary>
        void Discard(ZExpr value, List<IrStmt> outList, ZNode stmt)
        {
            if (value is ZIdent id && id.Name != "_" && !IsPrimitiveName(id.Name))
            {
                var s = Lookup(id.Name);
                if (s == null) { ErrA("use of undeclared identifier " + Q(id.Name), id); return; }
                if (s.Kind == ZSymKind.Local || s.Kind == ZSymKind.Param || s.Kind == ZSymKind.Capture)
                {
                    s.Discarded = true; s.DiscardStart = id.Start; s.DiscardEnd = id.End;
                    if (s.Ir != null) s.Ir.Tag = s.Ir.Tag ?? new ZigTag { Type = s.Type };
                    return;
                }
                s.Used = true;
                return;
            }
            var r = Expr(value, null, discard: true);
            if (r.T.IsErr) return;
            if (r.T.K == ZK.ErrorUnion) { ErrS("error union is ignored", value); return; }
            // _ = list.pop(); is fine on an empty list in Zig (null), an error in the game: only pop when there is something
            if (r.Special == "pop" && r.Ir is IrOpCall dpop && dpop.Op == IrOp.ListPop && dpop.Args.Count == 1 && CloneSimple(dpop.Args[0]) is IrExpr dl)
            {
                var notEmpty = NotEmpty(dl, dpop);
                outList.Add(new IrIf { Cond = notEmpty, Then = new IrBlock(new IrStmt[] { new IrExprStmt { Expr = dpop, Span = stmt.Span } }) { Span = stmt.Span }, Span = stmt.Span, Synthetic = true });
                return;
            }
            if (r.T.K == ZK.Void && r.Ir == null) { ErrS("pointless discard of a void value", value); return; }
            if (r.Ir != null && HasEffect(r.Ir)) outList.Add(new IrExprStmt { Expr = r.Ir, Span = stmt.Span });
        }

        static bool HasEffect(IrExpr e) => IrWalk.Descendants(e).Any(n => n is IrCall || (n is IrOpCall o && o.Op != IrOp.Len && o.Op != IrOp.IsEmpty && o.Op != IrOp.Contains && o.Op != IrOp.MapGetOrNull && o.Op != IrOp.MapGetOrDefault && o.Op != IrOp.PosX && o.Op != IrOp.PosY && o.Op != IrOp.CompanionEntity && o.Op != IrOp.CompanionPos));

        void ExprStmt(ZExprStmt es, List<IrStmt> outList)
        {
            var e = es.Expr;
            if (e is ZControlExpr ce && ce.Kw == "unreachable") { NotSupported("unreachable is", e); return; }
            if (e is ZBlockValue bv) { outList.Add(Block(bv.Block)); return; }
            var r = Expr(e, null, statement: true);
            if (r.T.IsErr) return;
            if (r.T.K == ZK.ErrorUnion) { ErrS("error union is ignored", e); return; }
            if (r.T.K != ZK.Void && r.T.K != ZK.NoReturn && r.T.K != ZK.Foreign)
            {
                ErrS("value of type " + Q(r.T) + " ignored", e);
                return;
            }
            if (r.Ir != null) outList.Add(new IrExprStmt { Expr = r.Ir, Span = es.Span });
            if (pendingStmts.Count > 0) { outList.AddRange(pendingStmts); pendingStmts.Clear(); }
        }

        readonly List<IrStmt> pendingStmts = new List<IrStmt>();

        void Return(ZReturn r, List<IrStmt> outList)
        {
            if (curFn == null) { ErrA("'return' outside function scope", r); return; }
            var rt = curFn.Ret;
            if (r.Value is ZSwitchExpr swe)
            {
                SwitchStmt(swe.Switch, outList, (val, at) => new IrReturn { Value = Coerce(val, rt, at), Span = at.Span }, rt);
                return;
            }
            if (r.Value == null)
            {
                if (rt.K != ZK.Void && !rt.IsErr) ErrS("expected type " + Q(rt) + ", found 'void'", r);
                outList.Add(new IrReturn { Span = r.Span });
                return;
            }
            var v = Whole(r.Value, rt);
            if (rt.K == ZK.Void && !v.T.IsErr) { ErrS("expected type 'void', found " + Q(v.T), r.Value); return; }
            outList.Add(new IrReturn { Value = Coerce(v, rt, r.Value), Span = r.Span });
        }

        void Defer(ZDefer df)
        {
            if (df.IsErr) { NotSupported("errdefer is", df); return; }
            // defer list.deinit(farm.allocator); / defer map.deinit();
            if (df.Body is ZExprStmt es && es.Expr is ZCall c && c.Callee is ZField f && f.Name == "deinit")
            {
                var r = Expr(es.Expr, null, statement: true);
                if (r.T.IsErr || r.T.K == ZK.Void) return;
            }
            NotSupported("defer is only supported for list.deinit(...) / map.deinit(); this defer is", df);
        }

        // ------------------------------------------------------------------ if / loops
        IrExpr Condition(ZExpr cond)
        {
            var r = Expr(cond, ZType.Bool);
            if (r.T.IsErr) return r.Ir ?? IrLiteral.OfBool(true);
            if (r.T.K != ZK.Bool) ErrS("expected type 'bool', found " + Q(r.T), cond);
            return r.Ir;
        }

        List<IrStmt> If(ZIf s)
        {
            var res = new List<IrStmt>();
            var ir = new IrIf { KwSpan = new Span(s.Start, s.KwEnd), Span = s.Span };
            if (s.ElseCapture != null) NotSupported("error captures (else |err|) are", new ZSpanNode(s.ElseCapture.Start, s.ElseCapture.End));
            if (s.Capture != null)
            {
                // if (opt) |v| { ... }
                var r = OptValue(Expr(s.Cond, null), s.Cond);
                PushScope();
                var cap = OptionalCapture(r, s.Cond, s.Capture, res, out var condIr);
                ir.Cond = condIr;
                ir.Then = Block(s.Then, true);
                PopScope();
                if (cap == null) return res;
            }
            else
            {
                ir.Cond = Condition(s.Cond);
                ir.Then = Block(s.Then, true);
            }
            if (s.Else != null)
            {
                if (s.Else is ZIf ei && ei.Capture == null) { var chain = If(ei); if (chain.Count == 1) ir.Else = chain[0]; else ir.Else = new IrBlock(chain) { Span = ei.Span }; }
                else ir.Else = Block(s.Else, true);
            }
            res.Add(ir);
            return res;
        }

        /// <summary>
        /// The payload of if (opt) |v| / while (opt) |v|: v aliases opt when it is a variable, else v = opt is declared
        /// before the statement. condIr = (v != null).
        /// </summary>
        ZSym OptionalCapture(ZR r, ZExpr condExpr, ZCapture capture, List<IrStmt> before, out IrExpr condIr)
        {
            condIr = IrLiteral.OfBool(true);
            if (capture.Names.Count != 1) { ErrA("extra capture in if", capture.Start, capture.End); return null; }
            var (name, ns, ne, isRef) = capture.Names[0];
            if (isRef) { NotSupported("pointer captures (|*x|) are", new ZSpanNode(ns - 1, ne)); return null; }
            if (r.T.IsErr) return null;
            if (r.T.K == ZK.ErrorUnion) { NotSupported("if / while on an error union is", condExpr); return null; }
            if (r.T.K != ZK.Optional) { ErrS("expected optional type, found " + Q(r.T), condExpr); return null; }
            if (name == "_") { ErrA("discard of capture; omit it instead", ns, ne); return null; }
            var t = r.T.Elem;
            var sym = Declare(name, ns, ne, "capture", t, false);
            sym.Ir.Tag = new ZigTag { Type = t };
            IrExpr subject;
            if (r.Ir is IrName n && (n.Symbol.Kind == IrSymbolKind.Local || n.Symbol.Kind == IrSymbolKind.Param || n.Symbol.Kind == IrSymbolKind.Global) && r.Sym != null)
            {
                var target = n.Symbol;
                var refSpan = condExpr.Span;
                sym.AliasOf = () => new IrName(target) { Span = refSpan, Type = t.Ir() };
                subject = new IrName(target) { Span = condExpr.Span, Type = r.T.Ir() };
            }
            else
            {
                sym.Ir.IsConst = true;
                var init = r.Ir;
                before.Add(new IrVarDecl { Symbol = sym.Ir, Init = init, NameSpan = new Span(ns, ne), Span = new Span(ns, ne), TypeExplicit = false });
                subject = new IrName(sym.Ir) { Span = new Span(ns, ne), Type = r.T.Ir() };
            }
            condIr = new IrBinary { Op = IrBinOp.Ne, Left = subject, Right = new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = new Span(capture.Start, capture.End), Synthetic = true }, OpSpan = new Span(capture.Start, capture.End), Type = IrType.Bool, Synthetic = true, Span = condExpr.Span };
            return sym;
        }

        void While(ZWhile w, List<IrStmt> outList)
        {
            if (w.Label != null) NotSupported("labeled loops are", new ZSpanNode(w.Start, w.KwEnd));
            if (w.Inline) NotSupported("inline loops are", new ZSpanNode(w.Start, w.KwEnd));
            if (w.Else != null) NotSupported("while ... else is", w.Else);
            var ir = new IrWhile { KwSpan = new Span(w.Start, w.KwEnd), Span = w.Span };
            Func<List<IrStmt>> update = null;
            if (w.Cont != null)
            {
                var cont = w.Cont;
                update = () => { var l = new List<IrStmt>(); PushScope(); Stmt(cont, l); PopScope(); return l; };
            }
            if (w.Capture != null)
            {
                // while (opt) |v| body  →  while True: v = opt; if v == None: break; body
                var r = Expr(w.Cond, null);
                if (r.T.IsErr) return;
                if (r.Ir is IrOpCall pop && pop.Op == IrOp.ListPop && pop.Args.Count == 1)
                {
                    // while (list.pop()) |x|  →  while len(list) > 0: x = list.pop()
                    PushScope();
                    var (name, ns, ne, isRef) = w.Capture.Names[0];
                    var t = r.T.K == ZK.Optional ? r.T.Elem : ZType.Err;
                    var sym = Declare(name, ns, ne, "capture", t, false);
                    sym.Ir.Tag = new ZigTag { Type = t };
                    var list = pop.Args[0];
                    var cond = new IrUnary { Op = IrUnOp.Not, Operand = new IrOpCall(IrOp.IsEmpty, CloneTarget(list)) { Type = IrType.Bool, Span = w.Cond.Span, Synthetic = true }, Type = IrType.Bool, Synthetic = true, Span = w.Cond.Span };
                    ir.Cond = cond;
                    loops.Add(update);
                    var body = Block(w.Body, true);
                    loops.RemoveAt(loops.Count - 1);
                    if (name != "_") body.Stmts.Insert(0, new IrVarDecl { Symbol = sym.Ir, Init = r.Ir, NameSpan = new Span(ns, ne), Span = new Span(ns, ne) });
                    else body.Stmts.Insert(0, new IrExprStmt { Expr = r.Ir, Span = w.Cond.Span });
                    PopScope();
                    if (update != null) body.Stmts.AddRange(update());
                    ir.Body = body;
                    outList.Add(ir);
                    return;
                }
                PushScope();
                var pre = new List<IrStmt>();
                r = OptValue(r, w.Cond);
                var cap = OptionalCapture(new ZR { Ir = r.Ir, T = r.T }, w.Cond, w.Capture, pre, out var condIr);
                if (cap == null) { PopScope(); return; }
                if (cap.AliasOf != null)
                {
                    // the condition re-reads a variable: while (x) |v| is "while x != None"
                    ir.Cond = condIr;
                    loops.Add(update);
                    ir.Body = Block(w.Body, true);
                    loops.RemoveAt(loops.Count - 1);
                }
                else
                {
                    ir.Cond = IrLiteral.OfBool(true);
                    ir.Cond.Span = new Span(w.Start, w.KwEnd);
                    ir.Cond.Synthetic = true;
                    loops.Add(update);
                    var body = Block(w.Body, true);
                    loops.RemoveAt(loops.Count - 1);
                    var check = new IrIf { Cond = new IrUnary { Op = IrUnOp.Not, Operand = condIr, Type = IrType.Bool, Synthetic = true, Span = condIr.Span }, Then = new IrBlock(new IrStmt[] { new IrBreak { Span = w.Capture.Span(), Synthetic = true } }), KwSpan = w.Capture.Span(), Span = w.Cond.Span, Synthetic = true };
                    body.Stmts.InsertRange(0, pre);
                    body.Stmts.Insert(pre.Count, check);
                    ir.Body = body;
                }
                PopScope();
                if (update != null) ir.Body.Stmts.AddRange(update());
                outList.Add(ir);
                return;
            }
            ir.Cond = Condition(w.Cond);
            loops.Add(update);
            ir.Body = Block(w.Body, true);
            loops.RemoveAt(loops.Count - 1);
            if (update != null && !(w.Body is ZBlock bb && bb.Stmts.Count > 0 && IsNoReturnStmt(bb.Stmts[bb.Stmts.Count - 1])) && !IsNoReturnStmt(w.Body)) ir.Body.Stmts.AddRange(update());
            outList.Add(ir);
        }

        void For(ZFor f, List<IrStmt> outList)
        {
            if (f.Label != null) NotSupported("labeled loops are", new ZSpanNode(f.Start, f.KwEnd));
            if (f.Inline) NotSupported("inline loops are", new ZSpanNode(f.Start, f.KwEnd));
            if (f.Else != null) NotSupported("for ... else is", f.Else);
            var caps = f.Capture.Names;
            if (caps.Count < f.Inputs.Count) { ErrA("for input is not captured", f.Inputs[caps.Count].Start, f.Inputs[caps.Count].End); return; }
            if (caps.Count > f.Inputs.Count) { ErrA("extra capture in for loop", caps[f.Inputs.Count].Start, caps[f.Inputs.Count].End); return; }
            foreach (var c in caps) if (c.Ref) { NotSupported("pointer captures (|*x|) are", new ZSpanNode(c.Start - 1, c.End)); return; }
            var kw = new Span(f.Start, f.KwEnd);
            var header = new Span(f.Start, f.HeaderEnd);
            if (f.Inputs.Count == 1)
            {
                var inp = f.Inputs[0];
                var (name, ns, ne, _) = caps[0];
                if (inp.IsRange)
                {
                    if (inp.RangeTo == null) { ErrA(name == "_" ? "discard of unbounded counter" : "unbounded for loop", inp.Start, inp.End); return; }
                    var from = Expr(inp.RangeFrom, ZType.Usize);
                    var fromIr = Coerce(from, ZType.Usize, inp.RangeFrom);
                    var to = Expr(inp.RangeTo, ZType.Usize);
                    var toIr = Coerce(to, ZType.Usize, inp.RangeTo);
                    PushScope();
                    var sym = Declare(name, ns, ne, "capture", ZType.Usize, false);
                    sym.Ir.Tag = new ZigTag { Type = ZType.Usize };
                    var rf = new IrRangeFor { Var = sym.Ir, Start = from.Known && from.IntVal == 0 && inp.RangeFrom is ZIntLit ? null : fromIr, End = toIr, KwSpan = kw, HeaderSpan = header, VarSpan = new Span(ns, ne), Span = f.Span };
                    loops.Add(null);
                    rf.Body = Block(f.Body, true);
                    loops.RemoveAt(loops.Count - 1);
                    PopScope();
                    outList.Add(rf);
                    return;
                }
                var it = Iterable(inp.Expr);
                if (it == null) return;
                PushScope();
                var esym = Declare(name, ns, ne, "capture", it.T, false);
                esym.Ir.Tag = new ZigTag { Type = it.T };
                var fe = new IrForEach { Var = esym.Ir, Iterable = it.Ir, KwSpan = kw, VarSpan = new Span(ns, ne), Span = f.Span };
                loops.Add(null);
                fe.Body = Block(f.Body, true);
                loops.RemoveAt(loops.Count - 1);
                PopScope();
                outList.Add(fe);
                return;
            }
            if (f.Inputs.Count == 2 && !f.Inputs[0].IsRange && f.Inputs[1].IsRange && f.Inputs[1].RangeTo == null)
            {
                // for (items, 0..) |x, i|  →  for i in range(len(items)): x = items[i]
                var from = Expr(f.Inputs[1].RangeFrom, ZType.Usize);
                if (!(from.Known && from.IntVal == 0)) { NotSupported("an index counter that does not start at 0 is", f.Inputs[1].RangeFrom); return; }
                var it = Iterable(f.Inputs[0].Expr);
                if (it == null) return;
                PushScope();
                var (xn, xs, xe, _) = caps[0];
                var (iname, iS, iE, _) = caps[1];
                var isym = Declare(iname == "_" ? "_" : iname, iS, iE, "capture", ZType.Usize, false);
                if (iname == "_") isym = new ZSym { Name = "i", Ir = new IrSymbol("i", IrSymbolKind.Local, IrType.Int) };
                isym.Ir.Tag = new ZigTag { Type = ZType.Usize };
                var xsym = Declare(xn, xs, xe, "capture", it.T, false);
                xsym.Ir.Tag = new ZigTag { Type = it.T };
                var len = new IrOpCall(IrOp.Len, it.Ir) { Type = IrType.Int, Span = f.Inputs[0].Expr.Span, NameSpan = f.Inputs[0].Expr.Span };
                var rf = new IrRangeFor { Var = isym.Ir, End = len, KwSpan = kw, HeaderSpan = header, VarSpan = new Span(iS, iE), Span = f.Span };
                loops.Add(null);
                rf.Body = Block(f.Body, true);
                loops.RemoveAt(loops.Count - 1);
                if (xn != "_")
                    rf.Body.Stmts.Insert(0, new IrVarDecl
                    {
                        Symbol = xsym.Ir, NameSpan = new Span(xs, xe), Span = new Span(xs, xe),
                        Init = new IrIndex { Target = CloneTarget(it.Ir), Index = new IrName(isym.Ir) { Span = new Span(iS, iE), Type = IrType.Int }, Type = it.T.Ir(), Span = new Span(xs, xe) }
                    });
                PopScope();
                rf.Body.Span = f.Body.Span;
                outList.Add(rf);
                return;
            }
            NotSupported("this for loop form (several inputs) is", new ZSpanNode(f.Start, f.HeaderEnd));
        }

        /// <summary>A for-loop input: array, slice or list.items. Returns the element type and the IR of the collection.</summary>
        ZR Iterable(ZExpr e)
        {
            var r = Expr(e, null);
            if (r.T.IsErr) return null;
            var t = r.T;
            if (t.K == ZK.Ptr) t = t.Elem;
            if (t.K == ZK.Array || (t.K == ZK.Slice && !t.IsString)) return new ZR { Ir = r.Ir, T = t.Elem };
            if (t.IsString) { NotSupported("iterating over the bytes of a string is", e); return null; }
            if (t.K == ZK.List) { ErrS("type " + Q(t) + " is not indexable and not a range", e); return null; }
            if (t.K == ZK.Map) { ErrS("type " + Q(t) + " is not indexable and not a range", e); return null; }
            ErrS("type " + Q(t) + " is not indexable and not a range", e);
            return null;
        }

        /// <summary>var it = map.iterator(); while (it.next()) |entry| { ... } → for key in map (+ value).</summary>
        bool TryMapLoop(ZVarDecl vd, ZWhile w, List<IrStmt> outList)
        {
            if (vd.IsConst || vd.Type != null || !(vd.Init is ZCall ic) || !(ic.Callee is ZField icf) || ic.Args.Count != 0) return false;
            if (icf.Name != "iterator" && icf.Name != "keyIterator" && icf.Name != "valueIterator") return false;
            if (!(w.Cond is ZCall nc) || !(nc.Callee is ZField ncf) || ncf.Name != "next" || !(ncf.Target is ZIdent itId) || itId.Name != vd.Name || nc.Args.Count != 0) return false;
            if (w.Capture == null || w.Capture.Names.Count != 1 || w.Cont != null || w.Label != null || w.Else != null) return false;
            var mr = Expr(icf.Target, null);
            if (mr.T.IsErr) return true;
            var mt = mr.T.K == ZK.Ptr ? mr.T.Elem : mr.T;
            if (mt.K != ZK.Map) { ErrS("no field or member function named " + Q(icf.Name) + " in " + Q(mr.T), icf.NameStart, icf.NameEnd); return true; }
            // the iterator variable: used and mutated by it.next()
            var itSym = Declare(vd.Name, vd.NameStart, vd.NameEnd, "local variable", new ZType { K = ZK.Iter, Key = mt.Key, Elem = mt.Elem, IterKind = icf.Name == "iterator" ? "entries" : icf.Name == "keyIterator" ? "keys" : "values" }, true);
            itSym.Used = true; itSym.Mutated = true;
            var (name, ns, ne, isRef) = w.Capture.Names[0];
            if (isRef) { NotSupported("pointer captures (|*x|) are", new ZSpanNode(ns - 1, ne)); return true; }
            PushScope();
            IrForEach fe;
            var kw = new Span(w.Start, w.KwEnd);
            if (icf.Name == "iterator")
            {
                var keySym = new IrSymbol(name == "_" ? "key" : name + "_key", IrSymbolKind.Local, mt.Key.Ir()) { Tag = new ZigTag { Type = mt.Key, EntryOf = name } };
                var valSym = mt.IsSet ? null : new IrSymbol(name == "_" ? "value" : name + "_value", IrSymbolKind.Local, mt.Elem.Ir()) { Tag = new ZigTag { Type = mt.Elem, EntryOf = name } };
                var entryType = new ZType { K = ZK.Entry, Key = mt.Key, Elem = mt.Elem };
                var esym = Declare(name, ns, ne, "capture", entryType, false);
                esym.Ir = keySym;
                entryInfo[esym] = (keySym, valSym, mr.Ir);
                fe = new IrForEach { Var = keySym, ValueVar = valSym, Iterable = mr.Ir, KwSpan = kw, VarSpan = new Span(ns, ne), ValueVarSpan = valSym != null ? new Span(ns, ne) : Span.None, Span = new Span(vd.Start, w.End) };
            }
            else
            {
                var elemT = icf.Name == "keyIterator" ? mt.Key : mt.Elem;
                var vsym = new IrSymbol(name == "_" ? (icf.Name == "keyIterator" ? "key" : "value") : name, IrSymbolKind.Local, elemT.Ir()) { Tag = new ZigTag { Type = elemT, PtrCapture = true } };
                var csym = Declare(name, ns, ne, "capture", new ZType { K = ZK.Ptr, Elem = elemT, Const = icf.Name == "keyIterator" }, false);
                csym.Ir = vsym;
                ptrCaptures[csym] = vsym;
                IrExpr iterable = icf.Name == "keyIterator" ? mr.Ir : new IrOpCall(IrOp.MapValues, mr.Ir) { Type = IrType.List(elemT.Ir()), NameSpan = new Span(icf.NameStart, icf.NameEnd), Span = vd.Init.Span };
                fe = new IrForEach { Var = vsym, Iterable = iterable, KwSpan = kw, VarSpan = new Span(ns, ne), Span = new Span(vd.Start, w.End) };
            }
            loops.Add(null);
            fe.Body = Block(w.Body, true);
            loops.RemoveAt(loops.Count - 1);
            PopScope();
            fe.Leading = null;
            outList.Add(fe);
            return true;
        }

        readonly Dictionary<ZSym, (IrSymbol Key, IrSymbol Value, IrExpr Map)> entryInfo = new Dictionary<ZSym, (IrSymbol, IrSymbol, IrExpr)>();
        readonly Dictionary<ZSym, IrSymbol> ptrCaptures = new Dictionary<ZSym, IrSymbol>();

        // ------------------------------------------------------------------ switch
        /// <summary>
        /// Switch statement (prongValue == null) or a switch expression used as a whole value: each prong's value goes
        /// through prongValue (an assignment or a return).
        /// </summary>
        void SwitchStmt(ZSwitch sw, List<IrStmt> outList, Func<ZR, ZNode, IrStmt> prongValue, ZType valueType = null)
        {
            if (sw.Label != null) NotSupported("labeled switches are", sw);
            var sel = Expr(sw.Selector, null);
            var st = sel.T;
            if (st.IsErr) return;
            if (!(st.IsIntLike || st.K == ZK.Game || st.K == ZK.Bool))
            {
                if (st.IsString) ErrA("cannot switch on strings", sw.Selector);
                else NotSupported("switch on " + Q(st) + " is", sw.Selector);
                return;
            }
            var ir = new IrSwitch { Selector = sel.Ir, KwSpan = new Span(sw.Start, sw.KwEnd), Span = sw.Span, Style = IrSwitchStyle.Match };
            var seen = new HashSet<string>();
            bool hasElse = false;
            foreach (var pr in sw.Prongs)
            {
                var c = new IrCase { IsDefault = pr.IsElse, LabelSpan = new Span(pr.Start, pr.ArrowEnd), Span = new Span(pr.Start, pr.End) };
                if (pr.IsElse)
                {
                    if (hasElse) ErrS("multiple else prongs in switch expression", pr.Start, pr.ArrowEnd);
                    hasElse = true;
                }
                if (pr.Ranges != null) { NotSupported("switch ranges (a...b) are", pr.Ranges[0].From); continue; }
                if (pr.Capture != null) { NotSupported("switch prong captures are", new ZSpanNode(pr.Capture.Start, pr.Capture.End)); continue; }
                foreach (var item in pr.Items)
                {
                    var lr = Expr(item, st);
                    if (lr.T.IsErr) continue;
                    var li = Coerce(lr, st, item);
                    string key = null;
                    if (li is IrConst cc) key = cc.Name;
                    else if (lr.Known) key = st.K == ZK.Bool ? lr.BoolVal.ToString() : lr.IntVal.ToString();
                    else { ErrS("unable to resolve comptime value", item); continue; }
                    if (!seen.Add(key)) ErrS("duplicate switch value", item);
                    c.Labels.Add(li);
                }
                c.Body = new IrBlock { Span = pr.Body.Span };
                PushScope();
                if (prongValue != null)
                {
                    if (pr.Body is ZBlock) NotSupported("a block as the value of a switch prong is", pr.Body);
                    else if (pr.Body is ZExprStmt es)
                    {
                        if (es.Expr is ZControlExpr ce && ce.Kw == "unreachable") { }
                        else if (es.Expr is ZControlExpr ce2 && ce2.Kw == "return") Stmt(new ZReturn { Value = ce2.Value, Start = ce2.Start, End = ce2.End }, c.Body.Stmts);
                        else
                        {
                            var v = Expr(es.Expr, valueType);
                            if (!v.T.IsErr) c.Body.Stmts.Add(prongValue(v, es.Expr));
                        }
                    }
                    else NotSupported("an assignment as the value of a switch prong is", pr.Body);
                }
                else
                {
                    if (pr.Body is ZBlock b) c.Body = Block(b, false);
                    else if (pr.Body is ZExprStmt es && es.Expr is ZBlockValue bv) c.Body = Block(bv.Block, false);
                    else Stmt(pr.Body, c.Body.Stmts);
                    c.Body.Span = pr.Body.Span;
                }
                PopScope();
                ir.Cases.Add(c);
            }
            if (!hasElse)
            {
                bool complete = false;
                if (st.K == ZK.Bool) complete = seen.Contains("True") && seen.Contains("False");
                else if (st.K == ZK.Game && st.Name == "Direction") complete = GameApi.DirectionNames.All(seen.Contains);
                if (!complete)
                {
                    if (st.K == ZK.Game && st.Name != "Direction")
                        ErrS("switch must handle all possibilities (JavaFarmer cannot know every member of " + Q(st) + ": add an 'else' prong)", sw.Start, sw.KwEnd);
                    else ErrS("switch must handle all possibilities", sw.Start, sw.KwEnd);
                }
            }
            outList.Add(ir);
        }
    }

    /// <summary>IrSymbol.Tag of Zig-frontend symbols: the exact Zig type (the printer reuses it for round trips).</summary>
    internal sealed class ZigTag
    {
        public ZType Type;
        public ZFnSig Sig;
        public string EntryOf;     // key / value symbol of a map iterator entry capture (the capture's name)
        public bool PtrCapture;    // |k| of keyIterator(): used as k.*
        public bool Undefined;     // a container-level var initialised with undefined: no assignment in the game (ZigLanguage.Compile)
    }

    internal static class ZCaptureExt
    {
        public static Span Span(this ZCapture c) => new Span(c.Start, c.End);
    }
}
