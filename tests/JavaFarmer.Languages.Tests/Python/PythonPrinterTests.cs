namespace JavaFarmer.Languages.Tests.Python;

/// <summary>PythonPrinter: textual round trips of clean code, structural round trips, IR from other languages.</summary>
public class PythonPrinterTests
{
    public static readonly string[] Clean =
    {
        "# Harvest the whole farm.\nsize = get_world_size()\n\ndef harvest_column():\n\tfor _ in range(size):\n\t\tif can_harvest():\n\t\t\tharvest()\n\t\tmove(North)\n\nwhile True:\n\tfor i in range(size):\n\t\tharvest_column()\n\t\tmove(East)\n",
        "count = 0\ncosts = {Items.Hay: 5, Items.Wood: 2}\n\ndef increment(n = 1):\n\tglobal count\n\tcount += n\n\treturn count\n\nfor key in costs:\n\tvalue = costs[key]\n\tif value > 3 and key != Items.Wood:\n\t\tincrement(value) # big ones\n\telif value == 2:\n\t\tincrement()\n\telse:\n\t\tpass\nx, y = get_pos_x(), get_pos_y()\npos = (x, y)\nprint(pos[0], count)\n",
        "def for_all(f):\n\tdef row():\n\t\tfor _ in range(get_world_size() - 1):\n\t\t\tf()\n\t\t\tmove(East)\n\t\tf()\n\tfor _ in range(get_world_size()):\n\t\tif not spawn_drone(row):\n\t\t\trow()\n\t\tmove(North)\n\ndef in_range(a, b, c):\n\treturn a <= b < c\n\nseen = set()\nif 3 not in seen and in_range(0, get_pos_x(), 5):\n\tseen.add(3)\n\tquick_print(\"it's\", 'say \"hi\"')\nfor_all(harvest)\n",
        "nums = [4, 7, 2, 5]\ntotal = 0\nfor number in nums:\n\ttotal += number\nnums.append(-total)\nnums.remove(7)\nnums.insert(1, 2 ** 3)\nlast = nums.pop(0)\nneg = -(1 + 2) * 3\ni = 0\nwhile i < len(nums) and not nums[i] == 0:\n\ti += 1\nprint(total // 3, total % 4, total / 2, last, neg)\n",
        "import utils\nfrom tools import helper\n\ndef main():\n\ta = \"left\" + str(utils.size)\n\tb = utils.go(helper(a), 1.5)\n\tif __name__ == \"__main__\":\n\t\treturn [a, b]\n\treturn None\n\ngrid = {}\nfor x in range(3):\n\tfor y in range(0, 3, 1):\n\t\tgrid[(x, y)] = x * y\nwhile len(grid) > 0:\n\tgrid.pop((0, 0))\n\tbreak\n# done\n",
        "def task():\n\tmove(North)\n\treturn get_entity_type()\n\ndrone = spawn_drone(task)\nif drone != None:\n\tprint(wait_for(drone))\nelse:\n\t# no free drone\n\ttask()\n",
    };

    [Fact]
    public void CleanPrograms_PrintBackIdentically()
    {
        for (int i = 0; i < Clean.Length; i++)
            Assert.Equal(Clean[i], PyTestUtil.RoundTrip(Clean[i]));
    }

    [Fact]
    public void CleanPrograms_GameParserAcceptsThem()
    {
        foreach (var src in Clean)
            Assert.True(GameOracle.TryParse(src) == null, GameOracle.TryParse(src) + "\n" + src);
    }

    [Fact]
    public void Reprint_IsStructurallyEqual()
    {
        var progs = Clean.Concat(new[]
        {
            "x = 2 ** 3 ** 2\ny = 2 ** (3 ** 2)\nz = (2 ** 3) ** 2\n",
            "t = 1,\nu = ()\nv = 1, 2\nprint(t, u, v)\n",
            "s = 'say \"hi\"' + \"it's\"\n",
            "for a, (b, c) in pairs:\n\tprint(a, b, c)\n",
            "if a:\n\tpass\nelif b:\n\t# nothing\n\tpass\n",
            "x = not (a and b) or c\ny = (a or b) and c\nz = -x ** 2\nw = (-x) ** 2\n",
        });
        foreach (var src in progs)
        {
            var m1 = PyTestUtil.ToIr(src);
            var printed = PyTestUtil.Print(m1);
            var m2 = PyTestUtil.ToIr(printed);
            Assert.True(PyTestUtil.Dump(m1) == PyTestUtil.Dump(m2), "source:\n" + src + "\nprinted:\n" + printed + "\n" + PyTestUtil.Dump(m1) + "---\n" + PyTestUtil.Dump(m2));
        }
    }

    [Fact]
    public void Pow_PrintsWithTheGamesAssociativity()
    {
        Assert.Equal("x = 2 ** 3 ** 2\n", PyTestUtil.RoundTrip("x = (2 ** 3) ** 2\n"));
        Assert.Equal("x = 2 ** (3 ** 2)\n", PyTestUtil.RoundTrip("x = 2 ** (3 ** 2)\n"));
    }

    [Fact]
    public void Conditional_BecomesIfElse()
    {
        Assert.Equal("if b:\n\tx = 1\nelse:\n\tx = 2\nprint(x)\n", PyTestUtil.RoundTrip("x = 1 if b else 2\nprint(x)\n"));
        Assert.Equal("if b:\n\tvalue = 1\nelse:\n\tvalue = 2\nprint(value + 3)\n", PyTestUtil.RoundTrip("print((1 if b else 2) + 3)\n"));
    }

    [Fact]
    public void SpacesIndent_Option()
    {
        var m = PyTestUtil.ToIr("if a:\n\tharvest()\n");
        Assert.Equal("if a:\n    harvest()\n", PyTestUtil.Print(m, new PrintOptions { Indent = "    " }));
    }

    // ------------------------------------------------------------------ IR from other languages
    static IrSymbol L(string n, IrType t = null) => new IrSymbol(n, IrSymbolKind.Local, t ?? IrType.Int);
    static IrName N(IrSymbol s) => new IrName(s);
    static IrLiteral I(long v) => IrLiteral.OfInt(v);
    static IrBlock B(params IrStmt[] s) => new IrBlock(s);

    static IrModule JavaLike(params IrStmt[] mainBody)
    {
        var m = new IrModule { Name = "Main", SourceLanguage = "java" };
        var main = new IrFunction { Symbol = new IrSymbol("main", IrSymbolKind.Function), IsEntry = true, ReturnType = IrType.Void, Body = B(mainBody) };
        main.Symbol.Function = main;
        m.Items.Add(main);
        return m;
    }

    static string P(IrModule m)
    {
        var py = PyTestUtil.Print(m);
        Assert.True(GameOracle.TryParse(py) == null, GameOracle.TryParse(py) + "\n" + py);
        Assert.NotNull(PyTestUtil.Lang.ToIr(py, new CompileOptions { FileName = "main" }, out _));
        return py;
    }

    [Fact]
    public void Entry_BodyPrintsAtTopLevel()
    {
        var i = L("i");
        var py = P(JavaLike(new IrVarDecl { Symbol = i, Init = I(3) }, new IrExprStmt(IrCall.ToBuiltin("print", N(i)))));
        Assert.Equal("i = 3\nprint(i)\n", py);
    }

    [Fact]
    public void Entry_WithReturn_KeepsDefMain()
    {
        var py = P(JavaLike(new IrIf { Cond = IrLiteral.OfBool(true), Then = B(new IrReturn()) }, new IrExprStmt(IrCall.ToBuiltin("harvest"))));
        Assert.Equal("def main():\n\tif True:\n\t\treturn\n\tharvest()\n\nmain()\n", py);
    }

    [Fact]
    public void ClassicFor_CountingLoopIsARange_OtherwiseWhile()
    {
        var i = L("i");
        var counting = new IrClassicFor
        {
            Init = { new IrVarDecl { Symbol = i, Init = I(0) } },
            Cond = new IrBinary { Op = IrBinOp.Lt, Left = N(i), Right = IrCall.ToBuiltin("get_world_size"), Type = IrType.Bool },
            Update = { new IrAssign { Target = N(i), Op = IrBinOp.Add, Value = I(1), IsIncDec = true } },
            Body = B(new IrExprStmt(IrCall.ToBuiltin("move", new IrConst("Directions", "North")))),
        };
        Assert.Equal("for i in range(get_world_size()):\n\tmove(North)\n", P(JavaLike(counting)));

        var j = L("j");
        var general = new IrClassicFor
        {
            Init = { new IrVarDecl { Symbol = j, Init = I(1) } },
            Cond = new IrBinary { Op = IrBinOp.Lt, Left = N(j), Right = I(100) },
            Update = { new IrAssign { Target = N(j), Op = IrBinOp.Mul, Value = I(2) } },
            Body = B(new IrIf { Cond = new IrBinary { Op = IrBinOp.Eq, Left = N(j), Right = I(8) }, Then = B(new IrContinue()) }, new IrExprStmt(IrCall.ToBuiltin("print", N(j)))),
        };
        Assert.Equal("j = 1\nwhile j < 100:\n\tif j == 8:\n\t\tj *= 2\n\t\tcontinue\n\tprint(j)\n\tj *= 2\n", P(JavaLike(general)));
    }

    [Fact]
    public void DoWhile_Switch_AndLoop()
    {
        var n = L("n");
        var dw = new IrDoWhile { Body = B(new IrAssign { Target = N(n), Op = IrBinOp.Sub, Value = I(1) }), Cond = new IrBinary { Op = IrBinOp.Gt, Left = N(n), Right = I(0) } };
        var sw = new IrSwitch
        {
            Selector = new IrBinary { Op = IrBinOp.ModFloor, Left = N(n), Right = I(3) },
            Cases =
            {
                new IrCase { Labels = { I(0), I(1) }, Body = B(new IrExprStmt(IrCall.ToBuiltin("harvest"))) },
                new IrCase { IsDefault = true, Body = B(new IrExprStmt(IrCall.ToBuiltin("till"))) },
            },
        };
        var py = P(JavaLike(new IrVarDecl { Symbol = n, Init = I(5) }, dw, sw, new IrLoop { Body = B(new IrBreak()) }));
        Assert.Equal("n = 5\nwhile True:\n\tn -= 1\n\tif not n > 0:\n\t\tbreak\nselected = n % 3\nif selected == 0 or selected == 1:\n\tharvest()\nelse:\n\ttill()\nwhile True:\n\tbreak\n", py);
    }

    [Fact]
    public void Lambdas_BecomeNestedDefs()
    {
        var lam = new IrLambda { Body = B(new IrExprStmt(IrCall.ToBuiltin("do_a_flip"))) };
        var py = P(JavaLike(new IrExprStmt(IrCall.ToBuiltin("spawn_drone", lam))));
        Assert.Equal("def task():\n\tdo_a_flip()\nspawn_drone(task)\n", py);
    }

    [Fact]
    public void JavaOnlyOps_UseHelpers()
    {
        var m = L("m", IrType.Map(IrType.String, IrType.Int));
        var l = L("l", IrType.List(IrType.Int));
        var d = L("d", IrType.Float);
        var py = P(JavaLike(
            new IrVarDecl { Symbol = m, Init = new IrMapLit { Type = m.Type } },
            new IrVarDecl { Symbol = l, Init = new IrListLit { Type = l.Type } },
            new IrVarDecl { Symbol = d, Init = IrLiteral.OfFloat(-2.5) },
            new IrExprStmt(IrCall.ToBuiltin("print",
                new IrOpCall(IrOp.MapGetOrDefault, N(m), IrLiteral.OfString("a"), I(0)) { Type = IrType.Int },
                new IrOpCall(IrOp.ListIndexOf, N(l), I(3)) { Type = IrType.Int },
                new IrCast { Target = IrType.Int, Operand = N(d), Type = IrType.Int },
                new IrBinary { Op = IrBinOp.IntDivTrunc, Left = I(-7), Right = I(2), Type = IrType.Int })),
            new IrExprStmt(new IrOpCall(IrOp.RemoveValueIfPresent, N(l), I(3))),
            new IrExprStmt(new IrOpCall(IrOp.Clear, N(m))),
            new IrExprStmt(new IrOpCall(IrOp.MapPut, N(m), IrLiteral.OfString("b"), I(2)))));
        Assert.Contains("def get_or_default(m, k, d):", py);
        Assert.Contains("def index_of(l, x):", py);
        Assert.Contains("def trunc(x):", py);
        Assert.Contains("def int_div(a, b):", py);
        Assert.Contains("def clear_dict(m):", py);
        Assert.Contains("print(get_or_default(m, \"a\", 0), index_of(l, 3), trunc(d), int_div(-7, 2))", py);
        Assert.Contains("remove_if_present(l, 3)\n", py);
        Assert.Contains("m[\"b\"] = 2\n", py);
        Assert.DoesNotContain("__j", py);
    }

    [Fact]
    public void ReservedNames_AreRenamed()
    {
        var lst = L("list", IrType.List(IrType.Int));
        var lam = L("lambda");
        var py = P(JavaLike(
            new IrVarDecl { Symbol = lst, Init = new IrListLit { Type = lst.Type } },
            new IrVarDecl { Symbol = lam, Init = new IrOpCall(IrOp.Len, N(lst)) { Type = IrType.Int } },
            new IrExprStmt(IrCall.ToBuiltin("print", N(lam)))));
        Assert.Equal("list_ = []\nlambda_ = len(list_)\nprint(lambda_)\n", py);
    }

    [Fact]
    public void JavaCorpus_PrintsAsGamePython()
    {
        var java = JavaFarmer.Compiler.Languages.Get("java");
        if (java == null) return;
        var dir = Path.Combine(RepoPaths.Corpus("java"), "valid");
        if (!Directory.Exists(dir)) return;
        var failures = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.java"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var ir = java.ToIr(File.ReadAllText(f), new CompileOptions { FileName = name }, out var errs);
            if (ir == null) continue;   // the Java frontend's business
            var py = PyTestUtil.Lang.Print(ir, new PrintOptions { FileName = name });
            var oracle = GameOracle.TryParse(py);
            if (oracle != null) failures.Add(name + ": game parser: " + oracle + "\n" + py);
            else if (PyTestUtil.Lang.ToIr(py, new CompileOptions { FileName = name }, out var perrs) == null)
                failures.Add(name + ": python frontend: " + string.Join("; ", perrs) + "\n" + py);
        }
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }
}
