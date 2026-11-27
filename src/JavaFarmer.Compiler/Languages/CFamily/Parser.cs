using System;
using System.Collections.Generic;
using System.Globalization;

namespace JavaFarmer.Compiler.CFamily
{
    /// <summary>
    /// Recursive-descent parser for the C / C++ subset (both dialects; Binder rejects what a dialect lacks).
    /// Stops at the first syntax error (gcc-style message, span on the offending token).
    /// </summary>
    internal sealed class Parser
    {
        sealed class Abort : Exception { }

        readonly List<Token> toks;
        readonly string src;
        readonly Dialect d;
        int p;
        public Diagnostic Error;

        public static readonly HashSet<string> GameTypes = new HashSet<string>
            { "Entity", "Ground", "Item", "Hat", "Unlock", "Leaderboard", "Direction", "Drone", "Position", "Companion" };
        /// <summary>C++ standard library types JavaFarmer knows (std:: or with using namespace std).</summary>
        public static readonly HashSet<string> StdTypes = new HashSet<string>
            { "vector", "map", "unordered_map", "set", "unordered_set", "function", "optional", "string", "pair", "array", "size_t", "tuple", "list", "deque", "any", "string_view", "multimap", "multiset" };
        static readonly HashSet<string> TemplateNames = new HashSet<string>
            { "vector", "map", "unordered_map", "set", "unordered_set", "function", "optional", "pair", "array", "tuple", "list", "deque", "multimap", "multiset" };
        /// <summary>farm.h macros whose first argument(s) are types.</summary>
        public static readonly Dictionary<string, int> TypeArgMacros = new Dictionary<string, int>
            { ["list_new"] = 1, ["list_of"] = 1, ["set_new"] = 1, ["set_of"] = 1, ["map_new"] = 2, ["map_of"] = 2 };
        static readonly HashSet<string> TypeKeywords = new HashSet<string>
            { "int", "long", "short", "unsigned", "signed", "double", "float", "bool", "void", "char", "auto", "_Bool", "wchar_t", "char8_t", "char16_t", "char32_t" };
        static readonly HashSet<string> Qualifiers = new HashSet<string> { "const", "volatile", "static", "extern", "constexpr", "inline", "register", "restrict", "thread_local", "mutable", "consteval", "constinit" };

        Parser(List<Token> tokens, string source, Dialect dialect) { toks = tokens; src = source; d = dialect; }

        public static Unit ParseUnit(LexResult lex, string source, Dialect dialect, out Diagnostic error)
        {
            var ps = new Parser(lex.Tokens, source, dialect);
            var u = new Unit { Length = source.Length };
            try { ps.Unit(u); }
            catch (Abort) { }
            error = ps.Error;
            return u;
        }

        /// <summary>Parses a single expression from a token list (used for #define values).</summary>
        public static Expr ParseLoneExpr(List<Token> tokens, string source, Dialect dialect, out Diagnostic error)
        {
            var ps = new Parser(tokens, source, dialect);
            Expr e = null;
            try
            {
                e = ps.Expression();
                if (ps.Cur.Kind != TK.EOF) ps.Fail("expected end of macro value before " + ps.Describe(ps.Cur), ps.Cur);
            }
            catch (Abort) { }
            error = ps.Error;
            return ps.Error == null ? e : null;
        }

        // ------------------------------------------------------------------ tokens
        Token Cur => toks[p];
        Token At(int k) => toks[Math.Min(p + k, toks.Count - 1)];
        Token Prev => toks[Math.Max(0, p - 1)];
        Token Next() { var t = toks[p]; if (p < toks.Count - 1) p++; return t; }
        bool IsP(string s) => Cur.Kind == TK.Punct && Cur.Text == s;
        bool IsP(Token t, string s) => t.Kind == TK.Punct && t.Text == s;
        bool IsK(string s) => Cur.Kind == TK.Keyword && Cur.Text == s;
        bool IsK(Token t, string s) => t.Kind == TK.Keyword && t.Text == s;
        bool IsId(string s) => Cur.Kind == TK.Ident && Cur.Text == s;
        bool AcceptP(string s) { if (IsP(s)) { Next(); return true; } return false; }

        string Describe(Token t)
        {
            switch (t.Kind)
            {
                case TK.EOF: return "end of input";
                case TK.Punct: return "'" + t.Text + "' token";
                case TK.Int: case TK.Float: return "numeric constant";
                case TK.String: return "string constant";
                case TK.Char: return "character constant";
                case TK.Preproc: return "'#' token";
            }
            return "'" + t.Text + "'";
        }

        Exception Fail(string msg, Token at) => Fail(msg, at.Start, Math.Max(at.End, at.Start + (at.Kind == TK.EOF ? 0 : 1)));
        Exception Fail(string msg, int s, int e)
        {
            if (Error == null) Error = new Diagnostic(msg, s, Math.Max(s, e));
            throw new Abort();
        }

        /// <summary>A missing ';' or ')' is reported at the token before the gap (where the fix goes).</summary>
        void Expect(string what)
        {
            if (IsP(what)) { Next(); return; }
            if ((what == ";" || what == ")" || what == "]") && p > 0 && Cur.NewlineBefore)
                throw Fail("expected '" + what + "' before " + Describe(Cur), Prev);
            throw Fail("expected '" + what + "' before " + Describe(Cur), Cur);
        }

        int Ident(out string name, string context = "identifier")
        {
            if (Cur.Kind != TK.Ident)
            {
                if (Cur.Kind == TK.Keyword) throw Fail("expected " + context + " before '" + Cur.Text + "'", Cur);
                throw Fail("expected " + context + " before " + Describe(Cur), Cur);
            }
            var t = Next();
            name = t.Text;
            return t.Start;
        }

        // ================================================================== unit
        void Unit(Unit u)
        {
            while (Cur.Kind != TK.EOF)
            {
                if (Cur.Kind == TK.Preproc) { u.Items.Add(Directive(Next())); continue; }
                if (IsP(";")) { Next(); continue; }
                if (IsK("using")) { u.Items.Add(Using()); continue; }
                if (IsK("struct") || IsK("typedef") || IsK("enum") || IsK("union") || IsK("class") || IsK("namespace") || IsK("template"))
                {
                    var t = Cur;
                    u.Items.Add(new Unsupported { Start = t.Start, End = t.End, What = t.Text, Message = t.Text + (t.Text == "typedef" ? "" : " declarations") + " are not supported in JavaFarmer" });
                    SkipDeclaration();
                    continue;
                }
                u.Items.Add(External());
            }
        }

        void SkipDeclaration()
        {
            int depth = 0;
            while (Cur.Kind != TK.EOF)
            {
                if (IsP("{")) depth++;
                else if (IsP("}")) { depth--; if (depth <= 0) { Next(); if (IsP(";")) Next(); return; } }
                else if (IsP(";") && depth == 0) { Next(); return; }
                Next();
            }
        }

        Node Using()
        {
            var t = Next();
            if (IsK("namespace"))
            {
                Next();
                int s = Ident(out var ns, "namespace name");
                var full = ns;
                while (IsP("::")) { Next(); Ident(out var more, "namespace name"); full += "::" + more; }
                Expect(";");
                return new UsingNamespace { Start = t.Start, End = Prev.End, Ns = full };
            }
            var res = new Unsupported { Start = t.Start, What = "using", Message = "using declarations and type aliases are not supported in JavaFarmer (only using namespace std;)" };
            while (!IsP(";") && Cur.Kind != TK.EOF) Next();
            res.End = Cur.End;
            if (IsP(";")) Next();
            return res;
        }

        Node Directive(Token t)
        {
            var text = t.Text;
            int i = 1;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            int ws = i;
            while (i < text.Length && Lexer.IsIdentPart(text[i])) i++;
            var word = text.Substring(ws, i - ws);
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            if (word == "include")
            {
                if (i < text.Length && (text[i] == '<' || text[i] == '"'))
                {
                    char close = text[i] == '<' ? '>' : '"';
                    int e = text.IndexOf(close, i + 1);
                    if (e > i)
                        return new Include { Start = t.Start, End = t.End, Path = text.Substring(i + 1, e - i - 1), System = close == '>', PathStart = t.Start + i, PathEnd = t.Start + e + 1 };
                }
                return new Unsupported { Start = t.Start, End = t.End, What = "#include", Message = "#include expects \"FILENAME\" or <FILENAME>" };
            }
            if (word == "define")
            {
                var def = new Define { Start = t.Start, End = t.End };
                int ns = i;
                while (i < text.Length && Lexer.IsIdentPart(text[i])) i++;
                def.Name = text.Substring(ns, i - ns);
                def.NameStart = t.Start + ns; def.NameEnd = t.Start + i;
                if (def.Name.Length == 0) { def.Error = "macro names must be identifiers"; def.ErrStart = t.Start; def.ErrEnd = t.End; return def; }
                if (i < text.Length && text[i] == '(') { def.Error = "function-like macros are not supported in JavaFarmer (use a function)"; def.ErrStart = t.Start; def.ErrEnd = t.End; return def; }
                var rest = text.Substring(i);
                if (rest.Trim().Length == 0) { def.Error = "#define without a value is not supported in JavaFarmer"; def.ErrStart = t.Start; def.ErrEnd = t.End; return def; }
                var vt = new List<Token>();
                var sub = Lexer.Lex(rest, d, false);
                foreach (var tk in sub.Tokens) { tk.Start += t.Start + i; tk.End += t.Start + i; tk.NewlineBefore = false; if (tk.Kind != TK.EOF && tk.Kind != TK.Preproc) vt.Add(tk); }
                if (sub.Errors.Count > 0) { def.Error = sub.Errors[0].Message; def.ErrStart = sub.Errors[0].Start + t.Start + i; def.ErrEnd = sub.Errors[0].End + t.Start + i; return def; }
                vt.Add(new Token { Kind = TK.EOF, Text = "", Start = t.End, End = t.End });
                def.Value = ParseLoneExpr(vt, src, d, out var err);
                if (err != null) { def.Error = err.Message; def.ErrStart = err.Start; def.ErrEnd = err.End; }
                return def;
            }
            return new OtherDirective { Start = t.Start, End = t.End, Text = word };
        }

        // ================================================================== declarations

        bool IsQualifier(Token t) => t.Kind == TK.Keyword && Qualifiers.Contains(t.Text);

        /// <summary>True when the tokens at offset k start a type (after qualifiers).</summary>
        bool StartsType(int k = 0)
        {
            while (IsQualifier(At(k))) k++;
            var t = At(k);
            if (t.Kind == TK.Keyword) return TypeKeywords.Contains(t.Text) || t.Text == "struct" || t.Text == "enum" || t.Text == "typename";
            if (t.Kind != TK.Ident) return false;
            if (t.Text == "std" && IsP(At(k + 1), "::")) return At(k + 2).Kind == TK.Ident && StdTypes.Contains(At(k + 2).Text);
            if (t.Text == "farm" && IsP(At(k + 1), "::")) return At(k + 2).Kind == TK.Ident && GameTypes.Contains(At(k + 2).Text);
            if (GameTypes.Contains(t.Text) || t.Text == "string") return true;
            if (d == Dialect.C) return (t.Text == "List" || t.Text == "Map" || t.Text == "Set") && IsP(At(k + 1), "(");
            if (StdTypes.Contains(t.Text) && t.Text != "list" && t.Text != "array" && t.Text != "any" && t.Text != "tuple") return true;
            return false;
        }

        /// <summary>Declaration specifiers + base type (pointers/refs after it stay for the declarator unless inType).</summary>
        TypeSyn TypeSpec(out bool isStatic, out bool isExtern, out bool isConstexpr, out bool isInline, bool inTemplate = false)
        {
            isStatic = isExtern = isConstexpr = isInline = false;
            var ts = new TypeSyn { Start = Cur.Start };
            bool sawBase = false;
            while (true)
            {
                var t = Cur;
                if (t.Kind == TK.Keyword)
                {
                    switch (t.Text)
                    {
                        case "const": ts.Const = true; Next(); continue;
                        case "volatile": ts.Volatile = true; Next(); continue;
                        case "static": isStatic = true; Next(); continue;
                        case "extern": isExtern = true; Next(); continue;
                        case "constexpr": case "consteval": case "constinit": isConstexpr = true; ts.Const = true; Next(); continue;
                        case "inline": isInline = true; Next(); continue;
                        case "register": case "restrict": case "thread_local": case "mutable": Next(); continue;
                        case "unsigned": ts.Unsigned = true; Next(); if (!sawBase) ts.Name = ts.Name ?? "int"; continue;
                        case "signed": ts.Signed = true; Next(); if (!sawBase) ts.Name = ts.Name ?? "int"; continue;
                        case "short": ts.Short = true; Next(); ts.Name = ts.Name ?? "int"; continue;
                        case "long":
                            Next();
                            if (ts.Long) ts.LongLong = true; else ts.Long = true;
                            if (ts.Name == null || ts.Name == "int") ts.Name = "int";
                            continue;
                        case "int": case "char": case "double": case "float": case "bool": case "void": case "auto": case "_Bool": case "wchar_t": case "char8_t": case "char16_t": case "char32_t":
                            if (sawBase && !(t.Text == "int" && (ts.Long || ts.Short || ts.Unsigned || ts.Signed)) && !(t.Text == "double" && ts.Long))
                                throw Fail("two or more data types in declaration specifiers", t);
                            ts.Name = t.Text == "_Bool" ? "bool" : t.Text;
                            sawBase = true; Next();
                            continue;
                        case "struct": case "enum": case "union": case "class": case "typename":
                            throw Fail(t.Text + " types are not supported in JavaFarmer", t);
                    }
                    break;
                }
                if (t.Kind == TK.Ident && !sawBase && ts.Name == null)
                {
                    // std::vector<...>, farm::Entity, Entity, string, List(int)
                    if (t.Text == "std" && IsP(At(1), "::") && At(2).Kind == TK.Ident && StdTypes.Contains(At(2).Text))
                    {
                        Next(); Next();
                        ts.Std = true;
                        NamedType(ts);
                        sawBase = true;
                        continue;
                    }
                    if (t.Text == "farm" && IsP(At(1), "::") && At(2).Kind == TK.Ident && GameTypes.Contains(At(2).Text)) { Next(); Next(); }
                    if (GameTypes.Contains(Cur.Text) || Cur.Text == "string" || (d == Dialect.Cpp && StdTypes.Contains(Cur.Text)) ||
                        (d == Dialect.C && (Cur.Text == "List" || Cur.Text == "Map" || Cur.Text == "Set") && IsP(At(1), "(")))
                    {
                        NamedType(ts);
                        sawBase = true;
                        continue;
                    }
                }
                break;
            }
            if (ts.Name == null)
            {
                if (ts.Const || ts.Volatile || isStatic || isExtern || isConstexpr) throw Fail("expected declaration specifiers before " + Describe(Cur), Cur);
                throw Fail("expected a type before " + Describe(Cur), Cur);
            }
            if (inTemplate)
            {
                while (IsP("*") || IsK("const") || IsP("&") || IsP("&&"))
                {
                    if (IsP("*")) ts.Pointers++;
                    else if (IsP("&")) ts.Ref = true;
                    else if (IsP("&&")) ts.RRef = true;
                    else ts.Const = true;
                    Next();
                }
            }
            ts.End = Prev.End;
            ts.Written = src.Substring(ts.Start, Math.Max(0, ts.End - ts.Start));
            return ts;
        }

        /// <summary>The name of a library / game type at Cur, with its template or macro arguments.</summary>
        void NamedType(TypeSyn ts)
        {
            var t = Next();
            ts.Name = t.Text == "string" && (ts.Std || d == Dialect.Cpp) ? "string" : t.Text == "string" ? "cstring" : t.Text;
            if (d == Dialect.C && (t.Text == "List" || t.Text == "Map" || t.Text == "Set"))
            {
                Expect("(");
                ts.Args.Add(TemplateArg());
                while (AcceptP(",")) ts.Args.Add(TemplateArg());
                Expect(")");
                return;
            }
            if (TemplateNames.Contains(t.Text) && IsP("<"))
            {
                Next();
                if (t.Text == "function")
                {
                    ts.FnRet = TemplateArg();
                    Expect("(");
                    ts.FnParams = new List<TypeSyn>();
                    if (!IsP(")"))
                    {
                        if (IsK("void") && IsP(At(1), ")")) Next();
                        else
                        {
                            ts.FnParams.Add(TemplateArg());
                            while (AcceptP(",")) ts.FnParams.Add(TemplateArg());
                        }
                    }
                    Expect(")");
                }
                else if (!IsP(">") && !IsP(">>"))
                {
                    ts.Args.Add(TemplateArg());
                    while (AcceptP(",")) ts.Args.Add(TemplateArg());
                }
                CloseAngle();
            }
        }

        TypeSyn TemplateArg()
        {
            if (Cur.Kind == TK.Int)
            {
                var t = Next();
                return new TypeSyn { Name = "#" + t.IntVal.ToString(CultureInfo.InvariantCulture), Start = t.Start, End = t.End, Written = t.Text };
            }
            return TypeSpec(out _, out _, out _, out _, true);
        }

        void CloseAngle()
        {
            if (IsP(">")) { Next(); return; }
            if (IsP(">>") || IsP(">=") || IsP(">>="))
            {
                // split ">>" of nested templates into two tokens (permanently, so backtracking sees the same tokens)
                var t = Cur;
                toks[p] = new Token { Kind = TK.Punct, Text = ">", Start = t.Start, End = t.Start + 1, NewlineBefore = t.NewlineBefore };
                toks.Insert(p + 1, new Token { Kind = TK.Punct, Text = t.Text.Substring(1), Start = t.Start + 1, End = t.End });
                Next();
                return;
            }
            throw Fail("expected '>' before " + Describe(Cur), Cur);
        }

        Declarator DeclaratorAt(bool allowInit, bool allowAbstract = false)
        {
            var dc = new Declarator { Start = Cur.Start };
            while (IsP("*") || IsP("&") || IsP("&&") || IsK("const"))
            {
                if (IsP("*")) dc.Pointers++;
                else if (IsP("&") || IsP("&&")) dc.Ref = true;
                Next();
            }
            if (IsP("(") && IsP(At(1), "*"))
            {
                // function pointer: (*name)(params)
                Next(); Next();
                if (Cur.Kind == TK.Ident) { dc.NameStart = Cur.Start; dc.Name = Next().Text; dc.NameEnd = Prev.End; }
                else if (!allowAbstract) throw Fail("expected identifier before " + Describe(Cur), Cur);
                Expect(")");
                Expect("(");
                dc.FuncPtr = true;
                dc.FnParams = ParamList(out _, out _, out _);
            }
            else if (Cur.Kind == TK.Ident)
            {
                dc.NameStart = Cur.Start; dc.Name = Next().Text; dc.NameEnd = Prev.End;
            }
            else if (!allowAbstract)
            {
                if (Cur.Kind == TK.Keyword) throw Fail("expected identifier or '(' before '" + Cur.Text + "'", Cur);
                throw Fail("expected identifier or '(' before " + Describe(Cur), Cur);
            }
            while (IsP("["))
            {
                Next();
                if (IsP("]")) { dc.Dims.Add(null); Next(); continue; }
                dc.Dims.Add(Assignment());
                Expect("]");
            }
            if (allowInit)
            {
                if (IsP("="))
                {
                    dc.AssignStart = Cur.Start; dc.AssignEnd = Cur.End;
                    Next();
                    dc.Init = IsP("{") ? InitListExpr() : Assignment();
                }
                else if (d == Dialect.Cpp && IsP("{"))
                {
                    var il = InitListExpr();
                    dc.CtorArgs = il.Items; dc.BraceInit = true;
                    dc.AssignStart = il.Start; dc.AssignEnd = il.Start + 1;
                }
                else if (d == Dialect.Cpp && IsP("(") && dc.Name != null && !dc.FuncPtr)
                {
                    int s = Cur.Start;
                    Next();
                    dc.CtorArgs = new List<Expr>();
                    if (!IsP(")")) { dc.CtorArgs.Add(Assignment()); while (AcceptP(",")) dc.CtorArgs.Add(Assignment()); }
                    Expect(")");
                    dc.AssignStart = s; dc.AssignEnd = s + 1;
                }
            }
            dc.End = Prev.End;
            return dc;
        }

        List<Param> ParamList(out bool voidParams, out bool empty, out bool variadic)
        {
            voidParams = empty = variadic = false;
            var ps = new List<Param>();
            if (IsP(")")) { Next(); empty = true; return ps; }
            if (IsK("void") && IsP(At(1), ")")) { Next(); Next(); voidParams = true; return ps; }
            while (true)
            {
                if (IsP("...")) { Next(); variadic = true; break; }
                var prm = new Param { Start = Cur.Start };
                if (!StartsType()) throw Fail("expected declaration specifiers or '...' before " + Describe(Cur), Cur);
                prm.Type = TypeSpec(out _, out _, out _, out _);
                prm.D = DeclaratorAt(false, true);
                if (d == Dialect.Cpp && IsP("=")) { Next(); prm.Default = Assignment(); }
                prm.End = Prev.End;
                ps.Add(prm);
                if (!AcceptP(",")) break;
            }
            Expect(")");
            return ps;
        }

        Node External()
        {
            int start = Cur.Start;
            if (!StartsType())
            {
                if (Cur.Kind == TK.Ident && IsP(At(1), "("))
                    throw Fail("return type defaults to 'int' is not allowed: write the return type of '" + Cur.Text + "'", Cur);
                if (Cur.Kind == TK.Ident) throw Fail("unknown type name '" + Cur.Text + "'", Cur);
                throw Fail("expected identifier or '(' before " + Describe(Cur), Cur);
            }
            var ts = TypeSpec(out bool st, out bool ex, out bool cx, out bool inl);
            // function definition / prototype: type [*] name (
            int save = p;
            int ptrs = 0; bool isRef = false;
            while (IsP("*") || IsP("&")) { if (IsP("*")) ptrs++; else isRef = true; Next(); }
            if (Cur.Kind == TK.Ident && IsP(At(1), "("))
            {
                var f = new FuncDecl { Start = start, HeaderStart = start, Ret = ts, Static = st, Extern = ex, Inline = inl, Constexpr = cx };
                ts.Pointers += ptrs; ts.Ref |= isRef;
                f.NameStart = Cur.Start; f.Name = Next().Text; f.NameEnd = Prev.End;
                Next();
                f.Params = ParamList(out f.VoidParams, out f.EmptyParams, out f.Variadic);
                while (IsK("const") || IsK("noexcept") || IsK("override")) Next();
                if (d == Dialect.Cpp && IsP("->")) { Next(); f.TrailingRet = TypeSpec(out _, out _, out _, out _, true); }
                f.HeaderEnd = Prev.End;
                if (IsP("{")) f.Body = BlockStmt();
                else if (IsP(";")) Next();
                else if (IsP("=")) throw Fail("function '" + f.Name + "' is initialized like a variable", Cur);
                else throw Fail("expected '{' before " + Describe(Cur), Cur);
                f.End = Prev.End;
                return f;
            }
            p = save;
            var vd = new VarDecl { Start = start, Type = ts, Static = st, Extern = ex, Constexpr = cx, Inline = inl };
            vd.Vars.Add(DeclaratorAt(true));
            while (AcceptP(",")) vd.Vars.Add(DeclaratorAt(true));
            if (IsP("(") || IsP("{")) throw Fail("expected '=', ',', ';', 'asm' or '__attribute__' before " + Describe(Cur), Cur);
            Expect(";");
            vd.End = Prev.End;
            return vd;
        }

        // ================================================================== statements

        Block BlockStmt()
        {
            var b = new Block { Start = Cur.Start };
            Expect("{");
            while (!IsP("}"))
            {
                if (Cur.Kind == TK.EOF) throw Fail("expected declaration or statement at end of input", Cur.Start > 0 ? Cur.Start - 1 : 0, Cur.Start);
                b.Stmts.Add(Statement());
            }
            Next();
            b.End = Prev.End;
            return b;
        }

        Stmt Statement()
        {
            var t = Cur;
            int s = t.Start;
            if (t.Kind == TK.Preproc)
            {
                Next();
                return new UnsupportedStmt { Start = s, End = t.End, Message = "preprocessor directives inside functions are not supported in JavaFarmer" };
            }
            if (IsP("{")) return BlockStmt();
            if (IsP(";")) { Next(); return new EmptyStmt { Start = s, End = t.End }; }
            if (t.Kind == TK.Keyword)
            {
                switch (t.Text)
                {
                    case "if":
                        {
                            Next();
                            var st = new IfStmt { Start = s, KwEnd = t.End };
                            if (IsK("constexpr")) { Next(); st.Constexpr = true; }
                            Expect("(");
                            st.Cond = Expression();
                            Expect(")");
                            st.Then = Statement();
                            if (IsK("else")) { Next(); st.Else = Statement(); }
                            st.End = Prev.End;
                            return st;
                        }
                    case "while":
                        {
                            Next();
                            var st = new WhileStmt { Start = s, KwEnd = t.End };
                            Expect("(");
                            st.Cond = Expression();
                            Expect(")");
                            st.Body = Statement();
                            st.End = Prev.End;
                            return st;
                        }
                    case "do":
                        {
                            Next();
                            var st = new DoWhileStmt { Start = s };
                            st.Body = Statement();
                            if (!IsK("while")) throw Fail("expected 'while' before " + Describe(Cur), Cur);
                            st.WhileStart = Cur.Start; st.WhileEnd = Cur.End;
                            Next();
                            Expect("(");
                            st.Cond = Expression();
                            Expect(")");
                            Expect(";");
                            st.End = Prev.End;
                            return st;
                        }
                    case "for": return For();
                    case "switch":
                        {
                            Next();
                            var st = new SwitchStmt { Start = s, KwEnd = t.End };
                            Expect("(");
                            st.Sel = Expression();
                            Expect(")");
                            if (!IsP("{")) throw Fail("switch without braces is not supported in JavaFarmer", Cur);
                            st.Body = BlockStmt();
                            st.End = Prev.End;
                            return st;
                        }
                    case "case":
                        {
                            Next();
                            var st = new CaseStmt { Start = s, Value = Conditional() };
                            if (IsP("...")) throw Fail("case ranges are not supported in JavaFarmer", Cur);
                            st.LabelEnd = Cur.End;
                            Expect(":");
                            st.End = Prev.End;
                            return st;
                        }
                    case "default":
                        {
                            Next();
                            var st = new CaseStmt { Start = s, IsDefault = true, LabelEnd = Cur.End };
                            Expect(":");
                            st.End = Prev.End;
                            return st;
                        }
                    case "break": Next(); Expect(";"); return new BreakStmt { Start = s, End = Prev.End };
                    case "continue": Next(); Expect(";"); return new ContinueStmt { Start = s, End = Prev.End };
                    case "return":
                        {
                            Next();
                            var st = new ReturnStmt { Start = s };
                            if (!IsP(";")) st.Value = IsP("{") ? InitListExpr() : Expression();
                            Expect(";");
                            st.End = Prev.End;
                            return st;
                        }
                    case "goto":
                        Next();
                        while (!IsP(";") && Cur.Kind != TK.EOF) Next();
                        AcceptP(";");
                        return new UnsupportedStmt { Start = s, End = Prev.End, Message = "goto is not supported in JavaFarmer" };
                    case "else": throw Fail("'else' without a previous 'if'", t);
                    case "typedef": case "struct": case "enum": case "union": case "class":
                        {
                            Next();
                            int depth = 0;
                            while (Cur.Kind != TK.EOF)
                            {
                                if (IsP("{")) depth++;
                                else if (IsP("}")) { depth--; if (depth == 0) { Next(); continue; } }
                                else if (IsP(";") && depth == 0) break;
                                Next();
                            }
                            AcceptP(";");
                            return new UnsupportedStmt { Start = s, End = Prev.End, Message = t.Text + (t.Text == "typedef" ? "" : " declarations") + " are not supported in JavaFarmer" };
                        }
                    case "try": case "throw": case "new": case "delete": case "static_assert": case "asm":
                        if (t.Text != "new" && t.Text != "delete")
                        {
                            Next();
                            return SkipUnsupported(s, t.Text + " is not supported in JavaFarmer");
                        }
                        break;
                }
            }
            if (d == Dialect.C && t.Kind == TK.Ident && t.Text == "for_each" && IsP(At(1), "(")) return ForEachMacro();
            if (t.Kind == TK.Ident && IsP(At(1), ":") && !IsP(At(2), ":"))
            {
                Next(); Next();
                return new UnsupportedStmt { Start = s, End = Prev.End, Message = "labels are not supported in JavaFarmer" };
            }
            if (StartsDeclaration()) return LocalDecl(true);
            var es = new ExprStmt { Start = s, E = Expression() };
            Expect(";");
            es.End = Prev.End;
            return es;
        }

        Stmt SkipUnsupported(int s, string msg)
        {
            int depth = 0;
            while (Cur.Kind != TK.EOF)
            {
                if (IsP("{")) depth++;
                else if (IsP("}")) { if (depth == 0) break; depth--; if (depth == 0) { Next(); if (!IsK("catch")) break; continue; } }
                else if (IsP(";") && depth == 0) { Next(); break; }
                Next();
            }
            return new UnsupportedStmt { Start = s, End = Prev.End, Message = msg };
        }

        /// <summary>A type followed by a declarator (name, *name, &amp;name, (*name), [bindings]).</summary>
        bool StartsDeclaration()
        {
            if (!StartsType()) return false;
            int save = p;
            var err = Error;
            try
            {
                TypeSpec(out _, out _, out _, out _);
                while (IsP("*") || IsP("&") || IsP("&&") || IsK("const")) Next();
                bool decl = Cur.Kind == TK.Ident || (IsP("(") && IsP(At(1), "*")) || (d == Dialect.Cpp && IsP("[") && At(1).Kind == TK.Ident);
                return decl;
            }
            catch (Abort) { Error = err; return false; }
            finally { p = save; }
        }

        Stmt LocalDecl(bool needSemi)
        {
            int s = Cur.Start;
            var ts = TypeSpec(out bool st, out bool ex, out bool cx, out bool inl);
            // C++ structured binding
            int save = p;
            bool bref = false;
            while (IsP("&") || IsP("&&")) { bref = true; Next(); }
            if (d == Dialect.Cpp && IsP("["))
            {
                var bd = new BindingDecl { Start = s, Type = ts };
                ts.Ref |= bref;
                Next();
                do
                {
                    int ns = Ident(out var nm);
                    bd.Names.Add((nm, ns, Prev.End));
                } while (AcceptP(","));
                Expect("]");
                if (needSemi)
                {
                    if (IsP("=")) { Next(); bd.Init = Assignment(); }
                    else if (IsP("{") || IsP("(")) { var il = IsP("{") ? InitListExpr() : (Expr)ParenOnly(); bd.Init = il is InitList l && l.Items.Count == 1 ? l.Items[0] : il; }
                    else throw Fail("expected initializer before " + Describe(Cur), Cur);
                    Expect(";");
                }
                bd.End = Prev.End;
                return bd;
            }
            p = save;
            var vd = new VarDecl { Start = s, Type = ts, Static = st, Extern = ex, Constexpr = cx, Inline = inl };
            vd.Vars.Add(DeclaratorAt(needSemi || !IsP(":")));
            if (needSemi)
            {
                while (AcceptP(",")) vd.Vars.Add(DeclaratorAt(true));
                Expect(";");
            }
            else
            {
                while (IsP(",")) { Next(); vd.Vars.Add(DeclaratorAt(true)); }
            }
            vd.End = Prev.End;
            return vd;
        }

        Expr ParenOnly()
        {
            Expect("(");
            var e = Expression();
            Expect(")");
            return e;
        }

        Stmt For()
        {
            var t = Next();
            int s = t.Start;
            Expect("(");
            // range-for / classic
            Stmt init = null;
            if (IsP(";")) Next();
            else if (StartsDeclaration())
            {
                init = LocalDecl(false);
                if (d == Dialect.Cpp && IsP(":"))
                {
                    Next();
                    var rf = new RangeForStmt { Start = s, KwEnd = t.End };
                    if (init is BindingDecl bd) { rf.Binding = bd; rf.Type = bd.Type; }
                    else
                    {
                        var vd = (VarDecl)init;
                        if (vd.Vars.Count != 1 || vd.Vars[0].Init != null || vd.Vars[0].CtorArgs != null) throw Fail("expected ';' before ':' token", Prev);
                        rf.Type = vd.Type; rf.Var = vd.Vars[0];
                    }
                    rf.Range = IsP("{") ? InitListExpr() : Expression();
                    rf.HeaderEnd = Cur.End;
                    Expect(")");
                    rf.Body = Statement();
                    rf.End = Prev.End;
                    return rf;
                }
                if (init is BindingDecl) throw Fail("expected ':' before " + Describe(Cur), Cur);
                Expect(";");
                init.End = Prev.End;
            }
            else
            {
                var es = new ExprStmt { Start = Cur.Start };
                es.E = ExprList();
                es.End = Prev.End;
                init = es;
                if (d == Dialect.Cpp && IsP(":")) throw Fail("expected a declaration before ':' token", Cur);
                Expect(";");
            }
            var f = new ForStmt { Start = s, KwEnd = t.End, Init = init };
            if (!IsP(";")) f.Cond = Expression();
            Expect(";");
            if (!IsP(")"))
            {
                var u = ExprList();
                if (u is CommaExpr ce) f.Update.AddRange(ce.Items); else f.Update.Add(u);
            }
            f.HeaderEnd = Cur.End;
            Expect(")");
            f.Body = Statement();
            f.End = Prev.End;
            return f;
        }

        Stmt ForEachMacro()
        {
            var t = Next();
            var rf = new RangeForStmt { Start = t.Start, KwEnd = t.End, Macro = true };
            Expect("(");
            if (!StartsType()) throw Fail("for_each expects a declaration (for_each (int x, list)) before " + Describe(Cur), Cur);
            rf.Type = TypeSpec(out _, out _, out _, out _);
            rf.Var = DeclaratorAt(false);
            Expect(",");
            rf.Range = Assignment();
            rf.HeaderEnd = Cur.End;
            Expect(")");
            rf.Body = Statement();
            rf.End = Prev.End;
            return rf;
        }

        // ================================================================== expressions

        Expr ExprList()
        {
            var first = Expression();
            if (!IsP(",")) return first;
            var c = new CommaExpr { Start = first.Start };
            c.Items.Add(first);
            while (AcceptP(",")) c.Items.Add(Expression());
            c.End = Prev.End;
            return c;
        }

        public Expr Expression() => Assignment();

        static readonly HashSet<string> AssignOps = new HashSet<string> { "=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=" };

        Expr Assignment()
        {
            var lhs = Conditional();
            if (Cur.Kind == TK.Punct && AssignOps.Contains(Cur.Text))
            {
                var op = Next();
                var rhs = IsP("{") && d == Dialect.Cpp ? InitListExpr() : Assignment();
                return new AssignExpr { Start = lhs.Start, End = rhs.End, Op = op.Text, Target = lhs, Value = rhs, OpStart = op.Start, OpEnd = op.End };
            }
            return lhs;
        }

        Expr Conditional()
        {
            var c = Binary(0);
            if (!IsP("?")) return c;
            var q = Next();
            var a = Expression();
            Expect(":");
            var b = Assignment();
            return new CondExpr { Start = c.Start, End = b.End, C = c, T = a, F = b, QStart = q.Start };
        }

        static int Prec(string op)
        {
            switch (op)
            {
                case "||": return 1;
                case "&&": return 2;
                case "|": return 3;
                case "^": return 4;
                case "&": return 5;
                case "==": case "!=": return 6;
                case "<": case ">": case "<=": case ">=": case "<=>": return 7;
                case "<<": case ">>": return 8;
                case "+": case "-": return 9;
                case "*": case "/": case "%": return 10;
            }
            return -1;
        }

        Expr Binary(int min)
        {
            var left = Unary();
            while (true)
            {
                var t = Cur;
                if (t.Kind != TK.Punct) break;
                int pr = Prec(t.Text);
                if (pr < 0 || pr < min) break;
                Next();
                var right = Binary(pr + 1);
                left = new BinaryExpr { Start = left.Start, End = right.End, Op = t.Text, L = left, R = right, OpStart = t.Start, OpEnd = t.End };
            }
            return left;
        }

        Expr Unary()
        {
            var t = Cur;
            if (t.Kind == TK.Punct)
            {
                switch (t.Text)
                {
                    case "++": case "--":
                        {
                            Next();
                            var e = Unary();
                            return new IncDecExpr { Start = t.Start, End = e.End, Op = t.Text, Prefix = true, Target = e, OpStart = t.Start, OpEnd = t.End };
                        }
                    case "-": case "+": case "!": case "~": case "&": case "*":
                        {
                            Next();
                            var e = Unary();
                            return new UnaryExpr { Start = t.Start, End = e.End, Op = t.Text, E = e, OpEnd = t.End };
                        }
                    case "(":
                        if (StartsType(1) && LooksLikeCast())
                        {
                            Next();
                            var ts = TypeSpec(out _, out _, out _, out _, true);
                            Expect(")");
                            int opEnd = Prev.End;
                            if (IsP("{"))
                            {
                                var il = InitListExpr();
                                var ce = new ConstructExpr { Start = t.Start, End = il.End, Type = ts, Braces = true, Compound = true };
                                ce.Args.AddRange(il.Items);
                                return Postfix(ce);
                            }
                            var operand = Unary();
                            return new CastExpr { Start = t.Start, End = operand.End, Type = ts, E = operand, Style = "c", OpEnd = opEnd };
                        }
                        break;
                }
            }
            if (t.Kind == TK.Keyword)
            {
                switch (t.Text)
                {
                    case "sizeof":
                        {
                            Next();
                            var so = new SizeofExpr { Start = t.Start };
                            if (IsP("(") && StartsType(1))
                            {
                                Next();
                                so.T = TypeSpec(out _, out _, out _, out _, true);
                                Expect(")");
                            }
                            else so.E = Unary();
                            so.End = Prev.End;
                            return so;
                        }
                    case "static_cast": case "dynamic_cast": case "reinterpret_cast": case "const_cast":
                        {
                            Next();
                            if (!IsP("<")) throw Fail("expected '<' before " + Describe(Cur), Cur);
                            Next();
                            var ts = TypeSpec(out _, out _, out _, out _, true);
                            CloseAngle();
                            int opEnd = Cur.Start;
                            Expect("(");
                            var e = Expression();
                            Expect(")");
                            return Postfix(new CastExpr { Start = t.Start, End = Prev.End, Type = ts, E = e, Style = t.Text, OpEnd = opEnd });
                        }
                    case "new": case "delete": case "throw": case "typeid": case "co_await": case "alignof": case "_Alignof": case "_Generic":
                        throw Fail(t.Text + " is not supported in JavaFarmer", t);
                }
            }
            return Postfix(Primary());
        }

        /// <summary>"(" type ")" followed by something that can be cast (not an operator): (int) x, (double)(a + b), (Position){...}.</summary>
        bool LooksLikeCast()
        {
            int save = p;
            var err = Error;
            try
            {
                Next();
                TypeSpec(out _, out _, out _, out _, true);
                if (!IsP(")")) return false;
                Next();
                var t = Cur;
                if (t.Kind == TK.EOF) return false;
                if (t.Kind == TK.Punct) return t.Text == "(" || t.Text == "{" || t.Text == "-" || t.Text == "+" || t.Text == "!" || t.Text == "~" || t.Text == "*" || t.Text == "&";
                return true;
            }
            catch (Abort) { Error = err; return false; }
            finally { p = save; }
        }

        Expr Postfix(Expr e)
        {
            while (true)
            {
                var t = Cur;
                if (IsP("("))
                {
                    var call = new CallExpr { Start = e.Start, Callee = e, LParen = t.Start };
                    Next();
                    if (e is NameExpr ne && ne.Qual.Count == 0 && TypeArgMacros.TryGetValue(ne.Name, out int nt) && d == Dialect.C)
                    {
                        call.TypeArgs = new List<TypeSyn>();
                        for (int i = 0; i < nt; i++)
                        {
                            if (i > 0) Expect(",");
                            if (!StartsType()) throw Fail(ne.Name + " expects a type as argument " + (i + 1) + " before " + Describe(Cur), Cur);
                            call.TypeArgs.Add(TypeSpec(out _, out _, out _, out _, true));
                        }
                        while (AcceptP(",")) call.Args.Add(IsP("{") ? InitListExpr() : Assignment());
                    }
                    else if (!IsP(")"))
                    {
                        call.Args.Add(IsP("{") ? InitListExpr() : Assignment());
                        while (AcceptP(",")) call.Args.Add(IsP("{") ? InitListExpr() : Assignment());
                    }
                    call.RParen = Cur.Start;
                    Expect(")");
                    call.End = Prev.End;
                    e = call;
                    continue;
                }
                if (IsP("["))
                {
                    Next();
                    var idx = Expression();
                    Expect("]");
                    e = new IndexExpr { Start = e.Start, End = Prev.End, Target = e, Index = idx };
                    continue;
                }
                if (IsP(".") || IsP("->"))
                {
                    Next();
                    if (Cur.Kind != TK.Ident && !(Cur.Kind == TK.Keyword && (Cur.Text == "template")))
                        throw Fail("expected identifier before " + Describe(Cur), Cur);
                    var nt = Next();
                    e = new MemberExpr { Start = e.Start, End = nt.End, Target = e, Name = nt.Text, Arrow = t.Text == "->", NameStart = nt.Start, NameEnd = nt.End };
                    continue;
                }
                if (IsP("++") || IsP("--"))
                {
                    Next();
                    e = new IncDecExpr { Start = e.Start, End = t.End, Op = t.Text, Prefix = false, Target = e, OpStart = t.Start, OpEnd = t.End };
                    continue;
                }
                return e;
            }
        }

        InitList InitListExpr()
        {
            var il = new InitList { Start = Cur.Start };
            Expect("{");
            while (!IsP("}"))
            {
                if (IsP(".") || IsP("[")) throw Fail("designated initializers are not supported in JavaFarmer", Cur);
                il.Items.Add(IsP("{") ? InitListExpr() : Assignment());
                if (!AcceptP(",")) break;
            }
            Expect("}");
            il.End = Prev.End;
            return il;
        }

        Expr Primary()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case TK.Int:
                    Next();
                    return new Lit { Start = t.Start, End = t.End, Kind = LitKind.Int, I = t.IntVal, Suffix = t.Suffix, Text = t.Text };
                case TK.Float:
                    Next();
                    return new Lit { Start = t.Start, End = t.End, Kind = LitKind.Float, F = t.FloatVal, Suffix = t.Suffix, Text = t.Text };
                case TK.String:
                    {
                        Next();
                        var s = t.StrVal;
                        int end = t.End;
                        bool bad = t.BadEscape;
                        while (Cur.Kind == TK.String) { s += Cur.StrVal; end = Cur.End; bad |= Cur.BadEscape; Next(); }
                        if (bad) throw Fail("unknown escape sequence in string constant", t);
                        return new Lit { Start = t.Start, End = end, Kind = LitKind.String, S = s, Text = src.Substring(t.Start, end - t.Start) };
                    }
                case TK.Char:
                    Next();
                    return new Lit { Start = t.Start, End = t.End, Kind = LitKind.Char, S = t.StrVal, Text = t.Text, I = t.StrVal.Length > 0 ? t.StrVal[0] : 0 };
                case TK.Keyword:
                    switch (t.Text)
                    {
                        case "true": case "false": Next(); return new Lit { Start = t.Start, End = t.End, Kind = LitKind.Bool, B = t.Text == "true", Text = t.Text };
                        case "nullptr": Next(); return new Lit { Start = t.Start, End = t.End, Kind = LitKind.Nullptr, Text = t.Text };
                        case "this": throw Fail("'this' is not supported in JavaFarmer", t);
                    }
                    if (d == Dialect.Cpp && StartsType()) return TypeConstruct();
                    if (d == Dialect.C && StartsType()) throw Fail("expected expression before '" + t.Text + "'", t);
                    throw Fail("expected expression before '" + t.Text + "'", t);
                case TK.Punct:
                    if (t.Text == "(")
                    {
                        Next();
                        var e = Expression();
                        if (IsP(",")) throw Fail("the comma operator is not supported in JavaFarmer", Cur);
                        Expect(")");
                        e.ParenStart = t.Start; e.ParenEnd = Prev.End;
                        // keep Start/End of the inner expression; parentheses are recorded separately
                        return e;
                    }
                    if (t.Text == "{") return InitListExpr();
                    if (t.Text == "[" && d == Dialect.Cpp) return Lambda();
                    if (t.Text == "::") { Next(); return Primary(); }
                    throw Fail("expected expression before " + Describe(t), t);
                case TK.Ident:
                    if (d == Dialect.Cpp && StartsType() && !IsVariableLikeType()) return TypeConstruct();
                    return Name();
                case TK.EOF:
                    throw Fail("expected expression at end of input", t.Start > 0 ? t.Start - 1 : 0, t.Start);
            }
            throw Fail("expected expression before " + Describe(t), t);
        }

        /// <summary>"string" etc. used as a C++ functional cast / construction only when followed by ( or { (or a template).</summary>
        bool IsVariableLikeType()
        {
            int k = 0;
            if (At(0).Text == "std" || At(0).Text == "farm") k = 2;
            if (k == 0 && IsP(At(1), "::")) return true;   // Direction::North
            var nt = At(k + 1);
            if (IsP(nt, "::")) return true;                  // std::string::npos
            return !(IsP(nt, "(") || IsP(nt, "{") || IsP(nt, "<") || IsP(nt, "::"));
        }

        Expr TypeConstruct()
        {
            int s = Cur.Start;
            var ts = TypeSpec(out _, out _, out _, out _, true);
            var ce = new ConstructExpr { Start = s, Type = ts };
            if (IsP("{"))
            {
                var il = InitListExpr();
                ce.Args.AddRange(il.Items);
                ce.Braces = true;
            }
            else if (IsP("("))
            {
                Next();
                if (!IsP(")")) { ce.Args.Add(Assignment()); while (AcceptP(",")) ce.Args.Add(Assignment()); }
                Expect(")");
            }
            else if (IsP("::"))
            {
                throw Fail("'" + ts.Written + "::" + At(1).Text + "' is not supported in JavaFarmer", Cur);
            }
            else throw Fail("expected primary-expression before " + Describe(Cur), Cur);
            ce.End = Prev.End;
            return ce;
        }

        Expr Name()
        {
            var t = Next();
            var n = new NameExpr { Start = t.Start, NameStart = t.Start, Name = t.Text };
            while (IsP("::") && At(1).Kind == TK.Ident)
            {
                Next();
                n.Qual.Add(n.Name);
                var nt = Next();
                n.Name = nt.Text;
                n.NameStart = nt.Start;
            }
            n.End = Prev.End;
            return n;
        }

        Expr Lambda()
        {
            var lam = new LambdaExpr { Start = Cur.Start };
            Expect("[");
            while (!IsP("]"))
            {
                if (IsP("&") && !(At(1).Kind == TK.Ident)) { Next(); lam.Default = "&"; }
                else if (IsP("=")) { Next(); lam.Default = "="; }
                else if (IsP("&")) { Next(); Ident(out var nm); lam.Captures.Add((nm, true)); }
                else if (Cur.Kind == TK.Ident)
                {
                    Ident(out var nm);
                    if (IsP("=") || IsP("(") || IsP("{")) throw Fail("init-captures are not supported in JavaFarmer", Cur);
                    lam.Captures.Add((nm, false));
                }
                else if (IsK("this")) throw Fail("capturing 'this' is not supported in JavaFarmer", Cur);
                else throw Fail("expected identifier before " + Describe(Cur), Cur);
                if (!AcceptP(",")) break;
            }
            Expect("]");
            if (IsP("("))
            {
                Next();
                lam.HasParams = true;
                lam.Params = ParamList(out _, out _, out bool variadic);
                if (variadic) throw Fail("variadic lambdas are not supported in JavaFarmer", Prev);
            }
            while (IsK("mutable") || IsK("constexpr") || IsK("noexcept")) Next();
            if (IsP("->")) { Next(); lam.Ret = TypeSpec(out _, out _, out _, out _, true); }
            lam.HeaderEnd = Prev.End;
            if (!IsP("{")) throw Fail("expected '{' before " + Describe(Cur), Cur);
            lam.Body = BlockStmt();
            lam.End = Prev.End;
            return lam;
        }
    }
}
