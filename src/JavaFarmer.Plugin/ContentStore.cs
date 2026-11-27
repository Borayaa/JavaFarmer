using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Docs, strings and unlock-box code shipped in the "content" folder next to the plugin DLL, per language:
    ///   content/&lt;lang&gt;/docs/...                (same relative paths as Languages/EN/docs)
    ///   content/&lt;lang&gt;/Strings/*.txt           (@key = value, only changed or new keys)
    ///   content/&lt;lang&gt;/unlock_display_code.txt (@unlockName, then the snippet)
    /// content/python/ holds the canonical (Python) texts of extensions (docs/mods/..., Strings/extensions.txt ...).
    ///
    /// Resolution chain for language L (docs/SPEC-v2.md §5):
    ///   docs:    content/L/docs/p → content/python/docs/p translated to L (DocsTranslator) → the game's file
    ///            (translated to L at runtime, see TranslateGameDoc).
    ///   strings: content/python/Strings (code spans translated to L) overlaid by content/L/Strings.
    ///   unlock display code: content/L/unlock_display_code.txt → the canonical Python snippet printed in L.
    /// Python (native) uses content/python only, untranslated. Everything is optional; translations are cached
    /// per language + path.
    /// </summary>
    public sealed class ContentStore
    {
        public const string Canonical = "python";

        public string Root { get; }

        /// <summary>Code-span translator (markdown with Python code spans → language). Tests may replace it.</summary>
        public Func<string, ILanguage, string> Translator = DefaultTranslate;

        /// <summary>Language lookup by id (Languages.Get). Tests may replace it.</summary>
        public Func<string, ILanguage> LanguageResolver = SafeGet;

        private readonly Dictionary<string, bool> printable = new Dictionary<string, bool>();

        /// <summary>
        /// True when the language's printer turns a small Python program into real code (not only comments). A language
        /// whose printer is not written yet keeps the Python text of docs, strings and unlock boxes instead of showing
        /// placeholder text. Probed once per language.
        /// </summary>
        public bool CanPrint(ILanguage lang)
        {
            if (lang == null) return false;
            lock (gate)
            {
                if (printable.TryGetValue(lang.Id, out var known)) return known;
            }
            bool ok;
            try
            {
                var py = LanguageResolver(Canonical);
                var ir = py?.ToIr("while True:\n\tharvest()\n", new CompileOptions { FileName = "Probe" }, out _);
                if (ir == null) ok = true; // cannot probe: trust the language
                else
                {
                    string t = lang.Print(ir, new PrintOptions { Snippet = true, FileName = "Probe" });
                    ok = !string.IsNullOrWhiteSpace(t) && t.IndexOf("arvest", StringComparison.OrdinalIgnoreCase) >= 0 && !OnlyComments(t, lang);
                }
            }
            catch { ok = false; }
            if (!ok) Log.Warn("the " + lang.DisplayName + " printer cannot print code yet: docs and tooltips stay in Python for " + lang.Id + ".");
            lock (gate) printable[lang.Id] = ok;
            return ok;
        }

        private readonly object gate = new object();
        private readonly Dictionary<string, Dictionary<string, string>> rawStrings = new Dictionary<string, Dictionary<string, string>>();
        private readonly Dictionary<string, Dictionary<string, string>> mergedStrings = new Dictionary<string, Dictionary<string, string>>();
        private readonly Dictionary<string, Dictionary<string, string>> displayCode = new Dictionary<string, Dictionary<string, string>>();
        private readonly Dictionary<string, string> translated = new Dictionary<string, string>();

        public ContentStore(string root)
        {
            Root = root;
        }

        public bool Exists => !string.IsNullOrEmpty(Root) && Directory.Exists(Root);

        private static readonly Regex langId = new Regex("^[a-z][a-z0-9_+#-]*$");

        public static bool IsValidLanguageId(string lang) => lang != null && langId.IsMatch(lang);

        /// <summary>content/&lt;lang&gt; (null for an invalid id).</summary>
        public string LanguageDir(string lang) => Exists && IsValidLanguageId(lang) ? Path.Combine(Root, lang) : null;

        // ------------------------------------------------------------------ docs

        /// <summary>v1 behaviour: the Java docs.</summary>
        public bool TryGetDoc(string doc, out string text) => TryGetDoc(doc, "java", out text);

        /// <summary>
        /// doc is the game's path relative to Languages/&amp;lt;lang&amp;gt;, e.g. "docs/home.md". Steps 1 and 2 of the chain:
        /// content/&lt;lang&gt;/&lt;doc&gt;, else content/python/&lt;doc&gt; translated to the language. False: use the game's file.
        /// </summary>
        public bool TryGetDoc(string doc, string lang, out string text)
        {
            text = null;
            if (!Exists || string.IsNullOrEmpty(doc) || !IsValidLanguageId(lang)) return false;
            if (ReadInside(lang, doc, out text)) return true;
            if (lang == Canonical) return false;
            if (!ReadInside(Canonical, doc, out var py)) return false;
            text = Translated("doc\0" + lang + "\0" + doc, py, lang);
            return true;
        }

        /// <summary>
        /// Step 3: the game's own page (Python code spans) translated to the language, cached per language + path.
        /// Python and unknown languages get the text unchanged.
        /// </summary>
        public string TranslateGameDoc(string doc, string gameText, string lang)
        {
            if (string.IsNullOrEmpty(gameText) || lang == null || lang == Canonical) return gameText;
            return Translated("game\0" + lang + "\0" + doc, gameText, lang);
        }

        private string Translated(string key, string pythonText, string lang)
        {
            string cacheKey = key + "\0" + pythonText.Length + "\0" + pythonText.GetHashCode();
            lock (gate)
            {
                if (translated.TryGetValue(cacheKey, out var hit)) return hit;
            }
            string result = pythonText;
            var target = LanguageResolver(lang);
            if (target != null && !target.IsNative && CanPrint(target))
            {
                try { result = Translator(pythonText, target) ?? pythonText; }
                catch (Exception e) { Log.Warn("could not translate " + key.Replace('\0', ' ') + " to " + lang + ": " + e.Message); }
            }
            lock (gate) translated[cacheKey] = result;
            return result;
        }

        /// <summary>Reads Root/&lt;lang&gt;/&lt;rel&gt; when it exists and stays inside Root/&lt;lang&gt;.</summary>
        private bool ReadInside(string lang, string rel, out string text)
        {
            text = null;
            try
            {
                string dir = LanguageDir(lang);
                if (dir == null) return false;
                rel = rel.Replace('\\', '/').TrimStart('/');
                string full = Path.GetFullPath(Path.Combine(dir, rel));
                string dirFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(dirFull, StringComparison.Ordinal)) return false;
                if (!File.Exists(full)) return false;
                text = File.ReadAllText(full).Replace("\r", "");
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("could not read content " + lang + "/" + rel + ": " + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ strings

        /// <summary>v1 behaviour: the Java string overrides.</summary>
        public IReadOnlyDictionary<string, string> Strings => StringsFor("java");

        /// <summary>The string files of one language folder only (content/&lt;lang&gt;/Strings/*.txt), untranslated.</summary>
        public IReadOnlyDictionary<string, string> RawStrings(string lang)
        {
            lock (gate)
            {
                if (rawStrings.TryGetValue(lang ?? "", out var d)) return d;
                d = LoadStrings(LanguageDir(lang));
                rawStrings[lang ?? ""] = d;
                return d;
            }
        }

        /// <summary>
        /// The string overrides for a language: content/python/Strings (canonical, code spans translated to the language)
        /// overlaid by content/&lt;lang&gt;/Strings. Python: content/python/Strings as they are.
        /// </summary>
        public IReadOnlyDictionary<string, string> StringsFor(string lang)
        {
            lock (gate)
            {
                if (mergedStrings.TryGetValue(lang ?? "", out var cached)) return cached;
            }
            var result = new Dictionary<string, string>();
            if (IsValidLanguageId(lang))
            {
                var own = RawStrings(lang);
                if (lang != Canonical)
                {
                    foreach (var kv in RawStrings(Canonical))
                        if (!own.ContainsKey(kv.Key)) result[kv.Key] = Translated("str\0" + lang + "\0" + kv.Key, kv.Value, lang);
                }
                foreach (var kv in own) result[kv.Key] = kv.Value;
            }
            lock (gate) mergedStrings[lang ?? ""] = result;
            return result;
        }

        /// <summary>
        /// A game string (from Languages/&lt;UI lang&gt;/Strings) that has no override for the language: its code spans
        /// translated (used for code_tooltip_* keys of languages without their own tooltips). Cached.
        /// </summary>
        public string TranslateGameString(string key, string value, string lang)
        {
            if (string.IsNullOrEmpty(value) || lang == null || lang == Canonical || value.IndexOf('`') < 0) return value;
            return Translated("gamestr\0" + lang + "\0" + key, value, lang);
        }

        private static Dictionary<string, string> LoadStrings(string langDir)
        {
            var d = new Dictionary<string, string>();
            if (langDir == null) return d;
            string dir = Path.Combine(langDir, "Strings");
            if (!Directory.Exists(dir)) return d;
            var files = new List<string>(Directory.GetFiles(dir, "*.txt"));
            files.Sort(StringComparer.Ordinal);
            foreach (var f in files)
            {
                try { foreach (var kv in ParseKeyValueFile(File.ReadAllText(f))) d[kv.Key] = kv.Value; }
                catch (Exception e) { Log.Warn("could not read " + f + ": " + e.Message); }
            }
            return d;
        }

        // ------------------------------------------------------------------ unlock display code

        /// <summary>v1 behaviour: the Java unlock snippets.</summary>
        public IReadOnlyDictionary<string, string> DisplayCode => DisplayCodeFor("java");

        /// <summary>content/&lt;lang&gt;/unlock_display_code.txt (unlock name → snippet, case-insensitive).</summary>
        public IReadOnlyDictionary<string, string> DisplayCodeFor(string lang)
        {
            lock (gate)
            {
                if (displayCode.TryGetValue(lang ?? "", out var d)) return d;
                d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string dir = LanguageDir(lang);
                string path = dir == null ? null : Path.Combine(dir, "unlock_display_code.txt");
                if (path != null && File.Exists(path))
                {
                    try { foreach (var kv in ParseBlockFile(File.ReadAllText(path))) d[kv.Key] = kv.Value; }
                    catch (Exception e) { Log.Warn("could not read " + path + ": " + e.Message); }
                }
                displayCode[lang ?? ""] = d;
                return d;
            }
        }

        /// <summary>
        /// The code an unlock box shows in the language: content/&lt;lang&gt;/unlock_display_code.txt, else the canonical
        /// Python snippet (UnlockSO.displayCode) printed in the language, else the snippet unchanged.
        /// </summary>
        public string ResolveDisplayCode(string unlockName, string pythonSnippet, string lang)
        {
            if (unlockName != null && DisplayCodeFor(lang).TryGetValue(unlockName, out var own)) return own;
            if (string.IsNullOrEmpty(pythonSnippet) || lang == null || lang == Canonical) return pythonSnippet;
            var target = LanguageResolver(lang);
            if (target == null || target.IsNative || !CanPrint(target)) return pythonSnippet;
            string key = "unlock\0" + lang + "\0" + (unlockName ?? "") + "\0" + pythonSnippet;
            lock (gate)
            {
                if (translated.TryGetValue(key, out var hit)) return hit;
            }
            string result = TranslateSnippet(pythonSnippet, target, Translator) ?? pythonSnippet;
            lock (gate) translated[key] = result;
            return result;
        }

        /// <summary>A bare Python snippet (no markdown) in another language, through the code-span translator.</summary>
        public static string TranslateSnippet(string python, ILanguage target, Func<string, ILanguage, string> translator = null)
        {
            if (string.IsNullOrEmpty(python) || target == null || target.IsNative) return python;
            if (python.IndexOf('`') >= 0) return python;
            try
            {
                // a one-line snippet is translated as an inline span (an expression, no statement terminator), like the
                // hand-written unlock_display_code.txt files; several lines as a code block
                bool inline = python.IndexOf('\n') < 0;
                string pre = inline ? "x `" : "`", post = inline ? "` x" : "`";
                string t = (translator ?? DefaultTranslate)(pre + python + post, target);
                if (t == null || t.Length < pre.Length + post.Length || !t.StartsWith(pre, StringComparison.Ordinal) || !t.EndsWith(post, StringComparison.Ordinal)) return python;
                return t.Substring(pre.Length, t.Length - pre.Length - post.Length);
            }
            catch { return python; }
        }

        /// <summary>True when the text has nothing but comments and whitespace (by the language's own highlighter).</summary>
        public static bool OnlyComments(string src, ILanguage lang)
        {
            try
            {
                foreach (var t in lang.Highlight(src) ?? Array.Empty<(int, int, TokenKind)>())
                    if (t.Kind != TokenKind.Comment && t.Kind != TokenKind.Whitespace && t.Start < src.Length && src.Substring(t.Start, Math.Min(t.Length, src.Length - t.Start)).Trim().Length > 0)
                        return false;
                return true;
            }
            catch { return false; }
        }

        public static string DefaultTranslate(string markdown, ILanguage target)
        {
            if (target == null || target.IsNative) return markdown;
            return DocsTranslator.Translate(markdown, target, out _, null);
        }

        private static ILanguage SafeGet(string lang)
        {
            try { return Languages.Get(lang); } catch { return null; }
        }

        /// <summary>Forgets every cached file and translation (after content or the API changed).</summary>
        public void ClearCache()
        {
            lock (gate)
            {
                rawStrings.Clear();
                mergedStrings.Clear();
                displayCode.Clear();
                translated.Clear();
                printable.Clear();
            }
        }

        // ------------------------------------------------------------------ file formats

        /// <summary>
        /// The game's strings format (Localizer.LoadLang): entries start with '@' at the beginning of a line,
        /// "key = value", values may span lines.
        /// </summary>
        public static List<KeyValuePair<string, string>> ParseKeyValueFile(string text)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(text)) return result;
            text = text.Replace("\r\n", "\n");
            string[] parts = text.Split(new[] { "\n\n@", "\n@" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (i == 0)
                {
                    // text before the first entry (or the first entry itself)
                    int at = part.IndexOf('@');
                    if (at < 0 || part.Substring(0, at).Trim().Length > 0) continue;
                    part = part.Substring(at + 1);
                }
                string[] kv = part.Split(new[] { " = " }, 2, StringSplitOptions.None);
                if (kv.Length != 2) { Log.Warn("malformed content entry: " + Short(part)); continue; }
                string key = kv[0].Trim();
                if (key.Length == 0) continue;
                result.Add(new KeyValuePair<string, string>(key, kv[1].TrimEnd('\n')));
            }
            return result;
        }

        /// <summary>
        /// unlock_display_code.txt: a line "@name" (name = letters, digits, '_') starts a block, the following lines (until
        /// the next such line) are the value. "@name = value" on one line is accepted too. Any other line is code, also
        /// when it starts with '@' (Zig: "@import(\"std\")"); a code line that would look like a header ("@import" alone)
        /// is written with "@@" ("@@import"), which stands for one '@'.
        /// </summary>
        public static List<KeyValuePair<string, string>> ParseBlockFile(string text)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(text)) return result;
            string key = null;
            var value = new System.Text.StringBuilder();
            void Flush()
            {
                if (!string.IsNullOrEmpty(key))
                    result.Add(new KeyValuePair<string, string>(key, value.ToString().Trim('\n')));
                key = null;
                value.Clear();
            }
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (IsBlockHeader(line, out var name, out var inline))
                {
                    Flush();
                    key = name;
                    if (inline != null) value.Append(inline);
                }
                else if (key != null)
                {
                    if (value.Length > 0) value.Append('\n');
                    value.Append(line.StartsWith("@@") ? line.Substring(1) : line);
                }
            }
            Flush();
            return result;
        }

        /// <summary>"@name" or "@name = value" (name: letters, digits, '_'; trailing spaces allowed).</summary>
        private static bool IsBlockHeader(string line, out string name, out string inline)
        {
            name = null; inline = null;
            if (line.Length < 2 || line[0] != '@') return false;
            int i = 1;
            while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
            if (i == 1) return false;
            string rest = line.Substring(i);
            if (rest.Trim().Length == 0) { name = line.Substring(1, i - 1); return true; }
            if (!rest.StartsWith(" = ", StringComparison.Ordinal)) return false;
            name = line.Substring(1, i - 1);
            inline = rest.Substring(3);
            return true;
        }

        private static string Short(string s) => s.Length > 60 ? s.Substring(0, 60) + "..." : s;
    }
}
