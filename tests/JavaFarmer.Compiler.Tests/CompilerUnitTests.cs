namespace JavaFarmer.Compiler.Tests;

/// <summary>Lexer, parser, type checker (message + span) and lowering snapshots.</summary>
public class LexerTests
{
    [Fact]
    public void LexesNumbersStringsAndOperators()
    {
        var toks = new Lexer("int x = 0x1F + 1_000 + 2.5e3 + .5 + 3d; // c\n\"a\\\"b\" >= <= -> :: ++").LexSignificant();
        var kinds = toks.Select(t => t.Kind + ":" + t.Text).ToList();
        Assert.Contains("IntLit:0x1F", kinds);
        Assert.Contains("IntLit:1_000", kinds);
        Assert.Contains("DoubleLit:2.5e3", kinds);
        Assert.Contains("DoubleLit:.5", kinds);
        Assert.Contains("DoubleLit:3d", kinds);
        Assert.Contains("Op:>=", kinds);
        Assert.Contains("Op:->", kinds);
        Assert.Contains("Op:::", kinds);
        var str = toks.First(t => t.Kind == Tk.StringLit);
        Assert.Equal("a\"b", str.StrValue);
        Assert.Equal(Tk.EOF, toks[^1].Kind);
    }

    [Fact]
    public void GenericClosersAreSeparateTokens()
    {
        var toks = new Lexer("Map<String, List<List<Integer>>> m;").LexSignificant();
        Assert.Equal(3, toks.Count(t => t.Text == ">"));
    }

    [Fact]
    public void ReportsUnclosedString()
    {
        var lx = new Lexer("String s = \"abc;\n");
        lx.LexSignificant();
        Assert.NotNull(lx.Error);
        Assert.Equal("unclosed string literal", lx.Error.Message);
        Assert.Equal(11, lx.Error.Start);
    }
}

public class ParserTests
{
    static CompilationUnit P(string src)
    {
        var cu = Parser.Parse(src, out var err);
        Assert.True(err == null, err?.ToString());
        return cu;
    }

    [Fact]
    public void ParsesCompactFile()
    {
        var cu = P("int a = 1, b;\nvoid main() { }\nint f(int x) { return x; }");
        Assert.False(cu.IsClass);
        Assert.Equal(2, cu.Fields[0].Vars.Count);
        Assert.Equal(2, cu.Methods.Count);
    }

    [Fact]
    public void ParsesClassFile()
    {
        var cu = P("import java.util.*;\npublic class Farm {\n  static int n;\n  public static void main(String[] args) { }\n}");
        Assert.True(cu.IsClass);
        Assert.Equal("Farm", cu.ClassName);
        Assert.Single(cu.Imports);
    }

    [Fact]
    public void DistinguishesCastsFromParentheses()
    {
        var cu = P("void main() { int a = (int) 2.5; int b = (a) + 1; Object o = null; Position p = (Position) o; double d = (double)(a); }");
        var stmts = cu.Methods[0].Body.Stmts.Cast<LocalVarStmt>().ToList();
        Assert.IsType<CastExpr>(stmts[0].Vars[0].Init);
        Assert.IsType<BinaryExpr>(stmts[1].Vars[0].Init);
        Assert.IsType<CastExpr>(stmts[3].Vars[0].Init);
        Assert.IsType<CastExpr>(stmts[4].Vars[0].Init);
    }

    [Fact]
    public void ParsesGenericsLambdasAndMethodRefs()
    {
        var cu = P("void main() { Map<String, List<Integer>> m = new HashMap<>(); Runnable r = () -> harvest(); Supplier<Integer> s = () -> { return 1; }; spawnDrone(this::main); }");
        var stmts = cu.Methods[0].Body.Stmts;
        var m = (LocalVarStmt)stmts[0];
        Assert.Equal("Map", m.Type.Name);
        Assert.Equal("List", m.Type.Args[1].Name);
        Assert.IsType<LambdaExpr>(((LocalVarStmt)stmts[1]).Vars[0].Init);
        Assert.NotNull(((LambdaExpr)((LocalVarStmt)stmts[2]).Vars[0].Init).BodyBlock);
        var call = (CallExpr)((ExprStmt)stmts[3]).E;
        Assert.IsType<MethodRefExpr>(call.Args[0]);
    }

    [Fact]
    public void ParsesSwitchForms()
    {
        var cu = P("void main() { switch (1) { case 1, 2 -> harvest(); default -> { till(); } } switch (2) { case 1: case 2: harvest(); break; default: } }");
        var a = (SwitchStmt)cu.Methods[0].Body.Stmts[0];
        var b = (SwitchStmt)cu.Methods[0].Body.Stmts[1];
        Assert.True(a.Arrow);
        Assert.Equal(2, a.Cases[0].Labels.Count);
        Assert.False(b.Arrow);
        Assert.Equal(3, b.Cases.Count);
    }

    [Theory]
    [InlineData("void main() { int x = 1 }", "';' expected", "1")]
    [InlineData("void main() { harvest() }", "';' expected", ")")]
    [InlineData("void main() { if (true { } }", "')' expected", "true")]
    [InlineData("void main() { x + ; }", "illegal start of expression", ";")]
    [InlineData("void main() { int class = 1; }", "<identifier> expected (class is a reserved word)", "class")]
    [InlineData("void main() { x >> 1; }", "shift operators is not supported in JavaFarmer", ">>")]
    [InlineData("void main() { boolean b = o instanceof String; }", "'instanceof' is not supported in JavaFarmer", "instanceof")]
    [InlineData("class A { interface B {} }", "nested interfacees is not supported in JavaFarmer", "interface")]
    [InlineData("void main() { outer: while (true) { } }", "labels is not supported in JavaFarmer", "outer")]
    [InlineData("void main() { int x = 1; x; }", "not a statement", "x")]
    [InlineData("void main() { new Foo() { }; }", "anonymous classes is not supported in JavaFarmer", "{")]
    [InlineData("void main() { int x = 99999999999; }", "integer number too large", "99999999999")]
    public void SyntaxErrors(string src, string msg, string at)
    {
        var err = TestUtil.CompileErr(src, "A");
        Assert.Equal(msg, err.Message);
        Assert.Equal(at, TestUtil.Slice(src, err.Start, err.End));
    }
}

public class TypeCheckerTests
{
    [Theory]
    [InlineData("int x = \"a\";", "incompatible types: String cannot be converted to int", "\"a\"")]
    [InlineData("boolean b = 1;", "incompatible types: int cannot be converted to boolean", "1")]
    [InlineData("String s = 1;", "incompatible types: int cannot be converted to String", "1")]
    [InlineData("double d = 1; int i = d;", "incompatible types: possible lossy conversion from double to int", "d")]
    [InlineData("int x = 1 + true;", "bad operand types for binary operator '+' (int and boolean)", "+")]
    [InlineData("boolean b = 1 == \"a\";", "incomparable types: int and String", "==")]
    [InlineData("move(1);", "incompatible types: int cannot be converted to Direction", "1")]
    [InlineData("useItem(Items.Water, 1, 2);", "no suitable method found for useItem(Item,int,int)", "useItem(Items.Water, 1, 2)")]
    [InlineData("int x = getEntityType();", "incompatible types: Entity cannot be converted to int", "getEntityType()")]
    [InlineData("List<Double> l = List.of(1, 2);", "incompatible types: int cannot be converted to Double", "1")]
    [InlineData("List<int> l = null;", "unexpected type: required reference, found int (use Integer)", "int")]
    [InlineData("var v;", "cannot infer type for local variable v (cannot use 'var' on variable without initializer)", "v")]
    [InlineData("var n = null;", "cannot infer type for local variable n (variable initializer is 'null')", "n")]
    [InlineData("Runnable r = () -> 5;", "bad return type in lambda expression: int cannot be converted to void", "5")]
    [InlineData("int[] a = new int[2]; int b = a.length();", "cannot find symbol: method length() (arrays use .length without parentheses)", "length()")]
    [InlineData("String s = \"x\"; int n = s.length;", "cannot find symbol: variable length (use length())", "length")]
    [InlineData("float f = 1;", "float is not supported in JavaFarmer; use int or double", "float")]
    [InlineData("int x = 0; x.foo();", "int cannot be dereferenced", "x")]
    [InlineData("Object o = measure(); int n = o + 1;", "bad operand types for binary operator '+' (Object and int)", "+")]
    [InlineData("int x = 5; x += \"a\";", "bad operand types for binary operator '+' (int and String)", "+=")]
    [InlineData("break;", "break outside switch or loop", "break;")]
    [InlineData("continue;", "continue outside of loop", "continue;")]
    [InlineData("return 5;", "incompatible types: unexpected return value", "5")]
    [InlineData("Map<String, Integer> m = new HashMap<>(); for (String k : m) { }", "for-each not applicable to expression type Map<String,Integer> (iterate over map.keySet() or map.values())", "m")]
    [InlineData("int a = 1; Runnable r = () -> { int a = 2; };", "variable a is already defined in method main()", "a")]
    [InlineData("Math.foo(1);", "Math.foo is not supported in JavaFarmer", "foo")]
    [InlineData("new Farm();", "cannot find symbol: class Farm", "Farm")]
    [InlineData("List<Integer> l = new List<>();", "List is abstract; cannot be instantiated (use new ArrayList<>())", "List")]
    [InlineData("int x = 1; x = x++ ;", "++ can only be used as a separate statement in JavaFarmer", "x++")]
    public void TypeErrorsInMain(string body, string msg, string at)
    {
        var src = TestUtil.InMain(body);
        var err = TestUtil.CompileErr(src, "Main");
        Assert.Equal(msg, err.Message);
        Assert.Equal(at, TestUtil.Slice(src, err.Start, err.End));
    }

    [Fact]
    public void CrossFileSymbols()
    {
        var others = new Dictionary<string, string>
        {
            ["Utils"] = "public class Utils { private static int hidden = 1; public static int count = 0; static int twice(int x) { return x * 2; } private static void secret() { } }",
            ["Broken"] = "void f( {",
        };
        var ok = TestUtil.CompileOk("void main() { int y = Utils.twice(Utils.count); Utils.count = y; }", "Main", others);
        Assert.Contains("import Utils", ok);
        Assert.Contains("Utils.count = y", ok);

        var e1 = TestUtil.CompileErr("void main() { Utils.secret(); }", "Main", others);
        Assert.Equal("secret() has private access in Utils", e1.Message);
        var e2 = TestUtil.CompileErr("void main() { int z = Utils.hidden; }", "Main", others);
        Assert.Equal("hidden has private access in Utils", e2.Message);
        var e3 = TestUtil.CompileErr("void main() { Utils.nope(); }", "Main", others);
        Assert.Equal("cannot find symbol: method nope in Utils", e3.Message);
        var e4 = TestUtil.CompileErr("void main() { String s = Utils.twice(2); }", "Main", others);
        Assert.Equal("incompatible types: int cannot be converted to String", e4.Message);
        var e5 = TestUtil.CompileErr("void main() { Broken.f(); }", "Main", others);
        Assert.Equal("cannot find symbol: method f in Broken", e5.Message);
    }

    [Fact]
    public void MainSignatureIsChecked()
    {
        Assert.Equal("main must be declared as void main()", TestUtil.CompileErr("int main() { return 0; }", "A").Message);
        Assert.Equal("main must be declared as public static void main(String[] args)",
            TestUtil.CompileErr("class A { static void main(int x) { } }", "A").Message);
    }

    [Fact]
    public void EffectivelyFinalCapturesAreAccepted()
    {
        TestUtil.CompileOk("void main() { int x; if (canHarvest()) { x = 1; } else { x = 2; } spawnDrone(() -> quickPrint(x)); }");
        TestUtil.CompileOk("void main() { for (int i = 0; i < 3; i++) { int c = i; spawnDrone(() -> quickPrint(c)); } }");
    }

    [Fact]
    public void LambdaCannotAssignCapturedLocal()
    {
        var src = TestUtil.InMain("int n = 0;\nRunnable r = () -> { n = 1; };");
        var err = TestUtil.CompileErr(src);
        Assert.Equal("local variables referenced from a lambda expression must be final or effectively final", err.Message);
        Assert.Equal("n", TestUtil.Slice(src, err.Start, err.End));
    }

    [Fact]
    public void NeverThrowsOnGarbage()
    {
        var corpus = Directory.GetFiles(TestUtil.CorpusDir, "*.java", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        var rnd = new Random(1234);
        foreach (var src in corpus)
        {
            for (int i = 0; i < 25; i++)
            {
                int a = rnd.Next(src.Length), b = rnd.Next(src.Length);
                var cut = src.Substring(0, Math.Max(a, b)) + (i % 3 == 0 ? src.Substring(Math.Min(a, b)) : "");
                var r = JavaCompiler.Compile(cut, new CompileOptions { FileName = "X" });
                Assert.NotNull(r);
                if (!r.Success)
                {
                    Assert.NotEmpty(r.Errors);
                    Assert.DoesNotContain(r.Errors, e => e.Message.StartsWith("internal"));
                    foreach (var e in r.Errors) { Assert.InRange(e.Start, 0, cut.Length); Assert.InRange(e.End, e.Start, cut.Length); }
                }
            }
        }
        var empty = JavaCompiler.Compile(null, null);
        Assert.True(empty.Success);
        Assert.Equal("", empty.Python);
        Assert.Null(GameOracle.TryParse(empty.Python));
    }
}

public class LoweringTests
{
    [Fact]
    public void MinimalProgramSnapshot()
    {
        var py = TestUtil.CompileOk("void main() {\n    while (true) {\n        harvest();\n    }\n}\n");
        Assert.Equal("def main():\n    while True:\n        harvest()\nif __name__ == \"__main__\":\n    main()\n", py);
    }

    [Fact]
    public void LibraryHasNoGuardAndFieldsComeAfterMethods()
    {
        var py = TestUtil.CompileOk("int count = 0;\nvoid inc() { count++; }\n", "Lib");
        Assert.Equal("def inc():\n    global count\n    count += 1\ncount = 0\n", py);
    }

    [Fact]
    public void DivisionUsesFloorOnlyWhenProvablyNonNegative()
    {
        var py = TestUtil.CompileOk(TestUtil.InMain("int a = getPosX() / 2;\nint b = a / 2;\nint c = getWorldSize() % 3;\nint d = a % 3;\ndouble e = a / 2.0;\nint f = (int) (getPosX() * 1.5);\nint g = (int) (a * 1.5);"));
        Assert.Contains("a = get_pos_x() // 2", py);
        Assert.Contains("b = __jdiv(a, 2)", py);
        Assert.Contains("c = get_world_size() % 3", py);
        Assert.Contains("d = __jmod(a, 3)", py);
        Assert.Contains("e = a / 2.0", py);
        Assert.Contains("f = get_pos_x() * 1.5 // 1", py);
        Assert.Contains("g = __jtrunc(a * 1.5)", py);
        Assert.Contains("def __jdiv(a, b):", py);
        Assert.Contains("def __jtrunc(x):", py);
    }

    [Fact]
    public void RangeForRequiresAnInvariantBoundAndUnassignedCounter()
    {
        var py = TestUtil.CompileOk(TestUtil.InMain("int n = 4;\nfor (int i = 0; i < n; i++) { }\nfor (int i = 1; i <= n; i += 2) { }\nfor (int i = n; i >= 0; i--) { }\nfor (int i = 0; i < n; i++) { n--; }\nfor (int i = 0; i < n; i++) { i++; }"));
        Assert.Contains("for i in range(n):", py);
        Assert.Contains("for i in range(1, n + 1, 2):", py);
        Assert.Contains("for i in range(n, 0 - 1, -1):", py);
        Assert.Contains("while i < n:", py);
    }

    [Fact]
    public void ListSizeBoundOnlyWhenListIsNotModified()
    {
        var py = TestUtil.CompileOk("import java.util.*;\n" + TestUtil.InMain("List<Integer> l = new ArrayList<>();\nfor (int i = 0; i < l.size(); i++) { quickPrint(l.get(i)); }\nfor (int j = 0; j < l.size(); j++) { l.add(j); }"));
        Assert.Contains("for i in range(len(l)):", py);
        Assert.Contains("while j < len(l):", py);
    }

    [Fact]
    public void ReservedNamesAreRenamedConsistently()
    {
        var py = TestUtil.CompileOk("int max = 3;\nvoid print(int len) { quickPrint(len + max); }\nvoid main() { int list = 1; print(list); }\n");
        Assert.Contains("def print_j(len_j):", py);
        Assert.Contains("quick_print(len_j + max_j)", py);
        Assert.Contains("list_j = 1", py);
        Assert.Contains("print_j(list_j)", py);
        Assert.Contains("max_j = 3", py);
    }

    [Fact]
    public void NonAsciiIdentifierStartIsPrefixed()
    {
        var py = TestUtil.CompileOk(TestUtil.InMain("int été = 1;\nquickPrint(été);"));
        Assert.Contains("j_été = 1", py);
        Assert.Null(GameOracle.TryParse(py));
    }

    [Fact]
    public void LocalsShadowingFieldsOrMethodsAreRenamed()
    {
        var py = TestUtil.CompileOk("int count = 0;\nint helper() { return 1; }\nvoid main() { int count = 5; int helper = helper(); count += helper; }\n");
        Assert.Contains("count_l = 5", py);
        Assert.Contains("helper_l = helper()", py);
        Assert.Contains("count_l += helper_l", py);
        Assert.DoesNotContain("global count", py);
    }

    [Fact]
    public void LambdaIsHoistedBeforeItsStatement()
    {
        var py = TestUtil.CompileOk(TestUtil.InMain("int c = 2;\nspawnDrone(() -> quickPrint(c));"));
        Assert.Contains("    c = 2\n    def __jlambda1():\n        quick_print(c)\n    spawn_drone(__jlambda1)\n", py);
    }

    [Fact]
    public void TernaryArgumentAfterSideEffectIsRejected()
    {
        var src = TestUtil.InMain("quickPrint(harvest(), canHarvest() ? 1 : 2);");
        var err = TestUtil.CompileErr(src);
        Assert.StartsWith("?: is only supported as a whole value here", err.Message);
        Assert.Equal("canHarvest() ? 1 : 2", TestUtil.Slice(src, err.Start, err.End));
        var src2 = TestUtil.InMain("boolean b = getPosX() > 0 && plant(canHarvest() ? Entities.Bush : Entities.Grass);");
        Assert.StartsWith("?: is only supported", TestUtil.CompileErr(src2).Message);
    }

    [Fact]
    public void StringsWithBothQuoteKindsAreConcatenated()
    {
        var py = TestUtil.CompileOk(TestUtil.InMain("quickPrint(\"a'b\\\"c\");"));
        Assert.Contains("quick_print((\"a'b\" + '\"c'))", py);
    }

    [Fact]
    public void NumbersUsePlainDecimalNotation()
    {
        var py = TestUtil.CompileOk(TestUtil.InMain("double a = 1e3; double b = 2.5e-4; double c = 1e20; int d = 0x10; double e = 3d;"));
        Assert.Contains("a = 1000.0", py);
        Assert.Contains("b = 0.00025", py);
        Assert.Contains("c = 100000000000000000000.0", py);
        Assert.Contains("d = 16", py);
        Assert.Contains("e = 3.0", py);
    }

    [Fact]
    public void CollectionsLowering()
    {
        var py = TestUtil.CompileOk("import java.util.*;\n" + TestUtil.InMain(
            "List<Integer> l = new ArrayList<>();\nl.add(1);\nl.add(0, 2);\nl.set(0, 3);\nint x = l.remove(0);\nl.remove(Integer.valueOf(1));\n" +
            "Map<Item, Integer> m = new HashMap<>();\nm.put(Items.Hay, 1);\nint h = m.get(Items.Hay);\nSet<Entity> s = new HashSet<>();\ns.add(Entities.Bush);\n" +
            "boolean has = s.contains(Entities.Bush) && m.containsKey(Items.Hay) && !l.isEmpty();\nint[] a = new int[3];\nint[][] g = new int[2][3];"));
        Assert.Contains("l = []", py);
        Assert.Contains("l.append(1)", py);
        Assert.Contains("l.insert(0, 2)", py);
        Assert.Contains("l[0] = 3", py);
        Assert.Contains("x = l.pop(0)", py);
        Assert.Contains("__jremove(l, 1)", py);
        Assert.Contains("m = {}", py);
        Assert.Contains("m[Items.Hay] = 1", py);
        Assert.Contains("h = __jget(m, Items.Hay)", py);
        Assert.Contains("s = set()", py);
        Assert.Contains("s.add(Entities.Bush)", py);
        Assert.Contains("has = Entities.Bush in s and Items.Hay in m and not len(l) == 0", py);
        Assert.Contains("a = __jarr(3, 0)", py);
        Assert.Contains("g = __jarr2(2, 3, 0)", py);
    }

    [Fact]
    public void PrecedenceIsPreserved()
    {
        var py = TestUtil.CompileOk(TestUtil.InMain("int a = 1, b = 2, c = 3;\nboolean x = !(a == b) == (b < c);\nint y = a - (b - c);\nint z = -(a + b) * c;\nboolean w = !true || a > b && !(b > c);"));
        Assert.Contains("x = (not a == b) == (b < c)", py);
        Assert.Contains("y = a - (b - c)", py);
        Assert.Contains("z = -(a + b) * c", py);
        Assert.Contains("w = not True or a > b and not b > c", py);
    }
}
