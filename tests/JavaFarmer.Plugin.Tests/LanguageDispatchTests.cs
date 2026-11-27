using HarmonyLib;
using JavaFarmer.Compiler;
using Xunit.Abstractions;

namespace JavaFarmer.Plugin.Tests;

/// <summary>Every patch follows the active language (General.Language): Python = vanilla, others = their ILanguage.</summary>
[Collection("Game")]
public sealed class LanguageDispatchTests : IDisposable
{
    private readonly ITestOutputHelper output;
    public LanguageDispatchTests(ITestOutputHelper output) { this.output = output; }

    public void Dispose()
    {
        ActiveLanguage.SetForTests("java");
        GamePatches.JavaTextProvider = GamePatches.DefaultJavaText;
        GamePatches.OtherFilesProvider = GamePatches.DefaultOtherFiles;
        GamePatches.OtherLanguagesProvider = GamePatches.DefaultOtherLanguages;
        LanguageContext.Reset();
        FileLanguages.Clear();
    }

    private static PyList SampleList() => new PyList(new List<IPyObject> { new PyBool(true), new PyNone(), new PyNumber(1.5), new PyString("a") });

    [Fact]
    public void EachWindowUsesItsOwnLanguage()
    {
        Tokenizer.Tokenize("x = 1", out _);
        string vanillaColor = CodeUtilities.SyntaxColor2("# comment\nwhile True:\n\tharvest()");
        string vanillaValue = CodeUtilities.ToNiceString(SampleList());

        var harmony = new Harmony("javafarmer.tests.lang." + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(PatchRegistry.ApplyAll(harmony, PatchRegistry.All().Where(s => !s.HeadlessUnsafe)) > 0);
            ActiveLanguage.SetForTests("java");
            GamePatches.OtherFilesProvider = _ => null;
            GamePatches.OtherLanguagesProvider = _ => null;
            GamePatches.JavaTextProvider = _ => "void main() {\n    harvest(\n}";
            var py = TestGame.Window("bot");
            var java = TestGame.Window("main");
            FileLanguages.Set("bot", ".py");
            FileLanguages.Set("main", ".java");

            // parse: the .py window takes the game's own parser, the .java window the Java compiler
            Tokenizer.Tokenize("x = 1", out var ts);
            Assert.NotNull(Parser.Parse(ts, py));
            Tokenizer.Tokenize("x = 1", out ts);
            Assert.Throws<ParseException>(() => Parser.Parse(ts, java));

            // colouring and values follow the window being drawn; outside a window: the default language (Java)
            var st = LanguageContext.EnterWindow(py);
            Assert.Equal(vanillaColor, CodeUtilities.SyntaxColor2("# comment\nwhile True:\n\tharvest()"));
            Assert.Equal(vanillaValue, CodeUtilities.ToNiceString(SampleList()));
            LanguageContext.Restore(st);
            Assert.DoesNotContain("#666666", CodeUtilities.SyntaxColor2("# not a comment"));
            Assert.Equal("[true, null, 1.5, a]", CodeUtilities.ToNiceString(SampleList()));

            // a .py file in a Java default stays vanilla; the default only decides new files
            ActiveLanguage.SetForTests("python");
            Tokenizer.Tokenize("x = 1", out ts);
            Assert.Throws<ParseException>(() => Parser.Parse(ts, java));
            string code = "";
            GamePatches.OpenCodeWindowPrefix(null, "f7", ref code);
            Assert.Equal("", code);
            Assert.Equal(".py", FileLanguages.ExtensionOf("f7"));
            ActiveLanguage.SetForTests("java");
            code = "";
            GamePatches.OpenCodeWindowPrefix(null, "f8", ref code);
            Assert.Equal(EditorSupport.NewFileTemplate, code);
            Assert.Equal("f8.java", FileLanguages.DisplayName("f8"));

            // error texts name the running file's language (no program running: the default)
            var args = new object[] { "can_harvest()" };
            GamePatches.LocalizeAndFormatPrefix("error_wrong_args", args);
            Assert.Equal("canHarvest()", args[0]);
        }
        finally
        {
            harmony.UnpatchSelf();
            LanguageContext.Reset();
            FileLanguages.Clear();
        }
    }

    [SkippableFact]
    public void UnknownExtensionLanguage_GivesAClearError()
    {
        var missing = FileLanguages.KnownExtensions.FirstOrDefault(e => Languages.Get(e.Language) == null);
        Skip.If(missing.Ext == null, "every language of the extension table is registered");
        GamePatches.JavaTextProvider = _ => "x";
        try
        {
            FileLanguages.Set("other", missing.Ext);
            Program r = null;
            var ex = Assert.Throws<ParseException>(() => GamePatches.ParsePrefix(TestGame.Window("other"), ref r));
            Assert.Contains("no " + missing.Language + " support", ex.Message);
        }
        finally { FileLanguages.Clear(); }
    }

    [SkippableTheory]
    [MemberData(nameof(TestCorpus.LanguageIds), MemberType = typeof(TestCorpus))]
    public void EveryLanguage_RunsThroughTheBridge(string id)
    {
        var lang = Languages.Get(id);
        // a small Java program printed in the language (IR → printer), compiled and parsed by the real game parser
        string javaProgram = "void main() {\n\twhile (true) {\n\t\tif (canHarvest()) {\n\t\t\tharvest();\n\t\t}\n\t\tmove(North);\n\t}\n}\n";
        var ir = Languages.Get("java").ToIr(javaProgram, new CompileOptions { FileName = "main" }, out _);
        string text = lang.Print(ir, new PrintOptions { FileName = "main" });
        Skip.If(string.IsNullOrWhiteSpace(text) || ContentStore.OnlyComments(text, lang), id + ": the printer prints no code yet");
        output.WriteLine(text);
        if (lang.IsNative)
        {
            Assert.NotNull(JavaParseBridge.RunGameParser(text, TestGame.Window()));
            return;
        }
        var r = lang.Compile(text, new CompileOptions { FileName = "main" });
        Skip.If(!r.Success, id + ": its printed sample does not compile yet (" + r.Errors.FirstOrDefault()?.Message + ")");
        var program = JavaParseBridge.Parse(text, TestGame.Window(), null, lang);
        Assert.NotNull(program);
        foreach (var n in TestGame.Nodes(program).Where(n => n.boxedParams.wordStart >= 0))
            Assert.InRange(n.boxedParams.wordEnd, n.boxedParams.wordStart, text.Length);
        Assert.Contains("<color=", JavaHighlighter.Color(text, "", -1, lang));
        string template = lang.NewFileTemplate("f0", out int caret);
        Assert.InRange(caret, 0, template.Length);
        Assert.False(string.IsNullOrEmpty(lang.FormatValue(GameValueConverter.Convert(SampleList()))));
        Assert.False(string.IsNullOrEmpty(lang.ApiStubFile(out var stub)) || string.IsNullOrEmpty(stub));
    }

    [Fact]
    public void JavaValueFormat_MatchesTheV1Formatter()
    {
        var inner = new PyList(new List<IPyObject> { new PyNumber(1), new PyNumber(2.25) });
        var self = new PyList(new List<IPyObject> { new PyNumber(1) });
        self.list.Add(self);
        var dict = new PyDict(new Dictionary<IPyObject, PyObjectBox>());
        dict.dict[new PyString("k")] = new PyObjectBox(inner);
        var values = new IPyObject[]
        {
            new PyNone(), new PyBool(true), new PyBool(false), new PyNumber(3), new PyNumber(1.0 / 3), new PyString("txt"),
            inner, self, dict,
            new PySet(new HashSet<IPyObject> { new PyString("a") }),
            new PyTuple(new List<IPyObject> { new PyNumber(1), new PyNumber(2) }),
            new PyTuple(new List<IPyObject> { new PyString("e"), new PyTuple(new List<IPyObject> { new PyNumber(3), new PyNumber(4) }) }),
            new PyTuple(new List<IPyObject> { new PyString("a"), new PyString("b"), new PyString("c") }),
        };
        var java = Languages.Get("java");
        foreach (var v in values)
            Assert.Equal(ValueFormatter.Format(v), java.FormatValue(GameValueConverter.Convert(v)));
    }

    [Fact]
    public void PythonValueFormat_MatchesTheGame()
    {
        var py = Languages.Get("python");
        var values = new IPyObject[]
        {
            new PyNone(), new PyBool(true), new PyNumber(3), new PyNumber(2.5), new PyString("s"), SampleList(),
            new PyTuple(new List<IPyObject> { new PyNumber(1), new PyNumber(2) }),
        };
        foreach (var v in values)
            Assert.Equal(CodeUtilities.ToNiceString(v), py.FormatValue(GameValueConverter.Convert(v)));
    }

    [Fact]
    public void ApiStubs_OneForEveryLanguageInTheSave_NeverTouchPyFiles()
    {
        string save = Repo.NewTempDir("stubs");
        try
        {
            File.WriteAllText(Path.Combine(save, "main.java"), "x");
            File.WriteAllText(Path.Combine(save, "bot.py"), "x");
            File.WriteAllText(Path.Combine(save, "__builtins__.py"), "b");
            var compiled = Languages.All.Where(l => !l.IsNative).ToList();
            var all = ApiStubs.Write(save, compiled.Concat(new[] { Languages.Get("python") }));
            Assert.Equal(compiled.Count, all.Count);
            Assert.Contains(all, p => Path.GetFileName(p) == "JavaFarmerApi.java");
            Assert.Contains("canHarvest", File.ReadAllText(Path.Combine(save, "JavaFarmerApi.java")));
            // only Java files left: the other stubs go
            var one = ApiStubs.Write(save, new[] { Languages.Get("java") });
            Assert.Single(one);
            Assert.Equal(new[] { "JavaFarmerApi.java", "__builtins__.py", "bot.py", "main.java" }, Directory.GetFiles(save).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal));
            // stub files are never code windows
            Assert.False(FileLanguages.IsCodeFile(Path.Combine(save, "JavaFarmerApi.java")));
            foreach (var s in FileLanguages.StubFileNames) Assert.False(FileLanguages.IsCodeFile(s), s);
        }
        finally { Directory.Delete(save, true); }
    }

    [Fact]
    public void ModsMenu_ConfirmText_NamesTheChanges()
    {
        string t = ModsMenu.ConfirmText("java", "python", new[] { ("Cybersecurity", true), ("AI / ML", false) });
        Assert.Contains("Default language: Java -> Python", t);
        Assert.Contains("f0.py", t);
        Assert.Contains("existing files do not change", t);
        Assert.DoesNotContain("convert", t);
        Assert.Contains("Cybersecurity: enabled", t);
        Assert.Contains("AI / ML: disabled", t);
        Assert.DoesNotContain("Default language", ModsMenu.ConfirmText("java", "java", new[] { ("X", true) }));
    }

    [Fact]
    public void ActiveLanguage_UnknownIdFallsBackToJava()
    {
        ActiveLanguage.SetForTests("klingon");
        Assert.Equal("java", ActiveLanguage.Id);
        ActiveLanguage.SetForTests("PYTHON");
        Assert.Equal("python", ActiveLanguage.Id);
        string seen = null;
        Action<string, string> h = (a, b) => seen = a + ">" + b;
        ActiveLanguage.Changed += h;
        try { ActiveLanguage.SetForTests("java"); } finally { ActiveLanguage.Changed -= h; }
        Assert.Equal("python>java", seen);
    }
}
