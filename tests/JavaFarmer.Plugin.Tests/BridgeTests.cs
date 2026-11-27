using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

[Collection("Game")]
public class BridgeTests
{
    private const string SimpleJava = "void main() {\n    harvest();\n}\n";

    private static CompileResult SimpleResult() => new PyBuilder(SimpleJava)
        .Add("def main():", "void main()", SegmentKind.Synthetic)
        .Raw("\n\t")
        .Add("harvest()", "harvest()")
        .Raw("\n")
        .Add("if __name__ == \"__main__\":\n\tmain()", "void main()", SegmentKind.Synthetic, rewind: true)
        .Build();

    [Fact]
    public void HandWrittenResult_ParsesWithGameParser_AndSpansPointIntoJava()
    {
        var cw = TestGame.Window();
        var program = JavaParseBridge.ParseCompiled(SimpleJava, SimpleResult(), cw);
        Assert.NotNull(program);
        var nodes = TestGame.Nodes(program);
        foreach (var n in nodes)
        {
            if (n.boxedParams.wordStart < 0) continue;
            Assert.InRange(n.boxedParams.wordStart, 0, SimpleJava.Length);
            Assert.InRange(n.boxedParams.wordEnd, n.boxedParams.wordStart, SimpleJava.Length);
        }
        var calls = nodes.OfType<CallNode>().ToList();
        var harvest = calls.Single(c => TestGame.JavaText(SimpleJava, c) == "harvest()");
        Assert.Null(DependencyPolicies.Get(harvest.boxedParams));

        // the synthetic main() call of the __name__ guard is free
        var mainCall = calls.Single(c => !ReferenceEquals(c, harvest));
        var mainPolicy = DependencyPolicies.Get(mainCall.boxedParams);
        Assert.NotNull(mainPolicy);
        Assert.True(mainPolicy.Skip);
        Assert.True(mainPolicy.IsCall);
        Assert.Equal("void main()", TestGame.JavaText(SimpleJava, mainCall));

        var def = nodes.OfType<DefNode>().Single();
        Assert.True(DependencyPolicies.Get(def.boxedParams).Skip);
        Assert.StartsWith("void", TestGame.JavaText(SimpleJava, def));
        Assert.True(DependencyPolicies.Get(nodes.OfType<BranchNode>().Single().boxedParams).Skip);
        Assert.True(DependencyPolicies.Get(nodes.OfType<ComparisonNode>().Single().boxedParams).Skip);

        // the function is registered in the window like a vanilla parse would
        Assert.True(cw.parsedFunctions.ContainsKey("main"));
    }

    [Fact]
    public void JavaStructure_CostsNoUnlocks_ButVanillaPythonWould()
    {
        var program = JavaParseBridge.ParseCompiled(SimpleJava, SimpleResult(), TestGame.Window());
        Assert.Empty(TestGame.CheckedDependencies(program));

        // same Python without policies (plain game parse) checks functions/if/operators
        var vanilla = JavaParseBridge.RunGameParser(SimpleResult().Python, TestGame.Window());
        var deps = TestGame.CheckedDependencies(vanilla);
        Assert.Contains("functions", deps);
        Assert.Contains("if", deps);
        Assert.Contains("operators", deps);
    }

    private const string ForJava = "void main() {\n    for (int i = 0; i < 3; i++) {\n        harvest();\n    }\n}\n";

    private static CompileResult ForResult() => new PyBuilder(ForJava)
        .Add("def main():", "void main()", SegmentKind.Synthetic)
        .Raw("\n\t")
        .Add("i = 0", "int i = 0", SegmentKind.Structural)
        .Raw("\n\t")
        .Add("while", "for", SegmentKind.Structural, "for", rewind: true)
        .Raw(" ")
        .Add("i < 3", "i < 3", SegmentKind.Structural)
        .Raw(":\n\t\t")
        .Add("harvest()", "harvest()")
        .Raw("\n\t\t")
        .Add("i += 1", "i++", SegmentKind.Structural, rewind: true)
        .Raw("\n")
        .Add("if __name__ == \"__main__\":\n\tmain()", "void main()", SegmentKind.Synthetic, rewind: true)
        .Build();

    [Fact]
    public void ClassicFor_LoweredToWhile_ChecksForInsteadOfWhileVariablesOperators()
    {
        var program = JavaParseBridge.ParseCompiled(ForJava, ForResult(), TestGame.Window());
        var nodes = TestGame.Nodes(program);
        var loop = nodes.OfType<BranchNode>().Single(b => DependencyPolicies.Get(b.boxedParams)?.Override != null);
        Assert.Equal("for", DependencyPolicies.Get(loop.boxedParams).Override);
        Assert.Equal("for", TestGame.JavaText(ForJava, loop).Substring(0, 3));
        foreach (var a in nodes.OfType<AssignmentNode>())
            Assert.True(DependencyPolicies.Get(a.boxedParams).Skip);

        var deps = TestGame.CheckedDependencies(program);
        Assert.Equal(new[] { "for" }, deps.ToArray());

        var vanillaDeps = TestGame.CheckedDependencies(JavaParseBridge.RunGameParser(ForResult().Python, TestGame.Window()));
        Assert.Contains("while", vanillaDeps);
        Assert.Contains("variables", vanillaDeps);
    }

    [Fact]
    public void OverrideDependency_KeepsJavaSpan()
    {
        var program = JavaParseBridge.ParseCompiled(ForJava, ForResult(), TestGame.Window());
        var st = TestGame.State();
        foreach (var n in TestGame.Nodes(program))
            if (DependencyPolicies.BeforeCheck(n.boxedParams, st.currentDependencies)) n.CheckDependencies(st, null);
        var (name, s, e) = st.currentDependencies.Single();
        Assert.Equal("for", name);
        Assert.Equal(ForJava.IndexOf("for", StringComparison.Ordinal), s);
    }

    [Fact]
    public void Breakpoints_FollowJavaOrder_NotPythonOrder()
    {
        // Java: main first, helper second. Python: helper def first, main last.
        const string java = "void main() {\n    go();\n}\nvoid go() {\n    harvest();\n}\n";
        var r = new PyBuilder(java)
            .Add("def go():", "void go()", SegmentKind.User)
            .Raw("\n\t")
            .Add("harvest()", "harvest()")
            .Raw("\n")
            .Add("def main():", "void main()", SegmentKind.Synthetic, rewind: true)
            .Raw("\n\t")
            .Add("go()", "go()", rewind: true)
            .Build();
        var program = JavaParseBridge.ParseCompiled(java, r, TestGame.Window());
        Assert.True(JavaParseBridge.IsJavaTree(program.syntaxTree));
        int line2 = java.IndexOf("    go();", StringComparison.Ordinal);
        var positions = new HashSet<int> { line2 };
        JavaParseBridge.InsertBreakpoints(program.syntaxTree, positions);
        var marked = TestGame.Nodes(program).Where(n => n.boxedParams.isBreakpoint).ToList();
        var node = Assert.Single(marked);
        Assert.Equal("go()", TestGame.JavaText(java, node));
        Assert.Empty(positions);
        Assert.False(JavaParseBridge.IsJavaTree(JavaParseBridge.RunGameParser("x = 1", TestGame.Window()).syntaxTree));
    }

    [Fact]
    public void GameParserRejection_BecomesInternalErrorAtJavaPosition()
    {
        const string java = "void main() {\n    harvest();\n}";
        // over-indented second statement: the game throws a raw-key ParseException (no localisation needed headless)
        var bad = new PyBuilder(java)
            .Add("def main():", "void main()", SegmentKind.Synthetic)
            .Raw("\n\t")
            .Add("harvest()", "harvest()")
            .Raw("\n\t\t")
            .Add("harvest()", "harvest()", rewind: true)
            .Build();
        var ex = Assert.Throws<ParseException>(() => JavaParseBridge.ParseCompiled(java, bad, TestGame.Window()));
        Assert.Contains("internal JavaFarmer error", ex.Message);
        Assert.InRange(ex.startIndex, 0, java.Length - 1);
        Assert.InRange(ex.endIndex, ex.startIndex, java.Length);
    }

    [Fact]
    public void CompileError_IsThrownAsParseExceptionWithJavaSpan()
    {
        const string java = "void main() {\n    harvest(\n}";
        var ex = Assert.Throws<ParseException>(() => JavaParseBridge.Parse(java, TestGame.Window()));
        Assert.False(string.IsNullOrEmpty(ex.Message));
        Assert.InRange(ex.startIndex, 0, java.Length - 1);
        Assert.InRange(ex.endIndex, ex.startIndex, java.Length);
    }

    [Fact]
    public void EmptyFile_IsAnEmptyProgram()
    {
        var p = JavaParseBridge.Parse("  \n\t\n", TestGame.Window());
        Assert.NotNull(p);
        Assert.Empty(p.syntaxTree.slots);
    }

    [Fact]
    public void HelperCode_MapsToEmptySpanAtZero_AndIsFree()
    {
        const string java = "void main() {\n    int x = 7 / 2;\n}";
        var r = new PyBuilder(java)
            .Add("def __jdiv(a, b):\n\treturn a // b\n", null, SegmentKind.Synthetic)
            .Add("def main():", "void main()", SegmentKind.Synthetic)
            .Raw("\n\t")
            .Add("x = ", "int x = ")
            .Add("__jdiv(", "7 / 2", SegmentKind.Synthetic, rewind: true)
            .Add("7", "7", rewind: true)
            .Add(", ", "/", SegmentKind.Synthetic, rewind: true)
            .Add("2", "2", rewind: true)
            .Add(")", "7 / 2", SegmentKind.Synthetic, rewind: true)
            .Build();
        var program = JavaParseBridge.ParseCompiled(java, r, TestGame.Window());
        var nodes = TestGame.Nodes(program);
        var helperDef = nodes.OfType<DefNode>().First(d => d.boxedParams.wordStart == 0 && d.boxedParams.wordEnd == 0);
        Assert.True(DependencyPolicies.Get(helperDef.boxedParams).Skip);
        // the helper's "a // b" costs nothing; the user assignment still needs "variables"
        var deps = TestGame.CheckedDependencies(program);
        Assert.Equal(new[] { "variables" }, deps.ToArray());
        var call = nodes.OfType<CallNode>().Single();
        Assert.True(DependencyPolicies.Get(call.boxedParams).Skip);
        Assert.Equal("7 / 2", TestGame.JavaText(java, call));
    }

    [Fact]
    public void SegmentIndex_InnermostWins_GapsUsePreceding()
    {
        var map = new SourceMap(new List<Segment>
        {
            new() { PyStart = 0, PyEnd = 10, JavaStart = 0, JavaEnd = 5, Kind = SegmentKind.Synthetic },
            new() { PyStart = 4, PyEnd = 6, JavaStart = 2, JavaEnd = 3, Kind = SegmentKind.User },
            new() { PyStart = 12, PyEnd = 14, JavaStart = 7, JavaEnd = 9, Kind = SegmentKind.User },
        });
        var idx = new JavaParseBridge.SegmentIndex(map);
        Assert.Equal(SegmentKind.Synthetic, idx.Find(0).Kind);
        Assert.Equal(SegmentKind.User, idx.Find(5).Kind);
        Assert.Equal(SegmentKind.Synthetic, idx.Find(6).Kind);
        Assert.Same(map.Segments[0], idx.Find(11));   // gap -> preceding
        Assert.Same(map.Segments[2], idx.Find(13));
        Assert.Same(map.Segments[2], idx.Find(99));   // past the end
        Assert.Null(new JavaParseBridge.SegmentIndex(null).Find(3));
    }

    [Fact]
    public void MapSpan_UsesStartAndEndSegments_AndClamps()
    {
        var map = new SourceMap(new List<Segment>
        {
            new() { PyStart = 0, PyEnd = 4, JavaStart = 10, JavaEnd = 14 },
            new() { PyStart = 4, PyEnd = 8, JavaStart = 20, JavaEnd = 30 },
            new() { PyStart = 8, PyEnd = 9, JavaStart = -1, JavaEnd = -1, Kind = SegmentKind.Synthetic },
        });
        Assert.Equal((10, 30), JavaParseBridge.MapSpan(map, 1, 7, 100));
        Assert.Equal((10, 14), JavaParseBridge.MapSpan(map, 1, 9, 100));   // end in helper code -> start segment end
        Assert.Equal((0, 0), JavaParseBridge.MapSpan(map, 8, 9, 100));     // helper code
        Assert.Equal((10, 12), JavaParseBridge.MapSpan(map, 0, 7, 12));    // clamped to Java length
    }

    [Fact]
    public void CallWrapper_RemovesOrRenamesOnlyTheCallsOwnDependency()
    {
        var bp = new BoxedNodeParams { wordStart = 5, wordEnd = 9 };
        var deps = new List<(string, int, int)>();
        IEnumerable<double> Inner()
        {
            deps.Add(("operators", 5, 9));   // nested node with the same span: untouched
            deps.Add(("len", 1, 2));         // other node: untouched
            yield return 1;
            deps.Add(("str", 5, 9));         // the call itself
            yield return 2;
        }
        var skipped = DependencyPolicies.WrapCall(Inner(), bp, () => deps, new NodePolicy { Skip = true, IsCall = true }).ToList();
        Assert.Equal(new[] { 1.0, 2.0 }, skipped);
        Assert.Equal(new[] { "operators", "len" }, deps.Select(d => d.Item1).ToArray());

        deps.Clear();
        DependencyPolicies.WrapCall(Inner(), bp, () => deps, new NodePolicy { Override = "for", IsCall = true }).ToList();
        Assert.Equal(new[] { "operators", "len", "for" }, deps.Select(d => d.Item1).ToArray());
    }

    [Fact]
    public void CallWrapper_FixesUpEvenWhenTheCallThrows()
    {
        var bp = new BoxedNodeParams { wordStart = 3, wordEnd = 4 };
        var deps = new List<(string, int, int)>();
        IEnumerable<double> Inner()
        {
            deps.Add(("print", 3, 4));
            throw new ExecuteException("boom");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        Assert.Throws<ExecuteException>(() => DependencyPolicies.WrapCall(Inner(), bp, () => deps, new NodePolicy { Skip = true }).ToList());
        Assert.Empty(deps);
    }

    // ------------------------------------------------------------------ end to end with the real compiler

    private static void SkipIfCompilerStub()
    {
        var r = JavaCompiler.Compile("void main() {\n}\n", new CompileOptions { FileName = "main" });
        Skip.If(!r.Success && r.Errors.Count > 0 && r.Errors[0].Message.Contains("compiler not implemented"),
            "JavaCompiler is still the stub");
    }

    [SkippableFact]
    public void EndToEnd_RealCompiler_WhileLoop()
    {
        SkipIfCompilerStub();
        const string java = "void main() {\n    while (true) {\n        harvest();\n    }\n}\n";
        var cw = TestGame.Window();
        var program = JavaParseBridge.Parse(java, cw);
        var nodes = TestGame.Nodes(program);
        foreach (var n in nodes.Where(n => n.boxedParams.wordStart >= 0))
            Assert.InRange(n.boxedParams.wordEnd, n.boxedParams.wordStart, java.Length);
        var harvest = nodes.OfType<CallNode>().Single(c => TestGame.JavaText(java, c).StartsWith("harvest"));
        Assert.Null(DependencyPolicies.Get(harvest.boxedParams));
        var deps = TestGame.CheckedDependencies(program);
        Assert.Contains("while", deps);
        Assert.DoesNotContain("functions", deps);
        Assert.DoesNotContain("if", deps);
        Assert.DoesNotContain("operators", deps);
    }

    [SkippableFact]
    public void EndToEnd_RealCompiler_ClassicForNeedsFor()
    {
        SkipIfCompilerStub();
        const string java = "void main() {\n    for (int i = 0; i < getWorldSize(); i++) {\n        move(North);\n    }\n}\n";
        var program = JavaParseBridge.Parse(java, TestGame.Window());
        var deps = TestGame.CheckedDependencies(program);
        Assert.Contains("for", deps);
        Assert.DoesNotContain("while", deps);
        Assert.DoesNotContain("functions", deps);
    }

    [SkippableFact]
    public void EndToEnd_RealCompiler_StringConcatStrIsFree()
    {
        SkipIfCompilerStub();
        const string java = "void main() {\n    String s = \"a\" + 1;\n    quickPrint(s);\n}\n";
        var program = JavaParseBridge.Parse(java, TestGame.Window());
        var calls = TestGame.Nodes(program).OfType<CallNode>().ToList();
        // str(...) added for the concatenation is synthetic: its CallNode is wrapped and its "str" dependency removed
        var strCall = calls.Single(c => DependencyPolicies.Get(c.boxedParams)?.Skip == true && TestGame.JavaText(java, c) == "1");
        var deps = new List<(string, int, int)>();
        IEnumerable<double> Inner() { deps.Add(("str", strCall.boxedParams.wordStart, strCall.boxedParams.wordEnd)); yield break; }
        DependencyPolicies.WrapCall(Inner(), strCall.boxedParams, () => deps, DependencyPolicies.Get(strCall.boxedParams)).ToList();
        Assert.Empty(deps);
        Assert.Null(DependencyPolicies.Get(calls.Single(c => TestGame.JavaText(java, c).StartsWith("quickPrint")).boxedParams));
    }

    [SkippableFact]
    public void EndToEnd_RealCompiler_TypeErrorHasJavaSpan()
    {
        SkipIfCompilerStub();
        const string java = "void main() {\n    int x = 1.5;\n}\n";
        var ex = Assert.Throws<ParseException>(() => JavaParseBridge.Parse(java, TestGame.Window()));
        Assert.InRange(ex.startIndex, java.IndexOf("int", StringComparison.Ordinal), java.Length);
    }

    [SkippableFact]
    public void EndToEnd_RealCompiler_CrossFileCall()
    {
        SkipIfCompilerStub();
        var others = new Dictionary<string, string> { { "Utils", "void go() {\n    harvest();\n}\n" } };
        const string java = "void main() {\n    Utils.go();\n}\n";
        var program = JavaParseBridge.Parse(java, TestGame.Window("main"), others);
        Assert.NotNull(program);
        Assert.Contains("Utils", program.allVars);
        var import = TestGame.Nodes(program).OfType<ImportNode>().Single();
        Assert.StartsWith("Utils", TestGame.JavaText(java, import));
        Assert.Contains("import", TestGame.CheckedDependencies(program));
    }
}
