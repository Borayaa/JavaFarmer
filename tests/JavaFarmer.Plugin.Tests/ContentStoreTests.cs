namespace JavaFarmer.Plugin.Tests;

public sealed class ContentStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jf-content-" + Guid.NewGuid().ToString("N"));
    private readonly string java;

    public ContentStoreTests()
    {
        java = Path.Combine(root, "java");
        Directory.CreateDirectory(Path.Combine(java, "docs", "scripting"));
        Directory.CreateDirectory(Path.Combine(java, "Strings"));
        File.WriteAllText(Path.Combine(java, "docs", "home.md"), "# Home\r\nJava!");
        File.WriteAllText(Path.Combine(java, "docs", "scripting", "while.md"), "while doc");
        File.WriteAllText(Path.Combine(java, "Strings", "code_tooltips.txt"),
            "@code_tooltip_harvest = Harvests.\n\n@code_tooltip_move = Moves.\nSecond line `move(North);`\n@broken line\n");
        File.WriteAllText(Path.Combine(java, "unlock_display_code.txt"), "@loops\nwhile (true) {\n}\n@Speed = if (x) {}\n@lists\nList.of(1, 2, 3)\n");
        File.WriteAllText(Path.Combine(root, "jf-secret.txt"), "secret");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "jf-secret.txt"), "secret");
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void BlockFile_OnlyBareNamesAreHeaders()
    {
        var blocks = ContentStore.ParseBlockFile("@import\n@import(\"utils.zig\").f();\n@@import\n@debug = std.debug.print(\"hi\\n\", .{});\n@lists  \nconst xs = [_]i32{ 1, 2 };\n@as(i32, 2)\n");
        Assert.Equal(new[] { "import", "debug", "lists" }, blocks.Select(b => b.Key).ToArray());
        Assert.Equal("@import(\"utils.zig\").f();\n@import", blocks[0].Value);
        Assert.Equal("std.debug.print(\"hi\\n\", .{});", blocks[1].Value);
        Assert.Equal("const xs = [_]i32{ 1, 2 };\n@as(i32, 2)", blocks[2].Value);
    }

    [Fact]
    public void Docs()
    {
        var c = new ContentStore(Path.Combine(root));
        Assert.True(c.TryGetDoc("docs/home.md", out var t));
        Assert.Equal("# Home\nJava!", t);
        Assert.True(c.TryGetDoc("docs/scripting/while.md", out t));
        Assert.False(c.TryGetDoc("docs/missing.md", out _));
        Assert.False(c.TryGetDoc("docs/scripting/while", out _));
        Assert.False(c.TryGetDoc("../jf-secret.txt", out _));
        Assert.False(c.TryGetDoc("../../jf-secret.txt", out _));
        Assert.False(c.TryGetDoc("docs/home.md", "../java", out _));
        Assert.False(c.TryGetDoc(null, out _));
    }

    [Fact]
    public void Strings_UseTheGameFormat()
    {
        var c = new ContentStore(root);
        Assert.Equal("Harvests.", c.Strings["code_tooltip_harvest"]);
        Assert.Equal("Moves.\nSecond line `move(North);`", c.Strings["code_tooltip_move"]);
        Assert.Equal(2, c.Strings.Count);
    }

    [Fact]
    public void DisplayCode_CaseInsensitive()
    {
        var c = new ContentStore(root);
        Assert.Equal("while (true) {\n}", c.DisplayCode["loops"]);
        Assert.Equal("if (x) {}", c.DisplayCode["speed"]);
        Assert.Equal("List.of(1, 2, 3)", c.DisplayCode["lists"]);
        Assert.Equal(3, c.DisplayCode.Count);
    }

    [Fact]
    public void ReadsTheRealContentFolder_WhenPresent()
    {
        string content = Repo.Content;
        if (!Directory.Exists(content)) return;
        var c = new ContentStore(content);
        Assert.True(c.TryGetDoc("docs/home.md", out var home) && home.Length > 0);
        Assert.False(File.Exists(Path.Combine(content, "unlock_display_code.txt")), "Java content lives in content/java now");
        Assert.False(Directory.Exists(Path.Combine(content, "docs")), "Java content lives in content/java now");
        if (File.Exists(Path.Combine(content, "java", "unlock_display_code.txt")))
        {
            Assert.NotEmpty(c.DisplayCode);
            Assert.DoesNotContain(c.DisplayCode.Keys, k => k.Contains(' ') || k.Length == 0);
        }
        if (Directory.Exists(Path.Combine(content, "java", "Strings")))
            Assert.Contains("code_tooltip_harvest", c.Strings.Keys);
    }

    [Fact]
    public void MissingFolder_IsTolerated()
    {
        var c = new ContentStore(Path.Combine(root, "nope"));
        Assert.False(c.Exists);
        Assert.False(c.TryGetDoc("docs/home.md", out _));
        Assert.Empty(c.Strings);
        Assert.Empty(c.DisplayCode);
        Assert.Empty(new ContentStore(null).Strings);
    }

    [Fact]
    public void ParsesTheGamesOwnStringFiles()
    {
        string en = Path.Combine(Repo.GameLanguages, "EN", "Strings", "code_tooltips.txt");
        if (!File.Exists(en)) return;
        var entries = ContentStore.ParseKeyValueFile(File.ReadAllText(en));
        Assert.True(entries.Count > 20);
        Assert.Contains(entries, e => e.Key == "code_tooltip_harvest");
    }
}
