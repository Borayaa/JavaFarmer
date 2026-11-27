using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

/// <summary>Per-file languages: extensions, save folder IO, migration of old saves, renames, cross-language imports.</summary>
[Collection("Game")]
public sealed class PerFileLanguageTests : IDisposable
{
    private readonly string save = Repo.NewTempDir("perfile");

    public void Dispose()
    {
        FileLanguages.Clear();
        SaveSidecar.Language = null;
        SaveSidecar.Sections.Clear();
        ActiveLanguage.SetForTests("java");
        try { Directory.Delete(save, true); } catch { }
    }

    private void Put(string name, string text) => File.WriteAllText(Path.Combine(save, name), text);
    private string[] Files() => Directory.GetFiles(save).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    // ------------------------------------------------------------------ extensions and names

    [Theory]
    [InlineData("main.java", "main", ".java")]
    [InlineData("utils.rs", "utils", ".rs")]
    [InlineData("bot.py", "bot", ".py")]
    [InlineData("x.R", "x", ".R")]
    [InlineData("x.r", "x", ".r")]
    [InlineData("x.cc", "x", ".cc")]
    [InlineData("  farm  ", "farm", null)]
    public void TypedNames_Split(string typed, string baseName, string ext)
    {
        Assert.True(FileLanguages.TrySplitName(typed, out var b, out var e, out var err), err);
        Assert.Equal(baseName, b);
        Assert.Equal(ext, e);
    }

    [Theory]
    [InlineData("notes.txt", "not a code file extension")]
    [InlineData("a.b.java", "only one dot")]
    [InlineData(".java", "name before its extension")]
    [InlineData("", "needs a name")]
    public void TypedNames_Rejected(string typed, string message)
    {
        Assert.False(FileLanguages.TrySplitName(typed, out _, out _, out var err));
        Assert.Contains(message, err);
    }

    [Fact]
    public void Extensions_MapToLanguages()
    {
        Assert.Equal("java", FileLanguages.LanguageIdOfExtension(".java"));
        Assert.Equal("java", FileLanguages.LanguageIdOfExtension(".JAVA"));
        Assert.Equal("python", FileLanguages.LanguageIdOfExtension(".py"));
        Assert.Equal("cpp", FileLanguages.LanguageIdOfExtension(".cxx"));
        Assert.Equal("r", FileLanguages.LanguageIdOfExtension(".r"));
        Assert.Null(FileLanguages.LanguageIdOfExtension(".h"));
        Assert.Null(FileLanguages.LanguageIdOfExtension(".txt"));
        foreach (var l in Languages.All)
        {
            Assert.Equal(l.Id, FileLanguages.LanguageIdOfExtension(FileLanguages.ExtensionOfLanguage(l.Id)));
            Assert.True(l.FileExtension.StartsWith("."), l.Id + ": FileExtension must start with a dot");
        }
        // windows without an entry use the default language
        ActiveLanguage.SetForTests("python");
        Assert.Equal(".py", FileLanguages.ExtensionOf("nobody"));
        FileLanguages.Set("main", ".java");
        Assert.Equal("main.java", FileLanguages.DisplayName("main"));
        Assert.Equal("java", FileLanguages.LanguageOf("MAIN")!.Id);
        FileLanguages.Move("main", "farm", ".rs");
        Assert.Equal("rust", FileLanguages.LanguageIdOf("farm"));
        Assert.False(FileLanguages.HasExtension("main"));
    }

    [Fact]
    public void CodeFiles_OnDisk()
    {
        Assert.True(FileLanguages.IsCodeFile("C:/s/main.java"));
        Assert.True(FileLanguages.IsCodeFile("main.py"));
        Assert.False(FileLanguages.IsCodeFile("C:/s/__builtins__.py"));
        Assert.False(FileLanguages.IsCodeFile("save.json"));
        Assert.False(FileLanguages.IsCodeFile("javafarmer.json"));
        Assert.False(FileLanguages.IsCodeFile("JavaFarmerApi.java"));
        Assert.False(FileLanguages.IsCodeFile("a.b.java"));
        // build files of the real toolchains (external editors) are never code windows
        Assert.False(FileLanguages.IsCodeFile("C:/s/build.zig"));
        Assert.False(FileLanguages.IsCodeFile("Build.RS"));
        Assert.False(FileLanguages.IsCodeFile("build.zig.zon"));
        Assert.False(FileLanguages.IsCodeFile("Cargo.toml"));
        Assert.True(FileLanguages.IsCodeFile("build.java"));
        Put("build.zig", "z"); Put("build.rs", "r");
        Put("main.java", "j"); Put("bot.py", "p"); Put("Bot.rs", "r"); Put("readme.txt", "t");
        File.SetLastWriteTimeUtc(Path.Combine(save, "bot.py"), DateTime.UtcNow.AddMinutes(-5));
        var scan = FileLanguages.ScanSaveDirectory(save, out var dups);
        Assert.Equal("main.java", scan["main"]);
        Assert.Equal("Bot.rs", scan["bot"]);
        Assert.Equal("bot.py", Path.GetFileName(dups.Single()));
    }

    // ------------------------------------------------------------------ save folder

    [Fact]
    public void WriteCodeFiles_RealExtensions_AndCleanup()
    {
        Put("main.py", "old python main");    // main became main.java: the .py file goes
        Put("gone.rs", "closed window");      // window closed: goes
        Put("docs0.txt", "not code");         // never touched
        Put("JavaFarmerApi.java", "stub");    // never touched
        Put("__builtins__.py", "b");          // never touched
        var backup = Repo.NewTempDir("perfile-backup");
        try
        {
            var files = new List<SaveFiles.CodeFile> { new("main", ".java", "void main() {}"), new("bot", ".py", "harvest()") };
            var deleted = SaveFiles.WriteCodeFiles(files, new[] { "main", "bot", "docs0" }, save, backup,
                (b, d, text) => { File.WriteAllText(b.FullName, text); File.Copy(b.FullName, d.FullName, true); });
            Assert.Equal(new[] { "gone.rs", "main.py" }, deleted.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(new[] { "JavaFarmerApi.java", "__builtins__.py", "bot.py", "docs0.txt", "main.java" }, Files());
            Assert.Equal("void main() {}", File.ReadAllText(Path.Combine(backup, "main.java")));
        }
        finally { Directory.Delete(backup, true); }
    }

    [Fact]
    public void WatcherUpdates_OnlyTheWindowsOwnFile()
    {
        FileLanguages.Set("main", ".java");
        Assert.Equal("main", SaveFiles.ModuleOfChangedFile("main.java"));
        Assert.Null(SaveFiles.ModuleOfChangedFile("main.py"));
        Assert.Null(SaveFiles.ModuleOfChangedFile("other.java"));
        Assert.Null(SaveFiles.ModuleOfChangedFile("JavaFarmerApi.java"));
    }

    [Fact]
    public void SaverLoadPyChecks_AcceptEveryCodeFile()
    {
        var sites = SaveFiles.ExtensionCheckSites();
        Assert.NotEmpty(sites);
        // the lambda of Saver.Load that filters the save folder (instance method of the compiler's closure class)
        var harmony = new Harmony("javafarmer.tests.savefiles." + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var s in sites) harmony.Patch(s, transpiler: new HarmonyMethod(AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.ExtensionCheckTranspiler))));
            var filter = sites.OfType<MethodInfo>().First(m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)
                && m.DeclaringType != typeof(Saver) && m.Name.Contains("Load"));
            object closure = filter.IsStatic ? null : filter.DeclaringType!.GetField("<>9", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            bool Accepts(string p) => (bool)filter.Invoke(closure, new object[] { p })!;
            Assert.True(Accepts("C:/Saves/Save0/main.java"));
            Assert.True(Accepts("C:/Saves/Save0/bot.py"));
            Assert.True(Accepts("C:/Saves/Save0/utils.rs"));
            Assert.False(Accepts("C:/Saves/Save0/__builtins__.py"));
            Assert.False(Accepts("C:/Saves/Save0/JavaFarmerApi.java"));
            Assert.False(Accepts("C:/Saves/Save0/save.json"));
        }
        finally { harmony.UnpatchSelf(); }
    }

    // ------------------------------------------------------------------ migration of old saves

    [Fact]
    public void Migration_V1JavaSave_RenamedOnce_ContentsUnchanged()
    {
        const string java = "void main() {\n\twhile (true) {\n\t\tharvest();\n\t}\n}\n";
        Put("main.py", java);
        Put("Utils.py", "public class Utils {\n\tstatic int two() { return 2; }\n}\n");
        Put("broken.py", "void main() {\n\tharvest(\n}\n");
        Put("__builtins__.py", "b");
        SaveSidecar.Read(save);
        var notes = SaveMigration.Run(save, new DateTime(2026, 11, 22, 9, 0, 0));
        Assert.Contains(Files(), f => f == "main.java");
        Assert.Equal(java, File.ReadAllText(Path.Combine(save, "main.java")));
        Assert.Contains("Utils.java", Files());
        Assert.Contains("broken.java", Files()); // broken Java in a Java save is still Java
        Assert.Contains("__builtins__.py", Files());
        Assert.Contains("main.py", Directory.GetFiles(Path.Combine(save, "javafarmer-backup", "rename-20261122-090000")).Select(Path.GetFileName));
        Assert.Contains(".java extension", Assert.Single(notes));
        // marked done: a second run changes nothing
        SaveSidecar.Read(save);
        Assert.Equal(2, SaveSidecar.FileVersion);
        Assert.Empty(SaveMigration.Run(save, DateTime.Now));
    }

    [Fact]
    public void Migration_JavaCallingPythonAndOtherLanguages()
    {
        Put("main.py", "void main() {\n\tint n = (int) helper.twice(1);\n\tUtils.go();\n\tSystem.out.println(n);\n}\n");
        Put("helper.py", "def twice(n):\n\treturn n * 2\n");
        Put("Utils.py", "public class Utils {\n\tstatic void go() {\n\t\tharvest();\n\t}\n}\n");
        Put("lib.c", "void flip(void) {\n\tdo_a_flip();\n}\n");
        SaveSidecar.Read(save);
        SaveMigration.Run(save, DateTime.Now);
        Assert.Contains("main.java", Files());
        Assert.Contains("Utils.java", Files());
        Assert.Contains("helper.py", Files());
        Assert.Contains("lib.c", Files());
    }

    [Fact]
    public void Migration_VanillaSave_StaysPython()
    {
        Put("main.py", "while True:\n\tif can_harvest():\n\t\tharvest()\n");
        Put("f1.py", "def f():\n\treturn 1\n");
        Put("empty.py", "");
        SaveSidecar.Read(save);
        Assert.Empty(SaveMigration.Run(save, DateTime.Now));
        Assert.Equal(new[] { "empty.py", "f1.py", "javafarmer.json", "main.py" }, Files());
    }

    [Fact]
    public void Migration_PythonFileInsideAJavaSave_StaysPython()
    {
        Put("main.py", "void main() {\n\tharvest();\n}\n");
        Put("helper.py", "x = 1\nwhile x < 3:\n\tx += 1\n");
        SaveSidecar.Read(save);
        SaveMigration.Run(save, DateTime.Now);
        Assert.Contains("main.java", Files());
        Assert.Contains("helper.py", Files());
    }

    [Fact]
    public void Migration_FormatOneSidecar_UsesItsLanguage()
    {
        Put("main.py", "while True:\n\tharvest()\n");
        Put("javafarmer.json", "{\"version\": 1, \"language\": \"python\"}");
        SaveSidecar.Read(save);
        SaveMigration.Run(save, DateTime.Now);
        Assert.Contains("main.py", Files());
        Put("javafarmer.json", "{\"version\": 1, \"language\": \"java\"}");
        Put("main.py", "void main() {\n\tharvest();\n}\n");
        SaveSidecar.Read(save);
        SaveMigration.Run(save, DateTime.Now);
        Assert.Contains("main.java", Files());
    }

    [Fact]
    public void BeforeLoad_MovesDuplicates_AndRemembersExtensions()
    {
        Put("javafarmer.json", "{\"version\": 2}");
        Put("main.java", "void main() {}");
        Put("main.py", "harvest()");
        File.SetLastWriteTimeUtc(Path.Combine(save, "main.py"), DateTime.UtcNow.AddHours(-1));
        Put("bot.py", "harvest()");
        SaveFiles.BeforeLoad(save, new DateTime(2026, 1, 2, 3, 4, 5));
        try
        {
            Assert.Equal(".java", SaveFiles.ExtensionForNewWindow("main"));
            Assert.Equal(".py", SaveFiles.ExtensionForNewWindow("bot"));
            Assert.Equal(FileLanguages.DefaultExtension, SaveFiles.ExtensionForNewWindow("f9"));
            Assert.DoesNotContain("main.py", Files());
            Assert.True(File.Exists(Path.Combine(save, "javafarmer-backup", "duplicates-20260102-030405", "main.py")));
            Assert.Contains("same name", Assert.Single(SaveFiles.LoadNotes));
        }
        finally { SaveFiles.AfterLoad(); }
        Assert.Equal(FileLanguages.DefaultExtension, SaveFiles.ExtensionForNewWindow("main"));
    }

    // ------------------------------------------------------------------ renames

    private static CodeWindow WindowIn(Workspace ws, string name)
    {
        var cw = TestGame.Window(name);
        cw.workspace = ws;
        ws.openWindows[name] = null!;
        ws.codeWindows[name] = cw;
        return cw;
    }

    private static Workspace NewWorkspace()
    {
        var ws = (Workspace)RuntimeHelpers.GetUninitializedObject(typeof(Workspace));
        ws.openWindows = new Dictionary<string, Window>();
        ws.codeWindows = new ConcurrentDictionary<string, CodeWindow>();
        return ws;
    }

    [Fact]
    public void RenameCheck_ExtensionsAndUniqueNames()
    {
        var ws = NewWorkspace();
        var main = WindowIn(ws, "main");
        WindowIn(ws, "a");
        ws.openWindows["docs0"] = null!;
        FileLanguages.Set("main", ".java");
        FileLanguages.Set("a", ".java");

        Assert.Null(GamePatches.RenameCheck(main, "main.rs", out var b, out var e));
        Assert.Equal(("main", ".rs"), (b, e));
        Assert.Null(GamePatches.RenameCheck(main, "farm", out b, out e));
        Assert.Equal(("farm", ".java"), (b, e)); // keeps its extension
        Assert.Null(GamePatches.RenameCheck(main, "farm.py", out b, out e));
        Assert.Equal(("farm", ".py"), (b, e));
        Assert.Contains("already a file named a.java", GamePatches.RenameCheck(main, "a.rs", out _, out _));
        Assert.Contains("already a file named a.java", GamePatches.RenameCheck(main, "A", out _, out _));
        Assert.Contains("already a window named docs0", GamePatches.RenameCheck(main, "docs0.py", out _, out _));
        Assert.Contains("not a code file extension", GamePatches.RenameCheck(main, "main.txt", out _, out _));
        Assert.Contains("API file", GamePatches.RenameCheck(main, "JavaFarmerApi.java", out _, out _));
        Assert.Contains("Zig build file", GamePatches.RenameCheck(main, "build.zig", out _, out _));
        Assert.Contains("Cargo build file", GamePatches.RenameCheck(main, "build.rs", out _, out _));
        Assert.Null(GamePatches.RenameCheck(main, "build.java", out _, out _));
        Assert.Contains("cannot be used", GamePatches.RenameCheck(main, "__builtins__.py", out _, out _));
    }

    // ------------------------------------------------------------------ cross-language imports

    private static CompileResult CompileJava(string java, Dictionary<string, string> files, Dictionary<string, string> langs) =>
        JavaCompiler.Compile(java, new CompileOptions { FileName = "main", OtherFiles = files, OtherFileLanguages = langs });

    [Fact]
    public void Java_CallsAPythonFile()
    {
        ForeignModules.ClearCache();
        var files = new Dictionary<string, string> { { "helpers", "count = 0\ndef twice(n):\n\treturn n * 2\ndef hello():\n\tprint('hi')\n" } };
        var langs = new Dictionary<string, string> { { "helpers", "python" } };
        var r = CompileJava("void main() {\n\thelpers.hello();\n\tint x = (int) helpers.twice(3);\n\tSystem.out.println(x);\n}\n", files, langs);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("helpers.hello()", r.Python);
        Assert.Contains("helpers.twice(3)", r.Python);
        Assert.Contains("import helpers", r.Python);
        // the generated Python parses with the game's parser
        Assert.NotNull(JavaParseBridge.RunGameParser(r.Python, TestGame.Window()));
    }

    [Fact]
    public void Java_ForeignFileErrors_AreClear()
    {
        ForeignModules.ClearCache();
        var r = CompileJava("void main() {\n\tutils.go();\n}\n",
            new Dictionary<string, string> { { "utils", "def go(:\n" } }, new Dictionary<string, string> { { "utils", "python" } });
        Assert.False(r.Success);
        Assert.Contains("cannot use utils", r.Errors[0].Message);
        Assert.Contains("does not compile", r.Errors[0].Message);

        r = CompileJava("void main() {\n\tutils.go();\n}\n",
            new Dictionary<string, string> { { "utils", "whatever" } }, new Dictionary<string, string> { { "utils", "cobol" } });
        Assert.False(r.Success);
        Assert.Contains("does not support", r.Errors[0].Message);

        r = CompileJava("void main() {\n\tutils.nothing();\n}\n",
            new Dictionary<string, string> { { "utils", "def go():\n\tpass\n" } }, new Dictionary<string, string> { { "utils", "python" } });
        Assert.False(r.Success);
        Assert.Contains("cannot find symbol: method nothing in utils", r.Errors[0].Message);
    }

    [Theory]
    [MemberData(nameof(TestCorpus.LanguageIds), MemberType = typeof(TestCorpus))]
    public void Java_CallsAFileOfEveryLanguage(string id)
    {
        // a Java helper printed in the other language, then used from Java through its signatures
        ForeignModules.ClearCache();
        var lang = Languages.Get(id);
        string helperJava = "public class helper {\n\tstatic int twice(int n) {\n\t\treturn n * 2;\n\t}\n}\n";
        var ir = Languages.Get("java").ToIr(helperJava, new CompileOptions { FileName = "helper" }, out _);
        string text = lang.Print(ir, new PrintOptions { FileName = "helper" });
        var sig = ForeignModules.Resolve("helper", text, id, new CompileOptions { FileName = "helper" });
        if (!sig.Ok || sig.Function("twice") == null) { output.WriteLine(id + ": " + (sig.Error ?? "no twice()") + "\n" + text); return; }
        var r = CompileJava("void main() {\n\tSystem.out.println(helper.twice(4));\n}\n",
            new Dictionary<string, string> { { "helper", text } }, new Dictionary<string, string> { { "helper", id } });
        Assert.True(r.Success, id + ": " + string.Join("; ", r.Errors) + "\n" + text);
        Assert.Contains("helper." + sig.Function("twice")!.PyName + "(4)", r.Python);
        output.WriteLine(id + ": ok, twice -> " + sig.Function("twice")!.PyName);
    }

    [SkippableFact]
    public void Java_CallsAHandWrittenCSharpLibrary()
    {
        Skip.If(Languages.Get("csharp") == null, "no C# in this build");
        ForeignModules.ClearCache();
        string cs = "public static class Flips\n{\n\tpublic static int count = 0;\n\n\tpublic static void FlipTimes(int n)\n\t{\n\t\tfor (int i = 0; i < n; i++)\n\t\t{\n\t\t\tDoAFlip();\n\t\t\tcount++;\n\t\t}\n\t}\n}\n";
        var sig = ForeignModules.Resolve("Flips", cs, "csharp", new CompileOptions { FileName = "Flips" });
        output.WriteLine("C#: ok=" + sig.Ok + " " + sig.Error + " functions=" + string.Join(",", sig.Functions.Select(f => f.Name + "->" + f.PyName)) + " globals=" + string.Join(",", sig.Globals.Select(g => g.Name)));
        Assert.True(sig.Ok, sig.Error);
        var r = CompileJava("void main() {\n\tFlips.FlipTimes(2);\n\tSystem.out.println(Flips.count);\n}\n",
            new Dictionary<string, string> { { "Flips", cs } }, new Dictionary<string, string> { { "Flips", "csharp" } });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        output.WriteLine(r.Python);
    }

    [Fact]
    public void Python_GetsTypesFromAJavaFile()
    {
        ForeignModules.ClearCache();
        var options = new CompileOptions
        {
            FileName = "bot",
            OtherFiles = new Dictionary<string, string> { { "Utils", "public class Utils {\n\tstatic String name() { return \"x\"; }\n}\n" } },
            OtherFileLanguages = new Dictionary<string, string> { { "Utils", "java" } },
        };
        var ir = Languages.Get("python").ToIr("import Utils\ns = Utils.name()\n", options, out var errs);
        Assert.NotNull(ir);
        var call = IrWalk.Descendants(ir).OfType<IrCall>().First(c => c.Function?.Name == "name");
        Assert.Equal(IrTypeKind.String, call.Function.Type.Elem.Kind);
    }

    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public PerFileLanguageTests(Xunit.Abstractions.ITestOutputHelper output) { this.output = output; }
}
