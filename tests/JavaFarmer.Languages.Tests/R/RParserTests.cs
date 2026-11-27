using static JavaFarmer.Languages.Tests.R.RTestUtil;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>R syntax: precedence, newlines, else rules, error wording and spans.</summary>
public class RParserTests
{
    [Theory]
    [InlineData("print(-2 ^ 2)", "print(-__jpow(2, 2))")]
    [InlineData("print(-1 + 2)", "print(-1 + 2)")]
    [InlineData("print(7 %/% 2 * 3)", "print(7 // 2 * 3)")]
    [InlineData("print(1 + 7 %% 3)", "print(1 + 7 % 3)")]
    [InlineData("print(!TRUE == FALSE)", "print(not True == False)")]
    [InlineData("print(TRUE || FALSE && FALSE)", "print(True or False and False)")]
    [InlineData("x <- y <- 3\nprint(x)", "y = 3\nx = y\nprint(x)")]
    [InlineData("4 -> x\nprint(x)", "x = 4\nprint(x)")]
    [InlineData("x = 5; print(x)", "x = 5\nprint(x)")]
    [InlineData("print(2 ** 3)", "print(__jpow(2, 3))")]
    [InlineData("print(5L + 0x10)", "print(5 + 16)")]
    [InlineData("print(1e3)", "print(1000.0)")]
    public void Expressions(string src, string py)
    {
        Assert.Equal(py, Py(src));
    }

    [Fact]
    public void Newline_EndsAStatement_ButNotInsideParentheses()
    {
        Assert.Equal("x = 1 + 2\nprint(x)", Py("x <- 1 +\n  2\nprint(x)"));
        Assert.Equal("print(1 + 2)", Py("print(1\n  + 2)"));
        var e = Error("x <- 1\n+ 2 3");
        Assert.Contains("unexpected numeric constant", e.Message);
    }

    [Fact]
    public void Else_OnItsOwnLine_IsOnlyAcceptedInsideBraces()
    {
        var e = Error("x <- 1\nif (x > 0) {\n  print(1)\n}\nelse {\n  print(2)\n}\n");
        Assert.Contains("unexpected 'else'", e.Message);
        Assert.Equal("else", Snip("x <- 1\nif (x > 0) {\n  print(1)\n}\nelse {\n  print(2)\n}\n", e));
        Assert.Equal("x = 1\nif x > 0:\n    print(1)\nelse:\n    print(2)", Py("x <- 1\n{\n  if (x > 0) {\n    print(1)\n  }\n  else {\n    print(2)\n  }\n}\n"));
    }

    [Theory]
    [InlineData("x y", "Error: unexpected symbol in \"x y\"", "y")]
    [InlineData("f <- function() {\n  1\n", "unexpected end of input", "{")]
    [InlineData("print(1))", "unexpected ')' in \"print(1))\"", ")")]
    [InlineData("if (x = 1) 2", "unexpected '='", "=")]
    [InlineData("print(\"abc)", "unexpected INCOMPLETE_STRING", "\"abc)")]
    [InlineData("x <- 5 5", "unexpected numeric constant", "5")]
    [InlineData("x <- 'a' 'b'", "unexpected string constant", "'b'")]
    [InlineData("1 < 2 < 3", "unexpected '<'", "<")]
    [InlineData("x <- _y", "unexpected input", "_")]
    [InlineData("print(\"\\q\")", "'\\q' is an unrecognized escape", "\\q")]
    public void SyntaxErrors_ImitateR(string src, string msg, string at)
    {
        var e = Error(src);
        Assert.Contains(msg, e.Message);
        Assert.Equal(at, Snip(src, e));
    }

    [Fact]
    public void DoubleBrackets_And_NestedSingleBrackets()
    {
        Assert.Equal("v = [1, 2, 3]\nw = [1, 2]\nprint(v[w[0] - 1])", Py("v <- c(1, 2, 3)\nw <- c(1, 2)\nprint(v[w[1]])"));
        Assert.Equal("v = [[1, 2], [3]]\nprint(v[1][0])", Py("v <- list(c(1, 2), c(3))\nprint(v[[2]][[1]])"));
    }

    [Fact]
    public void BackslashLambda_And_FunctionBodyWithoutBraces()
    {
        var py = Py("twice <- \\(x) x * 2\nprint(twice(4))");
        Assert.Contains("def twice(x):\n    return x * 2", py);
    }

    [Fact]
    public void Comments_And_RoxygenDocs_AreKept()
    {
        var m = ToIr("#' Doubles x.\n#' @param x a number\nf <- function(x) {\n  # inside\n  x * 2  # trailing\n}\n# end\n");
        var f = m.Functions.Single();
        Assert.True(f.Leading[0].IsDoc);
        Assert.Equal(" Doubles x.", f.Leading[0].Text);
        Assert.Equal(" inside", f.Body.Stmts[0].Leading[0].Text);
        Assert.Equal(" trailing", f.Body.Stmts[0].Trailing.Text);
        Assert.Equal(" end", m.TrailingComments.Single().Text);
        var printed = Print(m);
        Assert.Contains("#' Doubles x.", printed);
        Assert.Contains("x * 2 # trailing", printed);
    }
}
