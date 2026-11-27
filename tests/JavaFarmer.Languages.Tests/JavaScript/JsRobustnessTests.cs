using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;
using static JavaFarmer.Languages.Tests.JavaScript.JsTestUtil;

namespace JavaFarmer.Languages.Tests.JavaScript;

/// <summary>Compile / Highlight / ToIr / Print never throw on broken input; Compile is fast (SPEC §8).</summary>
[Collection("perf")]
public class JsRobustnessTests
{
    readonly ITestOutputHelper output;
    public JsRobustnessTests(ITestOutputHelper output) { this.output = output; }

    static IEnumerable<string> CorpusFiles() =>
        Directory.GetFiles(RepoPaths.Corpus("js"), "*.js", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal);

    const string Noise = "({[\"'`$}])/*\\;=>.";

    [Fact]
    public void Fuzz_TruncatedAndGarbledCorpus_NeverThrows()
    {
        var rnd = new Random(4321);
        int runs = 0;
        foreach (var f in CorpusFiles())
        {
            var src = File.ReadAllText(f);
            var variants = new List<string>();
            for (int cut = 0; cut < src.Length; cut += Math.Max(1, src.Length / 23)) variants.Add(src.Substring(0, cut));
            for (int k = 0; k < 12; k++)
            {
                var sb = new StringBuilder(src);
                for (int j = 0; j < 3 && sb.Length > 0; j++)
                {
                    int at = rnd.Next(sb.Length);
                    switch (rnd.Next(4))
                    {
                        case 0: sb.Remove(at, 1); break;
                        case 1: sb.Insert(at, Noise[rnd.Next(Noise.Length)]); break;
                        case 2: sb[at] = (char)rnd.Next(32, 127); break;
                        default: sb.Insert(at, "\n"); break;
                    }
                }
                variants.Add(sb.ToString());
            }
            foreach (var v in variants)
            {
                runs++;
                var r = Lang.Compile(v, new CompileOptions { FileName = "main" });
                Assert.NotNull(r);
                if (!r.Success) Assert.DoesNotContain(r.Errors, e => e.Message.StartsWith("internal JavaFarmer compiler error"));
                Assert.Equal(v.Length, Lang.Highlight(v).Sum(t => t.Length));
                var ir = Lang.ToIr(v, new CompileOptions { FileName = "main" }, out _);
                if (ir != null)
                {
                    var printed = Lang.Print(ir, new PrintOptions(), out var problems);
                    Assert.DoesNotContain("could not print", printed);
                }
            }
        }
        output.WriteLine(runs + " variants");
        Assert.True(runs > 500);
    }

    /// <summary>A realistic ~300-line JavaScript program (loops, ifs, arrays, maps, closures, switch).</summary>
    public static string Js300()
    {
        var sb = new StringBuilder();
        sb.Append("let harvested = 0;\nconst heights = [];\nconst counts = new Map();\n\n");
        int n = 0;
        while (sb.ToString().Split('\n').Length < 270)
        {
            n++;
            sb.Append("function work" + n + "(size, factor) {\n");
            sb.Append("\tlet total = 0;\n");
            sb.Append("\tfor (let x = 0; x < size; x++) {\n");
            sb.Append("\t\tfor (let y = 0; y < getWorldSize(); y++) {\n");
            sb.Append("\t\t\tif (canHarvest() && getEntityType() === Entities.Carrot) {\n");
            sb.Append("\t\t\t\tharvest();\n\t\t\t\tharvested++;\n\t\t\t\ttotal += x * y % 7;\n");
            sb.Append("\t\t\t} else if (getGroundType() !== Grounds.Soil) {\n\t\t\t\ttill();\n\t\t\t} else {\n");
            sb.Append("\t\t\t\tplant(x % 2 === 0 ? Entities.Bush : Entities.Grass);\n\t\t\t}\n");
            sb.Append("\t\t\theights.push(Math.floor(total / (y + 1)));\n");
            sb.Append("\t\t\tmove(North);\n\t\t}\n\t\tmove(East);\n\t}\n");
            sb.Append("\tswitch (total % 3) {\n\t\tcase 0:\n\t\t\tcounts.set(Entities.Bush, total);\n\t\t\tbreak;\n\t\tcase 1:\n\t\t\tquickPrint(`one ${total}`);\n\t\t\tbreak;\n\t\tdefault:\n\t\t\ttotal -= 1;\n\t}\n");
            sb.Append("\tlet i = 0;\n\twhile (i < heights.length) { if (heights[i] > factor) { i += 2; continue; } i++; }\n");
            sb.Append("\treturn Math.trunc(total * factor);\n}\n\n");
        }
        sb.Append("const s = getWorldSize();\n");
        for (int i = 1; i <= n; i++) sb.Append("harvested += work" + i + "(s, 1.5);\n");
        sb.Append("spawnDrone(() => work1(s, 2));\nconsole.log(harvested);\n");
        return sb.ToString();
    }

    [Fact]
    public void Js_300Lines_CompilesUnder5ms()
    {
        var src = Js300();
        int lines = src.Split('\n').Length;
        Assert.InRange(lines, 295, 330);
        var opts = new CompileOptions { FileName = "main" };
        var first = Lang.Compile(src, opts);
        Assert.True(first.Success, string.Join("; ", first.Errors));
        var java = Compiler.Languages.Get("java");
        var jsrc = PerformanceTests.Java300();
        var jopts = new CompileOptions { FileName = "Main" };
        for (int i = 0; i < 30; i++) { Lang.Compile(src, opts); java.Compile(jsrc, jopts); }   // warm up the JIT
        const int runs = 100;
        var times = new List<double>();
        var jtimes = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            Lang.Compile(src, opts);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            // the Java frontend's own 300-line benchmark, interleaved: the reference for a loaded machine
            sw.Restart();
            java.Compile(jsrc, jopts);
            sw.Stop();
            jtimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        jtimes.Sort();
        double p25 = times[runs / 4], median = times[runs / 2], jp25 = jtimes[runs / 4];
        output.WriteLine($"JS compile, {lines} lines: p25 {p25:F3} ms, median {median:F3} ms, min {times[0]:F3} ms; Java (interleaved) p25 {jp25:F3} ms");
        if (p25 < 5.0) return;
        // a loaded machine (other builds and tests running): the JS frontend (which also runs IrTypeInference) may
        // not be much slower than the Java frontend measured at the same moments
        Assert.True(jp25 >= 4.0 && p25 <= jp25 * 1.5, $"p25 {p25:F3} ms for {lines} lines (limit 5 ms; Java p25 {jp25:F3} ms)");
    }
}
