using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests;

/// <summary>
/// The shared per-language test battery (docs/LANGUAGE-GUIDE.md §Testing). Rows come from tests/corpus/&lt;lang&gt;/ for
/// every LanguageHooks subclass. Missing tools / frontends skip with a message instead of failing.
/// </summary>
public class HarnessTests
{
    readonly ITestOutputHelper output;
    public HarnessTests(ITestOutputHelper output) { this.output = output; }

    public static IEnumerable<object[]> Valid() => Corpus.Rows("valid");
    public static IEnumerable<object[]> Invalid() => Corpus.Rows("invalid");
    public static IEnumerable<object[]> Diff() => Corpus.Rows("diff");
    public static IEnumerable<object[]> Langs() => LanguageHooks.All.Select(h => new object[] { h.Id });

    static LanguageHooks Hooks(string lang)
    {
        Skip.If(lang == "-", "no corpus files for any language");
        var h = LanguageHooks.Get(lang);
        Skip.If(h.Language == null, "language '" + lang + "' is not registered yet");
        return h;
    }

    static string Errors(CompileResult r, string src) =>
        string.Join("; ", r.Errors.Select(e => e + " [" + Slice(src, e.Start, e.End) + "]"));

    public static string Slice(string s, int start, int end) =>
        start >= 0 && end <= s.Length && start <= end ? s.Substring(start, end - start) : "<bad span " + start + "-" + end + ">";

    /// <summary>Source map invariants every successful compile must satisfy.</summary>
    public static void AssertMapValid(string src, CompileResult r)
    {
        var segs = r.Map.Segments;
        if (r.Python.Length == 0) { Assert.Empty(segs); return; }
        Assert.NotEmpty(segs);
        Assert.Equal(0, segs[0].PyStart);
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            Assert.True(s.PyEnd > s.PyStart, "empty segment " + i);
            if (i > 0) Assert.Equal(segs[i - 1].PyEnd, s.PyStart);
            if (s.JavaStart == -1) { Assert.Equal(-1, s.JavaEnd); Assert.Equal(SegmentKind.Synthetic, s.Kind); }
            else { Assert.InRange(s.JavaStart, 0, src.Length); Assert.InRange(s.JavaEnd, s.JavaStart, src.Length); }
        }
        Assert.Equal(r.Python.Length, segs[^1].PyEnd);
    }

    // ---------------------------------------------------------------- (a) + (b)
    [SkippableTheory]
    [MemberData(nameof(Valid))]
    public void A_Valid_CompilesWithValidMap_B_GameParserAccepts(string lang, string rel)
    {
        var h = Hooks(lang);
        var p = Corpus.Load(h, rel);
        var r = h.Language.Compile(p.Source, p.Options);
        Assert.True(r.Success, rel + ": " + Errors(r, p.Source));
        AssertMapValid(p.Source, r);
        var oracle = GameOracle.TryParse(r.Python);
        Assert.True(oracle == null, rel + ": game parser rejected the Python: " + oracle + "\n" + r.Python);
    }

    [SkippableTheory]
    [MemberData(nameof(Invalid))]
    public void A_Invalid_FailsWithMessageAndSpan(string lang, string rel)
    {
        var h = Hooks(lang);
        var p = Corpus.Load(h, rel);
        var (msg, at) = Corpus.Expected(h, p.Source);
        Assert.False(string.IsNullOrEmpty(msg), rel + ": missing '" + h.ErrorMarker + "' line");
        IReadOnlyList<Diagnostic> errors;
        if (h.Language.IsNative)
        {
            // native (Python): Compile is the identity, the frontend (ToIr) reports the errors
            var ir = h.Language.ToIr(p.Source, p.Options, out errors);
            Assert.True(ir == null, rel + ": expected a frontend error");
        }
        else
        {
            var r = h.Language.Compile(p.Source, p.Options);
            Assert.False(r.Success, rel + ": expected an error but compilation succeeded:\n" + r.Python);
            errors = r.Errors;
        }
        Assert.NotEmpty(errors);
        var e = errors[0];
        Assert.Contains(msg, e.Message);
        if (at != null) Assert.Equal(at, Slice(p.Source, e.Start, e.End));
    }

    // ---------------------------------------------------------------- (c)
    [SkippableTheory]
    [MemberData(nameof(Valid))]
    public void C_Valid_CompilesWithRealToolchain(string lang, string rel)
    {
        var h = Hooks(lang);
        var tc = h.Conformance;
        Skip.If(tc == null, lang + " has no real-toolchain conformance check");
        var tool = tc.Resolve();
        Skip.If(tool == null, "SKIPPED: " + tc.Exe + " not found on PATH. " + tc.SkipHint);
        var p = Corpus.Load(h, rel);
        var dir = RepoPaths.NewTempDir("conf-" + lang);
        try
        {
            var ctx = Write(tool, p, dir);
            var (exit, outp) = tc.Run(ctx);
            output.WriteLine(outp);
            Assert.True(exit == 0, rel + ": " + tool.Exe + " rejected the program:\n" + outp);
        }
        finally { TryDelete(dir); }
    }

    static ToolContext Write(Toolchain tool, Corpus.Program p, string dir)
    {
        var sources = new Dictionary<string, string>();
        foreach (var kv in p.GroupSources)
        {
            var text = tool.PrepareSource != null ? tool.PrepareSource(kv.Value, kv.Key) : kv.Value;
            sources[kv.Key] = text;
            File.WriteAllText(Path.Combine(dir, kv.Key), text);
        }
        return new ToolContext { WorkDir = dir, Sources = sources, MainFile = p.GroupFiles[0] };
    }

    static void TryDelete(string dir) { try { Directory.Delete(dir, true); } catch { } }

    // ---------------------------------------------------------------- (d)
    [SkippableTheory]
    [MemberData(nameof(Diff))]
    public void D_Diff_RealRunPrintsTheSameAsGeneratedPython(string lang, string rel)
    {
        var h = Hooks(lang);
        var runner = h.Runner;
        Skip.If(runner == null, lang + " has no differential runner");
        var tool = runner.Resolve();
        Skip.If(tool == null, "SKIPPED: " + runner.Exe + " not found on PATH. " + runner.SkipHint);
        var python = Proc.Find("python3") ?? Proc.Find("python");
        Skip.If(python == null, "SKIPPED: python3 not found on PATH");
        var p = Corpus.Load(h, rel);
        var r = h.Language.Compile(p.Source, p.Options);
        Assert.True(r.Success, rel + ": " + Errors(r, p.Source));
        var dir = RepoPaths.NewTempDir("diff-" + lang);
        try
        {
            var ctx = Write(tool, p, dir);
            var (re, ro) = runner.Run(ctx);
            Assert.True(re == 0, "real run failed: " + ro);
            File.WriteAllText(Path.Combine(dir, "gen.py"), h.PythonPrelude + Normalise.RawStrings(r.Python));
            var (pe, po) = Proc.Run(python, "-I gen.py", dir);
            Assert.True(pe == 0, "python failed: " + po + "\n" + r.Python);
            output.WriteLine(ro);
            Assert.Equal(h.NormaliseOutput(ro), h.NormaliseOutput(po));
        }
        finally { TryDelete(dir); }
    }

    // ---------------------------------------------------------------- (e)
    [SkippableTheory]
    [MemberData(nameof(Valid))]
    public void E_Valid_RoundTripsThroughThePrinter(string lang, string rel)
    {
        var h = Hooks(lang);
        var p = Corpus.Load(h, rel);
        var first = h.Language.Compile(p.Source, p.Options);
        Assert.True(first.Success, rel + ": " + Errors(first, p.Source));
        var ir = h.Language.ToIr(p.Source, p.Options, out var errs);
        Assert.True(ir != null, rel + ": ToIr failed: " + string.Join("; ", errs));
        var printed = h.Language.Print(ir, h.RoundTripOptions(p.Module));
        Assert.False(string.IsNullOrWhiteSpace(printed), rel + ": printer returned nothing");
        var again = h.Language.Compile(printed, p.Options);
        Assert.True(again.Success, rel + ": printed code does not compile: " + Errors(again, printed) + "\n--- printed ---\n" + printed);
        Assert.True(Normalise.PythonShape(first.Python) == Normalise.PythonShape(again.Python),
            rel + ": printed program emits different Python\n--- printed ---\n" + printed + "\n--- python (original) ---\n" + first.Python + "\n--- python (printed) ---\n" + again.Python);
    }

    // ---------------------------------------------------------------- (f)
    [SkippableTheory]
    [MemberData(nameof(Langs))]
    public void F_DocsBlocks_TranslateAndCompile(string lang)
    {
        var h = Hooks(lang);
        var py = Compiler.Languages.Get("python");
        Skip.If(py == null, "the Python frontend is not registered yet");
        Skip.If(h.Language.IsNative, "native language");
        var spans = DocsCorpus.All().Where(s => s.IsBlock).ToList();
        Skip.If(spans.Count == 0, "game docs not found at " + RepoPaths.GameDocs);
        var exceptions = Corpus.DocsExceptions(h);
        var failures = new List<string>();
        int ok = 0, notPython = 0, excepted = 0;
        var stale = new List<string>();
        foreach (var s in spans)
        {
            var ir = py.ToIr(s.Text, new CompileOptions { FileName = h.DocsModuleName }, out var perr);
            if (ir == null) { notPython++; continue; }   // tracked by the Python frontend's own exceptions
            string printed = h.Language.Print(ir, h.DocsOptions);
            var r = h.Language.Compile(printed, new CompileOptions { FileName = h.DocsModuleName });
            bool pass = r.Success && GameOracle.TryParse(r.Python) == null;
            if (exceptions.ContainsKey(s.Id)) { excepted++; if (pass) stale.Add(s.Id); continue; }
            if (pass) { ok++; continue; }
            failures.Add(s.Id + ": " + (r.Success ? "game parser: " + GameOracle.TryParse(r.Python) : Errors(r, printed)) +
                         "\n  python: " + s.Text.Replace("\n", "\n          ") + "\n  " + lang + ": " + printed.Replace("\n", "\n          "));
        }
        output.WriteLine($"{lang}: {ok} blocks ok, {excepted} listed exceptions, {notPython} not parsed by the Python frontend, {failures.Count} failures");
        foreach (var st in stale) output.WriteLine("stale exception (now passes): " + st);
        Assert.True(failures.Count == 0, failures.Count + " docs blocks fail (fix them or list them with a reason in tests/corpus/" + lang + "/docs-exceptions.txt):\n" + string.Join("\n", failures));
    }

    // ---------------------------------------------------------------- invariants
    [SkippableTheory]
    [MemberData(nameof(Langs))]
    public void Language_ContractBasics(string lang)
    {
        var h = Hooks(lang);
        var l = h.Language;
        Assert.Equal(lang, l.Id);
        Assert.False(string.IsNullOrEmpty(l.DisplayName));
        Assert.StartsWith(".", l.FileExtension);
        Assert.NotNull(l.Editor);
        Assert.NotEmpty(l.Keywords);
        var tpl = l.NewFileTemplate("main", out int caret);
        Assert.InRange(caret, 0, tpl.Length);
        // never throws on garbage
        foreach (var junk in new[] { "", "(((", "\"unterminated", "}}}}", "\0ÿ", "if", new string('(', 500) })
        {
            var r = l.Compile(junk, new CompileOptions { FileName = "main" });
            Assert.NotNull(r);
            var hl = l.Highlight(junk);
            Assert.Equal(junk.Length, hl.Sum(t => t.Length));
            l.ToIr(junk, new CompileOptions { FileName = "main" }, out _);
        }
        Assert.NotNull(l.FormatValue(new GameList(new object[] { 1.0, "a", null, true })));
        Assert.False(string.IsNullOrEmpty(l.ApiStubFile(out var stubName)));
        Assert.False(string.IsNullOrEmpty(stubName));
        Assert.NotNull(l.ApiName("can_harvest"));
        Assert.Equal("can_harvest", l.FromApiName(l.ApiName("can_harvest")));
    }
}
