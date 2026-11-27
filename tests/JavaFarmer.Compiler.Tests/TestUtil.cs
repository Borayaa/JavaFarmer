using System.Diagnostics;
using System.Text;

namespace JavaFarmer.Compiler.Tests;

public static class TestUtil
{
    public static string CorpusDir => Path.Combine(AppContext.BaseDirectory, "corpus");

    public static CompileResult Compile(string java, string fileName = "Main", Dictionary<string, string> others = null) =>
        JavaCompiler.Compile(java, new CompileOptions { FileName = fileName, OtherFiles = others });

    public static string CompileOk(string java, string fileName = "Main", Dictionary<string, string> others = null)
    {
        var r = Compile(java, fileName, others);
        Assert.True(r.Success, "expected success but got: " + string.Join("; ", r.Errors.Select(e => e + " [" + Slice(java, e.Start, e.End) + "]")));
        return r.Python;
    }

    public static Diagnostic CompileErr(string java, string fileName = "Main", Dictionary<string, string> others = null)
    {
        var r = Compile(java, fileName, others);
        Assert.False(r.Success, "expected an error but compilation succeeded:\n" + r.Python);
        Assert.NotEmpty(r.Errors);
        return r.Errors[0];
    }

    public static string Slice(string s, int start, int end) =>
        start >= 0 && end <= s.Length && start <= end ? s.Substring(start, end - start) : $"<bad span {start}-{end}>";

    /// <summary>Wraps statements in a compact-file main.</summary>
    public static string InMain(string body) => "void main() {\n" + body + "\n}\n";

    public static string FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir, exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public static (int Exit, string Output) Run(string exe, string args, string workDir, int timeoutMs = 60000)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var sb = new StringBuilder();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "timeout"); }
        sb.Append(stdout.Result);
        sb.Append(stderr.Result);
        return (p.ExitCode, sb.ToString());
    }

    /// <summary>Checks the SourceMap invariants every successful compile must satisfy.</summary>
    public static void AssertMapValid(string java, CompileResult r)
    {
        var segs = r.Map.Segments;
        Assert.NotEmpty(segs);
        Assert.Equal(0, segs[0].PyStart);
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            Assert.True(s.PyEnd > s.PyStart, $"empty segment {i}");
            if (i > 0) Assert.Equal(segs[i - 1].PyEnd, s.PyStart);
            if (s.JavaStart == -1)
            {
                Assert.Equal(-1, s.JavaEnd);
                Assert.Equal(SegmentKind.Synthetic, s.Kind);
            }
            else
            {
                Assert.InRange(s.JavaStart, 0, java.Length);
                Assert.InRange(s.JavaEnd, s.JavaStart, java.Length);
            }
        }
        Assert.Equal(r.Python.Length, segs[^1].PyEnd);
    }
}
