using System;
using System.Collections.Generic;

namespace JavaFarmer.Compiler.CFamily
{
    /// <summary>Token colouring for the editor: covers every character, tolerates unfinished code, never throws.</summary>
    internal static class Highlighter
    {
        static readonly HashSet<string> TypeKeywords = new HashSet<string> { "int", "long", "short", "unsigned", "signed", "double", "float", "bool", "void", "char", "auto", "_Bool", "const", "static", "extern", "constexpr", "inline" };
        static readonly HashSet<string> ConstantWords = new HashSet<string> { "true", "false", "nullptr", "NULL", "North", "East", "South", "West", "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards", "Direction", "Directions" };
        static readonly HashSet<string> TypeNames = new HashSet<string> { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Drone", "Position", "Companion", "List", "Map", "Set", "string", "vector", "map", "unordered_map", "set", "unordered_set", "function", "optional", "std", "farm" };

        public static IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string src, Dialect d)
        {
            var res = new List<(int, int, TokenKind)>();
            try
            {
                var lex = Lexer.Lex(src, d);
                var spans = new List<(int S, int E, TokenKind K)>();
                foreach (var t in lex.Tokens)
                {
                    if (t.Kind == TK.EOF) continue;
                    spans.Add((t.Start, t.End, Kind(t, d)));
                }
                foreach (var c in lex.Comments) spans.Add((c.Span.Start, c.Span.End, TokenKind.Comment));
                spans.Sort((a, b) => a.S.CompareTo(b.S));
                int pos = 0;
                foreach (var (s, e, k) in spans)
                {
                    if (s < pos) continue;
                    if (s > pos) Gap(src, pos, s, res);
                    if (e > s) res.Add((s, e - s, k));
                    pos = Math.Max(pos, e);
                }
                if (pos < src.Length) Gap(src, pos, src.Length, res);
            }
            catch
            {
                res.Clear();
                if (src.Length > 0) res.Add((0, src.Length, TokenKind.Unknown));
            }
            return res;
        }

        static void Gap(string src, int s, int e, List<(int, int, TokenKind)> res)
        {
            int i = s;
            while (i < e)
            {
                int j = i;
                if (char.IsWhiteSpace(src[i])) { while (j < e && char.IsWhiteSpace(src[j])) j++; res.Add((i, j - i, TokenKind.Whitespace)); i = j; continue; }
                // an unterminated /* comment swallows the rest
                if (src[i] == '/' && i + 1 < e && src[i + 1] == '*') { res.Add((i, e - i, TokenKind.Comment)); return; }
                while (j < e && !char.IsWhiteSpace(src[j])) j++;
                res.Add((i, j - i, TokenKind.Unknown));
                i = j;
            }
        }

        static TokenKind Kind(Token t, Dialect d)
        {
            switch (t.Kind)
            {
                case TK.Keyword:
                    if (t.Text == "true" || t.Text == "false" || t.Text == "nullptr") return TokenKind.Constant;
                    return TypeKeywords.Contains(t.Text) ? TokenKind.Type : TokenKind.Keyword;
                case TK.Ident:
                    if (ConstantWords.Contains(t.Text)) return TokenKind.Constant;
                    if (TypeNames.Contains(t.Text)) return TokenKind.Type;
                    return TokenKind.Identifier;
                case TK.Int: case TK.Float: return TokenKind.Number;
                case TK.String: case TK.Char: return TokenKind.String;
                case TK.Preproc: return TokenKind.Keyword;
                case TK.Punct:
                    return t.Text.Length == 1 && "{}()[];,".IndexOf(t.Text[0]) >= 0 ? TokenKind.Punctuation : TokenKind.Operator;
            }
            return TokenKind.Unknown;
        }
    }
}
