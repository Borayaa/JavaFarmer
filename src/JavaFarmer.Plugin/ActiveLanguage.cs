using System;
using System.Collections.Generic;
using System.Linq;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// The DEFAULT programming language (docs/SPEC-v2.md §5): BepInEx config General.Language, default "java", changed in the
    /// Mods menu. Every code file has its own language, chosen by its extension (FileLanguages); this default is the language
    /// of new files and of everything that belongs to no file: docs, string tooltips, unlock boxes, the docs index.
    /// Changing it never touches existing files.
    /// </summary>
    public static class ActiveLanguage
    {
        public const string DefaultId = "java";
        public const string Section = "General";
        public const string Key = "Language";

        private static Action<string> persist;
        private static string id = DefaultId;
        private static ILanguage current;

        /// <summary>Raised after the language changed: (old id, new id).</summary>
        public static event Action<string, string> Changed;

        /// <summary>Id of the active language ("java", "python", "rust", ...). Always a registered language.</summary>
        public static string Id => id;

        /// <summary>The active language (never null while Java is registered).</summary>
        public static ILanguage Current => current ?? (current = Resolve(id));

        /// <summary>True for Python: the game compiles the code itself.</summary>
        public static bool IsNative => Current?.IsNative == true;

        public static bool IsJava => string.Equals(Current?.Id, "java", StringComparison.Ordinal);

        /// <summary>Registered language for an id, falling back to Java (then to the first registered language).</summary>
        public static ILanguage Resolve(string languageId)
        {
            ILanguage l = null;
            try { l = Languages.Get(languageId) ?? Languages.Get(DefaultId); } catch (Exception e) { Log.Error("language registry failed", e); }
            if (l == null) { try { l = Languages.All.FirstOrDefault(); } catch { } }
            return l;
        }

        /// <summary>Description of the config entry General.Language.</summary>
        public static string ConfigDescription()
        {
            string ids = "";
            try { ids = string.Join(", ", Languages.All.Select(l => l.Id)); } catch { }
            return "Default programming language (" + ids + "): the language of new code files and of the docs. Every file keeps the language of " +
                "its extension (main.java, utils.rs, bot.py). Change it in the game's Mods menu.";
        }

        /// <summary>
        /// Starts with the configured id (unknown ids fall back to Java with a warning, without changing the config);
        /// save is called with the new id whenever Set changes the language. (The plugin binds the BepInEx config entry,
        /// so this class has no BepInEx types and is unit testable.)
        /// </summary>
        public static void Initialize(string configured, Action<string> save)
        {
            persist = save;
            Use(configured, false);
        }

        /// <summary>Changes the active language (and the config value when bound). Returns false for an unknown id.</summary>
        public static bool Set(string languageId) => Use(languageId, true);

        private static bool Use(string languageId, bool persist)
        {
            var lang = Languages.Get((languageId ?? "").Trim());
            if (lang == null)
            {
                Log.Warn("unknown programming language '" + languageId + "' in the config: using " + DefaultId + ".");
                lang = Resolve(DefaultId);
                if (persist || lang == null) return false;
            }
            string old = id;
            id = lang.Id;
            current = lang;
            if (persist && ActiveLanguage.persist != null) { try { ActiveLanguage.persist(lang.Id); } catch (Exception e) { Log.Error("could not save the language setting", e); } }
            if (old != id)
            {
                try { Changed?.Invoke(old, id); } catch (Exception e) { Log.Error("ActiveLanguage.Changed handler failed", e); }
            }
            return true;
        }

        /// <summary>For tests: switch without a config file.</summary>
        public static void SetForTests(string languageId) => Use(languageId, false);

        private static readonly Dictionary<string, HashSet<string>> keywordSets = new Dictionary<string, HashSet<string>>();

        /// <summary>The language's keywords as a set (cached).</summary>
        public static HashSet<string> KeywordSet(ILanguage lang)
        {
            if (lang == null) return new HashSet<string>();
            lock (keywordSets)
            {
                if (!keywordSets.TryGetValue(lang.Id, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    try { foreach (var k in lang.Keywords ?? Array.Empty<string>()) if (k != null) set.Add(k); } catch { }
                    keywordSets[lang.Id] = set;
                }
                return set;
            }
        }

        private static readonly Dictionary<string, Dictionary<string, TokenKind?>> wordKinds = new Dictionary<string, Dictionary<string, TokenKind?>>();

        /// <summary>
        /// How the language colours a lone word: Keyword for its keywords, else its highlighter's kind when the word is one
        /// token (Rust: Vec, i32, String, Option are Type; Some, None are Constant). Null for an empty word. Cached.
        /// </summary>
        public static TokenKind? WordKind(ILanguage lang, string word)
        {
            if (lang == null || string.IsNullOrEmpty(word)) return null;
            if (KeywordSet(lang).Contains(word)) return TokenKind.Keyword;
            lock (wordKinds)
            {
                if (!wordKinds.TryGetValue(lang.Id, out var cache)) wordKinds[lang.Id] = cache = new Dictionary<string, TokenKind?>(StringComparer.Ordinal);
                if (cache.TryGetValue(word, out var known)) return known;
                TokenKind? r = null;
                try
                {
                    var toks = lang.Highlight(word);
                    if (toks != null && toks.Count == 1 && toks[0].Start == 0 && toks[0].Length == word.Length) r = toks[0].Kind;
                }
                catch { }
                if (cache.Count < 4096) cache[word] = r;
                return r;
            }
        }
    }
}
