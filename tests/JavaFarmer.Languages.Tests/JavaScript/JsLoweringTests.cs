using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>JS semantics → IR: operators, type-directed fix-ups, collection methods, scoping, imports.</summary>
public class JsLoweringTests
{
    static IEnumerable<T> All<T>(IrModule m) => IrWalk.Descendants(m).OfType<T>();

    [Fact]
    public void Remainder_IsModTrunc_Division_IsTrueDiv()
    {
        var m = ToIr("const a = -7;\nconsole.log(a % 3, a / 2, Math.floor(a / 2));\n");
        Assert.Contains(All<IrBinary>(m), b => b.Op == IrBinOp.ModTrunc);
        Assert.Contains(All<IrBinary>(m), b => b.Op == IrBinOp.TrueDiv);
        Assert.Contains(All<IrOpCall>(m), o => o.Op == IrOp.Floor);
        Assert.Contains("__jmod(a, 3)", ToPy("const a = -7;\nconsole.log(a % 3);\n"));
    }

    [Fact]
    public void CompoundRemainder_IsModTrunc()
    {
        var m = ToIr("let x = 7;\nx %= 3;\nx **= 2;\nx /= 2;\n");
        var ops = All<IrAssign>(m).Select(a => a.Op).ToList();
        Assert.Equal(new IrBinOp?[] { IrBinOp.ModTrunc, IrBinOp.Pow, IrBinOp.TrueDiv }, ops);
    }

    [Fact]
    public void Plus_OnStrings_IsConcat_AfterInference()
    {
        var m = ToIr("const n = 3;\nconst s = \"n = \" + n;\nconst t = n + 1;\nlet u = \"\";\nu += n;\n");
        var bins = All<IrBinary>(m).ToList();
        Assert.Contains(bins, b => b.Op == IrBinOp.Concat);
        Assert.Contains(bins, b => b.Op == IrBinOp.Add);
        Assert.Contains(All<IrAssign>(m), a => a.Op == IrBinOp.Concat);
    }

    [Fact]
    public void Equality_LooseAndStrict_AreEq()
    {
        var m = ToIr("const a = 1;\nconsole.log(a === 1, a == 1, a !== 2, a != 2, a === null, a === undefined);\n");
        Assert.Equal(4, All<IrBinary>(m).Count(b => b.Op == IrBinOp.Eq));
        Assert.Equal(2, All<IrBinary>(m).Count(b => b.Op == IrBinOp.Ne));
        Assert.Contains("a == None", ToPy("const a = 1;\nconsole.log(a === undefined);\n"));
    }

    [Fact]
    public void ArrayMethods()
    {
        var py = ToPy("const a = [1, 2];\na.push(3, 4);\na.unshift(0);\nconst x = a.pop();\nconst y = a.shift();\na.splice(1, 1);\na.splice(1, 0, 9);\nconst z = a.splice(0, 1)[0];\nconsole.log(a.indexOf(2), a.includes(9), a.length, x, y, z, [...a], a.slice());\na.length = 0;\n");
        Assert.Contains("a.append(3)\na.append(4)", py);
        Assert.Contains("a.insert(0, 0)", py);
        Assert.Contains("x = a.pop()", py);
        Assert.Contains("y = a.pop(0)", py);
        Assert.Contains("a.pop(1)", py);
        Assert.Contains("a.insert(1, 9)", py);
        Assert.Contains("z = a.pop(0)", py);
        Assert.Contains("__jindexof(a, 2)", py);
        Assert.Contains("9 in a", py);
        Assert.Contains("list(a), list(a)", py);
        Assert.Contains("__jlclear(a)", py);
    }

    [Fact]
    public void MapMethods()
    {
        var py = ToPy("const m = new Map([[\"a\", 1]]);\nm.set(\"b\", 2);\nconsole.log(m.get(\"a\"), m.get(\"z\") ?? 0, m.has(\"a\"), m.size);\nm.delete(\"a\");\nfor (const k of m.keys()) console.log(k);\nfor (const v of m.values()) console.log(v);\nfor (const [k, v] of m) console.log(k, v);\nm.clear();\n");
        Assert.Contains("m = {\"a\": 1}", py);
        Assert.Contains("m[\"b\"] = 2", py);
        Assert.Contains("__jget(m, \"a\")", py);
        Assert.Contains("__jgetd(m, \"z\", 0)", py);
        Assert.Contains("\"a\" in m", py);
        Assert.Contains("len(m)", py);
        Assert.Contains("__jmremove(m, \"a\")", py);
        Assert.Contains("for k in m:", py);
        Assert.Contains("__jvalues(m)", py);
        Assert.Contains("__jmclear(m)", py);
    }

    [Fact]
    public void MapForOfDestructuring_BindsKeyAndValue()
    {
        var m = ToIr("const m = new Map();\nm.set(1, 2);\nfor (const [k, v] of m) {\n\tconsole.log(k, v);\n}\n");
        var fe = All<IrForEach>(m).Single();
        Assert.Equal("k", fe.Var.Name);
        Assert.Equal("v", fe.ValueVar.Name);
    }

    [Fact]
    public void SetMethods_DeleteIsRemoveValueIfPresent()
    {
        var m = ToIr("const s = new Set([1, 2]);\ns.add(3);\ns.delete(1);\nconsole.log(s.has(2), s.size);\nconst t = new Set(s);\n");
        Assert.Contains(All<IrOpCall>(m), o => o.Op == IrOp.SetAdd);
        Assert.Contains(All<IrOpCall>(m), o => o.Op == IrOp.RemoveValueIfPresent);
        Assert.Contains(All<IrOpCall>(m), o => o.Op == IrOp.SetCopy);
        Assert.DoesNotContain(All<IrOpCall>(m), o => o.Op == IrOp.MapRemoveIfPresent);
    }

    [Fact]
    public void ObjectLiterals_AreStringKeyedMaps()
    {
        var py = ToPy("const o = { a: 1, \"b c\": 2 };\no.d = 3;\nconsole.log(o.a, o[\"b c\"], \"a\" in o, Object.keys(o), Object.values(o));\nfor (const k in o) console.log(k);\n");
        Assert.Contains("o = {\"a\": 1, \"b c\": 2}", py);
        Assert.Contains("o[\"d\"] = 3", py);
        Assert.Contains("o[\"a\"], o[\"b c\"], \"a\" in o, set(o), __jvalues(o)", py);
    }

    [Fact]
    public void ArrayFactories()
    {
        var m = ToIr("const n = 3;\nconst a = new Array(n).fill(0);\nconst b = Array(n).fill(false);\nconst g = Array.from({ length: n }, () => new Array(n).fill(0));\n");
        Assert.Equal(2, All<IrOpCall>(m).Count(o => o.Op == IrOp.ArrayNew));
        Assert.Single(All<IrOpCall>(m), o => o.Op == IrOp.ArrayNew2);
    }

    [Fact]
    public void ArrayLiteralAsMapKey_IsATuple()
    {
        var m = ToIr("const seen = new Map();\nseen.set([1, 2], true);\nconsole.log(seen.has([1, 2]));\n");
        Assert.Equal(2, All<IrTupleLit>(m).Count());
    }

    [Fact]
    public void Destructuring_IsTupleUnpacking()
    {
        var py = ToPy("let [a, b] = [1, 2];\n[a, b] = [b, a];\nconst [x, [y, z]] = [1, [2, 3]];\nconsole.log(a, b, x, y, z);\n");
        Assert.Contains("a, b = (1, 2)", py);
        Assert.Contains("a, b = (b, a)", py);
    }

    [Fact]
    public void MathFunctions()
    {
        var py = ToPy("const x = 2.5;\nconsole.log(Math.floor(x), Math.ceil(x), Math.round(x), Math.sqrt(x), Math.trunc(x), Math.pow(x, 2), Math.min(1, 2), Math.max(1, 2), Math.abs(-1), Math.random());\n");
        Assert.Contains("__jfloor(x), __jceil(x), __jround(x), __jsqrt(x), __jtrunc(x), __jpow(x, 2), min(1, 2), max(1, 2), abs(-1), random()", py);
    }

    [Fact]
    public void ConsoleLog_IsPrint_WithAnyNumberOfArguments()
    {
        // the emitter prints an empty line as print("") (the game's print needs a value)
        Assert.Contains("print(\"\")", ToPy("console.log();\n"));
        Assert.Contains("print(1, \"a\", True)", ToPy("console.log(1, \"a\", true);\n"));
    }

    [Fact]
    public void Constants_AnyMemberName()
    {
        var m = ToIr("plant(Entities.Some_Future_Plant);\nmove(North);\nconsole.log(Items.Hay, Grounds.Soil, Unlocks.Speed, Hats.Straw_Hat, Leaderboards.Fastest_Reset);\n");
        var consts = All<IrConst>(m).Select(c => c.Category + "." + c.Name).ToList();
        Assert.Contains("Entities.Some_Future_Plant", consts);
        Assert.Contains("Directions.North", consts);
        Assert.Contains("Leaderboards.Fastest_Reset", consts);
    }

    [Fact]
    public void TopLevelVariableUsedByAFunction_IsGlobal()
    {
        var m = ToIr("let count = 0;\nconst limit = 5;\nconst other = 1;\nfunction bump() {\n\tcount++;\n\treturn count < limit;\n}\nbump();\nconsole.log(other);\n");
        Assert.Equal(new[] { "count", "limit" }, m.Globals.Select(g => g.Symbol.Name).ToArray());
        Assert.True(m.Globals.Single(g => g.Symbol.Name == "limit").Symbol.IsConst);
        Assert.Contains("    global count\n", ToPy("let count = 0;\nfunction bump() {\n\tcount++;\n}\nbump();\n"));
    }

    [Fact]
    public void ClosureAssigningATopLevelVariable_GetsAGlobalLine()
    {
        var py = ToPy("let n = 0;\nspawnDrone(() => {\n\tn += 1;\n});\n");
        Assert.Contains("global n", py);
    }

    [Fact]
    public void BlockScoping_ShadowedNamesAreDifferentSymbols()
    {
        var py = ToPy("const x = 1;\nif (x > 0) {\n\tconst x = 2;\n\tconsole.log(x);\n}\nconsole.log(x);\n");
        Assert.Contains("x_2 = 2", py);
        Assert.Contains("print(x_2)", py);
    }

    [Fact]
    public void FunctionDeclarations_AreHoisted()
    {
        var py = ToPy("work();\nfunction work() {\n\tharvest();\n}\n");
        Assert.True(py.IndexOf("def work", StringComparison.Ordinal) < py.IndexOf("work()\n", py.IndexOf("harvest", StringComparison.Ordinal), StringComparison.Ordinal));
        var nested = ToPy("function outer() {\n\treturn inner();\n\tfunction inner() {\n\t\treturn 1;\n\t}\n}\nouter();\n");
        Assert.True(nested.IndexOf("def __jlambda", StringComparison.Ordinal) < nested.IndexOf("return inner", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedFunctionsAndArrows_AreLambdas()
    {
        var m = ToIr("function outer() {\n\tfunction inner(a, b = 2) {\n\t\treturn a + b;\n\t}\n\tconst f = (x) => x * 2;\n\tconst g = function () { return 1; };\n\treturn inner(f(1)) + g();\n}\nouter();\n");
        Assert.Equal(3, All<IrLambda>(m).Count());
        Assert.NotNull(All<IrLambda>(m).First().Params[1].Default);
    }

    [Fact]
    public void ClassicFor_IsIrClassicFor_RangeDetectedByEmitter()
    {
        var m = ToIr("for (let i = 0; i < 10; i += 2) {\n\tconsole.log(i);\n}\n");
        Assert.Single(All<IrClassicFor>(m));
        Assert.Contains("for i in range(0, 10, 2):", ToPy("for (let i = 0; i < 10; i += 2) {\n\tconsole.log(i);\n}\n"));
    }

    [Fact]
    public void DoWhile_IsIrDoWhile() => Assert.Single(All<IrDoWhile>(ToIr("let i = 0;\ndo {\n\ti++;\n} while (i < 3);\n")));

    [Fact]
    public void Switch_StacksEmptyCasesAndDropsTheFinalBreak()
    {
        var m = ToIr("const n = 2;\nswitch (n) {\n\tcase 1:\n\tcase 2:\n\t\tharvest();\n\t\tbreak;\n\tdefault:\n\t\tdoAFlip();\n}\n");
        var sw = All<IrSwitch>(m).Single();
        Assert.Equal(2, sw.Cases.Count);
        Assert.Equal(2, sw.Cases[0].Labels.Count);
        Assert.True(sw.Cases[1].IsDefault);
        Assert.DoesNotContain(All<IrBreak>(m), b => true);
    }


    // ------------------------------------------------------------------ other code windows
    static readonly Dictionary<string, string> Lib = new()
    {
        ["Utils"] = "export function f(a) { return a; }\nexport let count = 0;\nfunction hidden() { }\nconst secret = 1;\n",
    };

    [Fact]
    public void NamespaceImport_ExternalSymbols()
    {
        var py = ToPy("import * as Utils from \"./Utils.js\";\nconsole.log(Utils.f(1), Utils.count);\n", "Main", Lib);
        Assert.Contains("import Utils", py);
        Assert.Contains("Utils.f(1), Utils.count", py);
    }

    [Fact]
    public void NamedImport_BindsExternalSymbols()
    {
        var m = ToIr("import { f, count } from \"./Utils.js\";\nconsole.log(f(count));\n", "Main", Lib);
        var imp = m.Items.OfType<IrImport>().Single();
        Assert.Equal(new[] { "f", "count" }, imp.Names);
        Assert.Contains("Utils.f(Utils.count)", ToPy("import { f, count } from \"./Utils.js\";\nconsole.log(f(count));\n", "Main", Lib));
    }

    [Fact]
    public void Import_MissingExport_V8Wording()
    {
        var e = Error("import { hidden } from \"./Utils.js\";\nhidden();\n", "Main", Lib);
        Assert.Equal("The requested module './Utils.js' does not provide an export named 'hidden'", e.Message);
    }

    [Fact]
    public void Import_AssigningAnImportedBinding_IsAnError()
    {
        Assert.Equal("Assignment to constant variable.", Error("import { count } from \"./Utils.js\";\ncount = 2;\n", "Main", Lib).Message);
        Assert.Equal("Assignment to constant variable.", Error("import * as U from \"./Utils.js\";\nU.count = 2;\n", "Main", Lib).Message);
    }

    [Fact]
    public void Import_UnknownModule_WhenOtherFilesAreKnown()
    {
        var e = Error("import * as Nope from \"./Nope.js\";\n", "Main", Lib);
        Assert.StartsWith("Cannot find module './Nope.js'", e.Message);
    }

    [Fact]
    public void Import_WithoutOtherFiles_AcceptsAnything()
    {
        Assert.Contains("Other.g(1)", ToPy("import * as Other from \"./Other.js\";\nOther.g(1);\n"));
    }

    [Fact]
    public void Import_ArityOfOtherModuleFunctions()
    {
        Assert.Equal("Utils.f() expects 1 argument, but got 2", Error("import * as Utils from \"./Utils.js\";\nUtils.f(1, 2);\n", "Main", Lib).Message);
    }

    [Fact]
    public void ExportedVariables_AreGlobals()
    {
        var m = ToIr(Lib["Utils"], "Utils");
        Assert.Contains(m.Globals, g => g.Symbol.Name == "count" && !g.IsPrivate);
        Assert.Contains(m.Functions, f => f.Name == "hidden" && f.IsPrivate);
    }
}

/// <summary>Registers a test builtin with GameApi: runs in the non-parallel GameApiRegistrations collection.</summary>
[Collection(GameApiRegistrations.Name)]
public class JsRegisteredBuiltinsTests
{
    [Fact]
    public void ExtensionBuiltins_WorkWithoutCodeChanges()
    {
        GameApi.Register("scan_port", "host: string, port?: int", "bool", "Scans a port.", "cybersecurity");
        try
        {
            Assert.Contains("scan_port(\"a\", 80)", ToPy("if (scanPort(\"a\", 80)) {\n\tconsole.log(1);\n}\n"));
            Assert.Equal("scanPort() expects 1 to 2 arguments, but got 0", Error("scanPort();\n").Message);
            Assert.Equal("scanPort", Lang.ApiName("scan_port"));
            Assert.Equal("scan_port", Lang.FromApiName("scanPort"));
            Assert.Contains("export function scanPort(host, port)", Lang.ApiStubFile(out _));
        }
        finally { GameApi.Unregister("scan_port"); }
    }

}
