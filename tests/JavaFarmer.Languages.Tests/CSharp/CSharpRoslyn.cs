using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JavaFarmer.Languages.Tests.CSharp;

/// <summary>
/// The real C# compiler, in-process: Roslyn (C# 12, the .NET 8 reference assemblies of the test host) compiles the
/// sources together with the generated Farm.cs stub (CSharpLanguage.BuildStub: the API plus the constant members the
/// sources use). Run executes the entry point with Console redirected (pure-computation programs only).
/// </summary>
public static class CSharpRoslyn
{
    static readonly object gate = new();
    static List<MetadataReference> refs;

    static List<MetadataReference> References()
    {
        lock (gate)
        {
            if (refs != null) return refs;
            var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator);
            refs = tpa.Where(p => p.Length > 0 && File.Exists(p)).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            return refs;
        }
    }

    public sealed class Result
    {
        public bool Success;
        public List<string> Errors = new();
        public byte[] Assembly;
        public override string ToString() => string.Join("\n", Errors);
    }

    /// <summary>Compiles file name → source (plus Farm.cs). Warnings are allowed, errors fail.</summary>
    public static Result Compile(IReadOnlyDictionary<string, string> sources, bool emit = false)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12);
        var stub = CSharpLanguage.BuildStub(sources.Values);
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(stub, parse, path: "Farm.cs") };
        foreach (var kv in sources) trees.Add(CSharpSyntaxTree.ParseText(kv.Value, parse, path: kv.Key));
        bool entry = trees.Skip(1).Any(t => t.GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any()
            || t.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "Main"));
        var opts = new CSharpCompilationOptions(entry ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);
        var comp = CSharpCompilation.Create("prog" + Guid.NewGuid().ToString("N")[..8], trees, References(), opts);
        var res = new Result();
        if (emit)
        {
            using var ms = new MemoryStream();
            var er = comp.Emit(ms);
            res.Errors = er.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
            res.Success = er.Success;
            if (er.Success) res.Assembly = ms.ToArray();
        }
        else
        {
            res.Errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
            res.Success = res.Errors.Count == 0;
        }
        return res;
    }

    /// <summary>Compiles and runs the program; returns what it printed.</summary>
    public static string Run(IReadOnlyDictionary<string, string> sources)
    {
        var r = Compile(sources, emit: true);
        if (!r.Success) throw new InvalidOperationException("Roslyn rejected the program:\n" + r);
        var asm = System.Reflection.Assembly.Load(r.Assembly);
        var ep = asm.EntryPoint ?? throw new InvalidOperationException("no entry point");
        lock (gate)
        {
            var old = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try { ep.Invoke(null, ep.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() }); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
            finally { Console.SetOut(old); }
            return sw.ToString();
        }
    }
}
