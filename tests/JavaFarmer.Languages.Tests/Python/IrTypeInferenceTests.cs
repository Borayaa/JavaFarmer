namespace JavaFarmer.Languages.Tests.Python;

/// <summary>Core/IrTypeInference (exercised through the Python frontend, which runs it, and on hand-built IR).</summary>
public class IrTypeInferenceTests
{
    static IrModule Ir(string s) => PyTestUtil.ToIr(s);
    static IrSymbol S(IrModule m, string name) =>
        IrWalk.Descendants(m).Select(n => (n as IrName)?.Symbol ?? (n as IrVarDecl)?.Symbol ?? (n as IrGlobal)?.Symbol ?? (n as IrParam)?.Symbol ?? (n as IrRangeFor)?.Var ?? (n as IrForEach)?.Var)
            .First(s => s != null && s.Name == name);
    static string T(IrModule m, string name) => S(m, name).Type.ToString();

    [Fact]
    public void Literals_AndArithmetic()
    {
        var m = Ir("a = 1\nb = 2.5\nc = a + 1\nd = a / 2\ne = a + b\nf = a // 2\ng = True\nh = \"s\"\ni = a < 2\nj = not a\nk = -a\n");
        Assert.Equal("int", T(m, "a"));
        Assert.Equal("float", T(m, "b"));
        Assert.Equal("int", T(m, "c"));
        Assert.Equal("float", T(m, "d"));
        Assert.Equal("float", T(m, "e"));
        Assert.Equal("int", T(m, "f"));
        Assert.Equal("bool", T(m, "g"));
        Assert.Equal("string", T(m, "h"));
        Assert.Equal("bool", T(m, "i"));
        Assert.Equal("bool", T(m, "j"));
        Assert.Equal("int", T(m, "k"));
    }

    [Fact]
    public void SymbolTypes_JoinAllAssignments()
    {
        var m = Ir("x = 0\nx += 0.5\ny = None\ny = Entities.Bush\n");
        Assert.Equal("float", T(m, "x"));
        Assert.Equal("Entity?", T(m, "y"));
    }

    [Fact]
    public void Strings_AndConcat()
    {
        var m = Ir("s = \"a\" + str(3)\nt = s + \"b\"\nn = len(s)\nc = s[0]\n");
        Assert.Equal("string", T(m, "s"));
        Assert.Equal("string", T(m, "t"));
        Assert.Equal("int", T(m, "n"));
        Assert.Equal("string", T(m, "c"));
    }

    [Fact]
    public void EmptyLiterals_AreRefinedByLaterUse()
    {
        var m = Ir("l = []\nl.append(1)\ns = set()\ns.add(Items.Hay)\nd = {}\nd[Items.Hay] = 2.5\nq = []\nq.insert(0, \"x\")\n");
        Assert.Equal("list<int>", T(m, "l"));
        Assert.Equal("set<Item>", T(m, "s"));
        Assert.Equal("map<Item,float>", T(m, "d"));
        Assert.Equal("list<string>", T(m, "q"));
        var lit = IrWalk.Descendants(m).OfType<IrVarDecl>().Single(v => v.Symbol.Name == "l").Init;
        Assert.Equal("list<int>", lit.Type.ToString());
    }

    [Fact]
    public void Collections_Indexing_AndLoops()
    {
        var m = Ir("l = [1, 2]\nd = {Items.Hay: 3}\nt = (1, \"a\")\na = l[0]\nb = d[Items.Hay]\nc = t[1]\nfor k in d:\n\tquick_print(k)\nfor e in l:\n\tquick_print(e)\nfor i in range(3):\n\tquick_print(i)\nx, y = t\np = d.pop(Items.Hay)\n");
        Assert.Equal("int", T(m, "a"));
        Assert.Equal("int", T(m, "b"));
        Assert.Equal("string", T(m, "c"));
        Assert.Equal("Item", T(m, "k"));
        Assert.Equal("int", T(m, "e"));
        Assert.Equal("int", T(m, "i"));
        Assert.Equal("int", T(m, "x"));
        Assert.Equal("string", T(m, "y"));
        Assert.Equal("int", T(m, "p"));
        Assert.Equal("tuple<int,string>", T(m, "t"));
    }

    [Fact]
    public void Builtins_AndConstants()
    {
        var m = Ir("a = get_entity_type()\nb = min(1, 2)\nc = max(1, 2.5)\nd = get_pos_x()\ne = num_items(Items.Hay)\nf = North\ng = measure()\nh = get_companion()\nent, pos = h\n");
        Assert.Equal("Entity?", T(m, "a"));
        Assert.Equal("int", T(m, "b"));
        Assert.Equal("float", T(m, "c"));
        Assert.Equal("int", T(m, "d"));
        Assert.Equal("float", T(m, "e"));
        Assert.Equal("Direction", T(m, "f"));
        Assert.Equal("any", T(m, "g"));
        Assert.Equal("Companion?", T(m, "h"));
        Assert.Equal("Entity", T(m, "ent"));
        Assert.Equal("Position", T(m, "pos"));
    }

    [Fact]
    public void Functions_ReturnAndParamTypes()
    {
        var m = Ir("def double(a):\n\treturn a * 2\n\ndef act():\n\tharvest()\n\ndef maybe(x):\n\tif x:\n\t\treturn Entities.Tree\n\treturn\n\nr = double(3)\nact()\nq = maybe(True)\n");
        var fs = m.Functions.ToDictionary(f => f.Name);
        Assert.Equal("int", fs["double"].ReturnType.ToString());
        Assert.Equal("int", fs["double"].Params[0].Symbol.Type.ToString());
        Assert.Equal("void", fs["act"].ReturnType.ToString());
        Assert.Equal("Entity?", fs["maybe"].ReturnType.ToString());
        Assert.Equal("bool", fs["maybe"].Params[0].Symbol.Type.ToString());
        Assert.Equal("int", T(m, "r"));
        Assert.Equal("Entity?", T(m, "q"));
        Assert.Equal(IrTypeKind.Func, fs["double"].Symbol.Type.Kind);
    }

    [Fact]
    public void Lambdas_GetReturnTypes()
    {
        var m = Ir("def outer():\n\tdef inner(n):\n\t\treturn n + 1.5\n\treturn inner(1)\nv = outer()\n");
        var lam = IrWalk.Descendants(m).OfType<IrLambda>().Single();
        Assert.Equal("float", lam.ReturnType.ToString());
        Assert.Equal("int", lam.Params[0].Symbol.Type.ToString());
        Assert.Equal("float", T(m, "v"));
    }

    [Fact]
    public void UnknownStaysUnknown()
    {
        var m = Ir("a = mystery()\nb = a\n");
        Assert.Equal(IrTypeKind.Unknown, S(m, "a").Type.Kind);
        Assert.Equal(IrTypeKind.Unknown, S(m, "b").Type.Kind);
    }

    [Fact]
    public void RecursiveStructures_Terminate()
    {
        var m = Ir("x = []\nx = [x]\nx.append(x)\n");
        Assert.Equal(IrTypeKind.List, S(m, "x").Type.Kind);
    }

    [Fact]
    public void KnownTypes_AreNeverOverwritten()
    {
        var s = new IrSymbol("v", IrSymbolKind.Local, IrType.String);
        var lit = IrLiteral.OfInt(3);
        var partial = new IrSymbol("l", IrSymbolKind.Local, IrType.List(IrType.Unknown));
        var m = new IrModule { Name = "m" };
        m.Items.Add(new IrAssign { Target = new IrName(s), Value = lit });
        m.Items.Add(new IrAssign { Target = new IrName(partial), Value = new IrListLit { Items = { IrLiteral.OfFloat(1) } } });
        var unknownCall = new IrCall { Kind = IrCallKind.Builtin, Builtin = "get_pos_y" };
        m.Items.Add(new IrExprStmt(unknownCall));
        IrTypeInference.Infer(m);
        Assert.Equal("string", s.Type.ToString());        // known: untouched
        Assert.Equal("list<float>", partial.Type.ToString());   // only the unknown part is filled
        Assert.Equal("int", unknownCall.Type.ToString());
        IrTypeInference.Infer(null);
    }
}
