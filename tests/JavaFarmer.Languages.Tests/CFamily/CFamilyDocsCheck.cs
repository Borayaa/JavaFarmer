using JavaFarmer.Compiler;
using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests;

/// <summary>
/// Compiles every code block of content/&lt;c|cpp&gt;/docs. Lesson snippets are often bare statements or a function on its
/// own, so a block is tried (1) as written, (2) inside main, and with the declarations / statements of the earlier
/// blocks of the same page in front of it. The standard headers are always included (the docs show them once, in
/// first_program.md). Blocks listed in tests/corpus/&lt;id&gt;/content-fragments.txt are deliberately partial.
/// </summary>
public static class CFamilyDocsCheck
{
    public sealed class Result
    {
        public int Ok, Listed;
        public List<string> Failures = new(), Stale = new(), Problems = new();
    }

    public static string Prefix(string lang) => lang == "c"
        ? "#include \"farm.h\"\n#include <stdio.h>\n#include <stdbool.h>\n#include <math.h>\n#include <string.h>\n"
        : "#include \"farm.hpp\"\n#include <iostream>\n#include <vector>\n#include <map>\n#include <set>\n#include <string>\n#include <print>\n#include <functional>\n#include <algorithm>\n#include <optional>\n#include <cmath>\n";

    static string Main(string lang, string body) => (lang == "c" ? "int main(void) {\n" : "int main() {\n") + body + "\n}\n";

    /// <summary>A lone word / operator list in prose, not code.</summary>
    public static bool IsProse(string t)
    {
        t = t.Trim();
        if (t.Length == 0) return true;
        // number grids (cactus.md) are pictures made of numbers, not code
        if (Regex.IsMatch(t, @"^[\d\s]+$")) return true;
        if (t.Contains('\n')) return false;
        if (Regex.IsMatch(t, @"^[A-Za-z_][\w:.]*$")) return true;
        if (Regex.IsMatch(t, @"^[\s,+\-*/%<>=!.:#()\[\]{}&|^?;]+$")) return true;
        return false;
    }

    public static Dictionary<string, string> Fragments(string corpusDir)
    {
        var res = new Dictionary<string, string>();
        var f = Path.Combine(corpusDir, "content-fragments.txt");
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

    public static string TryCompile(ILanguage lang, string code)
    {
        var r = lang.Compile(code, new CompileOptions { FileName = "Docs" });
        if (!r.Success) return string.Join("; ", r.Errors.Select(e => e.Message + " [" + (e.Start >= 0 && e.End <= code.Length && e.Start <= e.End ? code.Substring(e.Start, e.End - e.Start) : "?") + "]"));
        var oracle = GameOracle.TryParse(r.Python);
        return oracle == null ? null : "game parser: " + oracle;
    }

    static readonly Regex Python = new(@"(^|\s)(def|elif|from)\s|\b(True|False|None)\b|^\s*print\(", RegexOptions.Multiline);

    public static Result Check(ILanguage lang, string contentDir, string corpusDir)
    {
        var res = new Result();
        var fragments = Fragments(corpusDir);
        var id = lang.Id;
        var prefix = Prefix(id);
        foreach (var page in Directory.GetFiles(contentDir, "*.md", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(contentDir, page).Replace('\\', '/');
            var md = File.ReadAllText(page).Replace("\r\n", "\n");
            var decls = new List<string>();
            var stmts = new List<string>();
            foreach (var s in DocsTranslator.Spans(md))
            {
                var sid = rel + "#" + s.Index;
                if (s.Text.Contains("{{")) res.Problems.Add(sid + ": contains {{ (the game reads it as a placeholder)");
                if (Regex.IsMatch(s.Text, @"(^|\n)#+ ")) res.Problems.Add(sid + ": a code line starts with '# ' (the game reads it as a heading)");
                if (s.Text.Contains("](") || s.Text.Contains("![")) res.Problems.Add(sid + ": contains a markdown link / image marker");
                if (!s.IsBlock || IsProse(s.Text)) continue;
                var code = s.Text.Trim('\n');
                if (Python.IsMatch(code)) res.Problems.Add(sid + ": looks like Python: " + code.Split('\n')[0]);
                string D() => string.Join("\n", decls) + "\n";
                string S() => string.Join("\n", stmts) + "\n";
                string why = null, kind = null;
                var tries = new (string Code, string Kind)[]
                {
                    (code, "decl"),
                    (prefix + code, "decl"),
                    (prefix + Main(id, code), "stmt"),
                    (prefix + D() + code, "decl"),
                    (prefix + D() + Main(id, code), "stmt"),
                    (prefix + D() + Main(id, S() + code), "stmt"),
                };
                foreach (var (c, k) in tries)
                {
                    var w = TryCompile(lang, c);
                    if (w == null) { why = null; kind = k; break; }
                    why ??= w;
                }
                bool listed = fragments.ContainsKey(sid);
                if (listed) { res.Listed++; if (kind != null) res.Stale.Add(sid); continue; }
                if (kind == null) { res.Failures.Add(sid + ": " + why + "\n    " + code.Replace("\n", "\n    ")); continue; }
                res.Ok++;
                if (kind == "decl" && !code.Contains("main(")) decls.Add(code.Contains("#include") ? Regex.Replace(code, @"(?m)^\s*#include.*$", "") : code);
                else if (kind == "stmt") stmts.Add(code);
            }
        }
        return res;
    }
}
