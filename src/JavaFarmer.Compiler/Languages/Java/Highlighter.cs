using System;
using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    public static partial class JavaSyntax
    {
        static readonly HashSet<string> TypeNames = new HashSet<string>
        {
            "int", "double", "boolean", "void", "long", "float", "char", "byte", "short", "var",
            "String", "Object", "Integer", "Double", "Boolean", "List", "ArrayList", "Map", "HashMap", "Set", "HashSet",
            "Collection", "Runnable", "Supplier", "Math", "System",
            "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion",
            "Entities", "Grounds", "Items", "Hats", "Unlocks", "Leaderboards",
        };

        static readonly HashSet<string> ConstantNames = new HashSet<string> { "true", "false", "null", "North", "East", "South", "West" };
        static readonly HashSet<string> ConstBags = new HashSet<string> { "Entities", "Grounds", "Items", "Hats", "Unlocks", "Leaderboards" };

        static readonly IReadOnlyCollection<string> KeywordsImpl = BuildKeywords();

        static IReadOnlyCollection<string> BuildKeywords()
        {
            var list = new List<string>();
            foreach (var k in Lexer.Keywords)
                if (!TypeNames.Contains(k) && !ConstantNames.Contains(k)) list.Add(k);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static IReadOnlyList<(int Start, int Length, TokenKind Kind)> HighlightImpl(string source)
        {
            var result = new List<(int, int, TokenKind)>();
            if (string.IsNullOrEmpty(source)) return result;
            try
            {
                var toks = new Lexer(source).LexAll();
                string prevSig = null, prevPrevSig = null;
                foreach (var t in toks)
                {
                    TokenKind k;
                    switch (t.Kind)
                    {
                        case Tk.Whitespace: k = TokenKind.Whitespace; break;
                        case Tk.Comment: k = TokenKind.Comment; break;
                        case Tk.StringLit: case Tk.CharLit: k = TokenKind.String; break;
                        case Tk.IntLit: case Tk.DoubleLit: k = TokenKind.Number; break;
                        case Tk.Keyword:
                            k = TypeNames.Contains(t.Text) ? TokenKind.Type : ConstantNames.Contains(t.Text) ? TokenKind.Constant : TokenKind.Keyword;
                            break;
                        case Tk.Identifier:
                            if (prevSig == "." && prevPrevSig != null && ConstBags.Contains(prevPrevSig)) k = TokenKind.Constant;
                            else if (ConstantNames.Contains(t.Text)) k = TokenKind.Constant;
                            else if (TypeNames.Contains(t.Text)) k = TokenKind.Type;
                            else k = TokenKind.Identifier;
                            break;
                        case Tk.Op:
                            k = IsPunct(t.Text) ? TokenKind.Punctuation : TokenKind.Operator;
                            break;
                        default: k = TokenKind.Unknown; break;
                    }
                    result.Add((t.Start, t.End - t.Start, k));
                    if (t.Kind != Tk.Whitespace && t.Kind != Tk.Comment) { prevPrevSig = prevSig; prevSig = t.Text; }
                }
            }
            catch (Exception)
            {
                // never throw: fall back to a single unknown token for the rest of the text
                int covered = 0;
                foreach (var r in result) covered = Math.Max(covered, r.Item1 + r.Item2);
                if (covered < source.Length) result.Add((covered, source.Length - covered, TokenKind.Unknown));
            }
            return result;
        }

        static bool IsPunct(string s)
        {
            switch (s)
            {
                case "(": case ")": case "{": case "}": case "[": case "]": case ";": case ",": case ".": case "@": case "::": case "...":
                    return true;
            }
            return false;
        }
    }
}
