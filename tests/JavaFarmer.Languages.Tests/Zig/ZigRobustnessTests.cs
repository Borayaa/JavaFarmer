using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests.Zig;

/// <summary>Speed (Compile p25 &lt; 5 ms for a 300-line file) and robustness (truncated / garbled corpus files never throw).</summary>
[Collection("perf")]
public class ZigRobustnessTests
{
    readonly ITestOutputHelper output;
    public ZigRobustnessTests(ITestOutputHelper output) { this.output = output; }

    static ILanguage Zig => ZigLanguageTests.Zig;

    /// <summary>A realistic ~300-line Zig program (loops, ifs, switch, lists, maps, optionals, helpers, drones).</summary>
    public static string Zig300()
    {
        var sb = new StringBuilder();
        sb.Append("const std = @import(\"std\");\nconst farm = @import(\"farm\");\n\nvar harvested: i32 = 0;\nvar heights: std.ArrayList(i32) = .empty;\n\n");
        int n = 0;
        while (sb.ToString().Split('\n').Length < 270)
        {
            n++;
            sb.Append("fn work" + n + "(size: usize, factor: f64) i32 {\n");
            sb.Append("    var total: i32 = 0;\n");
            sb.Append("    for (0..size) |x| {\n");
            sb.Append("        for (0..farm.getWorldSize()) |y| {\n");
            sb.Append("            const here = farm.getEntityType();\n");
            sb.Append("            if (farm.canHarvest() and here != null and here.? == .Carrot) {\n");
            sb.Append("                _ = farm.harvest();\n                harvested += 1;\n                total += @intCast((x * y) % 7);\n");
            sb.Append("            } else if (farm.getGroundType() != .Soil) {\n                farm.till();\n            } else {\n");
            sb.Append("                _ = farm.plant(if (x % 2 == 0) .Bush else .Grass);\n            }\n");
            sb.Append("            heights.append(farm.allocator, @divTrunc(total, @as(i32, @intCast(y + 1)))) catch unreachable;\n");
            sb.Append("            _ = farm.move(.North);\n        }\n        _ = farm.move(.East);\n    }\n");
            sb.Append("    switch (@mod(total, 3)) {\n        0 => farm.quickPrint(\"zero {d}\\n\", .{total}),\n        1 => farm.quickPrint(\"one {d}\\n\", .{total}),\n        else => total -= 1,\n    }\n");
            sb.Append("    var i: usize = 0;\n    while (i < heights.items.len) : (i += 1) {\n        if (@as(f64, @floatFromInt(heights.items[i])) > factor) continue;\n    }\n");
            sb.Append("    return @intFromFloat(@as(f64, @floatFromInt(total)) * factor);\n}\n\n");
        }
        sb.Append("fn drone() void {\n    _ = work1(farm.getWorldSize(), 2.0);\n}\n\n");
        sb.Append("pub fn main() void {\n    const s = farm.getWorldSize();\n");
        for (int i = 1; i <= n; i++) sb.Append("    harvested += work" + i + "(s, 1.5);\n");
        sb.Append("    _ = farm.spawnDrone(drone);\n    std.debug.print(\"{d}\\n\", .{harvested});\n}\n");
        return sb.ToString();
    }

    [Fact]
    public void Zig_300Lines_CompilesUnder5ms()
    {
        var src = Zig300();
        int lines = src.Split('\n').Length;
        var opts = new CompileOptions { FileName = "main" };
        var first = Zig.Compile(src, opts);
        Assert.True(first.Success, string.Join("; ", first.Errors.Select(e => e + " [" + HarnessTests.Slice(src, e.Start, e.End) + "]")));
        Assert.InRange(lines, 280, 340);
        for (int i = 0; i < 100; i++) Zig.Compile(src, opts);   // warm up the JIT
        const int runs = 100;
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            Zig.Compile(src, opts);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        double p25 = times[runs / 4], median = times[runs / 2], p90 = times[runs * 9 / 10];
        output.WriteLine($"Zig compile, {lines} lines: p25 {p25:F3} ms, median {median:F3} ms, p90 {p90:F3} ms, min {times[0]:F3} ms");
        Assert.True(p25 < 5.0, $"p25 {p25:F3} ms for {lines} lines (limit 5 ms)");
    }

    static IEnumerable<string> CorpusSources() =>
        Directory.GetFiles(RepoPaths.Corpus("zig"), "*.zig", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText);

    [Fact]
    public void Fuzz_TruncatedAndGarbledCorpus_NeverThrows()
    {
        var rnd = new Random(1234);
        var py = Compiler.Languages.Get("python");
        int cases = 0;
        foreach (var src in CorpusSources())
        {
            var variants = new List<string>();
            for (int k = 1; k <= 12; k++) variants.Add(src.Substring(0, src.Length * k / 13));
            for (int k = 0; k < 8; k++)
            {
                var chars = src.ToCharArray();
                for (int j = 0; j < 4; j++) { const string junk = "{}()[];.,|@\"'\\\n=+-*/%&!?:_aZ0\0"; chars[rnd.Next(chars.Length)] = junk[rnd.Next(junk.Length)]; }
                variants.Add(new string(chars));
            }
            variants.Add(src.Replace("{", ""));
            variants.Add(src.Replace(";", ""));
            foreach (var v in variants)
            {
                cases++;
                var opts = new CompileOptions { FileName = "main" };
                var r = Zig.Compile(v, opts);
                Assert.NotNull(r);
                Assert.All(r.Errors, e => Assert.DoesNotContain("internal", e.Message));
                var ir = Zig.ToIr(v, opts, out _);
                var hl = Zig.Highlight(v);
                Assert.Equal(v.Length, hl.Sum(t => t.Length));
                int pos = 0;
                foreach (var t in hl) { Assert.Equal(pos, t.Start); pos += t.Length; }
                if (ir != null)
                {
                    var printed = ((IPrinterDiagnostics)Zig).Print(ir, new PrintOptions { FileName = "main" }, out var problems);
                    Assert.DoesNotContain(problems, p => p.Contains("internal printer error"));
                    Assert.NotNull(printed);
                }
            }
        }
        output.WriteLine(cases + " fuzz cases");
        Assert.True(cases > 500);
    }
}
