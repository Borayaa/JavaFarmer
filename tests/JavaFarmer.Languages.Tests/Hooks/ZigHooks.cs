using System.Text;
using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests;

/// <summary>
/// Zig 0.17: the farm.zig stub (ZigLanguage.ApiStubFile plus every enum member the sources use) is the module "farm";
/// the file of the group that declares pub fn main is the root module (other files are imported with @import("x.zig")).
/// Conformance type-checks with "zig build-exe -fno-emit-bin"; Zig analyses lazily, so PrepareSource appends a
/// comptime block that references every top-level function and variable (otherwise unused helpers would never be
/// checked). Differential runs build the binary and run it (std.debug.print writes to stderr; the harness merges both
/// streams). The zig executable is "zig" on PATH or the absolute path in the environment variable JF_ZIG.
/// See docs/languages/zig.md.
/// </summary>
public sealed class ZigHooks : LanguageHooks
{
    public override string Id => "zig";

    static string Exe => Environment.GetEnvironmentVariable("JF_ZIG") is { Length: > 0 } p ? p : "zig";
    const string Hint = "install Zig 0.17.1 (ziglang.org/download, checksum verified) and put zig on PATH or set JF_ZIG to its absolute path";

    static readonly Regex Literal = new(@"(?<![\w\)\]\.])\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    static readonly Regex Qualified = new(@"\bfarm\.(Entity|Item|Ground|Unlock|Hat|Leaderboard|Direction)\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    static readonly Regex TopFn = new(@"(?m)^(?:pub\s+)?fn\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);
    static readonly Regex TopVar = new(@"(?m)^(?:pub\s+)?(?:var|const)\s+([A-Za-z_][A-Za-z0-9_]*)\b(?![^\n]*@import)", RegexOptions.Compiled);
    static readonly HashSet<string> NotMembers = new(StringComparer.Ordinal) { "empty", "init", "items", "len" };

    /// <summary>farm.zig with the game's known constants plus every enum member the sources name (.Carrot, farm.Item.Hay).</summary>
    public static string Stub(IEnumerable<string> sources)
    {
        var all = string.Join("\n", sources);
        var cats = new[] { "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards", "Directions" };
        var extra = new Dictionary<string, IEnumerable<string>>();
        // a bare .Member can belong to any enum: adding it to every enum is harmless
        var bare = Literal.Matches(all).Select(m => m.Groups[1].Value).Where(n => char.IsUpper(n[0]) && !NotMembers.Contains(n)).Distinct().ToList();
        foreach (var c in cats)
        {
            var type = GameApi.GameTypeOfCategory(c);
            extra[c] = bare.Concat(Qualified.Matches(all).Where(m => m.Groups[1].Value == type).Select(m => m.Groups[2].Value)).Distinct().ToList();
        }
        return ZigLanguage.BuildStub(extra);
    }

    /// <summary>The root module of a group: the file that declares pub fn main (else the file under test).</summary>
    public static string Root(ToolContext ctx)
    {
        foreach (var kv in ctx.Sources)
            if (Regex.IsMatch(kv.Value, @"(?m)^pub\s+fn\s+main\s*\(")) return kv.Key;
        return ctx.MainFile;
    }

    /// <summary>Appends comptime { _ = &f; ... } so that zig analyses every top-level declaration (Zig is lazy).</summary>
    public static string ForceAnalysis(string src, string fileName)
    {
        var names = TopFn.Matches(src).Select(m => m.Groups[1].Value).Where(n => n != "main")
            .Concat(TopVar.Matches(src).Select(m => m.Groups[1].Value)).Distinct().ToList();
        if (names.Count == 0) return src;
        var sb = new StringBuilder(src);
        if (!src.EndsWith("\n")) sb.Append('\n');
        sb.Append("\ncomptime {\n");
        foreach (var n in names) sb.Append("    _ = &").Append(n).Append(";\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    static IReadOnlyDictionary<string, string> Extra(ToolContext ctx) =>
        new Dictionary<string, string> { [ZigLanguage.StubFileName] = Stub(ctx.Sources.Values) };

    static string Modules(ToolContext ctx) => "--dep farm -Mroot=" + ctx.Quote(Root(ctx)) + " -Mfarm=" + ZigLanguage.StubFileName;

    static Dictionary<string, string> Env => new()
    {
        // keep zig's caches out of the home directory and the repo
        ["ZIG_GLOBAL_CACHE_DIR"] = Path.Combine(Environment.GetEnvironmentVariable("JF_TEST_TMP") ?? Path.GetTempPath(), "jf-zig-global-cache"),
    };

    public override Toolchain Conformance => new Toolchain
    {
        Exe = Exe,
        SkipHint = Hint,
        PrepareSource = ForceAnalysis,
        ExtraFiles = Extra,
        Env = Env,
        Args = ctx => "build-exe -fno-emit-bin " + Modules(ctx),
    };

    public override Toolchain Runner => new Toolchain
    {
        Exe = Exe,
        SkipHint = Hint,
        ExtraFiles = Extra,
        Env = Env,
        Args = ctx => "build-exe " + Modules(ctx) + " -femit-bin=prog",
        Then = new Toolchain { Exe = "./prog", Args = _ => "" },
    };

    /// <summary>
    /// print/str like std.debug.print with {d} / {s} / {any}: true/false, null, integral floats without ".0" (Zig's {d}
    /// prints 3.0 as 3), slices "{ 1, 2, 3 }" (empty: "{  }"), tuples ".{ 1, 2 }"; nested strings are shown quoted the
    /// way ZigLanguage.FormatValue shows them (real Zig would print their bytes, so diff programs never print them).
    /// </summary>
    public override string PythonPrelude => ZigPrelude.Text;
}

public static class ZigPrelude
{
    public const string Text = @"import builtins as __b
def __fmt(v, nested=False):
    if v is None:
        return 'null'
    if v is True:
        return 'true'
    if v is False:
        return 'false'
    if isinstance(v, float):
        if v == int(v) and abs(v) < 1e15:
            return __b.str(int(v))
        return repr(v)
    if isinstance(v, __b.str):
        return '""' + v + '""' if nested else v
    if isinstance(v, __b.list):
        return '{ ' + ', '.join(__fmt(x, True) for x in v) + ' }'
    if isinstance(v, __b.set):
        return '{ ' + ', '.join(__fmt(x, True) for x in v) + ' }'
    if isinstance(v, __b.dict):
        return '{ ' + ', '.join(__fmt(k, True) + ': ' + __fmt(x, True) for k, x in v.items()) + ' }'
    if isinstance(v, tuple):
        return '.{ ' + ', '.join(__fmt(x, True) for x in v) + ' }'
    return __b.str(v)
def print(*a):
    __b.print(' '.join(__fmt(x) for x in a))
def quick_print(*a):
    print(*a)
def str(*a):
    if len(a) == 0:
        return ''
    return __fmt(a[0])
";
}
