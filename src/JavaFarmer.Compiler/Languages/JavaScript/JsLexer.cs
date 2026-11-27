using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JavaFarmer.Compiler
{
    internal enum JsTok { Eof, Ident, Number, String, Template, Punct, Regex, BigInt, Error }

    /// <summary>One JavaScript token. Templates carry their cooked text parts and the tokens of each substitution.</summary>
    internal sealed class JsToken
    {
        public JsTok Kind;
        public string Text;          // identifier / punctuator text, raw text otherwise
        public int Start, End;
        /// <summary>A line terminator precedes this token (drives automatic semicolon insertion).</summary>
        public bool NL;
        public double Num;
        public bool IsFloat;         // the literal is written with a decimal point
        public string Str;           // cooked string value
        public string Error;         // Error tokens: the message
        // templates: Parts.Count == Exprs.Count + 1
        public List<string> Parts;
        public List<Span> PartSpans;
        public List<List<JsToken>> Exprs;
        public bool Unterminated;
        public Span Span => new Span(Start, End);
        public override string ToString() => Kind + ":" + Text;
    }

    /// <summary>
    /// ES2023 lexer (strict mode / module goal). Never throws: malformed input becomes an Error token with V8's message.
    /// Comments are collected separately (for IrComments.Attach). A '/' starts a regular expression literal where an
    /// expression may start (regex literals are rejected by the parser with a clear message). Template literals are
    /// one token whose substitutions are lexed into nested token lists.
    /// </summary>
    internal sealed class JsLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export",
            "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return",
            "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "let", "static",
            "yield", "await", "enum", "implements", "interface", "package", "private", "protected", "public", "of", "async",
        };

        /// <summary>Reserved words that can never be identifiers (strict-mode reserved words are handled by the parser).</summary>
        public static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export",
            "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return",
            "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "enum",
        };

        public static readonly HashSet<string> StrictReserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "implements", "interface", "let", "package", "private", "protected", "public", "static", "yield",
        };

        static readonly string[] Puncts =
        {
            ">>>=", "...", "===", "!==", "**=", "<<=", ">>=", ">>>", "&&=", "||=", "??=",
            "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "**", "<<", ">>",
            "{", "}", "(", ")", "[", "]", ";", ",", "<", ">", "+", "-", "*", "/", "%", "&", "|", "^", "!", "~", "?", ":", "=", ".",
        };

        /// <summary>Puncts by first character, in the order of Puncts (longest first), so Next() tries only a few.</summary>
        static readonly string[][] PunctsByChar = BuildPunctIndex();
        static string[][] BuildPunctIndex()
        {
            var idx = new string[128][];
            for (int ch = 0; ch < 128; ch++)
            {
                var l = new List<string>();
                foreach (var p in Puncts) if (p[0] == ch) l.Add(p);
                if (l.Count > 0) idx[ch] = l.ToArray();
            }
            return idx;
        }

        readonly string s;
        int i;
        readonly List<IrComment> comments;
        JsToken prev;
        int lastCodeEnd;

        JsLexer(string src, List<IrComment> comments) { s = src ?? ""; this.comments = comments; }

        /// <summary>Tokens of the whole source, ending with an Eof token.</summary>
        public static List<JsToken> Lex(string src, List<IrComment> comments)
        {
            var lx = new JsLexer(src, comments);
            var list = new List<JsToken>();
            lx.Run(list, false);
            return list;
        }

        /// <summary>Lexes until EOF (top level) or the '}' closing a template substitution (nested). Returns false at EOF when nested.</summary>
        bool Run(List<JsToken> into, bool nested)
        {
            int depth = 0;
            if (!nested && s.StartsWith("#!", StringComparison.Ordinal)) { while (i < s.Length && s[i] != '\n') i++; }
            while (true)
            {
                bool nl = SkipTrivia();
                if (i >= s.Length)
                {
                    into.Add(new JsToken { Kind = JsTok.Eof, Text = "", Start = s.Length, End = s.Length, NL = true });
                    return false;
                }
                char c = s[i];
                if (nested)
                {
                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        if (depth == 0)
                        {
                            into.Add(new JsToken { Kind = JsTok.Eof, Text = "}", Start = i, End = i, NL = nl });
                            i++;
                            return true;
                        }
                        depth--;
                    }
                }
                var t = Next(c);
                t.NL = nl;
                into.Add(t);
                prev = t;
                lastCodeEnd = t.End;
            }
        }

        static bool IsLineTerm(char c) => c == '\n' || c == '\r' || c == (char)0x2028 || c == (char)0x2029;

        /// <summary>Skips whitespace and comments; true when a line terminator was crossed.</summary>
        bool SkipTrivia()
        {
            bool nl = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (IsLineTerm(c)) { nl = true; i++; continue; }
                if (c == ' ' || c == '\t' || c == '\v' || c == '\f' || (c > 127 && char.IsWhiteSpace(c)) || c == (char)0xFEFF) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    int st = i;
                    i += 2;
                    while (i < s.Length && !IsLineTerm(s[i])) i++;
                    AddComment(st, i, s.Substring(st + 2, i - st - 2), false);
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int st = i;
                    int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (e < 0) return nl;   // unterminated: Next() reports it
                    for (int k = i; k < e; k++) if (IsLineTerm(s[k])) nl = true;
                    i = e + 2;
                    AddComment(st, i, s.Substring(st + 2, e - st - 2), true);
                    continue;
                }
                break;
            }
            return nl;
        }

        void AddComment(int st, int en, string text, bool block)
        {
            if (comments == null) return;
            var c = new IrComment(text, block) { Span = new Span(st, en) };
            if (block && text.StartsWith("*") && text.Length > 1 && text[1] != '*') c.IsDoc = true;
            int from = Math.Max(lastCodeEnd, comments.Count > 0 ? comments[comments.Count - 1].Span.End : 0);
            int lines = 0;
            for (int k = from; k < st && k < s.Length; k++) if (s[k] == '\n') lines++;
            c.BlankLinesBefore = from == 0 ? 0 : Math.Max(0, lines - 1);
            comments.Add(c);
        }

        JsToken Err(int st, int en, string msg)
        {
            en = Math.Max(st + 1, Math.Min(en, s.Length));
            st = Math.Min(st, s.Length);
            return new JsToken { Kind = JsTok.Error, Start = st, End = en, Text = st < s.Length ? s.Substring(st, Math.Min(en, s.Length) - st) : "", Error = msg };
        }

        static bool IdStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '$' || (c > 127 && char.IsLetter(c));
        static bool IdPart(char c) => IdStart(c) || (c >= '0' && c <= '9') || (c > 127 && (char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || char.GetUnicodeCategory(c) == UnicodeCategory.ConnectorPunctuation));

        bool RegexAllowed()
        {
            if (prev == null) return true;
            switch (prev.Kind)
            {
                case JsTok.Number: case JsTok.String: case JsTok.Template: case JsTok.Regex: case JsTok.BigInt: return false;
                case JsTok.Ident:
                    switch (prev.Text)
                    {
                        case "return": case "typeof": case "instanceof": case "in": case "of": case "new": case "delete": case "void":
                        case "throw": case "case": case "do": case "else": case "yield": case "await":
                            return true;
                    }
                    return false;
                case JsTok.Punct:
                    return !(prev.Text == ")" || prev.Text == "]" || prev.Text == "}");
            }
            return true;
        }

        JsToken Next(char c)
        {
            int st = i;
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { i = s.Length; return Err(st, st + 2, "Invalid or unexpected token"); }
            if (IdStart(c))
            {
                while (i < s.Length && IdPart(s[i])) i++;
                if (i < s.Length && s[i] == '\\') { int e = i; while (e < s.Length && !char.IsWhiteSpace(s[e])) e++; i = e; return Err(st, e, "Invalid or unexpected token"); }
                return new JsToken { Kind = JsTok.Ident, Text = s.Substring(st, i - st), Start = st, End = i };
            }
            if ((c >= '0' && c <= '9') || (c == '.' && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '9')) return Number();
            if (c == '"' || c == '\'') return Str(c);
            if (c == '`') return Template();
            if (c == '/' && RegexAllowed()) return Regex();
            var cands = c < 128 ? PunctsByChar[c] : null;
            if (cands != null) foreach (var p in cands)
            {
                if (p.Length > 1 && string.CompareOrdinal(s, i, p, 0, p.Length) != 0) continue;
                if (p == "?." && i + 2 < s.Length && s[i + 2] >= '0' && s[i + 2] <= '9') continue;   // a ?.5 : b
                i += p.Length;
                return new JsToken { Kind = JsTok.Punct, Text = p, Start = st, End = i };
            }
            i += char.IsHighSurrogate(c) && i + 1 < s.Length ? 2 : 1;
            return Err(st, i, "Invalid or unexpected token");
        }

        JsToken Number()
        {
            int st = i;
            // fast path: a plain decimal integer (1, 42) not followed by anything a longer literal could use
            if (s[i] >= '1' && s[i] <= '9' || (s[i] == '0' && (i + 1 >= s.Length || !(s[i + 1] >= '0' && s[i + 1] <= '9'))))
            {
                int k = i;
                double v = 0;
                while (k < s.Length && s[k] >= '0' && s[k] <= '9' && k - i < 15) { v = v * 10 + (s[k] - '0'); k++; }
                if (k >= s.Length || !(IdPart(s[k]) || s[k] == '.' || s[k] == '_' || s[k] == '\\' || (s[k] >= '0' && s[k] <= '9')))
                {
                    i = k;
                    return new JsToken { Kind = JsTok.Number, Text = s.Substring(st, k - st), Start = st, End = k, Num = v, IsFloat = false };
                }
            }
            var digits = new StringBuilder();
            bool bad = false;
            string sepError = null;
            void Digits(Func<char, bool> ok)
            {
                bool lastSep = false, any = false;
                while (i < s.Length && (ok(s[i]) || s[i] == '_'))
                {
                    if (s[i] == '_')
                    {
                        if (lastSep && sepError == null) sepError = "Only one underscore is allowed as numeric separator";
                        if (!any && sepError == null) sepError = "Numeric separators are not allowed here";
                        lastSep = true; i++; continue;
                    }
                    digits.Append(s[i]); lastSep = false; any = true; i++;
                }
                if (lastSep && sepError == null) sepError = "Numeric separators are not allowed at the end of numeric literals";
            }
            double value = 0;
            bool radixLit = false;
            if (s[i] == '0' && i + 1 < s.Length && "xXoObB".IndexOf(s[i + 1]) >= 0)
            {
                radixLit = true;
                char k = char.ToLowerInvariant(s[i + 1]);
                i += 2;
                int radix = k == 'x' ? 16 : k == 'o' ? 8 : 2;
                Digits(ch => radix == 16 ? Uri.IsHexDigit(ch) : radix == 8 ? ch >= '0' && ch <= '7' : ch == '0' || ch == '1');
                if (digits.Length == 0) bad = true;
                foreach (var ch in digits.ToString()) value = value * radix + Convert.ToInt32(ch.ToString(), 16);
            }
            else
            {
                bool legacyOctal = false, leadingZero = false;
                if (s[i] == '0' && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '9')
                {
                    int k = i + 1;
                    bool allOct = true;
                    while (k < s.Length && s[k] >= '0' && s[k] <= '9') { if (s[k] > '7') allOct = false; k++; }
                    if (allOct) legacyOctal = true; else leadingZero = true;
                }
                Digits(ch => ch >= '0' && ch <= '9');
                if (i < s.Length && s[i] == '.' && !legacyOctal)
                {
                    i++;
                    digits.Append('.');
                    Digits(ch => ch >= '0' && ch <= '9');
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E') && !legacyOctal)
                {
                    i++;
                    var exp = new StringBuilder("e");
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) exp.Append(s[i++]);
                    int ds = i;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') exp.Append(s[i++]);
                    if (i == ds) bad = true;
                    digits.Append(exp);
                }
                if (legacyOctal) return Err(st, i, "Octal literals are not allowed in strict mode.");
                if (leadingZero) return Err(st, i, "Decimals with leading zeros are not allowed in strict mode.");
                var txt = digits.ToString();
                if (txt.StartsWith(".")) txt = "0" + txt;
                if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) bad = true;
            }
            if (i < s.Length && s[i] == 'n')
            {
                i++;
                if (i < s.Length && IdPart(s[i])) { while (i < s.Length && IdPart(s[i])) i++; return Err(st, i, "Invalid or unexpected token"); }
                return new JsToken { Kind = JsTok.BigInt, Text = s.Substring(st, i - st), Start = st, End = i };
            }
            if (i < s.Length && (IdStart(s[i]) || (s[i] >= '0' && s[i] <= '9'))) return Err(i, i + 1, "Invalid or unexpected token");
            if (sepError != null) return Err(st, i, sepError);
            if (bad) return Err(st, i, "Invalid or unexpected token");
            var raw = s.Substring(st, i - st);
            return new JsToken { Kind = JsTok.Number, Text = raw, Start = st, End = i, Num = value, IsFloat = !radixLit && raw.IndexOf('.') >= 0 };
        }

        /// <summary>Reads one escape sequence after the backslash (i at the char after it). Null on error (msg set).</summary>
        string Escape(out string msg, bool template)
        {
            msg = null;
            if (i >= s.Length) { msg = "Invalid or unexpected token"; return null; }
            char e = s[i++];
            switch (e)
            {
                case 'n': return "\n";
                case 't': return "\t";
                case 'r': return "\r";
                case 'b': return "\b";
                case 'f': return "\f";
                case 'v': return "\v";
                case '\r': if (i < s.Length && s[i] == '\n') i++; return "";
                case '\n': return "";
                case '0':
                    if (i < s.Length && s[i] >= '0' && s[i] <= '9') { msg = template ? "Octal escape sequences are not allowed in template strings" : "Octal escape sequences are not allowed in strict mode."; return null; }
                    return "\0";
                case 'x':
                    if (i + 2 <= s.Length && Uri.IsHexDigit(s[i]) && Uri.IsHexDigit(s[i + 1]))
                    {
                        var v = (char)Convert.ToInt32(s.Substring(i, 2), 16);
                        i += 2;
                        return v.ToString();
                    }
                    msg = "Invalid hexadecimal escape sequence";
                    return null;
                case 'u':
                    if (i < s.Length && s[i] == '{')
                    {
                        int close = s.IndexOf('}', i);
                        if (close > i + 1)
                        {
                            var hex = s.Substring(i + 1, close - i - 1);
                            bool ok = hex.Length <= 6;
                            foreach (var ch in hex) if (!Uri.IsHexDigit(ch)) ok = false;
                            if (ok)
                            {
                                int cp = Convert.ToInt32(hex, 16);
                                if (cp <= 0x10FFFF) { i = close + 1; return char.ConvertFromUtf32(cp >= 0xD800 && cp <= 0xDFFF ? 0xFFFD : cp); }
                            }
                        }
                        msg = "Invalid Unicode escape sequence";
                        return null;
                    }
                    if (i + 4 <= s.Length && Uri.IsHexDigit(s[i]) && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]) && Uri.IsHexDigit(s[i + 3]))
                    {
                        var v = (char)Convert.ToInt32(s.Substring(i, 4), 16);
                        i += 4;
                        return v.ToString();
                    }
                    msg = "Invalid Unicode escape sequence";
                    return null;
                default:
                    if (IsLineTerm(e)) return "";
                    if (e >= '1' && e <= '9')
                    {
                        msg = e >= '8' ? (template ? "\\8 and \\9 are not allowed in template strings." : "\\8 and \\9 are not allowed in strict mode.")
                            : template ? "Octal escape sequences are not allowed in template strings" : "Octal escape sequences are not allowed in strict mode.";
                        return null;
                    }
                    return e.ToString();
            }
        }

        JsToken Str(char q)
        {
            int st = i;
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length || s[i] == '\n' || s[i] == '\r') return Err(st, i, "Invalid or unexpected token");
                char c = s[i];
                if (c == q) { i++; break; }
                if (c == '\\')
                {
                    i++;
                    var v = Escape(out var msg, false);
                    if (v == null)
                    {
                        while (i < s.Length && s[i] != q && !IsLineTerm(s[i])) i++;
                        if (i < s.Length && s[i] == q) i++;
                        return Err(st, i, msg);
                    }
                    sb.Append(v);
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return new JsToken { Kind = JsTok.String, Text = s.Substring(st, i - st), Start = st, End = i, Str = sb.ToString() };
        }

        JsToken Template()
        {
            int st = i;
            i++;
            var t = new JsToken { Kind = JsTok.Template, Start = st, Parts = new List<string>(), PartSpans = new List<Span>(), Exprs = new List<List<JsToken>>() };
            var sb = new StringBuilder();
            int partStart = i;
            while (true)
            {
                if (i >= s.Length)
                {
                    t.Parts.Add(sb.ToString());
                    t.PartSpans.Add(new Span(partStart, i));
                    t.Unterminated = true;
                    t.End = s.Length;
                    t.Text = s.Substring(st);
                    return t;
                }
                char c = s[i];
                if (c == '`')
                {
                    t.Parts.Add(sb.ToString());
                    t.PartSpans.Add(new Span(partStart, i));
                    i++;
                    break;
                }
                if (c == '\\')
                {
                    i++;
                    var v = Escape(out var msg, true);
                    if (v == null) { while (i < s.Length && s[i] != '`') i++; if (i < s.Length) i++; return Err(st, i, msg); }
                    sb.Append(v);
                    continue;
                }
                if (c == '$' && i + 1 < s.Length && s[i + 1] == '{')
                {
                    t.Parts.Add(sb.ToString());
                    t.PartSpans.Add(new Span(partStart, i));
                    sb.Clear();
                    i += 2;
                    var sub = new List<JsToken>();
                    var savedPrev = prev;
                    prev = null;
                    bool closed = Run(sub, true);
                    prev = savedPrev;
                    t.Exprs.Add(sub);
                    if (!closed)
                    {
                        t.Parts.Add("");
                        t.PartSpans.Add(new Span(i, i));
                        t.Unterminated = true;
                        t.End = s.Length;
                        t.Text = s.Substring(st);
                        return t;
                    }
                    partStart = i;
                    continue;
                }
                if (c == '\r') { sb.Append('\n'); i++; if (i < s.Length && s[i] == '\n') i++; continue; }
                sb.Append(c);
                i++;
            }
            t.End = i;
            t.Text = s.Substring(st, i - st);
            return t;
        }

        JsToken Regex()
        {
            int st = i;
            i++;
            bool inClass = false;
            while (i < s.Length && !IsLineTerm(s[i]))
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '[') inClass = true;
                else if (c == ']') inClass = false;
                else if (c == '/' && !inClass) { i++; while (i < s.Length && IdPart(s[i])) i++; return new JsToken { Kind = JsTok.Regex, Text = s.Substring(st, i - st), Start = st, End = i }; }
                i++;
            }
            return Err(st, i, "Invalid regular expression: missing /");
        }
    }
}
