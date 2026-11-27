using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Rust (edition 2024 subset) as a JavaFarmer language: lexer → parser → checker (rustc wording) → IR lowering, the
    /// IR → Rust printer, highlighting, the farm.rs API stub. See docs/languages/rust.md.
    /// </summary>
    public sealed class RustLanguage : LanguageBase, IPrinterDiagnostics, ISignatureProvider
    {
        public const string Template = "use farm::*;\n\nfn main() {\n\t\n}";
        public const string StubFileName = "farm.rs";

        public override string Id => "rust";
        public override string DisplayName => "Rust";
        public override string FileExtension => ".rs";
        protected override NamingStyle ApiNaming => NamingStyle.Snake;

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
                var errs = new List<Diagnostic>();
                var ir = Build(source, options, errs, comments: false);
                if (ir == null || errs.Count > 0) return CompileResults.Fail(source, errs.Count > 0 ? errs : new List<Diagnostic> { new Diagnostic("compilation failed", 0, Math.Min(1, source.Length)) });
                return IrEmitter.Compile(ir, source, options);
            }
            catch (Exception ex)
            {
                return CompileResults.Fail(source, new List<Diagnostic> { CompileResults.Internal(ex, source) });
            }
        }

        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors) => Build(source, options, errors, comments: true);

        static IrModule Build(string source, CompileOptions options, List<Diagnostic> errors, bool comments)
        {
            var fileName = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            try
            {
                var file = RustParser.Parse(source);
                var chk = new RustChecker(file, fileName, options?.OtherFiles) { Options = options };
                chk.Run();
                var m = RustLowering.Lower(chk, file, fileName, source, comments);
                if (comments) IrComments.Tidy(m);
                return m;
            }
            catch (RustAbort a)
            {
                errors.Add(a.Diag);
                return null;
            }
        }

        protected override string PrintImpl(IrModule module, PrintOptions options) => RustPrinter.Print(module, options, out _);

        public string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            try { return RustPrinter.Print(module, options ?? new PrintOptions(), out problems); }
            catch (Exception ex) { problems = new[] { "internal printer error: " + ex.Message }; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        /// <summary>Top-level fn / const / static declarations (bodies not checked; other files not needed), for files of other languages that use this one.</summary>
        public ModuleSignature Signatures(string source, CompileOptions options)
        {
            string name = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            try
            {
                var d = RModuleDecls.Get(name, source ?? "");
                if (d.Error != null) return ModuleSignature.Failed(name, Id, d.Error);
                var sig = new ModuleSignature { Module = name, LanguageId = Id, LanguageName = DisplayName, Ok = true };
                foreach (var f in d.Fns.Values)
                {
                    var fs = new FunctionSignature { Name = f.Name, PyName = f.PyName, ReturnType = RTy.ToIr(f.Ret), IsPrivate = !f.Pub, IsEntry = f.Name == "main" };
                    for (int i = 0; i < f.Params.Count; i++)
                        fs.Params.Add(new ParamSignature { Name = i < f.ParamNames.Count ? f.ParamNames[i] : "arg" + i, Type = RTy.ToIr(f.Params[i]) });
                    sig.Functions.Add(fs);
                }
                foreach (var v in d.Vars.Values)
                    sig.Globals.Add(new GlobalSignature { Name = v.Name, PyName = v.Ir?.PyName ?? v.Name, Type = RTy.ToIr(v.Type), IsConst = !v.Mut, IsPrivate = !v.Pub });
                return sig;
            }
            catch (Exception ex)
            {
                return ModuleSignature.Failed(name, Id, "internal JavaFarmer error while reading " + name + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ editor services
        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            try { return RustHighlighter.Highlight(source ?? ""); }
            catch { return string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, TokenKind.Unknown) }; }
        }

        static readonly IReadOnlyCollection<string> keywords = new[]
        {
            "as", "break", "const", "continue", "crate", "else", "false", "fn", "for", "if", "in", "let", "loop", "match", "mod", "move",
            "mut", "pub", "return", "static", "true", "unsafe", "use", "while",
        };
        public override IReadOnlyCollection<string> Keywords => keywords;

        public override string NewFileTemplate(string fileName, out int caret)
        {
            caret = Template.IndexOf('\t') + 1;
            return Template;
        }

        /// <summary>"can_harvest" → "can_harvest", "move" → "move_to", "print" → "println!", "quick_print" → "quick_print!".</summary>
        public override string ApiName(string pythonName)
        {
            try { return RustApi.ApiName(pythonName); } catch { return null; }
        }

        /// <summary>Inverse of ApiName; also accepts "r#move", "print!" and the "_with" forms ("use_item_with" → "use_item").</summary>
        public override string FromApiName(string name)
        {
            try { return RustApi.FromApiName(name); } catch { return null; }
        }

        /// <summary>Rust Debug style: true/false, None, [1, 2], {1, 2}, {"k": v}, (x, y) positions, enum variants by name.</summary>
        protected override ValueFormat CreateValueFormat() => new ValueFormat
        {
            NullText = "None", TrueText = "true", FalseText = "false",
            ListOpen = "[", ListClose = "]", SetOpen = "{", SetClose = "}", MapOpen = "{", MapClose = "}",
            ItemSep = ", ", KeyValueSep = ": ",
            QuoteNestedStrings = true, Quote = '"',
            TupleOpen = "(", TupleClose = ")",
            PositionFormat = "({0}, {1})",
            CompanionFormat = "({0}, {1})",
            DroneFormat = "Drone({0})",
            CycleText = "[...]",
            ConstantText = (c, n) => n,
            FunctionName = n =>
            {
                if (string.IsNullOrEmpty(n)) return n;
                if (n.StartsWith("__j", StringComparison.Ordinal)) return "{closure}";
                return RustApi.ApiName(n) ?? n;
            },
        };

        /// <summary>farm.rs: the game API as a Rust library crate (`use farm::*;`), generated from GameApi.Builtins.</summary>
        public override string ApiStubFile(out string fileName)
        {
            fileName = StubFileName;
            try { return RustStub.Build(null); }
            catch (Exception ex) { return "// JavaFarmer could not write the Rust API stub: " + ex.Message + "\n"; }
        }
    }
}
