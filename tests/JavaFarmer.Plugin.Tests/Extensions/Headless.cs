using JavaFarmer.Plugin.Extensions;

namespace JavaFarmer.Plugin.Tests.Extensions;

/// <summary>
/// Runs real game Python headless with the game's own interpreter (Tokenizer, Parser, Execution, ProgramState) and the
/// extension builtins. Only the side effects the docs programs need are emulated: extension calls, print/quick_print, num_items.
/// </summary>
public static class Headless
{
    public sealed class Result
    {
        public readonly List<string> Output = new();
        public double Ticks;
        public Simulation Sim;
        public string Error;
        public bool Finished;
        public double Item(string name) => ExtTestUtil.ItemCount(Sim, name);
        public override string ToString() => "ticks=" + Ticks + " finished=" + Finished + " error=" + Error + "\n" + string.Join("\n", Output.Take(40));
    }

    static readonly object gate = new();
    static bool patched;
    static readonly List<string> unityErrors = new();

    /// <summary>UnityEngine.Debug.LogError is native: headless, the game's "unexpected exception" log would crash.</summary>
    static void PatchUnityLog()
    {
        if (patched) return;
        patched = true;
        var h = new HarmonyLib.Harmony("javafarmer.tests.headless");
        h.Patch(HarmonyLib.AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogError), new[] { typeof(object) }),
            prefix: new HarmonyLib.HarmonyMethod(typeof(Headless), nameof(LogErrorPrefix)));
        // achievements talk to Steam
        foreach (var m in typeof(Achievements).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            if (m.ReturnType == typeof(void) && !m.IsGenericMethod && !m.IsSpecialName)
                h.Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(Headless), nameof(SkipPrefix)));
    }

    static bool SkipPrefix() => false;

    static bool LogErrorPrefix(object message)
    {
        lock (unityErrors) unityErrors.Add(message?.ToString());
        return false;
    }

    public static Result Run(string python, Dictionary<string, int> unlocks, double tickBudget = 2_000_000, Simulation sim = null)
    {
        ExtTestUtil.Setup();
        lock (gate)
        {
            PatchUnityLog();
            lock (unityErrors) unityErrors.Clear();
            var res = new Result { Sim = sim ?? ExtTestUtil.NewSim(unlocks) };
            var program = ExtTestUtil.Parse(python);
            var fns = BuiltinFunctions.Functions;
            var saved = new Dictionary<string, PyFunction>();
            void Swap(string name, PyFunction f) { if (!saved.ContainsKey(name)) saved[name] = fns.TryGetValue(name, out var old) ? old : null; fns[name] = f; }
            foreach (var name in new[] { "print", "quick_print" })
            {
                string n = name;
                Swap(n, new PyFunction(n, (p, s, e, d) =>
                {
                    res.Output.Add(string.Join(" ", p.Select(Format)));
                    e.States[d].ReturnValue = PyNone.Instance;
                    return n == "print" ? 1 : 0;
                }));
            }
            foreach (var ext in ExtensionHost.Extensions)
                foreach (var def in ext.Builtins) Swap(def.Name, ExtensionHost.FunctionFor(ext, def));
            try
            {
                var exec = new Execution(res.Sim, program.syntaxTree, 0);
                var st = exec.States[0];
                while (st.OpCount < tickBudget)
                {
                    st.PerformExecutionStep(double.MaxValue, out _);
                    st.currentDependencies.Clear();
                    var se = st.currentSideEffect;
                    if (se == SideEffect.None) continue;
                    if (se == SideEffect.Terminated) { res.Finished = true; break; }
                    if (se == SideEffect.Error) { lock (unityErrors) res.Error = st.currentExecuteException?.Message ?? string.Join("; ", unityErrors); break; }
                    try
                    {
                        if ((int)se >= ExtensionHost.SideEffectBase)
                        {
                            double r = 0;
                            ExtensionPatches.ApplySideEffectPrefix(exec, 0, ref r);
                        }
                        else if (se == SideEffect.NumItems)
                        {
                            st.ReturnValue = new PyNumber(res.Sim.farm.Items.items[((ItemSO)st.currentSideEffectArgument).itemId]);
                            st.OpCount += 1;
                        }
                        else throw new NotSupportedException("headless runner: side effect " + se);
                    }
                    catch (ExecuteException e) { res.Error = e.Message; break; }
                    st.currentSideEffect = SideEffect.None;
                    st.currentSideEffectArgument = null;
                    st.currentSideEffectArgument2 = null;
                }
                res.Ticks = st.OpCount;
            }
            finally
            {
                foreach (var kv in saved)
                    if (kv.Value == null) fns.Remove(kv.Key); else fns[kv.Key] = kv.Value;
            }
            return res;
        }
    }

    static string Format(IPyObject p)
    {
        var v = ExtArgs.FromPy(p);
        return v switch
        {
            null => "None",
            bool b => b ? "True" : "False",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => s,
            _ => v.ToString(),
        };
    }
}
