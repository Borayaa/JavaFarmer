namespace JavaFarmer.Languages.Tests;

/// <summary>
/// Python (the game's native language): Compile is the identity, so (b) checks the corpus with the game parser,
/// (c) checks it with CPython's parser, (e) checks Print(ToIr(x)) against x, and invalid files test ToIr errors.
/// No differential run (it would run the same code twice).
/// </summary>
public sealed class PythonHooks : LanguageHooks
{
    public override string Id => "python";
    public override string Extension => ".py";

    public override Toolchain Conformance => new Toolchain
    {
        Exe = "python3",
        SkipHint = "install CPython 3",
        // game strings have no escapes: make them raw before CPython parses them
        PrepareSource = (src, name) => Normalise.RawStrings(src),
        Args = ctx => "-I -c \"import ast,sys; [ast.parse(open(f, encoding='utf-8').read(), f) for f in sys.argv[1:]]\" " + string.Join(" ", ctx.Sources.Keys.Select(ctx.Quote)),
    };
}
