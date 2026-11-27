using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>IR (from Python, Java and JS) → idiomatic JavaScript that compiles again.</summary>
public class JsPrinterTests
{
    static string Compiles(string js)
    {
        var r = Lang.Compile(js, new CompileOptions { FileName = "Docs" });
        Assert.True(r.Success, "does not compile: " + string.Join("; ", r.Errors) + "\n" + js);
        return r.Python;
    }

    [Fact]
    public void Python_FunctionsLoopsAndBuiltins()
    {
        var js = FromPython("def harvest_column():\n\tfor i in range(get_world_size()):\n\t\tif can_harvest():\n\t\t\tharvest()\n\t\tmove(North)\n\nwhile True:\n\tharvest_column()\n\tmove(East)\n");
        Assert.Contains("function harvestColumn() {", js);
        Assert.Contains("for (let i = 0; i < getWorldSize(); i++) {", js);
        Assert.Contains("if (canHarvest()) {", js);
        Assert.Contains("while (true) {", js);
        Compiles(js);
    }

    [Fact]
    public void Python_DictsBecomeMaps_SetsBecomeSets_TuplesBecomeArrays()
    {
        var js = FromPython("d = {North: East}\nd[South] = West\ns = set()\ns.add(1)\nt = (1, 2)\nprint(d[North], len(d), 1 in s, t[0])\n");
        Assert.Contains("const d = new Map([[North, East]]);", js);
        Assert.Contains("d.set(South, West);", js);
        Assert.Contains("const s = new Set();", js);
        Assert.Contains("s.add(1);", js);
        Assert.Contains("const t = [1, 2];", js);
        Assert.Contains("console.log(d.get(North), d.size, s.has(1), t[0]);", js);
        Compiles(js);
    }

    [Fact]
    public void Python_FloorDivisionAndModulo()
    {
        var js = FromPython("a = 7\nprint(a // 2, a % 2 == 0, a % 3)\nfor i in range(10):\n\tprint(i % 3)\n");
        Assert.Contains("Math.floor(a / 2)", js);
        Assert.Contains("a % 2 === 0", js);
        Assert.Contains("((a % 3) + 3) % 3", js);
        Assert.Contains("console.log(i % 3)", js);
        Compiles(js);
    }

    [Fact]
    public void Python_GlobalsAndTupleUnpacking()
    {
        var js = FromPython("count = 0\ndef bump():\n\tglobal count\n\tcount += 1\nx, y = 1, 2\nfor a, b in [(1, 2)]:\n\tprint(a, b)\nbump()\n");
        Assert.Contains("let count = 0;", js);
        Assert.Contains("const [x, y] = [1, 2];", js);
        Assert.Contains("for (const [a, b] of [[1, 2]]) {", js);
        Compiles(js);
    }

    [Fact]
    public void Python_NestedDefIsANestedFunction()
    {
        var js = FromPython("def outer():\n\tdef task():\n\t\tharvest()\n\tspawn_drone(task)\nouter()\n");
        Assert.Contains("\tfunction task() {", js);
        Assert.Contains("spawnDrone(task);", js);
        Compiles(js);
    }

    [Fact]
    public void Python_ReservedWordsAreRenamed()
    {
        var js = FromPython("new = 1\nfunction = 2\ndefault = new + function\nprint(default)\n");
        Assert.Contains("const new_ = 1;", js);
        Assert.Contains("const function_ = 2;", js);
        Assert.Contains("console.log(default_);", js);
        Compiles(js);
    }

    [Fact]
    public void Python_StringConversions()
    {
        var js = FromPython("n = 3\nprint(\"n = \" + str(n))\n");
        Assert.Contains("console.log(\"n = \" + String(n));", js);
        Compiles(js);
    }

    [Fact]
    public void Python_Imports()
    {
        var js = FromPython("import utils\nfrom helpers import go_to\nutils.run(1)\ngo_to(1, 2)\n");
        Assert.Contains("import * as utils from \"./utils.js\";", js);
        Assert.Contains("import { goTo } from \"./helpers.js\";", js);
        Assert.Contains("utils.run(1);", js);
        Assert.Contains("goTo(1, 2);", js);
        Compiles(js);
    }

    [Fact]
    public void Python_UndefinedNamesInDocsBlocks()
    {
        var full = FromPython("value = dict[key]\nif condition():\n\tdo_it()\n");
        Assert.Contains("let dict; // defined elsewhere", full);
        Assert.Contains("function doIt() {", full);
        Compiles(full);
        var snippet = FromPython("value = dict[key]\n", true);
        Assert.DoesNotContain("defined elsewhere", snippet);
        Assert.Equal("const value = dict[key];", snippet.Trim());
    }

    [Fact]
    public void Python_ListRepetitionAndConcatenation()
    {
        var js = FromPython("a = [0] * 5\nb = a + [1]\n");
        Assert.Contains("new Array(5).fill(0)", js);
        Assert.Contains("[...a, 1]", js);
        Compiles(js);
    }

    [Fact]
    public void Java_EntryBecomesTopLevel_SwitchWithLoopBreakBecomesIf()
    {
        var java = Compiler.Languages.Get("java");
        var ir = java.ToIr("void main() {\n    int n = getWorldSize();\n    while (true) {\n        switch (n % 3) {\n            case 0 -> { break; }\n            default -> quickPrint(n);\n        }\n        n--;\n        if (n < 0) break;\n    }\n}\n", new CompileOptions { FileName = "Main" }, out var errs);
        Assert.True(ir != null, string.Join("; ", errs));
        var js = Lang.Print(ir, new PrintOptions { FileName = "Main" });
        Assert.DoesNotContain("function main", js);
        Assert.Contains("let n = getWorldSize();", js);
        Compiles(js);
    }

    [Fact]
    public void Js_RoundTrips_KeepObjectsAndMaps()
    {
        var printed = RoundTrip("const o = { a: 1 };\nconst m = new Map();\nm.set(\"a\", o.a);\nfor (const k in o) console.log(k, o[k]);\nconsole.log(m.get(\"a\"), \"a\" in o, m.has(\"a\"));\n");
        Assert.Contains("const o = { a: 1 };", printed);
        Assert.Contains("m.set(\"a\", o.a);", printed);
        Assert.Contains("for (const k in o) {", printed);
        Assert.Contains("\"a\" in o", printed);
        Assert.Contains("m.has(\"a\")", printed);
    }

    [Fact]
    public void Js_RoundTrips_Templates_Switch_Destructuring_Lambdas()
    {
        RoundTrip("const n = 2;\nconsole.log(`${n}${n}`, `n=${n}`, `${n}`);\n");
        RoundTrip("const x = 1;\nswitch (x) {\n\tcase 1:\n\tcase 2:\n\t\tharvest();\n\t\tbreak;\n\tdefault:\n\t\tdoAFlip();\n}\n");
        RoundTrip("let [a, b] = [1, 2];\n[a, b] = [b, a];\nconst m = new Map();\nfor (const [k, v] of m) console.log(k, v);\n");
        var p = RoundTrip("const f = (x) => x + 1;\nspawnDrone(() => {\n\tharvest();\n});\nfunction g() {\n\tfunction h() {\n\t\treturn 1;\n\t}\n\treturn h();\n}\ng();\n");
        Assert.Contains("\tfunction h() {", p);
    }

    [Fact]
    public void Js_PrintsComments()
    {
        var p = RoundTrip("// lead\nconst a = 1; // trail\n/* block */\nconsole.log(a);\n// end\n");
        Assert.Contains("// lead", p);
        Assert.Contains("const a = 1; // trail", p);
        Assert.Contains("/* block */", p);
        Assert.Contains("// end", p);
    }

    [Fact]
    public void Js_ExportsStayOnLibraryFiles()
    {
        var p = Lang.Print(ToIr("export let n = 0;\nexport function f() { return n; }\nfunction g() { }\n", "Utils"), new PrintOptions { FileName = "Utils" });
        Assert.Contains("export let n = 0;", p);
        Assert.Contains("export function f() {", p);
        Assert.Contains("\nfunction g() {", p);
    }

    [Fact]
    public void Problems_AreReported()
    {
        var java = Compiler.Languages.Get("java");
        var ir = java.ToIr("import java.util.*;\nvoid main() {\n    Map<Integer, Integer> m = new HashMap<>();\n    m.put(1, 2);\n    Integer v = m.remove(1);\n}\n", new CompileOptions { FileName = "Main" }, out var errs);
        Assert.True(ir != null, string.Join("; ", errs));
        Lang.Print(ir, new PrintOptions { FileName = "Main" }, out var problems);
        Assert.NotEmpty(problems);
    }
}
