using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.CSharp
{
    internal sealed partial class CsBinder
    {
        /// <summary>Target type marker for SpawnDrone(...) / var f = () => ...: Action or Func&lt;T&gt;, decided by the lambda body.</summary>
        internal static readonly CType InferDelegate = new CType { Kind = CK.Delegate, Elem = CType.Void, Params = new List<CType>() };

        /// <summary>Statements an expression needs before the statement that contains it (TryGetValue in an if condition).</summary>
        List<IrStmt> preStmts;

        // ================================================================== expression statements
        List<IrStmt> ExprStatement(CExpr e, Span span)
        {
            var res = new List<IrStmt>();
            switch (e)
            {
                case CParen p: return ExprStatement(p.Inner, span);
                case CAssign a: res.AddRange(Assign(a, span)); e.Type = CType.Void; return res;
                case CIncDec id:
                    {
                        var t = ResolveTarget(id.Target, true, out var target);
                        if (!t.IsError && !t.IsNumeric) Error("CS0023: Operator '" + id.Op + "' cannot be applied to operand of type '" + t + "'", id);
                        MarkAssigned(id.Target);
                        var opSpan = Sp(id.OpStart, id.OpEnd);
                        var one = IrLiteral.OfInt(1); one.Span = opSpan;
                        res.Add(new IrAssign { Target = target, Op = id.Op == "++" ? IrBinOp.Add : IrBinOp.Sub, Value = one, OpSpan = opSpan, IsIncDec = true, Prefix = id.Prefix, Span = span });
                        e.Type = CType.Void;
                        return res;
                    }
                case CCall c: return CallStatement(c, span);
                case CNew n:
                    X(n);
                    if (!(n.Type ?? CType.Error).IsError) Error("creating an object without using it is not supported in JavaFarmer", n);
                    return res;
            }
            X(e);
            Error("CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement", e);
            return res;
        }

        // ================================================================== assignment
        void MarkAssigned(CExpr target)
        {
            if (Unparen(target) is CName n)
            {
                var sym = scope.Lookup(n.Name);
                if (sym != null) da.Add(sym);
            }
            else if (Unparen(target) is CTupleExpr t) foreach (var it in t.Items) MarkAssigned(it);
        }

        /// <summary>Resolves an assignment target; returns its type and IR.</summary>
        CType ResolveTarget(CExpr target, bool compound, out IrExpr ir)
        {
            var t = Unparen(target);
            ir = null;
            switch (t)
            {
                case CName n:
                    {
                        var sym = scope.Lookup(n.Name);
                        if (sym != null)
                        {
                            n.Type = sym.Type;
                            ir = new IrName(sym.Ir) { Span = Sp(n) };
                            if (sym.IsConst) Error("CS0131: The left-hand side of an assignment must be a variable, property or indexer", n);
                            else if (sym.IsForEach) Error("CS1656: Cannot assign to '" + n.Name + "' because it is a 'foreach iteration variable'", n);
                            if (sym.Owner != fn) Captured(sym, n, true);
                            if (compound && !da.Contains(sym) && !(sym.Owner != fn)) Error("CS0165: Use of unassigned local variable '" + n.Name + "'", n);
                            return sym.Type;
                        }
                        if (Own.Fields.TryGetValue(n.Name, out var f))
                        {
                            n.Type = f.Type;
                            FieldAssigned(f, n);
                            ir = new IrName(f.Ir) { Span = Sp(n) };
                            return f.Type;
                        }
                        if (!Own.Methods.ContainsKey(n.Name) && !CsBinderConst.Directions.Contains(n.Name) && CsApi.Find(n.Name) == null && !CsBinderConst.ConstBags.ContainsKey(n.Name) && GetOther(n.Name) == null)
                        {
                            if (scope.DeclaredLater(n.Name)) Error("CS0841: Cannot use local variable '" + n.Name + "' before it is declared", n);
                            else Error("CS0103: The name '" + n.Name + "' does not exist in the current context (declare it first: var " + n.Name + " = ...; or int " + n.Name + " = ...;)", n);
                            return CType.Error;
                        }
                        X(n);
                        if (!(n.Type ?? CType.Error).IsError) Error("CS0131: The left-hand side of an assignment must be a variable, property or indexer", n);
                        return CType.Error;
                    }
                case CMember m:
                    {
                        if (m.Target is CName q && scope.Lookup(q.Name) == null && !Own.Fields.ContainsKey(q.Name))
                        {
                            if (q.Name == ClassName && IsClassForm && Own.Fields.TryGetValue(m.Name, out var f))
                            {
                                FieldAssigned(f, m);
                                ir = new IrName(f.Ir) { Span = Sp(m) };
                                m.Type = f.Type;
                                return f.Type;
                            }
                            var o = GetOther(q.Name);
                            if (ForeignFailed(o, q)) return CType.Error;
                            if (o != null && o.IsClass && o.Fields.TryGetValue(m.Name, out var of))
                            {
                                if (!of.IsAccessible) Error("CS0122: '" + q.Name + "." + m.Name + "' is inaccessible due to its protection level", m.NameStart, m.NameEnd);
                                else if (of.IsConst) Error("CS0131: The left-hand side of an assignment must be a variable, property or indexer", m);
                                else if (of.IsReadonly) Error("CS0198: A static readonly field cannot be assigned to (except in a static constructor or a variable initializer)", m);
                                var s = ExternalField(of, q.Name);
                                ir = new IrName(s) { QualifierSpan = Sp(q), NameSpan = Sp(m.NameStart, m.NameEnd), Span = Sp(m) };
                                m.Type = of.Type;
                                return of.Type;
                            }
                        }
                        var x = X(m);
                        var mt = m.Type ?? CType.Error;
                        if (!mt.IsError)
                        {
                            var tt = m.Target.Type ?? CType.Error;
                            if (x is IrConst || tt.IsError) Error("CS0131: The left-hand side of an assignment must be a variable, property or indexer", m);
                            else Error("CS0200: Property or indexer '" + tt + "." + m.Name + "' cannot be assigned to -- it is read only", m);
                        }
                        return CType.Error;
                    }
                case CIndex ix:
                    {
                        ir = Index(ix);
                        var tt = ix.Target.Type ?? CType.Error;
                        if (tt.Kind == CK.String) { Error("CS0200: Property or indexer 'string.this[int]' cannot be assigned to -- it is read only", ix); return CType.Error; }
                        ix.Type = ix.Type ?? CType.Error;
                        return ix.Type;
                    }
                case CTupleExpr tup:
                    {
                        if (compound) { Error("CS0019: Operator cannot be applied to a tuple", tup); return CType.Error; }
                        var items = new IrTupleLit { Span = Sp(tup) };
                        var types = new List<CType>();
                        foreach (var it in tup.Items)
                        {
                            if (it is CDiscard || (it is CName dn && dn.Name == "_" && scope.Lookup("_") == null)) { Error("discards (_) in deconstruction are not supported in JavaFarmer", it); types.Add(CType.Error); continue; }
                            var tt = ResolveTarget(it, false, out var ti);
                            types.Add(tt);
                            if (ti != null) items.Items.Add(ti);
                        }
                        ir = items;
                        var tt2 = CType.TupleOf(types);
                        tup.Type = tt2;
                        return tt2;
                    }
            }
            X(t);
            Error("CS0131: The left-hand side of an assignment must be a variable, property or indexer", t);
            return CType.Error;
        }

        void FieldAssigned(CFieldSym f, CNode at)
        {
            if (f.IsConst) Error("CS0131: The left-hand side of an assignment must be a variable, property or indexer", at);
            else if (f.IsReadonly) Error("CS0198: A static readonly field cannot be assigned to (except in a static constructor or a variable initializer)", at);
        }

        /// <summary>A local of an enclosing function used inside a lambda / local function.</summary>
        void Captured(CLocal sym, CNode at, bool assigned)
        {
            for (var f = fn; f != null && f != sym.Owner; f = f.Parent)
                if (f.IsStatic) { Error((f.Lambda != null ? "CS8820: A static anonymous function cannot contain a reference to '" : "CS8421: A static local function cannot contain a reference to '") + sym.Name + "'.", at); return; }
            if (sym.TopLevel)
            {
                // top-level locals used by local functions / lambdas live at module level (Python globals)
                sym.Ir.Kind = IrSymbolKind.Global;
                return;
            }
            if (assigned)
                Error("assigning a captured local variable inside a lambda or local function is not supported in JavaFarmer (use a static field, or a variable of the top-level statements)", at);
        }

        List<IrStmt> Assign(CAssign a, Span span)
        {
            var res = new List<IrStmt>();
            if (a.Op == "=")
            {
                var tt = ResolveTarget(a.Target, false, out var target);
                if (Unparen(a.Value) is CSwitchExpr sx)
                {
                    MarkAssigned(a.Target);
                    var tgt = target;
                    bool first = true;
                    return SwitchExprAssign(sx, tt, v =>
                    {
                        var t = first ? tgt : CloneTarget(tgt);
                        first = false;
                        return new IrAssign { Target = t, Value = v, OpSpan = Sp(a.OpStart, a.OpEnd), Span = span };
                    }, false);
                }
                IrExpr value;
                if (Unparen(a.Target) is CTupleExpr && Unparen(a.Value) is CTupleExpr vt)
                {
                    value = X(a.Value, tt);
                    RequireAssignable(a.Value.Type, tt, a.Value);
                }
                else if (Unparen(a.Target) is CTupleExpr)
                {
                    value = X(a.Value);
                    DeconstructCheck(a.Value.Type ?? CType.Error, ((CTupleExpr)Unparen(a.Target)).Items.Count, a.Value, tt);
                }
                else
                {
                    value = X(a.Value, tt);
                    RequireAssignable(a.Value.Type, tt, a.Value);
                }
                MarkAssigned(a.Target);
                if (target != null) res.Add(new IrAssign { Target = target, Value = value, OpSpan = Sp(a.OpStart, a.OpEnd), Span = span });
                return res;
            }
            var t2 = ResolveTarget(a.Target, true, out var target2);
            var v2 = X(a.Value);
            var vt2 = a.Value.Type ?? CType.Error;
            MarkAssigned(a.Target);
            if (t2.IsError || vt2.IsError || target2 == null) return res;
            var bop = a.Op.Substring(0, 1);
            IrBinOp op;
            if (vt2.Kind == CK.Void) { Error("CS0019: Operator '" + a.Op + "' cannot be applied to operands of type '" + t2 + "' and 'void'", a.OpStart, a.OpEnd); return res; }
            if (a.Op == "+=" && t2.Kind == CK.String) op = IrBinOp.Concat;
            else if (t2.Kind == CK.Delegate) { Error("combining delegates with " + a.Op + " is not supported in JavaFarmer", a.OpStart, a.OpEnd); return res; }
            else if (t2.IsNumeric && vt2.IsNumeric)
            {
                if (t2.Kind == CK.Int && vt2.Kind == CK.Double) { Error("CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)", a.Value); return res; }
                switch (bop)
                {
                    case "+": op = IrBinOp.Add; break;
                    case "-": op = IrBinOp.Sub; break;
                    case "*": op = IrBinOp.Mul; break;
                    case "/": op = t2.Kind == CK.Int ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv; break;
                    default: op = IrBinOp.ModTrunc; break;
                }
            }
            else
            {
                if (t2.Kind == CK.Char || vt2.Kind == CK.Char) Error("arithmetic on char values is not supported in JavaFarmer", a.OpStart, a.OpEnd);
                else Error("CS0019: Operator '" + a.Op + "' cannot be applied to operands of type '" + t2 + "' and '" + vt2 + "'", a.OpStart, a.OpEnd);
                return res;
            }
            res.Add(new IrAssign { Target = target2, Op = op, Value = v2, OpSpan = Sp(a.OpStart, a.OpEnd), Span = span });
            return res;
        }

        static IrExpr CloneTarget(IrExpr t)
        {
            switch (t)
            {
                case IrName n: return new IrName(n.Symbol) { Span = n.Span, QualifierSpan = n.QualifierSpan, NameSpan = n.NameSpan, Type = n.Type };
                case IrIndex ix: return new IrIndex { Target = CloneTarget(ix.Target), Index = CloneTarget(ix.Index), BaseOffset = ix.BaseOffset, Span = ix.Span, Type = ix.Type };
                case IrLiteral l: return new IrLiteral { Kind = l.Kind, Int = l.Int, Float = l.Float, Bool = l.Bool, Str = l.Str, Span = l.Span, Type = l.Type };
                case IrConst c: return new IrConst(c.Category, c.Name) { Span = c.Span };
            }
            return t;
        }

        // ================================================================== deconstruction
        void Deconstruct(CDeconstruct dc, List<IrStmt> outList)
        {
            var value = X(dc.Value);
            var vt = dc.Value.Type ?? CType.Error;
            var syms = new List<CLocal>();
            var target = DeclareDeconstruct(dc.Names, vt, dc.Value, syms);
            foreach (var s in syms) outList.Add(new IrVarDecl { Symbol = s.Ir, NameSpan = Sp(s.DeclStart, s.DeclEnd), Span = Sp(s.DeclStart, s.DeclEnd), TypeExplicit = false });
            outList.Add(new IrAssign { Target = target, Value = value, OpSpan = Sp(dc.OpStart, dc.OpEnd), Span = Sp(dc) });
            foreach (var s in syms) da.Add(s);
        }

        CType[] PartsOf(CType vt)
        {
            if (vt.Kind == CK.Tuple) return vt.Params.ToArray();
            if (vt.IsGame("Position")) return new[] { CType.Int, CType.Int };
            if (vt.IsGame("Companion")) return new[] { CType.GameType("Entity"), CType.GameType("Position") };
            if (vt.Kind == CK.KeyValue) return new[] { vt.Key, vt.Elem };
            return null;
        }

        void DeconstructCheck(CType vt, int count, CNode at, CType targets)
        {
            if (vt.IsError) return;
            var parts = PartsOf(vt);
            if (parts == null) { Error("CS8129: No suitable 'Deconstruct' instance or extension method was found for type '" + vt + "', with " + count + " out parameters and a void return type.", at); return; }
            if (parts.Length != count) { Error("CS8132: Cannot deconstruct a tuple of '" + parts.Length + "' elements into '" + count + "' variables.", at); return; }
            if (targets != null && targets.Kind == CK.Tuple)
                for (int i = 0; i < parts.Length && i < targets.Params.Count; i++)
                    if (!CType.Assignable(parts[i], targets.Params[i])) Error(IncompatibleMsg(parts[i], targets.Params[i]), at);
        }

        IrTupleLit DeclareDeconstruct(CTupleExpr names, CType vt, CNode at, List<CLocal> syms)
        {
            var res = new IrTupleLit { Span = Sp(names) };
            var parts = vt.IsError ? null : PartsOf(vt);
            if (!vt.IsError && parts == null) Error("CS8129: No suitable 'Deconstruct' instance or extension method was found for type '" + vt + "', with " + names.Items.Count + " out parameters and a void return type.", at);
            else if (parts != null && parts.Length != names.Items.Count) { Error("CS8132: Cannot deconstruct a tuple of '" + parts.Length + "' elements into '" + names.Items.Count + "' variables.", at); parts = null; }
            for (int i = 0; i < names.Items.Count; i++)
            {
                var t = parts != null ? parts[i] : CType.Error;
                var it = names.Items[i];
                if (it is CTupleExpr inner) { res.Items.Add(DeclareDeconstruct(inner, t, inner, syms)); continue; }
                if (it is CDiscard) { Error("discards (_) in deconstruction are not supported in JavaFarmer", it); continue; }
                var n = (CName)it;
                CheckIdentifier(n.Name, n.Start, n.End);
                CheckRedeclare(n.Name, n.Start, n.End);
                if (n.TypeRef != null && !(n.TypeRef.Name == "var" && n.TypeRef.Args == null))
                {
                    var declared = ResolveType(n.TypeRef);
                    if (!t.IsError && !declared.IsError && !CType.Assignable(t, declared)) Error(IncompatibleMsg(t, declared), n.TypeRef);
                    t = declared;
                }
                var sym = DeclareLocal(n.Name, t, false, n.Start, n.End);
                syms.Add(sym);
                res.Items.Add(new IrName(sym.Ir) { Span = Sp(n) });
            }
            res.Type = (parts != null ? CType.TupleOf(parts) : CType.Error).Ir();
            return res;
        }

        // ================================================================== expressions
        IrExpr X(CExpr e, CType expected = null)
        {
            if (e is CParen p)
            {
                var inner = X(p.Inner, expected);
                p.Type = p.Inner.Type;
                inner.ParenSpan = Sp(p);
                return inner;
            }
            IrExpr r;
            try { r = XCore(e, expected); }
            catch (StackOverflowGuard) { throw; }
            if (r == null) r = IrLiteral.Null();
            if (e.Type == null) e.Type = CType.Error;
            if (r.Span.IsNone) r.Span = Sp(e);
            if (!e.Type.IsError && (!r.Type.IsKnown || (r.Type.Kind == IrTypeKind.Null && e.Type.Kind != CK.Null))) r.Type = e.Type.Ir();
            return r;
        }

        sealed class StackOverflowGuard : Exception { }

        IrExpr XCore(CExpr e, CType expected)
        {
            switch (e)
            {
                case CLiteral l:
                    switch (l.Kind)
                    {
                        case CLitKind.Int: e.Type = CType.Int; return IrLiteral.OfInt(l.IntValue);
                        case CLitKind.Double: e.Type = CType.Double; return IrLiteral.OfFloat(l.DoubleValue);
                        case CLitKind.Bool: e.Type = CType.Bool; return IrLiteral.OfBool(l.BoolValue);
                        case CLitKind.Null: e.Type = CType.Null; return IrLiteral.Null();
                        case CLitKind.Char:
                            e.Type = CType.Char;
                            if (l.StrValue.IndexOf('\n') >= 0 || l.StrValue.IndexOf('\r') >= 0) Error("line breaks inside strings are not supported in JavaFarmer", l);
                            return IrLiteral.OfString(l.StrValue);
                        default:
                            e.Type = CType.String;
                            if (l.StrValue.IndexOf('\n') >= 0 || l.StrValue.IndexOf('\r') >= 0) Error("line breaks (\\n) inside strings are not supported in JavaFarmer", l);
                            return IrLiteral.OfString(l.StrValue ?? "");
                    }
                case CInterp ip: return Interp(ip);
                case CName n: return Name(n, expected);
                case CDiscard d: Error("CS0103: The name '_' does not exist in the current context", d); return null;
                case CMember m: return Member(m, expected);
                case CPredefined pd: Error("CS1525: Invalid expression term '" + pd.Name + "'", pd); return null;
                case CCall c: return Call(c, false, expected);
                case CNew ne: return New(ne, expected);
                case CNewArray na: return NewArray(na);
                case CArrayInit ai:
                    Error("CS0623: Array initializers can only be used in a variable or field initializer. Try using a new expression instead.", ai);
                    foreach (var it in ai.Items) if (!(it is CArrayInit)) X(it);
                    return null;
                case CCollection col: return Collection(col, expected);
                case CIndex ix: return Index(ix);
                case CFromEnd fe: Error("the index-from-end operator (^) is only supported directly inside [] in JavaFarmer", fe); X(fe.Operand); return null;
                case CUnary u: return Unary(u, expected);
                case CBinary b: return Binary(b);
                case CConditional ce: return Conditional(ce, expected);
                case CCast cast: return Cast(cast);
                case CTupleExpr tup:
                    {
                        var lit = new IrTupleLit();
                        var types = new List<CType>();
                        for (int i = 0; i < tup.Items.Count; i++)
                        {
                            var want = expected != null && expected.Kind == CK.Tuple && i < expected.Params.Count ? expected.Params[i] : null;
                            lit.Items.Add(X(tup.Items[i], want));
                            var t = tup.Items[i].Type ?? CType.Error;
                            if (t.Kind == CK.Void) Error("CS8210: A tuple may not contain a value of type 'void'.", tup.Items[i]);
                            types.Add(want != null && CType.Assignable(t, want) ? want : t);
                        }
                        e.Type = CType.TupleOf(types);
                        return lit;
                    }
                case CLambda lam: return Lambda(lam, expected);
                case CIs isx:
                    {
                        var left = X(isx.Operand);
                        var right = X(isx.Pattern, isx.Operand.Type);
                        var lt = isx.Operand.Type ?? CType.Error;
                        if (isx.Pattern is CLiteral pl && pl.Kind == CLitKind.Null)
                        {
                            if (lt.IsValueType) Error("CS0037: Cannot convert null to '" + lt + "' because it is a non-nullable value type", isx.Pattern);
                        }
                        else if (!IsConstantExpr(isx.Pattern)) Error("CS0150: A constant value is expected", isx.Pattern);
                        e.Type = CType.Bool;
                        return new IrBinary { Op = isx.Not ? IrBinOp.Ne : IrBinOp.Eq, Left = left, Right = right, OpSpan = Sp(isx.KwStart, isx.KwEnd), Type = IrType.Bool };
                    }
                case CSwitchExpr sx:
                    Error("a switch expression is only supported as a whole value (variable initializer, assignment or return) in JavaFarmer", sx.KwStart, sx.KwEnd);
                    return null;
                case CDefault d:
                    {
                        CType t = d.TypeRef != null ? ResolveType(d.TypeRef) : expected;
                        if (t == null) { Error("CS8716: There is no target type for the default literal.", d); return null; }
                        e.Type = t.IsValueType ? t : CType.Null;
                        if (t.IsValueType || t.IsError) e.Type = t;
                        return DefaultLiteral(t, Sp(d));
                    }
                case CAssign a:
                    Assign(a, Sp(a));
                    Error("using an assignment as a value is not supported in JavaFarmer; assign in a separate statement", a);
                    return null;
                case CIncDec id:
                    ResolveTarget(id.Target, true, out _);
                    Error(id.Op + " can only be used as a separate statement in JavaFarmer", id);
                    return null;
            }
            Error("unsupported expression", e);
            return null;
        }

        IrExpr Interp(CInterp ip)
        {
            ip.Type = CType.String;
            IrExpr acc = null;
            int holes = 0;
            foreach (var part in ip.Parts)
            {
                IrExpr x;
                if (part is string s)
                {
                    if (s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0) Error("line breaks (\\n) inside strings are not supported in JavaFarmer", ip);
                    x = IrLiteral.OfString(s);
                    x.Span = Sp(ip);
                }
                else
                {
                    var ce = (CExpr)part;
                    x = X(ce);
                    holes++;
                    if ((ce.Type ?? CType.Error).Kind == CK.Void) Error("CS0029: Cannot implicitly convert type 'void' to 'object'", ce);
                    else if ((ce.Type ?? CType.Error).Kind == CK.Delegate) Error("printing a function is not supported in JavaFarmer", ce);
                }
                acc = acc == null ? x : new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = x, OpSpan = Sp(ip), Type = IrType.String, Span = Sp(ip) };
            }
            if (acc == null) return IrLiteral.OfString("");
            if (ip.Parts.Count == 1 && holes == 1 && !(ip.Parts[0] is CExpr ce0 && (ce0.Type ?? CType.Error).Kind == CK.String))
                return new IrCast { Target = IrType.String, Operand = acc, OpSpan = Sp(ip), Type = IrType.String };
            return acc;
        }

        // ------------------------------------------------------------------ names
        IrExpr Name(CName n, CType expected)
        {
            var sym = scope.Lookup(n.Name);
            if (sym != null)
            {
                n.Type = sym.Type;
                if (sym.Type.Kind == CK.KeyValue) { Error("KeyValuePair values can only be used as " + n.Name + ".Key and " + n.Name + ".Value in JavaFarmer", n); return null; }
                if (sym.Owner != fn) Captured(sym, n, false);
                else if (!da.Contains(sym)) Error("CS0165: Use of unassigned local variable '" + n.Name + "'", n);
                return new IrName(sym.Ir);
            }
            if (Own.Fields.TryGetValue(n.Name, out var f))
            {
                n.Type = f.Type;
                return new IrName(f.Ir);
            }
            if (Own.Methods.TryGetValue(n.Name, out var ms)) return MethodGroup(ms, null, n, expected, null);
            if (CsBinderConst.Directions.Contains(n.Name)) { n.Type = CType.GameType("Direction"); return new IrConst("Directions", n.Name); }
            var api = CsApi.Find(n.Name);
            if (api != null) return BuiltinGroup(api, n, expected);
            if (scope.DeclaredLater(n.Name)) { Error("CS0841: Cannot use local variable '" + n.Name + "' before it is declared", n); return null; }
            if (CsBinderConst.ConstBags.ContainsKey(n.Name) || n.Name == "Direction" || n.Name == "Math" || n.Name == "Console" || n.Name == "Farm" || n.Name == ClassName || GetOther(n.Name) != null)
            {
                Error("CS0119: '" + n.Name + "' is a type, which is not valid in the given context", n);
                return null;
            }
            Error("CS0103: The name '" + n.Name + "' does not exist in the current context" + Hint(n.Name), n);
            return null;
        }

        string Hint(string name)
        {
            switch (name)
            {
                case "print": case "Print": case "println": return " (use Console.WriteLine)";
                case "min": case "Min": return usingStaticMath ? "" : " (use Math.Min)";
                case "max": case "Max": return usingStaticMath ? "" : " (use Math.Max)";
                case "abs": case "Abs": return usingStaticMath ? "" : " (use Math.Abs)";
                case "len": case "Len": return " (use .Count or .Length)";
                case "True": case "False": case "None": return " (C# writes true, false and null)";
            }
            if (name == "Entity" || name == "Item" || name == "Ground" || name == "Unlock" || name == "Hat") return " (game constants are written " + GameApi.CategoryOfGameType(name) + ".Name)";
            var lower = Own.Methods.Keys.Concat(CsApi.Names).Concat(CsBinderConst.Directions).Concat(CsBinderConst.ConstBags.Keys).FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x, name.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
            return lower != null && lower != name ? " (did you mean " + lower + "?)" : "";
        }

        /// <summary>A method used as a value: only where a delegate is expected.</summary>
        IrExpr MethodGroup(CMethodSym ms, string otherFile, CExpr at, CType expected, CExpr qualifier)
        {
            var dt = CType.DelegateOf(ms.ReturnType, ms.ParamTypes);
            if (expected == null || expected.Kind != CK.Delegate)
            {
                Error(expected == null || expected.IsError
                    ? "CS8917: The delegate type could not be inferred (call it with " + ms.Name + "(...), or pass it where an Action or Func is expected)"
                    : "CS0428: Cannot convert method group '" + ms.Name + "' to non-delegate type '" + expected + "'. Did you intend to invoke the method?", at);
                at.Type = CType.Error;
                return null;
            }
            if (!ReferenceEquals(expected, InferDelegate))
            {
                bool retOk = expected.Elem.Kind == CK.Void ? ms.ReturnType.Kind == CK.Void
                    : ms.ReturnType.Kind != CK.Void && (ms.ReturnType.SameAs(expected.Elem) || (ms.ReturnType.IsReference && CType.Assignable(ms.ReturnType, expected.Elem)));
                if (expected.Params.Count != ms.ParamTypes.Count || !retOk)
                { Error((retOk ? "CS0123: No overload for '" + ms.Name + "' matches delegate '" : "CS0407: '" + ms.ReturnType + " " + ms.Name + "(" + string.Join(", ", ms.ParamTypes) + ")' has the wrong return type for delegate '") + expected + "'", at); at.Type = CType.Error; return null; }
                for (int i = 0; i < ms.ParamTypes.Count; i++)
                    if (!ms.ParamTypes[i].SameAs(expected.Params[i])) { Error("CS0123: No overload for '" + ms.Name + "' matches delegate '" + expected + "'", at); at.Type = CType.Error; return null; }
                at.Type = expected;
            }
            else
            {
                if (ms.ParamTypes.Count > 0) { Error("CS1503: Argument 1: cannot convert from 'method group' to 'Action' (" + ms.Name + " needs arguments; pass a lambda like () => " + ms.Name + "(...))", at); at.Type = CType.Error; return null; }
                at.Type = dt;
            }
            var sym = otherFile == null ? ms.Ir : ExternalMethod(ms, otherFile);
            var fr = new IrFuncRef { Function = sym, NameSpan = at is CMember mm ? Sp(mm.NameStart, mm.NameEnd) : Sp(at) };
            if (otherFile != null && qualifier != null) fr.QualifierSpan = Sp(qualifier);
            return fr;
        }

        IrExpr BuiltinGroup(ApiBuiltin api, CExpr at, CType expected)
        {
            if (expected != null && expected.Kind == CK.Delegate && api.MinArgs == 0)
            {
                at.Type = ReferenceEquals(expected, InferDelegate) ? CType.DelegateOf(CsApi.ReturnType(api), null) : expected;
                return new IrFuncRef { Builtin = api.PythonName, NameSpan = at is CMember mm ? Sp(mm.NameStart, mm.NameEnd) : Sp(at) };
            }
            Error("CS0428: Cannot convert method group '" + GameApi.ToStyle(api.PythonName, NamingStyle.Pascal) + "' to non-delegate type '" + (expected?.ToString() ?? "object") + "'. Did you intend to invoke the method?", at);
            at.Type = CType.Error;
            return null;
        }

        IrSymbol ExternalField(CFieldSym f, string module)
        {
            var s = new IrSymbol(f.Name, IrSymbolKind.ExternalGlobal, f.Type.Ir()) { Module = module, PyName = f.PyName, IsConst = f.IsConst || f.IsReadonly };
            return s;
        }

        readonly Dictionary<(string, string), IrSymbol> externalMethods = new Dictionary<(string, string), IrSymbol>();
        IrSymbol ExternalMethod(CMethodSym m, string module)
        {
            if (externalMethods.TryGetValue((module, m.Name), out var s)) return s;
            s = new IrSymbol(m.Name, IrSymbolKind.ExternalFunction, IrType.Func(m.ReturnType.Ir())) { Module = module, PyName = m.PyName, ParamTypes = m.ParamTypes.Select(t => t.Ir()).ToList() };
            externalMethods[(module, m.Name)] = s;
            return s;
        }

        // ------------------------------------------------------------------ member access
        IrExpr Member(CMember m, CType expected)
        {
            if (m.Target is CPredefined pd) return PredefinedMember(pd.Name, m);
            if (m.Target is CName q && scope.Lookup(q.Name) == null && !Own.Fields.ContainsKey(q.Name))
            {
                if (CsBinderConst.ConstBags.TryGetValue(q.Name, out var gt))
                {
                    m.Type = CType.GameType(gt);
                    q.Type = CType.Error;
                    return new IrConst(q.Name, m.Name);
                }
                switch (q.Name)
                {
                    case "Direction":
                        if (CsBinderConst.Directions.Contains(m.Name)) { m.Type = CType.GameType("Direction"); return new IrConst("Directions", m.Name); }
                        Error("CS0117: 'Direction' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                        return null;
                    case "Farm":
                        {
                            if (CsBinderConst.Directions.Contains(m.Name)) { m.Type = CType.GameType("Direction"); return new IrConst("Directions", m.Name); }
                            var api = CsApi.Find(m.Name);
                            if (api != null) return BuiltinGroup(api, m, expected);
                            Error("CS0117: 'Farm' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                            return null;
                        }
                    case "Math": return PredefinedMember("Math", m);
                    case "Int32": return PredefinedMember("int", m);
                    case "Console":
                        Error("CS0428: Cannot convert method group '" + m.Name + "' to non-delegate type 'object'. Did you intend to invoke the method?", m);
                        return null;
                }
                if (IsClassForm && q.Name == ClassName)
                {
                    if (Own.Fields.TryGetValue(m.Name, out var f)) { m.Type = f.Type; return new IrName(f.Ir); }
                    if (Own.Methods.TryGetValue(m.Name, out var ms)) return MethodGroup(ms, null, m, expected, null);
                    Error("CS0117: '" + ClassName + "' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                    return null;
                }
                var o = GetOther(q.Name);
                if (o != null)
                {
                    if (ForeignFailed(o, q)) return null;
                    if (!o.IsClass) { Error("CS0103: The name '" + q.Name + "' does not exist in the current context (the window " + q.Name + " has top-level statements; other files can only use a static class)", q); return null; }
                    if (o.Fields.TryGetValue(m.Name, out var of))
                    {
                        if (!of.IsAccessible) Error("CS0122: '" + q.Name + "." + m.Name + "' is inaccessible due to its protection level", m.NameStart, m.NameEnd);
                        m.Type = of.Type;
                        return new IrName(ExternalField(of, q.Name)) { QualifierSpan = Sp(q), NameSpan = Sp(m.NameStart, m.NameEnd) };
                    }
                    if (o.Methods.TryGetValue(m.Name, out var om))
                    {
                        if (!om.IsAccessible) { Error("CS0122: '" + q.Name + "." + m.Name + "(" + string.Join(", ", om.ParamTypes) + ")' is inaccessible due to its protection level", m.NameStart, m.NameEnd); return null; }
                        return MethodGroup(om, q.Name, m, expected, q);
                    }
                    Error("CS0117: '" + q.Name + "' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                    return null;
                }
            }
            if (m.Target is CName kvn && scope.Lookup(kvn.Name) is CLocal kv && kv.Type.Kind == CK.KeyValue)
            {
                if (kv.Owner != fn) Captured(kv, kvn, false);
                if (m.Name == "Key") { m.Type = kv.Type.Key; return new IrName(kv.KeyIr); }
                if (m.Name == "Value") { m.Type = kv.Type.Elem; kv.ValueUsed = true; return new IrName(kv.ValueIr); }
                Error("CS1061: '" + kv.Type + "' does not contain a definition for '" + m.Name + "'", m.NameStart, m.NameEnd);
                return null;
            }
            var target = X(m.Target);
            var tt = m.Target.Type ?? CType.Error;
            if (tt.IsError) return null;
            IrOpCall Op(IrOp op, CType t, params IrExpr[] args) { m.Type = t; return new IrOpCall(op, args) { NameSpan = Sp(m.NameStart, m.NameEnd), Type = t.Ir() }; }
            switch (tt.Kind)
            {
                case CK.String:
                    if (m.Name == "Length") return Op(IrOp.Len, CType.Int, target);
                    break;
                case CK.List:
                    if (m.Name == "Count") return Op(IrOp.Len, CType.Int, target);
                    break;
                case CK.Set:
                    if (m.Name == "Count") return Op(IrOp.Len, CType.Int, target);
                    break;
                case CK.Map:
                    if (m.Name == "Count") return Op(IrOp.Len, CType.Int, target);
                    if (m.Name == "Keys") return Op(IrOp.MapKeys, CType.ViewOf(CType.SetOf(tt.Key)), target);
                    if (m.Name == "Values") return Op(IrOp.MapValues, CType.ViewOf(CType.ListOf(tt.Elem)), target);
                    break;
                case CK.Array:
                    if (m.Name == "Length")
                    {
                        if (tt.Rank == 2) { Error("Length of a 2-dimensional array is not supported in JavaFarmer; use GetLength(0) and GetLength(1)", m.NameStart, m.NameEnd); return null; }
                        return Op(IrOp.Len, CType.Int, target);
                    }
                    break;
                case CK.Game:
                    if (tt.Game == "Position")
                    {
                        if (m.Name == "X") return Op(IrOp.PosX, CType.Int, target);
                        if (m.Name == "Y") return Op(IrOp.PosY, CType.Int, target);
                    }
                    if (tt.Game == "Companion")
                    {
                        if (m.Name == "Entity") return Op(IrOp.CompanionEntity, CType.GameType("Entity"), target);
                        if (m.Name == "Position") return Op(IrOp.CompanionPos, CType.GameType("Position"), target);
                        if (m.Name == "X" || m.Name == "Y")
                        {
                            var pos = new IrOpCall(IrOp.CompanionPos, target) { Type = IrType.Game("Position"), Span = Sp(m), NameSpan = Sp(m.NameStart, m.NameEnd), Synthetic = true };
                            return Op(m.Name == "X" ? IrOp.PosX : IrOp.PosY, CType.Int, pos);
                        }
                    }
                    break;
                case CK.Tuple:
                    if (m.Name.StartsWith("Item") && int.TryParse(m.Name.Substring(4), out var k) && k >= 1 && k <= tt.Params.Count)
                    {
                        m.Type = tt.Params[k - 1];
                        var idx = IrLiteral.OfInt(k - 1); idx.Span = Sp(m.NameStart, m.NameEnd); idx.Synthetic = true;
                        return new IrIndex { Target = target, Index = idx };
                    }
                    break;
            }
            Error(MissingMember(tt, m.Name), m.NameStart, m.NameEnd);
            return null;
        }

        static readonly Dictionary<CK, string[]> KnownMembers = new Dictionary<CK, string[]>
        {
            { CK.List, new[] { "Add", "Insert", "RemoveAt", "Remove", "Contains", "IndexOf", "Clear", "Count" } },
            { CK.Set, new[] { "Add", "Remove", "Contains", "Clear", "Count" } },
            { CK.Map, new[] { "Add", "ContainsKey", "Remove", "GetValueOrDefault", "TryGetValue", "Clear", "Count", "Keys", "Values" } },
            { CK.String, new[] { "Length", "Contains", "ToString" } },
            { CK.Array, new[] { "Length", "GetLength" } },
        };

        static string MissingMember(CType tt, string name)
        {
            string hint = "";
            if (KnownMembers.TryGetValue(tt.Kind, out var known))
            {
                var m = known.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase) && k != name);
                if (m != null) hint = " (did you mean " + m + "?)";
            }
            if ((tt.Kind == CK.List || tt.Kind == CK.Set || tt.Kind == CK.Map) && (name == "Length" || name == "Size" || name == "size")) hint = " (use Count)";
            else if ((tt.Kind == CK.Array || tt.Kind == CK.String) && (name == "Count" || name == "Size" || name == "size" || name == "length")) hint = " (use Length)";
            return "CS1061: '" + tt + "' does not contain a definition for '" + name + "' and no accessible extension method '" + name + "' accepting a first argument of type '" + tt + "' could be found (are you missing a using directive or an assembly reference?)" + hint;
        }

        IrExpr PredefinedMember(string type, CMember m)
        {
            switch (type)
            {
                case "int":
                    if (m.Name == "MaxValue") { m.Type = CType.Int; return IrLiteral.OfInt(int.MaxValue); }
                    if (m.Name == "MinValue") { m.Type = CType.Int; return IrLiteral.OfInt(int.MinValue); }
                    break;
                case "Math":
                    if (m.Name == "PI") { m.Type = CType.Double; return IrLiteral.OfFloat(Math.PI); }
                    if (m.Name == "E") { m.Type = CType.Double; return IrLiteral.OfFloat(Math.E); }
                    break;
            }
            Error(type + "." + m.Name + " is not supported in JavaFarmer", m);
            return null;
        }

        // ------------------------------------------------------------------ operators
        IrExpr Unary(CUnary u, CType expected)
        {
            var x = X(u.Operand, u.Op == "!" ? CType.Bool : null);
            var t = u.Operand.Type ?? CType.Error;
            if (t.IsError) { u.Type = u.Op == "!" ? CType.Bool : CType.Error; return x; }
            if (u.Op == "!")
            {
                u.Type = CType.Bool;
                if (t.Kind != CK.Bool) Error("CS0023: Operator '!' cannot be applied to operand of type '" + t + "'", u);
                return new IrUnary { Op = IrUnOp.Not, Operand = x, OpSpan = Sp(u.Start, u.OpEnd) };
            }
            if (!t.IsNumeric) { Error(t.Kind == CK.Char ? "arithmetic on char values is not supported in JavaFarmer" : "CS0023: Operator '" + u.Op + "' cannot be applied to operand of type '" + t + "'", u); u.Type = CType.Error; return x; }
            u.Type = t;
            return new IrUnary { Op = u.Op == "-" ? IrUnOp.Neg : IrUnOp.Plus, Operand = x, OpSpan = Sp(u.Start, u.OpEnd) };
        }

        static bool IsComparison(string op) => op == "==" || op == "!=" || op == "<" || op == ">" || op == "<=" || op == ">=";

        IrExpr Binary(CBinary b)
        {
            bool logical = b.Op == "&&" || b.Op == "||";
            var l = X(b.Left, logical ? CType.Bool : null);
            var r = X(b.Right, logical ? CType.Bool : null);
            var lt = b.Left.Type ?? CType.Error;
            var rt = b.Right.Type ?? CType.Error;
            IrExpr Make(IrBinOp op) => new IrBinary { Op = op, Left = l, Right = r, OpSpan = Sp(b.OpStart, b.OpEnd) };
            if (lt.IsError || rt.IsError) { b.Type = IsComparison(b.Op) || logical ? CType.Bool : CType.Error; return Make(IrBinOp.Add); }
            void Bad() => Error("CS0019: Operator '" + b.Op + "' cannot be applied to operands of type '" + lt + "' and '" + rt + "'", b.OpStart, b.OpEnd);
            if (lt.Kind == CK.Void || rt.Kind == CK.Void) { Bad(); b.Type = CType.Error; return null; }
            bool str = lt.Kind == CK.String || rt.Kind == CK.String;
            bool chr = lt.Kind == CK.Char || rt.Kind == CK.Char;
            switch (b.Op)
            {
                case "+":
                    if (str)
                    {
                        b.Type = CType.String;
                        if (lt.Kind == CK.Delegate || rt.Kind == CK.Delegate) Error("printing a function is not supported in JavaFarmer", b);
                        return Make(IrBinOp.Concat);
                    }
                    if (chr) { Error("arithmetic on char values is not supported in JavaFarmer", b.OpStart, b.OpEnd); b.Type = CType.Error; return null; }
                    if (lt.IsNumeric && rt.IsNumeric) { b.Type = Promote(lt, rt); return Make(IrBinOp.Add); }
                    Bad(); b.Type = CType.Error; return null;
                case "-": case "*": case "/": case "%":
                    if (chr) { Error("arithmetic on char values is not supported in JavaFarmer", b.OpStart, b.OpEnd); b.Type = CType.Error; return null; }
                    if (lt.IsNumeric && rt.IsNumeric)
                    {
                        b.Type = Promote(lt, rt);
                        switch (b.Op)
                        {
                            case "-": return Make(IrBinOp.Sub);
                            case "*": return Make(IrBinOp.Mul);
                            case "/": return Make(b.Type.Kind == CK.Int ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv);
                            default: return Make(IrBinOp.ModTrunc);
                        }
                    }
                    Bad(); b.Type = CType.Error; return null;
                case "<": case ">": case "<=": case ">=":
                    {
                        b.Type = CType.Bool;
                        var op = b.Op == "<" ? IrBinOp.Lt : b.Op == ">" ? IrBinOp.Gt : b.Op == "<=" ? IrBinOp.Le : IrBinOp.Ge;
                        if ((lt.IsNumeric && rt.IsNumeric) || (lt.Kind == CK.Char && rt.Kind == CK.Char)) return Make(op);
                        if (lt.Kind == CK.String && rt.Kind == CK.String) { Error("CS0019: Operator '" + b.Op + "' cannot be applied to operands of type 'string' and 'string' (use string.Compare, which is not supported in JavaFarmer)", b.OpStart, b.OpEnd); return null; }
                        Bad(); return null;
                    }
                case "&&": case "||":
                    b.Type = CType.Bool;
                    if (lt.Kind == CK.Bool && rt.Kind == CK.Bool) return Make(b.Op == "&&" ? IrBinOp.And : IrBinOp.Or);
                    Bad(); return null;
                case "==": case "!=":
                    {
                        b.Type = CType.Bool;
                        bool ok;
                        if (lt.IsNumeric && rt.IsNumeric) ok = true;
                        else if (lt.Kind == CK.Null || rt.Kind == CK.Null)
                        {
                            var other = lt.Kind == CK.Null ? rt : lt;
                            ok = other.Kind == CK.Null || other.IsReference;
                            if (!ok) { Error("CS0037: Cannot convert null to '" + other + "' because it is a non-nullable value type", lt.Kind == CK.Null ? b.Left : b.Right); return null; }
                        }
                        else if (lt.IsCollection || rt.IsCollection)
                        {
                            Error("comparing collections with " + b.Op + " is not supported in JavaFarmer (C# compares references, the game compares contents)", b.OpStart, b.OpEnd);
                            return null;
                        }
                        else if (lt.Kind == CK.Object || rt.Kind == CK.Object) ok = (lt.Kind == CK.Object ? rt : lt).IsReference || lt.Kind == rt.Kind;
                        else ok = lt.SameAs(rt);
                        if (!ok) { Bad(); return null; }
                        return Make(b.Op == "==" ? IrBinOp.Eq : IrBinOp.Ne);
                    }
            }
            Bad();
            return null;
        }

        static CType Promote(CType a, CType b) => a.Kind == CK.Double || b.Kind == CK.Double ? CType.Double : CType.Int;

        IrExpr Conditional(CConditional ce, CType expected)
        {
            var c = Cond(ce.Cond);
            var a = X(ce.Then, expected);
            var z = X(ce.Else, expected);
            var at = ce.Then.Type ?? CType.Error;
            var zt = ce.Else.Type ?? CType.Error;
            CType t;
            if (at.IsError || zt.IsError) t = CType.Error;
            else if (expected != null && !expected.IsError && CType.Assignable(at, expected) && CType.Assignable(zt, expected)) t = expected;
            else if (CType.Assignable(at, zt)) t = zt.Kind == CK.Null ? at : zt;
            else if (CType.Assignable(zt, at)) t = at.Kind == CK.Null ? zt : at;
            else
            {
                Error("CS0173: Type of conditional expression cannot be determined because there is no implicit conversion between '" + at + "' and '" + zt + "'", ce);
                t = CType.Error;
            }
            if (t.Kind == CK.Null) t = expected ?? CType.Object;
            ce.Type = t;
            return new IrConditional { Cond = c, Then = a, Else = z, QSpan = Sp(ce.QStart, ce.QStart + 1) };
        }

        IrExpr Cast(CCast ca)
        {
            var tt = ResolveType(ca.TypeRef);
            var x = X(ca.Operand, tt.Kind == CK.Delegate ? tt : null);
            var ot = ca.Operand.Type ?? CType.Error;
            ca.Type = tt;
            if (tt.IsError || ot.IsError) return x;
            if (ot.Kind == CK.Void) { Error("CS0030: Cannot convert type 'void' to '" + tt + "'", ca); return x; }
            if (ot.Kind == CK.Char || tt.Kind == CK.Char)
            {
                if (!(ot.Kind == CK.Char && tt.Kind == CK.Char) && !(tt.Kind == CK.Char && ot.Kind == CK.Object)) { Error("casts between char and numbers are not supported in JavaFarmer", ca); return x; }
            }
            if (!CType.Castable(ot, tt)) { Error("CS0030: Cannot convert type '" + ot + "' to '" + tt + "'", ca); return x; }
            var cast = new IrCast { Target = tt.Ir(), Operand = x, OpSpan = Sp(ca.Start, ca.CloseParen) };
            // only (int) of a double converts; every other cast is a static type assertion
            cast.IsTypeAssertion = !(tt.Kind == CK.Int && ot.Kind == CK.Double);
            return cast;
        }

        // ------------------------------------------------------------------ indexing
        IrExpr Index(CIndex ix)
        {
            var target = X(ix.Target);
            var tt = ix.Target.Type ?? CType.Error;
            if (tt.IsError) { foreach (var i in ix.Indices) X(i); ix.Type = CType.Error; return null; }
            IrExpr IndexArg(CExpr i, CType keyType)
            {
                if (i is CFromEnd fe)
                {
                    if (keyType != null && keyType.Kind != CK.Int) { Error("CS0029: Cannot implicitly convert type 'System.Index' to '" + keyType + "'", fe); return null; }
                    if (!(tt.Kind == CK.List || tt.Kind == CK.Array || tt.Kind == CK.String)) { Error("the index-from-end operator (^) is only supported on lists, arrays and strings in JavaFarmer", fe); return null; }
                    var ox = X(fe.Operand, CType.Int);
                    var ot = fe.Operand.Type ?? CType.Error;
                    if (!ot.IsError && ot.Kind != CK.Int) Error(IncompatibleMsg(ot, CType.Int), fe.Operand);
                    fe.Type = CType.Int;
                    return new IrUnary { Op = IrUnOp.Neg, Operand = ox, OpSpan = Sp(fe.Start, fe.Start + 1), Span = Sp(fe), Type = IrType.Int };
                }
                var x = X(i, keyType);
                var it = i.Type ?? CType.Error;
                if (keyType != null && !it.IsError && !CType.Assignable(it, keyType)) Error(IncompatibleMsg(it, keyType), i);
                return x;
            }
            switch (tt.Kind)
            {
                case CK.Array:
                    if (ix.Indices.Count != tt.Rank) { Error("CS0022: Wrong number of indices inside []; expected " + tt.Rank, ix); return null; }
                    if (tt.Rank == 2)
                    {
                        ix.Type = tt.Elem;
                        var row = new IrIndex { Target = target, Index = IndexArg(ix.Indices[0], CType.Int), Span = Sp(ix), Type = IrType.Array(tt.Elem.Ir()) };
                        return new IrIndex { Target = row, Index = IndexArg(ix.Indices[1], CType.Int) };
                    }
                    ix.Type = tt.Elem;
                    return new IrIndex { Target = target, Index = IndexArg(ix.Indices[0], CType.Int) };
                case CK.List when !tt.View:
                    if (ix.Indices.Count != 1) { Error("CS1501: No overload for method 'this' takes " + ix.Indices.Count + " arguments", ix); return null; }
                    ix.Type = tt.Elem;
                    return new IrIndex { Target = target, Index = IndexArg(ix.Indices[0], CType.Int) };
                case CK.String:
                    if (ix.Indices.Count != 1) { Error("CS1501: No overload for method 'this' takes " + ix.Indices.Count + " arguments", ix); return null; }
                    ix.Type = CType.Char;
                    return new IrIndex { Target = target, Index = IndexArg(ix.Indices[0], CType.Int) };
                case CK.Map:
                    if (ix.Indices.Count != 1) { Error("CS1501: No overload for method 'this' takes " + ix.Indices.Count + " arguments", ix); return null; }
                    ix.Type = tt.Elem;
                    return new IrIndex { Target = target, Index = IndexArg(ix.Indices[0], tt.Key) };
            }
            foreach (var i in ix.Indices) X(i);
            Error("CS0021: Cannot apply indexing with [] to an expression of type '" + tt + "'", ix);
            ix.Type = CType.Error;
            return null;
        }

        // ------------------------------------------------------------------ lambdas
        IrExpr Lambda(CLambda lam, CType expected)
        {
            bool infer = expected == null || ReferenceEquals(expected, InferDelegate);
            if (!infer && expected.Kind != CK.Delegate)
            {
                if (!expected.IsError) Error("CS1660: Cannot convert lambda expression to type '" + expected + "' because it is not a delegate type", lam.Start, lam.ArrowEnd);
                lam.Type = CType.Error;
                return null;
            }
            var paramTypes = new List<CType>();
            if (infer)
            {
                foreach (var (pt, name, s, e) in lam.Params)
                {
                    if (pt == null) { Error("CS8917: The delegate type could not be inferred.", lam.Start, lam.ArrowEnd); lam.Type = CType.Error; return null; }
                    paramTypes.Add(ResolveType(pt));
                }
                if (ReferenceEquals(expected, InferDelegate) && lam.Params.Count > 0)
                { Error("CS1593: Delegate 'Action' does not take " + lam.Params.Count + " arguments", lam.Start, lam.ArrowEnd); lam.Type = CType.Error; return null; }
            }
            else
            {
                if (expected.Params.Count != lam.Params.Count)
                { Error("CS1593: Delegate '" + expected + "' does not take " + lam.Params.Count + " arguments", lam.Start, lam.ArrowEnd); lam.Type = CType.Error; return null; }
                for (int i = 0; i < lam.Params.Count; i++)
                {
                    var pt = lam.Params[i].Type != null ? ResolveType(lam.Params[i].Type) : expected.Params[i];
                    if (!pt.SameAs(expected.Params[i]) && !pt.IsError) Error("CS1678: Parameter " + (i + 1) + " is declared as type '" + pt + "' but should be '" + expected.Params[i] + "'", lam.Params[i].Start, lam.Params[i].End);
                    paramTypes.Add(expected.Params[i]);
                }
            }
            var oldFn = fn; var oldScope = scope; var oldDa = da; var oldReach = reachable; var oldPre = preStmts;
            fn = new FuncCtx
            {
                Parent = oldFn, Lambda = lam, Display = oldFn.Display, IsStatic = lam.IsStatic,
                ReturnType = infer ? null : expected.Elem, InferredReturns = new List<CType>(),
            };
            scope = new CScope(oldScope, fn);
            da = new HashSet<CLocal>(oldDa);
            reachable = true;
            preStmts = null;
            var ir = new IrLambda { HeaderSpan = Sp(lam.Start, lam.ArrowEnd) };
            for (int i = 0; i < lam.Params.Count; i++)
            {
                var (pt, name, s, e) = lam.Params[i];
                CheckIdentifier(name, s, e);
                CheckRedeclare(name, s, e);
                var sym = DeclareLocal(name, paramTypes[i], false, s, e, IrSymbolKind.Param);
                sym.IsParam = true;
                da.Add(sym);
                ir.Params.Add(new IrParam { Symbol = sym.Ir, Span = Sp(s, e), NameSpan = Sp(s, e) });
            }
            CType ret;
            if (lam.BodyExpr != null)
            {
                var body = lam.BodyExpr;
                var ub = Unparen(body);
                bool stmtLike = ub is CCall || ub is CAssign || ub is CIncDec;
                CType want = infer ? null : expected.Elem;
                if ((infer && stmtLike) || (!infer && want.Kind == CK.Void))
                {
                    if (!stmtLike && !infer)
                    {
                        X(body);
                        Error("CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement", body);
                        ret = CType.Void;
                    }
                    else
                    {
                        var stmts = ExprStatement(body, Sp(body));
                        var bt = body.Type ?? CType.Void;
                        bool valueCall = ub is CCall && bt.Kind != CK.Void && !bt.IsError && infer && !IsStatementOnlyCall(ub as CCall);
                        if (valueCall)
                        {
                            ret = bt;
                            var es = stmts.Count == 1 ? stmts[0] as IrExprStmt : null;
                            if (es != null) { ir.BodyExpr = es.Expr; ir.ReturnsValue = true; }
                            else { ir.Body = new IrBlock(stmts) { Span = Sp(body) }; }
                        }
                        else
                        {
                            ret = CType.Void;
                            if (stmts.Count == 1 && stmts[0] is IrExprStmt es && ub is CCall) ir.BodyExpr = es.Expr;
                            else ir.Body = new IrBlock(stmts) { Span = Sp(body) };
                        }
                    }
                }
                else if (Unparen(body) is CSwitchExpr sx)
                {
                    if (infer) { Error("a switch expression is only supported as a whole value (variable initializer, assignment or return) in JavaFarmer", sx.KwStart, sx.KwEnd); ret = CType.Error; }
                    else
                    {
                        ir.Body = new IrBlock(SwitchExprAssign(sx, want, v => new IrReturn { Value = v, Span = v.Span }, false)) { Span = Sp(body) };
                        ret = want;
                    }
                }
                else
                {
                    var v = X(body, want);
                    var bt = body.Type ?? CType.Error;
                    if (bt.Kind == CK.Void) { Error("CS0029: Cannot implicitly convert type 'void' to '" + (want?.ToString() ?? "object") + "'", body); ret = CType.Error; }
                    else if (want != null) { RequireAssignable(bt, want, body); ret = want; }
                    else ret = bt.Kind == CK.Null ? CType.Object : bt;
                    ir.BodyExpr = v;
                    ir.ReturnsValue = true;
                }
            }
            else
            {
                var blk = new IrBlock { Span = Sp(lam.BodyBlock) };
                scope.Later = LaterNames(lam.BodyBlock.Stmts);
                foreach (var s in lam.BodyBlock.Stmts) Stmt(s, blk.Stmts, true);
                ir.Body = blk;
                if (infer)
                {
                    if (fn.InferredReturns.Count > 0)
                    {
                        if (reachable) Error("CS1643: Not all code paths return a value in lambda expression of type 'Func<" + fn.InferredReturns[0] + ">'", lam.Start, lam.ArrowEnd);
                        ret = fn.InferredReturns[0];
                        foreach (var t in fn.InferredReturns.Skip(1)) if (CType.Assignable(ret, t)) ret = t;
                    }
                    else ret = CType.Void;
                }
                else
                {
                    ret = expected.Elem;
                    if (ret.Kind != CK.Void && reachable) Error("CS1643: Not all code paths return a value in lambda expression of type '" + expected + "'", lam.Start, lam.ArrowEnd);
                }
                ir.ReturnsValue = ret.Kind != CK.Void;
            }
            fn = oldFn; scope = oldScope; da = oldDa; reachable = oldReach; preStmts = oldPre;
            var dt = infer ? CType.DelegateOf(ret, paramTypes) : expected;
            lam.Type = dt;
            ir.ReturnType = ret.Ir();
            ir.Type = dt.Ir();
            return ir;
        }

        static bool IsStatementOnlyCall(CCall c) => c != null && c.Type != null && c.Type.Kind == CK.Void;
    }
}
