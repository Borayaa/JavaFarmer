using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.CSharp;

/// <summary>
/// Harness steps (c) and (d) for C#, with the real compiler in-process (Roslyn): every valid corpus program compiles
/// against Farm.cs, every diff program prints the same as the generated Python under CPython, and the printer's
/// output of every valid program is real C# too.
/// </summary>
[Collection("csharp-roslyn")]
public class CSharpRoslynTests
{
    readonly ITestOutputHelper output;
    public CSharpRoslynTests(ITestOutputHelper output) { this.output = output; }

    static LanguageHooks Hooks => LanguageHooks.Get("csharp");

    public static IEnumerable<object[]> Valid() => Rows("valid");
    public static IEnumerable<object[]> Diff() => Rows("diff");

    static IEnumerable<object[]> Rows(string kind)
    {
        var rows = Corpus.Rows(kind).Where(r => (string)r[0] == "csharp").Select(r => new[] { r[1] }).ToList();
        if (rows.Count == 0) rows.Add(new object[] { "-" });   // xunit needs a row; the test skips it
        return rows;
    }

    [SkippableTheory]
    [MemberData(nameof(Valid))]
    public void C_Valid_CompilesWithRoslyn(string rel)
    {
        Skip.If(rel == "-", "no C# corpus files");
        var p = Corpus.Load(Hooks, rel);
        var r = CSharpRoslyn.Compile(p.GroupSources);
        Assert.True(r.Success, rel + ": Roslyn rejected the program:\n" + r);
    }

    [SkippableTheory]
    [MemberData(nameof(Valid))]
    public void E2_PrintedProgram_CompilesWithRoslyn(string rel)
    {
        Skip.If(rel == "-", "no C# corpus files");
        var p = Corpus.Load(Hooks, rel);
        var lang = Hooks.Language;
        var ir = lang.ToIr(p.Source, p.Options, out var errs);
        Assert.True(ir != null, string.Join("; ", errs));
        var printed = lang.Print(ir, Hooks.RoundTripOptions(p.Module));
        var group = new Dictionary<string, string>(p.GroupSources) { [p.GroupFiles[0]] = printed };
        var r = CSharpRoslyn.Compile(group);
        Assert.True(r.Success, rel + ": Roslyn rejected the printed program:\n" + r + "\n--- printed ---\n" + printed);
    }

    [SkippableTheory]
    [MemberData(nameof(Diff))]
    public void D_Diff_RoslynRunPrintsTheSameAsGeneratedPython(string rel)
    {
        var python = Proc.Find("python3") ?? Proc.Find("python");
        Skip.If(python == null, "SKIPPED: python3 not found on PATH");
        Skip.If(rel == "-", "no C# diff programs");
        var h = Hooks;
        var p = Corpus.Load(h, rel);
        var r = h.Language.Compile(p.Source, p.Options);
        Assert.True(r.Success, rel + ": " + string.Join("; ", r.Errors));
        var real = CSharpRoslyn.Run(p.GroupSources);
        var dir = RepoPaths.NewTempDir("diff-csharp");
        try
        {
            File.WriteAllText(Path.Combine(dir, "gen.py"), h.PythonPrelude + Normalise.RawStrings(r.Python));
            var (pe, po) = Proc.Run(python, "-I gen.py", dir);
            Assert.True(pe == 0, "python failed: " + po + "\n" + r.Python);
            output.WriteLine(real);
            Assert.Equal(h.NormaliseOutput(real), h.NormaliseOutput(po));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Stub_CompilesAlone_AndCoversEveryBuiltin()
    {
        var lang = Compiler.Languages.Get("csharp");
        var stub = lang.ApiStubFile(out var name);
        Assert.Equal("Farm.cs", name);
        var r = CSharpRoslyn.Compile(new Dictionary<string, string> { ["empty.cs"] = "Harvest();\nMove(North);\nConsole.WriteLine(Entities.Carrot);\n" });
        Assert.True(r.Success, r.ToString());
        foreach (var b in GameApi.Builtins)
        {
            if (b.PythonName is "print" or "min" or "max" or "abs") continue;
            Assert.Contains(" " + GameApi.ToStyle(b.PythonName, NamingStyle.Pascal) + "(", stub);
        }
    }

    [Fact]
    public void Stub_ContainsRegisteredExtensionBuiltins()
    {
        GameApi.Register("scan_port_cs", "host: string, port?: int", "bool", "Scans a port.", extension: "cybersecurity");
        try
        {
            var stub = Compiler.Languages.Get("csharp").ApiStubFile(out _);
            Assert.Contains("public static bool ScanPortCs(string host) => default!;", stub);
            Assert.Contains("public static bool ScanPortCs(string host, int port) => default!;", stub);
            var src = "bool open = ScanPortCs(\"10.0.0.1\", 22);\nif (open) Console.WriteLine(\"open\");\n";
            Assert.True(CSharpRoslyn.Compile(new Dictionary<string, string> { ["main.cs"] = src }).Success);
            var c = Compiler.Languages.Get("csharp").Compile(src, new CompileOptions { FileName = "main" });
            Assert.True(c.Success, string.Join("; ", c.Errors));
            Assert.Contains("scan_port_cs(\"10.0.0.1\", 22)", c.Python);
        }
        finally { GameApi.Unregister("scan_port_cs"); }
    }
}

[CollectionDefinition("csharp-roslyn", DisableParallelization = true)]
public sealed class CSharpRoslynCollection { }
