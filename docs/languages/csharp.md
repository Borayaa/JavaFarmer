# C# in JavaFarmer (id `csharp`)

C# 12 (.NET 8 syntax) for The Farmer Was Replaced. Everything a valid JavaFarmer C# program contains is real C#: the
conformance tests compile every valid corpus program with Roslyn against the stub `Farm.cs` (see "Stub file").
Frontend: `src/JavaFarmer.Compiler/Languages/CSharp/` (CsLexer → CsParser → CsBinder (checker + IR lowering) →
shared IrEmitter), printer `CsPrinter` (IR → C#), `CSharpLanguage` (ILanguage).

## 1. Program forms

**A. Top-level statements (default, the new-window template).** The file is the program: statements run from top to
bottom. Helper functions are *local functions* declared anywhere at the top level (they may be called before their
declaration, as in C#); they can read and assign the top-level variables declared above them.

    int harvested = 0;
    while (true)
    {
        if (CanHarvest())
        {
            Harvest();
            harvested++;
        }
        Move(North);
    }

    void PlantRow() { ... }

**B. One class with static members.** `class Program { static ... static void Main() { } }` (also
`static void Main(string[] args)`) is a program; the class may have any name. A file without `Main` is a *library*:
`public static class Utils { public static int Twice(int x) => x * 2; }` — the class must be named like its window
(`Utils`), and other windows call `Utils.Twice(3)`, `Utils.Count`. Members other files use must be `public` (or
`internal`), exactly like C# (CS0122 otherwise). Only static members: fields (`static`, `const`, `static readonly`)
and methods (block or expression bodied `=> expr;`).

A window with top-level statements cannot be used by other windows (it has no class name); every window with
top-level statements is its own program (the game runs one window at a time). Real C# allows only one such file per
project, so the conformance tests compile each program with its library windows only.

Usings: `using System;`, `using System.Collections.Generic;`, `using static Farm;` and `using static System.Math;`
are accepted (and optional: the stub declares them as global usings). Everything else (namespaces, other usings,
`#` directives, attributes) is rejected with "… is not supported in JavaFarmer".

## 2. API naming

Game builtins are static methods of class `Farm`, called without prefix thanks to `global using static Farm;`
(`Farm.Harvest()` also works). Names are PascalCase of the game name (`GameApi.ToStyle(name, Pascal)`); registered
extension builtins appear automatically (`scan_port` → `ScanPort`).

| game (Python) | C# |
|---|---|
| `harvest()` / `can_harvest()` | `Harvest()` / `CanHarvest()` |
| `plant(Entities.Carrot)` | `Plant(Entities.Carrot)` |
| `move(North)` | `Move(North)` or `Move(Direction.North)` |
| `get_pos_x()`, `get_world_size()` | `GetPosX()`, `GetWorldSize()` (int) |
| `get_entity_type()` | `GetEntityType()` → `Entity?` |
| `use_item(Items.Water, 2)` | `UseItem(Items.Water, 2)` (optional parameters are overloads) |
| `num_items(Items.Hay)` | `NumItems(Items.Hay)` → double |
| `get_cost(x)` | `GetCost(x)` → `Dictionary<Item, double>?` |
| `measure()` / `wait_for(d)` | `Measure()` / `WaitFor(d)` → `object?` (cast: `(int)Measure()`, `var (x, y) = (Position)Measure();`) |
| `get_companion()` | `GetCompanion()` → `Companion?` (`.Entity`, `.Position`, `.X`, `.Y`; `var (plant, (x, y)) = companion;`) |
| `spawn_drone(f)` | `SpawnDrone(Work)`, `SpawnDrone(() => Work(3))`, `SpawnDrone(() => { ... })` → `Drone?` |
| `quick_print(a, b)` | `QuickPrint(a, b)` |
| `random()` | `Random()` (double in [0, 1); `System.Random` is not supported) |
| `print(x)` | `Console.WriteLine(x)`; `Console.Write(x)` collects text until the next `Console.WriteLine` |
| `min(a, b)` / `max` / `abs` | `Math.Min(a, b)` / `Math.Max` / `Math.Abs` (int when all arguments are int) |
| `str(x)` | `x.ToString()`, `$"{x}"`, or `"text" + x` |
| `len(x)` | `list.Count`, `set.Count`, `dict.Count`, `array.Length`, `text.Length` |

ILanguage.ApiName / FromApiName: `can_harvest` ↔ `CanHarvest`, `print` ↔ `Console.WriteLine` (also accepts
`Console.Write`), `min` ↔ `Math.Min` (also `Min`), `Farm.Harvest` → `harvest`.

Constants keep the game's names: `Entities.Carrot`, `Items.Hay`, `Grounds.Soil`, `Unlocks.Speed`, `Hats.…`,
`Leaderboards.…` (any member name is accepted; the stub declares them as `static readonly` fields of partial static
classes, so they are *not* compile-time constants), directions `North`, `East`, `South`, `West` (consts of
`Farm`) and `Direction.North` (`enum Direction`).

## 3. Types, variables, operators

* `int`, `double`, `bool`, `string`, `char` (a one-character string in the game), `object`, `var`, `const`
  locals/fields. The game has one number type: `int` vs `double` exists for checking only. `int` division and `%`
  follow C# (truncation toward zero, remainder with the dividend's sign); `(int)x` truncates. Integer overflow is not
  emulated (deviation).
* Implicit int → double; double → int needs a cast (CS0266). `x += 1.5` on an int is CS0266 like C#.
* `long`, `float`, `decimal`, `uint`, `byte`, `short` … and nullable value types (`int?`) are rejected.
* Game types: `Entity`, `Item`, `Ground`, `Unlock`, `Hat`, `Leaderboard`, `Drone` (sealed classes), `Direction`
  (enum), `Position` / `Companion` (records). `Entity?` etc. are accepted (nullable reference annotations).
* Operators: `+ - * / %`, `++ --` (statements only), `= += -= *= /= %=`, `== != < <= > >=`, `&& || !`, `?:`,
  `is null` / `is not null` / `x is 5`. String `+` concatenates (any value); interpolation `$"x = {x}"` (no
  format specifiers / alignment). Rejected: bitwise and shift operators, `??`, `?.`, `!` (null-forgiving), `as`,
  type patterns, relational patterns, assignments / `++` used as values.

## 4. Statements

`if / else if / else`, `while`, `do … while`, `for`, `foreach` (arrays, lists, sets, strings, dictionary
`Keys` / `Values`, dictionaries as `foreach (var (key, value) in dict)` or `foreach (var kv in dict)` with
`kv.Key` / `kv.Value`), `break`, `continue`, `return`, `switch` statements (sections with `case 1:`,
stacked labels, `case 1 or 2:`, `default:`; every section ends with `break;`, `return`, or `continue` like C#
requires; a `break` in the middle of a section is not supported), switch expressions
`sel switch { 1 => "a", 2 or 3 => "b", _ => "c" }` as a whole value (variable initializer, assignment, return,
expression-bodied member), deconstruction `var (x, y) = pos;`, `(int a, int b) = (1, 2);`, `(a, b) = (b, a);`,
`foreach (var (a, b) in pairs)` over lists/arrays of tuples (like Python's `for a, b in pairs`).

Switch labels must be constants (C# rule): ints, strings, chars, `Direction.North` / `North`, const locals and
fields. `case Entities.Carrot:` is CS0150 in C# (game constants are fields, not constants): use `if / else if`.

Lambdas: `() => expr`, `() => { … }`, with parameters when the target type is known (`Func<int, int> f = x => x * 2;`).
Delegate types `Action`, `Action<…>`, `Func<…>`; method groups (`SpawnDrone(Work)`, `Action a = Work;`); calls
`f()` / `f.Invoke()`. Lambdas and local functions capture variables by reference (C# semantics = Python closures);
assigning a captured *method/block local* inside a lambda is rejected (use a top-level variable or a static field),
because the game's Python has no `nonlocal`.

## 5. Collections

| C# | IR / Python |
|---|---|
| `List<T>`: `new List<int>()`, `new() `, `new List<int> { 1, 2 }`, `[1, 2]`, `new List<int>(other)` | list |
| `xs.Add(x)`, `xs.Insert(i, x)`, `xs.RemoveAt(i)`, `xs.Remove(x)`, `xs.Contains(x)`, `xs.IndexOf(x)`, `xs.Clear()`, `xs.Count`, `xs[i]`, `xs[^1]` | append / insert / pop(i) / remove-if-present / in / index / clear / len / [i] / [-1] |
| `HashSet<T>`: `Add`, `Remove`, `Contains`, `Clear`, `Count` | set |
| `Dictionary<K, V>`: `d[k]`, `d[k] = v`, `d.Add(k, v)`, `ContainsKey`, `Remove`, `GetValueOrDefault(k[, d])`, `TryGetValue(k, out var v)` (only as the whole condition of an `if`, or negated), `Keys`, `Values`, `Count`, `Clear`, initializers `{ ["a"] = 1 }` / `{ { "a", 1 } }`, `[]` | dict |
| arrays `int[]`, `new int[n]`, `new int[] { 1, 2 }`, `[1, 2]`, `Length`; 2-D `int[,]`, `new int[w, h]`, `a[x, y]`, `GetLength(0)` / `GetLength(1)` | lists (`__jarr` / `__jarr2`) |
| value tuples `(1, 2)`, `t.Item1`, `(int, int)` | tuples |

`Add` / `Remove` results (bool) cannot be used as values ("… is not supported in JavaFarmer; call it as a separate
statement"). LINQ, `Sort`, `AddRange`, jagged arrays, `Queue`/`Stack`/`LinkedList`… are rejected.
`Dictionary.Add` of an existing key does not throw in the game (deviation). Comparing collections with `==` is
rejected (C# compares references, the game compares contents).

## 6. Printing and values (ILanguage.FormatValue)

`Console.WriteLine(x)` prints one value; `Console.Write` text is buffered (a synthetic module variable `__jcout`) and
printed with the next `Console.WriteLine` of the same window (`Console.WriteLine(x)` after a `Console.Write` lowers to
`__jcout += str(x); print(__jcout); __jcout = ""`; the buffer lines are synthetic, only `print` costs Debug). Composite format strings
(`Console.WriteLine("{0}", x)`) are rejected: use `$"…"`.

FormatValue (print / hover): `True` / `False`, numbers like `double.ToString()` (`3`, `2.5`,
`0.30000000000000004`), strings as is, `null` (deviation: C# prints an empty line), lists / sets
`[1, 2, 3]` and dictionaries `{"a": 1}` (deviation: C# prints the type name), tuples `(1, 2)`, constants
`Entities.Carrot` / `North`, drones `Drone 3`, functions by C# name.

## 7. Rejected on purpose ("… is not supported in JavaFarmer")

Instances (`this`, constructors, instance fields/methods, `new Position(…)`), structs/records/interfaces/enums/
generics declared by the user, properties, nested types, namespaces, `try/catch/throw`, `goto`, labels, `lock`,
`using` statements, `async/await`, `yield`, LINQ, `ref/out/in/params` parameters (only `out` of `TryGetValue`),
named arguments, method overloading, local functions inside methods/blocks, `#` directives, verbatim identifiers,
raw string literals, line breaks inside strings (the game has no escapes), identifiers starting with `__j`.
Python / Java habits get C# diagnostics: `print` → CS0103 "(use Console.WriteLine)", `list.Length` → CS1061 "(use
Count)", `True` → CS0103 "(C# writes true, false and null)".

## 8. Deviations from real C# (documented)

* No overflow emulation; `Math.Round` rounds halves up (the game's round), not to even; `Math.Floor`/`Ceiling`/
  `Round`/`Sqrt`/`Pow` are typed `double` as in C#.
* No null checks: calling a method on null fails at run time in the game, as in C#, but with the game's message.
* `Dictionary.Add` / indexer reads of missing keys follow the game (Add replaces; a missing key is an execution error).
* `foreach` iteration variables captured by a lambda see the variable, not a per-iteration copy (Python closures).
* Default parameter values must be constants (C#); named arguments are not supported.

## 9. Unlock mapping (fairness, SPEC §7)

Mandatory structure is free: `class Program {`, the `static void Main()` header (entry: scaffolding, NoHighlight),
`using` lines (no IR), library class headers. Constructs cost their Python twin:

| C# | unlock |
|---|---|
| `while`, `do/while`, `true`, `break`, `continue` | Loops |
| `if/else`, `switch` (statement and expression), `?:` | Speed (`if`) |
| `for` (classic, `foreach`) | Expand 2 (`for`; counting loops also `range`) |
| arithmetic, comparisons, `&& || !`, string `+` and `$"…"` | Operators |
| variables, assignments, `++` | Variables |
| local functions / methods, `return` | Functions |
| other windows (`Utils.F()`) | Imports |
| `List`, arrays, collection expressions | Lists |
| `Dictionary`, `HashSet` | Dictionaries (sets) |
| `Console.WriteLine` | Debug (`print`); `Console.Write` only fills the buffer (free; the text shows with WriteLine) |
| `Math.Min/Max/Abs` | Utilities |
| `x.ToString()`, `$"{x}"` alone | Debug (`str`); the str() inside concatenation / interpolation is synthetic |

## 10. Stub file (Farm.cs)

`CSharpLanguage.ApiStubFile` returns `Farm.cs`: global usings (`System`, `System.Collections.Generic`,
`static Farm`), the game types, `enum Direction`, records `Position` / `Companion`, partial static classes
`Entities`, `Items`, … (members from `CSharpLanguage.ConstantMembers` when the plugin sets it), and
`public static class Farm` with every builtin (unions and optional parameters as overloads, `params object?[]`
for variadic ones, `Action` / `Func<T>` overloads of `SpawnDrone`). `CSharpLanguage.BuildStub(sources)` adds the
constant members the given sources use (Roslyn conformance tests).

## 11. Files, conversion, content

* `FileExtension` is `.cs` (a window `main.cs` is C#; the module name is the file name without extension).
* Printer (`CsPrinter`): IR from any frontend → C#. Script modules (Python, JS, R, C# top-level) print as top-level
  statements with local functions after them; modules with an entry point (Java, C# class form) as
  `class Program { static … static void Main() }`; declaration-only modules as `public static class <File>` (the class
  is named exactly like the window, members `public` and keeping their source names, because other windows in any
  language call them by those names). Untyped sources are typed by `CsTypeGuess` (a copy of Java's
  inference). Constructs C# cannot express are reported through `IPrinterDiagnostics` (e.g. `dict.get()` of a missing
  number key, list `pop()` inside an expression, jagged arrays, tuple indexing with a computed index) so callers keep the
  original text. `x = xs.pop(i)` prints as two statements (`x = xs[i]; xs.RemoveAt(i);`).
* Content: `content/csharp/docs/**` (48 pages), `content/csharp/Strings/*.txt`, `content/csharp/unlock_display_code.txt`.

## 12. Cross-language imports (LANGUAGE-GUIDE §9)

* Another window written in another language (`CompileOptions.OtherFileLanguages`) is read with its own frontend through
  `ForeignModules.Resolve` and seen as a static class: `Utils.twice(3)`, `helpers.count` (the other file's spelling).
  Its privacy is not enforced. Types come from the signature; unknown / dynamic types (Python, JS, R) are `object?`
  (cast them: `(int)helpers.twice(3)`). A foreign file that does not compile gives "cannot use utils: utils (Python)
  does not compile (line …)" at the use site.
* `CSharpLanguage` implements `ISignatureProvider`: the static members of its class, or the local functions and
  top-level variables of a top-level-statements window (bodies are not checked, so files that import each other work).

## 13. Tests

* Shared harness rows for `csharp` (a)(b)(e)(f) + contract; (c)(d) run in-process with Roslyn:
  `tests/JavaFarmer.Languages.Tests/CSharp/CSharpRoslynTests.cs` (valid corpus and printed programs compile, diff
  programs print the same as CPython running the emitted Python).
* `tests/JavaFarmer.Languages.Tests/CSharp/`: language unit tests (diagnostics, printer, FormatValue, naming, fairness
  segments, fuzzing, 300-line compile < 5 ms) and the content docs test (every code block of `content/csharp/docs`).
* Docs exceptions: `tests/corpus/csharp/docs-exceptions.txt`.
