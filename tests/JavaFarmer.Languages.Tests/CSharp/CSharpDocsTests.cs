using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.CSharp;

/// <summary>
/// content/csharp: the 48 docs pages (same paths as the game's EN docs) and every C# code block in them. A block must
/// compile with the C# frontend (and the game parser must accept the emitted Python), on its own or after the earlier
/// blocks of the same page (a lesson builds on its previous snippets); static classes shown on a page are the other
/// windows of the page. Blocks that are deliberately partial are listed in tests/corpus/csharp/content-fragments.txt.
/// </summary>
public class CSharpDocsTests
{
    readonly ITestOutputHelper output;
    public CSharpDocsTests(ITestOutputHelper output) { this.output = output; }

    static readonly ILanguage Cs = Compiler.Languages.Get("csharp");
    static string ContentDir => Path.Combine(RepoPaths.Root, "content", "csharp", "docs");

    static Dictionary<string, string> Fragments()
    {
        var res = new Dictionary<string, string>();
        var f = Path.Combine(RepoPaths.Corpus("csharp"), "content-fragments.txt");
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

    static IEnumerable<string> Pages() =>
        Directory.Exists(ContentDir) ? Directory.GetFiles(ContentDir, "*.md", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal) : Enumerable.Empty<string>();

    static string Rel(string full) => Path.GetRelativePath(ContentDir, full).Replace('\\', '/');

    /// <summary>A lone word / operator list in prose, not code.</summary>
    static bool IsProse(string t)
    {
        t = t.Trim();
        if (t.Length == 0) return true;
        if (t.Contains('\n')) return false;
        if (Regex.IsMatch(t, @"^[A-Za-z_]\w*$")) return true;
        if (Regex.IsMatch(t, @"^[\s,+\-*/%<>=!.:#()\[\]{}&|^?]+$")) return true;
        return false;
    }

    static readonly Regex ClassName = new(@"\bclass\s+([A-Za-z_]\w*)");

    [SkippableFact]
    public void EveryGameDocsPage_HasACSharpVersion()
    {
        Skip.IfNot(Directory.Exists(RepoPaths.GameDocs), "game docs not found");
        Skip.IfNot(Directory.Exists(ContentDir), "content/csharp/docs not written yet");
        var missing = Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/'))
            .Where(rel => !File.Exists(Path.Combine(ContentDir, rel))).ToList();
        Assert.True(missing.Count == 0, "missing pages: " + string.Join(", ", missing));
    }

    [SkippableFact]
    public void EveryCodeBlock_Compiles_OrIsAListedFragment()
    {
        Skip.IfNot(Directory.Exists(ContentDir), "content/csharp/docs not written yet");
        var fragments = Fragments();
        var failures = new List<string>();
        var stale = new List<string>();
        int ok = 0, listed = 0;
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var md = File.ReadAllText(page).Replace("\r\n", "\n");
            var spans = DocsCorpus.Spans(rel, md).Where(s => s.IsBlock && !IsProse(s.Text)).ToList();
            var classes = new Dictionary<string, string>();
            foreach (var s in spans)
            {
                var m = ClassName.Match(s.Text);
                if (m.Success && !s.Text.Contains("static void Main")) classes[m.Groups[1].Value] = s.Text;
            }
            var context = new List<string>();
            foreach (var s in spans)
            {
                var code = s.Text.Trim('\n');
                var cls = ClassName.Match(code);
                string module = cls.Success && !code.Contains("static void Main") ? cls.Groups[1].Value : "Docs";
                var others = classes.Where(kv => kv.Key != module).ToDictionary(kv => kv.Key, kv => kv.Value);
                string why = TryCompile(code, module, others);
                if (why != null && !cls.Success && context.Count > 0)
                {
                    var withContext = string.Join("\n", context) + "\n" + code;
                    if (TryCompile(withContext, module, others) == null) why = null;
                }
                bool isFragment = fragments.ContainsKey(s.Id);
                if (isFragment) { listed++; if (why == null) stale.Add(s.Id); }
                else if (why == null) ok++;
                else failures.Add(s.Id + ": " + why + "\n    " + code.Replace("\n", "\n    "));
                if (!cls.Success && why == null) context.Add(code);
            }
        }
        output.WriteLine($"{ok} blocks compile, {listed} listed fragments, {failures.Count} failures");
        foreach (var st in stale) output.WriteLine("stale fragment entry (now compiles): " + st);
        Assert.True(failures.Count == 0, failures.Count + " C# docs blocks do not compile (fix them or list them in tests/corpus/csharp/content-fragments.txt):\n" + string.Join("\n", failures));
    }

    static string TryCompile(string code, string module, Dictionary<string, string> others)
    {
        var r = Cs.Compile(code, new CompileOptions { FileName = module, OtherFiles = others });
        if (!r.Success) return string.Join("; ", r.Errors.Select(e => e.Message + " [" + HarnessTests.Slice(code, e.Start, e.End) + "]"));
        var oracle = GameOracle.TryParse(r.Python);
        return oracle == null ? null : "game parser: " + oracle;
    }

    /// <summary>Python left in the C# pages: def, elif, True/False/None, snake_case builtins in code.</summary>
    [SkippableFact]
    public void CodeSpans_ContainNoPython()
    {
        Skip.IfNot(Directory.Exists(ContentDir), "content/csharp/docs not written yet");
        var bad = new List<string>();
        var py = new Regex(@"(^|\s)(def|elif|import|from)\s|\b(True|False|None)\b|\bprint\(|\b(can_harvest|get_entity_type|get_pos_x|get_world_size|spawn_drone|num_items|use_item|get_companion)\b", RegexOptions.Multiline);
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            foreach (var s in DocsCorpus.Spans(rel, File.ReadAllText(page).Replace("\r\n", "\n")))
            {
                if (s.Text.Contains("{{")) bad.Add(s.Id + ": contains {{ (the game reads it as a placeholder)");
                if (Regex.IsMatch(s.Text, @"(^|\n)#")) bad.Add(s.Id + ": a code line starts with # (the game reads it as a heading)");
                if (py.IsMatch(s.Text) && !s.Text.Contains("//")) bad.Add(s.Id + ": looks like Python: " + s.Text.Split('\n')[0]);
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>content/csharp/Strings and unlock_display_code.txt: every code span that is a whole statement compiles.</summary>
    [SkippableFact]
    public void StringOverrides_CodeSpansCompile()
    {
        var dir = Path.Combine(RepoPaths.Root, "content", "csharp", "Strings");
        Skip.IfNot(Directory.Exists(dir), "content/csharp/Strings not written yet");
        var bad = new List<string>();
        int ok = 0;
        foreach (var f in Directory.GetFiles(dir, "*.txt").OrderBy(x => x, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(f).Replace("\r\n", "\n");
            // entries "@key = value": keyword tooltips of code_tooltips.txt show syntax templates, not programs
            var entries = Regex.Split(text, @"(?m)^(?=@)").Where(e => e.StartsWith("@")).ToList();
            var codes = new List<string>();
            foreach (var entry in entries)
            {
                var key = entry.Substring(1, Math.Max(0, entry.IndexOf(' ') - 1));
                if (Path.GetFileName(f) == "code_tooltips.txt" && GameApi.Find(key.Replace("code_tooltip_", "")) == null) continue;
                foreach (Match m in Regex.Matches(entry, "`([^`]+)`")) codes.Add(m.Groups[1].Value);
            }
            foreach (var raw in codes)
            {
                var code = raw.Trim();
                if (code.Contains("...")) continue;
                if (!(code.EndsWith(";") || code.EndsWith("}")) || code.StartsWith(")") || code.StartsWith("new ") || code.Contains("OtherFile.") || code.Contains("{0}") || code.Contains("{1}")) continue;
                // placeholder names a tooltip example may lean on (declared only when the example uses them)
                var ctx = new StringBuilder();
                foreach (var (name, decl) in new[] { ("list", "List<int> list = [1, 2];"), ("oldList", "List<int> oldList = [1];"), ("DoSomething", "void DoSomething() { }"),
                                                     ("DoSomethingElse", "void DoSomethingElse() { }"), ("HarvestColumn", "void HarvestColumn() { }"), ("Task", "void Task() { }") })
                    if (Regex.IsMatch(code, @"\b" + name + @"\b") && !Regex.IsMatch(code, @"[\w>\]?]\s+" + name + @"\s*[=;(]")) ctx.Append(decl).Append('\n');
                var src = ctx + code + "\n";
                var r = Cs.Compile(src, new CompileOptions { FileName = "main" });
                if (r.Success) { ok++; continue; }
                bad.Add(Path.GetFileName(f) + ": " + r.Errors[0].Message + "\n    " + code.Replace("\n", "\n    "));
            }
            foreach (var line in text.Split('\n'))
                if (line.StartsWith("@") && line.Contains(" = ") && line.Substring(line.IndexOf(" = ")).Contains("{{")) bad.Add(Path.GetFileName(f) + ": {{ in " + line);
        }
        output.WriteLine(ok + " string code spans compile");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
        Assert.True(File.Exists(Path.Combine(RepoPaths.Root, "content", "csharp", "unlock_display_code.txt")));
    }
}
