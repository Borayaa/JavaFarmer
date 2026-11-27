namespace JavaFarmer.Languages.Tests.Rust;

/// <summary>docs/LANGUAGE-GUIDE.md `9: Rust files using files of other languages (and being used by them).</summary>
public class RustCrossLanguageTests
{
    static readonly ILanguage Rs = Compiler.Languages.Get("rust");
    static readonly ILanguage Java = Compiler.Languages.Get("java");

    static CompileOptions Opts(string helperSrc, string helperLang, string name = "main") => new()
    {
        FileName = name,
        OtherFiles = new Dictionary<string, string> { ["helper"] = helperSrc },
        OtherFileLanguages = new Dictionary<string, string> { ["helper"] = helperLang },
    };

    const string JavaHelper = "public class helper {\n\tstatic final int LIMIT = 3;\n\n\tstatic int twice(int n) {\n\t\treturn n * 2;\n\t}\n\n\tstatic Entity crop() {\n\t\treturn Entities.Carrot;\n\t}\n}\n";
    const string PyHelper = "SIZE = 5\n\ndef bump(n):\n    return n + 1\n\ndef shout(text):\n    print(text)\n";
    const string RustHelper = "use farm::*;\n\npub const LIMIT: i32 = 3;\n\npub fn twice(n: i32) -> i32 {\n\tn * 2\n}\n\nfn hidden() -> bool {\n\ttrue\n}\n";

    static CompileResult Ok(ILanguage lang, string src, CompileOptions o)
    {
        ForeignModules.ClearCache();
        var r = lang.Compile(src, o);
        Assert.True(r.Success, string.Join("; ", r.Errors.Select(e => e + " [" + HarnessTests.Slice(src, e.Start, e.End) + "]")));
        Assert.Null(GameOracle.TryParse(r.Python));
        return r;
    }

    static Diagnostic Fail(string src, CompileOptions o)
    {
        ForeignModules.ClearCache();
        var r = Rs.Compile(src, o);
        Assert.False(r.Success, "expected an error:\n" + r.Python);
        return r.Errors[0];
    }

    [Fact]
    public void Rust_CallsAJavaFile()
    {
        var src = "use farm::*;\nmod helper;\n\nfn main() {\n\tlet n: i32 = helper::twice(4) + helper::LIMIT;\n\tplant(helper::crop());\n\tquick_print!(\"{}\", n);\n}\n";
        var r = Ok(Rs, src, Opts(JavaHelper, "java"));
        Assert.Contains("import helper", r.Python);
        Assert.Contains("helper.twice(4) + helper.LIMIT", r.Python);
        // the Java types are checked
        var e = Fail("use farm::*;\nmod helper;\n\nfn main() {\n\tlet n = helper::twice(1.5);\n}\n", Opts(JavaHelper, "java"));
        Assert.Contains("(E0308)", e.Message);
        Assert.Contains("(E0061)", Fail("use farm::*;\nmod helper;\n\nfn main() {\n\thelper::twice();\n}\n", Opts(JavaHelper, "java")).Message);
    }

    [Fact]
    public void Rust_CallsAPythonFile_UntypedValuesAreAccepted()
    {
        var src = "use farm::*;\nmod helper;\nuse helper::bump;\n\nfn main() {\n\tlet x: i32 = bump(2);\n\tlet s = helper::SIZE;\n\thelper::shout(\"hi\");\n\tquick_print!(\"{} {}\", x, s);\n}\n";
        var r = Ok(Rs, src, Opts(PyHelper, "python"));
        Assert.Contains("helper.bump(2)", r.Python);
        Assert.Contains("helper.SIZE", r.Python);
        Assert.Contains("helper.shout(\"hi\")", r.Python);
    }

    [Fact]
    public void Rust_ReportsAForeignFileThatDoesNotCompile_AtTheUseSite()
    {
        var src = "use farm::*;\nmod helper;\n\nfn main() {\n\thelper::twice(4);\n}\n";
        var e = Fail(src, Opts("public class helper {\n\tstatic int twice(int n) {\n\t\treturn n *;\n\t}\n}\n", "java"));
        Assert.Contains("cannot use `helper`: helper (Java) does not compile", e.Message);
        Assert.Equal("twice", HarnessTests.Slice(src, e.Start, e.End));
        var e2 = Fail("use farm::*;\nmod helper;\nuse helper::*;\n\nfn main() {\n}\n", Opts("def broken(:\n    pass\n", "python"));
        Assert.Contains("cannot use `helper`", e2.Message);
        // a Rust file with a syntax error, same wording
        var e3 = Fail(src, Opts("pub fn twice(n: i32) -> i32 {\n\tn *\n", "rust"));
        Assert.Contains("cannot use `helper`: helper (Rust) does not compile (line ", e3.Message);
    }

    [Fact]
    public void Java_CallsARustFile()
    {
        var r = Ok(Java, "void main() {\n\tSystem.out.println(helper.twice(4) + helper.LIMIT);\n}\n", new CompileOptions
        {
            FileName = "Main",
            OtherFiles = new Dictionary<string, string> { ["helper"] = RustHelper },
            OtherFileLanguages = new Dictionary<string, string> { ["helper"] = "rust" },
        });
        Assert.Contains("helper.twice(4)", r.Python);
        Assert.Contains("helper.LIMIT", r.Python);
    }

    [Fact]
    public void Signatures_AreDeclarationsOnly()
    {
        var sig = ((ISignatureProvider)Rs).Signatures(RustHelper + "\nfn main() {\n\tlet x: i32 = \"not checked\";\n}\n", new CompileOptions { FileName = "helper" });
        Assert.True(sig.Ok, sig.Error);
        var twice = sig.Function("twice");
        Assert.NotNull(twice);
        Assert.Equal(IrTypeKind.Int, twice.ReturnType.Kind);
        Assert.Equal(IrTypeKind.Int, twice.Params[0].Type.Kind);
        Assert.True(sig.Function("hidden").IsPrivate);
        Assert.True(sig.Function("main").IsEntry);
        Assert.True(sig.Global("LIMIT").IsConst);
        var bad = ((ISignatureProvider)Rs).Signatures("fn f( {", new CompileOptions { FileName = "helper" });
        Assert.False(bad.Ok);
        Assert.Contains("helper (Rust) does not compile (line 1:", bad.Error);
    }

    [Fact]
    public void SameLanguageModules_KeepRustPrivacy()
    {
        var e = Fail("use farm::*;\nmod helper;\n\nfn main() {\n\thelper::hidden();\n}\n", Opts(RustHelper, "rust"));
        Assert.Contains("(E0603)", e.Message);
    }
}
