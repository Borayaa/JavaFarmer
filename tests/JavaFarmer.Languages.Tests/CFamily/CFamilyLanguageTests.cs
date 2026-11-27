using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests;

/// <summary>C / C++ specifics: unlock fairness of scaffolding, editor services, value format, naming, stub, fuzzing.</summary>
public class CFamilyLanguageTests
{
    readonly ITestOutputHelper output;
    public CFamilyLanguageTests(ITestOutputHelper output) { this.output = output; }

    static ILanguage L(string id) => Compiler.Languages.Get(id);
    static CompileResult Compile(string id, string src, string file = "main") => L(id).Compile(src, new CompileOptions { FileName = file });

    internal static CompileResult Ok(string id, string src)
    {
        var r = Compile(id, src);
        Assert.True(r.Success, string.Join("; ", r.Errors.Select(e => e + " [" + HarnessTests.Slice(src, e.Start, e.End) + "]")));
        HarnessTests.AssertMapValid(src, r);
        Assert.Null(GameOracle.TryParse(r.Python));
        return r;
    }

    /// <summary>Segments whose Python text contains the given text.</summary>
    static List<Segment> Seg(CompileResult r, string pyText) =>
        r.Map.Segments.Where(s => r.Python.Substring(s.PyStart, s.PyEnd - s.PyStart).Contains(pyText)).ToList();

    static string SrcOf(string src, Segment s) => s.JavaStart < 0 ? "" : src.Substring(s.JavaStart, s.JavaEnd - s.JavaStart);

    // ------------------------------------------------------------------ unlock fairness (SPEC §7)

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n#include <stdio.h>\n\nint main(void) {\n\tharvest();\n\treturn 0;\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n#include <iostream>\nusing namespace std;\n\nint main() {\n\tharvest();\n\treturn 0;\n}\n")]
    public void MandatoryStructure_IsFree(string id, string src)
    {
        var r = Ok(id, src);
        // def main(): and the __name__ guard are Synthetic scaffolding that never highlights
        foreach (var s in Seg(r, "def main").Concat(Seg(r, "__name__")))
        {
            Assert.Equal(SegmentKind.Synthetic, s.Kind);
            Assert.True(s.NoHighlight);
        }
        // the only user code is harvest(): no include, no using, no return 0 in the Python
        Assert.DoesNotContain("return", r.Python);
        Assert.DoesNotContain("import", r.Python);
        foreach (var s in r.Map.Segments.Where(s => s.Kind == SegmentKind.User))
            Assert.Contains(SrcOf(src, s), new[] { "harvest", "harvest()", "harvest();" });
    }

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n#include <stdio.h>\n\nint main(void) {\n\tint n = 3;\n\tprintf(\"n is %d\\n\", n);\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n#include <iostream>\n\nint main() {\n\tint n = 3;\n\tstd::cout << \"n is \" << n << std::endl;\n}\n")]
    public void Printing_CostsOnlyPrint(string id, string src)
    {
        var r = Ok(id, src);
        Assert.Contains("print(\"n is \" + str(n))", r.Python);
        // the + and str() the lowering adds are Synthetic (no "operators" / "str" unlock), print itself is User
        Assert.All(Seg(r, " + "), s => Assert.Equal(SegmentKind.Synthetic, s.Kind));
        Assert.All(Seg(r, "str("), s => Assert.Equal(SegmentKind.Synthetic, s.Kind));
        Assert.Contains(Seg(r, "print"), s => s.Kind == SegmentKind.User);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void ClassicFor_CostsForEvenAsWhile(string id)
    {
        var head = id == "c" ? "#include \"farm.h\"\n\nint main(void) {\n" : "#include \"farm.hpp\"\n\nint main() {\n";
        // counting loop: range()
        var r1 = Ok(id, head + "\tfor (int i = 0; i < get_world_size(); i++) {\n\t\tmove(North);\n\t}\n}\n");
        Assert.Contains("for i in range(get_world_size()):", r1.Python);
        // not a counting loop: while with DependencyOverride "for", Structural header pieces
        var r2 = Ok(id, head + "\tint step = 2;\n\tfor (int i = 0; i < 10; i += step) {\n\t\tmove(North);\n\t}\n}\n");
        var w = Seg(r2, "while");
        Assert.NotEmpty(w);
        Assert.All(w, s => Assert.Equal("for", s.DependencyOverride));
        Assert.Contains(r2.Map.Segments, s => s.Kind == SegmentKind.Structural);
    }

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n\nint main(void) {\n\tint n = 3;\n\twhile (n) {\n\t\tn--;\n\t}\n\tif (get_entity_type()) {\n\t\tharvest();\n\t}\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n\nint main() {\n\tint n = 3;\n\twhile (n) {\n\t\tn--;\n\t}\n\tif (get_entity_type()) {\n\t\tharvest();\n\t}\n}\n")]
    public void ImplicitConditions_AreFree(string id, string src)
    {
        var r = Ok(id, src);
        Assert.Contains("while n != 0:", r.Python);
        Assert.Contains("if get_entity_type() != None:", r.Python);
        Assert.All(Seg(r, " != "), s => Assert.Equal(SegmentKind.Synthetic, s.Kind));
    }

    [Fact]
    public void BufferedOutput_PrintsOneLinePerNewline()
    {
        var src = "#include \"farm.h\"\n#include <stdio.h>\n\nint main(void) {\n\tprintf(\"a\");\n\tprintf(\"b\\n\");\n}\n";
        var r = Ok("c", src);
        Assert.Contains("__jout += \"a\"", r.Python);
        Assert.Contains("+ \"b\")", r.Python);
        Assert.Contains("__jout = \"\"", r.Python);
        Assert.All(Seg(r, "__jout"), s => Assert.Equal(SegmentKind.Synthetic, s.Kind));
    }

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n\nint main(void) {\n\tint a = -7 / 2;\n\tint b = -7 % 2;\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n\nint main() {\n\tint a = -7 / 2;\n\tint b = -7 % 2;\n}\n")]
    public void IntegerDivision_Truncates(string id, string src)
    {
        var r = Ok(id, src);
        Assert.Contains("__jdiv(", r.Python);
        Assert.Contains("__jmod(", r.Python);
    }

    // ------------------------------------------------------------------ editor services

    [Theory]
    [InlineData("c", ".c", "C", "farm.h")]
    [InlineData("cpp", ".cpp", "C++", "farm.hpp")]
    public void Identity_Template_Stub(string id, string ext, string display, string stub)
    {
        var l = L(id);
        Assert.Equal(ext, l.FileExtension);
        Assert.Equal(display, l.DisplayName);
        Assert.False(l.IsNative);
        var tpl = l.NewFileTemplate("main", out int caret);
        Assert.Equal('\t', tpl[caret - 1]);
        Assert.Contains("main(", tpl.Substring(0, caret));
        Ok(id, tpl);
        var text = l.ApiStubFile(out var name);
        Assert.Equal(stub, name);
        // C++ takes print / min / max / abs from std
        foreach (var b in GameApi.Builtins)
            if (b.PythonName != "print" && (id == "c" || (b.PythonName != "min" && b.PythonName != "max" && b.PythonName != "abs"))) Assert.Contains(b.PythonName, text);
        Assert.Equal("{", l.Editor.IndentAfter[0]);
        Assert.Equal("//", l.Editor.LineComment);
    }

    [Fact]
    public void ApiNames()
    {
        var c = L("c"); var cpp = L("cpp");
        Assert.Equal("can_harvest", c.ApiName("can_harvest"));
        Assert.Equal("printf", c.ApiName("print"));
        Assert.Equal("print", c.FromApiName("printf"));
        Assert.Equal("std::println", cpp.ApiName("print"));
        Assert.Equal("std::min", cpp.ApiName("min"));
        Assert.Equal("farm::random", cpp.ApiName("random"));
        Assert.Equal("random", cpp.FromApiName("farm::random"));
        Assert.Equal("harvest", cpp.FromApiName("farm::harvest"));
        Assert.Null(c.ApiName("no_such_builtin"));
        Assert.Null(c.FromApiName("no_such_builtin"));
    }

    [Fact]
    public void FormatValue_LooksLikeTheLanguage()
    {
        var v = new GameList(new object[] { 1.0, 2.5, "a", null, true, new GameConstant("Entities", "Carrot"), new GameConstant("Directions", "North") });
        Assert.Equal("{1, 2.5, \"a\", NULL, true, Entities.Carrot, North}", L("c").FormatValue(v));
        Assert.Equal("[1, 2.5, \"a\", nullptr, true, Entities::Carrot, North]", L("cpp").FormatValue(v));
        var m = new GameDict().Add("hay", 3.0);
        Assert.Equal("{\"hay\": 3}", L("c").FormatValue(m));
        Assert.Equal("{3, 4}", L("c").FormatValue(new GameTuple(3.0, 4.0)));
        Assert.Equal("(3, 4)", L("cpp").FormatValue(new GameTuple(3.0, 4.0)));
        Assert.Equal("false", L("cpp").FormatValue(false));
        Assert.Equal("NULL", L("c").FormatValue(null));
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void Highlight_KnowsTheLanguage(string id)
    {
        var src = "#include \"farm.h\"\n// note\nint main(void) {\n\tif (true) move(North); /* x */ \"s\" 12 3.5\n}";
        var hl = L(id).Highlight(src);
        Assert.Equal(src.Length, hl.Sum(t => t.Length));
        TokenKind KindAt(string text) { int at = src.IndexOf(text); return hl.First(t => t.Start <= at && at < t.Start + t.Length).Kind; }
        Assert.Equal(TokenKind.Comment, KindAt("// note"));
        Assert.Equal(TokenKind.Comment, KindAt("/* x */"));
        Assert.Equal(TokenKind.Keyword, KindAt("if"));
        Assert.Equal(TokenKind.Type, KindAt("int"));
        Assert.Equal(TokenKind.Constant, KindAt("North"));
        Assert.Equal(TokenKind.Constant, KindAt("true"));
        Assert.Equal(TokenKind.String, KindAt("\"s\""));
        Assert.Equal(TokenKind.Number, KindAt("3.5"));
        Assert.Equal(TokenKind.Keyword, KindAt("#include"));
    }

    // ------------------------------------------------------------------ registered builtins (extensions)


    // ------------------------------------------------------------------ printer

    [Fact]
    public void Printer_TranslatesPythonIdiomatically()
    {
        var py = Compiler.Languages.Get("python");
        var ir = py.ToIr("def harvest_column():\n    for _ in range(get_world_size()):\n        harvest()\n        move(North)\n\nwhile True:\n    if spawn_drone(harvest_column):\n        move(East)\n", new CompileOptions { FileName = "main" }, out _);
        var c = L("c").Print(ir, new PrintOptions { FileName = "main" });
        Assert.Contains("void harvest_column(void) {", c);
        Assert.Contains("if (spawn_drone(harvest_column) != NULL) {", c);
        Assert.Contains("int main(void) {", c);
        Ok("c", c);
        var cpp = L("cpp").Print(ir, new PrintOptions { FileName = "main" });
        Assert.Contains("void harvest_column() {", cpp);
        Assert.Contains("int main() {", cpp);
        Ok("cpp", cpp);
        var ir2 = py.ToIr("while True:\n    if can_harvest():\n        harvest()\n", new CompileOptions { FileName = "main" }, out _);
        var snippet = L("c").Print(ir2, new PrintOptions { FileName = "main", Snippet = true });
        Assert.Equal("while (true) {\n\tif (can_harvest()) {\n\t\tharvest();\n\t}\n}\n", snippet);
        Assert.DoesNotContain("#include", snippet);
        Assert.DoesNotContain("int main", snippet);
    }

    // ------------------------------------------------------------------ robustness

    static IEnumerable<string> CorpusSources(string id)
    {
        var dir = RepoPaths.Corpus(id);
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.GetFiles(dir, id == "c" ? "*.c" : "*.cpp", SearchOption.AllDirectories)) yield return File.ReadAllText(f);
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void Fuzz_CompileAndHighlightNeverThrow(string id)
    {
        var l = L(id);
        var rnd = new Random(1234);
        const string junk = "{}()[];,.<>=!&|+-*/%#\"'\\\n\t ab1:?";
        int runs = 0;
        foreach (var src in CorpusSources(id))
        {
            var cuts = new List<string>();
            for (int i = 0; i < 12; i++) cuts.Add(src.Substring(0, rnd.Next(src.Length + 1)));
            for (int i = 0; i < 12; i++)
            {
                var sb = new StringBuilder(src);
                for (int k = 0; k < 4 && sb.Length > 0; k++) sb[rnd.Next(sb.Length)] = junk[rnd.Next(junk.Length)];
                cuts.Add(sb.ToString());
            }
            foreach (var s in cuts)
            {
                var r = l.Compile(s, new CompileOptions { FileName = "main" });
                Assert.NotNull(r);
                if (!r.Success)
                {
                    Assert.NotEmpty(r.Errors);
                    Assert.DoesNotContain("internal JavaFarmer compiler error", r.Errors[0].Message);
                }
                Assert.Equal(s.Length, l.Highlight(s).Sum(t => t.Length));
                l.ToIr(s, new CompileOptions { FileName = "main" }, out _);
                runs++;
            }
        }
        output.WriteLine(runs + " garbled sources");
        Assert.True(runs > 100);
    }

    public static string Program300(string id) => CFamilyPerformanceTests.Program300(id);
}

/// <summary>SPEC §8: Compile runs on every keystroke and must take &lt; 5 ms for a 300-line file (serial, like the Java test).</summary>
[Collection("perf")]
public class CFamilyPerformanceTests
{
    readonly ITestOutputHelper output;
    public CFamilyPerformanceTests(ITestOutputHelper output) { this.output = output; }
    static ILanguage L(string id) => Compiler.Languages.Get(id);

    public static string Program300(string id)
    {
        bool c = id == "c";
        var sb = new StringBuilder(c ? "#include \"farm.h\"\n#include <stdio.h>\n\nint harvested = 0;\n\n" : "#include \"farm.hpp\"\n#include <iostream>\n#include <vector>\n#include <map>\n\nint harvested = 0;\nstd::vector<int> heights;\nstd::map<Entity, int> counts;\n\n");
        int n = 0;
        while (sb.ToString().Split('\n').Length < 270)
        {
            n++;
            sb.Append("int work" + n + "(int size, double factor) {\n\tint total = 0;\n");
            sb.Append("\tfor (int x = 0; x < size; x++) {\n\t\tfor (int y = 0; y < get_world_size(); y++) {\n");
            sb.Append("\t\t\tif (can_harvest() && get_entity_type() == " + (c ? "Entities.Carrot" : "Entities::Carrot") + ") {\n\t\t\t\tharvest();\n\t\t\t\tharvested++;\n\t\t\t\ttotal += x * y % 7;\n");
            sb.Append("\t\t\t} else if (get_ground_type() != " + (c ? "Grounds.Soil" : "Grounds::Soil") + ") {\n\t\t\t\ttill();\n\t\t\t} else {\n");
            sb.Append("\t\t\t\tplant(x % 2 == 0 ? " + (c ? "Entities.Bush : Entities.Grass" : "Entities::Bush : Entities::Grass") + ");\n\t\t\t}\n");
            sb.Append(c ? "\t\t\tprintf(\"%d\\n\", total / (y + 1));\n" : "\t\t\theights.push_back(total / (y + 1));\n");
            sb.Append("\t\t\tmove(North);\n\t\t}\n\t\tmove(East);\n\t}\n");
            sb.Append("\tswitch (total % 3) {\n\t\tcase 0:\n\t\t\ttotal += 2;\n\t\t\tbreak;\n\t\tcase 1:\n\t\t\tquick_print(" + (c ? "\"one %d\"" : "\"one {}\"") + ", total);\n\t\t\tbreak;\n\t\tdefault:\n\t\t\ttotal -= 1;\n\t}\n");
            sb.Append("\tint i = 0;\n\twhile (i < 10) {\n\t\tif (i > factor) {\n\t\t\ti += 2;\n\t\t\tcontinue;\n\t\t}\n\t\ti++;\n\t}\n");
            sb.Append("\treturn (int) (total * factor);\n}\n\n");
        }
        sb.Append(c ? "int main(void) {\n" : "int main() {\n").Append("\tint s = get_world_size();\n");
        for (int i = 1; i <= n; i++) sb.Append("\tharvested += work" + i + "(s, 1.5);\n");
        sb.Append(c ? "\tprintf(\"%d\\n\", harvested);\n}\n" : "\tstd::cout << harvested << std::endl;\n}\n");
        return sb.ToString();
    }

    [Theory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void Compile_300Lines_Under5ms(string id)
    {
        var src = Program300(id);
        int lines = src.Split('\n').Length;
        var l = L(id);
        var opts = new CompileOptions { FileName = "main" };
        var first = l.Compile(src, opts);
        Assert.True(first.Success, string.Join("; ", first.Errors));
        for (int i = 0; i < 30; i++) l.Compile(src, opts);
        var times = new List<double>();
        for (int i = 0; i < 100; i++)
        {
            var sw = Stopwatch.StartNew();
            l.Compile(src, opts);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        output.WriteLine($"{id} compile, {lines} lines: p25 {times[25]:F3} ms, median {times[50]:F3} ms, p90 {times[90]:F3} ms");
        Assert.True(times[25] < 5.0, $"p25 {times[25]:F3} ms for {lines} lines (limit 5 ms)");
    }
}

/// <summary>Registers a test builtin with GameApi: runs in the non-parallel GameApiRegistrations collection.</summary>
[Collection(GameApiRegistrations.Name)]
public class CFamilyRegisteredBuiltinsTests
{
    static ILanguage L(string id) => Compiler.Languages.Get(id);
    static CompileResult Ok(string id, string src) => CFamilyLanguageTests.Ok(id, src);

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n\nint main(void) {\n\tbool open = jf_test_scan(\"10.0.0.1\", 22);\n\tjf_test_scan(\"h\");\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n\nint main() {\n\tbool open = jf_test_scan(\"10.0.0.1\", 22);\n\tjf_test_scan(\"h\");\n}\n")]
    public void RegisteredBuiltins_WorkWithoutCodeChanges(string id, string src)
    {
        GameApi.Register("jf_test_scan", "host: string, port?: int", "bool", "test builtin", "test");
        try
        {
            var r = Ok(id, src);
            Assert.Contains("jf_test_scan(\"10.0.0.1\", 22)", r.Python);
            Assert.Contains("jf_test_scan", L(id).ApiStubFile(out _));
            Assert.Equal("jf_test_scan", L(id).FromApiName(L(id).ApiName("jf_test_scan")));
        }
        finally { GameApi.Unregister("jf_test_scan"); }
    }

}
