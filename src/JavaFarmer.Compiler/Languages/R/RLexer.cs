using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    internal enum RlTokKind { Num, Str, Ident, Kw, Op, Punct, Eof, Bad }

    internal sealed class RlToken
    {
        public RlTokKind Kind;
        public string Text;
        public int Start, End;
        /// <summary>A line break separates this token from the previous one.</summary>
        public bool NlBefore;
        public double Num;
        public bool LooksInt, HasL, Complex;
        public string StrValue;
        public bool Raw, Backtick;
        public Span Span => new Span(Start, End);
        public bool Is(string text) => (Kind == RlTokKind.Op || Kind == RlTokKind.Punct || Kind == RlTokKind.Kw) && Text == text;
        public override string ToString() => Kind + " " + Text;
    }

    /// <summary>R 4.x tokens (comments are collected separately; "#'" roxygen lines are doc comments).</summary>
    internal static class RlLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "if", "else", "repeat", "while", "function", "for", "in", "next", "break",
            "TRUE", "FALSE", "NULL", "Inf", "NaN", "NA", "NA_integer_", "NA_real_", "NA_character_",
        };

        static readonly string[] Ops3 = { "<<-", "->>", ":::" };
        static readonly string[] Ops2 = { "|>", "<-", "->", "<=", ">=", "==", "!=", "&&", "||", "::", "**" };
        const string Ops1 = "+-*/^<>!&|~?:=$@\\";
        const string Puncts = "(){}[],;";
        static readonly string[] PunctText = Puncts.Select(ch => ch.ToString()).ToArray();
        static readonly string[] Ops1Text = Ops1.Select(ch => ch.ToString()).ToArray();

        public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '.';
        public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '.' || c == '_';

        /// <summary>Tokens (ending with Eof) and comments. Unterminated strings throw RlSyntaxError.</summary>
        public static List<RlToken> Lex(string s, List<IrComment> comments)
        {
            var toks = new List<RlToken>(s.Length / 3 + 4);
            int i = 0, n = s.Length;
            bool nl = false;
            while (true)
            {
                // whitespace / comments
                while (i < n)
                {
                    char c = s[i];
                    if (c == '\n') { nl = true; i++; continue; }
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\f' || c == '\v' || c == ' ' || c == '﻿') { i++; continue; }
                    if (c == '#')
                    {
                        int st = i;
                        while (i < n && s[i] != '\n') i++;
                        if (comments != null)
                        {
                            bool doc = st + 1 < n && s[st + 1] == '\'';
                            var text = s.Substring(st + (doc ? 2 : 1), i - st - (doc ? 2 : 1)).TrimEnd('\r');
                            comments.Add(new IrComment(text) { IsDoc = doc, Span = new Span(st, i), BlankLinesBefore = BlankLinesBefore(s, st) });
                        }
                        continue;
                    }
                    break;
                }
                if (i >= n) { toks.Add(new RlToken { Kind = RlTokKind.Eof, Text = "", Start = n, End = n, NlBefore = nl }); return toks; }
                var t = Next(s, ref i);
                t.NlBefore = nl;
                nl = false;
                toks.Add(t);
            }
        }

        /// <summary>Empty (whitespace-only) lines directly above the line that contains offset.</summary>
        public static int BlankLinesBefore(string s, int offset)
        {
            if (s == null || offset <= 0) return 0;
            int ls = offset;
            while (ls > 0 && s[ls - 1] != '\n') ls--;
            for (int k = ls; k < offset && k < s.Length; k++) if (!char.IsWhiteSpace(s[k])) return 0;
            int count = 0;
            int p = ls - 1;   // at '\n' ending the previous line
            while (p >= 0)
            {
                int end = p;
                int st = p;
                while (st > 0 && s[st - 1] != '\n') st--;
                bool blank = true;
                for (int k = st; k < end; k++) if (!char.IsWhiteSpace(s[k])) { blank = false; break; }
                if (!blank) break;
                count++;
                p = st - 1;
                if (st == 0) break;
            }
            return count;
        }

        static RlToken Next(string s, ref int i)
        {
            int n = s.Length, st = i;
            char c = s[i];
            // numbers
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
                return Number(s, ref i);
            // raw strings r"(...)"
            if ((c == 'r' || c == 'R') && i + 1 < n && (s[i + 1] == '"' || s[i + 1] == '\''))
            {
                i++;
                var rt = LexString(s, ref i, st);
                rt.Raw = true;
                return rt;
            }
            if (IsIdentStart(c))
            {
                while (i < n && IsIdentPart(s[i])) i++;
                var w = s.Substring(st, i - st);
                return new RlToken { Kind = Keywords.Contains(w) ? RlTokKind.Kw : RlTokKind.Ident, Text = w, Start = st, End = i };
            }
            if (c == '"' || c == '\'') return LexString(s, ref i, st);
            if (c == '`')
            {
                int j = i + 1;
                while (j < n && s[j] != '`' && s[j] != '\n') j++;
                if (j >= n || s[j] != '`')
                {
                    i = j;
                    return new RlToken { Kind = RlTokKind.Bad, Text = s.Substring(st, j - st), Start = st, End = j };
                }
                i = j + 1;
                return new RlToken { Kind = RlTokKind.Ident, Text = s.Substring(st + 1, j - st - 1), Start = st, End = i, Backtick = true };
            }
            if (c == '%')
            {
                int j = i + 1;
                while (j < n && s[j] != '%' && s[j] != '\n') j++;
                if (j < n && s[j] == '%')
                {
                    i = j + 1;
                    return new RlToken { Kind = RlTokKind.Op, Text = s.Substring(st, i - st), Start = st, End = i };
                }
                i++;
                return new RlToken { Kind = RlTokKind.Bad, Text = "%", Start = st, End = i };
            }
            int pk = Puncts.IndexOf(c);
            if (pk >= 0) { i++; return new RlToken { Kind = RlTokKind.Punct, Text = PunctText[pk], Start = st, End = i }; }
            foreach (var o in Ops3) if (string.CompareOrdinal(s, i, o, 0, 3) == 0) { i += 3; return Op(o, st, i); }
            foreach (var o in Ops2) if (string.CompareOrdinal(s, i, o, 0, 2) == 0) { i += 2; return Op(o == "**" ? "^" : o, st, i); }
            int ok = Ops1.IndexOf(c);
            if (ok >= 0) { i++; return Op(Ops1Text[ok], st, i); }
            i += char.IsHighSurrogate(c) && i + 1 < n && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            return new RlToken { Kind = RlTokKind.Bad, Text = s.Substring(st, i - st), Start = st, End = i };
        }

        static RlToken Op(string text, int st, int end) => new RlToken { Kind = RlTokKind.Op, Text = text, Start = st, End = end };

        static RlToken Number(string s, ref int i)
        {
            int n = s.Length, st = i;
            var t = new RlToken { Kind = RlTokKind.Num, Start = st };
            if (s[i] == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X'))
            {
                i += 2;
                int hs = i;
                while (i < n && Uri.IsHexDigit(s[i])) i++;
                long v = 0;
                for (int k = hs; k < i; k++) v = v * 16 + Convert.ToInt32(s[k].ToString(), 16);
                t.Num = v; t.LooksInt = true;
                if (i < n && s[i] == 'L') { t.HasL = true; i++; }
            }
            else
            {
                bool dot = false, exp = false;
                while (i < n && char.IsDigit(s[i])) i++;
                if (i < n && s[i] == '.') { dot = true; i++; while (i < n && char.IsDigit(s[i])) i++; }
                if (i < n && (s[i] == 'e' || s[i] == 'E'))
                {
                    int save = i;
                    i++;
                    if (i < n && (s[i] == '+' || s[i] == '-')) i++;
                    if (i < n && char.IsDigit(s[i])) { exp = true; while (i < n && char.IsDigit(s[i])) i++; }
                    else i = save;
                }
                var text = s.Substring(st, i - st);
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out t.Num);
                t.LooksInt = !dot && !exp;
                if (i < n && s[i] == 'L') { t.HasL = true; i++; if (t.Num == Math.Floor(t.Num)) t.LooksInt = true; }
                else if (i < n && s[i] == 'i') { t.Complex = true; i++; }
            }
            t.End = i;
            t.Text = s.Substring(st, i - st);
            return t;
        }

        static RlToken LexString(string s, ref int i, int st)
        {
            int n = s.Length;
            char q = s[i];
            i++;
            var sb = new StringBuilder();
            while (i < n && s[i] != q)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < n)
                {
                    char e = s[i + 1];
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
                        case '`': sb.Append('`'); break;
                        case ' ': sb.Append(' '); break;
                        case 'a': sb.Append('\a'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'v': sb.Append('\v'); break;
                        case 'x':
                            {
                                int hs = i, v = 0;
                                while (i < n && i - hs < 2 && Uri.IsHexDigit(s[i])) { v = v * 16 + Convert.ToInt32(s[i].ToString(), 16); i++; }
                                sb.Append((char)v);
                                break;
                            }
                        case 'u': case 'U':
                            {
                                bool brace = i < n && s[i] == '{';
                                if (brace) i++;
                                int hs = i, v = 0, max = e == 'u' ? 4 : 8;
                                while (i < n && i - hs < max && Uri.IsHexDigit(s[i])) { v = v * 16 + Convert.ToInt32(s[i].ToString(), 16); i++; }
                                if (brace && i < n && s[i] == '}') i++;
                                try { sb.Append(char.ConvertFromUtf32(v)); } catch { sb.Append('?'); }
                                break;
                            }
                        default:
                            throw new RlSyntaxError("Error: '\\" + e + "' is an unrecognized escape in character string", new Span(i - 2, i));
                    }
                    continue;
                }
                sb.Append(c);
                i++;
            }
            if (i >= n)
            {
                int eol = s.IndexOf('\n', st);
                throw new RlSyntaxError("Error: unexpected INCOMPLETE_STRING", new Span(st, eol < 0 ? n : eol));
            }
            i++;
            return new RlToken { Kind = RlTokKind.Str, Text = s.Substring(st, i - st), Start = st, End = i, StrValue = sb.ToString() };
        }
    }
}
