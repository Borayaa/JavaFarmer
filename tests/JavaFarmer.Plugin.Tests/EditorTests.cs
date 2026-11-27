using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

public class EditorTests
{
    [Fact]
    public void Newline_AfterOpenBrace_IndentsOneLevel()
    {
        string text = "void main() {\n";
        Assert.True(EditorSupport.AfterNewline(text, text.Length, "\t", out var t, out var c));
        Assert.Equal("void main() {\n\t", t);
        Assert.Equal(t.Length, c);
    }

    [Fact]
    public void Newline_KeepsGameIndent_AndAddsSpacesUnit()
    {
        string text = "    if (x) {\n    "; // the game already copied the 4 spaces
        Assert.True(EditorSupport.AfterNewline(text, text.Length, "    ", out var t, out var c));
        Assert.Equal("    if (x) {\n        ", t);
        Assert.Equal(t.Length, c);
    }

    [Fact]
    public void Newline_BetweenBraces_PutsClosingBraceOnItsOwnLine()
    {
        string text = "\tif (x) {\n\t}";
        int caret = text.IndexOf('\n') + 2; // after "\n\t" (game copied the tab)
        Assert.True(EditorSupport.AfterNewline(text, caret, "\t", out var t, out var c));
        Assert.Equal("\tif (x) {\n\t\t\n\t}", t);
        Assert.Equal(caret + 1, c);
    }

    [Fact]
    public void Newline_IgnoresBraceInComment_AndOtherLines()
    {
        Assert.False(EditorSupport.AfterNewline("x = 1; // {\n", 12, "\t", out _, out _));
        Assert.False(EditorSupport.AfterNewline("harvest();\n", 11, "\t", out _, out _));
        Assert.False(EditorSupport.AfterNewline("abc", 2, "\t", out _, out _));
        Assert.False(EditorSupport.AfterNewline("", 0, "\t", out _, out _));
    }

    [Fact]
    public void CloseBrace_OnBlankLine_Dedents()
    {
        string text = "void main() {\n\t\t}";
        Assert.True(EditorSupport.AfterCloseBrace(text, text.Length, out var t, out var c));
        Assert.Equal("void main() {\n\t}", t);
        Assert.Equal(t.Length, c);
        Assert.True(EditorSupport.AfterCloseBrace("a {\n        }", 13, out t, out c));
        Assert.Equal("a {\n    }", t);
        Assert.Equal(9, c);
        Assert.False(EditorSupport.AfterCloseBrace("x; }", 4, out _, out _));
        Assert.False(EditorSupport.AfterCloseBrace("}", 1, out _, out _));
        Assert.False(EditorSupport.AfterCloseBrace("a {\n}", 5, out _, out _));
    }

    [Fact]
    public void WordList_IsJava()
    {
        var vanilla = new[] { "while", "for", "harvest", "can_harvest", "len", "myVar", "__jdiv", "__name__", "True", "None", "def" };
        var builtins = new HashSet<string> { "harvest", "can_harvest", "len" };
        var toJava = new Dictionary<string, string> { { "harvest", "harvest" }, { "can_harvest", "canHarvest" } };
        var words = EditorSupport.JavaWordList(vanilla, builtins.Contains, u => u == "variables", p => toJava.GetValueOrDefault(p));
        Assert.Contains("canHarvest", words);
        Assert.Contains("harvest", words);
        Assert.Contains("while", words);
        Assert.Contains("do", words);
        Assert.Contains("for", words);
        Assert.Contains("true", words);
        Assert.Contains("null", words);
        Assert.Contains("void", words);
        Assert.Contains("myVar", words);
        Assert.Contains("int", words);          // variables unlocked
        Assert.DoesNotContain("ArrayList", words); // lists locked
        Assert.DoesNotContain("can_harvest", words);
        Assert.DoesNotContain("len", words);
        Assert.DoesNotContain("__jdiv", words);
        Assert.DoesNotContain("__name__", words);
        Assert.DoesNotContain("True", words);
        Assert.DoesNotContain("def", words);
    }

    [Fact]
    public void MemberList()
    {
        Assert.Equal(new[] { "out" }, EditorSupport.JavaMemberList("System"));
        Assert.Contains("println", EditorSupport.JavaMemberList("out"));
        Assert.Contains("sqrt", EditorSupport.JavaMemberList("Math"));
        Assert.Contains("size", EditorSupport.JavaMemberList("myList"));
        Assert.Equal(new[] { "go" }, EditorSupport.FilterHelpers(new[] { "go", "__jdiv" }));
    }

    [Fact]
    public void HoverWords()
    {
        var toPy = new Dictionary<string, string> { { "canHarvest", "can_harvest" }, { "harvest", "harvest" } };
        Func<string, string> f = j => toPy.GetValueOrDefault(j);
        Assert.Equal("can_harvest", EditorSupport.HoverWordToPython("canHarvest", f));
        Assert.Null(EditorSupport.HoverWordToPython("harvest", f));
        Assert.Null(EditorSupport.HoverWordToPython("myVar", f));
        Assert.Equal("print", EditorSupport.HoverWordToPython("System.out.println", f));
        Assert.Equal("min", EditorSupport.HoverWordToPython("Math.min", f));
        Assert.Equal("True", EditorSupport.HoverWordToPython("true", f));
        Assert.Null(EditorSupport.HoverWordToPython("Entities.Carrot", f));
    }

    [Fact]
    public void NewFileTemplate()
    {
        Assert.Equal("void main() {\n\t\n}", EditorSupport.NewFileTemplate);
    }

    [Fact]
    public void ErrorArgs_UseJavaNames()
    {
        var toJava = new Dictionary<string, string> { { "can_harvest", "canHarvest" }, { "print", "System.out.println" } };
        Func<string, string> f = p => toJava.GetValueOrDefault(p);
        var args = new object[] { "can_harvest()", 3, "print", "myFunc()", "not" };
        Assert.True(ErrorText.MapArgs("error_wrong_number_args", args, f));
        Assert.Equal(new object[] { "canHarvest()", 3, "System.out.println", "myFunc()", "!" }, args);
        var other = new object[] { "can_harvest()" };
        Assert.False(ErrorText.MapArgs("popup_something", other, f));
        Assert.Equal("can_harvest()", other[0]);
        Assert.False(ErrorText.MapArgs("error_x", null, f));
    }

    [Fact]
    public void ApiStub_ListsFunctions()
    {
        var text = ApiStubWriter.Build(new[]
        {
            new ApiFunction { JavaName = "canHarvest", PythonName = "can_harvest", Signature = "boolean canHarvest()", ReturnType = "boolean" },
            new ApiFunction { JavaName = "till", PythonName = "till", ReturnType = "void" },
            new ApiFunction { JavaName = "System.out.println", PythonName = "print", Signature = "void System.out.println(Object value)", ReturnType = "void" },
        });
        Assert.Contains("    // void System.out.println(Object value);   // game: print", text);
        Assert.Contains("interface JavaFarmerApi {", text);
        Assert.Contains("    boolean canHarvest();   // game: can_harvest", text);
        Assert.Contains("    void till();", text);
        Assert.EndsWith("}\n", text);
    }
}
