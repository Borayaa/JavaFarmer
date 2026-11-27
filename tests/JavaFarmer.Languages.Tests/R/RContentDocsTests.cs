using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>
/// content/r: the 48 docs pages (same paths as the game's EN docs), the Strings overrides and unlock_display_code.txt.
/// Every code block of a page (a span with a line break or alone on its line) must compile with the R frontend and the
/// emitted Python must pass the game parser, on its own or after the earlier blocks of the same page (a lesson builds
/// on its previous snippets). A block whose preceding line names a file (`utils.R`) is that code window; the other
/// labelled blocks of the page are the other windows. Exceptions: tests/corpus/r/content-exceptions.txt
/// ("&lt;md path&gt;#&lt;span index&gt;: reason", span numbering = DocsTranslator.Spans); stale entries fail.
/// </summary>
public class RContentDocsTests
{
    readonly ITestOutputHelper output;
    public RContentDocsTests(ITestOutputHelper output) { this.output = output; }

    static readonly ILanguage Rl = Compiler.Languages.Get("r");
    static string ContentRoot => Path.Combine(RepoPaths.Root, "content", "r");
    static string DocsDir => Path.Combine(ContentRoot, "docs");
    static string StringsDir => Path.Combine(ContentRoot, "Strings");
    static string EnStrings => Path.Combine(RepoPaths.GameDocs, "..", "Strings");

    /// <summary>New code_tooltips keys for R words (not in the game's file).</summary>
    public static readonly string[] RWordKeys =
    {
        "TRUE", "FALSE", "NULL", "function", "repeat", "next", "in", "c", "length", "seq_len", "seq_along", "names",
        "source", "cat", "paste", "paste0", "is.null", "as.character", "switch", "seq", "floor", "nchar",
    };

    static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    static IEnumerable<string> Pages() =>
        Directory.Exists(DocsDir) ? Directory.GetFiles(DocsDir, "*.md", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal) : Enumerable.Empty<string>();

    static string Rel(string full) => Path.GetRelativePath(DocsDir, full).Replace('\\', '/');

    static Dictionary<string, string> Exceptions()
    {
        var res = new Dictionary<string, string>();
        var f = Path.Combine(RepoPaths.Corpus("r"), "content-exceptions.txt");
        if (!File.Exists(f)) return res;
        foreach (var raw in File.ReadAllLines(f))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("--")) continue;
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            Assert.True(colon > 0, "content-exceptions.txt: every entry needs a reason: " + line);
            res[line.Substring(0, colon).Trim()] = line.Substring(colon + 2);
        }
        return res;
    }

    /// <summary>A lone word / operator list in prose, not code.</summary>
    static bool IsProse(string t)
    {
        t = t.Trim();
        if (t.Length == 0) return true;
        if (Regex.IsMatch(t, @"^[\d \t\n]+$")) return true; // a grid of numbers (cactus sizes), not code
        if (t.Contains('\n')) return false;
        if (Regex.IsMatch(t, @"^[A-Za-z_][\w.]*$")) return true;
        if (Regex.IsMatch(t, @"^[\s,+\-*/%<>=!.:#()\[\]{}&|^?]+$")) return true;
        return false;
    }

    sealed class Block { public string Id, Code, Module; public int Index; }

    static List<Block> Blocks(string rel, string md)
    {
        var res = new List<Block>();
        foreach (var s in DocsTranslator.Spans(md))
        {
            if (!s.IsBlock || IsProse(s.Text)) continue;
            // the line before the block: "file `utils.R`:" makes the block the code window utils
            int lineStart = md.LastIndexOf('\n', Math.Max(0, s.Start - 2));
            int prevStart = lineStart <= 0 ? 0 : md.LastIndexOf('\n', lineStart - 1) + 1;
            var prev = lineStart <= 0 ? "" : md.Substring(prevStart, lineStart - prevStart);
            var m = Regex.Match(prev, @"`([A-Za-z_]\w*)\.R`");
            res.Add(new Block { Id = rel + "#" + s.Index, Index = s.Index, Code = s.Text.Trim('\n'), Module = m.Success ? m.Groups[1].Value : null });
        }
        return res;
    }

    /// <summary>null when the code compiles and the game parser accepts the Python, otherwise why not.</summary>
    public static string TryCompile(string code, string module = "Docs", Dictionary<string, string> others = null)
    {
        var r = Rl.Compile(code, new CompileOptions { FileName = module, OtherFiles = others });
        if (!r.Success) return string.Join("; ", r.Errors.Select(e => e.Message + " [" + HarnessTests.Slice(code, e.Start, e.End) + "]"));
        var oracle = GameOracle.TryParse(r.Python);
        return oracle == null ? null : "game parser: " + oracle;
    }

    [SkippableFact]
    public void EveryGameDocsPage_HasAnRVersion()
    {
        Skip.IfNot(Directory.Exists(RepoPaths.GameDocs), "game docs not found");
        var en = Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/')).ToHashSet();
        var missing = en.Where(rel => !File.Exists(Path.Combine(DocsDir, rel))).OrderBy(x => x).ToList();
        var extra = Pages().Select(Rel).Where(rel => !en.Contains(rel)).ToList();
        Assert.True(missing.Count == 0, "missing pages: " + string.Join(", ", missing));
        Assert.True(extra.Count == 0, "pages the game does not have: " + string.Join(", ", extra));
        Assert.Equal(48, en.Count);
    }

    [SkippableFact]
    public void EveryCodeBlock_Compiles_OrIsAListedException()
    {
        Skip.IfNot(Directory.Exists(DocsDir), "content/r/docs not written yet");
        var exceptions = Exceptions();
        var seen = new HashSet<string>();
        var failures = new List<string>();
        var stale = new List<string>();
        int ok = 0, withContext = 0;
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var blocks = Blocks(rel, Read(page));
            var files = blocks.Where(b => b.Module != null).GroupBy(b => b.Module).ToDictionary(g => g.Key, g => g.Last().Code);
            var context = new List<string>();
            foreach (var b in blocks)
            {
                var module = b.Module ?? "Docs";
                var others = files.Where(kv => kv.Key != module).ToDictionary(kv => kv.Key, kv => kv.Value);
                string why = TryCompile(b.Code, module, others);
                if (why != null && b.Module == null && context.Count > 0 && TryCompile(string.Join("\n", context) + "\n" + b.Code, module, others) == null)
                {
                    why = null;
                    withContext++;
                }
                seen.Add(b.Id);
                if (exceptions.ContainsKey(b.Id)) { if (why == null) stale.Add(b.Id); continue; }
                if (why == null) ok++;
                else failures.Add(b.Id + ": " + why + "\n    " + b.Code.Replace("\n", "\n    "));
                if (why == null && b.Module == null) context.Add(b.Code);
            }
        }
        stale.AddRange(exceptions.Keys.Where(k => !seen.Contains(k)));
        output.WriteLine($"{ok} blocks compile ({withContext} with the earlier blocks of their page), {exceptions.Count} listed exceptions, {failures.Count} failures");
        Assert.True(failures.Count == 0, failures.Count + " R docs blocks do not compile (fix them or list them in tests/corpus/r/content-exceptions.txt):\n" + string.Join("\n", failures));
        Assert.True(stale.Count == 0, "stale entries in tests/corpus/r/content-exceptions.txt (they compile now, or the block is gone): " + string.Join(", ", stale));
    }

    static readonly Regex PythonLeft = new(@"\b(True|False|None|elif|def|range|len|pass|lambda)\b|\.(append|pop|insert|remove|add)\(|^\s*(import|from)\s|:\s*$", RegexOptions.Multiline);

    /// <summary>Markup the game would read inside a code span. A line of code must not start with # (a heading) where it
    /// starts a markdown line: every line after the first, and the first when the span opens a line.</summary>
    static IEnumerable<string> Forbidden(string where, string code, bool opensLine = true)
    {
        if (code.Contains("{{")) yield return where + ": contains {{ (the game reads it as a placeholder)";
        if (code.Contains("}}")) yield return where + ": contains }} (the game reads it as a placeholder)";
        if (code.Contains("](")) yield return where + ": contains ]( (the game reads it as a link)";
        if (code.Contains("![")) yield return where + ": contains ![ (the game reads it as an image)";
        if (Regex.IsMatch(code, @"\n#") || (opensLine && code.StartsWith("#"))) yield return where + ": a code line starts with # (the game reads it as a heading)";
    }

    [SkippableFact]
    public void CodeSpans_HaveNoMarkup_AndNoPython()
    {
        Skip.IfNot(Directory.Exists(DocsDir), "content/r/docs not written yet");
        var bad = new List<string>();
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var md = Read(page);
            foreach (var s in DocsTranslator.Spans(md))
            {
                bad.AddRange(Forbidden(rel + "#" + s.Index, s.Text, s.Start < 2 || md[s.Start - 2] == '\n'));
                if (s.IsBlock && !IsProse(s.Text))
                {
                    var noStrings = Regex.Replace(Regex.Replace(s.Text, "\"[^\"\n]*\"", "\"\""), "#[^\n]*", "");
                    var m = PythonLeft.Match(noStrings);
                    if (m.Success) bad.Add(rel + "#" + s.Index + ": looks like Python (" + m.Value.Trim() + "): " + s.Text.Split('\n')[0]);
                }
            }
            if (md.Count(c => c == '`') % 2 != 0) bad.Add(rel + ": odd number of backticks");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>"@key = value" entries (a value continues until the next line that starts with @).</summary>
    public static List<(string Key, string Value)> Entries(string text)
    {
        var res = new List<(string, string)>();
        foreach (var part in Regex.Split(text.Replace("\r\n", "\n"), @"(?m)^(?=@)"))
        {
            if (!part.StartsWith("@")) continue;
            var m = Regex.Match(part, @"^@(\S+) = ?");
            if (!m.Success) { res.Add((part.Split('\n')[0], null)); continue; }
            res.Add((m.Groups[1].Value, part.Substring(m.Length).TrimEnd('\n')));
        }
        return res;
    }

    static readonly string[] FormattedFiles = { "parse_errors.txt", "execute_errors.txt", "warnings.txt", "other.txt" };

    static List<string> Placeholders(string v) => Regex.Matches(v, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();

    [SkippableFact]
    public void StringOverrides_MatchTheGameFiles()
    {
        Skip.IfNot(Directory.Exists(StringsDir), "content/r/Strings not written yet");
        Skip.IfNot(Directory.Exists(EnStrings), "game strings not found");
        var bad = new List<string>();
        var javaDir = Path.Combine(RepoPaths.Root, "content", "java", "Strings");
        var rFiles = Directory.GetFiles(StringsDir, "*.txt").Select(Path.GetFileName).OrderBy(x => x).ToList();
        var javaFiles = Directory.GetFiles(javaDir, "*.txt").Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(javaFiles, rFiles);
        foreach (var name in rFiles)
        {
            var en = Entries(Read(Path.Combine(EnStrings, name))).Where(e => e.Value != null).ToDictionary(e => e.Key, e => e.Value);
            var mine = Entries(Read(Path.Combine(StringsDir, name)));
            var keys = new HashSet<string>();
            foreach (var (key, value) in mine)
            {
                if (value == null) { bad.Add(name + ": a line starts with @ but is not \"@key = value\": " + key); continue; }
                if (!keys.Add(key)) bad.Add(name + ": duplicate key " + key);
                bool word = name == "code_tooltips.txt" && key.StartsWith("code_tooltip_") && (RWordKeys.Contains(key.Substring(13)) || key is "code_tooltip_global" or "code_tooltip_import");
                if (!en.TryGetValue(key, out var enValue)) { if (!word) bad.Add(name + ": " + key + " is not a key of the game's " + name); continue; }
                if (FormattedFiles.Contains(name))
                {
                    if (!Placeholders(enValue).SequenceEqual(Placeholders(value))) bad.Add(name + ": " + key + " must keep exactly the placeholders " + string.Join(" ", Placeholders(enValue)));
                    if (Regex.Replace(value, @"\{\d+\}", "").IndexOfAny(new[] { '{', '}' }) >= 0) bad.Add(name + ": " + key + " has braces other than {n} placeholders");
                }
                foreach (Match m in Regex.Matches(value, "`([^`]*)`")) bad.AddRange(Forbidden(name + " " + key, m.Groups[1].Value, m.Index == 0 || value[m.Index - 1] == '\n'));
            }
            foreach (var jk in Entries(Read(Path.Combine(javaDir, name))).Select(e => e.Key))
                if (!keys.Contains(jk) && !(name == "code_tooltips.txt" && !en.ContainsKey(jk))) bad.Add(name + ": the Java content overrides " + jk + ", R does not");
            if (name == "code_tooltips.txt")
            {
                foreach (var k in en.Keys) if (!keys.Contains(k)) bad.Add(name + ": no R tooltip for " + k);
                foreach (var w in RWordKeys) if (!keys.Contains("code_tooltip_" + w)) bad.Add(name + ": no tooltip for the R word " + w);
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>Placeholder names tooltip examples may lean on (declared only when the example uses them).</summary>
    static readonly (string Name, string Decl)[] TooltipPrelude =
    {
        ("do_something", "do_something <- function() {\n\tdo_a_flip()\n}"),
        ("do_something_else", "do_something_else <- function() {\n\tdo_a_flip()\n}"),
        ("filename", "filename <- \"f1\""), ("speedup", "speedup <- 64"), ("seed", "seed <- 0"),
        ("sim_unlocks", "sim_unlocks <- list(Unlocks$Carrots)"), ("sim_items", "sim_items <- list()"), ("sim_globals", "sim_globals <- list()"),
        ("plants", "plants <- c(Entities$Bush, Entities$Tree)"), ("harvest_column", "harvest_column <- function() {\n\tharvest()\n}"),
    };

    static string WithPrelude(string code)
    {
        var sb = new StringBuilder();
        foreach (var (name, decl) in TooltipPrelude)
            if (Regex.IsMatch(code, @"(?<![\w.$""])" + name + @"(?![\w.""])") && !Regex.IsMatch(code, @"(^|\n)\s*" + name + @"\s*<-"))
                sb.Append(decl).Append('\n');
        return sb + code;
    }

    [SkippableFact]
    public void TooltipExamples_Compile()
    {
        var path = Path.Combine(StringsDir, "code_tooltips.txt");
        Skip.IfNot(File.Exists(path), "content/r/Strings not written yet");
        var bad = new List<string>();
        int ok = 0;
        foreach (var (key, value) in Entries(Read(path)))
        {
            if (value == null) continue;
            int at = value.IndexOf("example:", StringComparison.Ordinal);
            if (at < 0) continue;
            var ms = Regex.Matches(value.Substring(at), "`([^`]+)`");
            if (ms.Count == 0) bad.Add(key + ": example: without a code block");
            foreach (Match m in ms)
            {
                var code = m.Groups[1].Value.Trim('\n');
                var why = TryCompile(WithPrelude(code), "main");
                if (why == null) ok++;
                else bad.Add(key + ": " + why + "\n    " + code.Replace("\n", "\n    "));
            }
        }
        // the unlock boxes of the research menu show full statements too
        foreach (var file in new[] { "unlock_tooltips.txt", "item_tooltips.txt", "warnings.txt" })
            foreach (var (key, value) in Entries(Read(Path.Combine(StringsDir, file))))
                foreach (Match m in Regex.Matches(value ?? "", "`([^`]+)`"))
                {
                    // only the code shown on lines of its own (a name like min() inside a sentence is prose)
                    var code = m.Groups[1].Value.Trim('\n');
                    bool ownLine = (m.Index == 0 || value[m.Index - 1] == '\n') && (m.Index + m.Length == value.Length || value[m.Index + m.Length] == '\n');
                    if (!ownLine || (!code.Contains('(') && !code.Contains("<-"))) continue;
                    var why = TryCompile(WithPrelude(code), "main");
                    if (why == null) ok++;
                    else bad.Add(file + " " + key + ": " + why + "\n    " + code.Replace("\n", "\n    "));
                }
        output.WriteLine(ok + " tooltip examples compile");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    static List<(string Name, string Code)> UnlockBlocks(string path)
    {
        var res = new List<(string, string)>();
        foreach (var part in Regex.Split(Read(path), @"(?m)^(?=@)"))
        {
            if (!part.StartsWith("@")) continue;
            int nl = part.IndexOf('\n');
            res.Add(((nl < 0 ? part : part.Substring(0, nl)).Substring(1).Trim(), nl < 0 ? "" : part.Substring(nl + 1).Trim('\n')));
        }
        return res;
    }

    [SkippableFact]
    public void UnlockDisplayCode_HasTheJavaUnlocks()
    {
        var path = Path.Combine(ContentRoot, "unlock_display_code.txt");
        Skip.IfNot(File.Exists(path), "content/r/unlock_display_code.txt not written yet");
        var mine = UnlockBlocks(path);
        var java = UnlockBlocks(Path.Combine(RepoPaths.Root, "content", "java", "unlock_display_code.txt"));
        Assert.Equal(java.Select(j => j.Name).OrderBy(x => x), mine.Select(m => m.Name).OrderBy(x => x));
        var bad = new List<string>();
        foreach (var (name, code) in mine)
        {
            if (code.Trim().Length == 0) bad.Add(name + ": empty snippet");
            bad.AddRange(Forbidden(name, code));
            if (code.Split('\n').Any(l => l.StartsWith("@"))) bad.Add(name + ": a snippet line starts with @");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
