using System;
using System.Collections.Generic;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Recursive-descent parser for the JavaFarmer JavaScript subset (ES2023 module code, strict mode) with real automatic
    /// semicolon insertion. Raises JsSyntaxError at the first problem, with V8's wording for real syntax errors and
    /// "X is not supported in JavaFarmer" for valid JavaScript that the game cannot run.
    /// </summary>
    internal sealed class JsParser
    {
        List<JsToken> toks;
        int p;
        readonly string src;
        int lastEnd;
        int fnDepth, loopDepth, switchDepth, blockDepth;

        JsParser(List<JsToken> tokens, string source) { toks = tokens; src = source ?? ""; }

        public static List<JsStmt> Parse(string source, List<IrComment> comments)
        {
            var toks = JsLexer.Lex(source ?? "", comments);
            var ps = new JsParser(toks, source);
            return ps.Program();
        }

        // ================================================================== tokens
        JsToken Cur => toks[p];
        JsToken PeekTok(int k = 1) => toks[Math.Min(p + k, toks.Count - 1)];
        bool Is(string punct) => Cur.Kind == JsTok.Punct && Cur.Text == punct;
        bool IsAt(int k, string punct) { var t = PeekTok(k); return t.Kind == JsTok.Punct && t.Text == punct; }
        bool IsKw(string kw) => Cur.Kind == JsTok.Ident && Cur.Text == kw;
        bool AtEnd => Cur.Kind == JsTok.Eof;

        JsToken Next()
        {
            var t = toks[p];
            if (t.Kind != JsTok.Eof) { p++; lastEnd = t.End; }
            return t;
        }

        static JsSyntaxError Fail(string msg, Span s) => new JsSyntaxError(msg, s);
        static JsSyntaxError NotSupported(string what, Span s) => new JsSyntaxError(what + " is not supported in JavaFarmer", s);

        /// <summary>The span to report for an unexpected end of input: the last token of the file.</summary>
        Span EofSpan()
        {
            for (int k = Math.Min(p, toks.Count - 1); k >= 0; k--)
                if (toks[k].Kind != JsTok.Eof) return toks[k].Span;
            return new Span(0, Math.Min(1, src.Length));
        }

        JsSyntaxError Unexpected(JsToken t)
        {
            switch (t.Kind)
            {
                case JsTok.Eof:
                    if (t.Text == "}") return Fail("Unexpected token '}'", new Span(t.Start, t.Start + 1));
                    return Fail("Unexpected end of input", EofSpan());
                case JsTok.Error: return Fail(t.Error, t.Span);
                case JsTok.Number: return Fail("Unexpected number", t.Span);
                case JsTok.String: return Fail("Unexpected string", t.Span);
                case JsTok.Template: return Fail("Unexpected template string", t.Span);
                case JsTok.BigInt: return Fail("Unexpected number", t.Span);
                case JsTok.Regex: return Fail("Unexpected regular expression", t.Span);
                case JsTok.Ident:
                    if (JsLexer.Reserved.Contains(t.Text)) return Fail("Unexpected token '" + t.Text + "'", t.Span);
                    if (JsLexer.StrictReserved.Contains(t.Text)) return Fail("Unexpected strict mode reserved word", t.Span);
                    if (t.Text == "await") return Fail("Unexpected reserved word", t.Span);
                    return Fail("Unexpected identifier '" + t.Text + "'", t.Span);
            }
            return Fail("Unexpected token '" + t.Text + "'", t.Span);
        }

        void Expect(string punct)
        {
            if (Is(punct)) { Next(); return; }
            throw Unexpected(Cur);
        }

        /// <summary>Automatic semicolon insertion: a statement ends at ';', before '}', at the end of input or at a line break.</summary>
        void Semi()
        {
            if (Is(";")) { Next(); return; }
            if (AtEnd || Is("}") || Cur.NL) return;
            throw Unexpected(Cur);
        }

        int StartOf() => Cur.Start;
        Span From(int start) => new Span(start, Math.Max(start, lastEnd));

        int BlankLinesBefore(int start)
        {
            int k = start - 1, lines = 0;
            while (k >= 0 && (src[k] == ' ' || src[k] == '\t' || src[k] == '\r' || src[k] == '\n')) { if (src[k] == '\n') lines++; k--; }
            // skip back over comment lines: blank lines before a statement's leading comments are the comment's business
            return k < 0 ? 0 : Math.Max(0, lines - 1);
        }

        // ================================================================== program / statements
        List<JsStmt> Program()
        {
            var list = new List<JsStmt>();
            while (!AtEnd)
            {
                if (Cur.Kind == JsTok.Eof) break;
                list.Add(Statement(true));
            }
            if (Cur.Kind == JsTok.Eof && Cur.Text == "}") throw Unexpected(Cur);
            return list;
        }

        string BindingName(out Span span)
        {
            var t = Cur;
            if (t.Kind != JsTok.Ident) throw Unexpected(t);
            if (JsLexer.Reserved.Contains(t.Text)) throw Fail("Unexpected token '" + t.Text + "'", t.Span);
            if (JsLexer.StrictReserved.Contains(t.Text)) throw Fail("Unexpected strict mode reserved word", t.Span);
            if (t.Text == "await") throw Fail("Unexpected reserved word", t.Span);
            if (t.Text == "eval" || t.Text == "arguments") throw Fail("Unexpected eval or arguments in strict mode", t.Span);
            Next();
            span = t.Span;
            return t.Text;
        }

        JsStmt Statement(bool topLevel = false)
        {
            int start = StartOf();
            int blank = BlankLinesBefore(start);
            var s = StatementCore(topLevel);
            s.BlankLines = blank;
            if (s.Span.IsNone || s.Span.End == 0) s.Span = From(start);
            return s;
        }

        /// <summary>The body of if / while / for: a declaration is not allowed there.</summary>
        JsStmt SubStatement()
        {
            if ((IsKw("let") && (PeekTok().Kind == JsTok.Ident || IsAt(1, "[") || IsAt(1, "{"))) || IsKw("const") || IsKw("class"))
                throw Fail("Lexical declaration cannot appear in a single-statement context", Cur.Span);
            if (IsKw("function")) throw Fail("In strict mode code, functions can only be declared at top level or inside a block.", Cur.Span);
            return Statement();
        }

        JsStmt StatementCore(bool topLevel)
        {
            var t = Cur;
            int start = t.Start;
            if (t.Kind == JsTok.Punct)
            {
                if (t.Text == "{") return Block();
                if (t.Text == ";") { Next(); return new JsEmpty { Span = t.Span }; }
            }
            if (t.Kind == JsTok.Ident)
            {
                switch (t.Text)
                {
                    case "let":
                        if (PeekTok().Kind == JsTok.Ident || IsAt(1, "[") || IsAt(1, "{")) return VarDecl(false);
                        break;
                    case "const":
                    case "var":
                        return VarDecl(false);
                    case "function": return FunctionDecl(false, start);
                    case "async":
                        if (PeekTok().Kind == JsTok.Ident && PeekTok().Text == "function" && !PeekTok().NL) throw NotSupported("async functions are", t.Span).Plural();
                        break;
                    case "class": throw NotSupported("classes are", t.Span).Plural();
                    case "if": return If();
                    case "while": return While();
                    case "do": return DoWhile();
                    case "for": return For();
                    case "switch": return Switch();
                    case "break":
                    case "continue":
                        return Jump();
                    case "return": return Return();
                    case "try": throw NotSupported("try/catch", t.Span);
                    case "throw": throw NotSupported("throw", t.Span);
                    case "with": throw Fail("Strict mode code may not include a with statement", t.Span);
                    case "debugger": throw NotSupported("debugger", t.Span);
                    case "import":
                        if (IsAt(1, "(") || IsAt(1, ".")) break;
                        if (!topLevel || blockDepth > 0 || fnDepth > 0) throw Fail("Cannot use import statement outside a module", t.Span);
                        return Import();
                    case "export":
                        if (!topLevel || blockDepth > 0 || fnDepth > 0) throw Unexpected(t);
                        return Export();
                    case "enum": throw Fail("Unexpected reserved word", t.Span);
                }
                if (IsAt(1, ":") && !JsLexer.Reserved.Contains(t.Text)) throw NotSupported("labels are", t.Span).Plural();
            }
            var e = Expression(false);
            Semi();
            return new JsExprStmt { Expr = e, Span = From(start) };
        }

        JsBlock Block()
        {
            int start = StartOf();
            Expect("{");
            var b = new JsBlock();
            blockDepth++;
            while (!Is("}"))
            {
                if (AtEnd) throw Unexpected(Cur);
                b.Stmts.Add(Statement());
            }
            blockDepth--;
            Next();
            b.Span = From(start);
            return b;
        }

        JsVarDecl VarDecl(bool inFor)
        {
            int start = StartOf();
            var kw = Next();
            var d = new JsVarDecl { Kind = kw.Text, KwSpan = kw.Span };
            while (true)
            {
                var dc = new JsDeclarator();
                int ds = StartOf();
                if (Is("["))
                {
                    dc.Target = ArrayPattern();
                }
                else if (Is("{"))
                {
                    throw NotSupported("object destructuring", Cur.Span);
                }
                else
                {
                    var name = BindingName(out var ns);
                    if (name == "let" && kw.Text != "var") throw Fail("let is disallowed as a lexically bound name", ns);
                    dc.Target = new JsIdent { Name = name, Span = ns };
                }
                if (Is("="))
                {
                    Next();
                    dc.Init = Assignment(inFor);
                }
                else if (!(inFor && (IsKw("of") || IsKw("in"))))
                {
                    if (dc.Target is JsArrayLit) throw Fail("Missing initializer in destructuring declaration", dc.Target.Span);
                    if (kw.Text == "const") throw Fail("Missing initializer in const declaration", dc.Target.Span);
                }
                dc.Span = From(ds);
                d.Decls.Add(dc);
                if (!Is(",")) break;
                Next();
            }
            if (!inFor) Semi();
            d.Span = From(start);
            return d;
        }

        /// <summary>[a, b] in a declaration or assignment target (identifiers only; holes skip an item).</summary>
        JsArrayLit ArrayPattern()
        {
            int start = StartOf();
            Expect("[");
            var a = new JsArrayLit();
            while (!Is("]"))
            {
                if (Is(",")) { Next(); a.Items.Add(null); continue; }
                if (Is("...")) throw NotSupported("rest elements (...) in destructuring are", Cur.Span).Plural();
                if (Is("[")) { a.Items.Add(ArrayPattern()); if (Is(",")) { Next(); continue; } if (!Is("]")) throw Unexpected(Cur); continue; }
                if (Is("{")) throw NotSupported("object destructuring", Cur.Span);
                var name = BindingName(out var ns);
                if (Is("=")) throw NotSupported("default values in destructuring are", Cur.Span).Plural();
                a.Items.Add(new JsIdent { Name = name, Span = ns });
                if (Is(",")) { Next(); continue; }
                if (!Is("]")) throw Unexpected(Cur);
            }
            Next();
            a.Span = From(start);
            return a;
        }

        List<JsParam> Params()
        {
            Expect("(");
            var list = new List<JsParam>();
            var seen = new HashSet<string>();
            while (!Is(")"))
            {
                if (Is("...")) throw NotSupported("rest parameters (...) are", Cur.Span).Plural();
                if (Is("[") || Is("{")) throw NotSupported("destructuring parameters are", Cur.Span).Plural();
                int ps = StartOf();
                var name = BindingName(out var ns);
                if (!seen.Add(name)) throw Fail("Duplicate parameter name not allowed in this context", ns);
                var prm = new JsParam { Name = name, NameSpan = ns };
                if (Is("=")) { Next(); prm.Default = Assignment(false); }
                prm.Span = From(ps);
                list.Add(prm);
                if (Is(",")) { Next(); continue; }
                if (!Is(")")) throw Unexpected(Cur);
            }
            Next();
            return list;
        }

        JsBlock FunctionBody()
        {
            int sf = fnDepth, sl = loopDepth, ss = switchDepth, sb = blockDepth;
            fnDepth++; loopDepth = 0; switchDepth = 0; blockDepth = 0;
            try { return Block(); }
            finally { fnDepth = sf; loopDepth = sl; switchDepth = ss; blockDepth = sb; }
        }

        JsFunctionDecl FunctionDecl(bool export, int start)
        {
            Next();   // function
            if (Is("*")) throw NotSupported("generator functions are", Cur.Span).Plural();
            var f = new JsFunctionDecl { IsExport = export };
            f.Name = BindingName(out var ns);
            f.NameSpan = ns;
            f.Params = Params();
            f.HeaderSpan = From(start);
            f.Body = FunctionBody();
            f.Span = From(start);
            return f;
        }

        JsExpr ParenExpr()
        {
            Expect("(");
            var e = Expression(false);
            Expect(")");
            return e;
        }

        JsIf If()
        {
            int start = StartOf();
            var kw = Next();
            var s = new JsIf { KwSpan = kw.Span };
            s.Cond = ParenExpr();
            s.Then = SubStatement();
            if (IsKw("else"))
            {
                s.ElseSpan = Next().Span;
                s.Else = SubStatement();
            }
            s.Span = From(start);
            return s;
        }

        JsWhile While()
        {
            int start = StartOf();
            var kw = Next();
            var s = new JsWhile { KwSpan = kw.Span };
            s.Cond = ParenExpr();
            loopDepth++;
            s.Body = SubStatement();
            loopDepth--;
            s.Span = From(start);
            return s;
        }

        JsDoWhile DoWhile()
        {
            int start = StartOf();
            var kw = Next();
            var s = new JsDoWhile { KwSpan = kw.Span };
            loopDepth++;
            s.Body = SubStatement();
            loopDepth--;
            if (!IsKw("while")) throw Unexpected(Cur);
            s.WhileSpan = Next().Span;
            s.Cond = ParenExpr();
            if (Is(";")) Next();
            s.Span = From(start);
            return s;
        }

        JsStmt For()
        {
            int start = StartOf();
            var kw = Next();
            if (IsKw("await")) throw NotSupported("for await", Cur.Span);
            Expect("(");
            JsVarDecl decl = null;
            List<JsExpr> initExprs = null;
            if ((IsKw("let") && (PeekTok().Kind == JsTok.Ident || IsAt(1, "["))) || IsKw("const") || IsKw("var"))
            {
                decl = VarDecl(true);
                if (IsKw("of") || IsKw("in"))
                {
                    bool isIn = Cur.Text == "in";
                    if (decl.Decls.Count != 1) throw Fail("Invalid left-hand side in for-" + (isIn ? "in" : "of") + " loop: Must have a single binding.", decl.Span);
                    if (decl.Decls[0].Init != null) throw Fail("for-" + (isIn ? "in" : "of") + " loop variable declaration may not have an initializer.", decl.Decls[0].Span);
                    Next();
                    return ForOfRest(start, kw, decl.Kind, decl.Decls[0].Target, isIn);
                }
            }
            else if (!Is(";"))
            {
                if (Is("["))
                {
                    // for ([a, b] of pairs) with existing variables
                    int close = MatchBracket(p);
                    if (close > 0 && close + 1 < toks.Count && toks[close + 1].Kind == JsTok.Ident && toks[close + 1].Text == "of")
                    {
                        var pat = ArrayPattern();
                        Next();
                        return ForOfRest(start, kw, null, pat, false);
                    }
                }
                if (Cur.Kind == JsTok.Ident && PeekTok().Kind == JsTok.Ident && (PeekTok().Text == "of" || PeekTok().Text == "in"))
                {
                    var name = BindingName(out var ns);
                    bool isIn = Cur.Text == "in";
                    Next();
                    return ForOfRest(start, kw, null, new JsIdent { Name = name, Span = ns }, isIn);
                }
                initExprs = new List<JsExpr>();
                while (true)
                {
                    initExprs.Add(Assignment(true));
                    if (!Is(",")) break;
                    Next();
                }
            }
            Expect(";");
            var f = new JsFor { InitDecl = decl, InitExprs = initExprs, KwSpan = kw.Span };
            if (!Is(";")) f.Cond = Expression(false);
            Expect(";");
            if (!Is(")"))
            {
                while (true)
                {
                    f.Update.Add(Assignment(false));
                    if (!Is(",")) break;
                    Next();
                }
            }
            Expect(")");
            f.HeaderSpan = From(start);
            loopDepth++;
            f.Body = SubStatement();
            loopDepth--;
            f.Span = From(start);
            return f;
        }

        JsForOf ForOfRest(int start, JsToken kw, string declKind, JsExpr target, bool isIn)
        {
            var f = new JsForOf { IsIn = isIn, DeclKind = declKind, Target = target, KwSpan = kw.Span };
            f.Iter = isIn ? Expression(false) : Assignment(false);
            Expect(")");
            f.HeaderSpan = From(start);
            loopDepth++;
            f.Body = SubStatement();
            loopDepth--;
            f.Span = From(start);
            return f;
        }

        JsSwitch Switch()
        {
            int start = StartOf();
            var kw = Next();
            var s = new JsSwitch { KwSpan = kw.Span };
            s.Disc = ParenExpr();
            Expect("{");
            switchDepth++;
            blockDepth++;
            bool sawDefault = false;
            while (!Is("}"))
            {
                if (AtEnd) throw Unexpected(Cur);
                int cs = StartOf();
                var c = new JsCase();
                if (IsKw("case"))
                {
                    Next();
                    c.Test = Expression(false);
                }
                else if (IsKw("default"))
                {
                    if (sawDefault) throw Fail("More than one default clause in switch statement", Cur.Span);
                    sawDefault = true;
                    Next();
                }
                else throw Unexpected(Cur);
                Expect(":");
                c.LabelSpan = From(cs);
                while (!Is("}") && !IsKw("case") && !IsKw("default"))
                {
                    if (AtEnd) throw Unexpected(Cur);
                    c.Body.Add(Statement());
                }
                c.Span = From(cs);
                s.Cases.Add(c);
            }
            blockDepth--;
            switchDepth--;
            Next();
            s.Span = From(start);
            return s;
        }

        JsStmt Jump()
        {
            int start = StartOf();
            var kw = Next();
            bool isBreak = kw.Text == "break";
            if (!Cur.NL && Cur.Kind == JsTok.Ident && !JsLexer.Reserved.Contains(Cur.Text))
                throw NotSupported("labels are", Cur.Span).Plural();
            if (isBreak && loopDepth == 0 && switchDepth == 0) throw Fail("Illegal break statement", kw.Span);
            if (!isBreak && loopDepth == 0) throw Fail("Illegal continue statement: no surrounding iteration statement", kw.Span);
            Semi();
            return isBreak ? (JsStmt)new JsBreak { Span = From(start) } : new JsContinue { Span = From(start) };
        }

        JsReturn Return()
        {
            int start = StartOf();
            var kw = Next();
            if (fnDepth == 0) throw Fail("Illegal return statement", kw.Span);
            var r = new JsReturn();
            if (!Is(";") && !Is("}") && !AtEnd && !Cur.NL) r.Value = Expression(false);
            Semi();
            r.Span = From(start);
            return r;
        }

        JsStmt Import()
        {
            int start = StartOf();
            Next();   // import
            var im = new JsImport();
            if (Cur.Kind == JsTok.String)
            {
                var ps = Next();
                im.Path = ps.Str; im.PathSpan = ps.Span;
                Semi();
                im.Span = From(start);
                return im;
            }
            if (Is("*"))
            {
                Next();
                if (!IsKw("as")) throw Unexpected(Cur);
                Next();
                im.Namespace = BindingName(out var ns);
                im.NamespaceSpan = ns;
            }
            else if (Is("{"))
            {
                Next();
                im.Names = new List<JsImportName>();
                while (!Is("}"))
                {
                    var t = Cur;
                    if (t.Kind != JsTok.Ident && t.Kind != JsTok.String) throw Unexpected(t);
                    Next();
                    var n = new JsImportName { Imported = t.Kind == JsTok.String ? t.Str : t.Text, ImportedSpan = t.Span, Local = t.Text, LocalSpan = t.Span };
                    if (IsKw("as"))
                    {
                        Next();
                        n.Local = BindingName(out var ls);
                        n.LocalSpan = ls;
                    }
                    else if (t.Kind == JsTok.String) throw Unexpected(Cur);
                    else if (JsLexer.Reserved.Contains(t.Text)) throw Fail("Unexpected token '" + t.Text + "'", t.Span);
                    im.Names.Add(n);
                    if (Is(",")) { Next(); continue; }
                    if (!Is("}")) throw Unexpected(Cur);
                }
                Next();
            }
            else if (Cur.Kind == JsTok.Ident)
            {
                im.Default = BindingName(out var ds);
                im.NamespaceSpan = ds;
                if (Is(",")) throw NotSupported("default imports are", From(start)).Plural();
            }
            else throw Unexpected(Cur);
            if (!IsKw("from")) throw Unexpected(Cur);
            Next();
            if (Cur.Kind != JsTok.String) throw Unexpected(Cur);
            var pt = Next();
            im.Path = pt.Str; im.PathSpan = pt.Span;
            if (IsKw("with") || IsKw("assert")) throw NotSupported("import attributes are", Cur.Span).Plural();
            Semi();
            im.Span = From(start);
            if (im.Default != null) throw NotSupported("default imports are (use import * as " + im.Default + " from \"" + im.Path + "\")", im.Span).Plural();
            return im;
        }

        JsStmt Export()
        {
            int start = StartOf();
            var kw = Next();
            if (IsKw("default")) throw NotSupported("export default", new Span(start, Cur.End));
            if (IsKw("function"))
            {
                var f = FunctionDecl(true, start);
                return f;
            }
            if (IsKw("const") || IsKw("let") || IsKw("var"))
            {
                var d = VarDecl(false);
                d.IsExport = true;
                d.Span = From(start);
                return d;
            }
            if (Is("{"))
            {
                Next();
                var ex = new JsExportList();
                while (!Is("}"))
                {
                    var t = Cur;
                    if (t.Kind != JsTok.Ident) throw Unexpected(t);
                    Next();
                    var n = new JsImportName { Imported = t.Text, ImportedSpan = t.Span, Local = t.Text, LocalSpan = t.Span };
                    if (IsKw("as")) throw NotSupported("export ... as", Cur.Span);
                    ex.Names.Add(n);
                    if (Is(",")) { Next(); continue; }
                    if (!Is("}")) throw Unexpected(Cur);
                }
                Next();
                if (IsKw("from")) throw NotSupported("re-exporting (export ... from)", From(start));
                Semi();
                ex.Span = From(start);
                return ex;
            }
            if (Is("*")) throw NotSupported("re-exporting (export * from)", new Span(start, Cur.End));
            if (IsKw("class")) throw NotSupported("classes are", Cur.Span).Plural();
            if (IsKw("async")) throw NotSupported("async functions are", Cur.Span).Plural();
            throw Unexpected(Cur);
        }

        // ================================================================== expressions
        /// <summary>An expression; the comma operator is rejected.</summary>
        JsExpr Expression(bool noIn)
        {
            var e = Assignment(noIn);
            if (Is(",")) throw NotSupported("the comma operator", Cur.Span);
            return e;
        }

        static bool IsAssignOp(string op)
        {
            switch (op)
            {
                case "=": case "+=": case "-=": case "*=": case "/=": case "%=": case "**=":
                case "&=": case "|=": case "^=": case "<<=": case ">>=": case ">>>=": case "&&=": case "||=": case "??=":
                    return true;
            }
            return false;
        }

        /// <summary>Index of the ')' matching the '(' at token index k, or -1.</summary>
        int MatchParen(int k)
        {
            int depth = 0;
            for (int j = k; j < toks.Count; j++)
            {
                var t = toks[j];
                if (t.Kind == JsTok.Eof) return -1;
                if (t.Kind != JsTok.Punct) continue;
                if (t.Text == "(" || t.Text == "[" || t.Text == "{") depth++;
                else if (t.Text == ")" || t.Text == "]" || t.Text == "}") { depth--; if (depth == 0) return t.Text == ")" ? j : -1; }
            }
            return -1;
        }

        /// <summary>Index of the ']' matching the '[' at token index k, or -1.</summary>
        int MatchBracket(int k)
        {
            int depth = 0;
            for (int j = k; j < toks.Count; j++)
            {
                var t = toks[j];
                if (t.Kind == JsTok.Eof) return -1;
                if (t.Kind != JsTok.Punct) continue;
                if (t.Text == "(" || t.Text == "[" || t.Text == "{") depth++;
                else if (t.Text == ")" || t.Text == "]" || t.Text == "}") { depth--; if (depth == 0) return t.Text == "]" ? j : -1; }
            }
            return -1;
        }

        bool ArrowAhead()
        {
            if (Cur.Kind == JsTok.Ident && IsAt(1, "=>") && !JsLexer.Reserved.Contains(Cur.Text)) return true;
            if (!Is("(")) return false;
            int m = MatchParen(p);
            if (m < 0 || m + 1 >= toks.Count) return false;
            var after = toks[m + 1];
            return after.Kind == JsTok.Punct && after.Text == "=>";
        }

        JsExpr Arrow()
        {
            int start = StartOf();
            var f = new JsFunctionExpr { IsArrow = true };
            if (Cur.Kind == JsTok.Ident)
            {
                var name = BindingName(out var ns);
                f.Params.Add(new JsParam { Name = name, NameSpan = ns, Span = ns });
            }
            else f.Params = Params();
            if (Cur.NL) throw Unexpected(Cur);
            Expect("=>");
            f.HeaderSpan = From(start);
            if (Is("{"))
            {
                f.Body = FunctionBody();
            }
            else
            {
                int sf = fnDepth, sl = loopDepth, ss = switchDepth;
                fnDepth++; loopDepth = 0; switchDepth = 0;
                try { f.ExprBody = Assignment(false); }
                finally { fnDepth = sf; loopDepth = sl; switchDepth = ss; }
            }
            f.Span = From(start);
            return f;
        }

        JsExpr Assignment(bool noIn)
        {
            if (IsKw("async") && !PeekTok().NL && (PeekTok().Kind == JsTok.Ident || IsAt(1, "(")))
            {
                int save = p;
                Next();
                if (ArrowAhead() || IsKw("function")) throw NotSupported("async functions are", toks[save].Span).Plural();
                p = save;
            }
            if (IsKw("yield")) throw Fail("Unexpected strict mode reserved word", Cur.Span);
            if (ArrowAhead()) return Arrow();
            int start = StartOf();
            var left = Conditional(noIn);
            if (Cur.Kind == JsTok.Punct && IsAssignOp(Cur.Text))
            {
                var opTok = Cur;
                var op = opTok.Text;
                if (op == "&&=" || op == "||=" || op == "??=") throw NotSupported("logical assignment (" + op + ")", opTok.Span);
                if (op == "&=" || op == "|=" || op == "^=" || op == "<<=" || op == ">>=" || op == ">>>=")
                    throw NotSupported("the bitwise operator '" + op.Substring(0, op.Length - 1) + "'", opTok.Span);
                if (op == "=" && left is JsObjectLit && left.Paren.IsNone) throw NotSupported("object destructuring", left.Span);
                if (op == "=" && left is JsArrayLit al && left.Paren.IsNone) left = ToPattern(al);
                else if (!(left is JsIdent || left is JsMember || left is JsIndex))
                    throw Fail("Invalid left-hand side in assignment", left.Span);
                Next();
                var value = Assignment(noIn);
                return new JsAssign { Op = op, Target = left, Value = value, OpSpan = opTok.Span, Span = From(start) };
            }
            return left;
        }

        static JsArrayLit ToPattern(JsArrayLit a)
        {
            foreach (var it in a.Items)
            {
                if (it == null || it is JsIdent || it is JsMember || it is JsIndex) continue;
                if (it is JsSpread sp) throw NotSupported("rest elements (...) in destructuring are", sp.Span).Plural();
                if (it is JsArrayLit inner) { ToPattern(inner); continue; }
                if (it is JsObjectLit) throw NotSupported("object destructuring", it.Span);
                throw Fail("Invalid destructuring assignment target", it.Span);
            }
            return a;
        }

        JsExpr Conditional(bool noIn)
        {
            int start = StartOf();
            var test = Binary(0, noIn);
            if (!Is("?")) return test;
            var q = Next();
            var then = Assignment(false);
            Expect(":");
            var els = Assignment(noIn);
            return new JsCond { Test = test, Then = then, Else = els, QSpan = q.Span, Span = From(start) };
        }

        static int BinPrec(string op)
        {
            switch (op)
            {
                case "??": return 1;
                case "||": return 2;
                case "&&": return 3;
                case "|": return 4;
                case "^": return 5;
                case "&": return 6;
                case "==": case "!=": case "===": case "!==": return 7;
                case "<": case ">": case "<=": case ">=": case "instanceof": case "in": return 8;
                case "<<": case ">>": case ">>>": return 9;
                case "+": case "-": return 10;
                case "*": case "/": case "%": return 11;
                case "**": return 12;
            }
            return -1;
        }

        JsExpr Binary(int minPrec, bool noIn)
        {
            int start = StartOf();
            var left = Unary();
            while (true)
            {
                var t = Cur;
                string op = null;
                if (t.Kind == JsTok.Punct) op = t.Text;
                else if (t.Kind == JsTok.Ident && (t.Text == "instanceof" || (t.Text == "in" && !noIn))) op = t.Text;
                if (op == null) break;
                int prec = BinPrec(op);
                if (prec < 0 || prec <= minPrec) break;
                if (op == "&" || op == "|" || op == "^" || op == "<<" || op == ">>" || op == ">>>")
                    throw NotSupported("the bitwise operator '" + op + "'", t.Span);
                if (op == "instanceof") throw NotSupported("instanceof", t.Span);
                if (op == "**" && left is JsUnary && left.Paren.IsNone)
                    throw Fail("Unary operator used immediately before exponentiation expression. Parenthesis must be used to disambiguate operator precedence", left.Span);
                Next();
                // ** is right-associative
                var right = op == "**" ? Binary(prec - 1, noIn) : Binary(prec, noIn);
                bool leftMix = left is JsBinary lb && lb.Paren.IsNone && Conflicts(lb.Op, op);
                if (leftMix) throw Fail("Unexpected token '" + op + "'", t.Span);
                // V8 reports the operator after the right operand (a ?? b || c: at '||')
                if (right is JsBinary rb && rb.Paren.IsNone && Conflicts(rb.Op, op)) throw Fail("Unexpected token '" + rb.Op + "'", rb.OpSpan);
                left = new JsBinary { Op = op, Left = left, Right = right, OpSpan = t.Span, Span = From(start) };
            }
            return left;
        }

        /// <summary>?? cannot be mixed with || / &amp;&amp; without parentheses.</summary>
        static bool Conflicts(string a, string b) =>
            (a == "??" && (b == "||" || b == "&&")) || (b == "??" && (a == "||" || a == "&&"));

        JsExpr Unary()
        {
            var t = Cur;
            int start = t.Start;
            if (t.Kind == JsTok.Punct)
            {
                switch (t.Text)
                {
                    case "!": case "-": case "+":
                        {
                            Next();
                            var operand = Unary();
                            return new JsUnary { Op = t.Text, Operand = operand, OpSpan = t.Span, Span = From(start) };
                        }
                    case "~": throw NotSupported("the bitwise operator '~'", t.Span);
                    case "++": case "--":
                        {
                            Next();
                            var target = Unary();
                            if (!(target is JsIdent || target is JsMember || target is JsIndex)) throw Fail("Invalid left-hand side expression in prefix operation", target.Span);
                            return new JsUpdate { Op = t.Text, Prefix = true, Target = target, OpSpan = t.Span, Span = From(start) };
                        }
                }
            }
            else if (t.Kind == JsTok.Ident)
            {
                switch (t.Text)
                {
                    case "typeof": throw NotSupported("typeof", t.Span);
                    case "void": throw NotSupported("the void operator", t.Span);
                    case "delete": throw NotSupported("the delete operator", t.Span);
                    case "await": throw NotSupported("await", t.Span);
                }
            }
            var e = Postfix();
            return e;
        }

        JsExpr Postfix()
        {
            int start = StartOf();
            var e = CallMember();
            if ((Is("++") || Is("--")) && !Cur.NL)
            {
                var t = Next();
                if (!(e is JsIdent || e is JsMember || e is JsIndex)) throw Fail("Invalid left-hand side expression in postfix operation", e.Span);
                return new JsUpdate { Op = t.Text, Prefix = false, Target = e, OpSpan = t.Span, Span = From(start) };
            }
            return e;
        }

        List<JsExpr> Arguments(out Span span)
        {
            int start = StartOf();
            Expect("(");
            var args = new List<JsExpr>();
            while (!Is(")"))
            {
                if (Is("..."))
                {
                    int ss = StartOf();
                    Next();
                    var a = Assignment(false);
                    args.Add(new JsSpread { Arg = a, Span = From(ss) });
                }
                else args.Add(Assignment(false));
                if (Is(",")) { Next(); continue; }
                if (!Is(")"))
                {
                    if (AtEnd) throw Unexpected(Cur);
                    throw Fail("missing ) after argument list", Cur.Span);
                }
            }
            Next();
            span = From(start);
            return args;
        }

        JsExpr CallMember()
        {
            int start = StartOf();
            JsExpr e;
            if (IsKw("new"))
            {
                var kw = Next();
                if (Is(".")) throw NotSupported("new.target", new Span(kw.Start, PeekTok().End));
                var callee = MemberOnly();
                var n = new JsNew { Callee = callee, KwSpan = kw.Span };
                if (Is("(")) { n.Args = Arguments(out _); n.HasArgs = true; }
                n.Span = From(start);
                e = n;
            }
            else e = Primary();
            while (true)
            {
                if (Is("."))
                {
                    Next();
                    var nt = Cur;
                    if (nt.Kind != JsTok.Ident) { if (nt.Kind == JsTok.Error && nt.Text.StartsWith("#")) throw NotSupported("private names", nt.Span); throw Unexpected(nt); }
                    Next();
                    e = new JsMember { Object = e, Name = nt.Text, NameSpan = nt.Span, Span = From(start) };
                    continue;
                }
                if (Is("?.")) throw NotSupported("optional chaining (?.)", Cur.Span);
                if (Is("["))
                {
                    Next();
                    var idx = Expression(false);
                    Expect("]");
                    e = new JsIndex { Object = e, Index = idx, Span = From(start) };
                    continue;
                }
                if (Is("("))
                {
                    var c = new JsCall { Callee = e };
                    c.Args = Arguments(out var asp);
                    c.ArgsSpan = asp;
                    c.Span = From(start);
                    e = c;
                    continue;
                }
                if (Cur.Kind == JsTok.Template) throw NotSupported("tagged templates are", Cur.Span).Plural();
                break;
            }
            return e;
        }

        /// <summary>Callee of new: a primary with .name / [i] accesses, no calls.</summary>
        JsExpr MemberOnly()
        {
            int start = StartOf();
            JsExpr e = IsKw("new") ? CallMember() : Primary();
            while (true)
            {
                if (Is("."))
                {
                    Next();
                    var nt = Cur;
                    if (nt.Kind != JsTok.Ident) throw Unexpected(nt);
                    Next();
                    e = new JsMember { Object = e, Name = nt.Text, NameSpan = nt.Span, Span = From(start) };
                    continue;
                }
                if (Is("["))
                {
                    Next();
                    var idx = Expression(false);
                    Expect("]");
                    e = new JsIndex { Object = e, Index = idx, Span = From(start) };
                    continue;
                }
                break;
            }
            return e;
        }

        JsExpr Primary()
        {
            var t = Cur;
            int start = t.Start;
            switch (t.Kind)
            {
                case JsTok.Number:
                    Next();
                    return new JsNum { Value = t.Num, IsFloat = t.IsFloat, Text = t.Text, Span = t.Span };
                case JsTok.String:
                    Next();
                    return new JsStr { Value = t.Str, Span = t.Span };
                case JsTok.Template:
                    return Template();
                case JsTok.BigInt: throw NotSupported("BigInt literals are", t.Span).Plural();
                case JsTok.Regex: throw NotSupported("regular expression literals are", t.Span).Plural();
                case JsTok.Error: throw Fail(t.Error, t.Span);
                case JsTok.Eof: throw Unexpected(t);
                case JsTok.Ident:
                    switch (t.Text)
                    {
                        case "true": case "false": Next(); return new JsBool { Value = t.Text == "true", Span = t.Span };
                        case "null": Next(); return new JsNull { Span = t.Span };
                        case "this": throw NotSupported("this", t.Span);
                        case "super": throw NotSupported("super", t.Span);
                        case "class": throw NotSupported("classes are", t.Span).Plural();
                        case "function": return FunctionExpr();
                        case "import": throw NotSupported("dynamic import()", t.Span);
                        case "async":
                            if (PeekTok().Kind == JsTok.Ident && PeekTok().Text == "function") throw NotSupported("async functions are", t.Span).Plural();
                            break;
                    }
                    if (JsLexer.Reserved.Contains(t.Text) || JsLexer.StrictReserved.Contains(t.Text) || t.Text == "await") throw Unexpected(t);
                    Next();
                    return new JsIdent { Name = t.Text, Span = t.Span };
                case JsTok.Punct:
                    switch (t.Text)
                    {
                        case "(":
                            {
                                Next();
                                if (Is(")")) throw Unexpected(Cur);
                                var e = Expression(false);
                                Expect(")");
                                var ps = From(start);
                                if (e.Paren.IsNone) e.Paren = ps;
                                return e;
                            }
                        case "[": return ArrayLit();
                        case "{": return ObjectLit();
                    }
                    break;
            }
            throw Unexpected(t);
        }

        JsExpr Template()
        {
            var t = Next();
            if (t.Unterminated) throw Fail("Unterminated template literal", t.Span);
            var tl = new JsTemplate { Span = t.Span };
            tl.Parts.AddRange(t.Parts);
            tl.PartSpans.AddRange(t.PartSpans);
            foreach (var sub in t.Exprs)
            {
                var saveToks = toks; int saveP = p, saveEnd = lastEnd;
                toks = sub; p = 0;
                try
                {
                    if (Cur.Kind == JsTok.Eof) throw Unexpected(Cur);
                    var e = Expression(false);
                    if (Cur.Kind != JsTok.Eof) throw Unexpected(Cur);
                    tl.Exprs.Add(e);
                }
                finally { toks = saveToks; p = saveP; lastEnd = saveEnd; }
            }
            return tl;
        }

        JsExpr FunctionExpr()
        {
            int start = StartOf();
            Next();   // function
            if (Is("*")) throw NotSupported("generator functions are", Cur.Span).Plural();
            var f = new JsFunctionExpr();
            if (Cur.Kind == JsTok.Ident) f.Name = BindingName(out _);
            f.Params = Params();
            f.HeaderSpan = From(start);
            f.Body = FunctionBody();
            f.Span = From(start);
            return f;
        }

        JsExpr ArrayLit()
        {
            int start = StartOf();
            Next();
            var a = new JsArrayLit();
            while (!Is("]"))
            {
                if (Is(",")) { a.Items.Add(null); Next(); continue; }
                if (Is("..."))
                {
                    int ss = StartOf();
                    Next();
                    var arg = Assignment(false);
                    a.Items.Add(new JsSpread { Arg = arg, Span = From(ss) });
                }
                else a.Items.Add(Assignment(false));
                if (Is(",")) { Next(); continue; }
                if (!Is("]")) throw Unexpected(Cur);
            }
            Next();
            a.Span = From(start);
            return a;
        }

        JsExpr ObjectLit()
        {
            int start = StartOf();
            Next();
            var o = new JsObjectLit();
            while (!Is("}"))
            {
                var t = Cur;
                int ps = t.Start;
                var prop = new JsProp();
                if (Is("...")) throw NotSupported("object spread (...)", t.Span);
                if (t.Kind == JsTok.Ident && (t.Text == "get" || t.Text == "set") && (PeekTok().Kind == JsTok.Ident || PeekTok().Kind == JsTok.String) && !IsAt(1, ":") && !IsAt(1, ","))
                    throw NotSupported("getters and setters are", t.Span).Plural();
                if (t.Kind == JsTok.Ident && t.Text == "async" && PeekTok().Kind == JsTok.Ident) throw NotSupported("async functions are", t.Span).Plural();
                if (Is("*")) throw NotSupported("generator functions are", t.Span).Plural();
                if (Is("["))
                {
                    Next();
                    prop.KeyExpr = Assignment(false);
                    Expect("]");
                    prop.KeySpan = From(ps);
                }
                else if (t.Kind == JsTok.Ident) { Next(); prop.Key = t.Text; prop.KeySpan = t.Span; }
                else if (t.Kind == JsTok.String) { Next(); prop.Key = t.Str; prop.KeySpan = t.Span; }
                else if (t.Kind == JsTok.Number) { Next(); prop.Key = JsFormat.Number(t.Num); prop.KeySpan = t.Span; }
                else throw Unexpected(t);
                if (Is("(")) throw NotSupported("methods in object literals are", prop.KeySpan).Plural();
                if (Is(":"))
                {
                    Next();
                    prop.Value = Assignment(false);
                }
                else if (t.Kind == JsTok.Ident && prop.KeyExpr == null && (Is(",") || Is("}")))
                {
                    if (JsLexer.Reserved.Contains(t.Text)) throw Unexpected(t);
                    prop.Value = new JsIdent { Name = t.Text, Span = t.Span };
                }
                else throw Unexpected(Cur);
                prop.Span = From(ps);
                o.Props.Add(prop);
                if (Is(",")) { Next(); continue; }
                if (!Is("}")) throw Unexpected(Cur);
            }
            Next();
            o.Span = From(start);
            return o;
        }
    }

    internal static class JsSyntaxErrorExt
    {
        /// <summary>"X are is not supported" → "X are not supported" (NotSupported appends " is not supported").</summary>
        public static JsSyntaxError Plural(this JsSyntaxError e) =>
            new JsSyntaxError(e.Message.Replace(" are is not supported", " are not supported"), e.Span);
    }
}
