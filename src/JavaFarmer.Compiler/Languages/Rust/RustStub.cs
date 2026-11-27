using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// farm.rs: the game API as a Rust library crate (compile with rustc --crate-type lib --crate-name farm). Generated from
    /// GameApi.Builtins so registered extension builtins appear. The constant enums list the game's known members (data
    /// for editors only; the compiler accepts any member) plus the extra members a caller passes.
    /// </summary>
    internal static class RustStub
    {
        /// <summary>Known members of the game's constant categories (for the players' stub only; never used for checking).</summary>
        static readonly Dictionary<string, string[]> Known = new Dictionary<string, string[]>
        {
            ["Entities"] = new[] { "Apple", "Grass", "Bush", "Tree", "Carrot", "Pumpkin", "Dead_Pumpkin", "Sunflower", "Cactus", "Hedge", "Treasure", "Dinosaur" },
            ["Items"] = new[] { "Carrot", "Fertilizer", "Gold", "Hay", "Power", "Pumpkin", "Water", "Weird_Substance", "Wood", "Cactus", "Bone" },
            ["Grounds"] = new[] { "Grassland", "Soil" },
            ["Unlocks"] = new[] { "Trees", "Speed", "Plant", "Loops", "Senses", "Expand", "Operators", "Pumpkins", "Variables", "Functions", "Imports",
                "Watering", "Carrots", "Lists", "Costs", "Fertilizer", "Mazes", "Debug", "Debug_2", "Timing", "Grass", "Multi_Trade", "Auto_Unlock",
                "Polyculture", "Sunflowers", "Leaderboard", "Dictionaries", "Utilities", "Cactus", "Dinosaurs", "Megafarm", "Hats" },
            ["Hats"] = new[] { "Brown_Hat", "Cactus_Hat", "Carrot_Hat", "Dinosaur_Hat", "Gold_Hat", "Gold_Trophy_Hat", "Golden_Cactus_Hat", "Golden_Carrot_Hat",
                "Golden_Gold_Hat", "Golden_Pumpkin_Hat", "Golden_Sunflower_Hat", "Golden_Tree_Hat", "Gray_Hat", "Green_Hat", "Pumpkin_Hat", "Purple_Hat",
                "Silver_Trophy_Hat", "Straw_Hat", "Sunflower_Hat", "The_Farmers_Remains", "Top_Hat", "Traffic_Cone", "Traffic_Cone_Stack", "Tree_Hat",
                "Wizard_Hat", "Wood_Trophy_Hat" },
            ["Leaderboards"] = new[] { "Cactus", "Cactus_Single", "Carrots", "Carrots_Single", "Dinosaur", "Fastest_Reset", "Hay", "Hay_Single", "Maze",
                "Maze_Single", "Pumpkins", "Pumpkins_Single", "Sunflowers", "Sunflowers_Single", "Wood", "Wood_Single" },
        };

        static readonly string[] Signed = { "i8", "i16", "i32", "i64", "i128", "isize", "f32", "f64" };

        public static string Build(Func<string, IEnumerable<string>> extraMembers)
        {
            var sb = new StringBuilder();
            sb.Append("//! farm: the game API of The Farmer Was Replaced for JavaFarmer Rust, written by the JavaFarmer mod.\n");
            sb.Append("//! The game never runs this file. It lets Rust editors and rustc know the game's functions and constants.\n");
            sb.Append("//! Build it as a library crate:  rustc --edition 2024 --crate-type lib --crate-name farm farm.rs\n");
            sb.Append("//! and use it from a program:    rustc --edition 2024 --extern farm=libfarm.rlib main.rs   (with `use farm::*;`)\n");
            sb.Append("//! Names are the game's names (snake_case); `move` is `move_to` (or `r#move`); a function with optional\n");
            sb.Append("//! parameters has a second form `<name>_with` that takes them all. print is `println!`, quick_print is `quick_print!`.\n");
            sb.Append("#![allow(non_camel_case_types, dead_code, unused_variables, unused_imports, clippy::all)]\n\n");
            sb.Append("use std::collections::{HashMap, HashSet};\nuse std::fmt;\nuse std::marker::PhantomData;\n\n");
            foreach (var cat in GameApi.ConstantCategories)
            {
                var en = GameApi.GameTypeOfCategory(cat);
                var members = new List<string>();
                if (Known.TryGetValue(cat, out var k)) members.AddRange(k);
                if (extraMembers != null) foreach (var m in extraMembers(cat) ?? Enumerable.Empty<string>()) if (!members.Contains(m)) members.Add(m);
                Enum(sb, en, "The game's " + cat + " (" + en + "::" + (members.FirstOrDefault() ?? "X") + "). Members are game data.", members);
            }
            Enum(sb, "Direction", "A direction to move in. `use farm::*;` also brings North, East, South and West into scope.", GameApi.DirectionNames.ToList());
            sb.Append("pub use Direction::*;\n\n");
            sb.Append("/// A position on the farm: (x, y).\npub type Position = (i32, i32);\n");
            sb.Append("/// What a plant wants as its companion: (entity, (x, y)).\npub type Companion = (Entity, Position);\n\n");
            sb.Append("/// A handle of a spawned drone; T is what its task returns.\npub struct Drone<T> {\n    id: u32,\n    result: PhantomData<fn() -> T>,\n}\n");
            sb.Append("impl<T> Clone for Drone<T> {\n    fn clone(&self) -> Self {\n        *self\n    }\n}\nimpl<T> Copy for Drone<T> {}\n");
            sb.Append("impl<T> PartialEq for Drone<T> {\n    fn eq(&self, other: &Self) -> bool {\n        self.id == other.id\n    }\n}\n");
            sb.Append("impl<T> fmt::Debug for Drone<T> {\n    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {\n        write!(f, \"Drone({})\", self.id)\n    }\n}\n\n");
            // traits for union parameters and generic results
            var unions = new Dictionary<string, List<string>>();
            foreach (var b in GameApi.Builtins)
                foreach (var p in b.Params)
                    if ((p.TypeText ?? "").Contains("|"))
                    {
                        var ms = RustApi.UnionMembers(p.TypeText).Where(x => x != "null" && x != "None").ToList();
                        if (ms.Count > 1) unions[RustApi.UnionTrait(string.Join("|", ms))] = ms;
                    }
            foreach (var kv in unions)
            {
                sb.Append("/// Accepted by parameters that take a ").Append(string.Join(", ", kv.Value)).Append(".\npub trait ").Append(kv.Key).Append(" {}\n");
                foreach (var m in kv.Value) sb.Append("impl ").Append(kv.Key).Append(" for ").Append(Rt(m, false, false)).Append(" {}\n");
                sb.Append('\n');
            }
            sb.Append("/// What measure() can give: f64 (most plants), i32, Position (mazes, dinosaurs) or an Option of them.\npub trait Measurement {}\n");
            foreach (var t in new[] { "f64", "i32", "Position", "Option<f64>", "Option<i32>", "Option<Position>" }) sb.Append("impl Measurement for ").Append(t).Append(" {}\n");
            sb.Append("\n/// Numbers that have an absolute value.\npub trait Number: Copy + PartialOrd {\n    fn abs_value(self) -> Self;\n}\n");
            foreach (var t in Signed) sb.Append("impl Number for ").Append(t).Append(" {\n    fn abs_value(self) -> Self {\n        self.abs()\n    }\n}\n");
            sb.Append("\n/// Python-style list helpers (JavaFarmer): remove the first x (error when missing), index of x (-1 when missing).\n");
            sb.Append("pub trait FarmVec<T> {\n    fn remove_value(&mut self, x: &T);\n    fn index_of(&self, x: &T) -> i32;\n}\n");
            sb.Append("impl<T: PartialEq> FarmVec<T> for Vec<T> {\n    fn remove_value(&mut self, x: &T) {\n        let i = self.iter().position(|y| y == x).expect(\"remove_value: value not in list\");\n        self.remove(i);\n    }\n");
            sb.Append("    fn index_of(&self, x: &T) -> i32 {\n        match self.iter().position(|y| y == x) {\n            Some(i) => i as i32,\n            None => -1,\n        }\n    }\n}\n\n");
            sb.Append("/// Prints to the output page only (no delay). Same format string as println!.\n#[macro_export]\nmacro_rules! quick_print {\n    () => {\n        println!()\n    };\n    ($($arg:tt)*) => {\n        println!($($arg)*)\n    };\n}\n\n");
            foreach (var b in GameApi.Builtins)
            {
                if (RustApi.Macros.Contains(b.PythonName)) continue;
                Function(sb, b, false);
                if (RustApi.HasOptional(b)) Function(sb, b, true);
            }
            return sb.ToString();
        }

        static void Enum(StringBuilder sb, string name, string doc, List<string> members)
        {
            sb.Append("/// ").Append(doc).Append("\n#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]\npub enum ").Append(name).Append(" {\n");
            foreach (var m in members) sb.Append("    ").Append(RustApi.IsRustKeyword(m) ? "r#" + m : m).Append(",\n");
            sb.Append("}\n");
            sb.Append("impl fmt::Display for ").Append(name).Append(" {\n    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {\n        fmt::Debug::fmt(self, f)\n    }\n}\n\n");
        }

        /// <summary>Neutral type text → Rust type text (generic parameters collected in gens).</summary>
        static string Rt(string text, bool param, bool inner, List<string> gens = null, string hint = "A")
        {
            text = (text ?? "any").Replace(" ", "");
            if (text.Contains("|"))
            {
                var ms = RustApi.UnionMembers(text).Where(x => x != "null" && x != "None" && x != "void").ToList();
                bool nul = ms.Count < RustApi.UnionMembers(text).Count;
                if (ms.Count == 1) return nul ? "Option<" + Rt(ms[0], param, inner, gens, hint) + ">" : Rt(ms[0], param, inner, gens, hint);
                var g = Fresh(gens, "T");
                gens?.Add(g + ": " + RustApi.UnionTrait(string.Join("|", ms)));
                return g;
            }
            if (text.EndsWith("?")) return "Option<" + Rt(text.Substring(0, text.Length - 1), param, inner, gens, hint) + ">";
            int lt = text.IndexOf('<');
            if (lt > 0 && text.EndsWith(">"))
            {
                var head = text.Substring(0, lt);
                var args = Split(text.Substring(lt + 1, text.Length - lt - 2));
                string A(int i) => i < args.Count ? Rt(args[i], param, true, gens, hint) : "()";
                switch (head)
                {
                    case "list": case "array": return param && !inner ? "&[" + A(0) + "]" : "Vec<" + A(0) + ">";
                    case "set": return "HashSet<" + A(0) + ">";
                    case "map": case "dict": return "HashMap<" + A(0) + ", " + A(1) + ">";
                    case "tuple": return "(" + string.Join(", ", args.Select(a => Rt(a, param, true, gens, hint))) + ")";
                }
            }
            switch (text)
            {
                case "int": return "i32";
                case "float": case "double": case "number": return "f64";
                case "bool": return "bool";
                case "string": case "str": return param ? "&str" : "String";
                case "void": case "None": return "()";
                case "Position": return "Position";
                case "Companion": return "Companion";
                case "Drone": { var g = Fresh(gens, "R"); gens?.Add(g); return "Drone<" + g + ">"; }
                case "any":
                    {
                        if (gens == null) return "()";
                        var g = Fresh(gens, hint);
                        gens.Add(g);
                        return g;
                    }
            }
            if (Array.IndexOf(RustApi.Enums, text) >= 0) return text;
            if (gens == null) return "()";
            var gg = Fresh(gens, hint);
            gens.Add(gg);
            return gg;
        }

        static string Fresh(List<string> gens, string baseName)
        {
            if (gens == null) return baseName;
            for (int i = 0; ; i++)
            {
                var n = i == 0 ? baseName : baseName + i;
                if (!gens.Any(g => g == n || g.StartsWith(n + ":"))) return n;
            }
        }

        static List<string> Split(string s)
        {
            var res = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<') depth++;
                else if (s[i] == '>') depth--;
                else if (s[i] == ',' && depth == 0) { res.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            res.Add(s.Substring(start));
            return res;
        }

        static string ParamName(string n) => RustApi.IsRustKeyword(n) ? n + "_" : n;

        static void Function(StringBuilder sb, ApiBuiltin b, bool withForm)
        {
            var py = b.PythonName;
            var name = withForm ? RustApi.BaseName(py).Replace("r#", "") + "_with" : RustApi.BaseName(py);
            var doc = (b.Doc ?? "").Replace("\n", " ");
            sb.Append("/// ").Append(doc).Append("\n/// Game: `").Append(b.Signature).Append('`');
            if (b.Extension != null) sb.Append(" (extension: ").Append(b.Extension).Append(')');
            sb.Append('\n');
            string Line(string sig) => "pub fn " + sig + " {\n    unimplemented!(\"game builtin\")\n}\n";
            switch (py)
            {
                case "spawn_drone":
                    sb.Append(Line("spawn_drone<T: Send + 'static, F: FnOnce() -> T + Send + 'static>(task: F) -> Option<Drone<T>>"));
                    if (py == "move") sb.Append("pub use move_to as r#move;\n");
                    sb.Append('\n');
                    return;
                case "wait_for": sb.Append(Line("wait_for<T>(drone: Drone<T>) -> T")).Append('\n'); return;
                case "has_finished": sb.Append(Line("has_finished<T>(drone: Drone<T>) -> bool")).Append('\n'); return;
                case "measure":
                    sb.Append(Line(withForm ? "measure_with<T: Measurement>(direction: Direction) -> T" : "measure<T: Measurement>() -> T")).Append('\n');
                    return;
                case "min": sb.Append("pub fn min<T: PartialOrd>(a: T, b: T) -> T {\n    if b < a { b } else { a }\n}\n\n"); return;
                case "max": sb.Append("pub fn max<T: PartialOrd>(a: T, b: T) -> T {\n    if b > a { b } else { a }\n}\n\n"); return;
                case "abs": sb.Append("pub fn abs<T: Number>(x: T) -> T {\n    x.abs_value()\n}\n\n"); return;
            }
            var gens = new List<string>();
            var ps = new List<string>();
            foreach (var p in b.Params)
            {
                if (p.Optional && !p.Variadic && !withForm) continue;
                var t = Rt(p.TypeText, true, false, gens, "A");
                if (p.Variadic) t = "&[" + Rt(p.TypeText, true, true, gens, "A") + "]";
                ps.Add(ParamName(p.Name) + ": " + t);
            }
            var ret = Rt(b.ReturnText, false, false, gens, "R");
            var g = gens.Count > 0 ? "<" + string.Join(", ", gens) + ">" : "";
            sb.Append(Line(name + g + "(" + string.Join(", ", ps) + ")" + (ret == "()" ? "" : " -> " + ret)));
            if (py == "move") sb.Append("/// The game's `move` (a Rust keyword): `r#move(North)` works too.\npub use move_to as r#move;\n");
            sb.Append('\n');
        }
    }
}
