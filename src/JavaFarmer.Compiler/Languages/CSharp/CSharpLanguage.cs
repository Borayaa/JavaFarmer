using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using JavaFarmer.Compiler.CSharp;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// C# 12 as a JavaFarmer language (docs/languages/csharp.md): top-level statements with local functions, or one
    /// class with static members (static void Main() for a program, static class File for a library used as File.Name).
    /// PascalCase game API (Harvest(), CanHarvest(), Move(North)), Console.WriteLine, List/Dictionary/HashSet, lambdas.
    /// </summary>
    public sealed class CSharpLanguage : LanguageBase, IPrinterDiagnostics, ISignatureProvider
    {
        public const string Template = "// C# top-level statements: they run from top to bottom\n";
        public const string StubFileName = "Farm.cs";

        public override string Id => "csharp";
        public override string DisplayName => "C#";
        public override string FileExtension => ".cs";
        protected override NamingStyle ApiNaming => NamingStyle.Pascal;

        static readonly EditorRules rules = new EditorRules
        {
            IndentAfter = new[] { "{" },
            DedentOn = new[] { "}" },
            LineComment = "//",
            BlockCommentStart = "/*",
            BlockCommentEnd = "*/",
            IndentUnit = "\t",
            Brackets = new[] { "{}", "()", "[]" },
        };
        public override EditorRules Editor => rules;

        // ------------------------------------------------------------------ compiler
        public override CompileResult Compile(string source, CompileOptions options)
        {
            source = source ?? "";
            options = options ?? new CompileOptions();
            try
            {
                var ir = Frontend(source, options, out var errors, false);
                if (errors.Count > 0) return CompileResults.Fail(source, errors);
                return IrEmitter.Compile(ir, source, options);
            }
            catch (Exception ex)
            {
                return CompileResults.Fail(source, new List<Diagnostic> { CompileResults.Internal(ex, source) });
            }
        }

        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors)
        {
            var m = Frontend(source, options, out var errs, true);
            errors.AddRange(errs);
            return m;
        }

        internal static IrModule Frontend(string source, CompileOptions options, out List<Diagnostic> errors, bool comments)
        {
            errors = new List<Diagnostic>();
            var fileName = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            var cu = CsParser.Parse(source, out var syntax);
            if (syntax != null) { errors.Add(syntax); return null; }
            var binder = new CsBinder(cu, fileName, options?.OtherFiles, source) { Options = options };
            IrModule m;
            try { m = binder.Run(); }
            catch (CsAbort a) { errors.Add(a.Diag); return null; }
            if (binder.Errors.Count > 0)
            {
                errors.AddRange(binder.Errors.OrderBy(d => d.Start));
                return null;
            }
            if (comments)
            {
                IrComments.Attach(m, Comments(source), source);
                IrComments.Tidy(m);
            }
            return m;
        }

        static List<IrComment> Comments(string source)
        {
            var res = new List<IrComment>();
            foreach (var t in new CsLexer(source, true).LexAll())
            {
                if (t.Kind != CTk.Comment) continue;
                var text = t.Text;
                if (text.StartsWith("///")) res.Add(new IrComment { Text = text.Substring(3), IsDoc = true, Span = new Span(t.Start, t.End) });
                else if (text.StartsWith("//")) res.Add(new IrComment { Text = text.Substring(2), Span = new Span(t.Start, t.End) });
                else if (text.StartsWith("/*"))
                {
                    bool doc = text.StartsWith("/**") && text.Length > 4;
                    int body = doc ? 3 : 2;
                    int end = text.EndsWith("*/") && text.Length >= body + 2 ? text.Length - 2 : text.Length;
                    res.Add(new IrComment { Text = text.Substring(body, Math.Max(0, end - body)), IsBlock = true, IsDoc = doc, Span = new Span(t.Start, t.End) });
                }
            }
            return res;
        }

        /// <summary>
        /// Declarations of a C# window for other windows (any language): the static members of its class, or the local
        /// functions and typed top-level variables of a top-level-statements window. Bodies are not checked.
        /// </summary>
        public ModuleSignature Signatures(string source, CompileOptions options)
        {
            var name = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            try
            {
                source = source ?? "";
                var cu = CsParser.Parse(source, out var syntax);
                if (cu == null)
                    return ModuleSignature.Failed(name, Id, name + " (C#) does not compile (line " + LineOf(source, syntax?.Start ?? 0) + ": " + (syntax?.Message ?? "syntax error") + ")");
                var b = new CsBinder(cu, name, options?.OtherFiles, source) { Options = options };
                b.DeclareOnly();
                var own = b.Own;
                var globals = new List<(string Name, CType Type, bool Const)>();
                foreach (var f in own.Fields.Values) globals.Add((f.Name, f.Type, f.IsConst || f.IsReadonly));
                if (!own.IsClass)
                    foreach (var lv in cu.TopLevel.OfType<CLocalVar>())
                        foreach (var v in lv.Vars)
                        {
                            bool isVar = lv.Type.Name == "var" && lv.Type.Args == null;
                            var t = isVar ? null : b.ResolveType(lv.Type);
                            if (globals.All(g => g.Name != v.Name)) globals.Add((v.Name, t, lv.IsConst));
                        }
                var fnPy = PyNaming.ModuleLevel(own.Methods.Keys, globals.Select(g => g.Name), out var gPy);
                var sig = new ModuleSignature { Module = name, LanguageId = Id, LanguageName = DisplayName, Ok = true };
                foreach (var m in own.Methods.Values)
                {
                    var fs = new FunctionSignature { Name = m.Name, PyName = fnPy[m.Name], ReturnType = m.ReturnType.Ir(), IsPrivate = !m.IsAccessible && !m.IsLocal, IsEntry = m.Decl != null && m.Decl.Name == "Main" && !m.IsLocal };
                    for (int i = 0; i < m.ParamTypes.Count; i++) fs.Params.Add(new ParamSignature { Name = m.ParamNames[i], Type = m.ParamTypes[i].Ir(), Optional = m.HasDefault[i] });
                    sig.Functions.Add(fs);
                }
                foreach (var g in globals)
                    sig.Globals.Add(new GlobalSignature { Name = g.Name, PyName = gPy[g.Name], Type = g.Type?.Ir() ?? IrType.Unknown, IsConst = g.Const, IsPrivate = own.Fields.TryGetValue(g.Name, out var fd) && !fd.IsAccessible });
                return sig;
            }
            catch (Exception ex)
            {
                return ModuleSignature.Failed(name, Id, "internal JavaFarmer error while reading " + name + " (C#): " + ex.Message);
            }
        }

        static int LineOf(string src, int offset)
        {
            int line = 1;
            for (int i = 0; i < Math.Min(offset, src.Length); i++) if (src[i] == '\n') line++;
            return line;
        }

        protected override string PrintImpl(IrModule module, PrintOptions options) => CsPrinter.Print(module, options, out _);

        public string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            try { return CsPrinter.Print(module, options ?? new PrintOptions(), out problems); }
            catch (Exception ex) { problems = new[] { "internal printer error: " + ex.Message }; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        // ------------------------------------------------------------------ editor services
        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            try { return CsHighlighter.Highlight(source ?? ""); }
            catch { return string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, TokenKind.Unknown) }; }
        }

        public override IReadOnlyCollection<string> Keywords => CsHighlighter.Keywords;

        /// <summary>A new window: top-level statements, the farming loop with the caret inside.</summary>
        public override string NewFileTemplate(string fileName, out int caret)
        {
            caret = Template.Length;
            return Template;
        }

        public override string ApiName(string pythonName)
        {
            try { return CsApi.ApiName(pythonName); } catch { return null; }
        }

        public override string FromApiName(string name)
        {
            try { return CsApi.FromApiName(name); } catch { return null; }
        }

        /// <summary>
        /// What Console.WriteLine shows in C#: True / False, numbers like double.ToString() (3, 2.5), strings as they are;
        /// collections (which C# itself prints as their type name) as collection expressions [1, 2, 3], dictionaries
        /// as {key: value}, tuples as (1, 2), null as "null".
        /// </summary>
        protected override ValueFormat CreateValueFormat() => new CsValueFormat
        {
            NullText = "null", TrueText = "True", FalseText = "False",
            ListOpen = "[", ListClose = "]", SetOpen = "[", SetClose = "]", MapOpen = "{", MapClose = "}",
            ItemSep = ", ", KeyValueSep = ": ", QuoteNestedStrings = true, Quote = '"',
            TupleOpen = "(", TupleClose = ")", RecognisePositions = false,
            DroneFormat = "Drone {0}",
            CycleText = "[...]",
            FunctionName = n =>
            {
                if (string.IsNullOrEmpty(n)) return n;
                if (n.StartsWith("__j", StringComparison.Ordinal)) return "lambda";
                string a = null;
                try { a = CsApi.ApiName(n); } catch { }
                return a ?? n;
            },
        };

        sealed class CsValueFormat : ValueFormat
        {
            public override string FormatNumber(double d)
            {
                if (double.IsNaN(d)) return "NaN";
                if (double.IsPositiveInfinity(d)) return "∞";
                if (double.IsNegativeInfinity(d)) return "-∞";
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Optional (set by the plugin): category ("Entities", "Items", ...) → the game's member names, so that the
        /// stub file lists Entities.Carrot etc. for external editors. The compiler itself never needs the list.
        /// </summary>
        public static Func<string, IEnumerable<string>> ConstantMembers;

        /// <summary>The stub with the constant members the given sources use (Roslyn conformance tests).</summary>
        public static string BuildStub(IEnumerable<string> sources)
        {
            var all = string.Join("\n", sources ?? Enumerable.Empty<string>());
            return CsStub.Build(cat =>
            {
                var res = new HashSet<string>(ConstantMembers?.Invoke(cat) ?? Enumerable.Empty<string>());
                int i = 0;
                while ((i = all.IndexOf(cat + ".", i, StringComparison.Ordinal)) >= 0)
                {
                    if (i > 0 && (char.IsLetterOrDigit(all[i - 1]) || all[i - 1] == '_')) { i += cat.Length; continue; }
                    int s = i + cat.Length + 1, e = s;
                    while (e < all.Length && (char.IsLetterOrDigit(all[e]) || all[e] == '_')) e++;
                    if (e > s && !char.IsDigit(all[s])) res.Add(all.Substring(s, e - s));
                    i = e;
                }
                return res;
            });
        }

        public override string ApiStubFile(out string fileName)
        {
            fileName = StubFileName;
            try { return CsStub.Build(ConstantMembers); }
            catch (Exception ex) { return "// JavaFarmer could not build the API stub: " + ex.Message + "\n"; }
        }
    }
}
