using System.Text;

namespace JavaFarmer.Languages.Tests;

/// <summary>
/// R: conformance = R's own parser (parse(file = f)) on every file of the program plus farm.R, with Rscript when it is
/// on PATH, else the docker r-base image (no network, read-only mount). Differential runs source farm.R (game-style
/// print / cat / paste) and then the program.
/// </summary>
public sealed class RHooks : LanguageHooks
{
    public override string Id => "r";

    const string Image = "r-base";

    static IReadOnlyDictionary<string, string> CheckFiles(ToolContext ctx)
    {
        var files = new[] { "farm.R" }.Concat(ctx.Sources.Keys).Select(f => "\"" + f.Replace("\\", "/") + "\"");
        return new Dictionary<string, string>
        {
            ["farm.R"] = Compiler.Languages.Get("r").ApiStubFile(out _),
            [".jf_check.R"] = "for (f in c(" + string.Join(", ", files) + ")) invisible(parse(file = f))\n",
        };
    }

    static IReadOnlyDictionary<string, string> RunFiles(ToolContext ctx) => new Dictionary<string, string>
    {
        ["farm.R"] = Compiler.Languages.Get("r").ApiStubFile(out _),
        [".jf_run.R"] = "source(\"farm.R\")\nsource(\"" + ctx.MainFile + "\")\n",
    };

    static string Docker(ToolContext ctx, string script) =>
        "run --rm --network none -v " + ctx.Quote(ctx.WorkDir + ":/w:ro") + " -w /w " + Image + " Rscript " + script;

    public override Toolchain Conformance => new Toolchain
    {
        Exe = "Rscript",
        SkipHint = "install R 4.x (Rscript) or docker with the r-base image (docker pull r-base)",
        ExtraFiles = CheckFiles,
        Args = _ => ".jf_check.R",
        Fallback = new Toolchain
        {
            Exe = "docker",
            SkipHint = "install R 4.x (Rscript) or docker with the r-base image (docker pull r-base)",
            ExtraFiles = CheckFiles,
            Args = ctx => Docker(ctx, ".jf_check.R"),
        },
    };

    public override Toolchain Runner => new Toolchain
    {
        Exe = "Rscript",
        SkipHint = "install R 4.x (Rscript) or docker with the r-base image (docker pull r-base)",
        ExtraFiles = RunFiles,
        Args = _ => ".jf_run.R",
        Fallback = new Toolchain
        {
            Exe = "docker",
            SkipHint = "install R 4.x (Rscript) or docker with the r-base image (docker pull r-base)",
            ExtraFiles = RunFiles,
            Args = ctx => Docker(ctx, ".jf_run.R"),
        },
    };

    /// <summary>CPython print / str formatted like farm.R's print (7 significant digits) and as.character (15).</summary>
    public override string PythonPrelude => RPrelude.Text;

    /// <summary>Also drops trailing spaces (cat(a, "\n") prints "a \n").</summary>
    public override string NormaliseOutput(string s) =>
        Normalise.Output(string.Join("\n", (s ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())));
}

public static class RPrelude
{
    public const string Text = @"import builtins as __b
import re as __re
__reserved = {'if', 'else', 'repeat', 'while', 'function', 'for', 'in', 'next', 'break', 'TRUE', 'FALSE', 'NULL', 'Inf', 'NaN', 'NA'}
def __num(v, d):
    if v == int(v) and abs(v) < 1e15:
        return __b.str(int(v))
    return ('%.' + __b.str(d) + 'g') % v
def __kind(x):
    if x is True or x is False:
        return 'b'
    if isinstance(x, (int, float)):
        return 'n'
    if isinstance(x, __b.str):
        return 's'
    return 'x'
def __key(k):
    if isinstance(k, __b.str) and __re.match(r'^[A-Za-z.][A-Za-z0-9._]*$', k) and not __re.match(r'^\.[0-9]', k) and k not in __reserved:
        return k
    return '""' + __b.str(k) + '""'
def __fmt(v, nested=False, d=7):
    if v is None:
        return 'NULL'
    if v is True:
        return 'TRUE'
    if v is False:
        return 'FALSE'
    if isinstance(v, (int, float)):
        return __num(v, d)
    if isinstance(v, __b.str):
        return '""' + v + '""' if nested else v
    if isinstance(v, dict):
        if len(v) == 0:
            return 'list()'
        return 'list(' + ', '.join(__key(k) + ' = ' + __fmt(x, True, d) for k, x in v.items()) + ')'
    if isinstance(v, (list, tuple, set)):
        v = __b.list(v)
        if len(v) == 0:
            return 'list()'
        kinds = __b.set(__kind(x) for x in v)
        opener = 'c(' if len(kinds) == 1 and 'x' not in kinds else 'list('
        return opener + ', '.join(__fmt(x, True, d) for x in v) + ')'
    return __b.str(v)
def print(*a):
    __b.print(' '.join(__fmt(x) for x in a))
def quick_print(*a):
    print(*a)
def str(*a):
    if len(a) == 0:
        return ''
    return __fmt(a[0], False, 15)
";
}
