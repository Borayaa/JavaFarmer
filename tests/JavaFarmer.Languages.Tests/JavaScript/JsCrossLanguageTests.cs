using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>
/// docs/LANGUAGE-GUIDE.md §9: JavaScript windows use windows written in other languages through ForeignModules
/// signatures (never by parsing them as JavaScript), and other languages use JavaScript windows (ISignatureProvider).
/// </summary>
public class JsCrossLanguageTests
{
    const string JavaUtils = "public class Utils {\n\tstatic int count = 3;\n\tstatic int twice(int n) {\n\t\treturn n * 2;\n\t}\n\tstatic String label() {\n\t\treturn \"x\";\n\t}\n}\n";
    const string PyHelper = "limit = 5\ndef add(a, b=1):\n\treturn a + b\n";
    const string JsUtils = "export let moves = 0;\nexport function goTo(x, y) {\n\tmoves++;\n}\nfunction hidden() {\n\treturn 1;\n}\n";

    static CompileResult Js(string src, Dictionary<string, string> files, Dictionary<string, string> langs)
    {
        ForeignModules.ClearCache();
        return Lang.Compile(src, new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });
    }

    static string Ok(CompileResult r)
    {
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return r.Python;
    }

    [Fact]
    public void Js_CallsAJavaFile()
    {
        var files = new Dictionary<string, string> { ["Utils"] = JavaUtils };
        var langs = new Dictionary<string, string> { ["Utils"] = "java" };
        var py = Ok(Js("import * as Utils from \"./Utils.java\";\nconsole.log(Utils.twice(2), Utils.count);\nconst s = Utils.label() + 1;\n", files, langs));
        Assert.Contains("import Utils", py);
        Assert.Contains("print(Utils.twice(2), Utils.count)", py);
        // the Java signature types label() as a (nullable) string: + is concatenation, not addition
        Assert.True(py.Contains("Utils.label()) + str(1)"), py);
        // the import specifier may also use .js
        Ok(Js("import { twice } from \"./Utils.js\";\nconsole.log(twice(1));\n", files, langs));
        Assert.Equal("Utils.twice() expects 1 argument, but got 0", Js("import * as Utils from \"./Utils.java\";\nUtils.twice();\n", files, langs).Errors[0].Message);
        Assert.Equal("The requested module './Utils.java' does not provide an export named 'nope'", Js("import { nope } from \"./Utils.java\";\n", files, langs).Errors[0].Message);
    }

    [Fact]
    public void Js_CallsAPythonFile()
    {
        var files = new Dictionary<string, string> { ["helper"] = PyHelper };
        var langs = new Dictionary<string, string> { ["helper"] = "python" };
        var py = Ok(Js("import { add, limit } from \"./helper.py\";\nimport * as helper from \"./helper.py\";\nconsole.log(add(1), add(1, 2), limit, helper.limit);\n", files, langs));
        Assert.Contains("print(helper.add(1), helper.add(1, 2), helper.limit, helper.limit)", py);
        Assert.Equal("add() expects 1 to 2 arguments, but got 3", Js("import { add } from \"./helper.py\";\nadd(1, 2, 3);\n", files, langs).Errors[0].Message);
    }

    [Fact]
    public void ForeignFileThatDoesNotCompile_IsReportedAtTheImport()
    {
        var src = "import * as Utils from \"./Utils.java\";\nUtils.twice(1);\n";
        var r = Js(src, new Dictionary<string, string> { ["Utils"] = "public class Utils {\n\tstatic int twice(int n) {\n\t\treturn n *;\n\t}\n}\n" }, new Dictionary<string, string> { ["Utils"] = "java" });
        Assert.False(r.Success);
        Assert.StartsWith("cannot use Utils: ", r.Errors[0].Message);
        Assert.Contains("does not compile", r.Errors[0].Message);
        Assert.Equal("\"./Utils.java\"", Snip(src, r.Errors[0]));
    }

    [Fact]
    public void ForeignPrivacyIsNotEnforced_JsPrivacyIs()
    {
        // a Python name is visible without export; another JavaScript window still needs export
        Ok(Js("import * as helper from \"./helper.py\";\nconsole.log(helper.limit);\n", new() { ["helper"] = PyHelper }, new() { ["helper"] = "python" }));
        var r = Js("import { hidden } from \"./Utils.js\";\n", new() { ["Utils"] = JsUtils }, new() { ["Utils"] = "js" });
        Assert.Equal("The requested module './Utils.js' does not provide an export named 'hidden'", r.Errors[0].Message);
    }

    [Fact]
    public void Signatures_OfAJavaScriptWindow()
    {
        var sig = ((ISignatureProvider)Lang).Signatures(JsUtils + "function main() {\n\tconst x = undefinedThing;\n}\n", new CompileOptions { FileName = "Utils" });
        Assert.True(sig.Ok, sig.Error);
        Assert.Equal("goTo", sig.Function("goTo").PyName);
        Assert.Equal(2, sig.Function("goTo").Params.Count);
        Assert.True(sig.Function("hidden").IsPrivate);
        Assert.Equal("moves", sig.Global("moves").PyName);
        var bad = ((ISignatureProvider)Lang).Signatures("function f( {", new CompileOptions { FileName = "Utils" });
        Assert.False(bad.Ok);
        Assert.StartsWith("Utils (JavaScript) does not compile (line 1: ", bad.Error);
    }

    [Fact]
    public void Java_CallsAJavaScriptFile()
    {
        ForeignModules.ClearCache();
        var java = Compiler.Languages.Get("java");
        var r = java.Compile("void main() {\n\tUtils.goTo(1, 2);\n\tSystem.out.println(Utils.moves);\n}\n", new CompileOptions
        {
            FileName = "Main",
            OtherFiles = new Dictionary<string, string> { ["Utils"] = JsUtils },
            OtherFileLanguages = new Dictionary<string, string> { ["Utils"] = "js" },
        });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("Utils.goTo(1, 2)", r.Python);
        Assert.Contains("Utils.moves", r.Python);
        // and the JavaScript window itself compiles to those names
        Assert.Contains("def goTo(x, y):", ToPy(JsUtils, "Utils"));
    }
}
