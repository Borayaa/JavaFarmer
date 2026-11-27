using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace JavaFarmer.Languages.Tests;

/// <summary>
/// Per-language plug-in for the shared harness (HarnessTests). Subclass it once per language in Hooks/&lt;Lang&gt;Hooks.cs
/// (public, parameterless constructor); the harness discovers it by reflection and runs, for every file of
/// tests/corpus/&lt;id&gt;/{valid,invalid,diff}:
///   (a) compile + expected diagnostics   (b) game-parser oracle   (c) real-toolchain conformance (Conformance)
///   (d) differential execution (Runner vs CPython of the emitted Python)   (e) round trip (Print → Compile)
///   (f) docs translation (EN docs Python blocks → Python frontend → Print → Compile; tests/corpus/&lt;id&gt;/docs-exceptions.txt).
/// </summary>
public abstract class LanguageHooks
{
    /// <summary>ILanguage.Id ("java", "c", ...).</summary>
    public abstract string Id { get; }

    public ILanguage Language => Compiler.Languages.Get(Id);
    public string CorpusDir => RepoPaths.Corpus(Id);
    /// <summary>Source file extension of corpus files (default: ILanguage.FileExtension).</summary>
    public virtual string Extension => Language?.FileExtension ?? "." + Id;

    /// <summary>Module name for a corpus file (default: file name without extension; Java: the class name).</summary>
    public virtual string ModuleName(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>(c) Real compiler/syntax check. Null: the language has none (skipped with a message).</summary>
    public virtual Toolchain Conformance => null;

    /// <summary>(d) Real build+run of a differential program, printing to stdout. Null: no differential tests.</summary>
    public virtual Toolchain Runner => null;

    /// <summary>(d) CPython prelude that makes print/str format values like this language's print does.</summary>
    public virtual string PythonPrelude => Prelude.JavaStyle;

    /// <summary>(d) Normalises both outputs before comparing (default: "3.0" → "3", CRLF → LF, trim).</summary>
    public virtual string NormaliseOutput(string s) => Normalise.Output(s);

    /// <summary>(a) Marker lines of invalid corpus files: "&lt;line comment&gt; ERROR: message" and "... AT: exact source text".</summary>
    public virtual string ErrorMarker => (Language?.Editor?.LineComment ?? "//") + " ERROR: ";
    public virtual string AtMarker => (Language?.Editor?.LineComment ?? "//") + " AT: ";

    /// <summary>(e) Options for printing a corpus program back.</summary>
    public virtual PrintOptions RoundTripOptions(string moduleName) => new PrintOptions { FileName = moduleName };

    /// <summary>(f) Options for printing a docs block before compiling it (full program, not a snippet).</summary>
    public virtual PrintOptions DocsOptions => new PrintOptions { FileName = "Docs" };
    /// <summary>(f) Module name used to compile translated docs blocks.</summary>
    public virtual string DocsModuleName => "Docs";

    static List<LanguageHooks> all;
    public static IReadOnlyList<LanguageHooks> All => all ??= typeof(LanguageHooks).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(LanguageHooks).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
        .Select(t => (LanguageHooks)Activator.CreateInstance(t)!)
        .OrderBy(h => Array.IndexOf(Compiler.Languages.Order, h.Id) is var i && i < 0 ? 999 : i).ToList();

    public static LanguageHooks Get(string id) => All.First(h => h.Id == id);
}

/// <summary>Files handed to a toolchain command.</summary>
public sealed class ToolContext
{
    /// <summary>Temp directory; sources are written here (relative paths = Sources keys) before the command runs.</summary>
    public string WorkDir;
    /// <summary>file name (relative, with extension) → source text, after PrepareSource.</summary>
    public IReadOnlyDictionary<string, string> Sources;
    /// <summary>The corpus file under test (key of Sources).</summary>
    public string MainFile;
    public string MainModule => Path.GetFileNameWithoutExtension(MainFile);
    public string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}

/// <summary>
/// An external command (real compiler, interpreter, docker). The first candidate whose Exe exists is used;
/// if none exists the test is skipped with SkipHint.
/// </summary>
public sealed class Toolchain
{
    /// <summary>Executable name looked up on PATH, or an absolute path.</summary>
    public string Exe;
    /// <summary>Arguments (one string, shell-like quoting with ").</summary>
    public Func<ToolContext, string> Args;
    /// <summary>Extra files (API stubs ...) to write into WorkDir: relative path → content.</summary>
    public Func<ToolContext, IReadOnlyDictionary<string, string>> ExtraFiles;
    /// <summary>Transforms a source before it is written (e.g. add an import line). Default: unchanged.</summary>
    public Func<string, string, string> PrepareSource;
    /// <summary>Optional second step (e.g. run the binary the first step built). Its output is the result.</summary>
    public Toolchain Then;
    /// <summary>Alternative when Exe is missing (e.g. docker r-base for Rscript).</summary>
    public Toolchain Fallback;
    public string SkipHint = "";
    public int TimeoutMs = 180000;
    /// <summary>Environment variables for the process.</summary>
    public Dictionary<string, string> Env = new();

    /// <summary>"./prog"-style executables live in the work dir (built by an earlier step).</summary>
    bool InWorkDir => Exe != null && !Path.IsPathRooted(Exe) && (Exe.Contains('/') || Exe.Contains('\\'));

    public Toolchain Resolve()
    {
        for (var t = this; t != null; t = t.Fallback)
            if (t.InWorkDir || Proc.Find(t.Exe) != null) return t;
        return null;
    }

    /// <summary>Runs the chain in ctx.WorkDir. Returns (exit code, combined output).</summary>
    public (int Exit, string Output) Run(ToolContext ctx)
    {
        var t = Resolve() ?? throw new InvalidOperationException("tool missing: " + Exe);
        if (t.ExtraFiles != null)
            foreach (var kv in t.ExtraFiles(ctx))
            {
                var p = Path.Combine(ctx.WorkDir, kv.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, kv.Value);
            }
        var exe = t.InWorkDir ? Path.GetFullPath(Path.Combine(ctx.WorkDir, t.Exe)) : Proc.Find(t.Exe)!;
        var r = Proc.Run(exe, t.Args(ctx), ctx.WorkDir, t.TimeoutMs, t.Env);
        if (r.Exit != 0 || t.Then == null) return r;
        return t.Then.Run(ctx);
    }
}

public static class Proc
{
    public static string Find(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        if (Path.IsPathRooted(exe)) return File.Exists(exe) ? exe : null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            var full = Path.Combine(dir, exe);
            if (File.Exists(full)) return full;
            if (OperatingSystem.IsWindows() && File.Exists(full + ".exe")) return full + ".exe";
        }
        return null;
    }

    public static (int Exit, string Output) Run(string exe, string args, string workDir, int timeoutMs = 120000, Dictionary<string, string> env = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "timeout after " + timeoutMs + " ms"); }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }
}
