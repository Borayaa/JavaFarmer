using static JavaFarmer.Languages.Tests.R.RTestUtil;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>LANGUAGE-GUIDE §9: R files use windows of any language through source("Name.ext"), and other languages use R files.</summary>
public class RForeignTests
{
    static CompileResult CompileWith(string src, Dictionary<string, string> files, Dictionary<string, string> langs) =>
        Lang.Compile(src, new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });

    [Fact]
    public void R_CallsAJavaFile()
    {
        ForeignModules.ClearCache();
        var files = new Dictionary<string, string> { ["Utils"] = "public class Utils {\n\tstatic int count = 3;\n\tstatic int twice(int n) {\n\t\treturn n * 2;\n\t}\n}\n" };
        var langs = new Dictionary<string, string> { ["Utils"] = "java" };
        var sig = ForeignModules.Resolve("Utils", new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });
        Assert.True(sig.Ok, sig.Error);
        var r = CompileWith("source(\"Utils.java\")\nx <- twice(4) + 1\nprint(x)\nprint(count)\n", files, langs);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("Utils." + sig.Function("twice")!.PyName + "(4)", r.Python);
        Assert.Contains("Utils." + sig.Global("count")!.PyName, r.Python);
        // the Java signature types the call (int)
        var ir = Lang.ToIr("source(\"Utils.java\")\nx <- twice(4)\n", new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs }, out var errs);
        Assert.Empty(errs);
        var call = IrWalk.Descendants(ir).OfType<IrCall>().Single(c => c.Function?.Module == "Utils");
        Assert.True(call.Type?.IsInt ?? false, Dump(call, true));
        // argument checks use the foreign parameters
        var bad = CompileWith("source(\"Utils.java\")\nprint(twice(1, 2))\n", files, langs);
        Assert.False(bad.Success);
        Assert.Contains("unused argument (2)", bad.Errors[0].Message);
    }

    [Fact]
    public void R_CallsAPythonFile()
    {
        ForeignModules.ClearCache();
        var files = new Dictionary<string, string> { ["bot"] = "limit = 5\n\ndef greet(name, times=1):\n    return name * times\n" };
        var langs = new Dictionary<string, string> { ["bot"] = "python" };
        var r = CompileWith("source(\"bot.py\")\nprint(greet(\"a\"))\nprint(greet(\"a\", times = 2))\nprint(limit)\n", files, langs);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("bot.greet(\"a\")", r.Python);
        Assert.Contains("bot.greet(\"a\", 2)", r.Python);
        Assert.Contains("bot.limit", r.Python);
    }

    [Fact]
    public void AForeignFileThatDoesNotCompile_IsAClearErrorAtTheSource()
    {
        ForeignModules.ClearCache();
        var src = "source(\"Utils.java\")\nprint(twice(4))\n";
        var r = CompileWith(src, new Dictionary<string, string> { ["Utils"] = "public class Utils {\n\tstatic int twice(int n) {\n\t\treturn n * ;\n\t}\n}\n" },
            new Dictionary<string, string> { ["Utils"] = "java" });
        Assert.False(r.Success);
        Assert.Contains("cannot use Utils", r.Errors[0].Message);
        Assert.Contains("does not compile", r.Errors[0].Message);
        Assert.Equal("\"Utils.java\"", Snip(src, r.Errors[0]));
    }

    [Fact]
    public void Java_CallsAnRFile()
    {
        ForeignModules.ClearCache();
        var files = new Dictionary<string, string> { ["helper"] = "limit <- 3\ntwice <- function(n) n * 2\n" };
        var langs = new Dictionary<string, string> { ["helper"] = "r" };
        var sig = ForeignModules.Resolve("helper", new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });
        Assert.True(sig.Ok, sig.Error);
        Assert.NotNull(sig.Function("twice"));
        Assert.NotNull(sig.Global("limit"));
        var java = Compiler.Languages.Get("java").Compile("void main() {\n\tSystem.out.println(helper.twice(4));\n\tSystem.out.println(helper.limit);\n}\n",
            new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });
        Assert.True(java.Success, string.Join("; ", java.Errors));
        Assert.Contains("helper." + sig.Function("twice")!.PyName + "(4)", java.Python);
    }

    [Fact]
    public void RFilesWithoutALanguageEntry_AreStillR()
    {
        var others = new Dictionary<string, string> { ["utils"] = "helper <- function(a) a + 1\n" };
        Assert.Equal("import utils\nprint(utils.helper(1))", Py("source(\"utils.R\")\nprint(helper(1))", others));
    }
}
