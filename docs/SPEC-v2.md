# JavaFarmer v2 — many languages + Mods menu (design spec)

v1 (docs/SPEC.md) made the game playable in Java. v2 lets the player pick the programming language
(**C, C++, Rust, JavaScript, Java, Python, Zig, C#, R**) and adds a **Mods** menu with optional content
extensions (**Cybersecurity**, **AI/ML**) that are OFF until the player turns them on.
Everything in SPEC.md still holds unless this file overrides it. Read SPEC.md first.

## 1. Big picture

```
 source text (any language)                                       game docs (Python snippets)
      │ frontend: lexer → parser → checker (static langs) → lower           │ Python frontend
      ▼                                                                      ▼
                       Farm IR  (typed where known, spans + comments)
      │ shared IR→Python emitter (+SourceMap, segments, helpers)     │ per-language printer (IR → source text)
      ▼                                                              ▼
 game Python → game Tokenizer/Parser (unchanged plugin bridge)     docs/content generation, code conversion
                                                                   when the player switches language
```
- **Python** is the game's native language: in Python mode the plugin does NOT compile anything (vanilla
  parse path), but the Python *frontend* exists (game-dialect parser → IR) for docs translation and for
  converting a save's code from Python to another language.
- Every language = one **frontend** (source → IR, with real-language diagnostics) + one **printer**
  (IR → idiomatic source in that language) + editor services (highlighting, keywords, indentation rules,
  API naming, value formatting, new-file template, external-editor stub file).
- The IR→Python emitter, helpers, source map/segment/dependency logic, range-for detection, ternary hoisting,
  lambda hoisting, `global` insertion, reserved-name renaming, integer-division strategy live ONCE in Core.

## 2. Compiler layout (src/JavaFarmer.Compiler, still netstandard2.1, still no game refs)

```
Core/        Ir.cs (IR nodes), IrTypes, IrEmitter (IR → Python + SourceMap), Helpers, SourceMap, Diagnostics,
             GameApi (canonical API table + per-language naming), ILanguage, Languages (registry),
             IrTypeInference (fills types for untyped sources), PrinterBase (indentation, comments, precedence)
Languages/Java/  Languages/Python/  Languages/C/  Languages/Cpp/  Languages/Rust/
Languages/JavaScript/  Languages/Zig/  Languages/CSharp/  Languages/R/
```
Public API (contract with the plugin; keep the v1 types/semantics — CompileResult, SourceMap, Segment incl.
NoHighlight/DependencyOverride, Names, Diagnostic, TokenKind):
```csharp
public interface ILanguage {
  string Id { get; }            // "java","python","c","cpp","rust","js","zig","csharp","r"
  string DisplayName { get; }   // "Java","Python","C","C++","Rust","JavaScript","Zig","C#","R"
  string FileExtension { get; } // ".java" … (external editor files only; saves keep .py)
  bool IsNative { get; }        // true only for Python (plugin bypasses compile)
  CompileResult Compile(string source, CompileOptions options);           // never throws
  IrModule ToIr(string source, CompileOptions options, out IReadOnlyList<Diagnostic> errors); // for conversion
  string Print(IrModule module, PrintOptions options);                     // IR → source; must re-compile with Compile()
  IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source); // never throws, covers all text
  IReadOnlyCollection<string> Keywords { get; }
  EditorRules Editor { get; }   // block open/close tokens for auto-indent (e.g. "{" "}" ; Python ":"), line comment prefix
  string NewFileTemplate(string fileName, out int caret);
  string ApiName(string pythonName);      // "can_harvest" → "canHarvest" / "CanHarvest" / "can_harvest"; null if unknown
  string FromApiName(string name);        // inverse; null if unknown
  string FormatValue(object gameValue);   // how println/hover shows values: true/True/TRUE, null/None/NULL/nil/nullptr, lists, maps, tuples
  string ApiStubFile(out string fileName); // farm.h / Farm.java / farm.rs / … for external editors
}
public static class Languages { IReadOnlyList<ILanguage> All; ILanguage Get(string id); }
```
`JavaCompiler.Compile`/`JavaSyntax`/`GameApi.ToJava` stay as thin wrappers until the plugin is migrated.

## 3. Farm IR (Core/Ir.cs) — requirements
- Module: imports (other files), globals (name, type, init), functions (name, params+types, return type, body),
  entry point (`main`-like) or top-level statements (Python/JS/R scripts), leading/trailing comments.
- Statements: VarDecl, Assign (incl. compound, index/field targets), ExprStmt, If/ElseIf/Else, While, DoWhile,
  Loop (infinite), ClassicFor(init;cond;update), RangeFor(var,start,end,step,inclusive), ForEach(var, iterable[, keyed]),
  Switch/Match (constant cases, default), Break, Continue, Return, Block. Every node: source span, comments, and
  emitter hints (segment kind / dependency override) are DERIVED by the emitter, not set by frontends, except where a
  frontend must mark synthetic scaffolding (e.g. Rust `fn main() {` header, C `int main(void)`, `#include`).
- Expressions: literals (int, float, bool, string, null), Name, Binary with explicit numeric semantics
  (Add, Sub, Mul, TrueDiv, IntDivTrunc, IntDivFloor, ModTrunc, ModFloor, Pow, comparisons, And/Or, string concat),
  Unary, Ternary/IfExpr, Call (user fn, other-module fn, game API by canonical Python name, collection op by
  canonical op, function value), Index (with base offset for 1-based R), Field (other module's global),
  ListLit/MapLit/SetLit/TupleLit, Lambda/Closure (no params; body = statements or expr), Cast (int/float/str),
  game constants (`Entities.Carrot`, `North` …, carried by category+name, never hard-coded lists).
- Collection ops are canonical (`list.append`, `list.insert`, `list.pop`, `list.remove_value`, `len`, `contains`,
  `map.get_or_null`, `map.get_or_default`, `map.keys`, `map.values`, `set.add` …) so every printer can render them.
- Types: Void, Int, Float, Bool, String, List<T>, Map<K,V>, Set<T>, Tuple, game types (Entity, Ground, Item, Hat,
  Unlock, Leaderboard, Direction, Drone, Position, Companion), Func(ret), Any/Unknown. Static frontends fill them;
  dynamic ones (Python, JS, R) leave Unknown and IrTypeInference infers what it can (printers of static languages need
  it: unknown → the language's escape hatch, e.g. `var`/`auto`/`let`/`Object`/`anytype`-free fallback, with a
  `// TODO type` comment only as last resort).
- The IR must be enough to print idiomatic code in every language AND to emit the same game Python as v1 for Java
  (v1 Java snapshot/oracle/differential tests must keep passing unchanged, unless a change is a deliberate fix).

## 4. Per-language rules (frontend owners refine these in docs/languages/<id>.md)
Shared: write what a beginner writes in that language **and it must be valid in the real language**: every valid
corpus program must compile with the real toolchain against our API stub file (see §6). When something real is not
supported: clear error "X is not supported in JavaFarmer". Game constants keep game names inside a language-natural
container. Error messages imitate the real compiler's wording.

| lang | entry | API naming | constants | print | collections | notes |
|---|---|---|---|---|---|---|
| Java | as v1 | camelCase | `Entities.Carrot`, `North` | System.out.println | ArrayList/HashMap/HashSet | unchanged |
| Python | top-level (game) | snake_case | game | print | list/dict/set | native |
| C | `int main(void)` (also `void`-less `int main()`) | snake_case | `Entities.Carrot` (global const struct) , `North` | printf (format specifiers lowered), puts | fixed arrays `int a[n]`, plus the stub-provided `List`/`Map` API if needed (owner decides, keep real-C valid) | `#include "farm.h"` / std headers accepted |
| C++ | `int main()` | snake_case | `Entities::Carrot`, `Direction::North` (+ plain `North`) | std::cout <<, std::println | std::vector/map/unordered_map/set | lambdas `[&]{}` for spawn_drone, range-for, auto |
| Rust | `fn main()` | snake_case | `Entity::Carrot`, `Direction::North` | println!/print! (format strings) | Vec/HashMap/HashSet | let/let mut enforced, Option for nullable returns, closures for spawn_drone, no borrow checker (document) |
| JS | top-level statements or `function main()` called at top level | camelCase | `Entities.Carrot`, `North` | console.log | Array/Map/Set (+ plain objects as maps) | numbers are doubles like the game |
| Zig | `pub fn main() void` | camelCase via `const farm = @import("farm");` → `farm.harvest()` | enum literals `.North`, `farm.Entity.Carrot` | std.debug.print | arrays/slices, std.ArrayList / std.AutoHashMap per current Zig | target latest stable Zig |
| C# | top-level statements or `static void Main()` | PascalCase | `Entities.Carrot`, `Direction.North` (+ `North` via using static) | Console.WriteLine | List/Dictionary/HashSet | close to Java |
| R | top-level script | snake_case | `Entities$Carrot`, `North` | print/cat | vectors `c()`, lists, named lists/environments as maps | 1-based indexing lowered, `<-`, `for (i in 1:n)`, `repeat` |

Unlock costs must stay fair across languages: mandatory structure (entry function headers, includes, `fn main`)
is synthetic (free); each construct costs the unlock of its Python twin (SPEC §7).

## 5. Plugin v2 (src/JavaFarmer.Plugin)
- **Every code file has its own language, chosen by its extension** (`main.java`, `utils.rs`, `bot.py`; see §8).
  The BepInEx config `Language` (default `java`), changeable in the **Mods** menu (main menu + pause menu), is only the
  DEFAULT language: the language of new files and of the docs / string tooltips / unlock boxes. Changing it never
  touches existing files. User code is never converted automatically. `Saves/SaveN/javafarmer.json` holds
  { "version": 2, "language": <default when saved>, "extensions": {...progress...} }.
- Per-window services (compile hook — `.py` = the game's vanilla parser —, highlighting, auto-indent, completion,
  hover tooltips, runtime error names, value formatting while a file runs, new-file template) come from that window's
  ILanguage; docs, strings and unlock boxes from the default language. Content lives in `content/<lang>/docs/...`, `content/<lang>/Strings/*.txt`,
  `content/<lang>/unlock_display_code.txt` (Java content moves from content/docs → content/java/docs etc.).
  Python mode uses the game's own docs + only extension pages.
- The **Mods** menu: a panel with (1) Programming language selector, (2) Extensions list with toggles
  (Cybersecurity, AI/ML) + one-line description each. Extension state is global (config); extension *progress*
  is per save (separate file, so turning an extension off never breaks a save and turning it on resumes).
- Extensions (src/JavaFarmer.Plugin/Extensions/) add: tech-tree unlocks, new builtins (canonical snake_case names,
  registered with GameApi so every language sees them under its naming), docs chapters (per language, generated
  from Python snippets by the printers), and new mechanics. Exact mechanics: see docs/EXTENSIONS.md.

## 8. Per-file languages (user direction, supersedes the conversion described in an earlier version of §5)
"You don't have to convert existing user code ... just let the user decide what file extension they choose."
- **Window names**: a code window keeps its module name inside the game (`CodeWindow.fileName` = "utils", what other
  files import); the plugin remembers its extension (FileLanguages) and the title shows "utils.rs". Renaming to
  "utils.rs" changes the language; a name typed without an extension keeps the window's extension; new windows
  (f0, f1 ...) get the default language's extension. Module names are unique across extensions ("a.rs" is refused
  while "a.java" exists, with a message). Extensions: ILanguage.FileExtension plus aliases `.cc .cxx` (C++),
  `.mjs` (JS), `.r` (R); case-insensitive. `.h/.hpp` are not code files (stub names like farm.h must stay free).
- **Disk**: files are saved as `<module><ext>`. Saver.WriteCodeFiles is replaced; the `.py` checks inside Saver.Load and
  the cleanup lambda are redirected by a transpiler to FileLanguages.IsCodeFile; Saver.OnFileChanged (external editors)
  is replaced. UpdateFromOldSaveGame keeps `.py` (pre-1.0 saves are Python). Stub files (every language's
  ApiStubFile name), __builtins__.py and the toolchain build files `build.zig` / `build.rs` (FileLanguages.ToolFileNames;
  a window cannot be renamed to them) are never code windows. Two files with the same module name: the newest is
  kept, the other moved to `javafarmer-backup/duplicates-<time>/`.
- **Old saves** (no javafarmer.json, or version 1): a JavaFarmer v1 save (a file compiles as Java but not as Python, or
  a version-1 sidecar says java) gets its `*.py` files renamed to `*.java` once (contents unchanged, originals copied to
  `javafarmer-backup/rename-<time>/`; a file that only parses as Python stays `.py`). A vanilla save stays Python.
- **Cross-language imports**: CompileOptions.OtherFileLanguages + Core/ForeignModules (docs/LANGUAGE-GUIDE.md §9).
- **External editors**: real extensions on disk; an API stub for every language that has a file in the save.

## 6. Testing (each language owner)
1. Corpus `tests/corpus/<lang>/{valid,invalid}` with expected diagnostics.
2. Oracle: every valid program's Python parses with the real game parser (headless).
3. **Real toolchain conformance**: every valid program compiles with the real compiler against the API stub
   (gcc -std=c23 -fsyntax-only, g++ -std=c++23 -fsyntax-only, rustc --edition 2024, node --check, zig ast-check/build,
   dotnet (Roslyn) build, Rscript parse (docker r-base if R is missing), javac). Skip with a visible message if missing.
4. Differential execution for pure-computation programs: real run vs CPython run of the emitted Python (with the
   number normalisation used in v1), stdout must match.
5. Round trip: printer(IR(program)) compiles again and emits equivalent Python.
6. Docs: every Python code block of the game's EN docs → Python frontend → this language's printer → this
   language's Compile succeeds (track exceptions explicitly in a list with reasons).
Builds by parallel agents must not collide: use `dotnet build/test --artifacts-path <your own dir>`.

## 7. Decisions made during the core refactor (v2 core agent)
These refine or change the sections above; docs/IR.md and docs/LANGUAGE-GUIDE.md are the detailed contracts.
- **Registry**: Languages discovers ILanguage implementations by reflection (public, non-abstract, parameterless
  ctor) and orders them by Languages.Order (§2 order). Adding a language never edits a shared registry file.
  Languages.Register(lang) exists for tests/plugins.
- **IR naming**: frontends keep source names; the emitter derives Python names (v1 rules: reserved → "_j", global
  clashing with a function → "_f", local clashing with a module-level name → "_l"; new: shadowed locals in nested
  blocks → "_2"...). Other modules' symbols carry IrSymbol.PyName (computed with PyNaming.ModuleLevel).
- **Collection ops** are IrOp enum members (docs/IR.md §8) rather than dotted strings; Python-strict and
  Java-lenient variants are separate ops (RemoveValue / RemoveValueIfPresent, MapRemove / MapRemoveIfPresent,
  MapGetOrNull vs IrIndex). ListSet / MapPut exist next to IrAssign-to-IrIndex so Java keeps v1's analyses.
  Java gained Math.floorDiv / Math.floorMod (IntDivFloor / ModFloor) so printers can express Python's // and %.
- **Pow** is always emitted through the __jpow helper (v1 Math.pow behaviour) whatever the source syntax.
- **FormatValue** takes the neutral runtime model of Core/GameValues.cs (null, bool, double, string, GameList,
  GameSet, GameDict, GameTuple, GameDrone, GameFunction, GameModule, GameConstant); the plugin converts its Py*
  objects before calling it. ValueFormat holds the per-language knobs; JavaLanguage reproduces v1 ValueFormatter.
- **GameApi.Register(pythonName, signature, returnType, doc, extension)**: signature = "name: type, opt?: type,
  *rest: type" in neutral types (int, float, bool, string, void, any, list<T>, map<K,V>, set<T>, func, func<T>,
  game types, T?, A|B). GameApi.Version / GameApi.Changed signal changes; Java sees registered builtins
  (camelCase, overloads for optional params) immediately.
- **Python Compile** is the identity: Python = source, one User segment covering the whole text, no Names.
- **Testing** (§6): the shared harness lives in tests/JavaFarmer.Languages.Tests (Harness/, Hooks/<Lang>Hooks.cs,
  Core/), separate from the v1 suites. Corpus layout tests/corpus/<lang>/{valid,diff,invalid} (diff = differential
  programs, also counted as valid; a sub-directory of valid = a multi-file program); docs exceptions in
  tests/corpus/<lang>/docs-exceptions.txt keyed "<md path>#<span index>" where every single-backtick span of a page
  is numbered from 0 (a block = a span containing a newline or standing alone on its line). The v1 Java corpus moved
  to tests/corpus/java (tests/JavaFarmer.Compiler.Tests links it, unchanged tests).
- **Java conformance deviation**: JavaFarmer accepts a compact source file without main as a library used as
  File.member (v1). javac rejects that, so the conformance hook checks such files as "public class File" with
  static members. Everything else is compiled by javac exactly as written (plus stub imports).
