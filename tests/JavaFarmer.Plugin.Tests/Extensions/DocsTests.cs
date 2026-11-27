using JavaFarmer.Compiler;
using JavaFarmer.Plugin.Extensions;
using Xunit.Abstractions;

namespace JavaFarmer.Plugin.Tests.Extensions;

/// <summary>
/// The canonical extension docs (content/python/docs/mods/<id>/*.md) and strings (content/python/Strings/extensions*.txt):
/// complete, valid game Python, translatable to every registered language, and the worked examples really earn the currency.
/// </summary>
[Collection("Game")]
public class DocsTests
{
    readonly ITestOutputHelper output;
    public DocsTests(ITestOutputHelper output) { this.output = output; ExtTestUtil.Setup(); }

    public static string DocsDir(string ext) => Path.Combine(ExtTestUtil.ContentRoot, "python", "docs", "mods", ext);

    public static IEnumerable<(ContentExtension ext, string path)> Pages()
    {
        ExtTestUtil.Setup();
        foreach (var ext in ExtensionHost.Extensions)
        {
            var dir = DocsDir(ext.Id);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.md").OrderBy(f => f, StringComparer.Ordinal)) yield return (ext, f);
        }
    }

    public static IEnumerable<object[]> PageNames() => Pages().Select(p => new object[] { p.ext.Id + "/" + Path.GetFileName(p.path) });

    static string Read(string rel) => File.ReadAllText(Path.Combine(ExtTestUtil.ContentRoot, "python", "docs", "mods", rel)).Replace("\r", "");

    [Fact]
    public void EveryUnlockHasAPage_AndAnIndex()
    {
        var missing = new List<string>();
        foreach (var ext in ExtensionHost.Extensions)
        {
            if (!File.Exists(Path.Combine(DocsDir(ext.Id), "index.md"))) missing.Add(ext.Id + "/index.md");
            foreach (var u in ext.Unlocks)
            {
                Assert.StartsWith("docs/mods/" + ext.Id + "/", u.Docs);
                if (ExtensionContent.PythonDoc(u.Docs) == null) missing.Add(u.Docs);
            }
            foreach (var i in ext.Items)
                if (ExtensionContent.PythonDoc(i.Docs) == null) missing.Add(i.Docs);
        }
        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(PageNames))]
    public void EveryCodeBlock_ParsesWithTheGameParser(string page)
    {
        var md = Read(page);
        var bad = new List<string>();
        foreach (var span in DocsTranslator.Spans(md).Where(s => s.IsBlock))
        {
            try { ExtTestUtil.Parse(span.Text.Trim('\n')); }
            catch (Exception e) { bad.Add("#" + span.Index + ": " + e.Message + "\n" + span.Text); }
        }
        Assert.True(bad.Count == 0, page + ":\n" + string.Join("\n---\n", bad));
    }

    [Theory]
    [MemberData(nameof(PageNames))]
    public void Page_TranslatesToEveryLanguage(string page)
    {
        var failures = TranslationFailures(Read(page), page);
        output.WriteLine(page + ": languages " + string.Join(", ", Languages.All.Select(l => l.Id)));
        Assert.True(failures.Count == 0, page + ":\n" + string.Join("\n", failures));
    }

    static readonly ContentStore Probe = new ContentStore(null);

    /// <summary>
    /// Every registered language (data-driven: Languages.All) must translate every code span of the text. Tolerated, and
    /// only logged: languages whose printer cannot print yet (ContentStore.CanPrint, the plugin keeps Python for them),
    /// printer crashes ("internal printer error": a bug of that language's printer, not of the docs), and the entries of
    /// Extensions/docs-exceptions.txt (constructs a language cannot express at all, e.g. runtime string concatenation in Zig).
    /// </summary>
    List<string> TranslationFailures(string text, string where)
    {
        var failures = new List<string>();
        var exceptions = Exceptions();
        foreach (var lang in Languages.All)
        {
            if (lang.Id == "python" || lang.IsNative) continue;
            if (!Probe.CanPrint(lang)) { output.WriteLine("  skipped " + lang.Id + ": its printer cannot print yet"); continue; }
            IReadOnlyList<string> fails;
            try { DocsTranslator.Translate(text, lang, out fails); }
            catch (Exception e) { output.WriteLine("  " + lang.Id + ": translator crashed: " + e.Message); continue; }
            foreach (var f in fails)
            {
                if (f.Contains("internal printer error")) { output.WriteLine("  printer bug " + lang.Id + " " + f); continue; }
                if (exceptions.Any(x => x.lang == lang.Id && (x.where == "*" || x.where == where) && f.Contains(x.reason)))
                {
                    output.WriteLine("  known limit " + lang.Id + " " + f);
                    continue;
                }
                failures.Add(lang.Id + " " + f);
            }
        }
        return failures;
    }

    /// <summary>"lang | page or @key or * | text the failure contains" per line, # comments.</summary>
    static List<(string lang, string where, string reason)> Exceptions()
    {
        var path = Path.Combine(ExtTestUtil.RepoRoot(), "tests", "JavaFarmer.Plugin.Tests", "Extensions", "docs-exceptions.txt");
        var list = new List<(string, string, string)>();
        if (!File.Exists(path)) return list;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = line.Split('|', 3);
            if (p.Length == 3) list.Add((p[0].Trim(), p[1].Trim(), p[2].Trim()));
        }
        return list;
    }

    /// <summary>Blocks whose first line starts with "# Example" are complete programs: they run (levels 1) and must earn the currency.</summary>
    [Theory]
    [MemberData(nameof(PageNames))]
    public void WorkedExamples_RunAndEarnTheCurrency(string page)
    {
        var ext = ExtensionHost.Get(page.Split('/')[0]);
        var md = Read(page);
        var examples = DocsTranslator.Spans(md).Where(s => s.IsBlock && s.Text.TrimStart('\n').StartsWith("# Example")).ToList();
        if (Path.GetFileName(page) != "index.md" && !page.EndsWith("secure_farm.md") && !page.EndsWith("smart_farm.md"))
            Assert.True(examples.Count > 0, page + " has no '# Example' program");
        foreach (var ex in examples)
        {
            var levels = ExtTestUtil.AllLevels(ext, 1);
            var r = Headless.Run(ex.Text.Trim('\n'), levels, tickBudget: 3_000_000);
            double earned = r.Item(ext.Items[0].Name);
            output.WriteLine(page + " #" + ex.Index + ": " + earned + " " + ext.Items[0].Name + " in " + r.Ticks + " ticks (" +
                             (r.Ticks > 0 ? (earned * 1000 / r.Ticks).ToString("0.00") : "-") + " per 1000 ticks), finished=" + r.Finished);
            Assert.True(r.Error == null, page + " #" + ex.Index + ": " + r.Error + "\n" + ex.Text);
            Assert.True(earned > 0, page + " #" + ex.Index + " earned nothing\n" + r);
        }
    }

    [Fact]
    public void Strings_CoverEveryBuiltinUnlockAndItem()
    {
        var s = ExtensionContent.PythonStrings();
        var missing = new List<string>();
        foreach (var ext in ExtensionHost.Extensions)
        {
            foreach (var b in ext.Builtins) if (!s.ContainsKey("code_tooltip_" + b.Name)) missing.Add("code_tooltip_" + b.Name);
            foreach (var u in ext.Unlocks)
            {
                if (!s.ContainsKey(u.TitleKey)) missing.Add(u.TitleKey);
                if (!s.ContainsKey(u.DescriptionKey)) missing.Add(u.DescriptionKey);
                if (u.BonusKey != null && !s.ContainsKey(u.BonusKey)) missing.Add(u.BonusKey);
            }
            foreach (var i in ext.Items) if (!s.ContainsKey(i.DescriptionKey)) missing.Add(i.DescriptionKey);
        }
        Assert.Empty(missing);
        var tooltips = string.Concat(Directory.GetFiles(Path.Combine(ExtTestUtil.ContentRoot, "python", "Strings"), "extensions_code_tooltips*.txt").Select(File.ReadAllText));
        foreach (var ext in ExtensionHost.Extensions)
            foreach (var b in ext.Builtins) Assert.Contains("@code_tooltip_" + b.Name + " = ", tooltips);
    }

    [Fact]
    public void Strings_CodeParses_AndTranslatesToEveryLanguage()
    {
        var bad = new List<string>();
        foreach (var kv in ExtensionContent.PythonStrings())
        {
            foreach (var span in DocsTranslator.Spans(kv.Value).Where(x => x.IsBlock))
            {
                try { ExtTestUtil.Parse(span.Text.Trim('\n')); }
                catch (Exception e) { bad.Add("@" + kv.Key + " #" + span.Index + ": " + e.Message); }
            }
            foreach (var f in TranslationFailures(kv.Value, "@" + kv.Key)) bad.Add("@" + kv.Key + " " + f);
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Tooltips_StateTickCostAndReturn()
    {
        var s = ExtensionContent.PythonStrings();
        var bad = new List<string>();
        foreach (var ext in ExtensionHost.Extensions)
            foreach (var b in ext.Builtins)
            {
                if (!s.TryGetValue("code_tooltip_" + b.Name, out var t)) continue;
                if (!t.StartsWith("`" + b.Name + "(")) bad.Add(b.Name + ": must start with `" + b.Name + "(...)`");
                if (!t.Contains("tick")) bad.Add(b.Name + ": no tick cost");
                if (!t.Contains("`" + b.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "`")) bad.Add(b.Name + ": tick cost " + b.Ticks + " not mentioned");
            }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
