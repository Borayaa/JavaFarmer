using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Convenience base for ILanguage: Compile = ToIr + IrEmitter, API naming by NamingStyle, FormatValue via ValueFormat,
    /// exception safety. Override what differs.
    /// </summary>
    public abstract class LanguageBase : ILanguage
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string FileExtension { get; }
        public virtual bool IsNative => false;
        /// <summary>How game builtins are spelled in this language.</summary>
        protected abstract NamingStyle ApiNaming { get; }

        /// <summary>Frontend: source → IR. May throw CompileAbort-like exceptions; ToIr wraps it.</summary>
        protected abstract IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors);
        protected abstract string PrintImpl(IrModule module, PrintOptions options);

        public virtual CompileResult Compile(string source, CompileOptions options)
        {
            source = source ?? "";
            try
            {
                var ir = ToIr(source, options, out var errors);
                if (ir == null || errors.Count > 0) return CompileResults.Fail(source, errors.Count > 0 ? errors.ToList() : new List<Diagnostic> { new Diagnostic("compilation failed", 0, Math.Min(1, source.Length)) });
                return IrEmitter.Compile(ir, source, options);
            }
            catch (Exception ex)
            {
                return CompileResults.Fail(source, new List<Diagnostic> { CompileResults.Internal(ex, source) });
            }
        }

        public virtual IrModule ToIr(string source, CompileOptions options, out IReadOnlyList<Diagnostic> errors)
        {
            var errs = new List<Diagnostic>();
            errors = errs;
            try
            {
                var m = Frontend(source ?? "", options ?? new CompileOptions(), errs);
                if (errs.Count > 0) { errs.Sort((a, b) => a.Start.CompareTo(b.Start)); return null; }
                if (m != null && m.SourceLanguage == null) m.SourceLanguage = Id;
                return m;
            }
            catch (Exception ex)
            {
                errs.Add(CompileResults.Internal(ex, source ?? ""));
                return null;
            }
        }

        public virtual string Print(IrModule module, PrintOptions options)
        {
            try { return PrintImpl(module, options ?? new PrintOptions()); }
            catch (Exception ex) { return (Editor.LineComment ?? "//") + " JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        public abstract IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source);
        public abstract IReadOnlyCollection<string> Keywords { get; }
        public virtual EditorRules Editor { get; } = new EditorRules();
        public abstract string NewFileTemplate(string fileName, out int caret);

        public virtual string ApiName(string pythonName) => GameApi.ApiName(pythonName, ApiNaming);
        public virtual string FromApiName(string name) => GameApi.FromApiName(name, ApiNaming);

        ValueFormat valueFormat;
        /// <summary>Override to configure the print style; created once.</summary>
        protected virtual ValueFormat CreateValueFormat() => new ValueFormat();
        public virtual string FormatValue(object gameValue)
        {
            try { return (valueFormat ?? (valueFormat = CreateValueFormat())).Format(gameValue); }
            catch { return gameValue?.ToString() ?? ""; }
        }

        public abstract string ApiStubFile(out string fileName);
    }

    /// <summary>Shared CompileResult helpers.</summary>
    public static class CompileResults
    {
        /// <summary>Failed result; clamps every span into the source and keeps it non-empty where possible.</summary>
        public static CompileResult Fail(string src, List<Diagnostic> errors)
        {
            src = src ?? "";
            foreach (var d in errors)
            {
                d.Start = Math.Max(0, Math.Min(d.Start, src.Length));
                d.End = Math.Max(d.Start, Math.Min(d.End, src.Length));
                if (d.End == d.Start && d.Start > 0 && d.Start == src.Length) d.Start--;
            }
            return new CompileResult { Success = false, Python = null, Map = null, Errors = errors };
        }

        public static Diagnostic Internal(Exception ex, string src) =>
            new Diagnostic("internal JavaFarmer compiler error: " + ex.GetType().Name + ": " + ex.Message, 0, Math.Min(1, (src ?? "").Length));
    }

    /// <summary>
    /// Helpers for IR → source printers: indentation, comments, precedence, literals, names.
    /// Subclass it, write statements with Line()/Open()/Close() and expressions as strings.
    /// </summary>
    public abstract class PrinterBase
    {
        protected readonly StringBuilder Out = new StringBuilder();
        protected readonly PrintOptions Options;
        protected int Depth;

        protected PrinterBase(PrintOptions options) { Options = options ?? new PrintOptions(); }

        /// <summary>"//" or "#".</summary>
        protected virtual string LineCommentPrefix => "//";
        /// <summary>null when the language has no block comments (Python, R): block comments print as line comments.</summary>
        protected virtual string BlockCommentStart => "/*";
        protected virtual string BlockCommentEnd => "*/";

        public string Result => Out.ToString();
        public override string ToString() => Out.ToString();

        protected string IndentText => string.Concat(Enumerable.Repeat(Options.Indent, Math.Max(0, Depth)));

        /// <summary>Writes one line at the current depth (an empty text writes an empty line without indentation).</summary>
        protected void Line(string text = "")
        {
            if (string.IsNullOrEmpty(text)) { Out.Append(Options.Newline); return; }
            Out.Append(IndentText).Append(text).Append(Options.Newline);
        }

        /// <summary>Line followed by a trailing comment of the node, if any.</summary>
        protected void Line(string text, IrNode commentsOf) => Line(text + TrailingComment(commentsOf));

        protected void Indent() => Depth++;
        protected void Dedent() => Depth--;

        /// <summary>Removes the last newline (to append " else {" etc. to a closing line).</summary>
        protected bool TrimLastNewline()
        {
            var nl = Options.Newline;
            if (Out.Length >= nl.Length && Out.ToString(Out.Length - nl.Length, nl.Length) == nl) { Out.Length -= nl.Length; return true; }
            return false;
        }

        /// <summary>Prints the node's leading comments on their own lines.</summary>
        protected void LeadingComments(IrNode n)
        {
            if (!Options.Comments || n?.Leading == null) return;
            foreach (var c in n.Leading) Comment(c);
        }

        protected void Comments(IEnumerable<IrComment> cs)
        {
            if (!Options.Comments || cs == null) return;
            foreach (var c in cs) Comment(c);
        }

        /// <summary>One comment on its own line(s).</summary>
        protected virtual void Comment(IrComment c)
        {
            var text = c.Text ?? "";
            if (c.IsBlock && BlockCommentStart != null)
            {
                var lines = text.Replace("\r\n", "\n").Split('\n');
                if (lines.Length == 1) { Line(BlockCommentStart + text + BlockCommentEnd); return; }
                Line(BlockCommentStart + lines[0].TrimEnd());
                for (int i = 1; i < lines.Length; i++) Out.Append(IndentText).Append(lines[i].TrimEnd()).Append(i == lines.Length - 1 ? BlockCommentEnd : "").Append(Options.Newline);
                return;
            }
            foreach (var l in text.Replace("\r\n", "\n").Split('\n'))
            {
                var t = c.IsBlock ? l.Trim().TrimStart('*').TrimEnd() : l.TrimEnd();
                if (c.IsBlock && t.Length > 0 && !t.StartsWith(" ")) t = " " + t;
                Line(LineCommentPrefix + t);
            }
        }

        /// <summary>" // text" for the node's trailing comment, or "".</summary>
        protected virtual string TrailingComment(IrNode n)
        {
            if (!Options.Comments || n?.Trailing == null) return "";
            var c = n.Trailing;
            if (c.IsBlock && BlockCommentStart != null) return " " + BlockCommentStart + c.Text + BlockCommentEnd;
            return " " + LineCommentPrefix + (c.Text ?? "").Replace("\n", " ");
        }

        // ------------------------------------------------------------------ precedence
        /// <summary>C-family binding strength (higher binds tighter). Override for other languages.</summary>
        protected virtual int Prec(IrBinOp op)
        {
            switch (op)
            {
                case IrBinOp.Or: return 1;
                case IrBinOp.And: return 2;
                case IrBinOp.Eq: case IrBinOp.Ne: return 3;
                case IrBinOp.Lt: case IrBinOp.Le: case IrBinOp.Gt: case IrBinOp.Ge: return 4;
                case IrBinOp.Add: case IrBinOp.Sub: case IrBinOp.Concat: return 5;
                case IrBinOp.Pow: return 7;
                default: return 6;   // * / % //
            }
        }
        public const int PrecTernary = 0, PrecUnary = 8, PrecPostfix = 9, PrecAtom = 10;

        /// <summary>Parenthesises text of precedence prec when the context needs at least min.</summary>
        protected static string Wrap(string text, int prec, int min) => prec < min ? "(" + text + ")" : text;

        // ------------------------------------------------------------------ literals / names
        /// <summary>C-style escaped string body (\\ \" \n \t \r). quote: the delimiter to escape.</summary>
        public static string Escape(string s, char quote = '"')
        {
            var sb = new StringBuilder();
            foreach (var ch in s ?? "")
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (ch == quote) sb.Append('\\').Append(ch); else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Round-trippable decimal without exponent, always with a '.' ("2.0", "0.001").</summary>
        public static string FormatFloat(double v) => PyFormat.Double(v);

        public static string Camel(string snake) => GameApi.ToStyle(snake, NamingStyle.Camel);
        public static string Pascal(string snake) => GameApi.ToStyle(snake, NamingStyle.Pascal);
        /// <summary>camelCase / PascalCase → snake_case ("moveNDir" → "move_n_dir").</summary>
        public static string Snake(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < (name ?? "").Length; i++)
            {
                char ch = name[i];
                if (char.IsUpper(ch)) { if (i > 0 && name[i - 1] != '_') sb.Append('_'); sb.Append(char.ToLowerInvariant(ch)); }
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    /// <summary>Number formatting shared by the emitter and printers.</summary>
    public static class PyFormat
    {
        /// <summary>Plain decimal (the game tokenizer only reads plain decimals) with ".0" for integral values.</summary>
        public static string Double(double v)
        {
            if (double.IsInfinity(v) || double.IsNaN(v)) return "0";
            var s = v.ToString("R", CultureInfo.InvariantCulture);
            if (s.IndexOf('E') >= 0)
            {
                int ei = s.IndexOf('E');
                var mant = s.Substring(0, ei);
                int exp = int.Parse(s.Substring(ei + 1), CultureInfo.InvariantCulture);
                bool neg = mant.StartsWith("-");
                if (neg) mant = mant.Substring(1);
                int dot = mant.IndexOf('.');
                string digits = dot < 0 ? mant : mant.Remove(dot, 1);
                int pointPos = (dot < 0 ? mant.Length : dot) + exp;
                string res;
                if (pointPos <= 0) res = "0." + new string('0', -pointPos) + digits;
                else if (pointPos >= digits.Length) res = digits + new string('0', pointPos - digits.Length) + ".0";
                else res = digits.Substring(0, pointPos) + "." + digits.Substring(pointPos);
                s = (neg ? "-" : "") + res;
            }
            if (s.IndexOf('.') < 0) s += ".0";
            return s;
        }
    }
}
