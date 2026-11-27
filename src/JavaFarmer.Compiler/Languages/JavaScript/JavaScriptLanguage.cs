using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// JavaScript (ES2023 modules, strict mode) as a JavaFarmer language: top-level statements run like a script;
    /// game builtins are camelCase globals (canHarvest(), doAFlip()), print is console.log, min / max / abs / random are
    /// Math.min / Math.max / Math.abs / Math.random. See docs/languages/js.md.
    /// </summary>
    public sealed class JavaScriptLanguage : LanguageBase, IPrinterDiagnostics, ISignatureProvider
    {
        public const string StubFileName = "farm.js";

        public override string Id => "js";
        public override string DisplayName => "JavaScript";
        public override string FileExtension => ".js";
        protected override NamingStyle ApiNaming => NamingStyle.Camel;

        static readonly EditorRules rules = new EditorRules
        {
            IndentAfter = new[] { "{" },
            DedentOn = new[] { "}" },
            LineComment = "//",
            BlockCommentStart = "/*",
            BlockCommentEnd = "*/",
            IndentUnit = "\t",
            Brackets = new[] { "{}", "()", "[]" },
        };
        public override EditorRules Editor => rules;

        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors) =>
            JsLowering.Build(source, options, errors);

        protected override string PrintImpl(IrModule module, PrintOptions options) => JsPrinter.Print(module, options, out _);

        public string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            try { return JsPrinter.Print(module, options ?? new PrintOptions(), out problems); }
            catch (Exception ex) { problems = new[] { "internal printer error: " + ex.Message }; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        /// <summary>Declarations of a JavaScript window for other languages (ForeignModules): bodies and imports not checked.</summary>
        public ModuleSignature Signatures(string source, CompileOptions options)
        {
            try { return JsModuleInfo.Signatures(source, options); }
            catch (Exception ex) { return ModuleSignature.Failed(options?.FileName ?? "main", Id, "internal JavaFarmer error while reading " + (options?.FileName ?? "main") + " (JavaScript): " + ex.Message); }
        }

        // ------------------------------------------------------------------ names
        /// <summary>can_harvest → canHarvest, print → console.log, min → Math.min, ... (registered builtins too).</summary>
        public override string ApiName(string pythonName)
        {
            try { return JsNames.ApiName(pythonName); } catch { return null; }
        }

        /// <summary>Inverse of ApiName; also accepts the short forms print / min / max / abs / random.</summary>
        public override string FromApiName(string name)
        {
            try { return JsNames.FromApiName(name); } catch { return null; }
        }

        // ------------------------------------------------------------------ editor services
        static readonly IReadOnlyCollection<string> keywords = new[]
        {
            "break", "case", "const", "continue", "default", "do", "else", "export", "false", "for", "from", "function", "if",
            "import", "in", "let", "new", "null", "of", "return", "switch", "true", "undefined", "var", "while", "as",
        }.OrderBy(k => k, StringComparer.Ordinal).ToList();
        public override IReadOnlyCollection<string> Keywords => keywords;

        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            try { return JsHighlighter.Highlight(source ?? ""); }
            catch { return string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, TokenKind.Unknown) }; }
        }

        /// <summary>A new window is an empty script: top-level statements run.</summary>
        public override string NewFileTemplate(string fileName, out int caret) { caret = 0; return ""; }

        protected override ValueFormat CreateValueFormat() => new JsValueFormat();

        /// <summary>Node's console.log / util.inspect style (always on one line).</summary>
        internal sealed class JsValueFormat : ValueFormat
        {
            public JsValueFormat()
            {
                NullText = "null"; TrueText = "true"; FalseText = "false";
                RecognisePositions = false;
            }

            public override string FormatNumber(double d) => JsFormat.Number(d);

            protected override string Format(object obj, int depth, HashSet<object> seen, bool nested)
            {
                if (depth > MaxDepth) return "[Object]";
                switch (obj)
                {
                    case null: return "null";
                    case bool b: return b ? "true" : "false";
                    case double d: return FormatNumber(d);
                    case float f: return FormatNumber(f);
                    case int i: return FormatNumber(i);
                    case long l: return FormatNumber(l);
                    case string s: return nested ? JsFormat.Inspect(s) : s;
                    case GameDrone dr: return "[Drone " + dr.Id.ToString(CultureInfo.InvariantCulture) + "]";
                    case GameFunction fn:
                        {
                            var n = fn.PythonName;
                            if (string.IsNullOrEmpty(n) || n.StartsWith("__j", StringComparison.Ordinal)) return "[Function (anonymous)]";
                            return "[Function: " + (JsNames.ApiName(n) ?? n) + "]";
                        }
                    case GameModule m: return "[Module: " + m.Name + "]";
                    case GameConstant c: return c.Category == null || c.Category == "Directions" ? c.Name : c.Category + "." + c.Name;
                }
                if (seen != null && seen.Contains(obj)) return "[Circular *1]";
                if (seen == null) seen = new HashSet<object>(RefEq.Instance);
                seen.Add(obj);
                try
                {
                    switch (obj)
                    {
                        case GameList list: return Seq("", list.Items, depth, seen, "[", "]");
                        case GameTuple t: return Seq("", t.Items, depth, seen, "[", "]");
                        case GameSet set: return Seq("Set(" + set.Items.Count + ") ", set.Items, depth, seen, "{", "}");
                        case GameDict dict:
                            {
                                var head = "Map(" + dict.Entries.Count + ") ";
                                if (dict.Entries.Count == 0) return head + "{}";
                                var sb = new StringBuilder(head).Append("{ ");
                                for (int k = 0; k < dict.Entries.Count; k++)
                                {
                                    if (k > 0) sb.Append(", ");
                                    sb.Append(Format(dict.Entries[k].Key, depth + 1, seen, true)).Append(" => ").Append(Format(dict.Entries[k].Value, depth + 1, seen, true));
                                }
                                return sb.Append(" }").ToString();
                            }
                    }
                }
                finally { seen.Remove(obj); }
                try { return Convert.ToString(obj, CultureInfo.InvariantCulture); } catch { return obj.GetType().Name; }
            }

            string Seq(string head, List<object> items, int depth, HashSet<object> seen, string open, string close)
            {
                if (items.Count == 0) return head + open + close;
                var sb = new StringBuilder(head).Append(open).Append(' ');
                for (int k = 0; k < items.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append(Format(items[k], depth + 1, seen, true));
                }
                return sb.Append(' ').Append(close).ToString();
            }
        }

        sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        // ------------------------------------------------------------------ stub
        public override string ApiStubFile(out string fileName)
        {
            fileName = StubFileName;
            try { return BuildApiStub(GameApi.Builtins); }
            catch { return BuildApiStub(new List<ApiBuiltin>()); }
        }

        static string JsDocType(string neutral)
        {
            if (string.IsNullOrWhiteSpace(neutral)) return "*";
            var s = neutral.Replace(" ", "");
            if (s.Contains("|")) return "(" + string.Join("|", s.Split('|').Select(JsDocType)) + ")";
            if (s.EndsWith("?")) return "?" + JsDocType(s.Substring(0, s.Length - 1));
            int lt = s.IndexOf('<');
            if (lt > 0 && s.EndsWith(">"))
            {
                var head = s.Substring(0, lt);
                var inner = s.Substring(lt + 1, s.Length - lt - 2);
                var parts = Split(inner).Select(JsDocType).ToList();
                switch (head)
                {
                    case "list": case "array": return "Array<" + (parts.Count > 0 ? parts[0] : "*") + ">";
                    case "set": return "Set<" + (parts.Count > 0 ? parts[0] : "*") + ">";
                    case "map": case "dict": return "Map<" + string.Join(", ", parts) + ">";
                    case "tuple": return "Array<*>";
                    case "func": return "function(): " + (parts.Count > 0 ? parts[0] : "*");
                }
                return "*";
            }
            switch (s)
            {
                case "int": case "float": case "number": case "double": return "number";
                case "bool": return "boolean";
                case "string": case "str": return "string";
                case "void": return "void";
                case "any": case "null": return "*";
                case "func": return "function(): *";
            }
            return Array.IndexOf(IrType.GameTypeNames, s) >= 0 ? s : "*";
        }

        static IEnumerable<string> Split(string s)
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

        static string DefaultReturn(string neutral)
        {
            var t = (neutral ?? "").Trim();
            if (t.EndsWith("?") || t.Contains("|")) return "null";
            switch (t)
            {
                case "bool": return "false";
                case "int": case "float": case "number": return "0";
                case "void": case "": return null;
            }
            return "null";
        }

        /// <summary>
        /// farm.js: an ES module exporting every game builtin as a JSDoc-typed camelCase stub, the constant containers
        /// (Proxy objects, so any member name works), North / East / South / West and quickPrint; it also installs them on
        /// globalThis, so a JavaFarmer program that calls canHarvest() unqualified runs in node after import "./farm.js".
        /// </summary>
        public static string BuildApiStub(IEnumerable<ApiBuiltin> builtins)
        {
            var sb = new StringBuilder();
            sb.Append("// JavaFarmer game API for The Farmer Was Replaced (JavaScript), written by the JavaFarmer mod.\n");
            sb.Append("// The game never runs this file: it lets editors and node know the game's functions and constants.\n");
            sb.Append("// Use it with: import \"./farm.js\";   (the functions and constants then exist as globals)\n");
            sb.Append("// or:          import { canHarvest, harvest, Entities } from \"./farm.js\";\n");
            sb.Append("// print is console.log; min / max / abs / random are Math.min / Math.max / Math.abs / Math.random.\n\n");
            foreach (var t in IrType.GameTypeNames)
                sb.Append("/** @typedef {{category: string, name: string}} ").Append(t).Append(" A game value of type ").Append(t).Append(". */\n");
            sb.Append("\nconst inspect = Symbol.for(\"nodejs.util.inspect.custom\");\n\n");
            sb.Append("function makeConstant(category, name) {\n");
            sb.Append("\tconst text = category === \"Directions\" ? name : category + \".\" + name;\n");
            sb.Append("\treturn Object.freeze({ category, name, toString: () => text, [inspect]: () => text });\n");
            sb.Append("}\n\n");
            sb.Append("function constants(category) {\n");
            sb.Append("\tconst cache = new Map();\n");
            sb.Append("\treturn new Proxy({}, {\n");
            sb.Append("\t\tget(target, name) {\n");
            sb.Append("\t\t\tif (typeof name !== \"string\") return name === inspect ? () => \"[\" + category + \"]\" : undefined;\n");
            sb.Append("\t\t\tif (name === \"then\" || name === \"toJSON\") return undefined;\n");
            sb.Append("\t\t\tif (!cache.has(name)) cache.set(name, makeConstant(category, name));\n");
            sb.Append("\t\t\treturn cache.get(name);\n");
            sb.Append("\t\t},\n");
            sb.Append("\t});\n");
            sb.Append("}\n\n");
            var exported = new List<string>();
            foreach (var cat in GameApi.ConstantCategories)
            {
                sb.Append("/** Every ").Append(GameApi.GameTypeOfCategory(cat)).Append(" of the game, for example ").Append(cat).Append(".").Append(Example(cat)).Append(". */\n");
                sb.Append("export const ").Append(cat).Append(" = constants(\"").Append(cat).Append("\");\n");
                exported.Add(cat);
            }
            sb.Append('\n');
            foreach (var d in GameApi.DirectionNames)
            {
                sb.Append("/** @type {Direction} */\nexport const ").Append(d).Append(" = makeConstant(\"Directions\", \"").Append(d).Append("\");\n");
                exported.Add(d);
            }
            sb.Append('\n');
            foreach (var b in builtins ?? Enumerable.Empty<ApiBuiltin>())
            {
                if (b == null || string.IsNullOrEmpty(b.PythonName)) continue;
                var js = JsNames.ApiName(b.PythonName) ?? GameApi.ToStyle(b.PythonName, NamingStyle.Camel);
                if (js == null || js.Contains(".") || JsLexer.Keywords.Contains(js) || !JsFormat.IsIdentifierName(js) || exported.Contains(js)) continue;
                sb.Append("/**\n * ").Append((b.Doc ?? "").Replace("*/", "* /"));
                if (b.Extension != null) sb.Append(" (").Append(b.Extension).Append(" extension)");
                sb.Append("\n * game: ").Append(b.PythonName).Append('\n');
                var names = new List<string>();
                foreach (var p in b.Params)
                {
                    var pn = GameApi.ToStyle(p.Name, NamingStyle.Camel);
                    if (JsLexer.Keywords.Contains(pn) || !JsFormat.IsIdentifierName(pn)) pn += "_";
                    var type = JsDocType(p.TypeText);
                    if (p.Variadic) { sb.Append(" * @param {...").Append(type).Append("} ").Append(pn).Append('\n'); names.Add("..." + pn); break; }
                    sb.Append(" * @param {").Append(type).Append("} ").Append(p.Optional ? "[" + pn + "]" : pn).Append('\n');
                    names.Add(pn);
                }
                sb.Append(" * @returns {").Append(JsDocType(b.ReturnText)).Append("}\n */\n");
                sb.Append("export function ").Append(js).Append('(').Append(string.Join(", ", names)).Append(") {");
                if (b.PythonName == "quick_print") sb.Append("\n\tconsole.log(").Append(names.Count > 0 ? names[0] : "").Append(");\n}\n\n");
                else
                {
                    var def = DefaultReturn(b.ReturnText);
                    sb.Append(def == null ? " }\n\n" : "\n\treturn " + def + ";\n}\n\n");
                }
                exported.Add(js);
            }
            sb.Append("Object.assign(globalThis, { ").Append(string.Join(", ", exported)).Append(" });\n");
            return sb.ToString();
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
                default: return "Fastest_Reset";
            }
        }
    }

    /// <summary>Tolerant JavaScript colouring (covers every character, never throws; template literals with substitutions).</summary>
    internal static class JsHighlighter
    {
        static readonly HashSet<string> Kw = new HashSet<string>(JsLexer.Keywords) { "of", "from", "as" };
        static readonly HashSet<string> Constants = new HashSet<string> { "true", "false", "null", "undefined", "North", "East", "South", "West", "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards" };
        static readonly HashSet<string> Bags = new HashSet<string>(GameApi.ConstantCategories);
        static readonly HashSet<string> Types = new HashSet<string> { "Map", "Set", "Array", "Object", "Math", "console", "String" };
        static readonly string[] Ops =
        {
            ">>>=", "===", "!==", "**=", "<<=", ">>=", ">>>", "&&=", "||=", "??=", "...",
            "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "**", "<<", ">>",
            "<", ">", "+", "-", "*", "/", "%", "&", "|", "^", "!", "~", "?", "=",
        };
        const string Punct = "()[]{},;:.";

        public static List<(int, int, TokenKind)> Highlight(string s)
        {
            var res = new List<(int, int, TokenKind)>();
            int i = 0;
            string prev = null, prev2 = null;
            var braces = new List<bool>();   // true: this '{' opened a template substitution
            void Add(int start, int end, TokenKind k)
            {
                if (end <= start) return;
                res.Add((start, end - start, k));
                if (k != TokenKind.Whitespace && k != TokenKind.Comment) { prev2 = prev; prev = s.Substring(start, end - start); }
            }
            // template text from i up to the closing backtick or the next substitution
            void TemplateText(int from)
            {
                int st = from;
                while (i < s.Length)
                {
                    if (s[i] == '\\') { i = Math.Min(s.Length, i + 2); continue; }
                    if (s[i] == '`') { i++; Add(st, i, TokenKind.String); return; }
                    if (s[i] == '$' && i + 1 < s.Length && s[i + 1] == '{')
                    {
                        Add(st, i, TokenKind.String);
                        Add(i, i + 2, TokenKind.Punctuation);
                        i += 2;
                        braces.Add(true);
                        return;
                    }
                    i++;
                }
                Add(st, i, TokenKind.String);
            }
            while (i < s.Length)
            {
                char c = s[i];
                int st = i;
                if (char.IsWhiteSpace(c)) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; Add(st, i, TokenKind.Whitespace); continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++; Add(st, i, TokenKind.Comment); continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = e < 0 ? s.Length : e + 2;
                    Add(st, i, TokenKind.Comment);
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    i++;
                    while (i < s.Length && s[i] != c && s[i] != '\n' && s[i] != '\r') { if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] != '\n') i++; i++; }
                    if (i < s.Length && s[i] == c) i++;
                    if (i > s.Length) i = s.Length;
                    Add(st, i, TokenKind.String);
                    continue;
                }
                if (c == '`') { i++; TemplateText(st); continue; }
                if (c == '}' && braces.Count > 0 && braces[braces.Count - 1])
                {
                    braces.RemoveAt(braces.Count - 1);
                    i++;
                    Add(st, i, TokenKind.Punctuation);
                    TemplateText(i);
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    i++;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.' || ((s[i] == '+' || s[i] == '-') && (s[i - 1] == 'e' || s[i - 1] == 'E')))) i++;
                    Add(st, i, TokenKind.Number);
                    continue;
                }
                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$')) i++;
                    var w = s.Substring(st, i - st);
                    TokenKind k;
                    if (prev == "." && prev2 != null && Bags.Contains(prev2)) k = TokenKind.Constant;
                    else if (prev == ".") k = TokenKind.Identifier;
                    else if (Constants.Contains(w)) k = TokenKind.Constant;
                    else if (Kw.Contains(w)) k = TokenKind.Keyword;
                    else if (Types.Contains(w)) k = TokenKind.Type;
                    else k = TokenKind.Identifier;
                    Add(st, i, k);
                    continue;
                }
                if (c == '{') { braces.Add(false); i++; Add(st, i, TokenKind.Punctuation); continue; }
                if (c == '}') { if (braces.Count > 0) braces.RemoveAt(braces.Count - 1); i++; Add(st, i, TokenKind.Punctuation); continue; }
                string op = null;
                foreach (var o in Ops) if (string.CompareOrdinal(s, i, o, 0, o.Length) == 0) { op = o; break; }
                if (op != null) { i += op.Length; Add(st, i, TokenKind.Operator); continue; }
                if (Punct.IndexOf(c) >= 0) { i++; Add(st, i, TokenKind.Punctuation); continue; }
                i += char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
                Add(st, i, TokenKind.Unknown);
            }
            return res;
        }
    }
}
