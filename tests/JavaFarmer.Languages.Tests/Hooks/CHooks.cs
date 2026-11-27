namespace JavaFarmer.Languages.Tests;

/// <summary>
/// C: gcc -std=c23 against the generated farm.h (header-only: the game functions are stubs, collections and strings
/// really work), differential runs build the program with gcc and run it. See docs/languages/c.md.
/// </summary>
public sealed class CHooks : LanguageHooks
{
    public override string Id => "c";

    /// <summary>The flags JavaFarmer's C subset is checked against (unused variables are not errors in the game).</summary>
    public const string Flags = "-std=c23 -Wall -Werror -Wno-unused";

    static IReadOnlyDictionary<string, string> Stub(ToolContext ctx) =>
        new Dictionary<string, string> { ["farm.h"] = ((CFamilyLanguage)Compiler.Languages.Get("c")).ApiStubFor(ctx.Sources.Values) };

    public override Toolchain Conformance => new Toolchain
    {
        Exe = "gcc",
        SkipHint = "install gcc 14+ (C23)",
        ExtraFiles = Stub,
        Args = ctx => Flags + " -fsyntax-only " + string.Join(" ", ctx.Sources.Keys.Select(ctx.Quote)),
    };

    public override Toolchain Runner => new Toolchain
    {
        Exe = "gcc",
        SkipHint = "install gcc 14+ (C23)",
        ExtraFiles = Stub,
        Args = ctx => Flags + " -o prog " + string.Join(" ", ctx.Sources.Keys.Select(ctx.Quote)) + " -lm",
        Then = new Toolchain { Exe = "./prog", Args = _ => "" },
    };

    public override string PythonPrelude => CFamilyPrelude.C;
}

/// <summary>CPython preludes that print values the way farm.h's str_of / farm.hpp's std::format do.</summary>
public static class CFamilyPrelude
{
    const string Template = @"import builtins as __b
def __fmt(v, nested=False):
    if v is None:
        return 'NULLTEXT'
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
        return 'LO' + ', '.join(__fmt(x, True) for x in v) + 'LC'
    if isinstance(v, __b.set):
        return '{' + ', '.join(__fmt(x, True) for x in v) + '}'
    if isinstance(v, __b.dict):
        return '{' + ', '.join(__fmt(k, True) + ': ' + __fmt(x, True) for k, x in v.items()) + '}'
    if isinstance(v, tuple):
        return 'TO' + ', '.join(__fmt(x, True) for x in v) + 'TC'
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

    public static readonly string C = Template.Replace("NULLTEXT", "NULL").Replace("'LO'", "'{'").Replace("'LC'", "'}'").Replace("'TO'", "'{'").Replace("'TC'", "'}'");
    public static readonly string Cpp = Template.Replace("NULLTEXT", "nullptr").Replace("'LO'", "'['").Replace("'LC'", "']'").Replace("'TO'", "'('").Replace("'TC'", "')'");
}
