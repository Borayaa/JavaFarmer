using System;
using System.Collections.Generic;
using System.Text;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Java replacement for CodeUtilities.SyntaxColor2: same TextMeshPro markup, same palette, same search marks.
    /// </summary>
    public static class JavaHighlighter
    {
        public const string CommentColor = "#666666";
        public const string StringColor = "#8f5d28";
        public const string KeywordColor = "#9e7124";
        public const string FunctionColor = "#e6c87c";
        public const string BuiltinColor = "#7f8a36";
        public const string NumberColor = "#7f8a36";

        private static readonly HashSet<string> primitiveTypes = new HashSet<string>
        {
            "int", "double", "boolean", "void", "var", "long", "float", "char", "byte", "short"
        };

        /// <summary>Type names of other languages drawn in the keyword colour (like Java's int/double).</summary>
        private static readonly HashSet<string> otherPrimitiveTypes = new HashSet<string>
        {
            "int", "double", "float", "bool", "boolean", "void", "var", "auto", "let", "char", "long", "short", "byte", "unsigned", "signed",
            "i8", "i16", "i32", "i64", "i128", "u8", "u16", "u32", "u64", "u128", "f32", "f64", "usize", "isize", "str", "string",
            "comptime_int", "comptime_float", "anytype", "noreturn", "object", "dynamic", "decimal", "uint", "ulong", "sbyte", "ushort",
        };

        /// <summary>Names the game's colouring treats as builtins (game constants, literal values of every language).</summary>
        private static readonly HashSet<string> gameConstants = new HashSet<string>
        {
            "North", "East", "South", "West", "Entities", "Items", "Grounds", "Unlocks", "Leaderboards", "Hats",
            "true", "false", "null", "True", "False", "None", "TRUE", "FALSE", "NULL", "nullptr", "nil", "undefined", "NA",
            "Entity", "Item", "Ground", "Unlock", "Leaderboard", "Hat", "Direction",
        };

        /// <summary>Colouring for any language: its Highlight tokens with the game's palette.</summary>
        public static string Color(string code, string searchWord, int searchIndex, ILanguage lang)
        {
            if (lang == null) return Color(code, searchWord, searchIndex);
            var keywords = ActiveLanguage.KeywordSet(lang);
            return Color(code, searchWord, searchIndex, lang.Highlight, w => otherPrimitiveTypes.Contains(w) || keywords.Contains(w));
        }

        /// <summary>Tokenizer used for colouring. Defaults to the compiler's JavaSyntax.Highlight.</summary>
        public static Func<string, IReadOnlyList<(int Start, int Length, TokenKind Kind)>> Lexer = JavaSyntax.Highlight;

        public static string Color(string code, string searchWord = "", int searchIndex = -1)
            => Color(code, searchWord, searchIndex, Lexer);

        public static string Color(string code, string searchWord, int searchIndex,
            Func<string, IReadOnlyList<(int Start, int Length, TokenKind Kind)>> lexer)
            => Color(code, searchWord, searchIndex, lexer, null);

        /// <summary>keywordType: type names drawn in the keyword colour (default: Java's primitives).</summary>
        public static string Color(string code, string searchWord, int searchIndex,
            Func<string, IReadOnlyList<(int Start, int Length, TokenKind Kind)>> lexer, Func<string, bool> keywordType)
        {
            keywordType = keywordType ?? primitiveTypes.Contains;
            if (string.IsNullOrEmpty(code)) return code ?? "";
            int n = code.Length;

            // 1. Per-character colour from the lexer (gaps stay uncoloured).
            var colorOf = new string[n];
            var tokenId = new int[n];
            for (int i = 0; i < n; i++) tokenId[i] = -1;
            IReadOnlyList<(int Start, int Length, TokenKind Kind)> tokens = null;
            try { tokens = lexer?.Invoke(code); } catch { tokens = null; }
            if (tokens != null)
            {
                for (int t = 0; t < tokens.Count; t++)
                {
                    var (start, length, kind) = tokens[t];
                    if (start < 0 || length <= 0 || start >= n) continue;
                    int end = Math.Min(n, start + length);
                    string c = ColorFor(code, start, end, kind, keywordType);
                    for (int i = start; i < end; i++) { colorOf[i] = c; tokenId[i] = t; }
                }
            }

            // 2. Search occurrences (case-insensitive, non-overlapping, like Regex.Replace).
            var occurrences = new List<(int s, int e, bool inner)>();
            if (!string.IsNullOrEmpty(searchWord))
            {
                int from = 0;
                while (from <= n - searchWord.Length)
                {
                    int idx = code.IndexOf(searchWord, from, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    int e = idx + searchWord.Length;
                    // Inside a single coloured token -> inner mark (token keeps its colour), else outer mark on plain text.
                    bool inner = colorOf[idx] != null && tokenId[idx] >= 0 && tokenId[e - 1] == tokenId[idx];
                    occurrences.Add((idx, e, inner));
                    from = e;
                }
                foreach (var o in occurrences)
                    if (!o.inner) for (int i = o.s; i < o.e; i++) { colorOf[i] = null; tokenId[i] = -2; }
            }

            // 3. Emit.
            var sb = new StringBuilder(n * 2);
            int oi = 0;
            int pos = 0;
            while (pos < n)
            {
                while (oi < occurrences.Count && occurrences[oi].e <= pos) oi++;
                if (oi < occurrences.Count && !occurrences[oi].inner && occurrences[oi].s == pos)
                {
                    var o = occurrences[oi];
                    bool selected = searchIndex >= 0 && o.s == searchIndex;
                    sb.Append(selected ? "<mark=#ffffff10>" : "<mark=#ffffff05>");
                    AppendEscaped(sb, code, o.s, o.e);
                    sb.Append("</mark>");
                    pos = o.e;
                    oi++;
                    continue;
                }
                // a run of characters with the same token id (outer occurrences have id -2 and are handled above)
                int id = tokenId[pos];
                int runEnd = pos + 1;
                while (runEnd < n && tokenId[runEnd] == id) runEnd++;
                string color = colorOf[pos];
                if (color != null) sb.Append("<color=").Append(color).Append('>');
                AppendWithInnerMarks(sb, code, pos, runEnd, occurrences, searchIndex);
                if (color != null) sb.Append("</color>");
                pos = runEnd;
            }
            return sb.ToString();
        }

        private static void AppendWithInnerMarks(StringBuilder sb, string code, int start, int end, List<(int s, int e, bool inner)> occ, int searchIndex)
        {
            int pos = start;
            foreach (var o in occ)
            {
                if (!o.inner || o.s < start || o.e > end) continue;
                AppendEscaped(sb, code, pos, o.s);
                bool selected = searchIndex >= 0 && o.s == searchIndex;
                sb.Append(selected ? "<mark=#ffffff14>" : "<mark=#ffffff09>");
                AppendEscaped(sb, code, o.s, o.e);
                sb.Append("</mark>");
                pos = o.e;
            }
            AppendEscaped(sb, code, pos, end);
        }

        /// <summary>
        /// Appends code text. A '<' that could open a TextMeshPro tag (e.g. List&lt;b&gt;) is wrapped in noparse so the coloured
        /// overlay keeps exactly the same visible characters as the input field.
        /// </summary>
        private static void AppendEscaped(StringBuilder sb, string code, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                char c = code[i];
                if (c == '<' && i + 1 < code.Length && (char.IsLetter(code[i + 1]) || code[i + 1] == '/' || code[i + 1] == '#'))
                    sb.Append("<noparse><</noparse>");
                else
                    sb.Append(c);
            }
        }

        private static string ColorFor(string code, int start, int end, TokenKind kind, Func<string, bool> keywordType)
        {
            switch (kind)
            {
                case TokenKind.Comment: return CommentColor;
                case TokenKind.String: return StringColor;
                case TokenKind.Keyword:
                {
                    string w = code.Substring(start, end - start);
                    return gameConstants.Contains(w) ? BuiltinColor : KeywordColor;
                }
                case TokenKind.Number: return NumberColor;
                case TokenKind.Constant: return BuiltinColor;
                case TokenKind.Type:
                {
                    string w = code.Substring(start, end - start);
                    if (end < code.Length && code[end] == '(') return FunctionColor; // e.g. constructor-like calls
                    return keywordType(w) ? KeywordColor : BuiltinColor;
                }
                case TokenKind.Identifier:
                {
                    if (end < code.Length && code[end] == '(') return FunctionColor;
                    if (start >= 2 && code[start - 1] == '.' && char.IsLetter(code[start - 2])) return BuiltinColor;
                    string w = code.Substring(start, end - start);
                    return gameConstants.Contains(w) ? BuiltinColor : null;
                }
                default: return null;
            }
        }
    }
}
