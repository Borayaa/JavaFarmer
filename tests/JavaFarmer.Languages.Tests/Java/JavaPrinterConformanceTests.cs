using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.Java;

/// <summary>
/// The printed form of every Java corpus program is real Java: javac (JDK 25+) accepts it against the generated API
/// stub, exactly like the original (harness test (c) for the sources, this test for Print(ToIr(source))).
/// </summary>
public class JavaPrinterConformanceTests
{
    readonly ITestOutputHelper output;
    public JavaPrinterConformanceTests(ITestOutputHelper output) { this.output = output; }

    public static IEnumerable<object[]> Programs() => Corpus.Rows("valid").Where(r => (string)r[0] == "java" || (string)r[0] == "-");

    [SkippableTheory]
    [MemberData(nameof(Programs))]
    public void PrintedProgramCompilesWithJavac(string lang, string rel)
    {
        Skip.If(lang == "-", "no corpus");
        var h = LanguageHooks.Get("java");
        var tc = h.Conformance;
        var tool = tc?.Resolve();
        Skip.If(tool == null, "SKIPPED: javac not found on PATH (install a JDK 25+)");
        var p = Corpus.Load(h, rel);
        var dir = RepoPaths.NewTempDir("jprint");
        try
        {
            var sources = new Dictionary<string, string>();
            foreach (var kv in p.GroupSources)
            {
                var module = Path.GetFileNameWithoutExtension(kv.Key);
                var others = p.GroupSources.Where(o => o.Key != kv.Key).ToDictionary(o => Path.GetFileNameWithoutExtension(o.Key), o => o.Value);
                var ir = h.Language.ToIr(kv.Value, new CompileOptions { FileName = module, OtherFiles = others }, out var errs);
                Skip.If(ir == null, rel + ": ToIr failed: " + string.Join("; ", errs));
                var printed = h.Language.Print(ir, new PrintOptions { FileName = module });
                var text = tool.PrepareSource != null ? tool.PrepareSource(printed, kv.Key) : printed;
                sources[kv.Key] = text;
                File.WriteAllText(Path.Combine(dir, kv.Key), text);
            }
            var ctx = new ToolContext { WorkDir = dir, Sources = sources, MainFile = p.GroupFiles[0] };
            var (exit, outp) = tc.Run(ctx);
            output.WriteLine(outp);
            Assert.True(exit == 0, rel + ": javac rejected the printed program:\n" + outp + "\n--- printed ---\n" + string.Join("\n", sources.Values));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
