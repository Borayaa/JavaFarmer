using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// R (4.x script subset) as a JavaFarmer language: top-level statements run, snake_case game API, Entities$Carrot
    /// constants, 1-based vectors / lists, named lists as maps. See docs/languages/r.md.
    /// </summary>
    public sealed class RLanguage : LanguageBase, IPrinterDiagnostics
    {
        public const string StubFileName = "farm.R";

        public override string Id => "r";
        public override string DisplayName => "R";
        public override string FileExtension => ".R";
        protected override NamingStyle ApiNaming => NamingStyle.Snake;

        static readonly EditorRules rules = new EditorRules
        {
            IndentAfter = new[] { "{" },
            DedentOn = new[] { "}" },
            LineComment = "#",
            BlockCommentStart = null,
            BlockCommentEnd = null,
            IndentUnit = "\t",
            Brackets = new[] { "{}", "()", "[]" },
        };
        public override EditorRules Editor => rules;

        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors) =>
            RlFrontend.Build(source, options, errors);

        protected override string PrintImpl(IrModule module, PrintOptions options) => RlPrinter.Print(module, options, out _);

        public string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            try { return RlPrinter.Print(module, options ?? new PrintOptions(), out problems); }
            catch (Exception ex) { problems = new[] { "internal printer error: " + ex.Message }; return "# JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            try { return RlHighlighter.Highlight(source ?? ""); }
            catch { return string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, TokenKind.Unknown) }; }
        }

        static readonly IReadOnlyCollection<string> keywords = RlLexer.Keywords.OrderBy(k => k, StringComparer.Ordinal).ToList();
        public override IReadOnlyCollection<string> Keywords => keywords;

        /// <summary>An R script has no entry function: a new window is empty.</summary>
        public override string NewFileTemplate(string fileName, out int caret) { caret = 0; return ""; }

        protected override ValueFormat CreateValueFormat() => new RlValueFormat();

        public override string ApiStubFile(out string fileName)
        {
            fileName = StubFileName;
            try { return RlStub.Build(GameApi.Builtins); }
            catch { return RlStub.Build(new List<ApiBuiltin>()); }
        }
    }

    internal static class RlFrontend
    {
        public static IrModule Build(string source, CompileOptions options, List<Diagnostic> errors)
        {
            source = source ?? "";
            RlProgram prog;
            try { prog = RlParser.Parse(source); }
            catch (RlSyntaxError e)
            {
                int s = Math.Max(0, Math.Min(e.Span.Start, source.Length));
                int en = Math.Max(s, Math.Min(e.Span.End, source.Length));
                errors.Add(new Diagnostic(e.Message, s, en));
                return null;
            }
            var name = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            return RlLowering.Build(prog, name, options, errors);
        }
    }

    /// <summary>R-style display of game values: TRUE / FALSE / NULL, c(1, 2), list(a = 1), Entities$Carrot.</summary>
    internal sealed class RlValueFormat : ValueFormat
    {
        public RlValueFormat()
        {
            NullText = "NULL"; TrueText = "TRUE"; FalseText = "FALSE";
            ItemSep = ", "; KeyValueSep = " = ";
            QuoteNestedStrings = true; Quote = '"';
            PositionFormat = "c({0}, {1})";
            CompanionFormat = "list({0}, {1})";
            DroneFormat = "<drone {0}>";
            CycleText = "...";
            RecognisePositions = true;
            FunctionName = n => n != null && n.StartsWith("__jlambda", StringComparison.Ordinal) ? "function" : n;
            ConstantText = (c, n) => c == null || c == "Directions" ? n : c + "$" + n;
        }

        public override string FormatNumber(double d) => RlNumbers.Print(d);

        static bool Scalar(object o) => o == null || o is bool || o is double || o is float || o is int || o is long || o is string || o is GameConstant;

        static int KindOf(object o) => o is bool ? 1 : (o is double || o is float || o is int || o is long) ? 2 : o is string ? 3 : o is GameConstant ? 4 : 0;

        protected override string Format(object obj, int depth, HashSet<object> seen, bool nested)
        {
            if (depth > MaxDepth) return "";
            switch (obj)
            {
                case string s: return nested ? "\"" + PrinterBase.Escape(s) + "\"" : s;
                case GameList l: return Vector(l.Items, depth, seen);
                case GameSet st: return Vector(st.Items, depth, seen);
                case GameDict d:
                    {
                        if (d.Entries.Count == 0) return "list()";
                        var sb = new StringBuilder("list(");
                        for (int i = 0; i < d.Entries.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            var k = d.Entries[i].Key;
                            if (k is string ks) sb.Append(RlNames.IsSyntactic(ks) ? ks : "\"" + PrinterBase.Escape(ks) + "\"");
                            else sb.Append(base.Format(k, depth + 1, seen, false));
                            sb.Append(" = ").Append(Format(d.Entries[i].Value, depth + 1, seen, true));
                        }
                        return sb.Append(')').ToString();
                    }
                case GameTuple t when !(t.Items.Count == 2 && (t.Items[0] is double || t.Items[0] is int)) && !(t.Items.Count == 2 && t.Items[1] is GameTuple):
                    return "list(" + string.Join(", ", t.Items.Select(x => Format(x, depth + 1, seen, true))) + ")";
            }
            return base.Format(obj, depth, seen, nested);
        }

        string Vector(List<object> items, int depth, HashSet<object> seen)
        {
            if (items.Count == 0) return "list()";
            int k = KindOf(items[0]);
            bool atomic = k != 0 && items.All(x => Scalar(x) && x != null && KindOf(x) == k);
            var inner = string.Join(", ", items.Select(x => Format(x, depth + 1, seen, true)));
            return (atomic ? "c(" : "list(") + inner + ")";
        }
    }

    internal static class RlNumbers
    {
        /// <summary>Like R's print: whole numbers in full, others with 7 significant digits (1e+20 style exponents).</summary>
        public static string Print(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsInfinity(d)) return d > 0 ? "Inf" : "-Inf";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            var s = d.ToString("G7", CultureInfo.InvariantCulture);
            return s.Replace("E+", "e+").Replace("E-", "e-");
        }
    }

    internal static class RlNames
    {
        public static readonly HashSet<string> Reserved = new HashSet<string>
        {
            "if", "else", "repeat", "while", "function", "for", "in", "next", "break", "TRUE", "FALSE", "NULL", "Inf", "NaN",
            "NA", "NA_integer_", "NA_real_", "NA_character_", "T", "F",
        };

        /// <summary>A name R accepts without backticks.</summary>
        public static bool IsSyntactic(string s)
        {
            if (string.IsNullOrEmpty(s) || Reserved.Contains(s)) return false;
            char c = s[0];
            if (!(char.IsLetter(c) || c == '.')) return false;
            if (c == '.' && s.Length > 1 && char.IsDigit(s[1])) return false;
            foreach (var ch in s) if (!(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_')) return false;
            return true;
        }
    }

    /// <summary>Tolerant lexer for colouring (covers every character, never throws).</summary>
    internal static class RlHighlighter
    {
        static readonly HashSet<string> Constants = new HashSet<string>
        {
            "TRUE", "FALSE", "NULL", "NA", "Inf", "NaN", "NA_integer_", "NA_real_", "NA_character_",
            "North", "East", "South", "West", "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards",
        };
        static readonly HashSet<string> Bags = new HashSet<string> { "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards" };
        static readonly string[] Ops = { "<<-", "->>", ":::", "|>", "<-", "->", "<=", ">=", "==", "!=", "&&", "||", "::", "**", "+", "-", "*", "/", "^", "<", ">", "!", "&", "|", "~", "?", ":", "=", "$", "@", "\\" };
        const string Punct = "(){}[],;";

        public static List<(int, int, TokenKind)> Highlight(string s)
        {
            var res = new List<(int, int, TokenKind)>();
            int i = 0;
            string prev = null, prev2 = null;
            void Add(int start, int end, TokenKind k, string text = null)
            {
                if (end <= start) return;
                res.Add((start, end - start, k));
                if (k != TokenKind.Whitespace && k != TokenKind.Comment) { prev2 = prev; prev = text ?? s.Substring(start, end - start); }
            }
            while (i < s.Length)
            {
                char c = s[i];
                int st = i;
                if (char.IsWhiteSpace(c))
                {
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    Add(st, i, TokenKind.Whitespace);
                    continue;
                }
                if (c == '#')
                {
                    while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++;
                    Add(st, i, TokenKind.Comment);
                    continue;
                }
                if (c == '"' || c == '\'' || c == '`')
                {
                    i++;
                    while (i < s.Length && s[i] != c)
                    {
                        if (s[i] == '\\' && c != '`') i++;
                        i++;
                    }
                    if (i < s.Length) i++;
                    if (i > s.Length) i = s.Length;
                    Add(st, i, c == '`' ? TokenKind.Identifier : TokenKind.String);
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    if (c == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X')) { i += 2; while (i < s.Length && Uri.IsHexDigit(s[i])) i++; }
                    else
                    {
                        while (i < s.Length && char.IsDigit(s[i])) i++;
                        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
                        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                        {
                            int save = i; i++;
                            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                            if (i < s.Length && char.IsDigit(s[i])) { while (i < s.Length && char.IsDigit(s[i])) i++; } else i = save;
                        }
                    }
                    if (i < s.Length && (s[i] == 'L' || s[i] == 'i')) i++;
                    Add(st, i, TokenKind.Number);
                    continue;
                }
                if (char.IsLetter(c) || c == '.')
                {
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.' || s[i] == '_')) i++;
                    var w = s.Substring(st, i - st);
                    TokenKind k;
                    if (prev == "$" && prev2 != null && Bags.Contains(prev2)) k = TokenKind.Constant;
                    else if (Constants.Contains(w)) k = TokenKind.Constant;
                    else if (RlLexer.Keywords.Contains(w)) k = TokenKind.Keyword;
                    else k = TokenKind.Identifier;
                    Add(st, i, k, w);
                    continue;
                }
                if (c == '%')
                {
                    int j = i + 1;
                    while (j < s.Length && s[j] != '%' && s[j] != '\n') j++;
                    if (j < s.Length && s[j] == '%') { i = j + 1; Add(st, i, TokenKind.Operator); continue; }
                    i++;
                    Add(st, i, TokenKind.Unknown);
                    continue;
                }
                string op = null;
                foreach (var o in Ops) if (string.CompareOrdinal(s, i, o, 0, o.Length) == 0) { op = o; break; }
                if (op != null) { i += op.Length; Add(st, i, TokenKind.Operator, op); continue; }
                if (Punct.IndexOf(c) >= 0) { i++; Add(st, i, TokenKind.Punctuation); continue; }
                i += char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
                Add(st, i, TokenKind.Unknown);
            }
            return res;
        }
    }

    /// <summary>farm.R: the game API as real R (stub functions, constant bags, game-like print) for external editors.</summary>
    internal static class RlStub
    {
        static string RType(string neutral)
        {
            if (string.IsNullOrWhiteSpace(neutral)) return "any";
            return neutral.Replace("map<", "named list<").Replace("float", "number").Replace("int", "whole number").Replace("bool", "TRUE/FALSE")
                .Replace("string", "text").Replace("void", "nothing").Replace("?", " or NULL").Replace("func", "function");
        }

        static string ParamName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "x";
            if (RlNames.Reserved.Contains(n)) return n + "_";
            return n;
        }

        static string DefaultResult(IrType t)
        {
            switch (t?.Kind ?? IrTypeKind.Any)
            {
                case IrTypeKind.Bool: return "invisible(FALSE)";
                case IrTypeKind.Int: case IrTypeKind.Float: return "invisible(0)";
                case IrTypeKind.String: return "invisible(\"\")";
                default: return "invisible(NULL)";
            }
        }

        static readonly HashSet<string> Handwritten = new HashSet<string> { "print", "quick_print", "min", "max", "abs", "random" };

        public static string Build(IEnumerable<ApiBuiltin> builtins)
        {
            var sb = new StringBuilder();
            sb.Append("# farm.R: the game API of The Farmer Was Replaced for R, written by the JavaFarmer mod.\n");
            sb.Append("# The game never runs this file. Your code in the game can use everything below without source().\n");
            sb.Append("# In RStudio or Rscript, run source(\"farm.R\") first: game actions do nothing here, but your code runs\n");
            sb.Append("# and prints like the game (print, cat, paste and as.character are replaced by game-style versions).\n\n");
            sb.Append("# ---- value formatting (like the game's print) -------------------------------------------------\n");
            sb.Append(Formatting);
            sb.Append("\n# ---- game constants: Entities$Carrot, Items$Hay, ... (any member name works) -------------------\n");
            sb.Append(".jf_constants <- function(category) structure(list(), category = category, class = \"jf_constants\")\n");
            sb.Append("`$.jf_constants` <- function(x, name) base::paste0(attr(x, \"category\"), \"$\", name)\n");
            sb.Append("`[[.jf_constants` <- function(x, i, ...) base::paste0(attr(x, \"category\"), \"$\", i)\n");
            foreach (var cat in GameApi.ConstantCategories)
                sb.Append("#' Every ").Append(GameApi.GameTypeOfCategory(cat)).Append(" of the game, for example ").Append(cat).Append("$").Append(Example(cat)).Append(".\n")
                  .Append(cat).Append(" <- .jf_constants(\"").Append(cat).Append("\")\n");
            sb.Append("\n#' Directions for move(), can_move(), swap() and measure().\n");
            foreach (var d in GameApi.DirectionNames) sb.Append(d).Append(" <- \"").Append(d).Append("\"\n");
            sb.Append("\n# ---- game functions ------------------------------------------------------------------------\n");
            sb.Append(HandwrittenText);
            foreach (var b in builtins ?? Enumerable.Empty<ApiBuiltin>())
            {
                if (b == null || string.IsNullOrEmpty(b.PythonName) || Handwritten.Contains(b.PythonName)) continue;
                var doc = (b.Doc ?? "").Replace("\n", " ");
                if (b.Extension != null) doc += (doc.Length > 0 ? " " : "") + "(" + b.Extension + " extension)";
                sb.Append("\n#' ").Append(doc).Append('\n');
                var ps = new List<string>();
                foreach (var p in b.Params)
                {
                    var pn = p.Variadic ? "..." : ParamName(p.Name);
                    sb.Append("#' @param ").Append(pn).Append(' ').Append(RType(p.TypeText)).Append(p.Optional && !p.Variadic ? " (optional)" : "").Append('\n');
                    ps.Add(p.Variadic ? "..." : p.Optional ? pn + " = NULL" : pn);
                }
                sb.Append("#' @return ").Append(RType(b.ReturnText)).Append('\n');
                sb.Append(b.PythonName).Append(" <- function(").Append(string.Join(", ", ps)).Append(") ").Append(DefaultResult(b.ReturnType)).Append('\n');
            }
            return sb.ToString();
        }

        static string Example(string cat)
        {
            switch (cat)
            {
                case "Entities": return "Carrot";
                case "Items": return "Hay";
                case "Grounds": return "Soil";
                case "Unlocks": return "Speed";
                case "Hats": return "Straw_Hat";
                default: return "Hay";
            }
        }

        const string Formatting = @".jf_bags <- c(""Entities"", ""Items"", ""Grounds"", ""Unlocks"", ""Hats"", ""Leaderboards"")
.jf_is_constant <- function(s) {
  if (s %in% c(""North"", ""East"", ""South"", ""West"")) return(TRUE)
  parts <- base::strsplit(s, ""$"", fixed = TRUE)[[1]]
  base::length(parts) == 2 && parts[1] %in% .jf_bags
}
.jf_num <- function(x, digits) {
  if (is.na(x)) return(""NA"")
  if (is.finite(x) && x == base::trunc(x) && base::abs(x) < 1e15) return(base::sprintf(""%.0f"", x))
  base::format(x, digits = digits)
}
.jf_scalar <- function(x, nested, digits) {
  if (is.logical(x)) return(if (x) ""TRUE"" else ""FALSE"")
  if (is.numeric(x)) return(.jf_num(x, digits))
  if (is.character(x)) {
    if (nested && !.jf_is_constant(x)) return(base::paste0(""\"""", x, ""\""""))
    return(x)
  }
  base::format(x)
}
.jf_key <- function(k) {
  if (base::make.names(k) == k || .jf_is_constant(k)) k else base::paste0(""\"""", k, ""\"""")
}
.jf_format <- function(x, nested = FALSE, digits = 7) {
  if (is.null(x)) return(""NULL"")
  if (is.function(x)) return(""function"")
  if (inherits(x, ""jf_constants"")) return(attr(x, ""category""))
  nm <- base::names(x)
  if (base::length(x) == 0) return(""list()"")
  if (!is.null(nm)) {
    items <- base::vapply(base::seq_along(x), function(i) base::paste0(.jf_key(nm[i]), "" = "", .jf_format(x[[i]], TRUE, digits)), """")
    return(base::paste0(""list("", base::paste(items, collapse = "", ""), "")""))
  }
  if (is.list(x)) {
    kinds <- base::vapply(x, function(e) if (is.null(e) || base::length(e) != 1 || is.list(e)) ""x"" else base::class(e)[1], """")
    items <- base::vapply(x, function(e) .jf_format(e, TRUE, digits), """")
    same <- base::all(kinds != ""x"") && base::length(base::unique(base::sub(""integer"", ""numeric"", kinds))) == 1
    return(base::paste0(if (same) ""c("" else ""list("", base::paste(items, collapse = "", ""), "")""))
  }
  if (base::length(x) == 1) return(.jf_scalar(x, nested, digits))
  items <- base::vapply(base::seq_along(x), function(i) .jf_scalar(x[[i]], TRUE, digits), """")
  base::paste0(""c("", base::paste(items, collapse = "", ""), "")"")
}
.jf_str <- function(x) {
  if (base::length(x) == 1 && !is.list(x) && is.null(base::names(x))) return(.jf_scalar(x, FALSE, 15))
  .jf_format(x, FALSE, 15)
}

#' Prints a value like the game does (TRUE, NULL, 3, c(1, 2), list(a = 1)).
#' @param x any value
print <- function(x, ...) {
  base::cat(.jf_format(x), ""\n"", sep = """")
  invisible(x)
}
#' Prints several values separated by spaces, like the game's print(a, b).
cat <- function(..., sep = "" "") {
  args <- list(...)
  base::cat(base::paste(base::vapply(args, function(a) .jf_format(a), """"), collapse = sep))
  invisible(NULL)
}
#' Joins values as text (numbers like the game's str()).
paste <- function(..., sep = "" "") base::paste(base::vapply(list(...), .jf_str, """"), collapse = sep)
paste0 <- function(...) base::paste(base::vapply(list(...), .jf_str, """"), collapse = """")
as.character <- function(x, ...) .jf_str(x)
";

        const string HandwrittenText = @"
#' Prints the values to the output page only (no delay).
#' @param ... any values
quick_print <- function(...) {
  base::cat(base::paste(base::vapply(list(...), function(a) .jf_format(a), """"), collapse = "" ""), ""\n"", sep = """")
  invisible(NULL)
}

#' The smallest argument.
#' @param a number
#' @param b number
min <- function(a, b, ...) base::min(a, b, ...)

#' The largest argument.
#' @param a number
#' @param b number
max <- function(a, b, ...) base::max(a, b, ...)

#' Absolute value.
#' @param x number
abs <- function(x) base::abs(x)

#' A random number in [0, 1).
random <- function() stats::runif(1)
";
    }
}
