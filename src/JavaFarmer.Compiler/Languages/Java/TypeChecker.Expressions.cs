using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    internal sealed partial class Checker
    {
        /// <summary>Target type marker for spawnDrone(...): Runnable or Supplier, decided by the lambda body.</summary>
        internal static readonly JType InferFunctional = new JType { Kind = TK.Runnable, Display = "infer" };

        static readonly Dictionary<string, string> ConstBags = new Dictionary<string, string>
        {
            { "Entities", "Entity" }, { "Items", "Item" }, { "Grounds", "Ground" }, { "Unlocks", "Unlock" },
            { "Hats", "Hat" }, { "Leaderboards", "Leaderboard" }
        };

        static readonly HashSet<string> BuiltinClasses = new HashSet<string>
        { "Math", "System", "String", "List", "Set", "Map", "Integer", "Double", "Boolean", "Arrays", "Collections", "Thread", "Object" };

        static readonly HashSet<string> Directions = new HashSet<string> { "North", "East", "South", "West" };

        internal static readonly HashSet<string> StatementOnlyMembers = new HashSet<string>
        { "list.add", "list.addAt", "list.set", "list.removeObj", "list.clear", "set.add", "set.remove", "set.clear", "map.put", "map.clear" };

        JType CheckExpr(Expr e, JType expected = null)
        {
            var t = CheckExprCore(e, expected) ?? JType.Error;
            e.Type = t;
            return t;
        }

        JType CheckExprCore(Expr e, JType expected)
        {
            switch (e)
            {
                case LiteralExpr l:
                    switch (l.Kind)
                    {
                        case LitKind.Int: return JType.Int;
                        case LitKind.Double: return JType.Double;
                        case LitKind.Bool: return JType.Boolean;
                        case LitKind.Null: return JType.Null;
                        default:
                            if (l.StrValue.IndexOf('\n') >= 0 || l.StrValue.IndexOf('\r') >= 0)
                                Error("line breaks (\\n) inside strings are not supported in JavaFarmer", l);
                            return JType.String;
                    }
                case NameExpr n: return CheckName(n);
                case ThisExpr t:
                    Error(cu.IsClass ? "non-static variable this cannot be referenced from a static context" : "'this' can only be used as this.name or this::name in JavaFarmer", t);
                    return JType.Error;
                case FieldAccessExpr fa: return CheckFieldAccess(fa);
                case CallExpr c: return CheckCall(c, false, expected);
                case NewExpr ne: return CheckNew(ne, expected);
                case NewArrayExpr na: return CheckNewArray(na);
                case ArrayInitExpr ai:
                    Error("illegal start of expression (array initializers {...} are only allowed in declarations)", ai);
                    return JType.Error;
                case IndexExpr ix: return CheckIndex(ix);
                case UnaryExpr u: return CheckUnary(u);
                case BinaryExpr b: return CheckBinary(b);
                case ConditionalExpr ce: return CheckConditional(ce, expected);
                case CastExpr cast:
                    {
                        var tt = ResolveType(cast.TypeRef);
                        var ot = CheckExpr(cast.Operand, tt.Kind == TK.Runnable || tt.Kind == TK.Supplier ? tt : null);
                        if (ot.Kind == TK.Void) { Error("'void' type not allowed here", cast.Operand); return tt; }
                        if (!JType.Castable(ot, tt)) Error("incompatible types: " + ot + " cannot be converted to " + tt, cast);
                        return tt;
                    }
                case ParenExpr p: return CheckExpr(p.Inner, expected);
                case LambdaExpr lam: return CheckLambda(lam, expected);
                case MethodRefExpr mr: return CheckMethodRef(mr, expected);
                case AssignExpr a:
                    CheckAssign(a);
                    Error("using an assignment as a value is not supported in JavaFarmer; assign in a separate statement", a);
                    return JType.Error;
                case IncDecExpr i:
                    CheckIncDec(i);
                    Error(i.Op + " can only be used as a separate statement in JavaFarmer", i);
                    return JType.Error;
            }
            Error("unsupported expression", e);
            return JType.Error;
        }

        bool IsVariableName(string name) => scope.Lookup(name) != null || Own.Fields.ContainsKey(name);

        /// <summary>If the expression is a class-like qualifier (Math, Entities, Utils, ...), returns its kind.</summary>
        string QualifierKind(Expr target)
        {
            if (!(target is NameExpr n)) return null;
            if (IsVariableName(n.Name)) return null;
            if (Directions.Contains(n.Name)) return null;
            if (n.Name == ClassName) { n.Kind = NameKind.Class; n.ClassName = n.Name; n.Type = JType.Error; return "own"; }
            if (GetOther(n.Name) != null) { n.Kind = NameKind.OtherClass; n.ClassName = n.Name; n.Type = JType.Error; return "other"; }
            if (ConstBags.ContainsKey(n.Name)) { n.Kind = NameKind.ConstBag; n.ClassName = n.Name; n.Type = JType.Error; return n.Name; }
            if (BuiltinClasses.Contains(n.Name)) { n.Kind = NameKind.BuiltinClass; n.ClassName = n.Name; n.Type = JType.Error; return n.Name; }
            return null;
        }

        JType CheckName(NameExpr n)
        {
            var sym = scope.Lookup(n.Name);
            if (sym != null)
            {
                n.Kind = NameKind.Local; n.Local = sym;
                if (sym.Owner != fn) fn.Root.Captures?.Add((sym, n));
                if (!da.Contains(sym)) Error("variable " + n.Name + " might not have been initialized", n);
                return sym.Type;
            }
            if (Own.Fields.TryGetValue(n.Name, out var f))
            {
                n.Kind = NameKind.Field; n.Field = f;
                return f.Type;
            }
            if (Directions.Contains(n.Name)) { n.Kind = NameKind.Direction; return JType.GameType("Direction"); }
            if (Own.Methods.ContainsKey(n.Name) || GameApi.Overloads(n.Name).Count > 0)
            {
                Error("cannot find symbol: variable " + n.Name + " (to pass a method use " + (cu.IsClass ? ClassName : "this") + "::" + n.Name + ")", n);
                return JType.Error;
            }
            Error("cannot find symbol: variable " + n.Name, n);
            return JType.Error;
        }

        JType CheckFieldAccess(FieldAccessExpr fa)
        {
            var q = QualifierKind(fa.Target);
            if (q != null)
            {
                switch (q)
                {
                    case "own":
                        if (Own.Fields.TryGetValue(fa.Name, out var f)) { fa.Field = f; return f.Type; }
                        Error("cannot find symbol: variable " + fa.Name, fa.NameStart, fa.NameEnd);
                        return JType.Error;
                    case "other":
                        {
                            var name = ((NameExpr)fa.Target).Name;
                            var other = GetOther(name);
                            NoteModuleUse(name, fa.Target.Start, fa.Target.End);
                            if (other.LoadError != null) { Error(other.LoadError, fa.Target.Start, fa.Target.End); return JType.Error; }
                            if (other.Fields.TryGetValue(fa.Name, out var of))
                            {
                                if (of.IsPrivate) Error(fa.Name + " has private access in " + name, fa.NameStart, fa.NameEnd);
                                fa.Field = of; return of.Type;
                            }
                            Error("cannot find symbol: variable " + fa.Name + " in " + name, fa.NameStart, fa.NameEnd);
                            return JType.Error;
                        }
                    case "Integer":
                        if (fa.Name == "MAX_VALUE") { fa.ConstText = "2147483647"; return JType.Int; }
                        if (fa.Name == "MIN_VALUE") { fa.ConstText = "-2147483648"; return JType.Int; }
                        break;
                    case "Math":
                        if (fa.Name == "PI") { fa.ConstText = "3.141592653589793"; return JType.Double; }
                        if (fa.Name == "E") { fa.ConstText = "2.718281828459045"; return JType.Double; }
                        break;
                    case "System":
                        if (fa.Name == "out") { Error("System.out can only be used as System.out.println(...)", fa); return JType.Error; }
                        break;
                    default:
                        if (ConstBags.TryGetValue(q, out var gt)) { fa.IsConstMember = true; return JType.GameType(gt); }
                        break;
                }
                Error("cannot find symbol: variable " + fa.Name + " in " + q, fa.NameStart, fa.NameEnd);
                return JType.Error;
            }
            if (fa.Target is ThisExpr)
            {
                fa.Target.Type = JType.Error;
                if (cu.IsClass) { Error("non-static variable this cannot be referenced from a static context", fa.Target); return JType.Error; }
                if (Own.Fields.TryGetValue(fa.Name, out var f)) { fa.Field = f; return f.Type; }
                Error("cannot find symbol: variable " + fa.Name, fa.NameStart, fa.NameEnd);
                return JType.Error;
            }
            var tt = CheckExpr(fa.Target);
            if (tt.IsError) return JType.Error;
            if (tt.Kind == TK.Array && fa.Name == "length") { fa.IsArrayLength = true; return JType.Int; }
            if ((tt.Kind == TK.List || tt.Kind == TK.String || tt.Kind == TK.Set || tt.Kind == TK.Map) && (fa.Name == "length" || fa.Name == "size"))
            {
                Error("cannot find symbol: variable " + fa.Name + " (use " + (tt.Kind == TK.String ? "length()" : "size()") + ")", fa.NameStart, fa.NameEnd);
                return JType.Error;
            }
            Error("cannot find symbol: variable " + fa.Name, fa.NameStart, fa.NameEnd);
            return JType.Error;
        }

        // ------------------------------------------------------------------ assignment / inc-dec
        void CheckExprStatement(Expr e)
        {
            switch (e)
            {
                case AssignExpr a: CheckAssign(a); e.Type = JType.Void; break;
                case IncDecExpr i: CheckIncDec(i); e.Type = JType.Void; break;
                case CallExpr c: e.Type = CheckCall(c, true, null); break;
                case NewExpr n:
                    if (!CheckExpr(n).IsError) Error("creating an object without using it is not supported in JavaFarmer", n);
                    break;
                default:
                    CheckExpr(e);
                    Error("not a statement", e);
                    break;
            }
        }

        static Expr Unparen(Expr e) { while (e is ParenExpr p) e = p.Inner; return e; }

        JType ResolveTarget(Expr target, bool compound)
        {
            var t = Unparen(target);
            switch (t)
            {
                case NameExpr n:
                    {
                        var sym = scope.Lookup(n.Name);
                        if (sym != null)
                        {
                            n.Kind = NameKind.Local; n.Local = sym; n.Type = sym.Type;
                            if (sym.Owner != fn)
                            {
                                Error("local variables referenced from a lambda expression must be final or effectively final", n);
                                return sym.Type;
                            }
                            if (sym.IsFinal && (sym.HasInit || sym.IsParam || da.Contains(sym) || compound))
                                Error("cannot assign a value to final variable " + n.Name, n);
                            if (compound && !da.Contains(sym)) Error("variable " + n.Name + " might not have been initialized", n);
                            return sym.Type;
                        }
                        if (Own.Fields.TryGetValue(n.Name, out var f))
                        {
                            n.Kind = NameKind.Field; n.Field = f; n.Type = f.Type;
                            FieldAssigned(f, n);
                            return f.Type;
                        }
                        Error("cannot find symbol: variable " + n.Name, n);
                        return JType.Error;
                    }
                case FieldAccessExpr fa:
                    {
                        var ft = CheckFieldAccess(fa);
                        fa.Type = ft;
                        if (fa.Field != null) FieldAssigned(fa.Field, fa);
                        else if (fa.IsArrayLength || fa.IsConstMember || fa.ConstText != null) Error("cannot assign a value to final variable " + fa.Name, fa);
                        return ft;
                    }
                case IndexExpr ix:
                    return CheckExpr(ix);
                case CallExpr c when c.Name == "get" && c.Args.Count == 1:
                    CheckExpr(c);
                    Error("unexpected type: required variable, found value (use list.set(i, x) or map.put(k, v))", c);
                    return JType.Error;
            }
            CheckExpr(t);
            Error("unexpected type: required variable, found value", t);
            return JType.Error;
        }

        void FieldAssigned(FieldSymbol f, Node at)
        {
            if (f.IsFinal) Error("cannot assign a value to final variable " + f.Name, at);
            if (f.FileName != fileName || fn.IsFieldInit) return;
            if (!fn.Globals.Any(g => g.PyName == f.PyName))
                fn.Globals.Add(new GlobalUse { PyName = f.PyName, Start = at.Start, End = at.End });
        }

        void MarkAssigned(Expr target)
        {
            if (Unparen(target) is NameExpr n && n.Kind == NameKind.Local && n.Local != null)
            {
                var sym = n.Local;
                if (sym.HasInit || sym.IsParam || da.Contains(sym)) sym.Reassignments++;
                da.Add(sym);
            }
        }

        void CheckAssign(AssignExpr a)
        {
            if (a.Op == "=")
            {
                var tt = ResolveTarget(a.Target, false);
                var vt = CheckExpr(a.Value, tt);
                RequireAssignable(vt, tt, a.Value);
                MarkAssigned(a.Target);
                return;
            }
            var t = ResolveTarget(a.Target, true);
            var v = CheckExpr(a.Value);
            if (t.IsError || v.IsError) { MarkAssigned(a.Target); return; }
            if (v.Kind == TK.Void) Error("'void' type not allowed here", a.Value);
            else if (a.Op == "+=" && t.Kind == TK.String) { }
            else if (!(t.IsNumeric && v.IsNumeric))
                Error("bad operand types for binary operator '" + a.Op.Substring(0, 1) + "' (" + t + " and " + v + ")", a.OpStart, a.OpEnd);
            MarkAssigned(a.Target);
        }

        void CheckIncDec(IncDecExpr i)
        {
            var t = ResolveTarget(i.Target, true);
            if (!t.IsError && !t.IsNumeric) Error("bad operand type " + t + " for unary operator '" + i.Op + "'", i);
            MarkAssigned(i.Target);
        }

        // ------------------------------------------------------------------ operators
        JType CheckUnary(UnaryExpr u)
        {
            var t = CheckExpr(u.Operand);
            if (t.IsError) return t;
            if (u.Op == "!")
            {
                if (t.Kind != TK.Boolean) { Error("bad operand type " + t + " for unary operator '!'", u); return JType.Error; }
                return JType.Boolean;
            }
            if (!t.IsNumeric) { Error("bad operand type " + t + " for unary operator '" + u.Op + "'", u); return JType.Error; }
            return t.Unboxed;
        }

        static JType Promote(JType a, JType b) => a.Kind == TK.Double || b.Kind == TK.Double ? JType.Double : JType.Int;

        JType CheckBinary(BinaryExpr b)
        {
            var lt = CheckExpr(b.Left);
            var rt = CheckExpr(b.Right);
            if (lt.IsError || rt.IsError) return b.Op == "==" || b.Op == "!=" || b.Op == "<" || b.Op == ">" || b.Op == "<=" || b.Op == ">=" || b.Op == "&&" || b.Op == "||" ? JType.Boolean : JType.Error;
            string Bad() { Error("bad operand types for binary operator '" + b.Op + "' (" + lt + " and " + rt + ")", b.OpStart, b.OpEnd); return null; }
            if (lt.Kind == TK.Void || rt.Kind == TK.Void) { Error("'void' type not allowed here", lt.Kind == TK.Void ? b.Left : b.Right); return JType.Error; }
            switch (b.Op)
            {
                case "+":
                    if (lt.Kind == TK.String || rt.Kind == TK.String) return JType.String;
                    if (lt.IsNumeric && rt.IsNumeric) return Promote(lt, rt);
                    Bad(); return JType.Error;
                case "-": case "*": case "/": case "%":
                    if (lt.IsNumeric && rt.IsNumeric) return Promote(lt, rt);
                    Bad(); return JType.Error;
                case "<": case ">": case "<=": case ">=":
                    if (lt.IsNumeric && rt.IsNumeric) return JType.Boolean;
                    if (lt.Kind == TK.String && rt.Kind == TK.String) { Error("bad operand types for binary operator '" + b.Op + "' (String and String); compare strings with equals()", b.OpStart, b.OpEnd); return JType.Boolean; }
                    Bad(); return JType.Boolean;
                case "&&": case "||":
                    if (lt.Kind == TK.Boolean && rt.Kind == TK.Boolean) return JType.Boolean;
                    Bad(); return JType.Boolean;
                case "==": case "!=":
                    {
                        bool ok;
                        if (lt.IsNumeric && rt.IsNumeric) ok = lt.IsPrimitive || rt.IsPrimitive || lt.Kind == rt.Kind;
                        else if (lt.Kind == TK.Boolean && rt.Kind == TK.Boolean) ok = true;
                        else if (lt.Kind == TK.Null || rt.Kind == TK.Null) ok = (lt.Kind == TK.Null ? rt : lt).IsReference;
                        else if (lt.IsReference && rt.IsReference) ok = JType.Castable(lt, rt);
                        else ok = false;
                        if (!ok) Error("incomparable types: " + lt + " and " + rt, b.OpStart, b.OpEnd);
                        return JType.Boolean;
                    }
            }
            Bad();
            return JType.Error;
        }

        JType CheckConditional(ConditionalExpr ce, JType expected)
        {
            CheckCond(ce.Cond);
            var functional = expected != null && (expected.Kind == TK.Runnable || expected.Kind == TK.Supplier) ? expected : null;
            var a = CheckExpr(ce.Then, functional ?? (expected != null && expected.Kind != TK.Object ? expected : null));
            var b = CheckExpr(ce.Else, functional ?? (expected != null && expected.Kind != TK.Object ? expected : null));
            if (a.IsError || b.IsError) return a.IsError ? b : a;
            if (a.Kind == TK.Void || b.Kind == TK.Void) { Error("'void' type not allowed here", a.Kind == TK.Void ? ce.Then : ce.Else); return JType.Error; }
            if (a.IsNumeric && b.IsNumeric) return a.Unboxed.Kind == b.Unboxed.Kind && a.Boxed && b.Boxed ? a : Promote(a, b);
            if (a.Kind == TK.Boolean && b.Kind == TK.Boolean) return JType.Boolean;
            if (a.Kind == TK.Null) return b.BoxedVersion;
            if (b.Kind == TK.Null) return a.BoxedVersion;
            if (JType.Assignable(a, b)) return b;
            if (JType.Assignable(b, a)) return a;
            return JType.Object;
        }

        JType CheckIndex(IndexExpr ix)
        {
            var tt = CheckExpr(ix.Target);
            var it = CheckExpr(ix.Index);
            if (!it.IsError)
            {
                if (it.Kind == TK.Double) Error("incompatible types: possible lossy conversion from double to int", ix.Index);
                else if (it.Kind != TK.Int) Error("incompatible types: " + it + " cannot be converted to int", ix.Index);
            }
            if (tt.IsError) return JType.Error;
            if (tt.Kind == TK.Array) return tt.Elem;
            if (tt.Kind == TK.List) Error("array required, but " + tt + " found (use .get(i))", ix);
            else if (tt.Kind == TK.String) Error("array required, but String found (use .charAt(i))", ix);
            else if (tt.Kind == TK.Map) Error("array required, but " + tt + " found (use .get(key))", ix);
            else Error("array required, but " + tt + " found", ix);
            return JType.Error;
        }

        // ------------------------------------------------------------------ object / array creation
        JType CheckNew(NewExpr ne, JType expected)
        {
            var tr = ne.TypeRef;
            string kind = null;
            switch (tr.Name)
            {
                case "ArrayList": kind = "List"; break;
                case "HashSet": kind = "Set"; break;
                case "HashMap": kind = "Map"; break;
                case "List": case "Set": case "Map": case "Collection":
                    Error(tr.Name + " is abstract; cannot be instantiated (use new " + (tr.Name == "Map" ? "HashMap" : tr.Name == "Set" ? "HashSet" : "ArrayList") + "<>())", tr.NameStart, tr.NameEnd);
                    foreach (var a in ne.Args) CheckExpr(a);
                    return JType.Error;
            }
            if (kind == null)
            {
                foreach (var a in ne.Args) CheckExpr(a);
                if (tr.Name == ClassName || GetOther(tr.Name) != null)
                    Error("creating objects is not supported in JavaFarmer (" + tr.Name + " can only have static members)", tr.NameStart, tr.NameEnd);
                else if (BuiltinClasses.Contains(tr.Name) || JType.GameTypeNames.Contains(tr.Name) || tr.Name == "Runnable" || tr.Name == "Supplier")
                    Error("new " + tr.Name + "(...) is not supported in JavaFarmer", ne);
                else
                    Error("cannot find symbol: class " + tr.Name, tr.NameStart, tr.NameEnd);
                return JType.Error;
            }
            JType type;
            JType argT = null;
            if (ne.Args.Count > 1)
            {
                foreach (var a in ne.Args) CheckExpr(a);
                Error("constructor " + tr.Name + " cannot be applied to given types", ne);
                return JType.Error;
            }
            if (ne.Args.Count == 1) argT = CheckExpr(ne.Args[0]);
            if (tr.Diamond)
            {
                var exp = expected;
                if (exp != null && kind == "List" && exp.Kind == TK.List) type = JType.ListOf(exp.Elem, tr.Name);
                else if (exp != null && kind == "List" && exp.Kind == TK.Collection) type = JType.ListOf(exp.Elem, tr.Name);
                else if (exp != null && kind == "Set" && (exp.Kind == TK.Set || exp.Kind == TK.Collection)) type = JType.SetOf(exp.Elem, tr.Name);
                else if (exp != null && kind == "Map" && exp.Kind == TK.Map) type = JType.MapOf(exp.Key, exp.Elem, tr.Name);
                else if (argT != null && kind != "Map" && argT.IsIterable) type = kind == "List" ? JType.ListOf(argT.Elem, tr.Name) : JType.SetOf(argT.Elem, tr.Name);
                else if (argT != null && kind == "Map" && argT.Kind == TK.Map) type = JType.MapOf(argT.Key, argT.Elem, tr.Name);
                else type = kind == "Map" ? JType.MapOf(JType.Object, JType.Object, tr.Name) : kind == "List" ? JType.ListOf(JType.Object, tr.Name) : JType.SetOf(JType.Object, tr.Name);
            }
            else type = ResolveType(tr);
            if (type.IsError) return type;
            if (argT != null && !argT.IsError)
            {
                bool ok;
                if (argT.Kind == TK.Int && argT.IsPrimitive) ok = true;   // initial capacity
                else if (kind == "Map") ok = argT.Kind == TK.Map && JType.Assignable(argT.Key, type.Key) && JType.Assignable(argT.Elem, type.Elem);
                else ok = (argT.IsIterable && argT.Kind != TK.Array) && JType.Assignable(argT.Elem, type.Elem);
                if (!ok) Error("incompatible types: " + argT + " cannot be converted to " + (kind == "Map" ? "Map<" + type.Key + "," + type.Elem + ">" : "Collection<" + type.Elem + ">"), ne.Args[0]);
            }
            return type;
        }

        JType CheckNewArray(NewArrayExpr na)
        {
            var elem = ResolveType(na.Elem);
            if (na.Elem.Args != null || na.Elem.Diamond) Error("generic array creation is not supported", na.Elem);
            if (na.TotalDims > 2) Error("arrays with more than 2 dimensions are not supported in JavaFarmer", na);
            foreach (var d in na.DimExprs)
            {
                var dt = CheckExpr(d);
                if (!dt.IsError && !(dt.Kind == TK.Int)) Error(dt.Kind == TK.Double ? "incompatible types: possible lossy conversion from double to int" : "incompatible types: " + dt + " cannot be converted to int", d);
            }
            var t = elem;
            for (int i = 0; i < na.TotalDims; i++) t = JType.ArrayOf(t);
            if (na.Init != null) CheckArrayInit(na.Init, t);
            return t;
        }

        void CheckArrayInit(ArrayInitExpr ai, JType arrayType)
        {
            ai.Type = arrayType;
            if (arrayType.IsError) { foreach (var it in ai.Items) if (!(it is ArrayInitExpr)) CheckExpr(it); return; }
            if (arrayType.Kind != TK.Array) { Error("illegal initializer for " + arrayType, ai); return; }
            foreach (var item in ai.Items)
            {
                if (item is ArrayInitExpr inner) CheckArrayInit(inner, arrayType.Elem);
                else
                {
                    var t = CheckExpr(item, arrayType.Elem);
                    RequireAssignable(t, arrayType.Elem, item);
                }
            }
        }

        // ------------------------------------------------------------------ lambdas / method refs
        JType CheckLambda(LambdaExpr lam, JType expected)
        {
            bool infer = ReferenceEquals(expected, InferFunctional);
            if (!infer && (expected == null || !(expected.Kind == TK.Runnable || expected.Kind == TK.Supplier)))
            {
                if (expected == null || expected.IsError) Error("lambda expression not expected here (lambdas can be passed to spawnDrone or stored in a Runnable/Supplier variable)", lam.Start, lam.ArrowEnd);
                else Error("incompatible types: " + expected + " is not a functional interface", lam.Start, lam.ArrowEnd);
                return JType.Error;
            }
            if (lam.ParamNames.Count > 0) Error("lambdas with parameters are not supported in JavaFarmer", lam.Start, lam.ArrowEnd);

            var oldFn = fn; var oldScope = scope; var oldDa = da; var oldReach = reachable;
            fn = new FunctionCtx
            {
                Parent = oldFn, Lambda = lam, Display = oldFn.Display,
                ReturnType = infer ? null : expected.Kind == TK.Runnable ? JType.Void : expected.Elem,
                InferredReturns = new List<JType>()
            };
            scope = new Scope(oldScope);
            da = new HashSet<LocalSymbol>(oldDa);
            reachable = true;
            JType result;
            if (lam.BodyExpr != null)
            {
                var body = lam.BodyExpr;
                if (infer)
                {
                    if (body is CallExpr call)
                    {
                        var t = CheckCall(call, true, null);
                        bool stmtOnly = call.Member != null && StatementOnlyMembers.Contains(call.Member);
                        if (t.Kind == TK.Void || t.IsError || stmtOnly) { result = JType.Runnable; }
                        else { call.ValueUsed = true; result = JType.SupplierOf(t.BoxedVersion); }
                    }
                    else if (body is AssignExpr || body is IncDecExpr) { CheckExprStatement(body); result = JType.Runnable; }
                    else
                    {
                        var t = CheckExpr(body);
                        result = t.IsError ? JType.Runnable : JType.SupplierOf(t.BoxedVersion);
                        if (t.Kind == TK.Void) result = JType.Runnable;
                    }
                }
                else if (expected.Kind == TK.Runnable)
                {
                    if (body is CallExpr || body is AssignExpr || body is IncDecExpr) CheckExprStatement(body);
                    else
                    {
                        var t = CheckExpr(body);
                        Error("bad return type in lambda expression: " + t + " cannot be converted to void", body);
                    }
                    result = JType.Runnable;
                }
                else
                {
                    var t = CheckExpr(body, expected.Elem);
                    if (t.Kind == TK.Void) Error("bad return type in lambda expression: void cannot be converted to " + expected.Elem, body);
                    else RequireAssignable(t, expected.Elem, body);
                    result = expected;
                }
                lam.IsSupplier = result.Kind == TK.Supplier;
            }
            else
            {
                foreach (var s in lam.BodyBlock.Stmts) CheckStmtInBlock(s);
                if (infer)
                {
                    if (fn.InferredReturns.Count > 0)
                    {
                        if (reachable) Error("missing return statement", lam.BodyBlock.End - 1, lam.BodyBlock.End);
                        result = JType.SupplierOf(fn.InferredReturns[0].BoxedVersion);
                    }
                    else result = JType.Runnable;
                }
                else
                {
                    if (expected.Kind == TK.Supplier && reachable) Error("missing return statement", lam.BodyBlock.End - 1, lam.BodyBlock.End);
                    result = expected;
                }
                lam.IsSupplier = result.Kind == TK.Supplier;
            }
            lam.ReturnType = result.Kind == TK.Supplier ? result.Elem : JType.Void;
            Globals[lam] = fn.Globals;
            fn = oldFn; scope = oldScope; da = oldDa; reachable = oldReach;
            return result;
        }

        JType CheckMethodRef(MethodRefExpr mr, JType expected)
        {
            MethodSymbol ms = null;
            if (mr.Qualifier is ThisExpr)
            {
                mr.Qualifier.Type = JType.Error;
                if (cu.IsClass) { Error("non-static variable this cannot be referenced from a static context", mr.Qualifier); return JType.Error; }
                Own.Methods.TryGetValue(mr.Name, out ms);
            }
            else
            {
                var q = QualifierKind(mr.Qualifier);
                if (q == "own") Own.Methods.TryGetValue(mr.Name, out ms);
                else if (q == "other")
                {
                    var name = ((NameExpr)mr.Qualifier).Name;
                    NoteModuleUse(name, mr.Qualifier.Start, mr.Qualifier.End);
                    var otherFile = GetOther(name);
                    if (otherFile.LoadError != null) { Error(otherFile.LoadError, mr.Qualifier.Start, mr.Qualifier.End); return JType.Error; }
                    otherFile.Methods.TryGetValue(mr.Name, out ms);
                    if (ms != null && ms.IsPrivate) Error(mr.Name + " has private access in " + name, mr.NameStart, mr.NameEnd);
                    mr.OtherFile = name;
                }
                else
                {
                    Error("method references are only supported as this::name or ClassName::name in JavaFarmer", mr);
                    return JType.Error;
                }
            }
            if (ms == null) { Error("invalid method reference: cannot find symbol " + mr.Name, mr.NameStart, mr.NameEnd); return JType.Error; }
            mr.Method = ms;
            if (ms.ParamTypes.Count != 0) { Error("invalid method reference: " + ms.Display + " needs arguments (only methods without parameters can be referenced)", mr); return JType.Error; }
            bool infer = ReferenceEquals(expected, InferFunctional);
            if (infer) return ms.ReturnType.Kind == TK.Void ? JType.Runnable : JType.SupplierOf(ms.ReturnType.BoxedVersion);
            if (expected == null || expected.IsError || !(expected.Kind == TK.Runnable || expected.Kind == TK.Supplier))
            {
                Error("method reference not expected here", mr);
                return JType.Error;
            }
            if (expected.Kind == TK.Supplier)
            {
                if (ms.ReturnType.Kind == TK.Void) { Error("bad return type in method reference: void cannot be converted to " + expected.Elem, mr); return JType.Error; }
                if (!JType.Assignable(ms.ReturnType, expected.Elem)) { Error("bad return type in method reference: " + ms.ReturnType + " cannot be converted to " + expected.Elem, mr); return JType.Error; }
            }
            return expected;
        }
    }
}
