using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// One programming language the player can pick. Implementations live in Languages/&lt;Name&gt;/ and are discovered by
    /// Languages (public, non-abstract, parameterless constructor). See docs/LANGUAGE-GUIDE.md.
    /// Every method must be safe to call from the game thread and must never throw.
    /// </summary>
    public interface ILanguage
    {
        /// <summary>"java","python","c","cpp","rust","js","zig","csharp","r"</summary>
        string Id { get; }
        /// <summary>"Java","Python","C","C++","Rust","JavaScript","Zig","C#","R"</summary>
        string DisplayName { get; }
        /// <summary>".java" ... (external editor files only; saves keep .py)</summary>
        string FileExtension { get; }
        /// <summary>True only for Python: the plugin bypasses compilation.</summary>
        bool IsNative { get; }

        /// <summary>Source → game Python + SourceMap. Never throws (internal errors become a Diagnostic).</summary>
        CompileResult Compile(string source, CompileOptions options);
        /// <summary>Source → IR (frontend only). Null plus errors when the source does not compile. Never throws.</summary>
        IrModule ToIr(string source, CompileOptions options, out IReadOnlyList<Diagnostic> errors);
        /// <summary>IR → idiomatic source of this language. The result must compile again with Compile(). Never throws.</summary>
        string Print(IrModule module, PrintOptions options);
        /// <summary>Lexes for colouring. Must never throw, must cover the whole text, must tolerate unfinished code.</summary>
        IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source);
        IReadOnlyCollection<string> Keywords { get; }
        EditorRules Editor { get; }
        /// <summary>Text of a new, empty code window; caret = offset where the cursor goes.</summary>
        string NewFileTemplate(string fileName, out int caret);
        /// <summary>"can_harvest" → this language's name for the game builtin ("canHarvest", "CanHarvest", ...); null if unknown.</summary>
        string ApiName(string pythonName);
        /// <summary>Inverse of ApiName; null if unknown.</summary>
        string FromApiName(string name);
        /// <summary>How print / hover show a game value (see GameValues for the neutral value model).</summary>
        string FormatValue(object gameValue);
        /// <summary>API reference / stub file for external editors (farm.h, JavaFarmerApi.java, farm.rs ...).</summary>
        string ApiStubFile(out string fileName);
    }

    /// <summary>Editor behaviour: auto-indent, dedent, comment toggling.</summary>
    public sealed class EditorRules
    {
        /// <summary>A line ending with one of these (ignoring trailing spaces/comments) indents the next line.</summary>
        public string[] IndentAfter = { "{" };
        /// <summary>Typing one of these as the first non-blank character of a line dedents it.</summary>
        public string[] DedentOn = { "}" };
        public string LineComment = "//";
        public string BlockCommentStart = "/*", BlockCommentEnd = "*/";
        /// <summary>Indentation unit inserted by the editor ("\t" like the game).</summary>
        public string IndentUnit = "\t";
        /// <summary>Bracket pairs that matter for auto-indent ( "{}" "()" "[]" ).</summary>
        public string[] Brackets = { "{}", "()", "[]" };
    }

    public sealed class PrintOptions
    {
        /// <summary>Indentation unit ("\t" by default, like the game editor).</summary>
        public string Indent = "\t";
        public string Newline = "\n";
        /// <summary>Window / module name (Java class name, R file ...).</summary>
        public string FileName = "main";
        /// <summary>
        /// Docs snippet mode: print statements bare, without the mandatory entry scaffolding (main function, includes),
        /// the way lesson snippets show them. Functions/globals are still printed as declarations.
        /// </summary>
        public bool Snippet;
        /// <summary>Print comments (Leading/Trailing). Default true.</summary>
        public bool Comments = true;
        /// <summary>Other modules' names (for qualified calls / imports).</summary>
        public IReadOnlyCollection<string> OtherFiles;
    }

    /// <summary>Registry of all languages (discovered by reflection, ordered by SPEC-v2 order).</summary>
    public static class Languages
    {
        /// <summary>Display order. Unlisted ids go last, alphabetically.</summary>
        public static readonly string[] Order = { "java", "python", "c", "cpp", "rust", "js", "zig", "csharp", "r" };

        static IReadOnlyList<ILanguage> all;
        static readonly object gate = new object();
        static readonly List<ILanguage> registered = new List<ILanguage>();

        public static IReadOnlyList<ILanguage> All
        {
            get
            {
                var a = all;
                if (a != null) return a;
                lock (gate)
                {
                    if (all != null) return all;
                    var list = new List<ILanguage>();
                    Type[] types;
                    try { types = typeof(Languages).Assembly.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (var t in types)
                    {
                        if (t.IsAbstract || t.IsInterface || !t.IsPublic || !typeof(ILanguage).IsAssignableFrom(t)) continue;
                        if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                        try { list.Add((ILanguage)Activator.CreateInstance(t)); } catch { }
                    }
                    list.AddRange(registered);
                    var dedup = new List<ILanguage>();
                    foreach (var l in list) if (l.Id != null && !dedup.Any(d => d.Id == l.Id)) dedup.Add(l);
                    all = dedup.OrderBy(l => { int i = Array.IndexOf(Order, l.Id); return i < 0 ? 1000 : i; })
                               .ThenBy(l => l.Id, StringComparer.Ordinal).ToList();
                    return all;
                }
            }
        }

        /// <summary>Language by id (case-insensitive); null if unknown.</summary>
        public static ILanguage Get(string id)
        {
            if (id == null) return null;
            foreach (var l in All) if (string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        /// <summary>Adds a language from outside this assembly (tests, plugins). Replaces one with the same id.</summary>
        public static void Register(ILanguage lang)
        {
            if (lang == null) return;
            lock (gate)
            {
                registered.RemoveAll(l => l.Id == lang.Id);
                registered.Add(lang);
                all = null;
            }
        }
    }
}
