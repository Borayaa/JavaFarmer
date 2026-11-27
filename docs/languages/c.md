# C in JavaFarmer (id `c`)

C23 for The Farmer Was Replaced. Everything a valid JavaFarmer C program contains is real C: the conformance tests
compile every valid corpus program with `gcc -std=c23 -Wall -Werror -Wno-unused -fsyntax-only` against the stub
`farm.h`, and the differential tests build pure-computation programs with gcc, run them, and compare their output with
the game Python JavaFarmer emits (CPython).
Frontend (shared with C++): `src/JavaFarmer.Compiler/Languages/CFamily/` (Lexer → Parser → Binder = checker + IR
lowering → shared IrEmitter), printer `CPrinter` (IR → C), stub generator `FarmStub`, `CLanguage` (ILanguage,
`Languages/C/CLanguage.cs`). File extension `.c`.

## 1. A program

    #include "farm.h"
    #include <stdio.h>

    int harvested = 0;

    void harvest_column(void) {
    	for (int i = 0; i < get_world_size(); i++) {
    		if (can_harvest()) {
    			harvest();
    			harvested++;
    		}
    		move(North);
    	}
    }

    int main(void) {
    	while (true) {
    		harvest_column();
    		printf("harvested %d\n", harvested);
    		move(East);
    	}
    }

* Entry point: `int main(void)` (also `int main()`, which C23 reads the same way). `void main` and `main` with
  parameters are rejected (gcc rejects `void main` with `-Wall -Werror`). A final `return 0;` is dropped; any
  other `return` in main just ends the program (the exit status means nothing in the game).
* A window without `main` is a library (see §8).
* `#include "farm.h"` declares the game API and is needed in every window that uses it. Standard headers
  `<stdio.h> <stdbool.h> <math.h> <string.h> <stdlib.h> <stddef.h>` are accepted (free); JavaFarmer does not
  require them, but real C does (`printf` needs `<stdio.h>`, `strlen` `<string.h>` ...), and the compiler says so.
  Any other include is rejected (`#include "other.c"` too: see §8).
* `#define NAME value` with a number, string or bool constant: a module constant. Function-like macros, `#if` and
  other directives are rejected; `#pragma` is ignored.
* The new-window template is `#include "farm.h"\n\nint main(void) {\n\t\n}\n` (caret inside main).

## 2. Game API (`farm.h`)

Names are the game's own (snake_case), so `ILanguage.ApiName` is the identity except `print` → `printf`.
Registered extension builtins appear automatically under their game name.

| game | C | notes |
|---|---|---|
| `harvest()`, `can_harvest()`, `plant(e)`, `till()`, `move(d)`, `can_move(d)`, `swap(d)` ... | same | `bool` results |
| `get_pos_x()`, `get_world_size()`, `num_unlocked(x)`, `num_drones()` | same | `int` |
| `get_time()`, `num_items(i)`, `get_water()` | same | `double` |
| `use_item(i)`, `use_item(i, n)`, `get_cost(x)`, `get_cost(x, level)` | same | optional parameters are a macro over two functions |
| `get_entity_type()` | same | `Entity`, `NULL` when the tile is empty |
| `get_cost(x)` | same | `Map(Item, double)`, `NULL` at max level |
| `get_companion()` | same | `Companion` (`c->entity`, `c->position`, `c->x`, `c->y`), `NULL` when none |
| `measure()`, `measure(d)` | same | `double` (petals, size, number) |
| — | `measure_pos()`, `measure_pos(d)` | the same game call when the result is a position (treasure hint, apple): `Position` |
| `spawn_drone(f)` | `spawn_drone(f)` with `void f(void)` (a function name, `&f` or a `void (*)(void)` variable) | `Drone`, `NULL` when no drone is free |
| `wait_for(d)`, `has_finished(d)` | same | `wait_for` returns nothing (drone functions are `void` in C) |
| `print(a, b)` | `printf(...)`, `puts(s)` | see §6 |
| `quick_print(a)` | `quick_print(format, ...)` | printf-style, one line, output page only |
| `min`, `max`, `abs` | `min(a, b)`, `max(a, b)`, `abs(x)` | `farm.h` macros (int when both arguments are int); `fmin` / `fmax` / `fabs` from `<math.h>` too |
| `random()` | `random()` | `double` in [0, 1) |
| `simulate(...)` | same | `sim_unlocks` is `Map(Unlock, int)`, `sim_globals` `Map(string, int)` |

Constants keep the game names inside a constant struct: `Entities.Carrot`, `Items.Hay`, `Grounds.Soil`,
`Unlocks.Speed`, `Hats.Straw_Hat`, `Leaderboards.Hay`; directions are plain names `North`, `East`, `South`,
`West`. Any member name compiles (the game decides which exist); `farm.h` lists the current ones.
Game values are handles (`Entity`, `Item`, `Ground`, `Hat`, `Unlock`, `Leaderboard`, `Direction`, `Drone`):
compare them with `==`, test them against `NULL`, store them in variables and collections.
`Position` is `struct { int x; int y; }`: `p.x`, `p.y`, `(Position){1, 2}`.

## 3. Types and variables

`int` (`long` is accepted as `int`), `double` (`float` = `double`), `bool` (`true`/`false`, `<stdbool.h>` not
needed in C23), text `const char *` / `char *` / `string` (a `farm.h` typedef), the game handles, `Position`,
`Companion`, `List(T)` / `Set(T)` / `Map(K, V)`, function pointers `void (*f)(void)`, `int (*g)(int)`.
`const` and `constexpr` variables; `static` functions and globals (private to the window).
Arrays: `int a[5];`, `int a[] = {1, 2, 3};`, `int g[8][8];`, `bool seen[n];` (local variable-length arrays),
`int a[5] = {1, 2};` (the rest is 0), `= {0}`; `sizeof(a) / sizeof(a[0])` is the length (also rejected on an array
parameter, like gcc warns). Locals declared without a value start at 0 / `false` / `NULL` (real C would leave them
undefined: initialise them). File-scope initialisers must be constants (gcc rule).
Rejected with "… is not supported in JavaFarmer": `char` (no characters in the game), `unsigned`, `short`,
`long long`, pointers other than `char *` and function pointers, `struct` / `union` / `enum` / `typedef`,
`malloc` / `free`, `goto` and labels, `static` locals, bitwise operators, the comma operator, character
constants, `++` / `--` / assignments used as values, `sizeof` (except the array length), designated initialisers.

## 4. Statements and expressions

`if` / `else`, `while`, `do ... while`, `for` (any form), `for_each (T x, coll)` (`farm.h`), `switch` over `int`,
`break`, `continue`, `return`, blocks. A `switch` case ends with `break;` (dropped), `return` or `continue`;
several labels may share a body (`case 1: case 2:`), falling through into a non-empty case is rejected, and a
`break` inside a case must be its last statement. `?:` is allowed as a whole value (declaration, assignment,
return, argument). Conditions may be numbers, handles or pointers (`if (n)`, `if (spawn_drone(f))`): JavaFarmer
adds the `!= 0` / `!= NULL` (free).
Numbers: `int / int` truncates toward zero and `%` keeps the dividend's sign (`-7 / 2 == -3`, `-7 % 2 == -1`), like C;
`%` on doubles is rejected (use `fmod`). `(int) x` truncates. `<math.h>`: `floor`, `ceil`, `round`, `sqrt`, `pow`,
`fmod`, `fabs`, `fmin`, `fmax`.

## 5. Strings

String literals (no line breaks inside, except in `printf` / `puts` text), `str_concat(a, b, ...)` (joins),
`str_of(x)` (any value as text, formatted like the game), `str_eq(a, b)`, `str_contains(s, part)`, `strlen(s)`,
`strcmp(a, b) == 0` / `!= 0`. `a == b` on strings is rejected (it compares addresses in real C). Indexing a string
is not supported (the game has no characters). `str_of` of a comparison is rejected: in C a comparison is an `int`
(1/0), the game prints `true`/`false` — store it in a `bool` first.

## 6. Printing

`printf(format, ...)` with `%d %i %ld %f %g %s %%` (precision `%.2f` accepted; flags and widths are rejected), `puts(s)`,
`quick_print(format, ...)`. Arguments are type-checked like `gcc -Wformat` (`%d` needs an `int`, `%s` a string —
use `str_of(x)` for anything else). The game's `print` writes one line per call, so JavaFarmer prints one game line
per finished `\n`: text without a final `\n` is kept (in a hidden module variable `__jout`) until a later `printf`
finishes the line.
Deviation: numbers are printed the way the game prints them (`3.5`, `2`, at most 2 decimals), so `%f` / `%.3f` do not
pad (`printf("%f", 1.5)` shows `1.5`, real C `1.500000`); `%g` matches real C for short numbers.

## 7. Collections (`farm.h`)

Real C has no generic containers, so `farm.h` defines one type per element type (`int`, `double`, `bool`, `string`,
`Position` and the game handles; map keys the same except `bool`) and `_Generic` macros that pick the right function.
They are handles: assignment shares the collection (like the game's lists), `*_copy` makes a copy.

| | list | set | map |
|---|---|---|---|
| type | `List(int)` | `Set(Entity)` | `Map(Item, double)` |
| new | `list_new(int)`, `list_of(int, 1, 2, 3)` | `set_new(T)`, `set_of(T, a, b)` | `map_new(K, V)`, `map_of(K, V, {k1, v1}, {k2, v2})` |
| add | `list_add(l, x)`, `list_insert(l, i, x)` | `set_add(s, x)` | `map_put(m, k, v)` |
| read | `list_get(l, i)` (negative i counts from the end) | `set_contains(s, x)` | `map_get(m, k)` (error if missing), `map_get_or(m, k, d)`, `map_has(m, k)` |
| write | `list_set(l, i, x)` | | `map_put` |
| remove | `list_pop(l)`, `list_remove_at(l, i)` (return the element), `list_remove(l, x)` (error if missing) | `set_remove(s, x)` | `map_remove(m, k)` (returns the value) |
| size | `list_len(l)`, `list_is_empty(l)` | `set_len`, `set_is_empty` | `map_len`, `map_is_empty` |
| other | `list_contains`, `list_index_of` (-1 if missing), `list_clear`, `list_copy` | `set_clear`, `set_copy` | `map_keys` / `map_values` (lists), `map_clear`, `map_copy` |
| loop | `for_each (int x, l) { }` | `for_each (T x, s)` | `for_each (K k, m)` (keys, in insertion order) |

`xs[i]` on a List is rejected with a hint (`list_get`). Lists of lists are not supported (use a 2D array).

## 8. Other code windows

Each window is a file; the game runs one program window and imports the others. Use another window's functions and
globals by declaring them, exactly like separate C files linked together:

    void plant_row(Entity e);   // defined in the window "farming"
    extern int rows_done;

The declaration is checked against the other window (`conflicting types` when they differ, `undefined reference`
when no window defines it). `static` functions and globals stay private. `#include "farming.c"` is rejected.
Unlock: Imports (the emitter adds `import farming`).

## 9. Unlock fairness (SPEC §7)

| construct | costs the unlock of | why |
|---|---|---|
| `#include`, `int main(...) {`, the final `return 0;`, prototypes, `extern` declarations | nothing | mandatory scaffolding: no IR node, or the entry header (emitted as the Synthetic `def main():` + `__name__` guard) |
| `while`, `do ... while`, `for (;;)`, `true`, `false`, `break`, `continue` | Loops | IrWhile / IrDoWhile / IrLoop |
| `if`, `else`, `switch`, `?:` | Speed ("if") | IrIf / IrSwitch / IrConditional |
| `for (int i = 0; i < n; i++)` and every other classic for; range-for / `for_each` | Expand 2 ("for"; counting loops also "range") | IrClassicFor (becomes `range()` or a while with DependencyOverride "for", header Structural), IrForEach |
| arithmetic, comparisons, `&&`, `||`, `!` | Operators | IrBinary / IrUnary (the `!= 0` / `!= NULL` that makes a C condition explicit is Synthetic: free) |
| variables, assignments, `++`, `+=` | Variables | IrVarDecl / IrAssign |
| your own functions, `return` | Functions | IrFunction / IrReturn |
| functions / globals of another code window | Imports | External symbols (the emitter adds `import Window`) |
| arrays, lists | Lists | IrListLit / ArrayNew |
| maps | Dictionaries | IrMapLit |
| sets | Dictionaries ("sets") | IrSetLit |
| output (`printf`, `puts`, `std::cout`, `std::println`) | Debug ("print") | one game `print` per finished line; the `str()` / `+` the lowering adds are Synthetic |
| `quick_print` | quick_print (free in the game) | |
| `min`, `max`, `abs`, `random` | Utilities | game builtins |
| `str_of`, `std::to_string` | Debug ("str") | IrCast(String) |

## 10. Deviations from real C (documented)

* Numbers: the game has one number type (double). `int` arithmetic is exact up to 2^53, never overflows or wraps;
  `long` = `int`, `float` = `double`.
* Uninitialised locals start at 0 / `false` / `NULL` instead of being undefined.
* Doubles print like the game (§6).
* `farm.h` never frees memory (strings from `str_concat` / `str_of`, collections): irrelevant in the game, a leak
  in a real build.
* `wait_for` returns nothing; drone functions are `void`.
* Closures do not exist in C: `spawn_drone` takes a plain function, so a drone cannot receive a parameter (use a
  global). Docs examples that need closures stay in Python (`tests/corpus/c/docs-exceptions.txt`).

## 11. Values (FormatValue)

`NULL`, `true` / `false`, numbers like the game, lists and sets `{1, 2}`, maps `{"a": 1}` (nested strings quoted),
positions `{3, 4}`, constants `Entities.Carrot`, directions `North`, drones `Drone 2`.

## 12. Stub file

`ILanguage.ApiStubFile` returns `farm.h` (header-only, generated by `FarmStub.CHeader` from `GameApi`, so registered
builtins are included). It is real C23: game functions are stubs that do nothing outside the game, while the
collections, strings and `quick_print` work, so `gcc -std=c23 main.c -lm` builds and runs a program that only
computes and prints. `CFamilyLanguage.ApiStubFor(sources)` adds the constant members the given sources use.

## 13. Tests

`tests/corpus/c/{valid,diff,invalid}` (harness checks (a)–(f), `Hooks/CHooks.cs`), `tests/corpus/c/docs-exceptions.txt`,
`tests/JavaFarmer.Languages.Tests/CFamily/` (language unit tests, content docs blocks, performance, fuzzing).
