using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JavaFarmer.Compiler.CFamily
{
    /// <summary>C (C23) or C++ (C++23): one shared frontend, dialect switches in the parser and the binder.</summary>
    internal enum Dialect { C, Cpp }

    internal enum TK { Ident, Keyword, Int, Float, String, Char, Punct, Preproc, EOF }

    internal sealed class Token
    {
        public TK Kind;
        public string Text;          // source text (Punct/Keyword/Ident: the spelling)
        public int Start, End;
        public long IntVal;
        public double FloatVal;
        public string StrVal;        // decoded string / char literal value
        public string Suffix = "";   // number suffix (f, L, u ...)
        public bool BadEscape;
        public bool NewlineBefore;   // first token on its line
        public override string ToString() => Kind + " '" + Text + "' @" + Start;
    }

    internal sealed class LexResult
    {
        public List<Token> Tokens = new List<Token>();
        public List<IrComment> Comments = new List<IrComment>();
        public List<Diagnostic> Errors = new List<Diagnostic>();
    }

    /// <summary>Hand-written C/C++ lexer (no regex: it runs on every keystroke). Never throws.</summary>
    internal static class Lexer
    {
        public static readonly HashSet<string> CKeywords = new HashSet<string>
        {
            "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern",
            "float", "for", "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed",
            "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while",
            "bool", "true", "false", "nullptr", "constexpr", "static_assert", "typeof", "typeof_unqual", "alignas", "alignof",
            "thread_local", "_Bool", "_Generic", "_Static_assert", "_Noreturn", "_Atomic", "_Complex", "_Alignas", "_Alignof",
            "_Thread_local", "_BitInt", "_Decimal32", "_Decimal64", "_Decimal128",
        };

        public static readonly HashSet<string> CppKeywords = new HashSet<string>
        {
            "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char",
            "char8_t", "char16_t", "char32_t", "class", "compl", "concept", "const", "consteval", "constexpr", "constinit",
            "const_cast", "continue", "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do", "double",
            "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if",
            "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or",
            "or_eq", "private", "protected", "public", "register", "reinterpret_cast", "requires", "return", "short", "signed",
            "sizeof", "static", "static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local", "throw",
            "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using", "virtual", "void", "volatile",
            "wchar_t", "while", "xor", "xor_eq",
        };

        public static HashSet<string> Keywords(Dialect d) => d == Dialect.C ? CKeywords : CppKeywords;

        static readonly string[] Puncts3 = { "<<=", ">>=", "...", "->*", "<=>" };
        static readonly string[] Puncts2 = { "::", "->", "++", "--", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", ".*", "##" };
        const string Puncts1 = "{}[]();,.:?~!+-*/%<>=&|^#@\\$";
        static readonly string[] Single = MakeSingle();
        static string[] MakeSingle() { var a = new string[128]; foreach (var ch in Puncts1) a[ch] = ch.ToString(); return a; }

        public static bool IsIdentStart(char c) => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        public static bool IsIdentPart(char c) => IsIdentStart(c) || (c >= '0' && c <= '9');

        public static LexResult Lex(string src, Dialect d, bool collectComments = true)
        {
            var res = new LexResult();
            src = src ?? "";
            var kws = Keywords(d);
            int i = 0, n = src.Length;
            bool lineStart = true;
            while (i < n)
            {
                char c = src[i];
                if (c == '\n') { lineStart = true; i++; continue; }
                if (c == ' ' || c == '\t' || c == '\r' || c == '\f' || c == '\v') { i++; continue; }
                // comments
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    int s = i;
                    while (i < n && src[i] != '\n') i++;
                    if (collectComments) res.Comments.Add(new IrComment { Text = src.Substring(s + 2, i - s - 2).TrimEnd('\r'), Span = new Span(s, i) });
                    continue;
                }
                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    int s = i;
                    int e = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (e < 0)
                    {
                        res.Errors.Add(new Diagnostic("unterminated comment", s, Math.Min(n, s + 2)));
                        i = n;
                        continue;
                    }
                    i = e + 2;
                    if (collectComments)
                    {
                        bool doc = i - s > 4 && src[s + 2] == '*';
                        int bodyStart = doc ? s + 3 : s + 2;
                        res.Comments.Add(new IrComment { Text = src.Substring(bodyStart, Math.Max(0, e - bodyStart)), IsBlock = true, IsDoc = doc, Span = new Span(s, i) });
                    }
                    continue;
                }
                var tok = new Token { Start = i, NewlineBefore = lineStart };
                // preprocessor directive: the whole (continued) line is one token
                if (c == '#' && lineStart)
                {
                    int s = i;
                    while (i < n && src[i] != '\n')
                    {
                        if (src[i] == '\\' && i + 1 < n && src[i + 1] == '\n') { i += 2; continue; }
                        if (src[i] == '/' && i + 1 < n && (src[i + 1] == '/' || src[i + 1] == '*')) break;
                        i++;
                    }
                    int e = i;
                    while (e > s && (src[e - 1] == ' ' || src[e - 1] == '\t' || src[e - 1] == '\r')) e--;
                    tok.Kind = TK.Preproc; tok.Text = src.Substring(s, e - s); tok.End = e;
                    res.Tokens.Add(tok);
                    lineStart = false;
                    continue;
                }
                lineStart = false;
                if (IsIdentStart(c))
                {
                    int s = i;
                    while (i < n && IsIdentPart(src[i])) i++;
                    var w = src.Substring(s, i - s);
                    // string / char literal prefixes (u8"..", L'x') are not supported: lex them as identifiers + literal
                    tok.Kind = kws.Contains(w) ? TK.Keyword : TK.Ident;
                    tok.Text = w; tok.End = i;
                    res.Tokens.Add(tok);
                    continue;
                }
                if ((c >= '0' && c <= '9') || (c == '.' && i + 1 < n && src[i + 1] >= '0' && src[i + 1] <= '9'))
                {
                    LexNumber(src, ref i, tok, res);
                    res.Tokens.Add(tok);
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    LexQuoted(src, ref i, tok, res, c);
                    res.Tokens.Add(tok);
                    continue;
                }
                // punctuation
                string p = null;
                if (i + 2 < n) foreach (var x in Puncts3) if (x[0] == c && x[1] == src[i + 1] && x[2] == src[i + 2]) { p = x; break; }
                if (p == null && i + 1 < n) foreach (var x in Puncts2) if (x[0] == c && x[1] == src[i + 1]) { p = x; break; }
                if (p == null && Puncts1.IndexOf(c) >= 0) p = Single[c];
                if (p == null)
                {
                    res.Errors.Add(new Diagnostic("stray '" + (char.IsControl(c) ? "\\" + ((int)c).ToString(CultureInfo.InvariantCulture) : c.ToString()) + "' in program", i, i + 1));
                    i++;
                    continue;
                }
                tok.Kind = TK.Punct; tok.Text = p; tok.End = i + p.Length;
                i += p.Length;
                res.Tokens.Add(tok);
            }
            res.Tokens.Add(new Token { Kind = TK.EOF, Text = "", Start = n, End = n, NewlineBefore = true });
            return res;
        }

        static void LexNumber(string src, ref int i, Token tok, LexResult res)
        {
            int n = src.Length, s = i;
            bool isFloat = false, hex = false;
            if (src[i] == '0' && i + 1 < n && (src[i + 1] == 'x' || src[i + 1] == 'X'))
            {
                hex = true; i += 2;
                while (i < n && (Uri.IsHexDigit(src[i]) || src[i] == '\'')) i++;
            }
            else
            {
                while (i < n && ((src[i] >= '0' && src[i] <= '9') || src[i] == '\'')) i++;
                if (i < n && src[i] == '.') { isFloat = true; i++; while (i < n && src[i] >= '0' && src[i] <= '9') i++; }
                if (i < n && (src[i] == 'e' || src[i] == 'E'))
                {
                    int save = i;
                    i++;
                    if (i < n && (src[i] == '+' || src[i] == '-')) i++;
                    if (i < n && src[i] >= '0' && src[i] <= '9') { isFloat = true; while (i < n && src[i] >= '0' && src[i] <= '9') i++; }
                    else i = save;
                }
            }
            int numEnd = i;
            while (i < n && IsIdentPart(src[i])) i++;
            var suffix = src.Substring(numEnd, i - numEnd);
            var digits = src.Substring(s, numEnd - s).Replace("'", "");
            tok.Text = src.Substring(s, i - s);
            tok.End = i;
            tok.Suffix = suffix;
            var sl = suffix.ToLowerInvariant();
            if (!isFloat && !hex && (sl == "f")) { /* 1f is invalid C */ }
            if (isFloat || (!hex && (sl == "f" && false)))
            {
                tok.Kind = TK.Float;
                double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out tok.FloatVal);
                if (sl.Length > 0 && sl != "f" && sl != "l") res.Errors.Add(new Diagnostic("invalid suffix \"" + suffix + "\" on floating constant", s, i));
                return;
            }
            tok.Kind = TK.Int;
            long v = 0;
            bool overflow = false;
            try
            {
                if (hex) v = digits.Length > 2 ? Convert.ToInt64(digits.Substring(2), 16) : 0;
                else if (digits.Length > 1 && digits[0] == '0')
                {
                    foreach (var ch in digits) { if (ch < '0' || ch > '7') { res.Errors.Add(new Diagnostic("invalid digit \"" + ch + "\" in octal constant", s, i)); break; } }
                    v = Convert.ToInt64(digits, 8);
                }
                else if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out v)) overflow = true;
            }
            catch { overflow = true; }
            if (overflow) res.Errors.Add(new Diagnostic("integer constant is too large for its type", s, i));
            tok.IntVal = v;
            if (sl.Length > 0 && sl != "l" && sl != "ll" && sl != "u" && sl != "ul" && sl != "lu" && sl != "ull" && sl != "llu" && sl != "z" && sl != "uz")
                res.Errors.Add(new Diagnostic("invalid suffix \"" + suffix + "\" on integer constant", s, i));
        }

        static void LexQuoted(string src, ref int i, Token tok, LexResult res, char q)
        {
            int n = src.Length, s = i;
            i++;
            var sb = new StringBuilder();
            bool closed = false;
            while (i < n)
            {
                char c = src[i];
                if (c == q) { closed = true; i++; break; }
                if (c == '\n') break;
                if (c == '\\' && i + 1 < n)
                {
                    char e = src[i + 1];
                    i += 2;
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '0': sb.Append('\0'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case '?': sb.Append('?'); break;
                        case 'a': sb.Append('\a'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'v': sb.Append('\v'); break;
                        case '\n': break;
                        default: tok.BadEscape = true; sb.Append(e); break;
                    }
                    continue;
                }
                sb.Append(c);
                i++;
            }
            tok.Kind = q == '"' ? TK.String : TK.Char;
            tok.Text = src.Substring(s, i - s);
            tok.End = i;
            tok.StrVal = sb.ToString();
            if (!closed) res.Errors.Add(new Diagnostic("missing terminating " + q + " character", s, i));
            else if (q == '\'' && sb.Length == 0) res.Errors.Add(new Diagnostic("empty character constant", s, i));
        }
    }
}
