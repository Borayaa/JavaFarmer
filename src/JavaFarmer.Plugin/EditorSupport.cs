using System;
using System.Collections.Generic;
using System.Linq;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>Pure editor helpers (auto-indent, completion words, hover names). No Unity calls: unit tested.</summary>
    public static class EditorSupport
    {
        public const string NewFileTemplate = "void main() {\n\t\n}";

        // ---------------------------------------------------------------- auto-indent

        /// <summary>
        /// After the game handled a typed newline (it already copied the previous line's indentation):
        /// add one level after '{', and when the caret sits right before '}', move that brace to its own line.
        /// Returns false when nothing changes.
        /// </summary>
        public static bool AfterNewline(string text, int caret, string indentUnit, out string newText, out int newCaret)
            => AfterNewline(text, caret, indentUnit, JavaRules, out newText, out newCaret);

        /// <summary>Java's editor rules (v1 behaviour).</summary>
        public static readonly EditorRules JavaRules = new EditorRules();

        /// <summary>
        /// AfterNewline for any language: one more level after a line ending with one of rules.IndentAfter (":" is left
        /// to the game, which already indents after it); a closer (rules.DedentOn) right after the caret moves to its own line.
        /// </summary>
        public static bool AfterNewline(string text, int caret, string indentUnit, EditorRules rules, out string newText, out int newCaret)
        {
            newText = text; newCaret = caret;
            rules = rules ?? JavaRules;
            if (text == null || caret <= 0 || caret > text.Length) return false;
            int q = caret;
            while (q > 0 && (text[q - 1] == ' ' || text[q - 1] == '\t')) q--;
            if (q == 0 || text[q - 1] != '\n') return false;
            string baseIndent = text.Substring(q, caret - q);
            int nl = q - 1;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, nl - 1));
            lineStart = nl == 0 ? 0 : lineStart + 1;
            string prevLine = text.Substring(lineStart, nl - lineStart);
            string trimmed = StripLineComment(prevLine, rules.LineComment).TrimEnd();
            bool opens = false;
            foreach (var o in rules.IndentAfter ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(o) && o != ":" && trimmed.EndsWith(o, StringComparison.Ordinal)) { opens = true; break; }
            if (!opens) return false;

            // The game indents after ':'; a line ending with '{' never ends with ':' so no double indent.
            string prevIndent = LeadingWhitespace(prevLine);
            string inner = prevIndent + indentUnit;
            string insert = inner.Length > baseIndent.Length ? inner.Substring(baseIndent.Length) : "";
            if (!inner.StartsWith(baseIndent)) { insert = ""; inner = baseIndent; }
            string after = "";
            int tail = caret;
            while (tail < text.Length && (text[tail] == ' ' || text[tail] == '\t')) tail++;
            if (tail < text.Length && StartsWithCloser(text, tail, rules))
                after = "\n" + prevIndent;
            if (insert.Length == 0 && after.Length == 0) return false;
            newText = text.Substring(0, caret) + insert + after + text.Substring(caret);
            newCaret = caret + insert.Length;
            return true;
        }

        private static bool StartsWithCloser(string text, int at, EditorRules rules)
        {
            foreach (var c in rules.DedentOn ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(c) && string.CompareOrdinal(text, at, c, 0, c.Length) == 0) return true;
            return false;
        }

        /// <summary>True when the typed text is one of the language's single-character dedent tokens ("}").</summary>
        public static bool IsDedentChar(string inserted, EditorRules rules)
        {
            if (string.IsNullOrEmpty(inserted) || inserted.Length != 1) return false;
            foreach (var c in (rules ?? JavaRules).DedentOn ?? Array.Empty<string>())
                if (c == inserted) return true;
            return false;
        }

        /// <summary>When '}' was typed on a line holding only whitespace, remove one indentation level before it.</summary>
        public static bool AfterCloseBrace(string text, int caret, out string newText, out int newCaret)
        {
            newText = text; newCaret = caret;
            if (text == null || caret <= 0 || caret > text.Length) return false;
            return AfterDedentChar(text, caret, text[caret - 1] == '}' ? JavaRules : null, out newText, out newCaret);
        }

        /// <summary>When a dedent character (rules.DedentOn, e.g. '}') was typed on a line holding only whitespace, remove one indentation level before it.</summary>
        public static bool AfterDedentChar(string text, int caret, EditorRules rules, out string newText, out int newCaret)
        {
            newText = text; newCaret = caret;
            if (rules == null || text == null || caret <= 0 || caret > text.Length || !IsDedentChar(text[caret - 1].ToString(), rules)) return false;
            int bracePos = caret - 1;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, bracePos - 1));
            lineStart = bracePos == 0 ? 0 : lineStart + 1;
            if (lineStart > bracePos) return false;
            string before = text.Substring(lineStart, bracePos - lineStart);
            if (before.Length == 0 || before.Any(c => c != ' ' && c != '\t')) return false;
            int remove;
            if (before.EndsWith("\t")) remove = 1;
            else
            {
                remove = 0;
                while (remove < 4 && remove < before.Length && before[before.Length - 1 - remove] == ' ') remove++;
            }
            if (remove == 0) return false;
            newText = text.Remove(bracePos - remove, remove);
            newCaret = caret - remove;
            return true;
        }

        private static string LeadingWhitespace(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            return line.Substring(0, i);
        }

        private static string StripLineComment(string line, string prefix = "//")
        {
            // good enough for indentation: ignore the line comment outside a string literal
            if (string.IsNullOrEmpty(prefix)) return line;
            bool inString = false;
            for (int i = 0; i <= line.Length - prefix.Length; i++)
            {
                char c = line[i];
                if (c == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
                if (!inString && string.CompareOrdinal(line, i, prefix, 0, prefix.Length) == 0) return line.Substring(0, i);
            }
            return line;
        }

        // ---------------------------------------------------------------- completion

        /// <summary>Python keyword (as reported unlocked by the game) -> Java words it unlocks.</summary>
        public static readonly Dictionary<string, string[]> KeywordMap = new Dictionary<string, string[]>
        {
            { "while", new[] { "while", "do" } },
            { "for", new[] { "for" } },
            { "if", new[] { "if" } },
            { "else", new[] { "else", "switch", "case", "default" } },
            { "elif", new string[0] },
            { "def", new[] { "void", "static", "return" } },
            { "return", new[] { "return" } },
            { "import", new[] { "import" } },
            { "break", new[] { "break" } },
            { "continue", new[] { "continue" } },
            { "pass", new string[0] },
            { "global", new string[0] },
            { "and", new string[0] },
            { "or", new string[0] },
            { "not", new string[0] },
            { "in", new string[0] },
            { "from", new string[0] },
            { "True", new[] { "true" } },
            { "False", new[] { "false" } },
            { "None", new[] { "null" } },
        };

        /// <summary>Extra Java words, shown when the given game unlock/builtin is unlocked.</summary>
        public static readonly (string unlock, string[] words)[] ExtraWords =
        {
            ("variables", new[] { "int", "double", "boolean", "String", "var", "final" }),
            ("lists", new[] { "List", "ArrayList", "new" }),
            ("dictionaries", new[] { "Map", "HashMap", "Set", "HashSet", "new" }),
            ("print", new[] { "System" }),
            ("min", new[] { "Math" }),
            ("str", new[] { "String" }),
            ("spawn_drone", new[] { "Runnable", "Supplier" }),
        };

        /// <summary>
        /// Converts the game's (Python) completion list into Java words.
        /// builtin: is this a builtin function name; isUnlocked: game unlock check.
        /// </summary>
        public static List<string> JavaWordList(IEnumerable<string> vanilla, Func<string, bool> isBuiltin, Func<string, bool> isUnlocked,
            Func<string, string> toJava = null)
        {
            toJava = toJava ?? SafeToJava;
            var set = new HashSet<string>();
            foreach (var w in vanilla ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(w) || w.StartsWith("__")) continue;
                if (KeywordMap.TryGetValue(w, out var kws)) { foreach (var k in kws) set.Add(k); continue; }
                if (isBuiltin != null && isBuiltin(w))
                {
                    string j = toJava(w);
                    if (!string.IsNullOrEmpty(j)) set.Add(j);
                    continue;
                }
                set.Add(w);
            }
            set.Add("void");
            set.Add("main");
            foreach (var (unlock, words) in ExtraWords)
            {
                bool ok;
                try { ok = isUnlocked == null || isUnlocked(unlock); } catch { ok = false; }
                if (ok) foreach (var x in words) set.Add(x);
            }
            var list = set.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public static readonly string[] CollectionMethods =
        {
            "add", "get", "set", "remove", "size", "isEmpty", "contains", "clear", "indexOf",
            "put", "getOrDefault", "containsKey", "keySet", "values", "length", "equals", "charAt",
        };

        public static readonly string[] MathMethods = { "abs", "ceil", "floor", "max", "min", "pow", "random", "round", "sqrt" };

        /// <summary>Members after "domain." when the domain is neither a game enum nor another code window.</summary>
        public static List<string> JavaMemberList(string domain)
        {
            switch (domain)
            {
                case "System": return new List<string> { "out" };
                case "out": return new List<string> { "print", "println" };
                case "Math": return MathMethods.ToList();
                case "String": return new List<string> { "valueOf" };
                case "List": case "Set": case "Map": return new List<string> { "of" };
                default: return CollectionMethods.ToList();
            }
        }

        public static List<string> FilterHelpers(IEnumerable<string> words) =>
            (words ?? Enumerable.Empty<string>()).Where(w => !string.IsNullOrEmpty(w) && !w.StartsWith("__")).ToList();

        // ---------------------------------------------------------------- hover tooltips

        private static readonly Dictionary<string, string> javaToPyWords = new Dictionary<string, string>
        {
            { "true", "True" }, { "false", "False" }, { "null", "None" }, { "void", "def" },
            { "System.out.println", "print" }, { "System.out.print", "print" }, { "println", "print" },
            { "String.valueOf", "str" },
            { "Math.min", "min" }, { "Math.max", "max" }, { "Math.abs", "abs" }, { "Math.random", "random" },
            // collection types: the game's constructor tooltips (rewritten for Java in content/Strings/code_tooltips.txt)
            { "ArrayList", "list" }, { "List", "list" }, { "HashMap", "dict" }, { "Map", "dict" }, { "HashSet", "set" }, { "Set", "set" },
        };

        /// <summary>
        /// Java words that have their own tooltip key (code_tooltip_&lt;word&gt; in content/Strings) -> the game unlock
        /// that must be unlocked to show it (the Python twin of the word).
        /// </summary>
        public static readonly Dictionary<string, string> JavaKeywordGates = new Dictionary<string, string>
        {
            { "true", "True" }, { "false", "False" }, { "null", "None" },
            { "void", "def" }, { "static", "def" },
            { "int", "variables" }, { "double", "variables" }, { "boolean", "variables" }, { "String", "variables" },
            { "var", "variables" }, { "final", "variables" },
            { "new", "lists" }, { "do", "while" }, { "switch", "if" },
        };

        /// <summary>
        /// Game builtins that have no unqualified Java spelling. A bare Java identifier with one of these names is the
        /// player's own variable or method, never the Python builtin, so it must not get the builtin's tooltip.
        /// </summary>
        public static readonly HashSet<string> PythonOnlyBuiltins = new HashSet<string>
        {
            "range", "len", "list", "set", "dict", "str", "print", "min", "max", "abs", "random",
            "append", "pop", "insert",
            // Python keywords: in Java code these can only be the player's own names
            "def", "elif", "pass", "global", "not", "and", "or", "in", "is", "from", "None", "True", "False",
        };

        /// <summary>True for a bare word (no qualifier) that only names a Python builtin (see PythonOnlyBuiltins).</summary>
        public static bool IsPythonOnlyWord(string word) =>
            !string.IsNullOrEmpty(word) && word.IndexOf('.') < 0 && PythonOnlyBuiltins.Contains(word);

        private static readonly HashSet<string> gameEnums = new HashSet<string> { "Entities", "Grounds", "Items", "Unlocks", "Hats", "Leaderboards" };

        /// <summary>Java word under the mouse -> the game's (Python) word for its tooltip, or null to keep the word.</summary>
        public static string HoverWordToPython(string word, Func<string, string> toPython = null)
        {
            toPython = toPython ?? SafeToPython;
            if (string.IsNullOrEmpty(word)) return null;
            if (javaToPyWords.TryGetValue(word, out var mapped)) return mapped;
            int dot = word.IndexOf('.');
            if (dot >= 0)
            {
                string first = word.Substring(0, dot);
                if (gameEnums.Contains(first)) return null;
                string py = toPython(word);
                if (py != null) return py;
                string last = word.Substring(word.LastIndexOf('.') + 1);
                if (javaToPyWords.TryGetValue(last, out mapped)) return mapped;
                py = toPython(last);
                return py != null && py != last ? py : null;
            }
            string p = toPython(word);
            return p != null && p != word ? p : null;
        }

        // ---------------------------------------------------------------- any language (not Java, not Python)

        /// <summary>Keywords a language may use for functions / variables / entry points (only those in its Keywords are shown).</summary>
        private static readonly string[] functionWords = { "fn", "function", "func", "def", "void", "return", "static", "pub", "inline" };
        private static readonly string[] variableWords = { "let", "mut", "var", "const", "auto", "int", "double", "float", "bool", "boolean", "string", "String",
            "char", "long", "i32", "i64", "u32", "u64", "f32", "f64", "usize", "isize", "str", "dynamic", "object" };
        private static readonly string[] entryWords = { "void", "int", "fn", "pub", "function", "main" };

        /// <summary>
        /// The game's (Python) completion list in another language: builtins by ILanguage.ApiName, Python keywords the
        /// language shares, True/False/None as the language prints them, function/variable keywords once "def" /
        /// "variables" are unlocked. User symbols and game constants pass through.
        /// </summary>
        public static List<string> LanguageWordList(IEnumerable<string> vanilla, Func<string, bool> isBuiltin, Func<string, bool> isUnlocked, ILanguage lang)
        {
            var keywords = ActiveLanguage.KeywordSet(lang);
            var set = new HashSet<string>();
            void AddIfKeyword(IEnumerable<string> words) { foreach (var w in words) if (keywords.Contains(w)) set.Add(w); }
            foreach (var w in vanilla ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(w) || w.StartsWith("__")) continue;
                if (KeywordMap.ContainsKey(w))
                {
                    switch (w)
                    {
                        case "True": AddLiteral(set, lang, true); break;
                        case "False": AddLiteral(set, lang, false); break;
                        case "None": AddLiteral(set, lang, null); break;
                        case "def": AddIfKeyword(functionWords); break;
                        case "elif": break;
                        default: if (keywords.Contains(w)) set.Add(w); break;
                    }
                    if (w == "while" && keywords.Contains("loop")) set.Add("loop");
                    if (w == "while" && keywords.Contains("repeat")) set.Add("repeat");
                    if (w == "else" && keywords.Contains("switch")) set.Add("switch");
                    if (w == "else" && keywords.Contains("match")) set.Add("match");
                    continue;
                }
                if (isBuiltin != null && isBuiltin(w))
                {
                    string n = null;
                    try { n = lang?.ApiName(w); } catch { }
                    if (!string.IsNullOrEmpty(n)) set.Add(n);
                    continue;
                }
                set.Add(w);
            }
            AddIfKeyword(entryWords);
            set.Add("main");
            bool vars;
            try { vars = isUnlocked == null || isUnlocked("variables"); } catch { vars = false; }
            if (vars) AddIfKeyword(variableWords);
            var list = set.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static void AddLiteral(HashSet<string> set, ILanguage lang, object value)
        {
            string s = null;
            try { s = lang?.FormatValue(value); } catch { }
            if (!string.IsNullOrEmpty(s) && s.All(c => char.IsLetterOrDigit(c) || c == '_')) set.Add(s);
        }

        /// <summary>True / False / None as the language spells them → the game's word, or null.</summary>
        public static string LiteralToPython(string word, ILanguage lang)
        {
            if (string.IsNullOrEmpty(word) || lang == null) return null;
            try
            {
                if (word == lang.FormatValue(true)) return "True";
                if (word == lang.FormatValue(false)) return "False";
                if (word == lang.FormatValue(null)) return "None";
            }
            catch { }
            return null;
        }

        /// <summary>
        /// A word under the mouse in another language → the game's (Python) word for its tooltip, or null to keep the
        /// word: builtins by ILanguage.FromApiName (also the last part of a qualified name), literals by their value.
        /// </summary>
        public static string HoverWordToPython(string word, ILanguage lang)
        {
            if (string.IsNullOrEmpty(word) || lang == null) return null;
            string lit = LiteralToPython(word, lang);
            if (lit != null) return lit == word ? null : lit;
            string py = SafeFromApi(lang, word);
            if (py != null) return py == word ? null : py;
            int dot = word.LastIndexOfAny(qualifierChars);
            if (dot >= 0)
            {
                string first = word.Substring(0, word.IndexOfAny(qualifierChars));
                if (gameEnums.Contains(first)) return null;
                string last = word.Substring(dot + 1);
                py = SafeFromApi(lang, last);
                if (py != null && py != word) return py;
            }
            return null;
        }

        private static readonly char[] qualifierChars = { '.', ':', '$' };

        /// <summary>
        /// True for a bare word that is a game builtin's Python name but which the language spells differently
        /// (len in a language that calls it size): in that language it is the player's own name, not the builtin.
        /// </summary>
        public static bool IsForeignBuiltinWord(string word, ILanguage lang, Func<string, bool> isBuiltin)
        {
            if (string.IsNullOrEmpty(word) || lang == null || word.IndexOf('.') >= 0) return false;
            if (PythonKeywordWords.Contains(word) && !ActiveLanguage.KeywordSet(lang).Contains(word) && LiteralToPython(word, lang) == null) return true;
            if (isBuiltin == null || !isBuiltin(word)) return false;
            if (SafeFromApi(lang, word) == word) return false;
            string api = null;
            try { api = lang.ApiName(word); } catch { }
            return api != null && api != word;
        }

        private static readonly HashSet<string> PythonKeywordWords = new HashSet<string>
        {
            "def", "elif", "pass", "global", "not", "and", "or", "in", "is", "from", "None", "True", "False",
        };

        private static string SafeFromApi(ILanguage lang, string name)
        {
            try { return lang.FromApiName(name); } catch { return null; }
        }

        public static string SafeToJava(string py)
        {
            try { return GameApi.ToJava(py); } catch { return null; }
        }

        public static string SafeToPython(string java)
        {
            try { return GameApi.ToPython(java); } catch { return null; }
        }
    }
}
