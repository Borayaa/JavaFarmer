using System;
using System.Collections.Generic;

namespace JavaFarmer.Compiler.ZigImpl
{
    internal sealed class ZParseError : Exception
    {
        public readonly Diagnostic Diag;
        public ZParseError(string msg, int start, int end) : base(msg) { Diag = new Diagnostic(msg, start, end); }
    }

    /// <summary>
    /// Recursive-descent parser for the Zig subset (grammar of lib/std/zig/Parse.zig, Zig 0.17.1). Parse errors use
    /// the wording of std.zig.Ast.renderError. The first error aborts (ZParseError).
    /// </summary>
    internal sealed class ZigParser
    {
        readonly List<ZToken> toks = new List<ZToken>();
        readonly string src;
        int p;
        public readonly ZFile File = new ZFile();

        ZigParser(string source, List<ZToken> all)
        {
            src = source;
            ZToken prev = null;
            foreach (var t in all)
            {
                if (t.Kind == ZTk.Whitespace) continue;
                if (t.Kind == ZTk.Comment) { File.Comments.Add(t); continue; }
                if (t.Kind == ZTk.Ident && !(prev != null && prev.Kind == ZTk.Op && prev.Text == "."))
                {
                    var n = t.Value ?? t.Text;
                    File.IdentCount[n] = File.IdentCount.TryGetValue(n, out var c) ? c + 1 : 1;
                }
                toks.Add(t);
                prev = t;
            }
        }

        public static ZFile Parse(string source, out Diagnostic error)
        {
            error = null;
            var all = new ZigLexer(source).LexAll();
            var ps = new ZigParser(source, all);
            try { ps.ParseFile(); }
            catch (ZParseError e) { error = e.Diag; }
            return ps.File;
        }

        // ------------------------------------------------------------------ token helpers
        ZToken Cur => toks[p];
        ZToken Peek(int k = 1) => toks[Math.Min(p + k, toks.Count - 1)];
        ZToken Prev => toks[Math.Max(0, p - 1)];
        bool IsOp(string s) => Cur.Kind == ZTk.Op && Cur.Text == s;
        bool IsOpAt(int k, string s) { var t = Peek(k); return t.Kind == ZTk.Op && t.Text == s; }
        bool IsKw(string s) => Cur.Kind == ZTk.Keyword && Cur.Text == s;
        bool IsKwAt(int k, string s) { var t = Peek(k); return t.Kind == ZTk.Keyword && t.Text == s; }
        ZToken Advance() { var t = Cur; if (p < toks.Count - 1) p++; return t; }
        bool AcceptOp(string s) { if (IsOp(s)) { Advance(); return true; } return false; }
        bool AcceptKw(string s) { if (IsKw(s)) { Advance(); return true; } return false; }

        static string Symbol(ZToken t)
        {
            switch (t.Kind)
            {
                case ZTk.Ident: return "an identifier";
                case ZTk.String: return "a string literal";
                case ZTk.MultiString: return "a multiline string literal";
                case ZTk.Char: return "a character literal";
                case ZTk.Eof: return "EOF";
                case ZTk.Builtin: return "a builtin function";
                case ZTk.Int: case ZTk.Float: return "a number literal";
                case ZTk.Invalid: return "invalid token";
            }
            return t.Text;
        }

        ZParseError Fail(string msg, ZToken at) => new ZParseError(msg, at.Start, Math.Max(at.End, at.Start + (at.Kind == ZTk.Eof ? 0 : 1)));
        ZParseError FailPrev(string msg) => Fail(msg, Prev);

        ZToken ExpectOp(string s)
        {
            if (IsOp(s)) return Advance();
            if (s == ";") throw FailPrev("expected ';' after statement");
            throw Fail("expected '" + s + "', found '" + Symbol(Cur) + "'", Cur);
        }

        ZToken ExpectIdent()
        {
            if (Cur.Kind == ZTk.Ident) return Advance();
            throw Fail("expected 'an identifier', found '" + Symbol(Cur) + "'", Cur);
        }

        void ExpectSemi(bool decl)
        {
            if (IsOp(";")) { Advance(); return; }
            throw FailPrev(decl ? "expected ';' after declaration" : "expected ';' after statement");
        }

        // ------------------------------------------------------------------ file
        void ParseFile()
        {
            while (Cur.Kind != ZTk.Eof)
            {
                int start = Cur.Start;
                if (IsKw("test"))
                {
                    Advance();
                    if (Cur.Kind == ZTk.String || Cur.Kind == ZTk.Ident) Advance();
                    var b = ParseBlock();
                    File.Decls.Add(new ZTopUnsupported { What = "test blocks are", Start = start, End = b.End });
                    continue;
                }
                if (IsKw("comptime") && IsOpAt(1, "{"))
                {
                    Advance();
                    var b = ParseBlock();
                    File.Decls.Add(new ZTopUnsupported { What = "comptime blocks are", Start = start, End = b.End });
                    continue;
                }
                bool isPub = AcceptKw("pub");
                bool isExport = false, isExtern = false, isInline = false;
                if (AcceptKw("export")) isExport = true;
                else if (AcceptKw("extern")) { isExtern = true; if (Cur.Kind == ZTk.String) Advance(); }
                else if (AcceptKw("inline") || AcceptKw("noinline")) isInline = true;
                if (IsKw("threadlocal")) Advance();
                if (IsKw("fn"))
                {
                    var f = ParseFnDecl(start);
                    f.IsPub = isPub; f.IsExport = isExport; f.IsExtern = isExtern; f.IsInline = isInline;
                    File.Decls.Add(f);
                    continue;
                }
                if (IsKw("const") || IsKw("var"))
                {
                    var d = ParseVarDeclProto(start);
                    d.IsPub = isPub; d.IsExtern = isExtern || isExport;
                    if (AcceptOp("=")) d.Init = ParseExpr();
                    else if (IsOp("==")) throw Fail("variable initialized with '==' instead of '='", Cur);
                    ExpectSemi(true);
                    d.End = Prev.End;
                    File.Decls.Add(d);
                    continue;
                }
                if (isPub) throw Fail("expected function or variable declaration after pub", Cur);
                if (Cur.Kind == ZTk.Ident && IsOpAt(1, ":"))
                {
                    // container field: the file would be a struct with fields
                    var nameTok = Advance();
                    Advance();
                    ParseTypeExpr();
                    if (AcceptOp("=")) ParseExpr();
                    if (!AcceptOp(",") && !IsOp("}") && Cur.Kind != ZTk.Eof) { }
                    File.Decls.Add(new ZTopUnsupported { What = "container fields are", Start = nameTok.Start, End = Prev.End });
                    continue;
                }
                if (Cur.Kind == ZTk.Ident && (IsOpAt(1, "(") || IsOpAt(1, "=") || IsOpAt(1, ".")))
                {
                    // a statement at top level: Zig has no top-level statements
                    throw Fail("expected test, comptime, var decl, or container field, found '" + Symbol(Cur) + "'", Cur);
                }
                throw Fail("expected test, comptime, var decl, or container field, found '" + Symbol(Cur) + "'", Cur);
            }
        }

        ZFnDecl ParseFnDecl(int start)
        {
            Advance(); // fn
            var f = new ZFnDecl { Start = start };
            if (Cur.Kind != ZTk.Ident) throw Fail("expected 'an identifier', found '" + Symbol(Cur) + "'", Cur);
            var name = Advance();
            f.Name = name.Value; f.NameStart = name.Start; f.NameEnd = name.End;
            if (!IsOp("(")) throw Fail("expected parameter list, found '" + Symbol(Cur) + "'", Cur);
            f.Params = ParseParams();
            if (IsKw("callconv")) { Advance(); ExpectOp("("); ParseExpr(); ExpectOp(")"); f.CallConv = "callconv"; }
            if (IsKw("align") || IsKw("linksection") || IsKw("addrspace")) { Advance(); ExpectOp("("); ParseExpr(); ExpectOp(")"); }
            if (IsOp("{") || IsOp(";")) throw Fail("expected return type expression, found '" + Symbol(Cur) + "'", Cur);
            if (AcceptOp("!")) f.RetBang = true;
            f.Ret = ParseTypeExpr();
            if (f.Ret is ZErrorUnionType eu && !f.RetBang) { }
            f.HeaderEnd = Prev.End;
            if (IsOp(";")) throw Fail("non-extern function has no body", Cur);
            if (!IsOp("{")) throw Fail("expected ';' or block after function prototype", Cur);
            f.Body = ParseBlock();
            f.End = f.Body.End;
            return f;
        }

        List<ZParam> ParseParams()
        {
            ExpectOp("(");
            var ps = new List<ZParam>();
            while (!IsOp(")"))
            {
                var prm = new ZParam { Start = Cur.Start };
                if (AcceptKw("comptime")) prm.IsComptime = true;
                if (AcceptKw("noalias")) prm.NoAlias = true;
                if (IsOp("...")) { Advance(); prm.Name = "..."; prm.IsAnytype = true; prm.End = Prev.End; ps.Add(prm); if (!AcceptOp(",")) break; continue; }
                if (Cur.Kind == ZTk.Ident && IsOpAt(1, ":"))
                {
                    var n = Advance();
                    prm.Name = n.Value; prm.NameStart = n.Start; prm.NameEnd = n.End;
                    Advance();
                }
                if (AcceptKw("anytype")) prm.IsAnytype = true;
                else prm.Type = ParseTypeExpr();
                prm.End = Prev.End;
                ps.Add(prm);
                if (!AcceptOp(","))
                {
                    if (!IsOp(")")) throw Fail("expected ',' after parameter", Cur);
                    break;
                }
            }
            ExpectOp(")");
            return ps;
        }

        ZVarDecl ParseVarDeclProto(int start)
        {
            var kw = Advance();
            var d = new ZVarDecl { Start = start, IsConst = kw.Text == "const", KwStart = kw.Start, KwEnd = kw.End };
            if (Cur.Kind != ZTk.Ident) throw Fail("expected 'an identifier', found '" + Symbol(Cur) + "'", Cur);
            var n = Advance();
            d.Name = n.Value; d.NameStart = n.Start; d.NameEnd = n.End;
            if (AcceptOp(":")) d.Type = ParseTypeExpr();
            if (IsKw("align") || IsKw("linksection") || IsKw("addrspace")) { Advance(); ExpectOp("("); ParseExpr(); ExpectOp(")"); }
            d.End = Prev.End;
            return d;
        }

        // ------------------------------------------------------------------ statements
        ZBlock ParseBlock(string label = null)
        {
            var open = ExpectOp("{");
            var b = new ZBlock { Start = open.Start, Label = label };
            while (!IsOp("}"))
            {
                if (Cur.Kind == ZTk.Eof) throw Fail("expected '}', found 'EOF'", Cur);
                b.Stmts.Add(ParseStatement());
            }
            b.End = Advance().End;
            return b;
        }

        ZStmt ParseStatement()
        {
            int start = Cur.Start;
            if (IsKw("comptime"))
            {
                Advance();
                if (IsKw("var") || IsKw("const"))
                {
                    var s = ParseVarDeclStatement(start);
                    return new ZUnsupportedStmt { What = "comptime variables are", Start = start, End = s.End };
                }
                var inner = ParseBlockExprStatement();
                return new ZUnsupportedStmt { What = "comptime blocks are", Start = start, End = inner.End };
            }
            if (IsKw("nosuspend") || IsKw("suspend"))
            {
                var kw = Advance().Text;
                var inner = ParseBlockExprStatement();
                return new ZUnsupportedStmt { What = "'" + kw + "' is", Start = start, End = inner.End };
            }
            if (IsKw("defer") || IsKw("errdefer"))
            {
                bool err = Advance().Text == "errdefer";
                if (err && IsOp("|")) ParseCapture();
                var inner = ParseBlockExprStatement();
                return new ZDefer { Body = inner, IsErr = err, Start = start, End = inner.End };
            }
            if (IsKw("var") || IsKw("const")) return ParseVarDeclStatement(start);
            if (IsKw("if")) return ParseIfStatement();
            // labels
            string label = null;
            if (Cur.Kind == ZTk.Ident && IsOpAt(1, ":") && (IsOpAt(2, "{") || IsKwAt(2, "while") || IsKwAt(2, "for") || IsKwAt(2, "inline") || IsKwAt(2, "switch")))
            {
                label = Advance().Value;
                Advance();
            }
            bool inl = false;
            if (IsKw("inline") && (IsKwAt(1, "for") || IsKwAt(1, "while"))) { Advance(); inl = true; }
            if (IsKw("while")) { var w = ParseWhile(start); w.Label = label; w.Inline = inl; return w; }
            if (IsKw("for")) { var f = ParseFor(start); f.Label = label; f.Inline = inl; return f; }
            if (IsOp("{")) { var b = ParseBlock(label); b.Start = start; return b; }
            if (IsKw("switch"))
            {
                var sw = ParseSwitch();
                sw.Label = label;
                sw.Start = start;
                // a switch statement needs no ';' (it may be followed by one: an empty statement is an error in Zig)
                return sw;
            }
            if (label != null) throw Fail("expected 'while', 'for', 'inline', or '{', found '" + Symbol(Cur) + "'", Cur);
            if (IsOp(";")) throw Fail("expected statement, found ';'", Cur);
            return ParseExprStatement(start);
        }

        ZStmt ParseVarDeclStatement(int start)
        {
            var d = ParseVarDeclProto(start);
            if (IsOp(","))
            {
                var ds = new ZDestructure { Start = start };
                ds.Targets.Add((d, null));
                while (AcceptOp(","))
                {
                    if (IsKw("var") || IsKw("const")) ds.Targets.Add((ParseVarDeclProto(Cur.Start), null));
                    else ds.Targets.Add((null, ParseExpr()));
                }
                if (!IsOp("=")) throw Fail("expected '=', found '" + Symbol(Cur) + "'", Cur);
                ds.EqStart = Advance().Start;
                ds.Value = ParseExpr();
                ExpectSemi(true);
                ds.End = Prev.End;
                return ds;
            }
            if (IsOp("==")) throw Fail("variable initialized with '==' instead of '='", Cur);
            if (AcceptOp("=")) d.Init = ParseExpr();
            ExpectSemi(true);
            d.End = Prev.End;
            return d;
        }

        static readonly HashSet<string> AssignOps = new HashSet<string>
        {
            "=", "+=", "-=", "*=", "/=", "%=", "<<=", ">>=", "&=", "|=", "^=", "+%=", "-%=", "*%=", "+|=", "-|=", "*|=", "<<|=",
        };

        /// <summary>Expr [AssignOp Expr] ';' (or a destructuring assignment).</summary>
        ZStmt ParseExprStatement(int start)
        {
            var s = ParseAssignExpr(start);
            ExpectSemi(false);
            s.End = Prev.End;
            return s;
        }

        /// <summary>An expression statement or assignment without the ';'.</summary>
        ZStmt ParseAssignExpr(int start, bool destructure = true)
        {
            var e = ParseExpr();
            if (Cur.Kind == ZTk.Op && AssignOps.Contains(Cur.Text))
            {
                var op = Advance();
                var v = ParseExpr();
                return new ZAssign { Target = e, Op = op.Text, Value = v, OpStart = op.Start, OpEnd = op.End, Start = start, End = v.End };
            }
            if (destructure && IsOp(","))
            {
                var ds = new ZDestructure { Start = start };
                ds.Targets.Add((null, e));
                while (AcceptOp(","))
                {
                    if (IsKw("var") || IsKw("const")) ds.Targets.Add((ParseVarDeclProto(Cur.Start), null));
                    else ds.Targets.Add((null, ParseExpr()));
                }
                if (!IsOp("=")) throw Fail("expected '=', found '" + Symbol(Cur) + "'", Cur);
                ds.EqStart = Advance().Start;
                ds.Value = ParseExpr();
                ds.End = ds.Value.End;
                return ds;
            }
            if (e is ZControlExpr ce)
            {
                switch (ce.Kw)
                {
                    case "return": return new ZReturn { Value = ce.Value, Start = ce.Start, End = ce.End };
                    case "break": return new ZBreak { Label = ce.Label, Value = ce.Value, Start = ce.Start, End = ce.End };
                    case "continue": return new ZContinue { Label = ce.Label, Start = ce.Start, End = ce.End };
                }
            }
            return new ZExprStmt { Expr = e, Start = start, End = e.End };
        }

        /// <summary>BlockExpr / AssignExpr ';' (bodies of defer, else branches, loops without braces).</summary>
        ZStmt ParseBlockExprStatement()
        {
            int start = Cur.Start;
            if (IsOp("{")) return ParseBlock();
            if (Cur.Kind == ZTk.Ident && IsOpAt(1, ":") && IsOpAt(2, "{"))
            {
                var label = Advance().Value; Advance();
                var b = ParseBlock(label); b.Start = start; return b;
            }
            return ParseExprStatement(start);
        }

        ZCapture ParseCapture()
        {
            var open = ExpectOp("|");
            var c = new ZCapture { Start = open.Start };
            while (true)
            {
                bool r = AcceptOp("*");
                var n = ExpectIdent();
                c.Names.Add((n.Value, n.Start, n.End, r));
                if (AcceptOp(",")) { if (IsOp("|")) break; continue; }
                break;
            }
            c.End = ExpectOp("|").End;
            return c;
        }

        /// <summary>A statement body after if/while/for/else: a block (no ';') or an assignment expression with ';'.</summary>
        ZStmt ParseBody(out bool blockBody)
        {
            int start = Cur.Start;
            blockBody = false;
            if (IsOp("{")) { blockBody = true; return ParseBlock(); }
            if (Cur.Kind == ZTk.Ident && IsOpAt(1, ":") && IsOpAt(2, "{")) { var l = Advance().Value; Advance(); blockBody = true; var b = ParseBlock(l); b.Start = start; return b; }
            if (IsKw("if")) { blockBody = true; return ParseIfStatement(); }
            if (IsKw("while")) { blockBody = true; return ParseWhile(start); }
            if (IsKw("for")) { blockBody = true; return ParseFor(start); }
            if (IsKw("switch")) { blockBody = true; return ParseSwitch(); }
            return ParseAssignExpr(start);
        }

        ZIf ParseIfStatement()
        {
            var kw = Advance();
            var s = new ZIf { Start = kw.Start, KwEnd = kw.End };
            ExpectOp("(");
            s.Cond = ParseExpr();
            ExpectOp(")");
            if (IsOp("|")) s.Capture = ParseCapture();
            s.Then = ParseBody(out bool thenBlock);
            if (IsKw("else"))
            {
                Advance();
                if (IsOp("|")) s.ElseCapture = ParseCapture();
                s.Else = ParseBody(out bool elseBlock);
                if (!elseBlock) ExpectSemi(false);
            }
            else if (!thenBlock)
            {
                if (!IsOp(";")) throw FailPrev("expected ';' or 'else' after statement");
                Advance();
            }
            s.End = Prev.End;
            return s;
        }

        ZWhile ParseWhile(int start)
        {
            var kw = Advance();
            var w = new ZWhile { Start = start, KwEnd = kw.End };
            ExpectOp("(");
            w.Cond = ParseExpr();
            ExpectOp(")");
            if (IsOp("|")) w.Capture = ParseCapture();
            if (AcceptOp(":"))
            {
                ExpectOp("(");
                w.Cont = ParseAssignExpr(Cur.Start);
                ExpectOp(")");
            }
            w.Body = ParseBody(out bool bodyBlock);
            if (IsKw("else"))
            {
                Advance();
                if (IsOp("|")) ParseCapture();
                w.Else = ParseBody(out bool eb);
                if (!eb) ExpectSemi(false);
            }
            else if (!bodyBlock) ExpectSemi(false);
            w.End = Prev.End;
            return w;
        }

        ZFor ParseFor(int start)
        {
            var kw = Advance();
            var f = new ZFor { Start = start, KwEnd = kw.End };
            ExpectOp("(");
            while (!IsOp(")"))
            {
                var inp = new ZForInput { Start = Cur.Start };
                var e = ParseExpr();
                if (AcceptOp(".."))
                {
                    inp.IsRange = true;
                    inp.RangeFrom = e;
                    if (!IsOp(",") && !IsOp(")")) inp.RangeTo = ParseExpr();
                }
                else inp.Expr = e;
                inp.End = Prev.End;
                f.Inputs.Add(inp);
                if (!AcceptOp(","))
                {
                    if (!IsOp(")")) throw Fail("expected ',' after for operand", Cur);
                    break;
                }
            }
            ExpectOp(")");
            if (!IsOp("|")) throw Fail("expected loop payload, found '" + Symbol(Cur) + "'", Cur);
            f.Capture = ParseCapture();
            f.HeaderEnd = Prev.End;
            f.Body = ParseBody(out bool bodyBlock);
            if (IsKw("else"))
            {
                Advance();
                f.Else = ParseBody(out bool eb);
                if (!eb) ExpectSemi(false);
            }
            else if (!bodyBlock) ExpectSemi(false);
            f.End = Prev.End;
            return f;
        }

        ZSwitch ParseSwitch()
        {
            var kw = Advance();
            var sw = new ZSwitch { Start = kw.Start, KwEnd = kw.End };
            ExpectOp("(");
            sw.Selector = ParseExpr();
            ExpectOp(")");
            ExpectOp("{");
            while (!IsOp("}"))
            {
                if (Cur.Kind == ZTk.Eof) throw Fail("expected '}', found 'EOF'", Cur);
                var pr = new ZProng { Start = Cur.Start };
                if (IsKw("inline")) Advance();
                if (AcceptKw("else")) pr.IsElse = true;
                else
                {
                    while (true)
                    {
                        var item = ParseExpr();
                        if (AcceptOp("..."))
                        {
                            var to = ParseExpr();
                            (pr.Ranges ?? (pr.Ranges = new List<(ZExpr, ZExpr)>())).Add((item, to));
                        }
                        else pr.Items.Add(item);
                        if (AcceptOp(",")) { if (IsOp("=>")) break; continue; }
                        break;
                    }
                }
                pr.ArrowEnd = ExpectOp("=>").End;
                if (IsOp("|")) pr.Capture = ParseCapture();
                int bs = Cur.Start;
                if (IsOp("{")) pr.Body = ParseBlock();
                else pr.Body = ParseAssignExpr(bs, false);   // SingleAssignExpr: no destructuring
                pr.End = Prev.End;
                sw.Prongs.Add(pr);
                if (!AcceptOp(","))
                {
                    if (!IsOp("}"))
                    {
                        if (pr.Body is ZBlock) continue;   // a block prong needs no comma
                        throw FailPrev("expected ',' after switch prong");
                    }
                }
            }
            sw.End = Advance().End;
            return sw;
        }

        // ------------------------------------------------------------------ expressions
        static int Prec(ZToken t)
        {
            if (t.Kind == ZTk.Keyword)
            {
                switch (t.Text) { case "or": return 10; case "and": return 20; case "orelse": case "catch": return 40; }
                return -1;
            }
            if (t.Kind != ZTk.Op) return -1;
            switch (t.Text)
            {
                case "==": case "!=": case "<": case ">": case "<=": case ">=": return 30;
                case "&": case "^": case "|": return 40;
                case "<<": case ">>": case "<<|": return 50;
                case "+": case "-": case "++": case "+%": case "-%": case "+|": case "-|": return 60;
                case "||": case "*": case "/": case "%": case "*%": case "*|": case "**": return 70;
                case "&&": return 20;
            }
            return -1;
        }

        public ZExpr ParseExpr() => ParseBinary(0);

        ZExpr ParseBinary(int minPrec)
        {
            var left = ParsePrefix();
            int banned = -1;
            while (true)
            {
                var t = Cur;
                int prec = Prec(t);
                if (prec < minPrec || prec < 0) break;
                if (prec == banned) throw Fail("comparison operators cannot be chained", t);
                if (t.Kind == ZTk.Op && t.Text == "&&")
                    throw Fail("ambiguous use of '&&'; use 'and' for logical AND, or change whitespace to ' & &' for bitwise AND", t);
                Advance();
                if (t.Kind == ZTk.Keyword && t.Text == "catch")
                {
                    ZCapture cap = null;
                    if (IsOp("|")) cap = ParseCapture();
                    ZExpr rhs;
                    if (IsOp("{")) { var b = ParseBlock(); rhs = new ZBlockValue { Block = b, Start = b.Start, End = b.End }; }
                    else rhs = ParseBinary(prec + 1);
                    left = new ZCatchExpr { Left = left, Capture = cap, Right = rhs, OpStart = t.Start, OpEnd = t.End, Start = left.Start, End = rhs.End };
                    continue;
                }
                ZExpr right;
                if (t.Kind == ZTk.Keyword && t.Text == "orelse" && IsOp("{")) { var b = ParseBlock(); right = new ZBlockValue { Block = b, Start = b.Start, End = b.End }; }
                else right = ParseBinary(prec + 1);
                left = new ZBinary { Op = t.Text, Left = left, Right = right, OpStart = t.Start, OpEnd = t.End, Start = left.Start, End = right.End };
                if (prec == 30) banned = 30;
            }
            return left;
        }

        ZExpr ParsePrefix()
        {
            var t = Cur;
            if ((t.Kind == ZTk.Op && (t.Text == "!" || t.Text == "-" || t.Text == "~" || t.Text == "-%" || t.Text == "&")) || (t.Kind == ZTk.Keyword && t.Text == "try"))
            {
                Advance();
                var operand = ParsePrefix();
                return new ZUnary { Op = t.Text, Operand = operand, Start = t.Start, OpEnd = t.End, End = operand.End };
            }
            return ParsePrimary();
        }

        ZExpr ParsePrimary()
        {
            var t = Cur;
            if (t.Kind == ZTk.Keyword)
            {
                switch (t.Text)
                {
                    case "if": return ParseIfExpr();
                    case "return":
                        {
                            Advance();
                            ZExpr v = null;
                            if (StartsExpr()) v = ParseExpr();
                            return new ZControlExpr { Kw = "return", Value = v, Start = t.Start, End = v?.End ?? t.End };
                        }
                    case "break":
                    case "continue":
                        {
                            Advance();
                            string label = null;
                            int end = t.End;
                            if (IsOp(":")) { Advance(); var l = ExpectIdent(); label = l.Value; end = l.End; }
                            ZExpr v = null;
                            if (t.Text == "break" && StartsExpr()) { v = ParseExpr(); end = v.End; }
                            return new ZControlExpr { Kw = t.Text, Label = label, Value = v, Start = t.Start, End = end };
                        }
                    case "comptime":
                        {
                            Advance();
                            var e = ParseExpr();
                            return new ZUnsupportedExpr { What = "comptime expressions are", Start = t.Start, End = e.End };
                        }
                    case "while":
                    case "for":
                        {
                            var s = t.Text == "while" ? (ZStmt)ParseWhile(t.Start) : ParseFor(t.Start);
                            return new ZUnsupportedExpr { What = "loop expressions are", Start = t.Start, End = s.End };
                        }
                }
            }
            if (t.Kind == ZTk.Op && t.Text == "{")
            {
                var b = ParseBlock();
                return new ZBlockValue { Block = b, Start = b.Start, End = b.End };
            }
            if (t.Kind == ZTk.Ident && IsOpAt(1, ":") && IsOpAt(2, "{"))
            {
                var label = Advance().Value; Advance();
                var b = ParseBlock(label);
                return new ZUnsupportedExpr { What = "labeled blocks are", Start = t.Start, End = b.End };
            }
            var e2 = ParseTypeExpr();
            if (IsOp("{") && IsInitStartable(e2)) e2 = ParseInitList(e2);
            return e2;
        }

        /// <summary>T{ ... } is only an init list after a type-like expression (not after a call result like f() {).</summary>
        static bool IsInitStartable(ZExpr e) =>
            e is ZIdent || e is ZField || e is ZArrayType || e is ZSliceType || e is ZCall || e is ZBuiltinCall || e is ZTupleType;

        bool StartsExpr()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case ZTk.Ident: case ZTk.Builtin: case ZTk.Int: case ZTk.Float: case ZTk.String: case ZTk.MultiString: case ZTk.Char: return true;
                case ZTk.Keyword:
                    switch (t.Text) { case "if": case "switch": case "try": case "unreachable": case "fn": case "struct": case "error": case "comptime": case "return": case "break": case "continue": return true; }
                    return false;
                case ZTk.Op:
                    switch (t.Text) { case "(": case ".": case "!": case "-": case "&": case "[": case "?": case "*": case "~": case "{": return true; }
                    return false;
            }
            return false;
        }

        ZIfExpr ParseIfExpr()
        {
            var kw = Advance();
            var e = new ZIfExpr { Start = kw.Start, KwEnd = kw.End };
            ExpectOp("(");
            e.Cond = ParseExpr();
            ExpectOp(")");
            if (IsOp("|")) e.Capture = ParseCapture();
            e.Then = ParseExpr();
            if (IsKw("else"))
            {
                Advance();
                if (IsOp("|")) ParseCapture();
                e.Else = ParseExpr();
            }
            e.End = Prev.End;
            return e;
        }

        /// <summary>TypeExpr: prefix type operators (?T, []T, *T, [N]T) then an error-union / suffix expression.</summary>
        ZExpr ParseTypeExpr()
        {
            var t = Cur;
            if (t.Kind == ZTk.Op)
            {
                if (t.Text == "?")
                {
                    Advance();
                    var c = ParseTypeExpr();
                    return new ZOptionalType { Child = c, Start = t.Start, End = c.End };
                }
                if (t.Text == "*" || t.Text == "**")
                {
                    Advance();
                    bool cst = false;
                    while (IsKw("const") || IsKw("volatile") || IsKw("allowzero") || IsKw("align"))
                    {
                        if (IsKw("align")) { Advance(); ExpectOp("("); ParseExpr(); ExpectOp(")"); continue; }
                        if (Advance().Text == "const") cst = true;
                    }
                    var c = ParseTypeExpr();
                    var pt = new ZPointerType { Child = c, Const = cst, Start = t.Start, End = c.End };
                    if (t.Text == "**") return new ZPointerType { Child = pt, Start = t.Start, End = c.End };
                    return pt;
                }
                if (t.Text == "[")
                {
                    Advance();
                    if (IsOp("]") || (IsOp(":") ))
                    {
                        bool sentinel = false;
                        if (AcceptOp(":")) { ParseExpr(); sentinel = true; }
                        ExpectOp("]");
                        bool cst = false;
                        while (IsKw("const") || IsKw("volatile") || IsKw("allowzero") || IsKw("align"))
                        {
                            if (IsKw("align")) { Advance(); ExpectOp("("); ParseExpr(); ExpectOp(")"); continue; }
                            if (Advance().Text == "const") cst = true;
                        }
                        var c = ParseTypeExpr();
                        return new ZSliceType { Child = c, Const = cst, Sentinel = sentinel, Start = t.Start, End = c.End };
                    }
                    if (IsOp("*"))
                    {
                        // [*]T many-item pointer / [*c]T
                        Advance();
                        if (Cur.Kind == ZTk.Ident && Cur.Text == "c") Advance();
                        if (AcceptOp(":")) ParseExpr();
                        ExpectOp("]");
                        while (IsKw("const") || IsKw("volatile") || IsKw("allowzero")) Advance();
                        var c = ParseTypeExpr();
                        return new ZPointerType { Child = c, Many = true, Start = t.Start, End = c.End };
                    }
                    var at = new ZArrayType { Start = t.Start };
                    if (Cur.Kind == ZTk.Ident && Cur.Text == "_" && IsOpAt(1, "]")) { Advance(); at.Inferred = true; }
                    else if (Cur.Kind == ZTk.Ident && Cur.Text == "_" && IsOpAt(1, ":")) { Advance(); at.Inferred = true; }
                    else at.Len = ParseExpr();
                    if (AcceptOp(":")) { ParseExpr(); at.Sentinel = true; }
                    ExpectOp("]");
                    at.Child = ParseTypeExpr();
                    at.End = at.Child.End;
                    return at;
                }
            }
            var e = ParseSuffix();
            if (IsOp("!") && !(e is ZControlExpr))
            {
                // E!T
                Advance();
                var payload = ParseTypeExpr();
                return new ZErrorUnionType { Error = e, Payload = payload, Start = e.Start, End = payload.End };
            }
            return e;
        }

        ZExpr ParseSuffix()
        {
            var e = ParsePrimaryType();
            while (true)
            {
                if (IsOp("("))
                {
                    var lp = Cur.Start;
                    var args = ParseArgs();
                    e = new ZCall { Callee = e, Args = args, LParen = lp, Start = e.Start, End = Prev.End };
                    continue;
                }
                if (IsOp("["))
                {
                    Advance();
                    var idx = ParseExpr();
                    if (AcceptOp(".."))
                    {
                        ZExpr to = null;
                        if (!IsOp("]") && !IsOp(":")) to = ParseExpr();
                        if (AcceptOp(":")) ParseExpr();
                        ExpectOp("]");
                        e = new ZSliceExpr { Target = e, From = idx, To = to, Start = e.Start, End = Prev.End };
                        continue;
                    }
                    ExpectOp("]");
                    e = new ZIndex { Target = e, Index = idx, Start = e.Start, End = Prev.End };
                    continue;
                }
                if (IsOp(".*")) { var d = Advance(); e = new ZDeref { Target = e, Start = e.Start, End = d.End }; continue; }
                if (IsOp(".?")) { var d = Advance(); e = new ZUnwrap { Target = e, Start = e.Start, End = d.End }; continue; }
                if (IsOp("."))
                {
                    Advance();
                    if (Cur.Kind == ZTk.Ident)
                    {
                        var n = Advance();
                        e = new ZField { Target = e, Name = n.Value, NameStart = n.Start, NameEnd = n.End, Start = e.Start, End = n.End };
                        continue;
                    }
                    if (Cur.Kind == ZTk.Op && Cur.Text == "*") { var d = Advance(); e = new ZDeref { Target = e, Start = e.Start, End = d.End }; continue; }
                    if (Cur.Kind == ZTk.Op && Cur.Text == "?") { var d = Advance(); e = new ZUnwrap { Target = e, Start = e.Start, End = d.End }; continue; }
                    throw Fail("expected pointer dereference, optional unwrap, or field access, found '" + Symbol(Cur) + "'", Cur);
                }
                break;
            }
            return e;
        }

        List<ZExpr> ParseArgs()
        {
            ExpectOp("(");
            var args = new List<ZExpr>();
            while (!IsOp(")"))
            {
                if (Cur.Kind == ZTk.Eof) throw Fail("expected ')', found 'EOF'", Cur);
                args.Add(ParseExpr());
                if (!AcceptOp(","))
                {
                    if (!IsOp(")")) throw Fail("expected ',' after argument", Cur);
                    break;
                }
            }
            ExpectOp(")");
            return args;
        }

        ZExpr ParseInitList(ZExpr type)
        {
            var open = ExpectOp("{");
            var init = new ZInit { Type = type, Start = type?.Start ?? open.Start, OpenStart = open.Start };
            if (IsOp(".") && Peek().Kind == ZTk.Ident && IsOpAt(2, "="))
            {
                init.Fields = new List<(string, int, int, ZExpr)>();
                while (!IsOp("}"))
                {
                    if (!IsOp(".")) throw Fail("expected field initializer", Cur);
                    Advance();
                    var n = ExpectIdent();
                    ExpectOp("=");
                    var v = ParseExpr();
                    init.Fields.Add((n.Value, n.Start, n.End, v));
                    if (!AcceptOp(","))
                    {
                        if (!IsOp("}")) throw Fail("expected ',' after initializer", Cur);
                        break;
                    }
                }
            }
            else
            {
                while (!IsOp("}"))
                {
                    if (Cur.Kind == ZTk.Eof) throw Fail("expected '}', found 'EOF'", Cur);
                    init.Items.Add(ParseExpr());
                    if (!AcceptOp(","))
                    {
                        if (!IsOp("}")) throw Fail("expected ',' after initializer", Cur);
                        break;
                    }
                }
            }
            init.End = ExpectOp("}").End;
            return init;
        }

        ZExpr ParsePrimaryType()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case ZTk.Ident:
                    Advance();
                    return new ZIdent { Name = t.Value, Quoted = t.Text.StartsWith("@"), Start = t.Start, End = t.End };
                case ZTk.Builtin:
                    {
                        Advance();
                        if (!IsOp("(")) throw Fail("expected '(', found '" + Symbol(Cur) + "'", Cur);
                        var args = ParseArgs();
                        return new ZBuiltinCall { Name = t.Text, NameEnd = t.End, Args = args, Start = t.Start, End = Prev.End };
                    }
                case ZTk.Int: Advance(); return new ZIntLit { Value = t.IntValue, Text = t.Text, Start = t.Start, End = t.End };
                case ZTk.Float: Advance(); return new ZFloatLit { Value = t.FloatValue, Text = t.Text, Start = t.Start, End = t.End };
                case ZTk.String:
                    if (t.Unterminated) throw Fail("expected expression, found 'invalid token'", t);
                    if (t.BadEscape) throw Fail("invalid escape sequence in string literal", t);
                    Advance();
                    return new ZStrLit { Value = t.Value, Start = t.Start, End = t.End };
                case ZTk.MultiString:
                    {
                        var sb = new System.Text.StringBuilder();
                        int end = t.End;
                        bool first = true;
                        while (Cur.Kind == ZTk.MultiString)
                        {
                            if (!first) sb.Append('\n');
                            sb.Append(Cur.Value);
                            end = Cur.End;
                            first = false;
                            Advance();
                        }
                        return new ZStrLit { Value = sb.ToString(), Multi = true, Start = t.Start, End = end };
                    }
                case ZTk.Char:
                    if (t.Unterminated || t.BadEscape) throw Fail("expected expression, found 'invalid token'", t);
                    Advance();
                    return new ZCharLit { Value = t.IntValue, Start = t.Start, End = t.End };
                case ZTk.Invalid:
                    throw Fail("expected expression, found 'invalid token'", t);
                case ZTk.Op:
                    switch (t.Text)
                    {
                        case "(":
                            {
                                Advance();
                                var inner = ParseExpr();
                                var close = ExpectOp(")");
                                return new ZGrouped { Inner = inner, Start = t.Start, End = close.End };
                            }
                        case ".":
                            {
                                Advance();
                                if (Cur.Kind == ZTk.Ident) { var n = Advance(); return new ZEnumLit { Name = n.Value, Start = t.Start, End = n.End }; }
                                if (IsOp("{")) { var init = (ZInit)ParseInitList(null); init.Start = t.Start; return init; }
                                throw Fail("expected expression, found '.'", t);
                            }
                    }
                    break;
                case ZTk.Keyword:
                    switch (t.Text)
                    {
                        case "unreachable": Advance(); return new ZControlExpr { Kw = "unreachable", Start = t.Start, End = t.End };
                        case "switch":
                            {
                                var sw = ParseSwitch();
                                return new ZSwitchExpr { Switch = sw, Start = sw.Start, End = sw.End };
                            }
                        case "fn":
                            {
                                Advance();
                                var ft = new ZFnType { Start = t.Start };
                                if (Cur.Kind == ZTk.Ident) throw Fail("function type cannot have a name", Cur);
                                ft.Params = ParseParams();
                                if (IsKw("callconv")) { Advance(); ExpectOp("("); ParseExpr(); ExpectOp(")"); }
                                AcceptOp("!");
                                ft.Ret = ParseTypeExpr();
                                ft.End = ft.Ret.End;
                                return ft;
                            }
                        case "struct":
                            {
                                Advance();
                                ExpectOp("{");
                                var tt = new ZTupleType { Start = t.Start };
                                bool named = false;
                                while (!IsOp("}"))
                                {
                                    if (Cur.Kind == ZTk.Eof) throw Fail("expected '}', found 'EOF'", Cur);
                                    if (Cur.Kind == ZTk.Ident && IsOpAt(1, ":")) { named = true; Advance(); Advance(); }
                                    else if (IsKw("pub") || IsKw("fn") || IsKw("const") || IsKw("var")) { named = true; SkipBalanced(); continue; }
                                    tt.Fields.Add(ParseTypeExpr());
                                    if (AcceptOp("=")) ParseExpr();
                                    if (!AcceptOp(",")) break;
                                }
                                var close = ExpectOp("}");
                                tt.End = close.End;
                                if (named) return new ZUnsupportedExpr { What = "struct declarations are", Start = t.Start, End = close.End };
                                return tt;
                            }
                        case "enum":
                        case "union":
                        case "opaque":
                            {
                                Advance();
                                if (IsOp("(")) { Advance(); if (!IsOp(")")) ParseExpr(); ExpectOp(")"); }
                                int s0 = t.Start;
                                if (IsOp("{")) SkipBalanced();
                                return new ZUnsupportedExpr { What = t.Text + " declarations are", Start = s0, End = Prev.End };
                            }
                        case "packed":
                        case "extern":
                            {
                                Advance();
                                if (IsKw("struct") || IsKw("union") || IsKw("enum")) { Advance(); if (IsOp("(")) { Advance(); ParseExpr(); ExpectOp(")"); } }
                                if (IsOp("{")) SkipBalanced();
                                return new ZUnsupportedExpr { What = "struct declarations are", Start = t.Start, End = Prev.End };
                            }
                        case "error":
                            {
                                Advance();
                                if (AcceptOp(".")) { var n = ExpectIdent(); return new ZUnsupportedExpr { What = "errors are", Start = t.Start, End = n.End }; }
                                if (IsOp("{")) SkipBalanced();
                                return new ZUnsupportedExpr { What = "error sets are", Start = t.Start, End = Prev.End };
                            }
                        case "anyframe":
                            Advance();
                            return new ZUnsupportedExpr { What = "async functions are", Start = t.Start, End = t.End };
                        case "asm":
                            {
                                Advance();
                                if (IsKw("volatile")) Advance();
                                if (IsOp("(")) SkipBalanced();
                                return new ZUnsupportedExpr { What = "inline assembly is", Start = t.Start, End = Prev.End };
                            }
                    }
                    break;
            }
            throw Fail("expected expression, found '" + Symbol(t) + "'", t);
        }

        /// <summary>Skips a balanced (...) / {...} / [...] group or a declaration up to its ';'.</summary>
        void SkipBalanced()
        {
            if (IsOp("{") || IsOp("(") || IsOp("["))
            {
                int depth = 0;
                do
                {
                    if (IsOp("{") || IsOp("(") || IsOp("[")) depth++;
                    else if (IsOp("}") || IsOp(")") || IsOp("]")) depth--;
                    if (Cur.Kind == ZTk.Eof) throw Fail("expected '}', found 'EOF'", Cur);
                    Advance();
                } while (depth > 0);
                return;
            }
            int d = 0;
            while (Cur.Kind != ZTk.Eof)
            {
                if (IsOp("{") || IsOp("(") || IsOp("[")) d++;
                else if (IsOp("}") || IsOp(")") || IsOp("]")) { if (d == 0) return; d--; if (d == 0 && IsOp("}")) { Advance(); if (AcceptOp(";")) { } return; } }
                else if (IsOp(";") && d == 0) { Advance(); return; }
                Advance();
            }
        }
    }
}
