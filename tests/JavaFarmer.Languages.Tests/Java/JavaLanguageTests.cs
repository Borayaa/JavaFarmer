namespace JavaFarmer.Languages.Tests.Java;

/// <summary>JavaLanguage: identity, editor services, API naming, value format and stub file (v1 plugin behaviour).</summary>
public class JavaLanguageTests
{
    static readonly ILanguage Java = Compiler.Languages.Get("java");

    [Fact]
    public void Identity()
    {
        Assert.Equal("java", Java.Id);
        Assert.Equal("Java", Java.DisplayName);
        Assert.Equal(".java", Java.FileExtension);
        Assert.False(Java.IsNative);
        Assert.Same(Java, Compiler.Languages.All.First());
    }

    [Fact]
    public void EditorRulesAndTemplate()
    {
        var e = Java.Editor;
        Assert.Equal(new[] { "{" }, e.IndentAfter);
        Assert.Equal(new[] { "}" }, e.DedentOn);
        Assert.Equal("//", e.LineComment);
        Assert.Equal("/*", e.BlockCommentStart);
        Assert.Equal("*/", e.BlockCommentEnd);
        Assert.Equal("\t", e.IndentUnit);
        var tpl = Java.NewFileTemplate("main", out int caret);
        Assert.Equal("void main() {\n\t\n}", tpl);
        Assert.Equal("void main() {\n\t".Length, caret);
        Assert.True(Java.Compile(tpl, new CompileOptions { FileName = "main" }).Success);
    }

    [Theory]
    [InlineData("can_harvest", "canHarvest")]
    [InlineData("do_a_flip", "doAFlip")]
    [InlineData("print", "System.out.println")]
    [InlineData("min", "Math.min")]
    [InlineData("random", "Math.random")]
    [InlineData("str", "String.valueOf")]
    [InlineData("get_pos_x", "getPosX")]
    public void ApiNamesAreTheV1Names(string py, string java)
    {
        Assert.Equal(java, Java.ApiName(py));
        Assert.Equal(py, Java.FromApiName(java));
    }

    [Fact]
    public void ApiNameUnknown()
    {
        Assert.Null(Java.ApiName("no_such_builtin"));
        Assert.Null(Java.FromApiName("noSuchBuiltin"));
        Assert.Null(Java.ApiName(null));
        Assert.Equal("print", Java.FromApiName("println"));
    }

    [Fact]
    public void FormatValueMatchesThePluginValueFormatter()
    {
        string F(object v) => Java.FormatValue(v);
        Assert.Equal("null", F(null));
        Assert.Equal("true", F(true));
        Assert.Equal("false", F(false));
        Assert.Equal("2", F(2.0));
        Assert.Equal("1.5", F(1.5));
        Assert.Equal("0.33", F(1.0 / 3));
        Assert.Equal("-7", F(-7.0));
        Assert.Equal("hay", F("hay"));
        Assert.Equal("[1, 2, 3]", F(new GameList(new object[] { 1.0, 2.0, 3.0 })));
        Assert.Equal("[a, null, true]", F(new GameList(new object[] { "a", null, true })));
        Assert.Equal("[a, b]", F(new GameSet(new object[] { "a", "b" })));
        Assert.Equal("{a=1, b=2}", F(new GameDict().Add("a", 1.0).Add("b", 2.0)));
        Assert.Equal("Position[x=1, y=2]", F(new GameTuple(1.0, 2.0)));
        Assert.Equal("Companion[entity=Entities.Tree, position=Position[x=1, y=2]]", F(new GameTuple(new GameConstant("Entities", "Tree"), new GameTuple(1.0, 2.0))));
        Assert.Equal("[a, b, c]", F(new GameTuple("a", "b", "c")));
        Assert.Equal("Drone@3", F(new GameDrone(3)));
        Assert.Equal("canHarvest", F(new GameFunction("can_harvest")));
        Assert.Equal("System.out.println", F(new GameFunction("print")));
        Assert.Equal("lambda", F(new GameFunction("__jlambda2")));
        Assert.Equal("lambda", F(new GameFunction("__jdiv")));
        Assert.Equal("myMethod", F(new GameFunction("myMethod")));
        Assert.Equal("Utils", F(new GameModule("Utils")));
        Assert.Equal("Entities.Carrot", F(new GameConstant("Entities", "Carrot")));
        Assert.Equal("North", F(new GameConstant("Directions", "North")));
        var self = new GameList();
        self.Items.Add(1.0);
        self.Items.Add(self);
        Assert.Equal("[1, (this Collection)]", F(self));
        Assert.Equal("{[1, 2]=Position[x=3, y=4]}", F(new GameDict().Add(new GameList(new object[] { 1.0, 2.0 }), new GameTuple(3.0, 4.0))));
    }

    [Fact]
    public void ApiStubFileIsThePluginsJavaFarmerApi()
    {
        var text = Java.ApiStubFile(out var name);
        Assert.Equal("JavaFarmerApi.java", name);
        Assert.StartsWith("// JavaFarmer game API reference, written by the JavaFarmer mod.\n// The game never runs this file.", text);
        Assert.Contains("\ninterface JavaFarmerApi {\n", text);
        Assert.EndsWith("}\n", text);
        Assert.Contains("    boolean canHarvest();   // game: can_harvest\n", text);
        Assert.Contains("    boolean useItem(Item item, int n);   // game: use_item\n", text);
        Assert.Contains("    // void System.out.println(Object value);   // game: print\n", text);
        foreach (var f in GameApi.Functions) Assert.Contains(f.Signature + ";", text);
        Assert.Equal(JavaLanguage.BuildApiStub(GameApi.Functions), text);
    }

    [Fact]
    public void KeywordsAndHighlight()
    {
        Assert.Contains("while", Java.Keywords);
        Assert.DoesNotContain("int", Java.Keywords);
        var src = "void main() { harvest(); }";
        var hl = Java.Highlight(src);
        Assert.Equal(src.Length, hl.Sum(t => t.Length));
        Assert.Equal(TokenKind.Type, hl[0].Kind);
    }

    [Fact]
    public void CompileAndToIr()
    {
        var src = "void main() {\n\tint x = 1;\n\tSystem.out.println(x);\n}";
        var r = Java.Compile(src, new CompileOptions { FileName = "main" });
        Assert.True(r.Success);
        var ir = Java.ToIr(src, new CompileOptions { FileName = "main" }, out var errs);
        Assert.NotNull(ir);
        Assert.Empty(errs);
        Assert.Equal("java", ir.SourceLanguage);
        Assert.Null(Java.ToIr("void main( {", new CompileOptions { FileName = "main" }, out var bad));
        Assert.NotEmpty(bad);
    }

    [Fact]
    public void PrintRoundTripOfTheTemplate()
    {
        var src = Java.NewFileTemplate("main", out _);
        var ir = Java.ToIr(src, new CompileOptions { FileName = "main" }, out _);
        Assert.Equal("void main() {\n}\n", Java.Print(ir, new PrintOptions { FileName = "main" }));
    }
}
