using System;
using System.Collections.Generic;
using System.Globalization;

namespace JavaFarmer.Compiler.CSharp
{
    internal sealed class CsAbort : Exception
    {
        public readonly Diagnostic Diag;
        public CsAbort(Diagnostic d) : base(d.Message) { Diag = d; }
    }

    /// <summary>Recursive-descent parser for the JavaFarmer C# subset (C# 12 syntax). Stops at the first syntax error.</summary>
    internal sealed class CsParser
    {
        readonly List<CsToken> toks;
        int p;
        readonly string src;
        int depth;

        static readonly HashSet<string> Predefined = new HashSet<string>
        { "int", "double", "bool", "string", "object", "char", "long", "float", "decimal", "byte", "sbyte", "short", "ushort", "uint", "ulong", "void" };

        CsParser(string source, List<CsToken> tokens) { src = source; toks = tokens; }

        public static CUnit Parse(string source, out Diagnostic error)
        {
            error = null;
            var lexer = new CsLexer(source);
            var tokens = lexer.LexSignificant();
            if (lexer.Error != null) { error = lexer.Error; return null; }
            var parser = new CsParser(source, tokens);
            try { return parser.ParseUnit(); }
            catch (CsAbort a) { error = a.Diag; return null; }
        }

        // ------------------------------------------------------------ token helpers
        CsToken Cur => toks[p];
        CsToken Peek(int n = 1) => toks[Math.Min(p + n, toks.Count - 1)];
        CsToken Prev => toks[Math.Max(p - 1, 0)];
        bool Is(string text) => (Cur.Kind == CTk.Op || Cur.Kind == CTk.Keyword) && Cur.Text == text;
        bool IsAt(int n, string text) { var t = Peek(n); return (t.Kind == CTk.Op || t.Kind == CTk.Keyword) && t.Text == text; }
        bool IsIdent => Cur.Kind == CTk.Identifier;
        bool IsWord(string w) => Cur.Kind == CTk.Identifier && Cur.Text == w;
        bool IsWordAt(int n, string w) { var t = Peek(n); return t.Kind == CTk.Identifier && t.Text == w; }
        CsToken Advance() { var t = Cur; if (p < toks.Count - 1) p++; return t; }
        bool Accept(string text) { if (Is(text)) { Advance(); return true; } return false; }

        static CsAbort Err(string msg, int s, int e) => new CsAbort(new Diagnostic(msg, s, e));
        CsAbort Eof() => Err("CS1513: } expected", Math.Max(0, Prev.Start), Prev.End);

        static string Code(string text)
        {
            switch (text)
            {
                case ";": return "CS1002";
                case ")": return "CS1026";
                case "}": return "CS1513";
                case "{": return "CS1514";
                case "]": return "CS1003";
                case "(": return "CS1003";
                case "=>": return "CS1003";
            }
            return "CS1003";
        }

        /// <summary>Roslyn reports a missing token right after the previous token ("; expected").</summary>
        CsToken Expect(string text)
        {
            if (Is(text)) return Advance();
            if (Cur.Kind == CTk.EOF && text != ";" && text != ")") throw Eof();
            var msg = Code(text) == "CS1003" ? "CS1003: Syntax error, '" + text + "' expected" : Code(text) + ": " + text + " expected";
            if (p == 0) throw Err(msg, Cur.Start, Cur.End);
            throw Err(msg, Prev.Start, Prev.End);
        }

        CsToken ExpectIdent()
        {
            if (IsIdent) return Advance();
            if (Cur.Kind == CTk.EOF) throw Err("CS1001: Identifier expected", Math.Max(0, Prev.Start), Prev.End);
            if (Cur.Kind == CTk.Keyword) throw Err("CS1041: Identifier expected; '" + Cur.Text + "' is a keyword", Cur.Start, Cur.End);
            throw Err("CS1001: Identifier expected", Cur.Start, Cur.End);
        }

        static CsAbort NotSupported(string what, int s, int e) => Err(what + (Plural(what) ? " are" : " is") + " not supported in JavaFarmer", s, e);

        /// <summary>"properties are" / "'goto' is": plural nouns get "are".</summary>
        internal static bool Plural(string what)
        {
            var w = what;
            int paren = w.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) w = w.Substring(0, paren);
            if (w.EndsWith("'") || w.EndsWith("\"")) return false;
            return w.EndsWith("s") && !w.EndsWith("ss") && !w.EndsWith("status");
        }

        CsAbort Invalid(CsToken t)
        {
            if (t.Kind == CTk.EOF) return Err("CS1733: Expected expression", Math.Max(0, Prev.Start), Prev.End);
            return Err("CS1525: Invalid expression term '" + t.Text + "'", t.Start, t.End);
        }

        // ------------------------------------------------------------ compilation unit
        CUnit ParseUnit()
        {
            var u = new CUnit { Start = 0, End = src.Length };
            while (Is("using") || (IsWord("global") && IsAt(1, "using")))
            {
                if (Is("using") && IsAt(1, "(")) break;   // using statement: rejected below
                if (Is("using") && Peek().Kind == CTk.Identifier && Peek().Text == "var") break;
                u.Usings.Add(ParseUsing());
            }
            if (Is("namespace")) throw NotSupported("namespaces", Cur.Start, Cur.End);
            int save = p;
            SkipModifiersAndAttributes();
            if (Is("class") || Is("struct") || Is("interface") || Is("enum") || IsWord("record"))
            {
                p = save;
                u.Class = ParseClass();
                if (Cur.Kind != CTk.EOF)
                {
                    if (Is("class") || Is("struct") || IsWord("record") || IsModifier())
                        throw Err("only one class per file is supported in JavaFarmer", Cur.Start, Cur.End);
                    if (Is("using")) throw Err("CS1529: A using clause must precede all other elements defined in the namespace except extern alias declarations", Cur.Start, Cur.End);
                    throw Err("CS8803: Top-level statements must precede namespace and type declarations.", Cur.Start, Cur.End);
                }
                return u;
            }
            p = save;
            while (Cur.Kind != CTk.EOF)
            {
                if (Is("using") && !(IsAt(1, "(") || (Peek().Kind == CTk.Identifier && Peek().Text == "var")))
                    throw Err("CS1529: A using clause must precede all other elements defined in the namespace except extern alias declarations", Cur.Start, Cur.End);
                if (Is("namespace")) throw NotSupported("namespaces", Cur.Start, Cur.End);
                int s2 = p;
                SkipModifiersAndAttributes();
                bool typeDecl = Is("class") || Is("struct") || Is("interface") || Is("enum") || (IsWord("record") && Peek().Kind == CTk.Identifier);
                p = s2;
                if (typeDecl) throw Err("classes next to top-level statements are not supported in JavaFarmer (put helpers in local functions, or library code in a separate file)", Cur.Start, Cur.End);
                u.TopLevel.Add(ParseStatement(true, true));
            }
            return u;
        }

        bool IsModifier()
        {
            if (Cur.Kind == CTk.Keyword)
            {
                switch (Cur.Text)
                {
                    case "public": case "private": case "protected": case "internal": case "static": case "const": case "readonly":
                    case "abstract": case "virtual": case "override": case "sealed": case "extern": case "unsafe": case "volatile": case "new":
                        return Cur.Text != "new" || !(IsAt(1, "(") || IsAt(1, "[") || Peek().Kind == CTk.Identifier && IsAt(2, "(") || Peek().Kind == CTk.Identifier && IsAt(2, "{") || Peek().Kind == CTk.Identifier && IsAt(2, "<"));
                }
                return false;
            }
            if (Cur.Kind == CTk.Identifier && (Cur.Text == "async" || Cur.Text == "partial" || Cur.Text == "required" || Cur.Text == "file")
                && (Peek().Kind == CTk.Identifier || Peek().Kind == CTk.Keyword)) return true;
            return false;
        }

        void SkipModifiersAndAttributes()
        {
            while (true)
            {
                if (Is("[")) { SkipBalanced("[", "]"); continue; }
                if (!IsModifier()) break;
                Advance();
            }
        }

        void SkipBalanced(string open, string close)
        {
            int d = 0;
            do
            {
                if (Is(open)) d++;
                else if (Is(close)) d--;
                if (Cur.Kind == CTk.EOF) throw Eof();
                Advance();
            } while (d > 0);
        }

        CMods ParseModifiers(bool member)
        {
            var mods = CMods.None;
            while (true)
            {
                if (Is("[")) throw NotSupported("attributes", Cur.Start, Cur.End);
                if (!IsModifier()) break;
                var t = Cur;
                switch (t.Text)
                {
                    case "public": mods |= CMods.Public; break;
                    case "private": mods |= CMods.Private; break;
                    case "protected": mods |= CMods.Protected; break;
                    case "internal": mods |= CMods.Internal; break;
                    case "static": mods |= CMods.Static; break;
                    case "const": mods |= CMods.Const; break;
                    case "readonly": mods |= CMods.Readonly; break;
                    default: throw NotSupported("the modifier '" + t.Text + "'", t.Start, t.End);
                }
                Advance();
            }
            return mods;
        }

        CUsing ParseUsing()
        {
            int s = Cur.Start;
            var d = new CUsing { Start = s };
            if (IsWord("global")) { Advance(); d.IsGlobal = true; }
            Expect("using");
            if (Accept("static")) d.IsStatic = true;
            if (IsIdent && IsAt(1, "=")) { d.Alias = Advance().Text; Advance(); }
            var name = ExpectIdent().Text;
            while (Accept(".")) name += "." + ExpectIdent().Text;
            if (Is("<")) throw NotSupported("generic types in using directives", Cur.Start, Cur.End);
            d.Name = name;
            Expect(";");
            d.End = Prev.End;
            return d;
        }

        CClass ParseClass()
        {
            int start = Cur.Start;
            while (Is("[")) throw NotSupported("attributes", Cur.Start, Cur.End);
            var mods = ParseModifiers(false);
            if (Is("struct") || Is("interface") || Is("enum") || IsWord("record")) throw NotSupported("'" + Cur.Text + "' declarations", Cur.Start, Cur.End);
            Expect("class");
            var name = ExpectIdent();
            var c = new CClass { Start = start, Mods = mods, Name = name.Text, NameStart = name.Start, NameEnd = name.End, HeaderEnd = name.End };
            if (Is("<")) throw NotSupported("generic classes", Cur.Start, Cur.End);
            if (Is(":")) throw NotSupported("inheritance", Cur.Start, Cur.End);
            if (Is("(")) throw NotSupported("primary constructors", Cur.Start, Cur.End);
            Expect("{");
            while (!Is("}"))
            {
                if (Cur.Kind == CTk.EOF) throw Eof();
                ParseMember(c);
            }
            Expect("}");
            Accept(";");
            c.End = Prev.End;
            return c;
        }

        void ParseMember(CClass c)
        {
            if (Accept(";")) return;
            int start = Cur.Start;
            var mods = ParseModifiers(true);
            if (Is("class") || Is("struct") || Is("interface") || Is("enum") || IsWord("record") || Is("delegate"))
                throw NotSupported("nested " + Cur.Text + " declarations", Cur.Start, Cur.End);
            if (Is("~")) throw NotSupported("finalizers", Cur.Start, Cur.End);
            if (IsIdent && Cur.Text == c.Name && IsAt(1, "(")) throw NotSupported("constructors", Cur.Start, Cur.End);
            if (Is("operator") || Is("implicit") || Is("explicit")) throw NotSupported("operator declarations", Cur.Start, Cur.End);
            if (Is("event")) throw NotSupported("events", Cur.Start, Cur.End);
            if (Cur.Kind == CTk.EOF) throw Eof();
            if (IsIdent && IsAt(1, "(") && !(Peek(2).Kind == CTk.Identifier))
            {
                // "Foo() { }" without a return type, or a statement in the class body
                int save = p;
                Advance(); SkipBalanced("(", ")");
                bool body = Is("{") || Is("=>");
                p = save;
                if (body) throw Err("CS1520: Method must have a return type", Cur.Start, Cur.End);
                throw Err("statements must be inside a method (write them inside static void Main() { ... }, or use top-level statements without a class)", Cur.Start, Cur.End);
            }
            if (!(IsIdent || IsPredefinedKw(Cur) || Is("(")))
            {
                if (Cur.Kind == CTk.Keyword && IsStatementKeyword(Cur.Text))
                    throw Err("statements must be inside a method (write them inside static void Main() { ... }, or use top-level statements without a class)", Cur.Start, Cur.End);
                throw Err("CS1519: Invalid token '" + Cur.Text + "' in class, record, struct, or interface member declaration", Cur.Start, Cur.End);
            }
            if (IsIdent && (IsAt(1, "=") || IsAt(1, "++") || IsAt(1, "--") || IsAt(1, "+=") || IsAt(1, "-=") || IsAt(1, ".")))
                throw Err("statements must be inside a method (write them inside static void Main() { ... }, or use top-level statements without a class)", Cur.Start, Cur.End);
            var type = ParseType();
            if (Is("this")) throw NotSupported("indexers", Cur.Start, Cur.End);
            var name = ExpectIdent();
            if (Is("<")) throw NotSupported("generic methods", Cur.Start, Cur.End);
            if (Is("("))
            {
                var m = ParseMethodRest(start, mods, type, name, false);
                c.Methods.Add(m);
                c.Members.Add(m);
                return;
            }
            if (Is("{") || (Is("=>")))
                throw NotSupported("properties", name.Start, name.End);
            if (type.Name == "void") throw Err("CS1547: Keyword 'void' cannot be used in this context", type.Start, type.End);
            var f = new CField { Start = start, Mods = mods, Type = type };
            f.Vars.Add(ParseDeclaratorRest(name));
            while (Accept(",")) f.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            Expect(";");
            f.End = Prev.End;
            c.Fields.Add(f);
            c.Members.Add(f);
        }

        static bool IsStatementKeyword(string t)
        {
            switch (t)
            {
                case "if": case "while": case "for": case "foreach": case "do": case "return": case "switch": case "break": case "continue": case "{":
                    return true;
            }
            return false;
        }

        CMethod ParseMethodRest(int start, CMods mods, CTypeRef type, CsToken name, bool local)
        {
            var m = new CMethod { Start = start, Mods = mods, ReturnType = type, Name = name.Text, NameStart = name.Start, NameEnd = name.End, HeaderStart = start, IsLocal = local };
            Expect("(");
            if (!Is(")"))
            {
                do
                {
                    int ps = Cur.Start;
                    if (Is("[")) throw NotSupported("attributes", Cur.Start, Cur.End);
                    if (Is("ref") || Is("out") || Is("in") || Is("params") || Is("this") || IsWord("scoped"))
                        throw NotSupported("'" + Cur.Text + "' parameters", Cur.Start, Cur.End);
                    var pt = ParseType();
                    var pn = ExpectIdent();
                    var prm = new CParam { Start = ps, Type = pt, Name = pn.Text, NameStart = pn.Start, NameEnd = pn.End };
                    if (Accept("=")) prm.Default = ParseExpr();
                    prm.End = Prev.End;
                    m.Params.Add(prm);
                } while (Accept(","));
            }
            Expect(")");
            m.HeaderEnd = Prev.End;
            if (IsWord("where")) throw NotSupported("generic constraints", Cur.Start, Cur.End);
            if (Is("=>"))
            {
                m.ArrowStart = Cur.Start;
                Advance();
                m.ExprBody = ParseExpr();
                Expect(";");
            }
            else if (Is(";")) throw Err("CS0501: '" + name.Text + "()' must declare a body because it is not marked abstract, extern, or partial", name.Start, name.End);
            else
            {
                if (!Is("{")) throw Err("CS1514: { expected", Prev.Start, Prev.End);
                m.Body = ParseBlock();
            }
            m.End = Prev.End;
            return m;
        }

        CVarDeclarator ParseDeclaratorRest(CsToken name)
        {
            var d = new CVarDeclarator { Start = name.Start, Name = name.Text, NameStart = name.Start, NameEnd = name.End };
            if (Is("[")) throw Err("CS0650: Bad array declarator: To declare a managed array the rank specifier precedes the variable's identifier. To declare a fixed size buffer field, use the fixed keyword before the field type.", Cur.Start, Cur.End);
            if (Accept("="))
            {
                d.Init = Is("{") ? ParseArrayInit() : ParseExpr();
            }
            d.End = Prev.End;
            return d;
        }

        // ------------------------------------------------------------ types
        static bool IsPredefinedKw(CsToken t) => t.Kind == CTk.Keyword && Predefined.Contains(t.Text);

        CTypeRef ParseType()
        {
            var t = TryParseType(out var err, false, true);
            if (t == null) throw err ?? (Cur.Kind == CTk.Keyword ? Err("CS1041: Identifier expected; '" + Cur.Text + "' is a keyword", Cur.Start, Cur.End) : Err("CS1001: Identifier expected", Cur.Start, Cur.End));
            return t;
        }

        /// <summary>Parses a type (no backtracking state is restored on failure).</summary>
        CTypeRef TryParseType(out CsAbort err, bool speculative, bool greedyNullable = false)
        {
            err = null;
            var start = Cur;
            CTypeRef tr;
            if (IsPredefinedKw(Cur))
            {
                Advance();
                tr = new CTypeRef { Name = start.Text, Start = start.Start, NameStart = start.Start, NameEnd = start.End };
            }
            else if (Is("("))
            {
                // tuple type (int, int)
                Advance();
                var items = new List<CTypeRef>();
                do
                {
                    var it = TryParseType(out err, speculative);
                    if (it == null) return null;
                    if (IsIdent && (IsAt(1, ",") || IsAt(1, ")"))) Advance();   // named element
                    items.Add(it);
                } while (Accept(","));
                if (!Accept(")") || items.Count < 2) { err = Err("CS1001: Identifier expected", Cur.Start, Cur.End); return null; }
                tr = new CTypeRef { Name = "(tuple)", IsTuple = true, Args = items, Start = start.Start, NameStart = start.Start, NameEnd = Prev.End };
            }
            else if (Cur.Kind == CTk.Identifier)
            {
                var nameTok = Advance();
                string name = nameTok.Text;
                int ns = nameTok.Start, ne = nameTok.End;
                // qualified: System.Collections.Generic.List
                while (Is(".") && Peek().Kind == CTk.Identifier)
                {
                    Advance();
                    var part = Advance();
                    name = part.Text; ns = part.Start; ne = part.End;
                }
                tr = new CTypeRef { Name = name, Start = start.Start, NameStart = ns, NameEnd = ne };
                if (Is("<"))
                {
                    int save = p;
                    Advance();
                    tr.Args = new List<CTypeRef>();
                    bool ok = true;
                    if (Is(">")) ok = false;
                    else
                    {
                        do
                        {
                            var a = TryParseType(out err, speculative, true);
                            if (a == null) { ok = false; break; }
                            tr.Args.Add(a);
                        } while (Accept(","));
                    }
                    if (!ok || !Is(">"))
                    {
                        if (speculative) { p = save; err = err ?? Err("CS1003: Syntax error, '>' expected", Prev.Start, Prev.End); return null; }
                        throw err ?? Err("CS1003: Syntax error, '>' expected", Prev.Start, Prev.End);
                    }
                    Advance();
                }
            }
            else
            {
                err = Cur.Kind == CTk.EOF ? Err("CS1001: Identifier expected", Math.Max(0, Prev.Start), Prev.End) : Err("CS1001: Identifier expected", Cur.Start, Cur.End);
                return null;
            }
            // T? x = / T? x; / T? x, / T? x) / T? x in: a nullable type (else "c ? a : b" is a conditional)
            if (Is("?") && ((Peek().Kind == CTk.Identifier && (IsAt(2, "=") || IsAt(2, ";") || IsAt(2, ",") || IsAt(2, ")") || IsAt(2, "in") || IsAt(2, "=>") || (greedyNullable && IsAt(2, "("))))
                || (greedyNullable && (IsAt(1, ">") || IsAt(1, ",") || IsAt(1, ")")))))
            { Advance(); tr.Nullable = true; }
            while (Is("[") && (IsAt(1, "]") || IsAt(1, ",")))
            {
                Advance();
                int rank = 1;
                while (Accept(",")) rank++;
                if (!Accept("]")) { err = Err("CS1003: Syntax error, ']' expected", Prev.Start, Prev.End); if (speculative) return null; throw err; }
                tr.Ranks.Add(rank);
            }
            tr.End = Prev.End;
            return tr;
        }

        // ------------------------------------------------------------ statements
        CBlock ParseBlock()
        {
            var open = Expect("{");
            var b = new CBlock { Start = open.Start };
            while (!Is("}"))
            {
                if (Cur.Kind == CTk.EOF) throw Eof();
                b.Stmts.Add(ParseStatement(true, false));
            }
            Advance();
            b.End = Prev.End;
            return b;
        }

        CStmt Embedded()
        {
            var t = Cur;
            var s = ParseStatement(false, false);
            return s;
        }

        CStmt ParseStatement(bool allowDecl, bool topLevel)
        {
            if (++depth > 200) throw Err("the code is nested too deeply", Cur.Start, Cur.End);
            try { return ParseStatementCore(allowDecl, topLevel); }
            finally { depth--; }
        }

        CStmt ParseStatementCore(bool allowDecl, bool topLevel)
        {
            var t = Cur;
            if (t.Kind == CTk.Op && t.Text == "{") return ParseBlock();
            if (t.Kind == CTk.Op && t.Text == ";") { Advance(); return new CEmpty { Start = t.Start, End = t.End }; }
            if (t.Kind == CTk.Op && t.Text == "[") 
            {
                // attribute on a local function, or a collection expression statement (invalid either way)
                if (Peek().Kind == CTk.Identifier && (IsAt(2, "]") || IsAt(2, "("))) throw NotSupported("attributes", t.Start, t.End);
            }
            if (t.Kind == CTk.Keyword)
            {
                switch (t.Text)
                {
                    case "if": return ParseIf();
                    case "while":
                        {
                            Advance();
                            var w = new CWhile { Start = t.Start, KwEnd = t.End };
                            w.Cond = ParseParenCond();
                            w.Body = Embedded();
                            w.End = w.Body.End;
                            return w;
                        }
                    case "do":
                        {
                            Advance();
                            var d = new CDoWhile { Start = t.Start };
                            d.Body = Embedded();
                            var wt = Expect("while");
                            d.WhileStart = wt.Start; d.WhileEnd = wt.End;
                            d.Cond = ParseParenCond();
                            Expect(";");
                            d.End = Prev.End;
                            return d;
                        }
                    case "for": return ParseFor();
                    case "foreach": return ParseForEach();
                    case "switch": return ParseSwitch();
                    case "return":
                        {
                            Advance();
                            var r = new CReturn { Start = t.Start };
                            if (!Is(";")) r.Value = ParseExpr();
                            Expect(";");
                            r.End = Prev.End;
                            return r;
                        }
                    case "break":
                        Advance(); Expect(";");
                        return new CBreak { Start = t.Start, End = Prev.End };
                    case "continue":
                        Advance(); Expect(";");
                        return new CContinue { Start = t.Start, End = Prev.End };
                    case "goto": throw NotSupported("'goto'", t.Start, t.End);
                    case "try": case "catch": case "finally": throw NotSupported("try/catch", t.Start, t.End);
                    case "throw": throw NotSupported("'throw'", t.Start, t.End);
                    case "lock": throw NotSupported("'lock'", t.Start, t.End);
                    case "using": throw NotSupported("using statements", t.Start, t.End);
                    case "checked": case "unchecked": throw NotSupported("'" + t.Text + "'", t.Start, t.End);
                    case "unsafe": case "fixed": throw NotSupported("unsafe code", t.Start, t.End);
                    case "else": throw Err("CS8641: 'else' cannot start a statement.", t.Start, t.End);
                    case "case": case "default":
                        if (t.Text == "default" && (IsAt(1, "(") || IsAt(1, ";"))) break;
                        throw Err("CS1525: Invalid expression term '" + t.Text + "'", t.Start, t.End);
                    case "class": case "struct": case "interface": case "enum":
                        throw Err(topLevel ? "classes next to top-level statements are not supported in JavaFarmer (put helpers in local functions, or library code in a separate file)" : "CS1525: Invalid expression term '" + t.Text + "'", t.Start, t.End);
                    case "const":
                        {
                            if (!allowDecl) throw Err("CS1023: Embedded statement cannot be a declaration or labeled statement", t.Start, t.End);
                            Advance();
                            var lv = ParseLocalVarDecl(t.Start, true, true);
                            return lv;
                        }
                    case "public": case "private": case "protected": case "internal": case "readonly": case "abstract": case "virtual": case "override": case "sealed": case "extern":
                        if (topLevel) throw Err("CS0106: The modifier '" + t.Text + "' is not valid for this item", t.Start, t.End);
                        throw Err("CS1525: Invalid expression term '" + t.Text + "'", t.Start, t.End);
                    case "static":
                        {
                            // static local function / static lambda statement
                            if (IsAt(1, "(") || IsAt(1, "=>")) break;
                            int s0 = p;
                            Advance();
                            var lf = TryLocalFunction(t.Start, CMods.Static, topLevel);
                            if (lf != null) return lf;
                            p = s0;
                            throw Err("CS0106: The modifier 'static' is not valid for this item", t.Start, t.End);
                        }
                }
            }
            if (t.Kind == CTk.Identifier)
            {
                if (IsAt(1, ":") && !IsAt(2, ":")) throw NotSupported("labels", t.Start, t.End);
                if (t.Text == "yield" && (IsAt(1, "return") || IsAt(1, "break"))) throw NotSupported("'yield'", t.Start, t.End);
                if (t.Text == "await") throw NotSupported("'await'", t.Start, t.End);
                if (t.Text == "async") throw NotSupported("async code", t.Start, t.End);
                if (t.Text == "elif") throw Err("CS0103: The name 'elif' does not exist in the current context (C# writes else if (condition) { ... })", t.Start, t.End);
                if (t.Text == "def" && Peek().Kind == CTk.Identifier) throw Err("CS0246: The type or namespace name 'def' could not be found (C# functions are written as void Name() { ... } or int Name() { return ...; })", t.Start, t.End);
                if (t.Text == "var" && IsAt(1, "("))
                {
                    // var (a, b) = expr;
                    if (!allowDecl) throw Err("CS1023: Embedded statement cannot be a declaration or labeled statement", t.Start, t.End);
                    Advance();
                    var names = ParseDeconstructNames();
                    var op = Expect("=");
                    var value = ParseExpr();
                    Expect(";");
                    return new CDeconstruct { Start = t.Start, End = Prev.End, Names = names, Value = value, OpStart = op.Start, OpEnd = op.End };
                }
            }

            // (int a, int b) = expr;  typed deconstruction
            if (Is("(") && allowDecl)
            {
                var td = TryTypedDeconstruction();
                if (td != null) return td;
            }

            // local function? (a type followed by a name: "int F(", "List<int> F(", "Entity? F(")
            bool typeLike = IsPredefinedKw(Cur) || Is("(") || (IsIdent && (Peek().Kind == CTk.Identifier || IsAt(1, "<") || IsAt(1, "?") || IsAt(1, "[") || IsAt(1, ".")));
            if (typeLike)
            {
                int s0 = p;
                var lf = TryLocalFunction(t.Start, CMods.None, topLevel);
                if (lf != null)
                {
                    if (!allowDecl) throw Err("CS1023: Embedded statement cannot be a declaration or labeled statement", t.Start, t.End);
                    return lf;
                }
                p = s0;
            }

            if (typeLike && IsLocalVarDeclStart())
            {
                if (!allowDecl) throw Err("CS1023: Embedded statement cannot be a declaration or labeled statement", t.Start, t.End);
                return ParseLocalVarDecl(t.Start, false, true);
            }

            var e = ParseExpr();
            if (!IsStatementExpr(e))
            {
                if (Is(";") || Cur.Kind == CTk.EOF || OnDifferentLine(Prev, Cur))
                {
                    if (!Is(";")) Expect(";");
                    throw Err("CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement", e.Start, e.End);
                }
            }
            Expect(";");
            return new CExprStmt { Start = e.Start, End = Prev.End, E = e };
        }

        static bool IsStatementExpr(CExpr e) => e is CAssign || e is CIncDec || e is CCall || e is CNew;

        bool OnDifferentLine(CsToken a, CsToken b)
        {
            for (int i = a.End; i < b.Start && i < src.Length; i++) if (src[i] == '\n') return true;
            return false;
        }

        /// <summary>"Type Name(" ... ")" followed by "{" or "=>": a local function. Null (position not restored) otherwise.</summary>
        CStmt TryLocalFunction(int start, CMods mods, bool topLevel)
        {
            if (!(IsIdent || IsPredefinedKw(Cur) || Is("("))) return null;
            var type = TryParseType(out _, true, true);
            if (type == null || !IsIdent || !IsAt(1, "(")) return null;
            // parameter list must look like a declaration: () or (Type name ...
            int save = p;
            var name = Advance();
            Advance();
            bool looks = Is(")") || IsPredefinedKw(Cur) || (IsIdent && (Peek().Kind == CTk.Identifier || IsAt(1, "<") || IsAt(1, "[") || IsAt(1, "?") || IsAt(1, ".")))
                || Is("ref") || Is("out") || Is("params") || Is("in") || Is("(");
            if (!looks) return null;
            p = save;
            int s1 = p;
            Advance(); SkipBalanced("(", ")");
            bool body = Is("{") || Is("=>") || IsWord("where");
            p = s1;
            if (!body) return null;
            if (!topLevel) throw NotSupported("local functions inside methods or blocks", name.Start, name.End);
            var m = ParseMethodRest(start, mods, type, Advance(), true);
            return new CLocalFuncStmt { Start = start, End = m.End, Method = m };
        }

        /// <summary>(int a, int b) = value;  /  (int a, (int b, int c)) = value;  Null (position restored) when it is not one.</summary>
        CStmt TryTypedDeconstruction()
        {
            int save = p;
            var start = Cur.Start;
            var names = TryTypedNames();
            if (names == null || !Is("=") ) { p = save; return null; }
            var op = Advance();
            var value = ParseExpr();
            Expect(";");
            return new CDeconstruct { Start = start, End = Prev.End, Names = names, Value = value, OpStart = op.Start, OpEnd = op.End };
        }

        CTupleExpr TryTypedNames()
        {
            if (!Is("(")) return null;
            var open = Advance();
            var tup = new CTupleExpr { Start = open.Start, Declares = true };
            while (true)
            {
                if (Is("("))
                {
                    var inner = TryTypedNames();
                    if (inner == null) return null;
                    tup.Items.Add(inner);
                }
                else
                {
                    if (!(IsIdent || IsPredefinedKw(Cur))) return null;
                    var tr = TryParseType(out _, true, true);
                    if (tr == null || !IsIdent) return null;
                    var n = Advance();
                    tup.Items.Add(new CName { Name = n.Text, Start = n.Start, End = n.End, Type = null, TypeRef = tr });
                }
                if (Accept(",")) continue;
                if (Accept(")")) break;
                return null;
            }
            if (tup.Items.Count < 2) return null;
            tup.End = Prev.End;
            return tup;
        }

        CTupleExpr ParseDeconstructNames()
        {
            var open = Expect("(");
            var tup = new CTupleExpr { Start = open.Start, Declares = true };
            do
            {
                if (Is("(")) tup.Items.Add(ParseDeconstructNames());
                else
                {
                    var n = ExpectIdent();
                    if (n.Text == "_") tup.Items.Add(new CDiscard { Start = n.Start, End = n.End });
                    else tup.Items.Add(new CName { Name = n.Text, Start = n.Start, End = n.End });
                }
            } while (Accept(","));
            Expect(")");
            tup.End = Prev.End;
            return tup;
        }

        bool IsLocalVarDeclStart()
        {
            if (IsPredefinedKw(Cur) && !IsAt(1, ".") && !IsAt(1, "(")) return true;
            if (!IsIdent && !Is("(")) return false;
            int save = p;
            var tr = TryParseType(out _, true);
            bool ok = tr != null && IsIdent && !(tr.IsTuple && false);
            if (ok && tr.Name == "var" && tr.Args == null && tr.Ranks.Count == 0 && !tr.Nullable) ok = IsIdent;
            if (ok && IsWordAt(0, "is")) ok = false;
            if (ok && Cur.Kind == CTk.Identifier && (Cur.Text == "switch" || Cur.Text == "when")) ok = false;
            p = save;
            return ok;
        }

        CLocalVar ParseLocalVarDecl(int start, bool isConst, bool semi)
        {
            var lv = new CLocalVar { Start = start, IsConst = isConst };
            lv.Type = ParseType();
            lv.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            while (Accept(",")) lv.Vars.Add(ParseDeclaratorRest(ExpectIdent()));
            if (semi) Expect(";");
            lv.End = Prev.End;
            return lv;
        }

        CExpr ParseParenCond()
        {
            if (!Is("(")) throw Err("CS1003: Syntax error, '(' expected (C# writes the condition in parentheses: " + Prev.Text + " (condition) { ... })", Prev.Start, Prev.End);
            Expect("(");
            var e = ParseExpr();
            Expect(")");
            return e;
        }

        CStmt ParseIf()
        {
            var kw = Advance();
            var s = new CIf { Start = kw.Start, KwEnd = kw.End };
            s.Cond = ParseParenCond();
            s.Then = Embedded();
            if (Accept("else")) s.Else = Embedded();
            s.End = Prev.End;
            return s;
        }

        CStmt ParseFor()
        {
            var kw = Advance();
            if (!Is("(")) throw Err("CS1003: Syntax error, '(' expected (C# loops: for (int i = 0; i < n; i++) { ... } or foreach (var x in items) { ... })", kw.Start, kw.End);
            Expect("(");
            var f = new CFor { Start = kw.Start, KwEnd = kw.End };
            if (!Is(";"))
            {
                int s0 = Cur.Start;
                if (IsLocalVarDeclStart()) f.Init.Add(ParseLocalVarDecl(s0, false, false));
                else
                {
                    do
                    {
                        var e = ParseExpr();
                        if (!IsStatementExpr(e)) throw Err("CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement", e.Start, e.End);
                        f.Init.Add(new CExprStmt { Start = e.Start, End = e.End, E = e });
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
                    if (!IsStatementExpr(e)) throw Err("CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement", e.Start, e.End);
                    f.Update.Add(e);
                } while (Accept(","));
            }
            Expect(")");
            f.HeaderEnd = Prev.End;
            f.Body = Embedded();
            f.End = f.Body.End;
            return f;
        }

        CStmt ParseForEach()
        {
            var kw = Advance();
            if (!Is("(")) throw Err("CS1003: Syntax error, '(' expected (C# writes foreach (var x in items) { ... })", kw.Start, kw.End);
            Expect("(");
            var fe = new CForEach { Start = kw.Start, KwEnd = kw.End };
            if (IsWord("var") && IsAt(1, "("))
            {
                Advance();
                fe.Deconstruct = ParseDeconstructNames();
            }
            else if (Is("("))
            {
                throw NotSupported("typed deconstruction in foreach (write foreach (var (a, b) in ...))", Cur.Start, Cur.End);
            }
            else
            {
                if (IsIdent && IsAt(1, "in")) throw Err("CS0230: Type and identifier are both required in a foreach statement (write foreach (var " + Cur.Text + " in ...))", Cur.Start, Cur.End);
                fe.VarType = ParseType();
                var n = ExpectIdent();
                fe.Name = n.Text; fe.NameStart = n.Start; fe.NameEnd = n.End;
            }
            if (Is(":")) throw Err("CS1515: 'in' expected (C# writes foreach (var x in items))", Cur.Start, Cur.End);
            if (!Is("in")) throw Err("CS1515: 'in' expected", Prev.Start, Prev.End);
            Advance();
            fe.Iterable = ParseExpr();
            Expect(")");
            fe.HeaderEnd = Prev.End;
            fe.Body = Embedded();
            fe.End = fe.Body.End;
            return fe;
        }

        CStmt ParseSwitch()
        {
            var kw = Advance();
            var sw = new CSwitch { Start = kw.Start, KwEnd = kw.End };
            sw.Selector = ParseParenCond();
            Expect("{");
            while (!Is("}"))
            {
                if (Cur.Kind == CTk.EOF) throw Eof();
                var sec = new CSection { Start = Cur.Start };
                while (Is("case") || Is("default"))
                {
                    var lt = Advance();
                    var lab = new CLabel { Start = lt.Start };
                    if (lt.Text == "default") lab.IsDefault = true;
                    else lab.Values.AddRange(ParsePatterns(":"));
                    if (IsWord("when")) throw NotSupported("'when' clauses", Cur.Start, Cur.End);
                    if (!Is(":")) throw Err("CS1003: Syntax error, ':' expected", Prev.Start, Prev.End);
                    Advance();
                    lab.LabelEnd = Prev.End;
                    lab.End = Prev.End;
                    sec.Labels.Add(lab);
                }
                if (sec.Labels.Count == 0) throw Err("CS1513: } expected", Prev.Start, Prev.End);
                while (!Is("case") && !Is("default") && !Is("}"))
                {
                    if (Cur.Kind == CTk.EOF) throw Eof();
                    sec.Body.Add(ParseStatement(true, false));
                }
                if (Is("default") && (IsAt(1, "(") )) { }
                sec.End = Prev.End;
                sw.Sections.Add(sec);
            }
            Advance();
            sw.End = Prev.End;
            return sw;
        }

        /// <summary>Constant patterns separated by 'or' (case 1 or 2:). Relational / type patterns are rejected.</summary>
        List<CExpr> ParsePatterns(string terminator)
        {
            var res = new List<CExpr>();
            while (true)
            {
                if (Is("<") || Is(">") || Is("<=") || Is(">=")) throw NotSupported("relational patterns", Cur.Start, Cur.End);
                if (IsWord("not")) throw NotSupported("'not' patterns", Cur.Start, Cur.End);
                if (IsWord("var") && Peek().Kind == CTk.Identifier) throw NotSupported("var patterns", Cur.Start, Peek().End);
                if ((IsPredefinedKw(Cur) || IsIdent) && Peek().Kind == CTk.Identifier && !IsWordAt(1, "or") && !IsWordAt(1, "and") && !IsWordAt(1, "when"))
                    throw NotSupported("type patterns", Cur.Start, Peek().End);
                if (Is("(") && false) { }
                var e = ParseBinary(LevelAdditive);
                res.Add(e);
                if (IsWord("or")) { Advance(); continue; }
                if (IsWord("and")) throw NotSupported("'and' patterns", Cur.Start, Cur.End);
                break;
            }
            return res;
        }

        // ------------------------------------------------------------ expressions
        public CExpr ParseExpr()
        {
            if (++depth > 200) throw Err("the expression is nested too deeply", Cur.Start, Cur.End);
            try { return ParseExprCore(); }
            finally { depth--; }
        }

        CExpr ParseExprCore()
        {
            if (IsLambdaStart()) return ParseLambda();
            var lhs = ParseConditional();
            if (Cur.Kind == CTk.Op)
            {
                switch (Cur.Text)
                {
                    case "=": case "+=": case "-=": case "*=": case "/=": case "%=":
                        {
                            var op = Advance();
                            if (!(lhs is CName || lhs is CMember || lhs is CIndex || lhs is CParen || lhs is CTupleExpr))
                                throw Err("CS0131: The left-hand side of an assignment must be a variable, property or indexer", lhs.Start, lhs.End);
                            var rhs = ParseExpr();
                            return new CAssign { Op = op.Text, Target = lhs, Value = rhs, Start = lhs.Start, End = rhs.End, OpStart = op.Start, OpEnd = op.End };
                        }
                    case "&=": case "|=": case "^=": case "<<=": case ">>=": case ">>>=":
                        throw NotSupported("the operator '" + Cur.Text + "'", Cur.Start, Cur.End);
                    case "??=": throw NotSupported("the operator '??='", Cur.Start, Cur.End);
                }
            }
            return lhs;
        }

        CExpr ParseConditional()
        {
            var c = ParseBinary(0);
            if (Is("?"))
            {
                var q = Advance();
                var a = Is("(") && IsLambdaStart() || IsLambdaStart() ? ParseLambda() : ParseExpr();
                Expect(":");
                var b = IsLambdaStart() ? ParseLambda() : ParseExpr();
                return new CConditional { Cond = c, Then = a, Else = b, Start = c.Start, End = b.End, QStart = q.Start };
            }
            return c;
        }

        static readonly string[][] Levels =
        {
            new[] { "??" },
            new[] { "||" },
            new[] { "&&" },
            new[] { "|" },
            new[] { "^" },
            new[] { "&" },
            new[] { "==", "!=" },
            new[] { "<", ">", "<=", ">=" },
            new[] { "<<", ">>", ">>>" },
            new[] { "+", "-" },
            new[] { "*", "/", "%" },
        };
        const int LevelRelational = 7, LevelAdditive = 9;

        CExpr ParseBinary(int level)
        {
            if (level >= Levels.Length) return ParseSwitchExprLevel();
            var left = ParseBinary(level + 1);
            while (true)
            {
                if (level == LevelRelational && (IsWord("is") || Is("is")))
                {
                    left = ParseIs(left);
                    continue;
                }
                if (level == LevelRelational && Is("as")) throw NotSupported("'as'", Cur.Start, Cur.End);
                if (Cur.Kind != CTk.Op || Array.IndexOf(Levels[level], Cur.Text) < 0) break;
                var op = Cur;
                if (op.Text == ">" && Peek().Kind == CTk.Op && Peek().Text.StartsWith(">") && Peek().Start == op.End)
                    throw NotSupported("shift operators", op.Start, Peek().End);
                if (op.Text == "|" || op.Text == "&" || op.Text == "^")
                    throw NotSupported("the operator '" + op.Text + "' (use && / || for conditions; bitwise operators are not available)", op.Start, op.End);
                if (op.Text == "<<" || op.Text == ">>" || op.Text == ">>>") throw NotSupported("shift operators", op.Start, op.End);
                if (op.Text == "??") throw NotSupported("the operator '??'", op.Start, op.End);
                Advance();
                var right = ParseBinary(level + 1);
                left = new CBinary { Op = op.Text, Left = left, Right = right, Start = left.Start, End = right.End, OpStart = op.Start, OpEnd = op.End };
            }
            return left;
        }

        CExpr ParseIs(CExpr left)
        {
            var kw = Advance();
            var e = new CIs { Operand = left, Start = left.Start, KwStart = kw.Start, KwEnd = kw.End };
            if (IsWord("not")) { Advance(); e.Not = true; }
            if (Is("null")) { var n = Advance(); e.Pattern = new CLiteral { Kind = CLitKind.Null, Start = n.Start, End = n.End }; }
            else if (Is("<") || Is(">") || Is("<=") || Is(">=")) throw NotSupported("relational patterns", Cur.Start, Cur.End);
            else if (Is("{") || Is("[")) throw NotSupported("property and list patterns", Cur.Start, Cur.End);
            else if (Is("(")) throw NotSupported("tuple patterns", Cur.Start, Cur.End);
            else
            {
                int save = p;
                var tr = TryParseType(out _, true);
                if (tr != null && (Predefined.Contains(tr.Name) || CType.GameTypeNames.Contains(tr.Name) || tr.Args != null) && !Is("."))
                    throw NotSupported("type patterns ('is " + tr + "')", tr.Start, tr.End);
                p = save;
                e.Pattern = ParseBinary(LevelAdditive);
            }
            if (IsWord("or") || IsWord("and")) throw NotSupported("'" + Cur.Text + "' patterns", Cur.Start, Cur.End);
            e.End = Prev.End;
            return e;
        }

        CExpr ParseSwitchExprLevel()
        {
            var e = ParseUnary();
            while (IsWord("switch") || Is("switch"))
            {
                var kw = Advance();
                var sx = new CSwitchExpr { Selector = e, Start = e.Start, KwStart = kw.Start, KwEnd = kw.End };
                Expect("{");
                while (!Is("}"))
                {
                    if (Cur.Kind == CTk.EOF) throw Eof();
                    int s = Cur.Start;
                    bool discard = false;
                    List<CExpr> pats = new List<CExpr>();
                    if (IsWord("_") && (IsAt(1, "=>"))) { Advance(); discard = true; }
                    else pats = ParsePatterns("=>");
                    if (IsWord("when")) throw NotSupported("'when' clauses", Cur.Start, Cur.End);
                    Expect("=>");
                    var v = ParseExpr();
                    sx.Arms.Add((pats, discard, v, s, v.End));
                    if (!Accept(",")) break;
                }
                Expect("}");
                sx.End = Prev.End;
                e = sx;
            }
            if (Is("..")) throw NotSupported("ranges (a..b)", Cur.Start, Cur.End);
            return e;
        }

        CExpr ParseUnary()
        {
            var t = Cur;
            if (t.Kind == CTk.Op)
            {
                switch (t.Text)
                {
                    case "+": case "-": case "!":
                        {
                            Advance();
                            var operand = ParseUnary();
                            return new CUnary { Op = t.Text, Operand = operand, Start = t.Start, End = operand.End, OpEnd = t.End };
                        }
                    case "~": throw NotSupported("the operator '~'", t.Start, t.End);
                    case "&": case "*": throw NotSupported("pointers", t.Start, t.End);
                    case "^":
                        {
                            Advance();
                            var operand = ParseUnary();
                            return new CFromEnd { Operand = operand, Start = t.Start, End = operand.End };
                        }
                    case "++": case "--":
                        {
                            Advance();
                            var operand = ParseUnary();
                            return new CIncDec { Op = t.Text, Prefix = true, Target = operand, Start = t.Start, End = operand.End, OpStart = t.Start, OpEnd = t.End };
                        }
                    case "(":
                        {
                            var cast = TryParseCast();
                            if (cast != null) return cast;
                            break;
                        }
                }
            }
            if (t.Kind == CTk.Identifier && t.Text == "await") throw NotSupported("'await'", t.Start, t.End);
            return ParsePostfix(ParsePrimary());
        }

        CExpr TryParseCast()
        {
            if (IsLambdaStart()) return null;
            int save = p;
            var open = Advance();
            if (IsPredefinedKw(Cur))
            {
                var tr = TryParseType(out _, true);
                if (tr != null && Is(")"))
                {
                    var close = Advance();
                    var operand = ParseUnary();
                    return new CCast { TypeRef = tr, Operand = operand, Start = open.Start, End = operand.End, CloseParen = close.End };
                }
                p = save; return null;
            }
            if (IsIdent)
            {
                var tr = TryParseType(out _, true);
                if (tr != null && Is(")"))
                {
                    var next = Peek();
                    bool castFollows = next.Kind == CTk.Identifier || next.Kind == CTk.StringLit || next.Kind == CTk.InterpStr || next.Kind == CTk.IntLit || next.Kind == CTk.DoubleLit || next.Kind == CTk.CharLit
                        || (next.Kind == CTk.Op && (next.Text == "(" || next.Text == "!" || next.Text == "~"))
                        || (next.Kind == CTk.Keyword && (next.Text == "this" || next.Text == "new" || next.Text == "true" || next.Text == "false" || next.Text == "null" || next.Text == "default" || Predefined.Contains(next.Text)));
                    if (castFollows)
                    {
                        var close = Advance();
                        var operand = IsLambdaStart() ? ParseLambda() : ParseUnary();
                        return new CCast { TypeRef = tr, Operand = operand, Start = open.Start, End = operand.End, CloseParen = close.End };
                    }
                }
            }
            p = save;
            return null;
        }

        bool IsLambdaStart()
        {
            if (!(Is("(") || Is("static") || (IsIdent && (IsAt(1, "=>") || (Cur.Text == "async"))))) return false;
            int i = 0;
            if (Is("static") || (IsWord("async") && (IsAt(1, "(") || Peek().Kind == CTk.Identifier))) i = 1;
            var t = Peek(i);
            if (t.Kind == CTk.Identifier && IsAt(i + 1, "=>")) return true;
            if (!IsAt(i, "(")) return false;
            // find the matching ')' and check for '=>'
            int d = 0;
            for (int j = i; p + j < toks.Count; j++)
            {
                var k = Peek(j);
                if (k.Kind == CTk.EOF) return false;
                if (k.Kind == CTk.Op && k.Text == "(") d++;
                else if (k.Kind == CTk.Op && k.Text == ")") { d--; if (d == 0) return IsAt(j + 1, "=>"); }
                else if (k.Kind == CTk.Op && (k.Text == ";" || k.Text == "{" || k.Text == "}")) return false;
            }
            return false;
        }

        CExpr ParseLambda()
        {
            var lam = new CLambda { Start = Cur.Start };
            if (Is("static")) { Advance(); lam.IsStatic = true; }
            if (IsWord("async")) throw NotSupported("async lambdas", Cur.Start, Cur.End);
            if (IsIdent && IsAt(1, "=>"))
            {
                var n = Advance();
                lam.Params.Add((null, n.Text, n.Start, n.End));
            }
            else
            {
                Expect("(");
                if (!Is(")"))
                {
                    do
                    {
                        if (Is("ref") || Is("out") || Is("in")) throw NotSupported("'" + Cur.Text + "' parameters", Cur.Start, Cur.End);
                        CTypeRef pt = null;
                        if (!(IsIdent && (IsAt(1, ",") || IsAt(1, ")")))) pt = ParseType();
                        var n = ExpectIdent();
                        lam.Params.Add((pt, n.Text, n.Start, n.End));
                    } while (Accept(","));
                }
                Expect(")");
            }
            Expect("=>");
            lam.ArrowEnd = Prev.End;
            if (Is("{")) lam.BodyBlock = ParseBlock();
            else lam.BodyExpr = ParseExpr();
            lam.End = Prev.End;
            return lam;
        }

        CExpr ParsePrimary()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case CTk.IntLit: Advance(); return IntLiteral(t);
                case CTk.DoubleLit: Advance(); return DoubleLiteral(t);
                case CTk.StringLit: Advance(); return new CLiteral { Kind = CLitKind.String, StrValue = t.StrValue ?? "", Start = t.Start, End = t.End };
                case CTk.CharLit: Advance(); return new CLiteral { Kind = CLitKind.Char, StrValue = t.StrValue ?? "", Start = t.Start, End = t.End };
                case CTk.InterpStr: Advance(); return Interpolated(t);
                case CTk.Identifier:
                    {
                        if (IsAt(1, "=>")) return ParseLambda();
                        Advance();
                        if (t.Text == "_" ) return new CName { Name = "_", Start = t.Start, End = t.End };
                        if (t.Text == "nameof" && Is("(")) throw NotSupported("'nameof'", t.Start, t.End);
                        if (Is("<") && LooksLikeGenericCall()) throw NotSupported("explicit generic method calls (" + t.Text + "<...>)", t.Start, t.End);
                        return new CName { Name = t.Text, Start = t.Start, End = t.End };
                    }
                case CTk.Keyword:
                    switch (t.Text)
                    {
                        case "true": case "false":
                            Advance(); return new CLiteral { Kind = CLitKind.Bool, BoolValue = t.Text == "true", Start = t.Start, End = t.End };
                        case "null":
                            Advance(); return new CLiteral { Kind = CLitKind.Null, Start = t.Start, End = t.End };
                        case "this": throw NotSupported("'this' (instances)", t.Start, t.End);
                        case "base": throw NotSupported("'base'", t.Start, t.End);
                        case "new": return ParseNew();
                        case "default":
                            {
                                Advance();
                                var d = new CDefault { Start = t.Start, End = t.End };
                                if (Is("(")) { Advance(); d.TypeRef = ParseType(); Expect(")"); d.End = Prev.End; }
                                return d;
                            }
                        case "typeof": case "sizeof": throw NotSupported("'" + t.Text + "'", t.Start, t.End);
                        case "checked": case "unchecked": throw NotSupported("'" + t.Text + "'", t.Start, t.End);
                        case "delegate": throw NotSupported("anonymous methods (use a lambda: () => ...)", t.Start, t.End);
                        case "stackalloc": throw NotSupported("'stackalloc'", t.Start, t.End);
                        case "throw": throw NotSupported("'throw'", t.Start, t.End);
                    }
                    if (Predefined.Contains(t.Text))
                    {
                        Advance();
                        if (Is(".")) return new CPredefined { Name = t.Text, Start = t.Start, End = t.End };
                        throw Err("CS1525: Invalid expression term '" + t.Text + "'", t.Start, t.End);
                    }
                    throw Invalid(t);
                case CTk.Op:
                    if (t.Text == "(")
                    {
                        if (IsLambdaStart()) return ParseLambda();
                        Advance();
                        var inner = ParseExpr();
                        if (Is(","))
                        {
                            var tup = new CTupleExpr { Start = t.Start };
                            tup.Items.Add(inner);
                            while (Accept(",")) tup.Items.Add(ParseExpr());
                            Expect(")");
                            tup.End = Prev.End;
                            return tup;
                        }
                        Expect(")");
                        return new CParen { Inner = inner, Start = t.Start, End = Prev.End };
                    }
                    if (t.Text == "[")
                    {
                        Advance();
                        var col = new CCollection { Start = t.Start };
                        while (!Is("]"))
                        {
                            if (Is("..")) { col.HasSpread = true; throw NotSupported("spread elements (..) in collection expressions", Cur.Start, Cur.End); }
                            col.Items.Add(ParseExpr());
                            if (!Accept(",")) break;
                        }
                        Expect("]");
                        col.End = Prev.End;
                        return col;
                    }
                    if (t.Text == "{") throw Err("CS1525: Invalid expression term '{'", t.Start, t.End);
                    if (t.Text == "$" || t.Text == "@") throw Err("CS1056: Unexpected character '" + t.Text + "'", t.Start, t.End);
                    throw Invalid(t);
                case CTk.EOF:
                    throw Err("CS1733: Expected expression", Math.Max(0, Prev.Start), Prev.End);
            }
            throw Invalid(t);
        }

        bool LooksLikeGenericCall()
        {
            int save = p;
            Advance();
            int d = 1;
            for (int j = 0; j < 20 && d > 0; j++)
            {
                if (Is("<")) d++;
                else if (Is(">")) d--;
                else if (!(IsIdent || IsPredefinedKw(Cur) || Is(",") || Is("[") || Is("]") || Is("?"))) { p = save; return false; }
                Advance();
            }
            bool r = d == 0 && Is("(");
            p = save;
            return r;
        }

        CExpr Interpolated(CsToken t)
        {
            var ie = new CInterp { Start = t.Start, End = t.End };
            foreach (var part in t.Parts ?? new List<InterpPart>())
            {
                if (!part.IsHole) { ie.Parts.Add(part.Text); continue; }
                if (part.HasFormat)
                    throw NotSupported("format specifiers and alignment in interpolated strings ({x:F2}, {x,5})", part.FormatStart, Math.Min(part.FormatStart + 1, src.Length));
                var text = src.Substring(part.Start, Math.Max(0, part.End - part.Start));
                if (text.Trim().Length == 0) throw Err("CS1733: Expected expression", Math.Max(0, part.Start - 1), part.Start);
                var lexer = new CsLexer(text);
                var tokens = lexer.LexSignificant();
                foreach (var k in tokens) { k.Start += part.Start; k.End += part.Start; }
                if (lexer.Error != null) throw Err(lexer.Error.Message, lexer.Error.Start + part.Start, lexer.Error.End + part.Start);
                var sub = new CsParser(src, tokens);
                var e = sub.ParseExpr();
                if (sub.Cur.Kind != CTk.EOF) throw Err("CS1003: Syntax error, '}' expected", sub.Cur.Start, sub.Cur.End);
                ie.Parts.Add(e);
            }
            return ie;
        }

        CLiteral IntLiteral(CsToken t)
        {
            string s = t.Text.Replace("_", "");
            string lower = s.ToLowerInvariant();
            if (lower.EndsWith("ul") || lower.EndsWith("lu") || lower.EndsWith("u")) throw Err("unsigned integers (uint/ulong) are not supported in JavaFarmer; use int", t.Start, t.End);
            if (lower.EndsWith("l")) throw Err("long is not supported in JavaFarmer; use int or double", t.Start, t.End);
            long v;
            bool ok;
            if (lower.StartsWith("0x")) ok = long.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) && s.Length > 2;
            else if (lower.StartsWith("0b"))
            {
                v = 0; ok = s.Length > 2;
                foreach (var ch in s.Substring(2)) { if (ch != '0' && ch != '1') { ok = false; break; } v = v * 2 + (ch - '0'); if (v > uint.MaxValue) { ok = false; break; } }
            }
            else ok = long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
            if (!ok)
            {
                foreach (var ch in lower) if (char.IsLetter(ch) && !lower.StartsWith("0x") && !lower.StartsWith("0b")) throw Err("CS1013: Invalid number", t.Start, t.End);
                throw Err("CS1021: Integral constant is too large", t.Start, t.End);
            }
            if (v > 2147483648L) throw Err("integer literals beyond the int range (long) are not supported in JavaFarmer; use double (" + s + ".0)", t.Start, t.End);
            return new CLiteral { Kind = CLitKind.Int, IntValue = v, Start = t.Start, End = t.End };
        }

        CLiteral DoubleLiteral(CsToken t)
        {
            string s = t.Text.Replace("_", "");
            char last = char.ToLowerInvariant(s[s.Length - 1]);
            if (last == 'f') throw Err("float is not supported in JavaFarmer; use double", t.Start, t.End);
            if (last == 'm') throw Err("decimal is not supported in JavaFarmer; use double", t.Start, t.End);
            if (last == 'd') s = s.Substring(0, s.Length - 1);
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || char.IsLetter(s[s.Length - 1]))
                throw Err("CS1013: Invalid number", t.Start, t.End);
            return new CLiteral { Kind = CLitKind.Double, DoubleValue = v, Start = t.Start, End = t.End };
        }

        List<CArg> ParseArgs(out bool any)
        {
            var args = new List<CArg>();
            Expect("(");
            any = true;
            if (!Is(")"))
            {
                do
                {
                    int s = Cur.Start;
                    var a = new CArg { Start = s };
                    if (IsIdent && IsAt(1, ":") && !IsAt(2, ":")) { a.Name = Advance().Text; Advance(); }
                    if (Is("ref") || Is("in")) throw NotSupported("'" + Cur.Text + "' arguments", Cur.Start, Cur.End);
                    if (Is("out"))
                    {
                        a.Modifier = Advance().Text;
                        if (IsWord("var") && Peek().Kind == CTk.Identifier) { Advance(); var n = Advance(); a.OutType = new CTypeRef { Name = "var", Start = n.Start, End = n.Start }; a.OutName = n.Text; a.OutNameStart = n.Start; a.OutNameEnd = n.End; }
                        else if (IsIdent && (IsAt(1, ")") || IsAt(1, ","))) { var n = Advance(); a.OutName = n.Text; a.OutNameStart = n.Start; a.OutNameEnd = n.End; a.Value = new CName { Name = n.Text, Start = n.Start, End = n.End }; }
                        else
                        {
                            a.OutType = ParseType();
                            var n = ExpectIdent();
                            a.OutName = n.Text; a.OutNameStart = n.Start; a.OutNameEnd = n.End;
                        }
                    }
                    else a.Value = IsLambdaStart() ? ParseLambda() : ParseExpr();
                    a.End = Prev.End;
                    args.Add(a);
                } while (Accept(","));
            }
            if (!Is(")"))
            {
                if (Cur.Kind == CTk.EOF) throw Err("CS1026: ) expected", Prev.Start, Prev.End);
                throw Err("CS1026: ) expected", Prev.Start, Prev.End);
            }
            Advance();
            return args;
        }

        CExpr ParsePostfix(CExpr e)
        {
            while (true)
            {
                if (Is("."))
                {
                    Advance();
                    if (Is("out") && e is CName sysName && sysName.Name == "System") throw Err("CS1041: Identifier expected; 'out' is a keyword (System.out.println is Java: C# writes Console.WriteLine(...))", Cur.Start, Cur.End);
                    var name = ExpectIdent();
                    var m = new CMember { Target = e, Name = name.Text, NameStart = name.Start, NameEnd = name.End, Start = e.Start, End = name.End };
                    if (Is("<") && LooksLikeGenericCall()) throw NotSupported("explicit generic method calls (" + name.Text + "<...>)", name.Start, name.End);
                    e = m;
                    continue;
                }
                if (Is("?.")) throw NotSupported("the null-conditional operator '?.'", Cur.Start, Cur.End);
                if (Is("("))
                {
                    var lp = Cur.Start;
                    var call = new CCall { Callee = e, Start = e.Start, LParen = lp };
                    call.Args = ParseArgs(out _);
                    call.End = Prev.End;
                    e = call;
                    continue;
                }
                if (Is("["))
                {
                    Advance();
                    var ix = new CIndex { Target = e, Start = e.Start };
                    do ix.Indices.Add(ParseExpr()); while (Accept(","));
                    Expect("]");
                    ix.End = Prev.End;
                    e = ix;
                    continue;
                }
                if (Is("++") || Is("--"))
                {
                    var op = Advance();
                    e = new CIncDec { Op = op.Text, Prefix = false, Target = e, Start = e.Start, End = op.End, OpStart = op.Start, OpEnd = op.End };
                    continue;
                }
                if (Is("!") && (IsAt(1, ".") || IsAt(1, ";") || IsAt(1, ")") || IsAt(1, ",")) && Cur.Start == e.End)
                    throw NotSupported("the null-forgiving operator '!'", Cur.Start, Cur.End);
                if (Is("->")) throw NotSupported("pointers", Cur.Start, Cur.End);
                return e;
            }
        }

        CExpr ParseNew()
        {
            var kw = Advance();
            if (Is("("))
            {
                // target-typed new()
                var tn = new CNew { Start = kw.Start };
                tn.Args = ParseArgs(out tn.HasArgs);
                if (Is("{")) ParseObjectInit(tn);
                tn.End = Prev.End;
                return tn;
            }
            if (Is("["))
            {
                // new[] { ... }
                Advance();
                int rank = 1; while (Accept(",")) rank++;
                Expect("]");
                if (!Is("{")) throw Err("CS1514: { expected", Prev.Start, Prev.End);
                var na0 = new CNewArray { Start = kw.Start, Elem = null, FirstRank = rank };
                na0.Init = ParseArrayInit();
                na0.End = Prev.End;
                return na0;
            }
            if (Is("{")) throw NotSupported("anonymous types", Cur.Start, Cur.End);
            if (!(IsIdent || IsPredefinedKw(Cur))) throw Err("CS1526: A new expression requires an argument list or (), [], or {} after type", kw.Start, kw.End);
            var nameTok = Cur;
            var tr = new CTypeRef { Start = Cur.Start };
            if (IsPredefinedKw(Cur)) { Advance(); tr.Name = nameTok.Text; tr.NameStart = nameTok.Start; tr.NameEnd = nameTok.End; }
            else
            {
                var n = Advance();
                tr.Name = n.Text; tr.NameStart = n.Start; tr.NameEnd = n.End;
                while (Is(".") && Peek().Kind == CTk.Identifier) { Advance(); var part = Advance(); tr.Name = part.Text; tr.NameStart = part.Start; tr.NameEnd = part.End; }
                if (Is("<"))
                {
                    Advance();
                    tr.Args = new List<CTypeRef>();
                    do tr.Args.Add(ParseType()); while (Accept(","));
                    Expect(">");
                }
            }
            if (Is("?")) { Advance(); tr.Nullable = true; }
            tr.End = Prev.End;
            if (Is("["))
            {
                var na = new CNewArray { Start = kw.Start, Elem = tr };
                Advance();
                if (Is("]") || Is(","))
                {
                    int rank = 1; while (Accept(",")) rank++;
                    Expect("]");
                    na.FirstRank = rank;
                }
                else
                {
                    na.Sizes.Add(ParseExpr());
                    while (Accept(",")) na.Sizes.Add(ParseExpr());
                    Expect("]");
                    na.FirstRank = na.Sizes.Count;
                }
                while (Is("["))
                {
                    Advance();
                    int rank = 1; while (Accept(",")) rank++;
                    if (!Is("]")) throw Err("CS0178: Invalid rank specifier: expected ',' or ']'", Cur.Start, Cur.End);
                    Advance();
                    na.Ranks.Add(rank);
                }
                if (Is("{")) na.Init = ParseArrayInit();
                else if (na.Sizes.Count == 0) throw Err("CS1586: Array creation must have array size or array initializer", kw.Start, Prev.End);
                na.End = Prev.End;
                return na;
            }
            var ne = new CNew { Start = kw.Start, TypeRef = tr };
            if (Is("(")) ne.Args = ParseArgs(out ne.HasArgs);
            if (Is("{")) ParseObjectInit(ne);
            else if (!ne.HasArgs) throw Err("CS1526: A new expression requires an argument list or (), [], or {} after type", kw.Start, Prev.End);
            ne.End = Prev.End;
            return ne;
        }

        /// <summary>Collection initializer: { 1, 2 } / { {k, v} } / { [k] = v }.</summary>
        void ParseObjectInit(CNew ne)
        {
            var open = Advance();
            ne.InitStart = open.Start;
            ne.Items = new List<CExpr>();
            ne.Pairs = new List<(CExpr, CExpr, bool)>();
            while (!Is("}"))
            {
                if (Cur.Kind == CTk.EOF) throw Eof();
                if (Is("["))
                {
                    Advance();
                    var k = ParseExpr();
                    Expect("]");
                    Expect("=");
                    var v = ParseExpr();
                    ne.Pairs.Add((k, v, true));
                }
                else if (Is("{"))
                {
                    Advance();
                    var k = ParseExpr();
                    Expect(",");
                    var v = ParseExpr();
                    Expect("}");
                    ne.Pairs.Add((k, v, false));
                }
                else if (IsIdent && IsAt(1, "=")) throw NotSupported("object initializers", Cur.Start, Cur.End);
                else ne.Items.Add(ParseExpr());
                if (!Accept(",")) break;
            }
            Expect("}");
        }

        CArrayInit ParseArrayInit()
        {
            var open = Expect("{");
            var ai = new CArrayInit { Start = open.Start };
            while (!Is("}"))
            {
                if (Cur.Kind == CTk.EOF) throw Eof();
                ai.Items.Add(Is("{") ? ParseArrayInit() : ParseExpr());
                if (!Accept(",")) break;
            }
            Expect("}");
            ai.End = Prev.End;
            return ai;
        }
    }
}
