using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

/// <summary>The per-language content resolution chain of ContentStore (docs/SPEC-v2.md §5).</summary>
public sealed class MultiLanguageContentTests : IDisposable
{
    private readonly string root = Repo.NewTempDir("content-chain");
    private static readonly string BT = "`";

    public MultiLanguageContentTests()
    {
        Write("java/docs/home.md", "# Java home");
        Write("java/Strings/code_tooltips.txt", "@code_tooltip_harvest = Java harvest.\n@code_tooltip_vault_try = Java-specific vault text.\n");
        Write("java/unlock_display_code.txt", "@loops\nwhile (true) {}\n");
        Write("python/docs/mods/cyber/index.md", "# Cyber\nCall " + BT + "can_harvest()" + BT + " first.\n");
        Write("python/docs/mods/home_section.md", "## Mods\n<unlock=cyber>[Cyber](docs/mods/cyber/index.md)</unlock>\n");
        Write("python/Strings/extensions.txt", "@code_tooltip_vault_try = Try " + BT + "can_harvest()" + BT + ".\n@cyber = cybersecurity\n");
    }

    private void Write(string rel, string text)
    {
        string p = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void Docs_OwnLanguageFirst_ThenCanonicalPythonTranslated()
    {
        var c = new ContentStore(root);
        Assert.True(c.TryGetDoc("docs/home.md", "java", out var home));
        Assert.Equal("# Java home", home);
        Assert.True(c.TryGetDoc("docs/mods/cyber/index.md", "java", out var cyber));
        Assert.Contains(BT + "canHarvest()" + BT, cyber);
        Assert.True(c.TryGetDoc("docs/mods/cyber/index.md", "python", out var raw));
        Assert.Contains(BT + "can_harvest()" + BT, raw);
        // python: content/python only, never the Java pages
        Assert.False(c.TryGetDoc("docs/home.md", "python", out _));
        Assert.False(c.TryGetDoc("docs/missing.md", "java", out _));
        Assert.False(c.TryGetDoc("docs/home.md", "Bad Id", out _));
    }

    [Fact]
    public void GameDocs_AreTranslated_AndCached()
    {
        var c = new ContentStore(root);
        int calls = 0;
        c.Translator = (md, lang) => { calls++; return ContentStore.DefaultTranslate(md, lang); };
        string game = "Use " + BT + "can_harvest()" + BT + " and " + BT + "True" + BT + ".";
        string t1 = c.TranslateGameDoc("docs/x.md", game, "java");
        Assert.Equal("Use " + BT + "canHarvest()" + BT + " and " + BT + "true" + BT + ".", t1);
        Assert.Same(t1, c.TranslateGameDoc("docs/x.md", game, "java"));
        Assert.Equal(1, calls);
        Assert.Equal(game, c.TranslateGameDoc("docs/x.md", game, "python"));
        // a changed page is translated again
        c.TranslateGameDoc("docs/x.md", game + " ", "java");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void HomePage_GetsTheExtensionSection_InEveryLanguage()
    {
        var c = new ContentStore(root);
        string javaHome = GamePatches.ResolveDoc(c, "docs/home.md", "GAME HOME", "java");
        Assert.StartsWith("# Java home", javaHome);
        Assert.Contains("## Mods", javaHome);
        string pyHome = GamePatches.ResolveDoc(c, "docs/home.md", "GAME HOME", "python");
        Assert.StartsWith("GAME HOME", pyHome);
        Assert.Contains("<unlock=cyber>", pyHome);
        Assert.Equal("other", GamePatches.ResolveDoc(c, "docs/other.md", "other", "python"));
    }

    [Fact]
    public void Strings_CanonicalPythonTranslated_OverlaidByTheLanguage()
    {
        var c = new ContentStore(root);
        var java = c.StringsFor("java");
        Assert.Equal("Java harvest.", java["code_tooltip_harvest"]);
        Assert.Equal("Java-specific vault text.", java["code_tooltip_vault_try"]); // the language's own text wins
        Assert.Equal("cybersecurity", java["cyber"]);
        var py = c.StringsFor("python");
        Assert.Equal("Try " + BT + "can_harvest()" + BT + ".", py["code_tooltip_vault_try"]);
        Assert.False(py.ContainsKey("code_tooltip_harvest"));
        Assert.Same(java, c.StringsFor("java"));
        // v1 API = Java
        Assert.Equal("Java harvest.", c.Strings["code_tooltip_harvest"]);
    }

    [Fact]
    public void Strings_PythonOnlyKeys_TranslatedForOtherLanguages()
    {
        File.Delete(Path.Combine(root, "java", "Strings", "code_tooltips.txt"));
        var c = new ContentStore(root);
        Assert.Equal("Try " + BT + "canHarvest()" + BT + ".", c.StringsFor("java")["code_tooltip_vault_try"]);
        Assert.Equal("x " + BT + "canHarvest()" + BT, c.TranslateGameString("k", "x " + BT + "can_harvest()" + BT, "java"));
        Assert.Equal("no code", c.TranslateGameString("k", "no code", "java"));
    }

    [Fact]
    public void DisplayCode_OwnFileFirst_ThenPrintedFromPython()
    {
        var c = new ContentStore(root);
        Assert.Equal("while (true) {}", c.ResolveDisplayCode("loops", "while", "java"));
        Assert.Equal("canHarvest()", c.ResolveDisplayCode("cyber_root", "can_harvest()", "java"));
        Assert.Equal("can_harvest()", c.ResolveDisplayCode("cyber_root", "can_harvest()", "python"));
        Assert.Equal("", c.ResolveDisplayCode("nothing", "", "java"));
        Assert.Equal("while (true) {}", c.DisplayCode["LOOPS"]);
    }

    [Fact]
    public void LanguageWithoutAPrinter_KeepsThePythonText()
    {
        var stub = new CommentPrinterLanguage();
        var c = new ContentStore(root);
        c.LanguageResolver = id => id == stub.Id ? stub : Languages.Get(id);
        Assert.False(c.CanPrint(stub));
        Assert.True(c.CanPrint(Languages.Get("java")));
        string game = "Use " + BT + "can_harvest()" + BT + ".";
        Assert.Equal(game, c.TranslateGameDoc("docs/x.md", game, stub.Id));
        Assert.True(c.TryGetDoc("docs/mods/cyber/index.md", stub.Id, out var cyber));
        Assert.Contains(BT + "can_harvest()" + BT, cyber);
        Assert.Equal("can_harvest()", c.ResolveDisplayCode("x", "can_harvest()", stub.Id));
        Assert.Equal("Try " + BT + "can_harvest()" + BT + ".", c.StringsFor(stub.Id)["code_tooltip_vault_try"]);
    }

    /// <summary>A language whose printer only prints a placeholder comment.</summary>
    private sealed class CommentPrinterLanguage : LanguageBase
    {
        public override string Id => "comment-printer";
        public override string DisplayName => "CommentPrinter";
        public override string FileExtension => ".cp";
        protected override NamingStyle ApiNaming => NamingStyle.Camel;
        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors) => new IrModule();
        protected override string PrintImpl(IrModule module, PrintOptions options) => "// printer not implemented\n";
        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source) =>
            string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, source.StartsWith("//") ? TokenKind.Comment : TokenKind.Identifier) };
        public override IReadOnlyCollection<string> Keywords => Array.Empty<string>();
        public override string NewFileTemplate(string fileName, out int caret) { caret = 0; return ""; }
        public override string ApiStubFile(out string fileName) { fileName = "x.cp"; return ""; }
    }

    [Theory]
    [MemberData(nameof(TestCorpus.LanguageIds), MemberType = typeof(TestCorpus))]
    public void Snippets_NeverThrow_InAnyLanguage(string id)
    {
        var lang = Languages.Get(id);
        foreach (var s in new[] { "while", "unlock()", "set_execution_speed()", "{1:2}", "(x,y)", "a = 2", "print()", "1 + 1", "[1,2,3]", "def f():", "?", "import" })
        {
            var t = ContentStore.TranslateSnippet(s, lang);
            Assert.False(string.IsNullOrEmpty(t), id + ": " + s);
        }
    }

    [Fact]
    public void RealContent_HasTheJavaLanguageFolder()
    {
        Skip.IfNot(Directory.Exists(Repo.Content));
        var c = new ContentStore(Repo.Content);
        Assert.True(c.TryGetDoc("docs/home.md", "java", out var h) && h.Length > 0);
        Assert.True(c.TryGetDoc("docs/scripting/functions.md", "java", out _));
        Assert.NotEmpty(c.DisplayCodeFor("java"));
        Assert.Contains("code_tooltip_harvest", c.RawStrings("java").Keys);
    }
}
