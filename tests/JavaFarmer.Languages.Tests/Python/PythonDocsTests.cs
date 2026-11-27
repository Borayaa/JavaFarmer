using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests.Python;

/// <summary>
/// Every code span of the game's EN docs goes through the Python frontend (spans that are plain prose such as
/// "if" or "+, -, *" are skipped); failures must be listed in tests/corpus/python/docs-exceptions.txt.
/// Each parsed span also round-trips: ToIr(Print(ToIr(x))) has the same structure as ToIr(x), and when the game's
/// parser accepts x it accepts Print(ToIr(x)) too.
/// </summary>
public class PythonDocsTests
{
    static readonly HashSet<string> Words = new(PyTestUtil.Lang.Keywords.Concat(new[] { "True", "False", "None" }));

    /// <summary>Prose-like spans: a lone keyword, or only operators / punctuation (lists like "+, -, *").</summary>
    public static bool IsProse(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return true;
        if (t.Contains('\n')) return false;
        if (Words.Contains(t) && t != "True" && t != "False" && t != "None") return true;
        if (Regex.IsMatch(t, @"^[\s,+\-*/%<>=!.:#()\[\]{}]+$") && t != "()" && t != "[]" && t != "{}") return true;
        var parts = t.Split(',').Select(p => p.Trim()).ToList();
        if (parts.Count > 1 && parts.All(p => Words.Contains(p) || Regex.IsMatch(p, @"^[+\-*/%<>=!.]+$") || p == "...")) return true;
        return false;
    }

    /// <summary>The text to parse for a span: a lone header line ("if key in dict:") gets a pass body.</summary>
    public static string Prepare(string text)
    {
        var t = text.Replace("\r\n", "\n");
        if (!t.Contains('\n') && t.TrimEnd().EndsWith(":")) t = t.TrimEnd() + "\n\tpass";
        return t;
    }

    static Dictionary<string, string> Exceptions()
    {
        var res = new Dictionary<string, string>();
        var f = Path.Combine(RepoPaths.Corpus("python"), "docs-exceptions.txt");
        if (!File.Exists(f)) return res;
        foreach (var raw in File.ReadAllLines(f))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("--")) continue;
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            res[colon < 0 ? line : line.Substring(0, colon).Trim()] = colon < 0 ? "" : line.Substring(colon + 2);
        }
        return res;
    }

    [Fact]
    public void EveryDocsSpan_Parses_OrIsAListedException()
    {
        var spans = DocsCorpus.All();
        if (spans.Count == 0) return;   // no game copy
        var exc = Exceptions();
        var failures = new List<string>();
        var unexpectedPass = new List<string>();
        int parsed = 0;
        foreach (var sp in spans)
        {
            if (IsProse(sp.Text)) continue;
            var src = Prepare(sp.Text);
            var m = PyTestUtil.Lang.ToIr(src, new CompileOptions { FileName = "main" }, out var errs);
            bool listed = exc.ContainsKey(sp.Id);
            if (m == null)
            {
                if (!listed) failures.Add(sp.Id + ": " + string.Join("; ", errs.Select(e => e.Message + " [" + PyTestUtil.Snip(src, e) + "]")) + "   <<" + sp.Text.Replace("\n", "\\n") + ">>");
                continue;
            }
            if (listed) { unexpectedPass.Add(sp.Id); continue; }
            parsed++;
            var printed = PyTestUtil.Print(m);
            var m2 = PyTestUtil.Lang.ToIr(printed, new CompileOptions { FileName = "main" }, out var errs2);
            if (m2 == null) { failures.Add(sp.Id + ": reprint does not parse: " + string.Join("; ", errs2.Select(e => e.Message)) + "\n" + printed); continue; }
            var d1 = PyTestUtil.Dump(m);
            var d2 = PyTestUtil.Dump(m2);
            if (d1 != d2) failures.Add(sp.Id + ": round trip changed the IR\n--- source\n" + src + "\n--- printed\n" + printed + "\n--- ir1\n" + d1 + "--- ir2\n" + d2);
            if (GameOracle.TryParse(src) == null && GameOracle.TryParse(printed) is string err)
                failures.Add(sp.Id + ": the game rejects the reprint (" + err + ")\n" + printed);
        }
        Assert.True(failures.Count == 0, failures.Count + " docs span(s) failed:\n" + string.Join("\n", failures));
        Assert.True(unexpectedPass.Count == 0, "listed in docs-exceptions.txt but parse now: " + string.Join(", ", unexpectedPass));
        Assert.True(parsed > 300, "only " + parsed + " docs spans parsed");
    }
}
