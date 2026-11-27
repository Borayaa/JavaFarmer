// Harmony patches of the content-extension framework (own patch list; Patches.cs belongs to the language work).
// Every body catches its own exceptions, except the ExecuteExceptions of builtins (the game turns those into errors).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Extensions
{
    public static class ExtensionPatches
    {
        public sealed class Spec
        {
            public string Name;
            public Func<MethodBase> Target;
            public MethodInfo Prefix, Postfix;
            public int Priority = HarmonyLib.Priority.Normal;
            public override string ToString() => Name;
        }

        static MethodInfo M(string name) => AccessTools.Method(typeof(ExtensionPatches), name);

        public static List<Spec> All() => new List<Spec>
        {
            new Spec { Name = "StringIds.SetItemIds", Target = () => AccessTools.Method(typeof(StringIds), nameof(StringIds.SetItemIds), new[] { typeof(IEnumerable<string>) }),
                Prefix = M(nameof(SetItemIdsPrefix)) },
            new Spec { Name = "ResourceManager.LoadAll", Target = () => AccessTools.Method(typeof(ResourceManager), nameof(ResourceManager.LoadAll), Type.EmptyTypes),
                Postfix = M(nameof(LoadAllPostfix)) },
            new Spec { Name = "Saver.Load", Target = () => AccessTools.Method(typeof(Saver), nameof(Saver.Load), new[] { typeof(MainSim) }),
                Prefix = M(nameof(SaverLoadPrefix)), Postfix = M(nameof(SaverLoadPostfix)) },
            new Spec { Name = "Execution.ApplySideEffect", Target = () => AccessTools.Method(typeof(Execution), "ApplySideEffect", new[] { typeof(int) }),
                Prefix = M(nameof(ApplySideEffectPrefix)), Priority = HarmonyLib.Priority.First },
            new Spec { Name = "ItemUI.Setup", Target = () => AccessTools.Method(typeof(ItemUI), nameof(ItemUI.Setup), new[] { typeof(int), typeof(double) }),
                Postfix = M(nameof(ItemUISetupPostfix)) },
            new Spec { Name = "Inventory.AddItem", Target = () => AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(int), typeof(double) }),
                Prefix = M(nameof(InventoryAddItemPrefix)) },
            new Spec { Name = "Localizer.LoadDoc", Target = () => AccessTools.Method(typeof(Localizer), nameof(Localizer.LoadDoc), new[] { typeof(string) }),
                Postfix = M(nameof(LoadDocPostfix)), Priority = HarmonyLib.Priority.Last },
            new Spec { Name = "Localizer.LoadLang", Target = () => AccessTools.Method(typeof(Localizer), nameof(Localizer.LoadLang), new[] { typeof(string) }),
                Postfix = M(nameof(LoadLangPostfix)), Priority = HarmonyLib.Priority.Last },
            new Spec { Name = "TooltipUtils.UnlockTooltip", Target = () => AccessTools.Method(typeof(TooltipUtils), nameof(TooltipUtils.UnlockTooltip), new[] { typeof(string) }),
                Postfix = M(nameof(UnlockTooltipPostfix)) },
            new Spec { Name = "Drone.Harvest", Target = () => AccessTools.Method(typeof(Drone), nameof(Drone.Harvest), Type.EmptyTypes),
                Prefix = M(nameof(DroneHarvestPrefix)), Postfix = M(nameof(DroneHarvestPostfix)) },
            new Spec { Name = "Farm.CollectItems", Target = () => AccessTools.Method(typeof(Farm), nameof(Farm.CollectItems)),
                Prefix = M(nameof(CollectItemsPrefix)) },
            new Spec { Name = "Growable.GetGrowTime", Target = () => AccessTools.Method(typeof(Growable), "GetGrowTime", Type.EmptyTypes),
                Postfix = M(nameof(GetGrowTimePostfix)) },
        };

        /// <summary>Applies every patch; a failing one is logged and skipped (extensions degrade, the game keeps running).</summary>
        public static int Apply(Harmony harmony)
        {
            int ok = 0;
            foreach (var spec in All())
            {
                try
                {
                    var target = spec.Target();
                    if (target == null) throw new MissingMethodException("target not found");
                    harmony.Patch(target,
                        prefix: spec.Prefix != null ? new HarmonyMethod(spec.Prefix) { priority = spec.Priority } : null,
                        postfix: spec.Postfix != null ? new HarmonyMethod(spec.Postfix) { priority = spec.Priority } : null);
                    ok++;
                }
                catch (Exception e)
                {
                    Log.Error("extension patch " + spec.Name + " failed: " + e.Message);
                }
            }
            return ok;
        }

        static readonly HashSet<string> logged = new HashSet<string>();
        static void LogOnce(string where, Exception e)
        {
            lock (logged) if (!logged.Add(where)) return;
            Log.Error("extensions: " + where + " failed", e);
        }

        // ------------------------------------------------------------------ registration

        public static void SetItemIdsPrefix(ref IEnumerable<string> items)
        {
            try { ExtensionHost.InjectItems(ref items); }
            catch (Exception e) { LogOnce("SetItemIdsPrefix", e); }
        }

        public static void LoadAllPostfix()
        {
            try
            {
                ExtensionHost.CreateUnlocks();
                ExtensionHost.Sync();
            }
            catch (Exception e) { LogOnce("LoadAllPostfix", e); }
        }

        static int generation, sidecarGeneration = -1, farmGeneration = -1;

        public static void SaverLoadPrefix()
        {
            try
            {
                generation++;
                farmGeneration = -1;
                ExtensionHost.Sync();
            }
            catch (Exception e) { LogOnce("SaverLoadPrefix", e); }
        }

        public static void SaverLoadPostfix()
        {
            try
            {
                farmGeneration = generation;
                if (sidecarGeneration != generation)
                {
                    // the language work did not raise SaveSidecar.Loaded during this load: use what Sections holds
                    ExtensionHost.LoadStates(SaveSidecar.Sections);
                }
                RestoreMirror();
            }
            catch (Exception e) { LogOnce("SaverLoadPostfix", e); }
        }

        /// <summary>SaveSidecar.Loaded handler.</summary>
        public static void OnSidecarLoaded()
        {
            try
            {
                sidecarGeneration = generation;
                ExtensionHost.LoadStates(SaveSidecar.Sections);
                if (farmGeneration == generation) RestoreMirror();
            }
            catch (Exception e) { LogOnce("OnSidecarLoaded", e); }
        }

        /// <summary>SaveSidecar.Saving handler.</summary>
        public static void OnSidecarSaving()
        {
            try
            {
                var ms = MainSim.Inst;
                ExtensionHost.SaveStates(SaveSidecar.Sections, name =>
                {
                    int id = StringIds.GetItemId(name);
                    return id < 0 || ms == null ? 0 : ms.GetNumItem(id);
                });
            }
            catch (Exception e) { LogOnce("OnSidecarSaving", e); }
        }

        static readonly FieldInfo mainSimField = AccessTools.Field(typeof(MainSim), "sim");

        public static Simulation MainSimulation()
        {
            var ms = MainSim.Inst;
            return ms == null ? null : mainSimField?.GetValue(ms) as Simulation;
        }

        static void RestoreMirror()
        {
            var farm = MainSimulation()?.farm;
            if (farm == null) return;
            int n = ExtensionHost.RestoreItemMirror(farm.Items);
            if (n > 0) Log.Info("extensions: restored " + n + " item count(s) from javafarmer.json");
        }

        // ------------------------------------------------------------------ builtins

        /// <summary>Custom side effects (SideEffect)1000+k: runs the pending extension call (lockSimulation is held).</summary>
        public static bool ApplySideEffectPrefix(Execution __instance, int droneId, ref double __result)
        {
            var states = __instance?.States;
            if (states == null || droneId < 0 || droneId >= states.Count) return true;
            var st = states[droneId];
            if (st == null || (int)st.currentSideEffect < ExtensionHost.SideEffectBase) return true;
            if (!(st.currentSideEffectArgument2 is ExtensionHost.PendingCall pending))
            {
                st.ReturnValue = PyNone.Instance;
                __result = 0;
                return false;
            }
            double ticks = ExtensionHost.Run(pending, __instance.sim, droneId, out var value);   // ExecuteException propagates
            st.ReturnValue = value;
            st.OpCount += ticks;
            __result = ticks;
            return false;
        }

        // ------------------------------------------------------------------ items

        static readonly FieldInfo itemImageField = AccessTools.Field(typeof(ItemUI), "image");

        public static void ItemUISetupPostfix(ItemUI __instance, int itemId)
        {
            try
            {
                var item = ResourceManager.GetItem(itemId);
                if (!ExtensionHost.IsExtensionItem(item)) return;
                var sprite = ExtensionContent.Icon(item.itemName);
                var image = itemImageField?.GetValue(__instance) as UnityEngine.UI.Image;
                if (sprite != null && image != null) image.sprite = sprite;
            }
            catch (Exception e) { LogOnce("ItemUISetupPostfix", e); }
        }

        /// <summary>Items of disabled extensions stay in the save but are not shown.</summary>
        public static bool InventoryAddItemPrefix(int itemId)
        {
            try
            {
                var item = ResourceManager.GetItem(itemId);
                if (!ExtensionHost.IsExtensionItem(item)) return true;
                return ExtensionHost.Enabled(ExtensionHost.OwnerOfItem(item.itemName));
            }
            catch (Exception e) { LogOnce("InventoryAddItemPrefix", e); return true; }
        }

        // ------------------------------------------------------------------ docs and strings

        public static void LoadDocPostfix(string doc, ref string __result)
        {
            try
            {
                if (doc == null) return;
                var owner = ExtensionHost.OwnerOfDoc(doc);
                if (owner != null)
                {
                    if (!ExtensionHost.Enabled(owner)) { __result = ""; return; }
                    if (string.IsNullOrEmpty(__result)) __result = ExtensionContent.Doc(doc, ExtensionContent.LanguageId) ?? "";
                    return;
                }
                if (doc.Replace('\\', '/').TrimStart('/') == "docs/home.md" && __result != null && !__result.Contains("docs/mods/"))
                    __result += ExtensionContent.HomeSection();
            }
            catch (Exception e) { LogOnce("LoadDocPostfix", e); }
        }

        public static void LoadLangPostfix()
        {
            try { ExtensionContent.MergeStrings(); }
            catch (Exception e) { LogOnce("LoadLangPostfix", e); }
        }

        /// <summary>Capstones: the tooltip shows the current and the next bonus.</summary>
        public static void UnlockTooltipPostfix(string unlockName, TooltipInfo __result)
        {
            try
            {
                if (__result == null || unlockName == null) return;
                var ext = ExtensionHost.OwnerOfUnlock(unlockName);
                var def = ext?.Unlock(unlockName);
                if (def == null || def.BonusPerLevel <= 0) return;
                int level = MainSim.Inst != null ? MainSim.Inst.NumUnlocked(unlockName) : 0;
                string line = BonusLine(def, level);
                string descr = Localizer.Localize(def.DescriptionKey);
                int at = descr == null ? -1 : __result.text.IndexOf(descr, StringComparison.Ordinal);
                __result.text = at < 0 ? __result.text + "\n" + line : __result.text.Insert(at + descr.Length, "\n\n" + line);
            }
            catch (Exception e) { LogOnce("UnlockTooltipPostfix", e); }
        }

        public static string BonusLine(UnlockDef def, int level)
        {
            string cur = Math.Round(def.BonusPerLevel * level * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string next = Math.Round(def.BonusPerLevel * Math.Min(level + 1, def.MaxLevel) * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string fmt = null;
            try { fmt = Localizer.Localize(def.BonusKey); } catch { }
            if (string.IsNullOrEmpty(fmt) || fmt == def.BonusKey) fmt = "Bonus: +{0}%";
            string line = string.Format(fmt, cur);
            if (level < def.MaxLevel) line += " => +" + next + "%";
            return line;
        }

        // ------------------------------------------------------------------ capstone bonuses

        [ThreadStatic] static int harvestDepth;
        static readonly ConditionalWeakTable<Farm, double[]> carry = new ConditionalWeakTable<Farm, double[]>();

        public static void DroneHarvestPrefix() { harvestDepth++; }
        public static void DroneHarvestPostfix() { if (harvestDepth > 0) harvestDepth--; }

        /// <summary>Yield bonus of the Cybersecurity capstone (secure_farm): harvests give +5 % per level (fractions carry over).</summary>
        public static void CollectItemsPrefix(Farm __instance, ref ItemBlock items)
        {
            try
            {
                if (harvestDepth <= 0 || items?.items == null || __instance == null) return;
                double bonus = Bonus(__instance, Cyber.CyberExtension.ExtId, "secure_farm");
                if (bonus <= 0) return;
                items = ScaleYield(__instance, items, 1 + bonus);
            }
            catch (Exception e) { LogOnce("CollectItemsPrefix", e); }
        }

        public static double Bonus(Farm farm, string extId, string unlock)
        {
            var ext = ExtensionHost.Get(extId);
            if (!ExtensionHost.Enabled(ext)) return 0;
            var def = ext.Unlock(unlock);
            int level = Math.Min(farm.NumUnlocked(unlock), def.MaxLevel);
            return level <= 0 ? 0 : def.BonusPerLevel * level;
        }

        /// <summary>items * factor, whole numbers only: the fractional part is kept per farm and paid out later.</summary>
        public static ItemBlock ScaleYield(Farm farm, ItemBlock items, double factor)
        {
            var acc = carry.GetValue(farm, _ => new double[items.items.Length]);
            var result = new ItemBlock(items);
            for (int i = 0; i < result.items.Length && i < acc.Length; i++)
            {
                double v = result.items[i];
                if (v <= 0) continue;
                double exact = v * factor + acc[i];
                double whole = Math.Floor(exact + 1e-9);
                acc[i] = exact - whole;
                result.items[i] = whole;
            }
            return result;
        }

        static readonly FieldInfo objectSimField = AccessTools.Field(typeof(FarmObject), "sim");

        /// <summary>Growth bonus of the AI / ML capstone (smart_farm): plants grow 5 % faster per level.</summary>
        public static void GetGrowTimePostfix(Growable __instance, ref Duration __result)
        {
            try
            {
                var farm = (objectSimField?.GetValue(__instance) as Simulation)?.farm;
                if (farm == null) return;
                double bonus = Bonus(farm, AiMl.AiExtension.ExtId, "smart_farm");
                if (bonus > 0) __result = __result * (1.0 / (1.0 + bonus));
            }
            catch (Exception e) { LogOnce("GetGrowTimePostfix", e); }
        }
    }
}
