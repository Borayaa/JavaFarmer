namespace JavaFarmer.Languages.Tests;

/// <summary>LANGUAGE-GUIDE §9: C / C++ windows use windows of any language (prototypes / extern), and are used by them.</summary>
public class CFamilyForeignTests
{
    const string JavaUtils = "int twice(int x) {\n\treturn x * 2;\n}\n\nint limit = 5;\n";
    const string PyHelpers = "count = 3\ndef rows():\n\treturn 4\ndef list(n):\n\treturn n\n";

    static CompileResult Compile(string id, string src, Dictionary<string, string> files, Dictionary<string, string> langs) =>
        Compiler.Languages.Get(id).Compile(src, new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });

    static string Errors(CompileResult r) => string.Join("; ", r.Errors);

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n\nint twice(int x);\nextern int limit;\nint rows(void);\nint list(int n);\nextern int count;\n\nint main(void) {\n\tint a = twice(limit) + rows() + list(count);\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n\nint twice(int x);\nextern int limit;\nint rows();\nint list(int n);\nextern int count;\n\nint main() {\n\tint a = twice(limit) + rows() + list(count);\n}\n")]
    public void CallsJavaAndPythonWindows(string id, string src)
    {
        ForeignModules.ClearCache();
        var files = new Dictionary<string, string> { ["Utils"] = JavaUtils, ["helpers"] = PyHelpers };
        var langs = new Dictionary<string, string> { ["Utils"] = "java", ["helpers"] = "python" };
        var r = Compile(id, src, files, langs);
        Assert.True(r.Success, Errors(r));
        Assert.Contains("Utils.twice(Utils.limit)", r.Python);
        Assert.Contains("helpers.rows()", r.Python);
        // the Python file defines "list" under its own (renamed) Python name
        Assert.Contains("helpers.list(helpers.count)", r.Python.Replace("list_j", "list"));
        Assert.Contains("import Utils", r.Python);
        Assert.Contains("import helpers", r.Python);
        Assert.Null(GameOracle.TryParse(r.Python));
    }

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n\ndouble twice(int x);\n\nint main(void) {\n\ttwice(2);\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n\ndouble twice(int x);\n\nint main() {\n\ttwice(2);\n}\n")]
    public void PrototypeMustMatchATypedForeignWindow(string id, string src)
    {
        ForeignModules.ClearCache();
        var r = Compile(id, src, new() { ["Utils"] = JavaUtils }, new() { ["Utils"] = "java" });
        Assert.False(r.Success);
        Assert.Contains("conflicting types for 'twice'; the code window Utils (Java) defines int twice(int)", r.Errors[0].Message);
    }

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n\nvoid go(void);\n\nint main(void) {\n\tgo();\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n\nvoid go();\n\nint main() {\n\tgo();\n}\n")]
    public void BrokenForeignWindow_IsReportedAtTheDeclaration(string id, string src)
    {
        ForeignModules.ClearCache();
        var r = Compile(id, src, new() { ["utils"] = "def go(:\n" }, new() { ["utils"] = "python" });
        Assert.False(r.Success);
        Assert.Contains("cannot use 'go': ", r.Errors[0].Message);
        Assert.Contains("utils (Python) does not compile", r.Errors[0].Message);
        Assert.Equal("go", HarnessTests.Slice(src, r.Errors[0].Start, r.Errors[0].End));
    }

    [Fact]
    public void CCallsACppWindow_AndCppCallsACWindow()
    {
        ForeignModules.ClearCache();
        var cpp = "#include \"farm.hpp\"\n#include <string>\n\nstd::string label(int n) {\n\treturn \"row \" + std::to_string(n);\n}\n";
        var r = Compile("c", "#include \"farm.h\"\n#include <stdio.h>\n\nconst char *label(int n);\n\nint main(void) {\n\tprintf(\"%s\\n\", label(2));\n}\n",
            new() { ["names"] = cpp }, new() { ["names"] = "cpp" });
        Assert.True(r.Success, Errors(r));
        Assert.Contains("names.label(2)", r.Python);
        var c = "#include \"farm.h\"\n\nint total = 0;\n\nvoid add(int n) {\n\ttotal += n;\n}\n";
        r = Compile("cpp", "#include \"farm.hpp\"\n\nvoid add(int n);\nextern int total;\n\nint main() {\n\tadd(total);\n}\n",
            new() { ["sums"] = c }, new() { ["sums"] = "c" });
        Assert.True(r.Success, Errors(r));
        Assert.Contains("sums.add_j(sums.total)", r.Python);   // "add" is reserved in game Python: the C window defines add_j
    }

    [Theory]
    [InlineData("c", "#include \"farm.h\"\n\nint harvested = 0;\nstatic int secret = 1;\n\nint harvest_row(int n) {\n\tharvested += n;\n\treturn harvested;\n}\n\nstatic void hidden(void) {\n}\n")]
    [InlineData("cpp", "#include \"farm.hpp\"\n#include <vector>\n\nint harvested = 0;\n\nint harvest_row(int n) {\n\tharvested += n;\n\treturn harvested;\n}\n\nstd::vector<int> rows(int n) {\n\treturn std::vector<int>(n, 0);\n}\n")]
    public void Signatures_AreTypedAndUsableFromJava(string id, string src)
    {
        ForeignModules.ClearCache();
        var sig = ((ISignatureProvider)Compiler.Languages.Get(id)).Signatures(src, new CompileOptions { FileName = "farming" });
        Assert.True(sig.Ok, sig.Error);
        var f = sig.Function("harvest_row");
        Assert.NotNull(f);
        Assert.Equal(IrTypeKind.Int, f.ReturnType.Kind);
        Assert.Equal(IrTypeKind.Int, f.Params[0].Type.Kind);
        Assert.Equal(IrTypeKind.Int, sig.Global("harvested").Type.Kind);
        Assert.Null(sig.Function("hidden"));       // static: private to the window
        Assert.Null(sig.Function("main"));
        var r = JavaCompiler.Compile("void main() {\n\tint n = farming.harvest_row(3);\n\tSystem.out.println(farming.harvested + n);\n}\n",
            new CompileOptions { FileName = "main", OtherFiles = new Dictionary<string, string> { ["farming"] = src }, OtherFileLanguages = new Dictionary<string, string> { ["farming"] = id } });
        Assert.True(r.Success, Errors(r));
        Assert.Contains("farming.harvest_row(3)", r.Python);
        Assert.Null(GameOracle.TryParse(r.Python));
        var bad = ((ISignatureProvider)Compiler.Languages.Get(id)).Signatures("int f( {", new CompileOptions { FileName = "farming" });
        Assert.False(bad.Ok);
        Assert.Contains("does not compile (line 1", bad.Error);
    }
}
