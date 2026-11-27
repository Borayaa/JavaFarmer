using System;
using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    /// <summary>Rust colouring for the editor: never throws, covers every character.</summary>
    internal static class RustHighlighter
    {
        static readonly HashSet<string> TypeNames = new HashSet<string>
        {
            "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize", "f32", "f64", "bool", "char", "str",
            "String", "Vec", "HashMap", "HashSet", "Option", "Self", "Entity", "Item", "Ground", "Unlock", "Hat", "Leaderboard", "Direction",
            "Drone", "Position", "Companion",
        };
        static readonly HashSet<string> ConstantNames = new HashSet<string> { "true", "false", "None", "Some", "North", "East", "South", "West" };
        static readonly HashSet<string> Enums = new HashSet<string> { "Entity", "Item", "Ground", "Unlock", "Hat", "Leaderboard", "Direction" };

        public static IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            var result = new List<(int, int, TokenKind)>();
            if (string.IsNullOrEmpty(source)) return result;
            try
            {
                var toks = new RustLexer(source).LexAll();
                RToken prev = null, prev2 = null;
                for (int i = 0; i < toks.Count; i++)
                {
                    var t = toks[i];
                    if (t.Kind == RTk.EOF) break;
                    TokenKind k;
                    switch (t.Kind)
                    {
                        case RTk.Whitespace: k = TokenKind.Whitespace; break;
                        case RTk.Comment: k = TokenKind.Comment; break;
                        case RTk.Str: case RTk.Char: k = TokenKind.String; break;
                        case RTk.Int: case RTk.Float: k = TokenKind.Number; break;
                        case RTk.Lifetime: k = TokenKind.Keyword; break;
                        case RTk.Keyword:
                            k = t.Text == "true" || t.Text == "false" ? TokenKind.Constant : t.Text == "Self" ? TokenKind.Type : TokenKind.Keyword;
                            break;
                        case RTk.Ident:
                            {
                                var next = Next(toks, i);
                                if (prev != null && prev.Text == "::" && prev2 != null && Enums.Contains(prev2.Text)) k = TokenKind.Constant;
                                else if (next != null && next.Kind == RTk.Punct && next.Text == "!") k = TokenKind.Keyword;
                                else if (ConstantNames.Contains(t.Text)) k = TokenKind.Constant;
                                else if (TypeNames.Contains(t.Text)) k = TokenKind.Type;
                                else if (t.Text.Length > 1 && IsUpper(t.Text)) k = TokenKind.Constant;
                                else k = TokenKind.Identifier;
                                break;
                            }
                        case RTk.Punct:
                            k = IsPunct(t.Text) ? TokenKind.Punctuation : TokenKind.Operator;
                            break;
                        default: k = TokenKind.Unknown; break;
                    }
                    result.Add((t.Start, t.End - t.Start, k));
                    if (t.Kind != RTk.Whitespace && t.Kind != RTk.Comment) { prev2 = prev; prev = t; }
                }
            }
            catch (Exception)
            {
                int covered = 0;
                foreach (var r in result) covered = Math.Max(covered, r.Item1 + r.Item2);
                if (covered < source.Length) result.Add((covered, source.Length - covered, TokenKind.Unknown));
            }
            return result;
        }

        static RToken Next(List<RToken> toks, int i)
        {
            for (int j = i + 1; j < toks.Count; j++)
                if (toks[j].Kind != RTk.Whitespace && toks[j].Kind != RTk.Comment) return toks[j];
            return null;
        }

        static bool IsUpper(string s)
        {
            bool letter = false;
            foreach (var c in s) { if (char.IsLower(c)) return false; if (char.IsLetter(c)) letter = true; }
            return letter;
        }

        static bool IsPunct(string s)
        {
            switch (s)
            {
                case "(": case ")": case "{": case "}": case "[": case "]": case ";": case ",": case ".": case "::": case ":": case "#": case "->": case "=>":
                    return true;
            }
            return false;
        }
    }
}
