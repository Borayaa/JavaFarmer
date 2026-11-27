using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>SPEC §7: every JS construct costs its Python twin's unlock; invented scaffolding is Synthetic.</summary>
public class JsUnlockFairnessTests
{
    static readonly Dictionary<string, string> Lib = new() { ["Utils"] = "export function f(a) { return a; }\n" };

    static Segment SegmentOf(CompileResult r, string text, int occurrence = 0)
    {
        Assert.True(r.Success, string.Join("; ", r.Errors));
        int at = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            at = r.Python.IndexOf(text, at + 1, StringComparison.Ordinal);
            Assert.True(at >= 0, "'" + text + "' not found in:\n" + r.Python);
        }
        var seg = r.Map.Find(at);
        Assert.NotNull(seg);
        return seg;
    }

    [Fact]
    public void ClassicFor_NotARange_IsAWhileThatCostsFor()
    {
        var r = Compile("let i = 0;\nfor (; i < 10; i = i * 2 + 1) {\n\tconsole.log(i);\n}\n");
        var w = SegmentOf(r, "while ");
        Assert.Equal(SegmentKind.Structural, w.Kind);
        Assert.Equal("for", w.DependencyOverride);
        Assert.Equal(SegmentKind.Structural, SegmentOf(r, "i = i * 2 + 1").Kind);
    }

    [Fact]
    public void CountingFor_IsARangeLoop_ThatTheUserPaysFor()
    {
        var r = Compile("for (let i = 0; i < 10; i++) {\n\tconsole.log(i);\n}\n");
        Assert.Equal(SegmentKind.User, SegmentOf(r, "for ").Kind);
        Assert.Null(SegmentOf(r, "for ").DependencyOverride);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "range(").Kind);
    }

    [Fact]
    public void TemplateLiteral_PlusIsUser_StrConversionIsSynthetic()
    {
        var r = Compile("const n = 3;\nconsole.log(`a ${n} b`);\n");
        Assert.Equal(SegmentKind.User, SegmentOf(r, " + ").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "str(").Kind);
    }

    [Fact]
    public void Helpers_AreSynthetic()
    {
        var r = Compile("const n = 3;\nconst a = new Array(n).fill(0);\nconst g = Array.from({ length: n }, () => new Array(n).fill(0));\nconsole.log(a[0] % 2, Math.trunc(n / 2));\n");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "def __jarr(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "def __jarr2(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "def __jmod(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "__jtrunc(", 1).Kind);
    }

    [Fact]
    public void ImportLine_IsUser()
    {
        var r = Compile("import * as Utils from \"./Utils.js\";\nUtils.f(1);\n", "Main", Lib);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "import Utils").Kind);
        var named = Compile("import { f } from \"./Utils.js\";\nf(1);\n", "Main", Lib);
        Assert.Equal(SegmentKind.User, SegmentOf(named, "import Utils").Kind);
    }

    [Fact]
    public void DoWhile_LoopIsUser_ExitTestIsStructural()
    {
        var r = Compile("let x = 0;\ndo {\n\tx++;\n} while (x < 3);\n");
        Assert.Equal(SegmentKind.User, SegmentOf(r, "while ").Kind);
        Assert.Equal(SegmentKind.Structural, SegmentOf(r, "True").Kind);
        Assert.Equal(SegmentKind.Structural, SegmentOf(r, "if not ").Kind);
    }

    [Fact]
    public void Switch_IfIsUser_ComparisonIsSynthetic()
    {
        var r = Compile("const n = 2;\nswitch (n) {\n\tcase 1:\n\t\tharvest();\n\t\tbreak;\n\tdefault:\n\t\tdoAFlip();\n}\n");
        Assert.Equal(SegmentKind.User, SegmentOf(r, "if ").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, " == ").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "else").Kind);
    }

    [Fact]
    public void EmptyConsoleLog_ArgumentIsSynthetic()
    {
        var r = Compile("console.log();\n");
        Assert.Equal(SegmentKind.User, SegmentOf(r, "print").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "\"\"").Kind);
    }

    [Fact]
    public void Constructs_CostTheirPythonTwin()
    {
        var r = Compile("const a = [1];\nconst b = [...a];\nfor (const v of a) console.log(v);\nfunction f() {\n\treturn 1;\n}\nconst m = new Map([[1, 2]]);\nconst s = new Set([1]);\nwhile (f() > 2) {\n\tbreak;\n}\n");
        foreach (var t in new[] { "list(", "for ", "def ", "return", "{1: 2}", "{1}", "while ", "break" })
        {
            var seg = SegmentOf(r, t);
            Assert.True(seg.Kind == SegmentKind.User, t + " is " + seg.Kind);
            Assert.Null(seg.DependencyOverride);
        }
    }
}
