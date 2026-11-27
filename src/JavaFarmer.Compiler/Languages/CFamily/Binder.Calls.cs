using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler.CFamily
{
    internal sealed partial class Binder
    {
        // ================================================================== expression statements

        void ExprStatement(ExprStmt es, List<IrStmt> outList) => ExprStatementCore(es.E, Sp(es), outList, false);

        void ExprStatementCore(Expr e, Span span, List<IrStmt> outList, bool forHeader)
        {
            switch (e)
            {
                case AssignExpr a: { var x = Assign(a, span); if (x != null) outList.Add(x); return; }
                case IncDecExpr id: { var x = IncDec(id, span); if (x != null) outList.Add(x); return; }
                case CallExpr c:
                    if (TryOutputCall(c, span, outList)) return;
                    if (TryStatementCall(c, span, outList)) return;
                    {
                        var b = BindExpr(c);
                        if (b.T.IsError) return;
                        outList.Add(new IrExprStmt { Expr = b.E, Span = span });
                        return;
                    }
                case BinaryExpr be when be.Op == "<<" && Cpp && IsStreamChain(be):
                    StreamOutput(be, span, outList);
                    return;
            }
            var v = BindExpr(e);
            if (v.T.IsError) return;
            if (v.E is IrCall || v.E is IrOpCall) { outList.Add(new IrExprStmt { Expr = v.E, Span = span }); return; }
            Err(C ? "statement with no effect" : "statement has no effect", e);
        }

        IrStmt IncDec(IncDecExpr id, Span span)
        {
            var t = BindExpr(id.Target);
            if (t.T.IsError) return null;
            if (!CheckAssignable(t, id.Target, id.Op == "++" ? "increment" : "decrement")) return null;
            if (!t.T.IsNumeric)
            {
                Err(t.T.Kind == CK.Bool ? "using a bool as a number is not supported in JavaFarmer" : (C ? "wrong type argument to " + (id.Op == "++" ? "increment" : "decrement") : "no match for 'operator" + id.Op + "' (operand type is '" + Show(t.T) + "')"), id);
                return null;
            }
            var opSpan = Sp(id.OpStart, id.OpEnd);
            var one = IrLiteral.OfInt(1); one.Span = opSpan;
            return new IrAssign { Target = t.E, Op = id.Op == "++" ? IrBinOp.Add : IrBinOp.Sub, Value = one, OpSpan = opSpan, IsIncDec = true, Prefix = id.Prefix, Span = span };
        }

        bool CheckAssignable(BE t, Expr target, string what)
        {
            var s = t.Sym;
            if (s != null && s.Kind == SymKind.Define) { Err("lvalue required as " + (what == "assignment" ? "left operand of assignment" : what + " operand"), target); return false; }
            if (!t.LValue) { Err("lvalue required as " + (what == "assignment" ? "left operand of assignment" : what + " operand"), target); return false; }
            if (s != null && s.Const && t.E is IrName)
            {
                Err(C ? "assignment of read-only " + (s.Kind == SymKind.Param ? "parameter" : "variable") + " '" + s.Name + "'" : "assignment of read-only " + (s.Kind == SymKind.Param ? "parameter" : "variable") + " '" + s.Name + "'", target);
                return false;
            }
            if (s != null && s.LoopRef && t.E is IrName)
            {
                Err("assigning to a range-for reference variable is not supported in JavaFarmer (assign the element through its index)", target);
                return false;
            }
            if (t.T.Kind == CK.Array && t.E is IrName) { Err("assignment to expression with array type", target); return false; }
            if (t.E is IrIndex ix && ix.Target.Type != null && ix.Target.Type.Kind == IrTypeKind.Array && t.T.Kind == CK.Array) { Err("assignment to expression with array type", target); return false; }
            return true;
        }

        IrStmt Assign(AssignExpr a, Span span)
        {
            var t = BindExpr(a.Target);
            if (t.T.IsError) return null;
            if (!CheckAssignable(t, a.Target, "assignment")) return null;
            var opSpan = Sp(a.OpStart, a.OpEnd);
            var tt = t.T;
            if (a.Op == "=")
            {
                var v = BindTo(a.Value, tt, "assignment");
                if (v.T.IsError) return null;
                return new IrAssign { Target = t.E, Value = CopyIfValue(v, tt, false), OpSpan = opSpan, Span = span };
            }
            var val = BindExpr(a.Value);
            if (val.T.IsError) return null;
            var res = new IrAssign { Target = t.E, OpSpan = opSpan, Span = span };
            switch (a.Op)
            {
                case "+=":
                    if (tt.IsStr)
                    {
                        if (C) { Err("invalid operands to binary + (have '" + Show(tt) + "' and '" + Show(val.T) + "'): join strings with str_concat(a, b)", opSpan.Start, opSpan.End); return null; }
                        if (!tt.StdString) { Err("pointer arithmetic is not supported in JavaFarmer (use std::string)", opSpan.Start, opSpan.End); return null; }
                        if (!(val.T.IsStr || val.T.Kind == CK.Char)) { Err("no match for 'operator+=' (operand types are 'std::string' and '" + Show(val.T) + "'); use std::to_string(x)", opSpan.Start, opSpan.End); return null; }
                        res.Op = IrBinOp.Concat; res.Value = val.E;
                        return res;
                    }
                    res.Op = IrBinOp.Add; break;
                case "-=": res.Op = IrBinOp.Sub; break;
                case "*=": res.Op = IrBinOp.Mul; break;
                case "/=": res.Op = tt.Kind == CK.Int && val.T.Kind == CK.Int ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv; break;
                case "%=":
                    if (tt.Kind != CK.Int || val.T.Kind != CK.Int) { Err(C ? "invalid operands to binary % (have '" + Show(tt) + "' and '" + Show(val.T) + "')" : "invalid operands of types '" + Show(tt) + "' and '" + Show(val.T) + "' to binary 'operator%'", opSpan.Start, opSpan.End); return null; }
                    res.Op = IrBinOp.ModTrunc; break;
                default:
                    Err("bitwise operators are not supported in JavaFarmer ('" + a.Op + "')", opSpan.Start, opSpan.End);
                    return null;
            }
            if (tt.Kind == CK.Bool || val.T.Kind == CK.Bool) { Err("using a bool as a number is not supported in JavaFarmer", a); return null; }
            if (!tt.IsNumeric || !(val.T.IsNumeric || (val.T.Kind == CK.Any && val.T.Game == null)))
            {
                Err(BinErr(a.Op.Substring(0, 1), tt, val.T), opSpan.Start, opSpan.End);
                return null;
            }
            res.Value = val.E;
            if (tt.Kind == CK.Int && val.T.Kind != CK.Int) res.TruncateToInt = true;
            return res;
        }

        /// <summary>Calls that only exist as statements: list_set / map_put, vector::pop_back / erase, map::erase ...</summary>
        bool TryStatementCall(CallExpr c, Span span, List<IrStmt> outList)
        {
            if (C && c.Callee is NameExpr n && n.Qual.Count == 0 && Lookup(n.Name) == null && (n.Name == "list_set" || n.Name == "map_put"))
            {
                if (!Farm) { Undeclared(n.Name, n, true); return true; }
                bool list = n.Name == "list_set";
                if (c.Args.Count != 3) { Err((c.Args.Count < 3 ? "too few" : "too many") + " arguments to function '" + n.Name + "'", c); return true; }
                var recv = BindExpr(c.Args[0]);
                if (recv.T.IsError) return true;
                if (recv.T.Kind != (list ? CK.List : CK.Map)) { Err(n.Name + " expects a " + (list ? "List" : "Map") + " as argument 1, not " + Show(recv.T), c.Args[0]); return true; }
                var k = list ? BindTo(c.Args[1], CT.Int, "argument 2 of '" + n.Name + "'") : BindTo(c.Args[1], recv.T.Key, "argument 2 of '" + n.Name + "'");
                var v = BindTo(c.Args[2], recv.T.Elem, "argument 3 of '" + n.Name + "'");
                if (k.T.IsError || v.T.IsError) return true;
                var target = new IrIndex { Target = recv.E, Index = k.E, Span = Sp(c), Type = recv.T.Elem.Ir() };
                outList.Add(new IrAssign { Target = target, Value = v.E, OpSpan = Sp(n), Span = span });
                return true;
            }
            if (Cpp && c.Callee is MemberExpr me && (me.Name == "insert_or_assign") && c.Args.Count == 2)
            {
                var recv = BindExpr(me.Target);
                if (recv.T.IsError) return true;
                if (recv.T.Kind != CK.Map) return false;
                var k = BindTo(c.Args[0], recv.T.Key, "argument 1");
                var v = BindTo(c.Args[1], recv.T.Elem, "argument 2");
                if (k.T.IsError || v.T.IsError) return true;
                outList.Add(new IrAssign { Target = new IrIndex { Target = recv.E, Index = k.E, Span = Sp(c), Type = recv.T.Elem.Ir() }, Value = v.E, OpSpan = Sp(me.NameStart, me.NameEnd), Span = span });
                return true;
            }
            return false;
        }

        // ================================================================== calls

        BE Call(CallExpr c, string ctx)
        {
            switch (c.Callee)
            {
                case NameExpr n when n.Qual.Count == 0:
                    {
                        var sym = Lookup(n.Name);
                        if (sym != null)
                        {
                            if (sym.Kind == SymKind.Function) return UserCall(sym.Fn, c, n);
                            if (sym.Type.Kind == CK.Func) return ValueCall(new IrName(sym.Ir) { Span = Sp(n), NameSpan = Sp(n) }, sym.Type, c, n);
                            Err(C ? "called object '" + n.Name + "' is not a function or function pointer" : "'" + n.Name + "' cannot be used as a function", n);
                            return BE.Error();
                        }
                        return LibraryCall(n.Name, n, c, false);
                    }
                case NameExpr q:
                    {
                        if (C) { Err("'::' is not valid in C", q); return BE.Error(); }
                        var full = q.Full;
                        if (q.Qual[0] == "farm")
                        {
                            if (!Farm) { Err("'farm' has not been declared; did you forget '#include \"farm.hpp\"'?", q); return BE.Error(); }
                            if (q.Qual.Count == 1) return LibraryCall(q.Name, q, c, true, true);
                        }
                        if (q.Qual.Count == 1 && q.Qual[0] == "std") return StdCall(q.Name, q, c);
                        if (full == "std::ranges::contains") return RangesContains(q, c);
                        Err("'" + full + "' is not supported in JavaFarmer", q);
                        return BE.Error();
                    }
                case MemberExpr me:
                    if (C)
                    {
                        Err("request for member '" + me.Name + "' in something not a structure or union (C has no methods: use " + CHelperHint(me.Name) + ")", me.NameStart, me.NameEnd);
                        return BE.Error();
                    }
                    return MethodCall(me, c);
            }
            var callee = BindExpr(c.Callee);
            if (callee.T.IsError) return BE.Error();
            if (callee.T.Kind == CK.Func) return ValueCall(callee.E, callee.T, c, c.Callee);
            Err(C ? "called object is not a function or function pointer" : "expression cannot be used as a function", c.Callee);
            return BE.Error();
        }

        static string CHelperHint(string method)
        {
            switch (method)
            {
                case "push_back": case "add": case "append": return "list_add(list, x)";
                case "size": case "length": case "len": return "list_len(list)";
                case "get": return "list_get(list, i)";
                case "put": return "map_put(map, key, value)";
            }
            return "the farm.h functions list_..., map_..., set_...";
        }

        List<IrExpr> Args(FnSig f, CallExpr c, Node nameNode, out bool ok)
        {
            ok = true;
            var res = new List<IrExpr>();
            int n = c.Args.Count, max = f.Params.Count;
            int min = max;
            while (min > 0 && f.Defaults.Count >= min && f.Defaults[min - 1] != null) min--;
            if (n < min || n > max)
            {
                ok = false;
                if (C) Err((n < min ? "too few" : "too many") + " arguments to function '" + f.Name + "'", c);
                else Err((n < min ? "too few" : "too many") + " arguments to function '" + SigText(f) + "'", c);
                return res;
            }
            for (int i = 0; i < n; i++)
            {
                var a = BindTo(c.Args[i], f.Params[i], C ? "argument " + (i + 1) + " of '" + f.Name + "'" : "argument " + (i + 1) + " of '" + f.Name + "'");
                if (a.T.IsError) { ok = false; continue; }
                res.Add(a.E);
            }
            return res;
        }

        BE UserCall(FnSig f, CallExpr c, Node nameNode)
        {
            f.Used = true;
            if (f.IsMain) { Err("calling main is not supported in JavaFarmer", nameNode); return BE.Error(); }
            var args = Args(f, c, nameNode, out bool ok);
            if (!ok) return BE.Error();
            var call = new IrCall { Kind = IrCallKind.Function, Function = f.Ir, NameSpan = Sp(nameNode) };
            if (f.Ir.Kind == IrSymbolKind.ExternalFunction) call.QualifierSpan = Sp(nameNode);
            call.Args.AddRange(args);
            return new BE(call, f.Ret);
        }

        BE ValueCall(IrExpr callee, CT ft, CallExpr c, Node nameNode)
        {
            if (c.Args.Count != ft.Params.Count) { Err((c.Args.Count < ft.Params.Count ? "too few" : "too many") + " arguments to function", c); return BE.Error(); }
            var call = new IrCall { Kind = IrCallKind.Value, Callee = callee, NameSpan = Sp(nameNode) };
            for (int i = 0; i < c.Args.Count; i++)
            {
                var a = BindTo(c.Args[i], ft.Params[i], "argument " + (i + 1));
                if (a.T.IsError) return BE.Error();
                call.Args.Add(a.E);
            }
            return new BE(call, ft.Elem);
        }

        // ------------------------------------------------------------------ game builtins

        /// <summary>This language's spelling of a game builtin.</summary>
        internal string ApiNameOf(string py)
        {
            if (C) return py == "print" ? "printf" : py;
            switch (py)
            {
                case "print": return "std::println";
                case "min": return "std::min";
                case "max": return "std::max";
                case "abs": return "std::abs";
                case "random": return "farm::random";
            }
            return py;
        }

        /// <summary>A game builtin callable by this unqualified name (null if none).</summary>
        ApiBuiltin BuiltinByName(string name)
        {
            if (name == "print") return null;
            if (Cpp && (name == "min" || name == "max" || name == "abs")) return null;
            return GameApi.Find(name);
        }

        CT BuiltinRet(ApiBuiltin api)
        {
            switch (api.PythonName)
            {
                case "measure": return C ? CT.Float : CT.Any;
                case "wait_for": return CT.Void;
                case "get_cost": return C ? CT.Map(CT.Handle("Item"), CT.Float) : CT.Optional(CT.Map(CT.Handle("Item"), CT.Float));
                case "get_companion": return C ? CT.Companion : CT.Optional(CT.Companion);
                case "simulate": return CT.Float;
            }
            if (api.ReturnText == "number") return CT.Float;
            return CT.FromIr(api.ReturnType, d);
        }

        List<CT> BuiltinParamTypes(ApiBuiltin api)
        {
            var res = new List<CT>();
            foreach (var p in api.Params) if (!p.Optional && !p.Variadic) res.Add(ParamCT(api, p));
            return res;
        }

        CT ParamCT(ApiBuiltin api, ApiParam p)
        {
            if (api.PythonName == "simulate")
            {
                switch (p.Name)
                {
                    case "sim_unlocks": return CT.Map(CT.Handle("Unlock"), CT.Int);
                    case "sim_globals": return CT.Map(Cpp ? CT.StdStr : CT.CStr, CT.Int);
                }
            }
            if (p.TypeText == "func") return CT.Func(CT.Void, new List<CT>(), Cpp);
            if (p.TypeText == "number") return CT.Float;
            return CT.FromIr(p.Type, d);
        }

        BE BuiltinCall(ApiBuiltin api, CallExpr c, Node nameNode)
        {
            var name = api.PythonName;
            int n = c.Args.Count;
            if (name == "min" || name == "max") { if (n != 2) { Err((C ? "macro \"" + name + "\" requires 2 arguments, but only " : "no matching function for call to '" + ApiNameOf(name) + "' with ") + n + (C ? " given" : " arguments") + " (JavaFarmer: " + ApiNameOf(name) + " takes 2 numbers)", c); return BE.Error(); } }
            else if (n < api.MinArgs || n > api.MaxArgs)
            {
                if (C) Err((n < api.MinArgs ? "too few" : "too many") + " arguments to function '" + name + "'", c);
                else Err("no matching function for call to '" + ApiNameOf(name) + "' with " + n + " argument" + (n == 1 ? "" : "s"), c);
                return BE.Error();
            }
            var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = name, NameSpan = Sp(nameNode) };
            var argTypes = new List<CT>();
            for (int i = 0; i < n; i++)
            {
                var p = api.Params.Count == 0 ? null : api.Params[Math.Min(i, api.Params.Count - 1)];
                var ctx = "argument " + (i + 1) + " of '" + ApiNameOf(name) + "'";
                if (p == null) { Err("too many arguments to function '" + name + "'", c.Args[i]); return BE.Error(); }
                BE a;
                if (p.TypeText == "func")
                {
                    a = SpawnArg(c.Args[i], name);
                    if (a.T.IsError) return BE.Error();
                }
                else if (p.TypeText == "any") { a = BindExpr(c.Args[i]); if (a.T.IsError) return BE.Error(); }
                else if (p.TypeText == "number")
                {
                    a = BindExpr(c.Args[i]);
                    if (a.T.IsError) return BE.Error();
                    if (a.T.Kind == CK.Any && a.T.Game == null) a = AsNumber(a);
                    if (!a.T.IsNumeric) { Err(a.T.Kind == CK.Bool ? "using a bool as a number is not supported in JavaFarmer" : ConvErr(a.T, CT.Float, ctx), c.Args[i]); return BE.Error(); }
                }
                else if (p.TypeText.IndexOf('|') >= 0)
                {
                    a = BindExpr(c.Args[i]);
                    if (a.T.IsError) return BE.Error();
                    var allowed = p.TypeText.Split('|').Select(x => x.Trim()).ToList();
                    if (!(a.T.Kind == CK.Handle && allowed.Contains(a.T.Game)))
                    {
                        Err((C ? "incompatible type for " + ctx : "cannot convert '" + Show(a.T) + "' in " + ctx) + ": expected " + string.Join(" or ", allowed), c.Args[i]);
                        return BE.Error();
                    }
                }
                else
                {
                    a = BindTo(c.Args[i], ParamCT(api, p), ctx);
                    if (a.T.IsError) return BE.Error();
                }
                argTypes.Add(a.T);
                call.Args.Add(a.E);
            }
            var rt = BuiltinRet(api);
            if (api.NumericLikeArgs)
            {
                if (Cpp && (name == "min" || name == "max") && !argTypes[0].Same(argTypes[1]) && argTypes[0].IsNumeric && argTypes[1].IsNumeric)
                {
                    Err("no matching function for call to '" + ApiNameOf(name) + "(" + Show(argTypes[0]) + ", " + Show(argTypes[1]) + ")' (both arguments must have the same type)", c);
                    return BE.Error();
                }
                rt = argTypes.All(t => t.Kind == CK.Int) ? CT.Int : CT.Float;
            }
            return new BE(call, rt);
        }

        /// <summary>spawn_drone's argument: a void function (C: its name; C++: also a lambda or std::function).</summary>
        BE SpawnArg(Expr e, string fn)
        {
            if (Cpp && e is LambdaExpr lam)
            {
                var l = Lambda(lam, null);
                if (l.T.IsError) return l;
                if (l.T.Params.Count > 0) { Err("the function given to " + fn + " cannot take parameters", lam); return BE.Error(); }
                return l;
            }
            var b = BindExpr(e);
            if (b.T.IsError) return b;
            if (b.T.Kind != CK.Func && e is CallExpr called && called.Args.Count == 0)
            {
                Err(fn + " needs the function itself: write " + fn + "(" + Text(called.Callee) + ") without ()", e);
                return BE.Error();
            }
            if (b.T.Kind != CK.Func)
            {
                Err(C ? "passing argument 1 of '" + fn + "' needs a function (void f(void)), not " + Show(b.T) : "'" + fn + "' needs a function or a lambda, not " + Show(b.T), e);
                return BE.Error();
            }
            if (b.T.Params.Count > 0) { Err("the function given to " + fn + " cannot take parameters", e); return BE.Error(); }
            if (C && b.T.Elem.Kind != CK.Void) { Err("passing argument 1 of '" + fn + "' from incompatible pointer type (expected 'void (*)(void)' but argument is of type '" + Show(b.T) + "')", e); return BE.Error(); }
            if (b.E is IrFuncRef fr && fr.Builtin != null && C) { Err("passing argument 1 of '" + fn + "' from incompatible pointer type", e); return BE.Error(); }
            return b;
        }

        // ------------------------------------------------------------------ C library, farm.h helpers, unqualified C++ names

        BE LibraryCall(string name, NameExpr n, CallExpr c, bool farmQualified, bool viaFarm = false)
        {
            if (Cpp && name == "random" && !farmQualified && Farm)
            {
                Err("call of overloaded 'random()' is ambiguous (the C library also has one): write farm::random()", n);
                return BE.Error();
            }
            if (Cpp && name == "quick_print") { }
            var api = BuiltinByName(name);
            if (Cpp && farmQualified && (name == "min" || name == "max" || name == "abs")) api = null;
            if (api != null && !(Cpp && name == "quick_print"))
            {
                if (!Farm) { Undeclared(name, n, true); if (C) { } return BE.Error(); }
                return BuiltinCall(api, c, n);
            }
            if (farmQualified) { Err("'" + name + "' is not a member of 'farm'", n); return BE.Error(); }
            if (C)
            {
                var r = CLibrary(name, n, c);
                if (r != null) return r;
            }
            else
            {
                var r = CppUnqualified(name, n, c);
                if (r != null) return r;
            }
            if (Cpp && (name == "min" || name == "max" || name == "abs") && !usingStd)
            {
                Err("'" + name + "' was not declared in this scope; did you mean 'std::" + name + "'?", n);
                return BE.Error();
            }
            Undeclared(name, n, true);
            return BE.Error();
        }

        BE MathCall(string name, Node n, CallExpr c)
        {
            IrOp op;
            switch (name)
            {
                case "floor": op = IrOp.Floor; break;
                case "ceil": op = IrOp.Ceil; break;
                case "round": op = IrOp.Round; break;
                case "sqrt": op = IrOp.Sqrt; break;
                case "pow": case "fmod":
                    {
                        if (c.Args.Count != 2) { Err((c.Args.Count < 2 ? "too few" : "too many") + " arguments to function '" + name + "'", c); return BE.Error(); }
                        var a = BindTo(c.Args[0], CT.Float, "argument 1 of '" + name + "'");
                        var b = BindTo(c.Args[1], CT.Float, "argument 2 of '" + name + "'");
                        if (a.T.IsError || b.T.IsError) return BE.Error();
                        return new BE(new IrBinary { Op = name == "pow" ? IrBinOp.Pow : IrBinOp.ModTrunc, Left = a.E, Right = b.E, OpSpan = Sp(n) }, CT.Float);
                    }
                case "fabs": case "fmin": case "fmax":
                    {
                        var py = name == "fabs" ? "abs" : name == "fmin" ? "min" : "max";
                        int want = name == "fabs" ? 1 : 2;
                        if (c.Args.Count != want) { Err((c.Args.Count < want ? "too few" : "too many") + " arguments to function '" + name + "'", c); return BE.Error(); }
                        var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = py, NameSpan = Sp(n) };
                        foreach (var x in c.Args) { var a = BindTo(x, CT.Float, "argument of '" + name + "'"); if (a.T.IsError) return BE.Error(); call.Args.Add(a.E); }
                        return new BE(call, CT.Float);
                    }
                default: return null;
            }
            if (c.Args.Count != 1) { Err((c.Args.Count < 1 ? "too few" : "too many") + " arguments to function '" + name + "'", c); return BE.Error(); }
            var x1 = BindTo(c.Args[0], CT.Float, "argument 1 of '" + name + "'");
            if (x1.T.IsError) return BE.Error();
            return new BE(new IrOpCall(op, x1.E) { NameSpan = Sp(n) }, CT.Float);
        }

        static readonly HashSet<string> MathNames = new HashSet<string> { "floor", "ceil", "round", "sqrt", "pow", "fmod", "fabs", "fmin", "fmax" };

        BE CLibrary(string name, NameExpr n, CallExpr c)
        {
            if (MathNames.Contains(name))
            {
                if (!(Farm || Has("math.h"))) { Err("implicit declaration of function '" + name + "'; include <math.h>", n); return BE.Error(); }
                return MathCall(name, n, c);
            }
            switch (name)
            {
                case "strlen":
                    {
                        if (!(Farm || Has("string.h"))) { Err("implicit declaration of function 'strlen'; include <string.h>", n); return BE.Error(); }
                        if (c.Args.Count != 1) { Err("wrong number of arguments to function 'strlen'", c); return BE.Error(); }
                        var s = BindTo(c.Args[0], CT.CStr, "argument 1 of 'strlen'");
                        if (s.T.IsError) return BE.Error();
                        return new BE(new IrOpCall(IrOp.Len, s.E) { NameSpan = Sp(n) }, CT.Int);
                    }
                case "strcmp":
                    if (!(Farm || Has("string.h"))) { Err("implicit declaration of function 'strcmp'; include <string.h>", n); return BE.Error(); }
                    Err("strcmp is only supported as strcmp(a, b) == 0 or != 0 in JavaFarmer", c);
                    return BE.Error();
                case "abs":
                    {
                        if (!(Farm || Has("stdlib.h"))) { Err("implicit declaration of function 'abs'; include <stdlib.h>", n); return BE.Error(); }
                        if (c.Args.Count != 1) { Err("wrong number of arguments to function 'abs'", c); return BE.Error(); }
                        var x = BindExpr(c.Args[0]);
                        if (x.T.IsError) return BE.Error();
                        if (!x.T.IsNumeric) { Err(ConvErr(x.T, CT.Int, "argument 1 of 'abs'"), c.Args[0]); return BE.Error(); }
                        var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = "abs", NameSpan = Sp(n) };
                        call.Args.Add(x.E);
                        return new BE(call, x.T.Kind == CK.Int ? CT.Int : CT.Float);
                    }
                case "printf": case "puts": case "putchar": case "quick_print":
                    Err("using the result of " + name + " is not supported in JavaFarmer (call it as a statement)", c);
                    return BE.Error();
            }
            if (name == "measure_pos")
            {
                // farm.h: measure() as a Position (treasure hint, apple position); C has no dynamic typing
                if (!Farm) { Err("implicit declaration of function 'measure_pos'; did you forget '#include \"farm.h\"'?", n); return BE.Error(); }
                if (c.Args.Count > 1) { Err("too many arguments to function 'measure_pos'", c); return BE.Error(); }
                var mc = new IrCall { Kind = IrCallKind.Builtin, Builtin = "measure", NameSpan = Sp(n) };
                if (c.Args.Count == 1) { var dv = BindTo(c.Args[0], CT.Handle("Direction"), "argument 1 of 'measure_pos'"); if (dv.T.IsError) return BE.Error(); mc.Args.Add(dv.E); }
                return new BE(mc, CT.Position);
            }
            if (name.StartsWith("list_") || name.StartsWith("set_") || name.StartsWith("map_") || name.StartsWith("str_"))
            {
                if (!Farm) { Err("implicit declaration of function '" + name + "'; did you forget '#include \"farm.h\"'?", n); return BE.Error(); }
                return FarmHelper(name, n, c);
            }
            switch (name)
            {
                case "malloc": case "calloc": case "free": case "realloc": case "exit": case "scanf": case "rand": case "srand": case "memset": case "memcpy": case "strcpy": case "strcat": case "sprintf": case "snprintf": case "atoi":
                    Err("'" + name + "' is not supported in JavaFarmer", n);
                    return BE.Error();
            }
            return null;
        }

        BE CheckArgs(CallExpr c, string name, int want)
        {
            if (c.Args.Count == want) return null;
            Err((c.Args.Count < want ? "too few" : "too many") + " arguments to function '" + name + "'", c);
            return BE.Error();
        }

        BE Recv(CallExpr c, string name, CK kind)
        {
            var r = BindExpr(c.Args[0]);
            if (r.T.IsError) return r;
            if (r.T.Kind != kind)
            {
                Err(name + " expects a " + (kind == CK.List ? "List" : kind == CK.Set ? "Set" : "Map") + " as argument 1, not " + Show(r.T), c.Args[0]);
                return BE.Error();
            }
            return r;
        }

        /// <summary>farm.h collection and string helpers (C).</summary>
        BE FarmHelper(string name, NameExpr n, CallExpr c)
        {
            var ns = Sp(n);
            IrOpCall Op(IrOp op, params IrExpr[] a) => new IrOpCall(op, a) { NameSpan = ns };
            switch (name)
            {
                case "list_new": case "set_new": case "map_new":
                    {
                        if (c.Args.Count > 0) { Err("too many arguments to macro \"" + name + "\"", c); return BE.Error(); }
                        var ts = c.TypeArgs;
                        if (name == "list_new") { var e = ElementType(ts[0], false); if (e.IsError) return BE.Error(); return new BE(new IrListLit(), CT.List(e)); }
                        if (name == "set_new") { var e = ElementType(ts[0], true); if (e.IsError) return BE.Error(); return new BE(new IrSetLit(), CT.Set(e)); }
                        var k = ElementType(ts[0], true); var v = ElementType(ts[1], false);
                        if (k.IsError || v.IsError) return BE.Error();
                        return new BE(new IrMapLit(), CT.Map(k, v));
                    }
                case "list_of": case "set_of":
                    {
                        var e = ElementType(c.TypeArgs[0], name == "set_of");
                        if (e.IsError) return BE.Error();
                        if (c.Args.Count == 0) { Err(name + " needs at least one value (use " + (name == "list_of" ? "list_new" : "set_new") + "(T) for an empty one)", c); return BE.Error(); }
                        var items = new List<IrExpr>();
                        foreach (var a in c.Args) { var v = BindTo(a, e, "an element of " + name); if (v.T.IsError) return BE.Error(); items.Add(v.E); }
                        if (name == "list_of") { var l = new IrListLit(); l.Items.AddRange(items); return new BE(l, CT.List(e)); }
                        var s = new IrSetLit(); s.Items.AddRange(items); return new BE(s, CT.Set(e));
                    }
                case "map_of":
                    {
                        var k = ElementType(c.TypeArgs[0], true); var v = ElementType(c.TypeArgs[1], false);
                        if (k.IsError || v.IsError) return BE.Error();
                        if (c.Args.Count == 0) { Err("map_of needs at least one {key, value} pair (use map_new(K, V) for an empty map)", c); return BE.Error(); }
                        var m = new IrMapLit();
                        foreach (var a in c.Args)
                        {
                            if (!(a is InitList pair) || pair.Items.Count != 2) { Err("each entry of map_of is {key, value}", a); return BE.Error(); }
                            var kb = BindTo(pair.Items[0], k, "a key of map_of"); var vb = BindTo(pair.Items[1], v, "a value of map_of");
                            if (kb.T.IsError || vb.T.IsError) return BE.Error();
                            m.Keys.Add(kb.E); m.Values.Add(vb.E);
                        }
                        return new BE(m, CT.Map(k, v));
                    }
            }
            // helpers with a collection receiver
            switch (name)
            {
                case "list_add": case "list_insert": case "list_get": case "list_len": case "list_pop": case "list_remove_at": case "list_remove":
                case "list_contains": case "list_index_of": case "list_clear": case "list_copy": case "list_is_empty":
                    {
                        int want = name == "list_insert" ? 3 : name == "list_len" || name == "list_pop" || name == "list_clear" || name == "list_copy" || name == "list_is_empty" ? 1 : 2;
                        var bad = CheckArgs(c, name, want); if (bad != null) return bad;
                        var l = Recv(c, name, CK.List); if (l.T.IsError) return l;
                        var et = l.T.Elem;
                        BE A(int i, CT t) => BindTo(c.Args[i], t, "argument " + (i + 1) + " of '" + name + "'");
                        switch (name)
                        {
                            case "list_add": { var x = A(1, et); if (x.T.IsError) return x; return new BE(Op(IrOp.ListAppend, l.E, x.E), CT.Void); }
                            case "list_insert": { var i = A(1, CT.Int); var x = A(2, et); if (i.T.IsError || x.T.IsError) return BE.Error(); return new BE(Op(IrOp.ListInsert, l.E, i.E, x.E), CT.Void); }
                            case "list_get": { var i = A(1, CT.Int); if (i.T.IsError) return i; return new BE(new IrIndex { Target = l.E, Index = i.E }, et); }
                            case "list_len": return new BE(Op(IrOp.Len, l.E), CT.Int);
                            case "list_is_empty": return new BE(Op(IrOp.IsEmpty, l.E), CT.Bool);
                            case "list_pop": return new BE(Op(IrOp.ListPop, l.E), et);
                            case "list_remove_at": { var i = A(1, CT.Int); if (i.T.IsError) return i; return new BE(Op(IrOp.ListPop, l.E, i.E), et); }
                            case "list_remove": { var x = A(1, et); if (x.T.IsError) return x; return new BE(Op(IrOp.RemoveValue, l.E, x.E), CT.Void); }
                            case "list_contains": { var x = A(1, et); if (x.T.IsError) return x; return new BE(Op(IrOp.Contains, l.E, x.E), CT.Bool); }
                            case "list_index_of": { var x = A(1, et); if (x.T.IsError) return x; return new BE(Op(IrOp.ListIndexOf, l.E, x.E), CT.Int); }
                            case "list_clear": return new BE(Op(IrOp.Clear, l.E), CT.Void);
                            case "list_copy": return new BE(Op(IrOp.ListCopy, l.E), l.T);
                        }
                        break;
                    }
                case "set_add": case "set_remove": case "set_contains": case "set_len": case "set_clear": case "set_copy": case "set_is_empty":
                    {
                        int want = name == "set_len" || name == "set_clear" || name == "set_copy" || name == "set_is_empty" ? 1 : 2;
                        var bad = CheckArgs(c, name, want); if (bad != null) return bad;
                        var s = Recv(c, name, CK.Set); if (s.T.IsError) return s;
                        BE A(int i) => BindTo(c.Args[i], s.T.Elem, "argument " + (i + 1) + " of '" + name + "'");
                        switch (name)
                        {
                            case "set_add": { var x = A(1); if (x.T.IsError) return x; return new BE(Op(IrOp.SetAdd, s.E, x.E), CT.Void); }
                            case "set_remove": { var x = A(1); if (x.T.IsError) return x; return new BE(Op(IrOp.RemoveValue, s.E, x.E), CT.Void); }
                            case "set_contains": { var x = A(1); if (x.T.IsError) return x; return new BE(Op(IrOp.Contains, s.E, x.E), CT.Bool); }
                            case "set_len": return new BE(Op(IrOp.Len, s.E), CT.Int);
                            case "set_is_empty": return new BE(Op(IrOp.IsEmpty, s.E), CT.Bool);
                            case "set_clear": return new BE(Op(IrOp.Clear, s.E), CT.Void);
                            case "set_copy": return new BE(Op(IrOp.SetCopy, s.E), s.T);
                        }
                        break;
                    }
                case "map_get": case "map_get_or": case "map_has": case "map_remove": case "map_len": case "map_keys": case "map_values": case "map_clear": case "map_copy": case "map_is_empty":
                    {
                        int want = name == "map_get_or" ? 3 : name == "map_get" || name == "map_has" || name == "map_remove" ? 2 : 1;
                        var bad = CheckArgs(c, name, want); if (bad != null) return bad;
                        var m = Recv(c, name, CK.Map); if (m.T.IsError) return m;
                        BE K() => BindTo(c.Args[1], m.T.Key, "argument 2 of '" + name + "'");
                        switch (name)
                        {
                            case "map_get": { var k = K(); if (k.T.IsError) return k; return new BE(new IrIndex { Target = m.E, Index = k.E }, m.T.Elem); }
                            case "map_get_or": { var k = K(); var dv = BindTo(c.Args[2], m.T.Elem, "argument 3 of 'map_get_or'"); if (k.T.IsError || dv.T.IsError) return BE.Error(); return new BE(Op(IrOp.MapGetOrDefault, m.E, k.E, dv.E), m.T.Elem); }
                            case "map_has": { var k = K(); if (k.T.IsError) return k; return new BE(Op(IrOp.Contains, m.E, k.E), CT.Bool); }
                            case "map_remove": { var k = K(); if (k.T.IsError) return k; return new BE(Op(IrOp.MapRemove, m.E, k.E), m.T.Elem); }
                            case "map_len": return new BE(Op(IrOp.Len, m.E), CT.Int);
                            case "map_is_empty": return new BE(Op(IrOp.IsEmpty, m.E), CT.Bool);
                            case "map_keys": { var o = Op(IrOp.ListCopy, m.E); o.Synthetic = true; o.Span = Sp(c); return new BE(o, CT.List(m.T.Key)); }
                            case "map_values": return new BE(Op(IrOp.MapValues, m.E), CT.List(m.T.Elem));
                            case "map_clear": return new BE(Op(IrOp.Clear, m.E), CT.Void);
                            case "map_copy": return new BE(Op(IrOp.MapCopy, m.E), m.T);
                        }
                        break;
                    }
                case "str_of":
                    {
                        var bad = CheckArgs(c, name, 1); if (bad != null) return bad;
                        if (IsCComparison(c.Args[0]))
                        {
                            Err("in C a comparison has type int: str_of(...) would give 1 or 0 in real C but true or false in the game; store it in a bool variable first", c.Args[0]);
                            return BE.Error();
                        }
                        var x = BindExpr(c.Args[0]);
                        if (x.T.IsError) return x;
                        if (x.T.Kind == CK.Void || x.T.Kind == CK.Func) { Err("str_of cannot convert " + Show(x.T), c.Args[0]); return BE.Error(); }
                        if (x.T.IsStr) return new BE(x.E, CT.CStr);
                        return new BE(new IrCast { Target = IrType.String, Operand = x.E, OpSpan = ns }, CT.CStr);
                    }
                case "str_concat":
                    {
                        if (c.Args.Count < 2) { Err("too few arguments to function 'str_concat'", c); return BE.Error(); }
                        IrExpr acc = null;
                        foreach (var a in c.Args)
                        {
                            var x = BindExpr(a);
                            if (x.T.IsError) return x;
                            if (!x.T.IsStr) { Err("str_concat joins strings; convert " + Show(x.T) + " with str_of(x)", a); return BE.Error(); }
                            acc = acc == null ? x.E : new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = x.E, OpSpan = ns, Type = IrType.String, Span = Sp(c) };
                        }
                        return new BE(acc, CT.CStr);
                    }
                case "str_eq": case "str_contains":
                    {
                        var bad = CheckArgs(c, name, 2); if (bad != null) return bad;
                        var a = BindTo(c.Args[0], CT.CStr, "argument 1 of '" + name + "'");
                        var b = BindTo(c.Args[1], CT.CStr, "argument 2 of '" + name + "'");
                        if (a.T.IsError || b.T.IsError) return BE.Error();
                        if (name == "str_eq") return new BE(new IrBinary { Op = IrBinOp.Eq, Left = a.E, Right = b.E, OpSpan = ns }, CT.Bool);
                        return new BE(Op(IrOp.Contains, a.E, b.E), CT.Bool);
                    }
            }
            Undeclared(name, n, true);
            return BE.Error();
        }

        static bool IsCComparison(Expr e) =>
            e is BinaryExpr b && (b.Op == "==" || b.Op == "!=" || b.Op == "<" || b.Op == ">" || b.Op == "<=" || b.Op == ">=" || b.Op == "&&" || b.Op == "||") ||
            e is UnaryExpr u && u.Op == "!";

        // ------------------------------------------------------------------ C++ library

        BE CppUnqualified(string name, NameExpr n, CallExpr c)
        {
            if (MathNames.Contains(name))
            {
                if (!(Farm || Has("cmath", "math.h"))) { Err("'" + name + "' was not declared in this scope; did you forget '#include <cmath>'?", n); return BE.Error(); }
                return MathCall(name, n, c);
            }
            if (usingStd)
            {
                switch (name)
                {
                    case "to_string": case "min": case "max": case "abs": case "println": case "print": case "format": case "find":
                        return StdCall(name, n, c);
                }
            }
            switch (name)
            {
                case "printf": case "puts": case "quick_print":
                    Err("using the result of " + name + " is not supported in JavaFarmer (call it as a statement)", c);
                    return BE.Error();
                case "to_string": case "println": case "format":
                    Err("'" + name + "' was not declared in this scope; did you mean 'std::" + name + "'?", n);
                    return BE.Error();
            }
            return null;
        }

        BE StdCall(string name, NameExpr n, CallExpr c)
        {
            switch (name)
            {
                case "to_string":
                    {
                        if (c.Args.Count != 1) { Err("no matching function for call to 'to_string' with " + c.Args.Count + " arguments", c); return BE.Error(); }
                        var x = BindExpr(c.Args[0]);
                        if (x.T.IsError) return x;
                        if (!x.T.IsNumeric) { Err("no matching function for call to 'to_string(" + Show(x.T) + ")'" + (x.T.Kind == CK.Bool ? " (std::to_string has no bool overload)" : ""), c.Args[0]); return BE.Error(); }
                        return new BE(new IrCast { Target = IrType.String, Operand = x.E, OpSpan = Sp(n) }, CT.StdStr);
                    }
                case "min": case "max": case "abs":
                    if (!(Farm || Has(name == "abs" ? "cmath" : "algorithm", "cstdlib"))) { Err("'" + name + "' is not a member of 'std'; did you forget '#include <" + (name == "abs" ? "cmath" : "algorithm") + ">'?", n); return BE.Error(); }
                    return BuiltinCall(GameApi.Find(name), c, n);
                case "println": case "print": case "format":
                    Err("using the result of std::" + name + " is not supported in JavaFarmer" + (name == "format" ? "" : " (call it as a statement)"), c);
                    if (name == "format") return FormatValueCall(n, c);
                    return BE.Error();
            }
            if (MathNames.Contains(name))
            {
                if (!(Farm || Has("cmath"))) { Err("'" + name + "' is not a member of 'std'; did you forget '#include <cmath>'?", n); return BE.Error(); }
                return MathCall(name, n, c);
            }
            Err("'std::" + name + "' is not supported in JavaFarmer", n);
            return BE.Error();
        }

        BE FormatValueCall(NameExpr n, CallExpr c)
        {
            Errors.RemoveAt(Errors.Count - 1);
            if (!Has("format", "print")) { Err("'format' is not a member of 'std'; did you forget '#include <format>'?", n); return BE.Error(); }
            var segs = FormatSegments(c, 0, "std::format");
            if (segs == null) return BE.Error();
            var e = ConcatOf(segs, c);
            return new BE(e, CT.StdStr);
        }

        BE RangesContains(NameExpr n, CallExpr c)
        {
            if (!(Farm || Has("algorithm", "ranges"))) { Err("'std::ranges::contains' has not been declared; did you forget '#include <algorithm>'?", n); return BE.Error(); }
            if (c.Args.Count != 2) { Err("no matching function for call to 'std::ranges::contains' with " + c.Args.Count + " arguments", c); return BE.Error(); }
            var r = BindExpr(c.Args[0]);
            if (r.T.IsError) return r;
            if (r.T.Kind != CK.List && r.T.Kind != CK.Array && r.T.Kind != CK.Set) { Err("std::ranges::contains needs a vector, array or set, not " + Show(r.T), c.Args[0]); return BE.Error(); }
            var x = BindTo(c.Args[1], r.T.Elem, "argument 2 of 'std::ranges::contains'");
            if (x.T.IsError) return x;
            return new BE(new IrOpCall(IrOp.Contains, r.E, x.E) { NameSpan = Sp(n) }, CT.Bool);
        }

        BE MethodCall(MemberExpr me, CallExpr c)
        {
            var recv = BindExpr(me.Target);
            if (recv.T.IsError) return BE.Error();
            var rt = recv.T;
            if (me.Arrow)
            {
                if (rt.Kind != CK.Optional) { Err("base operand of '->' has non-pointer type '" + Show(rt) + "'", me); return BE.Error(); }
                rt = rt.Elem;
            }
            var ns = Sp(me.NameStart, me.NameEnd);
            var m = me.Name;
            int n = c.Args.Count;
            IrOpCall Op(IrOp op, params IrExpr[] a) => new IrOpCall(op, a) { NameSpan = ns };
            BE Want(int k) { if (n == k) return null; Err("no matching function for call to '" + Show(rt) + "::" + m + "' with " + n + " argument" + (n == 1 ? "" : "s"), c); return BE.Error(); }
            BE Arg(int i, CT t) => BindTo(c.Args[i], t, "argument " + (i + 1) + " of '" + m + "'");
            if (!me.Arrow && rt.Kind == CK.Optional)
            {
                switch (m)
                {
                    case "has_value": { var w = Want(0); if (w != null) return w; return new BE(new IrBinary { Op = IrBinOp.Ne, Left = recv.E, Right = Syn(IrLiteral.Null(), me), OpSpan = ns, Synthetic = true }, CT.Bool); }
                    case "value": { var w = Want(0); if (w != null) return w; return new BE(recv.E, rt.Elem); }
                }
                Err("'" + Show(rt) + "' has no member named '" + m + "' (use -> to reach the value)", ns.Start, ns.End);
                return BE.Error();
            }
            switch (rt.Kind)
            {
                case CK.List:
                    switch (m)
                    {
                        case "push_back": case "emplace_back": { var w = Want(1); if (w != null) return w; var x = Arg(0, rt.Elem); if (x.T.IsError) return x; return new BE(Op(IrOp.ListAppend, recv.E, x.E), CT.Void); }
                        case "pop_back": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.ListPop, recv.E), CT.Void); }
                        case "size": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.Len, recv.E), CT.Int); }
                        case "empty": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.IsEmpty, recv.E), CT.Bool); }
                        case "clear": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.Clear, recv.E), CT.Void); }
                        case "at": { var w = Want(1); if (w != null) return w; var i = Arg(0, CT.Int); if (i.T.IsError) return i; return new BE(new IrIndex { Target = recv.E, Index = i.E }, rt.Elem) { LValue = true }; }
                        case "front": { var w = Want(0); if (w != null) return w; return new BE(new IrIndex { Target = recv.E, Index = Syn(IrLiteral.OfInt(0), me) }, rt.Elem); }
                        case "back":
                            {
                                var w = Want(0); if (w != null) return w;
                                if (!(recv.E is IrName)) { Err("back() on a computed vector is not supported in JavaFarmer", me); return BE.Error(); }
                                var len = new IrOpCall(IrOp.Len, Clone(recv.E)) { Synthetic = true, Span = Sp(me), Type = IrType.Int };
                                var idx = new IrBinary { Op = IrBinOp.Sub, Left = len, Right = Syn(IrLiteral.OfInt(1), me), Synthetic = true, Span = Sp(me), Type = IrType.Int };
                                return new BE(new IrIndex { Target = recv.E, Index = idx }, rt.Elem);
                            }
                        case "insert":
                            {
                                var w = Want(2); if (w != null) return w;
                                var pos = IterIndex(c.Args[0], me.Target, out bool atEnd);
                                if (pos == null && !atEnd) return BE.Error();
                                var x = Arg(1, rt.Elem); if (x.T.IsError) return x;
                                if (atEnd) return new BE(Op(IrOp.ListAppend, recv.E, x.E), CT.Void);
                                return new BE(Op(IrOp.ListInsert, recv.E, pos, x.E), CT.Void);
                            }
                        case "erase":
                            {
                                var w = Want(1); if (w != null) return w;
                                // v.erase(std::find(v.begin(), v.end(), x))
                                if (c.Args[0] is CallExpr fc && fc.Callee is NameExpr fn && fn.Name == "find" && (fn.Full == "std::find" || usingStd && fn.Qual.Count == 0) && fc.Args.Count == 3)
                                {
                                    var x = BindTo(fc.Args[2], rt.Elem, "argument 3 of 'std::find'");
                                    if (x.T.IsError) return x;
                                    return new BE(Op(IrOp.RemoveValue, recv.E, x.E), CT.Void);
                                }
                                var pos = IterIndex(c.Args[0], me.Target, out bool atEnd);
                                if (pos == null) { if (atEnd) Err("erasing end() is undefined behaviour", c.Args[0]); return BE.Error(); }
                                return new BE(Op(IrOp.ListPop, recv.E, pos), CT.Void);
                            }
                        case "begin": case "end":
                            Err("iterators are only supported in v.insert(v.begin() + i, x), v.erase(v.begin() + i) and std::find in JavaFarmer", c);
                            return BE.Error();
                    }
                    break;
                case CK.Map:
                    switch (m)
                    {
                        case "at": { var w = Want(1); if (w != null) return w; var k = Arg(0, rt.Key); if (k.T.IsError) return k; return new BE(new IrIndex { Target = recv.E, Index = k.E }, rt.Elem) { LValue = true }; }
                        case "count": case "contains": { var w = Want(1); if (w != null) return w; var k = Arg(0, rt.Key); if (k.T.IsError) return k; return new BE(Op(IrOp.Contains, recv.E, k.E), CT.Bool); }
                        case "erase": { var w = Want(1); if (w != null) return w; var k = Arg(0, rt.Key); if (k.T.IsError) return k; return new BE(Op(IrOp.MapRemoveIfPresent, recv.E, k.E), CT.Void); }
                        case "size": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.Len, recv.E), CT.Int); }
                        case "empty": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.IsEmpty, recv.E), CT.Bool); }
                        case "clear": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.Clear, recv.E), CT.Void); }
                        case "insert": case "emplace":
                            Err("map::" + m + " is not supported in JavaFarmer (use m[key] = value)", ns.Start, ns.End);
                            return BE.Error();
                    }
                    break;
                case CK.Set:
                    switch (m)
                    {
                        case "insert": case "emplace": { var w = Want(1); if (w != null) return w; var x = Arg(0, rt.Elem); if (x.T.IsError) return x; return new BE(Op(IrOp.SetAdd, recv.E, x.E), CT.Void); }
                        case "erase": { var w = Want(1); if (w != null) return w; var x = Arg(0, rt.Elem); if (x.T.IsError) return x; return new BE(Op(IrOp.RemoveValueIfPresent, recv.E, x.E), CT.Void); }
                        case "count": case "contains": { var w = Want(1); if (w != null) return w; var x = Arg(0, rt.Elem); if (x.T.IsError) return x; return new BE(Op(IrOp.Contains, recv.E, x.E), CT.Bool); }
                        case "size": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.Len, recv.E), CT.Int); }
                        case "empty": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.IsEmpty, recv.E), CT.Bool); }
                        case "clear": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.Clear, recv.E), CT.Void); }
                    }
                    break;
                case CK.Str:
                    if (!rt.StdString) { Err("request for member '" + m + "' in '" + Text(me.Target) + "', which is of non-class type 'const char*' (use std::string)", ns.Start, ns.End); return BE.Error(); }
                    switch (m)
                    {
                        case "size": case "length": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.Len, recv.E), CT.Int); }
                        case "empty": { var w = Want(0); if (w != null) return w; return new BE(Op(IrOp.IsEmpty, recv.E), CT.Bool); }
                        case "contains": { var w = Want(1); if (w != null) return w; var x = Arg(0, CT.StdStr); if (x.T.IsError) return x; return new BE(Op(IrOp.Contains, recv.E, x.E), CT.Bool); }
                        case "c_str": { var w = Want(0); if (w != null) return w; return new BE(recv.E, CT.CStr); }
                    }
                    break;
            }
            Err("'" + Show(rt) + "' has no member named '" + m + "'" + (rt.IsCollection || rt.IsStr ? " (or it is not supported in JavaFarmer)" : ""), ns.Start, ns.End);
            return BE.Error();
        }

        static IrExpr Clone(IrExpr e) => e is IrName n ? new IrName(n.Symbol) { Span = n.Span, Type = n.Type, NameSpan = n.NameSpan, Synthetic = true } : e;

        /// <summary>v.begin() + i / v.begin() / v.end() as an index (atEnd: v.end()).</summary>
        IrExpr IterIndex(Expr e, Expr recv, out bool atEnd)
        {
            atEnd = false;
            var rtext = Text(recv);
            bool IsIt(Expr x, string which) => x is CallExpr ic && ic.Args.Count == 0 && ic.Callee is MemberExpr im && im.Name == which && Text(im.Target) == rtext;
            if (IsIt(e, "begin")) return Syn(IrLiteral.OfInt(0), e);
            if (IsIt(e, "end")) { atEnd = true; return null; }
            if (e is BinaryExpr b && b.Op == "+" && IsIt(b.L, "begin"))
            {
                var i = BindTo(b.R, CT.Int, "an iterator offset");
                return i.T.IsError ? null : i.E;
            }
            Err("iterators are only supported as v.begin() + i in JavaFarmer", e);
            return null;
        }

        // ================================================================== output: printf / puts / quick_print / std::cout / std::println

        sealed class Seg { public string Text; public IrExpr Value; public Span At; }

        bool IsCall(CallExpr c, string name, bool std = false)
        {
            if (!(c.Callee is NameExpr n)) return false;
            if (std) return n.Name == name && ((n.Qual.Count == 1 && n.Qual[0] == "std") || (n.Qual.Count == 0 && usingStd && Lookup(name) == null));
            return n.Name == name && n.Qual.Count == 0 && Lookup(name) == null;
        }

        bool TryOutputCall(CallExpr c, Span span, List<IrStmt> outList)
        {
            var n = c.Callee as NameExpr;
            if (n == null) return false;
            if (IsCall(c, "printf") || IsCall(c, "puts"))
            {
                if (C ? !(Farm || Has("stdio.h")) : !Has("cstdio", "stdio.h"))
                {
                    Err(C ? "implicit declaration of function '" + n.Name + "'; include <stdio.h>" : "'" + n.Name + "' was not declared in this scope; did you forget '#include <cstdio>'?", n);
                    return true;
                }
                if (n.Name == "puts")
                {
                    if (c.Args.Count != 1) { Err((c.Args.Count == 0 ? "too few" : "too many") + " arguments to function 'puts'", c); return true; }
                    var s = BindExpr(c.Args[0], "print");
                    if (s.T.IsError) return true;
                    if (!s.T.IsStr || s.T.StdString) { Err(ConvErr(s.T, CT.CStr, "argument 1 of 'puts'"), c.Args[0]); return true; }
                    var segs = new List<Seg>();
                    if (s.E is IrLiteral sl && sl.Kind == IrLitKind.String) segs.Add(new Seg { Text = sl.Str + "\n", At = s.E.Span });
                    else { segs.Add(new Seg { Value = s.E, At = s.E.Span }); segs.Add(new Seg { Text = "\n", At = Sp(c) }); }
                    Emit(segs, false, Sp(n), span, c, outList);
                    return true;
                }
                var ps = PrintfSegments(c, 0, "printf");
                if (ps != null) Emit(ps, false, Sp(n), span, c, outList);
                return true;
            }
            if (IsCall(c, "quick_print") || (Cpp && n.Qual.Count == 1 && n.Qual[0] == "farm" && n.Name == "quick_print"))
            {
                if (!Farm) { Undeclared("quick_print", n, true); return true; }
                var qs = C ? PrintfSegments(c, 0, "quick_print") : FormatSegments(c, 0, "quick_print");
                if (qs != null) Emit(qs, true, Sp(n), span, c, outList);
                return true;
            }
            if (Cpp && (IsCall(c, "println", true) || IsCall(c, "print", true)))
            {
                if (!Has("print")) { Err("'" + n.Name + "' is not a member of 'std'; did you forget '#include <print>'?", n); return true; }
                List<Seg> fs;
                if (n.Name == "println" && c.Args.Count == 0) fs = new List<Seg>();
                else fs = FormatSegments(c, 0, "std::" + n.Name);
                if (fs == null) return true;
                if (n.Name == "println") fs.Add(new Seg { Text = "\n", At = Sp(c) });
                Emit(fs, false, Sp(n), span, c, outList);
                return true;
            }
            return false;
        }

        List<Seg> PrintfSegments(CallExpr c, int fmtIndex, string fn)
        {
            if (c.Args.Count <= fmtIndex) { Err("too few arguments to function '" + fn + "'", c); return null; }
            if (!(c.Args[fmtIndex] is Lit fl && fl.Kind == LitKind.String))
            {
                Err("the format of " + fn + " must be a string literal in JavaFarmer (print a string with " + fn + "(\"%s\\n\", s))", c.Args[fmtIndex]);
                return null;
            }
            var fmt = fl.S;
            var segs = new List<Seg>();
            var fmtSpan = Sp(fl);
            var text = new StringBuilder();
            int argi = fmtIndex + 1;
            for (int i = 0; i < fmt.Length; i++)
            {
                char ch = fmt[i];
                if (ch != '%') { text.Append(ch); continue; }
                int s = i;
                i++;
                if (i >= fmt.Length) { Err("spurious trailing '%' in format", fl); return null; }
                if (fmt[i] == '%') { text.Append('%'); continue; }
                // flags / width / precision / length
                int flagsStart = i;
                while (i < fmt.Length && "-+ #0".IndexOf(fmt[i]) >= 0) i++;
                bool flags = i > flagsStart;
                int ws = i;
                while (i < fmt.Length && char.IsDigit(fmt[i])) i++;
                bool width = i > ws || (i < fmt.Length && fmt[i] == '*');
                bool prec = false;
                if (i < fmt.Length && fmt[i] == '.') { prec = true; i++; while (i < fmt.Length && char.IsDigit(fmt[i])) i++; }
                string len = "";
                while (i < fmt.Length && "hlLzjt".IndexOf(fmt[i]) >= 0) { len += fmt[i]; i++; }
                if (i >= fmt.Length) { Err("conversion lacks type at end of format", fl); return null; }
                char conv = fmt[i];
                var spec = fmt.Substring(s, i - s + 1);
                if (flags || width) { Err("printf flags and field widths are not supported in JavaFarmer ('" + spec + "')", fl); return null; }
                if ("dief gsFG".IndexOf(conv) < 0 || conv == ' ')
                {
                    Err(conv == 'c' ? "%c is not supported in JavaFarmer (print text with %s)" : "the printf conversion '" + spec + "' is not supported in JavaFarmer (use %d, %f, %g or %s)", fl);
                    return null;
                }
                if (len != "" && len != "l") { Err("the length modifier '" + len + "' is not supported in JavaFarmer", fl); return null; }
                if (prec && conv != 'f' && conv != 'F' && conv != 'g' && conv != 'G') { Err("a precision is only supported with %f / %g in JavaFarmer", fl); return null; }
                if (argi >= c.Args.Count) { Err("format '" + spec + "' expects a matching " + ExpectedArg(conv, len) + " argument", fl); return null; }
                var argE = c.Args[argi];
                var a = BindExpr(argE);
                argi++;
                if (a.T.IsError) return null;
                var at = a.T;
                string want = ExpectedArg(conv, len);
                bool ok;
                switch (conv)
                {
                    case 'd': case 'i':
                        ok = at.Kind == CK.Int && (len == "l") == at.Long;
                        if (at.Kind == CK.Bool)
                        {
                            Err("format '" + spec + "' expects argument of type 'int', but argument " + argi + " has type 'bool' (JavaFarmer prints bools as true/false: use %s with str_of(b))", argE);
                            return null;
                        }
                        break;
                    case 'f': case 'F': case 'g': case 'G': case 'e':
                        ok = at.Kind == CK.Float || (Cpp && at.Kind == CK.Any && at.Game == null);
                        break;
                    default:
                        ok = at.IsStr && !at.StdString;
                        if (at.IsStr && at.StdString) { Err("format '%s' expects argument of type 'char*', but argument " + argi + " has type 'std::string' (use .c_str())", argE); return null; }
                        break;
                }
                if (!ok)
                {
                    var hint = conv == 's' && C && at.Kind != CK.Void ? " (convert it with str_of(x))" : at.Kind == CK.Int && (conv == 'f' || conv == 'g') ? " (cast it: (double) x)" : at.Kind == CK.Float && (conv == 'd' || conv == 'i') ? " (cast it: (int) x)" : "";
                    Err("format '" + spec + "' expects argument of type '" + want + "', but argument " + argi + " has type '" + Show(at) + "'" + hint, argE);
                    return null;
                }
                if (text.Length > 0) { segs.Add(new Seg { Text = text.ToString(), At = fmtSpan }); text.Clear(); }
                segs.Add(new Seg { Value = a.E, At = a.E.Span });
            }
            if (argi < c.Args.Count) { Err("too many arguments for format", c.Args[argi]); return null; }
            if (text.Length > 0) segs.Add(new Seg { Text = text.ToString(), At = fmtSpan });
            return segs;
        }

        static string ExpectedArg(char conv, string len)
        {
            switch (conv)
            {
                case 'd': case 'i': return len == "l" ? "long int" : "int";
                case 's': return "char *";
            }
            return "double";
        }

        /// <summary>std::format-style "{} {}" (std::println, std::print, std::format, C++ quick_print).</summary>
        List<Seg> FormatSegments(CallExpr c, int fmtIndex, string fn)
        {
            if (c.Args.Count <= fmtIndex) { Err("no matching function for call to '" + fn + "()'", c); return null; }
            if (!(c.Args[fmtIndex] is Lit fl && fl.Kind == LitKind.String))
            {
                Err("the first argument of " + fn + " must be a format string literal such as \"{}\"", c.Args[fmtIndex]);
                return null;
            }
            var fmt = fl.S;
            var segs = new List<Seg>();
            var text = new StringBuilder();
            var fmtSpan = Sp(fl);
            int argi = fmtIndex + 1;
            for (int i = 0; i < fmt.Length; i++)
            {
                char ch = fmt[i];
                if (ch == '{' && i + 1 < fmt.Length && fmt[i + 1] == '{') { text.Append('{'); i++; continue; }
                if (ch == '}' && i + 1 < fmt.Length && fmt[i + 1] == '}') { text.Append('}'); i++; continue; }
                if (ch == '}') { Err("unmatched '}' in format string", fl); return null; }
                if (ch != '{') { text.Append(ch); continue; }
                int close = fmt.IndexOf('}', i);
                if (close < 0) { Err("unmatched '{' in format string", fl); return null; }
                var inside = fmt.Substring(i + 1, close - i - 1);
                if (inside.Length > 0) { Err("format specifications like {" + inside + "} are not supported in JavaFarmer (use {})", fl); return null; }
                if (argi >= c.Args.Count) { Err("the format string has more {} than arguments", fl); return null; }
                var a = BindExpr(c.Args[argi]);
                argi++;
                if (a.T.IsError) return null;
                if (!Formattable(a.T)) { Err("'" + Show(a.T) + "' cannot be formatted by " + fn, c.Args[argi - 1]); return null; }
                if (text.Length > 0) { segs.Add(new Seg { Text = text.ToString(), At = fmtSpan }); text.Clear(); }
                segs.Add(new Seg { Value = a.E, At = a.E.Span });
                i = close;
            }
            if (argi < c.Args.Count) { Err("the format string has fewer {} than arguments", c.Args[argi]); return null; }
            if (text.Length > 0) segs.Add(new Seg { Text = text.ToString(), At = fmtSpan });
            return segs;
        }

        bool Formattable(CT t)
        {
            switch (t.Kind)
            {
                case CK.Int: case CK.Float: case CK.Bool: case CK.Str: case CK.Handle: case CK.Position: case CK.Any: return true;
                case CK.List: case CK.Set: return Formattable(t.Elem);
                case CK.Map: return Formattable(t.Key) && Formattable(t.Elem);
            }
            return false;
        }

        void StreamOutput(BinaryExpr be, Span span, List<IrStmt> outList)
        {
            var items = new List<Expr>();
            Expr e = be;
            while (e is BinaryExpr b && b.Op == "<<") { items.Add(b.R); e = b.L; }
            items.Reverse();
            var root = (NameExpr)e;
            var rs = Name(root, null);
            if (rs.T.IsError) return;
            var segs = new List<Seg>();
            bool boolalpha = false;
            foreach (var it in items)
            {
                if (it is NameExpr en && en.Name == "endl" && (en.Full == "std::endl" || usingStd && en.Qual.Count == 0)) { segs.Add(new Seg { Text = "\n", At = Sp(it) }); continue; }
                if (it is NameExpr ba && (ba.Name == "boolalpha" || ba.Name == "noboolalpha") && (ba.Full == "std::" + ba.Name || usingStd && ba.Qual.Count == 0)) { boolalpha = ba.Name == "boolalpha"; continue; }
                var v = BindExpr(it, "print");
                if (v.T.IsError) return;
                if (v.T.Kind == CK.Stream || v.T.Kind == CK.Endl) { Err("unexpected stream in std::cout chain", it); return; }
                if (v.E is IrLiteral sl && sl.Kind == IrLitKind.String) { segs.Add(new Seg { Text = sl.Str, At = v.E.Span }); continue; }
                if (v.T.Kind == CK.Bool && !boolalpha)
                {
                    Err("std::cout prints a bool as 1 or 0, the game as true or false: write std::cout << std::boolalpha << ... (or use std::println(\"{}\", b))", it);
                    return;
                }
                switch (v.T.Kind)
                {
                    case CK.Int: case CK.Float: case CK.Bool: case CK.Str: case CK.Handle: case CK.Position: case CK.Any:
                        segs.Add(new Seg { Value = v.E, At = v.E.Span });
                        break;
                    case CK.Void:
                        Err("no match for 'operator<<' (operand types are 'std::ostream' and 'void')", it);
                        return;
                    default:
                        Err("no match for 'operator<<' (operand types are 'std::ostream' and '" + Show(v.T) + "')" + (v.T.IsCollection ? "; print the elements one by one or use std::println(\"{}\", x)" : ""), it);
                        return;
                }
            }
            var first = be;
            while (first.L is BinaryExpr bl && bl.Op == "<<") first = bl;
            Emit(segs, false, Sp(root), span, be, outList);
        }

        /// <summary>
        /// Turns printed text into game print calls: one print per completed line (the game adds the line break).
        /// Text without a final \n is kept in the module's buffer (__jout) until a later line completes.
        /// </summary>
        void Emit(List<Seg> segs, bool quick, Span nameSpan, Span stmtSpan, Node call, List<IrStmt> outList)
        {
            var lines = new List<List<Seg>>();
            var curLine = new List<Seg>();
            foreach (var s in segs)
            {
                if (s.Value != null) { curLine.Add(s); continue; }
                var parts = s.Text.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > 0) curLine.Add(new Seg { Text = parts[i], At = s.At });
                    if (i < parts.Length - 1) { lines.Add(curLine); curLine = new List<Seg>(); }
                }
            }
            bool openEnd = curLine.Count > 0;
            if (quick)
            {
                if (openEnd || lines.Count == 0) lines.Add(curLine);
                foreach (var l in lines) outList.Add(PrintStmt("quick_print", LineExpr(l, call), nameSpan, stmtSpan));
                return;
            }
            foreach (var l in lines)
            {
                if (bufferMode)
                {
                    IrExpr text = LineExpr(l, call);
                    var withBuf = l.Count == 0 ? (IrExpr)BufRef(call) : new IrBinary { Op = IrBinOp.Concat, Left = BufRef(call), Right = text, Synthetic = true, Span = Sp(call), Type = IrType.String };
                    outList.Add(PrintStmt("print", withBuf, nameSpan, stmtSpan));
                    var empty = Syn(IrLiteral.OfString(""), call);
                    outList.Add(new IrAssign { Target = BufRef(call), Value = empty, Synthetic = true, Span = stmtSpan, OpSpan = nameSpan });
                }
                else outList.Add(PrintStmt("print", LineExpr(l, call), nameSpan, stmtSpan));
            }
            if (openEnd)
            {
                if (!bufferMode) { outList.Add(PrintStmt("print", LineExpr(curLine, call), nameSpan, stmtSpan)); return; }
                outList.Add(new IrAssign { Target = BufRef(call), Op = IrBinOp.Concat, Value = LineExpr(curLine, call), Synthetic = true, Span = stmtSpan, OpSpan = nameSpan });
            }
        }

        IrExpr BufRef(Node at) => new IrName(outBuf) { Synthetic = true, Span = Sp(at), Type = IrType.String };

        IrStmt PrintStmt(string fn, IrExpr arg, Span nameSpan, Span stmtSpan)
        {
            var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = fn, NameSpan = nameSpan, Span = stmtSpan, Type = IrType.Void };
            call.Args.Add(arg);
            return new IrExprStmt { Expr = call, Span = stmtSpan };
        }

        IrExpr LineExpr(List<Seg> line, Node at)
        {
            if (line.Count == 0) return Syn(IrLiteral.OfString(""), at);
            return ConcatOf(line, at);
        }

        IrExpr ConcatOf(List<Seg> segs, Node at)
        {
            IrExpr acc = null;
            foreach (var s in segs)
            {
                IrExpr x = s.Value ?? new IrLiteral { Kind = IrLitKind.String, Str = s.Text, Type = IrType.String, Span = s.At };
                acc = acc == null ? x : new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = x, Synthetic = true, Span = Sp(at), Type = IrType.String };
            }
            if (acc == null) acc = Syn(IrLiteral.OfString(""), at);
            // a lone value printed as text: str() keeps the number formatting of the game
            return acc;
        }

        // ------------------------------------------------------------------ buffering pre-scan

        /// <summary>Whether some output of the file does not end with a line break (then output is buffered).</summary>
        bool NeedsBuffer(Unit u)
        {
            bool need = false;
            void Walk(object n)
            {
                if (need || n == null) return;
                if (n is ExprStmt es)
                {
                    if (es.E is CallExpr c && c.Callee is NameExpr nm)
                    {
                        string fmt = c.Args.Count > 0 && c.Args[0] is Lit l && l.Kind == LitKind.String ? l.S : null;
                        if (nm.Name == "printf" && nm.Qual.Count == 0 && fmt != null && !fmt.EndsWith("\n")) need = true;
                        if (nm.Name == "print" && (nm.Qual.Count == 1 && nm.Qual[0] == "std" || nm.Qual.Count == 0 && Cpp) && fmt != null && !fmt.EndsWith("\n")) need = true;
                    }
                    else if (es.E is BinaryExpr b && b.Op == "<<" && Cpp)
                    {
                        Expr root = b;
                        while (root is BinaryExpr bb && bb.Op == "<<") root = bb.L;
                        if (root is NameExpr rn && rn.Name == "cout")
                        {
                            var last = b.R;
                            bool ends = (last is NameExpr en && en.Name == "endl") || (last is Lit ll && (ll.Kind == LitKind.String || ll.Kind == LitKind.Char) && ll.S.EndsWith("\n"));
                            if (!ends) need = true;
                        }
                    }
                }
                foreach (var ch in AstChildren(n)) Walk(ch);
            }
            foreach (var it in u.Items) if (it is FuncDecl f && f.Body != null) Walk(f.Body);
            return need;
        }
    }
}
