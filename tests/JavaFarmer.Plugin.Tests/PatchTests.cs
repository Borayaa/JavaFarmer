using HarmonyLib;
using Steamworks;

namespace JavaFarmer.Plugin.Tests;

[CollectionDefinition("Game", DisableParallelization = true)]
public class GameCollection { }

/// <summary>Every patch target exists in the real game DLLs, and the patches work when applied with HarmonyX on .NET 8.</summary>
[Collection("Game")]
public class PatchTests
{
    [Fact]
    public void EveryPatchTargetResolves()
    {
        var specs = PatchRegistry.All();
        var missing = specs.Where(s => s.Target() == null).Select(s => s.Name).ToList();
        Assert.Empty(missing);
        foreach (var s in specs)
            Assert.True(s.Prefix != null || s.Postfix != null || s.Transpiler != null, s.Name + " has no patch method");
        Assert.True(specs.Count >= 28, "expected >= 28 patches, got " + specs.Count);
    }

    [Fact]
    public void DependencyPatches_CoverNodeAndEveryOverride()
    {
        var names = PatchRegistry.DependencyNodeTypes().Select(t => t.Name).ToList();
        foreach (var expected in new[] { "Node", "AssignmentNode", "BinaryExprNode", "BranchNode", "ComparisonNode", "DefNode",
                     "DictNode", "ForNode", "ImportNode", "ListNode", "SetNode", "UnaryExprNode" })
            Assert.Contains(expected, names);
        // CallNode charges its dependency inside Execute: it is handled by the CallNode.Execute postfix instead.
        Assert.DoesNotContain("CallNode", names);
    }

    [Fact]
    public void PatchMethodParameters_MatchTargets()
    {
        // Harmony injects by parameter name: every non-special parameter of a patch method must exist on the target.
        foreach (var spec in PatchRegistry.All())
        {
            var target = spec.Target();
            var targetParams = target.GetParameters().Select(p => p.Name).ToHashSet();
            foreach (var m in new[] { spec.Prefix, spec.Postfix }.Where(m => m != null))
                foreach (var p in m.GetParameters())
                {
                    if (p.Name.StartsWith("__")) continue;
                    Assert.True(targetParams.Contains(p.Name), spec.Name + ": " + m.Name + " parameter '" + p.Name + "' not on target");
                }
        }
    }

    [Fact]
    public void PatchesApplyAndRunOnNet8()
    {
        var harmony = new Harmony("javafarmer.tests." + Guid.NewGuid().ToString("N"));
        // targets that call Unity internals cannot be re-JIT'ed by CoreCLR (Unity's Mono can): checked in-game
        var specs = PatchRegistry.All().Where(s => !s.HeadlessUnsafe).ToList();
        try
        {
            int applied = PatchRegistry.ApplyAll(harmony, specs);
            Assert.Equal(specs.Count, applied);

            // 1. Parser.Parse is redirected to the Java bridge (the vanilla parser would accept this Python token stream).
            GamePatches.JavaTextProvider = _ => "void main() {\n    harvest(\n}";
            GamePatches.OtherFilesProvider = _ => null;
            Tokenizer.Tokenize("x = 1", out var ts);
            Assert.Throws<ParseException>(() => Parser.Parse(ts, TestGame.Window()));

            // without Java text the patch falls back to the game's parser
            GamePatches.JavaTextProvider = _ => null;
            Tokenizer.Tokenize("x = 1", out ts);
            Assert.NotNull(Parser.Parse(ts, TestGame.Window()));

            // 2. CheckDependencies prefix honours policies
            Tokenizer.Tokenize("x = 1", out ts);
            var program = Parser.Parse(ts, TestGame.Window());
            var assign = TestGame.Nodes(program).OfType<AssignmentNode>().Single();
            var st = TestGame.State();
            assign.CheckDependencies(st, null);
            Assert.Single(st.currentDependencies);
            DependencyPolicies.Set(assign.boxedParams, new NodePolicy { Skip = true });
            st.currentDependencies.Clear();
            assign.CheckDependencies(st, null);
            Assert.Empty(st.currentDependencies);
            DependencyPolicies.Set(assign.boxedParams, new NodePolicy { Override = "for" });
            assign.CheckDependencies(st, null);
            Assert.Equal("for", st.currentDependencies.Single().Item1);

            // 3. CallNode.Execute postfix wraps calls that have a policy
            var call = new CallNode(TestGame.Window(), 3, 7);
            Assert.Equal("CallNode", call.Execute(st, null, 0).GetType().DeclaringType?.Name);
            DependencyPolicies.Set(call.boxedParams, new NodePolicy { Skip = true, IsCall = true });
            Assert.NotEqual("CallNode", call.Execute(st, null, 0).GetType().DeclaringType?.Name);

            // 4. Syntax colouring is the Java highlighter ('#' is a Python comment, not a Java one)
            Assert.DoesNotContain("#666666", CodeUtilities.SyntaxColor2("# not a comment"));

            // 6. Values are printed Java-style (print, String.valueOf, hover tooltips, error texts)
            Assert.Equal("[true, null, 1.5]", CodeUtilities.ToNiceString(new PyList(new List<IPyObject> { new PyBool(true), new PyNone(), new PyNumber(1.5) })));
            Assert.Equal("false", CodeUtilities.ToNiceString(new PyBool(false)));

            // 7. Docs home "Built-in Functions" index uses Java names
            var md = (MarkdownText)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MarkdownText));
            var toc = (string)AccessTools.Method(typeof(MarkdownText), "GenerateBuiltinsTOC").Invoke(md, null);
            Assert.Contains("[doAFlip](functions/do_a_flip)", toc);
            Assert.DoesNotContain("[do_a_flip]", toc);

            // 8. Error traces name Java methods
            Assert.Equal("lambda <- main\nx", JavaText.RewriteTrace("__jlambda1 <- main\nx"));

            // 5. Leaderboard score uploads never reach Steam
            var handle = SteamUserStats.UploadLeaderboardScore(default, ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest, 1234, null, 0);
            Assert.Equal(SteamAPICall_t.Invalid, handle);
        }
        finally
        {
            harmony.UnpatchSelf();
            GamePatches.JavaTextProvider = GamePatches.DefaultJavaText;
            GamePatches.OtherFilesProvider = GamePatches.DefaultOtherFiles;
        }
    }

    [Fact]
    public void DependencyProbe_LogsNoHarmonyWarnings()
    {
        var messages = new List<string>();
        EventHandler<HarmonyLib.Tools.Logger.LogEventArgs> handler = (_, e) => messages.Add(e.Message);
        var oldFilter = HarmonyLib.Tools.Logger.ChannelFilter;
        HarmonyLib.Tools.Logger.ChannelFilter = HarmonyLib.Tools.Logger.LogChannel.All;
        HarmonyLib.Tools.Logger.MessageReceived += handler;
        try
        {
            Assert.NotEmpty(PatchRegistry.DependencyNodeTypes());
            foreach (var spec in PatchRegistry.All()) Assert.NotNull(spec.Target());
        }
        finally
        {
            HarmonyLib.Tools.Logger.MessageReceived -= handler;
            HarmonyLib.Tools.Logger.ChannelFilter = oldFilter;
        }
        Assert.DoesNotContain(messages, m => m.Contains("Could not find"));
    }

    [Fact]
    public void LeaderboardPrefix_ZeroesScore()
    {
        int score = 4321;
        GamePatches.LoadLeaderboardPrefix("hay", ref score);
        Assert.Equal(0, score);
    }

    [Fact]
    public void Fingerprint_ChangesWhenAnotherFileChanges()
    {
        var a = new Dictionary<string, string> { { "Utils", "void go() {}" } };
        var b = new Dictionary<string, string> { { "Utils", "void go() { }" } };
        Assert.NotEqual(GamePatches.Fingerprint(a), GamePatches.Fingerprint(b));
        Assert.Equal(GamePatches.Fingerprint(a), GamePatches.Fingerprint(new Dictionary<string, string>(a)));
        Assert.Equal(0, GamePatches.Fingerprint(null));
    }
}
