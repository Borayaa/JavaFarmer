namespace JavaFarmer.Languages.Tests.Python;

/// <summary>Core/IrDeclarations (through the Python frontend, which runs it, and on hand-built IR).</summary>
public class IrDeclarationsTests
{
    static IrModule Ir(string s) => PyTestUtil.ToIr(s);
    static List<IrVarDecl> Decls(IrNode n) => IrWalk.Descendants(n).OfType<IrVarDecl>().ToList();

    [Fact]
    public void FirstAssignment_BecomesTheDeclaration()
    {
        var m = Ir("# c\nx = 1\nprint(x)\nx = 2\n");
        var d = Assert.IsType<IrVarDecl>(m.Items[0]);
        Assert.Equal("x", d.Symbol.Name);
        Assert.Equal(1, ((IrLiteral)d.Init).Int);
        Assert.False(d.TypeExplicit);
        Assert.Equal(" c", d.Leading.Single().Text);   // comments move with it
        Assert.IsType<IrAssign>(m.Items[2]);
    }

    [Fact]
    public void AssignedInBranches_DeclaredBeforeTheBranch()
    {
        var m = Ir("if a:\n\tx = 1\nelse:\n\tx = 2\nprint(x)\n");
        var d = Assert.IsType<IrVarDecl>(m.Items[0]);
        Assert.Null(d.Init);
        Assert.True(d.Synthetic);
        Assert.IsType<IrIf>(m.Items[1]);
        Assert.Equal(2, IrWalk.Descendants(m).OfType<IrAssign>().Count());
    }

    [Fact]
    public void UsedOnlyInsideABlock_DeclaredInThatBlock()
    {
        var m = Ir("def f():\n\twhile True:\n\t\ty = 3\n\t\tprint(y)\n");
        var w = IrWalk.Descendants(m).OfType<IrWhile>().Single();
        Assert.IsType<IrVarDecl>(w.Body.Stmts[0]);
    }

    [Fact]
    public void LoopVariables_AreDeclaredByTheLoop_UnlessUsedAfter()
    {
        var m = Ir("for i in range(3):\n\tprint(i)\nfor i in range(2):\n\tpass\n");
        Assert.Empty(Decls(m));
        var m2 = Ir("for i in range(3):\n\tpass\nprint(i)\n");
        var d = Assert.IsType<IrVarDecl>(m2.Items[0]);
        Assert.Equal("i", d.Symbol.Name);
        Assert.Null(d.Init);
    }

    [Fact]
    public void SelfReadingFirstAssignment_GetsADeclarationBefore()
    {
        var m = Ir("def f():\n\tx = x + 1\n\treturn x\n");
        var body = m.Functions.Single().Body.Stmts;
        Assert.Null(Assert.IsType<IrVarDecl>(body[0]).Init);
        Assert.IsType<IrAssign>(body[1]);
    }

    [Fact]
    public void ParamsAndGlobals_AreUntouched()
    {
        var m = Ir("g = 0\n\ndef f(p):\n\tglobal g\n\tp = 2\n\tg = p\n");
        Assert.Empty(Decls(m));
    }

    [Fact]
    public void ClosureCaptures_CountAsUses()
    {
        var m = Ir("def f():\n\tn = 1\n\tdef g():\n\t\treturn n\n\treturn g\n");
        var body = m.Functions.Single().Body.Stmts;
        Assert.Equal("n", Assert.IsType<IrVarDecl>(body[0]).Symbol.Name);
    }

    [Fact]
    public void Place_IsIdempotent()
    {
        var m = Ir("if a:\n\tx = 1\nelse:\n\tx = 2\ny = x\nfor i in range(3):\n\tpass\nprint(i, y)\n");
        var before = PyTestUtil.Dump(m);
        IrDeclarations.Place(m);
        IrDeclarations.Place(m);
        Assert.Equal(before, PyTestUtil.Dump(m));
        IrDeclarations.Place(null);
    }

    [Fact]
    public void HandBuiltIr()
    {
        var x = new IrSymbol("x", IrSymbolKind.Local, IrType.Int);
        var inner = new IrBlock(new IrStmt[] { new IrAssign { Target = new IrName(x), Value = IrLiteral.OfInt(1) }, new IrExprStmt(IrCall.ToBuiltin("print", new IrName(x))) });
        var f = new IrFunction { Symbol = new IrSymbol("f", IrSymbolKind.Function), Body = new IrBlock(new IrStmt[] { new IrWhile { Cond = IrLiteral.OfBool(true), Body = inner } }) };
        var m = new IrModule { Name = "m", Items = { f } };
        IrDeclarations.Place(m);
        var d = Assert.IsType<IrVarDecl>(inner.Stmts[0]);
        Assert.Same(x, d.Symbol);
        Assert.Equal(1, ((IrLiteral)d.Init).Int);
    }
}
