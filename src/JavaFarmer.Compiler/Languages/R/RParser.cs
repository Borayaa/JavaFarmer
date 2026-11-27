using System;
using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// R 4.x parser (the subset JavaFarmer lowers, but it accepts all common R syntax so unsupported constructs get a
    /// clear "not supported" message from the lowering). Newlines end an expression at the top level and inside braces
    /// but not inside ( ) and [ ]. Errors imitate R: Error: unexpected symbol in "x y".
    /// </summary>
    internal sealed class RlParser
    {
        readonly string src;
        readonly List<RlToken> t;
        int p;
        // 0 = top level, 1 = braces, 2 = parentheses / brackets
        readonly List<int> ctx = new List<int> { 0 };
        readonly List<RlToken> open = new List<RlToken>();

        RlParser(string src, List<RlToken> toks) { this.src = src; t = toks; }

        public static RlProgram Parse(string src)
        {
            src = src ?? "";
            var prog = new RlProgram { Source = src };
            var toks = RlLexer.Lex(src, prog.Comments);
            var ps = new RlParser(src, toks);
            prog.Exprs = ps.Statements(true);
            return prog;
        }

        RlToken Peek => t[p];
        RlToken At(int k) => t[Math.Min(p + k, t.Count - 1)];
        RlToken Advance() { var x = t[p]; if (p < t.Count - 1) p++; return x; }
        bool NlSig => ctx[ctx.Count - 1] != 2;
        void Push(int c) => ctx.Add(c);
        void Pop() => ctx.RemoveAt(ctx.Count - 1);

        // ------------------------------------------------------------------ errors
        string Context(RlToken tok)
        {
            int ls = tok.Start;
            while (ls > 0 && src[ls - 1] != '\n') ls--;
            var text = src.Substring(ls, Math.Max(0, tok.End - ls)).Trim();
            return text;
        }

        static string Describe(RlToken tok)
        {
            switch (tok.Kind)
            {
                case RlTokKind.Eof: return "end of input";
                case RlTokKind.Ident: return "symbol";
                case RlTokKind.Num: return "numeric constant";
                case RlTokKind.Str: return "string constant";
                case RlTokKind.Bad: return "input";
                case RlTokKind.Kw:
                    switch (tok.Text)
                    {
                        case "TRUE": case "FALSE": case "NA": case "Inf": case "NaN": case "NA_integer_": case "NA_real_": case "NA_character_": return "numeric constant";
                        case "NULL": return "NULL_CONST";
                    }
                    return "'" + tok.Text + "'";
                case RlTokKind.Op:
                    if (tok.Text == "<-" || tok.Text == "<<-" || tok.Text == "->" || tok.Text == "->>") return "assignment";
                    if (tok.Text.Length > 1 && tok.Text[0] == '%') return "SPECIAL";
                    return "'" + tok.Text + "'";
            }
            return "'" + tok.Text + "'";
        }

        Exception Unexpected(RlToken tok)
        {
            if (tok.Kind == RlTokKind.Eof)
            {
                if (open.Count > 0)
                {
                    var o = open[open.Count - 1];
                    return new RlSyntaxError("Error: unexpected end of input: the '" + o.Text + "' here is never closed", o.Span);
                }
                return new RlSyntaxError("Error: unexpected end of input", new Span(Math.Max(0, src.Length - 1), src.Length));
            }
            return new RlSyntaxError("Error: unexpected " + Describe(tok) + " in \"" + Context(tok) + "\"", tok.Span);
        }

        RlToken Expect(string text)
        {
            if (!Peek.Is(text)) throw Unexpected(Peek);
            return Advance();
        }

        // ------------------------------------------------------------------ statements
        List<RlExpr> Statements(bool top)
        {
            var list = new List<RlExpr>();
            while (true)
            {
                var tok = Peek;
                if (tok.Kind == RlTokKind.Eof)
                {
                    if (!top) throw Unexpected(tok);
                    break;
                }
                if (tok.Is("}"))
                {
                    if (top) throw Unexpected(tok);
                    break;
                }
                var e = Expr(0, true);
                list.Add(e);
                if (Peek.Is(";")) { Advance(); continue; }
                if (Peek.Kind == RlTokKind.Eof || (!top && Peek.Is("}")) || Peek.NlBefore) continue;
                throw Unexpected(Peek);
            }
            return list;
        }

        // ------------------------------------------------------------------ expressions
        const int PEq = 2, PAssign = 3, PRight = 4, PTilde = 5, POr = 6, PAnd = 7, PNot = 8, PCmp = 9, PAdd = 10, PMul = 11,
                  PSpecial = 12, PColon = 13, PUnary = 14, PPow = 15;

        static bool BinaryInfo(RlToken tok, out int prec, out bool right)
        {
            prec = 0; right = false;
            if (tok.Kind != RlTokKind.Op) return false;
            switch (tok.Text)
            {
                case "?": prec = 1; return true;
                case "=": prec = PEq; right = true; return true;
                case "<-": case "<<-": prec = PAssign; right = true; return true;
                case "->": case "->>": prec = PRight; return true;
                case "~": prec = PTilde; return true;
                case "||": case "|": prec = POr; return true;
                case "&&": case "&": prec = PAnd; return true;
                case "==": case "!=": case "<": case ">": case "<=": case ">=": prec = PCmp; return true;
                case "+": case "-": prec = PAdd; return true;
                case "*": case "/": prec = PMul; return true;
                case "|>": prec = PSpecial; return true;
                case ":": prec = PColon; return true;
                case "^": prec = PPow; right = true; return true;
            }
            if (tok.Text.Length > 1 && tok.Text[0] == '%') { prec = PSpecial; return true; }
            return false;
        }

        static bool IsCmp(RlToken tok) => tok.Kind == RlTokKind.Op && (tok.Text == "==" || tok.Text == "!=" || tok.Text == "<" || tok.Text == ">" || tok.Text == "<=" || tok.Text == ">=");

        RlExpr Expr(int minPrec, bool allowEq)
        {
            var left = Unary(allowEq);
            while (true)
            {
                var op = Peek;
                if (op.NlBefore && NlSig) break;
                if (!BinaryInfo(op, out int prec, out bool right)) break;
                if (prec < minPrec) break;
                if (op.Text == "=" && !allowEq) break;
                Advance();
                var rhs = Expr(right ? prec : prec + 1, allowEq);
                var span = Span.Cover(Outer(left), Outer(rhs));
                switch (op.Text)
                {
                    case "<-": case "<<-": case "=":
                        left = new RlAssign { Op = op.Text, Target = left, Value = rhs, OpSpan = op.Span, Span = span };
                        break;
                    case "->": case "->>":
                        left = new RlAssign { Op = op.Text, Target = rhs, Value = left, OpSpan = op.Span, Span = span };
                        break;
                    default:
                        left = new RlBinary { Op = op.Text, Left = left, Right = rhs, OpSpan = op.Span, Span = span };
                        if (prec == PCmp && IsCmp(Peek) && !(Peek.NlBefore && NlSig)) throw Unexpected(Peek);
                        break;
                }
            }
            return left;
        }

        static Span Outer(RlExpr e) => e.ParenSpan.IsNone ? e.Span : e.ParenSpan;

        RlExpr Unary(bool allowEq)
        {
            var tok = Peek;
            if (tok.Kind == RlTokKind.Op)
            {
                switch (tok.Text)
                {
                    case "-": case "+":
                        {
                            Advance();
                            var operand = Expr(PPow, allowEq);
                            return new RlUnary { Op = tok.Text, Operand = operand, OpSpan = tok.Span, Span = Span.Cover(tok.Span, Outer(operand)) };
                        }
                    case "!":
                        {
                            Advance();
                            var operand = Expr(PCmp, allowEq);
                            return new RlUnary { Op = "!", Operand = operand, OpSpan = tok.Span, Span = Span.Cover(tok.Span, Outer(operand)) };
                        }
                    case "~": case "?":
                        {
                            Advance();
                            var operand = Expr(POr, allowEq);
                            return new RlUnary { Op = tok.Text, Operand = operand, OpSpan = tok.Span, Span = Span.Cover(tok.Span, Outer(operand)) };
                        }
                }
            }
            return Postfix(Primary(allowEq));
        }

        RlExpr Postfix(RlExpr e)
        {
            while (true)
            {
                var tok = Peek;
                if (tok.NlBefore && NlSig) break;
                if (tok.Is("("))
                {
                    var call = new RlCall { Fn = e, OpenSpan = tok.Span };
                    Advance();
                    open.Add(tok);
                    Push(2);
                    Args(call.Args, ")");
                    Pop();
                    open.RemoveAt(open.Count - 1);
                    var close = Expect(")");
                    call.CloseSpan = close.Span;
                    call.Span = Span.Cover(Outer(e), close.Span);
                    e = call;
                    continue;
                }
                if (tok.Is("["))
                {
                    bool dbl = At(1).Is("[") && At(1).Start == tok.End;
                    var ix = new RlIndex { Target = e, Double = dbl, OpenSpan = dbl ? new Span(tok.Start, At(1).End) : tok.Span };
                    Advance();
                    if (dbl) Advance();
                    open.Add(tok);
                    Push(2);
                    Args(ix.Args, "]");
                    Pop();
                    open.RemoveAt(open.Count - 1);
                    var close = Expect("]");
                    if (dbl)
                    {
                        if (!(Peek.Is("]") && Peek.Start == close.End)) throw Unexpected(Peek);
                        close = Advance();
                    }
                    ix.Span = Span.Cover(Outer(e), close.Span);
                    e = ix;
                    continue;
                }
                if (tok.Is("$") || tok.Is("@"))
                {
                    Advance();
                    var nt = Peek;
                    if (nt.Kind != RlTokKind.Ident && nt.Kind != RlTokKind.Str) throw Unexpected(nt);
                    Advance();
                    e = new RlDollar { Target = e, Name = nt.Kind == RlTokKind.Str ? nt.StrValue : nt.Text, NameSpan = nt.Span, OpSpan = tok.Span, At = tok.Text == "@", Span = Span.Cover(Outer(e), nt.Span) };
                    continue;
                }
                break;
            }
            return e;
        }

        void Args(List<RlArg> args, string close)
        {
            if (Peek.Is(close)) return;
            while (true)
            {
                var arg = new RlArg();
                var tok = Peek;
                if (tok.Is(",") || tok.Is(close))
                {
                    arg.Span = new Span(tok.Start, tok.Start);
                }
                else if ((tok.Kind == RlTokKind.Ident || tok.Kind == RlTokKind.Str || (tok.Kind == RlTokKind.Kw && tok.Text == "NULL")) && At(1).Kind == RlTokKind.Op && At(1).Text == "=")
                {
                    Advance(); Advance();
                    arg.Name = tok.Kind == RlTokKind.Str ? tok.StrValue : tok.Text;
                    arg.NameSpan = tok.Span;
                    if (!(Peek.Is(",") || Peek.Is(close))) arg.Value = Expr(0, false);
                    arg.Span = arg.Value != null ? Span.Cover(tok.Span, Outer(arg.Value)) : tok.Span;
                }
                else
                {
                    arg.Value = Expr(0, false);
                    arg.Span = Outer(arg.Value);
                }
                args.Add(arg);
                if (Peek.Is(","))
                {
                    Advance();
                    if (Peek.Is(close)) { args.Add(new RlArg { Span = new Span(Peek.Start, Peek.Start) }); return; }
                    continue;
                }
                if (Peek.Is(close)) return;
                throw Unexpected(Peek);
            }
        }

        RlExpr Body(bool allowEq) => Expr(0, allowEq);

        RlExpr Primary(bool allowEq)
        {
            var tok = Peek;
            switch (tok.Kind)
            {
                case RlTokKind.Num:
                    Advance();
                    if (tok.Complex) throw new RlSyntaxError("complex numbers are not supported in JavaFarmer", tok.Span);
                    return new RlNum { Value = tok.Num, LooksInt = tok.LooksInt, HasL = tok.HasL, Text = tok.Text, Span = tok.Span };
                case RlTokKind.Str:
                    Advance();
                    return new RlStr { Value = tok.StrValue, Raw = tok.Raw, Span = tok.Span };
                case RlTokKind.Ident:
                    Advance();
                    if ((Peek.Is("::") || Peek.Is(":::")) && !(Peek.NlBefore && NlSig))
                    {
                        var op = Advance();
                        var nt = Peek;
                        if (nt.Kind != RlTokKind.Ident && nt.Kind != RlTokKind.Str) throw Unexpected(nt);
                        Advance();
                        return new RlNamespace { Pkg = tok.Text, Name = nt.Kind == RlTokKind.Str ? nt.StrValue : nt.Text, OpSpan = op.Span, Span = Span.Cover(tok.Span, nt.Span) };
                    }
                    return new RlIdent { Name = tok.Text, Backtick = tok.Backtick, Span = tok.Span };
                case RlTokKind.Kw:
                    switch (tok.Text)
                    {
                        case "TRUE": Advance(); return new RlConst { Kind = RlConstKind.True, Text = tok.Text, Span = tok.Span };
                        case "FALSE": Advance(); return new RlConst { Kind = RlConstKind.False, Text = tok.Text, Span = tok.Span };
                        case "NULL": Advance(); return new RlConst { Kind = RlConstKind.Null, Text = tok.Text, Span = tok.Span };
                        case "Inf": Advance(); return new RlConst { Kind = RlConstKind.Inf, Text = tok.Text, Span = tok.Span };
                        case "NaN": Advance(); return new RlConst { Kind = RlConstKind.NaN, Text = tok.Text, Span = tok.Span };
                        case "NA": case "NA_integer_": case "NA_real_": case "NA_character_":
                            Advance(); return new RlConst { Kind = RlConstKind.NA, Text = tok.Text, Span = tok.Span };
                        case "break": Advance(); return new RlBreak { Span = tok.Span };
                        case "next": Advance(); return new RlNext { Span = tok.Span };
                        case "if": return If(allowEq);
                        case "for": return For(allowEq);
                        case "while":
                            {
                                Advance();
                                var cond = ParenExpr();
                                var body = Body(allowEq);
                                return new RlWhile { Cond = cond, Body = body, KwSpan = tok.Span, Span = Span.Cover(tok.Span, Outer(body)) };
                            }
                        case "repeat":
                            {
                                Advance();
                                var body = Body(allowEq);
                                return new RlRepeat { Body = body, KwSpan = tok.Span, Span = Span.Cover(tok.Span, Outer(body)) };
                            }
                        case "function": return Function(allowEq, false);
                    }
                    throw Unexpected(tok);
                case RlTokKind.Op:
                    if (tok.Text == "\\") return Function(allowEq, true);
                    throw Unexpected(tok);
                case RlTokKind.Punct:
                    if (tok.Text == "(")
                    {
                        Advance();
                        open.Add(tok);
                        Push(2);
                        var inner = Expr(0, true);
                        Pop();
                        open.RemoveAt(open.Count - 1);
                        var close = Expect(")");
                        var span = Span.Cover(tok.Span, close.Span);
                        return new RlParen { Inner = inner, Span = span };
                    }
                    if (tok.Text == "{")
                    {
                        Advance();
                        open.Add(tok);
                        Push(1);
                        var stmts = Statements(false);
                        Pop();
                        open.RemoveAt(open.Count - 1);
                        var close = Expect("}");
                        var b = new RlBlock { OpenSpan = tok.Span, CloseSpan = close.Span, Span = Span.Cover(tok.Span, close.Span) };
                        b.Exprs.AddRange(stmts);
                        return b;
                    }
                    throw Unexpected(tok);
            }
            throw Unexpected(tok);
        }

        RlExpr ParenExpr()
        {
            var o = Expect("(");
            open.Add(o);
            Push(2);
            var e = Expr(0, false);
            Pop();
            open.RemoveAt(open.Count - 1);
            Expect(")");
            return e;
        }

        RlExpr If(bool allowEq)
        {
            var kw = Advance();
            var cond = ParenExpr();
            var then = Body(allowEq);
            var r = new RlIf { Cond = cond, Then = then, KwSpan = kw.Span, Span = Span.Cover(kw.Span, Outer(then)) };
            if (Peek.Is("else") && (!Peek.NlBefore || ctx[ctx.Count - 1] != 0))
            {
                var ek = Advance();
                r.ElseSpan = ek.Span;
                r.Else = Body(allowEq);
                r.Span = Span.Cover(kw.Span, Outer(r.Else));
            }
            return r;
        }

        RlExpr For(bool allowEq)
        {
            var kw = Advance();
            var o = Expect("(");
            open.Add(o);
            Push(2);
            var v = Peek;
            if (v.Kind != RlTokKind.Ident) throw Unexpected(v);
            Advance();
            Expect("in");
            var seq = Expr(0, false);
            Pop();
            open.RemoveAt(open.Count - 1);
            var close = Expect(")");
            var body = Body(allowEq);
            return new RlFor { Var = v.Text, VarSpan = v.Span, Seq = seq, Body = body, KwSpan = kw.Span, HeaderSpan = Span.Cover(kw.Span, close.Span), Span = Span.Cover(kw.Span, Outer(body)) };
        }

        RlExpr Function(bool allowEq, bool backslash)
        {
            var kw = Advance();
            var f = new RlFunction { Backslash = backslash };
            var o = Expect("(");
            open.Add(o);
            Push(2);
            if (!Peek.Is(")"))
            {
                while (true)
                {
                    var nt = Peek;
                    if (nt.Kind != RlTokKind.Ident) throw Unexpected(nt);
                    Advance();
                    var prm = new RlParam { Name = nt.Text, Span = nt.Span, Dots = nt.Text == "..." };
                    if (Peek.Kind == RlTokKind.Op && Peek.Text == "=")
                    {
                        Advance();
                        prm.Default = Expr(0, false);
                    }
                    f.Params.Add(prm);
                    if (Peek.Is(",")) { Advance(); continue; }
                    break;
                }
            }
            Pop();
            open.RemoveAt(open.Count - 1);
            var close = Expect(")");
            f.HeaderSpan = Span.Cover(kw.Span, close.Span);
            f.Body = Body(allowEq);
            f.Span = Span.Cover(kw.Span, Outer(f.Body));
            return f;
        }
    }
}
