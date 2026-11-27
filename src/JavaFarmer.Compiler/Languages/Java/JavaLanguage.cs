using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Java as a JavaFarmer language: the v1 frontend (JavaCompiler / JavaSyntax / GameApi.ToJava) behind ILanguage,
    /// plus the IR → Java printer (JavaPrinter). Discovered by the Languages registry.
    /// </summary>
    public sealed class JavaLanguage : LanguageBase, IPrinterDiagnostics, ISignatureProvider
    {
        /// <summary>Text of a new, empty code window (v1 plugin EditorSupport.NewFileTemplate).</summary>
        public const string Template = "void main() {\n\t\n}";
        /// <summary>Name of the API reference file written next to the saves (v1 plugin ApiStubWriter.FileName).</summary>
        public const string StubFileName = "JavaFarmerApi.java";

        public override string Id => "java";
        public override string DisplayName => "Java";
        public override string FileExtension => ".java";
        public override bool IsNative => false;
        protected override NamingStyle ApiNaming => NamingStyle.Camel;

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

        /// <summary>The v1 Java compiler (owned by the Java frontend); never throws.</summary>
        public override CompileResult Compile(string source, CompileOptions options)
        {
            try { return JavaCompiler.Compile(source ?? "", options ?? new CompileOptions()); }
            catch (Exception ex) { return CompileResults.Fail(source ?? "", new List<Diagnostic> { CompileResults.Internal(ex, source ?? "") }); }
        }

        public override IrModule ToIr(string source, CompileOptions options, out IReadOnlyList<Diagnostic> errors)
        {
            try
            {
                var m = JavaCompiler.ToIr(source ?? "", options ?? new CompileOptions(), out errors);
                if (errors == null) errors = new List<Diagnostic>();
                if (m != null && m.SourceLanguage == null) m.SourceLanguage = Id;
                return errors.Count > 0 ? null : m;
            }
            catch (Exception ex)
            {
                errors = new List<Diagnostic> { CompileResults.Internal(ex, source ?? "") };
                return null;
            }
        }

        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors)
        {
            var m = JavaCompiler.ToIr(source, options, out var errs);
            if (errs != null) errors.AddRange(errs);
            return m;
        }

        protected override string PrintImpl(IrModule module, PrintOptions options) => JavaPrinter.Print(module, options);

        /// <summary>Print plus the constructs Java cannot express (DocsTranslator keeps such blocks unchanged).</summary>
        public string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            try { return JavaPrinter.Print(module, options ?? new PrintOptions(), out problems); }
            catch (Exception ex) { problems = new[] { "internal printer error: " + ex.Message }; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        /// <summary>
        /// Cross-language imports: the file's static fields and methods (declarations only, bodies are not checked, so
        /// files that import each other still see each other).
        /// </summary>
        public ModuleSignature Signatures(string source, CompileOptions options)
        {
            string name = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            try
            {
                var cu = Parser.Parse(source ?? "", out var err);
                if (cu == null || err != null)
                    return ModuleSignature.Failed(name, Id, name + " (Java) does not compile" + (err != null ? " (" + err.Message + ")" : ""));
                var chk = new Checker(cu, name, null);
                chk.DeclareOnly();
                var own = chk.Own;
                var sig = new ModuleSignature { Module = name, LanguageId = Id, LanguageName = DisplayName, Ok = true };
                foreach (var m in own.Methods.Values)
                {
                    var f = new FunctionSignature { Name = m.Name, PyName = m.PyName, ReturnType = JavaLowering.Ir(m.ReturnType), IsPrivate = m.IsPrivate, IsEntry = m.Name == "main" };
                    for (int i = 0; i < m.ParamTypes.Count; i++)
                        f.Params.Add(new ParamSignature { Name = i < m.ParamNames.Count ? m.ParamNames[i] : "arg" + i, Type = JavaLowering.Ir(m.ParamTypes[i]) });
                    sig.Functions.Add(f);
                }
                foreach (var fd in own.Fields.Values)
                    sig.Globals.Add(new GlobalSignature { Name = fd.Name, PyName = fd.PyName, Type = JavaLowering.Ir(fd.Type), IsConst = fd.IsFinal, IsPrivate = fd.IsPrivate });
                return sig;
            }
            catch (CompileAbort a)
            {
                return ModuleSignature.Failed(name, Id, name + " (Java) does not compile (" + a.Diag?.Message + ")");
            }
            catch (Exception ex)
            {
                return ModuleSignature.Failed(name, Id, "internal JavaFarmer error while reading " + name + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ editor services

        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            try { return JavaSyntax.Highlight(source ?? ""); }
            catch { return string.IsNullOrEmpty(source) ? new List<(int, int, TokenKind)>() : new List<(int, int, TokenKind)> { (0, source.Length, TokenKind.Unknown) }; }
        }

        public override IReadOnlyCollection<string> Keywords => JavaSyntax.Keywords;

        /// <summary>"void main() {\n\t\n}" with the caret after the tab (v1 plugin behaviour).</summary>
        public override string NewFileTemplate(string fileName, out int caret)
        {
            caret = Template.IndexOf('\t') + 1;
            return Template;
        }

        /// <summary>v1 names: can_harvest → canHarvest, print → System.out.println, min → Math.min, str → String.valueOf.</summary>
        public override string ApiName(string pythonName)
        {
            try { return GameApi.ToJava(pythonName); } catch { return null; }
        }

        /// <summary>Inverse of ApiName (also accepts the short forms println / min ...).</summary>
        public override string FromApiName(string name)
        {
            try { return GameApi.ToPython(name); } catch { return null; }
        }

        /// <summary>The v1 plugin ValueFormatter on the neutral GameValues model (ValueFormat defaults are the Java style).</summary>
        protected override ValueFormat CreateValueFormat() => new ValueFormat
        {
            // lambdas and other JavaFarmer helpers show as "lambda"; builtins by their Java name; user functions as they are
            FunctionName = n =>
            {
                if (string.IsNullOrEmpty(n)) return n;
                if (n.StartsWith("__j", StringComparison.Ordinal)) return "lambda";
                string j = null;
                try { j = GameApi.ToJava(n); } catch { }
                return j ?? n;
            },
        };

        /// <summary>JavaFarmerApi.java: the API reference the v1 plugin writes for external editors.</summary>
        public override string ApiStubFile(out string fileName)
        {
            fileName = StubFileName;
            try { return BuildApiStub(GameApi.Functions); }
            catch { return BuildApiStub(null); }
        }

        /// <summary>Port of the v1 plugin ApiStubWriter.Build (same text).</summary>
        public static string BuildApiStub(IEnumerable<ApiFunction> functions)
        {
            var sb = new StringBuilder();
            sb.Append("// JavaFarmer game API reference, written by the JavaFarmer mod.\n");
            sb.Append("// The game never runs this file. It lists the functions your Java code can call.\n");
            sb.Append("// All functions are available without a prefix, as if statically imported.\n");
            sb.Append("// Game constants keep the game's names: North, East, South, West, Entities.Carrot, Items.Hay, ...\n");
            sb.Append("// \"A|B x\" means the parameter accepts an A or a B.\n\n");
            sb.Append("interface JavaFarmerApi {\n");
            if (functions != null)
            {
                foreach (var f in functions)
                {
                    if (f == null) continue;
                    string sig = string.IsNullOrEmpty(f.Signature) ? (f.ReturnType ?? "Object") + " " + f.JavaName + "()" : f.Signature;
                    bool qualified = f.JavaName != null && f.JavaName.IndexOf('.') >= 0;
                    sb.Append(qualified ? "    // " : "    ").Append(sig.TrimEnd(';')).Append(";");
                    if (!string.IsNullOrEmpty(f.PythonName)) sb.Append("   // game: ").Append(f.PythonName);
                    sb.Append('\n');
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
