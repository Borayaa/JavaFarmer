using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace JavaFarmer.Compiler.Tests;

/// <summary>SPEC §11: pure-computation programs run with the real JDK and, after compiling, with CPython.
/// CPython gets small stubs: print/quick_print/str format values like Java, and string literals are made raw
/// (the game's tokenizer does not process escapes). Numbers are compared after normalising 3.0 → 3.</summary>
public class DifferentialTests
{
    readonly ITestOutputHelper output;
    public DifferentialTests(ITestOutputHelper output) { this.output = output; }

    const string Prelude = @"import builtins as __b
def __fmt(v):
    if v is None:
        return 'null'
    if v is True:
        return 'true'
    if v is False:
        return 'false'
    if isinstance(v, float):
        if v == int(v) and abs(v) < 1e15:
            return __b.str(int(v))
        return repr(v)
    if isinstance(v, list):
        return '[' + ', '.join(__fmt(x) for x in v) + ']'
    return __b.str(v)
def print(*a):
    __b.print(' '.join(__fmt(x) for x in a))
def quick_print(*a):
    print(*a)
def str(*a):
    if len(a) == 0:
        return ''
    return __fmt(a[0])
";

    public static IEnumerable<object[]> DiffFiles() =>
        Directory.GetFiles(Path.Combine(TestUtil.CorpusDir, "diff"), "*.java").OrderBy(x => x).Select(f => new object[] { Path.GetFileName(f) });

    static string Normalise(string s) => Regex.Replace(s.Replace("\r\n", "\n"), @"(-?\d+)\.0(?!\d)", "$1").Trim();

    /// <summary>Makes game string literals raw for CPython: double every backslash inside '...' / "..." tokens.</summary>
    static string RawStrings(string py) =>
        Regex.Replace(py, "(['\"])(.*?)\\1", m => m.Groups[1].Value + m.Groups[2].Value.Replace("\\", "\\\\") + m.Groups[1].Value);

    [Theory]
    [MemberData(nameof(DiffFiles))]
    public void JavaAndGeneratedPythonPrintTheSame(string file)
    {
        var java = TestUtil.FindOnPath("java");
        var python = TestUtil.FindOnPath("python3");
        if (java == null || python == null)
        {
            output.WriteLine("SKIPPED: java or python3 not on PATH");
            return;
        }
        var src = File.ReadAllText(Path.Combine(TestUtil.CorpusDir, "diff", file));
        var name = Path.GetFileNameWithoutExtension(file);
        var py = TestUtil.CompileOk(src, name);

        var dir = Path.Combine(Path.GetTempPath(), "jf-diff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, file), src);
            File.WriteAllText(Path.Combine(dir, "gen.py"), Prelude + RawStrings(py));
            var (je, jo) = TestUtil.Run(java, file, dir, 120000);
            Assert.True(je == 0, "java failed: " + jo);
            var (pe, po) = TestUtil.Run(python, "-I gen.py", dir);
            Assert.True(pe == 0, "python failed: " + po + "\n" + py);
            output.WriteLine(jo);
            Assert.Equal(Normalise(jo), Normalise(po));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
