using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Recursive-descent parser for the game's Python dialect (see PyLexer). Operator precedence follows the game's
    /// Core Parser, which matches CPython except that ** is left-associative (2**3**2 == 64 in the game).
    /// Lowest to highest: conditional expression (CPython only, the game has none), or, and, not, comparisons / in /
    /// not in (chained), + -, * / // %, unary + -, **, call / index / attribute, atoms.
    /// Errors imitate CPython's messages; the first error aborts (PySyntaxError).
    /// Comments are attached to statements / blocks after parsing (see AttachComments).
    /// </summary>
    internal sealed class PyParser
    {
        readonly string src;
        readonly List<PyToken> t;
        readonly List<PyComment> comments;
        int p;
        int loopDepth, funcDepth;

        PyParser(string source, List<PyToken> tokens, List<PyComment> comments)
        {
            src = source; t = tokens; this.comments = comments;
        }

        public static PyModuleAst Parse(string source)
        {
            var lx = new PyLexer(source);
            var toks = lx.Lex();
            var ps = new PyParser(source ?? "", toks, lx.Comments);
            var m = ps.ParseModule();
            ps.AttachComments(m);
            return m;
        }

        /// <summary>Parses a single expression (docs inline snippets). Throws PySyntaxError.</summary>
        public static PyExpr ParseExpression(string source)
        {
            var lx = new PyLexer(source);
            var toks = lx.Lex();
            var ps = new PyParser(source ?? "", toks, lx.Comments);
            while (ps.Cur.Kind == PyTk.Indent) ps.p++;
            var e = ps.ParseTestList();
            while (ps.Cur.Kind == PyTk.Newline || ps.Cur.Kind == PyTk.Dedent) ps.p++;
            if (ps.Cur.Kind != PyTk.End) throw ps.Invalid();
            return e;
        }

        // ================================================================== tokens
        PyToken Cur => t[p];
        PyToken Next => p + 1 < t.Count ? t[p + 1] : t[t.Count - 1];
        PyToken Prev => p > 0 ? t[p - 1] : t[0];

        bool AtOp(string s) => Cur.Kind == PyTk.Op && Cur.Text == s;
        bool AtKw(string s) => Cur.Kind == PyTk.Name && Cur.Text == s;

        PyToken Take() { var x = t[p]; if (p < t.Count - 1) p++; return x; }

        /// <summary>Span to report for an error at the current token (the newline itself has no visible text).</summary>
        Span ErrSpan()
        {
            var c = Cur;
            if (c.Kind == PyTk.Name || c.Kind == PyTk.Number || c.Kind == PyTk.String || c.Kind == PyTk.Op) return c.Span;
            int at = p > 0 ? Prev.End : 0;
            return new Span(Math.Max(0, Math.Min(at, src.Length - 1)), Math.Min(src.Length, Math.Max(at, 0) + 1));
        }

        PySyntaxError Invalid() => Invalid(ErrSpan());
        PySyntaxError Invalid(Span s)
        {
            if (Cur.Kind == PyTk.Name && PyLexer.UnsupportedKeywords.Contains(Cur.Text) && s.Start == Cur.Start)
                return new PySyntaxError("SyntaxError: '" + Cur.Text + "' is not supported by the game", Cur.Span);
            return new PySyntaxError("SyntaxError: invalid syntax", s);
        }

        PyToken ExpectOp(string op, string message = null)
        {
            if (AtOp(op)) return Take();
            throw message != null ? new PySyntaxError(message, ErrSpan()) : Invalid();
        }

        PyToken ExpectColon()
        {
            if (AtOp(":")) return Take();
            throw new PySyntaxError("SyntaxError: expected ':'", ErrSpan());
        }

        // ================================================================== statements
        PyModuleAst ParseModule()
        {
            var m = new PyModuleAst();
            m.Body.Width = 0;
            bool first = true;
            while (Cur.Kind != PyTk.End)
            {
                if (Cur.Kind == PyTk.Newline || Cur.Kind == PyTk.Dedent) { Take(); continue; }
                if (Cur.Kind == PyTk.Indent) throw new PySyntaxError("IndentationError: unexpected indent", IndentErrSpan());
                if (first) { m.Body.Width = ColumnOf(Cur.Start); first = false; }
                m.Body.Stmts.Add(ParseStatement());
            }
            m.Body.Limit = src.Length;
            m.Body.Span = m.Body.Stmts.Count > 0 ? new Span(m.Body.Stmts[0].Span.Start, m.Body.Stmts[m.Body.Stmts.Count - 1].Span.End) : new Span(0, src.Length);
            return m;
        }

        Span IndentErrSpan()
        {
            int i = p + 1 < t.Count ? p + 1 : p;
            var n = t[i];
            return n.End > n.Start ? n.Span : new Span(Math.Max(0, n.Start - 1), Math.Max(n.Start, 1));
        }

        int ColumnOf(int pos)
        {
            int ls = pos;
            while (ls > 0 && src[ls - 1] != '\n') ls--;
            int w = 0;
            for (int i = ls; i < pos; i++) w += src[i] == '\t' ? 4 : 1;
            return w;
        }

        PyStmt ParseStatement()
        {
            var first = Cur;
            PyStmt s;
            if (first.Kind == PyTk.Name)
            {
                switch (first.Text)
                {
                    case "if": s = ParseIf(); goto done;
                    case "while": s = ParseWhile(); goto done;
                    case "for": s = ParseFor(); goto done;
                    case "def": s = ParseDef(); goto done;
                    case "elif": case "else":
                        throw new PySyntaxError("SyntaxError: invalid syntax", first.Span);
                }
            }
            s = ParseSimple();
            if (Cur.Kind == PyTk.Newline) s.Trailing = Take().Comment;
            else if (Cur.Kind != PyTk.End && Cur.Kind != PyTk.Dedent)
            {
                if (AtOp("=")) throw new PySyntaxError("SyntaxError: chained assignment (a = b = c) is not supported by the game", Cur.Span);
                throw Invalid();
            }
        done:
            s.BlankLines = first.BlankLinesBefore;
            s.Line = first.Line;
            return s;
        }

        PyStmt ParseSimple()
        {
            var tok = Cur;
            if (tok.Kind == PyTk.Name)
            {
                switch (tok.Text)
                {
                    case "pass": Take(); return new PyJumpStmt { Kind = PyJump.Pass, Span = tok.Span };
                    case "break":
                        Take();
                        if (loopDepth == 0) throw new PySyntaxError("SyntaxError: 'break' outside loop", tok.Span);
                        return new PyJumpStmt { Kind = PyJump.Break, Span = tok.Span };
                    case "continue":
                        Take();
                        if (loopDepth == 0) throw new PySyntaxError("SyntaxError: 'continue' not properly in loop", tok.Span);
                        return new PyJumpStmt { Kind = PyJump.Continue, Span = tok.Span };
                    case "return":
                        {
                            Take();
                            if (funcDepth == 0) throw new PySyntaxError("SyntaxError: 'return' outside function", tok.Span);
                            var r = new PyReturnStmt { Span = tok.Span };
                            if (!EndOfStatement()) { r.Value = ParseTestList(); r.Span = new Span(tok.Start, r.Value.Span.End); }
                            return r;
                        }
                    case "global":
                        {
                            Take();
                            var g = new PyGlobalStmt();
                            do
                            {
                                if (g.Names.Count > 0) Take();
                                var n = ExpectName();
                                g.Names.Add(n.Text); g.NameSpans.Add(n.Span);
                            } while (AtOp(","));
                            g.Span = new Span(tok.Start, Prev.End);
                            return g;
                        }
                    case "import":
                        {
                            Take();
                            var im = new PyImportStmt();
                            bool paren = AtOp("(");
                            if (paren) Take();
                            do
                            {
                                if (im.Modules.Count > 0) { Take(); if (paren && AtOp(")")) break; }
                                var n = ExpectName();
                                if (AtOp(".")) throw new PySyntaxError("SyntaxError: dotted module names are not supported by the game", Cur.Span);
                                im.Modules.Add(n.Text); im.ModuleSpans.Add(n.Span);
                            } while (AtOp(","));
                            if (paren) ExpectOp(")");
                            if (AtKw("as")) throw new PySyntaxError("SyntaxError: 'import ... as' is not supported by the game", Cur.Span);
                            im.Span = new Span(tok.Start, Prev.End);
                            return im;
                        }
                    case "from":
                        {
                            Take();
                            var fi = new PyFromImportStmt();
                            var mn = ExpectName();
                            fi.Module = mn.Text; fi.ModuleSpan = mn.Span;
                            if (!AtKw("import")) throw new PySyntaxError("SyntaxError: invalid syntax", ErrSpan());
                            Take();
                            if (AtOp("*"))
                            {
                                Take();
                                if (funcDepth > 0) throw new PySyntaxError("SyntaxError: import * only allowed at module level", Prev.Span);
                                fi.Star = true;
                            }
                            else
                            {
                                bool paren = AtOp("(");
                                if (paren) Take();
                                do
                                {
                                    if (fi.Names.Count > 0) { Take(); if (paren && AtOp(")")) break; }
                                    fi.Names.Add(ExpectName().Text);
                                } while (AtOp(","));
                                if (paren) ExpectOp(")");
                                if (AtKw("as")) throw new PySyntaxError("SyntaxError: 'import ... as' is not supported by the game", Cur.Span);
                            }
                            fi.Span = new Span(tok.Start, Prev.End);
                            return fi;
                        }
                }
                if (PyLexer.UnsupportedKeywords.Contains(tok.Text) && !(Next.IsOp("=") || Next.IsOp("(") || Next.IsOp(".") || Next.IsOp("[")))
                    throw new PySyntaxError("SyntaxError: '" + tok.Text + "' is not supported by the game", tok.Span);
            }

            var target = ParseTestList();
            if (Cur.Kind == PyTk.Op)
            {
                var op = Cur.Text;
                if (op == "=")
                {
                    CheckTarget(target, false);
                    var opTok = Take();
                    var value = ParseTestList();
                    return new PyAssignStmt { Target = target, Value = value, OpSpan = opTok.Span, Span = new Span(target.Span.Start, value.Span.End) };
                }
                if (op.Length >= 2 && op.EndsWith("=") && op != "==" && op != "!=" && op != "<=" && op != ">=")
                {
                    CheckTarget(target, true);
                    var opTok = Take();
                    var value = ParseTestList();
                    return new PyAssignStmt { Target = target, Op = op, Value = value, OpSpan = opTok.Span, Span = new Span(target.Span.Start, value.Span.End) };
                }
            }
            return new PyExprStmt { Expr = target, Span = target.Span };
        }

        PyToken ExpectName()
        {
            if (Cur.Kind == PyTk.Name && !PyLexer.Keywords.Contains(Cur.Text)) return Take();
            throw Invalid();
        }

        bool EndOfStatement() => Cur.Kind == PyTk.Newline || Cur.Kind == PyTk.End || Cur.Kind == PyTk.Dedent;

        static readonly HashSet<string> Unassignable = new HashSet<string>
        {
            "True", "False", "None", "Entities", "Grounds", "Items", "Unlocks", "Leaderboards", "Hats", "North", "South", "East", "West",
        };

        void CheckTarget(PyExpr e, bool augmented)
        {
            switch (e)
            {
                case PyNameExpr n:
                    if (n.Id == "True" || n.Id == "False" || n.Id == "None")
                        throw new PySyntaxError("SyntaxError: cannot assign to " + n.Id, n.Span);
                    if (Unassignable.Contains(n.Id))
                        throw new PySyntaxError("SyntaxError: cannot assign to " + n.Id + " (a game constant)", n.Span);
                    return;
                case PyAttrExpr a:
                    if (a.Target is PyNameExpr an && GameApi.ConstantCategories.Contains(an.Id))
                        throw new PySyntaxError("SyntaxError: cannot assign to a game constant", a.Span);
                    return;
                case PyIndexExpr _: return;
                case PySeqExpr s when (s.Kind == PySeqKind.Tuple || s.Kind == PySeqKind.List) && !augmented:
                    foreach (var i in s.Items) CheckTarget(i, false);
                    return;
                case PySeqExpr s2 when augmented:
                    throw new PySyntaxError("SyntaxError: '" + (s2.Kind == PySeqKind.List ? "list" : s2.Kind == PySeqKind.Set ? "set display" : "tuple") + "' is an illegal expression for augmented assignment", s2.Span);
                case PyCallExpr c:
                    throw new PySyntaxError("SyntaxError: cannot assign to function call" + (augmented ? "" : " here. Maybe you meant '==' instead of '='?"), c.Span);
                case PyNumExpr _: case PyStrExpr _:
                    throw new PySyntaxError("SyntaxError: cannot assign to literal" + (augmented ? "" : " here. Maybe you meant '==' instead of '='?"), e.Span);
            }
            throw new PySyntaxError("SyntaxError: cannot assign to expression" + (augmented ? "" : " here. Maybe you meant '==' instead of '='?"), e.Span);
        }

        PyIfStmt ParseIf()
        {
            var kw = Take();
            var s = new PyIfStmt { KwSpan = kw.Span };
            s.Cond = ParseTest();
            ExpectColon();
            s.Body = ParseBlock(kw);
            s.Trailing = s.Body.HeaderComment;
            s.Body.HeaderComment = null;
            if (AtKw("elif"))
            {
                var e = ParseIf();
                s.Elif = e;
            }
            else if (AtKw("else"))
            {
                var ek = Take();
                s.ElseSpan = ek.Span;
                ExpectColon();
                s.ElseBody = ParseBlock(ek);
            }
            int end = s.Elif != null ? s.Elif.Span.End : s.ElseBody != null ? s.ElseBody.Span.End : s.Body.Span.End;
            s.Span = new Span(kw.Start, Math.Max(end, kw.End));
            return s;
        }

        PyWhileStmt ParseWhile()
        {
            var kw = Take();
            var s = new PyWhileStmt { KwSpan = kw.Span };
            s.Cond = ParseTest();
            ExpectColon();
            loopDepth++;
            s.Body = ParseBlock(kw);
            loopDepth--;
            s.Trailing = s.Body.HeaderComment; s.Body.HeaderComment = null;
            s.Span = new Span(kw.Start, Math.Max(s.Body.Span.End, kw.End));
            return s;
        }

        PyForStmt ParseFor()
        {
            var kw = Take();
            var s = new PyForStmt { KwSpan = kw.Span };
            s.Target = ParseTargetList();
            CheckTarget(s.Target, false);
            if (!AtKw("in")) throw new PySyntaxError("SyntaxError: invalid syntax", ErrSpan());
            Take();
            s.Iter = ParseTestList();
            var colon = ExpectColon();
            s.HeaderSpan = new Span(kw.Start, colon.End);
            loopDepth++;
            s.Body = ParseBlock(kw);
            loopDepth--;
            s.Trailing = s.Body.HeaderComment; s.Body.HeaderComment = null;
            s.Span = new Span(kw.Start, Math.Max(s.Body.Span.End, colon.End));
            return s;
        }

        PyExpr ParseTargetList()
        {
            var first = ParsePostfix();
            if (!AtOp(",")) return first;
            var tup = new PySeqExpr { Kind = PySeqKind.Tuple };
            tup.Items.Add(first);
            while (AtOp(","))
            {
                Take();
                if (AtKw("in")) break;
                tup.Items.Add(ParsePostfix());
            }
            tup.Span = new Span(first.Span.Start, tup.Items[tup.Items.Count - 1].Span.End);
            return tup;
        }

        PyDefStmt ParseDef()
        {
            var kw = Take();
            var s = new PyDefStmt();
            var name = ExpectName();
            s.Name = name.Text; s.NameSpan = name.Span;
            ExpectOp("(", "SyntaxError: expected '('");
            bool sawDefault = false;
            while (!AtOp(")"))
            {
                if (AtOp("*") || AtOp("**") || AtOp("/"))
                    throw new PySyntaxError("SyntaxError: '" + Cur.Text + "' parameters are not supported by the game", Cur.Span);
                var pn = ExpectName();
                var prm = new PyParam { Name = pn.Text, Span = pn.Span };
                if (s.Params.Any(x => x.Name == pn.Text))
                    throw new PySyntaxError("SyntaxError: duplicate argument '" + pn.Text + "' in function definition", pn.Span);
                if (AtOp(":")) throw new PySyntaxError("SyntaxError: type annotations are not supported by the game", Cur.Span);
                if (AtOp("="))
                {
                    Take();
                    prm.Default = ParseTest();
                    sawDefault = true;
                }
                else if (sawDefault)
                    throw new PySyntaxError("SyntaxError: parameter without a default follows parameter with a default", pn.Span);
                s.Params.Add(prm);
                if (AtOp(",")) { Take(); continue; }
                if (!AtOp(")")) throw Invalid();
            }
            Take();
            if (AtOp("->")) throw new PySyntaxError("SyntaxError: return annotations are not supported by the game", Cur.Span);
            var colon = ExpectColon();
            s.HeaderSpan = new Span(kw.Start, colon.End);
            int oldLoop = loopDepth;
            loopDepth = 0; funcDepth++;
            s.Body = ParseBlock(kw);
            funcDepth--; loopDepth = oldLoop;
            s.Trailing = s.Body.HeaderComment; s.Body.HeaderComment = null;
            s.Span = new Span(kw.Start, Math.Max(s.Body.Span.End, colon.End));
            return s;
        }

        static string StatementName(PyToken kw) => kw.Text == "def" ? "function definition" : "'" + kw.Text + "' statement";

        /// <summary>NEWLINE INDENT stmt+ DEDENT. A body made only of comments (docs placeholders) is accepted as empty.</summary>
        PyBlock ParseBlock(PyToken kw)
        {
            var b = new PyBlock();
            if (Cur.Kind != PyTk.Newline)
            {
                if (Cur.Kind == PyTk.End)
                    throw new PySyntaxError("IndentationError: expected an indented block after " + StatementName(kw) + " on line " + kw.Line, kw.Span);
                throw new PySyntaxError("SyntaxError: the game needs the body of " + StatementName(kw).Replace("'", "") + " on its own, indented lines", Cur.Span);
            }
            var nl = Take();
            b.HeaderComment = nl.Comment;
            if (Cur.Kind != PyTk.Indent)
            {
                // comment-only body: "for i in sequence:\n\t#do something with i"
                var cs = comments.Where(c => c.OwnLine && c.Start > nl.Start && c.Start < Cur.Start && c.Col > nl.Width).ToList();
                if (cs.Count > 0)
                {
                    b.Width = cs.Min(c => c.Col);
                    b.Limit = Cur.Start;
                    b.Span = new Span(cs[0].Start, cs[cs.Count - 1].Comment.Span.End);
                    return b;
                }
                var at = Cur.Kind == PyTk.End || Cur.End <= Cur.Start ? (Cur.Kind == PyTk.End ? kw.Span : new Span(Cur.Start, Math.Min(src.Length, Cur.Start + 1))) : Cur.Span;
                throw new PySyntaxError("IndentationError: expected an indented block after " + StatementName(kw) + " on line " + kw.Line, at);
            }
            var ind = Take();
            b.Width = ind.Width;
            while (Cur.Kind != PyTk.Dedent && Cur.Kind != PyTk.End)
            {
                if (Cur.Kind == PyTk.Newline) { Take(); continue; }
                if (Cur.Kind == PyTk.Indent) throw new PySyntaxError("IndentationError: unexpected indent", IndentErrSpan());
                b.Stmts.Add(ParseStatement());
            }
            b.Limit = Cur.Start;
            if (Cur.Kind == PyTk.Dedent) Take();
            b.Span = b.Stmts.Count > 0 ? new Span(b.Stmts[0].Span.Start, b.Stmts[b.Stmts.Count - 1].Span.End) : new Span(nl.Start, nl.Start);
            return b;
        }

        // ================================================================== expressions
        bool EndsTuple() =>
            Cur.Kind != PyTk.Name && Cur.Kind != PyTk.Number && Cur.Kind != PyTk.String &&
            !(Cur.Kind == PyTk.Op && (Cur.Text == "(" || Cur.Text == "[" || Cur.Text == "{" || Cur.Text == "-" || Cur.Text == "+"))
            || AtKw("in") || AtKw("if") || AtKw("else") || AtKw("for");

        PyExpr ParseTestList()
        {
            var first = ParseTest();
            if (!AtOp(",")) return first;
            var tup = new PySeqExpr { Kind = PySeqKind.Tuple };
            tup.Items.Add(first);
            int end = first.Span.End;
            while (AtOp(","))
            {
                end = Take().End;
                if (EndsTuple()) break;
                var it = ParseTest();
                tup.Items.Add(it);
                end = it.Span.End;
            }
            tup.Span = new Span(first.Span.Start, end);
            return tup;
        }

        PyExpr ParseTest()
        {
            var e = ParseOr();
            if (AtKw("if"))
            {
                var ifTok = Take();
                var cond = ParseOr();
                if (!AtKw("else")) throw new PySyntaxError("SyntaxError: expected 'else' after 'if' expression", ErrSpan());
                Take();
                var els = ParseTest();
                return new PyIfExpr { Body = e, Cond = cond, Else = els, IfSpan = ifTok.Span, Span = new Span(e.Span.Start, els.Span.End) };
            }
            return e;
        }

        PyExpr ParseOr()
        {
            var l = ParseAnd();
            while (AtKw("or"))
            {
                var op = Take();
                var r = ParseAnd();
                l = new PyBinExpr { Op = "or", Left = l, Right = r, OpSpan = op.Span, Span = new Span(l.Span.Start, r.Span.End) };
            }
            return l;
        }

        PyExpr ParseAnd()
        {
            var l = ParseNot();
            while (AtKw("and"))
            {
                var op = Take();
                var r = ParseNot();
                l = new PyBinExpr { Op = "and", Left = l, Right = r, OpSpan = op.Span, Span = new Span(l.Span.Start, r.Span.End) };
            }
            return l;
        }

        PyExpr ParseNot()
        {
            if (AtKw("not"))
            {
                var op = Take();
                var e = ParseNot();
                return new PyUnaryExpr { Op = "not", Operand = e, OpSpan = op.Span, Span = new Span(op.Start, e.Span.End) };
            }
            return ParseCompare();
        }

        static readonly HashSet<string> CmpOps = new HashSet<string> { "==", "!=", "<", ">", "<=", ">=" };

        PyExpr ParseCompare()
        {
            var first = ParseArith();
            PyCompareExpr c = null;
            while (true)
            {
                string op; Span os;
                if (Cur.Kind == PyTk.Op && CmpOps.Contains(Cur.Text)) { op = Cur.Text; os = Take().Span; }
                else if (AtKw("in")) { op = "in"; os = Take().Span; }
                else if (AtKw("not") && Next.IsKw("in")) { var a = Take(); var b = Take(); op = "not in"; os = new Span(a.Start, b.End); }
                else break;
                if (c == null) { c = new PyCompareExpr(); c.Operands.Add(first); }
                c.Ops.Add(op); c.OpSpans.Add(os);
                c.Operands.Add(ParseArith());
            }
            if (c == null) return first;
            c.Span = new Span(first.Span.Start, c.Operands[c.Operands.Count - 1].Span.End);
            return c;
        }

        PyExpr ParseArith()
        {
            var l = ParseTerm();
            while (AtOp("+") || AtOp("-"))
            {
                var op = Take();
                var r = ParseTerm();
                l = new PyBinExpr { Op = op.Text, Left = l, Right = r, OpSpan = op.Span, Span = new Span(l.Span.Start, r.Span.End) };
            }
            return l;
        }

        PyExpr ParseTerm()
        {
            var l = ParseFactor();
            while (AtOp("*") || AtOp("/") || AtOp("//") || AtOp("%"))
            {
                var op = Take();
                var r = ParseFactor();
                l = new PyBinExpr { Op = op.Text, Left = l, Right = r, OpSpan = op.Span, Span = new Span(l.Span.Start, r.Span.End) };
            }
            return l;
        }

        PyExpr ParseFactor()
        {
            if (AtOp("-") || AtOp("+"))
            {
                var op = Take();
                var e = ParseFactor();
                return new PyUnaryExpr { Op = op.Text, Operand = e, OpSpan = op.Span, Span = new Span(op.Start, e.Span.End) };
            }
            return ParsePower();
        }

        PyExpr ParsePower()
        {
            var l = ParsePostfix();
            while (AtOp("**"))
            {
                var op = Take();
                var r = AtOp("-") || AtOp("+") ? ParseFactor() : ParsePostfix();
                l = new PyBinExpr { Op = "**", Left = l, Right = r, OpSpan = op.Span, Span = new Span(l.Span.Start, r.Span.End) };
            }
            return l;
        }

        PyExpr ParsePostfix()
        {
            var e = ParseAtom();
            while (true)
            {
                if (AtOp("("))
                {
                    Take();
                    var call = new PyCallExpr { Func = e };
                    while (!AtOp(")"))
                    {
                        if (AtOp("*") || AtOp("**")) throw new PySyntaxError("SyntaxError: '" + Cur.Text + "' arguments are not supported by the game", Cur.Span);
                        if (Cur.Kind == PyTk.Name && Next.IsOp("=") && !PyLexer.Keywords.Contains(Cur.Text))
                            throw new PySyntaxError("SyntaxError: keyword arguments (" + Cur.Text + "=...) are not supported by the game", new Span(Cur.Start, Next.End));
                        var a = ParseTest();
                        if (AtKw("for")) throw new PySyntaxError("SyntaxError: generator expressions are not supported by the game", Cur.Span);
                        call.Args.Add(a);
                        if (AtOp(",")) { Take(); continue; }
                        if (!AtOp(")")) throw new PySyntaxError(EndsAtLine() ? "SyntaxError: '(' was never closed" : "SyntaxError: invalid syntax. Perhaps you forgot a comma?", ErrSpan());
                    }
                    var close = Take();
                    call.Span = new Span(e.Span.Start, close.End);
                    e = call;
                    continue;
                }
                if (AtOp("["))
                {
                    var open = Take();
                    if (AtOp(":")) throw new PySyntaxError("slicing (a[i:j]) is not supported by JavaFarmer", Cur.Span);
                    var idx = ParseTestList();
                    if (AtOp(":")) throw new PySyntaxError("slicing (a[i:j]) is not supported by JavaFarmer", Cur.Span);
                    var close = ExpectOp("]");
                    e = new PyIndexExpr { Target = e, Index = idx, Span = new Span(e.Span.Start, close.End) };
                    continue;
                }
                if (AtOp("."))
                {
                    Take();
                    if (Cur.Kind != PyTk.Name || PyLexer.Keywords.Contains(Cur.Text)) throw Invalid();
                    var n = Take();
                    e = new PyAttrExpr { Target = e, Name = n.Text, NameSpan = n.Span, Span = new Span(e.Span.Start, n.End) };
                    continue;
                }
                return e;
            }
        }

        bool EndsAtLine() => Cur.Kind == PyTk.Newline || Cur.Kind == PyTk.End;

        PyExpr ParseAtom()
        {
            var tok = Cur;
            switch (tok.Kind)
            {
                case PyTk.Number:
                    {
                        Take();
                        var n = new PyNumExpr { Text = tok.Text, Span = tok.Span };
                        if (tok.Text.IndexOf('.') < 0 && long.TryParse(tok.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) n.Int = v;
                        else
                        {
                            n.IsFloat = true;
                            if (!double.TryParse(tok.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out n.Float)) n.Float = double.PositiveInfinity;
                            if (tok.Text.IndexOf('.') < 0) n.IsFloat = true;   // integer too large for long
                        }
                        return n;
                    }
                case PyTk.String:
                    Take();
                    return new PyStrExpr { Value = tok.Text.Substring(1, tok.Text.Length - 2), Quote = tok.Text[0], Span = tok.Span };
                case PyTk.Name:
                    if (PyLexer.Keywords.Contains(tok.Text)) throw Invalid();
                    if (PyLexer.UnsupportedKeywords.Contains(tok.Text) && (tok.Text == "lambda" || tok.Text == "yield" || tok.Text == "await"))
                        throw new PySyntaxError("SyntaxError: '" + tok.Text + "' is not supported by the game", tok.Span);
                    Take();
                    return new PyNameExpr { Id = tok.Text, Span = tok.Span };
                case PyTk.Op:
                    switch (tok.Text)
                    {
                        case "(":
                            {
                                Take();
                                if (AtOp(")")) { var c0 = Take(); return new PySeqExpr { Kind = PySeqKind.Tuple, Parens = true, Span = new Span(tok.Start, c0.End) }; }
                                var first = ParseTest();
                                if (AtKw("for")) throw new PySyntaxError("SyntaxError: generator expressions are not supported by the game", Cur.Span);
                                if (AtOp(","))
                                {
                                    var tup = new PySeqExpr { Kind = PySeqKind.Tuple, Parens = true };
                                    tup.Items.Add(first);
                                    while (AtOp(","))
                                    {
                                        Take();
                                        if (AtOp(")")) break;
                                        tup.Items.Add(ParseTest());
                                    }
                                    var close = ExpectOp(")", EndsAtLine() ? "SyntaxError: '(' was never closed" : null);
                                    tup.Span = new Span(tok.Start, close.End);
                                    return tup;
                                }
                                ExpectOp(")", EndsAtLine() ? "SyntaxError: '(' was never closed" : null);
                                return first;
                            }
                        case "[":
                            {
                                Take();
                                var l = new PySeqExpr { Kind = PySeqKind.List, Parens = true };
                                while (!AtOp("]"))
                                {
                                    l.Items.Add(ParseTest());
                                    if (AtKw("for")) throw new PySyntaxError("SyntaxError: list comprehensions are not supported by the game", Cur.Span);
                                    if (AtOp(",")) { Take(); continue; }
                                    if (!AtOp("]")) throw new PySyntaxError("SyntaxError: invalid syntax. Perhaps you forgot a comma?", ErrSpan());
                                }
                                var close = Take();
                                l.Span = new Span(tok.Start, close.End);
                                return l;
                            }
                        case "{":
                            {
                                Take();
                                if (AtOp("}")) { var c0 = Take(); return new PyDictExpr { Span = new Span(tok.Start, c0.End) }; }
                                var first = ParseTest();
                                if (AtKw("for")) throw new PySyntaxError("SyntaxError: comprehensions are not supported by the game", Cur.Span);
                                if (AtOp(":"))
                                {
                                    var d = new PyDictExpr();
                                    Take();
                                    d.Keys.Add(first); d.Values.Add(ParseTest());
                                    while (AtOp(","))
                                    {
                                        Take();
                                        if (AtOp("}")) break;
                                        d.Keys.Add(ParseTest());
                                        ExpectOp(":", "SyntaxError: ':' expected after dictionary key");
                                        d.Values.Add(ParseTest());
                                    }
                                    var close = ExpectOp("}", "SyntaxError: invalid syntax. Perhaps you forgot a comma?");
                                    d.Span = new Span(tok.Start, close.End);
                                    return d;
                                }
                                var s = new PySeqExpr { Kind = PySeqKind.Set, Parens = true };
                                s.Items.Add(first);
                                while (AtOp(","))
                                {
                                    Take();
                                    if (AtOp("}")) break;
                                    s.Items.Add(ParseTest());
                                }
                                var sc = ExpectOp("}", "SyntaxError: invalid syntax. Perhaps you forgot a comma?");
                                s.Span = new Span(tok.Start, sc.End);
                                return s;
                            }
                    }
                    break;
            }
            if (tok.Kind == PyTk.Indent) throw new PySyntaxError("IndentationError: unexpected indent", IndentErrSpan());
            throw Invalid();
        }

        // ================================================================== comments
        int ci;
        List<PyComment> attach;

        /// <summary>
        /// Own-line comments become Leading comments of the next statement of the innermost block whose indentation they
        /// match, or TrailingComments of the block they end (comment column &gt;= block indentation); comments inside
        /// brackets become Leading comments of their statement. Same-line comments were set by the parser (Trailing).
        /// </summary>
        void AttachComments(PyModuleAst m)
        {
            attach = comments.Where(c => c.OwnLine || c.Inner).OrderBy(c => c.Start).ToList();
            ci = 0;
            AttachBlock(m.Body, src.Length + 1, true);
        }

        void AttachBlock(PyBlock b, int limit, bool takeAll)
        {
            foreach (var s in b.Stmts)
            {
                while (ci < attach.Count && attach[ci].Start < s.Span.Start) s.AddLeading(attach[ci++].Comment);
                switch (s)
                {
                    case PyIfStmt i: AttachIf(i); break;
                    case PyWhileStmt w: AttachBlock(w.Body, w.Body.Limit, false); break;
                    case PyForStmt f: AttachBlock(f.Body, f.Body.Limit, false); break;
                    case PyDefStmt d: AttachBlock(d.Body, d.Body.Limit, false); break;
                    default:
                        while (ci < attach.Count && attach[ci].Inner && attach[ci].Start < s.Span.End) s.AddLeading(attach[ci++].Comment);
                        break;
                }
            }
            while (ci < attach.Count && attach[ci].Start < limit && (takeAll || attach[ci].Inner || attach[ci].Col >= b.Width))
                (b.TrailingComments ?? (b.TrailingComments = new List<IrComment>())).Add(attach[ci++].Comment);
        }

        void AttachIf(PyIfStmt i)
        {
            if (i.Elif != null)
            {
                AttachBlock(i.Body, i.Elif.KwSpan.Start, true);
                AttachIf(i.Elif);
            }
            else if (i.ElseBody != null)
            {
                AttachBlock(i.Body, i.ElseSpan.Start, true);
                AttachBlock(i.ElseBody, i.ElseBody.Limit, false);
            }
            else AttachBlock(i.Body, i.Body.Limit, false);
        }
    }
}
