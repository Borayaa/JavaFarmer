using System.Diagnostics;
using Xunit.Abstractions;

namespace JavaFarmer.Compiler.Tests;

public class HighlighterTests
{
    static void AssertCovers(string src, IReadOnlyList<(int Start, int Length, TokenKind Kind)> toks)
    {
        int pos = 0;
        foreach (var t in toks)
        {
            Assert.Equal(pos, t.Start);
            Assert.True(t.Length > 0);
            pos += t.Length;
        }
        Assert.Equal(src.Length, pos);
    }

    [Fact]
    public void ClassifiesTokens()
    {
        var src = "void main() { int x = 5; String s = \"hi\"; // note\n plant(Entities.Carrot); move(North); boolean b = true; }";
        var toks = JavaSyntax.Highlight(src);
        AssertCovers(src, toks);
        TokenKind KindOf(string word) => toks.First(t => src.Substring(t.Start, t.Length) == word).Kind;
        Assert.Equal(TokenKind.Type, KindOf("void"));
        Assert.Equal(TokenKind.Type, KindOf("int"));
        Assert.Equal(TokenKind.Type, KindOf("String"));
        Assert.Equal(TokenKind.Identifier, KindOf("main"));
        Assert.Equal(TokenKind.Number, KindOf("5"));
        Assert.Equal(TokenKind.String, KindOf("\"hi\""));
        Assert.Equal(TokenKind.Comment, KindOf("// note"));
        Assert.Equal(TokenKind.Type, KindOf("Entities"));
        Assert.Equal(TokenKind.Constant, KindOf("Carrot"));
        Assert.Equal(TokenKind.Constant, KindOf("North"));
        Assert.Equal(TokenKind.Constant, KindOf("true"));
        Assert.Equal(TokenKind.Operator, KindOf("="));
        Assert.Equal(TokenKind.Punctuation, KindOf(";"));
        Assert.Equal(TokenKind.Whitespace, KindOf(" "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"unterminated")]
    [InlineData("/* open comment")]
    [InlineData("'x")]
    [InlineData("int x = 0x; # \x60 \xe9 \t\r\n")]
    [InlineData("while (true) { if (")]
    public void ToleratesHalfTypedCode(string src)
    {
        var toks = JavaSyntax.Highlight(src);
        AssertCovers(src, toks);
    }

    [Fact]
    public void NeverThrowsOnCorpusPrefixes()
    {
        foreach (var f in Directory.GetFiles(TestUtil.CorpusDir, "*.java", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(f);
            for (int i = 0; i <= src.Length; i += 7)
            {
                var part = src.Substring(0, i);
                AssertCovers(part, JavaSyntax.Highlight(part));
            }
        }
        Assert.Empty(JavaSyntax.Highlight(null));
    }

    [Fact]
    public void KeywordsExcludeTypesAndConstants()
    {
        Assert.Contains("while", JavaSyntax.Keywords);
        Assert.Contains("return", JavaSyntax.Keywords);
        Assert.DoesNotContain("int", JavaSyntax.Keywords);
        Assert.DoesNotContain("true", JavaSyntax.Keywords);
    }
}

public class GameApiTests
{
    [Theory]
    [InlineData("canHarvest", "can_harvest")]
    [InlineData("getPosX", "get_pos_x")]
    [InlineData("doAFlip", "do_a_flip")]
    [InlineData("petThePiggy", "pet_the_piggy")]
    [InlineData("spawnDrone", "spawn_drone")]
    [InlineData("waitFor", "wait_for")]
    [InlineData("System.out.println", "print")]
    [InlineData("println", "print")]
    [InlineData("Math.min", "min")]
    [InlineData("String.valueOf", "str")]
    public void ToPython(string java, string py) => Assert.Equal(py, GameApi.ToPython(java));

    [Theory]
    [InlineData("can_harvest", "canHarvest")]
    [InlineData("print", "System.out.println")]
    [InlineData("random", "Math.random")]
    [InlineData("quick_print", "quickPrint")]
    public void ToJava(string py, string java) => Assert.Equal(java, GameApi.ToJava(py));

    [Fact]
    public void UnknownNamesGiveNull()
    {
        Assert.Null(GameApi.ToPython("nope"));
        Assert.Null(GameApi.ToJava("nope"));
        Assert.Null(GameApi.ToPython(null));
        Assert.Null(GameApi.ToJava(null));
        Assert.Null(GameApi.ToJava("trade"));
    }

    [Fact]
    public void FunctionsHaveSignatures()
    {
        var plant = GameApi.Functions.First(f => f.JavaName == "plant");
        Assert.Equal("boolean plant(Entity entity)", plant.Signature);
        Assert.Equal("boolean", plant.ReturnType);
        Assert.Contains(GameApi.Functions, f => f.Signature == "boolean useItem(Item item, int n)");
        Assert.Contains(GameApi.Functions, f => f.Signature == "Object measure(Direction direction)");
    }

    /// <summary>Every builtin the game defines (read from Core.dll) is reachable from Java, except the ones
    /// that Java expresses with its own syntax.</summary>
    [Fact]
    public void CoversEveryGameBuiltin()
    {
        var expressedBySyntax = new HashSet<string> { "range", "len", "list", "set", "dict" };
        foreach (var name in BuiltinFunctions.Functions.Keys)
        {
            if (expressedBySyntax.Contains(name)) continue;
            Assert.True(GameApi.ToJava(name) != null, "no Java name for game builtin " + name);
        }
    }

    [Fact]
    public void EveryApiFunctionIsCallableFromJava()
    {
        foreach (var f in GameApi.Functions.Where(f => !f.JavaName.Contains('.')))
            Assert.True(GameApi.ToPython(f.JavaName) != null);
    }
}

public class PerformanceTests
{
    readonly ITestOutputHelper output;
    public PerformanceTests(ITestOutputHelper output) { this.output = output; }

    [Fact]
    public void Compiles300LinesQuickly()
    {
        var body = new System.Text.StringBuilder();
        body.Append("import java.util.*;\nint total = 0;\n");
        int lines = 2;
        int m = 0;
        while (lines < 300)
        {
            body.Append("int work" + m + "(int n) {\n    int acc = 0;\n    for (int i = 0; i < n; i++) {\n        if (i % 3 == 0) {\n            acc += i / 2;\n        } else {\n            acc -= 1;\n        }\n    }\n    List<Integer> l = new ArrayList<>();\n    l.add(acc);\n    total += l.get(0);\n    return acc > 0 ? acc : -acc;\n}\n");
            lines += 15;
            m++;
        }
        body.Append("void main() {\n");
        for (int i = 0; i < m; i++) body.Append("    quickPrint(work" + i + "(" + i + "));\n");
        body.Append("}\n");
        var src = body.ToString();
        Assert.True(TestUtil.Compile(src).Success);
        for (int i = 0; i < 20; i++) TestUtil.Compile(src);   // warm up
        var sw = Stopwatch.StartNew();
        const int runs = 50;
        for (int i = 0; i < runs; i++) TestUtil.Compile(src);
        var ms = sw.Elapsed.TotalMilliseconds / runs;
        output.WriteLine(src.Split('\n').Length + " lines: " + ms.ToString("F2") + " ms per compile");
        Assert.True(ms < 20, "compile took " + ms.ToString("F2") + " ms");
    }
}
