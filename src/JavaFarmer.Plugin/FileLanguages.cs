using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Every code window has its own language, chosen by its file extension: "main.java", "utils.rs", "bot.py".
    /// Inside the game a window keeps its MODULE name (fileName = "main", the name other files import), and this class
    /// remembers its extension. The window title shows "main.java"; the save folder holds "main.java".
    /// A name typed without an extension gets the extension of the default language (Mods menu) for a new window,
    /// and keeps its current extension when an existing window is renamed.
    /// Module names are unique across extensions (case-insensitive, like the game): "a.rs" and "a.java" cannot coexist.
    /// </summary>
    public static class FileLanguages
    {
        /// <summary>Extensions the plugin recognises (case-insensitive), with their language id. First entry per id = canonical.</summary>
        public static readonly (string Ext, string Language)[] KnownExtensions =
        {
            (".java", "java"), (".py", "python"), (".c", "c"), (".cpp", "cpp"), (".cc", "cpp"), (".cxx", "cpp"),
            (".rs", "rust"), (".js", "js"), (".mjs", "js"), (".zig", "zig"), (".cs", "csharp"), (".R", "r"),
        };

        private static readonly ConcurrentDictionary<string, string> extensions = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Language id of an extension (".rs" → "rust"), or null for an unknown extension. Registered languages' FileExtension first.</summary>
        public static string LanguageIdOfExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return null;
            try
            {
                foreach (var l in Languages.All)
                    if (!string.IsNullOrEmpty(l.FileExtension) && string.Equals(l.FileExtension, ext, StringComparison.OrdinalIgnoreCase)) return l.Id;
            }
            catch { }
            foreach (var (e, id) in KnownExtensions)
                if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) return id;
            return null;
        }

        public static bool IsCodeExtension(string ext) => LanguageIdOfExtension(ext) != null;

        /// <summary>The extension new files of a language get (ILanguage.FileExtension, else the table).</summary>
        public static string ExtensionOfLanguage(string languageId)
        {
            var l = Languages.Get(languageId);
            if (l != null && !string.IsNullOrEmpty(l.FileExtension) && l.FileExtension.StartsWith(".")) return l.FileExtension;
            foreach (var (e, id) in KnownExtensions) if (id == languageId) return e;
            return ".py";
        }

        /// <summary>Extension of the default language (Mods menu): new windows get it.</summary>
        public static string DefaultExtension => ExtensionOfLanguage(ActiveLanguage.Id);

        /// <summary>"Java (.java), Python (.py), ..." for messages.</summary>
        public static string ExtensionList()
        {
            var parts = new List<string>();
            try { foreach (var l in Languages.All) parts.Add(l.DisplayName + " " + ExtensionOfLanguage(l.Id)); } catch { }
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------------ per window (module name → extension)

        /// <summary>The window's extension (".java"); a module without an entry has the default language's extension.</summary>
        public static string ExtensionOf(string module)
        {
            if (!string.IsNullOrEmpty(module) && extensions.TryGetValue(module, out var e)) return e;
            return DefaultExtension;
        }

        public static string ExtensionOf(CodeWindow w) => ExtensionOf(ReferenceEquals(w, null) ? null : w.fileName);

        public static bool HasExtension(string module) => !string.IsNullOrEmpty(module) && extensions.ContainsKey(module);

        /// <summary>Language id of a module ("rust"); may be a language this build does not have.</summary>
        public static string LanguageIdOf(string module) => LanguageIdOfExtension(ExtensionOf(module)) ?? ActiveLanguage.Id;

        /// <summary>The window's language, or null when its extension names a language this build does not have.</summary>
        public static ILanguage LanguageOf(string module) => Languages.Get(LanguageIdOf(module));

        public static ILanguage LanguageOf(CodeWindow w) => LanguageOf(ReferenceEquals(w, null) ? null : w.fileName);

        /// <summary>"main.java"</summary>
        public static string DisplayName(string module) => (module ?? "") + ExtensionOf(module);

        public static void Set(string module, string ext)
        {
            if (string.IsNullOrEmpty(module)) return;
            if (string.IsNullOrEmpty(ext)) extensions.TryRemove(module, out _);
            else extensions[module] = ext;
        }

        public static void Move(string oldModule, string newModule, string ext)
        {
            if (!string.IsNullOrEmpty(oldModule)) extensions.TryRemove(oldModule, out _);
            Set(newModule, ext);
        }

        /// <summary>Forgets every window (a new scene / save is loading).</summary>
        public static void Clear() => extensions.Clear();

        public static IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(extensions, StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ names typed by the player

        /// <summary>
        /// Splits a typed window name: "utils.rs" → ("utils", ".rs"); "utils" → ("utils", null). False with a message for
        /// an unknown extension ("a.txt") or a name the game cannot import (dots, empty).
        /// </summary>
        public static bool TrySplitName(string typed, out string baseName, out string ext, out string error)
        {
            baseName = null; ext = null; error = null;
            typed = (typed ?? "").Trim();
            if (typed.Length == 0) { error = "A file needs a name."; return false; }
            int dot = typed.LastIndexOf('.');
            if (dot >= 0)
            {
                string e = typed.Substring(dot);
                if (!IsCodeExtension(e))
                {
                    error = "\"" + e + "\" is not a code file extension. Use one of: " + ExtensionList() + ".";
                    return false;
                }
                ext = e;
                typed = typed.Substring(0, dot);
            }
            if (typed.Length == 0) { error = "A file needs a name before its extension."; return false; }
            if (typed.IndexOf('.') >= 0) { error = "A file name can have only one dot, before its extension (other files import it by the name before the dot)."; return false; }
            baseName = typed;
            return true;
        }

        // ------------------------------------------------------------------ files on disk

        private static HashSet<string> stubNames;

        /// <summary>Every language's external-editor stub file name (farm.h, JavaFarmerApi.java ...): never code windows.</summary>
        public static HashSet<string> StubFileNames
        {
            get
            {
                var s = stubNames;
                if (s != null) return s;
                s = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "JavaFarmerApi.java" };
                try
                {
                    foreach (var l in Languages.All)
                    {
                        try { l.ApiStubFile(out var n); if (!string.IsNullOrEmpty(n)) s.Add(n); } catch { }
                    }
                }
                catch { }
                return stubNames = s;
            }
        }

        /// <summary>
        /// Build files of the real toolchains that players put next to their code for external editors (ZLS reads
        /// build.zig, Cargo runs build.rs). They have a code extension but are never code windows, and a window cannot
        /// take these names. Other helper files (Cargo.toml, package.json, CMakeLists.txt, build.zig.zon) have no code
        /// extension or more than one dot, so they are ignored anyway.
        /// </summary>
        public static readonly HashSet<string> ToolFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "build.zig", "build.rs" };

        /// <summary>A file the game should load as a code window: known extension, not __builtins__.py, not a stub file, not a build file (build.zig, build.rs).</summary>
        public static bool IsCodeFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string name = Path.GetFileName(path);
            if (string.Equals(name, "__builtins__.py", StringComparison.OrdinalIgnoreCase)) return false;
            if (StubFileNames.Contains(name) || ToolFileNames.Contains(name)) return false;
            string module = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrEmpty(module) || module.IndexOf('.') >= 0) return false;
            return IsCodeExtension(Path.GetExtension(name));
        }

        /// <summary>
        /// The code files of a save folder: module → file name. When two files have the same module name ("a.py" and
        /// "a.java"), the most recently written one wins and the others are returned in duplicates.
        /// </summary>
        public static Dictionary<string, string> ScanSaveDirectory(string dir, out List<string> duplicates)
        {
            duplicates = new List<string>();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return result;
            var byModule = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(dir))
            {
                if (!IsCodeFile(f)) continue;
                string m = Path.GetFileNameWithoutExtension(f);
                if (!byModule.TryGetValue(m, out var l)) byModule[m] = l = new List<string>();
                l.Add(f);
            }
            foreach (var kv in byModule)
            {
                var ordered = kv.Value.OrderByDescending(f => SafeWriteTime(f)).ThenBy(f => f, StringComparer.Ordinal).ToList();
                result[kv.Key] = Path.GetFileName(ordered[0]);
                duplicates.AddRange(ordered.Skip(1));
            }
            return result;
        }

        private static DateTime SafeWriteTime(string f)
        {
            try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; }
        }
    }
}
