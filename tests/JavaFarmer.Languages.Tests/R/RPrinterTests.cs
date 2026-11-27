using static JavaFarmer.Languages.Tests.R.RTestUtil;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>IR from Python / Java → idiomatic R that compiles back to the same game Python shape.</summary>
public class RPrinterTests
{
    static readonly ILanguage Python = Compiler.Languages.Get("python");
    static readonly ILanguage Java = Compiler.Languages.Get("java");

    static string FromPython(string py, bool snippet = false)
    {
        var ir = Python.ToIr(py, new CompileOptions { FileName = "Docs" }, out var errs);
        Assert.True(ir != null, string.Join("; ", errs));
        return Lang.Print(ir, new PrintOptions { FileName = "Docs", Snippet = snippet });
    }

    static void SameShape(string pySource)
    {
        var r = FromPython(pySource);
        var back = Lang.Compile(r, new CompileOptions { FileName = "Docs" });
        Assert.True(back.Success, string.Join("; ", back.Errors) + "\n" + r);
        Assert.Equal(Normalise.PythonShape(pySource), Normalise.PythonShape(back.Python));
    }

    [Fact]
    public void Python_RangeLoops_PrintCorrectR()
    {
        Assert.Equal("for (i in seq_len(10) - 1) {\n\tprint(i)\n}\n", FromPython("for i in range(10):\n\tprint(i)\n"));
        Assert.Equal("for (i in 2:(n - 1)) {\n\tprint(i)\n}\n", FromPython("for i in range(2, n):\n\tprint(i)\n", true));
        Assert.Equal("for (i in seq(0, 9, by = 3)) {\n\tprint(i)\n}\n", FromPython("for i in range(0, 10, 3):\n\tprint(i)\n"));
    }

    [Fact]
    public void Python_ZeroBasedIndexing_IsShiftedAndFolded()
    {
        var r = FromPython("l = [1, 2, 3]\nprint(l[0])\ni = 1\nprint(l[i])\n");
        Assert.Contains("l <- c(1, 2, 3)", r);
        Assert.Contains("print(l[1])", r);
        Assert.Contains("print(l[i + 1])", r);
    }

    [Fact]
    public void Python_Functions_ReturnBareValueAtTheEnd_AndGlobalsUseSuperAssignment()
    {
        var r = FromPython("count = 0\ndef inc(n):\n\tglobal count\n\tcount += n\n\treturn count\n");
        Assert.Contains("inc <- function(n) {\n\tcount <<- count + n\n\tcount\n}", r);
        var r2 = FromPython("def f(x):\n\tif x > 0:\n\t\treturn 1\n\treturn 2\n");
        Assert.Contains("if (x > 0) {\n\t\treturn(1)\n\t}\n\t2\n", r2);
    }

    [Fact]
    public void Python_Dicts_Sets_AndAppends()
    {
        var r = FromPython("d = {\"a\": 1}\nd[\"b\"] = 2\nif \"a\" in d:\n\tprint(d[\"a\"])\ns = set()\ns.add(3)\nl = []\nl.append(4)\n");
        Assert.Contains("d <- list(a = 1)", r);
        Assert.Contains("d[[\"b\"]] <- 2", r);
        Assert.Contains("\"a\" %in% names(d)", r);
        Assert.Contains("if (!(3 %in% s)) s[[length(s) + 1]] <- 3", r);
        Assert.Contains("l[[length(l) + 1]] <- 4", r);
        var keyed = FromPython("d = {Items.Hay: 1}\nprint(d)\n");
        Assert.Contains("setNames(list(1), c(Items$Hay))", keyed);
    }

    [Fact]
    public void Python_RoundTrips_KeepTheShape()
    {
        SameShape("x = 0\nwhile x < 10:\n\tx = x + 1\n\tif x % 2 == 0:\n\t\tcontinue\n\tprint(x)\n");
        SameShape("def f(a, b):\n\treturn a * b\nprint(f(2, 3))\n");
        SameShape("for i in range(5):\n\tmove(North)\n");
    }

    [Fact]
    public void Keywords_AndRFunctionNames_AreRenamed()
    {
        var r = FromPython("c = 1\nlength = 2\nT = 3\nprint(c + length + T)\n");
        Assert.Contains("c_ <- 1", r);
        Assert.Contains("length_ <- 2", r);
        Assert.Contains("T_ <- 3", r);
        Assert.True(Lang.Compile(r, new CompileOptions { FileName = "Docs" }).Success, r);
    }

    [Fact]
    public void Java_Program_PrintsAsAScript()
    {
        var src = "int total = 0;\nvoid add(int n) { total += n; }\nvoid main() {\n    for (int i = 0; i < 3; i++) add(i);\n    System.out.println(total);\n}\n";
        var ir = Java.ToIr(src, new CompileOptions { FileName = "Main" }, out var errs);
        Assert.True(ir != null, string.Join("; ", errs));
        var r = Lang.Print(ir, new PrintOptions { FileName = "Main" });
        Assert.Contains("add <- function(n) {\n\ttotal <<- total + n\n}", r);
        Assert.Contains("for (i in seq_len(3) - 1) {", r);
        Assert.Contains("print(total)", r);
        var back = Lang.Compile(r, new CompileOptions { FileName = "Main" });
        Assert.True(back.Success, string.Join("; ", back.Errors) + "\n" + r);
    }

    [Fact]
    public void Problems_AreReported_ForTuples()
    {
        var ir = Python.ToIr("a, b = 1, 2\n", new CompileOptions { FileName = "Docs" }, out _);
        Lang.Print(ir, new PrintOptions(), out var problems);
        Assert.Contains(problems, p => p.Contains("tuple"));
    }

    [Fact]
    public void OwnIr_PrintsIdiomaticR()
    {
        var src = "s <- \"a\"\ny <- switch(s, a = 1, b = , c = 2, 3)\nf <- function(v) {\n  v[[1]] <- 0\n  v\n}\nprint(paste(\"y\", y))\nfor (i in seq_along(c(1, 2))) print(match(2, c(1, 2)))\n";
        var printed = Print(ToIr(src));
        Assert.Contains("y <- switch(s, a = 1, b = , c = 2, 3)", printed);
        Assert.Contains("f <- function(v) {\n\tv[1] <- 0\n\tv\n}", printed);
        Assert.Contains("print(paste(\"y\", y))", printed);
        Assert.Contains("for (i in seq_along(c(1, 2))) {", printed);
        Assert.Contains("print(match(2, c(1, 2)))", printed);
        Assert.Equal(Compile(src).Python, Compile(printed).Python);
    }
}
