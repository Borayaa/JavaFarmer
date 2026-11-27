using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.Rust;

/// <summary>
/// content/rust: the 48 docs pages (same paths as the game's EN docs), every Rust code block in them, the string
/// overrides (content/rust/Strings) and the unlock boxes (content/rust/unlock_display_code.txt).
///
/// How a docs block is compiled (Languages.Get("rust").Compile, then the game parser must accept the Python):
/// * A block whose first line is a comment naming a file ("// utils.rs") is that code window: it is compiled as it
///   is, as the module "utils", and every such block of the page is passed as CompileOptions.OtherFiles to the other
///   blocks of the page ("// main.rs" is the entry window).
/// * A block that contains "fn main(" is a full program: compiled exactly as written.
/// * Any other block is a snippet (see Wrap): its top-level items (fn, pub fn, const, static, use, mod) stay at the
///   top, its other lines go into "fn main() { .. }", and "use farm::*;" plus
///   "use std::collections::{HashMap, HashSet};" are put in front unless the block has its own use line for them.
///   A snippet that does not compile on its own is tried again after the items of the earlier blocks of its page,
///   then also after their statements (a lesson builds on its earlier snippets).
/// Deliberately partial blocks are listed in Exceptions ("path#span" → reason, span numbering = DocsTranslator.Spans);
/// an entry whose block compiles (or no longer exists) is stale and fails.
/// </summary>
public class RustContentTests
{
    readonly ITestOutputHelper output;
    public RustContentTests(ITestOutputHelper output) { this.output = output; }

    static ILanguage Rust => Compiler.Languages.Get("rust");
    static string ContentRoot => Path.Combine(RepoPaths.Root, "content", "rust");
    static string DocsDir => Path.Combine(ContentRoot, "docs");
    static string StringsDir => Path.Combine(ContentRoot, "Strings");
    static string GameStrings => Path.GetFullPath(Path.Combine(RepoPaths.GameDocs, "..", "Strings"));

    /// <summary>Docs blocks that are deliberately not complete Rust (path#span → reason).</summary>
    static readonly Dictionary<string, string> Exceptions = new()
    {
        ["scripting/for.md#4"] = "syntax template: variable_name and sequence are placeholders",
        ["scripting/if.md#3"] = "syntax template: condition1 / condition2 are placeholders",
        ["scripting/if.md#11"] = "syntax template: condition is a placeholder",
        ["scripting/if.md#18"] = "syntax template: condition is a placeholder",
        ["scripting/if.md#21"] = "syntax template: condition1 / condition2 are placeholders",
        ["scripting/if.md#23"] = "syntax template: condition1 / condition2 are placeholders",
        ["scripting/scopes.md#3"] = "shows the error on purpose: y is used outside the block that declares it (E0425)",
        ["scripting/while.md#5"] = "syntax template: condition is a placeholder",
        ["scripting/while.md#12"] = "syntax template: condition is a placeholder",
    };

    /// <summary>unlock_display_code.txt entries that are a word or a fragment, not a complete snippet (unlock → reason).</summary>
    static readonly Dictionary<string, string> UnlockFragments = new()
    {
        ["import"] = "utils::f() calls a function of another code window; the box shows no utils window",
        ["simulation"] = "simulate() shows only the name: the real call takes 6 arguments, too long for the unlock box",
        ["the_farmers_remains"] = "? is the game's secret placeholder, not code",
    };

    /// <summary>Rust words that get their own code_tooltip_ key (not in the game's file).</summary>
    static readonly string[] RustWords =
    {
        "true", "false", "fn", "let", "mut", "const", "static", "loop", "match", "mod", "use", "pub", "unsafe", "as",
        "in", "move", "Some", "Option", "Vec", "HashMap", "HashSet", "String", "i32", "f64", "bool", "usize",
    };

    /// <summary>Formatted strings (string.Format): only {n} placeholders, the same ones as the game's text.</summary>
    static readonly string[] FormattedFiles = { "parse_errors.txt", "execute_errors.txt", "warnings.txt", "other.txt" };

    static readonly Regex ModuleHeader = new(@"^//\s*([A-Za-z_]\w*)\.rs\b");
    static readonly string Bt = ((char)96).ToString();
    static readonly Regex CodeSpan = new(Bt + "([^" + Bt + "]+)" + Bt);

    static IEnumerable<string> Pages() =>
        Directory.Exists(DocsDir) ? Directory.GetFiles(DocsDir, "*.md", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal) : Enumerable.Empty<string>();

    static string Rel(string full) => Path.GetRelativePath(DocsDir, full).Replace('\\', '/');

    static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    /// <summary>A lone word, an operator list or a grid of numbers in prose, not code.</summary>
    static bool IsProse(string t)
    {
        t = t.Trim();
        if (t.Length == 0) return true;
        if (Regex.IsMatch(t, @"^[\d \t\n]+$")) return true; // a grid of numbers (cactus sizes), not code
        if (t.Contains('\n')) return false;
        if (Regex.IsMatch(t, @"^[A-Za-z_][\w:.!]*$")) return true;
        if (Regex.IsMatch(t, @"^[\s,+\-*/%<>=!.:()\[\]{}&|^?;]+$")) return true;
        return false;
    }

    static readonly Regex ItemStart = new(@"^(pub\s+)?(fn|const|static|use|mod)\s");

    /// <summary>A snippet as a program: items at the top, the other lines inside fn main, the prelude in front.</summary>
    internal static string Wrap(string code, IEnumerable<string> earlierItems = null, IEnumerable<string> earlierStatements = null)
    {
        var items = new List<string>();
        var stmts = new List<string>();
        var lines = code.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!ItemStart.IsMatch(lines[i]))
            {
                stmts.Add(lines[i]);
                continue;
            }
            // an item runs until its braces close (fn) or its line ends with ';' (const, static, use, mod)
            var sb = new StringBuilder();
            int depth = 0;
            for (; i < lines.Length; i++)
            {
                sb.Append(lines[i]).Append('\n');
                foreach (var ch in lines[i]) { if (ch == '{') depth++; else if (ch == '}') depth--; }
                var t = lines[i].TrimEnd();
                if (depth <= 0 && (t.EndsWith("}") || t.EndsWith(";"))) break;
            }
            items.Add(sb.ToString().TrimEnd('\n'));
        }
        var all = string.Join("\n", items);
        var res = new StringBuilder();
        if (!Regex.IsMatch(all, @"(?m)^use farm::")) res.Append("use farm::*;\n");
        if (!Regex.IsMatch(all, @"(?m)^use std::collections")) res.Append("use std::collections::{HashMap, HashSet};\n");
        if (earlierItems != null)
            foreach (var e in earlierItems) res.Append(e).Append('\n');
        foreach (var it in items) res.Append(it).Append('\n');
        bool hasMain = items.Any(it => Regex.IsMatch(it, @"^(pub\s+)?fn\s+main\s*\("));
        if (!hasMain)
        {
            res.Append("fn main() {\n");
            if (earlierStatements != null)
                foreach (var s in earlierStatements) res.Append(s).Append('\n');
            // a snippet that ends in an expression ("num_items(Item::Hay) > 0") is that expression as a statement
            int last = stmts.FindLastIndex(s => s.Trim().Length > 0 && !s.TrimStart().StartsWith("//"));
            for (int i = 0; i < stmts.Count; i++)
            {
                var s = stmts[i];
                var t = s.TrimEnd();
                if (i == last && !(t.EndsWith(";") || t.EndsWith("{") || t.EndsWith("}") || t.EndsWith(","))) s = "let _ = " + t.TrimStart() + ";";
                res.Append(s).Append('\n');
            }
            res.Append("}\n");
        }
        return res.ToString();
    }

    /// <summary>The top-level items and the statements of a snippet (for the later blocks of a page).</summary>
    static (List<string> Items, List<string> Statements) Split(string code)
    {
        var items = new List<string>();
        var stmts = new List<string>();
        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!ItemStart.IsMatch(lines[i]) || lines[i].StartsWith("use ") || lines[i].StartsWith("mod ")) { if (!lines[i].StartsWith("use ") && !lines[i].StartsWith("mod ")) stmts.Add(lines[i]); continue; }
            var sb = new StringBuilder();
            int depth = 0;
            for (; i < lines.Length; i++)
            {
                sb.Append(lines[i]).Append('\n');
                foreach (var ch in lines[i]) { if (ch == '{') depth++; else if (ch == '}') depth--; }
                var t = lines[i].TrimEnd();
                if (depth <= 0 && (t.EndsWith("}") || t.EndsWith(";"))) break;
            }
            items.Add(sb.ToString().TrimEnd('\n'));
        }
        return (items, stmts);
    }

    static string ItemName(string item)
    {
        var m = Regex.Match(item, @"^(pub\s+)?(fn|const|static)\s+(mut\s+)?([A-Za-z_]\w*)");
        return m.Success ? m.Groups[4].Value : null;
    }

    /// <summary>null when the code compiles and the game parser accepts the emitted Python, else the reason.</summary>
    internal static string TryCompile(string code, string module, Dictionary<string, string> others)
    {
        var r = Rust.Compile(code, new CompileOptions { FileName = module, OtherFiles = others });
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
    public void EveryGameDocsPage_HasARustVersion()
    {
        Skip.IfNot(Directory.Exists(RepoPaths.GameDocs), "game docs not found");
        var en = Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/')).ToList();
        var missing = en.Where(rel => !File.Exists(Path.Combine(DocsDir, rel))).ToList();
        Assert.True(missing.Count == 0, "missing content/rust/docs pages: " + string.Join(", ", missing));
        var extra = Pages().Select(Rel).Where(rel => !en.Contains(rel)).ToList();
        Assert.True(extra.Count == 0, "content/rust/docs pages without a game page: " + string.Join(", ", extra));
        Assert.Equal(48, en.Count);
    }

    [Fact]
    public void EveryCodeBlock_Compiles_OrIsAListedException()
    {
        Assert.True(Directory.Exists(DocsDir), "content/rust/docs is missing");
        var seen = new HashSet<string>();
        var failures = new List<string>();
        var stale = new List<string>();
        int ok = 0, full = 0, modules = 0, withContext = 0;
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var blocks = DocsTranslator.Spans(Read(page)).Where(s => s.IsBlock && !IsProse(s.Text)).ToList();
            var files = new Dictionary<string, string>();
            foreach (var s in blocks)
            {
                var m = ModuleHeader.Match(s.Text.Trim('\n'));
                if (m.Success && m.Groups[1].Value != "main") files[m.Groups[1].Value] = s.Text.Trim('\n');
            }
            var items = new List<string>();
            var stmts = new List<string>();
            foreach (var s in blocks)
            {
                var id = rel + "#" + s.Index;
                var code = s.Text.Trim('\n');
                var m = ModuleHeader.Match(code);
                var module = m.Success ? m.Groups[1].Value : "main";
                var others = files.Count == 0 ? null : files.Where(kv => kv.Key != module).ToDictionary(kv => kv.Key, kv => kv.Value);
                string why;
                bool isSnippet = !m.Success && !code.Contains("fn main(");
                if (!isSnippet)
                {
                    why = TryCompile(code, module, others);
                    if (why == null) { if (m.Success) modules++; else full++; }
                }
                else
                {
                    why = TryCompile(Wrap(code), module, others);
                    if (why != null)
                    {
                        var mine = Split(code).Items.Select(ItemName).Where(n => n != null).ToHashSet();
                        var before = items.Where(it => !mine.Contains(ItemName(it) ?? "")).ToList();
                        if (TryCompile(Wrap(code, before), module, others) == null
                            || TryCompile(Wrap(code, before, stmts), module, others) == null) { why = null; withContext++; }
                    }
                    if (why == null)
                    {
                        var (its, sts) = Split(code);
                        foreach (var it in its)
                        {
                            var n = ItemName(it);
                            items.RemoveAll(x => n != null && ItemName(x) == n);
                            items.Add(it);
                        }
                        stmts.AddRange(sts);
                    }
                }
                if (Exceptions.ContainsKey(id))
                {
                    seen.Add(id);
                    if (why == null) stale.Add(id + " (now compiles)");
                }
                else if (why == null) ok++;
                else failures.Add(id + ": " + why + "\n    " + code.Replace("\n", "\n    "));
            }
        }
        stale.AddRange(Exceptions.Keys.Where(k => !seen.Contains(k)).Select(k => k + " (no such block)"));
        output.WriteLine($"{ok} blocks compile ({full} full programs, {modules} module windows, {withContext} snippets with the earlier blocks of their page), {seen.Count} listed exceptions");
        Assert.True(failures.Count == 0, failures.Count + " Rust docs blocks do not compile (fix them or list them in RustContentTests.Exceptions):\n" + string.Join("\n", failures));
        Assert.True(stale.Count == 0, "stale entries in RustContentTests.Exceptions:\n" + string.Join("\n", stale));
        Assert.True(ok > 100, "suspiciously few blocks: " + ok);
    }

    static readonly Regex PythonLeft = new(@"\b(True|False|elif|def|pass|lambda|nonlocal)\b|(?<![.\w#:])(range|len|str|print|move|append|list|dict|set|type)\(|(^|\n)\s*(from \w+ )?import \w|(^|\n)\s*global \w|vec!");

    [Fact]
    public void CodeSpans_ContainNoGameMarkup_AndNoPython()
    {
        var bad = new List<string>();
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var md = Read(page);
            foreach (var s in DocsTranslator.Spans(md))
            {
                var why = ForbiddenMarkup(s.Text);
                if (why != null) bad.Add(rel + "#" + s.Index + ": " + why);
                if (rel != "patchnotes.md" && PythonLeft.IsMatch(s.Text)) bad.Add(rel + "#" + s.Index + ": looks like Python: " + s.Text.Split('\n')[0]);
                if (s.IsBlock && rel != "patchnotes.md" && Regex.IsMatch(s.Text, @":[ \t]*(\n|$)")) bad.Add(rel + "#" + s.Index + ": a line ends with ':' (Python): " + s.Text.Split('\n')[0]);
            }
            foreach (var line in md.Split('\n'))
                if (Regex.IsMatch(line, @"<(unlock|spoiler)[^>]*>\s*#")) bad.Add(rel + ": a heading follows an unlock/spoiler tag on the same line: " + line);
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
        Assert.True(Directory.Exists(StringsDir), "content/rust/Strings is missing");
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
                bool rustWord = name == "code_tooltips.txt" && RustWords.Any(w => key == "code_tooltip_" + w || key == "code_tooltip_keyword_" + w);
                if (!en.ContainsKey(key) && !rustWord) { bad.Add(name + ": " + key + " is not a key of the game's " + name); continue; }
                if (value.Contains("{{") || value.Contains("}}")) bad.Add(name + ": " + key + " contains {{ or }}");
                foreach (Match cm in CodeSpan.Matches(value))
                {
                    var mk = ForbiddenMarkup(cm.Groups[1].Value);
                    if (mk != null) bad.Add(name + ": " + key + ": code " + mk);
                }
                if (FormattedFiles.Contains(name))
                {
                    var mine = Placeholder.Matches(value).Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
                    var theirs = Placeholder.Matches(en[key]).Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
                    if (!mine.SequenceEqual(theirs)) bad.Add(name + ": " + key + " has placeholders " + string.Join(",", mine) + " but the game's text has " + string.Join(",", theirs));
                    if (Placeholder.Replace(value, "").IndexOfAny(new[] { '{', '}' }) >= 0) bad.Add(name + ": " + key + " is a formatted string: no braces except {n} placeholders");
                }
            }
            // every game key that the Java content overrides is rewritten for Rust too
            var javaFile = Path.Combine(javaDir, name);
            if (File.Exists(javaFile))
                foreach (var (key, _) in Entries(Read(javaFile)).Where(e => en.ContainsKey(e.Key)))
                    if (!entries.Any(e => e.Key == key)) bad.Add(name + ": " + key + " is overridden for Java but not for Rust");
        }
        if (Directory.Exists(javaDir))
            foreach (var jf in Directory.GetFiles(javaDir, "*.txt"))
                if (!File.Exists(Path.Combine(StringsDir, Path.GetFileName(jf)))) bad.Add("missing content/rust/Strings/" + Path.GetFileName(jf));
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void CodeTooltips_CoverEveryBuiltin_AndTheirExamplesCompile()
    {
        var entries = Entries(Read(Path.Combine(StringsDir, "code_tooltips.txt"))).Where(e => e.Value != null).ToList();
        var bad = new List<string>();
        foreach (var w in RustWords)
            if (!entries.Any(e => e.Key == "code_tooltip_" + w)) bad.Add("no tooltip for the Rust word " + w);
        foreach (var b in GameApi.Builtins.Where(b => b.Extension == null))
            if (!entries.Any(e => e.Key == "code_tooltip_" + b.PythonName)) bad.Add("no Rust tooltip for the builtin " + b.PythonName);
        var gamePath = Path.Combine(GameStrings, "code_tooltips.txt");
        var gameKeys = File.Exists(gamePath) ? Entries(Read(gamePath)).Select(e => e.Key).Distinct().ToList() : new List<string>();
        foreach (var k in gameKeys)
            if (!entries.Any(e => e.Key == k)) bad.Add("no Rust tooltip for the game key " + k);
        int ok = 0;
        foreach (var (key, value) in entries)
        {
            var i = value.IndexOf("example:", StringComparison.Ordinal);
            if (i < 0) continue;
            foreach (Match m in CodeSpan.Matches(value.Substring(i)))
            {
                var code = m.Groups[1].Value.Trim('\n');
                if (IsProse(code)) continue;
                var why = TryCompile(code.Contains("fn main(") ? code : Wrap(code), "main", null);
                if (why == null) ok++;
                else bad.Add(key + ": " + why + "\n    " + code.Replace("\n", "\n    "));
            }
        }
        output.WriteLine(ok + " tooltip examples compile");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
        Assert.True(ok >= gameKeys.Count / 2, "too few tooltip examples: " + ok);
    }

    static List<(string Name, string Code)> UnlockBlocks(string path)
    {
        var res = new List<(string, string)>();
        string name = null;
        var sb = new StringBuilder();
        foreach (var line in Read(path).Split('\n'))
        {
            if (line.StartsWith("@"))
            {
                if (name != null) res.Add((name, sb.ToString().Trim('\n')));
                name = line.Substring(1).Trim();
                sb.Clear();
            }
            else sb.Append(line).Append('\n');
        }
        if (name != null) res.Add((name, sb.ToString().Trim('\n')));
        return res;
    }

    [Fact]
    public void UnlockDisplayCode_HasTheJavaUnlocks_AndTheSnippetsCompile()
    {
        var rust = UnlockBlocks(Path.Combine(ContentRoot, "unlock_display_code.txt"));
        var java = UnlockBlocks(Path.Combine(RepoPaths.Root, "content", "java", "unlock_display_code.txt"));
        Assert.Equal(java.Select(b => b.Name).OrderBy(x => x, StringComparer.Ordinal), rust.Select(b => b.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(rust.Count, rust.Select(b => b.Name).Distinct().Count());
        var bad = new List<string>();
        int ok = 0;
        foreach (var (name, code) in rust)
        {
            var mk = ForbiddenMarkup(code);
            if (mk != null) bad.Add(name + ": " + mk);
            if (code.Length == 0) bad.Add(name + ": empty");
            if (PythonLeft.IsMatch(code)) bad.Add(name + ": looks like Python: " + code);
            var why = TryCompile(code.Contains("fn main(") ? code : Wrap(code), "main", null);
            if (UnlockFragments.ContainsKey(name)) { if (why == null) bad.Add(name + ": listed as a fragment but compiles"); continue; }
            if (why == null) ok++;
            else bad.Add(name + ": " + why + "\n    " + code.Replace("\n", "\n    "));
        }
        foreach (var k in UnlockFragments.Keys)
            if (!rust.Any(b => b.Name == k)) bad.Add(k + ": listed as a fragment but has no entry");
        output.WriteLine(ok + " unlock snippets compile, " + UnlockFragments.Count + " fragments");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
