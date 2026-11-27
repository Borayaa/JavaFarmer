using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.Zig;

/// <summary>
/// content/zig: the 48 docs pages (same paths as the game's EN docs), every Zig code block in them, the string
/// overrides (content/zig/Strings) and the unlock boxes (content/zig/unlock_display_code.txt).
///
/// How a docs block is compiled (Languages.Get("zig").Compile, then the game parser must accept the Python):
/// * A block whose first line is a comment naming a file ("// utils.zig") is that code window: it is compiled as it
///   is, as the module "utils", and every such block of the page is passed as CompileOptions.OtherFiles to the other
///   blocks of the page ("// main.zig" is the entry window).
/// * A block that contains "pub fn main(" is a full program: compiled exactly as written.
/// * Any other block is a snippet (see Wrap): its top-level declarations (lines starting in column 0 with fn, pub fn,
///   const, var, pub const, pub var, up to the end of their braces / ';') stay at the top level, its other lines go
///   into "pub fn main() void { .. }" ("!void" when the snippet uses try), and the two imports
///   "const std = @import("std");" / "const farm = @import("farm");" are put in front unless the block declares them.
///   A final line that is an expression ("farm.canHarvest()") becomes "_ = expr;". A snippet that does not compile on
///   its own is tried again after the declarations of the earlier blocks of its page, then also after their
///   statements (a lesson builds on its earlier snippets).
/// Deliberately partial blocks are listed in ZigContentExceptions.txt ("path#span: reason", span numbering =
/// DocsCorpus.Spans; "@unlock: reason" for unlock boxes); an entry whose block compiles (or no longer exists) is stale and fails.
/// </summary>
public class ZigContentTests
{
    readonly ITestOutputHelper output;
    public ZigContentTests(ITestOutputHelper output) { this.output = output; }

    static ILanguage Zig => Compiler.Languages.Get("zig");
    static string ContentRoot => Path.Combine(RepoPaths.Root, "content", "zig");
    static string DocsDir => Path.Combine(ContentRoot, "docs");
    static string StringsDir => Path.Combine(ContentRoot, "Strings");
    static string GameStrings => Path.GetFullPath(Path.Combine(RepoPaths.GameDocs, "..", "Strings"));
    static string ExceptionsFile => Path.Combine(RepoPaths.Root, "tests", "JavaFarmer.Languages.Tests", "Zig", "ZigContentExceptions.txt");

    /// <summary>ZigContentExceptions.txt: docs blocks ("path#span") and unlock boxes ("@name") that are deliberately not complete Zig.</summary>
    static Dictionary<string, string> Exceptions()
    {
        var res = new Dictionary<string, string>();
        if (!File.Exists(ExceptionsFile)) return res;
        foreach (var raw in File.ReadAllLines(ExceptionsFile))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("--")) continue;
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            res[colon < 0 ? line : line.Substring(0, colon).Trim()] = colon < 0 ? "" : line.Substring(colon + 2);
        }
        return res;
    }

    /// <summary>Zig words that get their own code_tooltip_ key (not in the game's file).</summary>
    internal static readonly string[] ZigWords =
    {
        "true", "false", "null", "fn", "pub", "const", "var", "void", "orelse", "try", "catch", "defer", "switch",
        "unreachable", "undefined", "std", "farm", "i32", "usize", "f64", "bool", "ArrayList", "AutoHashMap",
    };

    /// <summary>Formatted strings (string.Format): only {n} placeholders, the same ones as the game's text.</summary>
    static readonly string[] FormattedFiles = { "parse_errors.txt", "execute_errors.txt", "warnings.txt", "other.txt" };

    static readonly Regex ModuleHeader = new(@"^//\s*([A-Za-z_]\w*)\.zig\b");
    static readonly string Bt = ((char)96).ToString();
    static readonly Regex CodeSpan = new(Bt + "([^" + Bt + "]+)" + Bt);

    static IEnumerable<string> Pages() =>
        Directory.Exists(DocsDir) ? Directory.GetFiles(DocsDir, "*.md", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal) : Enumerable.Empty<string>();

    static string Rel(string full) => Path.GetRelativePath(DocsDir, full).Replace('\\', '/');

    static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    /// <summary>A lone word, an operator list or a grid of numbers in prose, not code.</summary>
    internal static bool IsProse(string t)
    {
        t = t.Trim();
        if (t.Length == 0) return true;
        if (Regex.IsMatch(t, @"^[\d \t\n]+$")) return true; // a grid of numbers (cactus sizes), not code
        if (t.Contains('\n')) return false;
        if (Regex.IsMatch(t, @"^[@A-Za-z_][\w.?!]*$")) return true;
        if (Regex.IsMatch(t, @"^[\s,+\-*/%<>=!.:()\[\]{}&|^?;]+$")) return true;
        return false;
    }

    static readonly Regex DeclStart = new(@"^(pub\s+)?(fn|const|var)\s");

    static readonly Regex VarDeclStart = new(@"^(pub\s+)?(const|var)\s");
    static readonly Regex GameCall = new(@"\bfarm\.[a-z]\w*\s*\(");

    /// <summary>
    /// The snippet's top-level declarations and its other lines. A "///" doc comment stays with the declaration it
    /// documents. varsInMain: a const / var declaration whose initializer calls a farm function (or reads such a
    /// declaration) is a statement of main instead (varsInMain 2: every const / var but the imports): Zig evaluates a
    /// container-level initializer at comptime, so "const b = farm.canHarvest();" can only be a local.
    /// </summary>
    static (List<string> Decls, List<string> Statements) Split(string code, int varsInMain = 0)
    {
        var decls = new List<string>();
        var stmts = new List<string>();
        var moved = new List<string>();
        var lines = code.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!DeclStart.IsMatch(lines[i])) { stmts.Add(lines[i]); continue; }
            // a declaration runs until its braces close and its line ends with '}' or ';'
            var own = new List<string>();
            int depth = 0;
            for (; i < lines.Length; i++)
            {
                own.Add(lines[i]);
                foreach (var ch in lines[i]) { if (ch == '{') depth++; else if (ch == '}') depth--; }
                var t = lines[i].TrimEnd();
                if (depth <= 0 && (t.EndsWith("}") || t.EndsWith(";"))) break;
            }
            var text = string.Join("\n", own);
            bool toMain = varsInMain > 0 && VarDeclStart.IsMatch(text) && !text.Contains("@import(")
                && (varsInMain > 1 || GameCall.IsMatch(text) || moved.Any(n => Regex.IsMatch(text.Substring(text.IndexOf('=') + 1), @"(?<![\w.])" + n + @"\b")));
            if (toMain) { stmts.AddRange(own); moved.Add(DeclName(text)); continue; }
            // its doc comment lines come along
            int k = stmts.Count;
            while (k > 0 && stmts[k - 1].TrimStart().StartsWith("///")) k--;
            var doc = stmts.Skip(k).ToList();
            stmts.RemoveRange(k, stmts.Count - k);
            decls.Add(string.Join("\n", doc.Concat(own)));
        }
        return (decls, stmts);
    }

    static string DeclName(string decl)
    {
        var m = Regex.Match(decl, @"^(pub\s+)?(fn|const|var)\s+([A-Za-z_]\w*)", RegexOptions.Multiline);
        return m.Success ? m.Groups[3].Value : null;
    }

    static bool IsImport(string decl, string name) => DeclName(decl) == name && decl.Contains("@import(");

    static readonly Regex LocalDecl = new(@"^(?:const|var)\s+([A-Za-z_]\w*)");

    /// <summary>A snippet as a program: declarations at the top, the other lines inside pub fn main, the imports in front.</summary>
    internal static string Wrap(string code, IEnumerable<string> earlierDecls = null, IEnumerable<string> earlierStatements = null, bool bareLast = false, int varsInMain = 0)
    {
        var (decls, stmts) = Split(code, varsInMain);
        var res = new StringBuilder();
        if (!decls.Any(d => IsImport(d, "std"))) res.Append("const std = @import(\"std\");\n");
        if (!decls.Any(d => IsImport(d, "farm"))) res.Append("const farm = @import(\"farm\");\n");
        if (earlierDecls != null)
            foreach (var e in earlierDecls) res.Append(e).Append('\n');
        foreach (var d in decls) res.Append(d).Append('\n');
        bool hasMain = decls.Any(d => Regex.IsMatch(d, @"^pub\s+fn\s+main\s*\("));
        if (!hasMain)
        {
            var body = new List<string>();
            if (earlierStatements != null) body.AddRange(earlierStatements);
            int last = stmts.FindLastIndex(s => s.Trim().Length > 0 && !s.TrimStart().StartsWith("//"));
            for (int i = 0; i < stmts.Count; i++)
            {
                var s = stmts[i];
                var t = s.TrimEnd();
                if (i == last && !(t.EndsWith(";") || t.EndsWith("{") || t.EndsWith("}") || t.EndsWith(",")))
                    s = bareLast ? t.TrimStart() + ";" : "_ = " + t.TrimStart() + ";";
                body.Add(s);
            }
            // the declarations that became statements of main (this snippet's or an earlier one's): discard the ones
            // nothing uses ("unused local constant" is an error in Zig)
            var all = res + string.Join("\n", body);
            foreach (var s in body.ToList())
            {
                var lm = LocalDecl.Match(s);
                if (lm.Success && Regex.Matches(all, @"(?<![\w.@""])" + lm.Groups[1].Value + @"\b").Count == 1) body.Add("_ = " + lm.Groups[1].Value + ";");
            }
            bool usesTry = body.Any(s => Regex.IsMatch(s, @"\btry\b"));
            res.Append(usesTry ? "pub fn main() !void {\n" : "pub fn main() void {\n");
            foreach (var s in body) res.Append(s.Length > 0 ? "    " + s : s).Append('\n');
            res.Append("}\n");
        }
        return res.ToString();
    }

    /// <summary>null when the code compiles and the game parser accepts the emitted Python, else the reason.</summary>
    internal static string TryCompile(string code, string module, Dictionary<string, string> others)
    {
        var r = Zig.Compile(code, new CompileOptions { FileName = module, OtherFiles = others });
        if (!r.Success)
            return string.Join("; ", r.Errors.Select(e => e.Message + " [" + (e.Start >= 0 && e.End <= code.Length && e.End >= e.Start ? code.Substring(e.Start, e.End - e.Start) : "?") + "]"));
        var oracle = GameOracle.TryParse(r.Python);
        return oracle == null ? null : "game parser: " + oracle + "\n    python:\n    " + r.Python.Replace("\n", "\n    ");
    }

    /// <summary>
    /// A snippet: compiled wrapped (the last expression discarded with "_ =", else as a bare statement; its const / var
    /// declarations at the top level, else inside main). program: the source that compiled.
    /// </summary>
    internal static string TrySnippet(string code, string module, Dictionary<string, string> others, IEnumerable<string> decls, IEnumerable<string> stmts, out string program, out int varsInMain)
    {
        string first = null;
        program = null; varsInMain = 0;
        bool hasVars = Split(code).Decls.Any(d => VarDeclStart.IsMatch(d) && !d.Contains("@import("));
        foreach (var vim in hasVars ? new[] { 0, 1, 2 } : new[] { 0 })
            foreach (var bare in new[] { false, true })
            {
                var p = Wrap(code, decls, stmts, bare, vim);
                var why = TryCompile(p, module, others);
                if (why == null) { program = p; varsInMain = vim; return null; }
                first ??= why;
            }
        return first;
    }

    internal static string TrySnippet(string code, string module, Dictionary<string, string> others, IEnumerable<string> decls = null, IEnumerable<string> stmts = null) =>
        TrySnippet(code, module, others, decls, stmts, out _, out _);

    /// <summary>"{{", "}}", "](", "![" and lines starting with "#" are markup for the game, even inside code.</summary>
    internal static string ForbiddenMarkup(string code)
    {
        foreach (var bad in new[] { "{{", "}}", "](", "![" })
            if (code.Contains(bad)) return "contains " + bad;
        if (Regex.IsMatch(code, @"(^|\n)#")) return "a line starts with #";
        return null;
    }

    [SkippableFact]
    public void EveryGameDocsPage_HasAZigVersion()
    {
        Skip.IfNot(Directory.Exists(RepoPaths.GameDocs), "game docs not found");
        var en = Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/')).ToList();
        var missing = en.Where(rel => !File.Exists(Path.Combine(DocsDir, rel))).ToList();
        Assert.True(missing.Count == 0, "missing content/zig/docs pages: " + string.Join(", ", missing));
        var extra = Pages().Select(Rel).Where(rel => !en.Contains(rel)).ToList();
        Assert.True(extra.Count == 0, "content/zig/docs pages without a game page: " + string.Join(", ", extra));
        Assert.Equal(48, en.Count);
        Assert.Equal(48, Pages().Count());
    }

    /// <summary>A content block that compiled: the exact program (wrapped snippet, full program or file window).</summary>
    internal sealed record CompiledBlock(string Id, string Module, string Program, Dictionary<string, string> Others);

    /// <summary>Compiles every docs block like EveryCodeBlock_Compiles_OrIsAListedException describes (see the class summary).</summary>
    static List<CompiledBlock> CompileDocsBlocks(Dictionary<string, string> exceptions, List<string> failures, List<string> stale, out string summary)
    {
        var compiled = new List<CompiledBlock>();
        var seen = new HashSet<string>();
        int ok = 0, full = 0, modules = 0, withContext = 0;
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var blocks = DocsCorpus.Spans(rel, Read(page)).Where(s => s.IsBlock && !IsProse(s.Text)).ToList();
            var files = new Dictionary<string, string>();
            foreach (var s in blocks)
            {
                var m = ModuleHeader.Match(s.Text.Trim('\n'));
                if (m.Success && m.Groups[1].Value != "main") files[m.Groups[1].Value] = s.Text.Trim('\n');
            }
            var decls = new List<string>();
            var stmts = new List<string>();
            foreach (var s in blocks)
            {
                var code = s.Text.Trim('\n');
                var m = ModuleHeader.Match(code);
                var module = m.Success ? m.Groups[1].Value : "main";
                var others = files.Count == 0 ? null : files.Where(kv => kv.Key != module).ToDictionary(kv => kv.Key, kv => kv.Value);
                string why, program = null;
                bool isSnippet = !m.Success && !code.Contains("pub fn main(");
                if (!isSnippet)
                {
                    why = TryCompile(code, module, others);
                    if (why == null) { program = code; if (m.Success) modules++; else full++; }
                }
                else
                {
                    why = TrySnippet(code, module, others, null, null, out program, out var vim);
                    if (why != null)
                    {
                        var mine = Split(code).Decls.Select(DeclName).Where(n => n != null).ToHashSet();
                        var before = decls.Where(d => !mine.Contains(DeclName(d) ?? "")).ToList();
                        if (TrySnippet(code, module, others, before, null, out program, out vim) == null || TrySnippet(code, module, others, before, stmts, out program, out vim) == null) { why = null; withContext++; }
                    }
                    if (why == null)
                    {
                        var (ds, sts) = Split(code, vim);
                        foreach (var d in ds)
                        {
                            var n = DeclName(d);
                            if (n == "std" || n == "farm") continue;
                            decls.RemoveAll(x => n != null && DeclName(x) == n);
                            decls.Add(d);
                        }
                        // a final bare expression is not a statement the later blocks can build on
                        var t = sts.Where(x => x.Trim().Length > 0).LastOrDefault()?.TrimEnd() ?? ";";
                        if (!(t.EndsWith(";") || t.EndsWith("}"))) sts = sts.Take(sts.FindLastIndex(x => x.Trim().Length > 0)).ToList();
                        stmts.AddRange(sts);
                    }
                }
                if (exceptions.ContainsKey(s.Id))
                {
                    seen.Add(s.Id);
                    if (why == null) stale.Add(s.Id + " (now compiles)");
                }
                else if (why == null) { ok++; compiled.Add(new CompiledBlock(s.Id, module, program, others)); }
                else failures.Add(s.Id + ": " + why + "\n    " + code.Replace("\n", "\n    "));
            }
        }
        stale.AddRange(exceptions.Keys.Where(k => !seen.Contains(k)).Select(k => k + " (no such block)"));
        summary = $"{ok} blocks compile ({full} full programs, {modules} file windows, {withContext} snippets with the earlier blocks of their page), {seen.Count} listed exceptions";
        return compiled;
    }

    static Dictionary<string, string> DocsExceptions() => Exceptions().Where(kv => kv.Key.Contains(".md#")).ToDictionary(kv => kv.Key, kv => kv.Value);

    [Fact]
    public void EveryCodeBlock_Compiles_OrIsAListedException()
    {
        Assert.True(Directory.Exists(DocsDir), "content/zig/docs is missing");
        var failures = new List<string>();
        var stale = new List<string>();
        var ok = CompileDocsBlocks(DocsExceptions(), failures, stale, out var summary).Count;
        output.WriteLine(summary);
        Assert.True(failures.Count == 0, failures.Count + " Zig docs blocks do not compile (fix them or list them in ZigContentExceptions.txt):\n" + string.Join("\n", failures));
        Assert.True(stale.Count == 0, "stale entries in ZigContentExceptions.txt:\n" + string.Join("\n", stale));
        Assert.True(ok > 100, "suspiciously few blocks: " + ok);
    }

    /// <summary>
    /// The same programs through the real Zig 0.17 compiler (zig on PATH or $JF_ZIG; skipped without it): "zig
    /// ast-check" on every file, then "zig build-exe -fno-emit-bin" with the farm.zig stub and every top-level
    /// declaration referenced (ZigHooks, like conformance rows). Covers the docs blocks, the tooltip examples and the
    /// unlock snippets that JavaFarmer accepts.
    /// </summary>
    [SkippableFact]
    public void EveryCompilingBlock_IsAcceptedByTheRealZigCompiler()
    {
        var tool = new ZigHooks().Conformance.Resolve();
        Skip.If(tool == null, "SKIPPED: zig not found on PATH (or $JF_ZIG): install Zig 0.17 to check the content with the real compiler");
        var exe = Proc.Find(tool.Exe);
        var blocks = CompileDocsBlocks(DocsExceptions(), new List<string>(), new List<string>(), out _);
        blocks.AddRange(TooltipPrograms());
        blocks.AddRange(UnlockPrograms());
        var bad = new System.Collections.Concurrent.ConcurrentBag<string>();
        var env = new Dictionary<string, string>
        {
            ["ZIG_GLOBAL_CACHE_DIR"] = Path.Combine(Environment.GetEnvironmentVariable("JF_TEST_TMP") ?? Path.GetTempPath(), "jf-zig-global-cache"),
        };
        Parallel.ForEach(blocks, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, b =>
        {
            var dir = RepoPaths.NewTempDir("zig-content");
            try
            {
                var sources = new Dictionary<string, string>();
                string root;
                if (b.Module != "main")
                {
                    // a file window: a root that imports it
                    sources[b.Module + ".zig"] = b.Program;
                    root = "root.zig";
                    File.WriteAllText(Path.Combine(dir, root), "pub fn main() void {}\ncomptime {\n    _ = @import(\"" + b.Module + ".zig\");\n}\n");
                }
                else { sources["main.zig"] = b.Program; root = "main.zig"; }
                if (b.Others != null)
                    foreach (var kv in b.Others) sources.TryAdd(kv.Key + ".zig", kv.Value);
                foreach (var kv in sources) File.WriteAllText(Path.Combine(dir, kv.Key), ZigHooks.ForceAnalysis(kv.Value, kv.Key));
                File.WriteAllText(Path.Combine(dir, ZigLanguage.StubFileName), ZigHooks.Stub(sources.Values));
                foreach (var f in sources.Keys)
                {
                    var (ae, ao) = Proc.Run(exe, "ast-check " + f, dir, 60000, env);
                    if (ae != 0) { bad.Add(b.Id + ": zig ast-check " + f + ":\n" + ao + "\n--- " + f + " ---\n" + sources[f]); return; }
                }
                var (e, o) = Proc.Run(exe, "build-exe -fno-emit-bin --dep farm -Mroot=" + root + " -Mfarm=" + ZigLanguage.StubFileName, dir, 180000, env);
                if (e != 0) bad.Add(b.Id + ": zig build-exe:\n" + o + "\n--- program ---\n" + b.Program);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        output.WriteLine(blocks.Count + " content programs checked with " + exe);
        Assert.True(bad.IsEmpty, bad.Count + " content programs are rejected by the real Zig compiler:\n" + string.Join("\n\n", bad.OrderBy(x => x, StringComparer.Ordinal)));
    }

    static readonly Regex PythonLeft = new(@"\b(True|False|None|elif|def|pass|lambda|nonlocal)\b|(?<![.\w@:])(range|len|str|print|append|list|dict|set|type|harvest|plant|move|till|measure)\(|(?<![.\w@])[a-z]+_[a-z_]+\(|(^|\n)\s*(from \w+ )?import \w|(^|\n)\s*global \w");

    [Fact]
    public void CodeSpans_ContainNoGameMarkup_AndNoPython()
    {
        var bad = new List<string>();
        foreach (var page in Pages())
        {
            var rel = Rel(page);
            var md = Read(page);
            foreach (var s in DocsCorpus.Spans(rel, md))
            {
                var why = ForbiddenMarkup(s.Text);
                if (why != null) bad.Add(s.Id + ": " + why);
                if (rel != "patchnotes.md" && PythonLeft.IsMatch(s.Text)) bad.Add(s.Id + ": looks like Python: " + s.Text.Split('\n')[0]);
                if (s.IsBlock && rel != "patchnotes.md" && Regex.IsMatch(s.Text, @":[ \t]*(\n|$)")) bad.Add(s.Id + ": a line ends with ':' (Python): " + s.Text.Split('\n')[0]);
            }
            foreach (var line in md.Split('\n'))
                if (Regex.IsMatch(line, @"<(unlock|spoiler)[^>]*>\s*#")) bad.Add(rel + ": a heading follows an unlock/spoiler tag on the same line: " + line);
            if (md.Split(Bt).Length % 2 == 0) bad.Add(rel + ": odd number of backticks");
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
        Assert.True(Directory.Exists(StringsDir), "content/zig/Strings is missing");
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
                bool zigWord = name == "code_tooltips.txt" && ZigWords.Any(w => key == "code_tooltip_" + w);
                if (!en.ContainsKey(key) && !zigWord) { bad.Add(name + ": " + key + " is not a key of the game's " + name); continue; }
                if (value.Contains("{{") || value.Contains("}}")) bad.Add(name + ": " + key + " contains {{ or }}");
                if (value.Split(Bt).Length % 2 == 0) bad.Add(name + ": " + key + " has an odd number of backticks");
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
            // every game key that the Java content overrides is rewritten for Zig too
            var javaFile = Path.Combine(javaDir, name);
            if (File.Exists(javaFile))
                foreach (var (key, _) in Entries(Read(javaFile)).Where(e => en.ContainsKey(e.Key)))
                    if (!entries.Any(e => e.Key == key)) bad.Add(name + ": " + key + " is overridden for Java but not for Zig");
        }
        if (Directory.Exists(javaDir))
            foreach (var jf in Directory.GetFiles(javaDir, "*.txt"))
                if (!File.Exists(Path.Combine(StringsDir, Path.GetFileName(jf)))) bad.Add("missing content/zig/Strings/" + Path.GetFileName(jf));
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void CodeTooltips_CoverEveryBuiltin_AndTheirExamplesCompile()
    {
        var entries = Entries(Read(Path.Combine(StringsDir, "code_tooltips.txt"))).Where(e => e.Value != null).ToList();
        var bad = new List<string>();
        foreach (var w in ZigWords)
            if (!entries.Any(e => e.Key == "code_tooltip_" + w)) bad.Add("no tooltip for the Zig word " + w);
        foreach (var b in GameApi.Builtins.Where(b => b.Extension == null))
            if (!entries.Any(e => e.Key == "code_tooltip_" + b.PythonName)) bad.Add("no Zig tooltip for the builtin " + b.PythonName);
        var gamePath = Path.Combine(GameStrings, "code_tooltips.txt");
        var gameKeys = File.Exists(gamePath) ? Entries(Read(gamePath)).Select(e => e.Key).Distinct().ToList() : new List<string>();
        foreach (var k in gameKeys)
            if (!entries.Any(e => e.Key == k)) bad.Add("no Zig tooltip for the game key " + k);
        int ok = 0;
        foreach (var (key, value) in entries)
        {
            var py = PythonLeft.Match(value);
            if (py.Success) bad.Add(key + ": looks like Python: " + py.Value);
            var i = value.IndexOf("example:", StringComparison.Ordinal);
            if (i < 0) continue;
            foreach (Match m in CodeSpan.Matches(value.Substring(i)))
            {
                var code = m.Groups[1].Value.Trim('\n');
                if (IsProse(code)) continue;
                var why = code.Contains("pub fn main(") ? TryCompile(code, "main", null) : TrySnippet(code, "main", null);
                if (why == null) ok++;
                else bad.Add(key + ": " + why + "\n    " + code.Replace("\n", "\n    "));
            }
        }
        output.WriteLine(ok + " tooltip examples compile");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
        Assert.True(ok >= gameKeys.Count / 2, "too few tooltip examples: " + ok);
    }

    /// <summary>The tooltip examples JavaFarmer accepts, as the programs that compiled.</summary>
    static IEnumerable<CompiledBlock> TooltipPrograms()
    {
        foreach (var (key, value) in Entries(Read(Path.Combine(StringsDir, "code_tooltips.txt"))).Where(e => e.Value != null))
        {
            var i = value.IndexOf("example:", StringComparison.Ordinal);
            if (i < 0) continue;
            int n = 0;
            foreach (Match m in CodeSpan.Matches(value.Substring(i)))
            {
                var code = m.Groups[1].Value.Trim('\n');
                if (IsProse(code)) continue;
                n++;
                if (code.Contains("pub fn main(")) { if (TryCompile(code, "main", null) == null) yield return new CompiledBlock("tooltip " + key + "#" + n, "main", code, null); }
                else if (TrySnippet(code, "main", null, null, null, out var p, out _) == null) yield return new CompiledBlock("tooltip " + key + "#" + n, "main", p, null);
            }
        }
    }

    /// <summary>The unlock snippets JavaFarmer accepts (not the listed fragments), as the programs that compiled.</summary>
    static IEnumerable<CompiledBlock> UnlockPrograms()
    {
        var fragments = Exceptions().Where(kv => kv.Key.StartsWith("@")).Select(kv => kv.Key.Substring(1)).ToHashSet();
        foreach (var (name, code) in UnlockBlocks(Path.Combine(ContentRoot, "unlock_display_code.txt")))
        {
            if (fragments.Contains(name) || code.Length == 0) continue;
            if (code.Contains("pub fn main(")) { if (TryCompile(code, "main", null) == null) yield return new CompiledBlock("unlock " + name, "main", code, null); }
            else if (TrySnippet(code, "main", null, null, null, out var p, out _) == null) yield return new CompiledBlock("unlock " + name, "main", p, null);
        }
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
        var zig = UnlockBlocks(Path.Combine(ContentRoot, "unlock_display_code.txt"));
        var java = UnlockBlocks(Path.Combine(RepoPaths.Root, "content", "java", "unlock_display_code.txt"));
        Assert.Equal(java.Select(b => b.Name).OrderBy(x => x, StringComparer.Ordinal), zig.Select(b => b.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(zig.Count, zig.Select(b => b.Name).Distinct().Count());
        var fragments = Exceptions().Where(kv => kv.Key.StartsWith("@")).ToDictionary(kv => kv.Key.Substring(1), kv => kv.Value);
        var bad = new List<string>();
        int ok = 0;
        foreach (var (name, code) in zig)
        {
            var mk = ForbiddenMarkup(code);
            if (mk != null) bad.Add(name + ": " + mk);
            if (code.Length == 0) bad.Add(name + ": empty");
            if (PythonLeft.IsMatch(code)) bad.Add(name + ": looks like Python: " + code);
            var why = code.Contains("pub fn main(") ? TryCompile(code, "main", null) : TrySnippet(code, "main", null);
            if (fragments.ContainsKey(name)) { if (why == null) bad.Add(name + ": listed as a fragment but compiles"); continue; }
            if (why == null) ok++;
            else bad.Add(name + ": " + why + "\n    " + code.Replace("\n", "\n    "));
        }
        foreach (var k in fragments.Keys)
            if (!zig.Any(b => b.Name == k)) bad.Add(k + ": listed as a fragment but has no entry");
        output.WriteLine(ok + " unlock snippets compile, " + fragments.Count + " fragments");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
