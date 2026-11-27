namespace JavaFarmer.Languages.Tests.Java;

/// <summary>Terse builders for hand-made IR in printer tests (untyped = Python-style, typed = Java-style).</summary>
public static class B
{
    public static IrSymbol Local(string n, IrType t = null) => new(n, IrSymbolKind.Local, t);
    public static IrSymbol Global(string n, IrType t = null) => new(n, IrSymbolKind.Global, t);
    public static IrName N(IrSymbol s) => new(s);
    public static IrLiteral I(long v) => IrLiteral.OfInt(v);
    public static IrLiteral F(double v) => IrLiteral.OfFloat(v);
    public static IrLiteral S(string v) => IrLiteral.OfString(v);
    public static IrLiteral T => IrLiteral.OfBool(true);
    public static IrLiteral Fa => IrLiteral.OfBool(false);
    public static IrLiteral Null => IrLiteral.Null();
    public static IrConst C(string cat, string name) => new(cat, name);
    public static IrConst Dir(string name) => new("Directions", name);
    public static IrBinary Bin(IrBinOp op, IrExpr l, IrExpr r) => new() { Op = op, Left = l, Right = r };
    public static IrUnary Not(IrExpr e) => new() { Op = IrUnOp.Not, Operand = e };
    public static IrUnary Neg(IrExpr e) => new() { Op = IrUnOp.Neg, Operand = e };
    public static IrAssign Set(IrExpr target, IrExpr v, IrBinOp? op = null) => new() { Target = target, Value = v, Op = op };
    public static IrAssign Set(IrSymbol s, IrExpr v, IrBinOp? op = null) => Set(N(s), v, op);
    public static IrExprStmt Do(IrExpr e) => new(e);
    public static IrCall Call(string builtin, params IrExpr[] a) => IrCall.ToBuiltin(builtin, a);
    public static IrExprStmt Stmt(string builtin, params IrExpr[] a) => new(Call(builtin, a));
    public static IrOpCall Op(IrOp op, params IrExpr[] a) => new(op, a);
    public static IrBlock Blk(params IrStmt[] s) => new(s);
    public static IrWhile While(IrExpr c, params IrStmt[] body) => new() { Cond = c, Body = Blk(body) };
    public static IrIf If(IrExpr c, IrStmt[] then, IrStmt els = null) => new() { Cond = c, Then = Blk(then), Else = els };
    public static IrRangeFor Range(IrSymbol v, IrExpr end, params IrStmt[] body) => new() { Var = v, End = end, Body = Blk(body) };
    public static IrForEach Each(IrSymbol v, IrExpr it, params IrStmt[] body) => new() { Var = v, Iterable = it, Body = Blk(body) };
    public static IrListLit List(params IrExpr[] items) { var l = new IrListLit(); l.Items.AddRange(items); return l; }
    public static IrTupleLit Tuple(params IrExpr[] items) { var l = new IrTupleLit(); l.Items.AddRange(items); return l; }
    public static IrReturn Ret(IrExpr v = null) => new() { Value = v };

    public static IrFunction Fn(string name, IrSymbol[] ps, params IrStmt[] body)
    {
        var f = new IrFunction { Symbol = new IrSymbol(name, IrSymbolKind.Function) };
        f.Symbol.Function = f;
        foreach (var p in ps) f.Params.Add(new IrParam { Symbol = p });
        f.Body.Stmts.AddRange(body);
        return f;
    }

    public static IrCall CallFn(IrFunction f, params IrExpr[] a) { var c = new IrCall { Kind = IrCallKind.Function, Function = f.Symbol }; c.Args.AddRange(a); return c; }

    /// <summary>A Python-style module (top-level statements, untyped).</summary>
    public static IrModule Script(params IrNode[] items) { var m = new IrModule { Name = "Docs", SourceLanguage = "python" }; m.Items.AddRange(items); return m; }
    /// <summary>A Java-style module (typed IR).</summary>
    public static IrModule JavaModule(params IrNode[] items) { var m = new IrModule { Name = "Main", SourceLanguage = "java" }; m.Items.AddRange(items); return m; }

    public static IrFunction Entry(params IrStmt[] body) { var f = Fn("main", Array.Empty<IrSymbol>(), body); f.IsEntry = true; f.ReturnType = IrType.Void; return f; }
}
