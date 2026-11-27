using System.Collections.Generic;
using System.Text;

namespace JavaFarmer.Compiler
{
    internal enum Tk
    {
        Identifier, Keyword, IntLit, DoubleLit, StringLit, CharLit, Op, Comment, Whitespace, Unknown, EOF
    }

    internal sealed class Token
    {
        public Tk Kind;
        public string Text;     // raw source text (for literals: the raw literal)
        public int Start, End;  // [Start, End)
        public string StrValue; // decoded value for string literals
        public bool Unterminated;
        public override string ToString() => Kind + ":" + Text;
    }

    /// <summary>Java lexer. In tolerant mode (highlighting) it never fails; otherwise the first lexical error is reported.</summary>
    internal sealed class Lexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue",
            "default", "do", "double", "else", "enum", "extends", "final", "finally", "float", "for", "goto", "if",
            "implements", "import", "instanceof", "int", "interface", "long", "native", "new", "package", "private",
            "protected", "public", "return", "short", "static", "strictfp", "super", "switch", "synchronized", "this",
            "throw", "throws", "transient", "try", "void", "volatile", "while", "true", "false", "null", "var", "yield", "record"
        };

        // Longest first.
        static readonly string[] Operators =
        {
            "<<=", "...", "->", "::", "++", "--", "&&", "||", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=",
            "&=", "|=", "^=", "<<", "(", ")", "{", "}", "[", "]", ";", ",", ".", "@", "=", ">", "<", "!", "~", "?", ":",
            "+", "-", "*", "/", "&", "|", "^", "%"
        };

        readonly string src;
        int pos;
        public Diagnostic Error;   // first lexical error (non-tolerant mode)

        public Lexer(string source) { src = source ?? ""; }

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
        public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        /// <summary>All tokens including whitespace and comments (used by the highlighter).</summary>
        public List<Token> LexAll()
        {
            var list = new List<Token>();
            while (pos < src.Length) list.Add(Next());
            return list;
        }

        /// <summary>Significant tokens only, terminated by EOF.</summary>
        public List<Token> LexSignificant()
        {
            var list = new List<Token>();
            while (pos < src.Length)
            {
                var t = Next();
                if (t.Kind == Tk.Whitespace || t.Kind == Tk.Comment) continue;
                list.Add(t);
            }
            list.Add(new Token { Kind = Tk.EOF, Text = "", Start = src.Length, End = src.Length });
            return list;
        }

        void Fail(string msg, int s, int e)
        {
            if (Error == null) Error = new Diagnostic(msg, s, e);
        }

        Token Make(Tk k, int s) => new Token { Kind = k, Start = s, End = pos, Text = src.Substring(s, pos - s) };

        Token Next()
        {
            int s = pos;
            char c = src[pos];
            if (char.IsWhiteSpace(c))
            {
                while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
                return Make(Tk.Whitespace, s);
            }
            if (c == '/' && pos + 1 < src.Length && src[pos + 1] == '/')
            {
                while (pos < src.Length && src[pos] != '\n') pos++;
                return Make(Tk.Comment, s);
            }
            if (c == '/' && pos + 1 < src.Length && src[pos + 1] == '*')
            {
                int close = src.IndexOf("*/", pos + 2, System.StringComparison.Ordinal);
                if (close < 0)
                {
                    pos = src.Length;
                    Fail("unclosed comment", s, s + 2);
                    var t = Make(Tk.Comment, s); t.Unterminated = true; return t;
                }
                pos = close + 2;
                return Make(Tk.Comment, s);
            }
            if (IsIdentStart(c))
            {
                while (pos < src.Length && IsIdentPart(src[pos])) pos++;
                var t = Make(Tk.Identifier, s);
                if (Keywords.Contains(t.Text)) t.Kind = Tk.Keyword;
                return t;
            }
            if (char.IsDigit(c) || (c == '.' && pos + 1 < src.Length && char.IsDigit(src[pos + 1])))
                return Number(s);
            if (c == '"') return StringLit(s);
            if (c == '\'') return CharLit(s);
            foreach (var op in Operators)
            {
                if (string.CompareOrdinal(src, pos, op, 0, op.Length) == 0)
                {
                    pos += op.Length;
                    return Make(Tk.Op, s);
                }
            }
            pos++;
            Fail("illegal character: '" + c + "'", s, pos);
            return Make(Tk.Unknown, s);
        }

        Token Number(int s)
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
                if (pos < src.Length && src[pos] == '.' && !(pos + 1 < src.Length && src[pos + 1] == '.'))
                {
                    // "1.foo" is not a member access on an int in Java; it is the double 1. followed by an identifier.
                    isDouble = true; pos++;
                    while (pos < src.Length && (char.IsDigit(src[pos]) || src[pos] == '_')) pos++;
                }
                if (pos < src.Length && (src[pos] == 'e' || src[pos] == 'E'))
                {
                    int save = pos;
                    pos++;
                    if (pos < src.Length && (src[pos] == '+' || src[pos] == '-')) pos++;
                    if (pos < src.Length && char.IsDigit(src[pos]))
                    {
                        isDouble = true;
                        while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                    }
                    else { pos = save; }
                }
            }
            if (pos < src.Length && "dDfFlL".IndexOf(src[pos]) >= 0)
            {
                char suf = char.ToLowerInvariant(src[pos]);
                pos++;
                if (suf != 'l') isDouble = true;
            }
            // swallow trailing identifier chars so "12abc" is one bad token
            while (pos < src.Length && IsIdentPart(src[pos])) pos++;
            return Make(isDouble ? Tk.DoubleLit : Tk.IntLit, s);
        }

        static bool IsHex(char c) => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        Token StringLit(int s)
        {
            // Text block?
            if (string.CompareOrdinal(src, pos, "\"\"\"", 0, 3) == 0)
            {
                int close = src.IndexOf("\"\"\"", pos + 3, System.StringComparison.Ordinal);
                pos = close < 0 ? src.Length : close + 3;
                Fail("text blocks are not supported in JavaFarmer", s, pos);
                var tb = Make(Tk.StringLit, s); tb.StrValue = ""; tb.Unterminated = close < 0; return tb;
            }
            pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= src.Length || src[pos] == '\n' || src[pos] == '\r')
                {
                    Fail("unclosed string literal", s, pos);
                    var u = Make(Tk.StringLit, s); u.StrValue = sb.ToString(); u.Unterminated = true; return u;
                }
                char c = src[pos];
                if (c == '"') { pos++; break; }
                if (c == '\\')
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
                        case 's': sb.Append(' '); break;
                        case '0': sb.Append('\0'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case '\\': sb.Append('\\'); break;
                        case 'u':
                            {
                                while (pos < src.Length && src[pos] == 'u') pos++;
                                if (pos + 4 <= src.Length && IsHex(src[pos]) && IsHex(src[pos + 1]) && IsHex(src[pos + 2]) && IsHex(src[pos + 3]))
                                {
                                    sb.Append((char)System.Convert.ToInt32(src.Substring(pos, 4), 16));
                                    pos += 4;
                                }
                                else Fail("illegal unicode escape", es, pos);
                                break;
                            }
                        default:
                            Fail("illegal escape character in string literal", es, pos);
                            break;
                    }
                    continue;
                }
                sb.Append(c);
                pos++;
            }
            var t = Make(Tk.StringLit, s);
            t.StrValue = sb.ToString();
            return t;
        }

        Token CharLit(int s)
        {
            pos++;
            while (pos < src.Length && src[pos] != '\'' && src[pos] != '\n')
            {
                if (src[pos] == '\\') pos++;
                pos++;
            }
            bool closed = pos < src.Length && src[pos] == '\'';
            if (closed) pos++;
            if (pos > src.Length) pos = src.Length;
            var t = Make(Tk.CharLit, s);
            t.Unterminated = !closed;
            if (!closed) Fail("unclosed character literal", s, pos);
            return t;
        }
    }
}
