using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.Zig;

/// <summary>
/// Corpus checks that do not need the Zig toolchain: every diff program's emitted Python (with the Zig print prelude)
/// prints exactly the hand-derived tests/corpus/zig/diff/&lt;name&gt;.expected; extension docs never crash the printer.
/// </summary>
public class ZigCorpusTests
{
    readonly ITestOutputHelper output;
    public ZigCorpusTests(ITestOutputHelper output) { this.output = output; }

    static ILanguage Zig => ZigLanguageTests.Zig;

    public static IEnumerable<object[]> DiffPrograms() =>
        Directory.GetFiles(Path.Combine(RepoPaths.Corpus("zig"), "diff"), "*.zig").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [SkippableTheory]
    [MemberData(nameof(DiffPrograms))]
    public void DiffProgram_PythonPrintsTheExpectedOutput(string name)
    {
        var dir = Path.Combine(RepoPaths.Corpus("zig"), "diff");
        var expectedPath = Path.Combine(dir, name + ".expected");
        Assert.True(File.Exists(expectedPath), "missing " + expectedPath);
        var python = Proc.Find("python3") ?? Proc.Find("python");
        Skip.If(python == null, "SKIPPED: python3 not found on PATH");
        var src = File.ReadAllText(Path.Combine(dir, name + ".zig"));
        var r = Zig.Compile(src, new CompileOptions { FileName = name });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var tmp = RepoPaths.NewTempDir("zigdiff");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "gen.py"), ZigPrelude.Text + Normalise.RawStrings(r.Python));
            var (exit, outp) = Proc.Run(python, "-I gen.py", tmp);
            Assert.True(exit == 0, "python failed: " + outp + "\n" + r.Python);
            output.WriteLine(outp);
            Assert.Equal(Normalise.Output(File.ReadAllText(expectedPath)), Normalise.Output(outp));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

}

/// <summary>Registers a test builtin with GameApi: runs in the non-parallel GameApiRegistrations collection.</summary>
[Collection(GameApiRegistrations.Name)]
public class ZigExtensionDocsTests
{
    readonly ITestOutputHelper output;
    public ZigExtensionDocsTests(ITestOutputHelper output) { this.output = output; }
    static ILanguage Zig => ZigLanguageTests.Zig;

    /// <summary>
    /// Regression: DocsTranslator over the extension docs (content/python/docs/mods/**/*.md) crashed the Zig printer
    /// (a lone prose expression of unknown type). Every block must print without an internal error, also with
    /// registered extension builtins of unusual types.
    /// </summary>
    [Fact]
    public void ExtensionDocs_NeverCrashThePrinter()
    {
        var root = Path.Combine(RepoPaths.Root, "content", "python", "docs", "mods");
        Skip.IfNot(Directory.Exists(root), "no extension docs at " + root);
        var names = new[] { "zt_scan", "zt_text", "zt_map", "zt_any", "zt_list" };
        GameApi.Register(names[0], "host: string, port?: int", "list<int>", "test", "zigtest");
        GameApi.Register(names[1], "s: string", "string", "test", "zigtest");
        GameApi.Register(names[2], "", "map<string,any>", "test", "zigtest");
        GameApi.Register(names[3], "*values: any", "any", "test", "zigtest");
        GameApi.Register(names[4], "xs: list<float>", "list<list<int>>", "test", "zigtest");
        try
        {
            var py = Compiler.Languages.Get("python");
            var crashes = new List<string>();
            int blocks = 0;
            var extra = new[] { "zt_scan(\"a\")", "x = zt_text('b')\nprint(x)", "m = zt_map()\nprint(m)", "v = zt_any(1, 2)", "print(zt_list([1.5]))", "zt_scan()" };
            var texts = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                .SelectMany(f => DocsTranslator.Spans(File.ReadAllText(f).Replace("\r", "")).Where(s => s.IsBlock).Select(s => (f + "#" + s.Index, s.Text)))
                .Concat(extra.Select((t, i) => ("extra#" + i, t)));
            foreach (var (id, text) in texts)
            {
                var ir = py.ToIr(text.Trim('\n'), new CompileOptions { FileName = "Docs" }, out _);
                if (ir == null) continue;
                blocks++;
                foreach (var snippet in new[] { true, false })
                {
                    ((IPrinterDiagnostics)Zig).Print(ir, new PrintOptions { FileName = "Docs", Snippet = snippet }, out var problems);
                    foreach (var p in problems) if (p.Contains("internal printer error")) crashes.Add(id + ": " + p);
                }
            }
            output.WriteLine(blocks + " blocks printed");
            Assert.True(crashes.Count == 0, string.Join("\n", crashes));
        }
        finally { foreach (var n in names) GameApi.Unregister(n); }
    }

}
