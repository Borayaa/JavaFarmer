using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JavaFarmer.Compiler
{
    internal enum RTk { Ident, Keyword, Int, Float, Str, Char, Lifetime, Punct, Comment, Whitespace, Unknown, EOF }

    /// <summary>One Rust token. Ident: Text is the name (raw identifiers r#x have Raw = true and Text "x").</summary>
    internal sealed class RToken
    {
        public RTk Kind;
        public int Start, End;
        public string Text;          // source text (Ident: the name without r#; Str: the decoded value; Punct: the operator)
        public string Suffix;        // literal suffix (i32, f64, ...) or null
        public bool Raw;             // raw identifier / raw string
        public bool Doc;             // doc comment
        public bool Block;           // block comment
        public bool Unterminated;    // string / comment without its end
        public bool Byte;            // b"..." / b'x'
        public string Error;         // lexical problem (reported by the parser)
        public ulong IntVal;
        public bool IntOverflow;
        public double FloatVal;
        public override string ToString() => Kind + ":" + Text;
    }

    /// <summary>Rust (edition 2024) lexer used by the parser and the highlighter. Never throws; covers the whole text.</summary>
    internal sealed class RustLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for",
            "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static",
            "struct", "super", "trait", "true", "type", "unsafe", "use", "where", "while", "gen",
            // reserved
            "abstract", "become", "box", "do", "final", "macro", "override", "priv", "typeof", "unsized", "virtual", "yield", "try",
        };

        static readonly string[] Puncts =
        {
            "<<=", ">>=", "...", "..=",
            "::", "->", "=>", "==", "!=", "<=", ">=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "^=", "&=", "|=", "<<", ">>", "..",
            "+", "-", "*", "/", "%", "^", "!", "&", "|", "=", "<", ">", "@", ".", ",", ";", ":", "#", "$", "?", "~",
            "{", "}", "[", "]", "(", ")",
        };

        /// <summary>Puncts by first character (longest first, as in Puncts).</summary>
        static readonly string[][] PunctsByChar = BuildPunctIndex();

        static string[][] BuildPunctIndex()
        {
            var idx = new string[128][];
            for (int c = 0; c < 128; c++)
            {
                var l = Puncts.Where(p => p[0] == c).ToArray();
                idx[c] = l.Length > 0 ? l : null;
            }
            return idx;
        }

        readonly string s;
        int i;
        public RustLexer(string source) { s = source ?? ""; }

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
        public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>All tokens including whitespace and comments (they cover the text without gaps), then EOF.</summary>
        public List<RToken> LexAll()
        {
            var res = new List<RToken>();
            while (i < s.Length)
            {
                int start = i;
                RToken t;
                try { t = Next(); }
                catch (Exception) { t = new RToken { Kind = RTk.Unknown, Start = start, End = Math.Max(start + 1, i), Text = "" }; i = t.End; }
                if (t.End <= t.Start) { t.End = start + 1; i = t.End; }
                if (t.End > s.Length) { t.End = s.Length; i = s.Length; }
                if (t.Text == null) t.Text = s.Substring(t.Start, t.End - t.Start);
                res.Add(t);
            }
            res.Add(new RToken { Kind = RTk.EOF, Start = s.Length, End = s.Length, Text = "" });
            return res;
        }

        char At(int k) => k < s.Length ? s[k] : '\0';

        RToken Next()
        {
            int start = i;
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                return new RToken { Kind = RTk.Whitespace, Start = start, End = i, Text = "" };   // never read: no substring
            }
            if (c == '/' && At(i + 1) == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                bool doc = (At(start + 2) == '/' && At(start + 3) != '/') || At(start + 2) == '!';
                return new RToken { Kind = RTk.Comment, Start = start, End = i, Doc = doc };
            }
            if (c == '/' && At(i + 1) == '*')
            {
                int depth = 0;
                bool done = false;
                while (i < s.Length)
                {
                    if (s[i] == '/' && At(i + 1) == '*') { depth++; i += 2; continue; }
                    if (s[i] == '*' && At(i + 1) == '/') { depth--; i += 2; if (depth == 0) { done = true; break; } continue; }
                    i++;
                }
                bool doc = (At(start + 2) == '*' && At(start + 3) != '*' && At(start + 3) != '/') || At(start + 2) == '!';
                return new RToken { Kind = RTk.Comment, Start = start, End = i, Block = true, Doc = doc, Unterminated = !done, Error = done ? null : "unterminated block comment" };
            }
            // raw identifiers / raw strings / byte strings
            if (c == 'r' && At(i + 1) == '#' && IsIdentStart(At(i + 2)))
            {
                i += 2;
                int ns = i;
                while (i < s.Length && IsIdentPart(s[i])) i++;
                return new RToken { Kind = RTk.Ident, Start = start, End = i, Text = s.Substring(ns, i - ns), Raw = true };
            }
            if ((c == 'r' && (At(i + 1) == '"' || (At(i + 1) == '#' && (At(i + 2) == '"' || At(i + 2) == '#'))))
                || (c == 'b' && At(i + 1) == 'r' && (At(i + 2) == '"' || At(i + 2) == '#')))
            {
                bool bytes = c == 'b';
                i += bytes ? 2 : 1;
                int hashes = 0;
                while (At(i) == '#') { hashes++; i++; }
                if (At(i) != '"') return new RToken { Kind = RTk.Unknown, Start = start, End = i, Error = "unknown start of token" };
                i++;
                int bodyStart = i;
                while (i < s.Length)
                {
                    if (s[i] == '"')
                    {
                        int k = 0;
                        while (k < hashes && At(i + 1 + k) == '#') k++;
                        if (k == hashes)
                        {
                            var val = s.Substring(bodyStart, i - bodyStart);
                            i += 1 + hashes;
                            return Suffixed(new RToken { Kind = RTk.Str, Start = start, Text = val, Raw = true, Byte = bytes });
                        }
                    }
                    i++;
                }
                return new RToken { Kind = RTk.Str, Start = start, End = i, Text = s.Substring(bodyStart, i - bodyStart), Raw = true, Unterminated = true, Error = "unterminated raw string", Byte = bytes };
            }
            if (c == 'b' && (At(i + 1) == '"' || At(i + 1) == '\''))
            {
                i++;
                var t = c == 'b' && At(i) == '"' ? StringLit(start) : CharOrLifetime(start);
                t.Byte = true;
                return t;
            }
            if (c == 'c' && At(i + 1) == '"') { i++; var t = StringLit(start); t.Byte = true; return t; }
            if (IsIdentStart(c))
            {
                while (i < s.Length && IsIdentPart(s[i])) i++;
                var text = s.Substring(start, i - start);
                return new RToken { Kind = Keywords.Contains(text) ? RTk.Keyword : RTk.Ident, Start = start, End = i, Text = text };
            }
            if (char.IsDigit(c)) return Number(start);
            if (c == '"') return StringLit(start);
            if (c == '\'') return CharOrLifetime(start);
            var cands = c < 128 ? PunctsByChar[c] : null;
            if (cands != null)
                foreach (var p in cands)
                    if (p.Length == 1 || string.CompareOrdinal(s, i, p, 0, p.Length) == 0)
                    {
                        i += p.Length;
                        return new RToken { Kind = RTk.Punct, Start = start, End = i, Text = p };
                    }
            i++;
            if (char.IsHighSurrogate(c) && i < s.Length && char.IsLowSurrogate(s[i])) i++;
            return new RToken { Kind = RTk.Unknown, Start = start, End = i, Error = "unknown start of token" };
        }

        RToken Suffixed(RToken t)
        {
            if (i < s.Length && IsIdentStart(s[i]))
            {
                int ss = i;
                while (i < s.Length && IsIdentPart(s[i])) i++;
                t.Suffix = s.Substring(ss, i - ss);
            }
            t.End = i;
            return t;
        }

        RToken Number(int start)
        {
            bool isFloat = false;
            int radix = 10;
            if (s[i] == '0' && (At(i + 1) == 'x' || At(i + 1) == 'o' || At(i + 1) == 'b'))
            {
                radix = At(i + 1) == 'x' ? 16 : At(i + 1) == 'o' ? 8 : 2;
                i += 2;
                while (i < s.Length && (Uri.IsHexDigit(s[i]) || s[i] == '_') && !(radix != 16 && (s[i] == 'e' || s[i] == 'E' || s[i] == 'f' || s[i] == 'b'))) i++;
            }
            else
            {
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) i++;
                if (At(i) == '.' && At(i + 1) != '.' && !IsIdentStart(At(i + 1)))
                {
                    isFloat = true;
                    i++;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) i++;
                }
                if ((At(i) == 'e' || At(i) == 'E') && (char.IsDigit(At(i + 1)) || ((At(i + 1) == '+' || At(i + 1) == '-') && char.IsDigit(At(i + 2)))))
                {
                    isFloat = true;
                    i += 2;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) i++;
                }
            }
            int numEnd = i;
            var t = Suffixed(new RToken { Kind = isFloat ? RTk.Float : RTk.Int, Start = start });
            var digits = s.Substring(start, numEnd - start).Replace("_", "");
            if (t.Suffix == "f32" || t.Suffix == "f64") { if (radix == 10) isFloat = true; }
            if (isFloat)
            {
                t.Kind = RTk.Float;
                double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out t.FloatVal);
            }
            else
            {
                var body = radix == 10 ? digits : digits.Substring(2);
                ulong v = 0;
                foreach (var ch in body)
                {
                    int d = ch >= '0' && ch <= '9' ? ch - '0' : char.ToLowerInvariant(ch) - 'a' + 10;
                    if (d < 0 || d >= radix) { t.Error = "invalid digit for a base " + radix + " literal"; break; }
                    ulong nv = unchecked(v * (ulong)radix + (ulong)d);
                    if (nv / (ulong)radix != v && v != 0) t.IntOverflow = true;
                    if (v > (ulong.MaxValue - (ulong)d) / (ulong)radix) t.IntOverflow = true;
                    v = nv;
                }
                if (body.Length == 0) t.Error = "no valid digits found for number";
                t.IntVal = v;
            }
            t.Text = s.Substring(start, t.End - start);
            return t;
        }

        RToken StringLit(int start)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            string err = null;
            while (i < s.Length && s[i] != '"')
            {
                char ch = s[i];
                if (ch == '\\')
                {
                    char e = At(i + 1);
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
                        case '\n':
                            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                            break;
                        case 'x':
                            {
                                int v = 0, k = 0;
                                while (k < 2 && Uri.IsHexDigit(At(i))) { v = v * 16 + Convert.ToInt32(At(i).ToString(), 16); i++; k++; }
                                if (k < 2) err = err ?? "numeric character escape is too short";
                                sb.Append((char)v);
                                break;
                            }
                        case 'u':
                            {
                                if (At(i) != '{') { err = err ?? "incorrect unicode escape sequence"; break; }
                                i++;
                                int v = 0;
                                while (i < s.Length && s[i] != '}' && s[i] != '"') { if (Uri.IsHexDigit(s[i])) v = v * 16 + Convert.ToInt32(s[i].ToString(), 16); else if (s[i] != '_') err = err ?? "invalid character in unicode escape"; i++; }
                                if (At(i) == '}') i++;
                                try { sb.Append(char.ConvertFromUtf32(v)); } catch { err = err ?? "invalid unicode character escape"; }
                                break;
                            }
                        default:
                            err = err ?? "unknown character escape: `" + e + "`";
                            break;
                    }
                    continue;
                }
                sb.Append(ch);
                i++;
            }
            if (i >= s.Length)
                return new RToken { Kind = RTk.Str, Start = start, End = s.Length, Text = sb.ToString(), Unterminated = true, Error = "unterminated double quote string" };
            i++;
            var t = Suffixed(new RToken { Kind = RTk.Str, Start = start, Text = sb.ToString(), Error = err });
            return t;
        }

        RToken CharOrLifetime(int start)
        {
            // 'a' char, '\n' char, 'label lifetime
            i++;
            if (At(i) == '\\')
            {
                i++;
                if (i < s.Length) i++;
                while (i < s.Length && s[i] != '\'' && s[i] != '\n' && i - start < 12) i++;
                if (At(i) == '\'') { i++; return Suffixed(new RToken { Kind = RTk.Char, Start = start }); }
                return new RToken { Kind = RTk.Char, Start = start, End = i, Unterminated = true, Error = "unterminated character literal" };
            }
            if (i < s.Length && At(i + 1) == '\'' && s[i] != '\n')
            {
                i += 2;
                return Suffixed(new RToken { Kind = RTk.Char, Start = start });
            }
            if (i + 2 < s.Length && char.IsHighSurrogate(s[i]) && At(i + 2) == '\'') { i += 3; return new RToken { Kind = RTk.Char, Start = start, End = i }; }
            if (IsIdentStart(At(i)))
            {
                while (i < s.Length && IsIdentPart(s[i])) i++;
                return new RToken { Kind = RTk.Lifetime, Start = start, End = i };
            }
            return new RToken { Kind = RTk.Unknown, Start = start, End = i, Error = "unterminated character literal" };
        }
    }
}
