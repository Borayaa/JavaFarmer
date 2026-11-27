using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Code files on disk keep their real extension ("main.java", "utils.rs", "bot.py"). The game hard-codes ".py" in
    /// Saver (research §5.2): WriteCodeFiles is replaced, the ".py" checks of Saver.Load (and of WriteCodeFiles' cleanup)
    /// are redirected to FileLanguages.IsCodeFile by a transpiler, OnFileChanged (external editors) is replaced.
    /// UpdateFromOldSaveGame (pre-1.0 save format) keeps writing ".py": those saves are Python.
    /// </summary>
    public static class SaveFiles
    {
        /// <summary>True while Saver.Load runs (windows opened now take their extension from the disk).</summary>
        public static bool Loading { get; private set; }
        private static string loadingDir;
        private static Dictionary<string, string> diskFiles;
        /// <summary>Messages for the player after a load (migration, duplicate files).</summary>
        public static readonly List<string> LoadNotes = new List<string>();

        // ------------------------------------------------------------------ transpiler: Path.GetExtension(f) == ".py"

        private static readonly MethodInfo getExtension = AccessTools.Method(typeof(Path), nameof(Path.GetExtension), new[] { typeof(string) });

        /// <summary>Replaces Path.GetExtension in "Path.GetExtension(f) == \".py\"": ".py" for every code file of any language.</summary>
        public static string GameExtension(string path)
        {
            try { return FileLanguages.IsCodeFile(path) ? ".py" : ""; }
            catch { return Path.GetExtension(path); }
        }

        /// <summary>Methods of Saver (and its compiler-generated lambdas) that compare Path.GetExtension(..) with ".py".</summary>
        public static List<MethodBase> ExtensionCheckSites()
        {
            var result = new List<MethodBase>();
            var types = new List<Type> { typeof(Saver) };
            types.AddRange(typeof(Saver).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (var t in types)
            {
                foreach (var m in t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    List<CodeInstruction> il;
                    try { il = PatchProcessor.GetOriginalInstructions(m); } catch { continue; }
                    if (FindSites(il).Count > 0) result.Add(m);
                }
            }
            return result;
        }

        private static List<int> FindSites(List<CodeInstruction> il)
        {
            var sites = new List<int>();
            for (int i = 0; i + 1 < il.Count; i++)
                if (il[i].Calls(getExtension) && il[i + 1].opcode == OpCodes.Ldstr && (il[i + 1].operand as string) == ".py") sites.Add(i);
            return sites;
        }

        public static IEnumerable<CodeInstruction> ExtensionCheckTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var il = instructions.ToList();
            var mine = AccessTools.Method(typeof(SaveFiles), nameof(GameExtension));
            foreach (int i in FindSites(il)) il[i].operand = mine;
            return il;
        }

        // ------------------------------------------------------------------ Saver.Load

        /// <summary>Before Saver.Load: sidecar, one-time migration of v1 saves, duplicate module names, extensions on disk.</summary>
        public static void BeforeLoad(string saveDir, DateTime now)
        {
            LoadNotes.Clear();
            FileLanguages.Clear();
            loadingDir = saveDir;
            Loading = true;
            SaveSidecar.Read(saveDir);
            try { LoadNotes.AddRange(SaveMigration.Run(saveDir, now)); }
            catch (Exception e) { Log.Error("save migration failed", e); }
            diskFiles = FileLanguages.ScanSaveDirectory(saveDir, out var duplicates);
            if (duplicates.Count > 0)
            {
                string backup = SaveMigration.NewBackupDir(saveDir, "duplicates", now);
                var moved = new List<string>();
                foreach (var d in duplicates)
                {
                    try { File.Move(d, Path.Combine(backup, Path.GetFileName(d))); moved.Add(Path.GetFileName(d)); }
                    catch (Exception e) { Log.Warn("could not move duplicate " + d + ": " + e.Message); }
                }
                if (moved.Count > 0)
                    LoadNotes.Add("Two files had the same name (other files import a file by its name without the extension). Kept the newest one and moved " +
                        string.Join(", ", moved) + " to " + SaveMigration.BackupFolder + "/" + Path.GetFileName(backup) + ".");
            }
        }

        /// <summary>Extension of a window the game opens now: from the disk while a save loads, else the default language's.</summary>
        public static string ExtensionForNewWindow(string module)
        {
            if (Loading && !string.IsNullOrEmpty(module))
            {
                if (diskFiles == null || !diskFiles.ContainsKey(module))
                    diskFiles = FileLanguages.ScanSaveDirectory(loadingDir, out _); // UpdateFromOldSaveGame wrote files
                if (diskFiles.TryGetValue(module, out var file)) return Path.GetExtension(file);
            }
            return FileLanguages.DefaultExtension;
        }

        public static void AfterLoad()
        {
            Loading = false;
            diskFiles = null;
        }

        // ------------------------------------------------------------------ Saver.WriteCodeFiles

        /// <summary>One open code window: module name, its extension and text.</summary>
        public struct CodeFile
        {
            public string Module, Extension, Text;
            public CodeFile(string module, string ext, string text) { Module = module; Extension = ext; Text = text; }
            public string FileName => Module + Extension;
        }

        /// <summary>
        /// Writes every window as &lt;module&gt;&lt;ext&gt; (save + backup folder, through the game's WriteFilesSafely), then deletes
        /// the code files of windows that are no longer open, or whose extension changed. Stub files, __builtins__.py and
        /// files with unknown extensions are never deleted. Returns the deleted file names.
        /// </summary>
        public static List<string> WriteCodeFiles(IReadOnlyList<CodeFile> files, ICollection<string> openWindowNames, string savePath, string backupPath,
            Action<FileInfo, FileInfo, string> writeSafely)
        {
            foreach (var f in files)
                writeSafely(new FileInfo(Path.Combine(backupPath, f.FileName)), new FileInfo(Path.Combine(savePath, f.FileName)), f.Text ?? "");
            var deleted = new List<string>();
            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) expected[f.Module] = f.FileName;
            var open = new HashSet<string>(openWindowNames ?? (ICollection<string>)Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(savePath))
            {
                if (!FileLanguages.IsCodeFile(path)) continue;
                string name = Path.GetFileName(path), module = Path.GetFileNameWithoutExtension(path);
                bool keep = expected.TryGetValue(module, out var exp) ? string.Equals(exp, name, StringComparison.OrdinalIgnoreCase) : open.Contains(module);
                if (keep) continue;
                try { File.Delete(path); deleted.Add(name); }
                catch (Exception e) { Log.Warn("could not delete " + path + ": " + e.Message); }
            }
            return deleted;
        }

        private static Action<FileInfo, FileInfo, string> writeSafelyGame;

        /// <summary>Prefix replacing Saver.WriteCodeFiles (returns false: the original never runs).</summary>
        public static bool WriteCodeFilesPrefix(Workspace workspace, string savePath, string backupPath)
        {
            try
            {
                if (writeSafelyGame == null)
                {
                    var m = AccessTools.Method(typeof(Saver), "WriteFilesSafely", new[] { typeof(FileInfo), typeof(FileInfo), typeof(string) });
                    writeSafelyGame = (Action<FileInfo, FileInfo, string>)Delegate.CreateDelegate(typeof(Action<FileInfo, FileInfo, string>), m);
                }
                var files = new List<CodeFile>();
                foreach (var cw in workspace.codeWindows.Values)
                    if (!ReferenceEquals(cw, null)) files.Add(new CodeFile(cw.fileName, FileLanguages.ExtensionOf(cw.fileName), cw.CodeInput.text));
                WriteCodeFiles(files, workspace.openWindows.Keys.ToList(), savePath, backupPath, writeSafelyGame);
                var builtins = new FileInfo(Path.Combine(savePath, "__builtins__.py"));
                if (!builtins.Exists)
                {
                    try { File.WriteAllText(builtins.FullName, Localizer.LoadBuiltins()); }
                    catch (Exception e) { Log.Warn("could not write __builtins__.py: " + e.Message); }
                }
                return false;
            }
            catch (Exception e)
            {
                Log.Error("JavaFarmer could not write the code files; the game writes them as .py", e);
                return true;
            }
        }

        // ------------------------------------------------------------------ Saver.OnFileChanged (external editors)

        private static AccessTools.FieldRef<List<(string, string)>> codeToUpdateRef;
        private static AccessTools.FieldRef<Mutex> codeToUpdateMutexRef;

        /// <summary>The module a changed file updates (an open window with that extension), or null.</summary>
        public static string ModuleOfChangedFile(string fileName)
        {
            if (!FileLanguages.IsCodeFile(fileName)) return null;
            string module = Path.GetFileNameWithoutExtension(fileName);
            if (!FileLanguages.HasExtension(module)) return null;
            return string.Equals(FileLanguages.ExtensionOf(module), Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase) ? module : null;
        }

        /// <summary>Prefix replacing Saver.OnFileChanged (file watcher thread).</summary>
        public static bool OnFileChangedPrefix(FileSystemEventArgs e)
        {
            try
            {
                string module = ModuleOfChangedFile(e?.Name);
                if (module == null) return false;
                if (codeToUpdateRef == null)
                {
                    codeToUpdateRef = AccessTools.StaticFieldRefAccess<List<(string, string)>>(AccessTools.Field(typeof(Saver), "codeToUpdate"));
                    codeToUpdateMutexRef = AccessTools.StaticFieldRefAccess<Mutex>(AccessTools.Field(typeof(Saver), "codeToUpdateMutex"));
                }
                string text = File.ReadAllText(e.FullPath);
                var mutex = codeToUpdateMutexRef();
                mutex.WaitOne();
                try { codeToUpdateRef().Add((module, text)); }
                finally { mutex.ReleaseMutex(); }
                return false;
            }
            catch (Exception ex)
            {
                Log.Warn("file watcher: " + ex.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// One-time migration of saves made before per-file languages (javafarmer.json missing, or format 1): their code files
    /// are all "*.py". Files of a JavaFarmer v1 save contain Java: they are renamed to "*.java" (contents never change; the
    /// originals are copied to javafarmer-backup/rename-&lt;time&gt;/ first). A save of the unmodded game stays Python.
    /// </summary>
    public static class SaveMigration
    {
        public const string BackupFolder = "javafarmer-backup";

        public static string NewBackupDir(string saveDir, string kind, DateTime now)
        {
            string stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string dir = Path.Combine(saveDir, BackupFolder, kind + "-" + stamp);
            for (int k = 2; Directory.Exists(dir); k++) dir = Path.Combine(saveDir, BackupFolder, kind + "-" + stamp + "-" + k.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// The language the *.py files of an old save are written in: the format-1 sidecar's language, else Java when at
        /// least one file compiles as Java and does not parse as the game's Python (a JavaFarmer v1 save), else Python.
        /// </summary>
        public static string OldSaveLanguage(IReadOnlyDictionary<string, string> pyFiles, bool sidecarFound, string sidecarLanguage,
            IReadOnlyDictionary<string, (string Text, string Language)> otherCodeFiles = null)
        {
            if (sidecarFound && !string.IsNullOrEmpty(sidecarLanguage)) return sidecarLanguage;
            var java = Languages.Get("java");
            foreach (var kv in pyFiles)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (Compiles(java, kv.Key, kv.Value, pyFiles, otherCodeFiles) && !Compiles(Languages.Get("python"), kv.Key, kv.Value, pyFiles, otherCodeFiles)) return "java";
            }
            return "python";
        }

        /// <summary>
        /// Does the text compile in the language? The other *.py files of the save are tried first as the same language
        /// (a v1 save: all its files are Java), then as Python; files with other extensions keep their own language.
        /// </summary>
        private static bool Compiles(ILanguage lang, string name, string text, IReadOnlyDictionary<string, string> pyFiles,
            IReadOnlyDictionary<string, (string Text, string Language)> otherCodeFiles)
        {
            if (lang == null) return false;
            foreach (var pyAs in new[] { lang.Id, "python" })
            {
                try
                {
                    var others = new Dictionary<string, string>();
                    var langs = new Dictionary<string, string>();
                    foreach (var kv in pyFiles) if (kv.Key != name) { others[kv.Key] = kv.Value; langs[kv.Key] = pyAs; }
                    if (otherCodeFiles != null)
                        foreach (var kv in otherCodeFiles) if (kv.Key != name && !others.ContainsKey(kv.Key)) { others[kv.Key] = kv.Value.Text; langs[kv.Key] = kv.Value.Language; }
                    if (lang.ToIr(text, new CompileOptions { FileName = name, OtherFiles = others, OtherFileLanguages = langs }, out _) != null) return true;
                }
                catch { }
                if (pyAs == "python") break;
            }
            return false;
        }

        /// <summary>Runs the migration once per save (it is marked done in javafarmer.json). Returns notes for the player.</summary>
        public static List<string> Run(string saveDir, DateTime now)
        {
            var notes = new List<string>();
            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir)) return notes;
            if (SaveSidecar.FileFound && SaveSidecar.FileVersion >= 2) return notes;
            var py = new Dictionary<string, string>(StringComparer.Ordinal);
            var otherCode = new Dictionary<string, (string Text, string Language)>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(saveDir))
            {
                if (!FileLanguages.IsCodeFile(f)) continue;
                string module = Path.GetFileNameWithoutExtension(f), fileExt = Path.GetExtension(f);
                string text = File.ReadAllText(f).Replace("\r", "");
                if (fileExt == ".py") py[module] = text;
                else otherCode[module] = (text, FileLanguages.LanguageIdOfExtension(fileExt));
            }
            string lang = py.Count == 0 ? "python" : OldSaveLanguage(py, SaveSidecar.FileFound, SaveSidecar.Language, otherCode);
            var target = Languages.Get(lang);
            string ext = FileLanguages.ExtensionOfLanguage(lang);
            if (py.Count > 0 && lang != "python")
            {
                string backup = null;
                var renamed = new List<string>();
                var python = Languages.Get("python");
                foreach (var kv in py.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    // a file that only works as the game's Python stays Python
                    if (!Compiles(target, kv.Key, kv.Value, py, otherCode) && Compiles(python, kv.Key, kv.Value, py, otherCode) && kv.Value.Trim().Length > 0) continue;
                    string from = Path.Combine(saveDir, kv.Key + ".py"), to = Path.Combine(saveDir, kv.Key + ext);
                    if (File.Exists(to)) { notes.Add(kv.Key + ".py was not renamed: " + kv.Key + ext + " already exists."); continue; }
                    if (backup == null) backup = NewBackupDir(saveDir, "rename", now);
                    File.Copy(from, Path.Combine(backup, kv.Key + ".py"), true);
                    File.Move(from, to);
                    renamed.Add(kv.Key + ext);
                }
                if (renamed.Count > 0)
                {
                    notes.Add("This save was made before JavaFarmer gave every file its own language. Its " + (target?.DisplayName ?? lang) +
                        " files now have the " + ext + " extension: " + string.Join(", ", renamed) + ". Their code did not change. " +
                        "The old .py files are in " + BackupFolder + "/" + Path.GetFileName(backup) + ".");
                    Log.Info("migrated " + saveDir + ": " + string.Join(", ", renamed));
                }
            }
            // mark the save as migrated right away (the game may not save before it quits)
            SaveSidecar.FileVersion = SaveSidecar.FormatVersion;
            SaveSidecar.Language = ActiveLanguage.Id;   // format 2: the default language when written (was the v1 language)
            SaveSidecar.Write(false, saveDir);
            return notes;
        }
    }
}
