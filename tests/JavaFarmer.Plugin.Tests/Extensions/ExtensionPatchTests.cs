using System.Reflection;
using HarmonyLib;
using JavaFarmer.Plugin.Extensions;

namespace JavaFarmer.Plugin.Tests.Extensions;

/// <summary>Every Harmony target and private field the extension framework uses exists in the real game DLLs.</summary>
[Collection("Game")]
public class ExtensionPatchTests
{
    [Fact]
    public void EveryPatchTargetResolves()
    {
        var specs = ExtensionPatches.All();
        Assert.Empty(specs.Where(s => s.Target() == null).Select(s => s.Name));
        Assert.All(specs, s => Assert.True(s.Prefix != null || s.Postfix != null, s.Name));
        Assert.Equal(12, specs.Count);
    }

    [Fact]
    public void PatchMethodParameters_MatchTheTargets()
    {
        foreach (var spec in ExtensionPatches.All())
        {
            var target = spec.Target();
            var names = target.GetParameters().ToDictionary(p => p.Name, p => p.ParameterType);
            foreach (var m in new[] { spec.Prefix, spec.Postfix }.Where(m => m != null))
                foreach (var p in m.GetParameters())
                {
                    if (p.Name == "__instance") { Assert.True(p.ParameterType.IsAssignableFrom(target.DeclaringType), spec.Name + " __instance"); continue; }
                    if (p.Name == "__result")
                    {
                        var ret = ((MethodInfo)target).ReturnType;
                        var want = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                        Assert.True(want.IsAssignableFrom(ret), spec.Name + " __result " + want + " vs " + ret);
                        continue;
                    }
                    Assert.True(names.ContainsKey(p.Name), spec.Name + ": " + m.Name + " parameter '" + p.Name + "' is not on the target");
                    var t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    Assert.Equal(names[p.Name], t);
                }
            if (spec.Prefix != null && spec.Prefix.ReturnType == typeof(bool))
                Assert.Contains(spec.Name, new[] { "Execution.ApplySideEffect", "Inventory.AddItem" });   // the only prefixes that may skip the original
        }
    }

    [Fact]
    public void PrivateGameMembers_Exist()
    {
        Assert.NotNull(ExtensionHost.ItemsField);
        Assert.Equal(typeof(ItemSO[]), ExtensionHost.ItemsField.FieldType);
        Assert.NotNull(ExtensionHost.UnlocksField);
        Assert.Equal(typeof(Dictionary<string, UnlockSO>), ExtensionHost.UnlocksField.FieldType);
        Assert.NotNull(ExtensionHost.KeywordDocsField);
        Assert.Equal(typeof(Dictionary<string, string>), ExtensionHost.KeywordDocsField.FieldType);
        Assert.Equal(typeof(UnityEngine.UI.Image), AccessTools.Field(typeof(ItemUI), "image")?.FieldType);
        Assert.Equal(typeof(Simulation), AccessTools.Field(typeof(MainSim), "sim")?.FieldType);
        Assert.Equal(typeof(Simulation), AccessTools.Field(typeof(FarmObject), "sim")?.FieldType);
        Assert.Equal(typeof(Dictionary<string, string>), AccessTools.Field(typeof(Localizer), "language")?.FieldType);
        Assert.NotNull(AccessTools.Constructor(typeof(PyFunction), new[] { typeof(string), typeof(Func<List<IPyObject>, Simulation, Execution, int, double>), typeof(IPyObject), typeof(bool) }));
        Assert.NotNull(typeof(BuiltinFunctions).GetProperty(nameof(BuiltinFunctions.Functions)));
        // the custom side effects are outside the vanilla enum
        Assert.True(Enum.GetValues(typeof(SideEffect)).Cast<int>().Max() < ExtensionHost.SideEffectBase);
    }

    [Fact]
    public void PatchesApplyWithHarmonyX_OnNet8()
    {
        var harmony = new Harmony("javafarmer.tests.extensions." + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(ExtensionPatches.All().Count, ExtensionPatches.Apply(harmony));
            // the ApplySideEffect prefix is live: a pending extension call runs through the real (patched) method
            ExtTestUtil.Setup();
            var sim = ExtTestUtil.NewSim(ExtTestUtil.AllLevels(ExtTestUtil.Cyber));
            var exec = ExtTestUtil.NewExecution(sim);
            var f = ExtensionHost.FunctionFor(ExtTestUtil.Cyber, ExtTestUtil.Cyber.Builtin("vault_pin_length"));
            f.binding(new List<IPyObject>(), sim, exec, 0);
            var applied = (double)AccessTools.Method(typeof(Execution), "ApplySideEffect").Invoke(exec, new object[] { 0 });
            Assert.Equal(1, applied);
            Assert.Equal(2.0, (double)(PyNumber)exec.States[0].ReturnValue);
        }
        finally
        {
            harmony.UnpatchSelf();
        }
    }
}
