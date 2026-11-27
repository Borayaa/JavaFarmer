using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// External-editor API files (ILanguage.ApiStubFile: JavaFarmerApi.java, farm.h, farm.rs, Farm.cs ...), written next to
    /// __builtins__.py for every language that has code files in the save. Stubs of the other languages are removed.
    /// Python players use the game's own __builtins__.py. Stub names are never loaded as code windows (FileLanguages).
    /// </summary>
    public static class ApiStubs
    {
        public static bool IsSafeName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && name != "." && name != ".."
            && !name.EndsWith(".py", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        /// <summary>Writes the stubs of the given languages (unless unchanged) and deletes the others. Returns the files written.</summary>
        public static List<string> Write(string saveDir, IEnumerable<ILanguage> languages)
        {
            var written = new List<string>();
            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir)) return written;
            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lang in (languages ?? Enumerable.Empty<ILanguage>()).Where(l => l != null && !l.IsNative).GroupBy(l => l.Id).Select(g => g.First()))
            {
                string name = null, content = null;
                try { content = lang.ApiStubFile(out name); } catch (Exception e) { Log.Warn("ApiStubFile of " + lang.Id + " failed: " + e.Message); }
                if (IsSafeName(name) && content != null) wanted[name] = content;
            }
            foreach (var stub in FileLanguages.StubFileNames)
            {
                if (wanted.ContainsKey(stub) || !IsSafeName(stub)) continue;
                string p = Path.Combine(saveDir, stub);
                try { if (File.Exists(p)) File.Delete(p); } catch (Exception e) { Log.Warn("could not delete stale " + stub + ": " + e.Message); }
            }
            foreach (var kv in wanted)
            {
                string path = Path.Combine(saveDir, kv.Key);
                if (!File.Exists(path) || File.ReadAllText(path) != kv.Value) File.WriteAllText(path, kv.Value);
                written.Add(path);
            }
            return written;
        }

        /// <summary>v1 overload: the stub of one language.</summary>
        public static string Write(string saveDir, ILanguage lang) => Write(saveDir, new[] { lang }).FirstOrDefault();
    }
}
