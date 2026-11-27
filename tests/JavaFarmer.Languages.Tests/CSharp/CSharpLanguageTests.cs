using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.CSharp;

/// <summary>CSharpLanguage: identity, editor services, naming, values, diagnostics, printer, unlock fairness, speed, robustness.</summary>
[Collection("perf")]
public class CSharpLanguageTests
{
    readonly ITestOutputHelper output;
    public CSharpLanguageTests(ITestOutputHelper output) { this.output = output; }

    static readonly ILanguage Cs = Compiler.Languages.Get("csharp");
    static CompileOptions Opts(string name = "main", Dictionary<string, string> others = null) => new() { FileName = name, OtherFiles = others };

    static CompileResult Ok(string src, string name = "main", Dictionary<string, string> others = null)
    {
        var r = Cs.Compile(src, Opts(name, others));
        Assert.True(r.Success, string.Join("; ", r.Errors.Select(e => e + " [" + HarnessTests.Slice(src, e.Start, e.End) + "]")));
        Assert.Null(GameOracle.TryParse(r.Python));
        return r;
    }

    static Diagnostic Fail(string src, string name = "main")
    {
        var r = Cs.Compile(src, Opts(name));
        Assert.False(r.Success, "expected an error:\n" + r.Python);
        return r.Errors[0];
    }

    // ------------------------------------------------------------------ identity / editor
    [Fact]
    public void Identity()
    {
        Assert.Equal("csharp", Cs.Id);
        Assert.Equal("C#", Cs.DisplayName);
        Assert.Equal(".cs", Cs.FileExtension);
        Assert.False(Cs.IsNative);
        Assert.IsType<CSharpLanguage>(Cs);
    }

    [Fact]
    public void EditorRulesAndTemplate()
    {
        var e = Cs.Editor;
        Assert.Equal(new[] { "{" }, e.IndentAfter);
        Assert.Equal(new[] { "}" }, e.DedentOn);
        Assert.Equal("//", e.LineComment);
        Assert.Equal("\t", e.IndentUnit);
        var tpl = Cs.NewFileTemplate("main", out int caret);
        Assert.Equal(tpl.Length, caret);
        Assert.StartsWith("//", tpl);
        Ok(tpl);
        Ok(tpl + "Harvest();\n");
    }

    [Theory]
    [InlineData("can_harvest", "CanHarvest")]
    [InlineData("do_a_flip", "DoAFlip")]
    [InlineData("get_pos_x", "GetPosX")]
    [InlineData("spawn_drone", "SpawnDrone")]
    [InlineData("print", "Console.WriteLine")]
    [InlineData("min", "Math.Min")]
    [InlineData("max", "Math.Max")]
    [InlineData("abs", "Math.Abs")]
    [InlineData("random", "Random")]
    [InlineData("quick_print", "QuickPrint")]
    public void ApiNames(string py, string cs)
    {
        Assert.Equal(cs, Cs.ApiName(py));
        Assert.Equal(py, Cs.FromApiName(cs));
    }

    [Fact]
    public void ApiNamesOtherSpellings()
    {
        Assert.Null(Cs.ApiName("no_such_builtin"));
        Assert.Null(Cs.FromApiName("NoSuchBuiltin"));
        Assert.Null(Cs.ApiName(null));
        Assert.Equal("print", Cs.FromApiName("Console.Write"));
        Assert.Equal("harvest", Cs.FromApiName("Farm.Harvest"));
        Assert.Equal("min", Cs.FromApiName("Min"));
        Assert.Null(Cs.FromApiName("canHarvest"));
    }

    [Fact]
    public void ApiNamesOfRegisteredBuiltins()
    {
        GameApi.Register("train_model_cs", "epochs: int", "float", "Trains.", extension: "aiml");
        try
        {
            Assert.Equal("TrainModelCs", Cs.ApiName("train_model_cs"));
            Assert.Equal("train_model_cs", Cs.FromApiName("TrainModelCs"));
            var r = Ok("double loss = TrainModelCs(3);\nConsole.WriteLine(loss);\n");
            Assert.Contains("train_model_cs(3)", r.Python);
            Assert.Contains("CS7036: There is no argument given that corresponds to the required parameter 'epochs'", Fail("TrainModelCs();\n").Message);
            Assert.Contains("CS1501: No overload for method 'TrainModelCs' takes 2 arguments", Fail("TrainModelCs(1, 2);\n").Message);
        }
        finally { GameApi.Unregister("train_model_cs"); }
        Assert.Contains("CS0103", Fail("TrainModelCs(3);\n").Message);
    }

    [Fact]
    public void FormatValueLikeCSharp()
    {
        string F(object v) => Cs.FormatValue(v);
        Assert.Equal("null", F(null));
        Assert.Equal("True", F(true));
        Assert.Equal("False", F(false));
        Assert.Equal("2", F(2.0));
        Assert.Equal("1.5", F(1.5));
        Assert.Equal("0.3333333333333333", F(1.0 / 3));
        Assert.Equal("-7", F(-7.0));
        Assert.Equal("hay", F("hay"));
        Assert.Equal("[1, 2, 3]", F(new GameList(new object[] { 1.0, 2.0, 3.0 })));
        Assert.Equal("[\"a\", null, True]", F(new GameList(new object[] { "a", null, true })));
        Assert.Equal("{\"a\": 1, \"b\": 2}", F(new GameDict().Add("a", 1.0).Add("b", 2.0)));
        Assert.Equal("(1, 2)", F(new GameTuple(1.0, 2.0)));
        Assert.Equal("(Entities.Tree, (1, 2))", F(new GameTuple(new GameConstant("Entities", "Tree"), new GameTuple(1.0, 2.0))));
        Assert.Equal("Drone 3", F(new GameDrone(3)));
        Assert.Equal("CanHarvest", F(new GameFunction("can_harvest")));
        Assert.Equal("Console.WriteLine", F(new GameFunction("print")));
        Assert.Equal("lambda", F(new GameFunction("__jlambda2")));
        Assert.Equal("MyMethod", F(new GameFunction("MyMethod")));
        Assert.Equal("Utils", F(new GameModule("Utils")));
        Assert.Equal("Entities.Carrot", F(new GameConstant("Entities", "Carrot")));
        Assert.Equal("North", F(new GameConstant("Directions", "North")));
    }

    [Fact]
    public void KeywordsAndHighlight()
    {
        Assert.Contains("while", Cs.Keywords);
        Assert.Contains("foreach", Cs.Keywords);
        Assert.Contains("var", Cs.Keywords);
        Assert.DoesNotContain("int", Cs.Keywords);
        var src = "int x = 5; // hi\nMove(North);\nvar s = $\"a{x}\";\nPlant(Entities.Carrot);";
        var hl = Cs.Highlight(src);
        Assert.Equal(src.Length, hl.Sum(t => t.Length));
        Assert.Equal(TokenKind.Type, hl[0].Kind);
        string KindOf(string word) { int i = src.IndexOf(word, StringComparison.Ordinal); return hl.First(t => t.Start == i).Kind.ToString(); }
        Assert.Equal("Comment", KindOf("// hi"));
        Assert.Equal("Constant", KindOf("North"));
        Assert.Equal("Constant", KindOf("Carrot"));
        Assert.Equal("String", KindOf("$\"a{x}\""));
        Assert.Equal("Number", KindOf("5"));
    }

    // ------------------------------------------------------------------ language
    [Fact]
    public void TopLevelStatements_LocalFunctionsComeFirst_AndGlobalsGetGlobalLines()
    {
        var r = Ok("int count = 0;\nwhile (count < 3)\n{\n\tBump();\n}\nvoid Bump() { count++; }\n");
        Assert.Equal("def Bump():\n    global count\n    count += 1\ncount = 0\nwhile count < 3:\n    Bump()", r.Python.Trim());
    }

    [Fact]
    public void ClassForm_MainIsTheEntry()
    {
        var src = "class Program\n{\n\tconst int N = 2;\n\n\tstatic void Main()\n\t{\n\t\tfor (int i = 0; i < N; i++) Harvest();\n\t}\n}\n";
        var r = Ok(src);
        Assert.Contains("def Main():", r.Python);
        Assert.Contains("if __name__ == \"__main__\":\n    Main()", r.Python);
        Assert.Contains("for i in range(N):", r.Python);
    }

    [Fact]
    public void Library_IsUsedByItsClassName()
    {
        var lib = "public static class Utils\n{\n\tpublic static int Count = 0;\n\tpublic static int Twice(int x) => x * 2;\n\tstatic int Hidden() => 1;\n}\n";
        Ok(lib, "Utils");
        var others = new Dictionary<string, string> { ["Utils"] = lib };
        var r = Ok("int y = Utils.Twice(3) + Utils.Count;\nConsole.WriteLine(y);\n", "main", others);
        Assert.Contains("import Utils", r.Python);
        Assert.Contains("Utils.Twice(3) + Utils.Count", r.Python);
        var e = Cs.Compile("Utils.Hidden();\n", Opts("main", others)).Errors[0];
        Assert.StartsWith("CS0122: 'Utils.Hidden()' is inaccessible due to its protection level", e.Message);
        Assert.Contains("must be named like its file", Fail(lib, "Helpers").Message);
    }

    [Fact]
    public void IntegerDivisionAndModuloTruncate()
    {
        var r = Ok("int a = -7;\nint q = a / 2;\nint m = a % 2;\ndouble d = a / 2.0;\nint t = (int)(a / 2.0);\n");
        Assert.Contains("q = __jdiv(a, 2)", r.Python);
        Assert.Contains("m = __jmod(a, 2)", r.Python);
        Assert.Contains("d = a / 2.0", r.Python);
        Assert.Contains("t = __jtrunc(a / 2.0)", r.Python);
    }

    [Theory]
    [InlineData("int x = 1.5;", "CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)", "1.5")]
    [InlineData("int x = 1;\nx += 0.5;", "CS0266", "0.5")]
    [InlineData("Harvest()", "CS1002: ; expected", ")")]
    [InlineData("int x = 1\nHarvest();", "CS1002: ; expected", "1")]
    [InlineData("harvest();", "CS0103: The name 'harvest' does not exist in the current context (did you mean Harvest?)", "harvest")]
    [InlineData("print(1);", "(use Console.WriteLine)", "print")]
    [InlineData("int y;\nConsole.WriteLine(y);", "CS0165: Use of unassigned local variable 'y'", "y")]
    [InlineData("Move(North, 2);", "CS1501: No overload for method 'Move' takes 2 arguments", "Move(North, 2)")]
    [InlineData("Move(3);", "CS1503: Argument 1: cannot convert from 'int' to 'Direction'", "3")]
    [InlineData("var xs = new List<int>();\nint n = xs.Length;", "CS1061: 'List<int>' does not contain a definition for 'Length'", "Length")]
    [InlineData("switch (1)\n{\n\tcase 1:\n\t\tHarvest();\n\tcase 2:\n\t\tbreak;\n}", "CS0163: Control cannot fall through from one case label ('case 1:') to another", "case 1:")]
    [InlineData("switch (GetEntityType())\n{\n\tcase Entities.Carrot:\n\t\tbreak;\n}", "CS9135: A constant value of type 'Entity?' is expected", "Entities.Carrot")]
    [InlineData("long big = 5;", "long is not supported in JavaFarmer", "long")]
    [InlineData("try { Harvest(); } catch { }", "try/catch is not supported in JavaFarmer", "try")]
    [InlineData("int x = 1;\nint y = x++;", "++ can only be used as a separate statement in JavaFarmer", "x++")]
    [InlineData("int __jx = 1;", "identifiers starting with __j are reserved by JavaFarmer", "__jx")]
    [InlineData("string s = \"a\\nb\";", "line breaks (\\n) inside strings are not supported in JavaFarmer", "\"a\\nb\"")]
    [InlineData("Console.WriteLine(\"{0}\", 1);", "composite format strings", "Console.WriteLine(\"{0}\", 1)")]
    [InlineData("int x = 1;\nint x = 2;", "CS0128: A local variable or function named 'x' is already defined in this scope", "x")]
    [InlineData("int f() { }", "CS0161: 'f()': not all code paths return a value", "f")]
    [InlineData("bool b = \"a\" < \"b\";", "CS0019: Operator '<' cannot be applied to operands of type 'string' and 'string'", "<")]
    [InlineData("if (true) { Harvest();", "CS1513: } expected", ";")]
    [InlineData("var d = new Dictionary<string, int>();\nint v;\nbool ok = d.TryGetValue(\"a\", out v);", "TryGetValue is only supported as the whole condition of an if statement in JavaFarmer", "d.TryGetValue(\"a\", out v)")]
    public void Diagnostics(string src, string message, string at)
    {
        var e = Fail(src);
        Assert.Contains(message, e.Message);
        Assert.Equal(at, HarnessTests.Slice(src, e.Start, e.End));
    }

    [Fact]
    public void ConsoleWrite_IsBufferedUntilWriteLine()
    {
        var r = Ok("Console.Write(\"a\");\nConsole.Write(1);\nConsole.WriteLine(\"!\");\nConsole.WriteLine();\n");
        Assert.Equal("__jcout = \"\"\n__jcout += \"a\"\n__jcout += str(1)\n__jcout += \"!\"\nprint(__jcout)\n__jcout = \"\"\nprint(__jcout)\n__jcout = \"\"", r.Python.Trim());
    }

    [Fact]
    public void Interpolation_AndConcatenation()
    {
        var r = Ok("int x = 3;\nstring s = $\"x={x}!\";\nstring t = \"x=\" + x;\nstring u = $\"{x}\";\n");
        Assert.Contains("s = \"x=\" + str(x) + \"!\"", r.Python);
        Assert.Contains("t = \"x=\" + str(x)", r.Python);
        Assert.Contains("u = str(x)", r.Python);
    }

    [Fact]
    public void SwitchExpression_BecomesAnIfChain()
    {
        var r = Ok("int n = GetPosX();\nstring s = n switch { 0 => \"zero\", 1 or 2 => \"small\", _ => \"big\" };\nConsole.WriteLine(s);\n");
        Assert.Contains("if n == 0:\n    s = \"zero\"\nelif n == 1 or n == 2:\n    s = \"small\"\nelse:\n    s = \"big\"", r.Python);
    }

    [Fact]
    public void TryGetValue_InAnIf()
    {
        var r = Ok("var d = new Dictionary<string, int> { [\"a\"] = 1 };\nif (d.TryGetValue(\"a\", out var v))\n{\n\tConsole.WriteLine(v);\n}\n");
        Assert.Contains("v = __jgetd(d, \"a\", 0)\nif \"a\" in d:\n    print(v)", r.Python);
    }

    [Fact]
    public void ForEachOverDictionary()
    {
        var r = Ok("var d = new Dictionary<string, int>();\nforeach (var (k, v) in d) Console.WriteLine(k + v);\nforeach (var kv in d) Console.WriteLine(kv.Value);\nforeach (var k in d.Keys) Console.WriteLine(k);\n");
        Assert.Contains("for k in d:\n    v = d[k]", r.Python);
        Assert.Contains("for kvKey in d:\n    kvValue = d[kvKey]\n    print(kvValue)", r.Python);
    }

    [Fact]
    public void TwoDimensionalArrays()
    {
        var r = Ok("int[,] g = new int[3, 4];\ng[1, 2] = 5;\nConsole.WriteLine(g.GetLength(0) + g.GetLength(1) + g[1, 2]);\n");
        Assert.Contains("g = __jarr2(3, 4, 0)", r.Python);
        Assert.Contains("g[1][2] = 5", r.Python);
        Assert.Contains("len(g) + len(g[0]) + g[1][2]", r.Python);
    }

    [Fact]
    public void Lambdas_And_MethodGroups()
    {
        var r = Ok("int Work() => 3;\nSpawnDrone(Work);\nSpawnDrone(() => Work());\nSpawnDrone(() => { Harvest(); });\nFunc<int, int> twice = x => x * 2;\nConsole.WriteLine(twice(4));\n");
        Assert.Contains("spawn_drone(Work)", r.Python);
        Assert.Contains("print(twice(4))", r.Python);
        Assert.Contains("assigning a captured local variable", Fail("void F()\n{\n\tint n = 0;\n\tSpawnDrone(() => { n++; });\n}\n").Message);
    }

    // ------------------------------------------------------------------ fairness (SPEC §7)
    static Segment SegmentOf(CompileResult r, string pyText, int nth = 0)
    {
        int at = -1;
        for (int i = 0; i <= nth; i++) at = r.Python.IndexOf(pyText, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, "'" + pyText + "' not in\n" + r.Python);
        return r.Map.Find(at);
    }

    [Fact]
    public void Scaffolding_IsFree()
    {
        var src = "using System;\n\nclass Program\n{\n\tstatic void Main()\n\t{\n\t\tHarvest();\n\t}\n}\n";
        var r = Ok(src);
        var def = SegmentOf(r, "def Main():");
        Assert.Equal(SegmentKind.Synthetic, def.Kind);
        Assert.True(def.NoHighlight);
        var guard = SegmentOf(r, "if __name__");
        Assert.Equal(SegmentKind.Synthetic, guard.Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "harvest()").Kind);
    }

    [Fact]
    public void ConsoleWriteBuffer_And_ConcatStr_AreFree_PrintCostsPrint()
    {
        var r = Ok("int x = 1;\nConsole.Write(x);\nConsole.WriteLine($\"x={x}\");\n");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "__jcout = \"\"").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "__jcout += ").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "print(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "str(x)", 1).Kind);
    }

    [Fact]
    public void ForLoops_CostFor()
    {
        var r = Ok("for (int i = 0; i < 5; i++) Harvest();\nint j = 0;\nfor (; j < 10; j += 3) Harvest();\nforeach (int k in new List<int> { 1 }) Harvest();\n");
        Assert.Equal(SegmentKind.User, SegmentOf(r, "for i in range(5)").Kind);
        var w = SegmentOf(r, "while j < 10");
        Assert.Equal("for", w.DependencyOverride);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "for k in").Kind);
    }

    [Fact]
    public void TopLevelVariablesUsedByLocalFunctions_GetFreeGlobalLines()
    {
        var r = Ok("int n = 0;\nvoid Inc() { n++; }\nInc();\n");
        var g = SegmentOf(r, "global n");
        Assert.True(g.Kind == SegmentKind.Synthetic && g.NoHighlight);
    }

    // ------------------------------------------------------------------ printer
    [Fact]
    public void PrintsPythonAsTopLevelCSharp()
    {
        var py = Compiler.Languages.Get("python");
        var ir = py.ToIr("count = 0\ndef bump():\n\tglobal count\n\tcount += 1\nfor i in range(3):\n\tbump()\nprint(count, 'x')\n", Opts(), out var errs);
        Assert.NotNull(ir);
        var text = Cs.Print(ir, new PrintOptions { FileName = "main" });
        Assert.Equal("int count = 0;\nfor (int i = 0; i < 3; i++)\n{\n\tBump();\n}\nConsole.WriteLine($\"{count} x\");\n\nvoid Bump()\n{\n\tcount += 1;\n}\n", text);
        Ok(text);
    }

    [Fact]
    public void PrintsALibraryAsAStaticClass()
    {
        var py = Compiler.Languages.Get("python");
        var ir = py.ToIr("def plant_row(n):\n\tfor i in range(n):\n\t\tplant(Entities.Bush)\n\t\tmove(East)\n", Opts("Farming"), out _);
        var text = Cs.Print(ir, new PrintOptions { FileName = "Farming" });
        // library members keep their names: other windows (in any language) call Farming.plant_row
        Assert.StartsWith("public static class Farming\n{\n\tpublic static void plant_row(int n)\n", text);
        Ok(text, "Farming");
    }

    [Fact]
    public void RoundTrip_KeepsSwitchExpressionsAndClassForm()
    {
        var src = "class Program\n{\n\tstatic string Name(int n) => n switch\n\t{\n\t\t0 => \"zero\",\n\t\t_ => \"many\",\n\t};\n\n\tstatic void Main()\n\t{\n\t\tConsole.WriteLine(Name(GetPosX()));\n\t}\n}\n";
        var ir = Cs.ToIr(src, Opts(), out _);
        var text = Cs.Print(ir, new PrintOptions { FileName = "main" });
        Assert.Contains("n switch", text);
        Assert.StartsWith("class Program\n{\n", text);
        Assert.Equal(Ok(src).Python, Ok(text).Python);
    }

    [Fact]
    public void PrintsJavaSaves()
    {
        var java = Compiler.Languages.Get("java");
        var ir = java.ToIr("int total = 0;\n\nvoid main() {\n    for (int i = 0; i < 3; i++) {\n        total += i;\n    }\n    System.out.println(total);\n}\n", Opts(), out var errs);
        Assert.NotNull(ir);
        var text = Cs.Print(ir, new PrintOptions { FileName = "main" });
        Ok(text);
        Assert.Contains("static void Main()", text);
    }

    // ------------------------------------------------------------------ speed / robustness
    public static string CSharp300()
    {
        var sb = new StringBuilder();
        sb.Append("int harvested = 0;\nList<int> heights = new List<int>();\nDictionary<Entity, int> counts = new Dictionary<Entity, int>();\n\n");
        int n = 0;
        while (sb.ToString().Split('\n').Length < 270)
        {
            n++;
            sb.Append("int Work" + n + "(int size, double factor)\n{\n");
            sb.Append("\tint total = 0;\n");
            sb.Append("\tfor (int x = 0; x < size; x++)\n\t{\n");
            sb.Append("\t\tfor (int y = 0; y < GetWorldSize(); y++)\n\t\t{\n");
            sb.Append("\t\t\tif (CanHarvest() && GetEntityType() == Entities.Carrot)\n\t\t\t{\n");
            sb.Append("\t\t\t\tHarvest();\n\t\t\t\tharvested++;\n\t\t\t\ttotal += x * y % 7;\n\t\t\t}\n");
            sb.Append("\t\t\telse if (GetGroundType() != Grounds.Soil)\n\t\t\t{\n\t\t\t\tTill();\n\t\t\t}\n\t\t\telse\n\t\t\t{\n");
            sb.Append("\t\t\t\tPlant(x % 2 == 0 ? Entities.Bush : Entities.Grass);\n\t\t\t}\n");
            sb.Append("\t\t\theights.Add(total / (y + 1));\n");
            sb.Append("\t\t\tMove(North);\n\t\t}\n\t\tMove(East);\n\t}\n");
            sb.Append("\tswitch (total % 3)\n\t{\n\t\tcase 0:\n\t\t\tcounts[Entities.Bush] = total;\n\t\t\tbreak;\n\t\tcase 1:\n\t\t\tQuickPrint($\"one {total}\");\n\t\t\tbreak;\n\t\tdefault:\n\t\t\ttotal -= 1;\n\t\t\tbreak;\n\t}\n");
            sb.Append("\tint i = 0;\n\twhile (i < heights.Count) { if (heights[i] > factor) { i += 2; continue; } i++; }\n");
            sb.Append("\treturn (int)(total * factor);\n}\n\n");
        }
        sb.Append("int s = GetWorldSize();\n");
        for (int i = 1; i <= n; i++) sb.Append("harvested += Work" + i + "(s, 1.5);\n");
        sb.Append("SpawnDrone(() => Work1(s, 2.0));\nConsole.WriteLine(harvested);\n");
        return sb.ToString();
    }

    [Fact]
    public void Compile300Lines_Under5ms()
    {
        var src = CSharp300();
        int lines = src.Split('\n').Length;
        var opts = Opts();
        Ok(src);
        for (int i = 0; i < 30; i++) Cs.Compile(src, opts);
        var times = new List<double>();
        for (int i = 0; i < 100; i++)
        {
            var sw = Stopwatch.StartNew();
            Cs.Compile(src, opts);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        output.WriteLine($"C# compile, {lines} lines: p25 {times[25]:F3} ms, median {times[50]:F3} ms, p90 {times[90]:F3} ms");
        if (times[25] < 5.0) return;
        // a loaded machine (parallel builds): compare with the Java frontend on its own 300-line benchmark, same moment
        var java = Compiler.Languages.Get("java");
        var jsrc = PerformanceTests.Java300();
        var jopts = new CompileOptions { FileName = "Main" };
        for (int i = 0; i < 30; i++) java.Compile(jsrc, jopts);
        var jt = new List<double>();
        for (int i = 0; i < 100; i++) { var sw = Stopwatch.StartNew(); java.Compile(jsrc, jopts); sw.Stop(); jt.Add(sw.Elapsed.TotalMilliseconds); }
        jt.Sort();
        output.WriteLine($"Java compile at the same time: p25 {jt[25]:F3} ms");
        Assert.True(jt[25] >= 5.0 && times[25] <= jt[25] * 1.25, $"p25 {times[25]:F3} ms for {lines} lines (limit 5 ms; Java p25 {jt[25]:F3} ms)");
    }

    [Fact]
    public void NeverThrows_OnTruncatedOrGarbledInput()
    {
        var samples = new List<string> { CSharp300(), "class Program\n{\n\tstatic void Main()\n\t{\n\t\tvar d = new Dictionary<string, int> { [\"a\"] = 1 };\n\t\tif (d.TryGetValue(\"a\", out var v)) Console.WriteLine($\"{v:F2} {{x}}\");\n\t}\n}\n" };
        var corpus = RepoPaths.Corpus("csharp");
        if (Directory.Exists(corpus)) samples.AddRange(Directory.GetFiles(corpus, "*.cs", SearchOption.AllDirectories).Take(40).Select(File.ReadAllText));
        var rnd = new Random(7);
        int runs = 0;
        foreach (var s in samples)
        {
            var cuts = Enumerable.Range(0, 25).Select(_ => rnd.Next(s.Length + 1)).ToList();
            foreach (var cut in cuts)
            {
                var variants = new[]
                {
                    s.Substring(0, cut),
                    s.Substring(cut),
                    s.Remove(cut, Math.Min(3, s.Length - cut)),
                    s.Insert(cut, new[] { "{", "}", "(", "\"", "$\"{", "'", "/*", "=>", "[", "switch", "@", "#" }[rnd.Next(12)]),
                };
                foreach (var v in variants)
                {
                    runs++;
                    var r = Cs.Compile(v, Opts());
                    Assert.NotNull(r);
                    if (!r.Success) Assert.DoesNotContain("internal JavaFarmer compiler error", r.Errors[0].Message);
                    var hl = Cs.Highlight(v);
                    Assert.Equal(v.Length, hl.Sum(t => t.Length));
                    var ir = Cs.ToIr(v, Opts(), out _);
                    if (ir != null) Assert.NotNull(Cs.Print(ir, new PrintOptions()));
                }
            }
        }
        output.WriteLine(runs + " garbled inputs");
    }
}
