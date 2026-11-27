namespace JavaFarmer.Languages.Tests.Core;

using JavaFarmer.Compiler;
using Langs = JavaFarmer.Compiler.Languages;

/// <summary>Core: IR emitter features no v1 Java program exercises, GameApi registration, types, registry, comments.</summary>
public class IrEmitterTests
{
    static IrSymbol Local(string n, IrType t = null) => new IrSymbol(n, IrSymbolKind.Local, t ?? IrType.Int);
    static IrName N(IrSymbol s) => new IrName(s);
    static IrLiteral I(long v) => IrLiteral.OfInt(v);
    static IrBlock B(params IrStmt[] s) => new IrBlock(s);
    static IrExprStmt Call(string builtin, params IrExpr[] args) => new IrExprStmt(IrCall.ToBuiltin(builtin, args));

    static string Emit(IrModule m)
    {
        var r = IrEmitter.Compile(m, "", new CompileOptions { FileName = m.Name ?? "main" });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.True(GameOracle.TryParse(r.Python) == null, GameOracle.TryParse(r.Python) + "\n" + r.Python);
        return r.Python;
    }

    static IrModule Script(params IrNode[] items) { var m = new IrModule { Name = "main" }; m.Items.AddRange(items); return m; }

    [Fact]
    public void Script_EmitsTopLevelStatementsInOrder()
    {
        var x = Local("x");
        var py = Emit(Script(new IrVarDecl { Symbol = x, Init = I(1) }, Call("print", N(x))));
        Assert.Equal("x = 1\nprint(x)\n", py);
    }

    [Fact]
    public void RangeFor_Native_InclusiveAndStep()
    {
        var i = Local("i");
        var py = Emit(Script(
            new IrRangeFor { Var = i, Start = I(1), End = I(10), Inclusive = true, Body = B(Call("print", N(i))) },
            new IrRangeFor { Var = Local("j"), End = I(5), Body = B() },
            new IrRangeFor { Var = Local("k"), Start = I(10), End = I(0), Step = new IrUnary { Op = IrUnOp.Neg, Operand = I(2) }, Body = B() }));
        Assert.Contains("for i in range(1, 10 + 1):", py);
        Assert.Contains("for j in range(5):\n    pass", py);
        Assert.Contains("for k in range(10, 0, -2):", py);
    }

    [Fact]
    public void Loop_DoWhile_ForEachWithValue()
    {
        var m = Local("m", IrType.Map(IrType.String, IrType.Int));
        var k = Local("k", IrType.String); var v = Local("v");
        var py = Emit(Script(
            new IrVarDecl { Symbol = m, Init = new IrMapLit() },
            new IrForEach { Var = k, ValueVar = v, Iterable = N(m), Body = B(Call("print", N(k), N(v))) },
            new IrLoop { Body = B(new IrBreak()) }));
        Assert.Contains("for k in m:\n    v = m[k]\n    print(k, v)\n", py);
        Assert.Contains("while True:\n    break\n", py);
    }

    [Fact]
    public void NumericSemantics()
    {
        var a = Local("a"); var b = Local("b");
        IrExpr Bin(IrBinOp op) => new IrBinary { Op = op, Left = N(a), Right = N(b) };
        var py = Emit(Script(
            new IrVarDecl { Symbol = a, Init = I(-7) }, new IrVarDecl { Symbol = b, Init = I(2) },
            Call("print", Bin(IrBinOp.IntDivFloor)), Call("print", Bin(IrBinOp.ModFloor)),
            Call("print", Bin(IrBinOp.IntDivTrunc)), Call("print", Bin(IrBinOp.ModTrunc)), Call("print", Bin(IrBinOp.Pow)),
            Call("print", new IrIndex { Target = new IrListLit { Items = { I(5), I(6) } }, Index = I(1), BaseOffset = 1 })));
        Assert.Contains("print(a // b)", py);
        Assert.Contains("print(a % b)", py);
        Assert.Contains("print(__jdiv(a, b))", py);
        Assert.Contains("print(__jmod(a, b))", py);
        Assert.Contains("print(__jpow(a, b))", py);
        Assert.Contains("print([5, 6][1 - 1])", py);
    }

    [Fact]
    public void SyntheticNodes_AreFree()
    {
        var x = Local("x");
        var concat = new IrBinary { Op = IrBinOp.Concat, Left = IrLiteral.OfString("x="), Right = N(x), Synthetic = true, Span = new Span(0, 10), Type = IrType.String };
        var call = IrCall.ToBuiltin("print", concat); call.Span = new Span(0, 10); call.NameSpan = new Span(0, 6);
        var m = Script(new IrVarDecl { Symbol = x, Init = I(1), Span = new Span(0, 1) }, new IrExprStmt(call));
        var r = IrEmitter.Compile(m, new string(' ', 20), new CompileOptions { FileName = "main" });
        Assert.True(r.Success);
        int plus = r.Python.IndexOf(" + ", StringComparison.Ordinal);
        Assert.Equal(SegmentKind.Synthetic, r.Map.Find(plus + 1).Kind);
        int pr = r.Python.IndexOf("print", StringComparison.Ordinal);
        Assert.Equal(SegmentKind.User, r.Map.Find(pr).Kind);
    }

    [Fact]
    public void Shadowing_InNestedBlocks_IsRenamed()
    {
        var outer = Local("i"); var inner = Local("i");
        var f = new IrFunction { Symbol = new IrSymbol("f", IrSymbolKind.Function), Body = B(
            new IrVarDecl { Symbol = outer, Init = I(1) },
            B(new IrVarDecl { Symbol = inner, Init = I(2) }, Call("print", N(inner))),
            Call("print", N(outer))) };
        f.Symbol.Function = f;
        var py = Emit(Script(f));
        Assert.Contains("i_2 = 2\n    print(i_2)", py);
        Assert.Contains("print(i)", py);
    }

    [Fact]
    public void Globals_TupleUnpacking_LambdaParams_Imports()
    {
        var g = new IrSymbol("total", IrSymbolKind.Global, IrType.Int);
        var a = Local("a"); var b = Local("b");
        var p = new IrSymbol("n", IrSymbolKind.Param, IrType.Int);
        var lam = new IrLambda { Params = { new IrParam { Symbol = p } }, BodyExpr = N(p), ReturnsValue = true };
        var cb = Local("cb", IrType.Func(IrType.Int));
        var ext = new IrSymbol("helper", IrSymbolKind.ExternalFunction) { Module = "Utils" };
        var fn = new IrFunction { Symbol = new IrSymbol("bump", IrSymbolKind.Function), Body = B(
            new IrAssign { Target = N(g), Op = IrBinOp.Add, Value = I(1) },
            new IrAssign { Target = new IrTupleLit { Items = { N(a), N(b) } }, Value = new IrTupleLit { Items = { I(1), I(2) } } },
            new IrVarDecl { Symbol = cb, Init = lam },
            new IrExprStmt(new IrCall { Kind = IrCallKind.Value, Callee = N(cb), Args = { I(3) } }),
            new IrExprStmt(new IrCall { Kind = IrCallKind.Function, Function = ext })) };
        fn.Symbol.Function = fn;
        var py = Emit(Script(new IrGlobal { Symbol = g, Init = I(0) }, fn));
        Assert.StartsWith("import Utils\n", py);
        Assert.Contains("def bump():\n    global total\n    total += 1\n    a, b = (1, 2)\n    def __jlambda1(n):\n        return n\n    cb = __jlambda1\n    cb(3)\n    Utils.helper()\n", py);
    }

    [Fact]
    public void ClearDispatchesOnReceiverType_TypeAssertionIsFree()
    {
        var l = Local("l", IrType.List(IrType.Int)); var s = Local("s", IrType.Set(IrType.Int)); var m = Local("m", IrType.Map(IrType.Int, IrType.Int));
        var o = Local("o", IrType.Any);
        var py = Emit(Script(
            new IrVarDecl { Symbol = l, Init = new IrListLit() }, new IrVarDecl { Symbol = s, Init = new IrSetLit() }, new IrVarDecl { Symbol = m, Init = new IrMapLit() },
            new IrExprStmt(new IrOpCall(IrOp.Clear, N(l))), new IrExprStmt(new IrOpCall(IrOp.Clear, N(s))), new IrExprStmt(new IrOpCall(IrOp.Clear, N(m))),
            new IrVarDecl { Symbol = o, Init = IrLiteral.OfString("x") },
            Call("print", new IrCast { Target = IrType.String, Operand = N(o), IsTypeAssertion = true })));
        Assert.Contains("__jlclear(l)", py);
        Assert.Contains("__jsclear(s)", py);
        Assert.Contains("__jmclear(m)", py);
        Assert.Contains("print(o)", py);
    }

    [Fact]
    public void ClosureAssigningOuterLocal_IsADiagnostic()
    {
        var c = Local("c");
        var lam = new IrLambda { Body = B(new IrAssign { Target = N(c), Op = IrBinOp.Add, Value = I(1) }), Span = new Span(2, 5) };
        var f = new IrFunction { Symbol = new IrSymbol("f", IrSymbolKind.Function), Body = B(new IrVarDecl { Symbol = c, Init = I(0) }, Call("spawn_drone", lam)) };
        f.Symbol.Function = f;
        var r = IrEmitter.Compile(Script(f), "0123456789", new CompileOptions());
        Assert.False(r.Success);
        Assert.Contains("cannot assign the outer variable 'c'", r.Errors[0].Message);
    }

    [Fact]
    public void Errors_AreDiagnosticsNotExceptions()
    {
        var x = Local("x");
        var bad = new IrConditional { Cond = IrLiteral.OfBool(true), Then = I(1), Else = I(2), Span = new Span(3, 8) };
        var m = Script(new IrVarDecl { Symbol = x, Init = new IrBinary { Op = IrBinOp.Add, Left = I(1), Right = bad } });
        var r = IrEmitter.Compile(m, "0123456789", new CompileOptions());
        Assert.False(r.Success);
        Assert.Contains("?: is only supported as a whole value here", r.Errors[0].Message);
        Assert.Equal(3, r.Errors[0].Start);
        var r2 = IrEmitter.Compile(Script(new IrExprStmt(new IrOpCall(IrOp.ListAppend))), "", new CompileOptions());
        Assert.False(r2.Success);
    }
}

[Collection(GameApiRegistrations.Name)]
public class GameApiTests
{
    [Fact]
    public void CanonicalTable_HasTheGameBuiltins()
    {
        foreach (var n in new[] { "harvest", "can_harvest", "plant", "move", "spawn_drone", "print", "quick_print", "min", "max", "abs", "random", "get_cost", "measure" })
            Assert.NotNull(GameApi.Find(n));
        Assert.Equal("use_item(item: Item, n?: int) -> bool", GameApi.Find("use_item").Signature);
        Assert.True(GameApi.Find("get_entity_type").ReturnType.Nullable);
        Assert.Equal("doAFlip", GameApi.ApiName("do_a_flip", NamingStyle.Camel));
        Assert.Equal("DoAFlip", GameApi.ApiName("do_a_flip", NamingStyle.Pascal));
        Assert.Equal("get_pos_x", GameApi.FromApiName("GetPosX", NamingStyle.Pascal));
        Assert.Null(GameApi.ApiName("no_such_builtin", NamingStyle.Camel));
    }

    [Fact]
    public void Register_MakesTheBuiltinVisibleToJava_AndUnregisterRemovesIt()
    {
        const string name = "zz_scan_port";
        try
        {
            var b = GameApi.Register(name, "host: string, port?: int", "list<int>", "Scans ports.", "cybersecurity");
            Assert.Equal(2, b.Params.Count);
            Assert.True(b.Params[1].Optional);
            Assert.Equal(IrTypeKind.List, b.ReturnType.Kind);
            var src = "void main() {\n    List<Integer> open = zzScanPort(\"10.0.0.1\", 80);\n    List<Integer> all = zzScanPort(\"h\");\n    System.out.println(open.size() + all.size());\n}\n";
            var r = JavaCompiler.Compile(src, new CompileOptions { FileName = "Main" });
            Assert.True(r.Success, string.Join("; ", r.Errors));
            Assert.Contains("open = zz_scan_port(\"10.0.0.1\", 80)", r.Python);
            Assert.Equal("zzScanPort", GameApi.ToJava(name));
            Assert.Equal("zzScanPort", Langs.Get("java").ApiName(name));
            var bad = JavaCompiler.Compile(src.Replace("80", "\"x\""), new CompileOptions { FileName = "Main" });
            Assert.False(bad.Success);
        }
        finally { GameApi.Unregister(name); }
        Assert.Null(GameApi.Find(name));
        var gone = JavaCompiler.Compile("void main() { zzScanPort(\"h\"); }", new CompileOptions { FileName = "Main" });
        Assert.False(gone.Success);
        Assert.Contains("cannot find symbol", gone.Errors[0].Message);
        Assert.False(GameApi.Unregister("harvest"));   // base game builtins stay
    }
}

public class IrTypeTests
{
    [Theory]
    [InlineData("int", "int")]
    [InlineData("list<int>", "list<int>")]
    [InlineData("map<Item,float>?", "map<Item,float>?")]
    [InlineData("Entity|None", "Entity?")]
    [InlineData("Entity|Item", "any")]
    [InlineData("dict<str,number>", "map<string,float>")]
    [InlineData("func<int>", "func<int>")]
    public void ParseAndPrint(string text, string expected) => Assert.Equal(expected, IrType.Parse(text).ToString());

    [Fact]
    public void Join()
    {
        Assert.Equal("float", IrType.Join(IrType.Int, IrType.Float).ToString());
        Assert.Equal("string?", IrType.Join(IrType.Null, IrType.String).ToString());
        Assert.Equal("list<float>", IrType.Join(IrType.List(IrType.Int), IrType.List(IrType.Float)).ToString());
        Assert.Equal("any", IrType.Join(IrType.Bool, IrType.String).ToString());
        Assert.Equal("int", IrType.Join(IrType.Unknown, IrType.Int).ToString());
    }
}

public class RegistryAndCommentTests
{
    [Fact]
    public void Registry_DiscoversLanguagesInSpecOrder()
    {
        var all = Langs.All;
        Assert.Contains(all, l => l.Id == "java");
        Assert.Same(Langs.Get("JAVA"), Langs.Get("java"));
        var ids = all.Select(l => l.Id).ToList();
        var known = ids.Where(i => Array.IndexOf(Langs.Order, i) >= 0).ToList();
        Assert.Equal(known.OrderBy(i => Array.IndexOf(Langs.Order, i)).ToList(), known);
        Assert.Null(Langs.Get("cobol"));
    }

    [Fact]
    public void JavaToIr_AttachesComments()
    {
        var src = "// file header\nint count = 0; // the count\n\n/** Entry. */\nvoid main() {\n    // loop forever\n    while (true) {\n        harvest(); // cut\n    }\n    // tail\n}\n// end\n";
        var ir = JavaCompiler.ToIr(src, new CompileOptions { FileName = "Main" }, out var errs);
        Assert.NotNull(ir);
        var g = ir.Globals.Single();
        Assert.Equal(" file header", g.Leading.Single().Text);
        Assert.Equal(" the count", g.Trailing.Text);
        var main = ir.Entry;
        Assert.True(main.Leading.Single().IsDoc);
        var loop = (IrWhile)main.Body.Stmts[0];
        Assert.Equal(" loop forever", loop.Leading.Single().Text);
        Assert.Equal(" cut", loop.Body.Stmts[0].Trailing.Text);
        Assert.Equal(" tail", main.Body.TrailingComments.Single().Text);
        Assert.Equal(" end", ir.TrailingComments.Single().Text);
    }

    [Fact]
    public void JavaToIr_TypesAndSemanticsAreExplicit()
    {
        var src = "void main() {\n    int a = 7 / 2;\n    double d = 7 / 2.0;\n    int m = -7 % 2;\n    String s = \"n=\" + a;\n    a += 1.5;\n}\n";
        var ir = JavaCompiler.ToIr(src, new CompileOptions { FileName = "Main" }, out var errs);
        var stmts = ir.Entry.Body.Stmts;
        Assert.Equal(IrBinOp.IntDivTrunc, ((IrBinary)((IrVarDecl)stmts[0]).Init).Op);
        Assert.Equal(IrBinOp.TrueDiv, ((IrBinary)((IrVarDecl)stmts[1]).Init).Op);
        Assert.Equal(IrBinOp.ModTrunc, ((IrBinary)((IrVarDecl)stmts[2]).Init).Op);
        Assert.Equal(IrBinOp.Concat, ((IrBinary)((IrVarDecl)stmts[3]).Init).Op);
        var asg = (IrAssign)stmts[4];
        Assert.True(asg.TruncateToInt);
        Assert.Equal(IrTypeKind.Int, ((IrVarDecl)stmts[0]).Symbol.Type.Kind);
    }
}
