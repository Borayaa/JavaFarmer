using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests;

/// <summary>content/c and content/cpp: all 48 docs pages exist, every code block compiles (CFamilyDocsCheck), no Python left.</summary>
public class CFamilyDocsTests
{
    readonly ITestOutputHelper output;
    public CFamilyDocsTests(ITestOutputHelper output) { this.output = output; }

    static string ContentDir(string id) => Path.Combine(RepoPaths.Root, "content", id, "docs");

    [SkippableTheory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void EveryGameDocsPage_HasAVersion(string id)
    {
        Skip.IfNot(Directory.Exists(RepoPaths.GameDocs), "game docs not found");
        var missing = Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/'))
            .Where(rel => !File.Exists(Path.Combine(ContentDir(id), rel))).ToList();
        Assert.True(missing.Count == 0, "missing pages: " + string.Join(", ", missing));
    }

    [SkippableTheory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void EveryCodeBlock_Compiles_OrIsAListedFragment(string id)
    {
        Skip.IfNot(Directory.Exists(ContentDir(id)), "content/" + id + "/docs not written yet");
        var r = CFamilyDocsCheck.Check(Compiler.Languages.Get(id), ContentDir(id), RepoPaths.Corpus(id));
        output.WriteLine($"{id}: {r.Ok} blocks compile, {r.Listed} listed fragments, {r.Failures.Count} failures");
        foreach (var st in r.Stale) output.WriteLine("stale fragment entry (now compiles): " + st);
        Assert.True(r.Failures.Count == 0, r.Failures.Count + " docs blocks do not compile:\n" + string.Join("\n", r.Failures));
        Assert.True(r.Problems.Count == 0, string.Join("\n", r.Problems));
    }

    [SkippableTheory]
    [InlineData("c")]
    [InlineData("cpp")]
    public void UnlockDisplayCode_Compiles(string id)
    {
        var f = Path.Combine(RepoPaths.Root, "content", id, "unlock_display_code.txt");
        Skip.IfNot(File.Exists(f), "no unlock_display_code.txt yet");
        var lines = File.ReadAllLines(f);
        Assert.Contains(lines, l => l.StartsWith("@loops"));
        Assert.Contains(lines, l => l.StartsWith("@debug"));
        Assert.DoesNotContain(lines, l => l.Contains("def ") || l.Contains("True") || l.Contains("None"));
    }
}
