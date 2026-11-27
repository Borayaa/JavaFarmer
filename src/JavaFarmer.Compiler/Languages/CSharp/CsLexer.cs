using System.Collections.Generic;
using System.Text;

namespace JavaFarmer.Compiler.CSharp
{
    internal enum CTk { Identifier, Keyword, IntLit, DoubleLit, StringLit, InterpStr, CharLit, Op, Comment, Whitespace, Directive, Unknown, EOF }

    /// <summary>One piece of an interpolated string: literal text, or the source range of a {hole}.</summary>
    internal sealed class InterpPart
    {
        public string Text;          // literal text (decoded) when IsHole is false
        public bool IsHole;
        public int Start, End;       // hole: expression source range [Start, End) (without the braces)
        public bool HasFormat;       // {x:F2} / {x,5}
        public int FormatStart;
    }

    internal sealed class CsToken
    {
        public CTk Kind;
        public string Text;          // raw source text
        public int Start, End;       // [Start, End)
        public string StrValue;      // decoded string / char value
        public bool Unterminated;
        public bool Verbatim;
        public List<InterpPart> Parts;   // InterpStr
        public override string ToString() => Kind + ":" + Text;
    }

    /// <summary>C# 12 lexer (the JavaFarmer subset plus everything needed to report the rest clearly). Never throws.</summary>
    internal sealed class CsLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
            "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
            "using", "virtual", "void", "volatile", "while",
        };

        /// <summary>Contextual keywords (identifiers in most positions; highlighted as keywords).</summary>
        public static readonly HashSet<string> Contextual = new HashSet<string>
        {
            "var", "global", "record", "when", "and", "or", "not", "async", "await", "yield", "nameof", "dynamic", "get", "set",
            "init", "value", "partial", "where", "from", "select", "let", "with", "required", "file", "scoped", "nint", "nuint",
        };

        static readonly string[] Operators =
        {
            ">>>=", "<<=", ">>=", "??=", ">>>", "=>", "??", "?.", "::", "++", "--", "&&", "||", "==", "!=", "<=", ">=", "+=", "-=",
            "*=", "/=", "%=", "&=", "|=", "^=", "<<", "->", "..",
            "(", ")", "{", "}", "[", "]", ";", ",", ".", "=", ">", "<", "!", "~", "?", ":", "+", "-", "*", "/", "&", "|", "^", "%", "@", "#", "$",
        };

        static readonly Dictionary<char, string[]> OpsByFirst = BuildOps();
        static Dictionary<char, string[]> BuildOps()
        {
            var d = new Dictionary<char, List<string>>();
            foreach (var op in Operators)
            {
                if (!d.TryGetValue(op[0], out var l)) d[op[0]] = l = new List<string>();
                l.Add(op);
            }
            var res = new Dictionary<char, string[]>();
            foreach (var kv in d) { kv.Value.Sort((a, b) => b.Length.CompareTo(a.Length)); res[kv.Key] = kv.Value.ToArray(); }
            return res;
        }

        readonly string src;
        int pos;
        bool skipTrivia;
        public Diagnostic Error;
        readonly bool tolerant;

        public CsLexer(string source, bool tolerant = false) { src = source ?? ""; this.tolerant = tolerant; }

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
        public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        public List<CsToken> LexAll()
        {
            var list = new List<CsToken>();
            while (pos < src.Length) { int before = pos; list.Add(Next()); if (pos <= before) pos = before + 1; }
            return list;
        }

        public List<CsToken> LexSignificant()
        {
            skipTrivia = true;
            var list = new List<CsToken>(src.Length / 4 + 4);
            while (pos < src.Length)
            {
                int before = pos;
                var t = Next();
                if (pos <= before) pos = before + 1;
                if (t.Kind == CTk.Whitespace || t.Kind == CTk.Comment) continue;
                list.Add(t);
            }
            list.Add(new CsToken { Kind = CTk.EOF, Text = "", Start = src.Length, End = src.Length });
            return list;
        }

        void Fail(string msg, int s, int e) { if (Error == null) Error = new Diagnostic(msg, s, e); }

        CsToken Make(CTk k, int s) => new CsToken { Kind = k, Start = s, End = pos, Text = src.Substring(s, pos - s) };
        static readonly CsToken Trivia = new CsToken { Kind = CTk.Whitespace, Text = "" };
        static readonly CsToken CommentTrivia = new CsToken { Kind = CTk.Comment, Text = "" };

        bool AtLineStart(int p)
        {
            for (int i = p - 1; i >= 0; i--)
            {
                char c = src[i];
                if (c == '\n') return true;
                if (c != ' ' && c != '\t' && c != '\r') return false;
            }
            return true;
        }

        CsToken Next()
        {
            int s = pos;
            char c = src[pos];
            if (char.IsWhiteSpace(c))
            {
                while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
                return skipTrivia ? Trivia : Make(CTk.Whitespace, s);
            }
            if (c == '/' && pos + 1 < src.Length && src[pos + 1] == '/')
            {
                while (pos < src.Length && src[pos] != '\n') pos++;
                return skipTrivia ? CommentTrivia : Make(CTk.Comment, s);
            }
            if (c == '/' && pos + 1 < src.Length && src[pos + 1] == '*')
            {
                int close = src.IndexOf("*/", pos + 2, System.StringComparison.Ordinal);
                if (close < 0)
                {
                    pos = src.Length;
                    Fail("CS1035: End-of-file found, '*/' expected", s, s + 2);
                    var t = Make(CTk.Comment, s); t.Unterminated = true; return t;
                }
                pos = close + 2;
                return Make(CTk.Comment, s);
            }
            if (c == '#' && AtLineStart(s))
            {
                while (pos < src.Length && src[pos] != '\n') pos++;
                var d = Make(CTk.Directive, s);
                int e = s + 1; while (e < src.Length && IsIdentPart(src[e])) e++;
                Fail("preprocessor directives (" + src.Substring(s, e - s) + ") are not supported in JavaFarmer", s, e);
                return d;
            }
            if (c == '@' && pos + 1 < src.Length && IsIdentStart(src[pos + 1]))
            {
                pos++;
                while (pos < src.Length && IsIdentPart(src[pos])) pos++;
                Fail("verbatim identifiers (@name) are not supported in JavaFarmer", s, pos);
                return Make(CTk.Identifier, s);
            }
            if (IsIdentStart(c))
            {
                while (pos < src.Length && IsIdentPart(src[pos])) pos++;
                var t = Make(CTk.Identifier, s);
                if (Keywords.Contains(t.Text)) t.Kind = CTk.Keyword;
                return t;
            }
            if (char.IsDigit(c) || (c == '.' && pos + 1 < src.Length && char.IsDigit(src[pos + 1]))) return Number(s);
            // strings: "..." @"..." $"..." $@"..." @$"..." """..."""
            if (c == '"' || ((c == '@' || c == '$') && pos + 1 < src.Length && (src[pos + 1] == '"' || ((src[pos + 1] == '@' || src[pos + 1] == '$') && pos + 2 < src.Length && src[pos + 2] == '"'))))
                return StringLit(s);
            if (c == '\'') return CharLit(s);
            if (OpsByFirst.TryGetValue(c, out var ops))
                foreach (var op in ops)
                {
                    if (op.Length == 1 || string.CompareOrdinal(src, pos, op, 0, op.Length) == 0)
                    {
                        pos += op.Length;
                        return new CsToken { Kind = CTk.Op, Start = s, End = pos, Text = op };
                    }
                }
            pos++;
            Fail("CS1056: Unexpected character '" + c + "'", s, pos);
            return Make(CTk.Unknown, s);
        }

        static bool IsHex(char c) => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        CsToken Number(int s)
        {
            bool isDouble = false;
            if (src[pos] == '0' && pos + 1 < src.Length && (src[pos + 1] == 'x' || src[pos + 1] == 'X' || src[pos + 1] == 'b' || src[pos + 1] == 'B'))
            {
                pos += 2;
                while (pos < src.Length && (IsHex(src[pos]) || src[pos] == '_')) pos++;
            }
            else
            {
                while (pos < src.Length && (char.IsDigit(src[pos]) || src[pos] == '_')) pos++;
                if (pos + 1 < src.Length && src[pos] == '.' && char.IsDigit(src[pos + 1]))
                {
                    isDouble = true; pos++;
                    while (pos < src.Length && (char.IsDigit(src[pos]) || src[pos] == '_')) pos++;
                }
                if (pos < src.Length && (src[pos] == 'e' || src[pos] == 'E'))
                {
                    int save = pos;
                    pos++;
                    if (pos < src.Length && (src[pos] == '+' || src[pos] == '-')) pos++;
                    if (pos < src.Length && char.IsDigit(src[pos])) { isDouble = true; while (pos < src.Length && char.IsDigit(src[pos])) pos++; }
                    else pos = save;
                }
            }
            // suffixes (validated by the parser): d D f F m M l L u U ul ...
            while (pos < src.Length && "dDfFmMlLuU".IndexOf(src[pos]) >= 0)
            {
                if ("dDfFmM".IndexOf(src[pos]) >= 0) isDouble = true;
                pos++;
            }
            while (pos < src.Length && IsIdentPart(src[pos])) pos++;   // "12abc" is one bad token
            return Make(isDouble ? CTk.DoubleLit : CTk.IntLit, s);
        }

        CsToken StringLit(int s)
        {
            bool verbatim = false, interp = false;
            while (pos < src.Length && (src[pos] == '@' || src[pos] == '$'))
            {
                if (src[pos] == '@') verbatim = true; else interp = true;
                pos++;
            }
            if (string.CompareOrdinal(src, pos, "\"\"\"", 0, 3) == 0)
            {
                int close = src.IndexOf("\"\"\"", pos + 3, System.StringComparison.Ordinal);
                pos = close < 0 ? src.Length : close + 3;
                while (pos < src.Length && src[pos] == '"') pos++;
                Fail("raw string literals are not supported in JavaFarmer", s, pos);
                var rt = Make(CTk.StringLit, s); rt.StrValue = ""; rt.Unterminated = close < 0; return rt;
            }
            pos++;   // opening quote
            var sb = new StringBuilder();
            var parts = interp ? new List<InterpPart>() : null;
            void FlushText()
            {
                if (parts != null && sb.Length > 0) { parts.Add(new InterpPart { Text = sb.ToString() }); sb.Clear(); }
            }
            while (true)
            {
                if (pos >= src.Length || (!verbatim && (src[pos] == '\n' || src[pos] == '\r')))
                {
                    Fail("CS1010: Newline in constant", s, pos);
                    FlushText();
                    var u = Make(interp ? CTk.InterpStr : CTk.StringLit, s); u.StrValue = sb.ToString(); u.Unterminated = true; u.Parts = parts; u.Verbatim = verbatim; return u;
                }
                char c = src[pos];
                if (c == '"')
                {
                    if (verbatim && pos + 1 < src.Length && src[pos + 1] == '"') { sb.Append('"'); pos += 2; continue; }
                    pos++;
                    break;
                }
                if (interp && c == '{')
                {
                    if (pos + 1 < src.Length && src[pos + 1] == '{') { sb.Append('{'); pos += 2; continue; }
                    FlushText();
                    pos++;
                    int hs = pos;
                    var part = new InterpPart { IsHole = true, Start = hs };
                    if (!SkipHole(part)) { Fail("CS8076: Missing close delimiter '}' for interpolated expression started with '{'", hs - 1, hs); part.End = pos; parts.Add(part); var u = Make(CTk.InterpStr, s); u.Parts = parts; u.Unterminated = true; return u; }
                    parts.Add(part);
                    continue;
                }
                if (interp && c == '}')
                {
                    if (pos + 1 < src.Length && src[pos + 1] == '}') { sb.Append('}'); pos += 2; continue; }
                    Fail("CS8086: A '}' character must be escaped (by doubling) in an interpolated string.", pos, pos + 1);
                    pos++;
                    continue;
                }
                if (c == '\\' && !verbatim)
                {
                    int es = pos;
                    pos++;
                    if (pos >= src.Length) continue;
                    char e = src[pos++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'v': sb.Append('\v'); break;
                        case 'a': sb.Append('\a'); break;
                        case '0': sb.Append('\0'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case '\\': sb.Append('\\'); break;
                        case 'u':
                            if (pos + 4 <= src.Length && IsHex(src[pos]) && IsHex(src[pos + 1]) && IsHex(src[pos + 2]) && IsHex(src[pos + 3]))
                            {
                                sb.Append((char)System.Convert.ToInt32(src.Substring(pos, 4), 16));
                                pos += 4;
                            }
                            else Fail("CS1009: Unrecognized escape sequence", es, pos);
                            break;
                        default:
                            Fail("CS1009: Unrecognized escape sequence", es, pos);
                            break;
                    }
                    continue;
                }
                sb.Append(c);
                pos++;
            }
            FlushText();
            var t = Make(interp ? CTk.InterpStr : CTk.StringLit, s);
            t.StrValue = sb.ToString();
            t.Parts = parts;
            t.Verbatim = verbatim;
            return t;
        }

        /// <summary>Skips an interpolation hole up to its closing '}' (nested braces, strings, char literals). pos ends after '}'.</summary>
        bool SkipHole(InterpPart part)
        {
            int depth = 0;
            while (pos < src.Length)
            {
                char c = src[pos];
                if (c == '\n' || c == '\r') return false;
                if (c == '"' || c == '\'')
                {
                    char q = c; pos++;
                    while (pos < src.Length && src[pos] != q && src[pos] != '\n') { if (src[pos] == '\\') pos++; pos++; }
                    pos++;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == '}')
                {
                    if (depth == 0) { if (part.End == 0) part.End = pos; pos++; return true; }
                    depth--;
                }
                else if ((c == ':' || c == ',') && depth == 0 && part.End == 0)
                {
                    if (!(c == ':' && pos + 1 < src.Length && src[pos + 1] == ':'))
                    {
                        part.End = pos; part.HasFormat = true; part.FormatStart = pos;
                    }
                }
                pos++;
            }
            return false;
        }

        CsToken CharLit(int s)
        {
            pos++;
            var sb = new StringBuilder();
            while (pos < src.Length && src[pos] != '\'' && src[pos] != '\n')
            {
                if (src[pos] == '\\' && pos + 1 < src.Length)
                {
                    char e = src[pos + 1];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        case '0': sb.Append('\0'); break;
                        default: sb.Append(e); break;
                    }
                    pos += 2;
                    continue;
                }
                sb.Append(src[pos]);
                pos++;
            }
            bool closed = pos < src.Length && src[pos] == '\'';
            if (closed) pos++;
            var t = Make(CTk.CharLit, s);
            t.StrValue = sb.ToString();
            t.Unterminated = !closed;
            if (!closed) Fail("CS1010: Newline in constant", s, pos);
            else if (sb.Length != 1) Fail(sb.Length == 0 ? "CS1011: Empty character literal" : "CS1012: Too many characters in character literal", s, pos);
            return t;
        }
    }
}
