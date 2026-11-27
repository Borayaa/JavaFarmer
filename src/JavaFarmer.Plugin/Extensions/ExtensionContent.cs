// Docs pages, strings, unlock display code and item icons of the extensions. Canonical content is Python
// (content/python/docs/mods/<id>/*.md, content/python/Strings/extensions*.txt); other languages get it through
// the DocsTranslator at runtime.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using JavaFarmer.Compiler;
using UnityEngine;

namespace JavaFarmer.Plugin.Extensions
{
    public static class ExtensionContent
    {
        /// <summary>The plugin's content folder (next to the DLL).</summary>
        public static string Root;

        /// <summary>Id of the language docs / tooltips must be shown in. ExtensionBootstrap.CurrentLanguage sets it.</summary>
        public static Func<string> CurrentLanguage = () => SaveSidecar.Language;

        public static string LanguageId
        {
            get
            {
                string id = null;
                try { id = CurrentLanguage?.Invoke(); } catch { }
                return string.IsNullOrEmpty(id) ? "java" : id.ToLowerInvariant();
            }
        }

        static readonly object gate = new object();
        static readonly Dictionary<string, string> docCache = new Dictionary<string, string>();
        static readonly Dictionary<string, Dictionary<string, string>> stringCache = new Dictionary<string, Dictionary<string, string>>();
        static int cacheVersion = -1;
        static readonly HashSet<string> loggedFailures = new HashSet<string>();

        public static string PythonRoot => string.IsNullOrEmpty(Root) ? null : Path.Combine(Root, "python");

        static void CheckCache()
        {
            if (cacheVersion == GameApi.Version) return;
            docCache.Clear();
            stringCache.Clear();
            cacheVersion = GameApi.Version;
        }

        /// <summary>Canonical (Python) text of a docs page, or null.</summary>
        public static string PythonDoc(string doc)
        {
            var root = PythonRoot;
            if (root == null || doc == null) return null;
            string rel = doc.Replace('\\', '/').TrimStart('/');
            if (!rel.StartsWith("docs/mods/", StringComparison.Ordinal) || rel.Contains("..")) return null;
            string full = Path.Combine(root, rel);
            return File.Exists(full) ? File.ReadAllText(full).Replace("\r", "") : null;
        }

        /// <summary>A page in the given language (translated from Python), or null if it does not exist.</summary>
        public static string Doc(string doc, string lang)
        {
            lock (gate)
            {
                CheckCache();
                string key = lang + "|" + doc;
                if (docCache.TryGetValue(key, out var cached)) return cached;
                var py = PythonDoc(doc);
                var text = py == null ? null : Translate(py, lang, doc);
                docCache[key] = text;
                return text;
            }
        }

        /// <summary>Code spans of a markdown text (Python) in the target language; spans that fail stay Python.</summary>
        public static string Translate(string markdown, string lang, string what = null)
        {
            if (string.IsNullOrEmpty(markdown) || string.IsNullOrEmpty(lang) || lang == "python") return markdown;
            ILanguage target;
            try { target = Languages.Get(lang); } catch { target = null; }
            if (target == null || target.IsNative) return markdown;
            try
            {
                var result = DocsTranslator.Translate(markdown, target, out var failures);
                if (failures != null && failures.Count > 0 && what != null && loggedFailures.Add(lang + "|" + what))
                    Log.Warn("extension docs " + what + " (" + lang + "): " + failures.Count + " code span(s) kept in Python: " + string.Join("; ", failures.Take(3)));
                return result;
            }
            catch (Exception e)
            {
                Log.Warn("extension docs translation failed for " + what + ": " + e.Message);
                return markdown;
            }
        }

        /// <summary>The unlock box snippet in the given language.</summary>
        public static string DisplayCode(string python, string lang)
        {
            if (string.IsNullOrEmpty(python)) return python;
            // inside a sentence the span is translated as an expression (alone on a line it would be a statement)
            var t = Translate("box `" + python + "` box", lang);
            int a = t.IndexOf('`'), b = t.LastIndexOf('`');
            return a >= 0 && b > a ? t.Substring(a + 1, b - a - 1) : python;
        }

        // ------------------------------------------------------------------ strings

        /// <summary>Raw (Python) strings of content/python/Strings/extensions*.txt.</summary>
        public static Dictionary<string, string> PythonStrings()
        {
            var d = new Dictionary<string, string>();
            var root = PythonRoot;
            if (root == null) return d;
            string dir = Path.Combine(root, "Strings");
            if (!Directory.Exists(dir)) return d;
            var files = Directory.GetFiles(dir, "extensions*.txt").OrderBy(f => f, StringComparer.Ordinal);
            foreach (var f in files)
                foreach (var kv in ParseStrings(File.ReadAllText(f))) d[kv.Key] = kv.Value;
            return d;
        }

        /// <summary>The game's strings format: an entry starts with "@key = " at the start of a line; values may span lines.</summary>
        public static List<KeyValuePair<string, string>> ParseStrings(string text)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(text)) return result;
            string key = null;
            var value = new StringBuilder();
            void Flush()
            {
                if (key != null) result.Add(new KeyValuePair<string, string>(key, value.ToString().TrimEnd('\n', ' ')));
                key = null;
                value.Clear();
            }
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                if (line.StartsWith("@") && eq > 1 && line.Substring(1, eq - 1).All(c => char.IsLetterOrDigit(c) || c == '_'))
                {
                    Flush();
                    key = line.Substring(1, eq - 1);
                    value.Append(line.Substring(eq + 3));
                }
                else if (key != null) value.Append('\n').Append(line);
            }
            Flush();
            return result;
        }

        /// <summary>Strings translated to a language (cached).</summary>
        public static Dictionary<string, string> Strings(string lang)
        {
            lock (gate)
            {
                CheckCache();
                if (stringCache.TryGetValue(lang, out var d)) return d;
                d = new Dictionary<string, string>();
                foreach (var kv in PythonStrings()) d[kv.Key] = Translate(kv.Value, lang, "strings @" + kv.Key);
                stringCache[lang] = d;
                return d;
            }
        }

        /// <summary>Extension owning a strings key (null = shared key, always merged).</summary>
        public static ContentExtension OwnerOfKey(string key)
        {
            foreach (var ext in ExtensionHost.Extensions)
            {
                foreach (var u in ext.Unlocks) if (key == u.TitleKey || key == u.DescriptionKey) return ext;
                foreach (var i in ext.Items) if (key == i.DescriptionKey) return ext;
                foreach (var b in ext.Builtins) if (key == "code_tooltip_" + b.Name) return ext;
            }
            return null;
        }

        static readonly FieldInfo languageField = AccessTools.Field(typeof(Localizer), "language");

        /// <summary>Puts the strings of enabled extensions into the game's string table and removes those of disabled ones.</summary>
        public static int MergeStrings()
        {
            var dict = languageField?.GetValue(null) as Dictionary<string, string>;
            if (dict == null) return 0;
            int n = 0;
            foreach (var kv in Strings(LanguageId))
            {
                var owner = OwnerOfKey(kv.Key);
                if (owner != null && !ExtensionHost.Enabled(owner)) { dict.Remove(kv.Key); continue; }
                string value;
                try { value = CodeUtilities.ApplyCodeTags(kv.Value); } catch { value = kv.Value; }
                dict[kv.Key] = value;
                n++;
            }
            return n;
        }

        /// <summary>Section appended to docs/home.md while an extension is enabled.</summary>
        public static string HomeSection()
        {
            var sb = new StringBuilder();
            foreach (var ext in ExtensionHost.Extensions)
                if (ExtensionHost.Enabled(ext)) sb.Append('[').Append(ext.DisplayName).Append("](").Append(ext.IndexDocs).Append(")      ");
            if (sb.Length == 0) return "";
            string title = "Mods";
            try { var t = Localizer.Localize("table_of_contents_section_mods"); if (!string.IsNullOrEmpty(t) && t != "table_of_contents_section_mods") title = t; } catch { }
            return "\n\n## " + title + "\n" + sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ icons

        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static MethodInfo loadImage;

        public static Sprite Icon(string itemName)
        {
            if (itemName == null) return null;
            lock (gate)
            {
                if (sprites.TryGetValue(itemName, out var s) && s != null) return s;
                var ext = ExtensionHost.OwnerOfItem(itemName);
                var def = ext?.Items.FirstOrDefault(i => i.Name == itemName);
                if (def == null) return null;
                s = LoadPng(Path.Combine(Root ?? "", "extensions", ext.Id, "icons", def.Icon ?? itemName + ".png"));
                if (s == null)
                {
                    try { s = Resources.Load<Sprite>("ItemTextures/" + def.FallbackIcon); } catch { }
                }
                sprites[itemName] = s;
                return s;
            }
        }

        static Sprite LoadPng(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                if (loadImage == null)
                {
                    var t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                    loadImage = t?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                }
                if (loadImage == null) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!(bool)loadImage.Invoke(null, new object[] { tex, File.ReadAllBytes(path) })) return null;
                tex.filterMode = FilterMode.Bilinear;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e)
            {
                Log.Warn("extension icon " + path + ": " + e.Message);
                return null;
            }
        }
    }
}
