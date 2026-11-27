// Content-extension model (docs/EXTENSIONS.md §2). Game-independent: the logic of every builtin runs on these
// types only, so it is unit-testable headless. The game binding lives in ExtensionHost / ExtensionPatches.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace JavaFarmer.Plugin.Extensions
{
    /// <summary>An item an extension adds (currency). Registered ALWAYS (the item count never changes); enabled follows the toggle.</summary>
    public sealed class ItemDef
    {
        /// <summary>Lowercase item name: Items.Bits = "bits".</summary>
        public string Name;
        /// <summary>Sort key among items; extension items use > 100000 so vanilla item ids stay unchanged.</summary>
        public float Priority;
        /// <summary>Docs page opened from the item tooltip.</summary>
        public string Docs;
        /// <summary>PNG file name in content/extensions/<id>/icons/.</summary>
        public string Icon;
        /// <summary>Vanilla item whose icon is used when the PNG is missing.</summary>
        public string FallbackIcon = "gold";
        public string DescriptionKey => "item_descr_" + Name;
    }

    /// <summary>One tech-tree node.</summary>
    public sealed class UnlockDef
    {
        /// <summary>Lowercase, no trailing _digits, must not start with "debug", unique among vanilla names.</summary>
        public string Name;
        /// <summary>Existing vanilla node or another node of the same extension.</summary>
        public string Parent;
        public int Order;
        /// <summary>Path of the docs page ("docs/mods/<id>/<name>.md").</summary>
        public string Docs;
        /// <summary>Canonical Python snippet drawn on the box (translated to the current language).</summary>
        public string DisplayCode;
        public int MaxLevel = 1;
        /// <summary>Cost of level 1.</summary>
        public Cost Cost = new Cost();
        /// <summary>Costs of levels 2.. (past the end: last * LevelFactor^n, like the game).</summary>
        public List<Cost> LevelCosts = new List<Cost>();
        public float LevelFactor = 2f;
        /// <summary>Names granted at level 1 (builtins, items); "2name" = granted at level 2 (game convention).</summary>
        public List<string> Grants = new List<string>();
        /// <summary>Capstone bonus per level (0.05 = +5 %) shown in the tooltip; 0 = none.</summary>
        public double BonusPerLevel;
        /// <summary>Loc key of the bonus line ("{0}" = current %, "{1}" = next %).</summary>
        public string BonusKey;
        public string TitleKey => Name;
        public string DescriptionKey => "unlock_descr_" + Name;
        public string MultiDescriptionKey => "unlock_multi_descr_" + Name;
    }

    /// <summary>Ordered {item: amount} list.</summary>
    public sealed class Cost : List<KeyValuePair<string, double>>
    {
        public Cost() { }
        /// <summary>Cost.Of("bits", 100, "gold", 5)</summary>
        public static Cost Of(params object[] pairs)
        {
            var c = new Cost();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                c.Add(new KeyValuePair<string, double>((string)pairs[i], Convert.ToDouble(pairs[i + 1], CultureInfo.InvariantCulture)));
            return c;
        }
    }

    public enum ParamKind
    {
        /// <summary>A number with an integral value (indices, digits, PINs).</summary>
        Int,
        /// <summary>Any number.</summary>
        Number,
        String,
        Bool,
    }

    /// <summary>One extension builtin. Neutral (language-independent) description + implementation.</summary>
    public sealed class BuiltinDef
    {
        /// <summary>Canonical snake_case name (Python name). Other languages get camelCase/PascalCase via GameApi.</summary>
        public string Name;
        /// <summary>Neutral GameApi signature, e.g. "i: int, d: int".</summary>
        public string Signature = "";
        /// <summary>Neutral GameApi return type: "bool", "int", "float", "string", "void".</summary>
        public string Returns = "void";
        /// <summary>One line for GameApi (compiler hover/stub).</summary>
        public string Doc = "";
        /// <summary>Unlock whose Grants contain this builtin.</summary>
        public string Unlock;
        /// <summary>Nominal tick cost (the implementation may change ExtCall.Ticks, e.g. failures).</summary>
        public double Ticks = 1;
        /// <summary>Pure: no state, no farm. Computed directly in the binding (sim thread, execution phase).</summary>
        public bool Pure;
        public List<ParamKind> Params = new List<ParamKind>();
        public Func<ExtCall, object> Impl;
        public override string ToString() => Name;
    }

    /// <summary>
    /// Error raised by builtin logic (wrong argument value, wrong order of calls). The game binding turns it into an
    /// ExecuteException, so the player sees it like any runtime error.
    /// </summary>
    public sealed class ExtError : Exception
    {
        public ExtError(string message) : base(message) { }
    }

    /// <summary>One call of an extension builtin, as seen by its implementation.</summary>
    public sealed class ExtCall
    {
        public BuiltinDef Def;
        /// <summary>Already validated: double (Int/Number), string, bool.</summary>
        public object[] Args = Array.Empty<object>();
        /// <summary>State of the extension for the simulation that runs the program (null for pure builtins).</summary>
        public ExtState State;
        /// <summary>Current level of an unlock on the farm that runs the program.</summary>
        public Func<string, int> Level = _ => 0;
        /// <summary>Adds n of an item to the farm; returns what was added.</summary>
        public Func<string, double, double> Grant = (_, n) => n;
        /// <summary>Removes up to n of an item from the farm; returns what was removed.</summary>
        public Func<string, double, double> Take = (_, n) => 0;
        /// <summary>Tick cost of this call (starts at Def.Ticks).</summary>
        public double Ticks;
        public int DroneId;

        public double Num(int i) => (double)Args[i];
        public int Int(int i) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (double)Args[i]));
        public long Long(int i) => (long)(double)Args[i];
        public string Str(int i) => (string)Args[i];
        public bool Bool(int i) => (bool)Args[i];

        public T StateAs<T>() where T : ExtState => (T)State;
        public string Fn => Def?.Name ?? "?";
        public ExtError Error(string message) => new ExtError(Fn + "(): " + message);
    }

    /// <summary>Per-save state of one extension. Serialized to SaveSidecar.Sections[id] as JSON.</summary>
    public abstract class ExtState
    {
        /// <summary>Per-save seed: every puzzle is generated from (Seed, puzzle kind, counter), so it is reproducible.</summary>
        public ulong Seed;
        public abstract Dictionary<string, object> ToJson();
        public abstract void FromJson(Dictionary<string, object> o);

        /// <summary>Deep copy (for simulate() / leaderboard runs, which must not change the real progress).</summary>
        public ExtState Clone()
        {
            var c = (ExtState)Activator.CreateInstance(GetType());
            c.FromJson(ExtJson.Parse(ExtJson.Write(ToJson())) as Dictionary<string, object>);
            return c;
        }

        protected static Dictionary<string, object> Obj(Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
    }

    /// <summary>
    /// A content extension (Cybersecurity, AI / ML). Everything the framework registers comes from these lists.
    /// </summary>
    public abstract class ContentExtension : IExtension
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        public abstract IReadOnlyList<ItemDef> Items { get; }
        public abstract IReadOnlyList<UnlockDef> Unlocks { get; }
        public abstract IReadOnlyList<BuiltinDef> Builtins { get; }
        /// <summary>Docs index page of the extension ("docs/mods/<id>/index.md").</summary>
        public string IndexDocs => "docs/mods/" + Id + "/index.md";
        /// <summary>Root unlock (the node hung under a vanilla node).</summary>
        public UnlockDef Root => Unlocks[0];

        /// <summary>A fresh state with the given seed.</summary>
        public abstract ExtState NewState(ulong seed);

        public BuiltinDef Builtin(string name)
        {
            foreach (var b in Builtins) if (b.Name == name) return b;
            return null;
        }

        public UnlockDef Unlock(string name)
        {
            foreach (var u in Unlocks) if (u.Name == name) return u;
            return null;
        }

        /// <summary>Fills BuiltinDef.Unlock from the unlocks' Grants and checks the definitions (throws on a broken table).</summary>
        protected void Link()
        {
            var names = new HashSet<string>();
            foreach (var b in Builtins)
            {
                if (!names.Add(b.Name)) throw new InvalidOperationException("duplicate builtin " + b.Name);
                if (b.Impl == null) throw new InvalidOperationException("builtin without implementation: " + b.Name);
                b.Unlock = null;
                foreach (var u in Unlocks)
                    foreach (var g in u.Grants)
                        if (g.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') == b.Name) b.Unlock = u.Name;
                if (b.Unlock == null) throw new InvalidOperationException("builtin not granted by any unlock: " + b.Name);
            }
        }

        /// <summary>Builtin from a signature like "i: int, d: int" (the parameter kinds are derived from it).</summary>
        protected static BuiltinDef Def(string name, string signature, string returns, double ticks, string doc, Func<ExtCall, object> impl, bool pure = false)
        {
            var b = new BuiltinDef { Name = name, Signature = signature ?? "", Returns = returns, Ticks = ticks, Doc = doc, Impl = impl, Pure = pure };
            foreach (var raw in b.Signature.Split(','))
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;
                int colon = p.IndexOf(':');
                string type = colon < 0 ? "number" : p.Substring(colon + 1).Trim();
                switch (type)
                {
                    case "int": b.Params.Add(ParamKind.Int); break;
                    case "float": case "number": b.Params.Add(ParamKind.Number); break;
                    case "string": b.Params.Add(ParamKind.String); break;
                    case "bool": b.Params.Add(ParamKind.Bool); break;
                    default: throw new InvalidOperationException(name + ": unsupported parameter type " + type);
                }
            }
            return b;
        }
    }
}
