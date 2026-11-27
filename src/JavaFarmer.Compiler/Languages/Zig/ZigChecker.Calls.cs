using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal sealed partial class ZigChecker
    {
        static string Args(int n) => n + " argument(s)";

        ZR Call(ZCall c, ZType expected, bool discard)
        {
            // decl literal: .init(farm.allocator) with a known map type
            if (c.Callee is ZEnumLit dl)
            {
                var et = expected != null && expected.K == ZK.Optional ? expected.Elem : expected;
                if (et == null || et.IsErr) { if (et == null) ErrS("'." + dl.Name + "(...)' needs a known type here (write std.AutoHashMap(K, V).init(farm.allocator) or annotate the variable)", c); return ZR.Error; }
                return TypeMethod(et, dl.Name, c, new Span(dl.Start + 1, dl.End));
            }
            if (c.Callee is ZField cf)
            {
                var recv = Expr(cf.Target, null);
                if (recv.T.IsErr) { foreach (var a in c.Args) Expr(a, null); return ZR.Error; }
                if (recv.T.K == ZK.Namespace)
                {
                    var m = NsMember(recv.T.Name, cf);
                    if (m.T.IsErr) { foreach (var a in c.Args) Expr(a, null); return ZR.Error; }
                    return CallValue(m, c, expected, new Span(cf.NameStart, cf.NameEnd), cf.Target.Span);
                }
                if (recv.T.K == ZK.Type && recv.T.Inner != null) return TypeMethod(recv.T.Inner, cf.Name, c, new Span(cf.NameStart, cf.NameEnd));
                if (recv.T.K == ZK.Type && recv.Special != null) { ErrS("type 'type' does not support field access", cf); return ZR.Error; }
                // any method call takes the receiver by reference (AstGen: the variable counts as mutated)
                if (recv.Sym != null) recv.Sym.Mutated = true;
                return Method(recv, cf, c, discard);
            }
            var callee = Expr(c.Callee, null);
            if (callee.T.IsErr) { foreach (var a in c.Args) Expr(a, null); return ZR.Error; }
            var nameSpan = c.Callee.Span;
            return CallValue(callee, c, expected, nameSpan, Span.None);
        }

        ZR CallValue(ZR callee, ZCall c, ZType expected, Span nameSpan, Span qualifier)
        {
            if (callee.Api != null) return ApiCall(callee.Api, c, nameSpan);
            if (callee.Special != null) return SpecialCall(callee.Special, c, expected, nameSpan);
            if (callee.T.K == ZK.Fn && callee.Sym != null) return UserCall(callee.Sym, c, nameSpan, qualifier);
            foreach (var a in c.Args) Expr(a, null);
            if (callee.T.K == ZK.Type) ErrS("type " + Q(callee.T.Inner) + " is not a function", c.Callee);
            else ErrS("type " + Q(callee.T) + " is not a function", c.Callee);
            return ZR.Error;
        }

        List<IrExpr> CoerceArgs(ZCall c, IList<ZType> types, int skip = 0, bool destConst = false)
        {
            var res = new List<IrExpr>();
            for (int i = skip; i < c.Args.Count; i++)
            {
                var t = i - skip < types.Count ? types[i - skip] : null;
                var r = Whole(c.Args[i], t);
                if (r.T.K == ZK.EnumLit && t == null) { NotSupported("an untyped enum literal is", c.Args[i]); continue; }
                res.Add(t != null ? Coerce(r, t, c.Args[i], destConst) : r.Ir);
            }
            return res;
        }

        ZR UserCall(ZSym fn, ZCall c, Span nameSpan, Span qualifier)
        {
            var sig = fn.Sig;
            if (sig == null) return ZR.Error;
            if (c.Args.Count != sig.Params.Count && !(sig.MinParams >= 0 && c.Args.Count >= sig.MinParams && c.Args.Count < sig.Params.Count))
            {
                foreach (var a in c.Args) Expr(a, null);
                ErrS("expected " + Args(sig.Params.Count) + ", found " + c.Args.Count, c);
                return ZR.Error;
            }
            var args = CoerceArgs(c, sig.Params, destConst: true);   // parameters are constants
            var call = new IrCall { Kind = IrCallKind.Function, Function = fn.Ir, NameSpan = qualifier.IsNone ? nameSpan : nameSpan, QualifierSpan = qualifier, Span = c.Span, Type = sig.Ret.Ir() };
            call.Args.AddRange(args);
            return new ZR { Ir = call, T = sig.RetError ? ZType.ErrU(sig.Ret) : sig.Ret };
        }

        // ------------------------------------------------------------------ farm API
        ZR ApiCall(ZApiFn fn, ZCall c, Span nameSpan)
        {
            int want = (fn.TypeArg ? 1 : 0) + fn.Params.Count;
            if (c.Args.Count != want)
            {
                foreach (var a in c.Args) Expr(a, null);
                var alts = ZigApi.OfPython(fn.Builtin.PythonName).Where(f => f != fn).Select(f => "farm." + f.ZigName).ToList();
                ErrS("expected " + Args(want) + ", found " + c.Args.Count + (alts.Count > 0 ? " (see also " + string.Join(", ", alts) + ")" : ""), c);
                return ZR.Error;
            }
            int k = 0;
            ZType ret = fn.Ret;
            if (fn.TypeArg)
            {
                ret = ResolveType(c.Args[0]);
                k = 1;
                if (ret.IsErr) return ZR.Error;
            }
            var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = fn.Builtin.PythonName, NameSpan = nameSpan, Span = c.Span };
            for (int i = 0; i < fn.Params.Count; i++)
            {
                var p = fn.Params[i];
                var arg = c.Args[k + i];
                if (p.IsFunc) { var fr = FuncArg(arg, fn); if (fr != null) call.Args.Add(fr); continue; }
                if (p.IsTuple)
                {
                    if (Unparen(arg) is ZInit ti && ti.Type == null && ti.Fields == null)
                    {
                        foreach (var item in ti.Items)
                        {
                            var ir = Expr(item, null);
                            if (ir.T.K == ZK.EnumLit) { NotSupported("an untyped enum literal is", item); continue; }
                            if (ir.Ir != null) call.Args.Add(ir.Ir);
                        }
                    }
                    else ErrS("expected tuple or struct argument, found " + Q(Expr(arg, null).T), arg);
                    continue;
                }
                if (p.T == null)
                {
                    // .{} for a map / any parameter (farm.simulate's unlocks and globals): an empty map, not the tuple ()
                    if (Unparen(arg) is ZInit ei && ei.Type == null && ei.Items.Count == 0 && (ei.Fields == null || ei.Fields.Count == 0))
                    {
                        var pt = p.Source?.Type;
                        call.Args.Add(new IrMapLit { Span = arg.Span, Type = pt != null && pt.Kind == IrTypeKind.Map ? pt : IrType.Map(IrType.Any, IrType.Any) });
                        continue;
                    }
                    var r = Whole(arg, null);
                    if (r.T.IsErr) continue;
                    if (r.T.K == ZK.EnumLit) { ErrS("farm." + fn.ZigName + " cannot know the type of '." + r.T.Name + "' here: write the type, e.g. farm.Entity." + r.T.Name, arg); continue; }
                    var text = p.Source?.TypeText ?? "";
                    if (text.Contains("|"))
                    {
                        var allowed = text.Split('|').Select(x => x.Trim()).ToList();
                        if (!(r.T.K == ZK.Game && allowed.Contains(r.T.Name)))
                        {
                            ErrS("expected type " + string.Join(" or ", allowed.Select(x => Q("farm." + x))) + ", found " + Q(r.T), arg);
                            continue;
                        }
                    }
                    else if (r.T.K == ZK.Type || r.T.K == ZK.Namespace) { NotSupported("passing a type to farm." + fn.ZigName + " is", arg); continue; }
                    call.Args.Add(r.Ir);
                    continue;
                }
                var v = Whole(arg, p.T);
                call.Args.Add(Coerce(v, p.T, arg));
            }
            call.Type = ret.Ir();
            return new ZR { Ir = call, T = ret };
        }

        /// <summary>A function passed to a builtin (farm.spawnDrone(worker)): an IrFuncRef.</summary>
        IrExpr FuncArg(ZExpr arg, ZApiFn fn)
        {
            var u = Unparen(arg);
            ZR r = Expr(u, null);
            if (r.T.IsErr) return null;
            if (r.T.K == ZK.Fn && r.Sym != null && r.Sym.Sig != null)
            {
                if (r.Sym.Sig.Params.Count != 0) { ErrS("farm." + fn.ZigName + " needs a function without parameters, found " + Q(ZType.FnText(r.Sym.Sig)), arg); return null; }
                var fr = new IrFuncRef { Function = r.Sym.Ir, Span = arg.Span, NameSpan = u is ZField zf ? new Span(zf.NameStart, zf.NameEnd) : arg.Span, Type = IrType.Func(r.Sym.Sig.Ret.Ir()) };
                if (u is ZField zf2) fr.QualifierSpan = zf2.Target.Span;
                return fr;
            }
            if (r.Api != null) { NotSupported("passing a farm function as a value is", arg); return null; }
            ErrS("farm." + fn.ZigName + " needs a function, found " + Q(r.T), arg);
            return null;
        }

        // ------------------------------------------------------------------ methods
        void Mutating(ZR recv, ZCall c, ZField f)
        {
            var t = recv.T;
            if (t.K == ZK.Ptr) { if (t.Const) ErrS("expected type " + Q("*" + t.Elem) + ", found " + Q("*const " + t.Elem), f.Target); return; }
            if (recv.Sym != null && !recv.Sym.IsVar && recv.Sym.Kind != ZSymKind.Global)
                ErrS("expected type " + Q("*" + t) + ", found " + Q("*const " + t) + " (declare it with 'var')", f.Target);
            else if (recv.Sym != null && !recv.Sym.IsVar)
                ErrS("expected type " + Q("*" + t) + ", found " + Q("*const " + t) + " (declare it with 'var')", f.Target);
        }

        bool AllocArg(ZExpr a)
        {
            var r = Expr(a, ZType.Alloc);
            if (r.T.IsErr) return false;
            if (r.T.K != ZK.Allocator) { ErrS("expected type 'mem.Allocator', found " + Q(r.T) + " (pass farm.allocator)", a); return false; }
            return true;
        }

        ZR Method(ZR recv, ZField f, ZCall c, bool discard)
        {
            var t = recv.T.K == ZK.Ptr ? recv.T.Elem : recv.T;
            var n = f.Name;
            var ns = new Span(f.NameStart, f.NameEnd);
            var recvIr = recv.Ir;
            bool Arity(int k)
            {
                if (c.Args.Count == k) return true;
                foreach (var a in c.Args) Expr(a, null);
                ErrS("member function expected " + Args(k) + ", found " + c.Args.Count, c);
                return false;
            }
            IrOpCall Op(IrOp op, IrType type, params IrExpr[] args) => new IrOpCall(op, args) { NameSpan = ns, Span = c.Span, Type = type };
            if (recv.Special == "items") { ErrS("no field or member function named " + Q(n) + " in " + Q(recv.T), ns.Start, ns.End); return ZR.Error; }
            switch (t.K)
            {
                case ZK.List:
                    switch (n)
                    {
                        case "append":
                            {
                                if (!Arity(2)) return ZR.Error;
                                Mutating(recv, c, f);
                                AllocArg(c.Args[0]);
                                var v = Whole(c.Args[1], t.Elem);
                                return new ZR { Ir = Op(IrOp.ListAppend, IrType.Void, recvIr, Coerce(v, t.Elem, c.Args[1])), T = ZType.ErrU(ZType.Void) };
                            }
                        case "insert":
                            {
                                if (!Arity(3)) return ZR.Error;
                                Mutating(recv, c, f);
                                AllocArg(c.Args[0]);
                                var i = Expr(c.Args[1], ZType.Usize);
                                var v = Whole(c.Args[2], t.Elem);
                                return new ZR { Ir = Op(IrOp.ListInsert, IrType.Void, recvIr, Coerce(i, ZType.Usize, c.Args[1]), Coerce(v, t.Elem, c.Args[2])), T = ZType.ErrU(ZType.Void) };
                            }
                        case "pop":
                            if (!Arity(0)) return ZR.Error;
                            Mutating(recv, c, f);
                            // Special "pop": OptValue / Compare / Discard guard it (the game's pop fails on an empty list, Zig's returns null)
                            return new ZR { Ir = Op(IrOp.ListPop, t.Elem.Ir().AsNullable(), recvIr), T = ZType.Opt(t.Elem), Special = "pop" };
                        case "orderedRemove":
                            {
                                if (!Arity(1)) return ZR.Error;
                                Mutating(recv, c, f);
                                // list.orderedRemove(std.mem.findScalar(T, list.items, x).?) → remove(x)
                                if (Unparen(c.Args[0]) is ZUnwrap uw && Unparen(uw.Target) is ZCall fc)
                                {
                                    var fcallee = Expr(fc.Callee, null);
                                    if (fcallee.Special == "mem.find" && fc.Args.Count == 3)
                                    {
                                        var fr = SpecialCall("mem.find", fc, null, fc.Callee.Span);
                                        if (fr.T.IsErr) return ZR.Error;
                                        var fo = (IrOpCall)fr.Ir;
                                        if (SameName(fo.Args[0], recvIr))
                                            return new ZR { Ir = Op(IrOp.RemoveValue, t.Elem.Ir(), recvIr, fo.Args[1]), T = t.Elem };
                                    }
                                }
                                var i = Expr(c.Args[0], ZType.Usize);
                                return new ZR { Ir = Op(IrOp.ListPop, t.Elem.Ir(), recvIr, Coerce(i, ZType.Usize, c.Args[0])), T = t.Elem };
                            }
                        case "clearRetainingCapacity":
                            if (!Arity(0)) return ZR.Error;
                            Mutating(recv, c, f);
                            return new ZR { Ir = Op(IrOp.Clear, IrType.Void, recvIr), T = ZType.Void };
                        case "clearAndFree":
                            if (!Arity(1)) return ZR.Error;
                            Mutating(recv, c, f);
                            AllocArg(c.Args[0]);
                            return new ZR { Ir = Op(IrOp.Clear, IrType.Void, recvIr), T = ZType.Void };
                        case "deinit":
                            if (!Arity(1)) return ZR.Error;
                            Mutating(recv, c, f);
                            AllocArg(c.Args[0]);
                            return new ZR { T = ZType.Void };
                        case "clone":
                            if (!Arity(1)) return ZR.Error;
                            AllocArg(c.Args[0]);
                            return new ZR { Ir = Op(IrOp.ListCopy, t.Ir(), recvIr), T = ZType.ErrU(t) };
                        case "appendSlice": case "swapRemove": case "last": case "getLast": case "insertSlice": case "resize": case "toOwnedSlice": case "ensureTotalCapacity": case "appendAssumeCapacity": case "addOne": case "items":
                            foreach (var a in c.Args) Expr(a, null);
                            NotSupported("ArrayList." + n + " is", ns2(f));
                            return ZR.Error;
                    }
                    foreach (var a in c.Args) Expr(a, null);
                    ErrS("no field or member function named " + Q(n) + " in " + Q(t), ns.Start, ns.End);
                    return ZR.Error;
                case ZK.Map:
                    switch (n)
                    {
                        case "put":
                            {
                                if (!Arity(2)) return ZR.Error;
                                Mutating(recv, c, f);
                                var k = Whole(c.Args[0], t.Key);
                                var ki = Coerce(k, t.Key, c.Args[0]);
                                if (t.IsSet)
                                {
                                    var v0 = Expr(c.Args[1], ZType.Void);
                                    if (!v0.T.IsErr && v0.T.K != ZK.Void) ErrS("expected type 'void', found " + Q(v0.T) + " (a set stores {})", c.Args[1]);
                                    return new ZR { Ir = Op(IrOp.SetAdd, IrType.Void, recvIr, ki), T = ZType.ErrU(ZType.Void) };
                                }
                                var v = Whole(c.Args[1], t.Elem);
                                return new ZR { Ir = Op(IrOp.MapPut, IrType.Void, recvIr, ki, Coerce(v, t.Elem, c.Args[1])), T = ZType.ErrU(ZType.Void) };
                            }
                        case "get":
                            {
                                if (!Arity(1)) return ZR.Error;
                                if (t.IsSet) { Expr(c.Args[0], t.Key); NotSupported("get() on a set is (use contains)", ns2(f)); return ZR.Error; }
                                var k = Whole(c.Args[0], t.Key);
                                return new ZR { Ir = Op(IrOp.MapGetOrNull, t.Elem.Ir().AsNullable(), recvIr, Coerce(k, t.Key, c.Args[0])), T = ZType.Opt(t.Elem) };
                            }
                        case "contains":
                            {
                                if (!Arity(1)) return ZR.Error;
                                var k = Whole(c.Args[0], t.Key);
                                return new ZR { Ir = Op(IrOp.Contains, IrType.Bool, recvIr, Coerce(k, t.Key, c.Args[0])), T = ZType.Bool };
                            }
                        case "count":
                            if (!Arity(0)) return ZR.Error;
                            return new ZR { Ir = Op(IrOp.Len, IrType.Int, recvIr), T = ZType.U32 };
                        case "remove":
                            {
                                if (!Arity(1)) return ZR.Error;
                                Mutating(recv, c, f);
                                var k = Whole(c.Args[0], t.Key);
                                var ki = Coerce(k, t.Key, c.Args[0]);
                                if (!discard) { NotSupported("using the result of remove() is (write _ = map.remove(k);) ; here it", c); return ZR.Error; }
                                return new ZR { Ir = Op(t.IsSet ? IrOp.RemoveValueIfPresent : IrOp.MapRemoveIfPresent, IrType.Bool, recvIr, ki), T = ZType.Bool };
                            }
                        case "deinit":
                            if (!Arity(0)) return ZR.Error;
                            Mutating(recv, c, f);
                            return new ZR { T = ZType.Void };
                        case "clearRetainingCapacity":
                        case "clearAndFree":
                            if (!Arity(0)) return ZR.Error;
                            Mutating(recv, c, f);
                            return new ZR { Ir = Op(IrOp.Clear, IrType.Void, recvIr), T = ZType.Void };
                        case "iterator": case "keyIterator": case "valueIterator":
                            foreach (var a in c.Args) Expr(a, null);
                            NotSupported("a map iterator outside 'var it = map." + n + "(); while (it.next()) |x| { ... }' is", c);
                            return ZR.Error;
                        case "getOrPut": case "fetchRemove": case "getPtr": case "putNoClobber": case "getOrPutValue": case "clone": case "fetchPut": case "getEntry": case "getKey": case "ensureTotalCapacity": case "capacity":
                            foreach (var a in c.Args) Expr(a, null);
                            NotSupported("HashMap." + n + " is", ns2(f));
                            return ZR.Error;
                    }
                    foreach (var a in c.Args) Expr(a, null);
                    ErrS("no field or member function named " + Q(n) + " in " + Q(t), ns.Start, ns.End);
                    return ZR.Error;
                case ZK.Iter:
                    NotSupported("a map iterator outside 'while (it.next()) |x| { ... }' is", c);
                    return ZR.Error;
            }
            foreach (var a in c.Args) Expr(a, null);
            if (t.K == ZK.Optional) ErrS("no field or member function named " + Q(n) + " in " + Q(t) + " (unwrap it first, e.g. with .? or if (x) |v|)", ns.Start, ns.End);
            else ErrS("no field or member function named " + Q(n) + " in " + Q(t), ns.Start, ns.End);
            return ZR.Error;
        }

        static ZNode ns2(ZField f) => new ZSpanNode(f.NameStart, f.NameEnd);

        static bool SameName(IrExpr a, IrExpr b) => a is IrName x && b is IrName y && x.Symbol == y.Symbol;

        ZR TypeMethod(ZType t, string name, ZCall c, Span nameSpan)
        {
            if (t.K == ZK.Map && name == "init")
            {
                if (c.Args.Count != 1) { foreach (var a in c.Args) Expr(a, null); ErrS("expected " + Args(1) + ", found " + c.Args.Count, c); return ZR.Error; }
                AllocArg(c.Args[0]);
                IrExpr lit = t.IsSet ? (IrExpr)new IrSetLit() : new IrMapLit();
                lit.Span = c.Span;
                lit.Type = t.Ir();
                return new ZR { Ir = lit, T = t };
            }
            foreach (var a in c.Args) Expr(a, null);
            if (t.K == ZK.List && name == "init")
            { ErrS("struct " + Q(t) + " has no member named 'init' (std.ArrayList is unmanaged since Zig 0.15: var list: std.ArrayList(T) = .empty;)", nameSpan.Start, nameSpan.End); return ZR.Error; }
            if (t.K == ZK.List && (name == "initCapacity" || name == "initBuffer")) { NotSupported("ArrayList." + name + " is", new ZSpanNode(nameSpan.Start, nameSpan.End)); return ZR.Error; }
            ErrS("struct " + Q(t) + " has no member named " + Q(name), nameSpan.Start, nameSpan.End);
            return ZR.Error;
        }

        // ------------------------------------------------------------------ std functions
        ZR SpecialCall(string special, ZCall c, ZType expected, Span nameSpan)
        {
            switch (special)
            {
                case "print":
                case "quick_print":
                    if (c.Args.Count != 2)
                    {
                        foreach (var a in c.Args) Expr(a, null);
                        ErrS("expected " + Args(2) + ", found " + c.Args.Count, c);
                        return ZR.Error;
                    }
                    return Print(special, c.Args[0], c.Args[1], c, nameSpan);
                case "mem.eql":
                    {
                        if (c.Args.Count != 3) { foreach (var a in c.Args) Expr(a, null); ErrS("expected " + Args(3) + ", found " + c.Args.Count, c); return ZR.Error; }
                        var et = ResolveType(c.Args[0]);
                        if (et.IsErr) return ZR.Error;
                        var st = ZType.SliceOf(et, true);
                        var a1 = Whole(c.Args[1], st); var a2 = Whole(c.Args[2], st);
                        var x = Coerce(a1, st, c.Args[1]); var y = Coerce(a2, st, c.Args[2]);
                        return new ZR { Ir = new IrBinary { Op = IrBinOp.Eq, Left = x, Right = y, OpSpan = nameSpan, Span = c.Span, Type = IrType.Bool }, T = ZType.Bool };
                    }
                case "meta.eql":
                    {
                        if (c.Args.Count != 2) { foreach (var a in c.Args) Expr(a, null); ErrS("expected " + Args(2) + ", found " + c.Args.Count, c); return ZR.Error; }
                        var a1 = Whole(c.Args[0], null);
                        if (a1.T.IsErr) { Expr(c.Args[1], null); return ZR.Error; }
                        if (a1.T.K == ZK.EnumLit || a1.T.IsComptimeNum) { NotSupported("std.meta.eql with an untyped first argument is", c.Args[0]); return ZR.Error; }
                        var a2 = Whole(c.Args[1], a1.T);
                        var y = Coerce(a2, a1.T, c.Args[1]);
                        return new ZR { Ir = new IrBinary { Op = IrBinOp.Eq, Left = a1.Ir, Right = y, OpSpan = nameSpan, Span = c.Span, Type = IrType.Bool }, T = ZType.Bool };
                    }
                case "mem.find":
                    {
                        if (c.Args.Count != 3) { foreach (var a in c.Args) Expr(a, null); ErrS("expected " + Args(3) + ", found " + c.Args.Count, c); return ZR.Error; }
                        var et = ResolveType(c.Args[0]);
                        if (et.IsErr) return ZR.Error;
                        var st = ZType.SliceOf(et, true);
                        var a1 = Expr(c.Args[1], st);
                        var x = Coerce(a1, st, c.Args[1]);
                        var a2 = Whole(c.Args[2], et);
                        var y = Coerce(a2, et, c.Args[2]);
                        return new ZR { Ir = new IrOpCall(IrOp.ListIndexOf, x, y) { NameSpan = nameSpan, Span = c.Span, Type = IrType.Int }, T = ZType.Opt(ZType.Usize), Special = "find" };
                    }
                case "math.pow":
                    {
                        if (c.Args.Count != 3) { foreach (var a in c.Args) Expr(a, null); ErrS("expected " + Args(3) + ", found " + c.Args.Count, c); return ZR.Error; }
                        var t = ResolveType(c.Args[0]);
                        if (t.IsErr) return ZR.Error;
                        if (!t.IsNumeric) { ErrS("pow not implemented for " + t, c.Args[0]); return ZR.Error; }
                        var a1 = Whole(c.Args[1], t); var a2 = Whole(c.Args[2], t);
                        var x = Coerce(a1, t, c.Args[1]); var y = Coerce(a2, t, c.Args[2]);
                        return new ZR { Ir = new IrBinary { Op = IrBinOp.Pow, Left = x, Right = y, OpSpan = nameSpan, Span = c.Span, Type = t.Ir() }, T = t };
                    }
                case "std.ArrayList":
                    {
                        if (c.Args.Count != 1) { ErrS("expected " + Args(1) + ", found " + c.Args.Count, c); return ZR.Error; }
                        var e = ResolveType(c.Args[0]);
                        if (e.IsErr) return ZR.Error;
                        if (!ElementOk(e, c.Args[0])) return ZR.Error;
                        return new ZR { T = ZType.TypeVal(ZType.ListOf(e)) };
                    }
                case "std.AutoHashMap":
                    {
                        if (c.Args.Count != 2) { ErrS("expected " + Args(2) + ", found " + c.Args.Count, c); return ZR.Error; }
                        var k = ResolveType(c.Args[0]); var v = ResolveType(c.Args[1]);
                        if (k.IsErr || v.IsErr) return ZR.Error;
                        if (k.K == ZK.Slice || k.K == ZK.StrLit) { ErrS("std.hash.autoHash does not allow slices here (" + k + "); use std.StringHashMap for string keys", c.Args[0]); return ZR.Error; }
                        if (k.IsFloatLike) { ErrS("unable to hash type " + Q(k), c.Args[0]); return ZR.Error; }
                        if (!KeyOk(k, c.Args[0]) || (v.K != ZK.Void && !ElementOk(v, c.Args[1]))) return ZR.Error;
                        return new ZR { T = ZType.TypeVal(ZType.MapOf(k, v)) };
                    }
                case "std.StringHashMap":
                    {
                        if (c.Args.Count != 1) { ErrS("expected " + Args(1) + ", found " + c.Args.Count, c); return ZR.Error; }
                        var v = ResolveType(c.Args[0]);
                        if (v.IsErr) return ZR.Error;
                        if (v.K != ZK.Void && !ElementOk(v, c.Args[0])) return ZR.Error;
                        return new ZR { T = ZType.TypeVal(ZType.MapOf(ZType.Str, v)) };
                    }
            }
            foreach (var a in c.Args) Expr(a, null);
            NotSupported(special + " is", c);
            return ZR.Error;
        }

        bool ElementOk(ZType e, ZExpr at)
        {
            switch (e.K)
            {
                case ZK.Int: case ZK.Float: case ZK.Bool: case ZK.Game: case ZK.Struct: case ZK.Optional: case ZK.List: case ZK.Map: case ZK.Array: case ZK.Tuple: return true;
                case ZK.Slice: if (e.IsString) return true; break;
            }
            NotSupported("collections of " + Q(e) + " are", at);
            return false;
        }

        bool KeyOk(ZType k, ZExpr at)
        {
            switch (k.K)
            {
                case ZK.Int: case ZK.Bool: case ZK.Game: case ZK.Struct: case ZK.Tuple: return true;
            }
            NotSupported("map keys of type " + Q(k) + " are", at);
            return false;
        }

        // ------------------------------------------------------------------ builtins
        ZR Builtin(ZBuiltinCall b, ZType expected)
        {
            var nameSpan = new Span(b.Start, b.NameEnd);
            bool Count(int n)
            {
                if (b.Args.Count == n) return true;
                foreach (var a in b.Args) Expr(a, null);
                ErrA("expected " + n + " argument" + (n == 1 ? "" : "s") + ", found " + b.Args.Count, b);
                return false;
            }
            switch (b.Name)
            {
                case "@import":
                    {
                        if (!Count(1)) return ZR.Error;
                        if (!(b.Args[0] is ZStrLit s) || s.Multi) { ErrA("@import operand must be a string literal", b.Args[0]); return ZR.Error; }
                        var path = s.Value;
                        if (path == "std") return new ZR { T = ZType.Ns("std") };
                        if (path == "farm") return new ZR { T = ZType.Ns("farm") };
                        // a file of another language: @import("utils"), @import("Utils.java"), @import("bot.py") (or .zig)
                        {
                            var fmod = path.IndexOf('.') > 0 ? path.Substring(0, path.LastIndexOf('.')) : path;
                            if (others != null && fmod != fileName && others.ContainsKey(fmod) && ForeignModules.IsForeign(options, fmod, "zig"))
                            {
                                var fom = Other(fmod);
                                if (fom?.Error != null) { ErrS("cannot use " + fmod + ": " + fom.Error, b.Args[0]); return ZR.Error; }
                                return new ZR { T = ZType.Ns("file:" + fmod) };
                            }
                        }
                        if (path.EndsWith(".zig"))
                        {
                            var mod = path.Substring(0, path.Length - 4);
                            if (mod == fileName) { NotSupported("importing a file from itself is", b); return ZR.Error; }
                            if (others != null && others.ContainsKey(mod)) { Other(mod); return new ZR { T = ZType.Ns("file:" + mod) }; }
                            ErrS("unable to load " + Q(path) + ": FileNotFound", b.Args[0]);
                            return ZR.Error;
                        }
                        if (path == "builtin" || path == "root") { NotSupported("@import(\"" + path + "\") is", b); return ZR.Error; }
                        ErrS("no module named " + Q(path) + " available within module 'root'", b.Args[0]);
                        return ZR.Error;
                    }
                case "@as":
                    {
                        if (!Count(2)) return ZR.Error;
                        var t = ResolveType(b.Args[0]);
                        if (t.IsErr) { Expr(b.Args[1], null); return ZR.Error; }
                        var r = Whole(b.Args[1], t);
                        var ir = Coerce(r, t, b.Args[1]);
                        var res = new ZR { Ir = ir, T = t, Known = r.Known && t.IsNumeric, IntVal = r.IntVal, FloatVal = r.FloatVal };
                        return res;
                    }
                case "@intCast":
                    {
                        if (!Count(1)) return ZR.Error;
                        var r = Expr(b.Args[0], null);
                        if (r.T.IsErr) return r;
                        if (!r.T.IsIntLike) { ErrS("expected integer type, found " + Q(r.T), b.Args[0]); return ZR.Error; }
                        if (expected == null || expected.IsErr) { if (expected == null) ErrS("@intCast must have a known result type (use @as(T, @intCast(x)))", b); return ZR.Error; }
                        var et = expected.K == ZK.Optional ? expected.Elem : expected;
                        if (!et.IsIntLike) { ErrS("expected integer type, found " + Q(et), b); return ZR.Error; }
                        if (r.Known && et.K == ZK.Int && !IntFits(et, r.IntVal)) { ErrS("type " + Q(et) + " cannot represent integer value " + Q(r.IntVal), b.Args[0]); return ZR.Error; }
                        return new ZR { Ir = new IrCast { Target = IrType.Int, Operand = r.Ir, OpSpan = nameSpan, Span = b.Span, Type = IrType.Int }, T = et, Known = r.Known, IntVal = r.IntVal, FloatVal = r.IntVal };
                    }
                case "@floatFromInt":
                    {
                        if (!Count(1)) return ZR.Error;
                        var r = Expr(b.Args[0], null);
                        if (r.T.IsErr) return r;
                        if (!r.T.IsIntLike) { ErrS("expected integer type, found " + Q(r.T), b.Args[0]); return ZR.Error; }
                        if (expected == null) { ErrS("@floatFromInt must have a known result type (use @as(f64, @floatFromInt(x)))", b); return ZR.Error; }
                        var et = expected.K == ZK.Optional ? expected.Elem : expected;
                        if (expected.IsErr) return ZR.Error;
                        if (!et.IsFloatLike) { ErrS("expected float type, found " + Q(et), b); return ZR.Error; }
                        return new ZR { Ir = new IrCast { Target = IrType.Float, Operand = r.Ir, OpSpan = nameSpan, Span = b.Span, Type = IrType.Float }, T = et };
                    }
                case "@intFromFloat":
                case "@trunc":
                    {
                        if (!Count(1)) return ZR.Error;
                        var r = Expr(b.Args[0], b.Name == "@trunc" && expected != null && expected.IsFloatLike ? expected : null);
                        if (r.T.IsErr) return r;
                        if (!r.T.IsFloatLike) { ErrS("expected float type, found " + Q(r.T), b.Args[0]); return ZR.Error; }
                        var et = expected != null && expected.K == ZK.Optional ? expected.Elem : expected;
                        if (et == null)
                        {
                            if (b.Name == "@trunc") return new ZR { Ir = new IrCast { Target = IrType.Int, Operand = r.Ir, OpSpan = nameSpan, Span = b.Span, Type = IrType.Float }, T = r.T.K == ZK.CFloat ? ZType.F64 : r.T };
                            ErrS("@intFromFloat must have a known result type (use @as(i32, @intFromFloat(x)))", b);
                            return ZR.Error;
                        }
                        if (et.IsErr) return ZR.Error;
                        if (et.IsFloatLike && b.Name == "@trunc") return new ZR { Ir = new IrCast { Target = IrType.Int, Operand = r.Ir, OpSpan = nameSpan, Span = b.Span, Type = IrType.Float }, T = et };
                        if (!et.IsIntLike) { ErrS("expected integer type, found " + Q(et), b); return ZR.Error; }
                        return new ZR { Ir = new IrCast { Target = IrType.Int, Operand = r.Ir, OpSpan = nameSpan, Span = b.Span, Type = IrType.Int }, T = et };
                    }
                case "@floatCast":
                    {
                        if (!Count(1)) return ZR.Error;
                        var r = Expr(b.Args[0], null);
                        if (r.T.IsErr) return r;
                        if (!r.T.IsFloatLike) { ErrS("expected float type, found " + Q(r.T), b.Args[0]); return ZR.Error; }
                        if (expected == null) { ErrS("@floatCast must have a known result type", b); return ZR.Error; }
                        return new ZR { Ir = r.Ir, T = expected.IsFloatLike ? expected : r.T };
                    }
                case "@divTrunc": case "@divFloor": case "@divExact": case "@rem": case "@mod":
                    {
                        if (!Count(2)) return ZR.Error;
                        var l = Expr(b.Args[0], null); var r = Expr(b.Args[1], null);
                        if (l.T.IsErr || r.T.IsErr) return ZR.Error;
                        if (!l.T.IsNumeric || !r.T.IsNumeric) { ErrS("invalid operands to binary expression: " + Q(l.T) + " and " + Q(r.T), b); return ZR.Error; }
                        var peer = Peer(l, r);
                        if (peer == null) { ErrS("incompatible types: " + Q(l.T) + " and " + Q(r.T), b); return ZR.Error; }
                        var op = b.Name == "@divTrunc" || b.Name == "@divExact" ? IrBinOp.IntDivTrunc : b.Name == "@divFloor" ? IrBinOp.IntDivFloor : b.Name == "@rem" ? IrBinOp.ModTrunc : IrBinOp.ModFloor;
                        var bin = new IrBinary { Op = op, Left = Coerce(l, peer, b.Args[0]), Right = Coerce(r, peer, b.Args[1]), OpSpan = nameSpan, Span = b.Span, Type = peer.Ir() };
                        var res = new ZR { Ir = bin, T = peer };
                        Fold(res, op, l, r, b);
                        return res;
                    }
                case "@min":
                case "@max":
                    {
                        if (b.Args.Count < 2) { foreach (var a in b.Args) Expr(a, null); ErrA("expected at least 2 arguments, found " + b.Args.Count, b); return ZR.Error; }
                        var rs = b.Args.Select(a => Expr(a, null)).ToList();
                        if (rs.Any(x => x.T.IsErr)) return ZR.Error;
                        ZR acc = rs[0];
                        ZType peer = rs[0].T;
                        for (int i = 1; i < rs.Count; i++)
                        {
                            peer = Peer(new ZR { T = peer }, rs[i]);
                            if (peer == null) { ErrS("incompatible types: " + Q(rs[0].T) + " and " + Q(rs[i].T), b); return ZR.Error; }
                        }
                        if (!peer.IsNumeric) { ErrS("expected number, found " + Q(peer), b); return ZR.Error; }
                        var call = IrCall.ToBuiltin(b.Name == "@min" ? "min" : "max");
                        for (int i = 0; i < rs.Count; i++) call.Args.Add(Coerce(rs[i], peer, b.Args[i]));
                        call.NameSpan = nameSpan; call.Span = b.Span; call.Type = peer.Ir();
                        return new ZR { Ir = call, T = peer };
                    }
                case "@abs":
                    {
                        if (!Count(1)) return ZR.Error;
                        var r = Expr(b.Args[0], null);
                        if (r.T.IsErr) return r;
                        if (!r.T.IsNumeric) { ErrS("expected integer, float, or vector of either integers or floats, found " + Q(r.T), b.Args[0]); return ZR.Error; }
                        var t = r.T;
                        if (t.K == ZK.Int && t.Signed) t = t.Name == "isize" ? ZType.Usize : ZType.IntT("u" + t.Bits);
                        var call = IrCall.ToBuiltin("abs", r.Ir);
                        call.NameSpan = nameSpan; call.Span = b.Span; call.Type = t.Ir();
                        return new ZR { Ir = call, T = t };
                    }
                case "@sqrt": case "@floor": case "@ceil": case "@round":
                    {
                        if (!Count(1)) return ZR.Error;
                        var ft = expected != null && expected.IsFloatLike ? expected : null;
                        var r = Expr(b.Args[0], ft ?? ZType.F64);
                        if (r.T.IsErr) return r;
                        var argT = r.T;
                        if (!argT.IsFloatLike)
                        {
                            if (argT.K == ZK.CInt) argT = ZType.CFloat;
                            else { ErrS("expected float type, found " + Q(r.T), b.Args[0]); return ZR.Error; }
                        }
                        var op = b.Name == "@sqrt" ? IrOp.Sqrt : b.Name == "@floor" ? IrOp.Floor : b.Name == "@ceil" ? IrOp.Ceil : IrOp.Round;
                        ZType t = argT.K == ZK.CFloat ? (ft ?? ZType.F64) : argT;
                        if (b.Name != "@sqrt" && expected != null && expected.IsIntLike) t = expected;
                        else if (b.Name == "@sqrt" && expected != null && expected.IsIntLike) { ErrS("expected float type, found " + Q(expected), b); return ZR.Error; }
                        var arg = r.Ir;
                        if (arg != null && r.T.IsIntLike) arg.Type = IrType.Float;
                        return new ZR { Ir = new IrOpCall(op, arg) { NameSpan = nameSpan, Span = b.Span, Type = t.IsIntLike ? IrType.Int : IrType.Float }, T = t };
                    }
                case "@splat":
                    {
                        if (!Count(1)) return ZR.Error;
                        if (expected == null) { ErrS("@splat must have a known result type", b); return ZR.Error; }
                        if (expected.IsErr) return ZR.Error;
                        if (expected.K != ZK.Array) { ErrS("expected array or vector type, found " + Q(expected), b); return ZR.Error; }
                        var ir = ArrayFill(expected, b.Args[0], b);
                        if (ir == null) return ZR.Error;
                        return new ZR { Ir = ir, T = expected };
                    }
                case "@intFromEnum": case "@enumFromInt": case "@backingInt": case "@fromBackingInt": case "@intFromBool": case "@tagName": case "@This": case "@TypeOf":
                case "@typeInfo": case "@typeName": case "@compileError": case "@compileLog": case "@panic": case "@field": case "@hasDecl": case "@hasField": case "@sizeOf":
                case "@bitCast": case "@ptrCast": case "@truncate": case "@embedFile": case "@src": case "@memset": case "@memcpy": case "@divCeil": case "@exp": case "@log":
                case "@sin": case "@cos": case "@tan": case "@exp2": case "@log2": case "@log10": case "@mulAdd": case "@reduce": case "@Vector": case "@errorName": case "@setEvalBranchQuota":
                case "@clz": case "@ctz": case "@popCount": case "@byteSwap": case "@bitReverse": case "@addWithOverflow": case "@subWithOverflow": case "@mulWithOverflow": case "@shlExact": case "@shrExact": case "@shlWithOverflow":
                case "@alignOf": case "@offsetOf": case "@call": case "@inComptime": case "@trap": case "@breakpoint": case "@branchHint": case "@constCast": case "@unionInit":
                    foreach (var a in b.Args) { try { Expr(a, null); } catch (ZCheckAbort) { throw; } }
                    NotSupported(b.Name + " is", new ZSpanNode(b.Start, b.NameEnd));
                    return ZR.Error;
            }
            if (BuiltinNames.Contains(b.Name)) { NotSupported(b.Name + " is", new ZSpanNode(b.Start, b.NameEnd)); return ZR.Error; }
            ErrA("invalid builtin function: " + Q(b.Name), b.Start, b.NameEnd);
            return ZR.Error;
        }

        static readonly HashSet<string> BuiltinNames = new HashSet<string>
        {
            "@addrSpaceCast", "@alignCast", "@atomicLoad", "@atomicRmw", "@atomicStore", "@bitOffsetOf", "@bitSizeOf", "@disableInstrumentation",
            "@disableIntrinsics", "@cmpxchgStrong", "@cmpxchgWeak", "@cVaArg", "@cVaCopy", "@cVaEnd", "@cVaStart", "@errorReturnTrace", "@intFromError",
            "@errorCast", "@export", "@extern", "@fieldParentPtr", "@FieldType", "@frame", "@Frame", "@frameAddress", "@errorFromInt", "@ptrFromInt",
            "@memmove", "@wasmMemorySize", "@wasmMemoryGrow", "@prefetch", "@intFromPtr", "@returnAddress", "@select", "@setFloatMode",
            "@setRuntimeSafety", "@shuffle", "@EnumLiteral", "@Int", "@Tuple", "@Pointer", "@Fn", "@Struct", "@Union", "@Enum", "@SpirvType",
            "@volatileCast", "@workItemId", "@workGroupSize", "@workGroupId",
        };

        /// <summary>@splat / undefined for an array type: ArrayNew(n, d) or ArrayNew2(w, h, d).</summary>
        IrExpr ArrayFill(ZType t, ZExpr value, ZNode at)
        {
            var span = at.Span;
            IrLiteral Len(long n) { var l = IrLiteral.OfInt(n); l.Span = span; return l; }
            if (t.Elem.K == ZK.Array)
            {
                var inner = t.Elem;
                IrExpr d;
                if (value == null) d = DefaultValue(inner.Elem, span);
                else if (Unparen(value) is ZBuiltinCall ib && ib.Name == "@splat" && ib.Args.Count == 1)
                {
                    var r = Expr(ib.Args[0], inner.Elem);
                    d = Coerce(r, inner.Elem, ib.Args[0]);
                }
                else { NotSupported("this initial value of a 2D array is (use @splat(@splat(x)))", value); return null; }
                if (inner.Elem.K == ZK.Array) { NotSupported("arrays with more than 2 dimensions are", at); return null; }
                return new IrOpCall(IrOp.ArrayNew2, Len(t.Len), Len(inner.Len), d) { Span = span, NameSpan = span, Type = t.Ir() };
            }
            IrExpr dv;
            if (value == null) dv = DefaultValue(t.Elem, span);
            else
            {
                var r = Expr(value, t.Elem);
                if (r.T.IsErr) return null;
                dv = Coerce(r, t.Elem, value);
            }
            return new IrOpCall(IrOp.ArrayNew, Len(t.Len), dv) { Span = span, NameSpan = span, Type = t.Ir() };
        }

        static IrExpr DefaultValue(ZType t, Span span)
        {
            IrLiteral l;
            if (t.K == ZK.Bool) l = IrLiteral.OfBool(false);
            else if (t.IsIntLike) l = IrLiteral.OfInt(0);
            else if (t.IsFloatLike) l = new IrLiteral { Kind = IrLitKind.Int, Int = 0, Type = IrType.Float };
            else l = IrLiteral.Null();
            l.Span = span;
            l.Synthetic = true;
            return l;
        }

        // ------------------------------------------------------------------ print
        ZR Print(string builtin, ZExpr fmtArg, ZExpr argsArg, ZCall call, Span nameSpan)
        {
            var fu = Unparen(fmtArg);
            string fmt = null;
            if (fu is ZStrLit sl && !sl.Multi) fmt = sl.Value;
            else
            {
                var fr = Expr(fmtArg, ZType.Str);
                if (fr.T.IsErr) return ZR.Error;
                if (fr.Known && fr.StrVal != null) fmt = fr.StrVal;
                else { ErrS("unable to resolve comptime value (the format of " + (builtin == "print" ? "std.debug.print" : "farm.quickPrint") + " must be a string literal)", fmtArg); return ZR.Error; }
            }
            var au = Unparen(argsArg);
            List<ZExpr> items;
            if (au is ZInit ai && ai.Type == null)
            {
                if (ai.Fields != null) { NotSupported("named format arguments are", argsArg); return ZR.Error; }
                items = ai.Items;
            }
            else
            {
                var r = Expr(argsArg, null);
                if (r.T.IsErr) return ZR.Error;
                ErrS("expected tuple or struct argument, found " + Q(r.T), argsArg);
                return ZR.Error;
            }
            var fmtSource = fmtArg is ZStrLit ? fmtArg.Span : fmtArg.Span;
            // parse the format string
            var pieces = new List<(string Text, string Spec)>();
            var sb = new StringBuilder();
            for (int i = 0; i < fmt.Length; i++)
            {
                char ch = fmt[i];
                if (ch == '{')
                {
                    if (i + 1 < fmt.Length && fmt[i + 1] == '{') { sb.Append('{'); i++; continue; }
                    int j = fmt.IndexOf('}', i + 1);
                    if (j < 0) { ErrS("missing closing }", fmtArg); return ZR.Error; }
                    if (sb.Length > 0) { pieces.Add((sb.ToString(), null)); sb.Clear(); }
                    var content = fmt.Substring(i + 1, j - i - 1);
                    var colon = content.IndexOf(':');
                    if (colon >= 0) { NotSupported("format options like {d:5} or {d:.2} are", fmtArg); return ZR.Error; }
                    if (content.Length > 0 && (char.IsDigit(content[0]) || content[0] == '[')) { NotSupported("positional and named placeholders ({0}, {[name]}) are", fmtArg); return ZR.Error; }
                    var spec = content;
                    var core = spec.StartsWith("?") ? spec.Substring(1) : spec;
                    if (!(core == "" || core == "d" || core == "s" || core == "any"))
                    {
                        NotSupported("the format specifier {" + spec + "} is (use {}, {d}, {s} or {any})", fmtArg);
                        return ZR.Error;
                    }
                    pieces.Add((null, spec));
                    i = j;
                    continue;
                }
                if (ch == '}')
                {
                    if (i + 1 < fmt.Length && fmt[i + 1] == '}') { sb.Append('}'); i++; continue; }
                    ErrS("missing opening {", fmtArg);
                    return ZR.Error;
                }
                sb.Append(ch);
            }
            if (sb.Length > 0) pieces.Add((sb.ToString(), null));
            int placeholders = pieces.Count(p => p.Spec != null);
            var fmtShown = fmt.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t");
            if (placeholders > items.Count) { foreach (var x in items) Expr(x, null); ErrS("too few arguments", argsArg); return ZR.Error; }
            if (placeholders < items.Count)
            {
                foreach (var x in items) Expr(x, null);
                int extra = items.Count - placeholders;
                ErrS((extra == 1 ? "unused argument in '" : extra + " unused arguments in '") + fmtShown + "'", items[placeholders]);
                return ZR.Error;
            }
            // trailing newline: one game print = one line
            var last = pieces.Count > 0 ? pieces[pieces.Count - 1] : (null, null);
            if (last.Text != null && last.Text.EndsWith("\n"))
            {
                var t = last.Text.Substring(0, last.Text.Length - 1);
                pieces.RemoveAt(pieces.Count - 1);
                if (t.Length > 0) pieces.Add((t, null));
            }
            foreach (var p in pieces)
                if (p.Text != null && (p.Text.IndexOf('\n') >= 0 || p.Text.IndexOf('\r') >= 0))
                {
                    ErrS("a line break inside a print is not supported in JavaFarmer (each print is one line in the game; end the format with \\n)", fmtArg);
                    return ZR.Error;
                }
            // the values
            var values = new List<IrExpr>();
            int k = 0;
            bool ok = true;
            var parts = new List<IrExpr>();
            foreach (var p in pieces)
            {
                if (p.Spec == null)
                {
                    var lit = IrLiteral.OfString(p.Text);
                    lit.Span = fmtSource;
                    parts.Add(lit);
                    continue;
                }
                var item = items[k++];
                var r = Expr(item, null);
                if (r.T.IsErr) { ok = false; continue; }
                if (RejectOptOp(r, item)) { ok = false; continue; }
                if (!CheckSpec(p.Spec, r.T, item)) { ok = false; continue; }
                parts.Add(r.Ir);
            }
            if (!ok) return ZR.Error;
            var callIr = IrCall.ToBuiltin(builtin);
            callIr.NameSpan = nameSpan;
            callIr.Span = call.Span;
            callIr.Type = IrType.Void;
            if (parts.Count == 1 && pieces.Count == 1 && pieces[0].Spec != null) callIr.Args.Add(parts[0]);
            else if (parts.Count == 1) callIr.Args.Add(parts[0]);
            else if (parts.Count > 1)
            {
                IrExpr acc = parts[0];
                for (int i = 1; i < parts.Count; i++)
                    acc = new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = parts[i], Synthetic = true, Type = IrType.String, Span = call.Span, OpSpan = fmtSource };
                callIr.Args.Add(acc);
            }
            return new ZR { Ir = callIr, T = ZType.Void };
        }

        bool CheckSpec(string spec, ZType t, ZExpr at)
        {
            if (t.K == ZK.Optional)
            {
                if (spec.StartsWith("?")) return CheckSpec(spec.Substring(1), t.Elem, at);
                if (spec == "any") return CheckSpec("any", t.Elem, at);
                if (spec == "") { ErrS("cannot print optional without a specifier (i.e. {?} or {any})", at); return false; }
                ErrS("invalid format string " + Q(spec) + " for type " + Q(t), at);
                return false;
            }
            if (spec.StartsWith("?")) { ErrS("invalid format string " + Q(spec) + " for type " + Q(t), at); return false; }
            switch (t.K)
            {
                case ZK.Int: case ZK.CInt: case ZK.Float: case ZK.CFloat:
                    if (spec == "" || spec == "d" || spec == "any") return true;
                    break;
                case ZK.Bool: case ZK.Null: case ZK.Void:
                    if (spec == "" || spec == "any") return true;
                    break;
                case ZK.Game: case ZK.EnumLit:
                    if (t.K == ZK.EnumLit) { NotSupported("printing an untyped enum literal is", at); return false; }
                    if (spec == "" || spec == "any") return true;
                    if (spec == "d") { NotSupported("printing an enum with {d} (its number) is", at); return false; }
                    break;
                case ZK.Struct: case ZK.Tuple:
                    if (spec == "" || spec == "any") return true;
                    break;
                case ZK.StrLit:
                case ZK.Slice:
                    if (t.IsString)
                    {
                        if (spec == "s") return true;
                        if (spec == "any") { ErrS("{any} prints the bytes of a string in Zig: use {s}", at); return false; }
                        if (spec == "") { ErrS("cannot format slice without a specifier (i.e. {s}, {x}, {b64}, or {any})", at); return false; }
                        break;
                    }
                    if (spec == "any") return true;
                    if (spec == "") { ErrS("cannot format slice without a specifier (i.e. {s}, {x}, {b64}, or {any})", at); return false; }
                    break;
                case ZK.Array:
                    if (spec == "any") return true;
                    if (spec == "") { ErrS("cannot format array without a specifier (i.e. {s} or {any})", at); return false; }
                    break;
                case ZK.List:
                    NotSupported("printing a std.ArrayList is (print list.items with {any});", at);
                    return false;
                case ZK.Map:
                    NotSupported("printing a std.AutoHashMap is", at);
                    return false;
                default:
                    NotSupported("printing a value of type " + Q(t) + " is", at);
                    return false;
            }
            ErrS("invalid format string " + Q(spec) + " for type " + Q(t), at);
            return false;
        }
    }
}
