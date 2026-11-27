using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Checked Java AST → Farm IR. Runs after the type checker (all symbols, kinds and types are resolved) and makes
    /// every Java-specific decision explicit in the IR: numeric semantics of / and %, string concatenation, compound
    /// assignment conversions, default values, switch grouping, stripped breaks, and source spans for the source map.
    /// </summary>
    internal sealed class JavaLowering
    {
        readonly Checker chk;
        readonly CompilationUnit cu;
        readonly string fileName;
        readonly Dictionary<object, IrSymbol> syms = new Dictionary<object, IrSymbol>();

        JavaLowering(Checker chk, CompilationUnit cu, string fileName) { this.chk = chk; this.cu = cu; this.fileName = fileName; }

        public static IrModule Lower(Checker chk, CompilationUnit cu, string fileName, string source, bool comments)
        {
            var l = new JavaLowering(chk, cu, fileName);
            var m = l.Module();
            if (comments && source != null) IrComments.Attach(m, JavaComments(source), source);
            return m;
        }

        static List<IrComment> JavaComments(string source)
        {
            var res = new List<IrComment>();
            foreach (var (s, len, k) in JavaSyntax.Highlight(source))
            {
                if (k != TokenKind.Comment) continue;
                var text = source.Substring(s, len);
                if (text.StartsWith("//")) res.Add(new IrComment { Text = text.Substring(2), Span = new Span(s, s + len) });
                else if (text.StartsWith("/*"))
                {
                    bool doc = text.StartsWith("/**") && text.Length > 4;
                    int body = doc ? 3 : 2;
                    int end = text.EndsWith("*/") && text.Length >= body + 2 ? text.Length - 2 : text.Length;
                    res.Add(new IrComment { Text = text.Substring(body, Math.Max(0, end - body)), IsBlock = true, IsDoc = doc, Span = new Span(s, s + len) });
                }
            }
            return res;
        }

        static Span Sp(Node n) => n == null ? Span.None : new Span(n.Start, n.End);
        static Span Sp(int s, int e) => new Span(s, e);

        // ------------------------------------------------------------------ types
        internal static IrType Ir(JType t)
        {
            if (t == null) return IrType.Unknown;
            switch (t.Kind)
            {
                case TK.Int: return IrType.Int;
                case TK.Double: return IrType.Float;
                case TK.Boolean: return IrType.Bool;
                case TK.String: return IrType.String;
                case TK.Object: return IrType.Any;
                case TK.Void: return IrType.Void;
                case TK.Null: return IrType.Null;
                case TK.Array: return IrType.Array(Ir(t.Elem));
                case TK.List: case TK.Collection: return IrType.List(Ir(t.Elem));
                case TK.Set: return IrType.Set(Ir(t.Elem));
                case TK.Map: return IrType.Map(Ir(t.Key), Ir(t.Elem));
                case TK.Game: return IrType.Game(t.Game);
                case TK.Runnable: return IrType.Func(IrType.Void);
                case TK.Supplier: return IrType.Func(Ir(t.Elem));
            }
            return IrType.Unknown;
        }

        // ------------------------------------------------------------------ symbols
        IrSymbol Sym(LocalSymbol l)
        {
            if (l == null) return null;
            if (syms.TryGetValue(l, out var s)) return s;
            s = new IrSymbol(l.Name, l.IsParam ? IrSymbolKind.Param : IrSymbolKind.Local, Ir(l.Type)) { IsConst = l.IsFinal, DeclSpan = Sp(l.DeclStart, l.DeclEnd) };
            syms[l] = s;
            return s;
        }

        IrSymbol Sym(FieldSymbol f)
        {
            if (f == null) return null;
            if (syms.TryGetValue(f, out var s)) return s;
            bool own = f.FileName == fileName;
            s = new IrSymbol(f.Name, own ? IrSymbolKind.Global : IrSymbolKind.ExternalGlobal, Ir(f.Type)) { IsConst = f.IsFinal };
            if (!own) { s.Module = f.FileName; s.PyName = f.PyName; }
            syms[f] = s;
            return s;
        }

        IrSymbol Sym(MethodSymbol m)
        {
            if (m == null) return null;
            if (syms.TryGetValue(m, out var s)) return s;
            bool own = m.FileName == fileName;
            s = new IrSymbol(m.Name, own ? IrSymbolKind.Function : IrSymbolKind.ExternalFunction, IrType.Func(Ir(m.ReturnType)))
            {
                ParamTypes = m.ParamTypes.Select(Ir).ToList()
            };
            if (!own) { s.Module = m.FileName; s.PyName = m.PyName; }
            syms[m] = s;
            return s;
        }

        // ------------------------------------------------------------------ module
        IrModule Module()
        {
            var m = new IrModule { Name = fileName, SourceLanguage = "java", Span = Sp(cu) };
            var items = new List<(int Pos, IrNode Node)>();
            foreach (var f in cu.Fields)
                foreach (var v in f.Vars)
                    if (v.Field != null) items.Add((v.Start, Global(f, v)));
            foreach (var md in cu.Methods)
                if (md.Symbol != null && (!md.IsMain || md == chk.Main)) items.Add((md.Start, Function(md)));
            foreach (var it in items.OrderBy(i => i.Pos)) m.Items.Add(it.Node);
            return m;
        }

        IrGlobal Global(FieldDecl f, VarDeclarator v)
        {
            var g = new IrGlobal
            {
                Symbol = Sym(v.Field), Span = Sp(v), NameSpan = Sp(v.NameStart, v.NameEnd),
                TypeExplicit = f.Type.Name != "var", IsPrivate = (f.Mods & Mods.Private) != 0
            };
            g.Symbol.DeclSpan = g.NameSpan;
            if (v.Init != null) g.Init = Expr(v.Init);
            else
            {
                g.Init = DefaultLiteral(v.Field.Type, Sp(v));
                g.ImplicitInit = true;
            }
            return g;
        }

        static IrLiteral DefaultLiteral(JType t, Span span)
        {
            IrLiteral lit;
            if (t.IsPrimitive) lit = t.Kind == TK.Boolean ? IrLiteral.OfBool(false) : t.Kind == TK.Double ? new IrLiteral { Kind = IrLitKind.Int, Int = 0, Type = IrType.Float } : IrLiteral.OfInt(0);
            else lit = IrLiteral.Null();
            lit.Span = span;
            return lit;
        }

        IrFunction Function(MethodDecl md)
        {
            var f = new IrFunction
            {
                Symbol = Sym(md.Symbol), ReturnType = Ir(md.Symbol.ReturnType), IsEntry = md == chk.Main,
                IsPrivate = (md.Mods & Mods.Private) != 0, Span = Sp(md), HeaderSpan = Sp(md.HeaderStart, md.HeaderEnd),
                NameSpan = Sp(md.NameStart, md.NameEnd)
            };
            f.Symbol.Function = f;
            f.Symbol.DeclSpan = f.NameSpan;
            foreach (var p in md.Params)
            {
                var ps = Sym(p.Symbol) ?? new IrSymbol(p.Name, IrSymbolKind.Param, Ir(p.Type?.Resolved));
                f.Params.Add(new IrParam { Symbol = ps, Span = Sp(p), NameSpan = Sp(p.NameStart, p.NameEnd) });
            }
            f.Body = Block(md.Body);
            return f;
        }

        // ------------------------------------------------------------------ statements
        IrBlock Block(Stmt s)
        {
            if (s is BlockStmt b)
            {
                var blk = new IrBlock { Span = Sp(b) };
                foreach (var st in b.Stmts) Stmt(st, blk.Stmts);
                return blk;
            }
            var one = new IrBlock { Span = Sp(s) };
            if (s != null) Stmt(s, one.Stmts);
            return one;
        }

        IrBlock Block(List<Stmt> stmts, Span span)
        {
            var blk = new IrBlock { Span = span };
            foreach (var st in stmts) Stmt(st, blk.Stmts);
            return blk;
        }

        void Stmt(Stmt s, List<IrStmt> outList)
        {
            switch (s)
            {
                case BlockStmt b: outList.Add(Block(b)); return;
                case EmptyStmt _: return;
                case LocalVarStmt lv:
                    foreach (var v in lv.Vars)
                    {
                        if (v.Local == null) continue;
                        var d = new IrVarDecl { Symbol = Sym(v.Local), NameSpan = Sp(v.NameStart, v.NameEnd), Span = Sp(v), TypeExplicit = lv.Type.Name != "var" };
                        d.Symbol.DeclSpan = d.NameSpan;
                        if (v.Init != null) d.Init = Expr(v.Init);
                        outList.Add(d);
                    }
                    return;
                case ExprStmt es:
                    {
                        var st = ExprAsStmt(es.E, Sp(es));
                        if (st != null) outList.Add(st);
                        return;
                    }
                case IfStmt ifs: outList.Add(If(ifs)); return;
                case WhileStmt ws:
                    outList.Add(new IrWhile { Cond = Expr(ws.Cond), Body = Block(ws.Body), KwSpan = Sp(ws.Start, ws.KwEnd), Span = Sp(ws) });
                    return;
                case DoWhileStmt dw:
                    outList.Add(new IrDoWhile { Body = Block(dw.Body), Cond = Expr(dw.Cond), KwSpan = Sp(dw.Start, dw.Start + 2), WhileSpan = Sp(dw.WhileStart, dw.WhileEnd), Span = Sp(dw) });
                    return;
                case ForStmt f:
                    {
                        var cf = new IrClassicFor { KwSpan = Sp(f.Start, f.KwEnd), HeaderSpan = Sp(f.Start, f.HeaderEnd), Span = Sp(f) };
                        foreach (var init in f.Init) Stmt(init, cf.Init);
                        if (f.Cond != null) cf.Cond = Expr(f.Cond);
                        foreach (var u in f.Update) { var st = ExprAsStmt(u, Sp(u)); if (st != null) cf.Update.Add(st); }
                        cf.Body = Block(f.Body);
                        outList.Add(cf);
                        return;
                    }
                case ForEachStmt fe:
                    {
                        var sym = Sym(fe.Local) ?? new IrSymbol(fe.Name, IrSymbolKind.Local);
                        sym.DeclSpan = Sp(fe.NameStart, fe.NameEnd);
                        outList.Add(new IrForEach { Var = sym, Iterable = Expr(fe.Iterable), Body = Block(fe.Body), KwSpan = Sp(fe.Start, fe.KwEnd), VarSpan = Sp(fe.NameStart, fe.NameEnd), Span = Sp(fe) });
                        return;
                    }
                case BreakStmt br:
                    if (!br.Strip) outList.Add(new IrBreak { Span = Sp(br) });
                    return;
                case ContinueStmt co: outList.Add(new IrContinue { Span = Sp(co) }); return;
                case ReturnStmt r: outList.Add(new IrReturn { Value = r.Value == null ? null : Expr(r.Value), Span = Sp(r) }); return;
                case SwitchStmt sw: outList.Add(Switch(sw)); return;
            }
            throw new InvalidOperationException("unsupported statement " + s?.GetType().Name);
        }

        IrIf If(IfStmt ifs)
        {
            var i = new IrIf { Cond = Expr(ifs.Cond), Then = Block(ifs.Then), KwSpan = Sp(ifs.Start, ifs.KwEnd), Span = Sp(ifs) };
            if (ifs.Else is IfStmt next) i.Else = If(next);
            else if (ifs.Else != null) i.Else = Block(ifs.Else);
            return i;
        }

        IrSwitch Switch(SwitchStmt sw)
        {
            var res = new IrSwitch { Selector = Expr(sw.Selector), KwSpan = Sp(sw.Start, sw.KwEnd), Span = Sp(sw), Style = sw.Arrow ? IrSwitchStyle.Arrow : IrSwitchStyle.Colon };
            // group cases: empty colon-form cases fall into the next one
            var pending = new List<Expr>();
            bool pendingDefault = false;
            SwitchCase first = null;
            foreach (var c in sw.Cases)
            {
                if (first == null) first = c;
                pending.AddRange(c.Labels);
                pendingDefault |= c.IsDefault;
                if (!sw.Arrow && c.Body.Count == 0 && c != sw.Cases[sw.Cases.Count - 1]) continue;
                var ic = new IrCase { IsDefault = pendingDefault, LabelSpan = Sp(first.Start, first.LabelEnd), Span = Sp(first.Start, c.End) };
                foreach (var l in pending) ic.Labels.Add(Expr(l));
                ic.Body = Block(c.Body, Sp(c));
                res.Cases.Add(ic);
                pending = new List<Expr>(); pendingDefault = false; first = null;
            }
            return res;
        }

        /// <summary>An expression used as a statement (also for-loop init/update).</summary>
        IrStmt ExprAsStmt(Expr e, Span span)
        {
            switch (e)
            {
                case ParenExpr p: return ExprAsStmt(p.Inner, span);
                case AssignExpr a: return Assign(a, span);
                case IncDecExpr id:
                    {
                        var opSpan = Sp(id.OpStart, id.OpEnd);
                        var one = IrLiteral.OfInt(1); one.Span = opSpan;
                        return new IrAssign
                        {
                            Target = Expr(id.Target), Op = id.Op == "++" ? IrBinOp.Add : IrBinOp.Sub, Value = one, OpSpan = opSpan,
                            IsIncDec = true, Prefix = id.Prefix, Span = span
                        };
                    }
                case CallExpr c when c.Kind == CallKind.Member && (c.Member == "list.set" || c.Member == "map.put"):
                    {
                        var op = new IrOpCall(c.Member == "list.set" ? IrOp.ListSet : IrOp.MapPut, Expr(c.Target), Expr(c.Args[0]), Expr(c.Args[1]))
                        { Span = Sp(c), NameSpan = Sp(c.NameStart, c.NameEnd), Type = IrType.Void };
                        return new IrExprStmt { Expr = op, Span = span };
                    }
                case NewExpr _:
                    return null;   // a bare object creation has no effect (the checker reports it)
            }
            return new IrExprStmt { Expr = Expr(e), Span = span };
        }

        IrAssign Assign(AssignExpr a, Span span)
        {
            var target = Expr(Unparen(a.Target));
            var res = new IrAssign { Target = target, Value = Expr(a.Value), OpSpan = Sp(a.OpStart, a.OpEnd), Span = span };
            if (a.Op == "=") return res;
            var tt = Unparen(a.Target).Type ?? JType.Error;
            var vt = a.Value.Type ?? JType.Error;
            bool intTarget = tt.Kind == TK.Int;
            switch (a.Op)
            {
                case "+=": res.Op = tt.Kind == TK.String ? IrBinOp.Concat : IrBinOp.Add; break;
                case "-=": res.Op = IrBinOp.Sub; break;
                case "*=": res.Op = IrBinOp.Mul; break;
                case "/=": res.Op = intTarget && vt.Kind == TK.Int ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv; break;
                case "%=": res.Op = IrBinOp.ModTrunc; break;
                default: throw new InvalidOperationException("operator " + a.Op);
            }
            if (res.Op != IrBinOp.Concat && intTarget && vt.Kind == TK.Double) res.TruncateToInt = true;
            return res;
        }

        static Expr Unparen(Expr e) { while (e is ParenExpr p) e = p.Inner; return e; }

        // ------------------------------------------------------------------ expressions
        IrExpr Expr(Expr e)
        {
            if (e is ParenExpr p)
            {
                var inner = Expr(Unparen(p));
                inner.ParenSpan = Sp(p);
                return inner;
            }
            var r = ExprCore(e);
            if (r.Span.IsNone) r.Span = Sp(e);
            if (!r.Type.IsKnown && e.Type != null) r.Type = Ir(e.Type);
            return r;
        }

        IrExpr ExprCore(Expr e)
        {
            switch (e)
            {
                case LiteralExpr l:
                    switch (l.Kind)
                    {
                        case LitKind.Int: return IrLiteral.OfInt(l.IntValue);
                        case LitKind.Double: return IrLiteral.OfFloat(l.DoubleValue);
                        case LitKind.Bool: return IrLiteral.OfBool(l.BoolValue);
                        case LitKind.Null: return IrLiteral.Null();
                        default: return IrLiteral.OfString(l.StrValue ?? "");
                    }
                case NameExpr n:
                    switch (n.Kind)
                    {
                        case NameKind.Local: return new IrName(Sym(n.Local));
                        case NameKind.Field: return new IrName(Sym(n.Field));
                        case NameKind.Direction: return new IrConst("Directions", n.Name);
                    }
                    throw new InvalidOperationException("unresolved name " + n.Name);
                case FieldAccessExpr fa: return FieldAccess(fa);
                case CallExpr c: return Call(c);
                case NewExpr ne: return NewCollection(ne);
                case NewArrayExpr na: return NewArray(na);
                case ArrayInitExpr ai: return ArrayLiteral(ai);
                case IndexExpr ix: return new IrIndex { Target = Expr(ix.Target), Index = Expr(ix.Index) };
                case UnaryExpr u:
                    return new IrUnary { Op = u.Op == "!" ? IrUnOp.Not : u.Op == "-" ? IrUnOp.Neg : IrUnOp.Plus, Operand = Expr(u.Operand), OpSpan = Sp(u.Start, u.OpEnd) };
                case BinaryExpr b: return Binary(b);
                case ConditionalExpr ce:
                    return new IrConditional { Cond = Expr(ce.Cond), Then = Expr(ce.Then), Else = Expr(ce.Else), QSpan = Sp(ce.QStart, ce.QStart + 1) };
                case CastExpr ca:
                    {
                        var tt = ca.Type ?? JType.Error;
                        var ot = ca.Operand.Type ?? JType.Error;
                        var cast = new IrCast { Target = Ir(tt), Operand = Expr(ca.Operand), OpSpan = Sp(ca.Start, ca.TypeRef.End + 1) };
                        // only (int) of a double converts; every other Java cast is a static type assertion
                        cast.IsTypeAssertion = !(tt.IsPrimitive && tt.Kind == TK.Int && ot.Kind == TK.Double);
                        return cast;
                    }
                case LambdaExpr lam:
                    {
                        var l = new IrLambda { ReturnsValue = lam.IsSupplier, ReturnType = Ir(lam.ReturnType), HeaderSpan = Sp(lam.Start, lam.ArrowEnd) };
                        if (lam.BodyExpr != null)
                        {
                            var u = Unparen(lam.BodyExpr);
                            if (!lam.IsSupplier && (u is AssignExpr || u is IncDecExpr || (u is CallExpr mc && mc.Kind == CallKind.Member && (mc.Member == "list.set" || mc.Member == "map.put"))))
                            {
                                // "() -> count++": a statement body becomes a one-statement block
                                l.Body = new IrBlock { Span = Sp(lam.BodyExpr) };
                                var st = ExprAsStmt(u, Sp(lam.BodyExpr));
                                if (st != null) l.Body.Stmts.Add(st);
                            }
                            else l.BodyExpr = Expr(lam.BodyExpr);
                        }
                        else l.Body = Block(lam.BodyBlock);
                        l.Type = lam.IsSupplier ? IrType.Func(Ir(lam.ReturnType)) : IrType.Func(IrType.Void);
                        return l;
                    }
                case MethodRefExpr mr:
                    {
                        var fr = new IrFuncRef { Function = Sym(mr.Method), NameSpan = Sp(mr.NameStart, mr.NameEnd) };
                        if (mr.OtherFile != null) fr.QualifierSpan = Sp(mr.Qualifier);
                        return fr;
                    }
            }
            throw new InvalidOperationException("unsupported expression " + e.GetType().Name);
        }

        IrExpr FieldAccess(FieldAccessExpr fa)
        {
            if (fa.ConstText != null)
            {
                if (fa.ConstText.IndexOf('.') >= 0) return IrLiteral.OfFloat(double.Parse(fa.ConstText, CultureInfo.InvariantCulture));
                return IrLiteral.OfInt(long.Parse(fa.ConstText, CultureInfo.InvariantCulture));
            }
            if (fa.IsConstMember && fa.Target is NameExpr bag) return new IrConst(bag.Name, fa.Name);
            if (fa.IsArrayLength) return new IrOpCall(IrOp.Len, Expr(fa.Target)) { NameSpan = Sp(fa.NameStart, fa.NameEnd), Type = IrType.Int };
            if (fa.Field != null)
            {
                var s = Sym(fa.Field);
                var n = new IrName(s);
                if (s.Kind == IrSymbolKind.ExternalGlobal) { n.QualifierSpan = Sp(fa.Target); n.NameSpan = Sp(fa.NameStart, fa.NameEnd); }
                return n;
            }
            throw new InvalidOperationException("unresolved field " + fa.Name);
        }

        IrExpr Binary(BinaryExpr b)
        {
            IrBinOp op;
            switch (b.Op)
            {
                case "+": op = (b.Type ?? JType.Error).Kind == TK.String ? IrBinOp.Concat : IrBinOp.Add; break;
                case "-": op = IrBinOp.Sub; break;
                case "*": op = IrBinOp.Mul; break;
                case "/": op = (b.Type ?? JType.Error).Kind == TK.Int ? IrBinOp.IntDivTrunc : IrBinOp.TrueDiv; break;
                case "%": op = IrBinOp.ModTrunc; break;
                case "<": op = IrBinOp.Lt; break;
                case ">": op = IrBinOp.Gt; break;
                case "<=": op = IrBinOp.Le; break;
                case ">=": op = IrBinOp.Ge; break;
                case "==": op = IrBinOp.Eq; break;
                case "!=": op = IrBinOp.Ne; break;
                case "&&": op = IrBinOp.And; break;
                case "||": op = IrBinOp.Or; break;
                default: throw new InvalidOperationException("operator " + b.Op);
            }
            return new IrBinary { Op = op, Left = Expr(b.Left), Right = Expr(b.Right), OpSpan = Sp(b.OpStart, b.OpEnd) };
        }

        IrExpr NewCollection(NewExpr ne)
        {
            var t = ne.Type ?? JType.Error;
            Expr arg = ne.Args.Count == 1 ? ne.Args[0] : null;
            bool copy = arg != null && !(arg.Type != null && arg.Type.Kind == TK.Int && arg.Type.IsPrimitive);
            var span = Sp(ne);
            switch (t.Kind)
            {
                case TK.List: return copy ? new IrOpCall(IrOp.ListCopy, Expr(arg)) { NameSpan = span } : (IrExpr)new IrListLit();
                case TK.Set: return copy ? new IrOpCall(IrOp.SetCopy, Expr(arg)) { NameSpan = span } : (IrExpr)new IrSetLit();
                case TK.Map: return copy ? new IrOpCall(IrOp.MapCopy, Expr(arg)) { NameSpan = span } : (IrExpr)new IrMapLit();
            }
            return IrLiteral.Null();
        }

        IrExpr NewArray(NewArrayExpr na)
        {
            if (na.Init != null) return ArrayLiteral(na.Init);
            var span = Sp(na);
            var elem = na.Type ?? JType.Error;
            var baseT = elem; while (baseT.Kind == TK.Array) baseT = baseT.Elem;
            if (na.DimExprs.Count == 1)
            {
                var d = na.TotalDims == 1 ? DefaultLiteral(baseT, span) : NullAt(span);
                return new IrOpCall(IrOp.ArrayNew, Expr(na.DimExprs[0]), d);
            }
            return new IrOpCall(IrOp.ArrayNew2, Expr(na.DimExprs[0]), Expr(na.DimExprs[1]), DefaultLiteral(baseT, span));
        }

        static IrLiteral NullAt(Span s) { var l = IrLiteral.Null(); l.Span = s; return l; }

        IrExpr ArrayLiteral(ArrayInitExpr ai)
        {
            var l = new IrListLit { IsArray = true, Span = Sp(ai) };
            foreach (var it in ai.Items) l.Items.Add(Expr(it));
            l.Type = Ir(ai.Type);
            return l;
        }

        IrExpr Call(CallExpr c)
        {
            var nameSpan = Sp(c.NameStart, c.NameEnd);
            List<IrExpr> Args() => c.Args.Select(Expr).ToList();
            IrCall Fn(IrCallKind k)
            {
                var call = new IrCall { Kind = k, NameSpan = nameSpan };
                call.Args.AddRange(Args());
                return call;
            }
            switch (c.Kind)
            {
                case CallKind.UserMethod: { var call = Fn(IrCallKind.Function); call.Function = Sym(c.Method); return call; }
                case CallKind.OtherMethod: { var call = Fn(IrCallKind.Function); call.Function = Sym(c.Method); call.QualifierSpan = Sp(c.Target); return call; }
                case CallKind.GameApi: { var call = Fn(IrCallKind.Builtin); call.Builtin = c.Api.PythonName; return call; }
                case CallKind.Print: { var call = Fn(IrCallKind.Builtin); call.Builtin = "print"; return call; }
                case CallKind.StringValueOf:
                    return new IrCast { Target = IrType.String, Operand = Expr(c.Args[0]), OpSpan = nameSpan };
                case CallKind.MathFn:
                    switch (c.Name)
                    {
                        case "min": case "max": case "abs": case "random": { var call = Fn(IrCallKind.Builtin); call.Builtin = c.Name; return call; }
                        case "floor": return new IrOpCall(IrOp.Floor, Args().ToArray()) { NameSpan = nameSpan };
                        case "ceil": return new IrOpCall(IrOp.Ceil, Args().ToArray()) { NameSpan = nameSpan };
                        case "round": return new IrOpCall(IrOp.Round, Args().ToArray()) { NameSpan = nameSpan };
                        case "sqrt": return new IrOpCall(IrOp.Sqrt, Args().ToArray()) { NameSpan = nameSpan };
                        case "pow": return new IrBinary { Op = IrBinOp.Pow, Left = Expr(c.Args[0]), Right = Expr(c.Args[1]), OpSpan = nameSpan };
                        case "floorDiv": return new IrBinary { Op = IrBinOp.IntDivFloor, Left = Expr(c.Args[0]), Right = Expr(c.Args[1]), OpSpan = nameSpan };
                        case "floorMod": return new IrBinary { Op = IrBinOp.ModFloor, Left = Expr(c.Args[0]), Right = Expr(c.Args[1]), OpSpan = nameSpan };
                    }
                    break;
                case CallKind.ListOf: { var l = new IrListLit(); l.Items.AddRange(Args()); return l; }
                case CallKind.SetOf: { var s = new IrSetLit(); s.Items.AddRange(Args()); return s; }
                case CallKind.MapOf:
                    {
                        var m = new IrMapLit();
                        var a = Args();
                        for (int i = 0; i + 1 < a.Count; i += 2) { m.Keys.Add(a[i]); m.Values.Add(a[i + 1]); }
                        return m;
                    }
                case CallKind.FunctionalRun:
                    return new IrCall { Kind = IrCallKind.Value, Callee = Expr(c.Target), NameSpan = nameSpan };
                case CallKind.Member:
                    return Member(c, nameSpan);
            }
            throw new InvalidOperationException("unsupported call " + c.Name);
        }

        IrExpr Member(CallExpr c, Span nameSpan)
        {
            IrOpCall Op(IrOp op, params IrExpr[] args) => new IrOpCall(op, args) { NameSpan = nameSpan };
            IrExpr T() => Expr(c.Target);
            IrExpr A(int i) => Expr(c.Args[i]);
            IrExpr CompPos() { var p = Op(IrOp.CompanionPos, T()); p.Type = IrType.Game("Position"); p.Span = Sp(c); return p; }
            switch (c.Member)
            {
                case "any.equals": return new IrBinary { Op = IrBinOp.Eq, Left = T(), Right = A(0), OpSpan = nameSpan };
                case "any.toString": return new IrCast { Target = IrType.String, Operand = T(), OpSpan = nameSpan };
                case "box": return c.Args.Count > 0 ? Expr(c.Args[0]) : T();
                case "unbox": return T();
                case "str.length": case "list.size": case "set.size": case "map.size": return Op(IrOp.Len, T());
                case "str.isEmpty": case "list.isEmpty": case "set.isEmpty": case "map.isEmpty": return Op(IrOp.IsEmpty, T());
                case "str.charAt": case "list.get": return new IrIndex { Target = T(), Index = A(0) };
                case "str.contains": case "list.contains": case "set.contains": case "map.containsKey": return Op(IrOp.Contains, T(), A(0));
                case "list.add": return Op(IrOp.ListAppend, T(), A(0));
                case "list.addAt": return Op(IrOp.ListInsert, T(), A(0), A(1));
                case "list.removeAt": return Op(IrOp.ListPop, T(), A(0));
                case "set.add": return Op(IrOp.SetAdd, T(), A(0));
                case "list.removeObj": case "set.remove": return Op(IrOp.RemoveValueIfPresent, T(), A(0));
                case "list.indexOf": return Op(IrOp.ListIndexOf, T(), A(0));
                case "list.clear": case "set.clear": case "map.clear": return Op(IrOp.Clear, T());
                case "map.get": return Op(IrOp.MapGetOrNull, T(), A(0));
                case "map.getOrDefault": return Op(IrOp.MapGetOrDefault, T(), A(0), A(1));
                case "map.remove": return Op(IrOp.MapRemoveIfPresent, T(), A(0));
                case "map.keySet": return Op(IrOp.MapKeys, T());
                case "map.values": return Op(IrOp.MapValues, T());
                case "pos.x": return Op(IrOp.PosX, T());
                case "pos.y": return Op(IrOp.PosY, T());
                case "comp.entity": return Op(IrOp.CompanionEntity, T());
                case "comp.position": return Op(IrOp.CompanionPos, T());
                case "comp.x": return Op(IrOp.PosX, CompPos());
                case "comp.y": return Op(IrOp.PosY, CompPos());
                case "list.set": return Op(IrOp.ListSet, T(), A(0), A(1));
                case "map.put": return Op(IrOp.MapPut, T(), A(0), A(1));
            }
            throw new InvalidOperationException("unsupported method " + c.Name);
        }
    }
}
