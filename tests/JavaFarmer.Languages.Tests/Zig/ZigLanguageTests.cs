using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.Zig;

/// <summary>ZigLanguage: identity, editor, naming, values, stub, diagnostics, unlock fairness, printer, cross-language files.</summary>
public class ZigLanguageTests
{
    readonly ITestOutputHelper output;
    public ZigLanguageTests(ITestOutputHelper output) { this.output = output; }

    internal static readonly ILanguage Zig = Compiler.Languages.Get("zig");
    const string Head = "const std = @import(\"std\");\nconst farm = @import(\"farm\");\n\n";
    internal static string Main(string body) => Head + "pub fn main() void {\n" + body + "\n}\n";
    static CompileOptions Opts(string name = "main", Dictionary<string, string> others = null, Dictionary<string, string> langs = null) =>
        new() { FileName = name, OtherFiles = others, OtherFileLanguages = langs };

    internal static CompileResult Ok(string src, string name = "main", Dictionary<string, string> others = null, Dictionary<string, string> langs = null)
    {
        var r = Zig.Compile(src, Opts(name, others, langs));
        Assert.True(r.Success, string.Join("; ", r.Errors.Select(e => e + " [" + HarnessTests.Slice(src, e.Start, e.End) + "]")) + "\n" + src);
        Assert.Null(GameOracle.TryParse(r.Python));
        HarnessTests.AssertMapValid(src, r);
        return r;
    }

    static Diagnostic Fail(string src, string name = "main", Dictionary<string, string> others = null, Dictionary<string, string> langs = null)
    {
        var r = Zig.Compile(src, Opts(name, others, langs));
        Assert.False(r.Success, "expected an error:\n" + r.Python);
        return r.Errors[0];
    }

    static Segment SegmentOf(CompileResult r, string pyText, int nth = 0)
    {
        int at = -1;
        for (int i = 0; i <= nth; i++) at = r.Python.IndexOf(pyText, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, "'" + pyText + "' not in\n" + r.Python);
        return r.Map.Find(at);
    }

    // ------------------------------------------------------------------ identity / editor
    [Fact]
    public void Identity()
    {
        Assert.Equal("zig", Zig.Id);
        Assert.Equal("Zig", Zig.DisplayName);
        Assert.Equal(".zig", Zig.FileExtension);
        Assert.False(Zig.IsNative);
        Assert.IsType<ZigLanguage>(Zig);
        Assert.IsAssignableFrom<IPrinterDiagnostics>(Zig);
    }

    [Fact]
    public void EditorRulesAndTemplate()
    {
        var e = Zig.Editor;
        Assert.Equal(new[] { "{" }, e.IndentAfter);
        Assert.Equal(new[] { "}" }, e.DedentOn);
        Assert.Equal("//", e.LineComment);
        Assert.Equal("\t", e.IndentUnit);
        var tpl = Zig.NewFileTemplate("main", out int caret);
        Assert.Equal("pub fn main() void {\n\t", tpl.Substring(tpl.IndexOf("pub fn", StringComparison.Ordinal), caret - tpl.IndexOf("pub fn", StringComparison.Ordinal)));
        Assert.Contains("const farm = @import(\"farm\");", tpl);
        Assert.True(Zig.Compile(tpl, Opts()).Success);
        Ok(tpl.Insert(caret, "_ = farm.harvest();"));
        foreach (var kw in new[] { "const", "var", "fn", "pub", "while", "for", "if", "else", "switch", "orelse", "return", "break", "continue", "and", "or" })
            Assert.Contains(kw, Zig.Keywords);
    }

    // ------------------------------------------------------------------ naming
    [Theory]
    [InlineData("can_harvest", "canHarvest")]
    [InlineData("do_a_flip", "doAFlip")]
    [InlineData("get_entity_type", "getEntityType")]
    [InlineData("use_item", "useItem")]
    [InlineData("quick_print", "quickPrint")]
    [InlineData("print", "std.debug.print")]
    [InlineData("min", "@min")]
    [InlineData("max", "@max")]
    [InlineData("abs", "@abs")]
    public void ApiNames_RoundTrip(string py, string zig)
    {
        Assert.Equal(zig, Zig.ApiName(py));
        Assert.Equal(py, Zig.FromApiName(zig));
    }

    [Fact]
    public void ApiNames_UnknownAndPrefixed()
    {
        Assert.Null(Zig.ApiName("no_such_builtin"));
        Assert.Null(Zig.FromApiName("noSuchBuiltin"));
        Assert.Equal("can_harvest", Zig.FromApiName("farm.canHarvest"));
        Assert.Equal("use_item", Zig.FromApiName("useItemN"));   // the variant with the optional count
    }


    // ------------------------------------------------------------------ values
    [Fact]
    public void FormatValue_LikeStdDebugPrintAny()
    {
        Assert.Equal("true", Zig.FormatValue(true));
        Assert.Equal("false", Zig.FormatValue(false));
        Assert.Equal("null", Zig.FormatValue(null));
        Assert.Equal("3", Zig.FormatValue(3.0));
        Assert.Equal("2.5", Zig.FormatValue(2.5));
        Assert.Equal("hay", Zig.FormatValue("hay"));
        Assert.Equal("{ 1, 2, 3 }", Zig.FormatValue(new GameList(new object[] { 1.0, 2.0, 3.0 })));
        Assert.Equal("{  }", Zig.FormatValue(new GameList()));
        Assert.Equal(".Carrot", Zig.FormatValue(new GameConstant("Entities", "Carrot")));
        Assert.Equal(".North", Zig.FormatValue(new GameConstant("Directions", "North")));
        Assert.Equal(".{ .x = 1, .y = 2 }", Zig.FormatValue(new GameTuple(1.0, 2.0)));
    }

    // ------------------------------------------------------------------ stub
    [Fact]
    public void Stub_DeclaresEveryBuiltin()
    {
        var stub = Zig.ApiStubFile(out var name);
        Assert.Equal("farm.zig", name);
        foreach (var b in GameApi.Builtins)
        {
            if (b.PythonName is "print" or "min" or "max" or "abs") continue;
            var z = Zig.ApiName(b.PythonName);
            Assert.Contains("pub fn " + z + "(", stub);
        }
        foreach (var e in new[] { "Entity", "Item", "Ground", "Unlock", "Hat", "Leaderboard", "Direction" })
            Assert.Contains("pub const " + e + " = enum {", stub);
        Assert.Contains("pub const allocator: std.mem.Allocator", stub);
        Assert.Contains("pub const Position = struct { x: usize, y: usize };", stub);
        Assert.Contains("pub fn getEntityType() ?Entity", stub);
        Assert.Contains("pub fn measure(comptime T: type) T", stub);
    }

    [Fact]
    public void HookStub_AddsTheMembersTheSourcesUse()
    {
        var stub = ZigHooks.Stub(new[] { "_ = farm.plant(.Golden_Thing); farm.changeHat(farm.Hat.Fancy_Hat);" });
        Assert.Contains("Golden_Thing,", stub);
        Assert.Contains("Fancy_Hat,", stub);
        var forced = ZigHooks.ForceAnalysis("const farm = @import(\"farm\");\nvar n: i32 = 0;\nfn helper() void {}\npub fn main() void {}\n", "main.zig");
        Assert.Contains("_ = &helper;", forced);
        Assert.Contains("_ = &n;", forced);
        Assert.DoesNotContain("_ = &main;", forced);
        Assert.DoesNotContain("_ = &farm;", forced);
    }

    // ------------------------------------------------------------------ diagnostics
    [Theory]
    [InlineData("\tconst x: i32 = 3;", "unused local constant", "x")]
    [InlineData("\tvar x: i32 = 3;\n\t_ = x;", "local variable is never mutated", "x")]
    [InlineData("\tfarm.harvest();", "value of type 'bool' ignored", "farm.harvest()")]
    [InlineData("\tvar i = 0;\n\ti += 1;", "variable of type 'comptime_int' must be const or comptime", "0")]
    [InlineData("\tconst a: i32 = @intCast(farm.getPosX());\n\t_ = a / 2;", "signed integers must use @divTrunc, @divFloor, or @divExact", "/")]
    [InlineData("\tconst a: i32 = @intCast(farm.getPosX());\n\t_ = a + farm.getPosY();", "incompatible types: 'i32' and 'usize'", "a + farm.getPosY()")]
    [InlineData("\t_ = farm.plant(.Carrot, 1);", "expected 1 argument(s), found 2", "farm.plant(.Carrot, 1)")]
    [InlineData("\tconst __jx = 1;\n\t_ = __jx;", "identifiers starting with '__j' are reserved by JavaFarmer", "__jx")]
    [InlineData("\tstd.debug.print(\"{d}\\n\", .{});", "too few arguments", ".{}")]
    [InlineData("\tstd.debug.print(\"hi\\n\", .{1});", "unused argument in", "1")]
    [InlineData("\tif (farm.getEntityType() == .Bush) {}", "operator == not allowed for type '?farm.Entity'", "farm.getEntityType() == .Bush")]
    public void Diagnostics_UseZigWording(string body, string msg, string at)
    {
        var src = Main(body);
        var e = Fail(src);
        Assert.Contains(msg, e.Message);
        Assert.Equal(at, HarnessTests.Slice(src, e.Start, e.End));
    }

    [Fact]
    public void UnsupportedButValid_SaysNotSupported()
    {
        foreach (var body in new[] { "\tconst x: u32 = 1 << 3;\n\t_ = x;", "\touter: while (true) { break :outer; }", "\tdefer farm.clear();" })
            Assert.Contains("not supported in JavaFarmer", Fail(Main(body)).Message);
    }

    [Fact]
    public void AnyConstantMemberName_BuildsAnIrConst()
    {
        var r = Ok(Main("\t_ = farm.plant(.Golden_Thing);\n\t_ = farm.useItem(farm.Item.Brand_New);"));
        Assert.Contains("plant(Entities.Golden_Thing)", r.Python);
        Assert.Contains("use_item(Items.Brand_New)", r.Python);
    }

    [Fact]
    public void MixedIntegerComparison_IsAllowedLikeZig()
    {
        var r = Ok(Main("\tconst n: i32 = 3;\n\tif (farm.getPosX() < n) {\n\t\t_ = farm.harvest();\n\t}"));
        Assert.Contains("if get_pos_x() < n:", r.Python);
    }

    [Fact]
    public void FailedSemaDoesNotReportSpuriousUnusedLocals()
    {
        // getPtr is not supported; x and y are still used (AstGen decides syntactically)
        var src = Head + "pub fn main() void {\n\tvar m = std.AutoHashMap(i32, farm.Position).init(farm.allocator);\n\tfor (0..3) |x| {\n\t\tfor (0..3) |y| {\n\t\t\tm.getPtr(1).?.* = .{ .x = x, .y = y };\n\t\t}\n\t}\n}\n";
        Assert.Contains("getPtr is not supported", Fail(src).Message);
    }

    // ------------------------------------------------------------------ unlock fairness
    [Fact]
    public void Scaffolding_IsFree()
    {
        var r = Ok(Main("\t_ = farm.harvest();"));
        var def = SegmentOf(r, "def main():");
        Assert.Equal(SegmentKind.Synthetic, def.Kind);
        Assert.True(def.NoHighlight);
        var guard = SegmentOf(r, "if __name__");
        Assert.Equal(SegmentKind.Synthetic, guard.Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "harvest()").Kind);
        Assert.DoesNotContain("import", r.Python);   // @import lines have no IR node
    }

    [Fact]
    public void OtherFileImport_CostsImports()
    {
        var others = new Dictionary<string, string> { ["utils"] = "pub fn f() void {}\n" };
        var r = Ok(Head + "const utils = @import(\"utils.zig\");\n\npub fn main() void {\n\tutils.f();\n}\n", "main", others);
        Assert.Contains("import utils", r.Python);
        Assert.Contains("utils.f()", r.Python);
    }

    [Fact]
    public void WhileWithContinueExpression_IsAWhileNotAFor()
    {
        var r = Ok(Main("\tvar i: i32 = 0;\n\twhile (i < 5) : (i += 1) {\n\t\tif (i == 2) continue;\n\t\t_ = farm.harvest();\n\t}"));
        var w = SegmentOf(r, "while i < 5:");
        Assert.Equal(SegmentKind.User, w.Kind);
        Assert.NotEqual("for", w.DependencyOverride);
        // the continue expression runs before every continue and at the end of the body
        Assert.Equal(2, r.Python.Split("i += 1").Length - 1);
    }

    [Fact]
    public void RangeAndForEachLoops_AreForLoops()
    {
        var r = Ok(Main("\tfor (0..3) |_| {\n\t\t_ = farm.harvest();\n\t}\n\tconst xs = [_]i32{ 1, 2 };\n\tfor (xs) |x| {\n\t\tfarm.quickPrint(\"{d}\\n\", .{x});\n\t}"));
        Assert.Contains("for _ in range(3):", r.Python);
        Assert.Contains("for x in xs:", r.Python);
    }

    [Fact]
    public void Print_ChargesOnlyPrint()
    {
        var r = Ok(Main("\tconst x: i32 = 4;\n\tstd.debug.print(\"x = {d} and {d}\\n\", .{ x, x + 1 });"));
        Assert.Contains("print(\"x = \" + str(x) + \" and \" + str(x + 1))", r.Python);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "print(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "str(x)").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, " + ").Kind);
    }

    [Fact]
    public void SignedDivisionBuiltins_PickTheMatchingPythonOps()
    {
        var r = Ok(Main("\tconst a: i32 = @intCast(farm.getPosX());\n\tfarm.quickPrint(\"{d} {d} {d} {d}\\n\", .{ @divFloor(a, 2), @mod(a, 2), @divTrunc(a, 2), @rem(a, 2) });"));
        Assert.Contains("a // 2", r.Python);
        Assert.Contains("a % 2", r.Python);
        Assert.Contains("__jdiv(a, 2)", r.Python);
    }

    // ------------------------------------------------------------------ printer
    static string FromPython(string py, bool snippet = false)
    {
        var ir = Compiler.Languages.Get("python").ToIr(py, Opts(), out var errs);
        Assert.True(ir != null, string.Join("; ", errs));
        var problems = new List<string>();
        var text = ((IPrinterDiagnostics)Zig).Print(ir, new PrintOptions { FileName = "main", Snippet = snippet }, out var pr);
        Assert.DoesNotContain(pr, p => p.Contains("internal printer error"));
        return text;
    }

    [Fact]
    public void PrintsPythonAsAZigProgram()
    {
        var text = FromPython("count = 0\nfor i in range(3):\n\tif can_harvest():\n\t\tharvest()\n\t\tcount += 1\n\tmove(North)\nprint(count, 'x')\n");
        output.WriteLine(text);
        Assert.Contains("pub fn main() void {", text);
        Assert.Contains("for (0..3) |_| {", text);
        Assert.Contains("_ = farm.harvest();", text);
        Assert.Contains("_ = farm.move(.North);", text);
        Ok(text);
    }

    [Fact]
    public void SnippetMode_PrintsBareIdiomaticStatements()
    {
        var text = FromPython("if get_entity_type() == Entities.Bush:\n\tharvest()\n", snippet: true);
        Assert.DoesNotContain("pub fn main", text);
        Assert.DoesNotContain("@import", text);
        Assert.StartsWith("if (", text);
        Assert.Equal("farm.canHarvest()", FromPython("can_harvest()", snippet: true).Trim());
    }

    [Fact]
    public void Printer_UsesIfCaptureOrelseAndContinueExpressions()
    {
        var src = Main("\tif (farm.getCompanion()) |c| {\n\t\tfarm.quickPrint(\"{d}\\n\", .{c.x});\n\t}\n\tvar n: i32 = 0;\n\twhile (n < 4) : (n += 1) {\n\t\tconst d = farm.spawnDrone(work) orelse continue;\n\t\t_ = farm.waitFor(i32, d);\n\t}") + "\nfn work() i32 {\n\treturn 1;\n}\n";
        var ir = Zig.ToIr(src, Opts(), out var errs);
        Assert.NotNull(ir);
        var text = Zig.Print(ir, new PrintOptions { FileName = "main" });
        output.WriteLine(text);
        Assert.Contains("if (farm.getCompanion()) |c| {", text);
        Assert.Contains("while (n < 4) : (n += 1) {", text);
        Assert.Contains("orelse continue;", text);
        Ok(text);
    }

    [Fact]
    public void Printer_KeepsComments()
    {
        var src = Head + "// tends the farm\npub fn main() void {\n\t// first harvest\n\t_ = farm.harvest(); // trailing\n}\n";
        var text = Zig.Print(Zig.ToIr(src, Opts(), out _), new PrintOptions { FileName = "main" });
        Assert.Contains("// tends the farm", text);
        Assert.Contains("// first harvest", text);
        Assert.Contains("// trailing", text);
    }

    [Fact]
    public void Printer_FillsInDefaultArguments()
    {
        var text = FromPython("def f(a = False):\n\tif a:\n\t\tdo_a_flip()\nf()\nf(True)\n");
        Assert.Contains("f(false);", text);
        Ok(text);
    }

    // ------------------------------------------------------------------ cross-language files (LANGUAGE-GUIDE §9)
    const string JavaUtils = "public class Utils {\n    public static int counter = 0;\n    public static int twice(int x) { return 2 * x; }\n}\n";
    const string PyBot = "def pick(k):\n\treturn k + 1\n\ndef hello():\n\tprint('hi')\n";

    [Fact]
    public void CallsAJavaFileAndAPythonFile()
    {
        var others = new Dictionary<string, string> { ["Utils"] = JavaUtils, ["bot"] = PyBot };
        var langs = new Dictionary<string, string> { ["Utils"] = "java", ["bot"] = "python" };
        var src = Head + "const utils = @import(\"Utils.java\");\nconst bot = @import(\"bot\");\n\npub fn main() void {\n\tconst n: i32 = utils.twice(3);\n\tutils.counter += n;\n\tconst v: i32 = bot.pick(2);\n\tbot.hello();\n\tfarm.quickPrint(\"{d} {d}\\n\", .{ n, v });\n}\n";
        var r = Ok(src, "main", others, langs);
        Assert.Contains("import Utils", r.Python);
        Assert.Contains("Utils.twice(3)", r.Python);
        Assert.Contains("Utils.counter += n", r.Python);
        Assert.Contains("bot.pick(2)", r.Python);
        // a Java int parameter is an i32 in Zig
        Assert.Contains("expected type 'i32', found", Fail(src.Replace("utils.twice(3)", "utils.twice(true)"), "main", others, langs).Message);
        // a Python result has no Zig type: the declaration must give one
        Assert.Contains("has no Zig type", Fail(src.Replace("const v: i32 = bot.pick(2);", "const v = bot.pick(2);"), "main", others, langs).Message);
    }

    [Fact]
    public void ForeignFileThatDoesNotCompile_IsReportedAtTheImport()
    {
        var others = new Dictionary<string, string> { ["broken"] = "def f(:\n\tpass\n" };
        var langs = new Dictionary<string, string> { ["broken"] = "python" };
        var src = "const broken = @import(\"broken.py\");\n\npub fn main() void {\n\tbroken.f();\n}\n";
        var e = Fail(src, "main", others, langs);
        Assert.StartsWith("cannot use broken: broken (Python) does not compile", e.Message);
        Assert.Equal("\"broken.py\"", HarnessTests.Slice(src, e.Start, e.End));
    }

    [Fact]
    public void JavaCallsAZigFile()
    {
        var others = new Dictionary<string, string> { ["helpers"] = "const farm = @import(\"farm\");\npub var total: i32 = 0;\npub fn square(x: i32) i32 {\n\ttotal += 1;\n\treturn x * x;\n}\n" };
        var langs = new Dictionary<string, string> { ["helpers"] = "zig" };
        var java = Compiler.Languages.Get("java");
        var r = java.Compile("void main() {\n    int s = helpers.square(4);\n    quickPrint(s, helpers.total);\n}\n", new CompileOptions { FileName = "Main", OtherFiles = others, OtherFileLanguages = langs });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("helpers.square(4)", r.Python);
    }

    // ------------------------------------------------------------------ highlighter
    [Fact]
    public void Highlight_CoversEverythingAndClassifiesTokens()
    {
        var src = Main("\t// note\n\tconst e = farm.Entity.Carrot;\n\t_ = farm.plant(.Bush);\n\tstd.debug.print(\"{d}\\n\", .{@abs(-3)});\n\t_ = e;");
        var toks = Zig.Highlight(src);
        Assert.Equal(src.Length, toks.Sum(t => t.Length));
        TokenKind KindOf(string text) { int at = src.IndexOf(text, StringComparison.Ordinal); return toks.First(t => t.Start <= at && at < t.Start + t.Length).Kind; }
        Assert.Equal(TokenKind.Keyword, KindOf("const"));
        Assert.Equal(TokenKind.Comment, KindOf("// note"));
        Assert.Equal(TokenKind.String, KindOf("\"{d}"));
        Assert.Equal(TokenKind.Constant, KindOf("Carrot"));
        Assert.Equal(TokenKind.Constant, KindOf("Bush"));
        Assert.Equal(TokenKind.Number, KindOf("3)"));
    }
}

/// <summary>Registers a test builtin with GameApi: runs in the non-parallel GameApiRegistrations collection.</summary>
[Collection(GameApiRegistrations.Name)]
public class ZigRegisteredBuiltinsTests
{
    static ILanguage Zig => ZigLanguageTests.Zig;
    static string Main(string body) => ZigLanguageTests.Main(body);
    static CompileResult Ok(string src) => ZigLanguageTests.Ok(src);

    [Fact]
    public void RegisteredBuiltins_AreNamedCompiledAndInTheStub()
    {
        GameApi.Register("scan_port_zig", "host: string, port?: int", "bool", "Scans a port.", "cybersecurity");
        try
        {
            Assert.Equal("scanPortZig", Zig.ApiName("scan_port_zig"));
            Assert.Equal("scan_port_zig", Zig.FromApiName("scanPortZig"));
            var stub = Zig.ApiStubFile(out _);
            Assert.Contains("pub fn scanPortZig(host: []const u8) bool", stub);
            Assert.Contains("pub fn scanPortZigPort(host: []const u8, port: usize) bool", stub);
            var r = Ok(Main("\tif (farm.scanPortZig(\"10.0.0.1\")) {\n\t\t_ = farm.scanPortZigPort(\"10.0.0.1\", 22);\n\t}"));
            Assert.Contains("scan_port_zig(\"10.0.0.1\", 22)", r.Python);
        }
        finally { GameApi.Unregister("scan_port_zig"); }
        Assert.Null(Zig.ApiName("scan_port_zig"));
    }

}
