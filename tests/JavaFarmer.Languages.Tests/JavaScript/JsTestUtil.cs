using System.Text;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>Helpers for the JavaScript frontend / printer tests.</summary>
public static class JsTestUtil
{
    public static readonly JavaScriptLanguage Lang = new();
    public static ILanguage Py => Compiler.Languages.Get("python");

    public static CompileOptions Opts(string file = "main", Dictionary<string, string> others = null) =>
        new() { FileName = file, OtherFiles = others };

    public static IrModule ToIr(string src, string file = "main", Dictionary<string, string> others = null)
    {
        var m = Lang.ToIr(src, Opts(file, others), out var errs);
        Assert.True(errs.Count == 0, "unexpected errors: " + string.Join("; ", errs.Select(e => e + " [" + Snip(src, e) + "]")));
        Assert.NotNull(m);
        return m;
    }

    public static string ToPy(string src, string file = "main", Dictionary<string, string> others = null)
    {
        var r = Lang.Compile(src, Opts(file, others));
        Assert.True(r.Success, "compile failed: " + string.Join("; ", r.Errors.Select(e => e + " [" + Snip(src, e) + "]")));
        return r.Python;
    }

    public static CompileResult Compile(string src, string file = "main", Dictionary<string, string> others = null) => Lang.Compile(src, Opts(file, others));

    public static Diagnostic Error(string src, string file = "main", Dictionary<string, string> others = null)
    {
        var r = Lang.Compile(src, Opts(file, others));
        Assert.False(r.Success, "expected an error, got:\n" + r.Python);
        Assert.NotEmpty(r.Errors);
        return r.Errors[0];
    }

    public static string Snip(string src, Diagnostic d) =>
        d.Start >= 0 && d.End <= src.Length && d.End >= d.Start ? src.Substring(d.Start, d.End - d.Start) : "?";

    /// <summary>Python source → IR → JavaScript.</summary>
    public static string FromPython(string py, bool snippet, out IReadOnlyList<string> problems)
    {
        var ir = Py.ToIr(py, new CompileOptions { FileName = "Docs" }, out var errs);
        Assert.True(ir != null, "python: " + string.Join("; ", errs));
        return Lang.Print(ir, new PrintOptions { FileName = "Docs", Snippet = snippet }, out problems);
    }

    public static string FromPython(string py, bool snippet = false) => FromPython(py, snippet, out _);

    /// <summary>Print(ToIr(src)) and check that it compiles to the same Python shape.</summary>
    public static string RoundTrip(string src)
    {
        var first = ToPy(src);
        var printed = Lang.Print(ToIr(src), new PrintOptions { FileName = "main" });
        var again = Lang.Compile(printed, Opts());
        Assert.True(again.Success, "printed code does not compile: " + string.Join("; ", again.Errors) + "\n" + printed);
        Assert.Equal(Normalise.PythonShape(first), Normalise.PythonShape(again.Python));
        return printed;
    }

    /// <summary>Segments overlapping the first occurrence of text in the Python.</summary>
    public static List<Segment> SegmentsAt(CompileResult r, string pyText)
    {
        int at = r.Python.IndexOf(pyText, StringComparison.Ordinal);
        Assert.True(at >= 0, "'" + pyText + "' not in:\n" + r.Python);
        int end = at + pyText.Length;
        return r.Map.Segments.Where(s => s.PyEnd > at && s.PyStart < end).ToList();
    }

    public static string Dump(IrNode n) => JavaFarmer.Languages.Tests.Python.PyTestUtil.Dump(n);
}
