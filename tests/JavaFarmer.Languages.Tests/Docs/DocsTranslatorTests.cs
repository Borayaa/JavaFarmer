using static JavaFarmer.Languages.Tests.Java.B;

namespace JavaFarmer.Languages.Tests.Docs;

/// <summary>A stand-in Python frontend: known snippets → hand-built IR (the real one is Languages/Python).</summary>
sealed class FakePython : LanguageBase
{
    public readonly Dictionary<string, Func<IrModule>> Known = new();
    public override string Id => "fakepy";
    public override string DisplayName => "Python";
    public override string FileExtension => ".py";
    protected override NamingStyle ApiNaming => NamingStyle.Snake;
    protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors)
    {
        if (Known.TryGetValue(source.Trim(), out var f)) return f();
        errors.Add(new Diagnostic("invalid syntax", 0, 0));
        return null;
    }
    protected override string PrintImpl(IrModule module, PrintOptions options) => "";
    public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source) => new List<(int, int, TokenKind)>();
    public override IReadOnlyCollection<string> Keywords => new List<string>();
    public override string NewFileTemplate(string fileName, out int caret) { caret = 0; return ""; }
    public override string ApiStubFile(out string fileName) { fileName = "x"; return ""; }
}

public class DocsTranslatorTests
{
    static readonly ILanguage Java = Compiler.Languages.Get("java");

    static FakePython Fake()
    {
        var p = new FakePython();
        p.Known["True"] = () => Script(Do(T));
        p.Known["None"] = () => Script(Do(Null));
        p.Known["can_harvest()"] = () => Script(Stmt("can_harvest"));
        p.Known["num_items(Items.Hay)"] = () => Script(Stmt("num_items", C("Items", "Hay")));
        p.Known["Items.Hay"] = () => Script(Do(C("Items", "Hay")));
        p.Known["[2, 6, 12, 7]"] = () => Script(Do(List(I(2), I(6), I(12), I(7))));
        p.Known["18"] = () => Script(Do(I(18)));
        p.Known["move_n_dir"] = () => Script(Do(N(Local("move_n_dir"))));
        p.Known["if get_entity_type() == Entities.Bush:\n\tdo_a_flip()"] =
            () => Script(If(Bin(IrBinOp.Eq, Call("get_entity_type"), C("Entities", "Bush")), new IrStmt[] { Stmt("do_a_flip") }));
        p.Known["t = (1, 2)"] = () => Script(Set(Local("t"), Tuple(I(1), I(2))));
        return p;
    }

    static string Tr(string md, out IReadOnlyList<string> failures, ILanguage source = null) =>
        DocsTranslator.Translate(md, Java, out failures, new DocsTranslateOptions { Source = source ?? Fake() });

    [Fact]
    public void SampleMarkdown()
    {
        var md = "# Senses\nThe function `can_harvest()` returns `True` or `None`.\n" +
                 "For example `num_items(Items.Hay)` returns how much hay you have.\n\n" +
                 "Do a flip if you are over a bush:\n`if get_entity_type() == Entities.Bush:\n\tdo_a_flip()`\n" +
                 "`list` is now `[2, 6, 12, 7]` and `sum` is `18`, see `move_n_dir`.\n" +
                 "<unlock=senses>![](Bush) {{harvest_key}} [link](docs/x.md)</unlock> <spoiler=hint>`Items.Hay`</spoiler>\n";
        var outText = Tr(md, out var failures);
        Assert.Empty(failures);
        Assert.Equal("# Senses\nThe function `canHarvest()` returns `true` or `null`.\n" +
                     "For example `numItems(Items.Hay)` returns how much hay you have.\n\n" +
                     "Do a flip if you are over a bush:\n`if (getEntityType() == Entities.Bush) {\n\tdoAFlip();\n}`\n" +
                     "`list` is now `[2, 6, 12, 7]` and `sum` is `18`, see `moveNDir`.\n" +
                     "<unlock=senses>![](Bush) {{harvest_key}} [link](docs/x.md)</unlock> <spoiler=hint>`Items.Hay`</spoiler>\n", outText);
    }

    [Fact]
    public void KeywordsComeFromThePrinter()
    {
        var md = "`while` `for` `if` `else` `elif` `break` `continue` `def` `return` `and` `or` `not` `True` `False` `None`";
        var outText = Tr(md, out _);
        Assert.Equal("`while` `for` `if` `else` `else if` `break` `continue` `void` `return` `&&` `||` `!` `true` `false` `null`", outText);
    }

    [Fact]
    public void UntranslatableBlocksStayAndAreReported()
    {
        var md = "intro\n`t = (1, 2)`\nand\n`this is not code\nat all`\nend `whatever_name()`";
        var outText = Tr(md, out var failures);
        Assert.Equal(md, outText);
        Assert.Contains(failures, f => f.StartsWith("#0: ") && f.Contains("tuple"));
        Assert.Contains(failures, f => f.StartsWith("#1: "));
        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public void ProseIsUntouched()
    {
        var md = "# Title\nNo code here, only <unlock=x>text</unlock>, {{placeholders}}, ![](Sprite) and [links](docs/a.md).\n";
        Assert.Equal(md, Tr(md, out var f));
        Assert.Empty(f);
        Assert.Equal("", DocsTranslator.Translate("", Java));
        Assert.Equal("a `b", Tr("a `b", out _));   // unmatched backtick
    }

    [Fact]
    public void JavaToJavaIsStable()
    {
        // the translator is language-neutral: Java blocks through the Java frontend come back as Java
        var md = "Plant:\n`void main() {\n\tplant(Entities.Bush);\n}`\ninline `getPosX()` value `[1, 2]`.";
        var once = DocsTranslator.Translate(md, Java, out var failures, new DocsTranslateOptions { Source = Java });
        Assert.Empty(failures);
        Assert.Equal("Plant:\n`plant(Entities.Bush);`\ninline `getPosX()` value `[1, 2]`.", once);
    }

    [SkippableFact]
    public void SpanNumberingMatchesTheHarness()
    {
        var all = DocsCorpus.All();
        Skip.If(all.Count == 0, "game docs not found");
        foreach (var g in all.GroupBy(s => s.File))
        {
            var md = File.ReadAllText(Path.Combine(RepoPaths.GameDocs, g.Key)).Replace("\r\n", "\n");
            var mine = DocsTranslator.Spans(md);
            Assert.Equal(g.Count(), mine.Count);
            foreach (var (h, m) in g.Zip(mine))
            {
                Assert.Equal(h.Index, m.Index);
                Assert.Equal(h.Text, m.Text);
                Assert.Equal(h.IsBlock, m.IsBlock);
            }
        }
    }

    /// <summary>
    /// Every EN docs page through the real Python frontend: never throws, prose outside code spans is unchanged, and
    /// every translated block compiles as a Java program unless it is listed in tests/corpus/java/docs-exceptions.txt.
    /// </summary>
    [SkippableFact]
    public void AllEnglishDocs()
    {
        var py = Compiler.Languages.Get("python");
        Skip.If(py == null, "the Python frontend is not registered yet");
        var all = DocsCorpus.All();
        Skip.If(all.Count == 0, "game docs not found");
        var exceptions = Corpus.DocsExceptions(LanguageHooks.Get("java"));
        var bad = new List<string>();
        int translated = 0;
        foreach (var g in all.GroupBy(s => s.File))
        {
            var md = File.ReadAllText(Path.Combine(RepoPaths.GameDocs, g.Key)).Replace("\r\n", "\n");
            var outText = DocsTranslator.Translate(md, Java, out var failures, new DocsTranslateOptions { VerifyCompiles = true });
            // prose between the spans is identical
            var a = DocsTranslator.Spans(md); var b = DocsTranslator.Spans(outText);
            Assert.Equal(a.Count, b.Count);
            string Gap(string s, List<DocsSpan> sp, int k) => s.Substring(k == 0 ? 0 : sp[k - 1].End + 1, (k == sp.Count ? s.Length : sp[k].Start - 1) - (k == 0 ? 0 : sp[k - 1].End + 1));
            for (int i = 0; i <= a.Count; i++) Assert.Equal(Gap(md, a, i), Gap(outText, b, i));
            foreach (var f in failures)
            {
                int colon = f.IndexOf(':');
                var span = a[int.Parse(f.Substring(1, colon - 1))];
                var id = g.Key + "#" + span.Index;
                if (!span.IsBlock) continue;              // inline mentions are best-effort
                if (f.Contains(": not Python")) continue; // blocks the Python frontend rejects are tracked by it
                if (!exceptions.ContainsKey(id)) bad.Add(id + f.Substring(colon));
            }
            translated += a.Count(s => s.IsBlock);
        }
        Assert.True(bad.Count == 0, bad.Count + " of " + translated + " blocks fail (list them in tests/corpus/java/docs-exceptions.txt):\n" + string.Join("\n", bad));
    }
}
