using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using JavaFarmer.Plugin.Extensions;
using JavaFarmer.Plugin.Extensions.AiMl;
using JavaFarmer.Plugin.Extensions.Cyber;

namespace JavaFarmer.Plugin.Tests.Extensions;

/// <summary>Headless stand-ins for the game objects the extension framework touches (real Core.dll / Utils.dll, no Unity runtime).</summary>
public static class ExtTestUtil
{
    static readonly object gate = new();
    static bool ready;
    public static readonly HashSet<string> Enabled = new();
    public static CyberExtension Cyber { get { Setup(); return (CyberExtension)ExtensionHost.Get(CyberExtension.ExtId); } }
    public static AiExtension Ai { get { Setup(); return (AiExtension)ExtensionHost.Get(AiExtension.ExtId); } }

    public static readonly (string name, float priority)[] VanillaItems =
    {
        ("hay", 1), ("wood", 2), ("carrot", 3), ("pumpkin", 7), ("cactus", 8), ("bone", 9), ("weird_substance", 9.5f),
        ("gold", 10), ("water", 11), ("fertilizer", 12), ("power", 13), ("piggy", 100000),
    };

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JavaFarmer.sln"))) dir = dir.Parent;
        if (dir == null)
        {
            // artifacts outside the repository: start from this source file
            dir = new FileInfo(SourceFile()).Directory;
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JavaFarmer.sln"))) dir = dir.Parent;
            if (dir != null) return dir.FullName;
            // scratch builds live outside the repository: the test assembly records where its sources are
            var env = Environment.GetEnvironmentVariable("JAVAFARMER_REPO");
            if (!string.IsNullOrEmpty(env)) return env;
            throw new InvalidOperationException("repository root (JavaFarmer.sln) not found");
        }
        return dir.FullName;
    }

    static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    public static string ContentRoot => Path.Combine(RepoRoot(), "content");

    /// <summary>Registers both extensions, fake item table (vanilla + extension items), empty unlock table. Idempotent.</summary>
    public static void Setup()
    {
        lock (gate)
        {
            if (ready) return;
            ExtensionHost.NewItemSO = () => (ItemSO)RuntimeHelpers.GetUninitializedObject(typeof(ItemSO));
            ExtensionHost.NewUnlockSO = () => (UnlockSO)RuntimeHelpers.GetUninitializedObject(typeof(UnlockSO));
            ExtensionHost.IsEnabled = id => Enabled.Contains(id);
            ExtensionContent.Root = ContentRoot;
            ExtensionHost.Register(new CyberExtension());
            ExtensionHost.Register(new AiExtension());
            var items = VanillaItems.Select(v =>
            {
                var so = (ItemSO)RuntimeHelpers.GetUninitializedObject(typeof(ItemSO));
                so.itemName = v.name; so.priority = v.priority; so.enabled = true; so.description = "item_descr_" + v.name;
                return so;
            }).ToArray();
            for (int i = 0; i < items.Length; i++) items[i].itemId = i;
            ExtensionHost.ItemsField.SetValue(null, items);
            IEnumerable<string> names = items.Select(i => i.itemName).ToList();
            ExtensionHost.InjectItems(ref names);
            StringIds.SetItemIds(names);
            if (ExtensionHost.GameUnlocks == null) ExtensionHost.UnlocksField.SetValue(null, new Dictionary<string, UnlockSO>());
            ExtensionHost.CreateUnlocks();
            var lang = AccessTools.Field(typeof(Localizer), "language");
            if (lang.GetValue(null) == null) lang.SetValue(null, new Dictionary<string, string>());
            Enabled.Add(CyberExtension.ExtId);
            Enabled.Add(AiExtension.ExtId);
            ExtensionHost.Sync();
            ready = true;
        }
    }

    // ------------------------------------------------------------------ logic-level calls

    public sealed class Calls
    {
        public ContentExtension Ext;
        public ExtState State;
        public Dictionary<string, int> Levels = new();
        public Dictionary<string, double> Items = new();
        public double Ticks;

        public object Call(string name, params object[] args)
        {
            var def = Ext.Builtin(name) ?? throw new ArgumentException("no builtin " + name);
            Assert.Equal(def.Params.Count, args.Length);
            var conv = args.Select(a => a is int i ? (object)(double)i : a is long l ? (double)l : a).ToArray();
            var call = new ExtCall
            {
                Def = def, Args = conv, State = def.Pure ? null : State, Ticks = def.Ticks,
                Level = u => Levels.TryGetValue(u, out var lv) ? lv : 0,
                Grant = (item, n) => { Items[item] = Items.GetValueOrDefault(item) + n; return n; },
                Take = (item, n) => { double have = Items.GetValueOrDefault(item), t = Math.Min(have, n); Items[item] = have - t; return t; },
            };
            var r = def.Impl(call);
            Ticks += call.Ticks;
            return r;
        }

        public double Num(string name, params object[] args) => Convert.ToDouble(Call(name, args));
        public int Int(string name, params object[] args) => (int)Num(name, args);
        public bool Bool(string name, params object[] args) => (bool)Call(name, args);
        public string Str(string name, params object[] args) => (string)Call(name, args);
        public double Has(string item) => Items.GetValueOrDefault(item);
    }

    public static Calls CyberCalls(ulong seed = 42, int level = 1)
    {
        Setup();
        var c = new Calls { Ext = Cyber, State = Cyber.NewState(seed) };
        foreach (var u in Cyber.Unlocks) c.Levels[u.Name] = level;
        return c;
    }

    public static Calls AiCalls(ulong seed = 42)
    {
        Setup();
        var c = new Calls { Ext = Ai, State = Ai.NewState(seed) };
        foreach (var u in Ai.Unlocks) c.Levels[u.Name] = 1;
        return c;
    }

    // ------------------------------------------------------------------ game objects

    static readonly FieldInfo farmItems = AccessTools.Field(typeof(Farm), "_items");
    static readonly FieldInfo farmUnlocks = AccessTools.Field(typeof(Farm), "unlocks");

    public static Simulation NewSim(Dictionary<string, int> unlocks = null, LeaderboardType type = LeaderboardType.none)
    {
        Setup();
        var sim = (Simulation)RuntimeHelpers.GetUninitializedObject(typeof(Simulation));
        var farm = (Farm)RuntimeHelpers.GetUninitializedObject(typeof(Farm));
        farmItems.SetValue(farm, ItemBlock.CreateEmpty());
        farmUnlocks.SetValue(farm, new Dictionary<string, int>(unlocks ?? new Dictionary<string, int>()));
        farm.drones = new List<Drone>();
        farm.sim = sim;
        sim.farm = farm;
        sim.leaderboardType = type;
        sim.randomRandom = new System.Random(1);
        AccessTools.Property(typeof(Simulation), nameof(Simulation.OpDuration)).SetValue(sim, Duration.FromSeconds(0.0025));
        return sim;
    }

    public static Dictionary<string, int> AllLevels(ContentExtension ext, int level = 1)
    {
        var d = new Dictionary<string, int>();
        foreach (var u in ext.Unlocks)
        {
            d[u.Name] = Math.Min(level, u.MaxLevel);
            foreach (var g in u.Grants) d[g] = 1;
        }
        return d;
    }

    public static double ItemCount(Simulation sim, string item) => sim.farm.Items.items[StringIds.GetItemId(item)];

    public static Execution NewExecution(Simulation sim)
    {
        var exec = (Execution)RuntimeHelpers.GetUninitializedObject(typeof(Execution));
        exec.sim = sim;
        var st = new ProgramState(0, new System.Random(1), 0);
        AccessTools.Field(typeof(Execution), "states").SetValue(exec, new List<ProgramState> { st });
        return exec;
    }

    public static List<IPyObject> Py(params object[] args) => args.Select(a => ExtArgs.ToPy(a is int i ? (double)i : a)).ToList();

    /// <summary>Calls a builtin through the game binding (+ the side-effect phase, as Execution would). Returns the value.</summary>
    public static IPyObject GameCall(Simulation sim, string name, params object[] args)
    {
        var exec = NewExecution(sim);
        var f = ExtensionHost.Functions.TryGetValue(name, out var fn) ? fn : null;
        if (f == null)
        {
            var ext = ExtensionHost.Extensions.First(e => e.Builtin(name) != null);
            f = ExtensionHost.FunctionFor(ext, ext.Builtin(name));
        }
        var st = exec.States[0];
        double ticks = f.binding(Py(args), sim, exec, 0);
        st.OpCount += ticks;
        if ((int)st.currentSideEffect >= ExtensionHost.SideEffectBase)
        {
            double r = 0;
            Assert.False(ExtensionPatches.ApplySideEffectPrefix(exec, 0, ref r));
        }
        LastTicks = st.OpCount;
        return st.ReturnValue;
    }

    public static double LastTicks;

    // ------------------------------------------------------------------ game parser

    public static Program Parse(string python)
    {
        var cw = (CodeWindow)RuntimeHelpers.GetUninitializedObject(typeof(CodeWindow));
        typeof(CodeWindow).GetField("parsedFunctions")!.SetValue(cw, new System.Collections.Concurrent.ConcurrentDictionary<string, FunctionNode>());
        cw.fileName = "main";
        Tokenizer.Tokenize(python, out var ts);
        return Parser.Parse(ts, cw);
    }
}
