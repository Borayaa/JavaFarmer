# Adding a language to JavaFarmer (v2)

Checklist for language agents (C, C++, Rust, JavaScript, Zig, C#, R). Read docs/SPEC.md, docs/SPEC-v2.md and
docs/IR.md first. Java (Languages/Java) is the reference implementation of a static language, Python
(Languages/Python) of a dynamic one.

## 0. What "done" means

1. tests/corpus/<id>/valid, diff, invalid exist (≥ 15 valid incl. at least one multi-file program and the farming
   scenarios of the v1 Java corpus re-written idiomatically; ≥ 30 invalid covering the typical beginner mistakes of the
   language and every "X is not supported in JavaFarmer" error; ≥ 6 diff programs: integer math, loops, switch/match,
   closures, collections, strings).
2. The shared harness passes for your id: dotnet test tests/JavaFarmer.Languages.Tests --artifacts-path <yours>
   --filter "FullyQualifiedName~HarnessTests" (a)–(f) + Language_ContractBasics, with the real toolchain installed
   (official release, checksum verified, in your scratch dir; never skip silently: a skip prints why).
3. tests/corpus/<id>/docs-exceptions.txt lists every docs block that cannot be expressed, each with a reason.
4. Your own unit tests (lexer/parser/checker/printer specifics) in tests/JavaFarmer.Languages.Tests/<Lang>/.
5. docs/languages/<id>.md: the supported subset, deviations from the real language, the API naming, the unlock
   mapping (which construct costs which unlock), the stub file.
6. All existing suites still pass (tests/JavaFarmer.Compiler.Tests, tests/JavaFarmer.Plugin.Tests, the harness for
   the other languages). Compile < 5 ms for a 300-line file (see Harness/PerformanceTests.cs; copy the pattern).

## 1. Files to create

| file | content |
|---|---|
| src/JavaFarmer.Compiler/Languages/<Lang>/<Lang>Language.cs | public sealed class XLanguage : LanguageBase (parameterless ctor). No registry edit: Languages discovers it by reflection and orders it by Languages.Order. |
| .../<Lang>/Lexer.cs, Parser.cs, Ast.cs | your frontend (keep the AST private: internal classes) |
| .../<Lang>/Checker.cs (static languages) | name resolution, types, real-compiler wording for errors |
| .../<Lang>/Lowering.cs | checked AST → IrModule (docs/IR.md) |
| .../<Lang>/Printer.cs | IrModule → idiomatic source (subclass PrinterBase) |
| .../<Lang>/Highlighter.cs | Highlight(): tokens covering the whole text, never throws |
| tests/JavaFarmer.Languages.Tests/Hooks/<Lang>Hooks.cs | LanguageHooks subclass (§4) |
| tests/JavaFarmer.Languages.Tests/<Lang>/*.cs | language-specific unit tests |
| tests/corpus/<id>/... | corpus (§3) |
| docs/languages/<id>.md | language notes |

Namespace: JavaFarmer.Compiler for compiler code (netstandard2.1: no newer APIs, no NuGet packages);
JavaFarmer.Languages.Tests for tests (inside it write Compiler.Languages.Get(...): "Languages" alone resolves to
the namespace). Shared files (JavaFarmer.sln, *.csproj, Core/*) only get minimal additive edits; say so in your report.

## 2. The ILanguage members (Core/ILanguage.cs, defaults in Core/LanguageBase.cs)

| member | rule |
|---|---|
| Id / DisplayName / FileExtension | "c"/"C"/".c", "cpp"/"C++"/".cpp", "rust"/"Rust"/".rs", "js"/"JavaScript"/".js", "zig"/"Zig"/".zig", "csharp"/"C#"/".cs", "r"/"R"/".R" |
| IsNative | false (only Python is native) |
| Compile | LanguageBase does ToIr + IrEmitter.Compile; override only to add caching. Never throws. Diagnostics: source offsets, End exclusive, first error is shown in game. |
| ToIr | protected Frontend(source, options, errors): parse, check, lower; add Diagnostics to errors. Must work without OtherFiles. options.OtherFiles = name → source of the other code windows (parse them for declarations only; cache like Java's DeclCache). |
| Print | protected PrintImpl(module, options). Must accept IR from ANY frontend (Python docs, Java saves): typed (static sources) or inferred (IrTypeInference already ran for dynamic sources). Snippet mode prints statements bare (no main wrapper). Output must compile with your Compile. |
| Highlight / Keywords / Editor | TokenKind per token (Core/Api.cs); Editor = EditorRules (indent after "{", dedent on "}", comment markers, IndentUnit "\t"). |
| NewFileTemplate | the smallest valid program with the caret inside the entry body (C: "#include \"farm.h\"\n\nint main(void) {\n\t\n}" ...). |
| ApiName / FromApiName | LanguageBase maps via NamingStyle (ApiNaming: Snake for C/C++/Rust/R, Camel for JS/Java/Zig, Pascal for C#). Must cover registered extension builtins (look them up in GameApi on each call or cache per GameApi.Version). |
| FormatValue | override CreateValueFormat() to configure ValueFormat (Core/GameValues.cs): null/true/false spelling, list/set/map brackets, KeyValueSep, Position/Companion/Drone formats, constant spelling (Rust Entity::Carrot, R Entities$Carrot ...). |
| ApiStubFile | the file external editors include (farm.h, farm.hpp, farm.rs, farm.js / farm.d.ts, farm.zig, Farm.cs, farm.R): every GameApi builtin (incl. registered ones) with your naming and types, the constant containers, Position/Companion. It must make every valid corpus program compile with the real toolchain (§4c). |

## 3. Corpus format

tests/corpus/<id>/valid/*.<ext> (a sub-directory = one multi-file program; files see each other as OtherFiles,
module name = file name without extension), diff/*.<ext> (pure computation, stdout via the language's print),
invalid/*.<ext> with marker comments in your line-comment syntax:

    // ERROR: incompatible types: double cannot be converted to int
    // AT: 1.5

ERROR is a substring of the first diagnostic's message, AT (optional) the exact source text of its span.

## 4. Harness hooks (tests/JavaFarmer.Languages.Tests/Harness/LanguageHooks.cs)

    public sealed class CHooks : LanguageHooks
    {
        public override string Id => "c";
        public override Toolchain Conformance => new Toolchain
        {
            Exe = "gcc", SkipHint = "install gcc 14+",
            ExtraFiles = ctx => new Dictionary<string, string> { ["farm.h"] = Language.ApiStubFile(out _) },
            Args = ctx => "-std=c23 -fsyntax-only -Wall -Werror=implicit-function-declaration " + string.Join(" ", ctx.Sources.Keys.Select(ctx.Quote)),
        };
        public override Toolchain Runner => new Toolchain
        {
            Exe = "gcc", Args = ctx => "-std=c23 -o prog " + ctx.Quote(ctx.MainFile) + " -lm",
            ExtraFiles = ctx => new Dictionary<string, string> { ["farm.h"] = Language.ApiStubFile(out _) },
            Then = new Toolchain { Exe = "./prog", Args = _ => "" },   // "./x" = relative to the work dir
        };
        public override string PythonPrelude => Prelude.WithLiterals("(null)", "true", "false");
    }

* (a) A_Valid_CompilesWithValidMap / A_Invalid_FailsWithMessageAndSpan — Compile, SourceMap invariants, markers.
* (b) the same test runs the real game parser (GameOracle) on the emitted Python.
* (c) C_Valid_CompilesWithRealToolchain — Conformance: sources (after PrepareSource) + ExtraFiles written to a temp
  dir, the command must exit 0. Commands per SPEC-v2 §6: gcc -std=c23 -fsyntax-only, g++ -std=c++23 -fsyntax-only,
  rustc --edition 2024 (--emit=metadata or a check crate), node --check, zig ast-check / zig build-exe, dotnet build
  (generate a csproj in ExtraFiles), Rscript -e "parse(...)" with Fallback = docker run r-base. Download toolchains
  into your scratch dir (official releases, checksums) and point Exe at the absolute path if they are not on PATH.
* (d) D_Diff_RealRunPrintsTheSameAsGeneratedPython — Runner builds/runs the real program; CPython runs the emitted
  Python with PythonPrelude (print/str must format like your print; numbers are compared after "3.0" → "3").
  Write diff programs whose output is identical in both worlds (no float formatting differences, no overflow).
* (e) E_Valid_RoundTripsThroughThePrinter — Print(ToIr(src)) must compile and emit the same Python modulo
  identifier names and whitespace (Normalise.PythonShape).
* (f) F_DocsBlocks_TranslateAndCompile — every block of the game's EN docs: Python ToIr → your Print (DocsOptions,
  full program) → your Compile → game parser. Failures not listed in tests/corpus/<id>/docs-exceptions.txt fail the
  test ("<md path>#<span index>: reason", "--" comments; span ids from DocsCorpus.Spans). Stale entries are printed.
* Language_ContractBasics — ids, template caret, never throws on junk, Highlight covers every character,
  ApiName/FromApiName round trip, stub file present.

## 5. Frontend rules (SPEC-v2 §4)

* Accept what a beginner writes, and only valid code of the real language (the conformance test enforces it).
  Unsupported but valid → "X is not supported in JavaFarmer". Imitate the real compiler's error wording.
* Mandatory structure is free: the entry function is IsEntry (its header and the __name__ guard are scaffolding);
  includes/imports/use lines have no IR node; drop "return 0;" at the end of C/C++ main.
* Every construct must cost the unlock of its Python twin: emit the IR that the Python twin would produce (a C
  "for (int i = 0; i < n; i++)" is IrClassicFor — the emitter turns it into range() or a while with "for"
  override; a printf becomes print with Synthetic str()/Concat so it costs only "print").
* Game constants keep game names inside a language-natural container: build IrConst(category, member) for ANY
  member name (never validate against a list; the plugin knows the names for completion).
* Look up game builtins in GameApi at compile time (registered extension builtins must work without code changes):
  argument count/type checks from ApiBuiltin.Params, return type from ReturnType.
* Integer division / modulo: pick the IrBinOp that matches your language (docs/IR.md §7). Truncating int
  conversions: IrCast(Int) or TruncateToInt on compound assignments.
* Reject identifiers starting with "__j" (reserved for the emitter).
* Spans everywhere (docs/IR.md §2); comments via IrComments.Attach if your parser drops them.
* Keep Compile fast: no regex in hot paths, no LINQ-heavy rewrites per node, cache other files' declarations.

## 6. Printer rules

* Idiomatic code a beginner would write; deterministic; tab indentation by default (Options.Indent).
* Input can be untyped (Unknown types after inference): use the language's escape hatch (auto, let, var, dynamic
  typing) and only as a last resort a comment.
* Names: ApiName(pythonName) for builtins; keep user names, but rename identifiers that are keywords in your
  language consistently (e.g. "new" → "new_", "fn" → "fn_").
* Things your language cannot express (closures in C, tuples in Java, ...) print as the closest construct (C:
  hoist capture-free lambdas to static functions; a capturing closure → clear "not supported" comment) and the
  docs blocks that need them go into docs-exceptions.txt with the reason.
* Comments (Leading, Trailing, block TrailingComments, module TrailingComments) must survive.
* Snippet mode: no entry wrapper; declarations still printed (functions/globals).

## 7. Docs generation (later)

Implement IPrinterDiagnostics on your language (Core/DocsTranslator.cs): Print(module, options, out problems), where
problems lists the constructs you could not express; DocsTranslator then keeps those spans in Python and reports them.


DocsTranslator.Translate(markdown, language) (Core/DocsTranslator.cs) rewrites the code spans of a game docs page
(blocks through the Python frontend and your printer in Snippet mode, inline spans like `can_harvest()`,
`True`, `None` by name). content/<id>/docs/... is generated with it and hand-polished; the harness (f) keeps
the printer honest. Also provide content/<id>/Strings overrides and unlock_display_code.txt (unlock → snippet).

## 8. Unlock fairness cheat sheet

while/loop/repeat → Loops; if/else/switch/match/?: → Speed ("if"); for (all forms) → Expand 2 ("for"), range
loops also "range"; arithmetic/comparison/logic → Operators; variables → Variables; functions/return → Functions;
other files → Imports; lists/arrays/vectors → Lists; maps/dicts → Dictionaries; sets → Dictionaries ("sets");
print/println/printf/console.log → Debug ("print"); min/max/abs → Utilities; str conversion → Debug ("str").
These fall out of the IR automatically when the lowering mirrors the Python twin.

## 9. Cross-language imports (every language: please adopt)

Each code window has its own language (its file extension: main.java, utils.rs, bot.py). A file may call functions
and read globals of another file written in ANY language (\`Utils.f()\` in Java, \`utils::f()\` / \`utils.f()\` in yours,
as your language spells module access). The plugin passes:

* \`CompileOptions.OtherFiles\`: module name → source text of every other window (module name = window name without
  extension);
* \`CompileOptions.OtherFileLanguages\`: module name → language id ("java", "rust", "python" ...). A name without an
  entry is in your own language (old callers and tests keep working).

Rules for your frontend:

1. When you resolve another module, check \`ForeignModules.IsForeign(options, module, Id)\`. If it is foreign, do NOT
   parse its text with your parser: call \`ForeignModules.Resolve(module, options)\`. It reads the file with that
   language's own frontend and returns a \`ModuleSignature\` (Core/ForeignModules.cs): \`Functions\` (Name, PyName,
   Params with neutral IrTypes and Optional, ReturnType, IsEntry) and \`Globals\` (Name, PyName, Type, IsConst). Results
   are cached by language + name + text, and import cycles are detected.
2. If \`sig.Ok\` is false, report \`sig.Error\` at the use site ("cannot use utils: utils (Rust) does not compile (line 3:
   ...)"). Never crash, never fall back to parsing the foreign text as your language.
3. Build your external symbols from the signature: \`IrSymbol { Kind = ExternalFunction/ExternalGlobal, Module = module,
   PyName = f.PyName, Type = IrType.Func(f.ReturnType, params...), ParamTypes }\`. Always use PyName: it is the name the
   other file's compiled Python really defines (reserved names are renamed: \`list\` → \`list_j\`).
4. Types: dynamic languages (Python, JS, R) give Unknown/Any. Map them to your escape hatch (Java: \`Object\`, which the
   player casts). Do not enforce the other language's privacy (Rust \`fn\` without \`pub\`, JS without \`export\`): the game
   shares every module-level name.
5. Optional: implement \`ISignatureProvider\` on your ILanguage (declarations only, function bodies not checked, no need
   for the file's own imports). Without it ForeignModules uses your ToIr, so a file in your language that imports the
   caller back (a cycle) fails with a clear "imports this file back" message.
6. Dynamic frontends can call \`ForeignModules.AnnotateExternals(module, options)\` before IrTypeInference to give
   external calls their real types (the Python frontend does).

Adopted by every language: Java (Checker.GetOther → ForeignSymbols; ISignatureProvider), Python (AnnotateExternals; Python
needs no compile-time resolution: the game imports modules itself), C and C++ (CFamily/OtherFiles.cs), Rust (RustChecker),
Zig (ZigChecker), JavaScript (JsLowering; ISignatureProvider), C# (CsBinder) and R (RLowering). Tests: every language
calls a file of another language (tests/JavaFarmer.Plugin.Tests, PerFileLanguageTests.Java_CallsAFileOfEveryLanguage,
and each language's cross-language tests).
While a save loads, windows open one by one; the plugin compiles every non-Python window again once all are open
(GamePatches.ReparseCompiledWindows), so a window that imports a window opened after it does not keep a false
"cannot find module" error.