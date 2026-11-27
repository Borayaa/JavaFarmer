using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Optional ILanguage extension: Print that also reports the constructs the language cannot express
    /// (DocsTranslator keeps such blocks unchanged instead of showing wrong code).
    /// </summary>
    public interface IPrinterDiagnostics
    {
        string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems);
    }

    public sealed class DocsTranslateOptions
    {
        /// <summary>Language of the code spans (default: the game's Python, Languages.Get("python")).</summary>
        public ILanguage Source;
        /// <summary>Indentation of translated blocks (the game docs use tabs).</summary>
        public string Indent = "\t";
        /// <summary>Also compile every translated block as a full program and keep the original when it fails.</summary>
        public bool VerifyCompiles;
        /// <summary>Module name used to parse / print / compile blocks.</summary>
        public string FileName = "Docs";
    }

    /// <summary>One code span of a docs page (same numbering as the test harness: every span, inline and block, from 0).</summary>
    public sealed class DocsSpan
    {
        public int Index;
        /// <summary>[Start, End) of the text between the backticks.</summary>
        public int Start, End;
        public string Text;
        /// <summary>Contains a newline or stands alone on its line.</summary>
        public bool IsBlock;
    }

    /// <summary>
    /// Rewrites the code spans of a game docs page (single-backtick spans, Python) into another language.
    /// Blocks go through the source frontend (ToIr) and the target printer (Snippet mode); inline spans are names,
    /// keywords, constants, values or short expressions. Prose, &lt;unlock=..&gt;, &lt;spoiler=..&gt;, {{...}}
    /// placeholders, images and links are never touched. A span that cannot be translated stays unchanged and is
    /// reported as "#&lt;span index&gt;: reason".
    /// </summary>
    public static class DocsTranslator
    {
        public static string Translate(string markdownWithPythonBlocks, ILanguage target) =>
            Translate(markdownWithPythonBlocks, target, out _, null);

        public static string Translate(string markdown, ILanguage target, out IReadOnlyList<string> failures, DocsTranslateOptions options = null)
        {
            var fails = new List<string>();
            failures = fails;
            if (string.IsNullOrEmpty(markdown) || target == null) return markdown ?? "";
            options = options ?? new DocsTranslateOptions();
            var source = options.Source ?? Languages.Get("python");
            if (source == null) { fails.Add("no source language (the Python frontend is not registered)"); return markdown; }
            if (source.Id == target.Id && source.Id == "python") return markdown;
            var ctx = new Ctx { Source = source, Target = target, Options = options };
            var sb = new StringBuilder();
            int pos = 0;
            foreach (var s in Spans(markdown))
            {
                sb.Append(markdown, pos, s.Start - pos);
                string outText;
                string why;
                try { outText = s.IsBlock ? ctx.Block(s.Text, out why) : ctx.Inline(s.Text, out why); }
                catch (Exception ex) { outText = null; why = "internal error: " + ex.Message; }
                if (outText == null)
                {
                    sb.Append(s.Text);
                    if (why != null) fails.Add("#" + s.Index + ": " + why);
                }
                else sb.Append(outText);
                pos = s.End;
            }
            sb.Append(markdown, pos, markdown.Length - pos);
            return sb.ToString();
        }

        /// <summary>The code spans of a page, numbered from 0 (inline and block alike).</summary>
        public static List<DocsSpan> Spans(string markdown)
        {
            var res = new List<DocsSpan>();
            if (markdown == null) return res;
            int i = 0, idx = 0;
            while (i < markdown.Length)
            {
                int open = markdown.IndexOf('`', i);
                if (open < 0) break;
                int close = markdown.IndexOf('`', open + 1);
                if (close < 0) break;
                var text = markdown.Substring(open + 1, close - open - 1);
                bool lineStart = open == 0 || markdown[open - 1] == '\n';
                bool lineEnd = close + 1 >= markdown.Length || markdown[close + 1] == '\n' || markdown[close + 1] == '\r';
                res.Add(new DocsSpan { Index = idx++, Start = open + 1, End = close, Text = text, IsBlock = text.IndexOf('\n') >= 0 || (lineStart && lineEnd) });
                i = close + 1;
            }
            return res;
        }

        sealed class Ctx
        {
            public ILanguage Source, Target;
            public DocsTranslateOptions Options;
            Dictionary<string, string> keywords;

            CompileOptions CO => new CompileOptions { FileName = Options.FileName };

            string PrintTarget(IrModule m, bool snippet, out IReadOnlyList<string> problems)
            {
                var po = new PrintOptions { Snippet = snippet, Indent = Options.Indent, FileName = Options.FileName };
                if (Target is IPrinterDiagnostics pd) return pd.Print(m, po, out problems);
                problems = Array.Empty<string>();
                return Target.Print(m, po);
            }

            public string Block(string text, out string why)
            {
                why = null;
                var code = text.Replace("\r\n", "\n");
                var ir = Source.ToIr(code, CO, out var errs);
                if (ir == null) { why = "not " + Source.DisplayName + ": " + (errs != null && errs.Count > 0 ? errs[0].Message : "does not parse"); return null; }
                var printed = PrintTarget(ir, true, out var problems);
                if (problems != null && problems.Count > 0) { why = string.Join("; ", problems); return null; }
                if (Options.VerifyCompiles && !Target.IsNative)
                {
                    var full = PrintTarget(ir, false, out _);
                    var r = Target.Compile(full, CO);
                    if (r == null || !r.Success) { why = "does not compile: " + (r?.Errors?.FirstOrDefault()?.Message ?? "?"); return null; }
                }
                printed = printed.Replace("\r\n", "\n").TrimEnd('\n', ' ', '\t');
                // keep the span's own leading/trailing line breaks
                int lead = 0; while (lead < code.Length && code[lead] == '\n') lead++;
                int trail = 0; while (trail < code.Length - lead && code[code.Length - 1 - trail] == '\n') trail++;
                return new string('\n', lead) + printed + new string('\n', trail);
            }

            public string Inline(string text, out string why)
            {
                why = null;
                var t = text.Trim();
                if (t.Length == 0) return null;
                if (Keywords().TryGetValue(t, out var kw)) return kw == t ? null : kw;
                // a builtin mentioned by name ("print", "can_harvest"): the target's name for it
                if (IsIdentifier(t) && GameApi.Find(t) != null) { var an = Target.ApiName(t); return an == null || an == t ? null : an; }
                var ir = Source.ToIr(t, CO, out _);
                if (ir != null)
                {
                    var stmts = ir.Items.OfType<IrStmt>().ToList();
                    if (stmts.Count == 1 && ir.Items.Count == 1 && stmts[0] is IrExprStmt es)
                    {
                        // "()" (the call operator), "0,1,2" / "North, East" (prose lists) are not values
                        if (es.Expr is IrTupleLit tup && (tup.Items.Count == 0 || !t.StartsWith("("))) return null;
                        var shown = ValueText(es.Expr);
                        if (shown != null) return shown;
                    }
                    var printed = PrintTarget(ir, true, out var problems);
                    printed = printed.Replace("\r\n", "\n").Trim();
                    if (problems != null && problems.Count > 0) { why = "inline " + t + ": " + string.Join("; ", problems); return null; }
                    if (printed.IndexOf('\n') >= 0) printed = LastStatement(printed);
                    if (printed == null || printed.Length == 0) { why = "inline " + t + ": translates to several lines"; return null; }
                    // an expression mentioned in prose has no statement terminator
                    if (stmts.Count == 1 && stmts[0] is IrExprStmt && printed.EndsWith(";")) printed = printed.Substring(0, printed.Length - 1);
                    return printed;
                }
                return Names(t);
            }

            static bool IsIdentifier(string t) => t.Length > 0 && (char.IsLetter(t[0]) || t[0] == '_') && t.All(c => char.IsLetterOrDigit(c) || c == '_');

            /// <summary>
            /// "i = i + 1" in prose prints as "int i = 0;\ni = i + 1;" (the snippet declares the unknown variable): keep the
            /// statement itself when every line before it only declares a variable with its default value.
            /// </summary>
            static string LastStatement(string printed)
            {
                var lines = printed.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                if (lines.Count < 2) return lines.FirstOrDefault();
                foreach (var l in lines.Take(lines.Count - 1))
                    if (!System.Text.RegularExpressions.Regex.IsMatch(l, @"^[A-Za-z_][\w<>\[\], ]*\s+[A-Za-z_]\w*\s*(=|:=|<-)\s*(0|0\.0|false|False|FALSE|null|None|NULL|nil|nullptr)\s*;?$")) return null;
                return lines[lines.Count - 1];
            }

            /// <summary>Collection / tuple literals shown as values ("[2, 6, 12, 7]"): the target's print format.</summary>
            string ValueText(IrExpr e)
            {
                if (!(e is IrListLit || e is IrSetLit || e is IrMapLit || e is IrTupleLit)) return null;
                if (!ToValue(e, out var v)) return null;
                return Target.FormatValue(v);
            }

            static bool ToValue(IrExpr e, out object v)
            {
                v = null;
                switch (e)
                {
                    case IrLiteral l:
                        switch (l.Kind)
                        {
                            case IrLitKind.Int: v = (double)l.Int; return true;
                            case IrLitKind.Float: v = l.Float; return true;
                            case IrLitKind.Bool: v = l.Bool; return true;
                            case IrLitKind.String: v = l.Str; return true;
                            case IrLitKind.Null: v = null; return true;
                        }
                        return false;
                    case IrUnary u when u.Op == IrUnOp.Neg && u.Operand is IrLiteral ul && (ul.Kind == IrLitKind.Int || ul.Kind == IrLitKind.Float):
                        v = -(ul.Kind == IrLitKind.Int ? (double)ul.Int : ul.Float); return true;
                    case IrConst c: v = new GameConstant(c.Category, c.Name); return true;
                    case IrListLit ll: { var g = new GameList(); foreach (var i in ll.Items) { if (!ToValue(i, out var x)) return false; g.Items.Add(x); } v = g; return true; }
                    case IrSetLit sl: { var g = new GameSet(); foreach (var i in sl.Items) { if (!ToValue(i, out var x)) return false; g.Items.Add(x); } v = g; return true; }
                    case IrTupleLit tl: { var g = new GameTuple(); foreach (var i in tl.Items) { if (!ToValue(i, out var x)) return false; g.Items.Add(x); } v = g; return true; }
                    case IrMapLit ml:
                        {
                            var g = new GameDict();
                            for (int i = 0; i < ml.Keys.Count; i++)
                            {
                                if (!ToValue(ml.Keys[i], out var k) || !ToValue(ml.Values[i], out var x)) return false;
                                g.Add(k, x);
                            }
                            v = g; return true;
                        }
                }
                return false;
            }

            /// <summary>Fallback when the span does not parse: rename builtin identifiers, keep everything else.</summary>
            string Names(string t)
            {
                var sb = new StringBuilder();
                bool changed = false;
                int i = 0;
                while (i < t.Length)
                {
                    char c = t[i];
                    if (char.IsLetter(c) || c == '_')
                    {
                        int j = i;
                        while (j < t.Length && (char.IsLetterOrDigit(t[j]) || t[j] == '_')) j++;
                        var w = t.Substring(i, j - i);
                        bool member = i > 0 && t[i - 1] == '.';
                        string mapped = null;
                        if (!member && GameApi.Find(w) != null) mapped = Target.ApiName(w);
                        else if (!member && Keywords().TryGetValue(w, out var k)) mapped = k;
                        if (mapped != null && mapped != w) { sb.Append(mapped); changed = true; }
                        else sb.Append(w);
                        i = j;
                        continue;
                    }
                    sb.Append(c);
                    i++;
                }
                return changed ? sb.ToString() : null;
            }

            /// <summary>
            /// Python keyword → target keyword, read off the target printer: each keyword is printed as a tiny IR
            /// program and the word is taken from the output (so every language gets its table for free).
            /// </summary>
            Dictionary<string, string> Keywords()
            {
                if (keywords != null) return keywords;
                var k = new Dictionary<string, string>(StringComparer.Ordinal);
                string Lit(IrExpr e) => Expr(e);
                Put(k, "True", Lit(IrLiteral.OfBool(true)));
                Put(k, "False", Lit(IrLiteral.OfBool(false)));
                Put(k, "None", Lit(IrLiteral.Null()));
                var a = Marker("aaa"); var b = Marker("bbb");
                Put(k, "while", FirstWord(Stmt(new IrWhile { Cond = IrLiteral.OfBool(true), Body = Blk(a()) })));
                Put(k, "if", FirstWord(Stmt(new IrIf { Cond = IrLiteral.OfBool(true), Then = Blk(a()) })));
                Put(k, "for", FirstWord(Stmt(new IrRangeFor { Var = new IrSymbol("i", IrSymbolKind.Local, IrType.Int), End = IrLiteral.OfInt(3), Body = Blk(a()) })));
                Put(k, "break", Inside(new IrLoop { Body = new IrBlock(new IrStmt[] { new IrBreak() }) }));
                Put(k, "continue", Inside(new IrLoop { Body = new IrBlock(new IrStmt[] { a(), new IrContinue() }) }, true));
                Put(k, "else", Between(Stmt(new IrIf { Cond = IrLiteral.OfBool(true), Then = Blk(a()), Else = Blk(b()) })));
                Put(k, "elif", Between(Stmt(new IrIf { Cond = IrLiteral.OfBool(true), Then = Blk(a()), Else = new IrIf { Cond = IrLiteral.OfBool(false), Then = Blk(b()) } }), true));
                var x = new IrSymbol("aaa", IrSymbolKind.Local, IrType.Bool); var y = new IrSymbol("bbb", IrSymbolKind.Local, IrType.Bool);
                Put(k, "and", Op(Expr(new IrBinary { Op = IrBinOp.And, Left = new IrName(x), Right = new IrName(y), Type = IrType.Bool })));
                Put(k, "or", Op(Expr(new IrBinary { Op = IrBinOp.Or, Left = new IrName(x), Right = new IrName(y), Type = IrType.Bool })));
                var notText = Expr(new IrUnary { Op = IrUnOp.Not, Operand = new IrName(y), Type = IrType.Bool });
                if (notText != null) Put(k, "not", notText.Replace("bbb", "").Replace("(", "").Replace(")", "").Trim());
                // def: the word in front of a procedure's name; return: the word in front of a returned value
                var ftext = Function(IrType.Void, null);
                int fi = ftext.IndexOf("fff", StringComparison.Ordinal);
                if (fi > 0) Put(k, "def", FirstWord(ftext.Substring(0, fi)));
                var rtext = Function(IrType.Int, new IrReturn { Value = IrLiteral.OfInt(7) });
                int ri = rtext.IndexOf("7", StringComparison.Ordinal);
                if (ri > 0) { var line = rtext.Substring(0, ri); line = line.Substring(line.LastIndexOf('\n') + 1); Put(k, "return", FirstWord(line)); }
                return keywords = k;
            }

            string Function(IrType ret, IrStmt body)
            {
                var f = new IrFunction { Symbol = new IrSymbol("fff", IrSymbolKind.Function), ReturnType = ret };
                f.Symbol.Function = f;
                if (body != null) f.Body.Stmts.Add(body);
                var mod = new IrModule { Name = Options.FileName, SourceLanguage = "ir" };
                mod.Items.Add(f);
                return PrintTarget(mod, true, out _) ?? "";
            }

            static void Put(Dictionary<string, string> k, string py, string word)
            {
                if (!string.IsNullOrWhiteSpace(word)) k[py] = word.Trim();
            }

            static Func<IrStmt> Marker(string name) => () => new IrExprStmt(new IrCall { Kind = IrCallKind.Function, Function = new IrSymbol(name, IrSymbolKind.Function) });
            static IrBlock Blk(params IrStmt[] s) => new IrBlock(s);

            string Stmt(IrStmt s)
            {
                var m = new IrModule { Name = Options.FileName, SourceLanguage = "ir" };
                m.Items.Add(s);
                return PrintTarget(m, true, out _);
            }

            string Expr(IrExpr e)
            {
                var t = Stmt(new IrExprStmt(e));
                if (t == null) return null;
                t = t.Trim();
                foreach (var p in new[] { Target.Editor?.LineComment, "//", "#" }) if (!string.IsNullOrEmpty(p) && t.StartsWith(p)) t = t.Substring(p.Length).Trim();
                return t.TrimEnd(';').Trim();
            }

            static string FirstWord(string text)
            {
                if (text == null) return null;
                var t = text.TrimStart();
                int j = 0;
                while (j < t.Length && (char.IsLetter(t[j]) || t[j] == '_')) j++;
                return j == 0 ? null : t.Substring(0, j);
            }

            string Inside(IrStmt loop, bool afterMarker = false)
            {
                var t = Stmt(loop);
                if (t == null) return null;
                var lines = t.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                // the loop body line: the line after the header (after the marker call for continue)
                int at = afterMarker ? lines.FindIndex(l => l.Contains("aaa")) + 1 : 1;
                return at > 0 && at < lines.Count ? FirstWord(lines[at]) : null;
            }

            static string Between(string t, bool keepIf = false)
            {
                if (t == null) return null;
                int a = t.IndexOf("aaa", StringComparison.Ordinal);
                if (a < 0) return null;
                a = t.IndexOf('\n', a);
                int b = t.IndexOf(keepIf ? "false" : "bbb", a < 0 ? 0 : a, StringComparison.Ordinal);
                if (keepIf) { int alt = t.IndexOf("False", a < 0 ? 0 : a, StringComparison.Ordinal); if (b < 0 || (alt >= 0 && alt < b)) b = alt; }
                if (a < 0 || b < 0) return null;
                var mid = t.Substring(a, b - a);
                if (!keepIf) mid = mid.Substring(0, Math.Max(0, mid.LastIndexOf('\n') < 0 ? mid.Length : mid.LastIndexOf('\n')));
                var words = new StringBuilder();
                foreach (var ch in mid) words.Append(char.IsLetter(ch) || ch == '_' ? ch : ' ');
                var res = string.Join(" ", words.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                return res.Length == 0 ? null : res;
            }

            static string Op(string t)
            {
                if (t == null) return null;
                int a = t.IndexOf("aaa", StringComparison.Ordinal), b = t.IndexOf("bbb", StringComparison.Ordinal);
                if (a < 0 || b < 0 || b < a) return null;
                var mid = t.Substring(a + 3, b - a - 3).Trim();
                return mid.Length == 0 ? null : mid;
            }
        }
    }
}
