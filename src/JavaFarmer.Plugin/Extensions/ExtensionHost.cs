// Runtime side of the content extensions: registers items / unlocks / builtins with the game and the compilers,
// keeps the per-save state, and applies the Mods-menu toggles (docs/EXTENSIONS.md §2, docs/research/game-extension-points.md).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JavaFarmer.Compiler;
using UnityEngine;

namespace JavaFarmer.Plugin.Extensions
{
    public static class ExtensionHost
    {
        /// <summary>Base of the custom side effects: (SideEffect)(SideEffectBase + extension index).</summary>
        public const int SideEffectBase = 1000;

        static readonly object gate = new object();
        static readonly List<ContentExtension> extensions = new List<ContentExtension>();
        public static IReadOnlyList<ContentExtension> Extensions => extensions;

        /// <summary>Tests replace these (ScriptableObject.CreateInstance needs the Unity runtime).</summary>
        public static Func<ItemSO> NewItemSO = () => ScriptableObject.CreateInstance<ItemSO>();
        public static Func<UnlockSO> NewUnlockSO = () => ScriptableObject.CreateInstance<UnlockSO>();
        /// <summary>Tests replace this: ExtensionRegistry.IsEnabled reads the BepInEx config.</summary>
        public static Func<string, bool> IsEnabled = id => ExtensionRegistry.IsEnabled(id);

        static readonly Dictionary<string, ItemSO> itemSOs = new Dictionary<string, ItemSO>();
        static readonly Dictionary<string, UnlockSO> unlockSOs = new Dictionary<string, UnlockSO>();
        static readonly Dictionary<string, PyFunction> functions = new Dictionary<string, PyFunction>();
        static readonly Dictionary<string, ExtState> mainStates = new Dictionary<string, ExtState>();
        static ConditionalWeakTable<Simulation, Dictionary<string, ExtState>> simStates = new ConditionalWeakTable<Simulation, Dictionary<string, ExtState>>();
        static readonly Dictionary<string, double> itemMirror = new Dictionary<string, double>();
        static readonly HashSet<string> registeredApi = new HashSet<string>();

        public static void Register(ContentExtension ext)
        {
            lock (gate)
            {
                if (ext == null || extensions.Any(e => e.Id == ext.Id)) return;
                extensions.Add(ext);
            }
            ExtensionRegistry.Register(ext);
        }

        public static ContentExtension Get(string id) => extensions.FirstOrDefault(e => e.Id == id);
        public static int IndexOf(ContentExtension ext) => extensions.IndexOf(ext);

        public static ContentExtension OwnerOfItem(string itemName) => extensions.FirstOrDefault(e => e.Items.Any(i => i.Name == itemName));
        public static ContentExtension OwnerOfUnlock(string unlockName) => extensions.FirstOrDefault(e => e.Unlocks.Any(u => u.Name == unlockName));
        public static ContentExtension OwnerOfDoc(string doc)
        {
            if (doc == null) return null;
            doc = doc.Replace('\\', '/').TrimStart('/');
            return extensions.FirstOrDefault(e => doc.StartsWith("docs/mods/" + e.Id + "/", StringComparison.Ordinal));
        }

        public static bool Enabled(ContentExtension ext) => ext != null && IsEnabled(ext.Id);

        // ================================================================== items (StringIds.SetItemIds prefix)

        public static IReadOnlyDictionary<string, ItemSO> ItemSOs => itemSOs;
        public static bool IsExtensionItem(ItemSO item) => !ReferenceEquals(item, null) && item.itemName != null && itemSOs.TryGetValue(item.itemName, out var so) && ReferenceEquals(so, item);

        /// <summary>Private static game fields the host writes (checked by the reflection tests).</summary>
        public static readonly System.Reflection.FieldInfo ItemsField = AccessTools.Field(typeof(ResourceManager), "items");
        public static readonly System.Reflection.FieldInfo UnlocksField = AccessTools.Field(typeof(ResourceManager), "unlocks");
        public static readonly System.Reflection.FieldInfo KeywordDocsField = AccessTools.Field(typeof(TooltipUtils), "keywordDocs");

        static ItemSO[] GameItems { get => ItemsField?.GetValue(null) as ItemSO[]; set => ItemsField?.SetValue(null, value); }
        public static Dictionary<string, UnlockSO> GameUnlocks => UnlocksField?.GetValue(null) as Dictionary<string, UnlockSO>;

        /// <summary>
        /// Adds every extension item (also of disabled extensions: the item count must never change) to
        /// ResourceManager.items and to the names StringIds.SetItemIds is about to number.
        /// </summary>
        public static void InjectItems(ref IEnumerable<string> names)
        {
            var current = GameItems;
            if (current == null) return;
            var list = current.ToList();
            bool added = false;
            foreach (var ext in extensions)
                foreach (var def in ext.Items)
                {
                    if (list.Any(i => !ReferenceEquals(i, null) && i.itemName == def.Name)) continue;
                    if (!itemSOs.TryGetValue(def.Name, out var so))
                    {
                        so = NewItemSO();
                        so.itemName = def.Name;
                        TrySetName(so, def.Name);
                        so.description = def.DescriptionKey;
                        so.docs = def.Docs;
                        so.mesh = null;
                        so.trackStats = false;   // no Steam stats for extension items
                        so.priority = def.Priority;
                        so.enabled = Enabled(ext);
                        itemSOs[def.Name] = so;
                    }
                    list.Add(so);
                    added = true;
                }
            if (!added) return;
            var sorted = list.OrderBy(i => i.priority).ToArray();
            for (int i = 0; i < sorted.Length; i++) sorted[i].itemId = i;
            GameItems = sorted;
            names = sorted.Select(i => i.itemName).ToList();
        }

        static void TrySetName(UnityEngine.Object o, string name)
        {
            try { o.name = name; } catch { /* headless tests */ }
        }

        // ================================================================== unlocks (ResourceManager.LoadAll postfix)

        public static IReadOnlyDictionary<string, UnlockSO> UnlockSOs => unlockSOs;

        /// <summary>Creates the UnlockSOs (once; item ids exist now) and applies the toggles.</summary>
        public static void CreateUnlocks()
        {
            foreach (var ext in extensions)
                foreach (var def in ext.Unlocks)
                {
                    if (unlockSOs.ContainsKey(def.Name)) continue;
                    var so = NewUnlockSO();
                    so.unlockName = def.Name;
                    TrySetName(so, def.Name);
                    so.description = def.DescriptionKey;
                    so.docs = def.Docs;
                    so.mesh = null;
                    so.displayCode = def.DisplayCode;
                    so.unlocks = new List<string>(def.Grants);
                    so.parentUnlock = def.Parent;
                    so.unlockedHat = "";
                    so.order = def.Order;
                    so.unlockCost = Block(def.Cost);
                    so.multiUnlockCost = def.LevelCosts.Select(Block).ToList();
                    so.multiUnlockFactor = def.LevelFactor;
                    so.multiUnlockDescr = def.DescriptionKey;
                    so.multiUnlockDescrMode = MultiUnlockDescrMode.None;
                    so.additivePercentStart = 0;
                    so.additivePercentFactor = 2;
                    so.maxUnlockLevel = def.MaxLevel;
                    so.enabled = true;
                    unlockSOs[def.Name] = so;
                }
        }

        public static ItemBlock Block(Cost cost)
        {
            var b = ItemBlock.CreateEmpty();
            foreach (var kv in cost)
            {
                int id = StringIds.GetItemId(kv.Key);
                if (id < 0) { Log.Warn("extension cost: unknown item " + kv.Key); continue; }
                b.items[id] += kv.Value;
            }
            return b;
        }

        // ================================================================== builtins

        public static IReadOnlyDictionary<string, PyFunction> Functions => functions;

        public static PyFunction FunctionFor(ContentExtension ext, BuiltinDef def)
        {
            if (!functions.TryGetValue(def.Name, out var f))
            {
                f = new PyFunction(def.Name, (p, sim, exec, drone) => Bind(ext, def, p, sim, exec, drone));
                functions[def.Name] = f;
            }
            return f;
        }

        /// <summary>The game binding of every extension builtin (runs on the sim thread, execution phase).</summary>
        public static double Bind(ContentExtension ext, BuiltinDef def, List<IPyObject> parameters, Simulation sim, Execution exec, int droneId)
        {
            var args = ExtArgs.Convert(def, parameters);
            var st = exec.States[droneId];
            if (def.Pure)
            {
                var call = new ExtCall { Def = def, Args = args, Ticks = def.Ticks, DroneId = droneId };
                object result;
                try { result = def.Impl(call); }
                catch (ExtError e) { throw new ExecuteException(e.Message); }
                st.ReturnValue = ExtArgs.ToPy(result);
                return call.Ticks;
            }
            st.currentSideEffect = (SideEffect)(SideEffectBase + Math.Max(0, IndexOf(ext)));
            st.currentSideEffectArgument = null;
            st.currentSideEffectArgument2 = new PendingCall { Ext = ext, Def = def, Args = args };
            return 0.0;
        }

        /// <summary>A side-effect builtin waiting for Execution.ApplySideEffect.</summary>
        public sealed class PendingCall
        {
            public ContentExtension Ext;
            public BuiltinDef Def;
            public object[] Args;
        }

        /// <summary>Runs a pending call in the side-effect phase (lockSimulation held). Returns the tick cost.</summary>
        public static double Run(PendingCall pending, Simulation sim, int droneId, out IPyObject value)
        {
            var state = StateFor(pending.Ext, sim);
            var farm = sim?.farm;
            var call = new ExtCall
            {
                Def = pending.Def, Args = pending.Args, State = state, Ticks = pending.Def.Ticks, DroneId = droneId,
                Level = name => farm == null ? 0 : farm.NumUnlocked(name),
                Grant = (item, n) => GrantItem(sim, droneId, item, n),
                Take = (item, n) => TakeItem(farm, item, n),
            };
            object result;
            try
            {
                lock (state) result = pending.Def.Impl(call);
            }
            catch (ExtError e) { throw new ExecuteException(e.Message); }
            catch (ExecuteException) { throw; }
            catch (Exception e)
            {
                Log.Error("extension builtin " + pending.Def.Name + " failed", e);
                throw new ExecuteException(pending.Def.Name + "(): internal error (" + e.GetType().Name + ")");
            }
            value = ExtArgs.ToPy(result);
            return call.Ticks;
        }

        public static double GrantItem(Simulation sim, int droneId, string item, double n)
        {
            var farm = sim?.farm;
            int id = StringIds.GetItemId(item);
            if (farm == null || id < 0 || n <= 0) return 0;
            if (ReferenceEquals(sim.mainSim, null))
            {
                farm.Items.items[id] += n;
                return n;
            }
            Vector3 pos = Vector3.zero;
            try { pos = farm.drones[droneId].GetTransform().GetPosition(); } catch { }
            farm.CollectItem(id, n, pos);
            return n;
        }

        public static double TakeItem(Farm farm, string item, double n)
        {
            int id = StringIds.GetItemId(item);
            if (farm == null || id < 0 || n <= 0) return 0;
            double have = farm.Items.items[id];
            double take = Math.Min(have, n);
            farm.Items.items[id] = have - take;
            return take;
        }

        // ================================================================== toggles

        /// <summary>
        /// Makes the game match the toggles: extension items enabled/disabled, unlocks in/out of ResourceManager.unlocks,
        /// builtins in/out of BuiltinFunctions.Functions and GameApi. Call when no program runs (scene load).
        /// </summary>
        public static void Sync()
        {
            string lang = ExtensionContent.LanguageId;
            var dict = GameUnlocks;
            foreach (var ext in extensions)
            {
                bool on = Enabled(ext);
                foreach (var def in ext.Items)
                    if (itemSOs.TryGetValue(def.Name, out var so)) so.enabled = on;
                foreach (var def in ext.Unlocks)
                {
                    if (!unlockSOs.TryGetValue(def.Name, out var so) || dict == null) continue;
                    if (on)
                    {
                        dict[def.Name] = so;
                        so.displayCode = ExtensionContent.DisplayCode(def.DisplayCode, lang);
                    }
                    else if (dict.TryGetValue(def.Name, out var cur) && ReferenceEquals(cur, so)) dict.Remove(def.Name);
                }
                SyncBuiltins(ext, on);
            }
            KeywordDocsField?.SetValue(null, null);
            ExtensionContent.MergeStrings();
        }

        public static void SyncBuiltins(ContentExtension ext, bool on)
        {
            Dictionary<string, PyFunction> game = null;
            try { game = BuiltinFunctions.Functions; } catch (Exception e) { Log.Warn("BuiltinFunctions not available: " + e.Message); }
            foreach (var def in ext.Builtins)
            {
                if (game != null)
                {
                    if (on) game[def.Name] = FunctionFor(ext, def);
                    else if (game.TryGetValue(def.Name, out var f) && functions.TryGetValue(def.Name, out var mine) && ReferenceEquals(f, mine)) game.Remove(def.Name);
                }
                if (on)
                {
                    var existing = GameApi.Find(def.Name);
                    if (existing == null || existing.Extension == ext.Id)
                    {
                        if (!registeredApi.Contains(def.Name) || existing == null)
                            GameApi.Register(def.Name, def.Signature, def.Returns, def.Doc, ext.Id);
                        registeredApi.Add(def.Name);
                    }
                    else Log.Warn("extension builtin " + def.Name + " clashes with " + existing.Signature);
                }
                else if (registeredApi.Remove(def.Name)) GameApi.Unregister(def.Name);
            }
        }

        /// <summary>Registers the builtins of every extension with GameApi (tests and docs generation).</summary>
        public static void RegisterApi(ContentExtension ext)
        {
            foreach (var def in ext.Builtins)
            {
                var existing = GameApi.Find(def.Name);
                if (existing == null) GameApi.Register(def.Name, def.Signature, def.Returns, def.Doc, ext.Id);
            }
        }

        // ================================================================== per-save state

        public static bool IsMainSim(Simulation sim) => sim == null || sim.leaderboardType == LeaderboardType.none;

        /// <summary>The state a program running in sim sees: the save's state, or a copy for simulate() / leaderboard runs.</summary>
        public static ExtState StateFor(ContentExtension ext, Simulation sim)
        {
            ExtState main;
            lock (gate)
            {
                if (!mainStates.TryGetValue(ext.Id, out main))
                {
                    main = ext.NewState(ExtRandom.NewSeed());
                    mainStates[ext.Id] = main;
                }
                if (IsMainSim(sim)) return main;
                var table = simStates.GetValue(sim, _ => new Dictionary<string, ExtState>());
                if (table.TryGetValue(ext.Id, out var copy)) return copy;
                lock (main) copy = main.Clone();
                table[ext.Id] = copy;
                return copy;
            }
        }

        /// <summary>Replaces the states with the ones in SaveSidecar.Sections (a save without a section gets a fresh seed).</summary>
        public static void LoadStates(IDictionary<string, string> sections)
        {
            lock (gate)
            {
                mainStates.Clear();
                itemMirror.Clear();
                simStates = new ConditionalWeakTable<Simulation, Dictionary<string, ExtState>>();
                foreach (var ext in extensions)
                {
                    string json = null;
                    if (sections != null) sections.TryGetValue(ext.Id, out json);
                    var o = string.IsNullOrEmpty(json) ? null : ExtJson.TryParseObject(json);
                    if (o == null)
                    {
                        if (!string.IsNullOrEmpty(json)) Log.Warn("extension " + ext.Id + ": unreadable state in javafarmer.json, starting fresh");
                        continue;   // created lazily with a new seed
                    }
                    var st = ext.NewState(ExtRandom.NewSeed());
                    try { st.FromJson(ExtJson.Obj(o, "state") ?? o); }
                    catch (Exception e) { Log.Warn("extension " + ext.Id + ": bad state (" + e.Message + "), starting fresh"); st = ext.NewState(ExtRandom.NewSeed()); }
                    mainStates[ext.Id] = st;
                    var items = ExtJson.Obj(o, "items");
                    if (items != null) foreach (var kv in items) if (kv.Value is double d) itemMirror[kv.Key] = d;
                }
            }
        }

        /// <summary>Writes every state (+ a mirror of the extension item counts) into SaveSidecar.Sections.</summary>
        public static void SaveStates(IDictionary<string, string> sections, Func<string, double> itemCount)
        {
            if (sections == null) return;
            foreach (var ext in extensions)
            {
                ExtState st;
                lock (gate) mainStates.TryGetValue(ext.Id, out st);
                var items = new Dictionary<string, object>();
                foreach (var def in ext.Items)
                {
                    double n = itemCount?.Invoke(def.Name) ?? 0;
                    if (n > 0) items[def.Name] = n;
                }
                if (st == null && items.Count == 0) continue;
                var o = new Dictionary<string, object> { ["format"] = 1.0, ["items"] = items };
                if (st != null) lock (st) o["state"] = st.ToJson();
                sections[ext.Id] = ExtJson.Write(o);
            }
        }

        /// <summary>
        /// Restores extension item counts from the sidecar mirror when the save lost them (the game drops unknown item
        /// names, e.g. after a session without the plugin). Only fills items the farm has none of.
        /// </summary>
        public static int RestoreItemMirror(ItemBlock farmItems)
        {
            if (farmItems?.items == null) return 0;
            int n = 0;
            lock (gate)
            {
                foreach (var kv in itemMirror)
                {
                    int id = StringIds.GetItemId(kv.Key);
                    if (id < 0 || id >= farmItems.items.Length || farmItems.items[id] > 0 || kv.Value <= 0) continue;
                    farmItems.items[id] = kv.Value;
                    n++;
                }
                itemMirror.Clear();
            }
            return n;
        }

        /// <summary>Test hook: drops all state and registrations.</summary>
        public static void ResetForTests()
        {
            lock (gate)
            {
                mainStates.Clear();
                itemMirror.Clear();
                simStates = new ConditionalWeakTable<Simulation, Dictionary<string, ExtState>>();
            }
        }

        public static void SetMainState(ContentExtension ext, ExtState st)
        {
            lock (gate) mainStates[ext.Id] = st;
        }
    }
}
