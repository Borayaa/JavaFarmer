using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace JavaFarmer.Plugin.Tests;

/// <summary>
/// Runs every "expect=ok" code sample of content/java/_samples.txt through the full pipeline
/// (real compiler -> game Tokenizer/Parser -> span remap). The bridge must never produce an
/// "internal JavaFarmer error" (= the game rejected generated Python) and every span must be inside the Java text.
/// Plain compile errors are reported, not failed (they belong to the compiler/content work).
/// </summary>
[Collection("Game")]
public class ContentSamplesTests
{
    private readonly ITestOutputHelper output;
    public ContentSamplesTests(ITestOutputHelper output) { this.output = output; }

    private sealed record Sample(string Source, string Kind, string Expect, string Code);

    private static string SamplesPath() =>
        Path.Combine(Repo.Content, "java", "_samples.txt");

    private static (List<Sample> samples, string context) Load(string path)
    {
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        var samples = new List<Sample>();
        var ctx = new List<string>();
        bool inCtx = false;
        string src = null, kind = null, expect = null;
        var code = new List<string>();
        void Flush()
        {
            if (src != null) samples.Add(new Sample(src, kind, expect, string.Join("\n", code).Trim('\n')));
            code.Clear();
        }
        var header = new Regex(@"^### (\S+) \[(\w[\w-]*)\] expect=(\S+)");
        foreach (var line in lines)
        {
            var m = header.Match(line);
            if (m.Success)
            {
                Flush();
                src = m.Groups[1].Value; kind = m.Groups[2].Value; expect = m.Groups[3].Value;
                inCtx = false;
                continue;
            }
            if (src == null)
            {
                if (line.StartsWith("// Context declarations")) { inCtx = true; continue; }
                if (inCtx && line.StartsWith("//   ")) ctx.Add(line.Substring(5));
                continue;
            }
            code.Add(line);
        }
        Flush();
        return (samples, string.Join("\n", ctx));
    }

    private static string FirstGameFrame(Exception e) =>
        e.ToString().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("at ") && !l.StartsWith("at System.")) ?? "";

    [SkippableFact]
    public void ExpectOkSamples_NeverHitAnInternalError()
    {
        string path = SamplesPath();
        Skip.IfNot(File.Exists(path), "content/java/_samples.txt not present");
        var probe = JavaFarmer.Compiler.JavaCompiler.Compile("void main() {\n}\n", new JavaFarmer.Compiler.CompileOptions { FileName = "main" });
        Skip.If(!probe.Success, "JavaCompiler is still the stub");

        var (samples, context) = Load(path);
        // Game constants (Items.Hay, ...) are resolved through Unity resources at parse time: not available headless.
        context = Regex.Replace(context, @"= (Leaderboards|Items|Unlocks|Entities|Grounds|Hats)\.\w+", "= null");
        var classFiles = samples.Where(s => s.Kind == "program" && Regex.IsMatch(s.Code, @"\bclass\s+\w+"))
            .GroupBy(s => s.Source)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => Regex.Match(s.Code, @"\bclass\s+(\w+)").Groups[1].Value, s => s.Code));

        int ok = 0, compileErrors = 0, headless = 0;
        var internalErrors = new List<string>();
        foreach (var s in samples.Where(s => s.Expect == "ok"))
        {
            string fileName = "main";
            string java;
            IReadOnlyDictionary<string, string> others = null;
            switch (s.Kind)
            {
                case "program":
                    var cls = Regex.Match(s.Code, @"\bclass\s+(\w+)");
                    if (cls.Success) fileName = cls.Groups[1].Value;
                    java = s.Code;
                    if (classFiles.TryGetValue(s.Source, out var group))
                        others = group.Where(kv => kv.Key != fileName).ToDictionary(kv => kv.Key, kv => kv.Value);
                    break;
                case "methods":
                    java = context + "\n" + s.Code + (s.Code.Contains("void main(") ? "" : "\nvoid main() {}");
                    break;
                case "statements":
                    java = context + "\nvoid main() {\n" + s.Code + "\n}";
                    break;
                case "call":
                    java = context + "\nvoid main() {\n" + s.Code + ";\n}";
                    break;
                default:
                    continue;
            }
            try
            {
                var program = JavaParseBridge.Parse(java, TestGame.Window(fileName), others);
                foreach (var n in TestGame.Nodes(program).Where(n => n.boxedParams.wordStart >= 0))
                {
                    Assert.InRange(n.boxedParams.wordStart, 0, Math.Max(0, java.Length - 1));
                    Assert.InRange(n.boxedParams.wordEnd, n.boxedParams.wordStart, java.Length);
                }
                ok++;
            }
            catch (ParseException e) when (e.Message.Contains("error_invalid_const"))
            {
                // game constants (Unlocks.Speed ...) are checked against the game's resource tables: other tests may have
                // installed a fake (partial) table, which the real game never has
                headless++;
                output.WriteLine("needs the game's resources: " + s.Source + " [" + s.Kind + "]");
            }
            catch (ParseException e) when (e.Message.Contains("internal JavaFarmer error"))
            {
                internalErrors.Add(s.Source + " [" + s.Kind + "]: " + e.Message + "\n" + s.Code);
            }
            catch (Exception e) when (!(e is ParseException) && e.ToString().Contains("at Parser."))
            {
                // The game parser resolves constants (Entities.Carrot, ...) through Unity resources: not loaded headless.
                headless++;
                output.WriteLine("needs Unity: " + s.Source + " [" + s.Kind + "]: " + e.GetType().Name + " " + FirstGameFrame(e));
            }
            catch (ParseException e)
            {
                compileErrors++;
                int a = Math.Min(e.startIndex, java.Length), b = Math.Min(Math.Max(e.endIndex, a), java.Length);
                output.WriteLine("compile error in " + s.Source + " [" + s.Kind + "]: " + e.Message + " at '" + java.Substring(a, b - a) + "'");
            }
        }
        output.WriteLine($"parsed {ok}, compile errors {compileErrors}, needs Unity (not checkable headless) {headless}, internal errors {internalErrors.Count}");
        foreach (var ie in internalErrors) output.WriteLine("INTERNAL: " + ie);
        Assert.True(ok > 0);
        Assert.Empty(internalErrors);
    }
}
