using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using JavaFarmer.Compiler;
using JavaFarmer.Plugin.Extensions;
using JavaFarmer.Plugin.Extensions.AiMl;
using JavaFarmer.Plugin.Extensions.Cyber;

namespace JavaFarmer.Plugin.Tests.Extensions;

/// <summary>The framework: game binding, side effects, per-save state, toggles, registration with the game and GameApi.</summary>
[Collection("Game")]
public class FrameworkTests
{
    public FrameworkTests()
    {
        ExtTestUtil.Setup();
        ExtTestUtil.Enabled.Add(CyberExtension.ExtId);
        ExtTestUtil.Enabled.Add(AiExtension.ExtId);
        ExtensionHost.Sync();
    }

    static Dictionary<string, int> CyberLevels(int level = 1) => ExtTestUtil.AllLevels(ExtTestUtil.Cyber, level);

    // ---------------------------------------------------------------- binding

    [Fact]
    public void Binding_ChecksArgumentCountAndTypes()
    {
        var sim = ExtTestUtil.NewSim(CyberLevels());
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "vault_try"));
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "vault_try", 1, 2));
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "vault_try", "1234"));
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "vault_try", 1.5));
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "char_code", 5, 0));
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "decrypt_submit", 3.0));
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "submit_line", "a", 1.0));
    }

    [Fact]
    public void Binding_LogicErrorsBecomeExecuteExceptions()
    {
        var sim = ExtTestUtil.NewSim(CyberLevels());
        var e = Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "vault_check_digit", 9, 0));
        Assert.Contains("vault_check_digit()", e.Message);
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "char_code", "A", 3));
        Assert.Throws<ExecuteException>(() => ExtTestUtil.GameCall(sim, "packet_port"));
    }

    [Fact]
    public void PureBuiltin_ReturnsDirectly_WithoutSideEffect()
    {
        var sim = ExtTestUtil.NewSim(CyberLevels());
        var exec = ExtTestUtil.NewExecution(sim);
        var f = ExtensionHost.FunctionFor(ExtTestUtil.Cyber, ExtTestUtil.Cyber.Builtin("char_code"));
        double ticks = f.binding(ExtTestUtil.Py("ABC", 2), sim, exec, 0);
        Assert.Equal(1, ticks);
        Assert.Equal(SideEffect.None, exec.States[0].currentSideEffect);
        Assert.Equal(67.0, (double)(PyNumber)exec.States[0].ReturnValue);
        Assert.Equal("HELLO", ((PyString)ExtTestUtil.GameCall(sim, "from_char_code", 72)).str + "ELLO");
    }

    [Fact]
    public void SideEffectBuiltin_RunsInTheSideEffectPhase_AndPays()
    {
        var sim = ExtTestUtil.NewSim(CyberLevels());
        var exec = ExtTestUtil.NewExecution(sim);
        var st = exec.States[0];
        var f = ExtensionHost.FunctionFor(ExtTestUtil.Cyber, ExtTestUtil.Cyber.Builtin("vault_try"));
        var state = (CyberState)ExtensionHost.StateFor(ExtTestUtil.Cyber, sim);
        state.Vault.Ensure(state.Seed, 1);
        Assert.Equal(0, f.binding(ExtTestUtil.Py(state.Vault.Pin), sim, exec, 0));
        Assert.Equal(ExtensionHost.SideEffectBase + ExtensionHost.IndexOf(ExtTestUtil.Cyber), (int)st.currentSideEffect);
        Assert.IsType<ExtensionHost.PendingCall>(st.currentSideEffectArgument2);
        double before = ExtTestUtil.ItemCount(sim, "bits");
        double r = 0;
        Assert.False(ExtensionPatches.ApplySideEffectPrefix(exec, 0, ref r));
        Assert.Equal(200, r);
        Assert.Equal(200, st.OpCount);
        Assert.True((double)(PyNumber)st.ReturnValue != 0);
        Assert.Equal(before + 20, ExtTestUtil.ItemCount(sim, "bits"));
    }

    [Fact]
    public void ApplySideEffectPrefix_LeavesVanillaSideEffectsAlone()
    {
        var sim = ExtTestUtil.NewSim();
        var exec = ExtTestUtil.NewExecution(sim);
        exec.States[0].currentSideEffect = SideEffect.Harvest;
        double r = 0;
        Assert.True(ExtensionPatches.ApplySideEffectPrefix(exec, 0, ref r));
        exec.States[0].currentSideEffect = (SideEffect)1000;
        exec.States[0].currentSideEffectArgument2 = null;
        Assert.False(ExtensionPatches.ApplySideEffectPrefix(exec, 0, ref r));
        Assert.IsType<PyNone>(exec.States[0].ReturnValue);
    }

    [Fact]
    public void Penalty_TakesItemsFromTheFarm_NeverBelowZero()
    {
        var sim = ExtTestUtil.NewSim(CyberLevels());
        sim.farm.Items.items[StringIds.GetItemId("bits")] = 30;
        var st = (CyberState)ExtensionHost.StateFor(ExtTestUtil.Cyber, sim);
        st.Ids.Ensure(st.Seed, 1);
        var legit = st.Ids.Log.First(e => !st.Ids.Attackers.Contains(e.Ip)).Ip;
        Assert.False(ExtArgsBool(ExtTestUtil.GameCall(sim, "block_ip", legit)));
        Assert.Equal(0, ExtTestUtil.ItemCount(sim, "bits"));
    }

    static bool ExtArgsBool(IPyObject p) => (bool)ExtArgs.FromPy(p);

    // ---------------------------------------------------------------- state

    [Fact]
    public void SimulateAndLeaderboardRuns_UseACopyOfTheState()
    {
        ExtensionHost.ResetForTests();
        var main = ExtTestUtil.NewSim(CyberLevels());
        var simulated = ExtTestUtil.NewSim(CyberLevels(), LeaderboardType.simulation);
        var a = (CyberState)ExtensionHost.StateFor(ExtTestUtil.Cyber, main);
        a.Vault.Ensure(a.Seed, 1);
        int pin = a.Vault.Pin;
        var b = (CyberState)ExtensionHost.StateFor(ExtTestUtil.Cyber, simulated);
        Assert.NotSame(a, b);
        Assert.Same(b, ExtensionHost.StateFor(ExtTestUtil.Cyber, simulated));
        Assert.True(ExtArgsBool(ExtTestUtil.GameCall(simulated, "vault_try", pin)));
        Assert.Equal(pin, a.Vault.Pin);                         // the real vault is unchanged
        Assert.Equal(20, ExtTestUtil.ItemCount(simulated, "bits"));
        Assert.Equal(0, ExtTestUtil.ItemCount(main, "bits"));
        Assert.Equal(a.Seed, b.Seed);
    }

    [Fact]
    public void SaveLoadRoundTrip_ContinuesEveryPuzzleExactly()
    {
        ExtensionHost.ResetForTests();
        var cyber = ExtTestUtil.CyberCalls(99, 2);
        cyber.Call("vault_pin_length");
        cyber.Call("intercept_message");
        var h = cyber.Call("leaked_hash", 0);
        cyber.Call("crack_submit", 0, ((CyberState)cyber.State).Hashing.Passwords[0]);
        cyber.Call("log_count");
        cyber.Call("block_ip", ((CyberState)cyber.State).Ids.Attackers.First());
        cyber.Call("packet_next"); cyber.Call("packet_allow"); cyber.Call("packet_next"); cyber.Call("packet_deny"); cyber.Call("packet_next");
        var ai = ExtTestUtil.AiCalls(77);
        ai.Call("sample_count");
        ai.Call("submit_centroid", 1, 3.5, 4.5);
        ai.Call("nn_submit_weight", 0, 1, 0, 2.5);
        ai.Call("nn_submit_bias", 1, 0, -1.25);
        ai.Call("submit_policy_action", 7, 2);
        ai.Call("env_reset");
        for (int k = 0; k < 2 && !ai.Bool("env_done"); k++) ai.Call("env_step", 2);
        ExtensionHost.SetMainState(ExtTestUtil.Cyber, cyber.State);
        ExtensionHost.SetMainState(ExtTestUtil.Ai, ai.State);
        var sections = new Dictionary<string, string>();
        ExtensionHost.SaveStates(sections, name => name == "bits" ? 123 : 0);
        Assert.Contains("cybersecurity", sections.Keys);
        Assert.Contains("ai_ml", sections.Keys);
        ExtensionHost.LoadStates(sections);
        var c2 = (CyberState)ExtensionHost.StateFor(ExtTestUtil.Cyber, null);
        var a2 = (AiState)ExtensionHost.StateFor(ExtTestUtil.Ai, null);
        Assert.NotSame(cyber.State, c2);
        Assert.Equal(ExtJson.Write(cyber.State.ToJson()), ExtJson.Write(c2.ToJson()));
        Assert.Equal(ExtJson.Write(ai.State.ToJson()), ExtJson.Write(a2.ToJson()));
        var o = (CyberState)cyber.State;
        Assert.Equal(o.Vault.Pin, c2.Vault.Pin);
        Assert.Equal(o.Crypto.Cipher, c2.Crypto.Cipher);
        Assert.Equal(o.Hashing.Hashes, c2.Hashing.Hashes);
        Assert.Equal(o.Hashing.Cracked, c2.Hashing.Cracked);
        Assert.Equal(o.Ids.Log.Select(e => e.Ip + e.User + e.Success), c2.Ids.Log.Select(e => e.Ip + e.User + e.Success));
        Assert.Equal(o.Firewall.Index, c2.Firewall.Index);
        Assert.Equal(o.Firewall.Current.Src, c2.Firewall.Current.Src);
        var oa = (AiState)ai.State;
        a2.Regression.Ensure(a2.Seed);
        Assert.Equal(oa.Regression.Y, a2.Regression.Y);
        // the episode continues with the same random slips
        var resumed = new ExtTestUtil.Calls { Ext = ExtTestUtil.Ai, State = a2, Levels = ai.Levels };
        for (int k = 0; k < 5 && !ai.Bool("env_done"); k++)
        {
            Assert.Equal(ai.Num("env_step", 1), resumed.Num("env_step", 1));
            Assert.Equal(ai.Int("env_state"), resumed.Int("env_state"));
        }
    }

    [Fact]
    public void ItemMirror_RestoresOnlyLostCounts()
    {
        ExtensionHost.ResetForTests();
        var sections = new Dictionary<string, string>();
        ExtensionHost.SaveStates(sections, name => name == "bits" ? 500 : name == "insight" ? 70 : 0);
        ExtensionHost.LoadStates(sections);
        var items = ItemBlock.CreateEmpty();
        items.items[StringIds.GetItemId("insight")] = 5;
        Assert.Equal(1, ExtensionHost.RestoreItemMirror(items));
        Assert.Equal(500, items.items[StringIds.GetItemId("bits")]);
        Assert.Equal(5, items.items[StringIds.GetItemId("insight")]);
        Assert.Equal(0, ExtensionHost.RestoreItemMirror(items));   // only once per load
    }

    [Fact]
    public void BrokenOrMissingSections_StartFresh()
    {
        ExtensionHost.LoadStates(new Dictionary<string, string> { ["cybersecurity"] = "{not json", ["ai_ml"] = "[]" });
        var a = ExtensionHost.StateFor(ExtTestUtil.Cyber, null);
        Assert.NotNull(a);
        ExtensionHost.LoadStates(null);
        Assert.NotSame(a, ExtensionHost.StateFor(ExtTestUtil.Cyber, null));
        var seeds = Enumerable.Range(0, 5).Select(_ => { ExtensionHost.LoadStates(null); return ExtensionHost.StateFor(ExtTestUtil.Cyber, null).Seed; }).Distinct().Count();
        Assert.Equal(5, seeds);
    }

    // ---------------------------------------------------------------- registration and toggles

    [Fact]
    public void Items_AreAlwaysRegistered_VanillaIdsUnchanged_NoSteamStats()
    {
        for (int i = 0; i < ExtTestUtil.VanillaItems.Length; i++) Assert.Equal(i, StringIds.GetItemId(ExtTestUtil.VanillaItems[i].name));
        Assert.Equal(ExtTestUtil.VanillaItems.Length, StringIds.GetItemId("bits"));
        Assert.Equal(ExtTestUtil.VanillaItems.Length + 1, StringIds.GetItemId("insight"));
        Assert.All(ExtensionHost.ItemSOs.Values, so => Assert.False(so.trackStats));
        // a second SetItemIds call (never happens in the game) does not add duplicates
        IEnumerable<string> names = ((ItemSO[])ExtensionHost.ItemsField.GetValue(null)).Select(i => i.itemName).ToList();
        int count = names.Count();
        ExtensionHost.InjectItems(ref names);
        Assert.Equal(count, names.Count());
    }

    [Fact]
    public void Toggle_AddsAndRemovesUnlocksBuiltinsItemsAndApi()
    {
        try
        {
            ExtTestUtil.Enabled.Remove(CyberExtension.ExtId);
            ExtensionHost.Sync();
            Assert.False(ExtensionHost.GameUnlocks.ContainsKey("cybersecurity"));
            Assert.False(BuiltinFunctions.Functions.ContainsKey("vault_try"));
            Assert.Null(GameApi.Find("vault_try"));
            Assert.False(ExtensionHost.ItemSOs["bits"].enabled);
            Assert.True(ExtensionHost.GameUnlocks.ContainsKey("machine_learning"));
            Assert.True(BuiltinFunctions.Functions.ContainsKey("submit_line"));
            Assert.NotNull(GameApi.Find("submit_line"));
            Assert.True(ExtensionPatches.InventoryAddItemPrefix(StringIds.GetItemId("insight")));
            Assert.False(ExtensionPatches.InventoryAddItemPrefix(StringIds.GetItemId("bits")));
            Assert.True(ExtensionPatches.InventoryAddItemPrefix(StringIds.GetItemId("hay")));
        }
        finally
        {
            ExtTestUtil.Enabled.Add(CyberExtension.ExtId);
            ExtensionHost.Sync();
        }
        Assert.Same(ExtensionHost.UnlockSOs["cybersecurity"], ExtensionHost.GameUnlocks["cybersecurity"]);
        Assert.Same(ExtensionHost.Functions["vault_try"], BuiltinFunctions.Functions["vault_try"]);
        Assert.Equal("cybersecurity", GameApi.Find("vault_try").Extension);
        Assert.True(ExtensionHost.ItemSOs["bits"].enabled);
        // other languages see the builtins under their naming convention
        var java = Languages.Get("java");
        Assert.Equal("vaultTry", java.ApiName("vault_try"));
        Assert.Equal("vault_try", java.FromApiName("vaultTry"));
    }

    [Fact]
    public void ExtensionRegistry_PersistsTheToggleInTheConfig_AndRaisesChanged()
    {
        var path = Path.Combine(Path.GetTempPath(), "jf-ext-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            var cfg = new ConfigFile(path, true);
            ExtensionRegistry.Init(cfg);
            var ext = new DummyExtension();
            ExtensionRegistry.Register(ext);
            Assert.False(ExtensionRegistry.IsEnabled(ext.Id));
            string changed = null;
            void OnChanged(string id, bool on) => changed = id + "=" + on;
            ExtensionRegistry.Changed += OnChanged;
            ExtensionRegistry.SetEnabled(ext.Id, true);
            ExtensionRegistry.Changed -= OnChanged;
            Assert.Equal(ext.Id + "=True", changed);
            Assert.True(ExtensionRegistry.IsEnabled(ext.Id));
            cfg.Save();
            Assert.Contains(ext.Id + " = true", File.ReadAllText(path));
            var reread = new ConfigFile(path, false);
            Assert.True(reread.Bind("Extensions", ext.Id, false).Value);
        }
        finally
        {
            ExtensionRegistry.Init(null);
            File.Delete(path);
        }
    }

    sealed class DummyExtension : IExtension
    {
        public string Id => "test_ext_" + GetHashCode();
        public string DisplayName => "Test";
        public string Description => "test";
    }

    static readonly string[] VanillaUnlocks =
    {
        "loops", "dinosaurs", "watering", "trees", "auto_unlock", "debug_2", "timing", "costs", "the_farmers_remains", "mazes", "import",
        "utilities", "dictionaries", "hats", "speed", "grass", "top_hat", "megafarm", "senses", "variables", "carrots", "debug", "operators",
        "cactus", "polyculture", "leaderboard", "expand", "plant", "simulation", "pumpkins", "lists", "functions", "fertilizer", "sunflowers",
    };

    [Fact]
    public void Unlocks_FollowTheGamesNamingRules_AndHangUnderExistingNodes()
    {
        var all = ExtensionHost.Extensions.SelectMany(e => e.Unlocks).ToList();
        foreach (var u in all)
        {
            Assert.Equal(u.Name.ToLowerInvariant(), u.Name);
            Assert.DoesNotMatch("_[0-9]+$", u.Name);
            Assert.False(u.Name.StartsWith("debug"));
            Assert.DoesNotContain(u.Name, VanillaUnlocks);
            Assert.True(VanillaUnlocks.Contains(u.Parent) || all.Any(o => o.Name == u.Parent), u.Name + " parent " + u.Parent);
            if (u.MaxLevel > 1) Assert.NotEmpty(u.LevelCosts);
            var so = ExtensionHost.UnlockSOs[u.Name];
            Assert.Null(so.mesh);
            Assert.Equal(u.Grants, so.unlocks);
            Assert.Equal(u.MaxLevel, so.maxUnlockLevel);
            Assert.Equal(u.LevelCosts.Count, so.multiUnlockCost.Count);
            foreach (var kv in u.Cost) Assert.Equal(kv.Value, so.unlockCost.items[StringIds.GetItemId(kv.Key)]);
        }
        Assert.Equal("lists", ExtTestUtil.Cyber.Root.Parent);
        Assert.Equal("utilities", ExtTestUtil.Ai.Root.Parent);
    }

    [Fact]
    public void Builtins_DoNotClashWithTheGame_AndFollowTheApiShapeRule()
    {
        var vanilla = new[] { "harvest", "can_harvest", "plant", "move", "print", "len", "range", "min", "max", "abs", "str", "random", "list", "set", "dict" };
        var types = new HashSet<string> { "int", "float", "number", "string", "bool", "void" };
        foreach (var ext in ExtensionHost.Extensions)
            foreach (var b in ext.Builtins)
            {
                Assert.DoesNotContain(b.Name, vanilla);
                Assert.DoesNotContain(b.Name, new[] { "def", "if", "elif", "else", "for", "in", "while", "and", "or", "not", "pass", "break", "continue", "return", "global", "import", "from" });
                Assert.Matches("^[a-z][a-z0-9_]*$", b.Name);
                Assert.Contains(b.Returns, types);
                Assert.True(b.Ticks >= 1, b.Name);
                var api = GameApi.Find(b.Name);
                Assert.NotNull(api);
                Assert.All(api.Params, p => Assert.Contains(p.TypeText, types));
                Assert.Equal(b.Params.Count, api.Params.Count);
            }
    }

    // ---------------------------------------------------------------- capstone bonuses

    [Fact]
    public void YieldBonus_OnlyOnHarvest_WholeItems_FractionsCarryOver()
    {
        var sim = ExtTestUtil.NewSim(new Dictionary<string, int> { ["secure_farm"] = 1 });
        int hay = StringIds.GetItemId("hay");
        double total = 0;
        for (int k = 0; k < 20; k++)
        {
            var items = new ItemBlock(hay, 10);
            ExtensionPatches.DroneHarvestPrefix();
            ExtensionPatches.CollectItemsPrefix(sim.farm, ref items);
            ExtensionPatches.DroneHarvestPostfix();
            Assert.Equal(Math.Floor(items.items[hay]), items.items[hay]);
            total += items.items[hay];
        }
        Assert.Equal(210, total);
        var other = new ItemBlock(hay, 10);
        ExtensionPatches.CollectItemsPrefix(sim.farm, ref other);   // not a harvest (e.g. a reward)
        Assert.Equal(10, other.items[hay]);
        try
        {
            ExtTestUtil.Enabled.Remove(CyberExtension.ExtId);
            var off = new ItemBlock(hay, 10);
            ExtensionPatches.DroneHarvestPrefix();
            ExtensionPatches.CollectItemsPrefix(sim.farm, ref off);
            ExtensionPatches.DroneHarvestPostfix();
            Assert.Equal(10, off.items[hay]);
        }
        finally { ExtTestUtil.Enabled.Add(CyberExtension.ExtId); }
    }

    [Fact]
    public void GrowthBonus_ShortensGrowTime()
    {
        var sim = ExtTestUtil.NewSim(new Dictionary<string, int> { ["smart_farm"] = 2 });
        var plant = (Growable)RuntimeHelpers.GetUninitializedObject(typeof(Growable));
        AccessTools.Field(typeof(FarmObject), "sim").SetValue(plant, sim);
        var d = Duration.FromSeconds(11);
        ExtensionPatches.GetGrowTimePostfix(plant, ref d);
        Assert.Equal(10.0, d.Seconds, 6);
    }

    [Fact]
    public void BonusLine_ShowsCurrentAndNext()
    {
        var def = ExtTestUtil.Cyber.Unlock("secure_farm");
        Assert.Contains("+10%", ExtensionPatches.BonusLine(def, 2));
        Assert.Contains("+15%", ExtensionPatches.BonusLine(def, 2));
        Assert.DoesNotContain("=>", ExtensionPatches.BonusLine(def, def.MaxLevel));
    }

    // ---------------------------------------------------------------- docs and strings at runtime

    [Fact]
    public void LoadDoc_ServesTranslatedPages_OnlyWhileEnabled()
    {
        var page = ExtTestUtil.Cyber.Root.Docs;
        ExtensionContent.CurrentLanguage = () => "java";
        try
        {
            string r = "";
            ExtensionPatches.LoadDocPostfix(page, ref r);
            Assert.Contains("vaultTry(", r);
            Assert.DoesNotContain("vault_try(", r);
            ExtensionContent.CurrentLanguage = () => "python";
            r = "";
            ExtensionPatches.LoadDocPostfix(page, ref r);
            Assert.Contains("vault_try(", r);
            string home = "## Unlocks\n{{unlocksTOC}}";
            ExtensionPatches.LoadDocPostfix("docs/home.md", ref home);
            Assert.Contains("(docs/mods/cybersecurity/index.md)", home);
            Assert.Contains("(docs/mods/ai_ml/index.md)", home);
            ExtTestUtil.Enabled.Remove(CyberExtension.ExtId);
            r = "";
            ExtensionPatches.LoadDocPostfix(page, ref r);
            Assert.Equal("", r);
            home = "x";
            ExtensionPatches.LoadDocPostfix("docs/home.md", ref home);
            Assert.DoesNotContain("cybersecurity", home);
            string escape = "";
            ExtensionPatches.LoadDocPostfix("docs/mods/../../secret.md", ref escape);
            Assert.Equal("", escape);
        }
        finally
        {
            ExtTestUtil.Enabled.Add(CyberExtension.ExtId);
            ExtensionContent.CurrentLanguage = () => SaveSidecar.Language;
        }
    }

    [Fact]
    public void MergeStrings_AddsEnabledKeys_RemovesDisabledOnes()
    {
        var dict = (Dictionary<string, string>)AccessTools.Field(typeof(Localizer), "language").GetValue(null);
        ExtensionContent.MergeStrings();
        Assert.Equal("Cybersecurity", dict["cybersecurity"]);
        Assert.True(dict.ContainsKey("unlock_descr_machine_learning"));
        try
        {
            ExtTestUtil.Enabled.Remove(AiExtension.ExtId);
            ExtensionContent.MergeStrings();
            Assert.False(dict.ContainsKey("unlock_descr_machine_learning"));
            Assert.True(dict.ContainsKey("cybersecurity"));
        }
        finally
        {
            ExtTestUtil.Enabled.Add(AiExtension.ExtId);
            ExtensionContent.MergeStrings();
        }
    }

    [Fact]
    public void DisplayCode_IsTranslatedToTheLanguage()
    {
        Assert.Equal("vaultTry()", ExtensionContent.DisplayCode("vault_try()", "java"));
        Assert.Equal("vault_try()", ExtensionContent.DisplayCode("vault_try()", "python"));
    }

    [Fact]
    public void ParseStrings_ReadsTheGameFormat()
    {
        var kv = ExtensionContent.ParseStrings("@a = one\n@b = two\nlines\n\n@c_d = x = y\n");
        Assert.Equal(new[] { "a", "b", "c_d" }, kv.Select(k => k.Key));
        Assert.Equal("two\nlines", kv[1].Value);
        Assert.Equal("x = y", kv[2].Value);
    }

    // ---------------------------------------------------------------- ExtJson

    [Fact]
    public void ExtJson_RoundTrip()
    {
        var o = new Dictionary<string, object> { ["s"] = "a\"b\\c\n", ["n"] = 1.5, ["b"] = true, ["z"] = null, ["l"] = new List<object> { 1.0, "x", false } };
        var back = (Dictionary<string, object>)ExtJson.Parse(ExtJson.Write(o));
        Assert.Equal("a\"b\\c\n", back["s"]);
        Assert.Equal(1.5, back["n"]);
        Assert.Equal(true, back["b"]);
        Assert.Null(back["z"]);
        Assert.Equal(3, ((List<object>)back["l"]).Count);
        Assert.Throws<FormatException>(() => ExtJson.Parse("{\"a\": }"));
        Assert.Null(ExtJson.TryParseObject("[1]"));
        Assert.Equal(18446744073709551615UL, ExtJson.ULong(new Dictionary<string, object> { ["u"] = "18446744073709551615" }, "u"));
    }
}
