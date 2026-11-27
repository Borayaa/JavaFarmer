using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests;

/// <summary>
/// Corpus layout (tests/corpus/&lt;lang&gt;/):
///   valid/   programs that must compile (a sub-directory = one multi-file program: its files see each other)
///   diff/    valid pure-computation programs also used for differential execution
///   invalid/ programs that must fail; marker lines give the expected message (substring) and exact span text
///   docs-exceptions.txt  docs blocks this language cannot express, one "&lt;md path&gt;#&lt;span index&gt;: reason" per line
/// </summary>
public static class Corpus
{
    public sealed class Program
    {
        public string Lang, Rel, Path, Source, Module;
        /// <summary>Other files of the same multi-file program: module name → source.</summary>
        public Dictionary<string, string> Others = new();
        /// <summary>Relative file names (with extension) of the whole group, this file first.</summary>
        public List<string> GroupFiles = new();
        public Dictionary<string, string> GroupSources = new();
        public CompileOptions Options => new() { FileName = Module, OtherFiles = Others };
    }

    static IEnumerable<string> Files(LanguageHooks h, string sub, bool recurse)
    {
        var dir = System.IO.Path.Combine(h.CorpusDir, sub);
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.GetFiles(dir, "*" + h.Extension).OrderBy(x => x, StringComparer.Ordinal)) yield return f;
        if (!recurse) yield break;
        foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.Ordinal))
            foreach (var f in Directory.GetFiles(d, "*" + h.Extension).OrderBy(x => x, StringComparer.Ordinal)) yield return f;
    }

    /// <summary>Theory rows (lang, relative path) for valid/ and diff/ (or just diff/).</summary>
    public static IEnumerable<object[]> Rows(string kind)
    {
        var rows = new List<object[]>();
        foreach (var h in LanguageHooks.All)
        {
            IEnumerable<string> files = kind switch
            {
                "valid" => Files(h, "valid", true).Concat(Files(h, "diff", false)),
                "diff" => Files(h, "diff", false),
                "invalid" => Files(h, "invalid", false),
                _ => Enumerable.Empty<string>()
            };
            foreach (var f in files) rows.Add(new object[] { h.Id, System.IO.Path.GetRelativePath(h.CorpusDir, f).Replace('\\', '/') });
        }
        if (rows.Count == 0) rows.Add(new object[] { "-", "-" });   // xunit needs a row; the test skips it
        return rows;
    }

    public static Program Load(LanguageHooks h, string rel)
    {
        var path = System.IO.Path.Combine(h.CorpusDir, rel);
        var p = new Program { Lang = h.Id, Rel = rel, Path = path, Source = File.ReadAllText(path), Module = h.ModuleName(path) };
        p.GroupFiles.Add(System.IO.Path.GetFileName(path));
        p.GroupSources[System.IO.Path.GetFileName(path)] = p.Source;
        // a program in its own sub-directory of valid/ is multi-file
        if (rel.Count(c => c == '/') >= 2)
            foreach (var o in Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!, "*" + h.Extension).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (o == path) continue;
                var src = File.ReadAllText(o);
                p.Others[h.ModuleName(o)] = src;
                p.GroupFiles.Add(System.IO.Path.GetFileName(o));
                p.GroupSources[System.IO.Path.GetFileName(o)] = src;
            }
        return p;
    }

    /// <summary>(expected message substring, expected exact span text) from the marker lines of an invalid program.</summary>
    public static (string Message, string At) Expected(LanguageHooks h, string source)
    {
        string Find(string marker)
        {
            foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith(marker)) return t.Substring(marker.Length).TrimEnd('\r');
            }
            return null;
        }
        return (Find(h.ErrorMarker), Find(h.AtMarker));
    }

    /// <summary>docs-exceptions.txt of a language: "path#index" → reason. Lines starting with "--" are comments.</summary>
    public static Dictionary<string, string> DocsExceptions(LanguageHooks h)
    {
        var res = new Dictionary<string, string>();
        var f = System.IO.Path.Combine(h.CorpusDir, "docs-exceptions.txt");
        if (!File.Exists(f)) return res;
        foreach (var raw in File.ReadAllLines(f))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("--")) continue;
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0) res[line] = "";
            else res[line.Substring(0, colon).Trim()] = line.Substring(colon + 2).Trim();
        }
        return res;
    }
}

/// <summary>
/// Code spans of the game's EN docs. The markdown dialect has no fenced blocks: code is in single backticks and a span
/// may cover several lines. A span is a BLOCK when it contains a newline or stands alone on its line; otherwise INLINE.
/// Spans are numbered per file from 0 in order of appearance (inline and block alike): "scripting/lists.md#3".
/// </summary>
public static class DocsCorpus
{
    public sealed class CodeSpan
    {
        public string File, Id, Text;
        public int Index, Start, End;   // [Start, End) of the text between the backticks
        public bool IsBlock;
    }

    public static List<CodeSpan> Spans(string relPath, string markdown)
    {
        var res = new List<CodeSpan>();
        int i = 0, idx = 0;
        while (i < markdown.Length)
        {
            int open = markdown.IndexOf('`', i);
            if (open < 0) break;
            int close = markdown.IndexOf('`', open + 1);
            if (close < 0) break;
            var text = markdown.Substring(open + 1, close - open - 1);
            bool lineStart = open == 0 || markdown[open - 1] == '\n';
            bool lineEnd = close + 1 >= markdown.Length || markdown[close + 1] == '\n' || markdown[close + 1] == '\r';
            res.Add(new CodeSpan
            {
                File = relPath, Index = idx, Id = relPath + "#" + idx, Text = text, Start = open + 1, End = close,
                IsBlock = text.Contains('\n') || (lineStart && lineEnd)
            });
            idx++;
            i = close + 1;
        }
        return res;
    }

    /// <summary>Every code span of every EN docs page (relative paths with '/').</summary>
    public static List<CodeSpan> All()
    {
        var res = new List<CodeSpan>();
        if (!Directory.Exists(RepoPaths.GameDocs)) return res;
        foreach (var f in Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/');
            res.AddRange(Spans(rel, File.ReadAllText(f).Replace("\r\n", "\n")));
        }
        return res;
    }
}
