// Public API of the JavaFarmer compiler. See docs/SPEC.md §7-§8.
// The signatures in this file are a contract with JavaFarmer.Plugin: do not change them without updating both sides.
using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    public sealed class CompileOptions
    {
        /// <summary>Window name = class name = Python module name.</summary>
        public string FileName;
        /// <summary>Other windows: name -> Java source (for cross-file symbols). May be null.</summary>
        public IReadOnlyDictionary<string, string> OtherFiles;
        /// <summary>
        /// Other windows: name -> language id ("java", "rust", "python" ...), chosen by each file's extension. May be null; a
        /// name without an entry is in the caller's own language. Resolve files of another language with
        /// ForeignModules.Resolve (docs/LANGUAGE-GUIDE.md "Cross-language imports").
        /// </summary>
        public IReadOnlyDictionary<string, string> OtherFileLanguages;
    }

    public sealed class Diagnostic
    {
        public string Message;
        /// <summary>Java offsets; End is exclusive.</summary>
        public int Start, End;
        public Diagnostic(string message, int start, int end) { Message = message; Start = start; End = end; }
        public override string ToString() => $"{Start}-{End}: {Message}";
    }

    public enum SegmentKind { User, Structural, Synthetic }

    public sealed class Segment
    {
        /// <summary>Python range [PyStart, PyEnd) and Java range [JavaStart, JavaEnd). Java range is (-1,-1) for helper code.</summary>
        public int PyStart, PyEnd, JavaStart, JavaEnd;
        public SegmentKind Kind;
        /// <summary>When non-null, the node classified by this segment checks this unlock instead of its own.</summary>
        public string DependencyOverride;
        /// <summary>
        /// Module scaffolding with no Java statement of its own (the `def main():` line, the `__name__` guard and its
        /// `main()` call, `global` lines): the game must not highlight or blink it while the program runs.
        /// </summary>
        public bool NoHighlight;
    }

    public sealed partial class SourceMap
    {
        public IReadOnlyList<Segment> Segments { get; }
        public SourceMap(IReadOnlyList<Segment> segments) { Segments = segments; }
        /// <summary>Segment containing the Python offset (nearest preceding segment if the offset falls in a gap). Null if none.</summary>
        public Segment Find(int pyOffset) => FindImpl(pyOffset);
    }

    public sealed class CompileResult
    {
        public bool Success;
        public string Python;
        public SourceMap Map;
        public IReadOnlyList<Diagnostic> Errors = new List<Diagnostic>();
        /// <summary>
        /// Python identifier -> Java name for every name the lowering changed: renamed fields, methods and locals
        /// (e.g. "list_j" -> "list", "count_l" -> "count") and lambdas ("__jlambda1" -> "lambda"). Never null on success.
        /// </summary>
        public IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>();
    }

    public static partial class JavaCompiler
    {
        /// <summary>Never throws: internal failures become a Diagnostic.</summary>
        public static CompileResult Compile(string javaSource, CompileOptions options) => CompileImpl(javaSource, options);
    }

    public enum TokenKind { Keyword, Type, Identifier, Number, String, Comment, Operator, Punctuation, Constant, Whitespace, Unknown }

    public static partial class JavaSyntax
    {
        /// <summary>Lexes for colouring. Must never throw, must cover the whole text, must tolerate unfinished code.</summary>
        public static IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source) => HighlightImpl(source);
        public static IReadOnlyCollection<string> Keywords => KeywordsImpl;
    }

    public sealed class ApiFunction
    {
        public string JavaName;       // "canHarvest"
        public string PythonName;     // "can_harvest"
        public string Signature;      // "boolean canHarvest()"
        public string ReturnType;     // "boolean"
    }

    public static partial class GameApi
    {
        public static string ToPython(string javaName) => ToPythonImpl(javaName);
        public static string ToJava(string pythonName) => ToJavaImpl(pythonName);
        public static IReadOnlyList<ApiFunction> Functions => FunctionsImpl;
    }
}
