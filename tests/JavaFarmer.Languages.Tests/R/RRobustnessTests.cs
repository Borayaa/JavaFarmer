using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;
using static JavaFarmer.Languages.Tests.R.RTestUtil;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>Compile / Highlight / ToIr / Print never throw on broken input; Compile is fast.</summary>
[Collection("perf")]
public class RRobustnessTests
{
    readonly ITestOutputHelper output;
    public RRobustnessTests(ITestOutputHelper output) { this.output = output; }

    static IEnumerable<string> CorpusFiles() =>
        Directory.GetFiles(RepoPaths.Corpus("r"), "*.R", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal);

    const string Noise = "({[\"'`%<-}])#\\";

    [Fact]
    public void Fuzz_TruncatedAndGarbledCorpus_NeverThrows()
    {
        var rnd = new Random(1234);
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
                    var printed = Lang.Print(ir, new PrintOptions());
                    Assert.DoesNotContain("could not print", printed);
                }
            }
        }
        output.WriteLine(runs + " variants");
    }

    /// <summary>A realistic ~300-line R program (loops, ifs, lists, maps, closures, switch).</summary>
    public static string R300()
    {
        var sb = new StringBuilder();
        sb.Append("harvested <- 0\nheights <- c()\ncounts <- list()\n\n");
        int n = 0;
        while (sb.ToString().Split('\n').Length < 270)
        {
            n++;
            sb.Append("work" + n + " <- function(size, factor) {\n");
            sb.Append("  total <- 0\n");
            sb.Append("  for (x in seq_len(size)) {\n");
            sb.Append("    for (y in 1:get_world_size()) {\n");
            sb.Append("      if (can_harvest() && get_entity_type() == Entities$Carrot) {\n");
            sb.Append("        harvest()\n        harvested <<- harvested + 1\n        total <- total + x * y %% 7\n");
            sb.Append("      } else if (get_ground_type() != Grounds$Soil) {\n        till()\n      } else {\n");
            sb.Append("        plant(if (x %% 2 == 0) Entities$Bush else Entities$Grass)\n      }\n");
            sb.Append("      heights[[length(heights) + 1]] <<- total %/% y\n");
            sb.Append("      move(North)\n    }\n    move(East)\n  }\n");
            sb.Append("  switch(as.character(total %% 3),\n    \"0\" = counts[[Entities$Bush]] <<- total,\n    \"1\" = quick_print(paste(\"one\", total)),\n    total <- total - 1\n  )\n");
            sb.Append("  i <- 1\n  while (i <= length(heights)) {\n    if (heights[[i]] > factor) {\n      i <- i + 2\n      next\n    }\n    i <- i + 1\n  }\n");
            sb.Append("  trunc(total * factor)\n}\n\n");
        }
        sb.Append("s <- get_world_size()\n");
        for (int i = 1; i <= n; i++) sb.Append("harvested <- harvested + work" + i + "(s, 1.5)\n");
        sb.Append("spawn_drone(function() work1(s, 2))\nprint(harvested)\n");
        return sb.ToString();
    }

    [Fact]
    public void R_300Lines_CompilesUnder5ms()
    {
        var src = R300();
        int lines = src.Split('\n').Length;
        var opts = new CompileOptions { FileName = "main" };
        var first = Lang.Compile(src, opts);
        Assert.True(first.Success, string.Join("; ", first.Errors));
        // warm up by time, not by count: tiered JIT promotes hot methods to optimised code only after ~30 calls plus a
        // background delay, so 30 back-to-back compiles still measure partly unoptimised (tier-0) code
        var java = Compiler.Languages.Get("java");
        var jsrc = PerformanceTests.Java300();
        var jopts = new CompileOptions { FileName = "Main" };
        var warm = Stopwatch.StartNew();
        for (int i = 0; i < 30 || warm.ElapsedMilliseconds < 1500; i++) { Lang.Compile(src, opts); java.Compile(jsrc, jopts); }
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
        output.WriteLine($"R compile, {lines} lines: p25 {p25:F3} ms, median {median:F3} ms, min {times[0]:F3} ms; Java (interleaved) p25 {jp25:F3} ms");
        if (p25 < 5.0) return;
        // a loaded machine (other builds and tests, swapping): the R frontend (which also runs IrTypeInference) may not
        // be much slower than the Java frontend measured at the same moments (same rule as the JavaScript test)
        Assert.True(jp25 >= 4.0 && p25 <= jp25 * 1.5, $"p25 {p25:F3} ms for {lines} lines (limit 5 ms; Java p25 {jp25:F3} ms)");
    }
}
