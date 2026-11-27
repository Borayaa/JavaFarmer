using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

/// <summary>Editor services for languages other than Java (FakeLanguage: camelCase API, '#' comments, Rust-like keywords).</summary>
public class GenericEditorTests
{
    private static readonly FakeLanguage Fake = new FakeLanguage();

    [Fact]
    public void Indent_UsesTheLanguageRules()
    {
        var hashRules = new EditorRules { LineComment = "#" };
        string t1 = "if (x) {\n";
        Assert.True(EditorSupport.AfterNewline(t1, t1.Length, "\t", hashRules, out var r1, out _));
        Assert.Equal("if (x) {\n\t", r1);
        string t2 = "x <- 1 # {\n";
        Assert.False(EditorSupport.AfterNewline(t2, t2.Length, "\t", hashRules, out _, out _));
        // Python-like rules: ':' is handled by the game itself
        var colon = new EditorRules { IndentAfter = new[] { ":" }, DedentOn = new string[0], LineComment = "#" };
        string t3 = "while True:\n";
        Assert.False(EditorSupport.AfterNewline(t3, t3.Length, "\t", colon, out _, out _));
        Assert.False(EditorSupport.IsDedentChar("}", colon));
        // dedent on '}'
        string t4 = "fn main() {\n\t\t}";
        Assert.True(EditorSupport.AfterDedentChar(t4, t4.Length, hashRules, out var r4, out var c4));
        Assert.Equal("fn main() {\n\t}", r4);
        Assert.Equal(r4.Length, c4);
    }

    [Fact]
    public void KeywordTooltips_TypeWords_AndKeywordsNamedLikeABuiltin()
    {
        var rust = Languages.Get("rust");
        var keys = new HashSet<string> { "code_tooltip_Vec", "code_tooltip_i32", "code_tooltip_Option", "code_tooltip_Some", "code_tooltip_North",
            "code_tooltip_move", "code_tooltip_keyword_move", "code_tooltip_loop", "code_tooltip_harvest" };
        foreach (var w in new[] { "Vec", "i32", "Option", "Some", "loop" })
            Assert.Equal("code_tooltip_" + w, GamePatches.KeywordTooltipKey(rust, w, keys.Contains));
        // the closure keyword move has its own string; code_tooltip_move documents the builtin (move_to in Rust)
        Assert.Equal("code_tooltip_keyword_move", GamePatches.KeywordTooltipKey(rust, "move", keys.Contains));
        Assert.Null(GamePatches.KeywordTooltipKey(rust, "move", k => k == "code_tooltip_move"));
        Assert.Null(GamePatches.KeywordTooltipKey(rust, "harvest", keys.Contains));   // a builtin, not a language word
        Assert.Null(GamePatches.KeywordTooltipKey(rust, "move_to", keys.Contains));
        // constants the game explains itself stay on the game's path
        Assert.Null(GamePatches.KeywordTooltipKey(rust, "North", keys.Contains, k => k == "code_tooltip_North"));
        // the shipped Rust strings have these entries
        var text = File.ReadAllText(Path.Combine(Repo.Root, "content", "rust", "Strings", "code_tooltips.txt"));
        foreach (var k in new[] { "Vec", "i32", "String", "Option", "Some", "keyword_move" }) Assert.Contains("@code_tooltip_" + k + " =", text);
    }

    [Fact]
    public void WordList_MapsBuiltinsKeywordsAndLiterals()
    {
        var vanilla = new[] { "while", "for", "True", "None", "def", "import", "can_harvest", "harvest", "myVar", "__jdiv", "Entities" };
        var words = EditorSupport.LanguageWordList(vanilla, w => w == "can_harvest" || w == "harvest", _ => true, Fake);
        foreach (var w in new[] { "while", "loop", "for", "true", "null", "fn", "return", "canHarvest", "harvest", "myVar", "Entities", "main", "let", "mut" })
            Assert.Contains(w, words);
        foreach (var w in new[] { "__jdiv", "can_harvest", "def", "import", "True", "None" })
            Assert.DoesNotContain(w, words);
        var locked = EditorSupport.LanguageWordList(new[] { "while" }, _ => false, u => u != "variables", Fake);
        Assert.DoesNotContain("let", locked);
    }

    [Fact]
    public void Hover_MapsNamesBackToTheGame()
    {
        Assert.Equal("can_harvest", EditorSupport.HoverWordToPython("canHarvest", Fake));
        Assert.Equal("True", EditorSupport.HoverWordToPython("true", Fake));
        Assert.Equal("None", EditorSupport.HoverWordToPython("null", Fake));
        Assert.Null(EditorSupport.HoverWordToPython("harvest", Fake));
        Assert.Null(EditorSupport.HoverWordToPython("Entities.Carrot", Fake));
        Assert.Equal("can_harvest", EditorSupport.HoverWordToPython("farm.canHarvest", Fake));
        Assert.Null(EditorSupport.HoverWordToPython("myVar", Fake));
        Assert.True(EditorSupport.IsForeignBuiltinWord("can_harvest", Fake, w => w == "can_harvest"));
        Assert.False(EditorSupport.IsForeignBuiltinWord("harvest", Fake, w => w == "harvest"));
        Assert.True(EditorSupport.IsForeignBuiltinWord("def", Fake, _ => false));
        Assert.False(EditorSupport.IsForeignBuiltinWord("while", Fake, _ => false));
    }

    [Fact]
    public void ErrorText_UsesTheLanguageNames()
    {
        Assert.Equal("canHarvest()", ErrorText.MapText("can_harvest()", Fake));
        Assert.Equal("!", ErrorText.MapText("not", Fake));
        Assert.Equal("&&", ErrorText.MapText("and", Fake));
        var args = new object[] { "can_harvest", 3 };
        Assert.True(ErrorText.MapArgs("error_wrong_number_args", args, Fake));
        Assert.Equal("canHarvest", args[0]);
        Assert.False(ErrorText.MapArgs("error_x", new object[] { "can_harvest" }, Languages.Get("python")));
    }

    [Fact]
    public void Toc_AndSignature_UseTheLanguage()
    {
        string toc = JavaText.BuiltinsToc(new[] { "can_harvest", "harvest" }, Fake);
        Assert.Contains("<unlock=can_harvest>[canHarvest](functions/can_harvest)", toc);
        string src = "# plants a row\n# of carrots\nfn row() {\n}\n";
        int start = src.IndexOf("fn");
        Assert.Equal("# plants a row\n# of carrots\nfn row() {", JavaText.SignatureWithComments(src, start, start + 10, Fake.Editor));
    }

    [Fact]
    public void Highlight_AnyLanguage_UsesTheGamePalette()
    {
        var java = Languages.Get("java");
        Assert.Equal(JavaHighlighter.Color("int x = 1; // c"), JavaHighlighter.Color("int x = 1; // c", "", -1, java));
        Assert.Equal("abc", JavaHighlighter.Color("abc", "", -1, Fake));
        Assert.Contains("<mark=", JavaHighlighter.Color("abc abc", "abc", 0, Fake));
    }
}
