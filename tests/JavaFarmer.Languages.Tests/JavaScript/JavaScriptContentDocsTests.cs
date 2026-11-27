using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>
/// content/js: the 48 docs pages (same paths as the game's EN docs), every JavaScript code block in them, the string
/// overrides (content/js/Strings) and the unlock boxes (content/js/unlock_display_code.txt).
/// A docs block must compile on its own with the JavaScript frontend and the game parser must accept the emitted
/// Python. A block whose first line is "// Name.js" is the other code window "Name" of its page: it is compiled as
/// that module, and every such block of the page is passed as OtherFiles to the other blocks.
/// Deliberately partial blocks are listed in tests/corpus/js/content-exceptions.txt ("path#span: reason",
/// span numbering = DocsTranslator.Spans); an entry whose block compiles (or no longer exists) is stale and fails.
/// </summary>
public class JavaScriptContentDocsTests
{
    readonly ITestOutputHelper output;
    public JavaScriptContentDocsTests(ITestOutputHelper output) { this.output = output; }

    static ILanguage Js => Compiler.Languages.Get("js");
    static string ContentRoot => Path.Combine(RepoPaths.Root, "content", "js");
    static string DocsDir => Path.Combine(ContentRoot, "docs");
    static string StringsDir => Path.Combine(ContentRoot, "Strings");
    static string GameStrings => Path.GetFullPath(Path.Combine(RepoPaths.GameDocs, "..", "Strings"));
    static string ExceptionsFile => Path.Combine(RepoPaths.Corpus("js"), "content-exceptions.txt");

    /// <summary>JavaScript words that get their own code_tooltip_ key (not in the game's file).</summary>
    static readonly string[] JsWords =
    {
        "true", "false", "null", "undefined", "function", "let", "const", "var", "new", "do", "switch", "case",
        "default", "of", "export", "import", "Math", "console",
    };

    /// <summary>Formatted strings (string.Format): only {n} placeholders, the same ones as the game's text.</summary>
    static readonly string[] FormattedFiles = { "parse_errors.txt", "execute_errors.txt", "warnings.txt", "other.txt" };

    static readonly Regex ModuleHeader = new(@"^//\s*([A-Za-z_]\w*)\.js\b");
    static readonly string Bt = ((char)96).ToString();
    static readonly Regex CodeSpan = new(Bt + "([^" + Bt + "]+)" + Bt);

    static IEnumerable<string> Pages() =>
        Directory.Exists(DocsDir) ? Directory.GetFiles(DocsDir, "*.md", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal) : Enumerable.Empty<string>();

    static string Rel(string full) => Path.GetRelativePath(DocsDir, full).Replace('\\', '/');

    static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    /// <summary>A lone word or an operator list in prose, not code.</summary>
    static bool IsProse(string t)
    {
        t = t.Trim();
        if (t.Length == 0) return true;
        if (t.Contains('\n')) return false;
        if (Regex.IsMatch(t, @"^[A-Za-z_]\w*$")) return true;
        if (Regex.IsMatch(t, @"^[\s,+\-*/%<>=!.:()\[\]{}&|^?]+$")) return true;
        return false;
    }

    static Dictionary<string, string> Exceptions()
    {
        var res = new Dictionary<string, string>();
        if (!File.Exists(ExceptionsFile)) return res;
        foreach (var raw in File.ReadAllLines(ExceptionsFile))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("--")) continue;
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            Assert.True(colon > 0 && line.Substring(colon + 2).Trim().Length > 0, "content-exceptions.txt: an entry needs a reason: " + line);
            res[line.Substring(0, colon).Trim()] = line.Substring(colon + 2);
        }
        return res;
    }

    /// <summary>null when the code compiles and the game parser accepts the emitted Python, else the reason.</summary>
    internal static string TryCompile(string code, string module, Dictionary<string, string> others)
    {
        var r = Js.Compile(code, new CompileOptions { FileName = module, OtherFiles = others });
        if (!r.Success)
            return string.Join("; ", r.Errors.Select(e => e.Message + " [" + (e.Start >= 0 && e.End <= code.Length && e.End >= e.Start ? code.Substring(e.Start, e.End - e.Start) : "?") + "]"));
        var oracle = GameOracle.TryParse(r.Python);
        return oracle == null ? null : "game parser: " + oracle + "\n    python:\n    " + r.Python.Replace("\n", "\n    ");
    }

    /// <summary>"{{", "}}", "](", "![" and lines starting with "#" are markup for the game, even inside code.</summary>
    internal static string ForbiddenMarkup(string code)
    {
        foreach (var bad in new[] { "{{", "}}", "](", "![" })
            if (code.Contains(bad)) return "contains " + bad;
        if (Regex.IsMatch(code, @"(^|\n)#")) return "a line starts with #";
        return null;
    }

    [SkippableFact]
    public void EveryGameDocsPage_HasAJavaScriptVersion()
    {
        Skip.IfNot(Directory.Exists(RepoPaths.GameDocs), "game docs not found");
        var en = Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/')).ToList();
        var missing = en.Where(rel => !File.Exists(Path.Combine(DocsDir, rel))).ToList();
        Assert.True(missing.Count == 0, "missing content/js/docs pages: " + string.Join(", ", missing));
        var extra = Pages().Select(Rel).Where(rel => !en.Contains(rel)).ToList();
        Assert.True(extra.Count == 0, "content/js/docs pages without a game page: " + string.Join(", ", extra));
        Assert.Equal(48, en.Count);
    }

    [Fact]
    public void EveryCodeBlock_Compiles_OrIsAListedException()
    {
        Assert.True(Directory.Exists(DocsDir), "content/js/docs is missing");
        var exceptions = Exceptions();
        var seen = new HashSet<string>();
        var failures = new List<string>();
        var stale = new List<string>();
        int ok = 0;
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var blocks = DocsTranslator.Spans(Read(page)).Where(s => s.IsBlock && !IsProse(s.Text)).ToList();
            var modules = new Dictionary<string, string>();
            foreach (var s in blocks)
            {
                var m = ModuleHeader.Match(s.Text.Trim('\n'));
                if (m.Success) modules[m.Groups[1].Value] = s.Text;
            }
            foreach (var s in blocks)
            {
                var id = rel + "#" + s.Index;
                var code = s.Text.Trim('\n');
                var m = ModuleHeader.Match(code);
                var module = m.Success ? m.Groups[1].Value : "main";
                var others = modules.Count == 0 ? null : modules.Where(kv => kv.Key != module).ToDictionary(kv => kv.Key, kv => kv.Value);
                var why = TryCompile(code, module, others);
                if (exceptions.ContainsKey(id))
                {
                    seen.Add(id);
                    if (why == null) stale.Add(id + " (now compiles)");
                }
                else if (why == null) ok++;
                else failures.Add(id + ": " + why + "\n    " + code.Replace("\n", "\n    "));
            }
        }
        stale.AddRange(exceptions.Keys.Where(k => !seen.Contains(k)).Select(k => k + " (no such block)"));
        output.WriteLine($"{ok} blocks compile, {seen.Count} listed exceptions");
        Assert.True(failures.Count == 0, failures.Count + " JavaScript docs blocks do not compile (fix them or list them in tests/corpus/js/content-exceptions.txt):\n" + string.Join("\n", failures));
        Assert.True(stale.Count == 0, "stale entries in tests/corpus/js/content-exceptions.txt:\n" + string.Join("\n", stale));
        Assert.True(ok > 100, "suspiciously few blocks: " + ok);
    }

    [Fact]
    public void CodeSpans_ContainNoGameMarkup_AndNoPython()
    {
        var bad = new List<string>();
        var pythonWords = new Regex(@"\b(True|False|None|elif)\b|\bdef |(^|\n)\s*(from \w+ )?import \w|\bprint\(|\b(can_harvest|get_entity_type|get_ground_type|get_pos_x|get_pos_y|get_world_size|spawn_drone|num_items|use_item|get_companion|do_a_flip|quick_print|get_cost|wait_for)\b");
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            foreach (var s in DocsTranslator.Spans(Read(page)))
            {
                var why = ForbiddenMarkup(s.Text);
                if (why != null) bad.Add(rel + "#" + s.Index + ": " + why);
                if (rel != "patchnotes.md" && pythonWords.IsMatch(s.Text)) bad.Add(rel + "#" + s.Index + ": looks like Python: " + s.Text.Split('\n')[0]);
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>"@key = value" entries (a value continues until the next line starting with "@").</summary>
    internal static List<(string Key, string Value)> Entries(string text)
    {
        var res = new List<(string, string)>();
        foreach (var chunk in Regex.Split(text, @"(?m)^(?=@)"))
        {
            if (!chunk.StartsWith("@")) continue;
            int eq = chunk.IndexOf('=');
            int nl = chunk.IndexOf('\n');
            if (eq < 0 || (nl >= 0 && eq > nl)) { res.Add((chunk.Split('\n')[0].Substring(1).Trim(), null)); continue; }
            res.Add((chunk.Substring(1, eq - 1).Trim(), chunk.Substring(eq + 1).Trim()));
        }
        return res;
    }

    static readonly Regex Placeholder = new(@"\{(\d+)\}");

    [SkippableFact]
    public void StringOverrides_MatchTheGameKeys()
    {
        Skip.IfNot(Directory.Exists(GameStrings), "game strings not found");
        Assert.True(Directory.Exists(StringsDir), "content/js/Strings is missing");
        var bad = new List<string>();
        var javaDir = Path.Combine(RepoPaths.Root, "content", "java", "Strings");
        foreach (var f in Directory.GetFiles(StringsDir, "*.txt").OrderBy(x => x, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(f);
            var enPath = Path.Combine(GameStrings, name);
            if (!File.Exists(enPath)) { bad.Add(name + ": the game has no Strings file of this name"); continue; }
            var en = Entries(Read(enPath)).Where(e => e.Value != null).GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First().Value);
            var text = Read(f);
            foreach (var line in text.Split('\n'))
                if (line.StartsWith("@") && !Regex.IsMatch(line, @"^@\w+ = ")) bad.Add(name + ": a value line starts with @ (or a key line is malformed): " + line);
            var entries = Entries(text);
            foreach (var dup in entries.GroupBy(e => e.Key).Where(g => g.Count() > 1)) bad.Add(name + ": duplicate key " + dup.Key);
            foreach (var (key, value) in entries)
            {
                if (value == null) continue;
                bool jsWord = name == "code_tooltips.txt" && JsWords.Any(w => key == "code_tooltip_" + w);
                if (!en.ContainsKey(key) && !jsWord) { bad.Add(name + ": " + key + " is not a key of the game's " + name); continue; }
                if (value.Contains("{{") || value.Contains("}}")) bad.Add(name + ": " + key + " contains {{ or }}");
                if (FormattedFiles.Contains(name))
                {
                    var mine = Placeholder.Matches(value).Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
                    var theirs = Placeholder.Matches(en[key]).Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
                    if (!mine.SequenceEqual(theirs)) bad.Add(name + ": " + key + " has placeholders " + string.Join(",", mine) + " but the game's text has " + string.Join(",", theirs));
                    if (Placeholder.Replace(value, "").IndexOfAny(new[] { '{', '}' }) >= 0) bad.Add(name + ": " + key + " is a formatted string: no braces except {n} placeholders");
                }
            }
            // every game key that the Java content overrides is rewritten for JavaScript too
            var javaFile = Path.Combine(javaDir, name);
            if (File.Exists(javaFile))
                foreach (var (key, _) in Entries(Read(javaFile)).Where(e => en.ContainsKey(e.Key)))
                    if (!entries.Any(e => e.Key == key)) bad.Add(name + ": " + key + " is overridden for Java but not for JavaScript");
        }
        if (Directory.Exists(javaDir))
            foreach (var jf in Directory.GetFiles(javaDir, "*.txt"))
                if (!File.Exists(Path.Combine(StringsDir, Path.GetFileName(jf)))) bad.Add("missing content/js/Strings/" + Path.GetFileName(jf));
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void CodeTooltips_CoverEveryBuiltin_AndTheirExamplesCompile()
    {
        var entries = Entries(Read(Path.Combine(StringsDir, "code_tooltips.txt"))).Where(e => e.Value != null).ToList();
        var bad = new List<string>();
        foreach (var w in JsWords)
            if (!entries.Any(e => e.Key == "code_tooltip_" + w)) bad.Add("no tooltip for the JavaScript word " + w);
        var gamePath = Path.Combine(GameStrings, "code_tooltips.txt");
        var gameKeys = File.Exists(gamePath) ? Entries(Read(gamePath)).Select(e => e.Key).Distinct().ToList() : new List<string>();
        foreach (var k in gameKeys)
            if (!entries.Any(e => e.Key == k)) bad.Add("no JavaScript tooltip for the game key " + k);
        int ok = 0;
        foreach (var (key, value) in entries)
        {
            var markupAll = CodeSpan.Matches(value).Select(m => ForbiddenMarkup(m.Groups[1].Value)).FirstOrDefault(x => x != null);
            if (markupAll != null) bad.Add(key + ": " + markupAll);
            var i = value.IndexOf("example:", StringComparison.Ordinal);
            if (i < 0) continue;
            foreach (Match m in CodeSpan.Matches(value.Substring(i)))
            {
                var code = m.Groups[1].Value.Trim('\n');
                var why = TryCompile(code, "main", null);
                if (why == null) ok++;
                else bad.Add(key + ": " + why + "\n    " + code.Replace("\n", "\n    "));
            }
        }
        output.WriteLine(ok + " tooltip examples compile");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
        Assert.True(ok >= gameKeys.Count / 2, "too few tooltip examples: " + ok);
    }

    [Fact]
    public void UnlockDisplayCode_HasTheJavaUnlocks()
    {
        static List<string> Names(string path) => File.ReadAllLines(path).Where(l => l.StartsWith("@")).Select(l => l.Substring(1).Trim()).ToList();
        var js = Names(Path.Combine(ContentRoot, "unlock_display_code.txt"));
        var java = Names(Path.Combine(RepoPaths.Root, "content", "java", "unlock_display_code.txt"));
        Assert.Equal(java.OrderBy(x => x, StringComparer.Ordinal), js.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(js.Count, js.Distinct().Count());
        foreach (var line in File.ReadAllLines(Path.Combine(ContentRoot, "unlock_display_code.txt")))
            Assert.True(ForbiddenMarkup(line) == null, "unlock_display_code.txt: " + line);
    }
}
