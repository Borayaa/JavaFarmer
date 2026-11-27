using System.Text;
using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests;

/// <summary>Java: javac 25+ against a generated "farm" package stub; differential runs with "java File.java".</summary>
public sealed class JavaHooks : LanguageHooks
{
    public override string Id => "java";

    // conformance: inject the stub imports (JavaStub.Prepare), compile everything with javac

    public override Toolchain Conformance => new Toolchain
    {
        Exe = "javac",
        SkipHint = "install a JDK 25+ (compact source files, JEP 512)",
        PrepareSource = JavaStub.Prepare,
        ExtraFiles = ctx => JavaStub.Files(ctx.Sources.Values),
        Args = ctx => "-proc:none -nowarn -Xlint:none -d out " + string.Join(" ", JavaStub.Files(ctx.Sources.Values).Keys.Concat(ctx.Sources.Keys).Select(ctx.Quote)),
    };

    // differential: the JDK single-file launcher (programs only use System.out)
    public override Toolchain Runner => new Toolchain
    {
        Exe = "java",
        SkipHint = "install a JDK 25+",
        Args = ctx => ctx.Quote(ctx.MainFile),
    };
}

/// <summary>
/// Compilable Java stub of the game API (package farm) generated from GameApi.Functions, plus constant bags with the
/// member names the given sources use (the compiler never hard-codes them, so neither does the stub).
/// </summary>
public static class JavaStub
{
    public const string ImportLine = "import static farm.Farm.*; import farm.*; import java.util.*; import java.util.function.*; ";

    /// <summary>
    /// Source as javac sees it: the stub imports are prepended. JavaFarmer also accepts a compact source file WITHOUT
    /// main as a library whose fields/methods other windows use as File.member (v1 behaviour); real Java rejects that
    /// (an implicit class needs main and cannot be named), so such a file is checked as "public class File" with every
    /// top-level member made static — the closest real-Java equivalent of what JavaFarmer runs.
    /// </summary>
    public static string Prepare(string src, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var code = Blank(src);
        bool isClass = Regex.IsMatch(code, @"\bclass\s+\w+");
        bool hasMain = Regex.IsMatch(code, @"\bvoid\s+main\s*\(");
        if (isClass || hasMain) return ImportLine + src;
        var header = new StringBuilder(ImportLine);
        var body = new StringBuilder();
        int depth = 0, last = 0;
        bool atStart = true;
        for (int i = 0; i < src.Length; i++)
        {
            char c = code[i];
            if (atStart && depth == 0 && !char.IsWhiteSpace(c) && c != ' ')
            {
                body.Append(src, last, i - last);
                last = i;
                atStart = false;
                if (string.CompareOrdinal(code, i, "import ", 0, 7) == 0)
                {
                    int end = code.IndexOf(';', i);
                    if (end < 0) end = src.Length - 1;
                    header.Append(src, i, end - i + 1).Append(' ');
                    last = end + 1;
                    i = end;
                    atStart = true;
                    continue;
                }
                body.Append("static ");
            }
            if (c == '(' || c == '{' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == '}') { depth--; if (depth == 0) atStart = true; }
            else if (c == ';' && depth == 0) atStart = true;
        }
        body.Append(src, last, src.Length - last);
        return header + "public class " + name + " {\n" + body + "\n}\n";
    }

    /// <summary>The source with comments and string literals replaced by spaces (same length), for scanning.</summary>
    static string Blank(string s)
    {
        var sb = new StringBuilder(s);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') sb[i++] = ' '; continue; }
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? s.Length : e + 2;
                for (; i < e; i++) if (s[i] != '\n') sb[i] = ' ';
                i--; continue;
            }
            if (s[i] == '"' || s[i] == '\'')
            {
                char q = s[i]; int j = i + 1;
                while (j < s.Length && s[j] != q && s[j] != '\n') { if (s[j] == '\\') j++; j++; }
                for (int k = i; k <= Math.Min(j, s.Length - 1); k++) if (s[k] != '\n') sb[k] = ' ';
                i = j; continue;
            }
        }
        return sb.ToString();
    }

    static readonly string[] Simple = { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone" };

    public static IReadOnlyDictionary<string, string> Files(IEnumerable<string> sources)
    {
        var files = new Dictionary<string, string>();
        foreach (var t in Simple) files["farm/" + t + ".java"] = "package farm;\npublic final class " + t + " { }\n";
        files["farm/Position.java"] = "package farm;\npublic record Position(int x, int y) { }\n";
        files["farm/Companion.java"] = "package farm;\npublic record Companion(Entity entity, Position position) {\n    public int x() { return position.x(); }\n    public int y() { return position.y(); }\n}\n";
        var all = string.Join("\n", sources);
        foreach (var bag in GameApi.ConstantCategories)
        {
            var type = GameApi.GameTypeOfCategory(bag);
            var members = Regex.Matches(all, @"\b" + bag + @"\.([A-Za-z_]\w*)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x, StringComparer.Ordinal);
            var sb = new StringBuilder("package farm;\npublic final class " + bag + " {\n    private " + bag + "() { }\n");
            foreach (var m in members) sb.Append("    public static final ").Append(type).Append(' ').Append(m).Append(" = new ").Append(type).Append("();\n");
            files["farm/" + bag + ".java"] = sb.Append("}\n").ToString();
        }
        files["farm/Farm.java"] = FarmClass();
        return files;
    }

    static string Default(string type) => type switch
    {
        "void" => null,
        "boolean" => "false",
        "int" or "double" => "0",
        _ => "null",
    };

    static string FarmClass()
    {
        var sb = new StringBuilder("package farm;\nimport java.util.*;\nimport java.util.function.*;\n\npublic final class Farm {\n    private Farm() { }\n");
        foreach (var d in GameApi.DirectionNames) sb.Append("    public static final Direction ").Append(d).Append(" = new Direction();\n");
        foreach (var f in GameApi.Functions)
        {
            if (f.JavaName.Contains('.')) continue;   // System.out.println, Math.min ...: real Java
            var m = Regex.Match(f.Signature, @"^(\S+)\s+(\w+)\((.*)\)$");
            if (!m.Success) continue;
            var ret = m.Groups[1].Value;
            var ps = m.Groups[3].Value.Length == 0 ? new List<(string T, string N)>() :
                Regex.Split(m.Groups[3].Value, @",\s+(?![^<]*>)").Select(p => { int sp = p.LastIndexOf(' '); return (T: p[..sp], N: p[(sp + 1)..]); }).ToList();
            // expand A|B unions into overloads
            var variants = new List<List<(string T, string N)>> { new() };
            foreach (var p in ps)
            {
                var opts = p.T.Split('|');
                variants = variants.SelectMany(v => opts.Select(o => v.Append((o, p.N)).ToList())).ToList();
            }
            foreach (var v in variants)
            {
                bool generic = v.Any(p => p.T.Contains("<T>"));
                sb.Append("    public static ").Append(generic ? "<T> " : "").Append(ret).Append(' ').Append(f.JavaName).Append('(')
                  .Append(string.Join(", ", v.Select(p => p.T + " " + p.N))).Append(") { ");
                var def = Default(ret);
                if (def != null) sb.Append("return ").Append(def).Append("; ");
                sb.Append("}\n");
            }
        }
        return sb.Append("}\n").ToString();
    }
}
