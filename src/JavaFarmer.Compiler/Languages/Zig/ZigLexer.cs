using System;
using System.Collections.Generic;
using System.Text;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal enum ZTk
    {
        Ident,       // foo
        Builtin,     // @foo
        Keyword,     // const, fn, ...
        Int,         // 12, 0x1f, 1_000
        Float,       // 1.5, 1e3
        String,      // "..."
        MultiString, // \\... (one line)
        Char,        // 'a'
        Op,          // operators and punctuation
        Comment,     // // ... (also /// and //!)
        Whitespace,
        Invalid,
        Eof
    }

    internal sealed class ZToken
    {
        public ZTk Kind;
        public string Text;          // source text
        public int Start, End;       // [Start, End)
        public string Value;         // decoded string literal value / identifier name
        public long IntValue;
        public double FloatValue;
        public bool BadEscape;       // string literal with an invalid escape
        public bool Unterminated;
        public bool HasNewlineBefore;
        public override string ToString() => Kind + " '" + Text + "' @" + Start;
    }

    /// <summary>
    /// Zig tokenizer (lib/std/zig/tokenizer.zig of Zig 0.17.1, the subset JavaFarmer needs). Never throws: bad input
    /// becomes Invalid tokens. Comments and whitespace are kept as tokens for the highlighter; the parser skips them.
    /// </summary>
    internal sealed class ZigLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "addrspace", "align", "allowzero", "and", "anyframe", "anytype", "asm", "break", "callconv", "catch",
            "comptime", "const", "continue", "defer", "else", "enum", "errdefer", "error", "export", "extern", "fn",
            "for", "if", "inline", "noalias", "noinline", "nosuspend", "opaque", "or", "orelse", "packed", "pub",
            "resume", "return", "linksection", "struct", "suspend", "switch", "test", "threadlocal", "try", "union",
            "unreachable", "var", "volatile", "while",
        };

        // longest first within each starting character
        static readonly string[] Ops =
        {
            "<<|=", "<<=", "<<|", ">>=", "+%=", "-%=", "*%=", "+|=", "-|=", "*|=", "...",
            "**", "++", "+=", "+%", "+|", "-=", "-%", "-|", "->", "*=", "*%", "*|", "/=", "%=", "==", "=>", "!=", "<=", ">=",
            "<<", ">>", "&=", "|=", "^=", "||", "&&", ".*", ".?", "..",
            "+", "-", "*", "/", "%", "=", "!", "<", ">", "&", "|", "^", "~", "(", ")", "{", "}", "[", "]", ";", ",", ".", ":", "?",
        };

        /// <summary>Ops grouped by their first character (longest first), for the lexer's hot path.</summary>
        static readonly string[][] OpsByChar = BuildOpsByChar();
        static string[][] BuildOpsByChar()
        {
            var res = new string[128][];
            for (int ch = 0; ch < 128; ch++)
            {
                var l = new List<string>();
                foreach (var op in Ops) if (op[0] == ch) l.Add(op);
                res[ch] = l.Count > 0 ? l.ToArray() : null;
            }
            return res;
        }

        readonly string src;
        bool lastWsNewline;
        int pos;
        public ZigLexer(string source) { src = source ?? ""; }

        public List<ZToken> LexAll()
        {
            var res = new List<ZToken>();
            bool nl = true;
            while (true)
            {
                var t = Next();
                if (t.Kind == ZTk.Eof) { t.HasNewlineBefore = nl; res.Add(t); break; }
                if (t.Kind == ZTk.Whitespace) { if (lastWsNewline) nl = true; res.Add(t); continue; }
                t.HasNewlineBefore = nl;
                if (t.Kind != ZTk.Comment) nl = false; else nl = true;
                res.Add(t);
            }
            return res;
        }

        static bool IsIdStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        static bool IsIdChar(char c) => IsIdStart(c) || (c >= '0' && c <= '9');

        ZToken Tok(ZTk k, int start) => new ZToken { Kind = k, Start = start, End = pos, Text = src.Substring(start, pos - start) };

        ZToken Next()
        {
            if (pos >= src.Length) return new ZToken { Kind = ZTk.Eof, Start = src.Length, End = src.Length, Text = "" };
            int start = pos;
            char c = src[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                bool newline = false;
                while (pos < src.Length && (src[pos] == ' ' || src[pos] == '\t' || src[pos] == '\r' || src[pos] == '\n')) { if (src[pos] == '\n') newline = true; pos++; }
                lastWsNewline = newline;
                // no Text for whitespace (nobody reads it; saves an allocation per token on every keystroke)
                return new ZToken { Kind = ZTk.Whitespace, Start = start, End = pos, Text = newline ? "\n" : " " };
            }
            if (c == '/' && pos + 1 < src.Length && src[pos + 1] == '/')
            {
                while (pos < src.Length && src[pos] != '\n') pos++;
                if (pos > start && src[pos - 1] == '\r') pos--;
                return Tok(ZTk.Comment, start);
            }
            if (c == '\\' && pos + 1 < src.Length && src[pos + 1] == '\\')
            {
                while (pos < src.Length && src[pos] != '\n') pos++;
                var t = Tok(ZTk.MultiString, start);
                t.Value = t.Text.Substring(2);
                return t;
            }
            if (IsIdStart(c))
            {
                while (pos < src.Length && IsIdChar(src[pos])) pos++;
                var t = Tok(ZTk.Ident, start);
                t.Value = t.Text;
                if (Keywords.Contains(t.Text)) t.Kind = ZTk.Keyword;
                return t;
            }
            if (c == '@')
            {
                pos++;
                if (pos < src.Length && src[pos] == '"')
                {
                    // @"quoted identifier"
                    var s = StringBody(start);
                    s.Kind = ZTk.Ident;
                    s.Text = src.Substring(start, pos - start);
                    s.Start = start;
                    return s;
                }
                if (pos < src.Length && IsIdStart(src[pos]))
                {
                    while (pos < src.Length && IsIdChar(src[pos])) pos++;
                    var t = Tok(ZTk.Builtin, start);
                    t.Value = t.Text;
                    return t;
                }
                return Tok(ZTk.Invalid, start);
            }
            if (c >= '0' && c <= '9') return Number(start);
            if (c == '"') return StringBody(start);
            if (c == '\'')
            {
                pos++;
                var sb = new StringBuilder();
                bool bad = false;
                while (pos < src.Length && src[pos] != '\'' && src[pos] != '\n')
                {
                    if (src[pos] == '\\') { if (!Escape(sb)) bad = true; }
                    else sb.Append(src[pos++]);
                }
                bool closed = pos < src.Length && src[pos] == '\'';
                if (closed) pos++;
                var t = Tok(ZTk.Char, start);
                t.Unterminated = !closed;
                t.BadEscape = bad;
                t.Value = sb.ToString();
                if (t.Value.Length > 0) t.IntValue = char.ConvertToUtf32(t.Value, 0) ;
                return t;
            }
            var cands = c < 128 ? OpsByChar[c] : null;
            if (cands != null) foreach (var op in cands)
            {
                if (pos + op.Length > src.Length) continue;
                if (string.CompareOrdinal(src, pos, op, 0, op.Length) != 0) continue;
                // "a.?" / ".*" are fine; but ".." followed by digit is a range — ok either way
                pos += op.Length;
                return Tok(ZTk.Op, start);
            }
            pos++;
            if (char.IsHighSurrogate(c) && pos < src.Length && char.IsLowSurrogate(src[pos])) pos++;
            return Tok(ZTk.Invalid, start);
        }

        ZToken StringBody(int start)
        {
            // at the opening quote
            pos++;
            int scan = pos;
            while (scan < src.Length && src[scan] != '"' && src[scan] != '\n' && src[scan] != '\\') scan++;
            if (scan < src.Length && src[scan] == '"')
            {
                var value = src.Substring(pos, scan - pos);
                pos = scan + 1;
                var ft = Tok(ZTk.String, start);
                ft.Value = value;
                return ft;
            }
            var sb = new StringBuilder();
            bool bad = false;
            while (pos < src.Length && src[pos] != '"' && src[pos] != '\n')
            {
                if (src[pos] == '\\') { if (!Escape(sb)) bad = true; }
                else sb.Append(src[pos++]);
            }
            bool closed = pos < src.Length && src[pos] == '"';
            if (closed) pos++;
            var t = Tok(ZTk.String, start);
            t.Value = sb.ToString();
            t.Unterminated = !closed;
            t.BadEscape = bad;
            return t;
        }

        /// <summary>Decodes one escape sequence at pos (a backslash). False when it is invalid.</summary>
        bool Escape(StringBuilder sb)
        {
            pos++;
            if (pos >= src.Length || src[pos] == '\n') return false;
            char e = src[pos++];
            switch (e)
            {
                case 'n': sb.Append('\n'); return true;
                case 'r': sb.Append('\r'); return true;
                case 't': sb.Append('\t'); return true;
                case '\\': sb.Append('\\'); return true;
                case '\'': sb.Append('\''); return true;
                case '"': sb.Append('"'); return true;
                case 'x':
                    {
                        if (pos + 2 > src.Length) return false;
                        int v = 0;
                        for (int i = 0; i < 2; i++) { int h = Hex(src[pos + i]); if (h < 0) return false; v = v * 16 + h; }
                        pos += 2;
                        sb.Append((char)v);
                        return true;
                    }
                case 'u':
                    {
                        if (pos >= src.Length || src[pos] != '{') return false;
                        int j = pos + 1, v = 0, n = 0;
                        while (j < src.Length && src[j] != '}' && n < 7) { int h = Hex(src[j]); if (h < 0) return false; v = v * 16 + h; j++; n++; }
                        if (j >= src.Length || src[j] != '}' || n == 0 || v > 0x10FFFF) return false;
                        pos = j + 1;
                        try { sb.Append(char.ConvertFromUtf32(v)); } catch { return false; }
                        return true;
                    }
            }
            return false;
        }

        static int Hex(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        ZToken Number(int start)
        {
            int radix = 10;
            if (src[pos] == '0' && pos + 1 < src.Length && (src[pos + 1] == 'x' || src[pos + 1] == 'o' || src[pos + 1] == 'b'))
            {
                radix = src[pos + 1] == 'x' ? 16 : src[pos + 1] == 'o' ? 8 : 2;
                pos += 2;
            }
            bool isFloat = false;
            while (pos < src.Length)
            {
                char c = src[pos];
                if (c == '_' || IsIdChar(c) && !(radix == 10 && (c == 'e' || c == 'E')) && !(radix == 16 && (c == 'p' || c == 'P'))) { pos++; continue; }
                if (c == '.' && pos + 1 < src.Length && char.IsDigit(src[pos + 1]) && !isFloat) { isFloat = true; pos++; continue; }
                if ((radix == 10 && (c == 'e' || c == 'E')) || (radix == 16 && (c == 'p' || c == 'P')))
                {
                    isFloat = true;
                    pos++;
                    if (pos < src.Length && (src[pos] == '+' || src[pos] == '-')) pos++;
                    continue;
                }
                break;
            }
            var t = Tok(isFloat ? ZTk.Float : ZTk.Int, start);
            var digits = t.Text.Replace("_", "");
            if (radix != 10) digits = digits.Substring(2);
            if (isFloat)
            {
                if (radix == 10 && double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) t.FloatValue = d;
                else t.Kind = ZTk.Invalid;
            }
            else
            {
                long v = 0;
                bool ok = digits.Length > 0;
                foreach (var ch in digits)
                {
                    int dv = Hex(ch);
                    if (dv < 0 || dv >= radix) { ok = false; break; }
                    try { v = checked(v * radix + dv); } catch (OverflowException) { ok = false; break; }
                }
                if (!ok) t.Kind = ZTk.Invalid;
                t.IntValue = v;
            }
            return t;
        }
    }
}
