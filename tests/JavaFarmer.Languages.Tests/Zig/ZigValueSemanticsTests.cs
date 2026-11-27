namespace JavaFarmer.Languages.Tests.Zig;

/// <summary>
/// Zig value semantics the game's Python does not have (docs/languages/zig.md "Values"): array copies, the null of
/// std.mem.indexOfScalar / list.pop(), const aggregates, tuple element assignment, container-level declarations
/// (comptime-known, order-independent, undefined), empty maps for farm.simulate, doc comment placement.
/// </summary>
public class ZigValueSemanticsTests
{
    static readonly ILanguage Zig = Compiler.Languages.Get("zig");
    const string Head = "const std = @import(\"std\");\nconst farm = @import(\"farm\");\n\n";
    static string Main(string body, string top = "") => Head + top + "pub fn main() !void {\n" + body + "\n}\n";

    static string Py(string src) => ZigLanguageTests.Ok(src).Python;

    static Diagnostic Fail(string src)
    {
        var r = Zig.Compile(src, new CompileOptions { FileName = "main" });
        Assert.False(r.Success, "expected an error:\n" + r.Python);
        return r.Errors[0];
    }

    static string RoundTrip(string src)
    {
        var ir = Zig.ToIr(src, new CompileOptions { FileName = "main" }, out var errors);
        Assert.True(ir != null, string.Join("; ", errors));
        var printed = ((IPrinterDiagnostics)Zig).Print(ir, new PrintOptions(), out var problems);
        Assert.True(problems.Count == 0, string.Join("; ", problems) + "\n" + printed);
        ZigLanguageTests.Ok(printed);
        return printed;
    }

    // ------------------------------------------------------------------ 1. arrays are values
    [Fact]
    public void ArrayCopies_AreListCopies()
    {
        var py = Py(Main("var a = [_]i32{ 1, 2, 3 };\nvar b = a;\nb[0] = 9;\na[1] = 5;\nstd.debug.print(\"{any} {any}\\n\", .{ a, b });"));
        Assert.Contains("b = list(a)", py);
        py = Py(Main("var g = [_][2]i32{ .{ 1, 2 }, .{ 3, 4 } };\nvar h = g;\nh[0][0] = 1;\ng[1][1] = 0;\nconst r = g[1];\nstd.debug.print(\"{any} {any}\\n\", .{ h, r });"));
        Assert.Contains("h = [list(g[0]), list(g[1])]", py);
        Assert.Contains("r = list(g[1])", py);
        // assignments, arguments and returns copy too
        py = Py(Main("var a = [_]i32{ 1, 2 };\na[1] = 4;\nvar b: [2]i32 = undefined;\nb = a;\nb[0] = 3;\nstd.debug.print(\"{d}\\n\", .{f(a) + b[0]});", "fn f(x: [2]i32) i32 {\n    return x[0];\n}\n\nvar g = [_]i32{ 1, 2 };\n\nfn get() [2]i32 {\n    return g;\n}\n\n"));
        Assert.Contains("b = list(a)", py);
        Assert.Contains("f(list(a))", py);
        Assert.Contains("return list(g)", py);
    }

    [Fact]
    public void ArrayCopies_NotForFreshValues_OrConstantToConstant()
    {
        var py = Py(Main("const a = [_]i32{ 1, 2, 3 };\nconst b = a;\nvar c = [_]i32{ 4, 5, 6 };\nc[0] = 1;\nstd.debug.print(\"{any} {any}\\n\", .{ b, c });"));
        Assert.Contains("b = a\n", py);
        Assert.DoesNotContain("list(", py);
        // ArrayList values share their buffer in Zig too: no copy
        py = Py(Main("var xs: std.ArrayList(i32) = .empty;\ndefer xs.deinit(farm.allocator);\ntry xs.append(farm.allocator, 1);\nconst ys = xs;\nstd.debug.print(\"{d}\\n\", .{ys.items.len});"));
        Assert.Contains("ys = xs\n", py);
    }

    [Fact]
    public void ArrayCopies_PrintBackAsPlainAssignments()
    {
        var printed = RoundTrip(Main("var g = [_][2]i32{ .{ 1, 2 }, .{ 3, 4 } };\nvar h = g;\nh[0][0] = 1;\nvar a = [_]i32{ 1, 2 };\nvar b = a;\nb[1] = 0;\na[0] = 2;\ng[1][1] = 0;\nstd.debug.print(\"{any} {any}\\n\", .{ h, b });"));
        Assert.Matches(@"var h(: \[2\]\[2\]i32)? = g;", printed);
        Assert.Matches(@"var b(: \[2\]i32)? = a;", printed);
    }

    // ------------------------------------------------------------------ 2. std.mem.indexOfScalar
    [Fact]
    public void IndexOfScalar_IsNullWhenMissing()
    {
        var py = Py(Main("const xs = [_]i32{ 1, 2 };\nif (std.mem.indexOfScalar(i32, &xs, 7)) |i| {\n    std.debug.print(\"{d}\\n\", .{i});\n}\nconst j = std.mem.indexOfScalar(i32, &xs, 2) orelse 9;\nconst k = std.mem.indexOfScalar(i32, &xs, 3);\nif (k == null) std.debug.print(\"{d}\\n\", .{j});"));
        Assert.Contains("if 7 in xs:\n        i = __jindexof(xs, 7)\n    else:\n        i = None", py);
        Assert.Contains("if 2 in xs:\n        j = __jindexof(xs, 2)\n    else:\n        j = 9", py);
        Assert.Contains("if k == None:", py);
        var printed = RoundTrip(Main("const xs = [_]i32{ 1, 2 };\nconst k = std.mem.indexOfScalar(i32, &xs, 3);\nconst j = std.mem.indexOfScalar(i32, &xs, 2) orelse 9;\nif (k) |i| std.debug.print(\"{d} {d}\\n\", .{ i, j });"));
        Assert.Contains("std.mem.findScalar(i32, &xs, 3)", printed);
        Assert.Contains("std.mem.findScalar(i32, &xs, 2) orelse 9", printed);
    }

    [Fact]
    public void IndexOfScalar_InsideALargerExpression_IsRejected()
    {
        var e = Fail(Main("const xs = [_]i32{ 1, 2 };\nstd.debug.print(\"{any}\\n\", .{std.mem.indexOfScalar(i32, &xs, 2)});"));
        Assert.Contains("std.mem.indexOfScalar is only supported as a whole value", e.Message);
        e = Fail(Main("const xs = [_]i32{ 1, 2 };\nconst i = std.mem.indexOfScalar(i32, &xs, farm.measure(i32));\n_ = i;"));
        Assert.Contains("not supported in JavaFarmer", e.Message);
    }

    // ------------------------------------------------------------------ 3. const aggregates, tuple elements
    [Theory]
    [InlineData("const t: struct { i32, i32 } = .{ 1, 2 };\nt[0] = 5;", "t")]
    [InlineData("const a = [_]i32{ 1, 2 };\na[0] = 3;", "a")]
    [InlineData("const p: farm.Position = .{ .x = 1, .y = 2 };\np.x = 3;", "p")]
    [InlineData("const g = [_][2]i32{ .{ 1, 2 }, .{ 3, 4 } };\ng[0][1] = 5;", "g[0]")]
    public void AssigningToAConstantsElement_IsAnError(string body, string at)
    {
        var src = Main(body);
        var e = Fail(src);
        Assert.Equal("cannot assign to constant", e.Message);
        Assert.Equal(at, HarnessTests.Slice(src, e.Start, e.End));
    }

    [Fact]
    public void TupleAndPositionElements_RebuildTheTuple()
    {
        var py = Py(Main("var t: struct { i32, i32 } = .{ 1, 2 };\nt[0] = 5;\nt[1] += 3;\nvar p: farm.Position = .{ .x = 1, .y = 2 };\np.y = 7;\nstd.debug.print(\"{any} {d}\\n\", .{ t, p.y });"));
        Assert.Contains("t = (5, t[1])", py);
        Assert.Contains("t = (t[0], t[1] + 3)", py);
        Assert.Contains("p = (p[0], 7)", py);
    }

    // ------------------------------------------------------------------ 4. list.pop()
    [Fact]
    public void Pop_AsAnOptional_IsGuarded()
    {
        var py = Py(Main("var xs: std.ArrayList(i32) = .empty;\ndefer xs.deinit(farm.allocator);\nconst a = xs.pop();\nconst b = xs.pop() orelse 0;\nif (xs.pop()) |c| std.debug.print(\"{d}\\n\", .{c});\nif (xs.pop() == null) std.debug.print(\"{any} {d}\\n\", .{ a, b });\n_ = xs.pop();\ntry xs.append(farm.allocator, 1);\nconst d = xs.pop().?;\n_ = d;"));
        Assert.Contains("if len(xs) > 0:\n        a = xs.pop()\n    else:\n        a = None", py);
        Assert.Contains("if len(xs) > 0:\n        b = xs.pop()\n    else:\n        b = 0", py);
        Assert.Contains("if len(xs) > 0:\n        c = xs.pop()\n    else:\n        c = None", py);
        Assert.Contains("if len(xs) == 0 or xs.pop() == None:", py);
        Assert.Contains("if len(xs) > 0:\n        xs.pop()", py);
        Assert.Contains("d = xs.pop()\n", py);   // .? : the game's error on an empty list is Zig's panic
        var r = ZigLanguageTests.Ok(Main("var xs: std.ArrayList(i32) = .empty;\ndefer xs.deinit(farm.allocator);\nconst a = xs.pop();\n_ = a;"));
        // the guard is free (Synthetic): only the pop is the player's code
        var at = r.Python.IndexOf("if len(xs) > 0", StringComparison.Ordinal);
        Assert.Equal(SegmentKind.Synthetic, r.Map.Find(at).Kind);
        var printed = RoundTrip(Main("var xs: std.ArrayList(i32) = .empty;\ndefer xs.deinit(farm.allocator);\ntry xs.append(farm.allocator, 1);\nconst a = xs.pop();\nconst b = xs.pop() orelse 0;\nstd.debug.print(\"{any} {d}\\n\", .{ a, b });"));
        Assert.Matches(@"const a(: \?i32)? = xs\.pop\(\);", printed);
        Assert.Matches(@"const b(: i32)? = xs\.pop\(\) orelse 0;", printed);
    }

    // ------------------------------------------------------------------ 5. farm.simulate(..., .{}, ...)
    [Fact]
    public void EmptyAnonymousLiteral_ForSimulateMaps_IsAnEmptyDict()
    {
        var py = Py(Main("var items = std.AutoHashMap(farm.Item, f64).init(farm.allocator);\ndefer items.deinit();\nconst t = farm.simulate(\"f1\", .{}, items, .{}, 0, 64);\nstd.debug.print(\"{d}\\n\", .{t});"));
        Assert.Contains("simulate(\"f1\", {}, items, {}, 0, 64)", py);
    }

    // ------------------------------------------------------------------ 6.-8. container-level declarations
    [Fact]
    public void UndefinedGlobal_GetsNoAssignment()
    {
        var src = Head + "var a: i32 = undefined;\nvar grid: [2]i32 = undefined;\n\npub fn main() void {\n    a += 1;\n    grid[0] = a;\n}\n";
        var py = Py(src);
        Assert.DoesNotContain("a = 0", py);
        Assert.DoesNotContain("a = None", py);
        Assert.Contains("grid = __jarr(2, 0)", py);
        // the IR keeps the declaration for printers
        var printed = RoundTrip(src);
        Assert.Contains("var a: i32 = undefined;", printed);
    }

    [Fact]
    public void IfValue_AtTheTopLevel_IsAccepted()
    {
        var py = Py(Head + "const n: i32 = if (big) 1 else 2;\nconst big = true;\n\npub fn main() void {\n    std.debug.print(\"{d}\\n\", .{n});\n}\n");
        Assert.True(py.IndexOf("big = True", StringComparison.Ordinal) < py.IndexOf("if big:", StringComparison.Ordinal), py);
        Assert.Contains("if big:\n    n = 1\nelse:\n    n = 2", py);
    }

    [Fact]
    public void ForwardReferences_BetweenGlobals_AreOrderedForPython()
    {
        var py = Py(Head + "const b: i32 = a + 1;\nconst a: i32 = 2;\n\npub fn main() void {\n    std.debug.print(\"{d}\\n\", .{b});\n}\n");
        Assert.True(py.IndexOf("a = 2", StringComparison.Ordinal) < py.IndexOf("b = a + 1", StringComparison.Ordinal), py);
    }

    [Theory]
    [InlineData("const n = farm.getWorldSize();", "farm.getWorldSize()", "unable to resolve comptime value")]
    [InlineData("var g: i32 = 0;\nconst h = g + 1;", "g", "unable to resolve comptime value")]
    [InlineData("const h: i32 = if (farm.canHarvest()) 1 else 2;", "farm.canHarvest()", "unable to resolve comptime value")]
    [InlineData("fn sq(x: i32) i32 {\n    return x * x;\n}\nconst h = sq(3);", "sq(3)", "not supported in JavaFarmer")]
    public void RuntimeInitializer_AtTheTopLevel_IsAnError(string top, string at, string msg)
    {
        var src = Head + top + "\n\npub fn main() void {}\n";
        var e = Fail(src);
        Assert.Contains(msg, e.Message);
        Assert.Equal(at, HarnessTests.Slice(src, e.Start, e.End));
    }

    [Fact]
    public void ComptimeInitializers_AtTheTopLevel_StillWork()
    {
        Py(Head + "var m = std.AutoHashMap(i32, i32).init(farm.allocator);\nvar xs: std.ArrayList(i32) = .empty;\nconst size: usize = 6;\nconst cells = size * size;\nconst dirs = [_]farm.Direction{ .North, .East };\nconst lo: i32 = @min(3, 4);\n\npub fn main() !void {\n    try m.put(1, 2);\n    try xs.append(farm.allocator, lo);\n    std.debug.print(\"{d} {any}\\n\", .{ cells, dirs });\n}\n");
    }

    [Fact]
    public void StubFunctions_RefuseComptimeCalls()
    {
        var stub = Zig.ApiStubFile(out _);
        Assert.Contains("pub fn getWorldSize() usize {\n    if (@inComptime()) @compileError(", stub);
    }

    // ------------------------------------------------------------------ doc comments
    [Theory]
    [InlineData("pub fn main() void {\n    /// doc\n    _ = farm.harvest();\n}\n", "expected statement, found 'a document comment'")]
    [InlineData("pub fn main() void {}\n/// doc\n", "unattached documentation comment")]
    public void DocComments_MustDocumentADeclaration(string code, string msg)
    {
        var src = Head + code;
        var e = Fail(src);
        Assert.Equal(msg, e.Message);
        Assert.Equal("/// doc", HarnessTests.Slice(src, e.Start, e.End));
        ZigLanguageTests.Ok(Head + "/// documented\npub fn main() void {\n    //// four slashes are a normal comment\n    _ = farm.harvest();\n}\n");
    }
}
