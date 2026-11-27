using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.Rust;

/// <summary>RustLanguage: identity, editor services, naming, values, diagnostics, cross-file modules, printer, unlock fairness, speed, robustness.</summary>
[Collection("perf")]
public class RustLanguageTests
{
    readonly ITestOutputHelper output;
    public RustLanguageTests(ITestOutputHelper output) { this.output = output; }

    static readonly ILanguage Rs = Compiler.Languages.Get("rust");
    static readonly ILanguage Py = Compiler.Languages.Get("python");
    static CompileOptions Opts(string name = "main", Dictionary<string, string> others = null) => new() { FileName = name, OtherFiles = others };

    const string Head = "use farm::*;\n\n";
    static string Main(string body) => Head + "fn main() {\n" + body + "\n}\n";

    static CompileResult Ok(string src, string name = "main", Dictionary<string, string> others = null)
    {
        var r = Rs.Compile(src, Opts(name, others));
        Assert.True(r.Success, string.Join("; ", r.Errors.Select(e => e + " [" + HarnessTests.Slice(src, e.Start, e.End) + "]")) + "\n" + src);
        Assert.Null(GameOracle.TryParse(r.Python));
        return r;
    }

    static Diagnostic Fail(string src, string name = "main", Dictionary<string, string> others = null)
    {
        var r = Rs.Compile(src, Opts(name, others));
        Assert.False(r.Success, "expected an error:\n" + r.Python);
        return r.Errors[0];
    }

    static string PrintPython(string py, bool snippet, out IReadOnlyList<string> problems)
    {
        var ir = Py.ToIr(py, Opts(), out var errs);
        Assert.True(ir != null, string.Join("; ", errs));
        return ((IPrinterDiagnostics)Rs).Print(ir, snippet ? new PrintOptions { Snippet = true } : new PrintOptions(), out problems);
    }

    // ------------------------------------------------------------------ identity / editor
    [Fact]
    public void Identity()
    {
        Assert.Equal("rust", Rs.Id);
        Assert.Equal("Rust", Rs.DisplayName);
        Assert.Equal(".rs", Rs.FileExtension);
        Assert.False(Rs.IsNative);
        Assert.IsType<RustLanguage>(Rs);
        Assert.IsAssignableFrom<IPrinterDiagnostics>(Rs);
    }

    [Fact]
    public void EditorRulesAndTemplate()
    {
        var e = Rs.Editor;
        Assert.Equal(new[] { "{" }, e.IndentAfter);
        Assert.Equal(new[] { "}" }, e.DedentOn);
        Assert.Equal("//", e.LineComment);
        Assert.Equal("\t", e.IndentUnit);
        var tpl = Rs.NewFileTemplate("main", out int caret);
        Assert.Equal("use farm::*;\n\nfn main() {\n\t\n}", tpl);
        Assert.Equal("use farm::*;\n\nfn main() {\n\t", tpl.Substring(0, caret));
        Ok(tpl);
        Ok(tpl.Insert(caret, "harvest();"));
    }

    [Fact]
    public void StubFile()
    {
        var stub = Rs.ApiStubFile(out var name);
        Assert.Equal("farm.rs", name);
        Assert.Contains("pub fn harvest() -> bool", stub);
        Assert.Contains("pub fn move_to(", stub);
        Assert.Contains("pub use move_to as r#move;", stub);
        Assert.Contains("pub enum Entity", stub);
        Assert.Contains("pub fn spawn_drone<", stub);
        foreach (var b in GameApi.Builtins)
            if (b.PythonName != "print" && b.PythonName != "quick_print")
                Assert.Contains("fn " + Rs.ApiName(b.PythonName), stub);
    }

    // ------------------------------------------------------------------ naming
    [Theory]
    [InlineData("can_harvest", "can_harvest")]
    [InlineData("do_a_flip", "do_a_flip")]
    [InlineData("get_pos_x", "get_pos_x")]
    [InlineData("spawn_drone", "spawn_drone")]
    [InlineData("move", "move_to")]
    [InlineData("print", "println!")]
    [InlineData("quick_print", "quick_print!")]
    [InlineData("min", "min")]
    [InlineData("abs", "abs")]
    [InlineData("random", "random")]
    public void ApiNames(string py, string rs)
    {
        Assert.Equal(rs, Rs.ApiName(py));
        Assert.Equal(py, Rs.FromApiName(rs));
    }

    [Fact]
    public void ApiNamesOtherSpellings()
    {
        Assert.Null(Rs.ApiName("no_such_builtin"));
        Assert.Null(Rs.FromApiName("no_such_builtin"));
        Assert.Null(Rs.ApiName(null));
        Assert.Equal("move", Rs.FromApiName("r#move"));
        Assert.Equal("print", Rs.FromApiName("print!"));
        Assert.Equal("use_item", Rs.FromApiName("use_item_with"));
        Assert.Null(Rs.FromApiName("canHarvest"));
    }

    [Fact]
    public void ApiNamesOfRegisteredBuiltins()
    {
        GameApi.Register("train_model_rs", "epochs: int", "float", "Trains.", extension: "aiml");
        try
        {
            Assert.Equal("train_model_rs", Rs.ApiName("train_model_rs"));
            Assert.Equal("train_model_rs", Rs.FromApiName("train_model_rs"));
            var r = Ok(Main("\tlet loss = train_model_rs(3);\n\tprintln!(\"{loss}\");"));
            Assert.Contains("train_model_rs(3)", r.Python);
            Assert.Contains("(E0061)", Fail(Main("\ttrain_model_rs();")).Message);
            Assert.Contains("(E0308)", Fail(Main("\ttrain_model_rs(1.5);")).Message);
            Assert.Contains("fn train_model_rs(epochs: i32) -> f64", Rs.ApiStubFile(out _));
        }
        finally { GameApi.Unregister("train_model_rs"); }
        Assert.Contains("(E0425)", Fail(Main("\ttrain_model_rs(3);")).Message);
    }

    [Fact]
    public void Constants_AnyMemberName_BecomesIrConst()
    {
        var r = Ok(Main("\tplant(Entity::Carrot);\n\tuse_item(Item::Some_Future_Item);\n\tmove_to(North);\n\tmove_to(Direction::South);"));
        Assert.Contains("plant(Entities.Carrot)", r.Python);
        Assert.Contains("use_item(Items.Some_Future_Item)", r.Python);
        Assert.Contains("move(North)", r.Python);
        Assert.Contains("move(South)", r.Python);
    }

    // ------------------------------------------------------------------ values
    [Fact]
    public void FormatValueLikeRustDebug()
    {
        string F(object v) => Rs.FormatValue(v);
        Assert.Equal("None", F(null));
        Assert.Equal("true", F(true));
        Assert.Equal("false", F(false));
        Assert.Equal("2", F(2.0));
        Assert.Equal("1.5", F(1.5));
        Assert.Equal("-7", F(-7.0));
        Assert.Equal("hay", F("hay"));
        Assert.Equal("[1, 2, 3]", F(new GameList(new object[] { 1.0, 2.0, 3.0 })));
        Assert.Equal("[\"a\", None, true]", F(new GameList(new object[] { "a", null, true })));
        Assert.Equal("{\"a\": 1}", F(new GameDict().Add("a", 1.0)));
        Assert.Equal("(1, 2)", F(new GameTuple(1.0, 2.0)));
        Assert.Equal("(Tree, (1, 2))", F(new GameTuple(new GameConstant("Entities", "Tree"), new GameTuple(1.0, 2.0))));
        Assert.Equal("Drone(3)", F(new GameDrone(3)));
        Assert.Equal("can_harvest", F(new GameFunction("can_harvest")));
        Assert.Equal("move_to", F(new GameFunction("move")));
        Assert.Equal("{closure}", F(new GameFunction("__jlambda2")));
        Assert.Equal("Carrot", F(new GameConstant("Entities", "Carrot")));
        Assert.Equal("North", F(new GameConstant("Directions", "North")));
    }

    [Fact]
    public void KeywordsAndHighlight()
    {
        Assert.Contains("fn", Rs.Keywords);
        Assert.Contains("let", Rs.Keywords);
        Assert.Contains("mut", Rs.Keywords);
        Assert.Contains("match", Rs.Keywords);
        Assert.DoesNotContain("i32", Rs.Keywords);
        var src = "let x = 5; // hi\nmove_to(North);\nlet s = \"a{x}\";\nplant(Entity::Carrot);\n/* open";
        var hl = Rs.Highlight(src);
        Assert.Equal(src.Length, hl.Sum(t => t.Length));
        string KindOf(string word) { int i = src.IndexOf(word, StringComparison.Ordinal); return hl.First(t => t.Start == i).Kind.ToString(); }
        Assert.Equal("Keyword", KindOf("let"));
        Assert.Equal("Comment", KindOf("// hi"));
        Assert.Equal("Number", KindOf("5"));
        Assert.Equal("String", KindOf("\"a{x}\""));
        Assert.Equal("Comment", KindOf("/* open"));
    }

    // ------------------------------------------------------------------ checker
    [Theory]
    [InlineData("\tlet x = 1;\n\tx = 2;", "cannot assign twice to immutable variable `x`", "x = 2")]
    [InlineData("\tlet v = vec![1];\n\tv.push(2);", "cannot borrow `v` as mutable", "v")]
    [InlineData("\tlet a: i32 = 2.5;", "mismatched types: expected `i32`, found `{float}`", "2.5")]
    [InlineData("\tlet v = vec![1];\n\tlet n: i32 = 0;\n\tlet x = v[n];", "cannot be indexed by `i32`", "n")]
    [InlineData("\tlet x: i32;\n\tlet y = x + 1;", "used binding `x` isn't initialized (E0381)", "x")]
    [InlineData("\tlet n = get_world_size() / 2.0;", "cannot divide `i32` by `{float}` (E0277)", "/")]
    [InlineData("\tlet __jx = 1;", "identifiers starting with `__j` are reserved", "__jx")]
    [InlineData("\tlet e: Entity = get_entity_type();", "expected `Entity`, found `Option<Entity>`", "get_entity_type()")]
    [InlineData("\tharvest()\n\tharvest();", "expected `;`", "harvest")]
    public void Diagnostics(string body, string message, string at)
    {
        var src = Main(body);
        var e = Fail(src);
        Assert.Contains(message, e.Message);
        Assert.Equal(at, HarnessTests.Slice(src, e.Start, e.End));
    }

    [Fact]
    public void Shadowing_AndCasts_AndUsizeIndexing()
    {
        var r = Ok(Main("\tlet x = 5;\n\tlet x = x as f64 * 1.5;\n\tlet v = vec![1, 2, 3];\n\tlet i: usize = 2;\n\tlet y = v[i] + v.len() as i32;\n\tquick_print!(\"{x} {y}\");"));
        Assert.Contains("v[i]", r.Python);
        Assert.Contains("len(v)", r.Python);
    }

    [Fact]
    public void IntegerDivisionAndModuloTruncate()
    {
        var r = Ok(Main("\tlet a: i32 = -7;\n\tlet q = a / 2;\n\tlet m = a % 2;\n\tlet f = -7.0 / 2.0;\n\tlet e = a.div_euclid(2);"));
        Assert.Contains("q = __jdiv(a, 2)", r.Python);
        Assert.Contains("m = __jmod(a, 2)", r.Python);
        Assert.Contains("/ 2.0", r.Python);
        Assert.Contains("a // 2", r.Python);
    }

    [Fact]
    public void Options_IfLetMatchUnwrap()
    {
        var r = Ok(Main("\tif let Some(e) = get_entity_type() {\n\t\tquick_print!(\"{}\", e);\n\t}\n\tlet c = get_cost(Unlock::Speed);\n\tif c.is_none() {\n\t\treturn;\n\t}\n\tlet c = c.unwrap();\n\tmatch get_entity_type() {\n\t\tSome(x) => quick_print!(\"{}\", x),\n\t\tNone => {\n\t\t\tharvest();\n\t\t}\n\t}"));
        Assert.Contains("e = get_entity_type()\n    if e != None:", r.Python);
        Assert.Contains("if c == None:", r.Python);
    }

    [Fact]
    public void StaticMut_NeedsUnsafe_ConstIsFree()
    {
        Ok(Head + "const N: i32 = 3;\nstatic mut COUNT: i32 = 0;\n\nfn main() {\n\tunsafe {\n\t\tCOUNT += N;\n\t}\n}\n");
        Assert.Contains("(E0133)", Fail(Head + "static mut COUNT: i32 = 0;\n\nfn main() {\n\tCOUNT += 1;\n}\n").Message);
    }

    // ------------------------------------------------------------------ other files
    [Fact]
    public void Modules_UsePubItemsOfOtherFiles()
    {
        var utils = "use farm::*;\n\npub const LIMIT: i32 = 3;\n\npub fn go_home() -> i32 {\n\tmove_to(South);\n\t1\n}\n\nfn hidden() {}\n";
        var others = new Dictionary<string, string> { ["utils"] = utils };
        var r = Ok(Head + "mod utils;\n\nfn main() {\n\tlet n = utils::go_home() + utils::LIMIT;\n\tspawn_drone(utils::go_home);\n}\n", others: others);
        Assert.Contains("import utils", r.Python);
        Assert.Contains("utils.go_home()", r.Python);
        var e = Fail(Head + "mod utils;\n\nfn main() {\n\tutils::hidden();\n}\n", others: others);
        Assert.Contains("(E0603)", e.Message);
        Assert.Contains("(E0583)", Fail(Head + "mod nothere;\n\nfn main() {\n}\n").Message);
        Ok(utils, "utils");   // a module file needs no main
    }

    // ------------------------------------------------------------------ fairness (SPEC `7)
    static Segment SegmentOf(CompileResult r, string pyText, int nth = 0)
    {
        int at = -1;
        for (int i = 0; i <= nth; i++) at = r.Python.IndexOf(pyText, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, "'" + pyText + "' not in\n" + r.Python);
        return r.Map.Find(at);
    }

    [Fact]
    public void Scaffolding_IsFree_UseAndModLinesHaveNoCode()
    {
        var others = new Dictionary<string, string> { ["utils"] = "pub fn f() {}\n" };
        var src = "use farm::*;\nuse std::collections::HashMap;\nmod utils;\n\nfn main() {\n\tharvest();\n}\n";
        var r = Ok(src, others: others);
        var def = SegmentOf(r, "def main():");
        Assert.Equal(SegmentKind.Synthetic, def.Kind);
        Assert.True(def.NoHighlight);
        var guard = SegmentOf(r, "if __name__");
        Assert.Equal(SegmentKind.Synthetic, guard.Kind);
        Assert.True(guard.NoHighlight);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "harvest()").Kind);
        Assert.DoesNotContain("HashMap", r.Python);
    }

    [Fact]
    public void WhileLoop_CostsLoops_NotFor()
    {
        var r = Ok(Main("\tlet mut i = 0;\n\twhile i < 3 {\n\t\ti += 1;\n\t}\n\tloop {\n\t\tbreak;\n\t}"));
        var w = SegmentOf(r, "while i < 3");
        Assert.Equal(SegmentKind.User, w.Kind);
        Assert.Null(w.DependencyOverride);
        Assert.Null(SegmentOf(r, "while True").DependencyOverride);
    }

    [Fact]
    public void ForRanges_CostFor()
    {
        var r = Ok(Main("\tfor i in 0..5 {\n\t\tharvest();\n\t}\n\tfor j in (0..10).rev().step_by(2) {\n\t\tharvest();\n\t}\n\tlet v = vec![1];\n\tfor x in &v {\n\t\tharvest();\n\t}"));
        Assert.Equal(SegmentKind.User, SegmentOf(r, "for i in range(5)").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "for j in range(").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "for x in v").Kind);
    }

    [Fact]
    public void PrintLowering_OnlyChargesPrint()
    {
        var r = Ok(Main("\tlet x = 7;\n\tprintln!(\"x = {x} and {}\", x + 1);"));
        Assert.Equal(SegmentKind.User, SegmentOf(r, "print(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "str(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, " + ").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "str(", 1).Kind);
    }

    // ------------------------------------------------------------------ printer
    [Fact]
    public void PrintsPythonSnippets_AsBeginnerRust()
    {
        var text = PrintPython("for i in range(get_world_size()):\n    if can_harvest():\n        harvest()\n    move(North)\n", true, out var problems);
        Assert.Equal("for i in 0..get_world_size() {\n\tif can_harvest() {\n\t\tharvest();\n\t}\n\tmove_to(Direction::North);\n}\n", text);
        Assert.Empty(problems);
        text = PrintPython("def plant_row(n):\n    for _ in range(n):\n        plant(Entities.Carrot)\n\nplant_row(get_world_size())\nprint(\"done\", num_items(Items.Carrot))\n", true, out problems);
        Assert.Equal("fn plant_row(n: i32) {\n\tfor _ in 0..n {\n\t\tplant(Entity::Carrot);\n\t}\n}\n\nplant_row(get_world_size());\nprintln!(\"done {}\", num_items(Item::Carrot));\n", text);
        Assert.Empty(problems);
    }

    [Fact]
    public void PrintsPythonPrograms_ThatCompile()
    {
        var py = "items = [Items.Hay, Items.Wood]\nfor item in items:\n    if num_items(item) < 100:\n        print(item)\nd = {}\nd[(get_pos_x(), get_pos_y())] = get_entity_type()\nx = measure()\nif x != None and x > 3:\n    quick_print(len(d))\ncost = get_cost(Unlocks.Speed)\nfor k in cost:\n    quick_print(cost[k])\n";
        var text = PrintPython(py, false, out var problems);
        Assert.Empty(problems);
        Assert.StartsWith("use farm::*;\nuse std::collections::HashMap;\n", text);
        Assert.Contains("let x: Option<f64> = measure();", text);
        Assert.Contains("x.is_some() && x.unwrap() > 3.0", text);
        Assert.Contains("let cost = get_cost(Unlock::Speed).unwrap();", text);
        Ok(text);
    }

    [Fact]
    public void PrinterProblems_ForWhatRustCannotSay()
    {
        PrintPython("a = [1, True]\n", true, out var problems);
        Assert.Contains(problems, p => p.Contains("different types"));
        PrintPython("for i in range(10):\n    break\nprint(i)\n", true, out problems);
        Assert.Contains(problems, p => p.Contains("used after its loop"));
    }

    [Fact]
    public void LoopVariableReusedByALaterLoop_IsNotUseAfterLoop()
    {
        var text = PrintPython("for i in range(3):\n    harvest()\nfor i in range(4):\n    print(i)\ni = 7\nprint(i)\nfor w in [1, 2]:\n    print(w)\nif True:\n    w = 3\n    print(w)\n", false, out var problems);
        Assert.DoesNotContain(problems, p => p.Contains("used after its loop"));
        Ok(text);
        PrintPython("for s in [1]:\n    pass\nif s > 0:\n    harvest()\n", true, out problems);
        Assert.Contains(problems, p => p.Contains("`s` is used after its loop"));
    }

    [Fact]
    public void RoundTrip_KeepsCommentsIfLetAndMatch()
    {
        var src = "use farm::*;\n\n// leading comment\nfn main() {\n\tharvest(); // trailing\n\t// inner\n\tif let Some(e) = get_entity_type() {\n\t\tquick_print!(\"{e}\");\n\t}\n\tmatch get_pos_x() {\n\t\t0 => {\n\t\t\tharvest();\n\t\t}\n\t\t1 | 2 => {\n\t\t\ttill();\n\t\t}\n\t\t_ => {}\n\t}\n}\n";
        var ir = Rs.ToIr(src, Opts(), out var errs);
        Assert.NotNull(ir);
        var text = Rs.Print(ir, new PrintOptions());
        Assert.Contains("// leading comment", text);
        Assert.Contains("harvest(); // trailing", text);
        Assert.Contains("// inner", text);
        Assert.Contains("if let Some(e) = get_entity_type() {", text);
        Assert.True(text.Contains("0 => {\n\t\t\tharvest();"), text);   // harvest() returns bool: its arm needs a block
        Assert.Contains("1 | 2 => till(),", text);
        Ok(text);
    }

    [Fact]
    public void PrintsJavaSaves()
    {
        var java = Compiler.Languages.Get("java");
        var ir = java.ToIr("int total = 0;\n\nvoid main() {\n    for (int i = 0; i < 3; i++) {\n        total += i;\n    }\n    System.out.println(total);\n}\n", new CompileOptions { FileName = "Main" }, out var errs);
        Assert.NotNull(ir);
        var text = Rs.Print(ir, new PrintOptions());
        Assert.Contains("fn main() {", text);
        Assert.Contains("for i in 0..3 {", text);
        Ok(text);
    }

    // ------------------------------------------------------------------ speed
    public static string Rust300()
    {
        var sb = new StringBuilder("use farm::*;\nuse std::collections::HashMap;\n\nstatic mut HARVESTED: i32 = 0;\n\n");
        int n = 0;
        while (sb.ToString().Split('\n').Length < 272)
        {
            n++;
            sb.Append("fn work" + n + "(size: i32, factor: f64) -> i32 {\n");
            sb.Append("    let mut total = 0;\n    let mut heights: Vec<i32> = Vec::new();\n");
            sb.Append("    for x in 0..size {\n        for y in 0..get_world_size() {\n");
            sb.Append("            if can_harvest() && get_entity_type() == Some(Entity::Carrot) {\n");
            sb.Append("                harvest();\n                unsafe {\n                    HARVESTED += 1;\n                }\n                total += x * y % 7;\n");
            sb.Append("            } else if get_ground_type() != Ground::Soil {\n                till();\n            } else {\n");
            sb.Append("                let kind = if x % 2 == 0 { Entity::Bush } else { Entity::Grass };\n                plant(kind);\n            }\n");
            sb.Append("            heights.push(total / (y + 1));\n            move_to(North);\n        }\n        move_to(East);\n    }\n");
            sb.Append("    match total % 3 {\n        0 => quick_print!(\"zero {}\", total),\n        1 => quick_print!(\"one {total}\"),\n        _ => total -= 1,\n    }\n");
            sb.Append("    let mut i = 0;\n    while i < heights.len() {\n        if heights[i] as f64 > factor {\n            i += 2;\n            continue;\n        }\n        i += 1;\n    }\n");
            sb.Append("    (total as f64 * factor) as i32\n}\n\n");
        }
        sb.Append("fn main() {\n    let s = get_world_size();\n    let mut counts: HashMap<i32, i32> = HashMap::new();\n    let mut sum = 0;\n");
        for (int i = 1; i <= n; i++) sb.Append("    sum += work" + i + "(s, 1.5);\n");
        while (sb.ToString().Split('\n').Length < 296) sb.Append("    sum += s % 3;\n");
        sb.Append("    counts.insert(s, sum);\n    spawn_drone(move || work1(s, 2.0));\n    println!(\"{}\", sum);\n}\n");
        return sb.ToString();
    }

    [Fact]
    public void Compile300Lines_Under5ms()
    {
        var src = Rust300();
        int lines = src.Split('\n').Length;
        Assert.True(lines >= 300, lines + " lines");
        var opts = Opts();
        Ok(src);
        for (int i = 0; i < 30; i++) Rs.Compile(src, opts);
        var times = new List<double>();
        for (int i = 0; i < 100; i++)
        {
            var sw = Stopwatch.StartNew();
            Rs.Compile(src, opts);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        output.WriteLine($"Rust compile, {lines} lines: p25 {times[25]:F3} ms, median {times[50]:F3} ms, p90 {times[90]:F3} ms");
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

    // ------------------------------------------------------------------ robustness
    [Fact]
    public void NeverThrows_OnTruncatedOrGarbledInput()
    {
        var samples = new List<string> { Rust300(), Head + "use std::collections::HashMap;\n\nfn main() {\n\tlet m: HashMap<i32, i32> = HashMap::new();\n\tif let Some(v) = m.get(&1) {\n\t\tprintln!(\"{v:?} {{x}}\");\n\t}\n\tlet f = move |a: i32| a + 1;\n}\n" };
        var corpus = RepoPaths.Corpus("rust");
        if (Directory.Exists(corpus)) samples.AddRange(Directory.GetFiles(corpus, "*.rs", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).Take(60).Select(File.ReadAllText));
        var rnd = new Random(11);
        int runs = 0;
        foreach (var s in samples)
        {
            var cuts = Enumerable.Range(0, 20).Select(_ => rnd.Next(s.Length + 1)).ToList();
            foreach (var cut in cuts)
            {
                var variants = new[]
                {
                    s.Substring(0, cut),
                    s.Substring(cut),
                    s.Remove(cut, Math.Min(3, s.Length - cut)),
                    s.Insert(cut, new[] { "{", "}", "(", "\"", "'", "/*", "=>", "[", "match", "|", "::", "#", "r#", "&mut ", "'a" }[rnd.Next(15)]),
                };
                foreach (var v in variants)
                {
                    runs++;
                    var r = Rs.Compile(v, Opts());
                    Assert.NotNull(r);
                    if (!r.Success) Assert.DoesNotContain("internal JavaFarmer compiler error", r.Errors[0].Message);
                    var hl = Rs.Highlight(v);
                    Assert.Equal(v.Length, hl.Sum(t => t.Length));
                    int pos = 0;
                    foreach (var t in hl) { Assert.Equal(pos, t.Start); pos += t.Length; }
                    var ir = Rs.ToIr(v, Opts(), out _);
                    if (ir != null) Assert.NotNull(Rs.Print(ir, new PrintOptions()));
                    if (ir != null) Assert.NotNull(Rs.Print(ir, new PrintOptions { Snippet = true }));
                }
            }
        }
        output.WriteLine(runs + " garbled inputs");
    }
}
