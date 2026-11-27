using static JavaFarmer.Languages.Tests.R.RTestUtil;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>ILanguage services: naming, value format, stub, editor, highlighting.</summary>
public class RLanguageTests
{
    [Fact]
    public void Identity_And_Editor()
    {
        Assert.Equal("r", Lang.Id);
        Assert.Equal("R", Lang.DisplayName);
        Assert.Equal(".R", Lang.FileExtension);
        Assert.False(Lang.IsNative);
        Assert.Equal("#", Lang.Editor.LineComment);
        Assert.Null(Lang.Editor.BlockCommentStart);
        Assert.Equal(new[] { "{" }, Lang.Editor.IndentAfter);
        Assert.Contains("repeat", Lang.Keywords);
        Assert.Contains("TRUE", Lang.Keywords);
        Assert.Equal("", Lang.NewFileTemplate("main", out int caret));
        Assert.Equal(0, caret);
        Assert.Same(Lang.GetType(), Compiler.Languages.Get("r").GetType());
    }

    [Fact]
    public void ApiNames_AreSnakeCase()
    {
        Assert.Equal("can_harvest", Lang.ApiName("can_harvest"));
        Assert.Equal("do_a_flip", Lang.FromApiName("do_a_flip"));
        Assert.Null(Lang.ApiName("not_a_builtin"));
        Assert.Null(Lang.FromApiName("canHarvest"));
    }

    [Theory]
    [InlineData(null, "NULL")]
    [InlineData(true, "TRUE")]
    [InlineData(false, "FALSE")]
    [InlineData(3.0, "3")]
    [InlineData(-12.0, "-12")]
    [InlineData(2.5, "2.5")]
    [InlineData(0.30000000000000004, "0.3")]
    [InlineData(0.3333333333333333, "0.3333333")]
    [InlineData(1.5e20, "1.5e+20")]
    [InlineData("hay", "hay")]
    public void FormatValue_Scalars(object v, string expected) => Assert.Equal(expected, Lang.FormatValue(v));

    [Fact]
    public void FormatValue_Collections_AndGameValues()
    {
        Assert.Equal("c(1, 2, 3)", Lang.FormatValue(new GameList(new object[] { 1.0, 2.0, 3.0 })));
        Assert.Equal("c(\"a\", \"b\")", Lang.FormatValue(new GameList(new object[] { "a", "b" })));
        Assert.Equal("list(1, \"a\", NULL, TRUE)", Lang.FormatValue(new GameList(new object[] { 1.0, "a", null, true })));
        Assert.Equal("list()", Lang.FormatValue(new GameList()));
        Assert.Equal("list(hay = 3, \"two words\" = TRUE)", Lang.FormatValue(new GameDict().Add("hay", 3.0).Add("two words", true)));
        Assert.Equal("list(Items$Hay = 1)", Lang.FormatValue(new GameDict().Add(new GameConstant("Items", "Hay"), 1.0)));
        Assert.Equal("Entities$Carrot", Lang.FormatValue(new GameConstant("Entities", "Carrot")));
        Assert.Equal("North", Lang.FormatValue(new GameConstant("Directions", "North")));
        Assert.Equal("c(3, 4)", Lang.FormatValue(new GameTuple(3.0, 4.0)));
        Assert.Equal("list(Entities$Tree, c(1, 2))", Lang.FormatValue(new GameTuple(new GameConstant("Entities", "Tree"), new GameTuple(1.0, 2.0))));
        Assert.Equal("<drone 2>", Lang.FormatValue(new GameDrone(2)));
        Assert.Equal("function", Lang.FormatValue(new GameFunction("__jlambda3")));
        Assert.Equal("list(c(1, 2), c(3))", Lang.FormatValue(new GameList(new object[] { new GameList(new object[] { 1.0, 2.0 }), new GameList(new object[] { 3.0 }) })));
    }

    [Fact]
    public void Stub_HasEveryBuiltinConstantsAndPrint()
    {
        var stub = Lang.ApiStubFile(out var name);
        Assert.Equal("farm.R", name);
        foreach (var b in GameApi.Builtins) Assert.Contains("\n" + b.PythonName + " <- function(", stub);
        foreach (var c in GameApi.ConstantCategories) Assert.Contains(c + " <- .jf_constants(\"" + c + "\")", stub);
        foreach (var d in GameApi.DirectionNames) Assert.Contains(d + " <- \"" + d + "\"", stub);
        Assert.Contains("print <- function(x, ...)", stub);
        Assert.Contains("#' @param entity", stub);
    }

    [Fact]
    public void Highlight_CoversEverything_AndClassifiesTokens()
    {
        var src = "x <- Entities$Carrot # hi\nif (TRUE) print(\"a\") else 3L %% 2\n`odd` <- \\(y) y";
        var toks = Lang.Highlight(src);
        Assert.Equal(src.Length, toks.Sum(t => t.Length));
        string Kind(string text) { int at = src.IndexOf(text, StringComparison.Ordinal); return toks.First(t => t.Start == at).Kind.ToString(); }
        Assert.Equal("Constant", Kind("Carrot"));
        Assert.Equal("Constant", Kind("Entities"));
        Assert.Equal("Comment", Kind("# hi"));
        Assert.Equal("Keyword", Kind("if"));
        Assert.Equal("Constant", Kind("TRUE"));
        Assert.Equal("String", Kind("\"a\""));
        Assert.Equal("Number", Kind("3L"));
        Assert.Equal("Operator", Kind("%%"));
        Assert.Equal("Operator", Kind("<-"));
        Assert.Equal("Identifier", Kind("print"));
        Assert.Equal("Identifier", Kind("`odd`"));
    }
}
