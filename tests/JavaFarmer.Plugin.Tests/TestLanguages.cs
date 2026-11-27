using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

/// <summary>Corpus programs of every registered language (tests/corpus/&lt;id&gt;/{valid,diff}; a sub-folder = a multi-file program).</summary>
public static class TestCorpus
{
    public sealed record Program(string Language, string Name, IReadOnlyDictionary<string, string> Files);

    public static IEnumerable<Program> Programs(ILanguage lang)
    {
        foreach (var kind in new[] { "valid", "diff" })
        {
            string dir = Path.Combine(Repo.Corpus(lang.Id), kind);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!f.EndsWith(lang.FileExtension, StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(f);
                yield return new Program(lang.Id, kind + "/" + Path.GetFileName(f), new Dictionary<string, string> { { name, File.ReadAllText(f).Replace("\r", "") } });
            }
            foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.Ordinal))
            {
                var files = Directory.GetFiles(d).Where(f => f.EndsWith(lang.FileExtension, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => File.ReadAllText(f).Replace("\r", ""));
                if (files.Count > 0) yield return new Program(lang.Id, kind + "/" + Path.GetFileName(d) + "/", files);
            }
        }
    }

    /// <summary>(from, to) pairs of registered languages where "from" has corpus programs.</summary>
    public static IEnumerable<object[]> Pairs()
    {
        foreach (var from in Languages.All)
        {
            if (!Programs(from).Any()) continue;
            foreach (var to in Languages.All)
                if (to.Id != from.Id) yield return new object[] { from.Id, to.Id };
        }
    }

    public static IEnumerable<object[]> LanguageIds() => Languages.All.Select(l => new object[] { l.Id });
}

/// <summary>A minimal language for editor-service tests (never registered in Languages).</summary>
public sealed class FakeLanguage : LanguageBase
{
    public override string Id => "fake-test";
    public override string DisplayName => "Fake";
    public override string FileExtension => ".fake";
    protected override NamingStyle ApiNaming => NamingStyle.Camel;
    protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors) { errors.Add(new Diagnostic("no", 0, 0)); return null; }
    protected override string PrintImpl(IrModule module, PrintOptions options) => "";
    public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source) =>
        string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, TokenKind.Identifier) };
    public override IReadOnlyCollection<string> Keywords { get; } = new[] { "fn", "let", "mut", "while", "loop", "if", "else", "match", "return", "true", "false", "null", "for", "in" };
    public override EditorRules Editor { get; } = new EditorRules { LineComment = "#", BlockCommentStart = null, BlockCommentEnd = null };
    public override string NewFileTemplate(string fileName, out int caret) { caret = 0; return "fn main() {\n}"; }
    public override string ApiStubFile(out string fileName) { fileName = "farm.fake"; return "// fake\n"; }
}
