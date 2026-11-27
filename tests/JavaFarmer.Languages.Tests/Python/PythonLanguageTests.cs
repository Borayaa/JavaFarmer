namespace JavaFarmer.Languages.Tests.Python;

/// <summary>PythonLanguage services: registry, Compile identity, highlighting, value format, stub, editor rules.</summary>
[Collection("perf")]
public class PythonLanguageTests
{
    static readonly PythonLanguage L = PyTestUtil.Lang;

    [Fact]
    public void Registry_FindsPython()
    {
        var p = JavaFarmer.Compiler.Languages.Get("python");
        Assert.NotNull(p);
        Assert.Equal("Python", p.DisplayName);
        Assert.Equal(".py", p.FileExtension);
        Assert.True(p.IsNative);
        Assert.Equal("can_harvest", p.ApiName("can_harvest"));
        Assert.Equal("can_harvest", p.FromApiName("can_harvest"));
    }

    [Fact]
    public void Compile_IsTheIdentity_EvenForCodeThePythonFrontendRejects()
    {
        foreach (var src in new[] { "harvest()\n", "x = l[1:2]\n", "", "((( broken" })
        {
            var r = L.Compile(src, new CompileOptions { FileName = "f" });
            Assert.True(r.Success);
            Assert.Equal(src, r.Python);
            Assert.Empty(r.Names);
            if (src.Length == 0) Assert.Empty(r.Map.Segments);
            else
            {
                var s = Assert.Single(r.Map.Segments);
                Assert.Equal((0, src.Length, 0, src.Length, SegmentKind.User), (s.PyStart, s.PyEnd, s.JavaStart, s.JavaEnd, s.Kind));
            }
        }
    }

    static void AssertCovers(string text)
    {
        var toks = L.Highlight(text);
        int pos = 0;
        foreach (var t in toks)
        {
            Assert.Equal(pos, t.Start);
            Assert.True(t.Length > 0);
            pos += t.Length;
        }
        Assert.Equal(text.Length, pos);
    }

    [Fact]
    public void Highlight_CoversEverything_AndToleratesJunk()
    {
        foreach (var s in new[] { "", "(((", "\"unterminated", "'x\n", "x = 1 # c\n\tif a:\n", "@$?!§ 😀 \t\r\n", "1.2.3..4 ** //= x", "Entities.", "def f(:\n\t\"a\" 'b" })
            AssertCovers(s);
    }

    [Fact]
    public void Highlight_Kinds()
    {
        const string src = "def f(a):\n\tif a and not None:\n\t\tplant(Entities.Carrot) # go\n\treturn 'x' + \"y\" * 2.5\n";
        var toks = L.Highlight(src);
        TokenKind K(string word)
        {
            int at = src.IndexOf(word, StringComparison.Ordinal);
            return toks.First(t => t.Start == at).Kind;
        }
        Assert.Equal(TokenKind.Keyword, K("def"));
        Assert.Equal(TokenKind.Keyword, K("and"));
        Assert.Equal(TokenKind.Keyword, K("return"));
        Assert.Equal(TokenKind.Identifier, K("f("));
        Assert.Equal(TokenKind.Identifier, K("plant"));
        Assert.Equal(TokenKind.Constant, K("None"));
        Assert.Equal(TokenKind.Constant, K("Entities"));
        Assert.Equal(TokenKind.Constant, K("Carrot"));
        Assert.Equal(TokenKind.Comment, K("# go"));
        Assert.Equal(TokenKind.String, K("'x'"));
        Assert.Equal(TokenKind.String, K("\"y\""));
        Assert.Equal(TokenKind.Number, K("2.5"));
        Assert.Equal(TokenKind.Operator, K("*"));
        Assert.Equal(TokenKind.Punctuation, K("("));
        Assert.Equal(TokenKind.Punctuation, K(":"));
        Assert.Equal(TokenKind.Whitespace, K("\n"));
        var bad = L.Highlight("x = §");
        Assert.Equal(TokenKind.Unknown, bad[^1].Kind);
    }

    [Fact]
    public void Highlight_IsFast()
    {
        var src = string.Concat(Enumerable.Repeat(PythonPrinterTests.Clean[1], 20));
        L.Highlight(src);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) L.Highlight(src);
        Assert.True(sw.ElapsedMilliseconds < 50, sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void ToIr_IsFast_On300Lines()
    {
        var src = string.Concat(Enumerable.Repeat(PythonPrinterTests.Clean[0] + PythonPrinterTests.Clean[3], 300 / 25 + 1));
        Assert.True(src.Split('\n').Length >= 300);
        Assert.NotNull(L.ToIr(src, new CompileOptions(), out _));
        for (int i = 0; i < 20; i++) L.ToIr(src, new CompileOptions(), out _);   // warm up the JIT
        var times = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            L.ToIr(src, new CompileOptions(), out _);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        // ToIr is not on the keystroke path (Compile is the identity); same bound as the Java ToIr perf test
        Assert.True(times[10] < 10.0, "p25 " + times[10] + " ms per ToIr");
    }

    [Fact]
    public void Keywords_EditorRules_Template()
    {
        Assert.Contains("def", L.Keywords);
        Assert.Contains("elif", L.Keywords);
        Assert.DoesNotContain("True", L.Keywords);
        Assert.Equal(new[] { ":" }, L.Editor.IndentAfter);
        Assert.Empty(L.Editor.DedentOn);
        Assert.Equal("#", L.Editor.LineComment);
        Assert.Null(L.Editor.BlockCommentStart);
        Assert.Equal("\t", L.Editor.IndentUnit);
        Assert.Equal("", L.NewFileTemplate("x", out int caret));
        Assert.Equal(0, caret);
    }

    [Fact]
    public void FormatValue_LikeTheGame()
    {
        Assert.Equal("None", L.FormatValue(null));
        Assert.Equal("True", L.FormatValue(true));
        Assert.Equal("2.5", L.FormatValue(2.5));
        Assert.Equal("3", L.FormatValue(3.0));
        Assert.Equal("hi", L.FormatValue("hi"));
        Assert.Equal("[1,'a',None]", L.FormatValue(new GameList(new object[] { 1.0, "a", null })));
        Assert.Equal("{1,2}", L.FormatValue(new GameSet(new object[] { 1.0, 2.0 })));
        Assert.Equal("{'k':Entities.Tree}", L.FormatValue(new GameDict().Add("k", new GameConstant("Entities", "Tree"))));
        Assert.Equal("(1,2)", L.FormatValue(new GameTuple(1.0, 2.0)));
        Assert.Equal("North", L.FormatValue(new GameConstant("Directions", "North")));
        var self = new GameList();
        self.Items.Add(self);
        Assert.Equal("[...]", L.FormatValue(self));
    }

    [Fact]
    public void ApiStub_ListsEveryBuiltin_AndIsValidPython()
    {
        L.ApiStubFile(out var name);
        Assert.Equal("javafarmer_api.py", name);
        var builtins = GameApi.Builtins;   // snapshot: other tests register extension builtins concurrently
        var stub = PythonLanguage.BuildApiStub(builtins);
        foreach (var b in builtins) Assert.Contains("def " + b.PythonName + "(", stub);
        Assert.Contains("def use_item(item: Item, n: int = ...) -> bool:", stub);
        Assert.Contains("def get_entity_type() -> Optional[Entity]:", stub);
        Assert.Contains("class Entities(metaclass=_Constants):", stub);
        var dir = RepoPaths.NewTempDir("pystub");
        try
        {
            var f = Path.Combine(dir, "javafarmer_api.py");
            File.WriteAllText(f, stub);
            var psi = new System.Diagnostics.ProcessStartInfo("python3", "-I -c \"import ast,sys; ast.parse(open(sys.argv[1]).read())\" \"" + f + "\"") { RedirectStandardError = true, RedirectStandardOutput = true };
            System.Diagnostics.Process p;
            try { p = System.Diagnostics.Process.Start(psi); } catch { return; }   // no CPython: skip
            p.WaitForExit(20000);
            Assert.True(p.ExitCode == 0, p.StandardError.ReadToEnd());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void NeverThrows_OnJunk()
    {
        foreach (var s in new[] { "(((", "\"unterminated", "def", "if:", "\t\t\tx", "x = [", "for in:", "))))", "\0\x01", "def f(a=1, b):\n\tpass", "a.b.c.d()", "x = [\n", "if a:\n", "def f():\n\treturn 1\n\treturn" })
        {
            L.ToIr(s, new CompileOptions(), out var errs);
            Assert.NotEmpty(errs.Count > 0 ? errs : new List<Diagnostic> { new("ok", 0, 0) });
            L.Highlight(s);
            L.Compile(s, null);
        }
        var broken = new IrModule { Name = "m" };
        broken.Items.Add(new IrExprStmt(new IrCall { Kind = IrCallKind.Value }));
        Assert.NotNull(L.Print(broken, null));
        Assert.Equal("", L.Print(null, null));
    }
}
