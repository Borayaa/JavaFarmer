using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>Lexer and parser: automatic semicolon insertion, template literals, numbers, V8 error wording.</summary>
public class JsSyntaxTests
{
    [Fact]
    public void Asi_StatementsEndAtLineBreaks()
    {
        var py = ToPy("let a = 1\nlet b = 2\nconsole.log(a + b)\n");
        Assert.Contains("a = 1\nb = 2\nprint(a + b)", py);
    }

    [Fact]
    public void Asi_ExpressionContinuesOnTheNextLine()
    {
        var py = ToPy("const total = 1 +\n\t2\nconst more = total\n\t* 3\nconsole.log(more)\n");
        Assert.Contains("total = 1 + 2", py);
        Assert.Contains("more = total * 3", py);
    }

    [Fact]
    public void Asi_ReturnFollowedByNewlineReturnsNothing()
    {
        var py = ToPy("function f() {\n\treturn\n\t42\n}\nf()\n");
        Assert.Contains("    return\n", py);
    }

    [Fact]
    public void Asi_PostfixIncrementAfterNewlineBelongsToTheNextLine()
    {
        var py = ToPy("let a = 1\nlet b = 2\na\n++b\nconsole.log(a, b)\n");
        Assert.Contains("b += 1", py);
        Assert.DoesNotContain("a += 1", py);
    }

    [Fact]
    public void Asi_TwoStatementsOnOneLineNeedASemicolon()
    {
        var e = Error("let x = 1 harvest()\n");
        Assert.Equal("Unexpected identifier 'harvest'", e.Message);
    }

    [Fact]
    public void Asi_BlockEndAndEndOfInputEndStatements()
    {
        var py = ToPy("if (canHarvest()) { harvest() }\nconsole.log(1)");
        Assert.Contains("harvest()", py);
        Assert.Contains("print(1)", py);
    }

    [Fact]
    public void Template_PartsBecomeConcatenation()
    {
        var py = ToPy("const n = 3\nconsole.log(`a ${n} b ${n * 2}`)\n");
        Assert.Contains("print(\"a \" + str(n) + \" b \" + str(n * 2))", py);
    }

    [Fact]
    public void Template_LoneSubstitutionIsStr() => Assert.Contains("s = str(get_pos_x())", ToPy("const s = `${getPosX()}`\n"));

    [Fact]
    public void Template_NestedTemplatesAndBraces()
    {
        var py = ToPy("const m = new Map([[\"a\", 1]])\nconsole.log(`x ${`in ${m.get(\"a\") ?? 0}`} y`)\n");
        Assert.Contains("__jgetd(m, \"a\", 0)", py);
    }

    [Fact]
    public void Template_NoSubstitutionIsAString() => Assert.Contains("s = \"plain\"", ToPy("const s = `plain`\n"));

    [Fact]
    public void Template_UnterminatedIsReported() => Assert.Equal("Unterminated template literal", Error("const s = `abc ${1}\n").Message);

    [Fact]
    public void Template_LineBreakIsRejected() => Assert.Contains("line breaks", Error("const s = `a\nb`\n").Message);

    [Theory]
    [InlineData("1_000", "1000")]
    [InlineData("0xff", "255")]
    [InlineData("0b101", "5")]
    [InlineData("0o17", "15")]
    [InlineData("1e3", "1000")]
    [InlineData("2.5", "2.5")]
    [InlineData(".5", "0.5")]
    public void Numbers(string js, string py) => Assert.Contains("x = " + py + "\n", ToPy("const x = " + js + "\nconsole.log(x)\n"));

    [Fact]
    public void RegexVersusDivision()
    {
        Assert.Contains("a / 2 / 3", ToPy("const a = 6\nconsole.log(a / 2 / 3)\n"));
        Assert.Equal("regular expression literals are not supported in JavaFarmer", Error("const r = /a+b/g\n").Message);
    }

    [Theory]
    [InlineData("f(1 2)", "missing ) after argument list", "2")]
    [InlineData("if (x {\n}", "Unexpected token '{'", "{")]
    [InlineData("let x = 'abc", "Invalid or unexpected token", "'abc")]
    [InlineData("let x = 1;\nlet x = 2;", "Identifier 'x' has already been declared", "x")]
    [InlineData("const x;", "Missing initializer in const declaration", "x")]
    [InlineData("break;", "Illegal break statement", "break")]
    [InlineData("continue;", "Illegal continue statement: no surrounding iteration statement", "continue")]
    [InlineData("return 1;", "Illegal return statement", "return")]
    [InlineData("let x = 08;", "Decimals with leading zeros are not allowed in strict mode.", "08")]
    [InlineData("-2 ** 2", "Unary operator used immediately before exponentiation expression", "-2")]
    [InlineData("const a = 1, b = 2; const c = a ?? b || 1;", "Unexpected token '||'", "||")]
    [InlineData("let static = 1;", "Unexpected strict mode reserved word", "static")]
    [InlineData("function f(a b) { }", "Unexpected identifier 'b'", "b")]
    [InlineData("let [a, b];", "Missing initializer in destructuring declaration", "[a, b]")]
    [InlineData("const x = 1 @", "Invalid or unexpected token", "@")]
    public void V8Messages(string src, string msg, string at)
    {
        var e = Error(src);
        Assert.Contains(msg, e.Message);
        Assert.Equal(at, Snip(src, e));
    }

    [Fact]
    public void UnexpectedEndOfInputPointsAtTheLastToken()
    {
        var src = "while (true) {\n\tharvest();\n";
        var e = Error(src);
        Assert.Equal("Unexpected end of input", e.Message);
        Assert.Equal(";", Snip(src, e));
    }

    [Theory]
    [InlineData("try { } catch (e) { }", "try/catch is not supported in JavaFarmer")]
    [InlineData("class A { }", "classes are not supported in JavaFarmer")]
    [InlineData("function* g() { }", "generator functions are not supported in JavaFarmer")]
    [InlineData("const f = async () => 1;", "async functions are not supported in JavaFarmer")]
    [InlineData("a: for (;;) { break; }", "labels are not supported in JavaFarmer")]
    [InlineData("const o = { a: 1 }; delete o.a;", "the delete operator is not supported in JavaFarmer")]
    [InlineData("const t = typeof 1;", "typeof is not supported in JavaFarmer")]
    [InlineData("const x = 1 | 2;", "the bitwise operator '|' is not supported in JavaFarmer")]
    [InlineData("const x = ~1;", "the bitwise operator '~' is not supported in JavaFarmer")]
    [InlineData("let x = 1; x <<= 2;", "the bitwise operator '<<' is not supported in JavaFarmer")]
    [InlineData("const m = new Map(); const s = m?.size;", "optional chaining (?.) is not supported in JavaFarmer")]
    [InlineData("const o = { f() { return 1; } };", "methods in object literals are not supported in JavaFarmer")]
    [InlineData("const p = {}; const o = { ...p };", "object spread (...) is not supported in JavaFarmer")]
    [InlineData("const x = 5n;", "BigInt literals are not supported in JavaFarmer")]
    [InlineData("let a = 0; a = 1, a = 2;", "the comma operator is not supported in JavaFarmer")]
    [InlineData("let a = 0; a ||= 1;", "logical assignment (||=) is not supported in JavaFarmer")]
    [InlineData("const o = {}; const { a } = o;", "object destructuring is not supported in JavaFarmer")]
    [InlineData("function f(...xs) { }", "rest parameters (...) are not supported in JavaFarmer")]
    [InlineData("throw 1;", "throw is not supported in JavaFarmer")]
    [InlineData("export default 1;", "export default is not supported in JavaFarmer")]
    [InlineData("function f({ a }) { }", "destructuring parameters are not supported in JavaFarmer")]
    [InlineData("const a = [1]; const b = a.at(-1);", "'.at()' is not supported in JavaFarmer: use a[i] (a[a.length - 1] for the last item)")]
    [InlineData("const x = 1.5; const t = x.toFixed(1);", "'.toFixed()' is not supported in JavaFarmer (the game has no number formatting)")]
    [InlineData("const s = \"a\"; const t = s.toUpperCase();", "'.toUpperCase()' is not supported in JavaFarmer (the game has no string methods)")]
    public void NotSupported(string src, string msg) => Assert.Equal(msg, Error(src).Message);

    [Fact]
    public void Comments_AreKeptOnTheIr()
    {
        var m = ToIr("// lead\nconst a = 1; // trail\n/* block */\nconsole.log(a);\n// end\n");
        var dump = Dump(m);
        Assert.Contains("#<lead>", dump);
        Assert.Contains("#>trail", dump);
        Assert.Contains("#<block>", dump);
        Assert.Contains("#end end", dump);
    }
}
