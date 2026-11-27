using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    public static partial class JavaCompiler
    {
        private static CompileResult CompileImpl(string javaSource, CompileOptions options)
        {
            javaSource = javaSource ?? "";
            try
            {
                var fileName = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
                var cu = Parser.Parse(javaSource, out var syntaxError);
                if (syntaxError != null) return Fail(javaSource, new List<Diagnostic> { syntaxError });

                var checker = new Checker(cu, fileName, options?.OtherFiles, options);
                checker.Run();
                if (checker.Errors.Count > 0)
                    return Fail(javaSource, checker.Errors.OrderBy(d => d.Start).ToList());

                var ir = JavaLowering.Lower(checker, cu, fileName, javaSource, comments: false);
                return IrEmitter.Compile(ir, javaSource, options);
            }
            catch (CompileAbort a)
            {
                return Fail(javaSource, new List<Diagnostic> { a.Diag });
            }
            catch (Exception ex)
            {
                return Fail(javaSource, new List<Diagnostic> { new Diagnostic("internal JavaFarmer compiler error: " + ex.GetType().Name + ": " + ex.Message, 0, Math.Min(1, javaSource.Length)) });
            }
        }

        /// <summary>Java source → Farm IR (parse + type check + lowering). Null plus errors when it does not compile. Never throws.</summary>
        public static IrModule ToIr(string javaSource, CompileOptions options, out IReadOnlyList<Diagnostic> errors) => ToIrImpl(javaSource, options, out errors);

        static IrModule ToIrImpl(string javaSource, CompileOptions options, out IReadOnlyList<Diagnostic> errors)
        {
            javaSource = javaSource ?? "";
            try
            {
                var fileName = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
                var cu = Parser.Parse(javaSource, out var syntaxError);
                if (syntaxError != null) { errors = Fail(javaSource, new List<Diagnostic> { syntaxError }).Errors; return null; }
                var checker = new Checker(cu, fileName, options?.OtherFiles, options);
                checker.Run();
                if (checker.Errors.Count > 0) { errors = Fail(javaSource, checker.Errors.OrderBy(d => d.Start).ToList()).Errors; return null; }
                var ir = JavaLowering.Lower(checker, cu, fileName, javaSource, comments: true);
                IrComments.Tidy(ir);
                errors = new List<Diagnostic>();
                return ir;
            }
            catch (CompileAbort a)
            {
                errors = Fail(javaSource, new List<Diagnostic> { a.Diag }).Errors;
                return null;
            }
            catch (Exception ex)
            {
                errors = new List<Diagnostic> { CompileResults.Internal(ex, javaSource) };
                return null;
            }
        }


        static CompileResult Fail(string src, List<Diagnostic> errors)
        {
            foreach (var d in errors)
            {
                // keep spans inside the text and non-empty where possible
                d.Start = Math.Max(0, Math.Min(d.Start, src.Length));
                d.End = Math.Max(d.Start, Math.Min(d.End, src.Length));
                if (d.End == d.Start && d.Start > 0 && d.Start == src.Length) d.Start--;
            }
            return new CompileResult { Success = false, Python = null, Map = null, Errors = errors };
        }
    }
}
