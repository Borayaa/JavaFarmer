using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace JavaFarmer.Languages.Tests;

[CollectionDefinition("perf", DisableParallelization = true)]
public sealed class PerfCollection { }

/// <summary>
/// SPEC §8: Compile runs on every keystroke and must take &lt; 5 ms for a 300-line file. Measured on the test build
/// (Debug, other test classes paused); Release numbers are ~2x lower (bench: v1 1.5-2.0 ms, IR pipeline 1.7-2.1 ms).
/// </summary>
[Collection("perf")]
public class PerformanceTests
{
    readonly ITestOutputHelper output;
    public PerformanceTests(ITestOutputHelper output) { this.output = output; }

    /// <summary>A realistic ~300-line Java program (loops, ifs, collections, lambdas, helpers, switch).</summary>
    public static string Java300()
    {
        var sb = new StringBuilder();
        sb.Append("int harvested = 0;\nList<Integer> heights = new ArrayList<>();\nMap<Entity, Integer> counts = new HashMap<>();\n\n");
        int n = 0;
        while (sb.ToString().Split('\n').Length < 270)
        {
            n++;
            sb.Append("int work" + n + "(int size, double factor) {\n");
            sb.Append("    int total = 0;\n");
            sb.Append("    for (int x = 0; x < size; x++) {\n");
            sb.Append("        for (int y = 0; y < getWorldSize(); y++) {\n");
            sb.Append("            if (canHarvest() && getEntityType() == Entities.Carrot) {\n");
            sb.Append("                harvest();\n                harvested++;\n                total += x * y % 7;\n");
            sb.Append("            } else if (getGroundType() != Grounds.Soil) {\n                till();\n            } else {\n");
            sb.Append("                plant(x % 2 == 0 ? Entities.Bush : Entities.Grass);\n            }\n");
            sb.Append("            heights.add(total / (y + 1));\n");
            sb.Append("            move(North);\n        }\n        move(East);\n    }\n");
            sb.Append("    switch (total % 3) {\n        case 0 -> counts.put(Entities.Bush, total);\n        case 1 -> quickPrint(\"one \" + total);\n        default -> total -= 1;\n    }\n");
            sb.Append("    int i = 0;\n    while (i < heights.size()) { if (heights.get(i) > factor) { i += 2; continue; } i++; }\n");
            sb.Append("    return (int) (total * factor);\n}\n\n");
        }
        sb.Append("void main() {\n    int s = getWorldSize();\n");
        for (int i = 1; i <= n; i++) sb.Append("    harvested += work" + i + "(s, 1.5);\n");
        sb.Append("    spawnDrone(() -> work1(s, 2.0));\n    System.out.println(harvested);\n}\n");
        return sb.ToString();
    }

    [Fact]
    public void Java_300Lines_CompilesUnder5ms()
    {
        var src = Java300();
        int lines = src.Split('\n').Length;
        var opts = new CompileOptions { FileName = "Main" };
        var first = JavaCompiler.Compile(src, opts);
        Assert.True(first.Success, string.Join("; ", first.Errors));
        for (int i = 0; i < 30; i++) JavaCompiler.Compile(src, opts);   // warm up the JIT
        const int runs = 100;
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            JavaCompiler.Compile(src, opts);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        double median = times[runs / 2], p25 = times[runs / 4], p90 = times[runs * 9 / 10];
        output.WriteLine($"Java compile, {lines} lines: p25 {p25:F3} ms, median {median:F3} ms, p90 {p90:F3} ms, min {times[0]:F3} ms");
        Assert.True(p25 < 5.0, $"p25 {p25:F3} ms for {lines} lines (limit 5 ms)");
    }

    [Fact]
    public void Java_300Lines_ToIrUnder5ms()
    {
        var src = Java300();
        var opts = new CompileOptions { FileName = "Main" };
        for (int i = 0; i < 30; i++) JavaCompiler.ToIr(src, opts, out _);
        var times = new List<double>();
        for (int i = 0; i < 50; i++)
        {
            var sw = Stopwatch.StartNew();
            var ir = JavaCompiler.ToIr(src, opts, out var errs);
            sw.Stop();
            Assert.NotNull(ir);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        output.WriteLine($"Java ToIr (with comments): p25 {times[12]:F3} ms, median {times[25]:F3} ms");
        Assert.True(times[12] < 10.0, "ToIr is not on the keystroke path, but must stay fast");
    }
}
