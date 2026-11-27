using System.Runtime.CompilerServices;

namespace JavaFarmer.Languages.Tests;

/// <summary>Repo locations, found from this source file's compile-time path (works with any --artifacts-path).</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    static string FindRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    /// <summary>tests/corpus/&lt;lang&gt;</summary>
    public static string Corpus(string lang) => Path.Combine(Root, "tests", "corpus", lang);

    /// <summary>The game's English docs (markdown, single-backtick code spans).</summary>
    public static string GameDocs => Path.Combine(Root, "TheFarmerWasReplaced_Data", "StreamingAssets", "Languages", "EN", "docs");

    /// <summary>Scratch directory for tool runs (system temp; override with JF_TEST_TMP).</summary>
    public static string NewTempDir(string tag)
    {
        var baseDir = Environment.GetEnvironmentVariable("JF_TEST_TMP") ?? Path.GetTempPath();
        var dir = Path.Combine(baseDir, "jf-" + tag + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
