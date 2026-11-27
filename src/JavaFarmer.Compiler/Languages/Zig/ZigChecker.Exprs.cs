using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal sealed partial class ZigChecker
    {
        /// <summary>The expression currently checked as a whole value (declaration init, assignment value, return, argument).</summary>
        ZExpr wholeValue;

        static ZExpr Unparen(ZExpr e) { while (e is ZGrouped g) e = g.Inner; return e; }

        ZR Expr(ZExpr e, ZType expected, bool statement = false, bool discard = false)
        {
            var r = ExprCore(e, expected, statement, discard) ?? ZR.Error;
            if (r.T == null) r.T = ZType.Err;
            if (r.Ir != null)
            {
                if (r.Ir.Span.IsNone) r.Ir.Span = e.Span;
                if (!r.Ir.Type.IsKnown && r.T.K != ZK.EnumLit) r.Ir.Type = r.T.Ir();
            }
            return r;
        }

        ZR Whole(ZExpr e, ZType expected)
        {
            var old = wholeValue;
            wholeValue = Unparen(e);
            try { return OptValue(Expr(e, expected), e); }
            finally { wholeValue = old; }
        }

        bool IsWhole(ZExpr e) => wholeValue != null && ReferenceEquals(Unparen(wholeValue), e);

        ZR ExprCore(ZExpr e, ZType expected, bool statement, bool discard)
        {
            switch (e)
            {
                case ZIntLit il:
                    if (il.Text.Length > 1 && il.Text[0] == '0' && char.IsDigit(il.Text[1])) { ErrA("number " + Q(il.Text) + " has leading zero", il); return ZR.Error; }
                    return new ZR { Ir = IrLiteral.OfInt(il.Value), T = ZType.CInt, Known = true, IntVal = il.Value, FloatVal = il.Value };
                case ZFloatLit fl:
                    return new ZR { Ir = IrLiteral.OfFloat(fl.Value), T = ZType.CFloat, Known = true, FloatVal = fl.Value };
                case ZStrLit sl:
                    if (sl.Multi) { NotSupported("multiline string literals are", sl); return ZR.Error; }
                    return new ZR { Ir = IrLiteral.OfString(sl.Value), T = ZType.StrLitT(Encoding.UTF8.GetByteCount(sl.Value)), Known = true, StrVal = sl.Value };
                case ZCharLit cl:
                    NotSupported("character literals are", cl);
                    return ZR.Error;
                case ZIdent id: return Ident(id);
                case ZEnumLit el:
                    {
                        var et = expected != null && expected.K == ZK.Optional ? expected.Elem : expected;
                        if (et != null && et.K == ZK.List && el.Name == "empty") return new ZR { Ir = new IrListLit { Span = el.Span, Type = et.Ir() }, T = et };
                        if (et != null && et.K == ZK.Game) return new ZR { Ir = new IrConst(GameApi.CategoryOfGameType(et.Name), el.Name) { Span = el.Span }, T = et };
                        if (et != null && !et.IsErr && et.K != ZK.Type) { ErrS(Mismatch(new ZType { K = ZK.EnumLit, Name = el.Name }, et), el); return ZR.Error; }
                        return new ZR { Ir = new IrConst { Category = null, Name = el.Name, Span = el.Span }, T = new ZType { K = ZK.EnumLit, Name = el.Name } };
                    }
                case ZGrouped g:
                    {
                        var r = Expr(g.Inner, expected, statement, discard);
                        if (r.Ir != null && r.Ir.ParenSpan.IsNone) r.Ir.ParenSpan = g.Span;
                        return r;
                    }
                case ZField f: return Field(f, expected);
                case ZDeref d:
                    {
                        var r = Expr(d.Target, null);
                        if (r.T.IsErr) return r;
                        if (r.T.K == ZK.Ptr)
                        {
                            if (r.Special == "key_ptr" || r.Special == "value_ptr" || r.Special == "ptrcap") return new ZR { Ir = r.Ir, T = r.T.Elem, Special = r.Special };
                            return new ZR { Ir = r.Ir, T = r.T.Elem, Sym = r.Sym };
                        }
                        ErrS("cannot dereference non-pointer type " + Q(r.T), d);
                        return ZR.Error;
                    }
                case ZUnwrap u:
                    {
                        var r = Expr(u.Target, expected != null ? ZType.Opt(expected) : null);
                        if (r.T.IsErr) return r;
                        if (r.T.K != ZK.Optional) { ErrS("expected optional type, found " + Q(r.T), u.Target); return ZR.Error; }
                        var t = r.T.Elem;
                        if (r.Ir is IrOpCall oc && oc.Op == IrOp.MapGetOrNull)
                            return new ZR { Ir = new IrIndex { Target = oc.Args[0], Index = oc.Args[1], Span = u.Span, Type = t.Ir() }, T = t };
                        if (r.Special == "find") { NotSupported("using the index std.mem.findScalar returns is", u); return ZR.Error; }
                        var ir = r.Ir;
                        if (ir != null) ir.Type = t.Ir();
                        return new ZR { Ir = ir, T = t, Sym = r.Sym };
                    }
                case ZIndex ix:
                    {
                        var b = Expr(ix.Target, null);
                        if (b.T.IsErr) { Expr(ix.Index, ZType.Usize); return ZR.Error; }
                        return Index(ix, b) ?? ZR.Error;
                    }
                case ZSliceExpr se:
                    NotSupported("slicing (a[x..y]) is", se);
                    return ZR.Error;
                case ZCall c: return Call(c, expected, discard);
                case ZBuiltinCall bc: return Builtin(bc, expected);
                case ZBinary b: return Binary(b, expected);
                case ZUnary u: return Unary(u, expected);
                case ZIfExpr ie: return IfExpr(ie, expected);
                case ZSwitchExpr swe:
                    NotSupported("a switch expression is only supported as the whole value of a declaration with a type, an assignment or a return; here it is", new ZSpanNode(swe.Start, swe.Switch.KwEnd));
                    return ZR.Error;
                case ZCatchExpr ce: return Catch(ce, expected);
                case ZInit init: return Init(init, expected);
                case ZControlExpr ctl:
                    if (ctl.Kw == "unreachable") { NotSupported("unreachable is", ctl); return ZR.Error; }
                    NotSupported("'" + ctl.Kw + "' as a value is", ctl);
                    return ZR.Error;
                case ZBlockValue bv:
                    if (bv.Block.Stmts.Count == 0 && bv.Block.Label == null) return new ZR { T = ZType.Void };
                    NotSupported("block expressions are", bv);
                    return ZR.Error;
                case ZUnsupportedExpr ue:
                    NotSupported(ue.What, ue);
                    return ZR.Error;
                case ZOptionalType _: case ZSliceType _: case ZArrayType _: case ZPointerType _: case ZTupleType _: case ZFnType _: case ZErrorUnionType _:
                    {
                        var t = ResolveType(e);
                        return t.IsErr ? ZR.Error : new ZR { T = ZType.TypeVal(t) };
                    }
            }
            NotSupported("this expression is", e);
            return ZR.Error;
        }

        // ------------------------------------------------------------------ names
        ZR Ident(ZIdent id)
        {
            if (id.Quoted) { NotSupported("@\"...\" identifiers are", id); return ZR.Error; }
            switch (id.Name)
            {
                case "_": ErrA("'_' used as an identifier without @\"_\"", id); return ZR.Error;
                case "true": return new ZR { Ir = IrLiteral.OfBool(true), T = ZType.Bool, Known = true, BoolVal = true };
                case "false": return new ZR { Ir = IrLiteral.OfBool(false), T = ZType.Bool, Known = true, BoolVal = false };
                case "null": return new ZR { Ir = IrLiteral.Null(), T = ZType.Null };
                case "undefined": return new ZR { T = ZType.Undefined };
            }
            var s = Lookup(id.Name);
            if (s == null)
            {
                var pt = PrimitiveType(id.Name);
                if (pt != null) return new ZR { T = ZType.TypeVal(pt) };
                if (IsPrimitiveName(id.Name)) { NotSupported("type " + Q(id.Name) + " is", id); return ZR.Error; }
                ErrA("use of undeclared identifier " + Q(id.Name), id);
                return ZR.Error;
            }
            s.Used = true;
            switch (s.Kind)
            {
                case ZSymKind.Alias:
                    {
                        if (!aliasValues.TryGetValue(s, out var a)) return ZR.Error;
                        return new ZR { T = a.T, Api = a.Api, Special = a.Special, Sym = a.Sym, Ir = null };
                    }
                case ZSymKind.Fn:
                    return new ZR { T = s.Type ?? ZType.Err, Sym = s };
            }
            if (s.Type == null || s.Type.IsErr) return ZR.Error;
            if (entryInfo.ContainsKey(s)) return new ZR { T = s.Type, Sym = s };
            if (ptrCaptures.TryGetValue(s, out var pv)) return new ZR { Ir = new IrName(pv) { Span = id.Span }, T = s.Type, Sym = s, Special = "ptrcap" };
            IrExpr ir = s.AliasOf != null ? s.AliasOf() : new IrName(s.Ir) { Span = id.Span };
            if (s.AliasOf != null) ir.Span = id.Span;
            ir.Type = s.Type.Ir();
            var r = new ZR { Ir = ir, T = s.Type, Sym = s };
            if (s.HasValue) { r.Known = true; r.IntVal = s.IntValue; r.FloatVal = s.FloatValue; r.StrVal = s.StrValue; }
            return r;
        }

        /// <summary>The value of a container-level alias initialiser (@import, namespaces, types); null when it is not one.</summary>
        ZR TryNamespaceValue(ZExpr e)
        {
            var u = Unparen(e);
            bool shape = false;
            if (u is ZBuiltinCall bc && bc.Name == "@import") shape = true;
            else
            {
                var root = u;
                if (root is ZCall rc) root = rc.Callee;
                while (root is ZField rf) root = rf.Target;
                if (root is ZIdent rid && (rid.Name == "std" || rid.Name == "farm" || (container.TryGetValue(rid.Name, out var s) && s.Kind == ZSymKind.Alias) || (container.TryGetValue(rid.Name, out var s2) && s2.Decl is ZVarDecl vd2 && vd2.IsConst && vd2.Type == null && TryShapeAlias(vd2.Init))))
                    shape = !(u is ZIdent);
                if (u is ZIdent uid && container.TryGetValue(uid.Name, out var s3) && (s3.Kind == ZSymKind.Fn)) shape = true;
                if (u is ZIdent uid2 && container.TryGetValue(uid2.Name, out var s4) && s4.Decl is ZVarDecl vd4 && vd4.IsConst && vd4.Type == null && TryShapeAlias(vd4.Init)) shape = true;
                if (PrimitiveType((u as ZIdent)?.Name ?? "") != null) shape = true;
                if (u is ZOptionalType || u is ZSliceType || u is ZArrayType || u is ZTupleType) shape = true;
            }
            if (!shape) return null;
            var r = Expr(e, null);
            if (r.T.K == ZK.Namespace || r.T.K == ZK.Type || (r.Special != null && r.Ir == null) || r.Api != null || (r.T.K == ZK.Fn && r.Ir == null) || r.T.K == ZK.Allocator) return r;
            if (r.T.IsErr) return r;
            return null;
        }

        static bool TryShapeAlias(ZExpr e)
        {
            var u = Unparen(e);
            if (u is ZBuiltinCall bc && bc.Name == "@import") return true;
            var root = u;
            if (root is ZCall rc) root = rc.Callee;
            while (root is ZField rf) root = rf.Target;
            return root is ZIdent rid && (rid.Name == "std" || rid.Name == "farm") && !(u is ZIdent);
        }

        static readonly HashSet<string> GameTypes = new HashSet<string> { "Entity", "Item", "Ground", "Unlock", "Hat", "Leaderboard", "Direction" };

        ZR NsMember(string path, ZField f)
        {
            var n = f.Name;
            var nameSpan = new Span(f.NameStart, f.NameEnd);
            switch (path)
            {
                case "std":
                    switch (n)
                    {
                        case "debug": case "mem": case "math": case "meta": case "heap": return new ZR { T = ZType.Ns("std." + n) };
                        case "ArrayList": case "AutoHashMap": case "StringHashMap": return new ZR { T = ZType.TypeOfType, Special = "std." + n };
                        case "ArrayListUnmanaged": case "HashMap": case "AutoArrayHashMap": case "BufSet": case "EnumMap":
                            NotSupported("std." + n + " is", f); return ZR.Error;
                    }
                    NotSupported("std." + n + " is", f);
                    return ZR.Error;
                case "std.debug":
                    if (n == "print") return new ZR { T = ZType.TypeOfType, Special = "print" };
                    NotSupported("std.debug." + n + " is", f); return ZR.Error;
                case "std.mem":
                    if (n == "eql") return new ZR { T = ZType.TypeOfType, Special = "mem.eql" };
                    if (n == "findScalar" || n == "indexOfScalar") return new ZR { T = ZType.TypeOfType, Special = "mem.find" };
                    if (n == "Allocator") return new ZR { T = ZType.TypeVal(ZType.Alloc) };
                    NotSupported("std.mem." + n + " is", f); return ZR.Error;
                case "std.math":
                    if (n == "pow") return new ZR { T = ZType.TypeOfType, Special = "math.pow" };
                    NotSupported("std.math." + n + " is (use the builtins @sqrt, @floor, @abs ...)", f); return ZR.Error;
                case "std.meta":
                    if (n == "eql") return new ZR { T = ZType.TypeOfType, Special = "meta.eql" };
                    NotSupported("std.meta." + n + " is", f); return ZR.Error;
                case "std.heap":
                    if (n == "page_allocator" || n == "smp_allocator") return new ZR { T = ZType.Alloc };
                    NotSupported("std.heap." + n + " is", f); return ZR.Error;
                case "farm":
                    if (GameTypes.Contains(n)) return new ZR { T = ZType.TypeVal(ZType.GameT(n)) };
                    if (n == "Position" || n == "Companion" || n == "Drone") return new ZR { T = ZType.TypeVal(ZType.StructT(n)) };
                    if (n == "allocator") return new ZR { T = ZType.Alloc };
                    if (n == "quickPrint") return new ZR { T = ZType.TypeOfType, Special = "quick_print" };
                    {
                        var api = ZigApi.Find(n);
                        if (api != null) return new ZR { T = ZType.TypeOfType, Api = api };
                    }
                    ErrS("root source file struct 'farm' has no member named " + Q(n), f.NameStart, f.NameEnd);
                    return ZR.Error;
            }
            if (path.StartsWith("file:"))
            {
                var mod = path.Substring(5);
                var om = Other(mod);
                if (om == null) return ZR.Error;
                if (!om.Decls.TryGetValue(n, out var s)) { ErrS("root source file struct " + Q(mod) + " has no member named " + Q(n), f.NameStart, f.NameEnd); return ZR.Error; }
                if (!s.IsPub) { ErrS(Q(n) + " is not marked 'pub'", f.NameStart, f.NameEnd); return ZR.Error; }
                if (s.Kind == ZSymKind.Fn) return new ZR { T = s.Type ?? new ZType { K = ZK.Fn, Sig = s.Sig, Sym = s }, Sym = s };
                if (s.Kind == ZSymKind.Global)
                {
                    var ir = new IrName(s.Ir) { QualifierSpan = f.Target.Span, NameSpan = nameSpan, Span = f.Span, Type = s.Type?.Ir() ?? IrType.Unknown };
                    var r = new ZR { Ir = ir, T = s.Type ?? ZType.Err, Sym = s };
                    if (s.HasValue) { r.Known = true; r.IntVal = s.IntValue; r.FloatVal = s.FloatValue; r.StrVal = s.StrValue; }
                    return r;
                }
                NotSupported("using another file's imports is", f);
                return ZR.Error;
            }
            ErrS("struct " + Q(path) + " has no member named " + Q(n), f.NameStart, f.NameEnd);
            return ZR.Error;
        }

        // ------------------------------------------------------------------ fields
        ZR Field(ZField f, ZType expected)
        {
            var r = Expr(f.Target, null);
            if (r.T.IsErr) return ZR.Error;
            var t = r.T;
            var n = f.Name;
            var nameSpan = new Span(f.NameStart, f.NameEnd);
            if (t.K == ZK.Namespace) return NsMember(t.Name, f);
            if (t.K == ZK.Type)
            {
                var inner = t.Inner;
                if (inner == null) { ErrS("type " + Q(t) + " does not support field access", f); return ZR.Error; }
                if (inner.K == ZK.Game) return new ZR { Ir = new IrConst(GameApi.CategoryOfGameType(inner.Name), n) { Span = f.Span }, T = inner };
                if (inner.K == ZK.List && n == "empty") return new ZR { Ir = new IrListLit { Span = f.Span, Type = inner.Ir() }, T = inner };
                if (inner.K == ZK.Map && n == "empty") { ErrS("struct " + Q(inner) + " has no member named 'empty' (use .init(farm.allocator))", f.NameStart, f.NameEnd); return ZR.Error; }
                ErrS("struct " + Q(inner) + " has no member named " + Q(n), f.NameStart, f.NameEnd);
                return ZR.Error;
            }
            if (t.K == ZK.Ptr && (t.Elem.K == ZK.List || t.Elem.K == ZK.Array || t.Elem.K == ZK.Map)) t = t.Elem;
            switch (t.K)
            {
                case ZK.Struct:
                    if (t.Name == "Position" && (n == "x" || n == "y"))
                        return new ZR { Ir = new IrOpCall(n == "x" ? IrOp.PosX : IrOp.PosY, r.Ir) { NameSpan = nameSpan, Type = IrType.Int, Span = f.Span }, T = ZType.Usize };
                    if (t.Name == "Companion")
                    {
                        if (n == "entity") return new ZR { Ir = new IrOpCall(IrOp.CompanionEntity, r.Ir) { NameSpan = nameSpan, Type = IrType.Game("Entity"), Span = f.Span }, T = ZType.GameT("Entity") };
                        if (n == "x" || n == "y")
                        {
                            var pos = new IrOpCall(IrOp.CompanionPos, r.Ir) { NameSpan = nameSpan, Type = IrType.Game("Position"), Span = f.Span, Synthetic = true };
                            return new ZR { Ir = new IrOpCall(n == "x" ? IrOp.PosX : IrOp.PosY, pos) { NameSpan = nameSpan, Type = IrType.Int, Span = f.Span }, T = ZType.Usize };
                        }
                    }
                    if (t.Name == "Drone" && n == "id") { NotSupported("drone ids are", f); return ZR.Error; }
                    ErrS("no field named " + Q(n) + " in struct " + Q(t), f.NameStart, f.NameEnd);
                    return ZR.Error;
                case ZK.List:
                    if (n == "items") return new ZR { Ir = r.Ir, T = ZType.SliceOf(t.Elem, false), Special = "items", Sym = r.Sym };
                    if (n == "capacity") { NotSupported("list.capacity is", f); return ZR.Error; }
                    ErrS("no field named " + Q(n) + " in struct " + Q(t), f.NameStart, f.NameEnd);
                    return ZR.Error;
                case ZK.Array: case ZK.Slice: case ZK.StrLit:
                    if (n == "len")
                    {
                        var res = new ZR { Ir = new IrOpCall(IrOp.Len, r.Ir) { NameSpan = nameSpan, Type = IrType.Int, Span = f.Span }, T = ZType.Usize };
                        if (t.K == ZK.Array || t.K == ZK.StrLit) { res.Known = true; res.IntVal = t.K == ZK.StrLit && r.StrVal != null ? Encoding.UTF8.GetByteCount(r.StrVal) : t.Len; }
                        if (t.K == ZK.Array) res.Known = false;   // keep it a runtime len() for the game (same value)
                        return res;
                    }
                    if (n == "ptr") { NotSupported("raw pointers are", f); return ZR.Error; }
                    ErrS("no field named " + Q(n) + " in " + Q(t), f.NameStart, f.NameEnd);
                    return ZR.Error;
                case ZK.Entry:
                    if (r.Sym != null && entryInfo.TryGetValue(r.Sym, out var info))
                    {
                        if (n == "key_ptr") return new ZR { Ir = new IrName(info.Key) { Span = f.Span, Type = t.Key.Ir() }, T = new ZType { K = ZK.Ptr, Elem = t.Key, Const = false }, Special = "key_ptr" };
                        if (n == "value_ptr")
                        {
                            if (info.Value == null) { NotSupported("value_ptr of a set entry is", f); return ZR.Error; }
                            return new ZR { Ir = new IrName(info.Value) { Span = f.Span, Type = t.Elem.Ir() }, T = new ZType { K = ZK.Ptr, Elem = t.Elem }, Special = "value_ptr", Sym = r.Sym };
                        }
                    }
                    ErrS("no field named " + Q(n) + " in struct " + Q(t), f.NameStart, f.NameEnd);
                    return ZR.Error;
                case ZK.Optional:
                    ErrS("type " + Q(t) + " does not support field access", f);
                    return ZR.Error;
                case ZK.Tuple:
                    NotSupported("tuple fields by name are (use t[0])", f);
                    return ZR.Error;
                case ZK.Map:
                    ErrS("no field named " + Q(n) + " in struct " + Q(t), f.NameStart, f.NameEnd);
                    return ZR.Error;
            }
            ErrS("type " + Q(t) + " does not support field access", f);
            return ZR.Error;
        }

        ZR Index(ZIndex ix, ZR b)
        {
            var t = b.T;
            if (t.K == ZK.Ptr) t = t.Elem;
            if (t.K == ZK.Tuple)
            {
                var ir0 = Expr(ix.Index, ZType.Usize);
                if (!ir0.Known) { ErrS("unable to resolve comptime value", ix.Index); return null; }
                if (ir0.IntVal < 0 || ir0.IntVal >= t.Items.Count) { ErrS("index " + ir0.IntVal + " outside tuple of length " + t.Items.Count, ix.Index); return null; }
                var it = t.Items[(int)ir0.IntVal];
                return new ZR { Ir = new IrIndex { Target = b.Ir, Index = ir0.Ir, Span = ix.Span, Type = it.Ir() }, T = it };
            }
            if (t.IsString) { Expr(ix.Index, ZType.Usize); NotSupported("indexing a string is", ix); return null; }
            if (t.K == ZK.Array || t.K == ZK.Slice)
            {
                var ir = Expr(ix.Index, ZType.Usize);
                var idx = Coerce(ir, ZType.Usize, ix.Index);
                if (t.K == ZK.Array && ir.Known && (ir.IntVal < 0 || ir.IntVal >= t.Len)) ErrS("index " + ir.IntVal + " outside array of length " + t.Len, ix.Index);
                return new ZR { Ir = new IrIndex { Target = b.Ir, Index = idx, Span = ix.Span, Type = t.Elem.Ir() }, T = t.Elem };
            }
            Expr(ix.Index, ZType.Usize);
            if (t.K == ZK.List) { ErrS("element access of non-indexable type " + Q(t) + " (use list.items[i])", ix); return null; }
            ErrS("element access of non-indexable type " + Q(t), ix);
            return null;
        }

        // ------------------------------------------------------------------ operators
        static IrBinary Bin(IrBinOp op, IrExpr l, IrExpr r, ZBinary b, IrType t) =>
            new IrBinary { Op = op, Left = l, Right = r, OpSpan = new Span(b.OpStart, b.OpEnd), Type = t, Span = b.Span };

        ZR Binary(ZBinary b, ZType expected)
        {
            switch (b.Op)
            {
                case "orelse": return OrElse(b, expected);
                case "and":
                case "or":
                    {
                        var l = Expr(b.Left, ZType.Bool);
                        var r = Expr(b.Right, ZType.Bool);
                        if (l.T.IsErr || r.T.IsErr) return ZR.Error;
                        if (l.T.K != ZK.Bool) { ErrS("expected type 'bool', found " + Q(l.T), b.Left); return ZR.Error; }
                        if (r.T.K != ZK.Bool) { ErrS("expected type 'bool', found " + Q(r.T), b.Right); return ZR.Error; }
                        var res = new ZR { Ir = Bin(b.Op == "and" ? IrBinOp.And : IrBinOp.Or, l.Ir, r.Ir, b, IrType.Bool), T = ZType.Bool };
                        if (l.Known && r.Known) { res.Known = true; res.BoolVal = b.Op == "and" ? l.BoolVal && r.BoolVal : l.BoolVal || r.BoolVal; }
                        return res;
                    }
                case "==": case "!=": case "<": case "<=": case ">": case ">=":
                    return Compare(b);
                case "++":
                    {
                        var l = Expr(b.Left, null);
                        var r = Expr(b.Right, null);
                        if (l.T.IsErr || r.T.IsErr) return ZR.Error;
                        if (l.T.IsString && r.T.IsString && l.StrVal != null && r.StrVal != null)
                        {
                            var v = l.StrVal + r.StrVal;
                            return new ZR { Ir = IrLiteral.OfString(v), T = ZType.StrLitT(Encoding.UTF8.GetByteCount(v)), Known = true, StrVal = v };
                        }
                        if (l.T.IsString && r.T.IsString) { ErrS("unable to resolve comptime value (++ joins comptime-known strings only; print values with std.debug.print)", l.StrVal == null ? b.Left : b.Right); return ZR.Error; }
                        NotSupported("'++' on " + Q(l.T) + " is", b);
                        return ZR.Error;
                    }
                case "**":
                    NotSupported("array multiplication ('**', removed in Zig 0.17; use @splat or std.math.pow) is", new ZSpanNode(b.OpStart, b.OpEnd));
                    return ZR.Error;
                case "||":
                    {
                        var l = Expr(b.Left, null);
                        Expr(b.Right, null);
                        if (!l.T.IsErr) ErrS("expected error set type, found " + Q(l.T), b.Left);
                        return ZR.Error;
                    }
                case "&": case "|": case "^": case "<<": case ">>": case "<<|":
                    NotSupported("bitwise operators are", new ZSpanNode(b.OpStart, b.OpEnd));
                    return ZR.Error;
                case "+%": case "-%": case "*%": case "+|": case "-|": case "*|":
                    NotSupported("wrapping and saturating operators are", new ZSpanNode(b.OpStart, b.OpEnd));
                    return ZR.Error;
                case "+": case "-": case "*": case "/": case "%":
                    return Arith(b);
            }
            NotSupported("operator " + Q(b.Op) + " is", new ZSpanNode(b.OpStart, b.OpEnd));
            return ZR.Error;
        }

        ZR Arith(ZBinary b)
        {
            var l = Expr(b.Left, null);
            var r = Expr(b.Right, null);
            if (l.T.IsErr || r.T.IsErr) return ZR.Error;
            if (!l.T.IsNumeric || !r.T.IsNumeric)
            {
                if (l.T.IsString || r.T.IsString)
                    ErrS("invalid operands to binary expression: " + Q(l.T) + " and " + Q(r.T) + " (Zig has no string '+'; print values with std.debug.print)", b);
                else ErrS("invalid operands to binary expression: " + Q(l.T) + " and " + Q(r.T), b);
                return ZR.Error;
            }
            var peer = Peer(l, r);
            if (peer == null) { ErrS("incompatible types: " + Q(l.T) + " and " + Q(r.T), b); return ZR.Error; }
            var li = Coerce(l, peer, b.Left);
            var ri = Coerce(r, peer, b.Right);
            IrBinOp op;
            switch (b.Op)
            {
                case "+": op = IrBinOp.Add; break;
                case "-": op = IrBinOp.Sub; break;
                case "*": op = IrBinOp.Mul; break;
                default:
                    {
                        var d = DivOp(b.Op, l, r, peer, new ZSpanNode(b.OpStart, b.OpEnd));
                        if (d == null) return ZR.Error;
                        op = d.Value;
                        break;
                    }
            }
            var res = new ZR { Ir = Bin(op, li, ri, b, peer.Ir()), T = peer };
            Fold(res, op, l, r, b);
            return res;
        }

        void Fold(ZR res, IrBinOp op, ZR l, ZR r, ZNode at)
        {
            if (!l.Known || !r.Known) return;
            var t = res.T;
            if (t.IsIntLike)
            {
                long a = l.IntVal, c = r.IntVal, v;
                switch (op)
                {
                    case IrBinOp.Add: v = a + c; break;
                    case IrBinOp.Sub: v = a - c; break;
                    case IrBinOp.Mul: v = a * c; break;
                    case IrBinOp.IntDivTrunc: case IrBinOp.IntDivFloor:
                        if (c == 0) { ErrS("division by zero here causes illegal behavior", at); return; }
                        v = op == IrBinOp.IntDivTrunc ? a / c : (long)Math.Floor((double)a / c); break;
                    case IrBinOp.ModTrunc: case IrBinOp.ModFloor:
                        if (c == 0) { ErrS("division by zero here causes illegal behavior", at); return; }
                        v = op == IrBinOp.ModTrunc ? a % c : ((a % c) + c) % c; break;
                    default: return;
                }
                if (t.K == ZK.Int && !IntFits(t, v)) { ErrS("overflow of integer type " + Q(t) + " with value " + Q(v), at); return; }
                res.Known = true; res.IntVal = v; res.FloatVal = v;
            }
            else if (t.IsFloatLike)
            {
                double a = l.T.IsIntLike ? l.IntVal : l.FloatVal, c = r.T.IsIntLike ? r.IntVal : r.FloatVal, v;
                switch (op)
                {
                    case IrBinOp.Add: v = a + c; break;
                    case IrBinOp.Sub: v = a - c; break;
                    case IrBinOp.Mul: v = a * c; break;
                    case IrBinOp.TrueDiv: if (c == 0) { ErrS("division by zero here causes illegal behavior", at); return; } v = a / c; break;
                    default: return;
                }
                res.Known = true; res.FloatVal = v;
            }
        }

        /// <summary>Zig's rules for / and % (Table of Operators): signed or float operands of % (signed of /) must be comptime-known and positive.</summary>
        IrBinOp? DivOp(string op, ZR l, ZR r, ZType peer, ZNode at)
        {
            bool div = op == "/";
            if (peer.IsFloatLike)
            {
                if (div) return IrBinOp.TrueDiv;
                if (l.Known && r.Known && l.FloatVal >= 0 && r.FloatVal > 0 && peer.K == ZK.CFloat) return IrBinOp.ModTrunc;
                ErrS("remainder division with " + Q(l.T) + " and " + Q(r.T) + ": signed integers and floats must use @rem or @mod", at);
                return null;
            }
            if (peer.K == ZK.Int && !peer.Signed) return div ? IrBinOp.IntDivFloor : IrBinOp.ModFloor;
            if (l.Known && r.Known && l.IntVal >= 0 && r.IntVal > 0) return div ? IrBinOp.IntDivTrunc : IrBinOp.ModTrunc;
            if (div) ErrS("division with " + Q(l.T) + " and " + Q(r.T) + ": signed integers must use @divTrunc, @divFloor, or @divExact", at);
            else ErrS("remainder division with " + Q(l.T) + " and " + Q(r.T) + ": signed integers and floats must use @rem or @mod", at);
            return null;
        }

        static IrBinOp CmpOp(string op)
        {
            switch (op)
            {
                case "==": return IrBinOp.Eq;
                case "!=": return IrBinOp.Ne;
                case "<": return IrBinOp.Lt;
                case "<=": return IrBinOp.Le;
                case ">": return IrBinOp.Gt;
                default: return IrBinOp.Ge;
            }
        }

        ZR Compare(ZBinary b)
        {
            ZR l, r;
            var ul = Unparen(b.Left); var ur = Unparen(b.Right);
            if (ul is ZEnumLit && !(ur is ZEnumLit)) { r = Expr(b.Right, null); l = Expr(b.Left, ExpectedFor(r.T)); }
            else { l = Expr(b.Left, null); r = Expr(b.Right, ExpectedFor(l.T)); }
            if (l.T.IsErr || r.T.IsErr) return ZR.Error;
            var op = CmpOp(b.Op);
            bool eq = op == IrBinOp.Eq || op == IrBinOp.Ne;
            // optionals against null
            if (l.T.K == ZK.Null || r.T.K == ZK.Null)
            {
                var other = l.T.K == ZK.Null ? r : l;
                if (!eq) { ErrS("operator " + b.Op + " not allowed for type " + Q(other.T), b); return ZR.Error; }
                if (other.T.K == ZK.Null) return new ZR { Ir = Bin(op, l.Ir, r.Ir, b, IrType.Bool), T = ZType.Bool };
                if (other.T.K != ZK.Optional) { ErrS("comparison of " + Q(other.T) + " with null", b); return ZR.Error; }
                if (other.Special == "find" && other.Ir is IrOpCall fo)
                {
                    IrExpr contains = new IrOpCall(IrOp.Contains, fo.Args[0], fo.Args[1]) { NameSpan = fo.NameSpan, Type = IrType.Bool, Span = b.Span };
                    if (op == IrBinOp.Eq) contains = new IrUnary { Op = IrUnOp.Not, Operand = contains, OpSpan = new Span(b.OpStart, b.OpEnd), Type = IrType.Bool, Span = b.Span };
                    return new ZR { Ir = contains, T = ZType.Bool };
                }
                if (other.Special == "pop" && other.Ir is IrOpCall po && po.Op == IrOp.ListPop && po.Args.Count == 1 && CloneSimple(po.Args[0]) is IrExpr pl)
                {
                    // list.pop() == null: "len(l) == 0 or l.pop() == None" (pops only a non-empty list, like Zig)
                    if (other.T.Elem.K == ZK.Optional) { NotSupported("comparing pop() of a list of optionals with null is", b); return ZR.Error; }
                    IrExpr empty = op == IrBinOp.Ne ? NotEmpty(pl, po) : new IrOpCall(IrOp.IsEmpty, pl) { NameSpan = po.NameSpan, Span = po.Span, Type = IrType.Bool, Synthetic = true };
                    var popCmp = Bin(op, po, SynthNull(new Span(b.OpStart, b.OpEnd)), b, IrType.Bool);
                    return new ZR { Ir = new IrBinary { Op = op == IrBinOp.Eq ? IrBinOp.Or : IrBinOp.And, Left = empty, Right = popCmp, OpSpan = new Span(b.OpStart, b.OpEnd), Span = b.Span, Type = IrType.Bool, Synthetic = true }, T = ZType.Bool };
                }
                if (other.Ir is IrOpCall go && go.Op == IrOp.MapGetOrNull)
                {
                    IrExpr contains = new IrOpCall(IrOp.Contains, go.Args[0], go.Args[1]) { NameSpan = go.NameSpan, Type = IrType.Bool, Span = b.Span };
                    if (op == IrBinOp.Eq) contains = new IrUnary { Op = IrUnOp.Not, Operand = contains, OpSpan = new Span(b.OpStart, b.OpEnd), Type = IrType.Bool, Span = b.Span };
                    return new ZR { Ir = contains, T = ZType.Bool };
                }
                return new ZR { Ir = Bin(op, l.Ir, r.Ir, b, IrType.Bool), T = ZType.Bool };
            }
            if (l.Special == "find" || r.Special == "find") { NotSupported("comparing the index std.mem.findScalar returns is (compare it with null)", b); return ZR.Error; }
            if (l.T.IsString || r.T.IsString)
            {
                if (ul is ZStrLit && ur is ZStrLit) ErrA("cannot compare strings with " + b.Op, b);
                else ErrS("operator " + b.Op + " not allowed for type '[]const u8' (compare strings with std.mem.eql(u8, a, b))", b);
                return ZR.Error;
            }
            var peer = Peer(l, r);
            // Zig compares integers of different types (even of different signedness) without a cast (Sema.cmpNumeric)
            if (peer == null && l.T.K == ZK.Int && r.T.K == ZK.Int)
                return new ZR { Ir = Bin(op, l.Ir, r.Ir, b, IrType.Bool), T = ZType.Bool };
            if (peer == null) { ErrS("incompatible types: " + Q(l.T) + " and " + Q(r.T), b); return ZR.Error; }
            if (peer.K == ZK.Optional) { ErrS("operator " + b.Op + " not allowed for type " + Q(peer) + " (use std.meta.eql(a, b), or unwrap it first)", b); return ZR.Error; }
            if (peer.IsNumeric)
            {
                var li = Coerce(l, peer, b.Left); var ri = Coerce(r, peer, b.Right);
                var res = new ZR { Ir = Bin(op, li, ri, b, IrType.Bool), T = ZType.Bool };
                if (l.Known && r.Known)
                {
                    double a = l.T.IsIntLike ? l.IntVal : l.FloatVal, c = r.T.IsIntLike ? r.IntVal : r.FloatVal;
                    res.Known = true;
                    res.BoolVal = op == IrBinOp.Eq ? a == c : op == IrBinOp.Ne ? a != c : op == IrBinOp.Lt ? a < c : op == IrBinOp.Le ? a <= c : op == IrBinOp.Gt ? a > c : a >= c;
                }
                return res;
            }
            if (!eq) { ErrS("operator " + b.Op + " not allowed for type " + Q(peer), b); return ZR.Error; }
            if (peer.K == ZK.Bool || peer.K == ZK.Game)
            {
                var li = Coerce(l, peer, b.Left); var ri = Coerce(r, peer, b.Right);
                var res = new ZR { Ir = Bin(op, li, ri, b, IrType.Bool), T = ZType.Bool };
                if (l.Known && r.Known && peer.K == ZK.Bool) { res.Known = true; res.BoolVal = (l.BoolVal == r.BoolVal) == (op == IrBinOp.Eq); }
                return res;
            }
            if (peer.K == ZK.EnumLit) { NotSupported("comparing two untyped enum literals is", b); return ZR.Error; }
            ErrS("operator " + b.Op + " not allowed for type " + Q(peer), b);
            return ZR.Error;
        }

        static ZType ExpectedFor(ZType t)
        {
            if (t == null) return null;
            if (t.K == ZK.Game) return t;
            if (t.K == ZK.Optional && t.Elem.K == ZK.Game) return t.Elem;
            return null;
        }

        ZR OrElse(ZBinary b, ZType expected)
        {
            var l = Expr(b.Left, expected != null ? ZType.Opt(expected) : null);
            if (l.T.IsErr) { if (!(b.Right is ZControlExpr)) Expr(b.Right, null); return ZR.Error; }
            if (l.T.K != ZK.Optional) { ErrS("expected optional type, found " + Q(l.T), b.Left); return ZR.Error; }
            var t = l.T.Elem;
            if (b.Right is ZControlExpr ce)
            {
                if (ce.Kw == "unreachable") { var ir0 = l.Ir; if (ir0 is IrOpCall g0 && g0.Op == IrOp.MapGetOrNull) ir0 = new IrIndex { Target = g0.Args[0], Index = g0.Args[1], Span = b.Span, Type = t.Ir() }; else if (ir0 != null) ir0.Type = t.Ir(); return new ZR { Ir = ir0, T = t }; }
                NotSupported("'orelse " + ce.Kw + "' is only supported in a declaration (const x = a orelse " + ce.Kw + ";); here it is", b);
                return ZR.Error;
            }
            if (b.Right is ZBlockValue) { NotSupported("'orelse { ... }' is", b.Right); return ZR.Error; }
            var r = Expr(b.Right, t);
            if (r.T.IsErr) return ZR.Error;
            var m = CoerceError(r, t);
            if (m != null) { ErrS(m, b.Right); return ZR.Error; }
            var ri = Coerce(r, t, b.Right);
            if ((l.Special == "find" || l.Special == "pop") && IsWhole(b))
            {
                // find(...) orelse d → (x in s) ? find : d;  list.pop() orelse d → len(l) > 0 ? l.pop() : d
                var ov = OptValue(l, b.Left, ri);
                if (ov.T.IsErr) return ZR.Error;
                if (ov.Ir is IrConditional oc) { oc.Span = b.Span; return ov; }
            }
            if (l.Ir is IrOpCall go && go.Op == IrOp.MapGetOrNull)
                return new ZR { Ir = new IrOpCall(IrOp.MapGetOrDefault, go.Args[0], go.Args[1], ri) { NameSpan = go.NameSpan, Span = b.Span, Type = t.Ir() }, T = t };
            if (l.Ir is IrName n && IsWhole(b))
            {
                var cond = new IrBinary { Op = IrBinOp.Ne, Left = new IrName(n.Symbol) { Span = n.Span, Type = n.Type }, Right = new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = new Span(b.OpStart, b.OpEnd), Synthetic = true }, OpSpan = new Span(b.OpStart, b.OpEnd), Type = IrType.Bool, Synthetic = true, Span = b.Span };
                n.Type = t.Ir();
                return new ZR { Ir = new IrConditional { Cond = cond, Then = n, Else = ri, QSpan = new Span(b.OpStart, b.OpEnd), Span = b.Span, Type = t.Ir() }, T = t };
            }
            NotSupported("'orelse' is only supported after a variable name (x orelse y), after map.get(k), or as 'const x = a orelse return;'; here it is", b);
            return ZR.Error;
        }

        ZR Unary(ZUnary u, ZType expected)
        {
            var opSpan = new Span(u.Start, u.OpEnd);
            switch (u.Op)
            {
                case "-":
                    {
                        var r = Expr(u.Operand, expected);
                        if (r.T.IsErr) return r;
                        if (!r.T.IsNumeric) { ErrS("invalid operand to unary '-': " + Q(r.T), u); return ZR.Error; }
                        if (r.T.K == ZK.Int && !r.T.Signed) { ErrS("negation of type " + Q(r.T) + " (an unsigned integer cannot be negative)", u); return ZR.Error; }
                        if (r.Known && r.Ir is IrLiteral lit && r.Ir.ParenSpan.IsNone)
                        {
                            var neg = lit.Kind == IrLitKind.Int ? IrLiteral.OfInt(-lit.Int) : IrLiteral.OfFloat(-lit.Float);
                            neg.Span = u.Span;
                            return new ZR { Ir = neg, T = r.T, Known = true, IntVal = -r.IntVal, FloatVal = -r.FloatVal };
                        }
                        var res = new ZR { Ir = new IrUnary { Op = IrUnOp.Neg, Operand = r.Ir, OpSpan = opSpan, Type = r.T.Ir(), Span = u.Span }, T = r.T };
                        if (r.Known) { res.Known = true; res.IntVal = -r.IntVal; res.FloatVal = -r.FloatVal; }
                        return res;
                    }
                case "!":
                    {
                        var r = Expr(u.Operand, ZType.Bool);
                        if (r.T.IsErr) return r;
                        if (r.T.K != ZK.Bool) { ErrS("expected type 'bool', found " + Q(r.T), u.Operand); return ZR.Error; }
                        var res = new ZR { Ir = new IrUnary { Op = IrUnOp.Not, Operand = r.Ir, OpSpan = opSpan, Type = IrType.Bool, Span = u.Span }, T = ZType.Bool };
                        if (r.Known) { res.Known = true; res.BoolVal = !r.BoolVal; }
                        return res;
                    }
                case "&":
                    {
                        var inner = Unparen(u.Operand);
                        if (inner is ZInit init && init.Type == null)
                        {
                            if (expected != null && expected.K == ZK.Slice)
                            {
                                var arr = Init(init, ZType.ArrayOf(expected.Elem, init.Items.Count));
                                if (arr.T.IsErr) return arr;
                                return new ZR { Ir = arr.Ir, T = new ZType { K = ZK.Ptr, Elem = arr.T, Const = true } };
                            }
                            NotSupported("&.{ ... } without a slice type is", u);
                            return ZR.Error;
                        }
                        var r = Expr(u.Operand, null);
                        if (r.T.IsErr) return r;
                        var t = r.T;
                        if (r.Sym != null && (r.Sym.Kind == ZSymKind.Local || r.Sym.Kind == ZSymKind.Global || r.Sym.Kind == ZSymKind.Param || r.Sym.Kind == ZSymKind.Capture) && (t.K == ZK.List || t.K == ZK.Map || t.K == ZK.Array) && r.Special == null)
                        {
                            r.Sym.Mutated = true;
                            return new ZR { Ir = r.Ir, T = new ZType { K = ZK.Ptr, Elem = t, Const = !r.Sym.IsVar }, Sym = r.Sym };
                        }
                        if (r.Special == "items") { NotSupported("&list.items is (pass list.items or &list)", u); return ZR.Error; }
                        NotSupported("taking the address of " + Q(t) + " is", u);
                        return ZR.Error;
                    }
                case "try":
                    {
                        var r = Expr(u.Operand, expected != null ? ZType.ErrU(expected) : null);
                        if (r.T.IsErr) return r;
                        if (r.T.K != ZK.ErrorUnion) { ErrS("expected error union type, found " + Q(r.T), u.Operand); return ZR.Error; }
                        if (curFn == null) { ErrA("'try' outside function scope", u); return ZR.Error; }
                        if (!curFn.RetError) { ErrS("expected type " + Q(curFn.Ret) + ", found 'error{OutOfMemory}' (a function that uses 'try' must return an error union, e.g. !void)", new ZSpanNode(u.Start, u.OpEnd)); return ZR.Error; }
                        return new ZR { Ir = r.Ir, T = r.T.Elem };
                    }
                case "~":
                    NotSupported("bitwise operators are", new ZSpanNode(u.Start, u.OpEnd));
                    return ZR.Error;
                case "-%":
                    NotSupported("wrapping and saturating operators are", new ZSpanNode(u.Start, u.OpEnd));
                    return ZR.Error;
            }
            NotSupported("operator " + Q(u.Op) + " is", u);
            return ZR.Error;
        }

        ZR IfExpr(ZIfExpr ie, ZType expected)
        {
            if (ie.Capture != null) { NotSupported("an 'if' expression with a capture is", new ZSpanNode(ie.Capture.Start, ie.Capture.End)); return ZR.Error; }
            if (ie.Else == null) { ErrS("an 'if' expression needs an 'else' branch", ie); return ZR.Error; }
            bool whole = IsWhole(ie);
            var cond = Condition(ie.Cond);
            var old = wholeValue;
            wholeValue = null;
            var a = Expr(ie.Then, expected);
            // if (a) x else if (b) y else z: the inner if is as whole as the outer one (an elif chain)
            if (whole && Unparen(ie.Else) is ZIfExpr) wholeValue = Unparen(ie.Else);
            var b = Expr(ie.Else, expected);
            wholeValue = old;
            if (a.T.IsErr || b.T.IsErr) return ZR.Error;
            if (RejectOptOp(a, ie.Then) || RejectOptOp(b, ie.Else)) return ZR.Error;
            ZType t = expected;
            if (t == null)
            {
                t = Peer(a, b);
                if (t == null) { ErrS("incompatible types: " + Q(a.T) + " and " + Q(b.T), ie); return ZR.Error; }
                if (t.IsComptimeNum) { ErrS("value with comptime-only type " + Q(t) + " depends on runtime control flow (give the variable a type)", ie.Cond); return ZR.Error; }
                if (t.K == ZK.EnumLit) { NotSupported("an untyped enum literal is", ie); return ZR.Error; }
            }
            var ai = Coerce(a, t, ie.Then);
            var bi = Coerce(b, t, ie.Else);
            if (!whole) { NotSupported("an 'if' expression is only supported as a whole value (a declaration, an assignment, a return or a whole argument); here it is", new ZSpanNode(ie.Start, ie.KwEnd)); return ZR.Error; }
            return new ZR { Ir = new IrConditional { Cond = cond, Then = ai, Else = bi, QSpan = new Span(ie.Start, ie.KwEnd), Span = ie.Span, Type = t.Ir() }, T = t };
        }

        ZR Catch(ZCatchExpr ce, ZType expected)
        {
            var l = Expr(ce.Left, expected != null ? ZType.ErrU(expected) : null);
            if (l.T.IsErr) return l;
            if (l.T.K != ZK.ErrorUnion) { ErrS("expected error union type, found " + Q(l.T), ce.Left); return ZR.Error; }
            if (ce.Capture != null) { NotSupported("catch |err| is", new ZSpanNode(ce.Capture.Start, ce.Capture.End)); return ZR.Error; }
            var t = l.T.Elem;
            if (ce.Right is ZControlExpr ctl && (ctl.Kw == "unreachable" || (ctl.Kw == "return" && ctl.Value == null))) return new ZR { Ir = l.Ir, T = t };
            if (ce.Right is ZBlockValue bv && bv.Block.Stmts.Count == 0)
            {
                if (t.K != ZK.Void) { ErrS("expected type " + Q(t) + ", found 'void'", ce.Right); return ZR.Error; }
                return new ZR { Ir = l.Ir, T = t };
            }
            var r = Expr(ce.Right, t);
            if (r.T.IsErr) return ZR.Error;
            var m = CoerceError(r, t);
            if (m != null) { ErrS(m, ce.Right); return ZR.Error; }
            return new ZR { Ir = l.Ir, T = t };
        }

        ZR Init(ZInit init, ZType expected)
        {
            ZType target = expected;
            if (init.Type != null)
            {
                if (init.Type is ZArrayType at && at.Inferred)
                {
                    var elem = ResolveType(at.Child);
                    if (elem.IsErr) return ZR.Error;
                    target = ZType.ArrayOf(elem, init.Items.Count);
                    if (init.Fields != null) { ErrS("initializing array with struct syntax", init); return ZR.Error; }
                }
                else
                {
                    target = ResolveType(init.Type);
                    if (target.IsErr) return ZR.Error;
                }
                if (target.K == ZK.List || target.K == ZK.Map) { NotSupported(Q(target) + "{} is (use .empty for lists and .init(farm.allocator) for maps); this", init); return ZR.Error; }
            }
            if (target != null && target.K == ZK.Optional) target = target.Elem;
            if (target == null || target.K == ZK.Type)
            {
                if (init.Fields != null) { NotSupported("anonymous struct literals are", init); return ZR.Error; }
                var items = init.Items.Select(x => Expr(x, null)).ToList();
                if (items.Any(x => x.T.IsErr)) return ZR.Error;
                for (int i = 0; i < items.Count; i++) if (items[i].T.K == ZK.EnumLit) { NotSupported("an untyped enum literal is", init.Items[i]); return ZR.Error; }
                var lit = new IrTupleLit { Span = init.Span };
                for (int i = 0; i < items.Count; i++) lit.Items.Add(CopyArray(items[i].Ir, items[i].T, init.Items[i], false) ?? IrLiteral.Null());
                var tt = ZType.TupleT(items.Select(x => x.T).ToList());
                lit.Type = tt.Ir();
                return new ZR { Ir = lit, T = tt };
            }
            switch (target.K)
            {
                case ZK.Array:
                    {
                        if (init.Fields != null) { ErrS("initializing array with struct syntax", init); return ZR.Error; }
                        var lit = new IrListLit { IsArray = true, Span = init.Span, Type = target.Ir() };
                        foreach (var x in init.Items)
                        {
                            var r = Whole(x, target.Elem);
                            lit.Items.Add(Coerce(r, target.Elem, x) ?? IrLiteral.Null());
                        }
                        if (init.Items.Count != target.Len) { ErrS("expected " + target.Len + " array elements; found " + init.Items.Count, init); return ZR.Error; }
                        return new ZR { Ir = lit, T = target };
                    }
                case ZK.Struct:
                    {
                        if (target.Name != "Position") { NotSupported("creating a " + Q(target) + " is", init); return ZR.Error; }
                        if (init.Fields == null || init.Fields.Count != 2 || !init.Fields.Any(f => f.Name == "x") || !init.Fields.Any(f => f.Name == "y"))
                        {
                            if (init.Fields == null && init.Items.Count > 0) { ErrS("expected type 'farm.Position', found 'struct { ... }' (write .{ .x = ..., .y = ... })", init); return ZR.Error; }
                            var missing = init.Fields == null || !init.Fields.Any(f => f.Name == "x") ? "x" : "y";
                            if (init.Fields != null)
                                foreach (var ef in init.Fields)
                                    if (ef.Name != "x" && ef.Name != "y") { ErrS("no field named " + Q(ef.Name) + " in struct 'farm.Position'", ef.NameStart, ef.NameEnd); return ZR.Error; }
                            ErrS("missing struct field: " + missing, init);
                            return ZR.Error;
                        }
                        var fx = init.Fields.First(f => f.Name == "x"); var fy = init.Fields.First(f => f.Name == "y");
                        var rx = Expr(fx.Value, ZType.Usize); var ry = Expr(fy.Value, ZType.Usize);
                        var lit = new IrTupleLit { Span = init.Span, Type = IrType.Game("Position") };
                        lit.Items.Add(Coerce(rx, ZType.Usize, fx.Value));
                        lit.Items.Add(Coerce(ry, ZType.Usize, fy.Value));
                        return new ZR { Ir = lit, T = target };
                    }
                case ZK.Tuple:
                    {
                        if (init.Fields != null || init.Items.Count != target.Items.Count) { ErrS(Mismatch(new ZType { K = ZK.Tuple, Items = init.Items.Select(_ => ZType.CInt).ToList() }, target), init); return ZR.Error; }
                        var lit = new IrTupleLit { Span = init.Span, Type = target.Ir() };
                        for (int i = 0; i < init.Items.Count; i++)
                        {
                            var r = Expr(init.Items[i], target.Items[i]);
                            lit.Items.Add(Coerce(r, target.Items[i], init.Items[i]));
                        }
                        return new ZR { Ir = lit, T = target };
                    }
                case ZK.Slice:
                    ErrS("expected type " + Q(target) + ", found an array literal (use &.{ ... })", init);
                    return ZR.Error;
            }
            if (target.IsErr) return ZR.Error;
            ErrS("type " + Q(target) + " does not support array initialization syntax", init);
            return ZR.Error;
        }
    }
}
