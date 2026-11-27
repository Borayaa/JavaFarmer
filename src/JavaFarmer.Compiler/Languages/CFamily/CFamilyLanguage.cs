using System;
using System.Collections.Generic;
using JavaFarmer.Compiler.CFamily;
using CLexer = JavaFarmer.Compiler.CFamily.Lexer;
using CParser = JavaFarmer.Compiler.CFamily.Parser;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Shared implementation of the C and C++ languages (Languages/CFamily): one lexer / parser / binder with dialect
    /// switches, one printer, one highlighter. CLanguage and CppLanguage only pick the dialect.
    /// </summary>
    public abstract class CFamilyLanguage : LanguageBase, IPrinterDiagnostics, ISignatureProvider
    {
        internal abstract Dialect Dialect { get; }
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

        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors)
        {
            var d = Dialect;
            var lex = CLexer.Lex(source, d);
            if (lex.Errors.Count > 0) { errors.Add(lex.Errors[0]); return null; }
            var unit = CParser.ParseUnit(lex, source, d, out var perr);
            if (perr != null) { errors.Add(perr); return null; }
            var others = options.OtherFiles != null && options.OtherFiles.Count > 0 ? new OtherFiles(d, options) : null;
            var b = new Binder(d, source, options, others);
            var m = b.Bind(unit, lex.Comments);
            if (b.Errors.Count > 0) { errors.AddRange(b.Errors); return null; }
            return m;
        }

        /// <summary>
        /// LANGUAGE-GUIDE §9: the functions and globals this window offers to windows in other languages (declarations only:
        /// bodies are not checked, other windows are not needed; static ones and main are not exported).
        /// </summary>
        public ModuleSignature Signatures(string source, CompileOptions options)
        {
            var module = options?.FileName ?? "main";
            try
            {
                source = source ?? "";
                var d = Dialect;
                var lex = CLexer.Lex(source, d, false);
                Diagnostic err = lex.Errors.Count > 0 ? lex.Errors[0] : null;
                var unit = err == null ? CParser.ParseUnit(lex, source, d, out err) : null;
                if (err != null) return ModuleSignature.Failed(module, Id, module + " (" + DisplayName + ") does not compile (line " + LineOf(source, err.Start) + ": " + err.Message + ")");
                var decls = new OtherFiles.Decls();
                new Binder(d, source, new CompileOptions { FileName = module }, null).Declarations(unit, decls);
                var fnPy = PyNaming.ModuleLevel(decls.Functions.Keys, decls.Globals.Keys, out var globalPy);
                var sig = new ModuleSignature { Module = module, LanguageId = Id, LanguageName = DisplayName, Ok = true };
                foreach (var kv in decls.Functions)
                {
                    var fs = new FunctionSignature { Name = kv.Key, PyName = fnPy[kv.Key], ReturnType = kv.Value.Ret.Ir() };
                    foreach (var p in kv.Value.Params) fs.Params.Add(new ParamSignature { Type = p.Ir() });
                    sig.Functions.Add(fs);
                }
                foreach (var kv in decls.Globals)
                    sig.Globals.Add(new GlobalSignature { Name = kv.Key, PyName = globalPy[kv.Key], Type = kv.Value.Ir() });
                return sig;
            }
            catch (Exception ex) { return ModuleSignature.Failed(module, Id, "internal JavaFarmer error while reading " + module + ": " + ex.Message); }
        }

        static int LineOf(string s, int offset)
        {
            int line = 1;
            for (int i = 0; i < Math.Min(offset, s.Length); i++) if (s[i] == '\n') line++;
            return line;
        }

        protected override string PrintImpl(IrModule module, PrintOptions options) => CPrinter.Print(module, options, Dialect, out _);

        public string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            try { return CPrinter.Print(module, options ?? new PrintOptions(), Dialect, out problems); }
            catch (Exception ex) { problems = new[] { "internal printer error: " + ex.Message }; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source) => Highlighter.Highlight(source ?? "", Dialect);

        public override IReadOnlyCollection<string> Keywords => CLexer.Keywords(Dialect);

        public override string ApiName(string pythonName)
        {
            if (pythonName == null || GameApi.Find(pythonName) == null) return null;
            return ApiNameOf(pythonName, Dialect);
        }

        public override string FromApiName(string name)
        {
            if (name == null) return null;
            foreach (var b in GameApi.Builtins) if (ApiNameOf(b.PythonName, Dialect) == name) return b.PythonName;
            if (Dialect == Dialect.Cpp)
            {
                if (name == "std::print" || name == "std::cout" || name == "println") return "print";
                if (name.StartsWith("farm::")) return FromApiName(name.Substring(6));
                if (name == "min" || name == "max" || name == "abs" || name == "random") return name;
            }
            else
            {
                if (name == "puts") return "print";
                if (name == "fmin") return "min";
                if (name == "fmax") return "max";
                if (name == "fabs") return "abs";
            }
            return GameApi.Find(name) != null && name != "print" ? name : null;
        }

        internal static string ApiNameOf(string py, Dialect d)
        {
            if (d == Dialect.C) return py == "print" ? "printf" : py;
            switch (py)
            {
                case "print": return "std::println";
                case "min": return "std::min";
                case "max": return "std::max";
                case "abs": return "std::abs";
                case "random": return "farm::random";
            }
            return py;
        }

        protected override ValueFormat CreateValueFormat()
        {
            var d = Dialect;
            Func<string, string> fn = n =>
            {
                if (string.IsNullOrEmpty(n)) return n;
                if (n.StartsWith("__j", StringComparison.Ordinal)) return "lambda";
                return GameApi.Find(n) != null ? ApiNameOf(n, d) : n;
            };
            if (d == Dialect.C)
                return new ValueFormat
                {
                    NullText = "NULL", TrueText = "true", FalseText = "false",
                    ListOpen = "{", ListClose = "}", SetOpen = "{", SetClose = "}", MapOpen = "{", MapClose = "}",
                    TupleOpen = "{", TupleClose = "}", KeyValueSep = ": ", QuoteNestedStrings = true, Quote = '"',
                    PositionFormat = "{{{0}, {1}}}", CompanionFormat = "{{{0}, {1}}}", DroneFormat = "Drone {0}",
                    FunctionName = fn,
                    ConstantText = (c, n) => c == null || c == "Directions" ? n : c + "." + n,
                };
            return new ValueFormat
            {
                NullText = "nullptr", TrueText = "true", FalseText = "false",
                ListOpen = "[", ListClose = "]", SetOpen = "{", SetClose = "}", MapOpen = "{", MapClose = "}",
                TupleOpen = "(", TupleClose = ")", KeyValueSep = ": ", QuoteNestedStrings = true, Quote = '"',
                PositionFormat = "({0}, {1})", CompanionFormat = "({0}, {1})", DroneFormat = "Drone {0}",
                FunctionName = fn,
                ConstantText = (c, n) => c == null || c == "Directions" ? n : c + "::" + n,
            };
        }

        public override string ApiStubFile(out string fileName)
        {
            fileName = Dialect == Dialect.C ? "farm.h" : "farm.hpp";
            try { return Dialect == Dialect.C ? FarmStub.CHeader(null) : FarmStub.CppHeader(null); }
            catch (Exception ex) { return "// JavaFarmer could not write the stub: " + ex.Message + "\n"; }
        }

        /// <summary>The stub file with the constant members the given sources use added to the known ones (tests).</summary>
        public string ApiStubFor(IEnumerable<string> sources) => Dialect == Dialect.C ? FarmStub.CHeader(sources) : FarmStub.CppHeader(sources);
    }
}
