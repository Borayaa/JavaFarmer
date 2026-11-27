using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// The game API as Rust sees it (one table for the checker, the printer, ApiName and the farm.rs stub):
    /// snake_case game names, except move → move_to (also r#move); a builtin with optional parameters has a second
    /// form "&lt;name&gt;_with" that takes all of them; print / quick_print are macros (println!, print!, quick_print!).
    /// Cached per GameApi.Version.
    /// </summary>
    internal static class RustApi
    {
        internal sealed class Entry
        {
            public ApiBuiltin B;
            public string RustName;      // "move_to", "use_item", "use_item_with"
            public bool WithForm;        // takes the optional parameters too
            public List<ApiParam> Params; // the parameters of this form
        }

        static int version = -1;
        static Dictionary<string, Entry> byRust = new Dictionary<string, Entry>();
        static readonly object gate = new object();

        public static readonly HashSet<string> Macros = new HashSet<string> { "print", "quick_print" };

        public static bool IsRustKeyword(string s) => RustLexer.Keywords.Contains(s);

        /// <summary>Rust name of a builtin's base form ("move" → "move_to"); null when unknown.</summary>
        public static string BaseName(string py)
        {
            if (py == null) return null;
            if (py == "move") return "move_to";
            if (IsRustKeyword(py)) return "r#" + py;
            return py;
        }

        public static bool HasOptional(ApiBuiltin b) =>
            !Macros.Contains(b.PythonName) && b.PythonName != "min" && b.PythonName != "max" && b.Params.Any(p => p.Optional && !p.Variadic);

        static void Refresh()
        {
            if (version == GameApi.Version && byRust.Count > 0) return;
            lock (gate)
            {
                if (version == GameApi.Version && byRust.Count > 0) return;
                var d = new Dictionary<string, Entry>();
                foreach (var b in GameApi.Builtins)
                {
                    if (Macros.Contains(b.PythonName)) continue;
                    var bn = BaseName(b.PythonName);
                    var plain = bn.StartsWith("r#") ? bn.Substring(2) : bn;
                    var req = b.Params.Where(p => !p.Optional || p.Variadic).ToList();
                    if (b.PythonName == "min" || b.PythonName == "max") req = b.Params.Where(p => !p.Optional && !p.Variadic).ToList();
                    d[plain] = new Entry { B = b, RustName = bn, Params = req };
                    if (HasOptional(b)) d[plain + "_with"] = new Entry { B = b, RustName = plain + "_with", WithForm = true, Params = b.Params.ToList() };
                }
                if (d.TryGetValue("move_to", out var mv)) d["move"] = mv;   // r#move
                byRust = d;
                version = GameApi.Version;
            }
        }

        /// <summary>Builtin by its Rust name (without "r#"); null if none.</summary>
        public static Entry Find(string rustName)
        {
            Refresh();
            lock (gate) return rustName != null && byRust.TryGetValue(rustName, out var e) ? e : null;
        }

        public static IEnumerable<Entry> All()
        {
            Refresh();
            lock (gate) return byRust.Where(kv => kv.Key != "move").Select(kv => kv.Value).ToList();
        }

        /// <summary>ILanguage.ApiName: "can_harvest" → "can_harvest", "move" → "move_to", "print" → "println!".</summary>
        public static string ApiName(string py)
        {
            if (py == null) return null;
            if (py == "print") return "println!";
            if (py == "quick_print") return "quick_print!";
            return GameApi.Find(py) == null ? null : BaseName(py);
        }

        public static string FromApiName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name)
            {
                case "println!": case "print!": case "println": return "print";
                case "quick_print!": return "quick_print";
            }
            if (name.EndsWith("!")) name = name.Substring(0, name.Length - 1);
            if (name.StartsWith("r#")) name = name.Substring(2);
            if (name == "quick_print") return "quick_print";
            var e = Find(name);
            return e?.B.PythonName;
        }

        /// <summary>Trait name for a union parameter type ("Entity|Item|Unlock" → Costable).</summary>
        public static string UnionTrait(string typeText)
        {
            var parts = (typeText ?? "").Replace(" ", "").Split('|').Where(x => x.Length > 0).ToList();
            var set = new HashSet<string>(parts);
            if (set.SetEquals(new[] { "Entity", "Item", "Unlock" })) return "Costable";
            if (set.SetEquals(new[] { "Unlock", "Entity", "Ground", "Item" })) return "Unlockable";
            return string.Join("Or", parts.Select(x => x.Length > 0 ? char.ToUpperInvariant(x[0]) + x.Substring(1) : x));
        }

        public static List<string> UnionMembers(string typeText) =>
            (typeText ?? "").Replace(" ", "").Split('|').Where(x => x.Length > 0).ToList();

        /// <summary>Plural constant category → Rust enum name ("Entities" → "Entity"); also "Directions".</summary>
        public static string EnumOfCategory(string category) => GameApi.GameTypeOfCategory(category);
        public static string CategoryOfEnum(string enumName) => GameApi.CategoryOfGameType(enumName);

        public static readonly string[] Enums = { "Entity", "Item", "Ground", "Unlock", "Hat", "Leaderboard", "Direction" };
    }
}
