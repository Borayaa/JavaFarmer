using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests;

/// <summary>
/// Rust: rustc (edition 2024). The farm.rs stub is compiled as a library crate "farm"; the crate root (the file of the group
/// that contains fn main, other files are its modules) is type-checked with --emit=metadata against it. Differential runs
/// build and run the binary.
/// </summary>
public sealed class RustHooks : LanguageHooks
{
    public override string Id => "rust";

    static readonly string[] Categories = { "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards" };

    /// <summary>farm.rs with the game's known constants plus every Enum::Member the sources use.</summary>
    public static string Stub(IEnumerable<string> sources)
    {
        var all = string.Join("\n", sources);
        return RustStub.Build(cat =>
        {
            var en = GameApi.GameTypeOfCategory(cat);
            return Regex.Matches(all, @"\b" + en + @"::([A-Za-z_]\w*)").Select(m => m.Groups[1].Value).Distinct().ToList();
        });
    }

    /// <summary>The crate root of a group: the file with fn main (else the file under test).</summary>
    public static string Root(ToolContext ctx)
    {
        foreach (var kv in ctx.Sources)
            if (Regex.IsMatch(kv.Value, @"(^|\n)\s*fn\s+main\s*\(")) return kv.Key;
        return ctx.MainFile;
    }

    public static string PrepareSource(string src, string name) => "#![allow(unused)]\n" + src;

    static Toolchain StubBuild(Toolchain then) => new Toolchain
    {
        Exe = "rustc",
        SkipHint = "install rustc 1.85+ (edition 2024)",
        PrepareSource = PrepareSource,
        ExtraFiles = ctx => new Dictionary<string, string> { ["farm.rs"] = Stub(ctx.Sources.Values) },
        Args = ctx => "--edition 2024 --crate-type lib --crate-name farm -o libfarm.rlib farm.rs",
        Then = then,
    };

    public override Toolchain Conformance => StubBuild(new Toolchain
    {
        Exe = "rustc",
        Args = ctx => "--edition 2024 --crate-type bin --emit=metadata -o check.rmeta --extern farm=libfarm.rlib -L . " + ctx.Quote(Root(ctx)),
    });

    public override Toolchain Runner => StubBuild(new Toolchain
    {
        Exe = "rustc",
        Args = ctx => "--edition 2024 --crate-type bin -o prog --extern farm=libfarm.rlib -L . " + ctx.Quote(Root(ctx)),
        Then = new Toolchain { Exe = "./prog", Args = _ => "" },
    });

    /// <summary>print/str like Rust's println!: true/false, None, Debug lists [1, 2] / tuples (1, 2) with nested strings in "".</summary>
    public override string PythonPrelude => @"import builtins as __b
def __fmt(v, nested=False):
    if v is None:
        return 'None'
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
    if isinstance(v, list):
        return '[' + ', '.join(__fmt(x, True) for x in v) + ']'
    if isinstance(v, tuple):
        return '(' + ', '.join(__fmt(x, True) for x in v) + ')'
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
