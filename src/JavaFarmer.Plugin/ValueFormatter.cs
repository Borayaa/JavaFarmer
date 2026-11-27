using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Java-style text for game values (replaces CodeUtilities.ToNiceString while the mod is active).
    /// It drives System.out.println / quickPrint, String.valueOf and "" + x, the output page, the value tooltip
    /// on hovered variables and the values quoted in runtime error messages.
    ///
    ///   null / true / false                 (Python: None / True / False)
    ///   List, array:  [1, 2, 3]            (Python: [1,2,3])
    ///   Set:          [a, b]               (Python: {'a','b'})
    ///   Map:          {a=1, b=2}           (Python: {'a':1,'b':2})
    ///   Position:     Position[x=1, y=2]   (Python tuple (1,2); Java record style)
    ///   Companion:    Companion[entity=Entities.Tree, position=Position[x=1, y=2]]
    ///   Drone:        Drone@3              (Python: &lt;drone 3&gt;)
    ///   method value: its Java name; lambda: "lambda"
    ///   Strings are never quoted, also inside collections (like Java). Numbers keep the game's format (1, 1.5, 0.33).
    /// </summary>
    public static class ValueFormatter
    {
        public const int MaxDepth = 100;

        public static string Format(object obj) => Format(obj, 0, null);

        private static string Format(object obj, int depth, HashSet<object> seen)
        {
            if (depth > MaxDepth) return "";
            if (obj == null || obj is PyNone) return "null";
            if (obj is PyBool b) return b.num != 0.0 ? "true" : "false";
            if (obj is PyNumber n) return n.num.ToString("0.##", CultureInfo.InvariantCulture);
            if (obj is PyString s) return s.str ?? "";
            if (seen != null && seen.Contains(obj)) return "(this Collection)";
            switch (obj)
            {
                case PyList list:
                    return Join("[", list, "]", depth, Enter(ref seen, obj));
                case PySet set:
                    return Join("[", set.set, "]", depth, Enter(ref seen, obj));
                case PyDict dict:
                {
                    Enter(ref seen, obj);
                    var sb = new StringBuilder("{");
                    bool first = true;
                    foreach (var kv in dict.dict)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append(Format(kv.Key, depth + 1, seen)).Append('=').Append(Format(kv.Value?.obj, depth + 1, seen));
                    }
                    return sb.Append('}').ToString();
                }
                case PyTuple tuple:
                    return FormatTuple(tuple, depth, seen);
                case PyDroneHandle drone:
                    return "Drone@" + drone.generation.ToString(CultureInfo.InvariantCulture);
                case PyFunction fn:
                    return RuntimeNames.ToJava(fn.functionName) ?? "lambda";
                case PyModule module:
                {
                    string m = module.ToString() ?? "";
                    return m.StartsWith("<Module ") && m.EndsWith(">") ? m.Substring(8, m.Length - 9) : m;
                }
            }
            try { return Convert.ToString(obj, CultureInfo.InvariantCulture); }
            catch { return obj.GetType().Name; }
        }

        private static HashSet<object> Enter(ref HashSet<object> seen, object obj)
        {
            if (seen == null) seen = new HashSet<object>(RefEq.Instance);
            seen.Add(obj);
            return seen;
        }

        private static string Join(string open, IEnumerable<IPyObject> items, string close, int depth, HashSet<object> seen)
        {
            var sb = new StringBuilder(open);
            bool first = true;
            foreach (var item in items)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(Format(item, depth + 1, seen));
            }
            return sb.Append(close).ToString();
        }

        private static bool IsNumber(IPyObject o) => o is PyNumber && !(o is PyBool);

        private static string FormatTuple(PyTuple t, int depth, HashSet<object> seen)
        {
            var e = t.elements;
            if (e != null && e.Count == 2 && IsNumber(e[0]) && IsNumber(e[1]))
                return "Position[x=" + Format(e[0], depth + 1, seen) + ", y=" + Format(e[1], depth + 1, seen) + "]";
            if (e != null && e.Count == 2 && e[1] is PyTuple p && p.elements != null && p.elements.Count == 2
                && IsNumber(p.elements[0]) && IsNumber(p.elements[1]))
                return "Companion[entity=" + Format(e[0], depth + 1, seen) + ", position=" + FormatTuple(p, depth + 1, seen) + "]";
            return Join("[", (IEnumerable<IPyObject>)e ?? Array.Empty<IPyObject>(), "]", depth, seen);
        }

        private sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }
    }
}
