using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>A diagnostic that stops the Rust frontend (first syntax error).</summary>
    internal sealed class RustAbort : Exception
    {
        public readonly Diagnostic Diag;
        public RustAbort(string msg, int start, int end) : base(msg) { Diag = new Diagnostic(msg, start, end); }
    }

    /// <summary>Recursive-descent parser for the JavaFarmer Rust subset (edition 2024 syntax). Stops at the first error.</summary>
    internal sealed class RustParser
    {
        readonly List<RToken> toks = new List<RToken>();
        readonly string src;
        int p;
        public readonly List<RToken> Comments = new List<RToken>();

        RustParser(string source)
        {
            src = source ?? "";
            foreach (var t in new RustLexer(src).LexAll())
            {
                if (t.Kind == RTk.Whitespace) continue;
                if (t.Kind == RTk.Comment) { Comments.Add(t); if (t.Unterminated) throw new RustAbort("unterminated block comment", t.Start, Math.Min(t.Start + 2, src.Length)); continue; }
                toks.Add(t);
            }
        }

        public static RFile Parse(string source)
        {
            var ps = new RustParser(source);
            var f = ps.File();
            f.Comments = ps.Comments;
            return f;
        }

        // ------------------------------------------------------------------ token helpers
        RToken Cur => toks[Math.Min(p, toks.Count - 1)];
        RToken Peek(int k = 1) => toks[Math.Min(p + k, toks.Count - 1)];
        RToken Prev => toks[Math.Max(0, p - 1)];
        bool AtEof => Cur.Kind == RTk.EOF;
        bool Is(string punct) => Cur.Kind == RTk.Punct && Cur.Text == punct;
        bool IsAt(int k, string punct) { var t = Peek(k); return t.Kind == RTk.Punct && t.Text == punct; }
        bool IsKw(string kw) => Cur.Kind == RTk.Keyword && Cur.Text == kw;
        bool IsKwAt(int k, string kw) { var t = Peek(k); return t.Kind == RTk.Keyword && t.Text == kw; }
        bool IsIdent => Cur.Kind == RTk.Ident;
        RToken Advance() { var t = Cur; if (p < toks.Count - 1) p++; return t; }
        bool Accept(string punct) { if (Is(punct)) { Advance(); return true; } return false; }
        bool AcceptKw(string kw) { if (IsKw(kw)) { Advance(); return true; } return false; }

        static string Describe(RToken t)
        {
            switch (t.Kind)
            {
                case RTk.EOF: return "end of file";
                case RTk.Str: return "`\"" + Trunc(t.Text) + "\"`";
                case RTk.Keyword: return "keyword `" + t.Text + "`";
                default: return "`" + Trunc(t.Raw && t.Kind == RTk.Ident ? "r#" + t.Text : t.Text) + "`";
            }
        }
        static string Trunc(string s) => s == null ? "" : s.Length > 20 ? s.Substring(0, 20) + "..." : s;

        RustAbort Err(string msg, RToken at) => new RustAbort(msg, at.Start, Math.Max(at.End, at.Start + (at.Kind == RTk.EOF ? 0 : 1)));
        RustAbort Err(string msg, int s, int e) => new RustAbort(msg, s, e);
        RustAbort Unsupported(string what, int s, int e) => new RustAbort(what + " is not supported in JavaFarmer", s, e);

        RustAbort Expected(string what)
        {
            var t = Cur;
            if (t.Kind == RTk.EOF) return new RustAbort("expected " + what + ", found end of file", Math.Max(0, Prev.End - 1), Prev.End);
            return Err("expected " + what + ", found " + Describe(t), t);
        }

        RToken Expect(string punct)
        {
            if (Is(punct)) return Advance();
            // ">>" closing two generic lists
            if (punct == ">" && Cur.Kind == RTk.Punct && Cur.Text.StartsWith(">") && Cur.Text.Length > 1)
            {
                var t = Cur;
                var first = new RToken { Kind = RTk.Punct, Start = t.Start, End = t.Start + 1, Text = ">" };
                t.Start++; t.Text = t.Text.Substring(1);
                return first;
            }
            throw Expected("`" + punct + "`");
        }

        string ExpectIdent(string what = "identifier")
        {
            if (Cur.Kind == RTk.Ident) return Advance().Text;
            if (Cur.Kind == RTk.Keyword)
                throw Err("expected " + what + ", found keyword `" + Cur.Text + "`" + (Cur.Text == "move" ? " (the game's move() is called move_to() or r#move() in Rust)" : ""), Cur);
            throw Expected(what);
        }

        void CheckTok(RToken t)
        {
            if (t.Error != null) throw Err(t.Error, t.Start, t.End);
        }

        // ------------------------------------------------------------------ file / items
        RFile File()
        {
            var f = new RFile { Start = 0, End = src.Length };
            while (!AtEof)
            {
                if (Is("#") && IsAt(1, "!")) { SkipAttribute(); continue; }
                f.Items.Add(Item(topLevel: true));
            }
            return f;
        }

        void SkipAttribute()
        {
            Expect("#");
            Accept("!");
            if (!Is("[")) throw Expected("`[`");
            int depth = 0;
            do
            {
                if (Is("[")) depth++;
                else if (Is("]")) depth--;
                if (AtEof) throw Err("this file contains an unclosed delimiter", Prev);
                Advance();
            } while (depth > 0);
        }

        RItem Item(bool topLevel)
        {
            while (Is("#")) SkipAttribute();
            int start = Cur.Start;
            bool pub = false;
            int pubStart = -1;
            if (IsKw("pub"))
            {
                pubStart = Cur.Start;
                Advance();
                pub = true;
                if (Is("(")) { Advance(); while (!Is(")") && !AtEof) Advance(); Expect(")"); }
            }
            RItem item;
            if (IsKw("fn") || ((IsKw("const") || IsKw("unsafe") || IsKw("async") || IsKw("extern")) && (IsKwAt(1, "fn") || IsKwAt(2, "fn"))))
                item = Fn(start);
            else if (IsKw("const") || IsKw("static")) item = ConstItem(start);
            else if (IsKw("use")) item = Use(start);
            else if (IsKw("mod")) item = Mod(start);
            else if (IsKw("struct")) throw Unsupported("defining a struct", Cur.Start, Cur.End);
            else if (IsKw("enum")) throw Unsupported("defining an enum", Cur.Start, Cur.End);
            else if (IsKw("impl")) throw Unsupported("an `impl` block", Cur.Start, Cur.End);
            else if (IsKw("trait")) throw Unsupported("defining a trait", Cur.Start, Cur.End);
            else if (IsKw("type")) throw Unsupported("a type alias", Cur.Start, Cur.End);
            else if (IsKw("extern")) throw Unsupported("`extern`", Cur.Start, Cur.End);
            else if (IsIdent && Cur.Text == "macro_rules" && IsAt(1, "!")) throw Unsupported("defining a macro", Cur.Start, Peek(1).End);
            else if (IsIdent && Cur.Text == "union") throw Unsupported("a union", Cur.Start, Cur.End);
            else if (topLevel)
            {
                if (IsKw("let")) throw Err("expected item, found keyword `let` (statements must be inside a function, e.g. fn main() { ... })", Cur);
                throw Err("expected item, found " + Describe(Cur), Cur);
            }
            else throw Expected("item");
            item.Pub = pub;
            item.PubStart = pubStart;
            item.Start = start;
            return item;
        }

        RFn Fn(int start)
        {
            while (!IsKw("fn"))
            {
                var m = Advance();
                if (m.Text == "async") throw Unsupported("`async fn`", m.Start, m.End);
                if (m.Text == "unsafe") throw Unsupported("`unsafe fn`", m.Start, m.End);
                if (m.Text == "extern") throw Unsupported("`extern fn`", m.Start, m.End);
                if (m.Text == "const") throw Unsupported("`const fn`", m.Start, m.End);
            }
            Advance();
            var f = new RFn();
            var nameTok = Cur;
            f.Name = ExpectIdent("identifier");
            f.NameStart = nameTok.Start; f.NameEnd = nameTok.End;
            if (Is("<")) throw Unsupported("a generic function", Cur.Start, Cur.End);
            Expect("(");
            while (!Is(")"))
            {
                while (Is("#")) SkipAttribute();
                if (IsKw("self") || (Is("&") && (IsKwAt(1, "self") || (IsKwAt(1, "mut") && IsKwAt(2, "self")))))
                    throw Unsupported("a method with `self`", Cur.Start, Cur.End);
                var pr = new RParam { Start = Cur.Start };
                pr.Pat = Pattern(allowOr: false);
                if (!Is(":")) throw Expected("`:` (parameters need a type, e.g. `n: i32`)");
                Advance();
                pr.Type = Type();
                pr.End = Prev.End;
                f.Params.Add(pr);
                if (!Accept(",")) break;
            }
            Expect(")");
            f.HeaderEnd = Prev.End;
            if (Accept("->"))
            {
                f.Ret = Type();
                f.HeaderEnd = Prev.End;
            }
            if (IsKw("where")) throw Unsupported("a `where` clause", Cur.Start, Cur.End);
            if (Is(";")) throw Err("free function without a body", start, Cur.End);
            if (!Is("{")) throw Expected("`{`");
            f.Body = Block();
            f.End = f.Body.End;
            return f;
        }

        RConst ConstItem(int start)
        {
            var c = new RConst();
            c.IsStatic = IsKw("static");
            Advance();
            if (c.IsStatic && AcceptKw("mut")) c.Mut = true;
            var nt = Cur;
            if (Is("_") ) throw Unsupported("an unnamed constant", nt.Start, nt.End);
            c.Name = ExpectIdent();
            c.NameStart = nt.Start; c.NameEnd = nt.End;
            if (!Is(":"))
                throw Err("missing type for `" + (c.IsStatic ? "static" : "const") + "` item (write e.g. `" + (c.IsStatic ? "static" : "const") + " " + c.Name + ": i32 = 0;`)", nt.Start, nt.End);
            Advance();
            c.Type = Type();
            if (!Is("=")) throw Expected("`=`");
            Advance();
            c.Init = Expr();
            Expect(";");
            c.End = Prev.End;
            return c;
        }

        RUse Use(int start)
        {
            Advance();
            var u = new RUse();
            Accept("::");
            while (true)
            {
                if (Is("*")) { Advance(); u.Glob = true; break; }
                if (Is("{"))
                {
                    Advance();
                    u.Group = new List<(string, int, int)>();
                    while (!Is("}"))
                    {
                        var t = Cur;
                        string n;
                        if (IsKw("self")) { Advance(); n = "self"; }
                        else if (Is("*")) { Advance(); n = "*"; }
                        else n = ExpectIdent();
                        if (Is("::")) throw Unsupported("a nested `use` path", Cur.Start, Cur.End);
                        if (IsKw("as")) throw Unsupported("`use ... as`", Cur.Start, Cur.End);
                        u.Group.Add((n, t.Start, t.End));
                        if (!Accept(",")) break;
                    }
                    Expect("}");
                    break;
                }
                var seg = Cur;
                if (seg.Kind == RTk.Keyword && (seg.Text == "crate" || seg.Text == "self" || seg.Text == "super")) { Advance(); u.Path.Add(seg.Text); }
                else u.Path.Add(ExpectIdent());
                if (IsKw("as")) throw Unsupported("`use ... as`", Cur.Start, Cur.End);
                if (!Accept("::")) break;
            }
            u.PathEnd = Prev.End;
            Expect(";");
            u.End = Prev.End;
            return u;
        }

        RMod Mod(int start)
        {
            Advance();
            var nt = Cur;
            var m = new RMod { Name = ExpectIdent(), NameStart = nt.Start, NameEnd = nt.End };
            if (Is("{")) throw Unsupported("an inline `mod` block (put the code in another code window and write `mod " + m.Name + ";`)", start, Cur.End);
            Expect(";");
            m.End = Prev.End;
            return m;
        }

        // ------------------------------------------------------------------ types
        RTypeRef Type()
        {
            var t = new RTypeRef { Start = Cur.Start };
            if (Is("&") || Is("&&"))
            {
                bool dbl = Is("&&");
                Advance();
                if (Cur.Kind == RTk.Lifetime && Cur.Text == "'static") Advance();   // &'static str: the only lifetime JavaFarmer needs
                else if (Cur.Kind == RTk.Lifetime) throw Unsupported("a lifetime", Cur.Start, Cur.End);
                t.Kind = RTypeKind.Ref;
                t.Mut = AcceptKw("mut");
                t.Elem = Type();
                if (dbl) t = new RTypeRef { Kind = RTypeKind.Ref, Elem = t, Start = t.Start };
            }
            else if (Is("("))
            {
                Advance();
                t.Kind = RTypeKind.Tuple;
                t.Args = new List<RTypeRef>();
                while (!Is(")")) { t.Args.Add(Type()); if (!Accept(",")) break; }
                Expect(")");
                if (t.Args.Count == 1 && src[Prev.Start - 1] != ',') t = t.Args[0];
            }
            else if (Is("["))
            {
                Advance();
                var elem = Type();
                if (Accept(";")) { t.Kind = RTypeKind.Array; t.Len = Expr(); }
                else t.Kind = RTypeKind.Slice;
                t.Elem = elem;
                Expect("]");
            }
            else if (Is("!")) { Advance(); t.Kind = RTypeKind.Never; }
            else if (Cur.Kind == RTk.Ident && Cur.Text == "_" ) { Advance(); t.Kind = RTypeKind.Infer; }
            else if (Is("_")) { Advance(); t.Kind = RTypeKind.Infer; }
            else if (IsKw("impl"))
            {
                Advance();
                t.Kind = RTypeKind.ImplTrait;
                t.Elem = Type();
            }
            else if (IsKw("dyn")) throw Unsupported("`dyn` trait objects", Cur.Start, Cur.End);
            else if (IsKw("fn"))
            {
                Advance();
                t.Kind = RTypeKind.Fn;
                t.Args = new List<RTypeRef>();
                Expect("(");
                while (!Is(")")) { t.Args.Add(Type()); if (!Accept(",")) break; }
                Expect(")");
                if (Accept("->")) t.Elem = Type();
            }
            else if (IsIdent || IsKw("crate") || IsKw("Self") || IsKw("self") || IsKw("super"))
            {
                t.Kind = RTypeKind.Path;
                t.Path = new List<string>();
                while (true)
                {
                    if (IsKw("Self")) throw Unsupported("`Self`", Cur.Start, Cur.End);
                    var seg = Advance();
                    t.Path.Add(seg.Text);
                    if (Is("::") && !IsAt(1, "<")) { Advance(); continue; }
                    if (Is("::")) Advance();
                    break;
                }
                if (Is("<"))
                {
                    Advance();
                    t.Args = new List<RTypeRef>();
                    while (!Is(">") && !(Cur.Kind == RTk.Punct && Cur.Text.StartsWith(">")))
                    {
                        if (Cur.Kind == RTk.Lifetime) throw Unsupported("a lifetime", Cur.Start, Cur.End);
                        t.Args.Add(Type());
                        if (!Accept(",")) break;
                    }
                    Expect(">");
                }
                // Fn() -> T trait sugar: Fn(i32) -> i32
                if (Is("(") && (t.Name == "Fn" || t.Name == "FnMut" || t.Name == "FnOnce"))
                {
                    Advance();
                    t.Args = new List<RTypeRef>();
                    while (!Is(")")) { t.Args.Add(Type()); if (!Accept(",")) break; }
                    Expect(")");
                    if (Accept("->")) t.Elem = Type();
                }
                while (Is("+")) { Advance(); if (Cur.Kind == RTk.Lifetime) Advance(); else Type(); }
            }
            else throw Expected("type");
            t.End = Prev.End;
            return t;
        }

        // ------------------------------------------------------------------ patterns
        RPat Pattern(bool allowOr = true)
        {
            int start = Cur.Start;
            Accept("|");
            var first = PatternAlt();
            if (!allowOr || !Is("|")) return first;
            var or = new RPat { Kind = RPatKind.Or, Items = new List<RPat> { first }, Start = start };
            while (Accept("|")) or.Items.Add(PatternAlt());
            or.End = Prev.End;
            return or;
        }

        RPat PatternAlt()
        {
            var pt = new RPat { Start = Cur.Start };
            if (Is("_") || (IsIdent && Cur.Text == "_")) { Advance(); pt.Kind = RPatKind.Wild; }
            else if (Is("..")) { Advance(); pt.Kind = RPatKind.Rest; }
            else if (Is("&") || Is("&&"))
            {
                Advance();
                AcceptKw("mut");
                pt.Kind = RPatKind.Ref;
                pt.Inner = PatternAlt();
            }
            else if (Is("("))
            {
                Advance();
                pt.Kind = RPatKind.Tuple;
                pt.Items = new List<RPat>();
                while (!Is(")")) { pt.Items.Add(Pattern()); if (!Accept(",")) break; }
                Expect(")");
                if (pt.Items.Count == 1 && src[Prev.Start - 1] != ',') { var inner = pt.Items[0]; return inner; }
            }
            else if (Is("[")) throw Unsupported("a slice pattern", Cur.Start, Cur.End);
            else if (Cur.Kind == RTk.Int || Cur.Kind == RTk.Float || Cur.Kind == RTk.Str || Cur.Kind == RTk.Char || IsKw("true") || IsKw("false")
                     || (Is("-") && (Peek().Kind == RTk.Int || Peek().Kind == RTk.Float)))
            {
                pt.Kind = RPatKind.Lit;
                pt.Lit = LiteralPatternExpr();
                if (Is("..=") || Is("..") || Is("..."))
                {
                    var op = Advance();
                    pt.Kind = RPatKind.Range;
                    if (Cur.Kind == RTk.Int || Cur.Kind == RTk.Float || Is("-")) pt.LitEnd = LiteralPatternExpr();
                    pt.End = Prev.End;
                    throw Unsupported("a range pattern", pt.Start, pt.End);
                }
            }
            else if (IsKw("ref") || IsKw("mut") || IsIdent || IsKw("crate") || IsKw("self"))
            {
                bool byRef = AcceptKw("ref");
                bool mut = AcceptKw("mut");
                if (byRef) throw Unsupported("a `ref` pattern", pt.Start, Prev.End);
                if (!IsIdent && !IsKw("crate") && !IsKw("self")) throw Expected("identifier");
                var first = Cur;
                if (!mut && (Is("::") || IsAt(1, "::") || IsAt(1, "(")))
                {
                    pt.Path = new List<string>();
                    while (true)
                    {
                        pt.Path.Add(Advance().Text);
                        if (!Accept("::")) break;
                        if (!IsIdent) throw Expected("identifier");
                    }
                    pt.PathEnd = Prev.End;
                    pt.Kind = RPatKind.Path;
                    if (Is("("))
                    {
                        Advance();
                        pt.Kind = RPatKind.TupleStruct;
                        pt.Items = new List<RPat>();
                        while (!Is(")")) { pt.Items.Add(Pattern()); if (!Accept(",")) break; }
                        Expect(")");
                    }
                    else if (Is("{")) throw Unsupported("a struct pattern", Cur.Start, Cur.End);
                }
                else
                {
                    var nameTok = Advance();
                    pt.Kind = RPatKind.Ident;
                    pt.Name = nameTok.Text;
                    pt.Mut = mut;
                    if (Is("@")) throw Unsupported("an `@` binding", Cur.Start, Cur.End);
                }
            }
            else throw Expected("pattern");
            pt.End = Prev.End;
            return pt;
        }

        RExpr LiteralPatternExpr()
        {
            if (Is("-"))
            {
                var m = Advance();
                var lit = Literal();
                return new RUnary { Op = "-", Operand = lit, Start = m.Start, OpEnd = m.End, End = lit.End };
            }
            return Literal();
        }

        // ------------------------------------------------------------------ blocks / statements
        RBlock Block()
        {
            var b = new RBlock { Start = Cur.Start };
            Expect("{");
            while (!Is("}"))
            {
                if (AtEof) throw Err("this file contains an unclosed delimiter", b.Start, b.Start + 1);
                if (Accept(";")) continue;
                if (Is("#")) { SkipAttribute(); continue; }
                if (IsKw("let")) { b.Stmts.Add(Let()); continue; }
                if (IsKw("fn") || IsKw("struct") || IsKw("enum") || IsKw("impl") || IsKw("trait") || IsKw("use") || IsKw("mod") || IsKw("pub")
                    || (IsKw("const") && !IsAt(1, "{")) || IsKw("static") || (IsKw("type") && Peek().Kind == RTk.Ident))
                {
                    var it = Item(topLevel: false);
                    b.Stmts.Add(new RItemStmt { Item = it, Start = it.Start, End = it.End });
                    continue;
                }
                int st = Cur.Start;
                if (StartsBlockLike())
                {
                    var e = BlockLikeExpr();
                    if (Is(".") || Is("?"))
                    {
                        e = PostfixFrom(e);
                        e = BinaryRest(e);
                    }
                    if (Accept(";")) { b.Stmts.Add(new RExprStmt { E = e, Semi = true, Start = st, End = Prev.End }); continue; }
                    if (Is("}")) { b.Tail = e; break; }
                    b.Stmts.Add(new RExprStmt { E = e, Semi = false, Start = st, End = e.End });
                    continue;
                }
                var ex = Expr();
                if (Accept(";")) { b.Stmts.Add(new RExprStmt { E = ex, Semi = true, Start = st, End = Prev.End }); continue; }
                if (Is("}")) { b.Tail = ex; break; }
                if ((Is("+") && IsAt(1, "+")) || (Is("-") && IsAt(1, "-")))
                    throw Err("Rust has no postfix " + (Is("+") ? "increment" : "decrement") + " operator (write `x " + Cur.Text + "= 1` instead)", Cur.Start, Peek().End);
                if (Cur.Kind == RTk.EOF) throw Err("this file contains an unclosed delimiter", b.Start, b.Start + 1);
                throw Expected("`;`");
            }
            Expect("}");
            b.End = Prev.End;
            return b;
        }

        bool StartsBlockLike()
        {
            if (IsKw("if") || IsKw("match") || IsKw("loop") || IsKw("while") || IsKw("for")) return true;
            if (Is("{")) return true;
            if (IsKw("unsafe") && IsAt(1, "{")) return true;
            if (Cur.Kind == RTk.Lifetime && IsAt(1, ":")) return true;
            return false;
        }

        RLet Let()
        {
            var l = new RLet { Start = Cur.Start };
            Advance();
            l.Pat = Pattern(allowOr: false);
            if (Accept(":")) l.Type = Type();
            if (Accept("="))
            {
                l.Init = Expr();
                if (IsKw("else"))
                {
                    Advance();
                    l.Else = Block();
                    throw Unsupported("`let ... else`", l.Start, l.Else.End);
                }
            }
            if (!Is(";"))
            {
                if ((Is("+") && IsAt(1, "+")) || (Is("-") && IsAt(1, "-")))
                    throw Err("Rust has no postfix " + (Is("+") ? "increment" : "decrement") + " operator", Cur.Start, Peek().End);
                throw Expected("`;`");
            }
            Advance();
            l.End = Prev.End;
            return l;
        }

        // ------------------------------------------------------------------ expressions
        RExpr Expr() => Assignment();

        static readonly HashSet<string> AssignOps = new HashSet<string> { "=", "+=", "-=", "*=", "/=", "%=", "^=", "&=", "|=", "<<=", ">>=" };

        RExpr Assignment()
        {
            var left = RangeExpr();
            if (Cur.Kind == RTk.Punct && AssignOps.Contains(Cur.Text))
            {
                var op = Advance();
                if (op.Text == "^=" || op.Text == "&=" || op.Text == "|=" || op.Text == "<<=" || op.Text == ">>=")
                    throw Unsupported("the bitwise operator `" + op.Text + "`", op.Start, op.End);
                var right = Assignment();
                return new RAssign { Op = op.Text, OpStart = op.Start, OpEnd = op.End, Target = left, Value = right, Start = left.Start, End = right.End };
            }
            return left;
        }

        bool StartsExpr()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case RTk.Ident: case RTk.Int: case RTk.Float: case RTk.Str: case RTk.Char: return true;
                case RTk.Keyword:
                    return t.Text == "true" || t.Text == "false" || t.Text == "if" || t.Text == "match" || t.Text == "loop" || t.Text == "while"
                        || t.Text == "for" || t.Text == "move" || t.Text == "return" || t.Text == "break" || t.Text == "continue" || t.Text == "crate"
                        || t.Text == "unsafe" || t.Text == "self";
                case RTk.Punct:
                    return t.Text == "(" || t.Text == "[" || t.Text == "-" || t.Text == "!" || t.Text == "*" || t.Text == "&" || t.Text == "&&" || t.Text == "|" || t.Text == "||" || t.Text == "{";
            }
            return false;
        }

        bool noBraceExpr;   // condition / scrutinee context: '{' starts the body, not an expression

        RExpr RangeExpr()
        {
            if (Is("..") || Is("..="))
            {
                var op = Advance();
                RExpr to = null;
                if (StartsExpr() && !(noBraceExpr && Is("{"))) to = OrExpr();
                return new RRange { From = null, To = to, Inclusive = op.Text == "..=", OpStart = op.Start, OpEnd = op.End, Start = op.Start, End = to?.End ?? op.End };
            }
            var left = OrExpr();
            if (Is("..") || Is("..=") || Is("..."))
            {
                var op = Advance();
                if (op.Text == "...") throw Err("unexpected token: `...` (use `..=` for an inclusive range)", op.Start, op.End);
                RExpr to = null;
                if (StartsExpr() && !(noBraceExpr && Is("{"))) to = OrExpr();
                if (op.Text == "..=" && to == null) throw Err("inclusive range with no end", op.Start, op.End);
                return new RRange { From = left, To = to, Inclusive = op.Text == "..=", OpStart = op.Start, OpEnd = op.End, Start = left.Start, End = to?.End ?? op.End };
            }
            return left;
        }

        RExpr Bin(RExpr l, RToken op, RExpr r) => new RBinary { Op = op.Text, OpStart = op.Start, OpEnd = op.End, Left = l, Right = r, Start = l.Start, End = r.End };

        RExpr OrExpr()
        {
            var l = AndExpr();
            while (Is("||")) { var op = Advance(); l = Bin(l, op, AndExpr()); }
            return l;
        }

        RExpr AndExpr()
        {
            var l = CmpExpr();
            while (Is("&&")) { var op = Advance(); l = Bin(l, op, CmpExpr()); }
            return l;
        }

        static bool IsCmp(RToken t) => t.Kind == RTk.Punct && (t.Text == "==" || t.Text == "!=" || t.Text == "<" || t.Text == ">" || t.Text == "<=" || t.Text == ">=");

        RExpr CmpExpr()
        {
            var l = BitOrExpr();
            if (IsCmp(Cur))
            {
                var op = Advance();
                var r = BitOrExpr();
                l = Bin(l, op, r);
                if (IsCmp(Cur)) throw Err("comparison operators cannot be chained", op.Start, Cur.End);
            }
            return l;
        }

        RExpr BitOrExpr()
        {
            var l = BitXorExpr();
            while (Is("|") && !noClosureBar) { var op = Advance(); throw Unsupported("the bitwise operator `|`", op.Start, op.End); }
            return l;
        }
        bool noClosureBar;

        RExpr BitXorExpr()
        {
            var l = BitAndExpr();
            while (Is("^")) { var op = Advance(); throw Unsupported("the bitwise operator `^`", op.Start, op.End); }
            return l;
        }

        RExpr BitAndExpr()
        {
            var l = ShiftExpr();
            while (Is("&")) { var op = Advance(); throw Unsupported("the bitwise operator `&`", op.Start, op.End); }
            return l;
        }

        RExpr ShiftExpr()
        {
            var l = AddExpr();
            while (Is("<<") || Is(">>")) { var op = Advance(); throw Unsupported("the shift operator `" + op.Text + "`", op.Start, op.End); }
            return l;
        }

        RExpr AddExpr()
        {
            var l = MulExpr();
            while ((Is("+") || Is("-")) && !(IsAt(1, Cur.Text) && (Peek(2).Kind == RTk.Punct && (Peek(2).Text == ";" || Peek(2).Text == "}" || Peek(2).Text == ")"))))
            {
                var op = Advance();
                l = Bin(l, op, MulExpr());
            }
            return l;
        }

        RExpr MulExpr()
        {
            var l = CastExpr();
            while (Is("*") || Is("/") || Is("%")) { var op = Advance(); l = Bin(l, op, CastExpr()); }
            return l;
        }

        RExpr CastExpr()
        {
            var e = Unary();
            while (IsKw("as"))
            {
                var asTok = Advance();
                var t = Type();
                e = new RCast { Operand = e, To = t, AsStart = asTok.Start, Start = e.Start, End = t.End };
            }
            return e;
        }

        RExpr Unary()
        {
            if (Is("-") || Is("!") || Is("*"))
            {
                var op = Advance();
                var operand = Unary();
                return new RUnary { Op = op.Text, OpEnd = op.End, Operand = operand, Start = op.Start, End = operand.End };
            }
            if (Is("&") || Is("&&"))
            {
                var op = Advance();
                bool mut = AcceptKw("mut");
                if (IsKw("raw")) throw Unsupported("raw pointers", Cur.Start, Cur.End);
                var operand = Unary();
                var u = new RUnary { Op = mut ? "&mut" : "&", OpEnd = Prev.Start <= op.End ? op.End : (mut ? op.End + 4 : op.End), Operand = operand, Start = op.Start, End = operand.End };
                u.OpEnd = op.End;
                if (op.Text == "&&") return new RUnary { Op = "&", OpEnd = op.Start + 1, Operand = new RUnary { Op = u.Op, OpEnd = op.End, Operand = operand, Start = op.Start + 1, End = operand.End }, Start = op.Start, End = operand.End };
                return u;
            }
            var e = Primary();
            return PostfixFrom(e);
        }

        RExpr PostfixFrom(RExpr e)
        {
            while (true)
            {
                if (Is("."))
                {
                    Advance();
                    if (IsKw("await")) throw Unsupported("`.await`", Cur.Start, Cur.End);
                    if (Cur.Kind == RTk.Int)
                    {
                        var it = Advance();
                        if (it.Suffix != null) throw Err("invalid suffix `" + it.Suffix + "` for number literal", it.Start, it.End);
                        e = new RField { Recv = e, Name = it.Text, TupleIndex = (int)it.IntVal, NameStart = it.Start, NameEnd = it.End, Start = e.Start, End = it.End };
                        continue;
                    }
                    if (Cur.Kind == RTk.Float)
                    {
                        // t.0.1 lexes as t . 0.1
                        var ft = Advance();
                        var parts = ft.Text.Split('.');
                        int a = int.Parse(parts[0], CultureInfo.InvariantCulture);
                        int dot = ft.Start + parts[0].Length;
                        e = new RField { Recv = e, Name = parts[0], TupleIndex = a, NameStart = ft.Start, NameEnd = dot, Start = e.Start, End = dot };
                        if (parts.Length > 1 && parts[1].Length > 0)
                            e = new RField { Recv = e, Name = parts[1], TupleIndex = int.Parse(parts[1], CultureInfo.InvariantCulture), NameStart = dot + 1, NameEnd = ft.End, Start = e.Start, End = ft.End };
                        continue;
                    }
                    var nameTok = Cur;
                    var name = ExpectIdent("identifier");
                    List<RTypeRef> turbo = null;
                    if (Is("::"))
                    {
                        Advance();
                        Expect("<");
                        turbo = new List<RTypeRef>();
                        while (!Is(">") && !(Cur.Kind == RTk.Punct && Cur.Text.StartsWith(">"))) { turbo.Add(Type()); if (!Accept(",")) break; }
                        Expect(">");
                    }
                    if (Is("("))
                    {
                        var mc = new RMethodCall { Recv = e, Name = name, NameStart = nameTok.Start, NameEnd = nameTok.End, Turbofish = turbo, Start = e.Start };
                        Args(mc.Args);
                        mc.End = Prev.End;
                        e = mc;
                    }
                    else e = new RField { Recv = e, Name = name, NameStart = nameTok.Start, NameEnd = nameTok.End, Start = e.Start, End = nameTok.End };
                    continue;
                }
                if (Is("("))
                {
                    var c = new RCall { Callee = e, Start = e.Start, ParenStart2 = Cur.Start };
                    Args(c.Args);
                    c.End = Prev.End;
                    e = c;
                    continue;
                }
                if (Is("["))
                {
                    Advance();
                    bool old = noBraceExpr; noBraceExpr = false;
                    var idx = Expr();
                    noBraceExpr = old;
                    Expect("]");
                    e = new RIndex { Target = e, Index = idx, Start = e.Start, End = Prev.End };
                    continue;
                }
                if (Is("?")) throw Unsupported("the `?` operator", Cur.Start, Cur.End);
                return e;
            }
        }

        void Args(List<RExpr> args)
        {
            Expect("(");
            bool old = noBraceExpr; noBraceExpr = false;
            bool oldBar = noClosureBar; noClosureBar = false;
            while (!Is(")"))
            {
                if (AtEof) throw Err("this file contains an unclosed delimiter", Prev);
                args.Add(Expr());
                if (!Accept(","))
                {
                    if (!Is(")")) throw Expected("one of `)` or `,`");
                    break;
                }
            }
            noBraceExpr = old; noClosureBar = oldBar;
            Expect(")");
        }

        RLit Literal()
        {
            var t = Advance();
            CheckTok(t);
            switch (t.Kind)
            {
                case RTk.Int:
                    if (t.Suffix != null && !IntSuffixes.Contains(t.Suffix) && t.Suffix != "f32" && t.Suffix != "f64")
                        throw Err("invalid suffix `" + t.Suffix + "` for number literal", t.Start, t.End);
                    if (t.IntOverflow) throw Err("integer literal is too large", t.Start, t.End);
                    return new RLit { Kind = RLitKind.Int, I = t.IntVal, Suffix = t.Suffix, Start = t.Start, End = t.End, Tok = t };
                case RTk.Float:
                    if (t.Suffix != null && t.Suffix != "f32" && t.Suffix != "f64")
                        throw Err("invalid suffix `" + t.Suffix + "` for float literal", t.Start, t.End);
                    return new RLit { Kind = RLitKind.Float, F = t.FloatVal, Suffix = t.Suffix, Start = t.Start, End = t.End, Tok = t };
                case RTk.Str:
                    if (t.Byte) throw Unsupported("byte strings", t.Start, t.End);
                    if (t.Suffix != null) throw Err("suffixes on string literals are invalid", t.Start, t.End);
                    return new RLit { Kind = RLitKind.Str, S = t.Text, Start = t.Start, End = t.End, Tok = t };
                case RTk.Char:
                    throw Unsupported("a `char` literal (use a string \"x\")", t.Start, t.End);
                case RTk.Keyword:
                    return new RLit { Kind = RLitKind.Bool, B = t.Text == "true", Start = t.Start, End = t.End, Tok = t };
            }
            throw Err("expected literal, found " + Describe(t), t);
        }

        internal static readonly HashSet<string> IntSuffixes = new HashSet<string> { "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize" };

        RExpr Primary()
        {
            var t = Cur;
            CheckTok(t);
            switch (t.Kind)
            {
                case RTk.Int: case RTk.Float: case RTk.Str: case RTk.Char:
                    return Literal();
                case RTk.Lifetime:
                    if (IsAt(1, ":")) throw Unsupported("a loop label", t.Start, Peek().End);
                    throw Unsupported("a lifetime", t.Start, t.End);
                case RTk.Unknown:
                    throw Err(t.Error ?? "unknown start of token: " + t.Text, t);
            }
            if (t.Kind == RTk.Keyword)
            {
                switch (t.Text)
                {
                    case "true": case "false": return Literal();
                    case "if": case "match": case "loop": case "while": case "for": return BlockLikeExpr();
                    case "unsafe":
                        if (IsAt(1, "{")) return BlockLikeExpr();
                        break;
                    case "move": return Closure();
                    case "return":
                        {
                            Advance();
                            RExpr v = null;
                            if (StartsExpr() && !Is("}") ) v = Expr();
                            return new RReturn { Value = v, Start = t.Start, End = v?.End ?? t.End };
                        }
                    case "break":
                        {
                            Advance();
                            string label = null;
                            if (Cur.Kind == RTk.Lifetime) { var lt = Advance(); label = lt.Text; throw Unsupported("`break` with a label", t.Start, lt.End); }
                            RExpr v = null;
                            if (StartsExpr() && !Is("{")) { v = Expr(); throw Unsupported("`break` with a value", t.Start, v.End); }
                            return new RBreak { Value = v, Label = label, Start = t.Start, End = t.End };
                        }
                    case "continue":
                        {
                            Advance();
                            if (Cur.Kind == RTk.Lifetime) { var lt = Advance(); throw Unsupported("`continue` with a label", t.Start, lt.End); }
                            return new RContinue { Start = t.Start, End = t.End };
                        }
                    case "crate": case "self": case "super":
                        return PathOrMacro();
                    case "Self":
                        throw Unsupported("`Self`", t.Start, t.End);
                    case "async": throw Unsupported("`async` blocks", t.Start, t.End);
                    case "let":
                        throw Err("expected expression, found `let` statement", t);
                }
                throw Err("expected expression, found keyword `" + t.Text + "`" + (t.Text == "move" ? "" : ""), t);
            }
            if (t.Kind == RTk.Ident) return PathOrMacro();
            if (Is("("))
            {
                Advance();
                bool old = noBraceExpr; noBraceExpr = false;
                bool oldBar = noClosureBar; noClosureBar = false;
                if (Is(")")) { Advance(); noBraceExpr = old; noClosureBar = oldBar; return new RTuple { Start = t.Start, End = Prev.End }; }
                var first = Expr();
                if (Is(","))
                {
                    var tup = new RTuple { Start = t.Start };
                    tup.Items.Add(first);
                    while (Accept(","))
                    {
                        if (Is(")")) break;
                        tup.Items.Add(Expr());
                    }
                    Expect(")");
                    tup.End = Prev.End;
                    noBraceExpr = old; noClosureBar = oldBar;
                    return tup;
                }
                Expect(")");
                noBraceExpr = old; noClosureBar = oldBar;
                if (first.ParenStart < 0 || true) { first.ParenStart = t.Start; first.ParenEnd = Prev.End; }
                return first;
            }
            if (Is("["))
            {
                Advance();
                bool old = noBraceExpr; noBraceExpr = false;
                var arr = new RArray { Start = t.Start };
                if (!Is("]"))
                {
                    var first = Expr();
                    if (Accept(";")) { arr.RepeatValue = first; arr.RepeatCount = Expr(); }
                    else
                    {
                        arr.Items.Add(first);
                        while (Accept(","))
                        {
                            if (Is("]")) break;
                            arr.Items.Add(Expr());
                        }
                    }
                }
                Expect("]");
                noBraceExpr = old;
                arr.End = Prev.End;
                return arr;
            }
            if (Is("{")) return BlockLikeExpr();
            if (Is("|") || Is("||")) return Closure();
            if (Is("-") || Is("!") || Is("*") || Is("&")) return Unary();
            throw Err("expected expression, found " + Describe(t), t);
        }

        RExpr BlockLikeExpr()
        {
            var t = Cur;
            if (t.Kind == RTk.Lifetime) throw Unsupported("a loop label", t.Start, Peek().End);
            if (Is("{"))
            {
                var b = Block();
                return new RBlockExpr { Block = b, Start = b.Start, End = b.End };
            }
            if (IsKw("unsafe"))
            {
                Advance();
                var b = Block();
                b.Unsafe = true;
                b.Start = t.Start;
                return new RBlockExpr { Block = b, Start = t.Start, End = b.End };
            }
            switch (t.Text)
            {
                case "if": return If();
                case "match":
                    {
                        Advance();
                        var m = new RMatch { Start = t.Start, KwEnd = t.End };
                        m.Scrutinee = CondExpr();
                        Expect("{");
                        while (!Is("}"))
                        {
                            if (AtEof) throw Err("this file contains an unclosed delimiter", m.Start, m.KwEnd);
                            var arm = new RArm { Start = Cur.Start };
                            arm.Pat = Pattern();
                            if (IsKw("if")) { Advance(); arm.Guard = Expr(); }
                            if (Is("=")) throw Expected("one of `=>`, `if`, or `|`");
                            Expect("=>");
                            arm.ArrowEnd = Prev.End;
                            if (StartsBlockLike())
                            {
                                arm.Body = BlockLikeExpr();
                                if (Is(".") || Is("?")) { arm.Body = PostfixFrom(arm.Body); arm.Body = BinaryRest(arm.Body); }
                                Accept(",");
                            }
                            else
                            {
                                arm.Body = Expr();
                                if (!Accept(","))
                                {
                                    if (!Is("}")) throw Expected("one of `,`, `.`, `?`, `}`, or an operator");
                                }
                            }
                            arm.End = arm.Body.End;
                            m.Arms.Add(arm);
                        }
                        Expect("}");
                        m.End = Prev.End;
                        return m;
                    }
                case "loop":
                    {
                        Advance();
                        var body = Block();
                        return new RLoop { Body = body, Start = t.Start, End = body.End, KwEnd = t.End };
                    }
                case "while":
                    {
                        Advance();
                        var w = new RWhile { Start = t.Start, KwEnd = t.End };
                        if (IsKw("let"))
                        {
                            Advance();
                            w.LetPat = Pattern();
                            Expect("=");
                            w.LetExpr = CondExpr();
                        }
                        else w.Cond = CondExpr();
                        w.Body = Block();
                        w.End = w.Body.End;
                        return w;
                    }
                case "for":
                    {
                        Advance();
                        var f = new RFor { Start = t.Start, KwEnd = t.End };
                        f.Pat = Pattern(allowOr: false);
                        if (!IsKw("in")) throw Expected("`in`");
                        Advance();
                        f.Iter = CondExpr();
                        f.HeaderEnd = Prev.End;
                        f.Body = Block();
                        f.End = f.Body.End;
                        return f;
                    }
            }
            throw Err("expected expression, found " + Describe(t), t);
        }

        RExpr CondExpr()
        {
            bool old = noBraceExpr; noBraceExpr = true;
            try
            {
                if (Is("{")) throw Expected("expression");
                return Expr();
            }
            finally { noBraceExpr = old; }
        }

        RIf If()
        {
            var t = Advance();
            var i = new RIf { Start = t.Start, KwEnd = t.End };
            if (IsKw("let"))
            {
                Advance();
                i.LetPat = Pattern();
                Expect("=");
                i.LetExpr = CondExpr();
                if (Is("&&")) throw Unsupported("an `if let` chain", Cur.Start, Cur.End);
            }
            else i.Cond = CondExpr();
            if (!Is("{")) throw Expected("`{`");
            i.Then = Block();
            i.End = i.Then.End;
            if (IsKw("else"))
            {
                Advance();
                if (IsKw("if")) i.Else = If();
                else if (Is("{")) { var b = Block(); i.Else = new RBlockExpr { Block = b, Start = b.Start, End = b.End }; }
                else throw Expected("`{`");
                i.End = i.Else.End;
            }
            return i;
        }

        RExpr BinaryRest(RExpr left)
        {
            // continue a binary expression after a block-like operand (rare: match x {..}.len() + 1)
            while (Cur.Kind == RTk.Punct && (Cur.Text == "+" || Cur.Text == "-" || Cur.Text == "*" || Cur.Text == "/" || Cur.Text == "%" || IsCmp(Cur) || Cur.Text == "&&" || Cur.Text == "||"))
            {
                var op = Advance();
                var r = AddExpr();
                left = Bin(left, op, r);
            }
            return left;
        }

        RExpr Closure()
        {
            var c = new RClosure { Start = Cur.Start };
            if (AcceptKw("move")) c.Move = true;
            if (c.Move && Is("(")) throw Err("expected one of `|` or `||`, found `(` (the game's move() is called move_to() or r#move() in Rust)", Cur);
            if (Is("||")) Advance();
            else
            {
                Expect("|");
                bool oldBar = noClosureBar; noClosureBar = true;
                while (!Is("|"))
                {
                    var pr = new RParam { Start = Cur.Start };
                    pr.Pat = PatternAlt();
                    if (Accept(":")) pr.Type = Type();
                    pr.End = Prev.End;
                    c.Params.Add(pr);
                    if (!Accept(",")) break;
                }
                noClosureBar = oldBar;
                Expect("|");
            }
            c.HeaderEnd = Prev.End;
            if (Accept("->"))
            {
                c.Ret = Type();
                if (!Is("{")) throw Expected("`{`");
            }
            bool old = noBraceExpr; noBraceExpr = false;
            c.Body = Expr();
            noBraceExpr = old;
            c.End = c.Body.End;
            return c;
        }

        RExpr PathOrMacro()
        {
            var pe = new RPathExpr { Start = Cur.Start };
            while (true)
            {
                var seg = Cur;
                if (!(seg.Kind == RTk.Ident || (seg.Kind == RTk.Keyword && (seg.Text == "crate" || seg.Text == "self" || seg.Text == "super"))))
                    throw Expected("identifier");
                Advance();
                var ps = new RPathSeg { Name = seg.Text, Start = seg.Start, End = seg.End, Raw = seg.Raw };
                pe.Segs.Add(ps);
                if (Is("::"))
                {
                    Advance();
                    if (Is("<"))
                    {
                        Advance();
                        ps.Generics = new List<RTypeRef>();
                        while (!Is(">") && !(Cur.Kind == RTk.Punct && Cur.Text.StartsWith(">"))) { ps.Generics.Add(Type()); if (!Accept(",")) break; }
                        Expect(">");
                        if (!Accept("::")) break;
                    }
                    if (Is("<")) throw Unsupported("a qualified path", Cur.Start, Cur.End);
                    continue;
                }
                break;
            }
            pe.End = Prev.End;
            if (Is("!") && !IsAt(1, "=")) return Macro(pe);
            if (Is("{") && !noBraceExpr && pe.Segs.Count >= 1 && char.IsUpper(pe.Last[0]) && (Peek().Kind == RTk.Ident && IsAt(2, ":") || IsAt(1, "}")))
                throw Unsupported("a struct literal", pe.Start, Cur.End);
            return pe;
        }

        static readonly HashSet<string> FormatMacros = new HashSet<string> { "println", "print", "eprintln", "eprint", "format", "quick_print", "panic" };

        RExpr Macro(RPathExpr path)
        {
            var bang = Advance();
            var name = path.Segs.Count == 1 ? path.Last : string.Join("::", path.Segs.Select(s => s.Name));
            if (path.Segs.Count == 2 && (path.Segs[0].Name == "std" || path.Segs[0].Name == "farm")) name = path.Last;
            var m = new RMacro { Name = name, NameStart = path.Start, NameEnd = path.End, BangEnd = bang.End, Start = path.Start };
            string open = Cur.Text, close;
            if (Is("(")) close = ")"; else if (Is("[")) close = "]"; else if (Is("{")) close = "}";
            else throw Expected("one of `(`, `[`, or `{`");
            if (name == "vec")
            {
                Advance();
                var arr = new RArray { Start = path.Start, IsVecMacro = true };
                bool old = noBraceExpr; noBraceExpr = false;
                if (!Is(close))
                {
                    var first = Expr();
                    if (Accept(";")) { arr.RepeatValue = first; arr.RepeatCount = Expr(); }
                    else
                    {
                        arr.Items.Add(first);
                        while (Accept(",")) { if (Is(close)) break; arr.Items.Add(Expr()); }
                    }
                }
                noBraceExpr = old;
                Expect(close);
                arr.End = Prev.End;
                return arr;
            }
            if (FormatMacros.Contains(name))
            {
                Advance();
                bool old = noBraceExpr; noBraceExpr = false;
                if (!Is(close))
                {
                    if (Cur.Kind != RTk.Str)
                    {
                        if (Cur.Kind == RTk.EOF) throw Expected("`" + close + "`");
                        var bad = Cur;
                        var e = Expr();
                        throw Err("format argument must be a string literal", bad.Start, e.End);
                    }
                    m.Format = Literal();
                    while (Accept(","))
                    {
                        if (Is(close)) break;
                        if (IsIdent && IsAt(1, "=") ) throw Unsupported("a named format argument", Cur.Start, Peek().End);
                        m.Args.Add(Expr());
                    }
                }
                noBraceExpr = old;
                Expect(close);
                m.End = Prev.End;
                m.Pieces = FormatPieces(m);
                return m;
            }
            // unsupported / unknown macros: skip the tokens for a precise error
            int depth = 0;
            do
            {
                if (Is("(") || Is("[") || Is("{")) depth++;
                else if (Is(")") || Is("]") || Is("}")) depth--;
                if (AtEof) throw Err("this file contains an unclosed delimiter", bang);
                Advance();
            } while (depth > 0);
            m.End = Prev.End;
            switch (name)
            {
                case "assert": case "assert_eq": case "assert_ne": case "debug_assert": case "debug_assert_eq": case "dbg": case "todo":
                case "unimplemented": case "unreachable": case "matches": case "write": case "writeln": case "include": case "include_str":
                case "concat": case "stringify": case "env": case "line": case "file": case "column": case "thread_local": case "cfg":
                    throw Unsupported("the macro `" + name + "!`", path.Start, bang.End);
            }
            throw Err("cannot find macro `" + name + "` in this scope", path.Start, path.End);
        }

        /// <summary>Parses a format string into literal text and {} placeholders (positional, inline names, :? and :.N).</summary>
        List<RFmtPiece> FormatPieces(RMacro m)
        {
            var res = new List<RFmtPiece>();
            if (m.Format == null) { if (m.Args.Count > 0) throw Err("format argument must be a string literal", m.Args[0].Start, m.Args[0].End); return res; }
            var lit = m.Format;
            var text = lit.S ?? "";
            // offsets map 1:1 into the source when the literal has no escapes
            string raw = src.Substring(lit.Start, lit.End - lit.Start);
            bool exact = raw.Length >= 2 && raw[0] == '"' && raw.IndexOf('\\') < 0 && raw.Length - 2 == text.Length;
            int Off(int k) => exact ? lit.Start + 1 + k : lit.Start;
            int OffEnd(int k) => exact ? lit.Start + 1 + k : lit.End;
            var sb = new StringBuilder();
            int next = 0;
            var used = new bool[m.Args.Count];
            void Flush() { if (sb.Length > 0) { res.Add(new RFmtPiece { Text = sb.ToString() }); sb.Clear(); } }
            for (int k = 0; k < text.Length; k++)
            {
                char ch = text[k];
                if (ch == '{')
                {
                    if (k + 1 < text.Length && text[k + 1] == '{') { sb.Append('{'); k++; continue; }
                    int close = text.IndexOf('}', k + 1);
                    if (close < 0) throw Err("invalid format string: expected `}` but string was terminated", Off(k), OffEnd(k + 1));
                    var inner = text.Substring(k + 1, close - k - 1);
                    string argName = inner, spec = "";
                    int colon = inner.IndexOf(':');
                    if (colon >= 0) { argName = inner.Substring(0, colon); spec = inner.Substring(colon + 1); }
                    argName = argName.Trim();
                    bool debug = false;
                    if (spec == "?" || spec == "#?") debug = true;
                    else if (spec.Length > 1 && spec[0] == '.' && spec.Substring(1).TrimEnd('?').All(char.IsDigit))
                        throw Unsupported("the precision `{" + inner + "}` (the game prints numbers in full)", Off(k), OffEnd(close + 1));
                    else if (spec.Length > 0)
                        throw Unsupported("the format spec `{" + inner + "}`", Off(k), OffEnd(close + 1));
                    RExpr arg;
                    if (argName.Length == 0)
                    {
                        if (next >= m.Args.Count)
                            throw Err((CountPlaceholders(text)) + " positional argument" + (CountPlaceholders(text) == 1 ? "" : "s") + " in format string, but " + (m.Args.Count == 0 ? "no arguments were given" : "there " + (m.Args.Count == 1 ? "is 1 argument" : "are " + m.Args.Count + " arguments")), Off(k), OffEnd(close + 1));
                        used[next] = true;
                        arg = m.Args[next++];
                    }
                    else if (argName.All(char.IsDigit))
                    {
                        int idx = int.Parse(argName, CultureInfo.InvariantCulture);
                        if (idx >= m.Args.Count) throw Err("invalid reference to positional argument " + idx + " (" + (m.Args.Count == 0 ? "no arguments were given" : "there " + (m.Args.Count == 1 ? "is 1 argument" : "are " + m.Args.Count + " arguments")) + ")", Off(k), OffEnd(close + 1));
                        used[idx] = true;
                        arg = m.Args[idx];
                    }
                    else if (RustLexer.IsIdentStart(argName[0]) && argName.All(RustLexer.IsIdentPart))
                    {
                        int ns = exact ? Off(k + 1) : lit.Start, ne = exact ? ns + argName.Length : lit.End;
                        var pe = new RPathExpr { Start = ns, End = ne };
                        pe.Segs.Add(new RPathSeg { Name = argName, Start = ns, End = ne });
                        arg = pe;
                    }
                    else throw Err("invalid format string: invalid argument name `" + argName + "`", Off(k), OffEnd(close + 1));
                    Flush();
                    res.Add(new RFmtPiece { Arg = arg, Debug = debug, Start = Off(k), End = OffEnd(close + 1) });
                    k = close;
                    continue;
                }
                if (ch == '}')
                {
                    if (k + 1 < text.Length && text[k + 1] == '}') { sb.Append('}'); k++; continue; }
                    throw Err("invalid format string: unmatched `}` found", Off(k), OffEnd(k + 1));
                }
                sb.Append(ch);
            }
            Flush();
            for (int a = 0; a < used.Length; a++)
                if (!used[a]) throw Err(used.Length - used.Count(x => x) == 1 ? "argument never used" : "multiple unused formatting arguments", m.Args[a].Start, m.Args[a].End);
            return res;
        }

        static int CountPlaceholders(string text)
        {
            int n = 0;
            for (int k = 0; k < text.Length; k++)
            {
                if (text[k] == '{') { if (k + 1 < text.Length && text[k + 1] == '{') { k++; continue; } int c = text.IndexOf('}', k); if (c < 0) break; var inner = text.Substring(k + 1, c - k - 1); int colon = inner.IndexOf(':'); if ((colon < 0 ? inner : inner.Substring(0, colon)).Trim().Length == 0) n++; k = c; }
            }
            return n;
        }
    }
}
