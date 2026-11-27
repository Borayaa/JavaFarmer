using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace JavaFarmer.Compiler.Tests;

public class SourceMapTests
{
    static Segment SegAt(CompileResult r, string pyNeedle, int occurrence = 0)
    {
        int idx = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            idx = r.Python.IndexOf(pyNeedle, idx + 1, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"'{pyNeedle}' not found in:\n{r.Python}");
        }
        return r.Map.Find(idx);
    }

    static string JavaText(string java, Segment s) => TestUtil.Slice(java, s.JavaStart, s.JavaEnd);

    [Fact]
    public void MainDefAndGuardAreSyntheticAndMapToTheMainHeader()
    {
        var java = "void main() {\n    harvest();\n}\n";
        var r = TestUtil.Compile(java);
        var def = SegAt(r, "def main():");
        Assert.Equal(SegmentKind.Synthetic, def.Kind);
        Assert.Equal("void main()", JavaText(java, def));
        var guard = SegAt(r, "if __name__");
        Assert.Equal(SegmentKind.Synthetic, guard.Kind);
        Assert.Equal("void main()", JavaText(java, guard));
        var call = SegAt(r, "harvest");
        Assert.Equal(SegmentKind.User, call.Kind);
        Assert.Equal("harvest", JavaText(java, call));
        var paren = SegAt(r, "harvest(");
        Assert.Equal("harvest()", JavaText(java, r.Map.Find(r.Python.IndexOf("harvest(") + "harvest".Length)));
    }

    [Fact]
    public void HelperMethodsAreUserCode()
    {
        var java = "int twice(int v) {\n    return v * 2;\n}\n\nvoid main() {\n    twice(3);\n}\n";
        var r = TestUtil.Compile(java);
        var def = SegAt(r, "def twice");
        Assert.Equal(SegmentKind.User, def.Kind);
        Assert.Equal("int twice(int v)", JavaText(java, def));
        var name = SegAt(r, "twice(v)");
        var ret = SegAt(r, "return");
        Assert.Equal(SegmentKind.User, ret.Kind);
        Assert.Equal("return v * 2;", JavaText(java, ret));
        var op = r.Map.Find(r.Python.IndexOf(" * ") + 1);
        Assert.Equal("*", JavaText(java, op));
    }

    [Fact]
    public void ClassicForLoweredToWhileHasStructuralHeaderAndForOverride()
    {
        var java = "void main() {\n    for (int i = 0; i < 10; i = i + 2) {\n        if (i == 4) continue;\n        harvest();\n    }\n}\n";
        var r = TestUtil.Compile(java);
        Assert.Contains("while i < 10:", r.Python);
        var w = SegAt(r, "while");
        Assert.Equal(SegmentKind.Structural, w.Kind);
        Assert.Equal("for", w.DependencyOverride);
        Assert.Equal("for", JavaText(java, w));
        var init = SegAt(r, "i = 0");
        Assert.Equal(SegmentKind.Structural, init.Kind);
        var cond = r.Map.Find(r.Python.IndexOf("i < 10") + 2);
        Assert.Equal(SegmentKind.Structural, cond.Kind);
        Assert.Null(cond.DependencyOverride);
        // the update is emitted twice (before continue and at the end of the body), both Structural
        var upd1 = SegAt(r, "i = i + 2", 0);
        var upd2 = SegAt(r, "i = i + 2", 1);
        Assert.Equal(SegmentKind.Structural, upd1.Kind);
        Assert.Equal(SegmentKind.Structural, upd2.Kind);
        // body stays user code
        Assert.Equal(SegmentKind.User, SegAt(r, "harvest").Kind);
        Assert.Equal(SegmentKind.User, SegAt(r, "continue").Kind);
    }

    [Fact]
    public void RangeForIsUserCode()
    {
        var java = "void main() {\n    for (int i = 0; i < getWorldSize(); i++) {\n        move(North);\n    }\n}\n";
        var r = TestUtil.Compile(java);
        Assert.Contains("for i in range(get_world_size()):", r.Python);
        var f = SegAt(r, "for i");
        Assert.Equal(SegmentKind.User, f.Kind);
        Assert.Equal("for", JavaText(java, f));
        Assert.Null(f.DependencyOverride);
        Assert.Equal(SegmentKind.User, SegAt(r, "range").Kind);
    }

    [Fact]
    public void GlobalsConcatStrAndHelpersAreSynthetic()
    {
        var java = "int count = 0;\n\nvoid main() {\n    count = count / 2;\n    quickPrint(\"n=\" + count);\n}\n";
        var r = TestUtil.Compile(java);
        var g = SegAt(r, "global count");
        Assert.Equal(SegmentKind.Synthetic, g.Kind);
        Assert.Equal("count", JavaText(java, g));
        var str = SegAt(r, "str(");
        Assert.Equal(SegmentKind.Synthetic, str.Kind);
        var helper = SegAt(r, "def __jdiv");
        Assert.Equal(SegmentKind.Synthetic, helper.Kind);
        Assert.Equal(-1, helper.JavaStart);
        var field = SegAt(r, "count = 0");
        Assert.Equal(SegmentKind.User, field.Kind);
    }

    [Fact]
    public void DoWhileCheckIsStructuralAndWhileMapsToDo()
    {
        var java = "void main() {\n    do {\n        harvest();\n    } while (canHarvest());\n}\n";
        var r = TestUtil.Compile(java);
        var w = SegAt(r, "while True");
        Assert.Equal(SegmentKind.User, w.Kind);
        Assert.Equal("do", JavaText(java, w));
        var check = SegAt(r, "if not");
        Assert.Equal(SegmentKind.Structural, check.Kind);
        Assert.Equal("while", JavaText(java, check));
        Assert.Equal(SegmentKind.User, SegAt(r, "can_harvest").Kind);
    }

    [Fact]
    public void ImportMapsToFirstUse()
    {
        var others = new Dictionary<string, string> { ["Utils"] = "public class Utils { public static void go() { } }" };
        var java = "void main() {\n    harvest();\n    Utils.go();\n    Utils.go();\n}\n";
        var r = TestUtil.Compile(java, "Main", others);
        var imp = SegAt(r, "import Utils");
        Assert.Equal(SegmentKind.User, imp.Kind);
        Assert.Equal(java.IndexOf("Utils"), imp.JavaStart);
        Assert.Equal("Utils", JavaText(java, imp));
    }

    [Fact]
    public void FindReturnsNearestPrecedingSegment()
    {
        var java = "void main() {\n    harvest();\n}\n";
        var r = TestUtil.Compile(java);
        Assert.Same(r.Map.Segments[^1], r.Map.Find(r.Python.Length + 50));
        Assert.Same(r.Map.Segments[0], r.Map.Find(0));
        Assert.Same(r.Map.Segments[0], r.Map.Find(-5));
    }

    // ------------------------------------------------------------ game AST walk
    static IEnumerable<global::Node> AllNodes(global::Node root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<global::Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == null || !seen.Add(n)) continue;
            yield return n;
            foreach (var c in n.slots) stack.Push(c);
            if (n is ForNode fn) stack.Push(fn.pattern);
        }
    }

    public static IEnumerable<object[]> ValidFiles() => CorpusTests.ValidFiles();

    /// <summary>Every node of the game's AST (parsed from our Python) maps through the SourceMap into the Java text,
    /// and nodes that start in user code map to a non-empty Java span.</summary>
    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void EveryGameNodeMapsIntoTheJavaText(string rel)
    {
        var path = Path.Combine(TestUtil.CorpusDir, rel);
        var java = File.ReadAllText(path);
        var others = new Dictionary<string, string>();
        if (rel.Count(c => c == '/') > 1)
            foreach (var o in Directory.GetFiles(Path.GetDirectoryName(path)!, "*.java"))
                if (o != path) others[Path.GetFileNameWithoutExtension(o)] = File.ReadAllText(o);
        var r = TestUtil.Compile(java, Path.GetFileNameWithoutExtension(path), others);
        Assert.True(r.Success);

        var cw = (CodeWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CodeWindow));
        var field = typeof(CodeWindow).GetField("parsedFunctions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        field.SetValue(cw, Activator.CreateInstance(field.FieldType));
        Tokenizer.Tokenize(GameOracle.NeutraliseConstBags(r.Python), out var ts);
        var program = global::Parser.Parse(ts, cw);
        int count = 0;
        foreach (var node in AllNodes(program.syntaxTree))
        {
            if (node is SequenceNode) continue;
            int ws = node.boxedParams.wordStart, we = node.boxedParams.wordEnd;
            var seg = r.Map.Find(ws);
            Assert.NotNull(seg);
            count++;
            if (seg.Kind == SegmentKind.Synthetic && seg.JavaStart < 0) continue;
            Assert.InRange(seg.JavaStart, 0, java.Length);
            Assert.InRange(seg.JavaEnd, seg.JavaStart, java.Length);
            if (seg.Kind != SegmentKind.Synthetic)
                Assert.True(seg.JavaEnd > seg.JavaStart, $"{rel}: {node.GetType().Name} at py {ws}-{we} maps to an empty Java span");
            var endSeg = r.Map.Find(Math.Max(ws, we - 1));
            Assert.NotNull(endSeg);
        }
        Assert.True(count > 3);
    }
}
