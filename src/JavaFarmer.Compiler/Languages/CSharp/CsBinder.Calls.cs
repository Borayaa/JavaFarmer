using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.CSharp
{
    internal sealed partial class CsBinder
    {
        string ArgTypesText(List<CArg> args) => string.Join(", ", args.Select(a => (a.Value?.Type ?? CType.Error).ToString()));

        void ArgsLoose(CCall c)
        {
            foreach (var a in c.Args)
                if (a.Value != null && a.Value.Type == null) X(a.Value, a.Value is CLambda ? InferDelegate : null);
        }

        bool CheckPlainArgs(CCall c)
        {
            foreach (var a in c.Args)
            {
                if (a.Name != null) { Error("named arguments are not supported in JavaFarmer", a); return false; }
                if (a.Modifier != null) { Error("'" + a.Modifier + "' arguments are only supported for Dictionary.TryGetValue in JavaFarmer", a); return false; }
            }
            return true;
        }

        static string CallName(CCall c) => c.Callee is CName n ? n.Name : c.Callee is CMember m ? m.Name : "?";
        static Span NameSpan(CCall c) => c.Callee is CMember m ? new Span(m.NameStart, m.NameEnd) : new Span(c.Callee.Start, c.Callee.End);

        // ================================================================== call statements
        List<IrStmt> CallStatement(CCall c, Span span)
        {
            var res = new List<IrStmt>();
            if (c.Callee is CMember cm && cm.Target is CName cq && cq.Name == "Console" && scope.Lookup("Console") == null && !Own.Fields.ContainsKey("Console"))
                return ConsoleCall(c, cm, span);
            var e = Call(c, true, null);
            if (e == null) return res;
            res.Add(new IrExprStmt { Expr = e, Span = span });
            return res;
        }

        List<IrStmt> ConsoleCall(CCall c, CMember cm, Span span)
        {
            var res = new List<IrStmt>();
            c.Type = CType.Void;
            CheckPlainArgs(c);
            var nameSpan = Sp(cm.Start, cm.NameEnd);
            if (cm.Name == "WriteLine" || cm.Name == "Write")
            {
                if (c.Args.Count > 1) { ArgsLoose(c); Error("composite format strings (Console." + cm.Name + "(format, args)) are not supported in JavaFarmer; use string interpolation $\"...\"", c); return res; }
                if (cm.Name == "Write" && c.Args.Count == 0) { Error("CS1501: No overload for method 'Write' takes 0 arguments", cm.NameStart, c.End); return res; }
                IrExpr arg = null;
                if (c.Args.Count == 1)
                {
                    var a = c.Args[0].Value;
                    arg = X(a);
                    var at = a.Type ?? CType.Error;
                    if (at.Kind == CK.Void) { Error("CS1503: Argument 1: cannot convert from 'void' to 'bool'", a); return res; }
                    if (at.Kind == CK.Delegate) { Error("printing a function is not supported in JavaFarmer", a); return res; }
                }
                if (cm.Name == "Write")
                {
                    // Console.Write appends to the line buffer; Console.WriteLine prints it
                    res.Add(new IrAssign { Target = new IrName(bufSym) { Span = nameSpan, Synthetic = true }, Op = IrBinOp.Concat, Value = arg, OpSpan = nameSpan, Span = span, Synthetic = true });
                    return res;
                }
                var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = "print", NameSpan = nameSpan, Span = Sp(c), Type = IrType.Void };
                if (bufSym != null)
                {
                    // Console.WriteLine(x) after Console.Write: buffer += x; print(buffer); buffer = ""
                    if (arg != null) res.Add(new IrAssign { Target = new IrName(bufSym) { Span = nameSpan, Synthetic = true }, Op = IrBinOp.Concat, Value = arg, OpSpan = nameSpan, Span = span, Synthetic = true });
                    call.Args.Add(new IrName(bufSym) { Span = nameSpan, Synthetic = true });
                    res.Add(new IrExprStmt { Expr = call, Span = span });
                    var empty = IrLiteral.OfString(""); empty.Span = nameSpan; empty.Synthetic = true;
                    res.Add(new IrAssign { Target = new IrName(bufSym) { Span = nameSpan, Synthetic = true }, Value = empty, OpSpan = nameSpan, Span = span, Synthetic = true });
                    return res;
                }
                if (arg != null) call.Args.Add(arg);
                res.Add(new IrExprStmt { Expr = call, Span = span });
                return res;
            }
            ArgsLoose(c);
            if (string.Equals(cm.Name, "WriteLine", StringComparison.OrdinalIgnoreCase) || string.Equals(cm.Name, "Write", StringComparison.OrdinalIgnoreCase) || cm.Name == "println")
                Error("CS0117: 'Console' does not contain a definition for '" + cm.Name + "' (did you mean " + (cm.Name.Length > 6 ? "WriteLine" : "Write") + "?)", cm.NameStart, cm.NameEnd);
            else Error("Console." + cm.Name + " is not supported in JavaFarmer (use Console.WriteLine or Console.Write)", cm.NameStart, cm.NameEnd);
            return res;
        }

        // ================================================================== calls
        IrExpr Call(CCall c, bool asStatement, CType expected)
        {
            var r = CallCore(c, asStatement, expected);
            if (c.Type == null) c.Type = CType.Error;
            if (!asStatement && c.Type.Kind == CK.Void && r != null && !(c.Callee is CMember cm0 && cm0.Target is CName cq0 && cq0.Name == "Console"))
            {
                // the caller reports "cannot convert void" where the value is used
            }
            return r;
        }

        IrExpr CallCore(CCall c, bool asStatement, CType expected)
        {
            if (c.Callee is CName n)
            {
                var local = scope.Lookup(n.Name);
                if (local != null)
                {
                    var callee = X(n);
                    var lt = n.Type ?? CType.Error;
                    if (lt.IsError) { ArgsLoose(c); return null; }
                    if (lt.Kind != CK.Delegate) { ArgsLoose(c); Error("CS1955: Non-invocable member '" + n.Name + "' cannot be used like a method.", n); return null; }
                    return DelegateCall(c, callee, lt);
                }
                if (Own.Fields.TryGetValue(n.Name, out var fld))
                {
                    var callee = X(n);
                    if (fld.Type.Kind != CK.Delegate) { ArgsLoose(c); Error("CS1955: Non-invocable member '" + n.Name + "' cannot be used like a method.", n); return null; }
                    return DelegateCall(c, callee, fld.Type);
                }
                if (Own.Methods.TryGetValue(n.Name, out var ms)) return UserCall(c, ms, null, null);
                var api = CsApi.Find(n.Name);
                if (api != null) return GameCall(c, api, asStatement);
                if (usingStaticMath && IsMathName(n.Name)) return MathCall(c, n.Name);
                ArgsLoose(c);
                if (scope.DeclaredLater(n.Name)) { Error("CS0841: Cannot use local variable '" + n.Name + "' before it is declared", n); return null; }
                Error("CS0103: The name '" + n.Name + "' does not exist in the current context" + Hint(n.Name), n);
                return null;
            }
            if (c.Callee is CMember m)
            {
                if (m.Target is CName q && scope.Lookup(q.Name) == null && !Own.Fields.ContainsKey(q.Name))
                {
                    switch (q.Name)
                    {
                        case "Console":
                            {
                                if (!asStatement && (m.Name == "WriteLine" || m.Name == "Write")) { ArgsLoose(c); c.Type = CType.Void; Error("CS0029: Cannot implicitly convert type 'void' to '" + (expected?.ToString() ?? "object") + "'", c); return null; }
                                ArgsLoose(c);
                                Error("Console." + m.Name + " is not supported in JavaFarmer (use Console.WriteLine or Console.Write)", m.NameStart, m.NameEnd);
                                return null;
                            }
                        case "Math": return MathCall(c, m.Name);
                        case "Farm":
                            {
                                var api = CsApi.Find(m.Name);
                                if (api != null) return GameCall(c, api, asStatement);
                                ArgsLoose(c);
                                Error("CS0117: 'Farm' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                                return null;
                            }
                        case "Random":
                            ArgsLoose(c);
                            Error("System.Random is not supported in JavaFarmer; call Random() (the game's random number in [0, 1))", m);
                            return null;
                        case "Convert": case "String": case "Int32": case "Double": case "Char": case "Enumerable": case "Array": case "Thread": case "Task":
                            ArgsLoose(c);
                            Error(q.Name + "." + m.Name + " is not supported in JavaFarmer", m);
                            return null;
                    }
                    if (IsClassForm && q.Name == ClassName)
                    {
                        if (Own.Methods.TryGetValue(m.Name, out var ms)) return UserCall(c, ms, null, null);
                        ArgsLoose(c);
                        Error("CS0117: '" + ClassName + "' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                        return null;
                    }
                    var o = GetOther(q.Name);
                    if (o != null)
                    {
                        if (ForeignFailed(o, q)) { ArgsLoose(c); return null; }
                        if (!o.IsClass) { ArgsLoose(c); Error("CS0103: The name '" + q.Name + "' does not exist in the current context (the window " + q.Name + " has top-level statements; other files can only use a static class)", q); return null; }
                        if (o.Methods.TryGetValue(m.Name, out var om))
                        {
                            if (!om.IsAccessible) Error("CS0122: '" + q.Name + "." + m.Name + "(" + string.Join(", ", om.ParamTypes) + ")' is inaccessible due to its protection level", m.NameStart, m.NameEnd);
                            return UserCall(c, om, q.Name, q);
                        }
                        ArgsLoose(c);
                        Error("CS0117: '" + q.Name + "' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                        return null;
                    }
                }
                if (m.Target is CPredefined pd)
                {
                    ArgsLoose(c);
                    Error(pd.Name + "." + m.Name + " is not supported in JavaFarmer", m);
                    return null;
                }
                // instance member call
                if (m.Name == "TryGetValue") return TryGetValue(c, m);
                var target = X(m.Target);
                var tt = m.Target.Type ?? CType.Error;
                if (tt.IsError) { ArgsLoose(c); return null; }
                return MemberCall(c, m, target, tt, asStatement);
            }
            // (expr)(args): call a delegate value
            var cv = X(c.Callee);
            var ct = c.Callee.Type ?? CType.Error;
            if (ct.IsError) { ArgsLoose(c); return null; }
            if (ct.Kind != CK.Delegate) { ArgsLoose(c); Error("CS0149: Method name expected", c.Callee); return null; }
            return DelegateCall(c, cv, ct);
        }

        static bool IsMathName(string n) => n == "Min" || n == "Max" || n == "Abs" || n == "Floor" || n == "Ceiling" || n == "Round" || n == "Sqrt" || n == "Pow" || n == "Truncate";

        bool CheckArgsAgainst(CCall c, IList<CType> ps, int required, string what, List<IrExpr> outArgs)
        {
            if (!CheckPlainArgs(c)) { ArgsLoose(c); return false; }
            if (c.Args.Count < required || c.Args.Count > ps.Count)
            {
                int before = Errors.Count;
                ArgsLoose(c);
                if (Errors.Count > before) return false;   // like Roslyn: no overload error for arguments that are wrong themselves
                if (c.Args.Count < required && c.Args.Count < ps.Count)
                    Error("CS7036: There is no argument given that corresponds to the required parameter '" + what.Split('|')[1] + "' of '" + what.Split('|')[0] + "'", NameSpan(c).Start, c.End);
                else Error("CS1501: No overload for method '" + CallName(c) + "' takes " + c.Args.Count + " arguments", NameSpan(c).Start, c.End);
                return false;
            }
            bool ok = true;
            for (int i = 0; i < c.Args.Count; i++)
            {
                var a = c.Args[i].Value;
                var x = X(a, ps[i]);
                outArgs.Add(x);
                var t = a.Type ?? CType.Error;
                if (t.IsError || ps[i].IsError) continue;
                if (!CType.Assignable(t, ps[i])) { Error("CS1503: Argument " + (i + 1) + ": cannot convert from '" + (t.Kind == CK.Null ? "<null>" : t.ToString()) + "' to '" + ps[i] + "'", a); ok = false; }
            }
            return ok;
        }

        IrExpr UserCall(CCall c, CMethodSym ms, string otherFile, CExpr qualifier)
        {
            var args = new List<IrExpr>();
            int firstMissing = ms.HasDefault.TakeWhile(h => !h).Count();
            var missingName = c.Args.Count < ms.ParamNames.Count ? ms.ParamNames[Math.Min(c.Args.Count, ms.ParamNames.Count - 1)] : "";
            CheckArgsAgainst(c, ms.ParamTypes, ms.Required, (ms.IsLocal ? "" : ms.ClassName + ".") + ms.Display + "|" + missingName, args);
            c.Type = ms.ReturnType;
            var call = new IrCall { Kind = IrCallKind.Function, Function = otherFile == null ? ms.Ir : ExternalMethod(ms, otherFile), NameSpan = NameSpan(c) };
            if (qualifier != null) call.QualifierSpan = Sp(qualifier);
            call.Args.AddRange(args);
            return call;
        }

        IrExpr DelegateCall(CCall c, IrExpr callee, CType dt)
        {
            var args = new List<IrExpr>();
            CheckArgsAgainst(c, dt.Params, dt.Params.Count, dt + ".Invoke|arg", args);
            c.Type = dt.Elem;
            var call = new IrCall { Kind = IrCallKind.Value, Callee = callee, NameSpan = NameSpan(c) };
            call.Args.AddRange(args);
            return call;
        }

        // ------------------------------------------------------------------ game builtins
        IrExpr GameCall(CCall c, ApiBuiltin api, bool asStatement)
        {
            var name = GameApi.ToStyle(api.PythonName, NamingStyle.Pascal);
            var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = api.PythonName, NameSpan = NameSpan(c) };
            c.Type = CsApi.ReturnType(api);
            if (!CheckPlainArgs(c)) { ArgsLoose(c); return call; }
            if (c.Args.Count < api.MinArgs || c.Args.Count > api.MaxArgs)
            {
                int before = Errors.Count;
                ArgsLoose(c);
                if (Errors.Count > before) return call;
                var req = api.Params.FirstOrDefault(p => !p.Optional && !p.Variadic);
                if (c.Args.Count < api.MinArgs && req != null && api.Params.Count > c.Args.Count)
                    Error("CS7036: There is no argument given that corresponds to the required parameter '" + GameApi.ToStyle(api.Params[c.Args.Count].Name, NamingStyle.Camel) + "' of 'Farm." + name + "(" + string.Join(", ", api.Params.Select(p => ParamText(p))) + ")'", NameSpan(c).Start, c.End);
                else Error("CS1501: No overload for method '" + name + "' takes " + c.Args.Count + " arguments", NameSpan(c).Start, c.End);
                return call;
            }
            for (int i = 0; i < c.Args.Count; i++)
            {
                var p = api.Params[Math.Min(i, api.Params.Count - 1)];
                var a = c.Args[i].Value;
                if (p.Type != null && p.Type.Kind == IrTypeKind.Func)
                {
                    var x = X(a, InferDelegate);
                    call.Args.Add(x);
                    var at = a.Type ?? CType.Error;
                    if (!at.IsError && at.Kind != CK.Delegate)
                        Error(at.Kind == CK.String ? "CS1503: Argument " + (i + 1) + ": cannot convert from 'string' to 'System.Action' (" + name + " takes a function: pass a lambda like () => Work() or a method name like Work)"
                                                   : "CS1503: Argument " + (i + 1) + ": cannot convert from '" + at + "' to 'System.Action'", a);
                    else if (!at.IsError && at.Params.Count > 0) Error("CS1503: Argument " + (i + 1) + ": cannot convert from '" + at + "' to 'System.Action'", a);
                    continue;
                }
                var opts = CsApi.UnionOptions(p);
                if (opts != null)
                {
                    var x = X(a);
                    call.Args.Add(x);
                    var at = a.Type ?? CType.Error;
                    if (!at.IsError && !(at.Kind == CK.Game && opts.Contains(at.Game)))
                        Error("CS1503: Argument " + (i + 1) + ": cannot convert from '" + (at.Kind == CK.Null ? "<null>" : at.ToString()) + "' to '" + opts[0] + "'", a);
                    continue;
                }
                var pt = CsApi.ParamType(p);
                if (p.Variadic) pt = CType.Object;
                var ax = X(a, pt);
                call.Args.Add(ax);
                var t = a.Type ?? CType.Error;
                if (t.IsError) continue;
                if (t.Kind == CK.Void) Error("CS1503: Argument " + (i + 1) + ": cannot convert from 'void' to '" + pt + "'", a);
                else if (!CType.Assignable(t, pt)) Error("CS1503: Argument " + (i + 1) + ": cannot convert from '" + (t.Kind == CK.Null ? "<null>" : t.ToString()) + "' to '" + pt + "'", a);
                else if (p.Variadic && t.Kind == CK.Delegate) Error("printing a function is not supported in JavaFarmer", a);
            }
            return call;
        }

        static string ParamText(ApiParam p) => (p.Variadic ? "params object[]" : CsApi.UnionOptions(p)?[0] ?? CsApi.ParamType(p).ToString()) + " " + GameApi.ToStyle(p.Name, NamingStyle.Camel);

        // ------------------------------------------------------------------ Math
        IrExpr MathCall(CCall c, string name)
        {
            var nameSpan = NameSpan(c);
            if (!CheckPlainArgs(c)) { ArgsLoose(c); return null; }
            List<IrExpr> NumArgs(int n)
            {
                var xs = c.Args.Select(a => X(a.Value)).ToList();
                if (c.Args.Count != n)
                {
                    Error("CS1501: No overload for method '" + name + "' takes " + c.Args.Count + " arguments", nameSpan.Start, c.End);
                    return null;
                }
                for (int i = 0; i < xs.Count; i++)
                {
                    var t = c.Args[i].Value.Type ?? CType.Error;
                    if (!t.IsError && !t.IsNumeric) { Error("CS1503: Argument " + (i + 1) + ": cannot convert from '" + t + "' to 'double'", c.Args[i].Value); return null; }
                }
                return xs;
            }
            IrExpr Op(IrOp op, List<IrExpr> xs, CType t) { c.Type = t; return new IrOpCall(op, xs.ToArray()) { NameSpan = nameSpan, Type = t.Ir() }; }
            switch (name)
            {
                case "Min": case "Max":
                    {
                        var xs = NumArgs(2);
                        if (xs == null) { c.Type = CType.Error; return null; }
                        c.Type = c.Args.All(a => (a.Value.Type ?? CType.Error).Kind == CK.Int) ? CType.Int : CType.Double;
                        var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = name == "Min" ? "min" : "max", NameSpan = nameSpan };
                        call.Args.AddRange(xs);
                        return call;
                    }
                case "Abs":
                    {
                        var xs = NumArgs(1);
                        if (xs == null) { c.Type = CType.Error; return null; }
                        c.Type = (c.Args[0].Value.Type ?? CType.Error).Kind == CK.Int ? CType.Int : CType.Double;
                        var call = new IrCall { Kind = IrCallKind.Builtin, Builtin = "abs", NameSpan = nameSpan };
                        call.Args.AddRange(xs);
                        return call;
                    }
                case "Floor": { var xs = NumArgs(1); if (xs == null) { c.Type = CType.Error; return null; } return Op(IrOp.Floor, xs, CType.Double); }
                case "Ceiling": { var xs = NumArgs(1); if (xs == null) { c.Type = CType.Error; return null; } return Op(IrOp.Ceil, xs, CType.Double); }
                case "Sqrt": { var xs = NumArgs(1); if (xs == null) { c.Type = CType.Error; return null; } return Op(IrOp.Sqrt, xs, CType.Double); }
                case "Round":
                    {
                        if (c.Args.Count == 2) { ArgsLoose(c); Error("Math.Round with a number of digits is not supported in JavaFarmer", nameSpan.Start, c.End); return null; }
                        var xs = NumArgs(1); if (xs == null) { c.Type = CType.Error; return null; }
                        return Op(IrOp.Round, xs, CType.Double);
                    }
                case "Truncate":
                    {
                        var xs = NumArgs(1); if (xs == null) { c.Type = CType.Error; return null; }
                        c.Type = CType.Double;
                        return new IrCast { Target = IrType.Int, Operand = xs[0], OpSpan = nameSpan, Type = IrType.Float };
                    }
                case "Pow":
                    {
                        var xs = NumArgs(2); if (xs == null) { c.Type = CType.Error; return null; }
                        c.Type = CType.Double;
                        return new IrBinary { Op = IrBinOp.Pow, Left = xs[0], Right = xs[1], OpSpan = nameSpan, Type = IrType.Float };
                    }
            }
            ArgsLoose(c);
            Error("Math." + name + " is not supported in JavaFarmer", nameSpan.Start, nameSpan.End);
            return null;
        }

        // ------------------------------------------------------------------ instance members
        IrExpr MemberCall(CCall c, CMember m, IrExpr target, CType tt, bool asStatement)
        {
            var nameSpan = Sp(m.NameStart, m.NameEnd);
            IrOpCall Op(IrOp op, CType t, params IrExpr[] args) { c.Type = t; return new IrOpCall(op, args) { NameSpan = nameSpan, Type = t.Ir() }; }
            List<IrExpr> Args(params CType[] ps)
            {
                var res = new List<IrExpr>();
                if (!CheckArgsAgainst(c, ps, ps.Length, tt + "." + m.Name + "|item", res)) return null;
                return res;
            }
            IrExpr NotFound()
            {
                ArgsLoose(c);
                if ((tt.Kind == CK.List || tt.Kind == CK.Set || tt.Kind == CK.Map) && m.Name == "Count" || (tt.Kind == CK.String || tt.Kind == CK.Array) && m.Name == "Length")
                    Error("CS1955: Non-invocable member '" + tt + "." + m.Name + "' cannot be used like a method.", nameSpan.Start, nameSpan.End);
                else Error(MissingMember(tt, m.Name) + LinqHint(m.Name), nameSpan.Start, nameSpan.End);
                return null;
            }
            IrExpr StmtOnly(IrExpr e, string what)
            {
                if (!asStatement) Error("using the result of " + what + " is not supported in JavaFarmer; call it as a separate statement", c);
                return e;
            }
            if (m.Name == "ToString" && c.Args.Count == 0 && !tt.IsError && tt.Kind != CK.Void)
            {
                if (tt.Kind == CK.Delegate) { Error("printing a function is not supported in JavaFarmer", c); return null; }
                c.Type = CType.String;
                return new IrCast { Target = IrType.String, Operand = target, OpSpan = nameSpan };
            }
            if (m.Name == "Equals" && c.Args.Count == 1 && (tt.Kind == CK.String || tt.Kind == CK.Char || tt.IsNumeric || tt.Kind == CK.Game || tt.Kind == CK.Bool))
            {
                var a = Args(CType.Object);
                if (a == null) return null;
                c.Type = CType.Bool;
                return new IrBinary { Op = IrBinOp.Eq, Left = target, Right = a[0], OpSpan = nameSpan };
            }
            if (tt.View)
            {
                if (tt.Kind == CK.Set && m.Name == "Contains") { var a = Args(tt.Elem); return a == null ? null : Op(IrOp.Contains, CType.Bool, target, a[0]); }
                return NotFound();
            }
            switch (tt.Kind)
            {
                case CK.String:
                    if (m.Name == "Contains") { var a = Args(CType.String); return a == null ? null : Op(IrOp.Contains, CType.Bool, target, a[0]); }
                    if (m.Name == "Substring" || m.Name == "ToUpper" || m.Name == "ToLower" || m.Name == "Split" || m.Name == "Replace" || m.Name == "Trim" || m.Name == "StartsWith" || m.Name == "EndsWith" || m.Name == "IndexOf")
                    { ArgsLoose(c); Error("string." + m.Name + " is not supported in JavaFarmer (strings support Length, [i], Contains, + and ==)", nameSpan.Start, nameSpan.End); return null; }
                    return NotFound();
                case CK.List:
                    switch (m.Name)
                    {
                        case "Add": { var a = Args(tt.Elem); if (a == null) return null; var r = Op(IrOp.ListAppend, CType.Void, target, a[0]); c.Type = CType.Void; return r; }
                        case "Insert": { var a = Args(CType.Int, tt.Elem); if (a == null) return null; var r = Op(IrOp.ListInsert, CType.Void, target, a[0], a[1]); return r; }
                        case "RemoveAt": { var a = Args(CType.Int); if (a == null) return null; var r = Op(IrOp.ListPop, CType.Void, target, a[0]); r.Type = tt.Elem.Ir(); return r; }
                        case "Remove": { var a = Args(tt.Elem); if (a == null) return null; return StmtOnly(Op(IrOp.RemoveValueIfPresent, CType.Bool, target, a[0]), "List.Remove"); }
                        case "Contains": { var a = Args(tt.Elem); return a == null ? null : Op(IrOp.Contains, CType.Bool, target, a[0]); }
                        case "IndexOf": { var a = Args(tt.Elem); return a == null ? null : Op(IrOp.ListIndexOf, CType.Int, target, a[0]); }
                        case "Clear": { var a = Args(); return a == null ? null : Op(IrOp.Clear, CType.Void, target); }
                        case "Sort": case "Reverse": case "AddRange": case "RemoveAll": case "Find": case "Exists": case "ForEach": case "ToArray": case "GetRange":
                            ArgsLoose(c); Error("List." + m.Name + " is not supported in JavaFarmer", nameSpan.Start, nameSpan.End); return null;
                    }
                    return NotFound();
                case CK.Set:
                    switch (m.Name)
                    {
                        case "Add": { var a = Args(tt.Elem); if (a == null) return null; return StmtOnly(Op(IrOp.SetAdd, CType.Bool, target, a[0]), "HashSet.Add"); }
                        case "Remove": { var a = Args(tt.Elem); if (a == null) return null; return StmtOnly(Op(IrOp.RemoveValueIfPresent, CType.Bool, target, a[0]), "HashSet.Remove"); }
                        case "Contains": { var a = Args(tt.Elem); return a == null ? null : Op(IrOp.Contains, CType.Bool, target, a[0]); }
                        case "Clear": { var a = Args(); return a == null ? null : Op(IrOp.Clear, CType.Void, target); }
                        case "UnionWith": case "IntersectWith": case "ExceptWith": case "IsSubsetOf": case "Overlaps":
                            ArgsLoose(c); Error("HashSet." + m.Name + " is not supported in JavaFarmer", nameSpan.Start, nameSpan.End); return null;
                    }
                    return NotFound();
                case CK.Map:
                    switch (m.Name)
                    {
                        case "Add":
                            {
                                var a = Args(tt.Key, tt.Elem); if (a == null) return null;
                                var r = Op(IrOp.MapPut, CType.Void, target, a[0], a[1]);
                                return r;
                            }
                        case "ContainsKey": { var a = Args(tt.Key); return a == null ? null : Op(IrOp.Contains, CType.Bool, target, a[0]); }
                        case "Remove": { var a = Args(tt.Key); if (a == null) return null; var r = Op(IrOp.MapRemoveIfPresent, CType.Bool, target, a[0]); r.Type = tt.Elem.Ir(); return StmtOnly(r, "Dictionary.Remove"); }
                        case "GetValueOrDefault":
                            {
                                var res = new List<IrExpr>();
                                if (c.Args.Count == 1) { if (!CheckArgsAgainst(c, new[] { tt.Key }, 1, "Dictionary.GetValueOrDefault|key", res)) return null; res.Add(DefaultLiteral(tt.Elem, nameSpan)); }
                                else if (!CheckArgsAgainst(c, new[] { tt.Key, tt.Elem }, 2, "Dictionary.GetValueOrDefault|key", res)) return null;
                                return Op(IrOp.MapGetOrDefault, tt.Elem, target, res[0], res[1]);
                            }
                        case "Clear": { var a = Args(); return a == null ? null : Op(IrOp.Clear, CType.Void, target); }
                        case "ContainsValue": case "TryAdd":
                            ArgsLoose(c); Error("Dictionary." + m.Name + " is not supported in JavaFarmer", nameSpan.Start, nameSpan.End); return null;
                    }
                    return NotFound();
                case CK.Array:
                    if (m.Name == "GetLength")
                    {
                        var a = Args(CType.Int); if (a == null) return null;
                        var arg = c.Args[0].Value;
                        if (!(Unparen(arg) is CLiteral dl && dl.Kind == CLitKind.Int && dl.IntValue >= 0 && dl.IntValue < tt.Rank))
                        { Error("GetLength is only supported with a constant dimension (0" + (tt.Rank == 2 ? " or 1" : "") + ") in JavaFarmer", arg); return null; }
                        if (dl.IntValue == 0) return Op(IrOp.Len, CType.Int, target);
                        var zero = IrLiteral.OfInt(0); zero.Span = Sp(arg); zero.Synthetic = true;
                        var row = new IrIndex { Target = target, Index = zero, Span = Sp(c), Synthetic = true, Type = IrType.Array(tt.Elem.Ir()) };
                        return Op(IrOp.Len, CType.Int, row);
                    }
                    return NotFound();
                case CK.Delegate:
                    if (m.Name == "Invoke") return DelegateCall(c, target, tt);
                    return NotFound();
            }
            return NotFound();
        }

        static string LinqHint(string name)
        {
            switch (name)
            {
                case "Sum": case "Max": case "Min": case "Where": case "Select": case "First": case "Last": case "Any": case "All": case "ToList": case "OrderBy": case "Average": case "Count":
                    return " (LINQ is not supported in JavaFarmer)";
            }
            return "";
        }

        // ------------------------------------------------------------------ TryGetValue
        /// <summary>
        /// dict.TryGetValue(key, out var v) as the whole condition of an if (or negated): v = dict.GetValueOrDefault(key)
        /// is assigned before the if, and the condition becomes key in dict.
        /// </summary>
        IrExpr TryGetValue(CCall c, CMember m)
        {
            c.Type = CType.Bool;
            var target = X(m.Target);
            var tt = m.Target.Type ?? CType.Error;
            if (tt.IsError) { ArgsLoose(c); return null; }
            if (tt.Kind != CK.Map) { ArgsLoose(c); Error(MissingMember(tt, "TryGetValue"), m.NameStart, m.NameEnd); return null; }
            if (c.Args.Count != 2 || c.Args[1].Modifier != "out" || c.Args[0].Modifier != null || c.Args.Any(a => a.Name != null))
            { ArgsLoose(c); Error("CS1501: No overload for method 'TryGetValue' takes these arguments (write dict.TryGetValue(key, out var value))", m.NameStart, c.End); return null; }
            if (preStmts == null)
            { ArgsLoose(c); Error("TryGetValue is only supported as the whole condition of an if statement in JavaFarmer (if (dict.TryGetValue(key, out var value)) ...)", c); return null; }
            var key = X(c.Args[0].Value, tt.Key);
            RequireAssignable(c.Args[0].Value.Type, tt.Key, c.Args[0].Value);
            if (!IsSimpleExpr(m.Target) || !IsSimpleExpr(c.Args[0].Value))
            { Error("TryGetValue on a computed dictionary or key is not supported in JavaFarmer; store it in a variable first", c); return null; }
            var oa = c.Args[1];
            IrExpr outTarget;
            if (oa.OutType != null)
            {
                CType vt = oa.OutType.Name == "var" && oa.OutType.Args == null ? tt.Elem : ResolveType(oa.OutType);
                if (!vt.IsError && !CType.Assignable(tt.Elem, vt)) Error("CS1503: Argument 2: cannot convert from 'out " + vt + "' to 'out " + tt.Elem + "'", oa);
                CheckIdentifier(oa.OutName, oa.OutNameStart, oa.OutNameEnd);
                CheckRedeclare(oa.OutName, oa.OutNameStart, oa.OutNameEnd);
                var sym = DeclareLocal(oa.OutName, vt, false, oa.OutNameStart, oa.OutNameEnd);
                da.Add(sym);
                var dflt = new IrOpCall(IrOp.MapGetOrDefault, CloneTarget(target), CloneTarget(key), DefaultLiteral(tt.Elem, Sp(oa))) { NameSpan = Sp(m.NameStart, m.NameEnd), Span = Sp(c), Type = tt.Elem.Ir() };
                preStmts.Add(new IrVarDecl { Symbol = sym.Ir, Init = dflt, NameSpan = Sp(oa.OutNameStart, oa.OutNameEnd), Span = Sp(oa), TypeExplicit = oa.OutType.Name != "var" });
            }
            else
            {
                var vt = ResolveTarget(oa.Value, false, out outTarget);
                if (!vt.IsError && !tt.Elem.SameAs(vt)) Error("CS1503: Argument 2: cannot convert from 'out " + vt + "' to 'out " + tt.Elem + "'", oa);
                MarkAssigned(oa.Value);
                var dflt = new IrOpCall(IrOp.MapGetOrDefault, CloneTarget(target), CloneTarget(key), DefaultLiteral(tt.Elem, Sp(oa))) { NameSpan = Sp(m.NameStart, m.NameEnd), Span = Sp(c), Type = tt.Elem.Ir() };
                if (outTarget != null) preStmts.Add(new IrAssign { Target = outTarget, Value = dflt, OpSpan = Sp(oa), Span = Sp(oa) });
            }
            return new IrOpCall(IrOp.Contains, target, key) { NameSpan = Sp(m.NameStart, m.NameEnd), Type = IrType.Bool };
        }

        bool IsSimpleExpr(CExpr e)
        {
            e = Unparen(e);
            switch (e)
            {
                case CName _: case CLiteral _: return true;
                case CMember mm: return mm.Target is CName q && (CsBinderConst.ConstBags.ContainsKey(q.Name) || q.Name == "Direction" || q.Name == ClassName || GetOther(q.Name) != null);
            }
            return false;
        }

        // ================================================================== object creation
        IrExpr New(CNew ne, CType expected)
        {
            CType t;
            if (ne.TypeRef == null)
            {
                if (expected == null || expected.IsError) { if (expected == null) Error("CS8754: There is no target type for 'new()'", ne); foreach (var a in ne.Args) if (a.Value != null) X(a.Value); ne.Type = CType.Error; return null; }
                t = expected;
                if (!(t.Kind == CK.List || t.Kind == CK.Set || t.Kind == CK.Map))
                { Error("new() of " + t + " is not supported in JavaFarmer", ne); ne.Type = CType.Error; return null; }
            }
            else
            {
                var tr = ne.TypeRef;
                switch (tr.Name)
                {
                    case "List": case "HashSet": case "Dictionary":
                        t = ResolveType(tr);
                        break;
                    case "Random":
                        Error("System.Random is not supported in JavaFarmer; call Random() (the game's random number in [0, 1))", ne);
                        ne.Type = CType.Error; return null;
                    case "Position": case "Companion":
                        Error("creating " + tr.Name + " values is not supported in JavaFarmer", ne);
                        ne.Type = CType.Error; return null;
                    case "Action": case "Func":
                        Error("new " + tr.Name + "(...) is not supported in JavaFarmer; assign a lambda or a method name directly", ne);
                        ne.Type = CType.Error; return null;
                    default:
                        {
                            foreach (var a in ne.Args) if (a.Value != null) X(a.Value);
                            var rt = ResolveType(tr);
                            if (!rt.IsError) Error("new " + rt + "(...) is not supported in JavaFarmer", ne);
                            ne.Type = CType.Error;
                            return null;
                        }
                }
            }
            ne.Type = t;
            if (t.IsError) { foreach (var a in ne.Args) if (a.Value != null) X(a.Value); return null; }
            var span = Sp(ne);
            IrExpr result;
            if (ne.Args.Count > 1 || ne.Args.Any(a => a.Name != null || a.Modifier != null))
            {
                foreach (var a in ne.Args) if (a.Value != null) X(a.Value);
                Error("CS1729: '" + t + "' does not contain a constructor that takes " + ne.Args.Count + " arguments", ne);
                return null;
            }
            if (ne.Args.Count == 1)
            {
                var arg = ne.Args[0].Value;
                var ax = X(arg);
                var at = arg.Type ?? CType.Error;
                if (at.IsError) return null;
                if (at.Kind == CK.Int)
                {
                    // initial capacity
                    result = t.Kind == CK.List ? new IrListLit() : t.Kind == CK.Set ? new IrSetLit() : (IrExpr)new IrMapLit();
                }
                else
                {
                    bool ok;
                    if (t.Kind == CK.Map) ok = at.Kind == CK.Map && at.Key.SameAs(t.Key) && at.Elem.SameAs(t.Elem);
                    else ok = (at.Kind == CK.List || at.Kind == CK.Set || (at.Kind == CK.Array && at.Rank == 1)) && at.Elem.SameAs(t.Elem);
                    if (!ok) { Error("CS1503: Argument 1: cannot convert from '" + at + "' to '" + (t.Kind == CK.Map ? "IDictionary<" + t.Key + ", " + t.Elem + ">" : "IEnumerable<" + t.Elem + ">") + "'", arg); return null; }
                    // new List<K>(dict.Keys): copy the dictionary itself (keeps the insertion order)
                    if (ax is IrOpCall mk && mk.Op == IrOp.MapKeys && t.Kind == CK.List) ax = mk.Args[0];
                    var op = t.Kind == CK.List ? IrOp.ListCopy : t.Kind == CK.Set ? IrOp.SetCopy : IrOp.MapCopy;
                    result = new IrOpCall(op, ax) { NameSpan = span };
                }
                if (ne.Items != null && (ne.Items.Count > 0 || ne.Pairs.Count > 0)) { Error("a collection initializer together with a constructor argument is not supported in JavaFarmer", ne.InitStart, ne.InitStart + 1); return null; }
                return result;
            }
            if (t.Kind == CK.Map)
            {
                var map = new IrMapLit();
                if (ne.Items != null && ne.Items.Count > 0) { foreach (var it in ne.Items) X(it); Error("CS7036: There is no argument given that corresponds to the required parameter 'value' of 'Dictionary<" + t.Key + ", " + t.Elem + ">.Add(" + t.Key + ", " + t.Elem + ")'", ne.Items[0]); return null; }
                if (ne.Pairs != null)
                    foreach (var (k, v, _) in ne.Pairs)
                    {
                        map.Keys.Add(X(k, t.Key)); RequireAssignable(k.Type, t.Key, k);
                        map.Values.Add(X(v, t.Elem)); RequireAssignable(v.Type, t.Elem, v);
                    }
                return map;
            }
            if (ne.Pairs != null && ne.Pairs.Count > 0) { Error("CS1061: '" + t + "' does not contain a definition for 'Add' that takes two arguments", ne); return null; }
            if (t.Kind == CK.List)
            {
                var l = new IrListLit();
                if (ne.Items != null) foreach (var it in ne.Items) { l.Items.Add(X(it, t.Elem)); RequireAssignable(it.Type, t.Elem, it); }
                return l;
            }
            var s = new IrSetLit();
            if (ne.Items != null) foreach (var it in ne.Items) { s.Items.Add(X(it, t.Elem)); RequireAssignable(it.Type, t.Elem, it); }
            return s;
        }

        IrExpr Collection(CCollection col, CType expected)
        {
            if (expected == null) { foreach (var it in col.Items) X(it); Error("CS9176: There is no target type for the collection expression.", col); col.Type = CType.Error; return null; }
            if (expected.IsError) { foreach (var it in col.Items) X(it); col.Type = CType.Error; return null; }
            col.Type = expected;
            switch (expected.Kind)
            {
                case CK.List:
                    {
                        var l = new IrListLit();
                        foreach (var it in col.Items) { l.Items.Add(X(it, expected.Elem)); RequireAssignable(it.Type, expected.Elem, it); }
                        return l;
                    }
                case CK.Set:
                    {
                        var s = new IrSetLit();
                        foreach (var it in col.Items) { s.Items.Add(X(it, expected.Elem)); RequireAssignable(it.Type, expected.Elem, it); }
                        return s;
                    }
                case CK.Array:
                    {
                        if (expected.Rank != 1) break;
                        var l = new IrListLit { IsArray = true };
                        foreach (var it in col.Items) { l.Items.Add(X(it, expected.Elem)); RequireAssignable(it.Type, expected.Elem, it); }
                        return l;
                    }
                case CK.Map:
                    if (col.Items.Count == 0) return new IrMapLit();
                    break;
            }
            foreach (var it in col.Items) X(it);
            Error("CS9174: Cannot initialize type '" + expected + "' with a collection expression because the type is not constructible.", col);
            col.Type = CType.Error;
            return null;
        }

        IrExpr NewArray(CNewArray na)
        {
            var span = Sp(na);
            if (na.Elem == null)
            {
                // new[] { ... }: element type from the items
                if (na.FirstRank != 1 || na.Init == null) { Error("implicitly typed arrays with more than one dimension are not supported in JavaFarmer", na); na.Type = CType.Error; return null; }
                CType et = null;
                var xs = new List<IrExpr>();
                foreach (var it in na.Init.Items)
                {
                    if (it is CArrayInit) { Error("CS0623: Array initializers can only be used in a variable or field initializer. Try using a new expression instead.", it); continue; }
                    xs.Add(X(it));
                    var t = it.Type ?? CType.Error;
                    if (et == null || (et.Kind == CK.Int && t.Kind == CK.Double) || et.Kind == CK.Null) et = t;
                    else if (!CType.Assignable(t, et) && !t.IsError) { Error("CS0826: No best type found for implicitly-typed array", na); na.Type = CType.Error; return null; }
                }
                if (et == null || et.Kind == CK.Null) { Error("CS0826: No best type found for implicitly-typed array", na); na.Type = CType.Error; return null; }
                na.Type = CType.ArrayOf(et);
                var lit = new IrListLit { IsArray = true, Span = span };
                lit.Items.AddRange(xs);
                return lit;
            }
            var elem = ResolveType(na.Elem);
            if (na.Ranks.Count > 1 || na.Ranks.Any(r => r > 1) || na.FirstRank > 2 || (na.FirstRank == 2 && na.Ranks.Count > 0))
            { Error("arrays with more than 2 dimensions are not supported in JavaFarmer", na); na.Type = CType.Error; return null; }
            CType type = na.Ranks.Count == 1 ? CType.ArrayOf(CType.ArrayOf(elem), 1) : CType.ArrayOf(elem, na.FirstRank);
            na.Type = type;
            foreach (var d in na.Sizes)
            {
                X(d, CType.Int);
                var dt = d.Type ?? CType.Error;
                if (!dt.IsError && dt.Kind != CK.Int) Error(IncompatibleMsg(dt, CType.Int), d);
            }
            if (na.Init != null)
            {
                if (na.Sizes.Count > 0) { Error("array creation with both sizes and an initializer is not supported in JavaFarmer", na); return null; }
                return ArrayInit(na.Init, type);
            }
            if (elem.IsError) return null;
            if (na.Ranks.Count == 1)
            {
                // jagged new int[n][]: n empty rows (null)
                var nul = IrLiteral.Null(); nul.Span = span;
                return new IrOpCall(IrOp.ArrayNew, X(na.Sizes[0], CType.Int), nul) { NameSpan = span, Type = type.Ir() };
            }
            if (na.Sizes.Count == 1) return new IrOpCall(IrOp.ArrayNew, X(na.Sizes[0], CType.Int), DefaultLiteral(elem, span)) { NameSpan = span, Type = type.Ir() };
            return new IrOpCall(IrOp.ArrayNew2, X(na.Sizes[0], CType.Int), X(na.Sizes[1], CType.Int), DefaultLiteral(elem, span)) { NameSpan = span, Type = type.Ir() };
        }

        IrExpr ArrayInit(CArrayInit ai, CType arrayType)
        {
            ai.Type = arrayType;
            if (arrayType.Kind != CK.Array) { Error("CS0622: Can only use array initializer expressions to assign to array types. Try using a new expression instead.", ai); return null; }
            var l = new IrListLit { IsArray = true, Span = Sp(ai), Type = arrayType.Ir() };
            if (arrayType.Rank == 2)
            {
                var rowT = CType.ArrayOf(arrayType.Elem);
                int width = -1;
                foreach (var item in ai.Items)
                {
                    if (!(item is CArrayInit inner)) { X(item); Error("CS0846: A nested array initializer is expected", item); continue; }
                    if (width < 0) width = inner.Items.Count;
                    else if (inner.Items.Count != width) Error("CS0847: An array initializer of length '" + width + "' is expected", inner);
                    l.Items.Add(ArrayInit(inner, rowT));
                }
                return l;
            }
            foreach (var item in ai.Items)
            {
                if (item is CArrayInit inner)
                {
                    if (arrayType.Elem.Kind == CK.Array) l.Items.Add(ArrayInit(inner, arrayType.Elem));
                    else Error("CS0623: Array initializers can only be used in a variable or field initializer. Try using a new expression instead.", inner);
                    continue;
                }
                l.Items.Add(X(item, arrayType.Elem));
                RequireAssignable(item.Type, arrayType.Elem, item);
            }
            return l;
        }

        // ================================================================== switch expressions
        readonly Dictionary<CSwitchExpr, List<IrExpr>> boundArms = new Dictionary<CSwitchExpr, List<IrExpr>>();
        readonly Dictionary<CSwitchExpr, IrExpr> boundSelectors = new Dictionary<CSwitchExpr, IrExpr>();

        void BindSwitchExpr(CSwitchExpr sx, CType expected)
        {
            if (boundArms.ContainsKey(sx)) return;
            boundSelectors[sx] = X(sx.Selector);
            var arms = new List<IrExpr>();
            foreach (var arm in sx.Arms) arms.Add(X(arm.Value, expected));
            boundArms[sx] = arms;
        }

        /// <summary>The natural type of a switch expression (best common type of its arms).</summary>
        CType SwitchExprType(CSwitchExpr sx, CType expected)
        {
            BindSwitchExpr(sx, expected);
            CType t = null;
            foreach (var arm in sx.Arms)
            {
                var at = arm.Value.Type ?? CType.Error;
                if (at.IsError) return CType.Error;
                if (t == null || t.Kind == CK.Null) t = at;
                else if (CType.Assignable(at, t)) { }
                else if (CType.Assignable(t, at)) t = at;
                else { Error("CS8506: No best type was found for the switch expression.", sx.KwStart, sx.KwEnd); return CType.Error; }
            }
            if (t == null) { Error("CS8506: No best type was found for the switch expression.", sx.KwStart, sx.KwEnd); return CType.Error; }
            if (t.Kind == CK.Null) t = CType.Object;
            if (t.Kind == CK.Void) { Error("CS0029: Cannot implicitly convert type 'void' to 'object'", sx); return CType.Error; }
            return t;
        }

        /// <summary>x = sel switch { p => a, _ => b } as a switch statement whose sections assign / return each arm.</summary>
        List<IrStmt> SwitchExprAssign(CSwitchExpr sx, CType target, Func<IrExpr, IrStmt> make, bool decl)
        {
            BindSwitchExpr(sx, target != null && !target.IsError ? target : null);
            var sel = boundSelectors[sx];
            var st = sx.Selector.Type ?? CType.Error;
            bool selOk = st.IsError || st.Kind == CK.Int || st.Kind == CK.String || st.Kind == CK.Char || st.Kind == CK.Bool || st.Kind == CK.Game;
            if (!selOk) Error("switch on a value of type '" + st + "' is not supported in JavaFarmer (use int, string, char or Direction)", sx.Selector);
            var sw = new IrSwitch { Selector = sel, KwSpan = Sp(sx.KwStart, sx.KwEnd), Span = Sp(sx), Style = IrSwitchStyle.Arrow };
            var labels = new HashSet<string>();
            bool hasDefault = false;
            var values = boundArms[sx];
            for (int i = 0; i < sx.Arms.Count; i++)
            {
                var arm = sx.Arms[i];
                var ic = new IrCase { Span = Sp(arm.Start, arm.End), LabelSpan = Sp(arm.Start, arm.Value.Start) };
                if (hasDefault) Error("CS8510: The pattern has already been handled by a previous arm of the switch expression.", arm.Start, arm.Value.Start);
                if (arm.Discard) { ic.IsDefault = true; hasDefault = true; }
                foreach (var pat in arm.Patterns)
                {
                    var lx = Label(pat, st, labels);
                    if (lx != null) ic.Labels.Add(lx);
                }
                var vt = arm.Value.Type ?? CType.Error;
                if (target != null) RequireAssignable(vt, target, arm.Value);
                ic.Body = new IrBlock { Span = Sp(arm.Value) };
                ic.Body.Stmts.Add(make(values[i]));
                sw.Cases.Add(ic);
            }
            return new List<IrStmt> { sw };
        }
    }
}
