using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JavaFarmer.Compiler
{
    internal enum PyTk { Name, Number, String, Op, Newline, Indent, Dedent, End }

    internal sealed class PyToken
    {
        public PyTk Kind;
        public string Text;
        public int Start, End;
        public int Line;                 // 1-based line of Start
        /// <summary>Newline: the comment at the end of the logical line (same line, outside brackets), or null.</summary>
        public IrComment Comment;
        /// <summary>First token of a logical line: empty lines directly before it (after any comment lines).</summary>
        public int BlankLinesBefore;
        /// <summary>Newline: indentation width of the logical line it ends. Indent: the new width.</summary>
        public int Width;
        public Span Span => new Span(Start, End);
        public bool Is(PyTk k, string text) => Kind == k && Text == text;
        public bool IsOp(string text) => Kind == PyTk.Op && Text == text;
        public bool IsKw(string text) => Kind == PyTk.Name && Text == text;
        public override string ToString() => Kind + " '" + Text + "' @" + Start;
    }

    /// <summary>A source comment with the layout facts the comment attacher needs.</summary>
    internal sealed class PyComment
    {
        public IrComment Comment;
        public int Line;
        /// <summary>Indentation width of the comment line (tab = 4, like the game).</summary>
        public int Col;
        /// <summary>Alone on its line, outside brackets.</summary>
        public bool OwnLine;
        /// <summary>Inside brackets of a multi-line expression.</summary>
        public bool Inner;
        public int Start => Comment.Span.Start;
    }

    /// <summary>Error with a source span, thrown by the Python lexer / parser (CPython-style message).</summary>
    internal sealed class PySyntaxError : Exception
    {
        public readonly Span Span;
        public PySyntaxError(string message, Span span) : base(message) { Span = span; }
    }

    /// <summary>
    /// Lexer for the game's Python dialect (Core Tokenizer rules): identifiers start with [A-Za-z_], numbers are plain
    /// decimals ("12", "1.5", ".5"), strings are '..' or ".." on one line WITHOUT escape processing, '#' comments,
    /// indentation by tabs or spaces (tab = 4 columns; one line may not mix both), newlines inside brackets are ignored.
    /// Produces Newline / Indent / Dedent tokens like CPython's tokenizer. The first logical line sets the module's base
    /// indentation (the game accepts an indented script).
    /// </summary>
    internal sealed class PyLexer
    {
        readonly string src;
        int pos, line = 1;
        readonly List<PyToken> tokens = new List<PyToken>();
        public readonly List<PyComment> Comments = new List<PyComment>();
        readonly List<int> indents = new List<int>();
        readonly List<char> brackets = new List<char>();
        readonly List<int> bracketPos = new List<int>();
        IrComment pendingLineComment;

        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "and", "break", "continue", "def", "elif", "else", "for", "from", "global", "if", "import", "in", "not", "or",
            "pass", "return", "while",
        };

        /// <summary>Python keywords the game does not support (reported as such instead of "invalid syntax").</summary>
        public static readonly HashSet<string> UnsupportedKeywords = new HashSet<string>
        {
            "as", "assert", "async", "await", "class", "del", "except", "finally", "is", "lambda", "nonlocal", "raise",
            "try", "with", "yield",
        };

        public PyLexer(string source) { src = (source ?? "").Replace('\v', '\n'); }

        public List<PyToken> Lex()
        {
            int blank = 0;
            bool lineHasTokens = false;
            while (true)
            {
                // ---- start of a physical line outside brackets: indentation, blank and comment-only lines
                if (brackets.Count == 0 && !lineHasTokens)
                {
                    int ls = pos, width = 0;
                    bool tab = false, space = false;
                    while (pos < src.Length && (src[pos] == ' ' || src[pos] == '\t' || src[pos] == '\f'))
                    {
                        if (src[pos] == '\t') { tab = true; width += 4; } else if (src[pos] == ' ') { space = true; width++; }
                        pos++;
                    }
                    if (pos >= src.Length) break;
                    char c0 = src[pos];
                    if (c0 == '\r' && pos + 1 < src.Length && src[pos + 1] == '\n') pos++;
                    if (src[pos] == '\n') { pos++; line++; blank++; continue; }
                    if (src[pos] == '\r') { pos++; blank++; continue; }
                    if (c0 == '#')
                    {
                        var c = ReadComment();
                        c.BlankLinesBefore = blank;
                        blank = 0;
                        Comments.Add(new PyComment { Comment = c, Line = line, Col = width, OwnLine = true });
                        if (pos < src.Length && src[pos] == '\r') pos++;
                        if (pos < src.Length && src[pos] == '\n') { pos++; line++; }
                        continue;
                    }
                    if (tab && space)
                        throw new PySyntaxError("TabError: inconsistent use of tabs and spaces in indentation", new Span(ls, pos));
                    Indentation(width, new Span(ls, pos));
                    lineHasTokens = true;
                    int before = tokens.Count;
                    LexToken();
                    if (tokens.Count > before) tokens[before].BlankLinesBefore = blank;
                    blank = 0;
                    continue;
                }
                if (pos >= src.Length) break;
                char ch = src[pos];
                if (ch == ' ' || ch == '\t' || ch == '\f' || ch == '\r') { pos++; continue; }
                if (ch == '\n')
                {
                    if (brackets.Count > 0) { pos++; line++; continue; }
                    AddNewline(pos);
                    pos++; line++;
                    lineHasTokens = false;
                    continue;
                }
                if (ch == '#')
                {
                    var c = ReadComment();
                    if (brackets.Count > 0) Comments.Add(new PyComment { Comment = c, Line = line, Col = -1, Inner = true });
                    else pendingLineComment = c;
                    continue;
                }
                LexToken();
            }
            if (brackets.Count > 0)
            {
                int bp = bracketPos[bracketPos.Count - 1];
                throw new PySyntaxError("SyntaxError: '" + brackets[brackets.Count - 1] + "' was never closed", new Span(bp, bp + 1));
            }
            if (lineHasTokens) AddNewline(src.Length);
            for (int i = 1; i < indents.Count; i++) tokens.Add(new PyToken { Kind = PyTk.Dedent, Text = "", Start = src.Length, End = src.Length, Line = line });
            tokens.Add(new PyToken { Kind = PyTk.End, Text = "", Start = src.Length, End = src.Length, Line = line });
            return tokens;
        }

        void AddNewline(int at)
        {
            var t = new PyToken { Kind = PyTk.Newline, Text = "\n", Start = at, End = Math.Min(at + 1, src.Length), Line = line, Comment = pendingLineComment };
            t.Width = indents.Count > 0 ? indents[indents.Count - 1] : 0;
            pendingLineComment = null;
            tokens.Add(t);
        }

        IrComment ReadComment()
        {
            int s = pos;
            while (pos < src.Length && src[pos] != '\n') pos++;
            int e = pos;
            if (e > s && src[e - 1] == '\r') e--;
            return new IrComment { Text = src.Substring(s + 1, e - s - 1), Span = new Span(s, e) };
        }

        void Indentation(int width, Span at)
        {
            if (indents.Count == 0) { indents.Add(width); return; }
            int top = indents[indents.Count - 1];
            if (width == top) return;
            if (width > top)
            {
                indents.Add(width);
                tokens.Add(new PyToken { Kind = PyTk.Indent, Text = "", Start = at.End, End = at.End, Line = line, Width = width });
                return;
            }
            while (indents.Count > 1 && indents[indents.Count - 1] > width)
            {
                indents.RemoveAt(indents.Count - 1);
                tokens.Add(new PyToken { Kind = PyTk.Dedent, Text = "", Start = at.End, End = at.End, Line = line, Width = width });
            }
            if (indents[indents.Count - 1] != width)
                throw new PySyntaxError("IndentationError: unindent does not match any outer indentation level", at.Length > 0 ? at : new Span(at.Start, at.Start + 1));
        }

        static bool IdStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        static bool IdPart(char c) => c == '_' || char.IsLetterOrDigit(c);

        static readonly string[] Ops3 = { "**=", "//=" };
        static readonly string[] Ops2 = { "**", "//", "==", "!=", ">=", "<=", "+=", "-=", "*=", "/=", "%=" };
        const string Ops1 = "+-*/%<>=()[]{},:.";

        void Add(PyTk k, int s, int e) => tokens.Add(new PyToken { Kind = k, Text = src.Substring(s, e - s), Start = s, End = e, Line = line });

        void LexToken()
        {
            int s = pos;
            char c = src[pos];
            if (IdStart(c))
            {
                pos++;
                while (pos < src.Length && IdPart(src[pos])) pos++;
                Add(PyTk.Name, s, pos);
                return;
            }
            if (char.IsDigit(c) || (c == '.' && pos + 1 < src.Length && char.IsDigit(src[pos + 1])))
            {
                while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                if (pos + 1 < src.Length && src[pos] == '.' && char.IsDigit(src[pos + 1]))
                {
                    pos++;
                    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                }
                if (pos < src.Length && IdPart(src[pos]))
                {
                    int e = pos;
                    while (e < src.Length && IdPart(src[e])) e++;
                    throw new PySyntaxError("SyntaxError: invalid decimal literal", new Span(s, e));
                }
                Add(PyTk.Number, s, pos);
                return;
            }
            if (c == '"' || c == '\'')
            {
                int e = pos + 1;
                while (e < src.Length && src[e] != c && src[e] != '\n') e++;
                if (e >= src.Length || src[e] != c)
                {
                    int end = e;
                    if (end - 1 > s && src[end - 1] == '\r') end--;
                    throw new PySyntaxError("SyntaxError: unterminated string literal (detected at line " + line + ")", new Span(s, Math.Max(end, s + 1)));
                }
                pos = e + 1;
                Add(PyTk.String, s, pos);
                return;
            }
            foreach (var op in Ops3)
                if (string.CompareOrdinal(src, pos, op, 0, 3) == 0) { pos += 3; Add(PyTk.Op, s, pos); return; }
            foreach (var op in Ops2)
                if (string.CompareOrdinal(src, pos, op, 0, 2) == 0) { pos += 2; Add(PyTk.Op, s, pos); return; }
            if (Ops1.IndexOf(c) >= 0)
            {
                pos++;
                if (c == '(' || c == '[' || c == '{') { brackets.Add(c); bracketPos.Add(s); }
                else if (c == ')' || c == ']' || c == '}')
                {
                    char open = c == ')' ? '(' : c == ']' ? '[' : '{';
                    if (brackets.Count == 0) throw new PySyntaxError("SyntaxError: unmatched '" + c + "'", new Span(s, s + 1));
                    if (brackets[brackets.Count - 1] != open)
                        throw new PySyntaxError("SyntaxError: closing parenthesis '" + c + "' does not match opening parenthesis '" + brackets[brackets.Count - 1] + "'", new Span(s, s + 1));
                    brackets.RemoveAt(brackets.Count - 1);
                    bracketPos.RemoveAt(bracketPos.Count - 1);
                }
                Add(PyTk.Op, s, pos);
                return;
            }
            if (c == ';') throw new PySyntaxError("SyntaxError: ';' is not supported by the game: write one statement per line", new Span(s, s + 1));
            if ("&|^~@!".IndexOf(c) >= 0)
            {
                int e = pos + 1;
                while (e < src.Length && "&|^~@!<>=".IndexOf(src[e]) >= 0) e++;
                throw new PySyntaxError("SyntaxError: operator '" + src.Substring(s, e - s) + "' is not supported by the game", new Span(s, e));
            }
            if (c == '\\') throw new PySyntaxError("SyntaxError: line continuation with '\\' is not supported by the game", new Span(s, s + 1));
            bool pair = char.IsSurrogatePair(src, pos);
            int cp = pair ? char.ConvertToUtf32(src, pos) : src[pos];
            int len = pair ? 2 : 1;
            throw new PySyntaxError("SyntaxError: invalid character '" + src.Substring(pos, len) + "' (U+" + cp.ToString("X4", CultureInfo.InvariantCulture) + ")", new Span(s, s + len));
        }
    }
}
