using System.Text.RegularExpressions;

namespace JavaFarmer.Compiler.Tests;

/// <summary>Every valid corpus program compiles, passes the game-parser oracle and has a valid source map;
/// every invalid program fails with the declared message and span.</summary>
public class CorpusTests
{
    public static IEnumerable<object[]> ValidFiles()
    {
        foreach (var sub in new[] { "valid", "diff" })
        {
            var dir = Path.Combine(TestUtil.CorpusDir, sub);
            foreach (var f in Directory.GetFiles(dir, "*.java").OrderBy(x => x)) yield return new object[] { sub + "/" + Path.GetFileName(f) };
            foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x))
                foreach (var f in Directory.GetFiles(d, "*.java").OrderBy(x => x))
                    yield return new object[] { sub + "/" + Path.GetFileName(d) + "/" + Path.GetFileName(f) };
        }
    }

    public static IEnumerable<object[]> InvalidFiles() =>
        Directory.GetFiles(Path.Combine(TestUtil.CorpusDir, "invalid"), "*.java").OrderBy(x => x).Select(f => new object[] { Path.GetFileName(f) });

    static (string Source, string Name, Dictionary<string, string> Others) Load(string rel)
    {
        var path = Path.Combine(TestUtil.CorpusDir, rel);
        var others = new Dictionary<string, string>();
        foreach (var o in Directory.GetFiles(Path.GetDirectoryName(path)!, "*.java"))
            if (o != path && Path.GetDirectoryName(rel) is string d && d.Contains('/')) others[Path.GetFileNameWithoutExtension(o)] = File.ReadAllText(o);
        return (File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), others);
    }

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void ValidProgram_CompilesAndPassesGameParser(string rel)
    {
        var (src, name, others) = Load(rel);
        var r = TestUtil.Compile(src, name, others);
        Assert.True(r.Success, rel + ": " + string.Join("; ", r.Errors.Select(e => e + " [" + TestUtil.Slice(src, e.Start, e.End) + "]")));
        var oracle = GameOracle.TryParse(r.Python);
        Assert.True(oracle == null, rel + ": game parser rejected the Python: " + oracle + "\n" + r.Python);
        TestUtil.AssertMapValid(src, r);
    }

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void InvalidProgram_FailsWithMessageAndSpan(string file)
    {
        var src = File.ReadAllText(Path.Combine(TestUtil.CorpusDir, "invalid", file));
        var expectedMsg = Regex.Match(src, @"^// ERROR: (.*)$", RegexOptions.Multiline).Groups[1].Value.TrimEnd('\r');
        var expectedAt = Regex.Match(src, @"^// AT: (.*)$", RegexOptions.Multiline).Groups[1].Value.TrimEnd('\r');
        Assert.NotEmpty(expectedMsg);
        var err = TestUtil.CompileErr(src, Path.GetFileNameWithoutExtension(file));
        Assert.Contains(expectedMsg, err.Message);
        Assert.Equal(expectedAt, TestUtil.Slice(src, err.Start, err.End));
    }

    [Fact]
    public void Corpus_IsNotEmpty()
    {
        Assert.True(ValidFiles().Count() >= 15);
        Assert.True(InvalidFiles().Count() >= 30);
    }
}
