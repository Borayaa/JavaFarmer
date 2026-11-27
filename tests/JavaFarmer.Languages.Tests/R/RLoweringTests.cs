using static JavaFarmer.Languages.Tests.R.RTestUtil;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>R semantics in the IR: scoping, implicit returns, 1-based indexing, maps, copies, switch, loops, imports.</summary>
public class RLoweringTests
{
    [Fact]
    public void ImplicitReturn_LastExpression_AndEachBranchOfAFinalIf()
    {
        Assert.Equal("def f(x):\n    if x > 0:\n        return 1\n    else:\n        return 2", Py("f <- function(x) {\n  if (x > 0) 1 else 2\n}"));
        var m = ToIr("f <- function(x) {\n  x + 1\n}");
        var ret = Assert.IsType<IrReturn>(m.Functions.Single().Body.Stmts.Single());
        Assert.True(ret.Synthetic);
        var m2 = ToIr("f <- function(x) {\n  return(x + 1)\n}");
        Assert.False(Assert.IsType<IrReturn>(m2.Functions.Single().Body.Stmts.Single()).Synthetic);
    }

    [Fact]
    public void ImplicitReturn_NotForCallsThatReturnNothing()
    {
        // print / till are void builtins; g never returns a value, so f's last call is not returned either
        Assert.Equal("def g():\n    till()\ndef f():\n    print(1)\n    g()", Py("g <- function() till()\nf <- function() {\n  print(1)\n  g()\n}"));
        // a builtin with a value is returned
        Assert.Equal("def f():\n    return harvest()", Py("f <- function() harvest()"));
    }

    [Fact]
    public void Indexing_IsOneBased_LiteralsAreFolded()
    {
        var m = ToIr("v <- c(1, 2)\ni <- 2\nprint(v[[i]])\nprint(v[1])");
        var ix = IrWalk.Descendants(m).OfType<IrIndex>().ToList();
        Assert.Equal(1, ix[0].BaseOffset);
        Assert.Equal(0, ix[1].BaseOffset);
        Assert.Equal(0L, ((IrLiteral)ix[1].Index).Int);
        Assert.Equal("v = [1, 2]\ni = 2\nprint(v[i - 1])\nprint(v[0])", Py("v <- c(1, 2)\ni <- 2\nprint(v[[i]])\nprint(v[1])"));
    }

    [Fact]
    public void GrowAndShrink_BecomeListOps()
    {
        var py = Py("v <- c()\nv[[length(v) + 1]] <- 1\nv[length(v) + 1] <- 2\nv <- c(v, 3)\nv <- append(v, 4)\nv <- c(0, v)\nv <- append(v, 9, after = 1)\nv <- v[-1]\nv <- v[-length(v)]\nv[[2]] <- NULL\ni <- 1\nv <- v[-i]");
        Assert.Equal("v = []\nv.append(1)\nv.append(2)\nv.append(3)\nv.append(4)\nv.insert(0, 0)\nv.insert(1, 9)\nv.pop(0)\nv.pop()\nv.pop(1)\ni = 1\nv.pop(i - 1)", py);
    }

    [Fact]
    public void NamedLists_AreMaps()
    {
        var py = Py("m <- list(a = 1)\nm$b <- 2\nm[[\"c\"]] <- 3\nprint(m$a)\nprint(m[[\"b\"]])\nm[[\"a\"]] <- NULL\nprint(\"b\" %in% names(m))\nfor (k in names(m)) print(k)\nprint(length(m))");
        Assert.Equal("m = {\"a\": 1}\nm[\"b\"] = 2\nm[\"c\"] = 3\nprint(__jget(m, \"a\"))\nprint(__jget(m, \"b\"))\n__jmremove(m, \"a\")\nprint(\"b\" in m)\nfor k in m:\n    print(k)\nprint(len(m))", py);
    }

    [Fact]
    public void EmptyList_UsedWithTextKeys_IsAMap()
    {
        Assert.Equal("m = {}\nm[Items.Hay] = 1", Py("m <- list()\nm[[Items$Hay]] <- 1"));
        Assert.Equal("v = []\nv.append(1)", Py("v <- list()\nv[[length(v) + 1]] <- 1"));
    }

    [Fact]
    public void ForOverANamedList_IteratesTheValues()
    {
        Assert.Contains("for v in __jvalues(m):", Py("m <- list(a = 1, b = 2)\nfor (v in m) print(v)"));
    }

    [Fact]
    public void Copies_ListAssignments_AndChangedParameters()
    {
        Assert.Equal("a = [1, 2]\nb = list(a)\nb[0] = 5", Py("a <- c(1, 2)\nb <- a\nb[[1]] <- 5"));
        Assert.Equal("m = {\"x\": 1}\nn = dict(m)", Py("m <- list(x = 1)\nn <- m"));
        Assert.Equal("def f(v):\n    v = list(v)\n    v.append(1)\n    return v", Py("f <- function(v) {\n  v[[length(v) + 1]] <- 1\n  v\n}"));
        // a function changing a global with <- works on a local copy (R semantics)
        // (a module of only functions and variables is emitted functions first; order does not matter there)
        Assert.Equal("def f():\n    g_l = list(g)\n    g_l[0] = 2\n    return g_l\ng = [1]", Py("g <- c(1)\nf <- function() {\n  g[[1]] <- 2\n  g\n}"));
        Assert.Equal("g = [1]\ndef f():\n    g_l = list(g)\n    g_l[0] = 2\n    return g_l\nprint(f())", Py("g <- c(1)\nf <- function() {\n  g[[1]] <- 2\n  g\n}\nprint(f())"));
    }

    [Fact]
    public void Scoping_ReadBeforeLocalAssignment_IsTheGlobal()
    {
        Assert.Equal("x = 1\ndef f():\n    x_l = x + 1\n    return x_l\nprint(f())", Py("x <- 1\nf <- function() {\n  x <- x + 1\n  x\n}\nprint(f())"));
        Assert.Equal("x = 1\ndef f():\n    global x\n    x = x + 1\nf()", Py("x <- 1\nf <- function() {\n  x <<- x + 1\n}\nf()"));
        // a top-level name only used at the top level stays a script local
        var m = ToIr("y <- 1\nprint(y)");
        Assert.Equal(IrSymbolKind.Local, IrWalk.Descendants(m).OfType<IrName>().First().Symbol.Kind);
    }

    [Fact]
    public void Switch_FallThrough_Default_AndAssignment()
    {
        Assert.Equal("s = \"b\"\nif s == \"a\" or s == \"b\":\n    print(1)\nelse:\n    print(2)", Py("s <- \"b\"\nswitch(s, a = , b = print(1), print(2))"));
        Assert.Equal("s = \"b\"\nif s == \"a\":\n    y = 1\nelse:\n    y = None\nprint(y)", Py("s <- \"b\"\ny <- switch(s, a = 1)\nprint(y)"));
    }

    [Fact]
    public void ForSequences()
    {
        Assert.Equal("for i in range(1, 5 + 1):\n    print(i)", Py("for (i in 1:5) print(i)"));
        Assert.Equal("for i in range(5, 1 - 1, -1):\n    print(i)", Py("for (i in 5:1) print(i)"));
        Assert.Equal("for i in range(0, 10 + 1, 5):\n    print(i)", Py("for (i in seq(0, 10, by = 5)) print(i)"));
        Assert.Equal("n = 3\nfor i in range(n):\n    print(i)", Py("n <- 3\nfor (i in seq_len(n) - 1) print(i)"));
        Assert.Equal("v = [4, 5]\nfor x in v:\n    print(x)", Py("v <- c(4, 5)\nfor (x in v) print(x)"));
    }

    [Fact]
    public void If_AsAValue()
    {
        Assert.Equal("x = 1\nif x > 0:\n    y = \"a\"\nelse:\n    y = \"b\"", Py("x <- 1\ny <- if (x > 0) \"a\" else \"b\""));
        var m = ToIr("x <- 1\ny <- if (x > 0) \"a\" else \"b\"");
        // a script local: IrDeclarations.Place turns its first assignment into the declaration
        var last = m.Items.Last();
        Assert.IsType<IrConditional>(last is IrVarDecl vd ? vd.Init : ((IrAssign)last).Value);
    }

    [Fact]
    public void Paste_Cat_And_Conversions()
    {
        Assert.Equal("n = 1\nprint(\"n\" + \" \" + str(n))", Py("n <- 1\nprint(paste(\"n\", n))"));
        Assert.Equal("n = 1\nprint(\"n=\" + str(n))", Py("n <- 1\nprint(paste0(\"n=\", n))"));
        Assert.Equal("n = 1\nprint(\"n\", n)\nprint(\"done\")\nprint(\"\")", Py("n <- 1\ncat(\"n\", n, \"\\n\")\ncat(\"done\\n\")\ncat(\"\\n\")"));
        Assert.Equal("x = 2.5\nprint(str(x), __jtrunc(x))", Py("x <- 2.5\ncat(as.character(x), as.integer(x), \"\\n\")"));
    }

    [Fact]
    public void Constants_Directions_AndAnyMemberName()
    {
        Assert.Equal("plant(Entities.Some_Future_Plant)\nmove(North)", Py("plant(Entities$Some_Future_Plant)\nmove(North)"));
    }


    [Fact]
    public void Source_ImportsTheOtherWindowsFunctions()
    {
        var others = new Dictionary<string, string> { ["utils"] = "helper <- function(a, b = 2) a + b\nlimit <- 3\n" };
        Assert.Equal("import utils\nprint(utils.helper(1))\nprint(utils.limit)", Py("source(\"utils.R\")\nprint(helper(1))\nprint(limit)", others));
        var e = Error("source(\"utils.R\")\nhelper(1, 2, 3)", others);
        Assert.Contains("unused argument (3)", e.Message);
        // without OtherFiles (ToIr for conversion) unknown names bind to the sourced file
        var m = ToIr("source(\"utils.R\")\nprint(helper(1))");
        Assert.Contains(IrWalk.Descendants(m).OfType<IrCall>(), c => c.Function?.Module == "utils");
    }

    [Fact]
    public void DottedNames_BecomeUnderscores()
    {
        Assert.Equal("my_count = 1\nprint(my_count)", Py("my.count <- 1\nprint(my.count)"));
    }

    [Fact]
    public void Closures_AndSpawnDrone()
    {
        // (Py() drops def __j... helpers, so read the whole output: lambdas are lifted to def __jlambdaN)
        var py = Compile("task <- function() harvest()\nspawn_drone(task)\nspawn_drone(function() move(North))").Python.Replace("\r\n", "\n");
        Assert.Contains("spawn_drone(task)", py);
        Assert.Contains("def __jlambda1():\n    return move(North)\nspawn_drone(__jlambda1)", py);
    }
}

/// <summary>Registers a test builtin with GameApi: runs in the non-parallel GameApiRegistrations collection.</summary>
[Collection(GameApiRegistrations.Name)]
public class RRegisteredBuiltinsTests
{
    [Fact]
    public void RegisteredBuiltins_WorkWithoutCodeChanges()
    {
        GameApi.Register("scan_port_r_test", "host: string, port?: int", "bool", "Scans a port.", extension: "cybersecurity");
        try
        {
            Assert.Equal("print(scan_port_r_test(\"a\", 80))", Py("print(scan_port_r_test(\"a\", port = 80))"));
            var e = Error("scan_port_r_test()");
            Assert.Contains("argument \"host\" is missing", e.Message);
            Assert.Contains("scan_port_r_test <- function(host, port = NULL)", Lang.ApiStubFile(out _));
        }
        finally { GameApi.Unregister("scan_port_r_test"); }
    }

}
