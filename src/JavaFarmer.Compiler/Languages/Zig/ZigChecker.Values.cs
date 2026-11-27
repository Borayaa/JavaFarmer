using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.ZigImpl
{
    /// <summary>
    /// Value semantics the game's Python does not have on its own (docs/languages/zig.md "Values"):
    /// array copies, the null of std.mem.indexOfScalar / list.pop(), tuple element assignment, comptime-known
    /// container-level initializers.
    /// </summary>
    internal sealed partial class ZigChecker
    {
        /// <summary>A copy of a simple pure expression (names, literals, constants, indexing and arithmetic of those); null otherwise.</summary>
        static IrExpr CloneSimple(IrExpr e)
        {
            switch (e)
            {
                case IrName n:
                    return new IrName(n.Symbol) { Span = n.Span, Type = n.Type, QualifierSpan = n.QualifierSpan, NameSpan = n.NameSpan };
                case IrLiteral l:
                    return new IrLiteral { Kind = l.Kind, Int = l.Int, Float = l.Float, Bool = l.Bool, Str = l.Str, Type = l.Type, Span = l.Span };
                case IrConst c:
                    return new IrConst { Category = c.Category, Name = c.Name, Type = c.Type, Span = c.Span };
                case IrIndex ix:
                    {
                        var t = CloneSimple(ix.Target);
                        var i = CloneSimple(ix.Index);
                        if (t == null || i == null) return null;
                        return new IrIndex { Target = t, Index = i, BaseOffset = ix.BaseOffset, Span = ix.Span, Type = ix.Type };
                    }
                case IrBinary b:
                    {
                        var l = CloneSimple(b.Left);
                        var r = CloneSimple(b.Right);
                        if (l == null || r == null) return null;
                        return new IrBinary { Op = b.Op, Left = l, Right = r, OpSpan = b.OpSpan, Span = b.Span, Type = b.Type };
                    }
                case IrUnary u:
                    {
                        var o = CloneSimple(u.Operand);
                        if (o == null) return null;
                        return new IrUnary { Op = u.Op, Operand = o, OpSpan = u.OpSpan, Span = u.Span, Type = u.Type };
                    }
                case IrOpCall oc when oc.Op == IrOp.Len || oc.Op == IrOp.PosX || oc.Op == IrOp.PosY || oc.Op == IrOp.CompanionEntity || oc.Op == IrOp.CompanionPos:
                    {
                        var res = new IrOpCall { Op = oc.Op, NameSpan = oc.NameSpan, Span = oc.Span, Type = oc.Type, Synthetic = oc.Synthetic };
                        foreach (var a in oc.Args)
                        {
                            var ca = CloneSimple(a);
                            if (ca == null) return null;
                            res.Args.Add(ca);
                        }
                        return res;
                    }
            }
            return null;
        }

        static IrLiteral SynthNull(Span at) => new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = at, Synthetic = true };

        /// <summary>len(list) > 0 (Synthetic) for the guard of a pop.</summary>
        static IrExpr NotEmpty(IrExpr list, IrOpCall at)
        {
            var zero = IrLiteral.OfInt(0); zero.Span = at.Span; zero.Synthetic = true;
            return new IrBinary { Op = IrBinOp.Gt, Left = new IrOpCall(IrOp.Len, list) { NameSpan = at.NameSpan, Span = at.Span, Type = IrType.Int, Synthetic = true }, Right = zero, OpSpan = at.NameSpan, Span = at.Span, Type = IrType.Bool, Synthetic = true };
        }

        // ------------------------------------------------------------------ optionals the game represents differently
        /// <summary>
        /// std.mem.indexOfScalar(...) (ListIndexOf: -1 when missing) and list.pop() (an error on an empty list in the
        /// game) used as an optional value: cond ? op : null, so that the value is null exactly when Zig's is
        /// ("x in s" / "len(list) > 0"; both tests and the IrConditional are Synthetic, i.e. free). Other results unchanged.
        /// </summary>
        ZR OptValue(ZR r, ZNode at, IrExpr orElse = null)
        {
            if (r == null || r.Ir == null || !(r.Ir is IrOpCall oc)) return r;
            IrExpr cond;
            if (r.Special == "find" && oc.Op == IrOp.ListIndexOf)
            {
                var s = CloneSimple(oc.Args[0]);
                var x = CloneSimple(oc.Args[1]);
                if (s == null || x == null)
                {
                    NotSupported("std.mem.indexOfScalar as an optional value is only supported with a variable (or list.items) and a simple value (store them in constants first); here it is", at);
                    return ZR.Error;
                }
                cond = new IrOpCall(IrOp.Contains, s, x) { NameSpan = oc.NameSpan, Span = oc.Span, Type = IrType.Bool, Synthetic = true };
            }
            else if (r.Special == "pop" && oc.Op == IrOp.ListPop && oc.Args.Count == 1)
            {
                var l = CloneSimple(oc.Args[0]);
                if (l == null) return r;
                cond = NotEmpty(l, oc);
            }
            else return r;
            var t = orElse != null ? r.T.Elem : r.T;
            oc.Type = r.T.Elem.Ir();
            var ce = new IrConditional { Cond = cond, Then = oc, Else = orElse ?? SynthNull(oc.Span), QSpan = oc.NameSpan, Span = oc.Span, Type = t.Ir(), Synthetic = true };
            return new ZR { Ir = ce, T = t };
        }

        /// <summary>A find / pop result where only a whole value can take it (print arguments, if branches ...).</summary>
        bool RejectOptOp(ZR r, ZNode at)
        {
            if (r?.Special == "find") { NotSupported("std.mem.indexOfScalar is only supported as a whole value (const i = std.mem.indexOfScalar(...);, if (...) |i|, ... orelse x, == null); here it is", at); return true; }
            if (r?.Special == "pop") { NotSupported("list.pop() is only supported as a whole value (const x = list.pop();, if (list.pop()) |x|, ... orelse x, == null) or unwrapped with .?; here it is", at); return true; }
            return false;
        }

        // ------------------------------------------------------------------ arrays are values
        static IrSymbol RootSymbol(IrExpr e)
        {
            while (e is IrIndex ix) e = ix.Target;
            return (e as IrName)?.Symbol;
        }

        /// <summary>
        /// Zig copies an array wherever its value goes (var b = a;, b = a;, arguments, returns, elements); a Python list
        /// would be shared. A value read from a variable or an element (IrName / IrIndex) of array type becomes
        /// list(a) (nested arrays: [list(a[0]), list(a[1]), ...]); fresh values (literals, calls) are not copied, nor
        /// a constant read into a constant (neither side can change). ArrayList / HashMap values are not copied: in Zig
        /// they copy their header and share the buffer.
        /// </summary>
        IrExpr CopyArray(IrExpr ir, ZType t, ZNode at, bool destConst)
        {
            if (ir == null || t == null) return ir;
            if (t.K == ZK.Optional) t = t.Elem;
            if (t == null || t.K != ZK.Array) return ir;
            if (!(ir is IrName || ir is IrIndex)) return ir;
            var root = RootSymbol(ir);
            if (root == null) return ir;
            switch (root.Kind)
            {
                case IrSymbolKind.Local: case IrSymbolKind.Param: case IrSymbolKind.Global: case IrSymbolKind.ExternalGlobal: break;
                default: return ir;
            }
            if (destConst && root.IsConst) return ir;
            return ArrayCopyOf(ir, t, at);
        }

        IrExpr ArrayCopyOf(IrExpr src, ZType t, ZNode at)
        {
            if (t.Elem == null || t.Elem.K != ZK.Array)
                return new IrOpCall(IrOp.ListCopy, src) { Span = src.Span, Type = src.Type.IsKnown ? src.Type : t.Ir(), Synthetic = true };
            if (t.Len > 256) { NotSupported("copying an array of more than 256 arrays is", at); return src; }
            var lit = new IrListLit { IsArray = true, Span = src.Span, Type = t.Ir(), Synthetic = true };
            for (int i = 0; i < t.Len; i++)
            {
                var s = i == t.Len - 1 ? src : CloneSimple(src);
                if (s == null) { NotSupported("copying an array of arrays that is not a variable or an element (store it in a variable first) is", at); return src; }
                var idx = IrLiteral.OfInt(i);
                idx.Span = src.Span; idx.Synthetic = true;
                lit.Items.Add(ArrayCopyOf(new IrIndex { Target = s, Index = idx, Span = src.Span, Type = t.Elem.Ir(), Synthetic = true }, t.Elem, at));
            }
            return lit;
        }

        // ------------------------------------------------------------------ container-level initializers are comptime
        /// <summary>
        /// Zig evaluates the initializer of a container-level const / var at comptime ("unable to resolve comptime
        /// value" + "initializer of container-level variable must be comptime-known"): it may not read a container-level
        /// var nor call a farm function (they run in the game). Calls of the program's own functions are not supported
        /// (Zig would run them at comptime; the game would run them when the file is loaded).
        /// </summary>
        void CheckComptimeInit(ZExpr init, IrExpr ir)
        {
            const string why = "unable to resolve comptime value: the initializer of a container-level variable must be comptime-known";
            var bad = FirstRuntimeRead(init);
            if (bad != null) { ErrS(why + " (" + Q(bad.Name) + " is a var; assign it in a function, e.g. at the start of main)", bad); return; }
            if (ir == null) return;
            foreach (var n in new[] { ir }.Concat(IrWalk.Descendants(ir)))
            {
                if (n is IrName en && en.Symbol != null && en.Symbol.Kind == IrSymbolKind.ExternalGlobal && !en.Symbol.IsConst)
                { ErrS(why + " (" + Q(en.Symbol.Module + "." + en.Symbol.Name) + " is a var; assign it in a function, e.g. at the start of main)", en.Span.Start, en.Span.End); return; }
                if (!(n is IrCall c)) continue;
                if (c.Kind == IrCallKind.Builtin)
                {
                    if (c.Builtin == "min" || c.Builtin == "max" || c.Builtin == "abs") continue;
                    var zn = ZigApi.OfPython(c.Builtin).Select(f => "farm." + f.ZigName).FirstOrDefault() ?? c.Builtin;
                    ErrS(why + " (" + zn + " runs in the game: call it in a function, e.g. at the start of main)", c.Span.Start, c.Span.End);
                    return;
                }
                NotSupported("calling a function in the initializer of a container-level variable (Zig would run it at comptime; call it in main instead) is", new ZSpanNode(c.Span.Start, c.Span.End));
                return;
            }
        }

        /// <summary>The first identifier in e that names a container-level var (not under &).</summary>
        ZIdent FirstRuntimeRead(ZExpr e)
        {
            switch (e)
            {
                case null: return null;
                case ZIdent id:
                    {
                        if (container.TryGetValue(id.Name, out var s) && s.Kind == ZSymKind.Global && s.IsVar) return id;
                        return null;
                    }
                case ZUnary u: return u.Op == "&" ? null : FirstRuntimeRead(u.Operand);
                case ZField f: return FirstRuntimeRead(f.Target);
                case ZDeref d: return FirstRuntimeRead(d.Target);
                case ZUnwrap uw: return FirstRuntimeRead(uw.Target);
                case ZIndex ix: return FirstRuntimeRead(ix.Target) ?? FirstRuntimeRead(ix.Index);
                case ZCall c: return FirstRuntimeRead(c.Callee) ?? c.Args.Select(FirstRuntimeRead).FirstOrDefault(x => x != null);
                case ZBuiltinCall bc: return bc.Args.Select(FirstRuntimeRead).FirstOrDefault(x => x != null);
                case ZBinary b: return FirstRuntimeRead(b.Left) ?? FirstRuntimeRead(b.Right);
                case ZGrouped g: return FirstRuntimeRead(g.Inner);
                case ZIfExpr ie: return FirstRuntimeRead(ie.Cond) ?? FirstRuntimeRead(ie.Then) ?? FirstRuntimeRead(ie.Else);
                case ZCatchExpr ce: return FirstRuntimeRead(ce.Left) ?? FirstRuntimeRead(ce.Right);
                case ZInit init:
                    return init.Items.Select(FirstRuntimeRead).FirstOrDefault(x => x != null)
                        ?? init.Fields?.Select(f => FirstRuntimeRead(f.Value)).FirstOrDefault(x => x != null);
            }
            return null;
        }

        // ------------------------------------------------------------------ tuples / positions are immutable in Python
        /// <summary>
        /// t[i] = v on a tuple (or p.x = v on a farm.Position): Python tuples cannot be changed, so the whole value is
        /// rebuilt: t = (v, t[1]) / p = (v, p[1]). The base must be a variable or an element with a simple index.
        /// Returns false when the target is not such an element.
        /// </summary>
        bool TupleElementAssign(ZAssign a, List<IrStmt> outList)
        {
            ZExpr baseExpr; int index = -1; string field = null;
            var target = Unparen(a.Target);
            if (target is ZIndex ix) baseExpr = ix.Target;
            else if (target is ZField f && (f.Name == "x" || f.Name == "y")) { baseExpr = f.Target; field = f.Name; }
            else return false;
            var probe = TypeOfQuiet(baseExpr);
            if (probe == null) return false;
            if (field == null && probe.K != ZK.Tuple) return false;
            if (field != null && !(probe.K == ZK.Struct && probe.Name == "Position")) return false;
            var lv = LValue(baseExpr);
            if (lv == null) { Expr(a.Value, null); return true; }
            var bt = lv.T;
            List<ZType> items;
            if (field != null) { items = new List<ZType> { ZType.Usize, ZType.Usize }; index = field == "x" ? 0 : 1; }
            else
            {
                items = bt.Items;
                var ir0 = Expr(((ZIndex)target).Index, ZType.Usize);
                if (ir0.T.IsErr) return true;
                if (!ir0.Known) { ErrS("unable to resolve comptime value", ((ZIndex)target).Index); return true; }
                if (ir0.IntVal < 0 || ir0.IntVal >= items.Count) { ErrS("index " + ir0.IntVal + " outside tuple of length " + items.Count, ((ZIndex)target).Index); return true; }
                index = (int)ir0.IntVal;
            }
            var et = items[index];
            IrExpr Item(int i)
            {
                var b = CloneSimple(lv.Ir);
                if (b == null) return null;
                if (field != null) return new IrOpCall(i == 0 ? IrOp.PosX : IrOp.PosY, b) { Span = target.Span, Type = IrType.Int, Synthetic = true };
                var li = IrLiteral.OfInt(i); li.Span = target.Span; li.Synthetic = true;
                return new IrIndex { Target = b, Index = li, Span = target.Span, Type = items[i].Ir(), Synthetic = true };
            }
            var val = Whole(a.Value, et);
            if (val.T.IsErr) return true;
            IrExpr v;
            if (a.Op == "=")
            {
                if (val.T.K == ZK.Undefined) { NotSupported("assigning undefined is", a.Value); return true; }
                v = Coerce(val, et, a.Value);
            }
            else
            {
                if (!CompoundOps.TryGetValue(a.Op, out var op)) { NotSupported("operator " + Q(a.Op) + " is", new ZSpanNode(a.OpStart, a.OpEnd)); return true; }
                if (!et.IsNumeric) { ErrS("invalid operands to binary expression: " + Q(et) + " and " + Q(val.T), a); return true; }
                var cur = Item(index);
                if (cur == null) { NotSupported("assigning to an element of a computed tuple is", a.Target); return true; }
                var lhs = new ZR { T = et, Ir = cur };
                var peer = Peer(lhs, val);
                if (peer == null) { ErrS("incompatible types: " + Q(et) + " and " + Q(val.T), a); return true; }
                if (!peer.IsErr && !peer.Same(et) && !val.T.IsComptimeNum) { ErrS(Mismatch(val.T, et), a.Value); return true; }
                if (op == IrBinOp.TrueDiv || op == IrBinOp.ModTrunc)
                {
                    var bop = DivOp(a.Op == "/=" ? "/" : "%", lhs, val, et, new ZSpanNode(a.OpStart, a.OpEnd));
                    if (bop == null) return true;
                    op = bop.Value;
                }
                v = new IrBinary { Op = op, Left = cur, Right = Coerce(val, et, a.Value), OpSpan = new Span(a.OpStart, a.OpEnd), Span = a.Span, Type = et.Ir() };
            }
            var lit = new IrTupleLit { Span = a.Span, Type = lv.Ir.Type, Synthetic = true };
            for (int i = 0; i < items.Count; i++)
            {
                if (i == index) { lit.Items.Add(v); continue; }
                var it = Item(i);
                if (it == null) { NotSupported("assigning to an element of a computed tuple is", a.Target); return true; }
                lit.Items.Add(it);
            }
            outList.Add(new IrAssign { Target = lv.Ir, Value = lit, OpSpan = new Span(a.OpStart, a.OpEnd), Span = a.Span });
            return true;
        }

        /// <summary>The Zig type of an expression without reporting errors or marking anything used (null when unknown).</summary>
        ZType TypeOfQuiet(ZExpr e)
        {
            switch (Unparen(e))
            {
                case ZIdent id:
                    {
                        var s = LookupQuiet(id.Name);
                        return s != null && (s.Kind == ZSymKind.Local || s.Kind == ZSymKind.Global || s.Kind == ZSymKind.Param || s.Kind == ZSymKind.Capture) ? s.Type : null;
                    }
                case ZIndex ix:
                    {
                        var bt = TypeOfQuiet(ix.Target);
                        if (bt == null) return null;
                        if (bt.K == ZK.Ptr) bt = bt.Elem;
                        if (bt.K == ZK.Array || bt.K == ZK.Slice) return bt.Elem;
                        if (bt.K == ZK.Tuple && Unparen(ix.Index) is ZIntLit il && il.Value >= 0 && il.Value < bt.Items.Count) return bt.Items[(int)il.Value];
                        return null;
                    }
                case ZField f when f.Name == "items":
                    {
                        var bt = TypeOfQuiet(f.Target);
                        if (bt != null && bt.K == ZK.Ptr) bt = bt.Elem;
                        return bt != null && bt.K == ZK.List ? ZType.SliceOf(bt.Elem, false) : null;
                    }
            }
            return null;
        }

        ZSym LookupQuiet(string name)
        {
            for (int i = scopes.Count - 1; i >= 0; i--) if (scopes[i].TryGetValue(name, out var s)) return s;
            return container.TryGetValue(name, out var g) && g.Resolved ? g : null;
        }
    }
}
