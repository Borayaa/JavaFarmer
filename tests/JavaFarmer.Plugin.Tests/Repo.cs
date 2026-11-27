using System.Runtime.CompilerServices;

namespace JavaFarmer.Plugin.Tests;

/// <summary>Repo locations from this source file's compile-time path (works with any --artifacts-path).</summary>
public static class Repo
{
    public static string Root { get; } = FindRoot();

    static string FindRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    public static string Content => Path.Combine(Root, "content");
    public static string Corpus(string lang) => Path.Combine(Root, "tests", "corpus", lang);
    public static string GameLanguages => Path.Combine(Root, "TheFarmerWasReplaced_Data", "StreamingAssets", "Languages");

    public static string NewTempDir(string tag)
    {
        var baseDir = Environment.GetEnvironmentVariable("JF_TEST_TMP") ?? Path.GetTempPath();
        var dir = Path.Combine(baseDir, "jf-" + tag + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
