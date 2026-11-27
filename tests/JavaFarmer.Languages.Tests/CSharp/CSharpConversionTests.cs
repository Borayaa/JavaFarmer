namespace JavaFarmer.Languages.Tests.CSharp;

/// <summary>
/// Switching a save to C#: every valid Java / Python corpus program goes through its frontend and the C# printer.
/// The printer either produces code that compiles with the C# frontend and with Roslyn, or reports a problem
/// (IPrinterDiagnostics) so the plugin keeps the original text: it never produces broken C# silently.
/// </summary>
[Collection("csharp-roslyn")]
public class CSharpConversionTests
{
    static readonly ILanguage Cs = Compiler.Languages.Get("csharp");

    public static IEnumerable<object[]> Programs()
    {
        foreach (var lang in new[] { "java", "python" })
        {
            var h = LanguageHooks.All.FirstOrDefault(x => x.Id == lang);
            if (h == null || !Directory.Exists(h.CorpusDir)) continue;
            foreach (var sub in new[] { "valid", "diff" })
            {
                var dir = Path.Combine(h.CorpusDir, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*" + h.Extension).OrderBy(x => x, StringComparer.Ordinal))
                    yield return new object[] { lang, sub + "/" + Path.GetFileName(f) };
            }
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Programs))]
    public void ConvertedProgram_CompilesOrReportsAProblem(string lang, string rel)
    {
        var h = LanguageHooks.Get(lang);
        Skip.If(h.Language == null, lang + " is not registered");
        var p = Corpus.Load(h, rel);
        var ir = h.Language.ToIr(p.Source, p.Options, out var errs);
        Assert.True(ir != null, rel + ": " + string.Join("; ", errs));
        var text = ((IPrinterDiagnostics)Cs).Print(ir, new PrintOptions { FileName = p.Module }, out var problems);
        if (problems.Count > 0) return;   // reported: the plugin keeps the original text
        var r = Cs.Compile(text, new CompileOptions { FileName = p.Module });
        Assert.True(r.Success, rel + ": the C# printer produced code that does not compile (and reported no problem): " +
            string.Join("; ", r.Errors.Select(e => e.Message + " [" + HarnessTests.Slice(text, e.Start, e.End) + "]")) + "\n--- printed ---\n" + text);
        var roslyn = CSharpRoslyn.Compile(new Dictionary<string, string> { [p.Module + ".cs"] = text });
        Assert.True(roslyn.Success, rel + ": Roslyn rejects the converted program:\n" + roslyn + "\n--- printed ---\n" + text);
    }

    /// <summary>A multi-file program converts as a whole: every printed window compiles with the other printed windows.</summary>
    [SkippableTheory]
    [InlineData("java", "valid/multi")]
    [InlineData("python", "-")]
    public void MultiFileProgram_ConvertsTogether(string lang, string dirRel)
    {
        var h = LanguageHooks.Get(lang);
        Skip.If(h.Language == null, lang + " is not registered");
        Dictionary<string, string> files;
        if (dirRel == "-")
            files = new Dictionary<string, string>
            {
                ["farm_utils"] = "count = 0\ndef plant_row(n):\n\tglobal count\n\tfor i in range(n):\n\t\tplant(Entities.Bush)\n\t\tmove(East)\n\t\tcount += 1\n",
                ["main"] = "import farm_utils\nfarm_utils.plant_row(get_world_size())\nprint(farm_utils.count)\n",
            };
        else
        {
            var dir = Path.Combine(h.CorpusDir, dirRel);
            Skip.IfNot(Directory.Exists(dir), "no multi-file program");
            files = Directory.GetFiles(dir, "*" + h.Extension).ToDictionary(f => h.ModuleName(f), File.ReadAllText);
        }
        var printed = new Dictionary<string, string>();
        foreach (var (name, src) in files)
        {
            var others = files.Where(kv => kv.Key != name).ToDictionary(kv => kv.Key, kv => kv.Value);
            var ir = h.Language.ToIr(src, new CompileOptions { FileName = name, OtherFiles = others }, out var errs);
            Assert.True(ir != null, name + ": " + string.Join("; ", errs));
            var text = ((IPrinterDiagnostics)Cs).Print(ir, new PrintOptions { FileName = name, OtherFiles = others.Keys.ToList() }, out var problems);
            Assert.True(problems.Count == 0, name + ": " + string.Join("; ", problems) + "\n" + text);
            printed[name] = text;
        }
        foreach (var (name, text) in printed)
        {
            var others = printed.Where(kv => kv.Key != name).ToDictionary(kv => kv.Key, kv => kv.Value);
            var r = Cs.Compile(text, new CompileOptions { FileName = name, OtherFiles = others });
            Assert.True(r.Success, name + ": " + string.Join("; ", r.Errors.Select(e => e.Message + " [" + HarnessTests.Slice(text, e.Start, e.End) + "]")) + "\n--- printed ---\n" + text);
        }
        // the main window with its libraries is real C# too
        foreach (var main in printed.Where(kv => !kv.Value.Contains("public static class")))
        {
            var group = printed.Where(kv => kv.Key == main.Key || kv.Value.Contains("public static class")).ToDictionary(kv => kv.Key + ".cs", kv => kv.Value);
            var roslyn = CSharpRoslyn.Compile(group);
            Assert.True(roslyn.Success, main.Key + ": " + roslyn + "\n" + string.Join("\n----\n", group.Values));
        }
    }
}
