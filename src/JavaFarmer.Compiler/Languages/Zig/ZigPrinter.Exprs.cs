using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal sealed partial class ZigPrinter
    {
        struct Ex
        {
            public string T; public int P;
            public Ex(string t, int p) { T = t; P = p; }
        }

        const int PrecIf = 0, PrecOr = 10, PrecAnd = 20, PrecCmp = 30, PrecOrElse = 40, PrecAdd = 60, PrecMul = 70, PrecUn = 80, PrecPost = 90, PrecLit = 100;
        const string Self = "@@SELF@@";

        static string W(Ex x, int min) => x.P < min ? "(" + x.T + ")" : x.T;

        // ================================================================== types
        IrType SymIr(IrSymbol s) => s == null ? IrType.Unknown : types?.Sym(s) ?? s.Type ?? IrType.Unknown;

        readonly Dictionary<IrSymbol, ZType> symCache = new Dictionary<IrSymbol, ZType>();

        ZType SymZ(IrSymbol s)
        {
            if (s == null) return null;
            if (forcedTypes.TryGetValue(s, out var ft) && ft != null) return ft;
            if (symCache.TryGetValue(s, out var c)) return c;
            symCache[s] = null;
            ZType t;
            var tag = TagOf(s);
            if (tag?.Type != null && !tag.Type.IsErr) t = tag.Type;
            else if (tag?.Sig != null) t = new ZType { K = ZK.Fn, Sig = tag.Sig };
            else
            {
                var it = SymIr(s);
                t = FromIr(it, s);
                if (t != null && t.K == ZK.List && !mutated.Contains(s) && ArrayInitLength(s, out long n)) t = ZType.ArrayOf(t.Elem, n);
                if (t != null && t.K == ZK.Array && t.Len < 0 && ArrayInitLength(s, out long n2)) t = ZType.ArrayOf(t.Elem, n2);
            }
            symCache[s] = t;
            return t;
        }

        /// <summary>A list initialised once from a literal (and only read) prints as a fixed array of that length.</summary>
        bool ArrayInitLength(IrSymbol s, out long n)
        {
            n = -1;
            IrExpr init = null;
            int count = 0;
            foreach (var x in IrWalk.Descendants(mod))
            {
                if (x is IrVarDecl v && v.Symbol == s) { if (v.Init != null) { init = v.Init; count++; } }
                else if (x is IrGlobal g && g.Symbol == s) { if (g.Init != null) { init = g.Init; count++; } }
                else if (x is IrAssign a && a.Target is IrName tn && tn.Symbol == s) { init = a.Value; count++; }
                else if (x is IrAssign a2 && a2.Target is IrTupleLit tl && tl.Items.Any(i => NameSym(i) == s)) return false;
            }
            if (count != 1) return false;
            if (init is IrListLit ll) { n = ll.Items.Count; return true; }
            if (init is IrOpCall o && (o.Op == IrOp.ArrayNew || o.Op == IrOp.ArrayNew2) && o.Args[0] is IrLiteral li && li.Kind == IrLitKind.Int) { n = li.Int; return true; }
            return false;
        }

        ZType FromIr(IrType t, IrSymbol s)
        {
            if (t == null) return null;
            ZType r;
            switch (t.Kind)
            {
                case IrTypeKind.Int: r = s != null && usize.Contains(s) ? ZType.Usize : ZType.I32; break;
                case IrTypeKind.Float: r = ZType.F64; break;
                case IrTypeKind.Bool: r = ZType.Bool; break;
                case IrTypeKind.String: r = ZType.Str; break;
                case IrTypeKind.Void: r = ZType.Void; break;
                case IrTypeKind.List:
                    {
                        var e = FromIr(t.Elem, null);
                        if (e == null) return null;
                        r = ZType.ListOf(e);
                        break;
                    }
                case IrTypeKind.Array:
                    {
                        var e = FromIr(t.Elem, null);
                        if (e == null) return null;
                        r = ZType.ArrayOf(e, -1);
                        break;
                    }
                case IrTypeKind.Set:
                    {
                        var e = FromIr(t.Elem, null);
                        if (e == null) return null;
                        r = ZType.MapOf(e, ZType.Void);
                        break;
                    }
                case IrTypeKind.Map:
                    {
                        var k = FromIr(t.Key, null); var v = FromIr(t.Elem, null);
                        if (k == null || v == null) return null;
                        r = ZType.MapOf(k, v);
                        break;
                    }
                case IrTypeKind.Game:
                    r = t.GameName == "Position" || t.GameName == "Companion" || t.GameName == "Drone" ? ZType.StructT(t.GameName) : ZType.GameT(t.GameName ?? "Entity");
                    break;
                case IrTypeKind.Tuple:
                    {
                        if (t.Items != null && t.Items.Count == 2 && t.Items.All(i => i.IsInt)) { r = ZType.StructT("Position"); break; }
                        var items = t.Items?.Select(i => FromIr(i, null)).ToList();
                        if (items == null || items.Any(i => i == null)) return null;
                        r = ZType.TupleT(items);
                        break;
                    }
                case IrTypeKind.Func:
                    r = new ZType { K = ZK.Fn, Sig = new ZFnSig { Ret = FromIr(t.Elem, null) ?? ZType.Void } };
                    break;
                default: return null;
            }
            if (t.Nullable && r.K != ZK.Void && r.K != ZK.Optional) r = ZType.Opt(r);
            return r;
        }

        ZType RetZ(IrFunction f)
        {
            var sig = TagOf(f.Symbol)?.Sig;
            if (sig != null) return sig.Ret;
            var rt = f.ReturnType != null && f.ReturnType.IsKnown ? f.ReturnType : (f.Symbol != null ? types.Ret(f.Symbol) : IrType.Void);
            if (rt.Kind == IrTypeKind.Int)
            {
                int k = 0;
                foreach (var r in IrWalk.Descendants(f.Body, false).OfType<IrReturn>()) if (r.Value != null) k = Math.Max(k, IntKindOf(r.Value) == 2 ? 3 : IntKindOf(r.Value));
                if (k == 1) return ZType.Usize;
            }
            var z = FromIr(rt, null);
            if (z == null && rt.IsKnown && rt.Kind != IrTypeKind.Void) Problem("the result type of " + f.Name + " is unknown");
            return z ?? ZType.Void;
        }

        ZType RetOf(IrSymbol fn)
        {
            if (fn == null) return null;
            if (fn.Function != null) return RetZ(fn.Function);
            var sig = TagOf(fn)?.Sig;
            if (sig != null) return sig.Ret;
            if (fn.Type != null && fn.Type.Kind == IrTypeKind.Func) return FromIr(fn.Type.Elem, null) ?? ZType.Void;
            return FromIr(types.Ret(fn), null);
        }

        string TypeText(ZType t, IrSymbol s)
        {
            if (t == null) { Problem("a value of unknown type" + (s != null ? " (" + s.Name + ")" : "")); return "i32"; }
            switch (t.K)
            {
                case ZK.Int: case ZK.Float: return t.Name;
                case ZK.CInt: return "i32";
                case ZK.CFloat: return "f64";
                case ZK.Bool: return "bool";
                case ZK.Void: return "void";
                case ZK.StrLit: return "[]const u8";
                case ZK.Slice: return "[]" + (t.Const ? "const " : "") + TypeText(t.Elem, null);
                case ZK.Array:
                    if (t.Len < 0) { Problem("an array whose length is not a constant"); return "[]const " + TypeText(t.Elem, null); }
                    return "[" + t.Len.ToString(CultureInfo.InvariantCulture) + "]" + TypeText(t.Elem, null);
                case ZK.List: return "std.ArrayList(" + TypeText(t.Elem, null) + ")";
                case ZK.Map:
                    if (t.Key.IsString) return "std.StringHashMap(" + TypeText(t.Elem, null) + ")";
                    return "std.AutoHashMap(" + TypeText(t.Key, null) + ", " + TypeText(t.Elem, null) + ")";
                case ZK.Game: case ZK.Struct: return "farm." + t.Name;
                case ZK.Optional: return "?" + TypeText(t.Elem, null);
                case ZK.Tuple: return "struct { " + string.Join(", ", t.Items.Select(i => TypeText(i, null))) + " }";
                case ZK.Ptr: return "*" + (t.Const ? "const " : "") + TypeText(t.Elem, null);
                case ZK.ErrorUnion: return "!" + TypeText(t.Elem, null);
                case ZK.Fn: case ZK.FnType: Problem("a function value" + (s != null ? " (" + s.Name + ")" : "")); return "void";
            }
            Problem("a value of type " + t);
            return "i32";
        }

        readonly HashSet<IrSymbol> ptrParams = new HashSet<IrSymbol>();

        string ParamTypeText(ZType t, IrSymbol s)
        {
            if (t != null && (t.K == ZK.List || t.K == ZK.Map) && s != null && mutated.Contains(s) && TagOf(s) == null) { ptrParams.Add(s); return "*" + TypeText(t, s); }
            if (t != null && t.K == ZK.Array && t.Len < 0) return "[]const " + TypeText(t.Elem, null);
            return TypeText(t, s);
        }

        string DefaultText(ZType t)
        {
            if (t == null) return "undefined";
            switch (t.K)
            {
                case ZK.Int: case ZK.Float: case ZK.CInt: case ZK.CFloat: return "0";
                case ZK.Bool: return "false";
                case ZK.Slice: case ZK.StrLit: return t.IsString ? "\"\"" : "&.{}";
                case ZK.Optional: return "null";
                case ZK.List: return ".empty";
                case ZK.Map: return ".init(farm.allocator)";
                case ZK.Array: return t.Len >= 0 ? "@splat(" + DefaultText(t.Elem) + ")" : "undefined";
            }
            return "undefined";
        }

        static ZType PeerZ(ZType a, ZType b)
        {
            if (a == null) return b;
            if (b == null) return a;
            if (a.Same(b)) return a;
            if (a.K == ZK.CInt && (b.IsNumeric)) return b.K == ZK.CInt ? a : b;
            if (b.K == ZK.CInt && a.IsNumeric) return a;
            if (a.K == ZK.CFloat && b.IsFloatLike) return b.K == ZK.Float ? b : a;
            if (b.K == ZK.CFloat && a.IsFloatLike) return a;
            if (a.IsFloatLike || b.IsFloatLike) return ZType.F64;
            if (a.K == ZK.Int && b.K == ZK.Int) return a.Signed ? a : b.Signed ? b : a.Bits >= b.Bits ? a : b;
            if (a.K == ZK.Optional && a.Elem.Same(b)) return a;
            if (b.K == ZK.Optional && b.Elem.Same(a)) return b;
            if (a.K == ZK.Null) return b.K == ZK.Optional ? b : ZType.Opt(b);
            if (b.K == ZK.Null) return a.K == ZK.Optional ? a : ZType.Opt(a);
            return a;
        }

        /// <summary>The Zig type of an expression as printed.</summary>
        ZType T(IrExpr e)
        {
            switch (e)
            {
                case null: return null;
                case IrLiteral l:
                    switch (l.Kind)
                    {
                        case IrLitKind.Int: return l.Type != null && l.Type.Kind == IrTypeKind.Float ? ZType.CFloat : ZType.CInt;
                        case IrLitKind.Float: return ZType.CFloat;
                        case IrLitKind.Bool: return ZType.Bool;
                        case IrLitKind.String: return ZType.Str;
                    }
                    return ZType.Null;
                case IrName n:
                    if (n.Symbol == null) return null;
                    if (n.Symbol.Kind == IrSymbolKind.Function || n.Symbol.Kind == IrSymbolKind.ExternalFunction) return new ZType { K = ZK.Fn };
                    {
                        var st = SymZ(n.Symbol);
                        if (st == null && n.Type != null && n.Type.IsKnown) st = FromIr(n.Type, n.Symbol);
                        if (st != null && st.K == ZK.Ptr) return st.Elem;
                        return st;
                    }
                case IrConst c: return ZType.GameT(GameApi.GameTypeOfCategory(c.Category) ?? "Entity");
                case IrBinary b:
                    switch (b.Op)
                    {
                        case IrBinOp.Eq: case IrBinOp.Ne: case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge: case IrBinOp.And: case IrBinOp.Or: return ZType.Bool;
                        case IrBinOp.Concat: return ZType.Str;
                        case IrBinOp.TrueDiv: { var p = PeerZ(T(b.Left), T(b.Right)); return p != null && p.K == ZK.CFloat && b.Left is IrLiteral && b.Right is IrLiteral ? p : ZType.F64; }
                    }
                    {
                        var p = PeerZ(T(b.Left), T(b.Right));
                        if (b.Op == IrBinOp.Pow && p != null && p.K == ZK.CInt) return ZType.I32;
                        if (b.Op == IrBinOp.Pow && p != null && p.K == ZK.CFloat) return ZType.F64;
                        return p;
                    }
                case IrUnary u:
                    if (u.Op == IrUnOp.Not) return ZType.Bool;
                    { var t = T(u.Operand); if (u.Op == IrUnOp.Neg && t != null && t.K == ZK.Int && !t.Signed) return ZType.I32; return t; }
                case IrConditional ce:
                    if (OptGuard(ce) is IrOpCall gop && gop.Op == IrOp.ListIndexOf) return IsNullLit(ce.Else) ? ZType.Opt(ZType.Usize) : ZType.Usize;
                    return PeerZ(T(ce.Then), T(ce.Else));
                case IrCast ca:
                    if (ca.IsTypeAssertion) return FromIr(ca.Target, null) ?? T(ca.Operand);
                    if (ca.Target.Kind == IrTypeKind.Int) { var ot = T(ca.Operand); return ot != null && ot.IsIntLike ? ot : ZType.I32; }
                    if (ca.Target.Kind == IrTypeKind.Float) return ZType.F64;
                    if (ca.Target.Kind == IrTypeKind.String) return ZType.Str;
                    return FromIr(ca.Target, null);
                case IrCall c:
                    switch (c.Kind)
                    {
                        case IrCallKind.Function: return RetOf(c.Function);
                        case IrCallKind.Value: return null;
                    }
                    switch (c.Builtin)
                    {
                        case "print": case "quick_print": return ZType.Void;
                        case "min": case "max":
                            {
                                ZType p = null;
                                foreach (var a in c.Args) p = PeerZ(p, T(a));
                                if (p != null && p.K == ZK.CInt) p = ZType.I32;
                                return p;
                            }
                        case "abs":
                            {
                                var t = T(c.Args.FirstOrDefault());
                                if (t != null && t.K == ZK.Int && t.Signed) return t.Name == "isize" ? ZType.Usize : ZType.IntT("u" + t.Bits);
                                return t;
                            }
                    }
                    {
                        var f = ZigApi.ForArity(c.Builtin, c.Args.Count);
                        if (f == null) return null;
                        if (f.TypeArg) return FromIr(types.TypeOf(c), null) ?? FromIr(c.Type, null);
                        return f.Ret;
                    }
                case IrOpCall o:
                    {
                        var rt = o.Args.Count > 0 ? Deref(T(o.Args[0])) : null;
                        switch (o.Op)
                        {
                            case IrOp.Len: return rt != null && rt.K == ZK.Map ? ZType.U32 : ZType.Usize;
                            case IrOp.IsEmpty: case IrOp.Contains: return ZType.Bool;
                            case IrOp.ListPop: return rt?.Elem == null ? null : (o.Args.Count == 1 ? ZType.Opt(rt.Elem) : rt.Elem);
                            case IrOp.MapGetOrNull: return rt?.Elem == null ? null : ZType.Opt(rt.Elem);
                            case IrOp.MapGetOrDefault: return rt?.Elem;
                            case IrOp.MapRemove: case IrOp.MapRemoveIfPresent: case IrOp.RemoveValueIfPresent: return ZType.Bool;
                            case IrOp.RemoveValue: return rt?.Elem;
                            case IrOp.ListCopy: return rt != null && rt.K == ZK.List ? rt : rt?.Elem != null ? ZType.ListOf(rt.Elem) : null;
                            case IrOp.ArrayNew:
                                {
                                    var d = T(o.Args[1]);
                                    if (d != null && d.K == ZK.CInt) d = ZType.I32;
                                    if (d != null && d.K == ZK.CFloat) d = ZType.F64;
                                    long n = o.Args[0] is IrLiteral li && li.Kind == IrLitKind.Int ? li.Int : -1;
                                    return d == null ? null : ZType.ArrayOf(d, n);
                                }
                            case IrOp.ArrayNew2:
                                {
                                    var d = T(o.Args[2]);
                                    if (d != null && d.K == ZK.CInt) d = ZType.I32;
                                    if (d != null && d.K == ZK.CFloat) d = ZType.F64;
                                    long w = o.Args[0] is IrLiteral lw && lw.Kind == IrLitKind.Int ? lw.Int : -1;
                                    long h = o.Args[1] is IrLiteral lh && lh.Kind == IrLitKind.Int ? lh.Int : -1;
                                    return d == null ? null : ZType.ArrayOf(ZType.ArrayOf(d, h), w);
                                }
                            case IrOp.Floor: case IrOp.Ceil: case IrOp.Sqrt: return o.Type != null && o.Type.IsInt ? ZType.I32 : ZType.F64;
                            case IrOp.Round: return o.Type != null && o.Type.IsInt ? ZType.I32 : ZType.F64;
                            case IrOp.PosX: case IrOp.PosY: return ZType.Usize;
                            case IrOp.CompanionEntity: return ZType.GameT("Entity");
                            case IrOp.CompanionPos: return ZType.StructT("Position");
                            case IrOp.ListIndexOf: return ZType.I32;
                        }
                        return ZType.Void;
                    }
                case IrIndex ix:
                    {
                        var tt = Deref(T(ix.Target));
                        if (tt == null) return FromIr(types.TypeOf(ix), null);
                        switch (tt.K)
                        {
                            case ZK.List: case ZK.Array: case ZK.Slice: return tt.IsString ? ZType.Str : tt.Elem;
                            case ZK.Map: return tt.Elem;
                            case ZK.Tuple:
                                if (ix.Index is IrLiteral li && li.Kind == IrLitKind.Int && li.Int >= 0 && li.Int < tt.Items.Count) return tt.Items[(int)li.Int];
                                return null;
                            case ZK.Struct:
                                if (tt.Name == "Position") return ZType.Usize;
                                if (tt.Name == "Companion" && ix.Index is IrLiteral ci) return ci.Int == 0 ? ZType.GameT("Entity") : ZType.StructT("Position");
                                return null;
                        }
                        return null;
                    }
                case IrListLit ll:
                    {
                        ZType el = null;
                        foreach (var i in ll.Items) el = PeerZ(el, T(i));
                        if (el == null) { var it = types.TypeOf(ll); el = FromIr(it.Elem, null); }
                        if (el != null && el.K == ZK.CInt) el = ZType.I32;
                        if (el != null && el.K == ZK.CFloat) el = ZType.F64;
                        return el == null ? null : ZType.ArrayOf(el, ll.Items.Count);
                    }
                case IrSetLit _: case IrMapLit _: return FromIr(types.TypeOf(e), null);
                case IrTupleLit tl:
                    {
                        if (e.Type != null && e.Type.IsGame("Position")) return ZType.StructT("Position");
                        var items = tl.Items.Select(T).ToList();
                        if (items.Count == 2 && items.All(i => i != null && i.IsIntLike)) return ZType.StructT("Position");
                        if (items.Any(i => i == null)) return null;
                        return ZType.TupleT(items);
                    }
                case IrLambda _: case IrFuncRef _: return new ZType { K = ZK.Fn };
            }
            return FromIr(e.Type, null);
        }

        static ZType Deref(ZType t)
        {
            if (t == null) return null;
            if (t.K == ZK.Ptr) return t.Elem;
            if (t.K == ZK.Optional) return t.Elem.K == ZK.Ptr ? t.Elem.Elem : t.Elem;
            return t;
        }

        static bool IntCoercesZ(ZType from, ZType to)
        {
            if (from.Name == to.Name) return true;
            if (from.Signed == to.Signed) return to.Bits >= from.Bits && !(from.Name == "usize" && to.Name != "usize" && to.Name != "u64");
            if (!from.Signed && to.Signed) return to.Bits > from.Bits;
            return false;
        }

        /// <summary>Converts text of natural type nt so that it becomes 'want' (result-type context or not).</summary>
        Ex Convert(Ex x, ZType nt, ZType want, bool ctx)
        {
            if (want == null || nt == null || want.IsErr || nt.IsErr) return x;
            if (want.K == ZK.Optional)
            {
                if (nt.K == ZK.Optional || nt.K == ZK.Null) return x;
                return Convert(x, nt, want.Elem, ctx);
            }
            if (nt.K == ZK.Optional && want.K != ZK.Optional && want.K != ZK.Void && want.K != ZK.Bool)
            {
                x = new Ex(W(x, PrecPost) + ".?", PrecPost);
                nt = nt.Elem;
            }
            if (nt.K == ZK.Int && want.K == ZK.Int && !IntCoercesZ(nt, want))
                return ctx ? new Ex("@intCast(" + x.T + ")", PrecLit) : new Ex("@as(" + want.Name + ", @intCast(" + x.T + "))", PrecLit);
            if (nt.K == ZK.Int && want.K == ZK.Float && (nt.Signed ? nt.Bits - 1 : nt.Bits) > 53)
                return ctx ? new Ex("@floatFromInt(" + x.T + ")", PrecLit) : new Ex("@as(f64, @floatFromInt(" + x.T + "))", PrecLit);
            if (nt.K == ZK.Float && want.K == ZK.Int)
                return ctx ? new Ex("@intFromFloat(" + x.T + ")", PrecLit) : new Ex("@as(" + want.Name + ", @intFromFloat(" + x.T + "))", PrecLit);
            if (nt.K == ZK.Array && want.K == ZK.Slice) return new Ex("&" + W(x, PrecUn), PrecUn);
            if ((nt.K == ZK.List || nt.K == ZK.Map) && want.K == ZK.Ptr) return new Ex("&" + W(x, PrecUn), PrecUn);
            return x;
        }

        Ex X(IrExpr e, ZType want, bool ctx)
        {
            var raw = Raw(e, want, ctx);
            if (e is IrLiteral || e is IrConst || e is IrListLit || e is IrTupleLit || e is IrMapLit || e is IrSetLit || e is IrConditional || e is IrCast) return raw;
            return Convert(raw, T(e), want, ctx);
        }

        string Postfix(IrExpr e, bool receiver)
        {
            var x = X(e, null, false);
            var t = T(e);
            if (t != null && t.K == ZK.Optional && receiver) return W(x, PrecPost) + ".?";
            return W(x, PrecPost);
        }

        /// <summary>A condition: bool, or Python truthiness of optionals / numbers / lists.</summary>
        string Cond(IrExpr e)
        {
            var t = T(e);
            var x = X(e, ZType.Bool, true);
            if (t == null || t.K == ZK.Bool || t.IsErr) return x.T;
            if (t.K == ZK.Optional) return W(x, PrecCmp + 1) + " != null";
            if (t.IsIntLike || t.IsFloatLike) return W(x, PrecCmp + 1) + " != 0";
            if (t.K == ZK.List) return W(x, PrecPost) + ".items.len > 0";
            if (t.IsString || t.K == ZK.Array || t.K == ZK.Slice) return W(x, PrecPost) + ".len > 0";
            if (t.K == ZK.Map) return W(x, PrecPost) + ".count() > 0";
            Problem("a condition of type " + t);
            return x.T;
        }

        // ================================================================== expressions
        Ex Raw(IrExpr e, ZType want, bool ctx)
        {
            switch (e)
            {
                case null: return new Ex("undefined", PrecLit);
                case IrLiteral l: return Literal(l, want);
                case IrName n: return NameRef(n);
                case IrConst c:
                    {
                        var type = GameApi.GameTypeOfCategory(c.Category) ?? "Entity";
                        var w = want != null && want.K == ZK.Optional ? want.Elem : want;
                        if (w != null && w.K == ZK.Game && w.Name == type) return new Ex("." + c.Name, PrecPost);
                        return new Ex("farm." + type + "." + c.Name, PrecPost);
                    }
                case IrBinary b: return Binary(b, want);
                case IrUnary u:
                    {
                        if (u.Op == IrUnOp.Not) return new Ex("!" + Wrap(Cond(u.Operand), CondPrec(u.Operand), PrecUn), PrecUn);
                        var ot = T(u.Operand);
                        var target = ot != null && ot.K == ZK.Int && !ot.Signed ? ZType.I32 : null;
                        var x = target != null ? Convert(X(u.Operand, null, false), ot, target, false) : X(u.Operand, want, false);
                        if (u.Op == IrUnOp.Plus) return x;
                        return new Ex("-" + W(x, PrecUn), PrecUn);
                    }
                case IrConditional ce:
                    {
                        // len(l) > 0 ? l.pop() : null → l.pop(); x in s ? s.index(x) : null → std.mem.findScalar (Zig's own optionals)
                        if (OptGuard(ce) is IrOpCall gop)
                        {
                            var opEx = gop.Op == IrOp.ListPop ? Op(gop, null, false) : FindScalar(gop);
                            if (IsNullLit(ce.Else)) return opEx;
                            var et = T(ce.Else);
                            return new Ex(W(opEx, PrecOrElse + 1) + " orelse " + W(X(ce.Else, want ?? et, true), PrecOrElse + 1), PrecOrElse);
                        }
                        var t = want ?? T(ce);
                        return new Ex("if (" + Cond(ce.Cond) + ") " + X(ce.Then, t, true).T + " else " + X(ce.Else, t, true).T, PrecIf);
                    }
                case IrCall c: return Call(c, want, ctx);
                case IrOpCall o: return Op(o, want, ctx);
                case IrIndex ix: return Index(ix);
                case IrListLit ll:
                    {
                        // [list(a[0]), list(a[1])]: the frontend's copy of a nested array is just "a" in Zig
                        if (ll.Synthetic && ArrayCopySource(ll) is IrExpr copied) return X(copied, want, ctx);
                        var w = want != null && want.K == ZK.Optional ? want.Elem : want;
                        var lt = T(ll);
                        var et = (w != null && (w.K == ZK.Array || w.K == ZK.Slice) ? w.Elem : null) ?? lt?.Elem;
                        var items = string.Join(", ", ll.Items.Select(i => X(i, et, true).T));
                        if (w != null && w.K == ZK.Array) return new Ex(".{ " + items + " }", PrecPost);
                        if (w != null && w.K == ZK.Slice) return new Ex("&.{ " + items + " }", PrecUn);
                        if (w != null && w.K == ZK.List && ll.Items.Count == 0) return new Ex(ctx ? ".empty" : TypeText(w, null) + ".empty", PrecPost);
                        if (w != null && w.K == ZK.List) Problem("a list literal used as a value");
                        return new Ex("[_]" + TypeText(et, null) + "{ " + items + " }", PrecPost);
                    }
                case IrMapLit _: case IrSetLit _:
                    Problem("a dictionary / set literal used as a value (Zig builds maps with put)");
                    return new Ex(".init(farm.allocator)", PrecPost);
                case IrTupleLit tl:
                    {
                        var t = want ?? T(tl);
                        if (t != null && t.K == ZK.Struct && t.Name == "Position" && tl.Items.Count == 2)
                            return new Ex(".{ .x = " + X(tl.Items[0], ZType.Usize, true).T + ", .y = " + X(tl.Items[1], ZType.Usize, true).T + " }", PrecPost);
                        var its = t != null && t.K == ZK.Tuple ? t.Items : null;
                        return new Ex(".{ " + string.Join(", ", tl.Items.Select((i, k) => X(i, its != null && k < its.Count ? its[k] : null, true).T)) + " }", PrecPost);
                    }
                case IrLambda lam:
                    if (lambdaNames.TryGetValue(lam, out var ln)) return new Ex(ln, PrecLit);
                    Problem("a function value (lambda)");
                    return new Ex("undefined", PrecLit);
                case IrFuncRef fr:
                    if (fr.Function != null)
                    {
                        if (fr.Function.IsExternal) return new Ex(fr.Function.Module + "." + Clean(fr.Function.Name), PrecPost);
                        return new Ex(Name(fr.Function), PrecLit);
                    }
                    Problem("a game function used as a value (" + fr.Builtin + ")");
                    return new Ex("farm." + (ZigApi.OfPython(fr.Builtin).FirstOrDefault()?.ZigName ?? fr.Builtin), PrecPost);
                case IrCast ca: return Cast(ca, want, ctx);
            }
            Problem("unsupported expression " + e.GetType().Name);
            return new Ex("undefined", PrecLit);
        }

        Ex Literal(IrLiteral l, ZType want)
        {
            switch (l.Kind)
            {
                case IrLitKind.Int:
                    {
                        var s = l.Int.ToString(CultureInfo.InvariantCulture);
                        return new Ex(s, l.Int < 0 ? PrecUn : PrecLit);
                    }
                case IrLitKind.Float:
                    {
                        var s = FormatFloat(l.Float);
                        return new Ex(s, l.Float < 0 ? PrecUn : PrecLit);
                    }
                case IrLitKind.Bool: return new Ex(l.Bool ? "true" : "false", PrecLit);
                case IrLitKind.Null: return new Ex("null", PrecLit);
                default:
                    if ((l.Str ?? "").IndexOf('\n') >= 0) Problem("a line break inside a string");
                    return new Ex("\"" + Escape(l.Str) + "\"", PrecLit);
            }
        }

        Ex NameRef(IrName n)
        {
            var s = n.Symbol;
            if (s == null) return new Ex("undefined", PrecLit);
            if (derefNames.TryGetValue(s, out var d)) return new Ex(d, PrecPost);
            if (hoisted.ContainsKey(s)) return new Ex(names[s], PrecLit);
            if (s.Kind == IrSymbolKind.ExternalGlobal || s.Kind == IrSymbolKind.ExternalFunction) return new Ex(s.Module + "." + Clean(s.Name), PrecPost);
            return new Ex(Name(s), PrecLit);
        }

        Ex Cast(IrCast ca, ZType want, bool ctx)
        {
            if (ca.IsTypeAssertion) return X(ca.Operand, want, ctx);
            var ot = T(ca.Operand);
            switch (ca.Target.Kind)
            {
                case IrTypeKind.Int:
                    if (ot != null && ot.IsFloatLike)
                    {
                        var target = want != null && want.K == ZK.Int ? want : ZType.I32;
                        var inner = X(ca.Operand, null, true).T;
                        return ctx && want != null && want.K == ZK.Int ? new Ex("@intFromFloat(" + inner + ")", PrecLit) : new Ex("@as(" + target.Name + ", @intFromFloat(" + inner + "))", PrecLit);
                    }
                    return X(ca.Operand, want, ctx);
                case IrTypeKind.Float:
                    if (ot != null && ot.K == ZK.Int)
                    {
                        var inner = X(ca.Operand, null, true).T;
                        return ctx && want != null && want.IsFloatLike ? new Ex("@floatFromInt(" + inner + ")", PrecLit) : new Ex("@as(f64, @floatFromInt(" + inner + "))", PrecLit);
                    }
                    return X(ca.Operand, want, ctx);
                case IrTypeKind.String:
                    Problem("converting a value to a string (str()) outside a print");
                    return X(ca.Operand, null, ctx);
            }
            return X(ca.Operand, want, ctx);
        }

        // ------------------------------------------------------------------ operators
        Ex Binary(IrBinary b, ZType want)
        {
            var lt = T(b.Left); var rt = T(b.Right);
            switch (b.Op)
            {
                case IrBinOp.And:
                case IrBinOp.Or:
                    {
                        // len(l) == 0 or l.pop() == null / len(l) > 0 and l.pop() != null: the frontend's l.pop() == null
                        if (b.Right is IrBinary pc && (pc.Op == IrBinOp.Eq || pc.Op == IrBinOp.Ne) && pc.Left is IrOpCall pp && pp.Op == IrOp.ListPop && pp.Args.Count == 1 && IsNullLit(pc.Right)
                            && PopGuardList(b.Left, b.Op == IrBinOp.Or) is IrExpr gl && SameSimple(gl, pp.Args[0]) && (b.Op == IrBinOp.Or) == (pc.Op == IrBinOp.Eq))
                            return Binary(pc, want);
                        bool and = b.Op == IrBinOp.And;
                        int p = and ? PrecAnd : PrecOr;
                        var l = Cond(b.Left); var r = Cond(b.Right);
                        return new Ex(Wrap(l, CondPrec(b.Left), p) + (and ? " and " : " or ") + Wrap(r, CondPrec(b.Right), p + 1), p);
                    }
                case IrBinOp.Eq: case IrBinOp.Ne:
                    {
                        bool ne = b.Op == IrBinOp.Ne;
                        bool lNull = b.Left is IrLiteral ll && ll.Kind == IrLitKind.Null, rNull = b.Right is IrLiteral rl && rl.Kind == IrLitKind.Null;
                        if (lNull || rNull)
                        {
                            var other = lNull ? b.Right : b.Left;
                            var ot = T(other);
                            if (ot != null && ot.K != ZK.Optional && ot.K != ZK.Null) Problem("comparing a value that cannot be null with null");
                            return new Ex(W(X(other, null, false), PrecCmp + 1) + (ne ? " != null" : " == null"), PrecCmp);
                        }
                        if ((lt != null && lt.IsString) || (rt != null && rt.IsString))
                            return new Ex((ne ? "!" : "") + "std.mem.eql(u8, " + X(b.Left, ZType.Str, true).T + ", " + X(b.Right, ZType.Str, true).T + ")", ne ? PrecUn : PrecPost);
                        if (lt != null && lt.K == ZK.Optional || rt != null && rt.K == ZK.Optional)
                        {
                            var opt = lt != null && lt.K == ZK.Optional ? b.Left : b.Right;
                            var val = opt == b.Left ? b.Right : b.Left;
                            var otT = T(opt);
                            return new Ex((ne ? "!" : "") + "std.meta.eql(" + X(opt, null, true).T + ", " + X(val, otT, true).T + ")", ne ? PrecUn : PrecPost);
                        }
                        if (lt != null && (lt.K == ZK.Struct || lt.K == ZK.Tuple))
                            return new Ex((ne ? "!" : "") + "std.meta.eql(" + X(b.Left, null, true).T + ", " + X(b.Right, lt, true).T + ")", ne ? PrecUn : PrecPost);
                        if (lt != null && (lt.K == ZK.List || lt.K == ZK.Map || lt.K == ZK.Array)) Problem("comparing collections with ==");
                        return Compare(b, lt, rt);
                    }
                case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge:
                    return Compare(b, lt, rt);
                case IrBinOp.Concat:
                    Problem("string concatenation (Zig joins strings only at compile time; print the parts with std.debug.print)");
                    return new Ex(W(X(b.Left, null, false), PrecAdd) + " ++ " + W(X(b.Right, null, false), PrecAdd + 1), PrecAdd);
            }
            var peer = PeerZ(lt, rt);
            if (peer != null && peer.K == ZK.CInt && want != null && want.IsIntLike && b.Op != IrBinOp.TrueDiv) peer = want;
            if (peer != null && peer.K == ZK.Optional) peer = peer.Elem;
            bool unsigned = peer != null && peer.K == ZK.Int && !peer.Signed;
            Ex Fn(string name) => new Ex(name + "(" + X(b.Left, peer, true).T + ", " + X(b.Right, peer, true).T + ")", PrecPost);
            switch (b.Op)
            {
                case IrBinOp.Add: return Infix(" + ", PrecAdd, b, peer);
                case IrBinOp.Sub: return Infix(" - ", PrecAdd, b, peer);
                case IrBinOp.Mul: return Infix(" * ", PrecMul, b, peer);
                case IrBinOp.TrueDiv:
                    {
                        if (peer != null && peer.IsFloatLike) return Infix(" / ", PrecMul, b, peer);
                        string F(IrExpr x) { if (x is IrLiteral li && li.Kind == IrLitKind.Int) return li.Int.ToString(CultureInfo.InvariantCulture) + ".0"; return "@as(f64, @floatFromInt(" + X(x, null, true).T + "))"; }
                        return new Ex(F(b.Left) + " / " + F(b.Right), PrecMul);
                    }
                case IrBinOp.IntDivTrunc:
                    if (unsigned || BothNonNegLiterals(b)) return Infix(" / ", PrecMul, b, peer);
                    return Fn("@divTrunc");
                case IrBinOp.IntDivFloor:
                    if (unsigned) return Infix(" / ", PrecMul, b, peer);
                    return Fn("@divFloor");
                case IrBinOp.ModTrunc:
                    if (unsigned || BothNonNegLiterals(b)) return Infix(" % ", PrecMul, b, peer);
                    return Fn("@rem");
                case IrBinOp.ModFloor:
                    if (unsigned) return Infix(" % ", PrecMul, b, peer);
                    return Fn("@mod");
                case IrBinOp.Pow:
                    {
                        var t = peer == null || peer.K == ZK.CInt ? ZType.I32 : peer.K == ZK.CFloat ? ZType.F64 : peer;
                        return new Ex("std.math.pow(" + TypeText(t, null) + ", " + X(b.Left, t, true).T + ", " + X(b.Right, t, true).T + ")", PrecPost);
                    }
            }
            Problem("operator " + b.Op);
            return new Ex("undefined", PrecLit);
        }

        Ex Compare(IrBinary b, ZType lt, ZType rt)
        {
            var p = PeerZ(lt, rt);
            if (p != null && p.K == ZK.Game) p = lt != null && lt.K == ZK.Game ? lt : rt;
            var l = X(b.Left, p, false); var r = X(b.Right, p, false);
            string op = b.Op == IrBinOp.Eq ? " == " : b.Op == IrBinOp.Ne ? " != " : b.Op == IrBinOp.Lt ? " < " : b.Op == IrBinOp.Le ? " <= " : b.Op == IrBinOp.Gt ? " > " : " >= ";
            return new Ex(W(l, PrecCmp + 1) + op + W(r, PrecCmp + 1), PrecCmp);
        }

        static bool BothNonNegLiterals(IrBinary b) => b.Left is IrLiteral l && l.Kind == IrLitKind.Int && l.Int >= 0 && b.Right is IrLiteral r && r.Kind == IrLitKind.Int && r.Int > 0;

        int CondPrec(IrExpr e)
        {
            if (e is IrBinary b)
            {
                switch (b.Op) { case IrBinOp.And: return PrecAnd; case IrBinOp.Or: return PrecOr; }
                return PrecCmp;
            }
            var t = T(e);
            if (t != null && t.K != ZK.Bool) return PrecCmp;
            if (e is IrConditional) return PrecIf;
            if (e is IrUnary) return PrecUn;
            return PrecPost;
        }

        Ex Infix(string op, int prec, IrBinary b, ZType peer)
        {
            var l = X(b.Left, peer, false); var r = X(b.Right, peer, false);
            return new Ex(W(l, prec) + op + W(r, prec + 1), prec);
        }

        // ------------------------------------------------------------------ calls
        Ex Call(IrCall c, ZType want, bool ctx)
        {
            switch (c.Kind)
            {
                case IrCallKind.Function:
                    {
                        var fn = c.Function;
                        if (fn == null) return new Ex("undefined", PrecLit);
                        string callee;
                        if (fn.IsExternal) callee = fn.Module + "." + Clean(fn.Name);
                        else if (hoisted.ContainsKey(fn)) callee = names[fn];
                        else callee = Name(fn);
                        var decl = fn.Function;
                        var sig = TagOf(fn)?.Sig;
                        var args = new List<string>();
                        for (int i = 0; i < c.Args.Count; i++)
                        {
                            ZType pt = null;
                            if (decl != null && i < decl.Params.Count)
                            {
                                var ps = decl.Params[i].Symbol;
                                pt = SymZ(ps);
                                if (ps != null && ptrParams.Contains(ps)) pt = new ZType { K = ZK.Ptr, Elem = pt };
                            }
                            else if (sig != null && i < sig.Params.Count) pt = sig.Params[i];
                            if (pt != null && pt.K == ZK.Array && pt.Len < 0) pt = ZType.SliceOf(pt.Elem, true);
                            args.Add(X(c.Args[i], pt, true).T);
                        }
                        if (decl != null && c.Args.Count < decl.Params.Count)
                        {
                            // Zig has no default parameter values: pass them explicitly
                            for (int i = c.Args.Count; i < decl.Params.Count; i++)
                            {
                                var dv = decl.Params[i].Default;
                                if (dv == null) { Problem("a call that leaves out parameters"); break; }
                                args.Add(X(dv, SymZ(decl.Params[i].Symbol), true).T);
                            }
                        }
                        var text = callee + "(" + string.Join(", ", args) + ")";
                        if (errorFns.Contains(fn) || (sig != null && sig.RetError))
                            return tryAllowed ? new Ex("try " + text, PrecUn) : new Ex(text + " catch unreachable", PrecOrElse);
                        return new Ex(text, PrecPost);
                    }
                case IrCallKind.Value:
                    Problem("calling a function value");
                    return new Ex(X(c.Callee, null, false).T + "(" + string.Join(", ", c.Args.Select(a => X(a, null, true).T)) + ")", PrecPost);
            }
            switch (c.Builtin)
            {
                case "print": case "quick_print": return new Ex(PrintText(c), PrecPost);
                case "min": case "max":
                    {
                        var p = T(c);
                        return new Ex("@" + c.Builtin + "(" + string.Join(", ", c.Args.Select(a => X(a, p, false).T)) + ")", PrecPost);
                    }
                case "abs": return new Ex("@abs(" + X(c.Args[0], null, false).T + ")", PrecPost);
            }
            var f = ZigApi.ForArity(c.Builtin, c.Args.Count);
            if (f == null)
            {
                // a prose mention like `vault_try()` names the function without its arguments: print it, it is no program
                if (!proseMention) Problem(c.Builtin + " with " + c.Args.Count + " argument(s)");
                var shortest = ZigApi.OfPython(c.Builtin).FirstOrDefault()?.ZigName ?? GameApi.ToStyle(c.Builtin, NamingStyle.Camel);
                return new Ex("farm." + shortest + "(" + string.Join(", ", c.Args.Select(a => X(a, null, true).T)) + ")", PrecPost);
            }
            var pl = new List<string>();
            if (f.TypeArg)
            {
                var rt = want;
                if (discardedCall == c) rt = ZType.Void;
                if (rt == null) rt = FromIr(types.TypeOf(c), null);
                if (rt == null) { Problem("the result type of " + c.Builtin + "() is unknown"); rt = ZType.Void; }
                pl.Add(TypeText(rt, null));
            }
            int k = 0;
            foreach (var p in f.Params)
            {
                if (p.IsTuple)
                {
                    var rest = c.Args.Skip(k).Select(a => X(a, null, true).T).ToList();
                    pl.Add(".{ " + string.Join(", ", rest) + " }");
                    k = c.Args.Count;
                    break;
                }
                if (k >= c.Args.Count) break;
                var a = c.Args[k++];
                if (p.IsFunc) { pl.Add(FuncArgText(a)); continue; }
                // an empty map for an anytype parameter (farm.simulate's unlocks / globals)
                if (p.T == null && (a is IrMapLit em && em.Keys.Count == 0 || a is IrSetLit es && es.Items.Count == 0)) { pl.Add(".{}"); continue; }
                pl.Add(X(a, p.T, true).T);
            }
            return new Ex("farm." + f.ZigName + "(" + string.Join(", ", pl) + ")", PrecPost);
        }

        IrCall discardedCall;

        string FuncArgText(IrExpr a)
        {
            switch (a)
            {
                case IrFuncRef fr when fr.Function != null:
                    if (fr.Function.Function != null && fr.Function.Function.Params.Count > 0) Problem("a drone function with parameters");
                    return fr.Function.IsExternal ? fr.Function.Module + "." + Clean(fr.Function.Name) : Name(fr.Function);
                case IrName n when n.Symbol != null && (n.Symbol.Kind == IrSymbolKind.Function || n.Symbol.Kind == IrSymbolKind.ExternalFunction || hoisted.ContainsKey(n.Symbol)):
                    if (n.Symbol.Function != null && n.Symbol.Function.Params.Count > 0) Problem("a drone function with parameters");
                    if (hoisted.TryGetValue(n.Symbol, out var hl) && hl.Params.Count > 0) Problem("a drone function with parameters");
                    return NameRef(n).T;
                case IrLambda lam when lambdaNames.TryGetValue(lam, out var ln): return ln;
            }
            Problem("a drone task that is not a function name");
            return X(a, null, true).T;
        }

        /// <summary>print(...) → std.debug.print("fmt\n", .{ args }).</summary>
        string PrintText(IrCall c)
        {
            var fmt = new StringBuilder();
            var args = new List<string>();
            bool first = true;
            void Piece(IrExpr e)
            {
                if (e is IrBinary b && b.Op == IrBinOp.Concat) { Piece(b.Left); Piece(b.Right); return; }
                if (e is IrLiteral l && l.Kind == IrLitKind.String) { fmt.Append((l.Str ?? "").Replace("{", "{{").Replace("}", "}}")); return; }
                if (e is IrCast ca && !ca.IsTypeAssertion && ca.Target.Kind == IrTypeKind.String) e = ca.Operand;
                var t = T(e);
                string spec;
                string text = X(e, null, false).T;
                if (t == null) { Problem("printing a value of unknown type"); spec = "any"; }
                else switch (t.K)
                    {
                        case ZK.Int: case ZK.CInt: case ZK.Float: case ZK.CFloat: spec = "d"; break;
                        case ZK.Slice: case ZK.StrLit: spec = t.IsString ? "s" : "any"; break;
                        case ZK.Bool: case ZK.Game: case ZK.Struct: case ZK.Tuple: spec = ""; break;
                        case ZK.Optional: case ZK.Null: case ZK.Array: spec = "any"; break;
                        case ZK.List: spec = "any"; text = W(X(e, null, false), PrecPost) + ".items"; break;
                        default: Problem("printing a value of type " + t); spec = "any"; break;
                    }
                fmt.Append('{').Append(spec).Append('}');
                args.Add(text);
            }
            foreach (var a in c.Args)
            {
                if (!first) fmt.Append(' ');
                first = false;
                Piece(a);
            }
            var name = c.Builtin == "print" ? "std.debug.print" : "farm.quickPrint";
            var lit = "\"" + Escape(fmt.ToString()) + "\\n\"";
            return name + "(" + lit + ", .{" + (args.Count > 0 ? " " + string.Join(", ", args) + " " : "") + "})";
        }

        string TryWrap(string text) => tryAllowed ? "try " + text : text + " catch unreachable";

        // ------------------------------------------------------------------ operations
        Ex Op(IrOpCall o, ZType want, bool ctx)
        {
            var a = o.Args;
            var rt0 = a.Count > 0 ? T(a[0]) : null;
            var rt = Deref(rt0);
            string R() => Postfix(a[0], true);
            Ex M(string s) => new Ex(s, PrecPost);
            ZType Elem() => rt?.Elem;
            switch (o.Op)
            {
                case IrOp.Len:
                    if (rt == null) { Problem("len() of a value of unknown type"); return M(R() + ".len"); }
                    if (rt.K == ZK.List) return M(R() + ".items.len");
                    if (rt.K == ZK.Map) return M(R() + ".count()");
                    return M(R() + ".len");
                case IrOp.IsEmpty:
                    {
                        var len = Op(new IrOpCall(IrOp.Len, a[0]), null, false).T;
                        return new Ex(len + " == 0", PrecCmp);
                    }
                case IrOp.Contains:
                    if (rt == null) { Problem("'in' on a value of unknown type"); return M(R() + ".contains(" + X(a[1], null, true).T + ")"); }
                    if (rt.K == ZK.Map) return M(R() + ".contains(" + X(a[1], rt.Key, true).T + ")");
                    if (rt.K == ZK.List) return new Ex("std.mem.findScalar(" + TypeText(rt.Elem, null) + ", " + R() + ".items, " + X(a[1], rt.Elem, true).T + ") != null", PrecCmp);
                    if (rt.K == ZK.Array || rt.K == ZK.Slice && !rt.IsString) return new Ex("std.mem.findScalar(" + TypeText(rt.Elem, null) + ", " + (rt.K == ZK.Array ? "&" + R() : R()) + ", " + X(a[1], rt.Elem, true).T + ") != null", PrecCmp);
                    Problem("'in' on a string");
                    return M("false");
                case IrOp.ListAppend: return M(R() + ".append(farm.allocator, " + X(a[1], Elem(), true).T + ")");
                case IrOp.ListInsert: return M(R() + ".insert(farm.allocator, " + X(a[1], ZType.Usize, true).T + ", " + X(a[2], Elem(), true).T + ")");
                case IrOp.ListPop:
                    if (rt != null && rt.K != ZK.List) Problem("pop() on a value that is not a list");
                    if (a.Count == 1) return M(R() + ".pop()");
                    return M(R() + ".orderedRemove(" + X(a[1], ZType.Usize, true).T + ")");
                case IrOp.ListSet: return M(R() + ".items[" + X(a[1], ZType.Usize, true).T + "] = " + X(a[2], Elem(), true).T);
                case IrOp.RemoveValue:
                    if (rt != null && rt.K == ZK.Map) return M(R() + ".remove(" + X(a[1], rt.Key, true).T + ")");
                    return M(R() + ".orderedRemove(std.mem.findScalar(" + TypeText(Elem(), null) + ", " + R() + ".items, " + X(a[1], Elem(), true).T + ").?)");
                case IrOp.RemoveValueIfPresent:
                    if (rt != null && rt.K == ZK.Map) return M(R() + ".remove(" + X(a[1], rt.Key, true).T + ")");
                    Problem("removing a value from a list if it is present");
                    return M(R() + ".orderedRemove(std.mem.findScalar(" + TypeText(Elem(), null) + ", " + R() + ".items, " + X(a[1], Elem(), true).T + ").?)");
                case IrOp.Clear: return M(R() + ".clearRetainingCapacity()");
                case IrOp.SetAdd: return M(R() + ".put(" + X(a[1], rt?.Key, true).T + ", {})");
                case IrOp.MapPut: return M(R() + ".put(" + X(a[1], rt?.Key, true).T + ", " + X(a[2], rt?.Elem, true).T + ")");
                case IrOp.MapGetOrNull: return M(R() + ".get(" + X(a[1], rt?.Key, true).T + ")");
                case IrOp.MapGetOrDefault: return new Ex(R() + ".get(" + X(a[1], rt?.Key, true).T + ") orelse " + W(X(a[2], rt?.Elem, true), PrecOrElse + 1), PrecOrElse);
                case IrOp.MapRemove: case IrOp.MapRemoveIfPresent:
                    return M(R() + ".remove(" + X(a[1], rt?.Key, true).T + ")");
                case IrOp.ListCopy:
                    if (rt != null && rt.K == ZK.Array) return M(R());   // a Zig array is copied by assignment
                    if (rt != null && rt.K == ZK.List) return new Ex(TryWrap(R() + ".clone(farm.allocator)"), tryAllowed ? PrecUn : PrecOrElse);
                    Problem("converting a collection to a list");
                    return M(R());
                case IrOp.SetCopy: case IrOp.MapCopy:
                    Problem("copying a set or map");
                    return M(R());
                case IrOp.MapKeys: case IrOp.MapValues:
                    Problem("map.keys() / map.values() used as a value");
                    return M(R());
                case IrOp.ArrayNew:
                    {
                        if (!(a[0] is IrLiteral)) Problem("an array whose length is not a constant");
                        var w = want != null && want.K == ZK.Array ? want : T(o);
                        return M("@splat(" + X(a[1], w?.Elem, true).T + ")");
                    }
                case IrOp.ArrayNew2:
                    {
                        if (!(a[0] is IrLiteral) || !(a[1] is IrLiteral)) Problem("an array whose length is not a constant");
                        var w = want != null && want.K == ZK.Array ? want : T(o);
                        return M("@splat(@splat(" + X(a[2], w?.Elem?.Elem, true).T + "))");
                    }
                case IrOp.Floor: case IrOp.Ceil: case IrOp.Round: case IrOp.Sqrt:
                    {
                        var name = o.Op == IrOp.Floor ? "@floor" : o.Op == IrOp.Ceil ? "@ceil" : o.Op == IrOp.Round ? "@round" : "@sqrt";
                        var at = T(a[0]);
                        string arg = at != null && at.K == ZK.Int ? "@as(f64, @floatFromInt(" + X(a[0], null, true).T + "))" : at != null && at.K == ZK.CInt ? "@as(f64, " + X(a[0], null, true).T + ")" : X(a[0], ZType.F64, true).T;
                        var text = name + "(" + arg + ")";
                        if (o.Op != IrOp.Sqrt && o.Type != null && o.Type.IsInt)
                        {
                            if (ctx && want != null && want.K == ZK.Int) return M(text);
                            return M("@as(" + (want != null && want.K == ZK.Int ? want.Name : "i32") + ", " + text + ")");
                        }
                        return M(text);
                    }
                case IrOp.PosX: case IrOp.PosY:
                    {
                        var field = o.Op == IrOp.PosX ? ".x" : ".y";
                        if (a[0] is IrOpCall cp && cp.Op == IrOp.CompanionPos) return M(Postfix(cp.Args[0], true) + field);
                        return M(R() + field);
                    }
                case IrOp.CompanionEntity: return M(R() + ".entity");
                case IrOp.CompanionPos: { var c = R(); return M(".{ .x = " + c + ".x, .y = " + c + ".y }"); }
                case IrOp.ListIndexOf:
                    Problem("index() of a list value (Zig's std.mem.findScalar returns null instead of -1)");
                    return M("std.mem.findScalar(" + TypeText(Elem(), null) + ", " + R() + ".items, " + X(a[1], Elem(), true).T + ")");
            }
            Problem("operation " + o.Op);
            return M("undefined");
        }

        Ex Index(IrIndex ix)
        {
            var tt = Deref(T(ix.Target));
            if (ix.BaseOffset != 0) Problem("1-based indexing");
            var target = Postfix(ix.Target, true);
            if (tt == null) { Problem("indexing a value of unknown type"); return new Ex(target + "[" + X(ix.Index, ZType.Usize, true).T + "]", PrecPost); }
            switch (tt.K)
            {
                case ZK.List: return new Ex(target + ".items[" + X(ix.Index, ZType.Usize, true).T + "]", PrecPost);
                case ZK.Array: case ZK.Slice:
                    if (tt.IsString) { Problem("indexing a string"); break; }
                    return new Ex(target + "[" + X(ix.Index, ZType.Usize, true).T + "]", PrecPost);
                case ZK.Map: return new Ex(target + ".get(" + X(ix.Index, tt.Key, true).T + ").?", PrecPost);
                case ZK.Tuple:
                    if (!(ix.Index is IrLiteral)) Problem("indexing a tuple with a computed index");
                    return new Ex(target + "[" + X(ix.Index, null, true).T + "]", PrecPost);
                case ZK.Struct:
                    if (ix.Index is IrLiteral li && li.Kind == IrLitKind.Int)
                    {
                        if (tt.Name == "Position") return new Ex(target + (li.Int == 0 ? ".x" : ".y"), PrecPost);
                        if (tt.Name == "Companion") return li.Int == 0 ? new Ex(target + ".entity", PrecPost) : new Ex(".{ .x = " + target + ".x, .y = " + target + ".y }", PrecPost);
                    }
                    Problem("indexing a " + tt);
                    break;
                default:
                    Problem("indexing a value of type " + tt);
                    break;
            }
            return new Ex(target + "[" + X(ix.Index, ZType.Usize, true).T + "]", PrecPost);
        }

        // ================================================================== declarations / assignments
        HashSet<IrSymbol> nullCompared;
        /// <summary>Variables compared with None somewhere: they hold an optional (?T) in Zig.</summary>
        HashSet<IrSymbol> NullCompared
        {
            get
            {
                if (nullCompared != null) return nullCompared;
                nullCompared = new HashSet<IrSymbol>();
                foreach (var n in IrWalk.Descendants(mod))
                    if (n is IrBinary b && (b.Op == IrBinOp.Eq || b.Op == IrBinOp.Ne))
                    {
                        if (IsNullLit(b.Right) && NameSym(b.Left) != null) nullCompared.Add(NameSym(b.Left));
                        else if (IsNullLit(b.Left) && NameSym(b.Right) != null) nullCompared.Add(NameSym(b.Right));
                    }
                return nullCompared;
            }
        }

        void VarDecl(IrVarDecl v)
        {
            var s = v.Symbol;
            if (s != null && hoisted.ContainsKey(s)) return;
            if (v.Init is IrLambda)
            {
                Problem("a nested function that uses the variables around it (a closure)");
                Line("// " + (s?.Name ?? "f") + ": a closure cannot be written in Zig", v);
                return;
            }
            if (v.Init == null && s != null && pendingCandidate.Contains(v)) { pendingDecls.Add(s); return; }
            var t = SymZ(s);
            if (t == null && v.Init != null) t = T(v.Init);
            if (t != null && t.K == ZK.CInt) t = ZType.I32;
            if (t != null && t.K == ZK.CFloat) t = ZType.F64;
            if (t == null && v.Init is IrListLit el && el.Items.Count == 0) { t = ZType.ListOf(ZType.I32); if (s != null) { forcedTypes[s] = t; symCache.Remove(s); } }   // [] of unknown items
            if (t == null) { Problem("the type of " + s?.Name + " is unknown"); t = ZType.I32; }
            if (s != null && t.K != ZK.Optional && t.K != ZK.Void && NullCompared.Contains(s)) { t = ZType.Opt(t); forcedTypes[s] = t; symCache.Remove(s); }
            if (t.K == ZK.Fn) Problem("a function stored in a variable (" + s?.Name + ")");
            bool isVar = s != null && (reassigned.Contains(s) || (mutated.Contains(s) && (t.K == ZK.List || t.K == ZK.Map || t.K == ZK.Array)));
            if (s != null && TagOf(s) != null && fromZig && !s.IsConst) isVar = true;
            var init = v.Init;
            string initText;
            bool annotate;
            var after = new List<string>();
            if (init == null)
            {
                initText = t.K == ZK.List ? ".empty" : t.K == ZK.Map ? ".init(farm.allocator)" : "undefined";
                annotate = true;
                isVar = isVar || t.K == ZK.List || t.K == ZK.Map;
            }
            else initText = DeclInit(init, t, s, out annotate, after);
            var name = Declare(s);
            for (int i = 0; i < after.Count; i++) after[i] = after[i].Replace(Self, name);
            if (after.Count > 0 && (t.K == ZK.List || t.K == ZK.Map)) isVar = true;
            if (isVar && t.K != ZK.Map) annotate = true;
            if (isVar && t.K == ZK.Map && init == null) annotate = true;
            Line((isVar ? "var " : "const ") + name + (annotate ? ": " + TypeText(t, s) : "") + " = " + initText + ";", v);
            foreach (var a in after) Line(a);
            if (s != null && !refs.ContainsKey(s) && !Options.Snippet && after.Count == 0) Line("_ = " + name + ";");
        }

        /// <summary>The initial value of a declaration; collection literals become .empty / .init(...) plus statements.</summary>
        string DeclInit(IrExpr init, ZType t, IrSymbol s, out bool annotate, List<string> after)
        {
            annotate = false;
            if (init is IrListLit cl && cl.Synthetic && ArrayCopySource(cl) is IrExpr copied) init = copied;   // a copied nested array
            switch (init)
            {
                case IrListLit ll when t.K == ZK.List:
                    annotate = true;
                    foreach (var it in ll.Items) after.Add(TryWrap(Self + ".append(farm.allocator, " + X(it, t.Elem, true).T + ")") + ";");
                    return ".empty";
                case IrListLit ll2 when t.K == ZK.Array:
                    {
                        if (ll2.Items.Count == 0) { annotate = true; return ".{}"; }
                        var items = string.Join(", ", ll2.Items.Select(i => X(i, t.Elem, true).T));
                        return "[_]" + TypeText(t.Elem, null) + "{ " + items + " }";
                    }
                case IrMapLit ml:
                    if (t.K != ZK.Map) { Problem("a dictionary literal for a non-map variable"); return "undefined"; }
                    for (int i = 0; i < ml.Keys.Count; i++) after.Add(TryWrap(Self + ".put(" + X(ml.Keys[i], t.Key, true).T + ", " + X(ml.Values[i], t.Elem, true).T + ")") + ";");
                    return TypeText(t, s) + ".init(farm.allocator)";
                case IrSetLit sl:
                    if (t.K != ZK.Map) { Problem("a set literal for a non-set variable"); return "undefined"; }
                    foreach (var it in sl.Items) after.Add(TryWrap(Self + ".put(" + X(it, t.Key, true).T + ", {})") + ";");
                    return TypeText(t, s) + ".init(farm.allocator)";
                case IrOpCall o when (o.Op == IrOp.ArrayNew || o.Op == IrOp.ArrayNew2):
                    annotate = true;
                    return X(o, t, true).T;
                case IrConditional _:
                    annotate = true;
                    return X(init, t, true).T;
                case IrLiteral l when l.Kind == IrLitKind.Null:
                    annotate = true;
                    return "null";
                case IrTupleLit _ when t.K == ZK.Struct:
                    annotate = true;
                    return X(init, t, true).T;
            }
            var nt = T(init);
            if (nt == null || !nt.Same(t)) annotate = true;
            if (nt != null && nt.K == ZK.CInt && t.K == ZK.Int && t.Name == "i32" && !(s != null && reassigned.Contains(s))) annotate = false;
            if (nt != null && nt.K == ZK.CFloat && t.K == ZK.Float && !(s != null && reassigned.Contains(s))) annotate = false;
            if (nt != null && nt.IsString && t.IsString) annotate = false;
            if (t.K == ZK.Optional && nt != null && nt.K != ZK.Optional) annotate = true;
            if (t.K == ZK.Array && nt != null && nt.K == ZK.Array) annotate = false;
            var text = X(init, t, true).T;
            // @intCast / @intFromFloat / @floatFromInt take their result type from the declaration
            if (!annotate && (text.StartsWith("@intCast(") || text.StartsWith("@intFromFloat(") || text.StartsWith("@floatFromInt("))) annotate = true;
            return text;
        }

        void Assign(IrAssign a)
        {
            if (a.Target is IrTupleLit tl) { Unpack(tl, a); return; }
            var text = AssignText(a);
            if (text != null) Line(text + ";", a);
        }

        string AssignText(IrAssign a)
        {
            if (a.Target is IrIndex ix)
            {
                var tt = Deref(T(ix.Target));
                if (tt != null && tt.K == ZK.Map)
                {
                    if (a.Op != null) Problem("a compound assignment to a dictionary entry");
                    return TryWrap(Postfix(ix.Target, true) + ".put(" + X(ix.Index, tt.Key, true).T + ", " + X(a.Value, tt.Elem, true).T + ")");
                }
                if (tt != null && tt.K == ZK.Tuple) Problem("assigning to a tuple item");
            }
            if (a.Target is IrTupleLit) { Problem("unpacking in a loop update"); return null; }
            var target = X(a.Target, null, false).T;
            var t = T(a.Target);
            if (a.TruncateToInt) Problem("a compound assignment that truncates to an integer");
            if (a.Op == null) return target + " = " + X(a.Value, t, true).T;
            bool unsigned = t != null && t.K == ZK.Int && !t.Signed;
            string Fn(string f) => target + " = " + f + "(" + target + ", " + X(a.Value, t, true).T + ")";
            switch (a.Op.Value)
            {
                case IrBinOp.Add: return target + " += " + X(a.Value, t, true).T;
                case IrBinOp.Sub: return target + " -= " + X(a.Value, t, true).T;
                case IrBinOp.Mul: return target + " *= " + X(a.Value, t, true).T;
                case IrBinOp.TrueDiv:
                    if (t == null || !t.IsFloatLike) Problem("dividing an integer variable as a real number");
                    return target + " /= " + X(a.Value, t, true).T;
                case IrBinOp.IntDivTrunc: return unsigned ? target + " /= " + X(a.Value, t, true).T : Fn("@divTrunc");
                case IrBinOp.IntDivFloor: return unsigned ? target + " /= " + X(a.Value, t, true).T : Fn("@divFloor");
                case IrBinOp.ModTrunc: return unsigned ? target + " %= " + X(a.Value, t, true).T : Fn("@rem");
                case IrBinOp.ModFloor: return unsigned ? target + " %= " + X(a.Value, t, true).T : Fn("@mod");
                case IrBinOp.Pow: return target + " = std.math.pow(" + TypeText(t, null) + ", " + target + ", " + X(a.Value, t, true).T + ")";
                case IrBinOp.Concat:
                    Problem("string concatenation with +=");
                    return target + " = " + target + " ++ " + X(a.Value, null, false).T;
            }
            Problem("assignment operator " + a.Op);
            return target + " = " + X(a.Value, t, true).T;
        }

        void Unpack(IrTupleLit targets, IrAssign a)
        {
            var vt = T(a.Value);
            if ((vt == null || vt.K == ZK.Void) && a.Value is IrCall uc && uc.Kind == IrCallKind.Builtin && targets.Items.Count == 2 && ZigApi.ForArity(uc.Builtin, uc.Args.Count)?.TypeArg == true)
                vt = ZType.StructT("Position");   // x, y = measure(): the position of the next apple / treasure
            if (vt != null && vt.K == ZK.Struct && !(a.Value is IrTupleLit) && UnpackStruct(targets, a, vt)) return;
            if (vt != null && vt.K == ZK.Struct && !(a.Value is IrTupleLit)) { Problem("unpacking a " + vt + " (Zig structs cannot be destructured)"); Line("// unpacking is not supported here", a); return; }
            if (targets.Items.Any(i => i is IrTupleLit)) { Problem("nested unpacking"); Line("// unpacking is not supported here", a); return; }
            var parts = new List<string>();
            var fresh = new HashSet<int>();
            var discards = new List<string>();
            var vlit = a.Value as IrTupleLit;
            for (int i = 0; i < targets.Items.Count; i++)
            {
                var it = targets.Items[i];
                var s = NameSym(it);
                if (s != null && pendingDecls.Contains(s))
                {
                    pendingDecls.Remove(s);
                    bool v = assignCount.TryGetValue(s, out var n) && n > 1;
                    fresh.Add(i);
                    // a fresh name takes the type of its value (no conversion inside the tuple)
                    var nt = vlit != null && i < vlit.Items.Count ? T(vlit.Items[i]) : null;
                    if (nt != null && nt.K != ZK.CInt && nt.K != ZK.CFloat && !nt.IsErr && (nt.K != ZK.Optional || true)) { forcedTypes[s] = nt; symCache.Remove(s); }
                    var dn = Declare(s);
                    parts.Add((v ? "var " : "const ") + dn);
                    if (!v && Unused(s) && !Options.Snippet) discards.Add("_ = " + dn + ";");
                }
                else parts.Add(X(it, null, false).T);
            }
            string value;
            if (vlit != null) value = ".{ " + string.Join(", ", vlit.Items.Select((x, k) => X(x, k < targets.Items.Count && !fresh.Contains(k) ? T(targets.Items[k]) : null, true).T)) + " }";
            else value = X(a.Value, null, true).T;
            Line(string.Join(", ", parts) + " = " + value + ";", a);
            foreach (var d in discards) Line(d);
        }

        /// <summary>
        /// x, y = position / entity, (x, y) = companion: Zig structs cannot be destructured, so the value goes into a
        /// constant and each name reads its field.
        /// </summary>
        bool UnpackStruct(IrTupleLit targets, IrAssign a, ZType vt)
        {
            var fields = new List<(IrExpr Target, string Field, ZType T)>();
            if (vt.Name == "Position" && targets.Items.Count == 2)
            {
                fields.Add((targets.Items[0], "x", ZType.Usize));
                fields.Add((targets.Items[1], "y", ZType.Usize));
            }
            else if (vt.Name == "Companion" && targets.Items.Count == 2 && targets.Items[1] is IrTupleLit xy && xy.Items.Count == 2)
            {
                fields.Add((targets.Items[0], "entity", ZType.GameT("Entity")));
                fields.Add((xy.Items[0], "x", ZType.Usize));
                fields.Add((xy.Items[1], "y", ZType.Usize));
            }
            else return false;
            if (fields.Any(f => !(f.Target is IrName))) return false;
            var tmpSym = new IrSymbol(vt.Name == "Companion" ? "companion" : "pos", IrSymbolKind.Local, IrType.Unknown);
            var tmp = Declare(tmpSym);
            var value = X(a.Value, vt, true).T;
            Line("const " + tmp + " = " + value + ";", a);
            foreach (var (target, field, ft) in fields)
            {
                var s = NameSym(target);
                if (s != null && pendingDecls.Contains(s))
                {
                    pendingDecls.Remove(s);
                    bool v = assignCount.TryGetValue(s, out var n) && n > 1;
                    forcedTypes[s] = ft; symCache.Remove(s);
                    var name = Declare(s);
                    Line((v ? "var " : "const ") + name + (v ? ": " + TypeText(ft, s) : "") + " = " + tmp + "." + field + ";");
                    if (!v && Unused(s) && !Options.Snippet) Line("_ = " + name + ";");
                }
                else
                {
                    var tt = T(target);
                    var rhs = Convert(new Ex(tmp + "." + field, PrecPost), ft, tt, true).T;
                    Line(X(target, null, false).T + " = " + rhs + ";");
                }
            }
            return true;
        }

        /// <summary>The symbol is only ever assigned (every reference is an assignment target).</summary>
        bool Unused(IrSymbol s) => !refs.TryGetValue(s, out var r) || r <= (assignCount.TryGetValue(s, out var n) ? n : 0);

        readonly HashSet<IrSymbol> pendingDecls = new HashSet<IrSymbol>();
        readonly HashSet<IrVarDecl> pendingCandidate = new HashSet<IrVarDecl>();
        readonly Dictionary<IrSymbol, int> assignCount = new Dictionary<IrSymbol, int>();

        void ExprStmt(IrExprStmt es)
        {
            var e = es.Expr;
            switch (e)
            {
                case IrCall c:
                    {
                        if (c.Kind == IrCallKind.Builtin && (c.Builtin == "print" || c.Builtin == "quick_print")) { Line(PrintText(c) + ";", es); return; }
                        var t = T(c);
                        discardedCall = c;
                        var x = X(c, null, false).T;
                        discardedCall = null;
                        if (t == null || t.K == ZK.Void || t.K == ZK.NoReturn) { Line(x + ";", es); return; }
                        if (c.Kind == IrCallKind.Builtin && ZigApi.ForArity(c.Builtin, c.Args.Count)?.TypeArg == true) { Line(x + ";", es); return; }
                        Line("_ = " + x + ";", es);
                        return;
                    }
                case IrOpCall o:
                    {
                        var x = Op(o, null, false).T;
                        switch (o.Op)
                        {
                            case IrOp.ListAppend: case IrOp.ListInsert: case IrOp.SetAdd: case IrOp.MapPut: Line(TryWrap(x) + ";", es); return;
                            case IrOp.ListSet: case IrOp.Clear: Line(x + ";", es); return;
                            case IrOp.ListPop: case IrOp.RemoveValue: case IrOp.RemoveValueIfPresent: case IrOp.MapRemove: case IrOp.MapRemoveIfPresent: Line("_ = " + x + ";", es); return;
                        }
                        Problem("an expression statement without effect");
                        Line("_ = " + x + ";", es);
                        return;
                    }
            }
            Problem("an expression statement without effect");
            Line("_ = " + X(e, null, false).T + ";", es);
        }

        /// <summary>l of the frontend's pop guard: "len(l) > 0" / "not isEmpty(l)" (empty: false), "isEmpty(l)" / "len(l) == 0" (empty: true).</summary>
        static IrExpr PopGuardList(IrExpr cond, bool empty)
        {
            if (empty)
            {
                if (cond is IrOpCall ie && ie.Op == IrOp.IsEmpty) return ie.Args[0];
                if (cond is IrBinary eb && eb.Op == IrBinOp.Eq && eb.Left is IrOpCall el && el.Op == IrOp.Len && eb.Right is IrLiteral ez && ez.Kind == IrLitKind.Int && ez.Int == 0) return el.Args[0];
                return null;
            }
            if (cond is IrBinary b && b.Op == IrBinOp.Gt && b.Left is IrOpCall len && len.Op == IrOp.Len && b.Right is IrLiteral z && z.Kind == IrLitKind.Int && z.Int == 0) return len.Args[0];
            if (cond is IrUnary u && u.Op == IrUnOp.Not && u.Operand is IrOpCall ne && ne.Op == IrOp.IsEmpty) return ne.Args[0];
            return null;
        }

        /// <summary>The statement of "if (len(l) > 0) { l.pop(); }" (what the frontend makes of _ = l.pop();), else null.</summary>
        static IrStmt PopGuardStmt(IrIf f)
        {
            if (f.Then.Stmts.Count != 1 || !(f.Then.Stmts[0] is IrExprStmt es) || !(es.Expr is IrOpCall op) || op.Op != IrOp.ListPop || op.Args.Count != 1) return null;
            var l = PopGuardList(f.Cond, false);
            return l != null && SameSimple(l, op.Args[0]) ? es : null;
        }

        /// <summary>a for list(a) / a synthetic [copy(a[0]), copy(a[1]), ...]; null otherwise.</summary>
        static IrExpr ArrayCopySource(IrExpr e)
        {
            if (e is IrOpCall o && o.Op == IrOp.ListCopy && o.Args.Count == 1) return o.Args[0];
            if (!(e is IrListLit ll) || !ll.Synthetic || ll.Items.Count == 0) return null;
            IrExpr src = null;
            for (int i = 0; i < ll.Items.Count; i++)
            {
                var s = ArrayCopySource(ll.Items[i]);
                if (!(s is IrIndex ix) || !(ix.Index is IrLiteral li) || li.Kind != IrLitKind.Int || li.Int != i) return null;
                if (src != null && !SameSimple(src, ix.Target)) return null;
                src = ix.Target;
            }
            return src;
        }

        static bool SameSimple(IrExpr a, IrExpr b)
        {
            switch (a)
            {
                case IrName n1: return b is IrName n2 && n1.Symbol == n2.Symbol;
                case IrLiteral l1: return b is IrLiteral l2 && l1.Kind == l2.Kind && l1.Int == l2.Int && l1.Float == l2.Float && l1.Bool == l2.Bool && l1.Str == l2.Str;
                case IrConst c1: return b is IrConst c2 && c1.Category == c2.Category && c1.Name == c2.Name;
                case IrIndex i1: return b is IrIndex i2 && SameSimple(i1.Target, i2.Target) && SameSimple(i1.Index, i2.Index);
            }
            return false;
        }

        /// <summary>
        /// The pop / indexOf of a guarded optional (what the Zig frontend makes of list.pop() and std.mem.indexOfScalar):
        /// "len(l) > 0 ? l.pop() : d" / "not isEmpty(l) ? ..." / "x in s ? index(s, x) : d"; null for other conditionals.
        /// </summary>
        static IrOpCall OptGuard(IrConditional ce)
        {
            if (!(ce.Then is IrOpCall op)) return null;
            if (op.Op == IrOp.ListPop && op.Args.Count == 1)
            {
                var l = PopGuardList(ce.Cond, false);
                return l != null && SameSimple(l, op.Args[0]) ? op : null;
            }
            if (op.Op == IrOp.ListIndexOf && ce.Cond is IrOpCall c && c.Op == IrOp.Contains && SameSimple(c.Args[0], op.Args[0]) && SameSimple(c.Args[1], op.Args[1])) return op;
            return null;
        }

        /// <summary>std.mem.findScalar(T, s, x) for ListIndexOf(s, x) whose -1 the frontend already turned into null.</summary>
        Ex FindScalar(IrOpCall o)
        {
            var st = Deref(T(o.Args[0]));
            var recv = Postfix(o.Args[0], true);
            var elem = st?.Elem;
            string s = st == null ? recv : st.K == ZK.List ? recv + ".items" : st.K == ZK.Array ? "&" + recv : recv;
            if (st == null) Problem("index() of a value of unknown type");
            return new Ex("std.mem.findScalar(" + TypeText(elem, null) + ", " + s + ", " + X(o.Args[1], elem, true).T + ")", PrecPost);
        }
    }
}
