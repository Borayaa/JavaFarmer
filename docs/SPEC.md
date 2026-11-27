# JavaFarmer — design spec (v1)

A BepInEx mod for *The Farmer Was Replaced* (TFWR) that replaces the in-game Python with **Java**.
The player writes real Java in the code windows. The mod compiles it to the game's Python dialect and
feeds that to the game's own parser and interpreter. Gameplay, op/tick costs, drones, the unlock tree
and the debugger keep working because the game still runs its own AST.

Reference material (local only, gitignored, never commit):
- `.decompiled/Core.decompiled.cs`, `Utils.decompiled.cs`, `Assembly-CSharp.decompiled.cs` (ILSpy output of the game DLLs).
- Game install copy: `TheFarmerWasReplaced_Data/` (Managed DLLs in `TheFarmerWasReplaced_Data/Managed`,
  docs/strings in `TheFarmerWasReplaced_Data/StreamingAssets/Languages/EN`, API reference `Languages/builtins.py`).
- Game: Unity 6000.0.43f1, **Mono** scripting backend, x64 Windows build (Linux players run it under Proton).

## 1. Architecture

```
 Java text in CodeWindow
        │  (Harmony prefix on Parser.Parse(TokenStream, CodeWindow))
        ▼
 JavaFarmer.Compiler  ── lexer → parser → type checker → lowering → emitter
        │  CompileResult { Python text, SourceMap, Diagnostics }
        ▼
 Tokenizer.Tokenize(python) → original Parser.Parse(tokens, codeWindow)   (game code)
        │
        ▼
 Remap every Node.boxedParams.wordStart/wordEnd from Python offsets → Java offsets,
 register per-node dependency policy (synthetic / structural / override)
        │
        ▼
 Program returned to CodeWindow.Parse() exactly as the game expects
```

Java compile errors are thrown as the game's `ParseException(message, javaStart, javaEnd)`, so the
editor underlines them in place with the game's own error UI.

## 2. Repo layout & build

```
Directory.Build.props        GameDir (default: repo root, where the game copy lives), LangVersion latest
JavaFarmer.sln
src/JavaFarmer.Compiler/     netstandard2.1, NO references to game/Unity DLLs (pure, testable)
src/JavaFarmer.Plugin/       netstandard2.1, BepInEx 5 plugin + HarmonyX patches; refs Compiler + game DLLs
tests/JavaFarmer.Compiler.Tests/  net8.0 xunit: compiler unit tests + game-parser oracle tests
tests/JavaFarmer.Plugin.Tests/    net8.0 xunit: bridge tests (headless, real Core.dll)
content/docs/...             Java versions of the in-game docs (mirrors Languages/EN/docs)
content/Strings/...          Java-flavoured overrides of Languages/EN/Strings/*.txt (only changed keys)
scripts/                     build / package / install helpers
```
- Code must run on Unity's Mono (netstandard2.1). No APIs beyond netstandard2.1. If you use `init`/records,
  add the `IsExternalInit` polyfill.
- Game DLLs are referenced via `$(GameDir)/TheFarmerWasReplaced_Data/Managed/*.dll` with `<Private>false</Private>`.
- **Headless game parser works under .NET 8** (verified): `Tokenizer.Tokenize(code, out ts); Parser.Parse(ts, cw)`
  where `cw = (CodeWindow)RuntimeHelpers.GetUninitializedObject(typeof(CodeWindow))` and its private field
  `parsedFunctions` is set to a new instance via reflection (FunctionNode's ctor writes into it).
  `Program.syntaxTree.ToString()` gives a structural dump. Localizer is unloaded headless, so ParseException messages are raw keys.

## 3. Java language subset

Target: what a Java 25 beginner writes. When something is valid Java but unsupported, report a clear
error: "X is not supported in JavaFarmer". Never silently accept invalid Java.

### 3.1 File structure (each code window = one file = one class; file name = class name)
Two accepted forms:
1. **Compact source file** (Java 25, JEP 512): top-level fields and methods, entry point `void main()`.
   ```java
   int rows = 0;            // field
   void main() {            // entry point
       while (true) { harvest(); }
   }
   void helper() { ... }
   ```
2. **Class**: `[public] class <FileName> { static fields; static methods; [public static void main(String[] args)] }`.
   The class name must equal the window's file name (error otherwise, like javac's "class X is public, should be declared in a file named X.java").
   Only `static` members (no instance fields or methods, no constructors, no `new MyClass()`). Nested classes, interfaces, enums, records: not supported in v1.
   `main(String[] args)` is allowed; `args` is unused (bind it to an empty list).
- A file without `main` is a library (other files call `FileName.method()`).
- Modifiers `public/private/protected/static/final` are accepted. `final` is enforced (assigning a final is a compile error).
- `import` declarations: accept and ignore `import java.util.*;` / `java.util.ArrayList` etc. (the collections are always available). Any other import is an error.
- Comments `//`, `/* */`, `/** */`.

### 3.2 Statements
Local variable declarations (with explicit type or `var`, multiple declarators `int a = 1, b;`), expression
statements (assignment, compound assignment `+= -= *= /= %=`, `++`/`--` pre/post **as statements only**, method calls),
`if / else if / else`, `while`, `do … while`, classic `for (init; cond; update)`, enhanced `for (T x : iterable)`,
`break`, `continue` (unlabeled only), `return`, blocks `{}`, empty statement `;`,
`switch` statement over int/String/enum-like constants (both `case X:` with fallthrough-free `break` style and `case X ->` arrow form;
reject fallthrough between non-empty cases with a clear error).
Not supported: try/catch/throw, labels, assert, synchronized, local classes, `yield`.

### 3.3 Expressions
Literals: int, double (`1.5`, `1e3`, `2d`), boolean, String (escapes `\n \t \" \\`), `null`. char literals: not supported in v1.
Operators: `+ - * / %`, unary `- + !`, `== != < <= > >=`, `&& ||`, ternary `c ? a : b` (see lowering),
casts `(int) (double) (String) (Entity)` …, parentheses, method calls, field access, array/list indexing `a[i]`,
`new int[n]`, `new int[]{1,2}`, `new int[w][h]`, `new ArrayList<>()` etc., lambdas `() -> expr` / `() -> { … }`,
method references `this::m`, `FileName::m`, string concatenation with `+`. Bitwise ops & `instanceof`: not supported in v1.

## 4. Type system (checked at compile time, like javac)

- Primitives: `int`, `double`, `boolean` (`long`, `float`, `char`, `byte`, `short`: compile error "use int/double in JavaFarmer"). Boxed `Integer Double Boolean` for generics; auto (un)boxing.
- `String`, `Object` (anything assigns to Object; Object → T requires a cast; casts from Object are unchecked at runtime).
- Arrays `T[]`, `T[][]`. `List<T>/ArrayList<T>`, `Map<K,V>/HashMap<K,V>`, `Set<T>/HashSet<T>`. Diamond `<>` inference.
- Game types: `Entity`, `Ground`, `Item`, `Hat`, `Unlock`, `Leaderboard`, `Direction`, `Drone`, `Position`, `Companion`.
- Functional type for lambdas / method refs: `Runnable` (no args, void) and `Supplier<T>` (no args, returns T). Lambdas are only allowed where a Runnable/Supplier is expected (spawnDrone, a local of that type). `r.run()` / `s.get()` call them.
- Numeric promotion int→double implicit; double→int needs a cast. `int / int` is integer division, `%` is Java remainder.
- Definite assignment for locals, "missing return statement", unreachable code after return/break are errors (keep the checks simple but present).
- Java scoping: block-scoped locals; redeclaring a name already in scope is an error ("variable x is already defined in method main()").
- Error messages follow javac wording where practical: `';' expected`, `cannot find symbol: variable foo`, `incompatible types: double cannot be converted to int`, `missing return statement`, `method plant in class Farm cannot be applied to given types`.

## 5. Game API (as seen from Java)

All game functions are available unqualified (as if statically imported). Names are **lowerCamelCase**.
Game constants keep the game's own names (they are game data): `North East South West`, `Entities.Carrot`, `Items.Hay`, `Grounds.Soil`, `Hats.Straw_Hat`, `Unlocks.Carrots`, `Leaderboards.Hay`.
The compiler must not hard-code the member lists of Entities/Items/... (they change between game versions): any
`Entities.<Ident>` type-checks as Entity, etc. (The plugin may supply known names for completion.)

| Java | game Python | Java return type |
|---|---|---|
| `harvest()` | harvest | boolean |
| `canHarvest()` | can_harvest | boolean |
| `plant(Entity e)` | plant | boolean |
| `move(Direction d)` | move | boolean |
| `canMove(Direction d)` | can_move | boolean |
| `swap(Direction d)` | swap | boolean |
| `till()` | till | void |
| `getPosX()`, `getPosY()` | get_pos_x, get_pos_y | int |
| `getWorldSize()` | get_world_size | int |
| `getEntityType()` | get_entity_type | Entity (may be null) |
| `getGroundType()` | get_ground_type | Ground |
| `getTime()`, `getTickCount()` | get_time, get_tick_count | double |
| `useItem(Item i)`, `useItem(Item i, int n)` | use_item | boolean |
| `getWater()` | get_water | double |
| `doAFlip()`, `petThePiggy()`, `clear()` | do_a_flip, pet_the_piggy, clear | void |
| `changeHat(Hat h)` | change_hat | void |
| `System.out.println(x)`, `System.out.println()`, `System.out.print(x)` | print | void |
| `quickPrint(x)` | quick_print | void |
| `numItems(Item i)` | num_items | double |
| `getCost(Entity\|Item\|Unlock t)` | get_cost | Map<Item, Double> (may be null) |
| `getCompanion()` | get_companion | Companion (may be null) |
| `unlock(Unlock u)` | unlock | boolean |
| `numUnlocked(Unlock\|Entity\|Ground\|Item t)` | num_unlocked | int |
| `measure()`, `measure(Direction d)` | measure | Object (number, Position or null — learners cast) |
| `leaderboardRun(Leaderboard l, String file, double speedup)` | leaderboard_run | void |
| `simulate(String file, Object unlocks, Map<Item,Double> items, Map<String,Object> globals, double seed, double speedup)` | simulate | double |
| `setExecutionSpeed(double)`, `setWorldSize(double)` | set_execution_speed, set_world_size | void |
| `spawnDrone(Runnable r)`, `spawnDrone(Supplier<T> s)`, `spawnDrone(String file)` | spawn_drone | Drone (may be null) |
| `waitFor(Drone d)` | wait_for | Object |
| `hasFinished(Drone d)` | has_finished | boolean |
| `numDrones()`, `maxDrones()` | num_drones, max_drones | int |
| `Math.min(a,b)`, `Math.max(a,b)`, `Math.abs(a)` | min, max, abs | numeric (int if all int) |
| `Math.random()` | random | double |
| `Math.floor/ceil/round/sqrt/pow` | synthetic helpers (see §6.6) | double / long→int for round |
| `String.valueOf(x)`, `"" + x` | str | String |
`Position` = game `(x, y)` tuple: `p.x()`→`p[0]`, `p.y()`→`p[1]`.
`Companion` = game `(entity, (x, y))`: `c.entity()`→`c[0]`, `c.position()`→`c[1]`, `c.x()`→`c[1][0]`, `c.y()`→`c[1][1]`.
`trade` was removed from the game: not exposed.

Collections (static types drive the lowering):
| Java | Python |
|---|---|
| `new ArrayList<>()`, `new ArrayList<>(c)`, `List.of(a,b)`, `new HashSet<>()`, `new HashSet<>(c)`, `Set.of(..)`, `new HashMap<>()`, `new HashMap<>(m)`, `Map.of(k,v,..)` | `[]`, `list(c)`, `[a,b]`, `set()`, `set(c)`, `{a,b}`, `{}`, `dict(m)`, `{k:v}` |
| list `add(x)`, `add(i,x)`, `get(i)`, `set(i,x)` (statement), `remove(int i)`→value, `remove(Object)`(on List<Integer> ambiguous: follow Java: int arg = index), `size()`, `isEmpty()`, `contains(x)`, `clear()`, `indexOf(x)`(helper) | `append`, `insert`, `l[i]`, `l[i] = x`, `pop(i)`, `remove(x)`, `len`, `len==0`, `x in l`, (helper) |
| set `add`, `remove`, `contains`, `size`, `isEmpty` | `add`, `remove`, `in`, `len` |
| map `put(k,v)` (statement), `get(k)` (null if missing), `getOrDefault(k,d)`, `containsKey(k)`, `remove(k)`, `keySet()`, `values()`, `size()`, `isEmpty()` | `m[k] = v`, helper, helper, `k in m`, `m.pop(k)`, `m` (iterate keys), helper, `len` |
| arrays `new int[n]`, `new T[n]` (null-filled), `a.length`, `a[i]`, `a[i] = v` | helper `__jarr(n, default)`, `len(a)`, indexing |
| `for (T x : coll)` (list/set/array/`m.keySet()`/`m.values()`) | `for x in …` |
| String `length()`, `equals(o)`, `isEmpty()`, `charAt(i)` (returns String in v1, error if compared to char), `+` | `len`, `==`, …, `s[i]` |
Only use Python features the game actually implements: check `BuiltinFunctions` (Core) and the Py* types in Utils (methods: append, add, remove, pop, insert). Anything else must be a synthetic helper written with basic game Python.

## 6. Lowering to game Python

The emitter produces game Python (tabs or 4-space indents, `\n` newlines). It must be accepted by the game's
`Parser` (oracle tests check every corpus program).

### 6.1 Module shape
```
<helpers used by this file>                 # synthetic
import Other                                # one per referenced file; mapped to the first use of Other.
def method(a, b):                           # user methods (+ "global f" lines for static fields they assign)
    ...
field = <init or Java default 0/0.0/False/None>   # fields in declaration order
def main():                                 # def line synthetic, body user code
    ...
if __name__ == "__main__":                  # synthetic
    main()
```
Imported modules get `__name__ = <module name>` from the game, so a library's `main` does not run on import.
Reserved Python names (`len, range, print, list, dict, set, str, min, max, abs, random`, every game builtin's snake name,
`True False None and or not in is def pass global import from elif`) used as Java identifiers must be renamed
(e.g. `list`→`list_j`) consistently. Java identifiers can't start with `__j`; that prefix is reserved for helpers.

### 6.2 Statements
- `int x = e;` → `x = e`; `int x;` emits nothing (definite assignment guarantees a later assignment).
- `x++` → `x += 1`. `else if` → `elif`. `!`→`not`, `&&`→`and`, `||`→`or`, `true/false/null`→`True/False/None`.
- Classic for: if it matches `for (int i = A; i < B; i++)` (also `<=`→`B+1`, `i += k` → step k, counting down with `>`/`--`), the body
  never assigns `i`, and `B` is loop-invariant (literal, a local/param not assigned in the body, `getWorldSize()`, or `a.length`/`l.size()` of an array/list not structurally modified in the body), emit
  `for i in range(A, B[, step]):`. Otherwise lower to `init; while cond: body; update` and rewrite each `continue` that belongs to
  this loop into `update; continue`.
- do-while → `while True: body; if not (cond): break`, `continue` rewritten to jump to the condition check (duplicate the check before `continue`).
- switch → if/elif chain over a temp (evaluate the selector once); `break` inside a switch case must not be emitted as a loop break (strip it; any other break inside switch that targets an enclosing loop needs a flag — v1 may reject `break` to an outer loop from inside a switch with a clear error).
- ternary: allowed anywhere; lower by hoisting into a temp with if/else **only** when evaluation order is preserved (it is the whole RHS of an assignment/declaration/return, a whole argument whose preceding arguments have no side effects, or a whole expression statement). Otherwise compile error "?: is only supported as a whole value here".
- `a.set(i, v);`, `m.put(k, v);` as statements only (their return value is not supported: error if used as value).

### 6.3 Expressions
- String concatenation: if either side is String, wrap the non-String side in `str()` (synthetic call). Java renders doubles like `1.0`; the game renders numbers its own way — accepted v1 difference (document it).
- `int / int` → `a // b` when both operands are provably non-negative (non-negative literals, getPosX/getPosY/getWorldSize/numDrones/maxDrones, size/length, loop counters of a range-for with non-negative start counting up, sums/products of those); otherwise helper `__jdiv(a, b)` (truncates toward 0). Same for `%` → `%` or `__jmod`. `(int) d` → `__jtrunc(d)` (or `d // 1` if provably non-negative).
- double `/` → `/`.
- `==`/`!=` on Strings/objects → Python `==`/`!=` (v1: no reference-equality warning).
- Lambdas / method refs → hoisted nested `def __jlambdaN():` emitted immediately before the statement that contains them, in the current function (closures capture lexically — verified in Scope/PyFunction). `this::m`/`File::m` → the function value `m`/`File.m` directly.
- Static field access from another file: `Utils.count` → `Utils.count`.

### 6.4 Globals
Every method that assigns a static field gets `global <name>` at the top of its body (synthetic).

### 6.5 Numbers
The game has one number type (double). `int` vs `double` exists only at compile time. Integer overflow is not emulated (document).

### 6.6 Helpers (synthetic, emitted once per module, only if used)
`__jdiv, __jmod, __jtrunc, __jfloor, __jceil, __jround, __jsqrt (Newton), __jpow (int exponent loop; non-int exponent → compile error in v1), __jarr(n, d), __jarr2(w, h, d), __jget(m, k) (None if missing), __jgetd(m, k, d), __jindexof(l, x)`.
Write them with constructs the game supports (def/if/while/return/arith). Name them with the `__j` prefix.

## 7. Source map & dependency policy (compiler ↔ plugin contract)

The game charges unlock **dependencies** per AST node at runtime (`Node.CheckDependencies`):
AssignmentNode→"variables", BinaryExpr/Comparison/Unary→"operators", BranchNode→"if"/"while", DefNode→"functions",
ForNode→"for", ListNode→"lists", DictNode→"dicts", SetNode→"sets", ImportNode→"import", CallNode→the function's name.
A fresh save has almost nothing unlocked (`while` comes first via Loops; `if` via Speed; `for`/`range` only at Expand level 2; functions later).
Java's mandatory structure must not cost unlocks, and Java constructs must cost the same unlock as their Python twin.

Every emitted Python character range is tagged:
```csharp
public enum SegmentKind { User, Structural, Synthetic }
public sealed class Segment { public int PyStart, PyEnd, JavaStart, JavaEnd; public SegmentKind Kind; public string DependencyOverride; }
```
- **User**: normal game behaviour.
- **Synthetic** (helpers, `def main():`, the `__name__` guard, `global` lines, `str()` for concatenation, temps): the node checks **no** dependencies. Its Java span is the construct that caused it (e.g. `void main()` header), or (-1,-1) for helper bodies.
- **Structural** (the header of a classic for lowered to while: init assignment, condition, update): non-call nodes check no dependencies; CallNodes keep theirs.
- **DependencyOverride** (string): replaces the node's dependency. Used on the `while` produced by a classic for (→ "for") so both Java for-loop forms need the same unlock as Python's `for`. `range` calls produced from a classic for stay User (Python's `for i in range(n)` needs "range" too).
A node is classified by the segment that contains its Python `wordStart`. Java span of a node = JavaStart of the segment containing wordStart … JavaEnd of the segment containing wordEnd-1.

Java concept → unlock mapping (falls out naturally from the lowering, listed for the doc writers):
`while`/`true`/`false`/`break`/`continue`→Loops, `if/else`→Speed, `for` (both kinds)→Expand 2, `&& || !` and arithmetic/comparison→Operators,
local variables/fields→Variables, methods→Functions, `return`→Functions, other files' classes→Imports, `ArrayList`/arrays→Lists, `HashMap`→Dictionaries,
`HashSet`→Dictionaries (grants "sets"), `System.out.println`→Debug, `Math.*`→Utilities, `String.valueOf`→Debug ("str").

## 8. Compiler public API (src/JavaFarmer.Compiler)

```csharp
namespace JavaFarmer.Compiler {
  public sealed class CompileOptions {
    public string FileName;                                   // window name = class name = Python module name
    public IReadOnlyDictionary<string, string> OtherFiles;    // name → Java source of the other windows (for cross-file symbols); may be null
  }
  public sealed class Diagnostic { public string Message; public int Start, End; }   // Java offsets, End exclusive
  public sealed class SourceMap {
    public IReadOnlyList<Segment> Segments;
    public Segment Find(int pyOffset);                         // segment containing the offset (nearest preceding if between)
  }
  public sealed class CompileResult {
    public bool Success; public string Python; public SourceMap Map;
    public IReadOnlyList<Diagnostic> Errors;                   // first error is shown in game
    public IReadOnlyDictionary<string, string> Names;          // Python identifier -> Java name for renamed symbols ("list_j" -> "list", "__jlambda1" -> "lambda")
  }
  // Segment also has: public bool NoHighlight;  // scaffolding (def main, __name__ guard, global lines) never blinks/highlights at runtime
  public static class JavaCompiler { public static CompileResult Compile(string javaSource, CompileOptions options); }

  // Editor support (used by the plugin)
  public enum TokenKind { Keyword, Type, Identifier, Number, String, Comment, Operator, Punctuation, Constant, Whitespace, Unknown }
  public static class JavaSyntax {
    public static IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source);  // must never throw
    public static IReadOnlyCollection<string> Keywords { get; }
  }
  public static class GameApi {   // single source of truth for names
    public static string ToPython(string javaName);   // "canHarvest" → "can_harvest", null if unknown
    public static string ToJava(string pythonName);   // "can_harvest" → "canHarvest", null if unknown
    public static IReadOnlyList<ApiFunction> Functions { get; }   // Java name, python name, signature text, return type
  }
}
```
Compile must be fast (< 5 ms for a 300-line file; it runs on every edit) and must never throw (internal errors → Diagnostic).

## 9. Plugin integration points (src/JavaFarmer.Plugin)

Line numbers refer to `.decompiled/Core.decompiled.cs` unless noted.
1. **Parse hook** — prefix `Parser.Parse(TokenStream, CodeWindow f)` (9150). Java text = `f.CodeInput.text`. Compile; on errors throw `ParseException`
   with Java offsets. On success `Tokenizer.Tokenize(python)`, call the original parser (re-entrancy guard), then walk the tree (all `slots`
   recursively; dedupe `BoxedNodeParams` by reference) and remap spans + register the dependency policy. If the game parser rejects the
   Python, report "internal JavaFarmer error" at the mapped position and log the Python to the BepInEx log.
   Put the bridge in a class testable headless (`JavaParseBridge.Parse(string java, CodeWindow f)`).
2. **Dependencies** — prefix on `CheckDependencies` of Node and every override (AssignmentNode, BinaryExprNode, BranchNode, CallNode,
   ComparisonNode, DefNode, DictNode, ForNode, ImportNode, ListNode, SetNode, UnaryExprNode): consult a
   `ConditionalWeakTable<BoxedNodeParams, NodePolicy>`.
3. **Editor** — syntax colouring `CodeUtilities.SyntaxColor2(code, searchWord, searchIndex)` (Utils:151; also used by docs code blocks and
   unlock boxes): Java colouring with the same colour palette/markup and search-word highlighting. Auto-indent after `{` (Core ~12570 checks
   `':'`) and dedent when `}` is typed on an otherwise blank line. Code completion word list (`GetWordList` 12866,
   `GetSubWordList`): Java names of unlocked builtins (use `GameApi.ToJava`), Java keywords for unlocked features, user symbols; hide `__j*` names.
   Hover tooltips (`TooltipUtils.GetWordTooltip`): map Java names → snake keys.
4. **New file template** — a new empty code window starts with `void main() {\n\t\n}` (find where windows are created). Don't touch existing non-empty files.
5. **Docs & strings** — prefix `Localizer.LoadDoc(path)` (Utils): serve `content/docs/<path>` from the plugin folder when present, for every UI language.
   Override string keys from `content/Strings/*.txt` (same `@key` format). Unlock boxes show `UnlockSO.displayCode` (Python): map to Java via a table in content.
6. **Runtime error text** — ExecuteException messages that contain Python builtin names (e.g. `error_wrong_number_args`) show the Java name.
7. **Leaderboards** — modded runs must not be uploaded to the Steam leaderboards: block score submission while the mod is active (find the upload call), and say so in the leaderboard docs.
8. Config (BepInEx ConfigFile): `Enabled` (default true). If disabled the game behaves exactly vanilla.
9. Save files keep the `.py` extension in v1 (contents are Java). `__builtins__.py` is left alone; additionally write `JavaFarmerApi.java` (API stubs for external editors) next to it if easy.

## 10. Content (content/)
Rewrite every EN doc (`Languages/EN/docs/**/*.md`) for Java, keeping file names, the game's markdown dialect (single backticks
for code blocks, `<unlock=…>`, `<spoiler=…>`, `{{…}}` placeholders, `![](Sprite)` images), structure and tone.
Every code sample must be valid JavaFarmer Java. Snippets inside a lesson may show statements without the `void main()` wrapper
once the wrapper has been introduced (first_program.md introduces it). The EN text is CC0 (Languages/LICENSE), so rewriting is allowed.
Also: `Strings/code_tooltips.txt` (builtin tooltips), parse/execute error strings that mention Python syntax, `unlock_tooltips.txt` where it mentions Python, and `content/unlock_display_code.txt` (unlock name → Java snippet for the unlock box).

## 11. Testing
- Compiler unit tests: lexer, parser, type errors (message + span), lowering snapshots.
- **Oracle tests**: every corpus program's Python goes through the real game `Tokenizer`+`Parser` headless and must parse.
- **Differential tests**: for pure-computation programs (no farm actions), run the Java with the real JDK (`java File.java` with a
  tiny stub for println) and the generated Python with CPython (`python3 -I` with stubs mapping print/len/range/str/min/max/abs, plus True
  division semantics matching the game: all numbers are floats → run CPython with ints converted? simplest: compare printed integers
  after normalising `3.0`→`3`) and compare stdout. Skip if java/python3 are missing.
- Plugin bridge tests: headless `JavaParseBridge.Parse` with a fake CodeWindow; assert spans map into Java text and policies are set.

## 12. Out of scope for v1
Classes with instances, interfaces, generics declarations by the user, exceptions, char, bitwise ops, labeled breaks,
non-EN docs translations, reproducing Java's int overflow and double formatting.
