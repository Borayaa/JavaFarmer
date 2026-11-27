using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>ILanguage surface: FormatValue (node's console.log), API names, editor rules, stub file, highlighter.</summary>
public class JsLanguageTests
{
    [Theory]
    [InlineData(3.0, "3")]
    [InlineData(0.5, "0.5")]
    [InlineData(-1.5, "-1.5")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    [InlineData(9007199254740992.0, "9007199254740992")]
    [InlineData(-0.0, "0")]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    [InlineData(null, "null")]
    [InlineData("plain text", "plain text")]
    public void FormatValue_Scalars(object v, string expected) => Assert.Equal(expected, Lang.FormatValue(v));

    [Fact]
    public void FormatValue_Collections_LikeUtilInspect()
    {
        Assert.Equal("[ 1, 'a', null, true ]", Lang.FormatValue(new GameList(new object[] { 1.0, "a", null, true })));
        Assert.Equal("[]", Lang.FormatValue(new GameList()));
        Assert.Equal("[ [], Map(0) {} ]", Lang.FormatValue(new GameList(new object[] { new GameList(), new GameDict() })));
        Assert.Equal("Map(2) { 'a' => 1, 'b' => [ 2 ] }", Lang.FormatValue(new GameDict().Add("a", 1.0).Add("b", new GameList(new object[] { 2.0 }))));
        Assert.Equal("Set(2) { 1, 2 }", Lang.FormatValue(new GameSet(new object[] { 1.0, 2.0 })));
        Assert.Equal("Set(0) {}", Lang.FormatValue(new GameSet()));
        Assert.Equal("[ \"it's\" ]", Lang.FormatValue(new GameList(new object[] { "it's" })));
        Assert.Equal("[ 3, 4 ]", Lang.FormatValue(new GameTuple(3.0, 4.0)));
    }

    [Fact]
    public void FormatValue_GameValues()
    {
        Assert.Equal("Entities.Carrot", Lang.FormatValue(new GameConstant("Entities", "Carrot")));
        Assert.Equal("North", Lang.FormatValue(new GameConstant("Directions", "North")));
        Assert.Equal("Map(1) { Items.Hay => 3 }", Lang.FormatValue(new GameDict().Add(new GameConstant("Items", "Hay"), 3.0)));
        Assert.Equal("[Function: canHarvest]", Lang.FormatValue(new GameFunction("can_harvest")));
        Assert.Equal("[Function (anonymous)]", Lang.FormatValue(new GameFunction("__jlambda1")));
        Assert.Equal("[Drone 2]", Lang.FormatValue(new GameDrone(2)));
    }

    [Fact]
    public void FormatValue_Cycles_DoNotHang()
    {
        var l = new GameList();
        l.Items.Add(l);
        Assert.Contains("[Circular *1]", Lang.FormatValue(l));
    }

    [Theory]
    [InlineData("can_harvest", "canHarvest")]
    [InlineData("do_a_flip", "doAFlip")]
    [InlineData("get_pos_x", "getPosX")]
    [InlineData("print", "console.log")]
    [InlineData("min", "Math.min")]
    [InlineData("max", "Math.max")]
    [InlineData("abs", "Math.abs")]
    [InlineData("random", "Math.random")]
    [InlineData("quick_print", "quickPrint")]
    [InlineData("spawn_drone", "spawnDrone")]
    public void ApiName_AndBack(string py, string js)
    {
        Assert.Equal(js, Lang.ApiName(py));
        Assert.Equal(py, Lang.FromApiName(js));
    }

    [Theory]
    [InlineData("print", "print")]
    [InlineData("min", "min")]
    [InlineData("max", "max")]
    [InlineData("abs", "abs")]
    [InlineData("random", "random")]
    public void FromApiName_AcceptsShortForms(string js, string py) => Assert.Equal(py, Lang.FromApiName(js));

    [Fact]
    public void Identity_AndEditorRules()
    {
        Assert.Equal("js", Lang.Id);
        Assert.Equal("JavaScript", Lang.DisplayName);
        Assert.Equal(".js", Lang.FileExtension);
        Assert.Equal(new[] { "{" }, Lang.Editor.IndentAfter);
        Assert.Equal(new[] { "}" }, Lang.Editor.DedentOn);
        Assert.Equal("//", Lang.Editor.LineComment);
        Assert.Equal("/*", Lang.Editor.BlockCommentStart);
        Assert.Equal("*/", Lang.Editor.BlockCommentEnd);
        Assert.Contains("let", Lang.Keywords);
        Assert.Contains("function", Lang.Keywords);
        var t = Lang.NewFileTemplate("main", out int caret);
        Assert.InRange(caret, 0, t.Length);
        Assert.True(Lang.Compile(t, new CompileOptions { FileName = "main" }).Success);
    }

    [Fact]
    public void ApiStub_ExportsEverythingAndInstallsGlobals()
    {
        var stub = Lang.ApiStubFile(out var name);
        Assert.Equal("farm.js", name);
        Assert.Contains("export function canHarvest() {", stub);
        Assert.Contains("export function quickPrint(", stub);
        Assert.Contains("export const Entities = constants(\"Entities\");", stub);
        Assert.Contains("export const North = ", stub);
        Assert.Contains("new Proxy(", stub);
        Assert.Contains("Object.assign(globalThis, {", stub);
        Assert.DoesNotContain("export function print(", stub);
    }

    [Fact]
    public void Highlight_CoversEveryCharacter_TemplatesAndComments()
    {
        var src = "// c\nconst s = `a ${x + `in ${y}`} b`; /* k */ console.log(s, Entities.Carrot, 0x1f);\n";
        var toks = Lang.Highlight(src);
        int pos = 0;
        foreach (var (start, len, _) in toks) { Assert.Equal(pos, start); pos += len; }
        Assert.Equal(src.Length, pos);
        Assert.Contains(toks, t => t.Kind == TokenKind.Comment);
        Assert.Contains(toks, t => t.Kind == TokenKind.String && src.Substring(t.Start, t.Length) == "`a ");
        Assert.Contains(toks, t => t.Kind == TokenKind.Constant && src.Substring(t.Start, t.Length) == "Carrot");
        Assert.Contains(toks, t => t.Kind == TokenKind.Keyword && src.Substring(t.Start, t.Length) == "const");
    }
}
