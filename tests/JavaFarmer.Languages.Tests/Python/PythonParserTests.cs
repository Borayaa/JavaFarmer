namespace JavaFarmer.Languages.Tests.Python;

/// <summary>Python frontend: statements, expressions, scoping, spans, comments and errors.</summary>
public class PythonParserTests
{
    static IrModule Ir(string s) => PyTestUtil.ToIr(s);
    static string D(string s) => PyTestUtil.Dump(Ir(s));
    static string Text(string src, Span s) => src.Substring(s.Start, s.End - s.Start);
    static T First<T>(IrNode n) where T : IrNode => IrWalk.Descendants(n).OfType<T>().First();

    // ------------------------------------------------------------------ statements
    [Fact]
    public void Assignments_AndAugmentedOperators()
    {
        var m = Ir("a = 1\na += 2\na -= 1\na *= 3\na /= 2\na //= 2\na %= 5\na **= 2\n");
        var ops = IrWalk.Descendants(m).OfType<IrAssign>().Select(x => x.Op).ToList();
        Assert.Equal(new IrBinOp?[] { IrBinOp.Add, IrBinOp.Sub, IrBinOp.Mul, IrBinOp.TrueDiv, IrBinOp.IntDivFloor, IrBinOp.ModFloor, IrBinOp.Pow }, ops);
        Assert.IsType<IrVarDecl>(m.Items[0]);   // first plain assignment of a script local becomes its declaration
    }

    [Fact]
    public void TupleUnpacking_AndIndexAssignment()
    {
        var m = Ir("a, b = 1, 2\nx, y = get_pos_x(), get_pos_y()\nd = {}\nd[(1, 2)] = a\n");
        var a = IrWalk.Descendants(m).OfType<IrAssign>().First();
        Assert.IsType<IrTupleLit>(a.Target);
        Assert.IsType<IrTupleLit>(a.Value);
        var ix = IrWalk.Descendants(m).OfType<IrAssign>().Last();
        Assert.IsType<IrIndex>(ix.Target);
        Assert.IsType<IrTupleLit>(((IrIndex)ix.Target).Index);
    }

    [Fact]
    public void If_Elif_Else()
    {
        var m = Ir("if a:\n\tdo_a_flip()\nelif b:\n\tharvest()\nelse:\n\tif c:\n\t\ttill()\n");
        var i = (IrIf)m.Items[0];
        Assert.IsType<IrIf>(i.Else);                 // elif
        var e = (IrIf)i.Else;
        Assert.IsType<IrBlock>(e.Else);              // else: if → a block holding an if
        Assert.IsType<IrIf>(((IrBlock)e.Else).Stmts[0]);
    }

    [Fact]
    public void For_Range_ForEach_While()
    {
        var m = Ir("for i in range(5):\n\tpass\nfor i in range(1, 5):\n\tpass\nfor i in range(10, 0, -2):\n\tpass\nfor e in [1, 2]:\n\tbreak\nwhile True:\n\tcontinue\n");
        var rs = IrWalk.Descendants(m).OfType<IrRangeFor>().ToList();
        Assert.Equal(3, rs.Count);
        Assert.Null(rs[0].Start); Assert.Null(rs[0].Step);
        Assert.NotNull(rs[1].Start); Assert.Null(rs[1].Step);
        Assert.IsType<IrUnary>(rs[2].Step);
        Assert.Single(IrWalk.Descendants(m).OfType<IrForEach>());
        Assert.Single(IrWalk.Descendants(m).OfType<IrBreak>());
        Assert.Single(IrWalk.Descendants(m).OfType<IrContinue>());
        Assert.True(rs[0].Body.Stmts.Count == 0);    // pass → empty block
    }

    [Fact]
    public void ShadowedRange_IsAForEach()
    {
        var m = Ir("def range(n):\n\treturn [n]\nfor i in range(3):\n\tpass\n");
        Assert.Empty(IrWalk.Descendants(m).OfType<IrRangeFor>());
        Assert.Single(IrWalk.Descendants(m).OfType<IrForEach>());
    }

    [Fact]
    public void TupleForTarget_UnpacksInTheBody()
    {
        var m = Ir("for a, b in [(1, 2)]:\n\tprint(a, b)\n");
        var fe = First<IrForEach>(m);
        var un = fe.Body.Stmts.OfType<IrAssign>().First();
        Assert.IsType<IrTupleLit>(un.Target);
        Assert.True(un.Synthetic);
        Assert.Equal("for a, b in [(1, 2)]:\n\tprint(a, b)\n", PyTestUtil.Print(m));
    }

    [Fact]
    public void Def_DefaultsReturnTupleAndClosures()
    {
        var m = Ir("def f(a, b = False):\n\tdef d():\n\t\treturn a\n\treturn d, b\n\nf(1)()\n");
        var f = m.Functions.Single();
        Assert.Equal(IrSymbolKind.Function, f.Symbol.Kind);
        Assert.Same(f, f.Symbol.Function);
        Assert.Equal(IrSymbolKind.Param, f.Params[0].Symbol.Kind);
        Assert.IsType<IrLiteral>(f.Params[1].Default);
        var nested = Assert.IsType<IrVarDecl>(f.Body.Stmts[0]);
        var lam = Assert.IsType<IrLambda>(nested.Init);
        Assert.Equal(IrSymbolKind.Local, nested.Symbol.Kind);
        // the closure reads the enclosing param
        Assert.Same(f.Params[0].Symbol, First<IrName>(lam.Body).Symbol);
        Assert.IsType<IrTupleLit>(((IrReturn)f.Body.Stmts[1]).Value);
        var call = (IrCall)((IrExprStmt)m.Items[1]).Expr;
        Assert.Equal(IrCallKind.Value, call.Kind);
        Assert.Equal(IrCallKind.Function, ((IrCall)call.Callee).Kind);
    }

    [Fact]
    public void Globals_AndScriptLocals()
    {
        const string src = "x = 0\ny = 1\n\ndef f():\n\tglobal x\n\tx = y + 1\n\ty2 = 3\n\treturn y2\nz = 5\nf()\nprint(x, z)\n";
        var m = Ir(src);
        var gs = m.Globals.ToList();
        Assert.Equal(new[] { "x", "y" }, gs.Select(g => g.Symbol.Name));
        Assert.All(gs, g => Assert.Equal(IrSymbolKind.Global, g.Symbol.Kind));
        Assert.All(gs, g => Assert.NotNull(g.Init));
        var f = m.Functions.Single();
        Assert.IsType<IrAssign>(f.Body.Stmts[0]);    // 'global x' dropped, x = y + 1 assigns the global
        Assert.Equal(IrSymbolKind.Global, ((IrName)((IrAssign)f.Body.Stmts[0]).Target).Symbol.Kind);
        var z = IrWalk.Descendants(m).OfType<IrVarDecl>().Single(v => v.Symbol.Name == "z");
        Assert.Equal(IrSymbolKind.Local, z.Symbol.Kind);
        Assert.Contains(m.Items, i => i == z);       // script-level declaration
    }

    [Fact]
    public void LocalShadowsGlobalName()
    {
        var m = Ir("x = 0\n\ndef f():\n\tx = 1\nf()\nprint(x)\n");
        var fx = IrWalk.Descendants(m.Functions.Single()).OfType<IrVarDecl>().Single().Symbol;
        Assert.Equal(IrSymbolKind.Local, fx.Kind);
        var top = IrWalk.Descendants(m).OfType<IrVarDecl>().First(v => v != null && !IrWalk.Descendants(m.Functions.Single()).Contains(v)).Symbol;
        Assert.NotSame(fx, top);
        Assert.Equal(IrSymbolKind.Local, top.Kind);   // no function reads the top-level x
    }

    [Fact]
    public void Imports()
    {
        var m = Ir("import utils\nfrom tools import a, b\nutils.go(1)\nprint(utils.count, a())\n");
        var imps = IrWalk.Descendants(m).OfType<IrImport>().ToList();
        Assert.Equal("utils", imps[0].Module);
        Assert.Null(imps[0].Names);
        Assert.Equal(new[] { "a", "b" }, imps[1].Names);
        var go = IrWalk.Descendants(m).OfType<IrCall>().First(c => c.Function?.Name == "go");
        Assert.Equal(IrSymbolKind.ExternalFunction, go.Function.Kind);
        Assert.Equal("utils", go.Function.Module);
        var cnt = IrWalk.Descendants(m).OfType<IrName>().First(n => n.Symbol.Name == "count");
        Assert.Equal(IrSymbolKind.ExternalGlobal, cnt.Symbol.Kind);
        var a = IrWalk.Descendants(m).OfType<IrCall>().First(c => c.Function?.Name == "a");
        Assert.Equal("tools", a.Function.Module);
        var star = Ir("from lib import *\nhelper()\n");
        Assert.True(First<IrImport>(star).ImportAll);
        Assert.Equal("lib", First<IrCall>(star).Function.Module);
    }

    // ------------------------------------------------------------------ expressions
    [Fact]
    public void Precedence_FollowsTheGame()
    {
        Assert.Contains("Binary Add\n      Literal Int=1\n      Binary Mul", D("x = 1 + 2 * 3\n"));
        var notEq = First<IrUnary>(Ir("x = not a == b\n"));
        Assert.Equal(IrUnOp.Not, notEq.Op);
        Assert.IsType<IrBinary>(notEq.Operand);
        var neg = First<IrUnary>(Ir("x = -2 ** 2\n"));
        Assert.Equal(IrUnOp.Neg, neg.Op);
        Assert.Equal(IrBinOp.Pow, ((IrBinary)neg.Operand).Op);
        // the game's ** is left-associative
        var pow = (IrBinary)((IrVarDecl)Ir("x = 2 ** 3 ** 2\n").Items[0]).Init;
        Assert.Equal(IrBinOp.Pow, ((IrBinary)pow.Left).Op);
        Assert.IsType<IrLiteral>(pow.Right);
        var andOr = (IrBinary)((IrVarDecl)Ir("x = a or b and c\n").Items[0]).Init;
        Assert.Equal(IrBinOp.Or, andOr.Op);
        Assert.Equal(IrBinOp.And, ((IrBinary)andOr.Right).Op);
    }

    [Fact]
    public void Operators_Map()
    {
        var m = Ir("x = 7 / 2\ny = 7 // 2\nz = 7 % 2\nw = 2 ** 8\n");
        var ops = IrWalk.Descendants(m).OfType<IrBinary>().Select(b => b.Op).ToList();
        Assert.Equal(new[] { IrBinOp.TrueDiv, IrBinOp.IntDivFloor, IrBinOp.ModFloor, IrBinOp.Pow }, ops);
    }

    [Fact]
    public void ChainedComparison_DuplicatesTheMiddleOperand()
    {
        var m = Ir("x = 1 < y <= 3\n");
        var and = (IrBinary)((IrVarDecl)m.Items[0]).Init;
        Assert.Equal(IrBinOp.And, and.Op);
        Assert.True(and.Synthetic);
        var l = (IrBinary)and.Left; var r = (IrBinary)and.Right;
        Assert.Equal(IrBinOp.Lt, l.Op); Assert.Equal(IrBinOp.Le, r.Op);
        Assert.False(l.Right.Synthetic);
        Assert.True(r.Left.Synthetic);
        Assert.NotSame(l.Right, r.Left);
        Assert.Same(((IrName)l.Right).Symbol, ((IrName)r.Left).Symbol);
        Assert.Equal("x = 1 < y <= 3\n", PyTestUtil.Print(m));
    }

    [Fact]
    public void In_And_NotIn()
    {
        var m = Ir("a = 1 in c\nb = 2 not in c\n");
        var c1 = (IrOpCall)((IrVarDecl)m.Items[0]).Init;
        Assert.Equal(IrOp.Contains, c1.Op);
        Assert.IsType<IrName>(c1.Args[0]);           // container first
        var n = (IrUnary)((IrVarDecl)m.Items[1]).Init;
        Assert.Equal(IrUnOp.Not, n.Op);
        Assert.Equal(IrOp.Contains, ((IrOpCall)n.Operand).Op);
    }

    [Fact]
    public void ConditionalExpression()
    {
        var c = (IrConditional)((IrVarDecl)Ir("x = 1 if a else 2\n").Items[0]).Init;
        Assert.IsType<IrName>(c.Cond);
        Assert.Equal(1, ((IrLiteral)c.Then).Int);
    }

    [Fact]
    public void Constants_Literals_AndStringsWithoutEscapes()
    {
        var src = "a = Entities.Carrot\nb = North\nc = 'it\\n'\nd = .5\ne = None\nf = 12\n";
        var m = Ir(src);
        var k = First<IrConst>(m);
        Assert.Equal("Entities", k.Category); Assert.Equal("Carrot", k.Name);
        Assert.Equal("Directions", IrWalk.Descendants(m).OfType<IrConst>().Last().Category);
        var s = IrWalk.Descendants(m).OfType<IrLiteral>().Single(l => l.Kind == IrLitKind.String);
        Assert.Equal("it\\n", s.Str);                // the game does not process escapes
        Assert.Contains(IrWalk.Descendants(m).OfType<IrLiteral>(), l => l.Kind == IrLitKind.Float && l.Float == 0.5);
        Assert.Contains(IrWalk.Descendants(m).OfType<IrLiteral>(), l => l.Kind == IrLitKind.Null);
        Assert.Contains(IrWalk.Descendants(m).OfType<IrLiteral>(), l => l.Kind == IrLitKind.Int && l.Int == 12);
    }

    [Fact]
    public void Builtins_AndCollectionOps()
    {
        var m = Ir("l = []\nl.append(1)\nl.insert(0, 2)\nl.pop()\nl.remove(2)\ns = set()\ns.add(1)\nd = {}\nd.pop(3)\nn = len(l)\nt = str(n)\nc = list(s)\nk = set(l)\nm2 = dict(d)\nharvest()\nmove(North)\n");
        var ops = IrWalk.Descendants(m).OfType<IrOpCall>().Select(o => o.Op).ToList();
        Assert.Equal(new[] { IrOp.ListAppend, IrOp.ListInsert, IrOp.ListPop, IrOp.RemoveValue, IrOp.SetAdd, IrOp.MapRemove, IrOp.Len, IrOp.ListCopy, IrOp.SetCopy, IrOp.MapCopy }, ops);
        Assert.IsType<IrSetLit>(IrWalk.Descendants(m).OfType<IrVarDecl>().Single(v => v.Symbol.Name == "s").Init);
        Assert.IsType<IrMapLit>(IrWalk.Descendants(m).OfType<IrVarDecl>().Single(v => v.Symbol.Name == "d").Init);
        Assert.Equal(IrTypeKind.String, First<IrCast>(m).Target.Kind);
        var calls = IrWalk.Descendants(m).OfType<IrCall>().ToList();
        Assert.All(calls, c => Assert.Equal(IrCallKind.Builtin, c.Kind));
        Assert.Equal(new[] { "harvest", "move" }, calls.Select(c => c.Builtin));
    }

    [Fact]
    public void Concat_WhenASideIsAString()
    {
        var m = Ir("a = \"x\" + str(1)\nb = 1 + 2\nc = \"p\"\nc += \"q\"\n");
        var bins = IrWalk.Descendants(m).OfType<IrBinary>().ToList();
        Assert.Equal(IrBinOp.Concat, bins[0].Op);
        Assert.Equal(IrBinOp.Add, bins[1].Op);
        Assert.Equal(IrBinOp.Concat, IrWalk.Descendants(m).OfType<IrAssign>().Last().Op);
    }

    [Fact]
    public void Calls_UndefinedValuesAndFunctionValues()
    {
        var m = Ir("def f(g, arg):\n\tg(arg)\n\nf(move, North)\ndo_something()\nspawn_drone(f)\n");
        var gcall = First<IrCall>(m.Functions.Single());
        Assert.Equal(IrCallKind.Value, gcall.Kind);
        var top = IrWalk.Descendants(m).OfType<IrCall>().Where(c => !IrWalk.Descendants(m.Functions.Single()).Contains(c)).ToList();
        Assert.Equal("move", ((IrFuncRef)top[0].Args[0]).Builtin);
        Assert.Equal(IrSymbolKind.Function, top[1].Function.Kind);
        Assert.Null(top[1].Function.Function);           // defined nowhere
        Assert.Equal("do_something", top[1].Function.Name);
        Assert.Same(m.Functions.Single().Symbol, ((IrFuncRef)top[2].Args[0]).Function);
    }

    // ------------------------------------------------------------------ spans
    [Fact]
    public void Spans_AreSet()
    {
        const string src = "def go(n, d = North):\n\tfor i in range(n):\n\t\tmove(d)\n\nx = 1 + go(2)\nif x > 1:\n\tx += 2\n";
        var m = Ir(src);
        var f = m.Functions.Single();
        Assert.Equal("go", Text(src, f.NameSpan));
        Assert.Equal("def go(n, d = North):", Text(src, f.HeaderSpan));
        Assert.Equal("n", Text(src, f.Params[0].NameSpan));
        var rf = First<IrRangeFor>(f);
        Assert.Equal("for", Text(src, rf.KwSpan));
        Assert.Equal("i", Text(src, rf.VarSpan));
        Assert.Equal("for i in range(n):", Text(src, rf.HeaderSpan));
        var mv = First<IrCall>(f);
        Assert.Equal("move", Text(src, mv.NameSpan));
        Assert.Equal("move(d)", Text(src, mv.Span));
        var bin = First<IrBinary>(m.Items[1]);
        Assert.Equal("+", Text(src, bin.OpSpan));
        Assert.Equal("1 + go(2)", Text(src, bin.Span));
        var ifs = (IrIf)m.Items.OfType<IrIf>().Single();
        Assert.Equal("if", Text(src, ifs.KwSpan));
        var aug = First<IrAssign>(ifs);
        Assert.Equal("+=", Text(src, aug.OpSpan));
        Assert.Equal("x += 2", Text(src, aug.Span));
        var decl = (IrVarDecl)m.Items[1];
        Assert.Equal("x", Text(src, decl.NameSpan));
        Assert.Equal("x = 1 + go(2)", Text(src, decl.Span));
        Assert.All(IrWalk.Descendants(m).OfType<IrExpr>().Where(e => !e.Synthetic), e => Assert.False(e.Span.IsNone, e.GetType().Name + " has no span"));
    }

    [Fact]
    public void Spans_OfQualifiedNamesAndOps()
    {
        const string src = "import u\nu.f(1)\nl = [1]\nl.append(u.v)\n";
        var m = Ir(src);
        var c = IrWalk.Descendants(m).OfType<IrCall>().Single();
        Assert.Equal("u", Text(src, c.QualifierSpan));
        Assert.Equal("f", Text(src, c.NameSpan));
        var o = First<IrOpCall>(m);
        Assert.Equal("append", Text(src, o.NameSpan));
        var n = IrWalk.Descendants(m).OfType<IrName>().Single(x => x.Symbol.IsExternal);
        Assert.Equal("u", Text(src, n.QualifierSpan));
        Assert.Equal("v", Text(src, n.NameSpan));
        Assert.Equal("u", Text(src, ((IrImport)m.Items[0]).NameSpan));
    }

    // ------------------------------------------------------------------ comments
    [Fact]
    public void Comments_LeadingTrailingAndBlockEnd()
    {
        const string src = "# top\nx = 1  # one\n\n# before f\ndef f():\n\t# in f\n\tharvest()\n\t# end of f\n# module end\n";
        var m = Ir(src);
        var x = m.Items[0];
        Assert.Equal(" top", Assert.Single(x.Leading).Text);
        Assert.Equal(" one", x.Trailing.Text);
        var f = m.Functions.Single();
        Assert.Equal(" before f", Assert.Single(f.Leading).Text);
        Assert.Equal(1, f.Leading[0].BlankLinesBefore);
        Assert.Equal(" in f", f.Body.Stmts[0].Leading.Single().Text);
        Assert.Equal(" end of f", Assert.Single(f.Body.TrailingComments).Text);
        Assert.Equal(" module end", Assert.Single(m.TrailingComments).Text);
        Assert.Equal(src.Replace("1  #", "1 #"), PyTestUtil.Print(m));
    }

    [Fact]
    public void Comments_OnlyBody_IsAnEmptyBlock()
    {
        var m = Ir("for i in seq:\n\t#do something with i\n");
        var fe = First<IrForEach>(m);
        Assert.Empty(fe.Body.Stmts);
        Assert.Equal("do something with i", Assert.Single(fe.Body.TrailingComments).Text);
    }

    [Fact]
    public void Comments_OnHeadersAndDroppedStatements()
    {
        var m = Ir("while True:  # forever\n\tpass  # nothing\n\tglobal g\n\tharvest()\n");
        var w = First<IrWhile>(m);
        Assert.Equal(" forever", w.Trailing.Text);
        Assert.Equal(" nothing", Assert.Single(w.Body.Stmts[0].Leading).Text);
    }

    // ------------------------------------------------------------------ errors
    static void Err(string src, string message, string at)
    {
        var e = PyTestUtil.Error(src);
        Assert.Contains(message, e.Message);
        if (at != null) Assert.Equal(at, PyTestUtil.Snip(src, e));
    }

    [Fact]
    public void Errors_ImitateCPython()
    {
        Err("x = = 1\n", "SyntaxError: invalid syntax", "=");
        Err("while True:\nharvest()\n", "IndentationError: expected an indented block after 'while' statement on line 1", "harvest");
        Err("x = 1\n\ty = 2\n", "IndentationError: unexpected indent", null);
        Err("if a:\n\t\tx = 1\n\ty = 2\n", "IndentationError: unindent does not match any outer indentation level", null);
        Err("if a:\n\t  x = 1\n", "TabError", null);
        Err("print(\"abc)\n", "SyntaxError: unterminated string literal (detected at line 1)", "\"abc)");
        Err("print(1, 2\n", "SyntaxError: '(' was never closed", "(");
        Err("x = 1)\n", "SyntaxError: unmatched ')'", ")");
        Err("5 = x\n", "SyntaxError: cannot assign to literal", "5");
        Err("f() = 1\n", "SyntaxError: cannot assign to function call", "f()");
        Err("if a\n\tpass\n", "SyntaxError: expected ':'", null);
        Err("North = 1\n", "cannot assign to North", "North");
        Err("break\n", "SyntaxError: 'break' outside loop", "break");
        Err("return 1\n", "SyntaxError: 'return' outside function", "return");
        Err("x = 1e5\n", "SyntaxError: invalid decimal literal", "1e5");
        Err("x = a ? b\n", "SyntaxError: invalid character '?'", "?");
        Err("x = a & b\n", "operator '&' is not supported by the game", "&");
    }

    [Fact]
    public void Errors_ForWhatTheGameLacks()
    {
        Err("f(a=1)\n", "keyword arguments", null);
        Err("x = l[1:2]\n", "slicing", null);
        Err("f = lambda: 1\n", "'lambda' is not supported by the game", "lambda");
        Err("class A:\n\tpass\n", "'class' is not supported by the game", "class");
        Err("x = [i for i in l]\n", "comprehensions are not supported", null);
        Err("l = []\nl.sort()\n", "'sort' is not a method the game supports", "sort");
        Err("x = 1; y = 2\n", "';' is not supported", ";");
        Err("if a: harvest()\n", "on its own, indented lines", null);
        Err("a = b = 1\n", "chained assignment", null);
    }

    [Fact]
    public void Indentation_TabsAndSpacesAndIndentedScript()
    {
        Ir("if a:\n    x = 1\n    if b:\n\t\ty = 2\n");      // 4 spaces and a tab are the same width (game rule)
        Ir("    harvest()\n    move(North)\n");                // the game accepts an indented script
        Ir("x = 1\r\nif x:\r\n\tharvest()\r\n");
        Ir("");
    }
}
