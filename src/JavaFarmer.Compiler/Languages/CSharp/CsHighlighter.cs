using System;
using System.Collections.Generic;

namespace JavaFarmer.Compiler.CSharp
{
    /// <summary>C# syntax colouring for the game editor (same TokenKind palette as Java). Never throws.</summary>
    internal static class CsHighlighter
    {
        internal static readonly HashSet<string> TypeNames = new HashSet<string>
        {
            "int", "double", "bool", "void", "long", "float", "char", "byte", "short", "string", "object", "decimal", "uint", "ulong", "var",
            "List", "Dictionary", "HashSet", "KeyValuePair", "Action", "Func", "Math", "Console", "Farm", "String", "Object",
            "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion",
            "Entities", "Grounds", "Items", "Hats", "Unlocks", "Leaderboards",
        };

        internal static readonly HashSet<string> ConstantNames = new HashSet<string> { "true", "false", "null", "North", "East", "South", "West" };
        static readonly HashSet<string> ConstBags = new HashSet<string> { "Entities", "Grounds", "Items", "Hats", "Unlocks", "Leaderboards", "Direction" };
        static readonly HashSet<string> ContextualShown = new HashSet<string> { "var", "when", "or", "and", "not", "global", "record", "async", "await", "yield", "nameof" };

        internal static readonly IReadOnlyCollection<string> Keywords = BuildKeywords();

        static IReadOnlyCollection<string> BuildKeywords()
        {
            var list = new List<string>();
            foreach (var k in CsLexer.Keywords)
                if (!TypeNames.Contains(k) && !ConstantNames.Contains(k)) list.Add(k);
            list.Add("var");
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public static IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            var result = new List<(int, int, TokenKind)>();
            if (string.IsNullOrEmpty(source)) return result;
            int covered = 0;
            try
            {
                var toks = new CsLexer(source, true).LexAll();
                string prevSig = null, prevPrevSig = null;
                foreach (var t in toks)
                {
                    if (t.Start < covered) continue;
                    TokenKind k;
                    switch (t.Kind)
                    {
                        case CTk.Whitespace: k = TokenKind.Whitespace; break;
                        case CTk.Comment: k = TokenKind.Comment; break;
                        case CTk.Directive: k = TokenKind.Comment; break;
                        case CTk.StringLit: case CTk.CharLit: case CTk.InterpStr: k = TokenKind.String; break;
                        case CTk.IntLit: case CTk.DoubleLit: k = TokenKind.Number; break;
                        case CTk.Keyword:
                            k = TypeNames.Contains(t.Text) ? TokenKind.Type : ConstantNames.Contains(t.Text) ? TokenKind.Constant : TokenKind.Keyword;
                            break;
                        case CTk.Identifier:
                            if (prevSig == "." && prevPrevSig != null && ConstBags.Contains(prevPrevSig)) k = TokenKind.Constant;
                            else if (ConstantNames.Contains(t.Text)) k = TokenKind.Constant;
                            else if (TypeNames.Contains(t.Text)) k = TokenKind.Type;
                            else if (ContextualShown.Contains(t.Text) && t.Text != "var") k = TokenKind.Keyword;
                            else k = TokenKind.Identifier;
                            break;
                        case CTk.Op:
                            k = IsPunct(t.Text) ? TokenKind.Punctuation : TokenKind.Operator;
                            break;
                        default: k = TokenKind.Unknown; break;
                    }
                    int end = Math.Min(t.End, source.Length);
                    if (end <= t.Start) continue;
                    result.Add((t.Start, end - t.Start, k));
                    covered = end;
                    if (t.Kind != CTk.Whitespace && t.Kind != CTk.Comment) { prevPrevSig = prevSig; prevSig = t.Text; }
                }
            }
            catch (Exception) { }
            if (covered < source.Length) result.Add((covered, source.Length - covered, TokenKind.Unknown));
            return result;
        }

        static bool IsPunct(string s)
        {
            switch (s)
            {
                case "(": case ")": case "{": case "}": case "[": case "]": case ";": case ",": case ".": case "::":
                    return true;
            }
            return false;
        }
    }
}
