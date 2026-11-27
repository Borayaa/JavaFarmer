using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    /// <summary>Synthetic helper functions (SPEC §6.6), written in the basic Python the game supports.
    /// They avoid builtin calls where possible because builtin calls are charged as unlock dependencies.</summary>
    internal static class Helpers
    {
        public static readonly Dictionary<string, string> Source = new Dictionary<string, string>
        {
            ["__jdiv"] =
                "def __jdiv(a, b):\n" +
                "    q = a // b\n" +
                "    if q < 0 and q * b != a:\n" +
                "        q += 1\n" +
                "    return q\n",
            ["__jmod"] =
                "def __jmod(a, b):\n" +
                "    return a - b * __jdiv(a, b)\n",
            ["__jtrunc"] =
                "def __jtrunc(x):\n" +
                "    if x < 0:\n" +
                "        return -((-x) // 1)\n" +
                "    return x // 1\n",
            ["__jfloor"] =
                "def __jfloor(x):\n" +
                "    return x // 1\n",
            ["__jceil"] =
                "def __jceil(x):\n" +
                "    return -((-x) // 1)\n",
            ["__jround"] =
                "def __jround(x):\n" +
                "    return (x + 0.5) // 1\n",
            ["__jsqrt"] =
                "def __jsqrt(x):\n" +
                "    return x ** 0.5\n",
            ["__jpow"] =
                "def __jpow(a, b):\n" +
                "    return a ** b\n",
            ["__jarr"] =
                "def __jarr(n, d):\n" +
                "    r = []\n" +
                "    while n > 0:\n" +
                "        r += [d]\n" +
                "        n -= 1\n" +
                "    return r\n",
            ["__jarr2"] =
                "def __jarr2(w, h, d):\n" +
                "    r = []\n" +
                "    while w > 0:\n" +
                "        r += [__jarr(h, d)]\n" +
                "        w -= 1\n" +
                "    return r\n",
            ["__jget"] =
                "def __jget(m, k):\n" +
                "    if k in m:\n" +
                "        return m[k]\n" +
                "    return None\n",
            ["__jgetd"] =
                "def __jgetd(m, k, d):\n" +
                "    if k in m:\n" +
                "        return m[k]\n" +
                "    return d\n",
            ["__jmremove"] =
                "def __jmremove(m, k):\n" +
                "    if k in m:\n" +
                "        return m.pop(k)\n" +
                "    return None\n",
            ["__jremove"] =
                "def __jremove(c, x):\n" +
                "    if x in c:\n" +
                "        c.remove(x)\n",
            ["__jindexof"] =
                "def __jindexof(l, x):\n" +
                "    i = 0\n" +
                "    for e in l:\n" +
                "        if e == x:\n" +
                "            return i\n" +
                "        i += 1\n" +
                "    return -1\n",
            ["__jvalues"] =
                "def __jvalues(m):\n" +
                "    r = []\n" +
                "    for k in m:\n" +
                "        r += [m[k]]\n" +
                "    return r\n",
            ["__jlclear"] =
                "def __jlclear(l):\n" +
                "    while l != []:\n" +
                "        l.pop()\n",
            ["__jsclear"] =
                "def __jsclear(s):\n" +
                "    r = []\n" +
                "    for x in s:\n" +
                "        r += [x]\n" +
                "    for x in r:\n" +
                "        s.remove(x)\n",
            ["__jmclear"] =
                "def __jmclear(m):\n" +
                "    r = []\n" +
                "    for k in m:\n" +
                "        r += [k]\n" +
                "    for k in r:\n" +
                "        m.pop(k)\n",
        };

        static readonly Dictionary<string, string[]> Deps = new Dictionary<string, string[]>
        {
            ["__jmod"] = new[] { "__jdiv" },
            ["__jarr2"] = new[] { "__jarr" },
        };

        /// <summary>The used helpers plus their dependencies, in a stable order.</summary>
        public static List<string> Ordered(HashSet<string> used)
        {
            var all = new HashSet<string>();
            void Add(string h)
            {
                if (!all.Add(h)) return;
                if (Deps.TryGetValue(h, out var ds)) foreach (var d in ds) Add(d);
            }
            foreach (var h in used) Add(h);
            var res = new List<string>();
            foreach (var k in Source.Keys) if (all.Contains(k)) res.Add(k);
            return res;
        }
    }
}
