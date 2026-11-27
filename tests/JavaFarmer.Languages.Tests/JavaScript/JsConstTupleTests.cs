using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>
/// "const p = [x, y]" that is only read becomes a game tuple, so it works as a Map key / Set item like an inline
/// array literal (game lists cannot be dict keys or set items). Any mutation or escaping use keeps a list.
/// </summary>
public class JsConstTupleTests
{
    [Fact]
    public void ConstArray_UsedAsKey_IsATuple()
    {
        var py = ToPy("const seen = new Set();\nconst p = [getPosX(), getPosY()];\nseen.add(p);\nif (seen.has(p)) { console.log(p[0]); }\nconst m = new Map();\nm.set(p, 1);\nconst [x, y] = p;\nconsole.log(x + y, p.length);\n");
        Assert.Contains("p = (get_pos_x(), get_pos_y())", py);
        Assert.Contains("seen.add(p)", py);
        RoundTrip("const seen = new Set();\nconst p = [1, 2];\nseen.add(p);\nconsole.log(seen.has(p));\n");
    }

    [Fact]
    public void ConstArray_InsideLoopsAndFunctions_IsATuple()
    {
        var py = ToPy("const visited = new Set();\nfunction mark() {\n\tconst here = [getPosX(), getPosY()];\n\tif (visited.has(here)) {\n\t\treturn false;\n\t}\n\tvisited.add(here);\n\treturn true;\n}\nmark();\n");
        Assert.Contains("here = (get_pos_x(), get_pos_y())", py);
    }

    [Theory]
    [InlineData("const p = [1, 2];\np.push(3);\nconsole.log(p);\n")]
    [InlineData("const p = [1, 2];\np[0] = 5;\nconsole.log(p);\n")]
    [InlineData("const p = [1, 2];\np[0] += 5;\nconsole.log(p);\n")]
    [InlineData("function f(a) { a.push(1); }\nconst p = [1, 2];\nf(p);\n")]
    [InlineData("const p = [1, 2];\nconst q = p;\nq.push(3);\n")]
    [InlineData("let p = [1, 2];\nconst s = new Set();\nconsole.log(p);\n")]
    [InlineData("const p = [1, 2];\nconsole.log(\"p: \" + p);\n")]
    public void MutatedOrEscapingArrays_StayLists(string src)
    {
        var py = ToPy(src);
        Assert.Contains("p = [1, 2]", py);
    }

    [Fact]
    public void ExportedConstArray_StaysAList()
    {
        var py = ToPy("export const HOME = [0, 0];\nconst s = new Set();\ns.add(HOME);\n");
        Assert.Contains("HOME = [0, 0]", py);
    }
}
