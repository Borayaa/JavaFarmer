namespace JavaFarmer.Languages.Tests.Zig;

/// <summary>Authoring helpers (skipped unless ZIG_DRAFT_OUT / ZIG_PROBE is set): the DocsTranslator draft of every EN page, and a probe
/// that compiles every *.zig of a directory (siblings = OtherFiles) and writes &lt;name&gt;.out.</summary>
public class ZigContentDraftGen
{
    [SkippableFact]
    public void WriteDraft()
    {
        var outDir = Environment.GetEnvironmentVariable("ZIG_DRAFT_OUT");
        Skip.If(string.IsNullOrEmpty(outDir), "draft generator");
        var zig = Compiler.Languages.Get("zig");
        var log = new System.Text.StringBuilder();
        foreach (var f in Directory.GetFiles(RepoPaths.GameDocs, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(RepoPaths.GameDocs, f).Replace('\\', '/');
            var md = File.ReadAllText(f).Replace("\r\n", "\n");
            var t = DocsTranslator.Translate(md, zig, out var fails, new DocsTranslateOptions { VerifyCompiles = true });
            var p = Path.Combine(outDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, t);
            foreach (var x in fails) log.AppendLine(rel + x);
        }
        File.WriteAllText(Path.Combine(outDir, "_failures.txt"), log.ToString());
        File.WriteAllText(Path.Combine(outDir, "_stub.zig"), zig.ApiStubFile(out var stubName));
        File.WriteAllText(Path.Combine(outDir, "_builtins.txt"), stubName + "\n" + string.Join("\n", GameApi.Builtins.Where(b => b.Extension == null).Select(b => b.PythonName + " | " + b.Signature + " -> " + b.ReturnText + "  | " + zig.ApiName(b.PythonName))));
    }

    [SkippableFact]
    public void Probe()
    {
        var dir = Environment.GetEnvironmentVariable("ZIG_PROBE");
        Skip.If(string.IsNullOrEmpty(dir), "probe");
        var zig = Compiler.Languages.Get("zig");
        foreach (var grp in Directory.GetDirectories(dir).Append(dir))
        {
            var files = Directory.GetFiles(grp, "*.zig");
            var all = files.ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => File.ReadAllText(f));
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var others = all.Where(kv => kv.Key != name && kv.Key != "main" && !kv.Key.StartsWith("t_")).ToDictionary(kv => kv.Key, kv => kv.Value);
                var code = all[name];
                var r = zig.Compile(code, new CompileOptions { FileName = name.StartsWith("t_") ? "main" : name, OtherFiles = others });
                string res = r.Success ? "OK\n" + r.Python + "\n" + (GameOracle.TryParse(r.Python) ?? "") : "FAIL\n" + string.Join("\n", r.Errors.Select(e => e.Message + " [" + (e.Start >= 0 && e.End <= code.Length && e.End >= e.Start ? code.Substring(e.Start, e.End - e.Start) : "?") + "]"));
                File.WriteAllText(Path.Combine(grp, name + ".out"), res);
            }
        }
    }
}
