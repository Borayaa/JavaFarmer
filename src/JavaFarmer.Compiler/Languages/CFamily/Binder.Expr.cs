using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.CFamily
{
    /// <summary>A bound expression: its IR, checker type, and whether it designates a modifiable object.</summary>
    internal sealed class BE
    {
        public IrExpr E;
        public CT T;
        public bool LValue;
        public Sym Sym;
        public static BE Error() => new BE { E = IrLiteral.Null(), T = CT.Error };
        public BE() { }
        public BE(IrExpr e, CT t) { E = e; T = t; if (e != null && t != null && !t.IsError) e.Type = t.Ir(); }
    }

    internal sealed partial class Binder
    {
        static readonly HashSet<string> Bags = new HashSet<string> { "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards" };
        static readonly HashSet<string> Dirs = new HashSet<string> { "North", "East", "South", "West" };

        BE BindExpr(Expr e, string ctx = null)
        {
            if (e == null) return BE.Error();
            BE r;
            try { r = BindCore(e, ctx); }
            catch (InvalidOperationException ex) { Err(ex.Message, e); r = BE.Error(); }
            if (r == null) r = BE.Error();
            if (r.E != null)
            {
                if (r.E.Span.IsNone) r.E.Span = Sp(e);
                if (e.ParenStart >= 0) r.E.ParenSpan = Sp(e.ParenStart, e.ParenEnd);
                if (!r.T.IsError && (r.E.Type == null || !r.E.Type.IsKnown)) r.E.Type = r.T.Ir();
            }
            return r;
        }

        BE BindCore(Expr e, string ctx)
        {
            switch (e)
            {
                case Lit l: return Literal(l, ctx);
                case NameExpr n: return Name(n, ctx);
                case BinaryExpr b: return Binary(b, ctx);
                case UnaryExpr u: return Unary(u);
                case IncDecExpr id:
                    Err((id.Prefix ? id.Op + Text(id.Target) : Text(id.Target) + id.Op) + " as a value is not supported in JavaFarmer (write it as its own statement)", id);
                    return BE.Error();
                case AssignExpr a:
                    Err("assignment as a value is not supported in JavaFarmer (write it as its own statement)", a.OpStart, a.OpEnd);
                    return BE.Error();
                case CondExpr c: return Conditional(c);
                case CallExpr call: return Call(call, ctx);
                case IndexExpr ix: return Index(ix);
                case MemberExpr me: return Member(me);
                case CastExpr ca: return Cast(ca);
                case InitList il:
                    Err("a braced initializer list needs a type here", il);
                    return BE.Error();
                case ConstructExpr ce:
                    {
                        if (ce.Compound && C)
                        {
                            var t = Resolve(ce.Type);
                            if (t.IsError) return BE.Error();
                            if (t.Kind != CK.Position) { Err("compound literals of type " + Show(t) + " are not supported in JavaFarmer (only (Position){x, y})", ce); return BE.Error(); }
                            return Construct(t, ce.Args, true, ce);
                        }
                        if (C) { Err("expected expression before '" + ce.Type?.Written + "'", ce); return BE.Error(); }
                        var ct = Resolve(ce.Type);
                        if (ct.IsError) return BE.Error();
                        return Construct(ct, ce.Args, ce.Braces, ce);
                    }
                case LambdaExpr lam: return Lambda(lam, null);
                case SizeofExpr so:
                    Err("sizeof is not supported in JavaFarmer (except sizeof(a) / sizeof(a[0]) for the length of an array)", so);
                    return BE.Error();
                case CommaExpr cm:
                    Err("the comma operator is not supported in JavaFarmer", cm);
                    return BE.Error();
            }
            Err("expected expression", e);
            return BE.Error();
        }

        // ------------------------------------------------------------------ literals

        BE Literal(Lit l, string ctx)
        {
            switch (l.Kind)
            {
                case LitKind.Int:
                    {
                        var s = l.Suffix.ToLowerInvariant();
                        if (s.Contains("u")) { Err("unsigned constants are not supported in JavaFarmer", l); return BE.Error(); }
                        var t = s.Contains("l") ? new CT { Kind = CK.Int, Long = true } : CT.Int;
                        return new BE(IrLiteral.OfInt(l.I), t);
                    }
                case LitKind.Float:
                    {
                        var t = l.Suffix.ToLowerInvariant() == "f" ? new CT { Kind = CK.Float, Single = true } : CT.Float;
                        return new BE(IrLiteral.OfFloat(l.F), t);
                    }
                case LitKind.String:
                    {
                        if (ctx != "print" && (l.S.IndexOf('\n') >= 0 || l.S.IndexOf('\r') >= 0))
                        {
                            Err("line breaks inside strings are not supported by the game (\\n only works in printed text)", l);
                            return BE.Error();
                        }
                        if (l.S.IndexOf('\0') >= 0) { Err("\\0 inside strings is not supported by the game", l); return BE.Error(); }
                        var t = CT.CStr.Clone(); t.IsLiteralStr = true;
                        return new BE(IrLiteral.OfString(l.S), t);
                    }
                case LitKind.Char:
                    if (ctx == "print") return new BE(IrLiteral.OfString(l.S), CT.Char);
                    Err("character constants are not supported in JavaFarmer (use a string such as \"" + l.S.Replace("\n", "\\n") + "\")", l);
                    return BE.Error();
                case LitKind.Bool: return new BE(IrLiteral.OfBool(l.B), CT.Bool);
                case LitKind.Null:
                case LitKind.Nullptr:
                    return new BE(IrLiteral.Null(), CT.Null);
            }
            return BE.Error();
        }

        // ------------------------------------------------------------------ names

        BE Name(NameExpr n, string ctx)
        {
            if (n.Qual.Count > 0)
            {
                if (C) { Err("'::' is not valid in C (write " + n.Name + ")", n); return BE.Error(); }
                return QualifiedName(n, ctx);
            }
            var name = n.Name;
            var sym = Lookup(name);
            if (sym != null) return SymValue(sym, n);
            if (Dirs.Contains(name))
            {
                if (!Farm) { UndeclaredStub(name, n); return BE.Error(); }
                return new BE(new IrConst("Directions", name) { Span = Sp(n) }, CT.Handle("Direction"));
            }
            if (name == "NULL")
            {
                if (C ? !(Farm || Has("stdio.h", "stdlib.h", "string.h", "stddef.h")) : !(Farm || Has("cstdio", "cstdlib", "stdio.h", "iostream", "string", "cmath")))
                { Err(C ? "'NULL' undeclared (first use in this function); include <stddef.h>" : "'NULL' was not declared in this scope", n); return BE.Error(); }
                if (Cpp && ctx != "nullcheck") { }
                return new BE(IrLiteral.Null(), CT.Null);
            }
            if (Cpp && usingStd && (name == "cout" || name == "endl" || name == "cerr" || name == "nullopt")) return StdName(name, n);
            if (Bags.Contains(name) || name == "Directions")
            {
                if (!Farm) { UndeclaredStub(name, n); return BE.Error(); }
                Err("'" + name + "' cannot be used as a value (use " + name + (C ? "." : "::") + "Member)", n);
                return BE.Error();
            }
            // a game builtin used as a function value (spawn_drone(harvest))
            var api = BuiltinByName(name);
            if (api != null)
            {
                if (!Farm) { UndeclaredStub(name, n); return BE.Error(); }
                return new BE(new IrFuncRef { Builtin = api.PythonName, NameSpan = Sp(n), Span = Sp(n) }, CT.Func(BuiltinRet(api), BuiltinParamTypes(api)));
            }
            Undeclared(name, n);
            return BE.Error();
        }

        void UndeclaredStub(string name, Node at)
        {
            if (C) Err("'" + name + "' undeclared (first use in this function); did you forget '#include \"farm.h\"'?", at);
            else Err("'" + name + "' was not declared in this scope; did you forget '#include \"farm.hpp\"'?", at);
        }

        void Undeclared(string name, Node at, bool call = false)
        {
            var near = Nearest(name);
            var hint = near != null ? "; did you mean '" + near + "'?" : "";
            if (C)
            {
                if (call) Err("implicit declaration of function '" + name + "'" + hint, at);
                else Err((curFn == null ? "'" + name + "' undeclared here (not in a function)" : "'" + name + "' undeclared (first use in this function)") + hint, at);
            }
            else Err("'" + name + "' was not declared in this scope" + hint, at);
        }

        /// <summary>Closest visible name (gcc's "did you mean" spell check).</summary>
        string Nearest(string name)
        {
            string best = null;
            int bestD = name.Length <= 3 ? 1 : 2;
            void Try(string cand)
            {
                if (cand == null || cand == name || cand.StartsWith("__j")) return;
                if (Math.Abs(cand.Length - name.Length) > bestD) return;
                int dd = Levenshtein(name, cand);
                if (dd <= bestD && (best == null || dd < Levenshtein(name, best))) best = cand;
            }
            for (var s = cur; s != null; s = s.Parent) foreach (var k in s.Names.Keys) Try(k);
            if (Farm) foreach (var b in GameApi.Builtins) Try(ApiNameOf(b.PythonName));
            foreach (var k in Dirs) Try(k);
            if (C) foreach (var k in new[] { "printf", "puts", "list_add", "list_get", "list_len", "map_get", "map_put", "strlen", "str_eq", "str_of" }) Try(k);
            return best;
        }

        static int Levenshtein(string a, string b)
        {
            var dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return dp[a.Length, b.Length];
        }

        BE SymValue(Sym sym, Node at)
        {
            switch (sym.Kind)
            {
                case SymKind.Function:
                    sym.Fn.Used = true;
                    return new BE(new IrFuncRef { Function = sym.Ir, NameSpan = Sp(at), Span = Sp(at) }, CT.Func(sym.Fn.Ret, sym.Fn.Params));
                case SymKind.Define:
                    return new BE(new IrName(sym.Ir) { Span = Sp(at), NameSpan = Sp(at) }, sym.Type) { Sym = sym };
            }
            if (sym.PairFirst != null)
            {
                Err("using the std::pair '" + sym.Name + "' as a value is not supported in JavaFarmer (use " + sym.Name + ".first / " + sym.Name + ".second)", at);
                return BE.Error();
            }
            var nm = new IrName(sym.Ir) { Span = Sp(at), NameSpan = Sp(at) };
            if (sym.Ir.Kind == IrSymbolKind.ExternalGlobal) nm.QualifierSpan = Sp(at);
            return new BE(nm, sym.Type) { LValue = true, Sym = sym };
        }

        BE StdName(string name, Node at)
        {
            switch (name)
            {
                case "cout":
                case "cerr":
                    if (!Has("iostream")) { Err("'" + name + "' is not a member of 'std'; did you forget '#include <iostream>'?", at); return BE.Error(); }
                    if (name == "cerr") { Err("std::cerr is not supported in JavaFarmer (use std::cout)", at); return BE.Error(); }
                    return new BE(null, CT.Stream);
                case "endl":
                    if (!Has("iostream")) { Err("'endl' is not a member of 'std'; did you forget '#include <iostream>'?", at); return BE.Error(); }
                    return new BE(null, CT.Endl);
                case "nullopt":
                    return new BE(IrLiteral.Null(), CT.Null);
            }
            Err("'std::" + name + "' is not supported in JavaFarmer", at);
            return BE.Error();
        }

        BE QualifiedName(NameExpr n, string ctx)
        {
            var q = n.Qual;
            // farm::X is the same as X
            if (q.Count >= 1 && q[0] == "farm")
            {
                if (!Farm) { Err("'farm' has not been declared; did you forget '#include \"farm.hpp\"'?", n); return BE.Error(); }
                if (q.Count == 1)
                {
                    if (Dirs.Contains(n.Name)) return new BE(new IrConst("Directions", n.Name) { Span = Sp(n) }, CT.Handle("Direction"));
                    var api = BuiltinByName(n.Name);
                    if (api != null) return new BE(new IrFuncRef { Builtin = api.PythonName, NameSpan = Sp(n.NameStart, n.End), Span = Sp(n) }, CT.Func(BuiltinRet(api), BuiltinParamTypes(api)));
                    Err("'" + n.Name + "' is not a member of 'farm'", n);
                    return BE.Error();
                }
                q = q.Skip(1).ToList();
            }
            if (q.Count == 1 && (Bags.Contains(q[0]) || q[0] == "Direction" || q[0] == "Directions"))
            {
                if (!Farm) { Err("'" + q[0] + "' has not been declared; did you forget '#include \"farm.hpp\"'?", n); return BE.Error(); }
                if (q[0] == "Direction" || q[0] == "Directions")
                {
                    if (!Dirs.Contains(n.Name)) { Err("'" + n.Name + "' is not a member of 'Direction'", n); return BE.Error(); }
                    return new BE(new IrConst("Directions", n.Name) { Span = Sp(n) }, CT.Handle("Direction"));
                }
                // any member name: the game decides which exist
                return new BE(new IrConst(q[0], n.Name) { Span = Sp(n) }, CT.Handle(GameApi.GameTypeOfCategory(q[0])));
            }
            if (q.Count == 1 && q[0] == "std")
            {
                switch (n.Name)
                {
                    case "cout": case "endl": case "cerr": case "nullopt": return StdName(n.Name, n);
                }
                Err("'std::" + n.Name + "' is not supported in JavaFarmer", n);
                return BE.Error();
            }
            if (q.Count == 2 && q[0] == "std" && q[1] == "string" && n.Name == "npos")
            {
                Err("std::string::npos is only supported in s.find(t) != std::string::npos", n);
                return BE.Error();
            }
            Err("'" + n.Full + "' is not supported in JavaFarmer", n);
            return BE.Error();
        }

        // ------------------------------------------------------------------ operators

        BE Binary(BinaryExpr b, string ctx)
        {
            switch (b.Op)
            {
                case "+": case "-": case "*": case "/": case "%": return Arith(b);
                case "==": case "!=": return Equality(b);
                case "<": case ">": case "<=": case ">=": return Relational(b);
                case "&&": case "||":
                    {
                        var l = Cond(b.L); var r = Cond(b.R);
                        return new BE(new IrBinary { Op = b.Op == "&&" ? IrBinOp.And : IrBinOp.Or, Left = l, Right = r, OpSpan = Sp(b.OpStart, b.OpEnd) }, CT.Bool);
                    }
                case "<<":
                    if (Cpp && IsStreamChain(b)) { Err("std::cout << ... is only supported as a statement in JavaFarmer", b); return BE.Error(); }
                    goto default;
                case "<=>": Err("the <=> operator is not supported in JavaFarmer", b.OpStart, b.OpEnd); return BE.Error();
                default:
                    Err("bitwise operators are not supported in JavaFarmer ('" + b.Op + "')", b.OpStart, b.OpEnd);
                    return BE.Error();
            }
        }

        bool IsStreamChain(Expr e)
        {
            while (e is BinaryExpr b && b.Op == "<<") e = b.L;
            return e is NameExpr n && n.Name == "cout" && (n.Qual.Count == 1 && n.Qual[0] == "std" || n.Qual.Count == 0 && usingStd);
        }

        string BinErr(string op, CT a, CT b) =>
            C ? "invalid operands to binary " + op + " (have '" + Show(a) + "' and '" + Show(b) + "')"
              : "no match for 'operator" + op + "' (operand types are '" + Show(a) + "' and '" + Show(b) + "')";

        BE Arith(BinaryExpr b)
        {
            // sizeof(a) / sizeof(a[0]): the length of an array
            if (b.Op == "/" && b.L is SizeofExpr sl && b.R is SizeofExpr sr) return ArrayLength(b, sl, sr);
            var l = BindExpr(b.L);
            var r = BindExpr(b.R);
            if (l.T.IsError || r.T.IsError) return BE.Error();
            var opSpan = Sp(b.OpStart, b.OpEnd);
            if (l.T.IsStr || r.T.IsStr || l.T.Kind == CK.Char || r.T.Kind == CK.Char)
            {
                if (b.Op == "+" && Cpp)
                {
                    bool ls = l.T.IsStr && l.T.StdString, rs = r.T.IsStr && r.T.StdString;
                    bool lok = l.T.IsStr || l.T.Kind == CK.Char, rok = r.T.IsStr || r.T.Kind == CK.Char;
                    if ((ls || rs) && lok && rok)
                        return new BE(new IrBinary { Op = IrBinOp.Concat, Left = l.E, Right = r.E, OpSpan = opSpan }, CT.StdStr);
                    if ((ls || rs) && (l.T.IsNumeric || r.T.IsNumeric || l.T.Kind == CK.Bool || r.T.Kind == CK.Bool))
                    {
                        Err(BinErr("+", l.T, r.T) + "; convert the number with std::to_string(x)", opSpan.Start, opSpan.End);
                        return BE.Error();
                    }
                    if (l.T.IsStr && r.T.IsStr)
                    {
                        Err("invalid operands of types '" + Show(l.T) + "' and '" + Show(r.T) + "' to binary 'operator+' (make one side a std::string: std::string(\"...\") + ...)", opSpan.Start, opSpan.End);
                        return BE.Error();
                    }
                    Err("pointer arithmetic is not supported in JavaFarmer (to join text use std::string and +)", opSpan.Start, opSpan.End);
                    return BE.Error();
                }
                if (C && b.Op == "+")
                {
                    Err(BinErr("+", l.T, r.T) + ": join strings with str_concat(a, b)", opSpan.Start, opSpan.End);
                    return BE.Error();
                }
                Err(BinErr(b.Op, l.T, r.T), opSpan.Start, opSpan.End);
                return BE.Error();
            }
            if (l.T.Kind == CK.Bool || r.T.Kind == CK.Bool)
            {
                Err("using a bool as a number is not supported in JavaFarmer", (l.T.Kind == CK.Bool ? b.L : b.R));
                return BE.Error();
            }
            if (l.T.Kind == CK.Any && l.T.Game == null) l = AsNumber(l);
            if (r.T.Kind == CK.Any && r.T.Game == null) r = AsNumber(r);
            if (!l.T.IsNumeric || !r.T.IsNumeric)
            {
                Err(BinErr(b.Op, l.T, r.T), opSpan.Start, opSpan.End);
                return BE.Error();
            }
            bool ints = l.T.Kind == CK.Int && r.T.Kind == CK.Int;
            IrBinOp op;
            switch (b.Op)
            {
                case "+": op = IrBinOp.Add; break;
                case "-": op = IrBinOp.Sub; break;
                case "*": op = IrBinOp.Mul; break;
                case "/": op = ints ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv; break;
                default:
                    if (!ints)
                    {
                        Err(C ? BinErr("%", l.T, r.T) + "; use fmod(a, b) for doubles" : "invalid operands of types '" + Show(l.T) + "' and '" + Show(r.T) + "' to binary 'operator%'; use std::fmod(a, b)", opSpan.Start, opSpan.End);
                        return BE.Error();
                    }
                    op = IrBinOp.ModTrunc;
                    break;
            }
            var rt = ints ? (l.T.Long || r.T.Long ? new CT { Kind = CK.Int, Long = true } : CT.Int) : CT.Float;
            return new BE(new IrBinary { Op = op, Left = l.E, Right = r.E, OpSpan = opSpan }, rt);
        }

        /// <summary>farm::Value (C++ measure()) used as a number.</summary>
        BE AsNumber(BE b) => new BE(b.E, CT.Float);

        BE ArrayLength(BinaryExpr b, SizeofExpr sl, SizeofExpr sr)
        {
            var target = sl.E;
            if (target == null) { Err("sizeof(type) / sizeof(...) is not supported in JavaFarmer", b); return BE.Error(); }
            var arr = BindExpr(target);
            if (arr.T.IsError) return BE.Error();
            if (arr.T.Kind != CK.Array) { Err("sizeof is only supported for the length of an array: sizeof(a) / sizeof(a[0])", sl); return BE.Error(); }
            if (arr.Sym != null && arr.Sym.ArrayParam) { Err("'sizeof' on array function parameter '" + arr.Sym.Name + "' will return the size of a pointer: pass the length as another parameter", sl); return BE.Error(); }
            bool okRight = (sr.E is IndexExpr ix && Text(ix.Target) == Text(target)) || sr.T != null;
            if (!okRight) { Err("write the length of an array as sizeof(a) / sizeof(a[0])", sr); return BE.Error(); }
            return new BE(new IrOpCall(IrOp.Len, arr.E) { NameSpan = Sp(sl), Span = Sp(b) }, CT.Int);
        }

        BE Equality(BinaryExpr b)
        {
            bool ne = b.Op == "!=";
            var opSpan = Sp(b.OpStart, b.OpEnd);
            // strcmp(a, b) == 0
            if (C && IsZero(b.R) && b.L is CallExpr sc && sc.Callee is NameExpr scn && scn.Name == "strcmp" && scn.Qual.Count == 0 && sc.Args.Count == 2)
            {
                if (!(Farm || Has("string.h"))) { Err("implicit declaration of function 'strcmp'; include <string.h>", scn); return BE.Error(); }
                var a1 = BindExpr(sc.Args[0]); var a2 = BindExpr(sc.Args[1]);
                if (a1.T.IsError || a2.T.IsError) return BE.Error();
                if (!a1.T.IsStr || !a2.T.IsStr) { Err("strcmp expects two strings", sc); return BE.Error(); }
                return new BE(new IrBinary { Op = ne ? IrBinOp.Ne : IrBinOp.Eq, Left = a1.E, Right = a2.E, OpSpan = Sp(scn) }, CT.Bool);
            }
            var cnt = CountCompare(b);
            if (cnt != null) return cnt;
            // C++: x.find(y) != end / npos  →  contains
            var contains = FindPattern(b);
            if (contains != null)
            {
                if (contains.T.IsError) return contains;
                if (!ne) return new BE(new IrUnary { Op = IrUnOp.Not, Operand = contains.E, OpSpan = opSpan, Synthetic = true }, CT.Bool);
                return contains;
            }
            var l = BindExpr(b.L, "nullcheck");
            var r = BindExpr(b.R, "nullcheck");
            if (l.T.IsError || r.T.IsError) return BE.Error();
            var lt = l.T; var rt = r.T;
            if (lt.Kind == CK.Any && lt.Game == null && rt.IsNumeric) lt = CT.Float;
            if (rt.Kind == CK.Any && rt.Game == null && lt.IsNumeric) rt = CT.Float;
            string problem = null;
            if (lt.IsNumeric && rt.IsNumeric) { }
            else if (lt.Kind == CK.Bool && (rt.Kind == CK.Bool || rt.IsNumeric) || rt.Kind == CK.Bool && lt.IsNumeric) { }
            else if (lt.Kind == CK.Null || rt.Kind == CK.Null)
            {
                var other = lt.Kind == CK.Null ? rt : lt;
                if (!Nullable(other) && other.Kind != CK.Null) problem = C ? "comparison between pointer and " + Show(other) : "no match for 'operator" + b.Op + "' (operand types are '" + Show(lt) + "' and '" + Show(rt) + "')";
            }
            else if (lt.Kind == CK.Handle && rt.Kind == CK.Handle)
            {
                if (lt.Game != rt.Game) problem = "comparing " + lt.Game + " with " + rt.Game + " is always " + (ne ? "true" : "false");
            }
            else if (lt.IsStr && rt.IsStr)
            {
                if (C || (!lt.StdString && !rt.StdString))
                    problem = C ? "comparing strings with " + b.Op + " compares their addresses; use str_eq(a, b) or strcmp(a, b) == 0" : "comparing const char* strings with " + b.Op + " compares their addresses; use std::string";
            }
            else if (lt.Kind == CK.Position && rt.Kind == CK.Position) { if (C) problem = BinErr(b.Op, lt, rt) + "; compare p.x and p.y"; }
            else if (lt.IsCollection && rt.IsCollection && lt.Same(rt)) { if (C) problem = "comparing collections with " + b.Op + " compares their addresses (not supported in JavaFarmer)"; }
            else if (lt.Kind == CK.Optional && rt.Kind == CK.Optional && lt.Same(rt)) { }
            else problem = BinErr(b.Op, lt, rt);
            if (problem != null) { Err(problem, opSpan.Start, opSpan.End); return BE.Error(); }
            return new BE(new IrBinary { Op = ne ? IrBinOp.Ne : IrBinOp.Eq, Left = l.E, Right = r.E, OpSpan = opSpan }, CT.Bool);
        }

        bool Nullable(CT t)
        {
            switch (t.Kind)
            {
                case CK.Handle: case CK.Optional: return true;
                case CK.Companion: case CK.Func: return C;
                case CK.Str: return C || !t.StdString;
                case CK.List: case CK.Map: case CK.Set: return C;
            }
            return false;
        }

        static bool IsZero(Expr e) => e is Lit l && l.Kind == LitKind.Int && l.I == 0;

        /// <summary>v.find(x) != v.end(), std::find(v.begin(), v.end(), x) != v.end(), s.find(t) != std::string::npos.</summary>
        BE FindPattern(BinaryExpr b)
        {
            if (!Cpp) return null;
            Expr findE = null, endE = null;
            if (b.L is CallExpr) { findE = b.L; endE = b.R; } else return null;
            var fc = (CallExpr)findE;
            bool IsEndOf(Expr e, string recvText) =>
                e is CallExpr ec && ec.Args.Count == 0 && ec.Callee is MemberExpr em && em.Name == "end" && Text(em.Target) == recvText;
            // m.find(k) != m.end()  /  s.find(t) != std::string::npos
            if (fc.Callee is MemberExpr fm && fm.Name == "find" && fc.Args.Count == 1)
            {
                var recvText = Text(fm.Target);
                bool npos = endE is NameExpr np && np.Name == "npos" && (np.Full == "std::string::npos" || (usingStd && np.Full == "string::npos"));
                if (!IsEndOf(endE, recvText) && !npos) return null;
                var recv = BindExpr(fm.Target);
                if (recv.T.IsError) return BE.Error();
                var rt = Deref(recv.T);
                var x = BindExpr(fc.Args[0]);
                if (x.T.IsError) return BE.Error();
                if (rt.Kind == CK.Str && npos) return new BE(new IrOpCall(IrOp.Contains, recv.E, Convert(x, CT.StdStr, fc.Args[0], "argument").E) { NameSpan = Sp(fm.NameStart, fm.NameEnd), Span = Sp(b) }, CT.Bool);
                if ((rt.Kind == CK.Map || rt.Kind == CK.Set) && !npos)
                {
                    var key = Convert(x, rt.Kind == CK.Map ? rt.Key : rt.Elem, fc.Args[0], "argument");
                    if (key.T.IsError) return BE.Error();
                    return new BE(new IrOpCall(IrOp.Contains, recv.E, key.E) { NameSpan = Sp(fm.NameStart, fm.NameEnd), Span = Sp(b) }, CT.Bool);
                }
                return null;
            }
            // std::find(v.begin(), v.end(), x) != v.end()
            if (fc.Callee is NameExpr fn && fn.Name == "find" && (fn.Full == "std::find" || (usingStd && fn.Qual.Count == 0)) && fc.Args.Count == 3)
            {
                if (!(fc.Args[0] is CallExpr bc && bc.Callee is MemberExpr bm && bm.Name == "begin" && bc.Args.Count == 0)) return null;
                var recvText = Text(bm.Target);
                if (!IsEndOf(fc.Args[1], recvText) || !IsEndOf(endE, recvText)) return null;
                if (!(Farm || Has("algorithm"))) { Err("'find' is not a member of 'std'; did you forget '#include <algorithm>'?", fn); return BE.Error(); }
                var recv = BindExpr(bm.Target);
                if (recv.T.IsError) return BE.Error();
                if (recv.T.Kind != CK.List && recv.T.Kind != CK.Array) { Err("std::find is only supported on std::vector in JavaFarmer", fc); return BE.Error(); }
                var x = Convert(BindExpr(fc.Args[2]), recv.T.Elem, fc.Args[2], "argument");
                if (x.T.IsError) return BE.Error();
                return new BE(new IrOpCall(IrOp.Contains, recv.E, x.E) { NameSpan = Sp(fn), Span = Sp(b) }, CT.Bool);
            }
            return null;
        }

        static CT Deref(CT t) => t.Kind == CK.Optional ? t.Elem : t;

        /// <summary>C++ m.count(k) compared with 0 / 1: the IR has membership (Contains), not a count.</summary>
        BE CountCompare(BinaryExpr b)
        {
            if (!Cpp || !(b.L is CallExpr c && c.Callee is MemberExpr me && me.Name == "count" && c.Args.Count == 1 && b.R is Lit l && l.Kind == LitKind.Int)) return null;
            bool? present = null;
            switch (b.Op)
            {
                case ">": if (l.I == 0) present = true; break;
                case ">=": if (l.I == 1) present = true; break;
                case "!=": if (l.I == 0) present = true; break;
                case "==": if (l.I == 1) present = true; else if (l.I == 0) present = false; break;
                case "<": if (l.I == 1) present = false; break;
            }
            if (present == null) return null;
            var has = BindExpr(c);
            if (has.T.IsError) return has;
            if (present.Value) return new BE(has.E, CT.Bool);
            return new BE(new IrUnary { Op = IrUnOp.Not, Operand = has.E, OpSpan = Sp(b.OpStart, b.OpEnd) }, CT.Bool);
        }

        BE Relational(BinaryExpr b)
        {
            var cc = CountCompare(b);
            if (cc != null) return cc;
            var l = BindExpr(b.L);
            var r = BindExpr(b.R);
            if (l.T.IsError || r.T.IsError) return BE.Error();
            var lt = l.T.Kind == CK.Any && l.T.Game == null ? CT.Float : l.T;
            var rt = r.T.Kind == CK.Any && r.T.Game == null ? CT.Float : r.T;
            if (lt.IsStr && rt.IsStr) { Err("comparing strings with " + b.Op + " is not supported in JavaFarmer", b.OpStart, b.OpEnd); return BE.Error(); }
            if (lt.Kind == CK.Bool || rt.Kind == CK.Bool) { Err("using a bool as a number is not supported in JavaFarmer", lt.Kind == CK.Bool ? b.L : b.R); return BE.Error(); }
            if (!lt.IsNumeric || !rt.IsNumeric) { Err(BinErr(b.Op, lt, rt), b.OpStart, b.OpEnd); return BE.Error(); }
            IrBinOp op = b.Op == "<" ? IrBinOp.Lt : b.Op == ">" ? IrBinOp.Gt : b.Op == "<=" ? IrBinOp.Le : IrBinOp.Ge;
            return new BE(new IrBinary { Op = op, Left = l.E, Right = r.E, OpSpan = Sp(b.OpStart, b.OpEnd) }, CT.Bool);
        }

        BE Unary(UnaryExpr u)
        {
            var opSpan = Sp(u.Start, u.OpEnd);
            switch (u.Op)
            {
                case "!":
                    {
                        var c = Cond(u.E);
                        return new BE(new IrUnary { Op = IrUnOp.Not, Operand = c, OpSpan = opSpan }, CT.Bool);
                    }
                case "-": case "+":
                    {
                        var x = BindExpr(u.E);
                        if (x.T.IsError) return x;
                        if (x.T.Kind == CK.Any && x.T.Game == null) x = AsNumber(x);
                        if (x.T.Kind == CK.Bool) { Err("using a bool as a number is not supported in JavaFarmer", u.E); return BE.Error(); }
                        if (!x.T.IsNumeric) { Err(C ? "wrong type argument to unary " + (u.Op == "-" ? "minus" : "plus") : "no match for 'operator" + u.Op + "' (operand type is '" + Show(x.T) + "')", u); return BE.Error(); }
                        return new BE(new IrUnary { Op = u.Op == "-" ? IrUnOp.Neg : IrUnOp.Plus, Operand = x.E, OpSpan = opSpan }, x.T);
                    }
                case "&":
                    {
                        var x = BindExpr(u.E);
                        if (x.T.IsError) return x;
                        if (x.T.Kind == CK.Func && x.E is IrFuncRef) return x;
                        Err("pointers are not supported in JavaFarmer ('&' takes the address of a variable)", opSpan.Start, opSpan.End);
                        return BE.Error();
                    }
                case "*":
                    {
                        var x = BindExpr(u.E);
                        if (x.T.IsError) return x;
                        if (Cpp && x.T.Kind == CK.Optional) return new BE(x.E, x.T.Elem) { LValue = false };
                        if (C && x.T.Kind == CK.Func) return x;
                        Err("pointers are not supported in JavaFarmer (unary '*')", opSpan.Start, opSpan.End);
                        return BE.Error();
                    }
            }
            Err("bitwise operators are not supported in JavaFarmer ('" + u.Op + "')", opSpan.Start, opSpan.End);
            return BE.Error();
        }

        BE Conditional(CondExpr c)
        {
            var cond = Cond(c.C);
            var a = BindExpr(c.T);
            var b = BindExpr(c.F);
            if (a.T.IsError || b.T.IsError) return BE.Error();
            CT t;
            if (a.T.Same(b.T)) t = a.T.IsStr ? (a.T.StdString || b.T.StdString ? CT.StdStr : CT.CStr) : a.T;
            else if (a.T.IsNumeric && b.T.IsNumeric) t = CT.Float;
            else if (a.T.Kind == CK.Null && Nullable(b.T)) t = b.T;
            else if (b.T.Kind == CK.Null && Nullable(a.T)) t = a.T;
            else if (a.T.IsStr && b.T.IsStr) t = CT.StdStr;
            else { Err(C ? "type mismatch in conditional expression" : "operands to '?:' have different types '" + Show(a.T) + "' and '" + Show(b.T) + "'", c.QStart, c.QStart + 1); return BE.Error(); }
            return new BE(new IrConditional { Cond = cond, Then = a.E, Else = b.E, QSpan = Sp(c.QStart, c.QStart + 1) }, Decay(t));
        }

        /// <summary>A condition: C truthiness made explicit (n != 0, p != NULL) as synthetic (free) comparisons.</summary>
        IrExpr Cond(Expr e)
        {
            var b = BindExpr(e);
            if (b.T.IsError) return IrLiteral.OfBool(true);
            return Truth(b, e);
        }

        IrExpr Truth(BE b, Node at)
        {
            switch (b.T.Kind)
            {
                case CK.Bool: return b.E;
                case CK.Int:
                case CK.Float:
                    if (b.E is IrLiteral l && (l.Kind == IrLitKind.Int || l.Kind == IrLitKind.Float))
                    {
                        var lit = IrLiteral.OfBool(l.Kind == IrLitKind.Int ? l.Int != 0 : l.Float != 0);
                        lit.Span = b.E.Span;
                        return lit;
                    }
                    return new IrBinary { Op = IrBinOp.Ne, Left = b.E, Right = Syn(IrLiteral.OfInt(0), at), Synthetic = true, Span = Sp(at), Type = IrType.Bool };
                case CK.Any:
                    if (b.T.Game == null) goto case CK.Int;
                    break;
            }
            if (Nullable(b.T) || b.T.Kind == CK.Companion && C)
                return new IrBinary { Op = IrBinOp.Ne, Left = b.E, Right = Syn(IrLiteral.Null(), at), Synthetic = true, Span = Sp(at), Type = IrType.Bool };
            Err(C ? "used " + Show(b.T) + " where a condition (bool) is required" : "could not convert '" + Text(at) + "' from '" + Show(b.T) + "' to 'bool'", at);
            return IrLiteral.OfBool(true);
        }

        // ------------------------------------------------------------------ conversions

        BE BindTo(Expr e, CT target, string ctx = "initialization")
        {
            if (target.IsError) return BE.Error();
            if (e is InitList il) return InitTo(il, target);
            if (e is LambdaExpr lam && target.Kind == CK.Func) return Lambda(lam, target);
            var b = BindExpr(e);
            return Convert(b, target, e, ctx);
        }

        BE InitTo(InitList il, CT t)
        {
            switch (t.Kind)
            {
                case CK.List: case CK.Set: case CK.Map:
                    if (C) { Err("a " + Show(t) + " cannot be initialized with { }: use " + (t.Kind == CK.List ? "list_of" : t.Kind == CK.Set ? "set_of" : "map_of") + "(...)", il); return BE.Error(); }
                    return Construct(t, il.Items, true, il);
                case CK.Position: return Construct(t, il.Items, true, il);
                case CK.Int: case CK.Float: case CK.Bool: case CK.Str: case CK.Handle:
                    if (il.Items.Count == 0) return new BE(DefaultValue(t, il), t);
                    if (il.Items.Count == 1) return BindTo(il.Items[0], t);
                    Err("too many initializers for '" + Show(t) + "'", il);
                    return BE.Error();
                case CK.Array:
                    Err("arrays can only be initialized in their declaration", il);
                    return BE.Error();
            }
            Err("a braced initializer list cannot be converted to " + Show(t) + " in JavaFarmer", il);
            return BE.Error();
        }

        BE Convert(BE b, CT target, Node at, string ctx)
        {
            if (b.T.IsError || target.IsError) return BE.Error();
            var s = b.T;
            if (target.Kind == CK.Any && target.Game == "auto") return b;
            if (s.Kind == CK.Void) { Err("void value not ignored as it ought to be", at); return BE.Error(); }
            if (s.Same(target))
            {
                if (s.IsStr && !s.StdString && target.StdString) return new BE(b.E, target);
                if (s.IsStr && s.StdString && !target.StdString && Cpp) { Err(ConvErr(s, target, ctx) + " (use .c_str() or a std::string)", at); return BE.Error(); }
                return new BE(b.E, target) { LValue = b.LValue, Sym = b.Sym };
            }
            switch (target.Kind)
            {
                case CK.Int:
                    if (s.Kind == CK.Float) return new BE(new IrCast { Target = IrType.Int, Operand = b.E, Synthetic = true, Span = b.E.Span }, target);
                    if (s.Kind == CK.Any && s.Game == null) return new BE { E = b.E, T = target };
                    if (s.Kind == CK.Bool) { Err("using a bool as a number is not supported in JavaFarmer", at); return BE.Error(); }
                    break;
                case CK.Float:
                    if (s.Kind == CK.Int || (s.Kind == CK.Any && s.Game == null)) return new BE { E = b.E, T = target };
                    if (s.Kind == CK.Bool) { Err("using a bool as a number is not supported in JavaFarmer", at); return BE.Error(); }
                    break;
                case CK.Bool:
                    if (s.IsNumeric || Nullable(s)) return new BE(Truth(b, at), CT.Bool);
                    break;
                case CK.Str:
                    if (s.Kind == CK.Null && C) return new BE(b.E, target);
                    if (s.Kind == CK.Char && Cpp && target.StdString) return new BE(b.E, target);
                    break;
                case CK.Position:
                    if (s.Kind == CK.Any && s.Game == null && Cpp) return new BE { E = b.E, T = target };
                    break;
                case CK.Optional:
                    if (s.Kind == CK.Null) return new BE(b.E, target);
                    if (s.Same(target.Elem) || (s.IsNumeric && target.Elem.IsNumeric)) return new BE { E = b.E, T = target };
                    break;
                case CK.Func:
                    if (s.Kind == CK.Null && C) return new BE(b.E, target);
                    if (s.Kind == CK.Func && FuncCompatible(s, target)) return new BE(b.E, target);
                    break;
            }
            if (s.Kind == CK.Null && Nullable(target)) return new BE(b.E, target);
            if (s.Kind == CK.Optional && s.Elem.Same(target)) { Err(ConvErr(s, target, ctx) + " (dereference it: *" + Text(at) + ")", at); return BE.Error(); }
            Err(ConvErr(s, target, ctx), at);
            return BE.Error();
        }

        bool FuncCompatible(CT s, CT t)
        {
            if (s.Params.Count != t.Params.Count) return false;
            for (int i = 0; i < s.Params.Count; i++) if (!s.Params[i].Same(t.Params[i])) return false;
            if (t.Elem.Kind == CK.Void && Cpp) return true;   // std::function<void()> discards a result
            return s.Elem.Same(t.Elem);
        }

        string ConvErr(CT from, CT to, string ctx)
        {
            if (C)
            {
                switch (ctx)
                {
                    case "assignment": return "incompatible types when assigning to type '" + Show(to) + "' from type '" + Show(from) + "'";
                    case "return": return "incompatible types when returning type '" + Show(from) + "' but '" + Show(to) + "' was expected";
                    default:
                        if (ctx != null && ctx.StartsWith("argument")) return "incompatible type for " + ctx + ": expected '" + Show(to) + "' but argument is of type '" + Show(from) + "'";
                        return "incompatible types when initializing type '" + Show(to) + "' using type '" + Show(from) + "'";
                }
            }
            var where = ctx == "assignment" ? " in assignment" : ctx == "return" ? " in return" : ctx != null && ctx.StartsWith("argument") ? " in " + ctx : " in initialization";
            return "cannot convert '" + Show(from) + "' to '" + Show(to) + "'" + where;
        }

        // ------------------------------------------------------------------ index / member / cast

        BE Index(IndexExpr ix)
        {
            var t = BindExpr(ix.Target);
            if (t.T.IsError) return BE.Error();
            var tt = t.T;
            switch (tt.Kind)
            {
                case CK.Array:
                case CK.List:
                    {
                        if (C && tt.Kind == CK.List) { Err("a List is not an array: read elements with list_get(" + Text(ix.Target) + ", i)", ix); return BE.Error(); }
                        var i = BindExpr(ix.Index);
                        if (i.T.IsError) return BE.Error();
                        if (i.T.Kind != CK.Int) { Err("array subscript is not an integer", ix.Index); return BE.Error(); }
                        return new BE(new IrIndex { Target = t.E, Index = i.E }, tt.Elem) { LValue = true };
                    }
                case CK.Map:
                    {
                        if (C) { Err("a Map is not an array: read values with map_get(" + Text(ix.Target) + ", key)", ix); return BE.Error(); }
                        var k = Convert(BindExpr(ix.Index), tt.Key, ix.Index, "argument 1 of 'operator[]'");
                        if (k.T.IsError) return BE.Error();
                        return new BE(new IrIndex { Target = t.E, Index = k.E }, tt.Elem) { LValue = true };
                    }
                case CK.Str:
                    Err("indexing a string (char) is not supported in JavaFarmer", ix);
                    return BE.Error();
            }
            Err(C ? "subscripted value is neither array nor pointer nor vector" : "no match for 'operator[]' (operand types are '" + Show(tt) + "' and 'int')", ix);
            return BE.Error();
        }

        BE Member(MemberExpr me)
        {
            // game constants (C): Entities.Carrot
            if (me.Target is NameExpr bag && bag.Qual.Count == 0 && (Bags.Contains(bag.Name) || bag.Name == "Directions") && Lookup(bag.Name) == null)
            {
                if (Cpp) { Err("use " + bag.Name + "::" + me.Name + " in C++", me); return BE.Error(); }
                if (!Farm) { UndeclaredStub(bag.Name, bag); return BE.Error(); }
                if (me.Arrow) { Err("invalid type argument of '->' (have 'const struct " + bag.Name + "')", me); return BE.Error(); }
                if (bag.Name == "Directions")
                {
                    Err("'Directions' undeclared: the directions are plain names in C (North, East, South, West)", bag);
                    return BE.Error();
                }
                return new BE(new IrConst(bag.Name, me.Name) { Span = Sp(me) }, CT.Handle(GameApi.GameTypeOfCategory(bag.Name)));
            }
            // std::pair loop variable
            if (me.Target is NameExpr pn && pn.Qual.Count == 0 && Lookup(pn.Name) is Sym ps && ps.PairFirst != null)
            {
                if (me.Name == "first") { var x = ps.PairFirst(); x.Span = Sp(me); return new BE(x, ps.Type.Key); }
                if (me.Name == "second") { var x = ps.PairSecond(); x.Span = Sp(me); return new BE(x, ps.Type.Elem); }
                Err("'std::pair' has no member named '" + me.Name + "'", me.NameStart, me.NameEnd);
                return BE.Error();
            }
            var t = BindExpr(me.Target);
            if (t.T.IsError) return BE.Error();
            var tt = t.T;
            if (me.Arrow)
            {
                if (Cpp && tt.Kind == CK.Optional) tt = tt.Elem;
                else if (C && tt.Kind == CK.Companion) { }
                else { Err(C ? "invalid type argument of '->' (have '" + Show(tt) + "')" : "base operand of '->' has non-pointer type '" + Show(tt) + "'", me); return BE.Error(); }
            }
            else if (C && tt.Kind == CK.Companion)
            {
                Err("'" + Text(me.Target) + "' is a pointer; did you mean to use '->'?", me);
                return BE.Error();
            }
            else if (tt.Kind == CK.Optional) { Err("'" + Show(tt) + "' has no member named '" + me.Name + "' (use ->)", me.NameStart, me.NameEnd); return BE.Error(); }
            var nameSpan = Sp(me.NameStart, me.NameEnd);
            switch (tt.Kind)
            {
                case CK.Position:
                    if (me.Name == "x") return new BE(new IrOpCall(IrOp.PosX, t.E) { NameSpan = nameSpan }, CT.Int);
                    if (me.Name == "y") return new BE(new IrOpCall(IrOp.PosY, t.E) { NameSpan = nameSpan }, CT.Int);
                    break;
                case CK.Companion:
                    switch (me.Name)
                    {
                        case "entity": return new BE(new IrOpCall(IrOp.CompanionEntity, t.E) { NameSpan = nameSpan }, CT.Handle("Entity"));
                        case "position": return new BE(new IrOpCall(IrOp.CompanionPos, t.E) { NameSpan = nameSpan }, CT.Position);
                        case "x": case "y":
                            {
                                var pos = new IrOpCall(IrOp.CompanionPos, t.E) { NameSpan = nameSpan, Synthetic = true, Span = Sp(me), Type = IrType.Game("Position") };
                                return new BE(new IrOpCall(me.Name == "x" ? IrOp.PosX : IrOp.PosY, pos) { NameSpan = nameSpan }, CT.Int);
                            }
                    }
                    break;
            }
            if (tt.Kind == CK.List || tt.Kind == CK.Map || tt.Kind == CK.Set || tt.Kind == CK.Str)
                Err(Cpp ? "invalid use of member function '" + me.Name + "' (did you forget the '()' ?)" : "request for member '" + me.Name + "' in something not a structure or union", me.NameStart, me.NameEnd);
            else
                Err(C ? "'" + Show(tt) + "' has no member named '" + me.Name + "'" : "'" + Show(tt) + "' has no member named '" + me.Name + "'", me.NameStart, me.NameEnd);
            return BE.Error();
        }

        BE Cast(CastExpr ca)
        {
            var t = Resolve(ca.Type);
            if (t.IsError) return BE.Error();
            var x = BindExpr(ca.E);
            if (x.T.IsError) return BE.Error();
            var opSpan = Sp(ca.Start, ca.OpEnd);
            switch (t.Kind)
            {
                case CK.Int:
                    if (x.T.Kind == CK.Float || (x.T.Kind == CK.Any && x.T.Game == null)) return new BE(new IrCast { Target = IrType.Int, Operand = x.E, OpSpan = opSpan }, t);
                    if (x.T.Kind == CK.Int) return new BE(new IrCast { Target = IrType.Int, Operand = x.E, OpSpan = opSpan }, t);
                    if (x.T.Kind == CK.Bool) { Err("converting a bool to a number is not supported in JavaFarmer", ca); return BE.Error(); }
                    break;
                case CK.Float:
                    if (x.T.IsNumeric || (x.T.Kind == CK.Any && x.T.Game == null)) return new BE(new IrCast { Target = IrType.Float, Operand = x.E, OpSpan = opSpan }, t);
                    if (x.T.Kind == CK.Bool) { Err("converting a bool to a number is not supported in JavaFarmer", ca); return BE.Error(); }
                    break;
                case CK.Bool:
                    return new BE(Truth(x, ca.E), CT.Bool);
                case CK.Void:
                    Err("casting to void is not supported in JavaFarmer", ca);
                    return BE.Error();
            }
            if (x.T.Same(t)) return x;
            if (x.T.IsStr && t.IsStr) return new BE(x.E, t);
            Err((C ? "conversion to non-scalar type '" : "invalid cast to type '") + Show(t) + "' (from '" + Show(x.T) + "') is not supported in JavaFarmer", ca);
            return BE.Error();
        }

        // ------------------------------------------------------------------ construction (C++ objects, (Position){x, y})

        BE Construct(CT t, List<Expr> args, bool braces, Node at)
        {
            var span = Sp(at);
            switch (t.Kind)
            {
                case CK.Position:
                    {
                        if (args.Count != 2) { Err("a Position needs 2 values {x, y}", at); return BE.Error(); }
                        var tl = new IrTupleLit { Span = span };
                        foreach (var a in args) { var v = BindTo(a, CT.Int); if (v.T.IsError) return BE.Error(); tl.Items.Add(v.E); }
                        return new BE(tl, CT.Position);
                    }
                case CK.Int: case CK.Float: case CK.Bool:
                    {
                        if (args.Count == 0) return new BE(DefaultValue(t, at), t);
                        if (args.Count != 1) { Err("too many arguments to " + Show(t) + "(...)", at); return BE.Error(); }
                        if (braces) { var v = BindExpr(args[0]); if (v.T.Kind == CK.Float && t.Kind == CK.Int) { Err("narrowing conversion of '" + Text(args[0]) + "' from 'double' to 'int'", args[0]); return BE.Error(); } return Convert(v, t, args[0], "initialization"); }
                        var x = BindExpr(args[0]);
                        if (x.T.IsError) return x;
                        if (t.Kind == CK.Int && (x.T.Kind == CK.Float || x.T.Kind == CK.Int)) return new BE(new IrCast { Target = IrType.Int, Operand = x.E, OpSpan = span }, t);
                        if (t.Kind == CK.Float && x.T.IsNumeric) return new BE(new IrCast { Target = IrType.Float, Operand = x.E, OpSpan = span }, t);
                        return Convert(x, t, args[0], "initialization");
                    }
                case CK.Str:
                    {
                        if (args.Count == 0) return new BE(DefaultValue(t, at), t);
                        if (args.Count != 1) { Err("std::string(n, c) and other string constructors are not supported in JavaFarmer", at); return BE.Error(); }
                        var x = BindExpr(args[0]);
                        if (x.T.IsError) return x;
                        if (!x.T.IsStr) { Err("no matching function for call to 'std::string(" + Show(x.T) + ")'; use std::to_string(x)", at); return BE.Error(); }
                        return new BE(x.E, CT.StdStr);
                    }
                case CK.List:
                    {
                        if (braces)
                        {
                            var ll = new IrListLit { Span = span };
                            foreach (var a in args) { var v = BindTo(a, t.Elem); if (v.T.IsError) return BE.Error(); ll.Items.Add(v.E); }
                            return new BE(ll, t);
                        }
                        if (args.Count == 0) return new BE(new IrListLit { Span = span }, t);
                        if (args.Count == 1)
                        {
                            var a0 = BindExpr(args[0]);
                            if (a0.T.IsError) return a0;
                            if (a0.T.Kind == CK.Int)
                            {
                                var dflt = t.Elem.Kind == CK.List ? new IrListLit { Span = span, Type = t.Elem.Ir() } : DefaultValue(t.Elem, at);
                                if (t.Elem.Kind == CK.List) { Err("a vector of n empty vectors is not supported in JavaFarmer: use std::vector<std::vector<T>> g(w, std::vector<T>(h, value))", at); return BE.Error(); }
                                if (dflt == null) return BE.Error();
                                return new BE(new IrOpCall(IrOp.ArrayNew, a0.E, dflt) { Span = span, NameSpan = span }, t);
                            }
                            if (a0.T.Kind == CK.List && a0.T.Elem.Same(t.Elem) || a0.T.Kind == CK.Set && a0.T.Elem.Same(t.Elem))
                                return new BE(new IrOpCall(IrOp.ListCopy, a0.E) { Span = span, NameSpan = span }, t);
                            Err("no matching constructor for " + Show(t) + "(" + Show(a0.T) + ")", at);
                            return BE.Error();
                        }
                        if (args.Count == 2)
                        {
                            var n = BindExpr(args[0]);
                            if (n.T.IsError) return n;
                            if (n.T.Kind != CK.Int) { Err("the size of a vector must be an int", args[0]); return BE.Error(); }
                            if (t.Elem.Kind == CK.List)
                            {
                                // vector<vector<T>> g(w, vector<T>(h, v))
                                if (args[1] is ConstructExpr inner && !inner.Braces && inner.Args.Count == 2)
                                {
                                    var it = Resolve(inner.Type);
                                    if (it.IsError) return BE.Error();
                                    if (!it.Same(t.Elem)) { Err("the inner vector must be " + Show(t.Elem), args[1]); return BE.Error(); }
                                    var h = BindTo(inner.Args[0], CT.Int);
                                    var v2 = BindTo(inner.Args[1], it.Elem);
                                    if (h.T.IsError || v2.T.IsError) return BE.Error();
                                    return new BE(new IrOpCall(IrOp.ArrayNew2, n.E, h.E, v2.E) { Span = span, NameSpan = span }, t);
                                }
                                Err("a vector of copies of a vector is only supported as std::vector<std::vector<T>> g(w, std::vector<T>(h, value)) in JavaFarmer", args[1]);
                                return BE.Error();
                            }
                            var v = BindTo(args[1], t.Elem);
                            if (v.T.IsError) return BE.Error();
                            return new BE(new IrOpCall(IrOp.ArrayNew, n.E, v.E) { Span = span, NameSpan = span }, t);
                        }
                        Err("no matching constructor for " + Show(t) + " with " + args.Count + " arguments", at);
                        return BE.Error();
                    }
                case CK.Set:
                    {
                        if (braces)
                        {
                            var sl = new IrSetLit { Span = span };
                            foreach (var a in args) { var v = BindTo(a, t.Elem); if (v.T.IsError) return BE.Error(); sl.Items.Add(v.E); }
                            return new BE(sl, t);
                        }
                        if (args.Count == 0) return new BE(new IrSetLit { Span = span }, t);
                        if (args.Count == 1)
                        {
                            var a0 = BindExpr(args[0]);
                            if (a0.T.IsError) return a0;
                            if ((a0.T.Kind == CK.Set || a0.T.Kind == CK.List) && a0.T.Elem.Same(t.Elem)) return new BE(new IrOpCall(IrOp.SetCopy, a0.E) { Span = span, NameSpan = span }, t);
                        }
                        Err("this " + Show(t) + " constructor is not supported in JavaFarmer", at);
                        return BE.Error();
                    }
                case CK.Map:
                    {
                        if (braces)
                        {
                            var ml = new IrMapLit { Span = span };
                            foreach (var a in args)
                            {
                                if (!(a is InitList pair) || pair.Items.Count != 2) { Err("each element of a map initializer is {key, value}", a); return BE.Error(); }
                                var k = BindTo(pair.Items[0], t.Key);
                                var v = BindTo(pair.Items[1], t.Elem);
                                if (k.T.IsError || v.T.IsError) return BE.Error();
                                ml.Keys.Add(k.E); ml.Values.Add(v.E);
                            }
                            return new BE(ml, t);
                        }
                        if (args.Count == 0) return new BE(new IrMapLit { Span = span }, t);
                        if (args.Count == 1)
                        {
                            var a0 = BindExpr(args[0]);
                            if (a0.T.IsError) return a0;
                            if (a0.T.Kind == CK.Map && a0.T.Same(t)) return new BE(new IrOpCall(IrOp.MapCopy, a0.E) { Span = span, NameSpan = span }, t);
                        }
                        Err("this " + Show(t) + " constructor is not supported in JavaFarmer", at);
                        return BE.Error();
                    }
                case CK.Optional:
                    if (args.Count == 0) return new BE(IrLiteral.Null(), t);
                    if (args.Count == 1) return BindTo(args[0], t);
                    break;
                case CK.Func:
                    if (args.Count == 1) return BindTo(args[0], t);
                    break;
            }
            Err("constructing " + Show(t) + " is not supported in JavaFarmer", at);
            return BE.Error();
        }

        // ------------------------------------------------------------------ lambdas (C++)

        BE Lambda(LambdaExpr lam, CT expected)
        {
            if (C) { Err("lambdas are not supported in C (pass a function name)", lam); return BE.Error(); }
            foreach (var cap in lam.Captures)
                if (Lookup(cap.Name) == null) { Err("'" + cap.Name + "' was not declared in this scope", lam.Start, lam.HeaderEnd); return BE.Error(); }
            var irl = new IrLambda { HeaderSpan = Sp(lam.Start, lam.HeaderEnd), Span = Sp(lam) };
            var ps = new List<CT>();
            var outer = cur;
            cur = new Scope { Parent = outer, FunctionTop = true };
            int savedLoop = loopDepth, savedSwitch = switchDepth;
            loopDepth = 0; switchDepth = 0;
            foreach (var p in lam.Params)
            {
                var pt = Resolve(p.Type, p.D, true);
                if (pt.IsError) { cur = outer; loopDepth = savedLoop; switchDepth = savedSwitch; return BE.Error(); }
                if (pt.Kind == CK.Any) { Err("'auto' lambda parameters are not supported in JavaFarmer", p.Type); cur = outer; loopDepth = savedLoop; switchDepth = savedSwitch; return BE.Error(); }
                var name = p.D?.Name ?? "_";
                var irs = new IrSymbol(name, IrSymbolKind.Param, pt.Ir()) { DeclSpan = Sp(p) };
                irl.Params.Add(new IrParam { Symbol = irs, Span = Sp(p), NameSpan = Sp(p.D?.NameStart ?? p.Start, p.D?.NameEnd ?? p.End) });
                if (p.D?.Name != null) cur.Names[p.D.Name] = new Sym { Name = name, Kind = SymKind.Param, Type = pt, Ir = irs };
                ps.Add(pt);
            }
            CT ret = lam.Ret != null ? Resolve(lam.Ret) : new CT { Kind = CK.Any, Game = "auto" };
            if (ret.IsError) { cur = outer; loopDepth = savedLoop; switchDepth = savedSwitch; return BE.Error(); }
            lambdaDepth++;
            lambdaRets.Add(ret);
            var body = new IrBlock { Span = Sp(lam.Body) };
            BlockInto(lam.Body, body.Stmts, false);
            var finalRet = lambdaRets[lambdaRets.Count - 1];
            lambdaRets.RemoveAt(lambdaRets.Count - 1);
            lambdaDepth--;
            loopDepth = savedLoop; switchDepth = savedSwitch;
            cur = outer;
            if (finalRet.Kind == CK.Any && finalRet.Game == "auto") finalRet = CT.Void;
            // a single "return x;" body is an expression lambda (Supplier)
            irl.ReturnType = finalRet.Ir();
            irl.ReturnsValue = finalRet.Kind != CK.Void;
            irl.Body = body;
            if (irl.ReturnsValue && finalRet.Kind != CK.Void && CanComplete(lam.Body))
            {
                Err("control reaches end of a lambda returning '" + Show(finalRet) + "'", lam.Body.End - 1, lam.Body.End);
                return BE.Error();
            }
            var ft = CT.Func(finalRet, ps, false, true);
            return new BE(irl, ft);
        }
    }
}
