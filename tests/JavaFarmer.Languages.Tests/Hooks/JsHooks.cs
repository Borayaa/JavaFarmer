namespace JavaFarmer.Languages.Tests;

/// <summary>
/// JavaScript: conformance = node --check of every program file as an ES module (package.json "type": "module"; farm.js
/// is written next to them). Differential runs execute the main file in node with farm.js imported first.
/// </summary>
public sealed class JsHooks : LanguageHooks
{
    public override string Id => "js";

    const string PackageJson = "{ \"type\": \"module\" }\n";

    /// <summary>Runs node --check on each file given on the command line (node checks one file per run).</summary>
    const string CheckScript = @"import { spawnSync } from 'node:child_process';
let failed = 0;
for (const f of process.argv.slice(2)) {
	const r = spawnSync(process.execPath, ['--check', f], { encoding: 'utf8' });
	if (r.status !== 0) { failed++; process.stdout.write(f + ':\n' + r.stdout + r.stderr); }
}
process.exit(failed ? 1 : 0);
";

    static IReadOnlyDictionary<string, string> Files(bool check)
    {
        var d = new Dictionary<string, string>
        {
            ["package.json"] = PackageJson,
            ["farm.js"] = new JavaScriptLanguage().ApiStubFile(out _),
        };
        if (check) d["jf-check.mjs"] = CheckScript;
        return d;
    }

    public override Toolchain Conformance => new Toolchain
    {
        Exe = "node",
        SkipHint = "install node 20+ (https://nodejs.org)",
        ExtraFiles = _ => Files(true),
        Args = ctx => "jf-check.mjs farm.js " + string.Join(" ", ctx.Sources.Keys.Select(ctx.Quote)),
    };

    public override Toolchain Runner => new Toolchain
    {
        Exe = "node",
        SkipHint = "install node 20+ (https://nodejs.org)",
        ExtraFiles = _ => Files(false),
        // import declarations are hoisted, so farm.js runs before the program even when the program imports others
        PrepareSource = (src, name) => "import \"./farm.js\";\n" + src,
        Args = ctx => ctx.Quote(ctx.MainFile),
    };

    public override string PythonPrelude => JsPrelude.Text;
}

/// <summary>CPython prelude that prints values the way node's console.log does (and str() like JavaScript's String()).</summary>
public static class JsPrelude
{
    public const string Text = @"import builtins as __b
def __num(v):
    v = float(v)
    if v != v:
        return 'NaN'
    if v == float('inf'):
        return 'Infinity'
    if v == float('-inf'):
        return '-Infinity'
    if v == 0:
        return '0'
    if v == int(v) and abs(v) < 1e21:
        return __b.str(int(v))
    r = repr(v)
    neg = r.startswith('-')
    if neg:
        r = r[1:]
    mant, _, ex = r.partition('e')
    ex = int(ex) if ex else 0
    ip, _, fp = mant.partition('.')
    allds = ip + fp
    stripped = allds.lstrip('0')
    point = len(ip) + ex - (len(allds) - len(stripped))
    ds = stripped.rstrip('0')
    k = len(ds)
    n = point
    if k <= n <= 21:
        s = ds + '0' * (n - k)
    elif 0 < n <= 21:
        s = ds[:n] + '.' + ds[n:]
    elif -6 < n <= 0:
        s = '0.' + '0' * (-n) + ds
    else:
        e = n - 1
        s = ds[0] + ('.' + ds[1:] if k > 1 else '') + 'e' + ('+' if e >= 0 else '-') + __b.str(abs(e))
    return ('-' if neg else '') + s
def __quote(s):
    q = ""'""
    if ""'"" in s:
        if '""' not in s:
            q = '""'
        elif '`' not in s and '${' not in s:
            q = '`'
    out = q
    for ch in s:
        if ch == '\n':
            out += '\\n'
        elif ch == '\t':
            out += '\\t'
        elif ch == '\\':
            out += '\\\\'
        elif ch == q:
            out += '\\' + ch
        else:
            out += ch
    return out + q
def __fmt(v, nested):
    if v is None:
        return 'null'
    if v is True:
        return 'true'
    if v is False:
        return 'false'
    if isinstance(v, (int, float)):
        return __num(v)
    if isinstance(v, __b.str):
        return __quote(v) if nested else v
    if isinstance(v, (__b.list, tuple)):
        if len(v) == 0:
            return '[]'
        return '[ ' + ', '.join(__fmt(x, True) for x in v) + ' ]'
    if isinstance(v, __b.dict):
        if len(v) == 0:
            return 'Map(0) {}'
        return 'Map(' + __b.str(len(v)) + ') { ' + ', '.join(__fmt(k, True) + ' => ' + __fmt(v[k], True) for k in v) + ' }'
    if isinstance(v, __b.set):
        if len(v) == 0:
            return 'Set(0) {}'
        return 'Set(' + __b.str(len(v)) + ') { ' + ', '.join(__fmt(x, True) for x in v) + ' }'
    if callable(v):
        n = getattr(v, '__name__', '')
        return '[Function (anonymous)]' if n.startswith('__j') else '[Function: ' + n + ']'
    return __b.str(v)
def __jsstr(v):
    if v is None:
        return 'null'
    if v is True:
        return 'true'
    if v is False:
        return 'false'
    if isinstance(v, (int, float)):
        return __num(v)
    if isinstance(v, __b.str):
        return v
    if isinstance(v, (__b.list, tuple)):
        return ','.join('' if x is None else __jsstr(x) for x in v)
    if isinstance(v, __b.dict):
        return '[object Map]'
    if isinstance(v, __b.set):
        return '[object Set]'
    return __b.str(v)
def print(*a):
    __b.print(' '.join(__fmt(x, False) for x in a))
def quick_print(*a):
    print(*a)
def str(*a):
    if len(a) == 0:
        return ''
    return __jsstr(a[0])
";
}
