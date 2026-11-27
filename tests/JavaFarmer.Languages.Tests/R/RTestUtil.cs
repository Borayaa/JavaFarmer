namespace JavaFarmer.Languages.Tests.R;

/// <summary>Helpers for the R frontend / printer tests.</summary>
public static class RTestUtil
{
    public static readonly RLanguage Lang = new();

    public static CompileResult Compile(string src, string file = "main", Dictionary<string, string> others = null)
    {
        var r = Lang.Compile(src, new CompileOptions { FileName = file, OtherFiles = others });
        Assert.True(r.Success, "unexpected errors: " + string.Join("; ", r.Errors.Select(e => e + " [" + Snip(src, e) + "]")));
        return r;
    }

    /// <summary>Emitted Python without the helper definitions (__j...) at the top.</summary>
    public static string Py(string src, Dictionary<string, string> others = null)
    {
        var py = Compile(src, "main", others).Python.Replace("\r\n", "\n");
        var lines = py.Split('\n').ToList();
        var res = new List<string>();
        bool inHelper = false;
        foreach (var l in lines)
        {
            if (l.StartsWith("def __j")) { inHelper = true; continue; }
            if (inHelper && (l.StartsWith(" ") || l.StartsWith("\t"))) continue;
            inHelper = false;
            res.Add(l);
        }
        return string.Join("\n", res).Trim();
    }

    public static IrModule ToIr(string src, string file = "main")
    {
        var m = Lang.ToIr(src, new CompileOptions { FileName = file }, out var errs);
        Assert.True(errs.Count == 0, "unexpected errors: " + string.Join("; ", errs.Select(e => e + " [" + Snip(src, e) + "]")));
        Assert.NotNull(m);
        return m;
    }

    public static Diagnostic Error(string src, Dictionary<string, string> others = null)
    {
        var r = Lang.Compile(src, new CompileOptions { FileName = "main", OtherFiles = others });
        Assert.False(r.Success, "expected an error, got:\n" + r.Python);
        Assert.NotEmpty(r.Errors);
        return r.Errors[0];
    }

    public static string Snip(string src, Diagnostic d) =>
        d.Start >= 0 && d.End <= src.Length && d.End >= d.Start ? src.Substring(d.Start, d.End - d.Start) : "?";

    public static string Print(IrModule m, PrintOptions o = null) => Lang.Print(m, o ?? new PrintOptions());

    /// <summary>Segment kind of the n-th occurrence of text in the emitted Python.</summary>
    public static Segment SegmentOf(CompileResult r, string text, int occurrence = 0)
    {
        int at = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            at = r.Python.IndexOf(text, at + 1, StringComparison.Ordinal);
            Assert.True(at >= 0, "'" + text + "' not found in:\n" + r.Python);
        }
        var seg = r.Map.Find(at);
        Assert.NotNull(seg);
        return seg;
    }

    public static string Dump(IrNode n, bool types = false) => JavaFarmer.Languages.Tests.Python.PyTestUtil.Dump(n, types);
}
