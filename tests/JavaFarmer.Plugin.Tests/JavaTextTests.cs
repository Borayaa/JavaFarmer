using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

/// <summary>No Python leaks into game-generated text: docs index, printed values, traces, error arguments, highlights.</summary>
[Collection("Game")]
public class JavaTextTests
{
    // ------------------------------------------------------------------ docs home: Built-in Functions

    [Fact]
    public void BuiltinsToc_ShowsJavaNames_KeepsPythonLinkTargets()
    {
        string toc = JavaText.BuiltinsToc(BuiltinFunctions.Functions.Keys);
        Assert.Contains("<unlock=do_a_flip>[doAFlip](functions/do_a_flip)", toc);
        Assert.Contains("<unlock=pet_the_piggy>[petThePiggy](functions/pet_the_piggy)", toc);
        Assert.Contains("<unlock=harvest>[harvest](functions/harvest)", toc);
        Assert.Contains("<unlock=print>[System.out.println](functions/print)", toc);
        Assert.Contains("<unlock=max>[Math.max](functions/max)", toc);
        Assert.Contains("<unlock=str>[String.valueOf](functions/str)", toc);
        Assert.Contains("<unlock=list>[ArrayList](functions/list)", toc);
        Assert.Contains("<unlock=dict>[HashMap](functions/dict)", toc);
        Assert.Contains("<unlock=set>[HashSet](functions/set)", toc);
        Assert.DoesNotContain("functions/range", toc);
        Assert.DoesNotContain("functions/len", toc);
        // no link text is a snake_case Python name
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(toc, @"\[([^\]]*)\]"))
            Assert.DoesNotContain("_", m.Groups[1].Value);
        Assert.Equal(BuiltinFunctions.Functions.Count - 2, System.Text.RegularExpressions.Regex.Matches(toc, "<unlock=").Count);
    }

    [Fact]
    public void BuiltinsToc_IsSortedByJavaName()
    {
        string toc = JavaText.BuiltinsToc(new[] { "print", "abs", "do_a_flip", "list", "can_harvest" });
        var names = System.Text.RegularExpressions.Regex.Matches(toc, @"\[([^\]]*)\]").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "ArrayList", "canHarvest", "doAFlip", "Math.abs", "System.out.println" }, names);
    }

    [Fact]
    public void UnknownBuiltin_GetsCamelCase()
    {
        Assert.Equal("getDroneId", JavaText.BuiltinJavaName("get_drone_id", _ => null));
        Assert.Equal("petThePiggy", JavaText.SnakeToCamel("pet_the_piggy"));
    }

    [Fact]
    public void CollectionTypes_HoverTheirConstructorTooltips()
    {
        Assert.Equal("list", EditorSupport.HoverWordToPython("ArrayList", _ => null));
        Assert.Equal("dict", EditorSupport.HoverWordToPython("HashMap", _ => null));
        Assert.Equal("set", EditorSupport.HoverWordToPython("HashSet", _ => null));
    }

    [Fact]
    public void BarePythonBuiltinNames_AreUserIdentifiers()
    {
        Assert.True(EditorSupport.IsPythonOnlyWord("list"));
        Assert.True(EditorSupport.IsPythonOnlyWord("max"));
        Assert.False(EditorSupport.IsPythonOnlyWord("Math.max"));
        Assert.False(EditorSupport.IsPythonOnlyWord("harvest"));
        Assert.False(EditorSupport.IsPythonOnlyWord("ArrayList"));
    }

    // ------------------------------------------------------------------ values

    private static PyNumber N(double d) => new PyNumber(d);
    private static PyList L(params IPyObject[] xs) => new PyList(xs.ToList());
    private static PyTuple Tup(params IPyObject[] xs) => new PyTuple(xs.ToList());

    [Fact]
    public void Format_Scalars_AreJava()
    {
        Assert.Equal("true", ValueFormatter.Format(new PyBool(true)));
        Assert.Equal("false", ValueFormatter.Format(new PyBool(false)));
        Assert.Equal("null", ValueFormatter.Format(new PyNone()));
        Assert.Equal("null", ValueFormatter.Format(null));
        Assert.Equal("3", ValueFormatter.Format(N(3)));
        Assert.Equal("1.5", ValueFormatter.Format(N(1.5)));
        Assert.Equal("0.33", ValueFormatter.Format(N(1.0 / 3)));
        Assert.Equal("hi", ValueFormatter.Format(new PyString("hi")));
    }

    [Fact]
    public void Format_Collections_AreJava()
    {
        Assert.Equal("[1, 2, 3]", ValueFormatter.Format(L(N(1), N(2), N(3))));
        Assert.Equal("[]", ValueFormatter.Format(L()));
        Assert.Equal("[hi, yo]", ValueFormatter.Format(L(new PyString("hi"), new PyString("yo"))));
        Assert.Equal("[true, null, [1]]", ValueFormatter.Format(L(new PyBool(true), new PyNone(), L(N(1)))));

        var dict = new PyDict(new Dictionary<IPyObject, PyObjectBox>
        {
            { new PyString("a"), new PyObjectBox(N(1)) },
            { new PyString("b"), new PyObjectBox(new PyBool(false)) },
        });
        Assert.Equal("{a=1, b=false}", ValueFormatter.Format(dict));
        Assert.Equal("{}", ValueFormatter.Format(new PyDict(new Dictionary<IPyObject, PyObjectBox>())));

        var set = new PySet(new HashSet<IPyObject> { new PyString("x") });
        Assert.Equal("[x]", ValueFormatter.Format(set));
    }

    [Fact]
    public void Format_PositionAndCompanion_AreRecordStyle()
    {
        Assert.Equal("Position[x=5, y=3]", ValueFormatter.Format(Tup(N(5), N(3))));
        Assert.Equal("Companion[entity=Entities.Tree, position=Position[x=1, y=2]]",
            ValueFormatter.Format(Tup(new PyString("Entities.Tree"), Tup(N(1), N(2)))));
        Assert.Equal("[North, 1]", ValueFormatter.Format(Tup(new PyGridDirection(GridDirection.North), N(1))));
    }

    [Fact]
    public void Format_SelfContainingList_DoesNotRecurseForever()
    {
        var l = L(N(1));
        l.Add(l);
        Assert.Equal("[1, (this Collection)]", ValueFormatter.Format(l));
    }

    [Fact]
    public void Format_FunctionsAndDrones()
    {
        RuntimeNames.Register(new Dictionary<string, string> { { "list_j", "list" } });
        Assert.Equal("lambda", ValueFormatter.Format(new PyFunction("__jlambda3")));
        Assert.Equal("list", ValueFormatter.Format(new PyFunction("list_j")));
        Assert.Equal("helper", ValueFormatter.Format(new PyFunction("helper")));
        Assert.Equal("Drone@7", ValueFormatter.Format(new PyDroneHandle(2, 7)));
    }

    // ------------------------------------------------------------------ names, traces, errors

    [Fact]
    public void CompileResult_Names_MapRenamedIdentifiers()
    {
        var r = JavaCompiler.Compile(
            "int count = 0;\nvoid count2() {}\nvoid main() {\n    int list = 1;\n    int count2 = 2;\n    Runnable r = () -> harvest();\n    r.run();\n}\n",
            new CompileOptions { FileName = "main" });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal("list", r.Names["list_j"]);
        Assert.Equal("count2", r.Names["count2_l"]);
        Assert.Equal("lambda", r.Names["__jlambda1"]);
        Assert.False(r.Names.ContainsKey("count"));
    }

    [Fact]
    public void RuntimeNames_DemangleAndCandidates()
    {
        RuntimeNames.Register(new Dictionary<string, string> { { "list_j", "list" }, { "count_l", "count" }, { "__jlambda1", "lambda" } });
        Assert.Equal("list", RuntimeNames.ToJava("list_j"));
        Assert.Equal("lambda", RuntimeNames.ToJava("__jlambda9"));
        Assert.Null(RuntimeNames.ToJava("__jdiv"));
        Assert.Equal("plain", RuntimeNames.ToJava("plain"));
        Assert.Contains("count_l", RuntimeNames.PythonCandidates("count"));
        Assert.Empty(RuntimeNames.PythonCandidates("nothing"));
        RuntimeNames.Register(new Dictionary<string, string> { { "list_j", "list" }, { "list_j_l", "list" } });
        Assert.Equal("list_j_l", RuntimeNames.PythonCandidates("list")[0]);
    }

    [Fact]
    public void Trace_UsesJavaNames_AndHidesHelpers()
    {
        RuntimeNames.Register(new Dictionary<string, string> { { "list_j", "list" } });
        Assert.Equal("lambda <- main\nx = 1\n^", JavaText.RewriteTrace("__jlambda2 <- main\nx = 1\n^"));
        Assert.Equal("list <- main\nline", JavaText.RewriteTrace("__jdiv <- list_j <- main\nline"));
    }

    [Fact]
    public void ErrorArgs_DemangleUserNames()
    {
        RuntimeNames.Register(new Dictionary<string, string> { { "list_j", "list" } });
        var args = new object[] { "__jlambda4", "list_j()", "do_a_flip()", "not", new PyString("list_j") };
        Assert.True(ErrorText.MapArgs("error_function_in_operator", args));
        Assert.Equal("lambda", args[0]);
        Assert.Equal("list()", args[1]);
        Assert.Equal("doAFlip()", args[2]);
        Assert.Equal("!", args[3]);
        Assert.Equal("list_j", ((PyString)args[4]).str); // values are not names
    }

    [Fact]
    public void Signature_IncludesJavaComments()
    {
        string text = "void main() {\n}\n// Flips twice.\n// Really.\nvoid flip2() {\n    doAFlip();\n}";
        int s = text.IndexOf("void flip2"), e = text.IndexOf(") {", s) + 1;
        Assert.Equal("// Flips twice.\n// Really.\nvoid flip2() {", JavaText.SignatureWithComments(text, s, e));
    }

    // ------------------------------------------------------------------ highlights

    [Fact]
    public void MainScaffolding_IsHidden_HelperCode_IsMarked()
    {
        string java = "void main() {\n    int a = 7;\n    int b = 0;\n    int c = a / b;\n}\n";
        var cw = TestGame.Window();
        var program = JavaParseBridge.Parse(java, cw);
        var nodes = TestGame.Nodes(program);

        var def = nodes.OfType<DefNode>().Single(d => d.boxedParams.wordStart >= 0 && TestGame.JavaText(java, d) == "" && JavaParseBridge.IsHidden(d.boxedParams));
        Assert.Equal(def.boxedParams.wordStart, def.boxedParams.wordEnd);
        Assert.False(def.blink);
        var mainCall = nodes.OfType<CallNode>().Single(c => JavaParseBridge.IsHidden(c.boxedParams));
        Assert.False(mainCall.blink);
        Assert.Contains(nodes, n => n is BranchNode && JavaParseBridge.IsHidden(n.boxedParams));

        // user statements are neither hidden nor helper code
        var userAssign = nodes.OfType<AssignmentNode>().First(n => TestGame.JavaText(java, n).Contains("7"));
        Assert.False(JavaParseBridge.IsHidden(userAssign.boxedParams));
        Assert.False(JavaParseBridge.IsHelperCode(userAssign.boxedParams));
        Assert.True(userAssign.blink);

        // a / b with possibly negative ints goes through the __jdiv helper, whose body is helper code
        Assert.Contains(nodes, n => JavaParseBridge.IsHelperCode(n.boxedParams));
    }

    [Fact]
    public void CompilerMarksScaffoldingSegments()
    {
        var r = JavaCompiler.Compile("public class Main {\n    static int n;\n    static void inc() { n++; }\n    public static void main(String[] args) { inc(); }\n}\n", new CompileOptions { FileName = "Main" });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var hidden = r.Map.Segments.Where(s => s.NoHighlight).Select(s => r.Python.Substring(s.PyStart, s.PyEnd - s.PyStart)).ToList();
        string all = string.Concat(hidden);
        Assert.Contains("def main():", all);
        Assert.Contains("if __name__", all);
        Assert.Contains("main()", all);
        Assert.Contains("global n", all);
        Assert.Contains("args = []", all);
        Assert.DoesNotContain(r.Map.Segments, s => s.NoHighlight && s.Kind != SegmentKind.Synthetic);
    }
}
