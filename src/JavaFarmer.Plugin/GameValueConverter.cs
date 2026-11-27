using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// The game's runtime values (Py* objects of Utils/Core) -> the neutral model of Compiler/Core/GameValues.cs that
    /// ILanguage.FormatValue reads. Shared objects and cycles keep their identity (a list containing itself maps to a
    /// GameList containing itself), so the formatter's cycle detection works.
    /// </summary>
    public static class GameValueConverter
    {
        private static readonly Regex constantText = new Regex(@"^(Entities|Items|Grounds|Unlocks|Hats|Leaderboards)\.([A-Za-z0-9_]+)$");

        public static object Convert(object value) => Convert(value, new Dictionary<object, object>(RefEq.Instance), 0);

        private static object Convert(object obj, Dictionary<object, object> seen, int depth)
        {
            if (depth > ValueFormat.MaxDepth) return "";
            switch (obj)
            {
                case null: return null;
                case PyNone _: return null;
                case PyBool b: return b.num != 0.0;
                case PyNumber n: return n.num;
                case PyString s: return s.str ?? "";
                case PyGridDirection d: return new GameConstant("Directions", d.dir.ToString());
                case PyDroneHandle drone: return new GameDrone(drone.generation);
                case PyFunction fn: return new GameFunction(FunctionName(fn));
                case PyModule module:
                {
                    string m = module.ToString() ?? "";
                    return new GameModule(m.StartsWith("<Module ") && m.EndsWith(">") ? m.Substring(8, m.Length - 9) : m);
                }
            }
            if (seen.TryGetValue(obj, out var done)) return done;
            switch (obj)
            {
                case PyList list:
                {
                    var g = new GameList();
                    seen[obj] = g;
                    foreach (var item in list) g.Items.Add(Convert(item, seen, depth + 1));
                    return g;
                }
                case PySet set:
                {
                    var g = new GameSet();
                    seen[obj] = g;
                    foreach (var item in set.set) g.Items.Add(Convert(item, seen, depth + 1));
                    return g;
                }
                case PyDict dict:
                {
                    var g = new GameDict();
                    seen[obj] = g;
                    foreach (var kv in dict.dict) g.Add(Convert(kv.Key, seen, depth + 1), Convert(kv.Value?.obj, seen, depth + 1));
                    return g;
                }
                case PyTuple tuple:
                {
                    var g = new GameTuple();
                    seen[obj] = g;
                    if (tuple.elements != null) foreach (var item in tuple.elements) g.Items.Add(Convert(item, seen, depth + 1));
                    return g;
                }
            }
            string text;
            try { text = System.Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture); }
            catch { text = obj.GetType().Name; }
            var cm = text == null ? null : constantText.Match(text);
            if (cm != null && cm.Success) return new GameConstant(cm.Groups[1].Value, cm.Groups[2].Value);
            return text ?? "";
        }

        /// <summary>
        /// Builtins keep their game name (the language maps it: print -> System.out.println). User functions get the
        /// name the player wrote back (list_j -> list, __jlambda1 -> lambda); JavaFarmer helpers show as "lambda".
        /// </summary>
        private static string FunctionName(PyFunction fn)
        {
            string name = fn.functionName;
            if (fn.syntaxTree == null) return name;
            return RuntimeNames.ToJava(name) ?? "__jlambda";
        }

        private sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }
    }
}
