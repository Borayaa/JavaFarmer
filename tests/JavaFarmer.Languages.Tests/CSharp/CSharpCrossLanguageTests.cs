namespace JavaFarmer.Languages.Tests.CSharp;

/// <summary>
/// Cross-language imports (docs/LANGUAGE-GUIDE.md §9): a C# window calls functions / reads globals of windows written in
/// Java and Python (through ForeignModules signatures, never by parsing them as C#), foreign files that do not compile
/// give a clear error at the use site, and other languages call C# windows (ISignatureProvider).
/// </summary>
[Collection("csharp-roslyn")]
public class CSharpCrossLanguageTests
{
    static readonly ILanguage Cs = Compiler.Languages.Get("csharp");

    static CompileResult CompileCs(string src, Dictionary<string, string> files, Dictionary<string, string> langs)
    {
        ForeignModules.ClearCache();
        return Cs.Compile(src, new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });
    }

    static string Errors(CompileResult r, string src) => string.Join("; ", r.Errors.Select(e => e.Message + " [" + HarnessTests.Slice(src, e.Start, e.End) + "]"));

    [Fact]
    public void CSharp_CallsAJavaFile_WithItsTypes()
    {
        var java = "public class Utils {\n\tstatic int count = 0;\n\tstatic int twice(int n) { return n * 2; }\n\tstatic String name() { return \"bot\"; }\n}\n";
        var src = "int x = Utils.twice(3) + Utils.count;\nstring s = Utils.name();\nConsole.WriteLine(s + x);\n";
        var r = CompileCs(src, new() { ["Utils"] = java }, new() { ["Utils"] = "java" });
        Assert.True(r.Success, Errors(r, src));
        Assert.Contains("import Utils", r.Python);
        Assert.Contains("Utils.twice(3) + Utils.count", r.Python);
        Assert.Null(GameOracle.TryParse(r.Python));
        // the Java types are checked: twice takes an int
        var bad = CompileCs("Utils.twice(\"a\");\n", new() { ["Utils"] = java }, new() { ["Utils"] = "java" });
        Assert.False(bad.Success);
        Assert.Contains("CS1503: Argument 1: cannot convert from 'string' to 'int'", bad.Errors[0].Message);
    }

    [Fact]
    public void CSharp_CallsAPythonFile_DynamicValuesAreObject()
    {
        var py = "count = 0\ndef twice(n):\n\treturn n * 2\ndef hello():\n\tprint('hi')\ndef list_j():\n\treturn [1]\n";
        var src = "helpers.hello();\nint x = (int)helpers.twice(3);\nobject c = helpers.count;\nConsole.WriteLine(x);\n";
        var r = CompileCs(src, new() { ["helpers"] = py }, new() { ["helpers"] = "python" });
        Assert.True(r.Success, Errors(r, src));
        Assert.Contains("helpers.hello()", r.Python);
        Assert.Contains("helpers.twice(3)", r.Python);
        Assert.Contains("import helpers", r.Python);
        Assert.Null(GameOracle.TryParse(r.Python));
    }

    [Fact]
    public void ForeignFileErrors_AreClear()
    {
        var r = CompileCs("utils.Go();\n", new() { ["utils"] = "def go(:\n" }, new() { ["utils"] = "python" });
        Assert.False(r.Success);
        Assert.Contains("cannot use utils", r.Errors[0].Message);
        Assert.Contains("does not compile", r.Errors[0].Message);
        Assert.Equal("utils", HarnessTests.Slice("utils.Go();\n", r.Errors[0].Start, r.Errors[0].End));

        r = CompileCs("utils.Go();\n", new() { ["utils"] = "whatever" }, new() { ["utils"] = "cobol" });
        Assert.False(r.Success);
        Assert.Contains("does not support", r.Errors[0].Message);

        r = CompileCs("utils.nothing();\n", new() { ["utils"] = "def go():\n\tpass\n" }, new() { ["utils"] = "python" });
        Assert.False(r.Success);
        Assert.Contains("CS0117: 'utils' does not contain a definition for 'nothing'", r.Errors[0].Message);

        // a Java file is never parsed as C# (it would not parse)
        r = CompileCs("Lib.go();\n", new() { ["Lib"] = "void main() {\n\tharvest(\n}\n" }, new() { ["Lib"] = "java" });
        Assert.False(r.Success);
        Assert.Contains("cannot use Lib: Lib (Java) does not compile", r.Errors[0].Message);
    }

    [Fact]
    public void Java_CallsAHandWrittenCSharpLibrary()
    {
        ForeignModules.ClearCache();
        string cs = "public static class Flips\n{\n\tpublic static int count = 0;\n\n\tpublic static void FlipTimes(int n)\n\t{\n\t\tfor (int i = 0; i < n; i++)\n\t\t{\n\t\t\tDoAFlip();\n\t\t\tcount++;\n\t\t}\n\t}\n}\n";
        var sig = ForeignModules.Resolve("Flips", cs, "csharp", new CompileOptions { FileName = "Flips" });
        Assert.True(sig.Ok, sig.Error);
        Assert.Equal(IrTypeKind.Int, sig.Function("FlipTimes").Params[0].Type.Kind);
        var opts = new CompileOptions { FileName = "main", OtherFiles = new Dictionary<string, string> { ["Flips"] = cs }, OtherFileLanguages = new Dictionary<string, string> { ["Flips"] = "csharp" } };
        var r = Compiler.Languages.Get("java").Compile("void main() {\n\tFlips.FlipTimes(2);\n\tSystem.out.println(Flips.count);\n}\n", opts);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        // the names the C# library's compiled Python defines
        var lib = Cs.Compile(cs, new CompileOptions { FileName = "Flips" });
        Assert.Contains("def FlipTimes(n):", lib.Python);
        Assert.Contains("Flips.FlipTimes(2)", r.Python);
        Assert.Contains("Flips.count", r.Python);
    }

    [Fact]
    public void Java_CallsAJavaHelperPrintedInCSharp()
    {
        // the plugin test Java_CallsAFileOfEveryLanguage: a Java library printed in C# keeps its member names
        ForeignModules.ClearCache();
        string helperJava = "public class helper {\n\tstatic int twice(int n) {\n\t\treturn n * 2;\n\t}\n}\n";
        var ir = Compiler.Languages.Get("java").ToIr(helperJava, new CompileOptions { FileName = "helper" }, out _);
        string text = Cs.Print(ir, new PrintOptions { FileName = "helper" });
        Assert.Contains("public static class helper", text);
        Assert.Contains("public static int twice(int n)", text);
        Assert.True(Cs.Compile(text, new CompileOptions { FileName = "helper" }).Success, text);
        var sig = ForeignModules.Resolve("helper", text, "csharp", new CompileOptions { FileName = "helper" });
        Assert.True(sig.Ok, sig.Error);
        var opts = new CompileOptions { FileName = "main", OtherFiles = new Dictionary<string, string> { ["helper"] = text }, OtherFileLanguages = new Dictionary<string, string> { ["helper"] = "csharp" } };
        var r = Compiler.Languages.Get("java").Compile("void main() {\n\tSystem.out.println(helper.twice(4));\n}\n", opts);
        Assert.True(r.Success, string.Join("; ", r.Errors) + "\n" + text);
        Assert.Contains("helper." + sig.Function("twice").PyName + "(4)", r.Python);
        Assert.True(CSharpRoslyn.Compile(new Dictionary<string, string> { ["helper.cs"] = text }).Success);
    }

    [Fact]
    public void Python_AndJava_SeeATopLevelCSharpWindowsFunctions()
    {
        ForeignModules.ClearCache();
        string cs = "int total = 0;\nint Twice(int n) => n * 2;\nConsole.WriteLine(Twice(2));\n";
        var sig = ForeignModules.Resolve("bot", cs, "csharp", new CompileOptions { FileName = "bot" });
        Assert.True(sig.Ok, sig.Error);
        Assert.NotNull(sig.Function("Twice"));
        Assert.Equal(IrTypeKind.Int, sig.Global("total").Type.Kind);
        var bad = ForeignModules.Resolve("bot", "int x = ;\n", "csharp", new CompileOptions { FileName = "bot" });
        Assert.False(bad.Ok);
        Assert.Contains("bot (C#) does not compile (line 1", bad.Error);
    }

    [Fact]
    public void CSharpFilesImportingEachOther_ThroughAnotherLanguage()
    {
        // C# main → Java Lib → C# Shared: every file is read with its own frontend
        ForeignModules.ClearCache();
        var files = new Dictionary<string, string>
        {
            ["Lib"] = "public class Lib {\n\tstatic int get() { return Shared.Base + 1; }\n}\n",
            ["Shared"] = "public static class Shared\n{\n\tpublic const int Base = 41;\n}\n",
        };
        var langs = new Dictionary<string, string> { ["Lib"] = "java", ["Shared"] = "csharp" };
        var src = "Console.WriteLine(Lib.get() + Shared.Base);\n";
        var r = Cs.Compile(src, new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });
        Assert.True(r.Success, Errors(r, src));
        Assert.Contains("Lib.get() + Shared.Base", r.Python);
    }
}
