// Argument checking and value conversion between the game's IPyObject values and the neutral values of ExtCall.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace JavaFarmer.Plugin.Extensions
{
    public static class ExtArgs
    {
        /// <summary>Checks count and kinds like the game's CorrectParams; throws ExecuteException.</summary>
        public static object[] Convert(BuiltinDef def, List<IPyObject> parameters)
        {
            int n = parameters?.Count ?? 0;
            if (n != def.Params.Count) throw new ExecuteException(WrongCount(def.Name, def.Params.Count, n));
            var result = new object[n];
            for (int i = 0; i < n; i++)
            {
                var p = parameters[i];
                switch (def.Params[i])
                {
                    case ParamKind.Int:
                    {
                        if (!(p is PyNumber num)) throw new ExecuteException(WrongType(def.Name, i, p, "a number"));
                        double d = num;
                        if (double.IsNaN(d) || double.IsInfinity(d) || d != Math.Floor(d))
                            throw new ExecuteException(def.Name + "(): argument " + (i + 1) + " must be a whole number, not " + d.ToString(CultureInfo.InvariantCulture));
                        result[i] = d;
                        break;
                    }
                    case ParamKind.Number:
                    {
                        if (!(p is PyNumber num)) throw new ExecuteException(WrongType(def.Name, i, p, "a number"));
                        double d = num;
                        if (double.IsNaN(d) || double.IsInfinity(d)) throw new ExecuteException(def.Name + "(): argument " + (i + 1) + " must be a finite number");
                        result[i] = d;
                        break;
                    }
                    case ParamKind.String:
                        if (!(p is PyString s)) throw new ExecuteException(WrongType(def.Name, i, p, "a string"));
                        result[i] = s.str ?? "";
                        break;
                    case ParamKind.Bool:
                        if (!(p is PyNumber b)) throw new ExecuteException(WrongType(def.Name, i, p, "True or False"));
                        result[i] = (double)b != 0;
                        break;
                }
            }
            return result;
        }

        static string WrongCount(string fn, int expected, int got)
        {
            try
            {
                var s = CodeUtilities.LocalizeAndFormat("error_wrong_number_args", fn + "()", expected);
                if (!string.IsNullOrEmpty(s) && s != "error_wrong_number_args") return s;
            }
            catch { }
            return fn + "() takes " + expected + (expected == 1 ? " argument" : " arguments") + " (" + got + " given)";
        }

        static string WrongType(string fn, int i, IPyObject p, string expected)
        {
            try
            {
                var s = CodeUtilities.LocalizeAndFormat("error_wrong_args", i + 1, fn + "()", p);
                if (!string.IsNullOrEmpty(s) && s != "error_wrong_args") return s;
            }
            catch { }
            return fn + "(): argument " + (i + 1) + " must be " + expected;
        }

        public static IPyObject ToPy(object v)
        {
            switch (v)
            {
                case null: return PyNone.Instance;
                case bool b: return new PyBool(b);
                case double d: return new PyNumber(d);
                case int i: return new PyNumber(i);
                case long l: return new PyNumber(l);
                case string s: return new PyString(s);
                case IEnumerable<double> nums:
                {
                    var list = new List<IPyObject>();
                    foreach (var x in nums) list.Add(new PyNumber(x));
                    return new PyList(list);
                }
                case IEnumerable<string> strs:
                {
                    var list = new List<IPyObject>();
                    foreach (var x in strs) list.Add(new PyString(x));
                    return new PyList(list);
                }
                case IPyObject py: return py;
            }
            return new PyString(System.Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        /// <summary>Neutral value of a game value (tests, hover).</summary>
        public static object FromPy(IPyObject p)
        {
            switch (p)
            {
                case null: return null;
                case PyNone _: return null;
                case PyBool b: return (double)b != 0;
                case PyNumber n: return (double)n;
                case PyString s: return s.str;
            }
            return p.ToString();
        }
    }
}
