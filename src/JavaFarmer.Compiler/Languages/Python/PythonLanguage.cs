using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// The game's own Python dialect as a JavaFarmer language. Native: Compile is the identity (the game parses the
    /// text itself). ToIr parses the dialect (PyParser → PyLowering → IrTypeInference → fix-ups → IrDeclarations) for
    /// docs translation and code conversion; Print renders any IR as game Python (PythonPrinter).
    /// </summary>
    public sealed class PythonLanguage : LanguageBase
    {
        public const string StubFileName = "javafarmer_api.py";

        public override string Id => "python";
        public override string DisplayName => "Python";
        public override string FileExtension => ".py";
        public override bool IsNative => true;
        protected override NamingStyle ApiNaming => NamingStyle.Snake;

        static readonly EditorRules rules = new EditorRules
        {
            IndentAfter = new[] { ":" },
            DedentOn = new string[0],
            LineComment = "#",
            BlockCommentStart = null,
            BlockCommentEnd = null,
            IndentUnit = "\t",
            Brackets = new[] { "{}", "()", "[]" },
        };
        public override EditorRules Editor => rules;

        /// <summary>Identity: the game compiles its own language. One User segment covers the text.</summary>
        public override CompileResult Compile(string source, CompileOptions options)
        {
            source = source ?? "";
            var segs = new List<Segment>();
            if (source.Length > 0)
                segs.Add(new Segment { PyStart = 0, PyEnd = source.Length, JavaStart = 0, JavaEnd = source.Length, Kind = SegmentKind.User });
            return new CompileResult { Success = true, Python = source, Map = new SourceMap(segs), Errors = new List<Diagnostic>(), Names = new Dictionary<string, string>() };
        }

        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors) =>
            PythonFrontend.Build(source, options?.FileName, errors, options);

        protected override string PrintImpl(IrModule module, PrintOptions options) => PythonPrinter.Print(module, options);

        // ------------------------------------------------------------------ editor services
        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            try { return PythonHighlighter.Highlight(source ?? ""); }
            catch { return string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, TokenKind.Unknown) }; }
        }

        static readonly IReadOnlyCollection<string> keywords = PyLexer.Keywords.OrderBy(k => k, StringComparer.Ordinal).ToList();
        public override IReadOnlyCollection<string> Keywords => keywords;

        public override string NewFileTemplate(string fileName, out int caret) { caret = 0; return ""; }

        /// <summary>The game's own print format (CodeUtilities.ToNiceString): [1,2] {1,2} {k:v} (1,2), nested strings in '…'.</summary>
        protected override ValueFormat CreateValueFormat() => new PyValueFormat();

        sealed class PyValueFormat : ValueFormat
        {
            public PyValueFormat()
            {
                NullText = "None"; TrueText = "True"; FalseText = "False";
                ListOpen = "["; ListClose = "]"; SetOpen = "{"; SetClose = "}"; MapOpen = "{"; MapClose = "}";
                TupleOpen = "("; TupleClose = ")";
                ItemSep = ","; KeyValueSep = ":";
                QuoteNestedStrings = true; Quote = '\'';
                RecognisePositions = false;
                CycleText = "...";
                DroneFormat = "<drone {0}>";
                FunctionName = n => n;
            }

            protected override string Format(object obj, int depth, HashSet<object> seen, bool nested)
            {
                if (obj is GameModule m) return "<Module " + m.Name + ">";
                return base.Format(obj, depth, seen, nested);
            }
        }

        public override string ApiStubFile(out string fileName)
        {
            fileName = StubFileName;
            try { return BuildApiStub(GameApi.Builtins); }
            catch { return BuildApiStub(new List<ApiBuiltin>()); }
        }

        static string PyHint(string neutral)
        {
            if (string.IsNullOrWhiteSpace(neutral)) return "Any";
            var s = neutral.Replace(" ", "");
            if (s.Contains("|")) return string.Join(" | ", s.Split('|').Select(PyHint));
            if (s.EndsWith("?")) return "Optional[" + PyHint(s.Substring(0, s.Length - 1)) + "]";
            int lt = s.IndexOf('<');
            if (lt > 0 && s.EndsWith(">"))
            {
                var head = s.Substring(0, lt);
                var args = SplitArgs(s.Substring(lt + 1, s.Length - lt - 2)).Select(PyHint).ToList();
                switch (head)
                {
                    case "list": case "array": return "list[" + string.Join(", ", args) + "]";
                    case "set": return "set[" + string.Join(", ", args) + "]";
                    case "map": case "dict": return "dict[" + string.Join(", ", args) + "]";
                    case "tuple": return "tuple[" + string.Join(", ", args) + "]";
                    case "func": return "Callable[[], " + (args.Count > 0 ? args[0] : "Any") + "]";
                }
                return "Any";
            }
            switch (s)
            {
                case "int": return "int";
                case "float": case "number": case "double": return "float";
                case "bool": return "bool";
                case "string": case "str": return "str";
                case "void": return "None";
                case "any": case "null": return "Any";
                case "func": return "Callable[[], Any]";
            }
            return Array.IndexOf(IrType.GameTypeNames, s) >= 0 ? s : "Any";
        }

        static IEnumerable<string> SplitArgs(string s)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<') depth++;
                else if (s[i] == '>') depth--;
                else if (s[i] == ',' && depth == 0) { yield return s.Substring(start, i - start); start = i + 1; }
            }
            yield return s.Substring(start);
        }

        /// <summary>A Python reference stub of the game API (typed defs with docstrings) for external editors.</summary>
        public static string BuildApiStub(IEnumerable<ApiBuiltin> builtins)
        {
            var sb = new StringBuilder();
            sb.Append("# JavaFarmer game API reference for The Farmer Was Replaced, written by the JavaFarmer mod.\n");
            sb.Append("# The game never runs this file: it only lets Python editors know the game's functions and constants.\n");
            sb.Append("# Use it with: from javafarmer_api import *\n\n");
            sb.Append("from typing import Any, Callable, Optional\n\n\n");
            foreach (var t in IrType.GameTypeNames)
                sb.Append("class ").Append(t).Append(":\n    \"\"\"A game value of type ").Append(t).Append(".\"\"\"\n\n\n");
            sb.Append("class _Constants(type):\n");
            sb.Append("    \"\"\"Constant bags: the members are game data (for example Entities.Carrot, Items.Hay).\"\"\"\n");
            sb.Append("    def __getattr__(cls, name: str) -> Any: ...\n\n\n");
            foreach (var cat in GameApi.ConstantCategories)
            {
                var gt = GameApi.GameTypeOfCategory(cat);
                sb.Append("class ").Append(cat).Append("(metaclass=_Constants):\n");
                sb.Append("    \"\"\"Every ").Append(gt).Append(" of the game, for example ").Append(cat).Append(".").Append(Example(cat)).Append(".\"\"\"\n\n\n");
            }
            foreach (var d in GameApi.DirectionNames) sb.Append(d).Append(": Direction = Direction()\n");
            sb.Append("\n\n");
            foreach (var b in builtins ?? Enumerable.Empty<ApiBuiltin>())
            {
                if (b == null || string.IsNullOrEmpty(b.PythonName)) continue;
                sb.Append("def ").Append(b.PythonName).Append('(');
                for (int i = 0; i < b.Params.Count; i++)
                {
                    var p = b.Params[i];
                    if (i > 0) sb.Append(", ");
                    if (p.Variadic) sb.Append('*');
                    sb.Append(p.Name).Append(": ").Append(PyHint(p.TypeText));
                    if (p.Optional && !p.Variadic) sb.Append(" = ...");
                }
                sb.Append(") -> ").Append(PyHint(b.ReturnText)).Append(":\n");
                var doc = (b.Doc ?? "").Replace("\\", "\\\\").Replace("\"\"\"", "'''");
                if (b.Extension != null) doc += (doc.Length > 0 ? " " : "") + "(" + b.Extension + " extension)";
                sb.Append("    \"\"\"").Append(doc).Append("\"\"\"\n");
                sb.Append("    ...\n\n\n");
            }
            // the game's Python builtins that are not game actions
            sb.Append("def len(collection: Any) -> int:\n    \"\"\"Number of items in a list, set, dict, tuple or string.\"\"\"\n    ...\n\n\n");
            sb.Append("def range(start: int, stop: int = ..., step: int = ...) -> list[int]:\n    \"\"\"range(n) counts 0 .. n-1; range(a, b, step) counts from a while below b.\"\"\"\n    ...\n\n\n");
            sb.Append("def str(value: Any) -> str:\n    \"\"\"The value as text.\"\"\"\n    ...\n");
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        static string Example(string cat)
        {
            switch (cat)
            {
                case "Entities": return "Carrot";
                case "Items": return "Hay";
                case "Grounds": return "Soil";
                case "Unlocks": return "Speed";
                case "Hats": return "Straw_Hat";
                default: return "Hay";
            }
        }
    }

    /// <summary>Python source → typed IR with declarations placed.</summary>
    internal static class PythonFrontend
    {
        public static IrModule Build(string source, string fileName, List<Diagnostic> errors) => Build(source, fileName, errors, null);

        /// <summary>options: other files (any language) give the types of their functions before inference (cross-language imports).</summary>
        public static IrModule Build(string source, string fileName, List<Diagnostic> errors, CompileOptions options)
        {
            source = source ?? "";
            PyModuleAst ast;
            try { ast = PyParser.Parse(source); }
            catch (PySyntaxError e)
            {
                int s = Math.Max(0, Math.Min(e.Span.Start, source.Length));
                int en = Math.Max(s, Math.Min(e.Span.End, source.Length));
                errors.Add(new Diagnostic(e.Message, s, en));
                return null;
            }
            var m = PyLowering.Lower(ast, string.IsNullOrEmpty(fileName) ? "main" : fileName, errors);
            if (errors.Count > 0) return null;
            ForeignModules.AnnotateExternals(m, options);
            IrTypeInference.Infer(m);
            FixUps(m);
            IrDeclarations.Place(m);
            return m;
        }

        /// <summary>Type-directed lowering: + on strings → Concat, pop(k) on a dict → MapRemove.</summary>
        static void FixUps(IrModule m)
        {
            foreach (var n in IrWalk.Descendants(m))
            {
                switch (n)
                {
                    case IrBinary b when b.Op == IrBinOp.Add && ((b.Left.Type?.IsString ?? false) || (b.Right.Type?.IsString ?? false)):
                        b.Op = IrBinOp.Concat;
                        b.Type = IrType.String;
                        break;
                    case IrAssign a when a.Op == IrBinOp.Add && ((a.Target.Type?.IsString ?? false) || (a.Value.Type?.IsString ?? false)):
                        a.Op = IrBinOp.Concat;
                        break;
                    case IrOpCall o when o.Op == IrOp.ListPop && o.Args.Count == 2 && o.Args[0].Type?.Kind == IrTypeKind.Map:
                        o.Op = IrOp.MapRemove;
                        break;
                }
            }
        }
    }

    /// <summary>Tolerant lexer for colouring (covers every character, never throws).</summary>
    internal static class PythonHighlighter
    {
        static readonly HashSet<string> Constants = new HashSet<string>
        {
            "True", "False", "None", "North", "East", "South", "West", "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards",
        };
        static readonly HashSet<string> Bags = new HashSet<string> { "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards" };
        static readonly string[] Ops = { "**=", "//=", "**", "//", "==", "!=", ">=", "<=", "+=", "-=", "*=", "/=", "%=", "+", "-", "*", "/", "%", "<", ">", "=" };
        const string Punct = "()[]{},:.;";

        public static List<(int, int, TokenKind)> Highlight(string s)
        {
            var res = new List<(int, int, TokenKind)>();
            int i = 0;
            string prev = null, prev2 = null;
            void Add(int start, int end, TokenKind k, string text = null)
            {
                res.Add((start, end - start, k));
                if (k != TokenKind.Whitespace && k != TokenKind.Comment) { prev2 = prev; prev = text ?? s.Substring(start, end - start); }
            }
            while (i < s.Length)
            {
                char c = s[i];
                int st = i;
                if (char.IsWhiteSpace(c))
                {
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    Add(st, i, TokenKind.Whitespace);
                    continue;
                }
                if (c == '#')
                {
                    while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++;
                    Add(st, i, TokenKind.Comment);
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    i++;
                    while (i < s.Length && s[i] != c && s[i] != '\n' && s[i] != '\r') i++;
                    if (i < s.Length && s[i] == c) i++;
                    Add(st, i, TokenKind.String);
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                    if (i + 1 < s.Length && s[i] == '.' && char.IsDigit(s[i + 1])) { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
                    Add(st, i, TokenKind.Number);
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    var w = s.Substring(st, i - st);
                    TokenKind k;
                    if (prev == "." && prev2 != null && Bags.Contains(prev2)) k = TokenKind.Constant;
                    else if (Constants.Contains(w)) k = TokenKind.Constant;
                    else if (PyLexer.Keywords.Contains(w)) k = TokenKind.Keyword;
                    else k = TokenKind.Identifier;
                    Add(st, i, k, w);
                    continue;
                }
                string op = null;
                foreach (var o in Ops) if (string.CompareOrdinal(s, i, o, 0, o.Length) == 0) { op = o; break; }
                if (op != null) { i += op.Length; Add(st, i, TokenKind.Operator, op); continue; }
                if (Punct.IndexOf(c) >= 0) { i++; Add(st, i, TokenKind.Punctuation); continue; }
                i += char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
                Add(st, i, TokenKind.Unknown);
            }
            return res;
        }
    }
}
