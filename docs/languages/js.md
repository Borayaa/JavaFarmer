# JavaScript in JavaFarmer (id `js`)

Modern JavaScript (ES2023, ES module semantics, strict mode) for The Farmer Was Replaced. Every valid JavaFarmer
JavaScript program is real JavaScript: the conformance tests run `node --check` on every valid corpus file as an ES
module, and the differential tests run the diff programs in real node (with the stub `farm.js` imported first) and
compare the output with the game-Python version.

Frontend: `src/JavaFarmer.Compiler/Languages/JavaScript/` — `JsLexer` (tokens, ASI line-break flags, template
literals) → `JsParser` (AST in `JsAst.cs`, V8 error wording) → `JsLowering` (scopes, checks, IR lowering;
`JsLowering.Expr.cs` for expressions / API calls, `JsLowering.Post.cs` for the type-directed fix-ups after
`IrTypeInference`) → shared `IrEmitter`. Printer `JsPrinter` (IR from any language → JavaScript),
`JsFormat` (number and string spelling), `JavaScriptLanguage` (ILanguage: `Id` "js", `DisplayName`
"JavaScript", `FileExtension` ".js"; the plugin picks the language of a code window by this extension).

TypeScript is not accepted (type annotations are syntax errors, as in node).

## 1. Program form and entry point

A file is an ES module whose **top-level statements run from top to bottom** (like a Python script). There is no
entry function. `function main() { ... }` followed by `main();` is just a normal function and a normal call.

    let harvested = 0;
    while (true) {
    	if (canHarvest()) {
    		harvest();
    		harvested++;
    	}
    	move(North);
    }

* Function declarations are hoisted: top-level code may call a function declared further down (it is emitted before
  the first statement that needs it).
* Top-level variables that a function declaration uses (reads or assigns), that a closure assigns, or that are
  exported become game globals (the emitter writes the `global x` lines). Other top-level variables are script
  locals, exactly like the Python frontend.
* The new-window template is an empty file (caret at 0).
* Strict mode is always on (ES modules): assigning an undeclared name is `x is not defined` at compile time,
  `with`, octal literals and `eval` / `arguments` as names are rejected with V8's messages.

## 2. API naming

Game builtins are camelCase globals (`GameApi.ToStyle(name, Camel)`); registered extension builtins work with no
code change (`scan_port` → `scanPort`). Calls are checked at compile time: unknown names are
`fooBar is not defined`, a snake_case name gets `can_harvest is not defined (did you mean canHarvest?)`,
`print(x)` gets `print is not defined (did you mean console.log?)`. JavaScript itself never checks argument
counts, but the game does, so JavaFarmer reports `plant() expects 1 argument, but got 0` (also
`expects 1 to 2 arguments`) and argument types it can see (`move() expects a Direction as argument 1, but got a string`).

| game (Python) | JavaScript |
|---|---|
| `harvest()`, `can_harvest()` | `harvest()`, `canHarvest()` |
| `plant(Entities.Carrot)`, `till()` | `plant(Entities.Carrot)`, `till()` |
| `move(North)`, `can_move(East)`, `swap(South)` | `move(North)`, `canMove(East)`, `swap(South)` |
| `get_pos_x()`, `get_pos_y()`, `get_world_size()` | `getPosX()`, `getPosY()`, `getWorldSize()` |
| `get_entity_type()`, `get_ground_type()` | `getEntityType()`, `getGroundType()` |
| `use_item(Items.Water, 2)`, `get_water()` | `useItem(Items.Water, 2)`, `getWater()` |
| `num_items(Items.Hay)`, `get_cost(Entities.Pumpkin)` | `numItems(Items.Hay)`, `getCost(Entities.Pumpkin)` (a Map, or null) |
| `do_a_flip()`, `pet_the_piggy()`, `change_hat(Hats.Straw_Hat)` | `doAFlip()`, `petThePiggy()`, `changeHat(Hats.Straw_Hat)` |
| `measure()`, `get_companion()` | `measure()`, `getCompanion()` |
| `unlock(Unlocks.Speed)`, `num_unlocked(x)` | `unlock(Unlocks.Speed)`, `numUnlocked(x)` |
| `spawn_drone(f)`, `wait_for(d)`, `has_finished(d)`, `num_drones()`, `max_drones()` | `spawnDrone(f)`, `waitFor(d)`, `hasFinished(d)`, `numDrones()`, `maxDrones()` |
| `get_time()`, `get_tick_count()`, `set_execution_speed(s)`, `set_world_size(n)` | `getTime()`, `getTickCount()`, `setExecutionSpeed(s)`, `setWorldSize(n)` |
| `leaderboard_run(...)`, `simulate(...)`, `clear()` | `leaderboardRun(...)`, `simulate(...)`, `clear()` |
| `print(a, b)` | `console.log(a, b)` (any number of arguments; `console.log()` prints an empty line) |
| `quick_print(a, b)` | `quickPrint(a, b)` |
| `min(a, b)`, `max(a, b)`, `abs(x)` | `Math.min(a, b)`, `Math.max(a, b)`, `Math.abs(x)` |
| `random()` | `Math.random()` |
| `len(x)` | `a.length`, `text.length`, `map.size`, `set.size` |
| `str(x)` | `String(x)`, `x.toString()`, `${x}` in a template, or `"text" + x` |
| `x // y` | `Math.floor(x / y)` |
| `int(x)` (truncate) | `Math.trunc(x)` |

`ILanguage.ApiName` / `FromApiName`: `can_harvest` ↔ `canHarvest`, `print` ↔ `console.log`,
`min` / `max` / `abs` / `random` ↔ `Math.min` / `Math.max` / `Math.abs` / `Math.random`;
FromApiName also accepts the short forms `print`, `min`, `max`, `abs`, `random`.

Math: `Math.floor` / `Math.ceil` / `Math.sqrt` / `Math.round` (half up, like JS:
`Math.round(-2.5)` is -2) / `Math.trunc` / `Math.pow` / `Math.min` / `Math.max` / `Math.abs` /
`Math.random`. Other members (`Math.sin`, ...) are `Math.sin is not supported in JavaFarmer`.
`Number(x)`, `parseInt`, `parseFloat`: `… is not supported in JavaFarmer (the game cannot convert text to numbers)`.

## 3. Constants

`Entities.Carrot`, `Items.Hay`, `Grounds.Soil`, `Unlocks.Speed`, `Hats.Straw_Hat`,
`Leaderboards.Fastest_Reset`: the game's names, any member name is accepted (new game content needs no update).
Directions are bare: `North`, `East`, `South`, `West`. Constants cannot be assigned
(`game constants cannot be changed`). In the stub the containers are Proxy objects, so every member exists in node too.

## 4. Variables, values, operators

* `let`, `const`, `var`. Block scoping is real: a shadowing `const x` in an inner block is a different
  variable (the emitter renames it, e.g. `x_2`). Redeclaration: `Identifier 'x' has already been declared`
  (a second `var x` is allowed, as in JS). `const` reassignment: `Assignment to constant variable.`
  `const x;`: `Missing initializer in const declaration`. Use before a `let` / `const`
  declaration: `Cannot access 'x' before initialization`.
* `var` is function scoped (hoisted to the enclosing function or the module), but reading a `var` before its
  first assignment is a game error instead of `undefined` (simplified hoisting). `let x;` starts as null.
* Array destructuring: `const [x, y] = pos;`, `[a, b] = [b, a];`, nested `const [e, [x, y]] = companion;`
  (tuple unpacking). Object destructuring and destructuring parameters are not supported.
* Numbers: integers, decimals, `1e3`, `0xff`, `0b101`, `0o17`, `1_000`. Strings `'..'`, `".."` with the
  usual escapes; a string may not contain a line break (`line breaks (\n) inside strings are not supported in JavaFarmer`:
  the game's strings cannot hold one). Template literals `` `a ${x} b` `` (nested too) become `"a " + str(x) + " b"`.
  `true`, `false`, `null`, `undefined`.
* Operators: `+` (adds numbers; concatenates when a side is a string, decided after type inference), `- *`,
  `/` (always true division: `7 / 2` is 3.5), `%` (JS remainder, sign of the dividend: `-7 % 3` is -1),
  `**`, unary `- + !`, `=== !==`, `== !=` (same meaning as `===` / `!==`), `< <= > >=`,
  `&& ||` (short-circuit, return one of the operands), `?:`, `x in obj`. Assignments
  `= += -= *= /= %= **=` and `++` / `--` are statements only (`using an assignment as a value is not supported in JavaFarmer`,
  `++ can only be used as a separate statement in JavaFarmer`). `-2 ** 2` is V8's
  `Unary operator used immediately before exponentiation expression…`; `a ?? b || c` is
  `Unexpected token '||'`. `a < b < c` is rejected (`… compares a boolean with c in JavaScript`).
* `??` only as `map.get(key) ?? defaultValue` (one lookup with a default); anything else:
  `?? is only supported as map.get(key) ?? default in JavaFarmer`.

## 5. Statements

`if / else if / else`, `while`, `do … while`, classic `for (init; test; update)` (counting loops become
`for i in range(...)`, others a `while`), `for...of` (arrays, strings, Sets, `map.keys()`,
`map.values()`, `Object.keys(o)`, `Object.values(o)`), `for (const [k, v] of map)` /
`map.entries()` / `Object.entries(o)`, `for...in` over object or Map keys, `switch`, `break`,
`continue`, `return`, blocks, the empty statement, `import` / `export`.

* `switch`: becomes `if / elif / else` with `==`. Empty cases stack (`case 1: case 2:`); every non-empty
  case must end with `break`, `return` or `continue` (or be the last one): fall-through is
  `falling through to the next case is not supported in JavaFarmer`; a `break` elsewhere in a case is
  `break inside a switch case is only supported as the last statement of the case`. `continue` inside a switch
  continues the enclosing loop.
* `for...in` over something known to be an array, string or Set: `for...in loops over the keys of an object: use for (const x of list) to loop over the items`.
  `for (const x of map)`: `for...of over a Map gives [key, value] pairs: use for (const [key, value] of map) or for (const key of map.keys()) in JavaFarmer`.
* Labels: `labels are not supported in JavaFarmer`. `break` / `continue` / `return` in the wrong place:
  `Illegal break statement`, `Illegal continue statement: no surrounding iteration statement`, `Illegal return statement`.

### Automatic semicolon insertion

Real ASI: a statement may end at a line break, `}` or the end of the file when the next token cannot continue it;
`return` / `break` / `continue` followed by a line break end there, and `++` / `--` after a line break
belong to the next line. Two statements on one line without `;` are V8's `Unexpected identifier 'harvest'`.
Syntax errors use V8's wording: `Unexpected token '…'`, `Unexpected identifier 'x'`, `Unexpected number`,
`Unexpected string`, `missing ) after argument list`, `Unexpected end of input` (pointing at the last token),
`Invalid or unexpected token`, `Unterminated template literal`.

## 6. Functions and closures

* Top-level `function f(a, b = 2) { … }` → a game function (default parameters supported). Calls of your own
  functions are checked for the argument count (`step() expects 1 argument, but got 2`): the game checks it at run
  time, JavaScript would not.
* Nested function declarations, function expressions `function () { … }` and arrow functions `(x) => x * 2`,
  `() => { … }` become nested game functions (closures see the enclosing variables by reference). Named function
  expressions are rejected (declare the function instead).
* A closure that **assigns** a local variable of an enclosing function: `assigning a captured variable inside a closure is not supported in JavaFarmer`
  (the game has no `nonlocal`). Assigning a top-level variable from a closure is fine (it becomes a global).
* `spawnDrone(worker)`, `spawnDrone(() => farmColumn(x))`, `spawnDrone(function () { … })`; it returns the
  drone or null.
* Functions are values: `const f = sum; f(1, 2)`.

## 7. Collections

Arrays (game lists), Maps (game dicts, any key: numbers, strings, game constants, positions), Sets, plain objects
(string-keyed game dicts). Methods are chosen by name and fixed up by type after type inference.

| JavaScript | game |
|---|---|
| `[1, 2]`, `[...a]`, `a.slice()`, `Array.from(a)` | list literal, `list(a)` (copy) |
| `new Array(n).fill(v)`, `Array(n).fill(v)` | a list of n copies of v (helper) |
| `Array.from({ length: w }, () => new Array(h).fill(v))` | a w × h grid (helper) |
| `a.push(x)` (also `a.push(x, y)`), `a.pop()`, `a.shift()`, `a.unshift(x)` | `append`, `pop()`, `pop(0)`, `insert(0, x)` |
| `a.splice(i, 1)`, `a.splice(i, 1)[0]`, `a.splice(i, 0, x)` | `pop(i)`, `insert(i, x)` |
| `a.indexOf(x)`, `a.includes(x)`, `a.length`, `a[i]`, `a[i] = v`, `a.length = 0` | index (-1 when missing), `in`, `len`, index, store, clear |
| `new Map()`, `new Map([[k, v], …])`, `new Map(m)` | dict literal, dict copy |
| `m.get(k)` (null when missing), `m.get(k) ?? d`, `m.set(k, v)`, `m.has(k)`, `m.delete(k)`, `m.size`, `m.clear()` | lookups, `m[k] = v`, `in`, remove if present, `len`, clear |
| `m.keys()`, `m.values()`, `for (const [k, v] of m)` | keys, values, key/value loop |
| `new Set()`, `new Set([1, 2])`, `new Set(a)` | set literal, `set(a)` |
| `s.add(x)`, `s.has(x)`, `s.delete(x)`, `s.size`, `s.clear()` | `add`, `in`, remove if present, `len`, clear |
| `{ a: 1, "b c": 2, [k]: v }`, `o.a`, `o["a"]`, `o[k]`, `o.a = 1`, `"a" in o` | string-keyed dict, index, store, `in` |
| `Object.keys(o)`, `Object.values(o)`, `Object.entries(o)` (for...of only) | keys, values, key/value loop |
| `s.length`, `s[i]`, `s.charAt(i)`, `s.includes(x)` | `len`, index, `in` |

* `.push()`, `.set()`, `.add()`, `.unshift()`, `.clear()` are statements (`'.push()' can only be used as a separate statement in JavaFarmer`);
  chained `m.set(a, 1).set(b, 2)` is rejected.
* An array literal used as a Map key or Set item (`seen.add([x, y])`) is a game tuple, so `seen.has([x, y])`
  finds it (positions work as keys). So is `const p = [x, y];` when `p` is only read: indexed (`p[0]`), destructured,
  measured (`p.length`), searched (`includes`/`indexOf`), iterated, copied (`[...p]`), printed with
  `console.log`, or used as a key / item / stored value. Any other use (`p.push()`, `p[0] = v`, passing `p` to a
  function, `const q = p`, `"" + p`, `p === q`, `export`) keeps it a game list, and a game list cannot be a Map key
  or Set item (game error at run time). `let` arrays and arrays returned by functions are lists too.
* `map.length` / `array.size` are mistakes: `a Map has no .length in JavaScript: use .size`, `arrays have no .size in JavaScript: use .length`.
* Not supported, with the reason in the message: higher-order and other array methods (`'.map()' is not supported in JavaFarmer (use a for...of loop)`,
  `.sort()` "sort the list with your own loop", `.join()` "build the text with a loop and +"), `.slice(1)` with
  arguments (the game has no slicing), `.at()`, string methods (`'.toUpperCase()' is not supported in JavaFarmer (the game has no string methods)`),
  `.toFixed()`, `new Date()` and other constructors (`new … is not supported in JavaFarmer`).

## 8. Printing and FormatValue

`console.log(a, b)` is the game's `print(a, b)` (values joined by a space); `quickPrint` is `quick_print`.
`FormatValue` (how the mod shows a game value to a JavaScript player) is node's `console.log` / `util.inspect`
format, always on one line:

| value | shown as |
|---|---|
| numbers (the game's numbers are doubles) | `3`, `0.5`, `1e+21`, `1e-7`; `-0` shows as `0` |
| booleans, None | `true`, `false`, `null` |
| strings | bare at the top level, `'quoted'` inside collections (`"it's"` when that avoids an escape) |
| lists and tuples (positions) | `[ 1, 2, 3 ]`, `[]`, `[ 3, 4 ]` |
| dicts | `Map(2) { 'a' => 1, Items.Hay => 2 }`, `Map(0) {}` |
| sets | `Set(2) { 1, 2 }`, `Set(0) {}` |
| constants, directions | `Entities.Carrot`, `North` |
| functions | `[Function: canHarvest]`, `[Function (anonymous)]` for arrow functions |
| drones | `[Drone 2]` |

The differential prelude (`tests/JavaFarmer.Languages.Tests/Hooks/JsHooks.cs`, `JsPrelude`) reproduces this
format for the CPython run.

## 9. null and undefined

`null` and `undefined` are both the game's None. `x === null`, `x === undefined`, `x == null`
all test for None. Builtins that return nothing useful return null (`getEntityType()` on an empty tile,
`spawnDrone` when no drone is free, `m.get(k)` for a missing key).

## 10. Other code windows (imports)

Every other code window is another ES module, named by its window: `Utils.js`.

    // Utils.js (a library window)
    export let moves = 0;
    export const SIZE_LIMIT = 32;
    export function goTo(x, y) { … }
    function helper() { … }          // private: not exported

    // Main.js
    import * as Utils from "./Utils.js";
    import { goTo } from "./Utils.js";
    Utils.goTo(0, 0);
    goTo(1, 2);
    console.log(Utils.moves);

* `import * as M from "./M.js"` → the game's `import M`; `M.f()` / `M.count`. `import { f, g } from "./M.js"` →
  `import M` with `f` meaning `M.f`. The specifier may also be `"./M"` or `"./M.mjs"`.
* Only exported declarations are visible: `The requested module './Utils.js' does not provide an export named 'f'`
  (V8). Unknown window: `Cannot find module './Nope.js' …`. Imported bindings are read-only:
  `Assignment to constant variable.`. Imported functions are arity-checked (`Utils.f() expects 1 argument, but got 2`).
  When the compiler is not told about the other windows, any import is accepted.
* `import "./farm.js"`, `import { harvest } from "./farm.js"`, `import * as farm from "./farm.js"` (the stub) are
  accepted and have no effect in the game. Default imports, `export default` and npm packages are rejected.
* Other windows are parsed for their declarations only, cached by source text.

### Windows written in other languages

A window may be written in any JavaFarmer language (its file extension decides: `Utils.java`, `helper.py`,
`farm.rs`). Import it like a JavaScript window; the specifier may use its real extension or `.js`:

    import * as Utils from "./Utils.java";     // Java: public class Utils { static int twice(int n) { ... } }
    import { add, limit } from "./helper.py";  // Python: def add(a, b=1): ...   limit = 5
    console.log(Utils.twice(2), add(1), limit);

* Such a window is read with its own language's frontend (`ForeignModules.Resolve`, docs/LANGUAGE-GUIDE.md §9),
  never parsed as JavaScript. Every module-level function and variable is visible (no `export` needed: the game
  shares all module-level names); argument counts come from its declarations (optional parameters included).
* Its types are used for type-directed choices (`Utils.label() + 1` concatenates when `label()` returns a String).
* When it does not compile, the import reports `cannot use Utils: Utils (Java) does not compile (line 3: …)`.
* Other languages use a JavaScript window through `JavaScriptLanguage`'s `ISignatureProvider`: its top-level
  functions and variables (bodies and imports are not checked), names as the JavaScript window's compiled Python
  defines them (`goTo` stays `goTo`), types unknown (Java sees `Object`).

## 11. Rejected constructs

Each gives one clear message, usually `X is not supported in JavaFarmer`: try/catch (`try/catch is not supported in JavaFarmer`),
`throw`, classes, `this`, `new` of anything but Map / Set / Array, generators / `yield`, async / await,
labels, `with` (V8: `Strict mode code may not include a with statement`), the `delete` operator, `typeof`,
`instanceof`, bitwise operators `& | ^ ~ << >> >>>` (`the bitwise operator '&' is not supported in JavaFarmer`),
optional chaining `?.`, getters / setters, methods in object literals, object spread, spread in calls, rest
parameters, object destructuring, destructuring parameters, logical assignment `||= &&= ??=`, regular expression
literals, BigInt literals, the comma operator, named function expressions, default imports, `export default`,
identifiers starting with `__j` (`identifiers starting with __j are reserved by JavaFarmer`).

## 12. Deviations from real JavaScript

* **No type coercion.** `==` means `===`: `1 == "1"` is false, `null == undefined` is true, and so is
  `null === undefined` (both are None).
* **Text conversion is the game's `str()`.** `"x" + n`, templates and `String(x)` give the same text as JS for
  numbers and strings, but the game's spelling for other values (booleans and null as the game writes them, lists in
  the game's list format) where JavaScript would write `true`, `null`, `1,2`.
* **Truthiness follows the game:** an empty array, Map, Set or object is falsy in the game but truthy in JavaScript.
  Write `a.length > 0` / `m.size > 0` to be clear. `0`, `""`, null are falsy in both.
* **Missing keys:** `o.missing` / `o[k]` on a plain object is a game error, not `undefined`. Use
  `k in o` first, or a Map with `m.get(k)` (null when missing) or `m.get(k) ?? d`.
* **Array keys compare by value:** an array literal (or a read-only `const` holding one) used as a Map key or Set item is a game tuple, so
  `s.has([1, 2])` finds an equal position (JavaScript compares arrays by identity).
* **Set iteration order** is the game's (hash) order, not insertion order. Map order is insertion order in both.
* **Numbers** are doubles in both (no integer overflow). Division by zero is a game error (JavaScript gives Infinity).
* **`var` hoisting is simplified:** reading a `var` before it is assigned is a game error.
* **Argument counts are checked** for builtins and function declarations (JavaScript ignores extra arguments and makes
  missing ones `undefined`).
* `console.log` prints through the game's `print` (shown above the drone too).

## 13. Unlock mapping (SPEC §7)

Every construct costs the unlock of the Python the player would write; scaffolding JavaFarmer invents is Synthetic
(free). Checked by `tests/JavaFarmer.Languages.Tests/JavaScript/JsUnlockFairnessTests.cs`.

| JavaScript | costs |
|---|---|
| counting `for (let i = 0; i < n; i++)` | `for` (a range loop) |
| other classic `for` | a `while` whose header carries DependencyOverride `for`; condition and update are structural |
| `for...of`, `for...in` | `for` |
| `while`, `do … while` | `while` (the `True` and the exit test of do-while are structural) |
| `function`, arrow / function expressions | `def` (functions) |
| `switch` | `if / elif / else`; the generated `==` comparisons are synthetic |
| template literal | `+` (operators); the `str()` around substitutions is synthetic |
| `console.log()` | `print`; the generated empty string is synthetic |
| `import` | `import` |
| `[...a]`, `new Set(a)`, collection methods | the Python method or builtin they become |
| helper functions (`__jarr`, `__jmod`, `__jget`, ...) | their definitions are synthetic; `Math.trunc`'s call is synthetic |

## 14. Stub file (`farm.js`)

`ApiStubFile` returns `farm.js`: an ES module that exports every GameApi builtin (registered extension builtins
included) as a JSDoc-typed camelCase function stub, the constant containers `Entities`, `Items`, `Grounds`,
`Unlocks`, `Hats`, `Leaderboards` (Proxy objects: any member name works; members print as
`Entities.Carrot`), `North` / `East` / `South` / `West` and `quickPrint` (writes with
`console.log`); it also installs all of them on `globalThis`. So `import "./farm.js";` makes a JavaFarmer program
run in node unchanged (the differential runner prepends exactly that line). `print`, `min`, `max`, `abs`,
`random` are not exported: use `console.log` and `Math`.

## 15. Printer (IR from any language → JavaScript)

* Top-level statements; Java's `main` body becomes top-level code. Functions → `function` declarations; nested
  Python defs → nested `function`s; lambdas → arrow functions.
* `const` for variables that are never reassigned, `let` otherwise; `===` / `!==`; `for...of`;
  range loops → classic `for`; Python `//` → `Math.floor(a / b)`; Python `%` → `a % b` where the sign
  cannot matter (loop counters, `a % 2 === 0` tests), else `((a % b) + b) % b`; `str(x)` → `String(x)`.
* Python dicts → `Map` (keys may be game constants), sets → `Set`, tuples → arrays, `[0] * n` →
  `new Array(n).fill(0)`, `a + [1]` → `[...a, 1]`, `a.remove(x)` → `a.splice(a.indexOf(x), 1)`.
  Plain objects written in JavaScript stay objects.
* Imports → `import * as M from "./M.js";` / `import { f } from "./M.js";`. `export`: kept from JavaScript
  source; for IR from other languages every top-level function and global is exported when other windows exist
  (`PrintOptions.OtherFiles` non-empty).
* snake_case names from other languages become camelCase; names that are JavaScript reserved words or well-known
  globals (`new`, `function`, `default`, `in`, `Map`, ...) get a `_` suffix, consistently.
* Comments are kept. Constructs JavaScript cannot express are listed in `IPrinterDiagnostics` problems.

## 16. Tests

Corpus `tests/corpus/js/`: `valid/` (farm programs, `valid/multi/` is a multi-window program), `diff/`
(pure computation, run in node and in CPython), `invalid/` (`// ERROR:` / `// AT:` markers),
`docs-exceptions.txt`. Unit tests: `tests/JavaFarmer.Languages.Tests/JavaScript/` (syntax, lowering, printer, language surface, unlock fairness, cross-language imports, fuzz and the 300-line performance test: p25 < 5 ms, or on a loaded machine at most 1.5x the Java frontend measured at the same moments). Hooks:
`tests/JavaFarmer.Languages.Tests/Hooks/JsHooks.cs` (conformance `node --check` as ES modules with a
`package.json` `{"type": "module"}`; runner = node with `farm.js` imported first).

## Content cheat sheet

How to write each docs concept in JavaScript (for the docs content):

* **Program start:** the file runs from the top; no `main` needed. A forever loop is `while (true) { … }`.
* **Statements:** end with `;` (optional, but write them). Blocks use `{ }`; `if (cond) { … } else if (…) { … } else { … }`;
  `while (cond) { … }`; `for (let i = 0; i < n; i++) { … }` for "for i in range(n)";
  `for (let i = a; i < b; i += step)` for `range(a, b, step)`; `for (const x of list) { … }` for "for x in list";
  `break;`, `continue;`. Comments `// …` and `/* … */`.
* **Variables:** `let x = 0;` (changes later), `const size = getWorldSize();` (never reassigned).
* **Functions:** `function plantRow(length) { … return value; }`; call `plantRow(5);`. Defaults: `function f(a, b = 2)`.
  Top-level variables can be read and changed inside functions (no `global` statement).
* **Operators:** `===` / `!==` for "==" / "!=", `&&` / `||` / `!` for and / or / not, `Math.floor(a / b)`
  for `a // b`, `a % b`, `a ** b`, `x += 1` or `x++`.
* **Arrays (lists):** `const list = [1, 2, 3];`, `list.push(4);`, `list.pop()`, `list.length`, `list[0]`,
  `list.includes(2)`, `list.indexOf(2)`, `list.splice(i, 1);` (remove at), `list.splice(i, 0, x);` (insert),
  `[...list]` (copy), `new Array(n).fill(0)`.
* **Maps (dicts):** `const costs = new Map();`, `costs.set(Items.Hay, 5);`, `costs.get(Items.Hay)`,
  `costs.has(key)`, `costs.delete(key);`, `costs.size`, `for (const [key, value] of costs) { … }`,
  `for (const key of costs.keys())`, `new Map([[Items.Hay, 5], [Items.Wood, 2]])`. `getCost(…)` returns a Map.
* **Sets:** `const seen = new Set();`, `seen.add(x);`, `seen.has(x)`, `seen.delete(x);`, `seen.size`,
  `for (const x of seen)`.
* **Positions / tuples:** arrays: `const pos = [getPosX(), getPosY()];`, `const [x, y] = pos;`, `pos[0]`.
  `measure()` on some plants and `getCompanion()` return such arrays (or null): `const [plant, [x, y]] = getCompanion();`.
  An array written directly in the call (`seen.add([x, y])`) or kept in a `const` that is only read
  (`const here = [x, y]; seen.add(here);`) works as a Map key / Set item, compared by value. Arrays that are changed,
  kept in `let`, or returned by your own functions are game lists and cannot be keys.
* **Other files (imports):** library window `Utils.js` with `export function goTo(x, y) { … }` and
  `export let count = 0;`; use it with `import * as Utils from "./Utils.js";` then `Utils.goTo(1, 2)`, or
  `import { goTo } from "./Utils.js";`. Imports go at the top of the file. A window in another language is imported the same way with its own extension: `import * as Utils from "./Utils.py";`.
* **spawn_drone:** `spawnDrone(worker);` with `function worker() { … }`, or `spawnDrone(() => { … });`; it returns
  the drone or `null`: `const drone = spawnDrone(task); if (drone !== null) waitFor(drone);`.
* **Printing:** `console.log("hay:", numItems(Items.Hay));` (= print), `quickPrint(x)`; text with templates
  `` `x = ${x}` `` or `"x = " + x`.
* **Constants:** `Entities.Carrot`, `Items.Hay`, `Grounds.Soil`, `Unlocks.Speed`, `Hats.Straw_Hat`,
  `Leaderboards.…`; directions `North`, `East`, `South`, `West`. API names are camelCase
  (`canHarvest()`, `getEntityType()`, `doAFlip()`); `min` / `max` / `abs` / `random` are
  `Math.min` / `Math.max` / `Math.abs` / `Math.random()`.
* **None:** write `null`. "is None" is `=== null`, "is not None" is `!== null`
  (`if (getEntityType() === null)`). `undefined` means the same thing in JavaFarmer, but prefer `null` in docs.
