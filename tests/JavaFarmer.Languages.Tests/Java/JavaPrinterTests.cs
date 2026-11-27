using System.Diagnostics;
using static JavaFarmer.Languages.Tests.Java.B;

namespace JavaFarmer.Languages.Tests.Java;

/// <summary>Hand-built IR → expected Java; every full print must compile with JavaCompiler.</summary>
[Collection("perf")]
public class JavaPrinterTests
{
    static readonly ILanguage Java = Compiler.Languages.Get("java");

    static string Print(IrModule m, bool snippet = false) =>
        JavaPrinter.Print(m, new PrintOptions { Snippet = snippet, FileName = m.Name }).Replace("\r\n", "\n");

    static string PrintOk(IrModule m, string expected)
    {
        var text = JavaPrinter.Print(m, new PrintOptions { FileName = m.Name }, out var problems).Replace("\r\n", "\n");
        Assert.True(problems.Count == 0, "problems: " + string.Join("; ", problems) + "\n" + text);
        if (expected != null) Assert.Equal(expected.Replace("\r\n", "\n").Replace("    ", "\t"), text.TrimEnd('\n'));
        var r = JavaCompiler.Compile(text, new CompileOptions { FileName = m.Name });
        Assert.True(r.Success, "does not compile: " + string.Join("; ", r.Errors) + "\n" + text);
        return text;
    }

    [Fact]
    public void Registered()
    {
        Assert.IsType<JavaLanguage>(Java);
    }

    [Fact]
    public void TypedJavaModule()
    {
        var count = Global("count", IrType.Int);
        var i = Local("i", IrType.Int);
        var bump = Fn("bump", Array.Empty<IrSymbol>(), Set(N(count), I(1), IrBinOp.Add));
        bump.ReturnType = IrType.Void;
        ((IrAssign)bump.Body.Stmts[0]).IsIncDec = true;
        var loop = new IrClassicFor
        {
            Init = { new IrVarDecl { Symbol = i, Init = I(0) } },
            Cond = Bin(IrBinOp.Lt, N(i), Call("get_world_size")),
            Update = { new IrAssign { Target = N(i), Op = IrBinOp.Add, Value = I(1), IsIncDec = true } },
            Body = Blk(Do(CallFn(bump)), Stmt("move", Dir("North"))),
        };
        var m = JavaModule(new IrGlobal { Symbol = count, Init = I(0) }, bump,
            Entry(loop, Stmt("print", Bin(IrBinOp.Concat, S("count: "), N(count)))));
        PrintOk(m, @"int count = 0;

void bump() {
    count++;
}

void main() {
    for (int i = 0; i < getWorldSize(); i++) {
        bump();
        move(North);
    }
    System.out.println(""count: "" + count);
}");
    }

    [Fact]
    public void PythonScriptGetsDeclarationsAndMain()
    {
        var x = Local("x");
        var m = Script(Set(x, I(0)),
            While(T, Set(x, I(1), IrBinOp.Add), If(Bin(IrBinOp.Gt, N(x), I(3)), new IrStmt[] { new IrBreak() })),
            Stmt("print", N(x)));
        PrintOk(m, @"void main() {
    int x = 0;
    while (true) {
        x += 1;
        if (x > 3) {
            break;
        }
    }
    System.out.println(x);
}");
        Assert.Equal("int x = 0;\nwhile (true) {\n\tx += 1;\n\tif (x > 3) {\n\t\tbreak;\n\t}\n}\nSystem.out.println(x);\n", Print(m, true));
    }

    [Fact]
    public void ParameterTypesComeFromCallSites()
    {
        var n = new IrSymbol("n", IrSymbolKind.Param);
        var dir = new IrSymbol("dir", IrSymbolKind.Param);
        var i = Local("i");
        var f = Fn("move_n_dir", new[] { n, dir }, Range(i, N(n), Stmt("move", N(dir))));
        var m = Script(f, Do(CallFn(f, I(10), Dir("North"))), Do(CallFn(f, I(2), Dir("West"))));
        PrintOk(m, @"void moveNDir(int n, Direction dir) {
    for (int i = 0; i < n; i++) {
        move(dir);
    }
}

void main() {
    moveNDir(10, North);
    moveNDir(2, West);
}");
    }

    [Fact]
    public void ReturnTypesAndDefaultArguments()
    {
        var a = new IrSymbol("a", IrSymbolKind.Param);
        var b = new IrSymbol("b", IrSymbolKind.Param);
        var xor = Fn("xor", new[] { a, b }, Ret(Bin(IrBinOp.Ne, N(a), N(b))));
        var flag = new IrSymbol("flag", IrSymbolKind.Param);
        var f = Fn("f", new[] { flag }, If(N(flag), new IrStmt[] { Stmt("do_a_flip") }));
        f.Params[0].Default = Fa;
        var m = Script(xor, f, If(CallFn(xor, T, Fa), new IrStmt[] { Do(CallFn(f)) }), Do(CallFn(f, T)));
        PrintOk(m, @"boolean xor(boolean a, boolean b) {
    return a != b;
}

void f(boolean flag) {
    if (flag) {
        doAFlip();
    }
}

void main() {
    if (xor(true, false)) {
        f(false);
    }
    f(true);
}");
    }

    [Fact]
    public void ListsFromAppendsAndIntegerRemove()
    {
        var l = Local("list");
        var total = Local("total");
        var v = Local("v");
        var m = Script(Set(l, List()), Do(Op(IrOp.ListAppend, N(l), I(5))), Do(Op(IrOp.ListAppend, N(l), I(7))),
            Do(Op(IrOp.RemoveValue, N(l), I(5))), Set(total, I(0)), Each(v, N(l), Set(total, N(v), IrBinOp.Add)),
            Stmt("print", Op(IrOp.Len, N(l)), N(total)), Do(Op(IrOp.ListPop, N(l))));
        PrintOk(m, @"void main() {
    List<Integer> list = new ArrayList<>();
    list.add(5);
    list.add(7);
    list.remove((Integer) 5);
    int total = 0;
    for (int v : list) {
        total += v;
    }
    System.out.println(list.size() + "" "" + total);
    list.remove(list.size() - 1);
}");
    }

    [Fact]
    public void ImmutableAndMutableLiterals()
    {
        var a = Local("a"); var b = Local("b"); var x = Local("x");
        var m = Script(Set(a, List(I(4), I(7))), Set(b, List(F(1.5), I(2))), Do(Op(IrOp.ListAppend, N(b), I(3))),
            Set(x, new IrIndex { Target = N(a), Index = I(1) }), Stmt("print", N(x), new IrIndex { Target = N(b), Index = Neg(I(1)) }));
        PrintOk(m, @"void main() {
    List<Integer> a = List.of(4, 7);
    List<Double> b = new ArrayList<>(List.of(1.5, 2.0));
    b.add(3.0);
    int x = a.get(1);
    System.out.println(x + "" "" + b.get(b.size() - 1));
}");
    }

    [Fact]
    public void Arithmetic()
    {
        var a = Local("a"); var b = Local("b");
        var m = Script(Set(a, Neg(I(7))), Set(b, I(2)),
            Stmt("print", Bin(IrBinOp.IntDivFloor, N(a), N(b))),
            Stmt("print", Bin(IrBinOp.ModFloor, N(a), N(b))),
            Stmt("print", Bin(IrBinOp.TrueDiv, N(a), N(b))),
            Stmt("print", Bin(IrBinOp.IntDivFloor, I(7), I(2))),
            Stmt("print", Bin(IrBinOp.Pow, N(b), I(3))),
            Stmt("print", Bin(IrBinOp.Concat, N(a), N(b))),
            Stmt("print", Bin(IrBinOp.Mul, Bin(IrBinOp.Add, N(a), N(b)), I(2))),
            Stmt("print", Bin(IrBinOp.Sub, N(a), Bin(IrBinOp.Sub, N(b), I(1)))));
        PrintOk(m, @"void main() {
    int a = -7;
    int b = 2;
    System.out.println(Math.floorDiv(a, b));
    System.out.println(Math.floorMod(a, b));
    System.out.println((double) a / b);
    System.out.println(7 / 2);
    System.out.println((int) Math.pow(b, 3));
    System.out.println("""" + a + b);
    System.out.println((a + b) * 2);
    System.out.println(a - (b - 1));
}");
    }

    [Fact]
    public void TupleUnpacking()
    {
        var a = Local("a"); var b = Local("b");
        var px = Local("x"); var py = Local("y");
        var measure = Call("measure");
        var m = Script(new IrAssign { Target = Tuple(N(a), N(b)), Value = Tuple(I(1), I(2)) },
            new IrAssign { Target = Tuple(N(a), N(b)), Value = Tuple(N(b), N(a)) },
            new IrAssign { Target = Tuple(N(px), N(py)), Value = new IrCast { Target = IrType.Game("Position"), Operand = measure, IsTypeAssertion = true } },
            Stmt("print", N(a), N(b), N(px), N(py)));
        PrintOk(m, @"void main() {
    int a = 1;
    int b = 2;
    int aNew = b;
    int bNew = a;
    a = aNew;
    b = bNew;
    Position pos = (Position) measure();
    int x = pos.x();
    int y = pos.y();
    System.out.println(a + "" "" + b + "" "" + x + "" "" + y);
}");
    }

    [Fact]
    public void ReservedWordsAndSnakeCaseAreRenamedConsistently()
    {
        var nw = Local("new"); var cls = Local("my_class");
        var m = Script(Set(nw, I(1)), Set(cls, Bin(IrBinOp.Add, N(nw), I(1))), Stmt("print", N(nw), N(cls)));
        PrintOk(m, @"void main() {
    int new_ = 1;
    int myClass = new_ + 1;
    System.out.println(new_ + "" "" + myClass);
}");
        // Java sources keep their names
        var x = Local("my_var", IrType.Int);
        var jm = JavaModule(Entry(new IrVarDecl { Symbol = x, Init = I(3) }, Stmt("print", N(x))));
        Assert.Contains("int my_var = 3;", PrintOk(jm, null));
    }

    [Fact]
    public void SwitchWithLoopBreakBecomesIfChain()
    {
        var i = Local("i", IrType.Int);
        var sw = new IrSwitch { Selector = Bin(IrBinOp.ModTrunc, N(i), I(3)) };
        sw.Cases.Add(new IrCase { Labels = { I(0) }, Body = Blk(Stmt("harvest")) });
        sw.Cases.Add(new IrCase { Labels = { I(1), I(2) }, Body = Blk(new IrBreak()) });
        sw.Cases.Add(new IrCase { IsDefault = true, Body = Blk(Stmt("till")) });
        var loop = new IrWhile { Cond = T, Body = Blk(new IrVarDecl { Symbol = i, Init = Call("get_pos_x") }, sw) };
        var text = PrintOk(JavaModule(Entry(loop)), null);
        Assert.Contains("int selector = i % 3;", text);
        Assert.Contains("if (selector == 0) {", text);
        Assert.Contains("} else if (selector == 1 || selector == 2) {", text);
        // without a break: an arrow switch
        var sw2 = new IrSwitch { Selector = N(i) };
        sw2.Cases.Add(new IrCase { Labels = { I(0) }, Body = Blk(Stmt("harvest")) });
        sw2.Cases.Add(new IrCase { IsDefault = true, Body = Blk(Stmt("till"), Stmt("harvest")) });
        var t2 = PrintOk(JavaModule(Entry(new IrVarDecl { Symbol = i, Init = I(1) }, sw2)), null);
        Assert.Contains("switch (i) {\n\t\tcase 0 -> harvest();\n\t\tdefault -> {\n\t\t\ttill();\n\t\t\tharvest();\n\t\t}\n\t}", t2);
    }

    [Fact]
    public void RangeForms()
    {
        var i = Local("i"); var j = Local("j");
        var down = new IrRangeFor { Var = i, Start = I(10), End = I(0), Step = Neg(I(2)), Body = Blk(Stmt("print", N(i))) };
        var incl = new IrRangeFor { Var = j, Start = I(1), End = I(3), Inclusive = true, Body = Blk(Stmt("print", N(j))) };
        PrintOk(Script(down, incl), @"void main() {
    for (int i = 10; i > 0; i -= 2) {
        System.out.println(i);
    }
    for (int j = 1; j <= 3; j++) {
        System.out.println(j);
    }
}");
    }

    [Fact]
    public void MapsAndForEachWithValue()
    {
        var d = Local("d"); var k = Local("k"); var v = Local("v");
        var lit = new IrMapLit(); lit.Keys.Add(S("hay")); lit.Values.Add(I(1));
        var fe = new IrForEach { Var = k, ValueVar = v, Iterable = N(d), Body = Blk(Stmt("print", N(k), N(v))) };
        var m = Script(Set(d, lit), Set(new IrIndex { Target = N(d), Index = S("wood") }, I(2)),
            Set(new IrIndex { Target = N(d), Index = S("hay") }, I(5), IrBinOp.Add),
            If(Op(IrOp.Contains, N(d), S("wood")), new IrStmt[] { Do(Op(IrOp.MapRemove, N(d), S("wood"))) }), fe);
        PrintOk(m, @"void main() {
    Map<String, Integer> d = new HashMap<>(Map.of(""hay"", 1));
    d.put(""wood"", 2);
    d.put(""hay"", d.get(""hay"") + 5);
    if (d.containsKey(""wood"")) {
        d.remove(""wood"");
    }
    for (String k : d.keySet()) {
        int v = d.get(k);
        System.out.println(k + "" "" + v);
    }
}");
    }

    [Fact]
    public void LambdasAndFunctionValues()
    {
        var work = Fn("work", Array.Empty<IrSymbol>(), Stmt("harvest"));
        var lam = new IrLambda { Body = Blk(Stmt("till"), Stmt("harvest")) };
        var m = Script(work, Do(Call("spawn_drone", new IrFuncRef { Function = work.Symbol })), Do(Call("spawn_drone", lam)),
            Do(Call("spawn_drone", new IrFuncRef { Builtin = "harvest" })), Do(Call("spawn_drone", N(work.Symbol))));
        PrintOk(m, @"void work() {
    harvest();
}

void main() {
    spawnDrone(this::work);
    spawnDrone(() -> {
        till();
        harvest();
    });
    spawnDrone(() -> harvest());
    spawnDrone(this::work);
}");
    }

    [Fact]
    public void CommentsArePreserved()
    {
        var x = Local("x");
        var s1 = Set(x, I(1)); s1.AddLeading(new IrComment(" first")); s1.Trailing = new IrComment(" one");
        var s2 = Stmt("print", N(x)); s2.AddLeading(new IrComment(" block\n   more ", true));
        var m = Script(s1, s2);
        m.TrailingComments = new List<IrComment> { new(" end") };
        PrintOk(m, "void main() {\n    // first\n    int x = 1; // one\n    /* block\n       more */\n    System.out.println(x);\n}\n// end");
    }

    [Fact]
    public void GlobalsUsedByFunctionsBecomeFields()
    {
        var count = Global("count");
        var inc = Fn("inc", Array.Empty<IrSymbol>(), Set(count, I(1), IrBinOp.Add));
        var m = Script(Set(count, I(0)), inc, Do(CallFn(inc)), Stmt("print", N(count)));
        PrintOk(m, @"int count = 0;

void inc() {
    count += 1;
}

void main() {
    inc();
    System.out.println(count);
}");
    }

    [Fact]
    public void TruthinessAndNone()
    {
        var l = Local("l"); var e = Local("e");
        var m = Script(Set(l, List(I(1))), Set(e, Call("get_entity_type")),
            If(Bin(IrBinOp.And, N(l), Not(Bin(IrBinOp.Eq, N(e), Null))), new IrStmt[] { Stmt("harvest") }),
            While(Op(IrOp.Len, N(l)), Do(Op(IrOp.ListPop, N(l)))));
        PrintOk(m, @"void main() {
    List<Integer> l = new ArrayList<>(List.of(1));
    Entity e = getEntityType();
    if (!l.isEmpty() && !(e == null)) {
        harvest();
    }
    while (l.size() != 0) {
        l.remove(l.size() - 1);
    }
}");
    }

    [Fact]
    public void SnippetModeAndLoneExpressions()
    {
        Assert.Equal("true\n", Print(Script(Do(T)), true));
        Assert.Equal("canHarvest();\n", Print(Script(Stmt("can_harvest")), true));
        Assert.Equal("Items.Hay\n", Print(Script(Do(C("Items", "Hay"))), true));
        var f = Fn("f", Array.Empty<IrSymbol>(), Stmt("do_a_flip"));
        // functions are still declarations; their callers go into main
        Assert.Equal("void f() {\n\tdoAFlip();\n}\n\nvoid main() {\n\tf();\n}\n", Print(Script(f, Do(CallFn(f))), true));
        // a typed module with only main: its body
        Assert.Equal("harvest();\n", Print(JavaModule(Entry(Stmt("harvest"))), true));
    }

    [Fact]
    public void UnsupportedConstructsAreReported()
    {
        var t = Local("t");
        JavaPrinter.Print(Script(Set(t, Tuple(I(1), I(2)))), new PrintOptions(), out var p1);
        Assert.Contains(p1, x => x.Contains("tuple"));
        var g = new IrSymbol("g", IrSymbolKind.Param);
        var f = Fn("f", new[] { g }, Do(new IrCall { Kind = IrCallKind.Value, Callee = N(g), Args = { I(1) } }));
        JavaPrinter.Print(Script(f, Do(CallFn(f, new IrFuncRef { Builtin = "move" }))), new PrintOptions(), out var p2);
        Assert.NotEmpty(p2);
    }

    [Fact]
    public void NeverThrows()
    {
        var weird = new IrModule();
        weird.Items.Add(new IrExprStmt(null));
        weird.Items.Add(new IrAssign());
        weird.Items.Add(new IrIf());
        weird.Items.Add(new IrForEach());
        weird.Items.Add(new IrFunction());
        weird.Items.Add(new IrGlobal());
        var text = Java.Print(weird, new PrintOptions());
        Assert.NotNull(text);
        Assert.NotNull(Java.Print(null, null));
    }

    [Fact]
    public void PrintIsFast()
    {
        var dir = Path.Combine(RepoPaths.Corpus("java"), "diff");
        Skip.IfNot(Directory.Exists(dir));
        var src = File.ReadAllText(Path.Combine(dir, "Algorithms.java"));
        var ir = Java.ToIr(src, new CompileOptions { FileName = "Algorithms" }, out _);
        Skip.If(ir == null, "Java ToIr not available");
        for (int k = 0; k < 30; k++) Java.Print(ir, new PrintOptions());
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 100; k++) Java.Print(ir, new PrintOptions());
        double perLine = sw.Elapsed.TotalMilliseconds / 100 / Math.Max(1, src.Split('\n').Length) * 300;
        Assert.True(perLine < 5, "printing 300 lines takes " + perLine.ToString("0.00") + " ms");
    }

    [Fact]
    public void Deterministic()
    {
        var dir = Path.Combine(RepoPaths.Corpus("java"), "valid");
        Skip.IfNot(Directory.Exists(dir));
        foreach (var f in Directory.GetFiles(dir, "*.java"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var ir = Java.ToIr(File.ReadAllText(f), new CompileOptions { FileName = name }, out _);
            if (ir == null) continue;
            Assert.Equal(Java.Print(ir, new PrintOptions { FileName = name }), Java.Print(ir, new PrintOptions { FileName = name }));
        }
    }
}
