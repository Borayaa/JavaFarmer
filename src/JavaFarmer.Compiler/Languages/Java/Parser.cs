using System;
using System.Collections.Generic;
using System.Globalization;

namespace JavaFarmer.Compiler
{
    internal sealed class CompileAbort : Exception
    {
        public readonly Diagnostic Diag;
        public CompileAbort(Diagnostic d) : base(d.Message) { Diag = d; }
    }

    /// <summary>Recursive-descent parser for the JavaFarmer Java subset. Stops at the first syntax error.</summary>
    internal sealed class Parser
    {
        readonly List<Token> toks;
        int p;
        readonly string src;

        static readonly HashSet<string> Primitives = new HashSet<string> { "int", "double", "boolean", "long", "float", "char", "byte", "short" };

        public Parser(string source, List<Token> tokens) { src = source; toks = tokens; }

        public static CompilationUnit Parse(string source, out Diagnostic error)
        {
            error = null;
            var lexer = new Lexer(source);
            var tokens = lexer.LexSignificant();
            if (lexer.Error != null) { error = lexer.Error; return null; }
            var parser = new Parser(source, tokens);
            try { return parser.ParseUnit(); }
            catch (CompileAbort a) { error = a.Diag; return null; }
        }

        // ------------------------------------------------------------ token helpers
        Token Cur => toks[p];
        Token Peek(int n = 1) => toks[Math.Min(p + n, toks.Count - 1)];
        Token Prev => toks[Math.Max(p - 1, 0)];
        bool Is(string text) => (Cur.Kind == Tk.Op || Cur.Kind == Tk.Keyword) && Cur.Text == text;
        bool IsAt(int n, string text) { var t = Peek(n); return (t.Kind == Tk.Op || t.Kind == Tk.Keyword) && t.Text == text; }
        bool IsIdent => Cur.Kind == Tk.Identifier;
        Token Advance() { var t = Cur; if (p < toks.Count - 1) p++; return t; }
        bool Accept(string text) { if (Is(text)) { Advance(); return true; } return false; }

        static CompileAbort Err(string msg, int s, int e) => new CompileAbort(new Diagnostic(msg, s, e));
        CompileAbort ErrAt(string msg, Token t) => t.Kind == Tk.EOF ? Err("reached end of file while parsing", Math.Max(0, t.Start - 1), t.Start) : Err(msg, t.Start, t.End);

        /// <summary>javac reports "'x' expected" right after the previous token.</summary>
        Token Expect(string text)
        {
            if (Is(text)) return Advance();
            if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Math.Max(0, Prev.Start), Prev.End);
            var prev = Prev;
            if (p == 0) throw Err("'" + text + "' expected", Cur.Start, Cur.End);
            // If the current token is on the same line, point at it; otherwise at the end of the previous token.
            if (text == ";" || text == ")" || text == "]" || OnDifferentLine(prev, Cur))
                throw Err("'" + text + "' expected", prev.Start, prev.End);
            throw Err("'" + text + "' expected", Cur.Start, Cur.End);
        }

        bool OnDifferentLine(Token a, Token b)
        {
            for (int i = a.End; i < b.Start && i < src.Length; i++) if (src[i] == '\n') return true;
            return false;
        }

        Token ExpectIdent()
        {
            if (IsIdent) return Advance();
            if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Math.Max(0, Prev.Start), Prev.End);
            if (Cur.Kind == Tk.Keyword) throw Err("<identifier> expected (" + Cur.Text + " is a reserved word)", Cur.Start, Cur.End);
            throw Err("<identifier> expected", Cur.Start, Cur.End);
        }

        static CompileAbort NotSupported(string what, int s, int e) => Err(what + " is not supported in JavaFarmer", s, e);

        // ------------------------------------------------------------ compilation unit
        CompilationUnit ParseUnit()
        {
            var cu = new CompilationUnit { Start = 0, End = src.Length };
            if (Is("package")) throw NotSupported("'package'", Cur.Start, Cur.End);
            while (Is("import")) cu.Imports.Add(ParseImport());

            // Class form?
            int save = p;
            SkipModifiersAndAnnotations(out _);
            if (Is("class"))
            {
                p = save;
                ParseClass(cu);
                if (Cur.Kind != Tk.EOF)
                {
                    if (Is("class") || Is("interface") || Is("enum") || Is("record") || IsModifierToken())
                        throw Err("only one class per file is supported in JavaFarmer", Cur.Start, Cur.End);
                    throw Err("class, interface, enum, or record expected", Cur.Start, Cur.End);
                }
                return cu;
            }
            if (Is("interface") || Is("enum") || Is("record"))
                throw NotSupported("'" + Cur.Text + "'", Cur.Start, Cur.End);
            p = save;
            while (Cur.Kind != Tk.EOF)
            {
                if (Is("import")) throw Err("imports must come before the code", Cur.Start, Cur.End);
                ParseMember(cu, false);
            }
            return cu;
        }

        bool IsModifierToken()
        {
            if (Cur.Kind != Tk.Keyword) return false;
            switch (Cur.Text)
            {
                case "public": case "private": case "protected": case "static": case "final": case "abstract":
                case "native": case "synchronized": case "transient": case "volatile": case "strictfp":
                    return true;
            }
            return false;
        }

        void SkipModifiersAndAnnotations(out Mods mods)
        {
            mods = Mods.None;
            while (true)
            {
                if (Is("@") && Peek().Kind == Tk.Identifier)
                {
                    Advance(); Advance();
                    while (Is(".") && Peek().Kind == Tk.Identifier) { Advance(); Advance(); }
                    if (Is("(")) SkipBalanced("(", ")");
                    continue;
                }
                if (!IsModifierToken()) break;
                switch (Cur.Text)
                {
                    case "public": mods |= Mods.Public; break;
                    case "private": mods |= Mods.Private; break;
                    case "protected": mods |= Mods.Protected; break;
                    case "static": mods |= Mods.Static; break;
                    case "final": mods |= Mods.Final; break;
                    default: mods |= Mods.Other; break;
                }
                Advance();
            }
        }

        void SkipBalanced(string open, string close)
        {
            int depth = 0;
            do
            {
                if (Is(open)) depth++;
                else if (Is(close)) depth--;
                if (Cur.Kind == Tk.EOF) throw ErrAt("", Cur);
                Advance();
            } while (depth > 0);
        }

        Mods ParseModifiers(out int start)
        {
            start = Cur.Start;
            var mods = Mods.None;
            while (true)
            {
                if (Is("@") && Peek().Kind == Tk.Identifier)
                {
                    Advance(); Advance();
                    while (Is(".") && Peek().Kind == Tk.Identifier) { Advance(); Advance(); }
                    if (Is("(")) SkipBalanced("(", ")");
                    continue;
                }
                if (!IsModifierToken()) break;
                var t = Cur;
                switch (t.Text)
                {
                    case "public": mods |= Mods.Public; break;
                    case "private": mods |= Mods.Private; break;
                    case "protected": mods |= Mods.Protected; break;
                    case "static": mods |= Mods.Static; break;
                    case "final": mods |= Mods.Final; break;
                    default: throw NotSupported("the modifier '" + t.Text + "'", t.Start, t.End);
                }
                Advance();
            }
            return mods;
        }

        ImportDecl ParseImport()
        {
            var kw = Expect("import");
            var d = new ImportDecl { Start = kw.Start };
            if (Accept("static")) d.IsStatic = true;
            var name = ExpectIdent().Text;
            while (Accept("."))
            {
                if (Accept("*")) { name += ".*"; break; }
                name += "." + ExpectIdent().Text;
            }
            d.Name = name;
            Expect(";");
            d.End = Prev.End;
            return d;
        }

        void ParseClass(CompilationUnit cu)
        {
            var mods = ParseModifiers(out _);
            if ((mods & (Mods.Static | Mods.Private | Mods.Protected)) != 0)
                throw Err("modifier not allowed here", cu.Start, Cur.End);
            Expect("class");
            var name = ExpectIdent();
            cu.IsClass = true;
            cu.ClassName = name.Text;
            cu.ClassNameStart = name.Start; cu.ClassNameEnd = name.End;
            if (Is("<")) throw NotSupported("generic classes", Cur.Start, Cur.End);
            if (Is("extends") || Is("implements")) throw NotSupported("'" + Cur.Text + "'", Cur.Start, Cur.End);
            Expect("{");
            while (!Is("}"))
            {
                if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Prev.Start, Prev.End);
                ParseMember(cu, true);
            }
            Expect("}");
        }

        void ParseMember(CompilationUnit cu, bool inClass)
        {
            if (Accept(";")) return;
            int start = Cur.Start;
            if (Is("{") || (Is("static") && IsAt(1, "{")))
                throw NotSupported("initializer blocks", Cur.Start, Cur.End);
            var mods = ParseModifiers(out _);
            if (Is("class") || Is("interface") || Is("enum") || Is("record"))
                throw NotSupported(inClass ? "nested " + Cur.Text + "es" : "'" + Cur.Text + "' here", Cur.Start, Cur.End);
            if (Is("<")) throw NotSupported("generic methods", Cur.Start, Cur.End);
            if (inClass && IsIdent && Cur.Text == cu.ClassName && IsAt(1, "("))
                throw NotSupported("constructors", Cur.Start, Cur.End);
            if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Prev.Start, Prev.End);

            TypeRef type;
            if (Is("void"))
            {
                var v = Advance();
                type = new TypeRef { Name = "void", Start = v.Start, End = v.End, NameStart = v.Start, NameEnd = v.End };
            }
            else
            {
                if (!(IsIdent || IsPrimitiveKw(Cur)))
                {
                    if (Cur.Kind == Tk.Keyword || Cur.Kind == Tk.Op)
                    {
                        if (!inClass && IsStatementStart())
                            throw Err("statements must be inside a method (write them inside void main() { ... })", Cur.Start, Cur.End);
                        throw Err(inClass ? "illegal start of type" : "class, interface, enum, or record expected", Cur.Start, Cur.End);
                    }
                    throw ErrAt("<identifier> expected", Cur);
                }
                if (IsIdent && IsAt(1, "(") && !inClass)
                {
                    // e.g. "harvest();" at top level, or "main() {"
                    int save = p;
                    Advance(); SkipBalanced("(", ")");
                    bool isBody = Is("{");
                    p = save;
                    if (isBody) throw Err("invalid method declaration; return type required", Cur.Start, Cur.End);
                    throw Err("statements must be inside a method (write them inside void main() { ... })", Cur.Start, Cur.End);
                }
                if (IsIdent && (IsAt(1, "=") || IsAt(1, "++") || IsAt(1, "--") || IsAt(1, "+=") || IsAt(1, "-=") || IsAt(1, ".")) && !inClass)
                    throw Err("statements must be inside a method (write them inside void main() { ... })", Cur.Start, Cur.End);
                type = ParseType();
            }
            var name = ExpectIdent();
            if (Is("("))
            {
                var m = new MethodDecl { Start = start, Mods = mods, ReturnType = type, Name = name.Text, NameStart = name.Start, NameEnd = name.End, HeaderStart = start };
                Advance();
                if (!Is(")"))
                {
                    do
                    {
                        int ps = Cur.Start;
                        var pm = ParseModifiers(out _);
                        if ((pm & ~Mods.Final) != 0) throw Err("modifier not allowed here", ps, Prev.End);
                        var pt = ParseType();
                        if (Is("...")) throw NotSupported("varargs", Cur.Start, Cur.End);
                        var pn = ExpectIdent();
                        while (Is("[")) { Advance(); Expect("]"); pt.Dims++; }
                        m.Params.Add(new Param { Start = ps, End = pn.End, Type = pt, Name = pn.Text, NameStart = pn.Start, NameEnd = pn.End, IsFinal = (pm & Mods.Final) != 0 });
                    } while (Accept(","));
                }
                Expect(")");
                m.HeaderEnd = Prev.End;
                if (Is("throws")) throw NotSupported("'throws'", Cur.Start, Cur.End);
                if (Is(";")) throw Err("missing method body, or declare abstract", name.Start, name.End);
                if (!Is("{")) throw Err("'{' expected", Prev.Start, Prev.End);
                m.Body = ParseBlock();
                m.End = Prev.End;
                cu.Methods.Add(m);
                return;
            }
            if (type.Name == "void") throw Err("'(' expected", name.Start, name.End);
            var f = new FieldDecl { Start = start, Mods = mods, ModsStart = start, Type = type };
            f.Vars.Add(ParseDeclaratorRest(name));
            while (Accept(",")) f.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            Expect(";");
            f.End = Prev.End;
            cu.Fields.Add(f);
        }

        bool IsStatementStart()
        {
            switch (Cur.Text)
            {
                case "if": case "while": case "for": case "do": case "return": case "switch": case "break": case "continue": case "{":
                    return true;
            }
            return false;
        }

        VarDeclarator ParseDeclaratorRest(Token name)
        {
            var d = new VarDeclarator { Start = name.Start, Name = name.Text, NameStart = name.Start, NameEnd = name.End };
            while (Is("[")) { Advance(); Expect("]"); d.ExtraDims++; }
            if (Accept("="))
            {
                d.Init = Is("{") ? ParseArrayInit() : ParseExpr();
            }
            d.End = Prev.End;
            return d;
        }

        // ------------------------------------------------------------ types
        static bool IsPrimitiveKw(Token t) => t.Kind == Tk.Keyword && Primitives.Contains(t.Text);

        TypeRef ParseType()
        {
            var t = TryParseType(out var err);
            if (t == null) throw err ?? ErrAt("<identifier> expected", Cur);
            return t;
        }

        /// <summary>Parses a type; returns null (and restores nothing) on failure.</summary>
        TypeRef TryParseType(out CompileAbort err)
        {
            err = null;
            var start = Cur;
            TypeRef tr;
            if (IsPrimitiveKw(Cur))
            {
                Advance();
                tr = new TypeRef { Name = start.Text, Start = start.Start, NameStart = start.Start, NameEnd = start.End };
            }
            else if (Cur.Kind == Tk.Identifier || (Cur.Kind == Tk.Keyword && Cur.Text == "var"))
            {
                var nameTok = Advance();
                string name = nameTok.Text;
                int ns = nameTok.Start, ne = nameTok.End;
                // qualified name: java.util.List
                while (Is(".") && Peek().Kind == Tk.Identifier)
                {
                    Advance();
                    var part = Advance();
                    name = part.Text; ns = part.Start; ne = part.End;
                }
                tr = new TypeRef { Name = name, Start = start.Start, NameStart = ns, NameEnd = ne };
                if (Is("<"))
                {
                    Advance();
                    if (Is(">")) { Advance(); tr.Diamond = true; }
                    else
                    {
                        tr.Args = new List<TypeRef>();
                        do
                        {
                            if (Is("?")) { err = NotSupported("wildcard types", Cur.Start, Cur.End); return null; }
                            var a = TryParseType(out err);
                            if (a == null) return null;
                            tr.Args.Add(a);
                        } while (Accept(","));
                        if (!Accept(">")) { err = Err("'>' expected", Prev.Start, Prev.End); return null; }
                    }
                }
            }
            else
            {
                err = ErrAt("<identifier> expected", Cur);
                return null;
            }
            while (Is("[") && IsAt(1, "]")) { Advance(); Advance(); tr.Dims++; }
            tr.End = Prev.End;
            return tr;
        }

        // ------------------------------------------------------------ statements
        BlockStmt ParseBlock()
        {
            var open = Expect("{");
            var b = new BlockStmt { Start = open.Start };
            while (!Is("}"))
            {
                if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Prev.Start, Prev.End);
                b.Stmts.Add(ParseStatement(true));
            }
            Advance();
            b.End = Prev.End;
            return b;
        }

        Stmt ParseStatement(bool allowDecl)
        {
            var t = Cur;
            if (t.Kind == Tk.Op && t.Text == "{") return ParseBlock();
            if (t.Kind == Tk.Op && t.Text == ";") { Advance(); return new EmptyStmt { Start = t.Start, End = t.End }; }
            if (t.Kind == Tk.Keyword)
            {
                switch (t.Text)
                {
                    case "if": return ParseIf();
                    case "while":
                        {
                            Advance();
                            var w = new WhileStmt { Start = t.Start, KwEnd = t.End };
                            w.Cond = ParseParenCond();
                            w.Body = ParseStatement(false);
                            w.End = w.Body.End;
                            return w;
                        }
                    case "do":
                        {
                            Advance();
                            var d = new DoWhileStmt { Start = t.Start };
                            d.Body = ParseStatement(false);
                            var wt = Expect("while");
                            d.WhileStart = wt.Start; d.WhileEnd = wt.End;
                            d.Cond = ParseParenCond();
                            Expect(";");
                            d.End = Prev.End;
                            return d;
                        }
                    case "for": return ParseFor();
                    case "switch": return ParseSwitch();
                    case "return":
                        {
                            Advance();
                            var r = new ReturnStmt { Start = t.Start };
                            if (!Is(";")) r.Value = ParseExpr();
                            Expect(";");
                            r.End = Prev.End;
                            return r;
                        }
                    case "break":
                        Advance();
                        if (IsIdent) throw NotSupported("labeled break", Cur.Start, Cur.End);
                        Expect(";");
                        return new BreakStmt { Start = t.Start, End = Prev.End };
                    case "continue":
                        Advance();
                        if (IsIdent) throw NotSupported("labeled continue", Cur.Start, Cur.End);
                        Expect(";");
                        return new ContinueStmt { Start = t.Start, End = Prev.End };
                    case "try": case "catch": case "finally": throw NotSupported("try/catch", t.Start, t.End);
                    case "throw": throw NotSupported("'throw'", t.Start, t.End);
                    case "assert": throw NotSupported("'assert'", t.Start, t.End);
                    case "synchronized": throw NotSupported("'synchronized'", t.Start, t.End);
                    case "class": case "interface": case "enum": case "record": throw NotSupported("local " + t.Text + "es", t.Start, t.End);
                    case "else": throw Err("'else' without 'if'", t.Start, t.End);
                    case "case": case "default": throw Err("orphaned " + t.Text, t.Start, t.End);
                    case "yield": throw NotSupported("'yield'", t.Start, t.End);
                    case "final":
                        {
                            if (!allowDecl) throw Err("variable declaration not allowed here", t.Start, t.End);
                            Advance();
                            var lv = ParseLocalVarDecl(t.Start, true);
                            return lv;
                        }
                    case "static": case "public": case "private": case "protected":
                        throw Err("illegal start of expression", t.Start, t.End);
                }
            }
            if (t.Kind == Tk.Identifier && IsAt(1, ":") ) throw NotSupported("labels", t.Start, t.End);

            if (IsLocalVarDeclStart())
            {
                if (!allowDecl) throw Err("variable declaration not allowed here", t.Start, t.End);
                return ParseLocalVarDecl(t.Start, false);
            }

            var e = ParseExpr();
            if (!(e is AssignExpr || e is IncDecExpr || e is CallExpr || e is NewExpr))
            {
                if (Is(";") || Cur.Kind == Tk.EOF) throw Err("not a statement", e.Start, e.End);
                if (e is NameExpr && IsIdent) throw ErrAt("';' expected", Prev);
            }
            Expect(";");
            if (!(e is AssignExpr || e is IncDecExpr || e is CallExpr || e is NewExpr))
                throw Err("not a statement", e.Start, e.End);
            return new ExprStmt { Start = e.Start, End = Prev.End, E = e };
        }

        bool IsLocalVarDeclStart()
        {
            if (Cur.Kind == Tk.Keyword && Cur.Text == "var" && Peek().Kind == Tk.Identifier) return true;
            if (IsPrimitiveKw(Cur) && !IsAt(1, ".")) return true;   // "int x", also "int class" (reports <identifier> expected)
            if (!IsIdent) return false;
            int save = p;
            var tr = TryParseType(out _);
            bool ok = tr != null && IsIdent;
            p = save;
            return ok;
        }

        LocalVarStmt ParseLocalVarDecl(int start, bool isFinal)
        {
            var lv = new LocalVarStmt { Start = start, IsFinal = isFinal };
            lv.Type = ParseType();
            lv.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            while (Accept(",")) lv.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            Expect(";");
            lv.End = Prev.End;
            return lv;
        }

        Expr ParseParenCond()
        {
            Expect("(");
            var e = ParseExpr();
            Expect(")");
            return e;
        }

        Stmt ParseIf()
        {
            var kw = Advance();
            var s = new IfStmt { Start = kw.Start, KwEnd = kw.End };
            s.Cond = ParseParenCond();
            s.Then = ParseStatement(false);
            if (Accept("else")) s.Else = ParseStatement(false);
            s.End = Prev.End;
            return s;
        }

        Stmt ParseFor()
        {
            var kw = Advance();
            Expect("(");
            // enhanced for?
            int save = p;
            bool isFinal = false;
            if (Is("final")) { Advance(); isFinal = true; }
            if (IsLocalVarDeclStart())
            {
                var tr = ParseType();
                var nameTok = ExpectIdent();
                if (Is(":"))
                {
                    Advance();
                    var fe = new ForEachStmt { Start = kw.Start, KwEnd = kw.End, VarType = tr, IsFinal = isFinal, Name = nameTok.Text, NameStart = nameTok.Start, NameEnd = nameTok.End };
                    fe.Iterable = ParseExpr();
                    Expect(")");
                    fe.Body = ParseStatement(false);
                    fe.End = fe.Body.End;
                    return fe;
                }
            }
            p = save;
            var f = new ForStmt { Start = kw.Start, KwEnd = kw.End };
            if (!Is(";"))
            {
                int s0 = Cur.Start;
                if (Is("final")) { Advance(); f.Init.Add(ParseLocalVarDeclNoSemi(s0, true)); }
                else if (IsLocalVarDeclStart()) f.Init.Add(ParseLocalVarDeclNoSemi(s0, false));
                else
                {
                    do
                    {
                        var e = ParseExpr();
                        if (!(e is AssignExpr || e is IncDecExpr || e is CallExpr)) throw Err("not a statement", e.Start, e.End);
                        f.Init.Add(new ExprStmt { Start = e.Start, End = e.End, E = e });
                    } while (Accept(","));
                }
            }
            Expect(";");
            if (!Is(";")) f.Cond = ParseExpr();
            Expect(";");
            if (!Is(")"))
            {
                do
                {
                    var e = ParseExpr();
                    if (!(e is AssignExpr || e is IncDecExpr || e is CallExpr)) throw Err("not a statement", e.Start, e.End);
                    f.Update.Add(e);
                } while (Accept(","));
            }
            Expect(")");
            f.HeaderEnd = Prev.End;
            f.Body = ParseStatement(false);
            f.End = f.Body.End;
            return f;
        }

        LocalVarStmt ParseLocalVarDeclNoSemi(int start, bool isFinal)
        {
            var lv = new LocalVarStmt { Start = start, IsFinal = isFinal };
            lv.Type = ParseType();
            lv.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            while (Accept(",")) lv.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            lv.End = Prev.End;
            return lv;
        }

        Stmt ParseSwitch()
        {
            var kw = Advance();
            var sw = new SwitchStmt { Start = kw.Start, KwEnd = kw.End };
            sw.Selector = ParseParenCond();
            Expect("{");
            bool? arrow = null;
            while (!Is("}"))
            {
                if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Prev.Start, Prev.End);
                var c = new SwitchCase { Start = Cur.Start };
                if (Accept("default")) c.IsDefault = true;
                else if (Accept("case"))
                {
                    do
                    {
                        if (Is("default")) { Advance(); c.IsDefault = true; continue; }
                        if (IsIdent && (IsAt(1, "(") && false)) { }
                        c.Labels.Add(ParseTernary());
                    } while (Accept(","));
                }
                else throw Err("case, default, or '}' expected", Cur.Start, Cur.End);

                bool isArrow;
                if (Is("->")) { Advance(); isArrow = true; }
                else if (Is(":")) { Advance(); isArrow = false; }
                else throw Err("':' or '->' expected", Prev.Start, Prev.End);
                c.LabelEnd = Prev.End;
                if (arrow.HasValue && arrow.Value != isArrow)
                    throw Err("different case kinds used in the switch", c.Start, c.LabelEnd);
                arrow = isArrow;

                if (isArrow)
                {
                    if (Is("{")) c.Body.Add(ParseBlock());
                    else if (Is("throw")) throw NotSupported("'throw'", Cur.Start, Cur.End);
                    else
                    {
                        var e = ParseExpr();
                        Expect(";");
                        if (!(e is AssignExpr || e is IncDecExpr || e is CallExpr || e is NewExpr)) throw Err("not a statement", e.Start, e.End);
                        c.Body.Add(new ExprStmt { Start = e.Start, End = Prev.End, E = e });
                    }
                }
                else
                {
                    while (!Is("case") && !Is("default") && !Is("}"))
                    {
                        if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Prev.Start, Prev.End);
                        c.Body.Add(ParseStatement(true));
                    }
                }
                c.End = Prev.End;
                sw.Cases.Add(c);
            }
            Advance();
            sw.Arrow = arrow ?? false;
            sw.End = Prev.End;
            return sw;
        }

        // ------------------------------------------------------------ expressions
        public Expr ParseExpr()
        {
            var lhs = ParseTernary();
            if (Cur.Kind == Tk.Op)
            {
                switch (Cur.Text)
                {
                    case "=": case "+=": case "-=": case "*=": case "/=": case "%=":
                        {
                            var op = Advance();
                            if (!(lhs is NameExpr || lhs is FieldAccessExpr || lhs is IndexExpr || (lhs is ParenExpr)))
                                throw Err("unexpected type\n  required: variable\n  found:    value", lhs.Start, lhs.End);
                            var rhs = Is("{") ? throw Err("illegal start of expression", Cur.Start, Cur.End) : ParseExpr();
                            return new AssignExpr { Op = op.Text, Target = lhs, Value = rhs, Start = lhs.Start, End = rhs.End, OpStart = op.Start, OpEnd = op.End };
                        }
                    case "&=": case "|=": case "^=": case "<<=":
                        throw NotSupported("the operator '" + Cur.Text + "'", Cur.Start, Cur.End);
                }
            }
            return lhs;
        }

        Expr ParseTernary()
        {
            var c = ParseBinary(0);
            if (Is("?"))
            {
                var q = Advance();
                var a = ParseTernaryBranch();
                Expect(":");
                var b = ParseTernaryBranch();
                return new ConditionalExpr { Cond = c, Then = a, Else = b, Start = c.Start, End = b.End, QStart = q.Start };
            }
            return c;
        }

        Expr ParseTernaryBranch()
        {
            if (IsLambdaStart()) return ParseLambda();
            return ParseTernary();
        }

        static readonly string[][] Levels =
        {
            new[] { "||" },
            new[] { "&&" },
            new[] { "|" },
            new[] { "^" },
            new[] { "&" },
            new[] { "==", "!=" },
            new[] { "<", ">", "<=", ">=" },
            new[] { "<<" },
            new[] { "+", "-" },
            new[] { "*", "/", "%" },
        };

        Expr ParseBinary(int level)
        {
            if (level >= Levels.Length) return ParseUnary();
            var left = ParseBinary(level + 1);
            while (true)
            {
                if (level == 6 && Is("instanceof")) throw NotSupported("'instanceof'", Cur.Start, Cur.End);
                if (Cur.Kind != Tk.Op || Array.IndexOf(Levels[level], Cur.Text) < 0) break;
                var op = Cur;
                // ">>" / ">>>" shift operators arrive as adjacent '>' tokens
                if (op.Text == ">" && Peek().Kind == Tk.Op && Peek().Text.StartsWith(">") && Peek().Start == op.End)
                    throw NotSupported("shift operators", op.Start, Peek().End);
                if (op.Text == "|" || op.Text == "&" || op.Text == "^" || op.Text == "<<")
                    throw NotSupported("the bitwise operator '" + op.Text + "'", op.Start, op.End);
                Advance();
                var right = ParseBinary(level + 1);
                left = new BinaryExpr { Op = op.Text, Left = left, Right = right, Start = left.Start, End = right.End, OpStart = op.Start, OpEnd = op.End };
            }
            return left;
        }

        Expr ParseUnary()
        {
            var t = Cur;
            if (t.Kind == Tk.Op)
            {
                switch (t.Text)
                {
                    case "+": case "-":
                        {
                            Advance();
                            // fold "-123" into a literal so that int range checks work like javac
                            var operand = ParseUnary();
                            return new UnaryExpr { Op = t.Text, Operand = operand, Start = t.Start, End = operand.End, OpEnd = t.End };
                        }
                    case "!":
                        {
                            Advance();
                            var operand = ParseUnary();
                            return new UnaryExpr { Op = "!", Operand = operand, Start = t.Start, End = operand.End, OpEnd = t.End };
                        }
                    case "~": throw NotSupported("the bitwise operator '~'", t.Start, t.End);
                    case "++": case "--":
                        {
                            Advance();
                            var operand = ParseUnary();
                            return new IncDecExpr { Op = t.Text, Prefix = true, Target = operand, Start = t.Start, End = operand.End, OpStart = t.Start, OpEnd = t.End };
                        }
                    case "(":
                        {
                            var cast = TryParseCast();
                            if (cast != null) return cast;
                            break;
                        }
                }
            }
            return ParsePostfix(ParsePrimary());
        }

        Expr TryParseCast()
        {
            if (IsLambdaStart()) return null;
            int save = p;
            var open = Advance();
            if (IsPrimitiveKw(Cur))
            {
                var tr = TryParseType(out _);
                if (tr != null && Is(")"))
                {
                    Advance();
                    var operand = ParseUnary();
                    return new CastExpr { TypeRef = tr, Operand = operand, Start = open.Start, End = operand.End };
                }
                p = save; return null;
            }
            if (IsIdent)
            {
                var tr = TryParseType(out _);
                if (tr != null && Is(")"))
                {
                    var next = Peek();
                    bool castFollows = next.Kind == Tk.Identifier || next.Kind == Tk.StringLit || next.Kind == Tk.IntLit || next.Kind == Tk.DoubleLit
                        || (next.Kind == Tk.Op && (next.Text == "(" || next.Text == "!"))
                        || (next.Kind == Tk.Keyword && (next.Text == "this" || next.Text == "new" || next.Text == "true" || next.Text == "false" || next.Text == "null"));
                    if (castFollows)
                    {
                        Advance();
                        var operand = IsLambdaStart() ? ParseLambda() : ParseUnary();
                        return new CastExpr { TypeRef = tr, Operand = operand, Start = open.Start, End = operand.End };
                    }
                }
            }
            p = save;
            return null;
        }

        bool IsLambdaStart()
        {
            if (IsIdent && IsAt(1, "->")) return true;
            if (!Is("(")) return false;
            int i = 1;
            if (IsAt(1, ")")) return IsAt(2, "->");
            while (true)
            {
                var t = Peek(i);
                if (t.Kind != Tk.Identifier) return false;
                i++;
                if (IsAt(i, ")")) return IsAt(i + 1, "->");
                if (!IsAt(i, ",")) return false;
                i++;
            }
        }

        Expr ParseLambda()
        {
            var lam = new LambdaExpr { Start = Cur.Start };
            if (IsIdent) lam.ParamNames.Add(Advance().Text);
            else
            {
                Expect("(");
                if (!Is(")"))
                {
                    do lam.ParamNames.Add(ExpectIdent().Text); while (Accept(","));
                }
                Expect(")");
            }
            Expect("->");
            lam.ArrowEnd = Prev.End;
            if (Is("{")) lam.BodyBlock = ParseBlock();
            else lam.BodyExpr = ParseExpr();
            lam.End = Prev.End;
            return lam;
        }

        Expr ParsePrimary()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case Tk.IntLit: Advance(); return IntLiteral(t);
                case Tk.DoubleLit: Advance(); return DoubleLiteral(t);
                case Tk.StringLit: Advance(); return new LiteralExpr { Kind = LitKind.String, StrValue = t.StrValue, Start = t.Start, End = t.End };
                case Tk.CharLit: throw Err("char literals are not supported in JavaFarmer; use a String like \"a\"", t.Start, t.End);
                case Tk.Identifier:
                    {
                        if (IsAt(1, "->")) return ParseLambda();
                        Advance();
                        if (Is("("))
                            return ParseCallRest(null, t);
                        return new NameExpr { Name = t.Text, Start = t.Start, End = t.End };
                    }
                case Tk.Keyword:
                    switch (t.Text)
                    {
                        case "true": case "false":
                            Advance(); return new LiteralExpr { Kind = LitKind.Bool, BoolValue = t.Text == "true", Start = t.Start, End = t.End };
                        case "null":
                            Advance(); return new LiteralExpr { Kind = LitKind.Null, Start = t.Start, End = t.End };
                        case "this":
                            Advance();
                            if (Is("(")) throw NotSupported("constructors", t.Start, t.End);
                            return new ThisExpr { Start = t.Start, End = t.End };
                        case "new": return ParseNew();
                        case "super": throw NotSupported("'super'", t.Start, t.End);
                        case "switch": throw NotSupported("switch expressions", t.Start, t.End);
                        case "int": case "double": case "boolean": case "long": case "float": case "char": case "byte": case "short": case "void":
                            if (IsAt(1, ".") && Peek(2).Text == "class") throw NotSupported("class literals", t.Start, Peek(2).End);
                            throw Err("illegal start of expression", t.Start, t.End);
                        case "instanceof": throw NotSupported("'instanceof'", t.Start, t.End);
                    }
                    throw Err("illegal start of expression", t.Start, t.End);
                case Tk.Op:
                    if (t.Text == "(")
                    {
                        if (IsLambdaStart()) return ParseLambda();
                        Advance();
                        var inner = ParseExpr();
                        Expect(")");
                        return new ParenExpr { Inner = inner, Start = t.Start, End = Prev.End };
                    }
                    if (t.Text == "{") throw Err("illegal start of expression (array initializers {...} are only allowed in declarations; use new int[]{...})", t.Start, t.End);
                    throw Err("illegal start of expression", t.Start, t.End);
                case Tk.EOF:
                    throw Err("reached end of file while parsing", Prev.Start, Prev.End);
            }
            throw Err("illegal start of expression", t.Start, t.End);
        }

        LiteralExpr IntLiteral(Token t)
        {
            string s = t.Text.Replace("_", "");
            if (s.EndsWith("l") || s.EndsWith("L")) throw Err("long is not supported in JavaFarmer; use int or double", t.Start, t.End);
            long v;
            bool ok;
            if (s.StartsWith("0x") || s.StartsWith("0X")) ok = long.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
            else if (s.StartsWith("0b") || s.StartsWith("0B"))
            {
                v = 0; ok = s.Length > 2;
                foreach (var ch in s.Substring(2)) { if (ch != '0' && ch != '1') { ok = false; break; } v = v * 2 + (ch - '0'); if (v > uint.MaxValue) { ok = false; break; } }
            }
            else if (s.Length > 1 && s[0] == '0')
            {
                v = 0; ok = true;
                foreach (var ch in s.Substring(1)) { if (ch < '0' || ch > '7') { ok = false; break; } v = v * 8 + (ch - '0'); if (v > uint.MaxValue) { ok = false; break; } }
            }
            else ok = long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
            if (!ok)
            {
                foreach (var ch in s) if (char.IsLetter(ch) && !(s.StartsWith("0x") || s.StartsWith("0X"))) throw Err("malformed number: " + t.Text, t.Start, t.End);
                throw Err("integer number too large", t.Start, t.End);
            }
            if (v > 2147483648L) throw Err("integer number too large", t.Start, t.End);
            return new LiteralExpr { Kind = LitKind.Int, IntValue = v, Start = t.Start, End = t.End };
        }

        LiteralExpr DoubleLiteral(Token t)
        {
            string s = t.Text.Replace("_", "");
            if (s.EndsWith("f") || s.EndsWith("F")) throw Err("float is not supported in JavaFarmer; use double", t.Start, t.End);
            if (s.EndsWith("d") || s.EndsWith("D")) s = s.Substring(0, s.Length - 1);
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw Err("malformed floating-point literal", t.Start, t.End);
            return new LiteralExpr { Kind = LitKind.Double, DoubleValue = v, Start = t.Start, End = t.End };
        }

        CallExpr ParseCallRest(Expr target, Token name)
        {
            var lp = Expect("(");
            var call = new CallExpr { Target = target, Name = name.Text, NameStart = name.Start, NameEnd = name.End, Start = target?.Start ?? name.Start, LParen = lp.Start };
            if (!Is(")"))
            {
                do call.Args.Add(IsLambdaStart() ? ParseLambda() : ParseExpr()); while (Accept(","));
            }
            if (!Is(")"))
            {
                if (Cur.Kind == Tk.EOF) throw Err("reached end of file while parsing", Prev.Start, Prev.End);
                throw Err("')' expected", Prev.Start, Prev.End);
            }
            Advance();
            call.End = Prev.End;
            return call;
        }

        Expr ParsePostfix(Expr e)
        {
            while (true)
            {
                if (Is("."))
                {
                    Advance();
                    if (Is("<")) throw NotSupported("explicit generic method calls", Cur.Start, Cur.End);
                    if (Is("class")) throw NotSupported("class literals", Cur.Start, Cur.End);
                    if (Is("new")) throw NotSupported("inner class creation", Cur.Start, Cur.End);
                    var name = ExpectIdent();
                    if (Is("(")) e = ParseCallRest(e, name);
                    else e = new FieldAccessExpr { Target = e, Name = name.Text, NameStart = name.Start, NameEnd = name.End, Start = e.Start, End = name.End };
                    continue;
                }
                if (Is("["))
                {
                    Advance();
                    var idx = ParseExpr();
                    Expect("]");
                    e = new IndexExpr { Target = e, Index = idx, Start = e.Start, End = Prev.End };
                    continue;
                }
                if (Is("::"))
                {
                    Advance();
                    if (Is("new")) throw NotSupported("constructor references", Cur.Start, Cur.End);
                    var name = ExpectIdent();
                    e = new MethodRefExpr { Qualifier = e, Name = name.Text, NameStart = name.Start, NameEnd = name.End, Start = e.Start, End = name.End };
                    continue;
                }
                if (Is("++") || Is("--"))
                {
                    var op = Advance();
                    e = new IncDecExpr { Op = op.Text, Prefix = false, Target = e, Start = e.Start, End = op.End, OpStart = op.Start, OpEnd = op.End };
                    continue;
                }
                return e;
            }
        }

        Expr ParseNew()
        {
            var kw = Advance();
            if (!(IsIdent || IsPrimitiveKw(Cur))) throw ErrAt("<identifier> expected", Cur);
            var nameTok = Cur;
            var tr = new TypeRef { Start = Cur.Start };
            if (IsPrimitiveKw(Cur)) { Advance(); tr.Name = nameTok.Text; tr.NameStart = nameTok.Start; tr.NameEnd = nameTok.End; }
            else
            {
                var n = Advance();
                tr.Name = n.Text; tr.NameStart = n.Start; tr.NameEnd = n.End;
                while (Is(".") && Peek().Kind == Tk.Identifier) { Advance(); var part = Advance(); tr.Name = part.Text; tr.NameStart = part.Start; tr.NameEnd = part.End; }
                if (Is("<"))
                {
                    Advance();
                    if (Is(">")) { Advance(); tr.Diamond = true; }
                    else
                    {
                        tr.Args = new List<TypeRef>();
                        do
                        {
                            if (Is("?")) throw NotSupported("wildcard types", Cur.Start, Cur.End);
                            tr.Args.Add(ParseType());
                        } while (Accept(","));
                        Expect(">");
                    }
                }
            }
            tr.End = Prev.End;
            if (Is("["))
            {
                var na = new NewArrayExpr { Start = kw.Start, Elem = tr };
                while (Is("["))
                {
                    Advance();
                    if (Is("]"))
                    {
                        Advance(); na.TotalDims++;
                        continue;
                    }
                    if (na.TotalDims > na.DimExprs.Count) throw Err("']' expected", Prev.Start, Prev.End);
                    na.DimExprs.Add(ParseExpr());
                    Expect("]");
                    na.TotalDims++;
                }
                if (Is("{"))
                {
                    if (na.DimExprs.Count > 0) throw Err("array creation with both dimension expression and initialization is illegal", kw.Start, Cur.End);
                    na.Init = ParseArrayInit();
                }
                else if (na.DimExprs.Count == 0) throw Err("array dimension missing", kw.Start, Prev.End);
                na.End = Prev.End;
                return na;
            }
            if (!Is("(")) throw Err("'(' or '[' expected", Prev.Start, Prev.End);
            Advance();
            var ne = new NewExpr { Start = kw.Start, TypeRef = tr };
            if (!Is(")"))
            {
                do ne.Args.Add(ParseExpr()); while (Accept(","));
            }
            Expect(")");
            if (Is("{")) throw NotSupported("anonymous classes", Cur.Start, Cur.End);
            ne.End = Prev.End;
            return ne;
        }

        ArrayInitExpr ParseArrayInit()
        {
            var open = Expect("{");
            var ai = new ArrayInitExpr { Start = open.Start };
            while (!Is("}"))
            {
                ai.Items.Add(Is("{") ? ParseArrayInit() : ParseExpr());
                if (!Accept(",")) break;
            }
            Expect("}");
            ai.End = Prev.End;
            return ai;
        }
    }
}
