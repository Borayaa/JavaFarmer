using System.Globalization;
using System.Text;

namespace JavaFarmer.Languages.Tests.Python;

/// <summary>Helpers for the Python frontend / printer tests.</summary>
public static class PyTestUtil
{
    public static readonly PythonLanguage Lang = new();

    public static IrModule ToIr(string src, string file = "main")
    {
        var m = Lang.ToIr(src, new CompileOptions { FileName = file }, out var errs);
        Assert.True(errs.Count == 0, "unexpected errors: " + string.Join("; ", errs.Select(e => e + " [" + Snip(src, e) + "]")));
        Assert.NotNull(m);
        return m;
    }

    public static Diagnostic Error(string src)
    {
        var m = Lang.ToIr(src, new CompileOptions { FileName = "main" }, out var errs);
        Assert.Null(m);
        Assert.NotEmpty(errs);
        return errs[0];
    }

    public static string Snip(string src, Diagnostic d) =>
        d.Start >= 0 && d.End <= src.Length && d.End >= d.Start ? src.Substring(d.Start, d.End - d.Start) : "?";

    public static string Print(IrModule m, PrintOptions o = null) => Lang.Print(m, o ?? new PrintOptions());

    public static string RoundTrip(string src) => Print(ToIr(src));

    /// <summary>Structural dump of a module: node kinds, operators, values, symbols (name:kind), comments; no spans.</summary>
    public static string Dump(IrNode n, bool types = false)
    {
        var sb = new StringBuilder();
        Dump(n, sb, 0, types);
        return sb.ToString();
    }

    static string Sym(IrSymbol s) => s == null ? "null" : (s.Module != null ? s.Module + "." : "") + s.Name + ":" + s.Kind;

    static void Dump(IrNode n, StringBuilder sb, int d, bool types)
    {
        if (n == null) { sb.Append(' ', d * 2).Append("null\n"); return; }
        sb.Append(' ', d * 2).Append(n.GetType().Name.Substring(2));
        switch (n)
        {
            case IrLiteral l: sb.Append(' ').Append(l.Kind).Append('=').Append(l.Kind == IrLitKind.String ? "'" + l.Str + "'" : l.Kind == IrLitKind.Float ? l.Float.ToString("R", CultureInfo.InvariantCulture) : l.Kind == IrLitKind.Int ? l.Int.ToString(CultureInfo.InvariantCulture) : l.Bool.ToString()); break;
            case IrName nm: sb.Append(' ').Append(Sym(nm.Symbol)); break;
            case IrConst c: sb.Append(' ').Append(c.Category).Append('.').Append(c.Name); break;
            case IrBinary b: sb.Append(' ').Append(b.Op); break;
            case IrUnary u: sb.Append(' ').Append(u.Op); break;
            case IrCall c: sb.Append(' ').Append(c.Kind).Append(' ').Append(c.Builtin ?? (c.Function != null ? Sym(c.Function) : "")); break;
            case IrOpCall o: sb.Append(' ').Append(o.Op); break;
            case IrFuncRef f: sb.Append(' ').Append(f.Builtin ?? Sym(f.Function)); break;
            case IrCast c: sb.Append(' ').Append(c.Target); break;
            case IrAssign a: sb.Append(' ').Append(a.Op?.ToString() ?? "="); break;
            case IrVarDecl v: sb.Append(' ').Append(Sym(v.Symbol)); break;
            case IrGlobal g: sb.Append(' ').Append(Sym(g.Symbol)).Append(g.ImplicitInit ? " implicit" : ""); break;
            case IrFunction f: sb.Append(' ').Append(Sym(f.Symbol)); break;
            case IrParam p: sb.Append(' ').Append(Sym(p.Symbol)); break;
            case IrRangeFor r: sb.Append(' ').Append(Sym(r.Var)).Append(r.Start == null ? " from0" : "").Append(r.Step == null ? " step1" : ""); break;
            case IrForEach fe: sb.Append(' ').Append(fe.Var?.Tag != null ? "<tuple>" : Sym(fe.Var)); break;
            case IrImport im: sb.Append(' ').Append(im.Module).Append(im.ImportAll ? " *" : "").Append(im.Names != null ? " [" + string.Join(",", im.Names) + "]" : ""); break;
            case IrIf i: sb.Append(i.Else is IrIf ? " elif" : i.Else != null ? " else" : ""); break;
        }
        if (n.Synthetic) sb.Append(" synthetic");
        if (types && n is IrExpr e) sb.Append(" : ").Append(e.Type);
        if (n.Leading != null) foreach (var c in n.Leading) sb.Append(" #<").Append(c.Text.Trim()).Append('>');
        if (n.Trailing != null) sb.Append(" #>").Append(n.Trailing.Text.Trim());
        sb.Append('\n');
        if (n is IrBlock blk && blk.TrailingComments != null) foreach (var c in blk.TrailingComments) sb.Append(' ', d * 2 + 2).Append("#end ").Append(c.Text.Trim()).Append('\n');
        foreach (var ch in IrWalk.Children(n)) Dump(ch, sb, d + 1, types);
        if (n is IrModule m && m.TrailingComments != null) foreach (var c in m.TrailingComments) sb.Append("#end ").Append(c.Text.Trim()).Append('\n');
    }
}
