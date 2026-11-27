using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Python identifiers produced by the lowering -> the Java names the player wrote. Filled from every successful
    /// compile (CompileResult.Names); read by runtime error texts, the error trace and the hover value tooltip.
    /// The renaming is a pure function of the Java name, so merging the maps of all windows is safe.
    /// </summary>
    public static class RuntimeNames
    {
        private static readonly object gate = new object();
        private static Dictionary<string, string> pyToJava = new Dictionary<string, string>();
        private static Dictionary<string, List<string>> javaToPy = new Dictionary<string, List<string>>();

        public const string HelperPrefix = "__j";
        public const string LambdaPrefix = "__jlambda";

        public static void Register(IReadOnlyDictionary<string, string> names)
        {
            if (names == null || names.Count == 0) return;
            lock (gate)
            {
                bool changed = false;
                foreach (var kv in names)
                    if (!pyToJava.TryGetValue(kv.Key, out var old) || old != kv.Value) { changed = true; break; }
                if (!changed) return;
                var a = new Dictionary<string, string>(pyToJava);
                foreach (var kv in names)
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value)) a[kv.Key] = kv.Value;
                var b = new Dictionary<string, List<string>>();
                foreach (var kv in a)
                {
                    if (kv.Key.StartsWith(HelperPrefix, StringComparison.Ordinal)) continue;
                    if (!b.TryGetValue(kv.Value, out var l)) b[kv.Value] = l = new List<string>();
                    l.Add(kv.Key);
                }
                // most specific first: a renamed local ("list_j_l") before the method it was renamed away from ("list_j")
                foreach (var l in b.Values) l.Sort((x, y) => x.Length != y.Length ? y.Length.CompareTo(x.Length) : string.CompareOrdinal(x, y));
                pyToJava = a;
                javaToPy = b;
            }
        }

        /// <summary>For tests.</summary>
        public static void Clear()
        {
            lock (gate) { pyToJava = new Dictionary<string, string>(); javaToPy = new Dictionary<string, List<string>>(); }
        }

        public static bool IsHelper(string py) =>
            py != null && py.StartsWith(HelperPrefix, StringComparison.Ordinal) && !py.StartsWith(LambdaPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Python identifier -> the name to show the player: renamed user names get their Java name back,
        /// lambdas become "lambda", other JavaFarmer helpers return null (hide them). Unknown names are returned unchanged.
        /// </summary>
        public static string ToJava(string py)
        {
            if (string.IsNullOrEmpty(py)) return py;
            if (py.StartsWith(LambdaPrefix, StringComparison.Ordinal)) return "lambda";
            if (py.StartsWith(HelperPrefix, StringComparison.Ordinal)) return null;
            var map = pyToJava;
            return map.TryGetValue(py, out var j) ? j : py;
        }

        /// <summary>Python identifiers a Java name was renamed to (most specific first), or an empty list.</summary>
        public static IReadOnlyList<string> PythonCandidates(string java)
        {
            if (string.IsNullOrEmpty(java)) return Array.Empty<string>();
            var map = javaToPy;
            return map.TryGetValue(java, out var l) ? (IReadOnlyList<string>)l : Array.Empty<string>();
        }
    }

    /// <summary>Pure text rewrites of game-generated UI text (unit tested, no Unity calls).</summary>
    public static class JavaText
    {
        /// <summary>Game builtins that are constructors or Python-only: their Java spelling in the docs index (null = hidden).</summary>
        public static readonly Dictionary<string, string> BuiltinTocNames = new Dictionary<string, string>
        {
            { "list", "ArrayList" },
            { "set", "HashSet" },
            { "dict", "HashMap" },
            { "range", null },   // Java has a classic for loop instead
            { "len", null },     // .length / .size() / .length()
        };

        /// <summary>Java name of a game builtin for the docs index, or null when it has no Java spelling.</summary>
        public static string BuiltinJavaName(string py, Func<string, string> toJava = null)
        {
            if (string.IsNullOrEmpty(py)) return null;
            if (BuiltinTocNames.TryGetValue(py, out var fixedName)) return fixedName;
            toJava = toJava ?? EditorSupport.SafeToJava;
            string j = toJava(py);
            return string.IsNullOrEmpty(j) ? SnakeToCamel(py) : j;
        }

        /// <summary>"pet_the_piggy" -> "petThePiggy".</summary>
        public static string SnakeToCamel(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            bool up = false;
            foreach (char c in s)
            {
                if (c == '_') { up = sb.Length > 0; continue; }
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = false;
            }
            return sb.ToString();
        }

        /// <summary>
        /// The docs home "Built-in Functions" list (replaces MarkdownText.GenerateBuiltinsTOC): Java names as link text,
        /// sorted by Java name; the unlock gate and the link target keep the game's (Python) key.
        /// </summary>
        /// <summary>The docs index for any language: builtins listed by ILanguage.ApiName (Java: v1 names).</summary>
        public static string BuiltinsToc(IEnumerable<string> pythonNames, ILanguage lang)
        {
            if (lang == null || lang.Id == "java") return BuiltinsToc(pythonNames);
            return BuiltinsToc(pythonNames, py => { try { return lang.ApiName(py) ?? py; } catch { return py; } }, false);
        }

        public static string BuiltinsToc(IEnumerable<string> pythonNames, Func<string, string> toJava = null)
            => BuiltinsToc(pythonNames, toJava, true);

        private static string BuiltinsToc(IEnumerable<string> pythonNames, Func<string, string> toJava, bool javaNames)
        {
            var entries = new List<(string java, string py)>();
            foreach (var py in pythonNames ?? Enumerable.Empty<string>())
            {
                string j = javaNames ? BuiltinJavaName(py, toJava) : toJava?.Invoke(py);
                if (!string.IsNullOrEmpty(j)) entries.Add((j, py));
            }
            entries.Sort((a, b) =>
            {
                int c = string.Compare(a.java, b.java, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.CompareOrdinal(a.java, b.java);
            });
            var sb = new StringBuilder();
            foreach (var (java, py) in entries)
                sb.Append("<unlock=").Append(py).Append(">[").Append(java).Append("](functions/").Append(py).Append(")      </unlock>");
            return sb.ToString();
        }

        /// <summary>
        /// Rewrites the call chain line of ProgramState.GetTrace ("__jlambda1 &lt;- main\n...") into Java names:
        /// lambdas become "lambda", JavaFarmer helpers are dropped, renamed methods get their Java name.
        /// </summary>
        public static string RewriteTrace(string trace, Func<string, string> toJava = null)
        {
            if (string.IsNullOrEmpty(trace)) return trace;
            toJava = toJava ?? RuntimeNames.ToJava;
            int nl = trace.IndexOf('\n');
            string first = nl < 0 ? trace : trace.Substring(0, nl);
            string rest = nl < 0 ? "" : trace.Substring(nl);
            const string sep = " <- ";
            var parts = first.Split(new[] { sep }, StringSplitOptions.None);
            var outParts = new List<string>();
            foreach (var p in parts)
            {
                if (p == "...") { outParts.Add(p); continue; }
                string j = toJava(p);
                if (string.IsNullOrEmpty(j)) continue;
                if (outParts.Count > 0 && outParts[outParts.Count - 1] == j && j == "lambda") continue;
                outParts.Add(j);
            }
            return string.Join(sep, outParts) + rest;
        }

        /// <summary>
        /// FunctionNode.GetSignature for Java: the lines of the method header plus the // or /** */ comment lines right
        /// above it (the game only looks for Python '#' comments).
        /// </summary>
        public static string SignatureWithComments(string text, int start, int end)
            => SignatureWithComments(text, start, end, new[] { "//", "/*", "*" });

        /// <summary>SignatureWithComments with the comment prefixes of a language ("#" for R ...).</summary>
        public static string SignatureWithComments(string text, int start, int end, EditorRules rules)
        {
            var prefixes = new List<string>();
            if (!string.IsNullOrEmpty(rules?.LineComment)) prefixes.Add(rules.LineComment);
            if (!string.IsNullOrEmpty(rules?.BlockCommentStart)) { prefixes.Add(rules.BlockCommentStart); prefixes.Add("*"); }
            if (prefixes.Count == 0) prefixes.Add("//");
            return SignatureWithComments(text, start, end, prefixes.ToArray());
        }

        public static string SignatureWithComments(string text, int start, int end, string[] commentPrefixes)
        {
            if (text == null) return null;
            var lines = text.Split('\n');
            int a = LineOf(lines, Math.Max(0, start));
            int b = LineOf(lines, Math.Max(start, end - 1));
            int first = a;
            while (first > 0)
            {
                string t = lines[first - 1].Trim();
                bool isComment = false;
                foreach (var p in commentPrefixes) if (t.StartsWith(p, StringComparison.Ordinal)) { isComment = true; break; }
                if (isComment) first--;
                else break;
            }
            return string.Join("\n", lines.Skip(first).Take(b - first + 1).Select(l => l.TrimEnd('\r')));
        }

        private static int LineOf(string[] lines, int index)
        {
            int pos = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                int next = pos + lines[i].Length + 1;
                if (index < next) return i;
                pos = next;
            }
            return Math.Max(0, lines.Length - 1);
        }
    }
}
