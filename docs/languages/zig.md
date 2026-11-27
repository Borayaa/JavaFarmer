# Zig in JavaFarmer

Language id `zig`, display name "Zig", file extension `.zig` (class `ZigLanguage`,
src/JavaFarmer.Compiler/Languages/Zig/). The player writes real Zig 0.17 (the subset below). JavaFarmer checks it
like the Zig compiler would (AstGen + Sema rules, Zig's error wording) and translates it to the game's Python through
the shared IR. The stub file `farm.zig` lets an external editor and the real compiler check the same program.

A code file's language is its file extension (`main.zig`, `bot.py`, `Utils.java` ...). The Mods-menu language
only picks the default for new files and docs; code is never converted automatically.

## Entry point and imports

```zig
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
	if (farm.canHarvest()) {
		_ = farm.harvest();
	}
}
```

* `pub fn main() void` (or `!void` when the body uses `try`) is the entry point. Its header is scaffolding: it
  becomes `def main():` plus the `if __name__ == "__main__":` guard, both Synthetic and free (no unlock).
  Real Zig also allows `noreturn`, `u8` and `!u8`; JavaFarmer accepts `void` / `!void` and says so otherwise.
  `main` must be `pub` (Zig: "'main' is not marked 'pub'"); parameters are not supported.
* `@import("std")` and `@import("farm")` have no IR node (free). `farm` is a module, so the real compiler needs
  `zig build-exe --dep farm -Mroot=main.zig -Mfarm=farm.zig`.
* Other files: `const utils = @import("utils.zig");` then `utils.goTo(0, 0)`, `utils.moves += 1`. The module name
  is the file name without extension; the import costs the Imports unlock (`import utils`). The other file's
  functions / variables must be `pub` (Zig rule).
* Files in other languages (docs/LANGUAGE-GUIDE.md §9): `@import("Utils.java")`, `@import("bot.py")` or just
  `@import("bot")`. Their declarations come from that language's frontend (ForeignModules); privacy of the other
  language is not enforced. Java/C#/Rust types map to Zig types (int → i32, double → f64, boolean → bool, String →
  []const u8, lists → std.ArrayList(T) ...). Values of dynamic languages (Python, JS, R) have no Zig type: they
  coerce to whatever they are used as, so a declaration needs a type (`const v: i32 = bot.pick(2);`; otherwise
  "this value comes from a file in another language and has no Zig type"). A foreign file that does not compile is
  reported at the import ("cannot use bot: bot (Python) does not compile (line 1: ...)").

## API naming

Game builtins are functions of the `farm` module in camelCase (ApiName returns the bare name, e.g. `canHarvest`;
FromApiName also accepts `farm.canHarvest`). Builtins registered at runtime (GameApi.Register, mods) appear
automatically, with the same rules.

| game Python | Zig | notes |
|---|---|---|
| `harvest()`, `can_harvest()`, `plant(e)`, `till()` ... | `farm.harvest()`, `farm.canHarvest()`, `farm.plant(.Carrot)`, `farm.till()` | snake_case → camelCase |
| `print(a, b)` | `std.debug.print("{d} {s}\n", .{ a, b })` | ApiName "std.debug.print" |
| `quick_print(...)` | `farm.quickPrint("...\n", .{ ... })` | same format rules |
| `min`, `max`, `abs` | `@min`, `@max`, `@abs` | ApiName "@min" ... |
| `use_item(item, n)` | `farm.useItem(.Water)` / `farm.useItemN(.Fertilizer, 3)` | Zig has no default parameters: one function per arity, the optional parameters' names appended (PascalCase) |
| `get_cost(thing, level)` | `farm.getCost(x)` / `farm.getCostLevel(x, 2)` | |
| `measure()`, `measure(dir)` | `farm.measure(T)` / `farm.measureDirection(T, .East)` | the result type is the first argument: `farm.measure(i32)`, `farm.measure(?farm.Position)` |
| `wait_for(drone)` | `farm.waitFor(T, drone)` | likewise |
| `spawn_drone(f)` | `farm.spawnDrone(f)` | f: a function without parameters; returns `?farm.Drone` |

Result types: game ints that are never negative (`getPosX`, `getWorldSize`, `numDrones` ...) are `usize`;
`numItems`, `getTime`, `getWater`, `random` are `f64`; `getEntityType()` is `?farm.Entity`,
`getCompanion()` is `?farm.Companion`, `getCost()` is `?std.AutoHashMap(farm.Item, f64)`. Parameters that
take game ints are `usize`. Results that are not void must be used or discarded (`_ = farm.harvest();`).

## Constants

Game constant containers (plural in Python) are enums of the farm module (singular):
`Entities.Carrot` → `farm.Entity.Carrot`, `Items.Hay` → `farm.Item.Hay`, `Grounds` → `farm.Ground`,
`Unlocks` → `farm.Unlock`, `Hats` → `farm.Hat`, `Leaderboards` → `farm.Leaderboard`, `North` → `farm.Direction.North`.
Where the type is known (arguments, comparisons, switch prongs, typed declarations) the enum literal is idiomatic:
`farm.plant(.Carrot)`, `farm.move(.North)`, `e == .Bush`. Any member name builds an IrConst (names are never
validated against a list: the plugin knows them for completion). An untyped `const c = .Carrot;` is rejected
("an untyped enum literal is not supported").

## Types, variables and operators

* `const` / `var` with Zig's rules: a `var` that is never changed is an error ("local variable is never
  mutated"), unused locals / parameters / captures are errors ("unused local constant", "unused function
  parameter", "unused capture"), `_ = x;` after a use is "pointless discard". No shadowing of locals or top-level
  names. Identifiers starting with `__j` are reserved by JavaFarmer.
* Types: `i32`, `i64`, `u32`, `usize` (any iN / uN), `f64` / `f32`, `bool`, `[]const u8` (strings), arrays
  `[N]T` / `[_]T{...}`, slices `[]const T`, optionals `?T`, `std.ArrayList(T)`, `std.AutoHashMap(K, V)`,
  `farm.Position` / `farm.Companion` / `farm.Drone`. `var x = 0;` is "variable of type 'comptime_int' must be
  const or comptime" (give it a type).
* Coercion like Zig: integers widen implicitly, narrowing needs `@intCast` (with a result type), `@floatFromInt`,
  `@intFromFloat`, `@as(T, x)`; `usize + i32` is "incompatible types: 'usize' and 'i32'"; comparisons of
  different integer types are allowed (Zig compares them exactly).
* Division: `/` and `%` on signed integers are errors ("division with 'i32' and 'comptime_int': signed integers
  must use @divTrunc, @divFloor, or @divExact"); use `@divTrunc` (IntDivTrunc), `@divFloor` (Python `//`),
  `@rem` (ModTrunc) and `@mod` (Python `%`). Unsigned `/` and `%` map directly; `/` on floats is true division.
  Comptime-known operands are allowed like in Zig.
* `and`, `or`, `!`, comparisons, `+ - *`, compound assignments. Bitwise / wrapping / saturating operators,
  `**` and `++` at runtime are not supported.

## Values (what the frontend adds so that the game behaves like Zig)

* **Arrays are values.** Wherever Zig copies an array (`var b = a;`, `b = a;`, arguments, returns, array / tuple
  elements, `append` / `put` values) and the value is read from a variable or an element, the IR copies it:
  `ListCopy` (`list(a)`), for nested arrays `[list(a[0]), list(a[1]), ...]` (at most 256 rows; the source must
  be a variable or an element with a simple index). Fresh values (literals, calls) and a constant read into a
  constant (or a constant parameter) are not copied. The copies are Synthetic (free); the printer prints them back as
  plain `b = a`. `std.ArrayList` / `std.AutoHashMap` values are *not* copied: in Zig they copy their header and
  share the buffer, which is what sharing the Python list / dict does.
* **Constants are constant all the way down.** Assigning to an element or field of a `const` array, tuple or struct
  (`t[0] = 1;`, `p.x = 1;`, `grid[0][1] = 1;`), a parameter or a capture is "cannot assign to constant".
* **Tuples and positions** are Python tuples, which cannot change: `t[i] = v` / `t[i] += v` on a `var` tuple and
  `p.x = v` / `p.y = v` on a `var farm.Position` rebuild the value (`t = (v, t[1])`, `p = (p[0], v)`). The base
  must be a variable or an element with a simple index.
* **`std.mem.indexOfScalar` / `findScalar`** return `?usize`, ListIndexOf returns -1: as a whole value (declaration,
  assignment, return, argument, `if (...) |i|`, `orelse` decl) the frontend emits `(x in s) ? index(s, x) : null`
  (Synthetic IrConditional, `x in s` Synthetic; slice and value must be simple, they are evaluated twice);
  `find(...) orelse d` becomes `(x in s) ? index : d`, `== null` / `!= null` become `not x in s` / `x in s`.
  Inside a larger expression (print arguments, if branches) it is rejected; `.?` is only supported in
  `list.orderedRemove(std.mem.findScalar(T, list.items, x).?)` (→ `remove(x)`).
* **`list.pop()`** returns null on an empty list in Zig but fails in the game. As a whole value it becomes
  `len(l) > 0 ? l.pop() : null` (`orelse d`: `: d`), `_ = l.pop();` becomes `if len(l) > 0: l.pop()`,
  `l.pop() == null` becomes `len(l) == 0 or l.pop() == None` (`!=`: `len(l) > 0 and l.pop() != None`; not for
  lists of optionals), `while (l.pop()) |x|` loops while `len(l) > 0`. All guards are Synthetic. `l.pop().?` is
  the plain pop (the game's error on an empty list is Zig's panic); `l.pop()` inside a larger expression is
  rejected. The printer turns every guard back into `l.pop()` / `std.mem.findScalar(...)`.
* **Container-level declarations** (top-level `const` / `var`) follow Zig: the initializer must be comptime-known
  ("unable to resolve comptime value: the initializer of a container-level variable must be comptime-known"): it
  may not read a `var` (of this or another file) nor call a farm function (`const n = farm.getWorldSize();`
  belongs in a function); calling the program's own functions there is not supported (Zig would run them at
  comptime, the game when the file loads). Literals, other constants, `@min` / `@as` ..., `.empty`,
  `.init(farm.allocator)`, `std.mem.indexOfScalar` on constants and `if` values (a hoisted module-level
  if/else, `const n: i32 = if (big) 1 else 2;`) are fine. Zig's declarations are order-independent: a global whose
  initializer reads a later one is emitted after it. `var a: i32 = undefined;` at the top level emits *no*
  assignment (ZigLanguage.Compile drops the IrGlobal; ToIr keeps it, printed `= undefined`), so a value passed by
  `farm.simulate` survives; reading it before any assignment is the game's "never defined" error (undefined
  behaviour in Zig). An undefined array still gets its lists (`__jarr`).
* `.{}` passed to a farm parameter without a Zig type (`anytype`: `farm.simulate`'s unlocks and globals) is an
  empty map `{}`, not the tuple `()`.
* `///` doc comments must document a declaration: inside a function body they are "expected statement, found 'a
  document comment'", at the end of the file "unattached documentation comment" (`////` is a plain comment).

## Control flow (and what each costs)

| Zig | Python twin | unlock |
|---|---|---|
| `while (c) { }` | `while c:` | Loops |
| `while (c) : (i += 1) { }` | `while c:` with `i += 1` appended to the body and before every `continue` (IrWhile, never "for") | Loops |
| `for (0..n) \|i\| { }`, `for (a..b) \|i\|` | `for i in range(n):` (IrRangeFor) | Expand 2 ("for") + range |
| `for (items) \|x\| { }`, `for (list.items) \|x\|` | `for x in items:` (IrForEach) | for |
| `var it = map.iterator(); while (it.next()) \|e\|` / `keyIterator()` / `valueIterator()` | `for k in m:` / values | for |
| `if` / `else if` / `else`, `if (c) a else b` as a whole value | `if` / `elif`, hoisted if/else | Speed ("if") |
| `switch` statement / expression | if/elif chain on a temp | if |
| `if (opt) \|v\| {}`, `x orelse y`, `const v = opt orelse return;`, `opt.?`, `== null` | `v = opt; if v != None:` ... | if |
| functions, `return` | `def`, `return` | Functions |
| top-level `var` / `const` | module globals (`global` lines are Synthetic) | Variables |

`break` / `continue` work as usual; labels, `while ... else`, `inline` loops, pointer captures `|*x|` and
several `for` inputs (other than `for (items, 0..) |x, i|`) are not supported.

## Printing

`std.debug.print(fmt, .{ args })` and `farm.quickPrint(fmt, .{ args })` become `print` / `quick_print` with
the format pieces joined by Synthetic `str()` and `+` (only "print" is charged). Specifiers `{d}` (numbers),
`{s}` (strings), `{}` (bool, enum), `{any}` (anything, also optionals and slices), `{?}`; placeholder and
argument counts are checked ("too few arguments", "unused argument in ..."). Each print is one game line, so the
format must end with `\n` and contain no other line break.

FormatValue (output page, hover) shows values like `std.debug.print("{any}")` in Zig 0.17: `true` / `false`,
`null`, numbers like `{d}` (3.0 prints `3`), enums `.Carrot` / `.North`, lists / sets / maps `{ 1, 2, 3 }`
(empty `{  }`), positions `.{ .x = 1, .y = 2 }`, tuples `.{ 1, 2 }`, drones `.{ .id = 1 }`. Deviation: strings
inside a list show quoted (`{ "a", "b" }`); real Zig would print their bytes.

## Collections

* Lists: `var xs: std.ArrayList(i32) = .empty;` (unmanaged, Zig 0.17), `xs.append(farm.allocator, x)`,
  `xs.insert(farm.allocator, i, x)`, `xs.pop()` (?T), `xs.orderedRemove(i)`, `xs.items[i]`, `xs.items.len`,
  `defer xs.deinit(farm.allocator);` (free). Allocating calls return an error union: `try` (in `!void`
  functions) or `catch unreachable`; ignoring it is "error union is ignored". Arrays `[_]T{...}` print as lists.
* Maps: `var m = std.AutoHashMap(K, V).init(farm.allocator);`, `put`, `get` (?V, with `orelse` → `dict.get(k, d)`),
  `contains`, `remove`, `count`, iterators. Sets: `std.AutoHashMap(T, void)` with `put(x, {})` (Dictionaries
  "sets"). `std.StringHashMap(V)` for string keys.
* Strings: `[]const u8`, compared with `std.mem.eql(u8, a, b)` (`==` is "operator == not allowed for type
  '[]const u8'"). No runtime concatenation.
* `farm.allocator` is the allocator the stub provides (`std.heap.page_allocator`); the game ignores it.

## Functions as values

Functions are passed to `farm.spawnDrone(f)` (no parameters) and may be aliased (`const op = square;`). Zig has no
closures and JavaFarmer does not support function types / pointers as parameters ("function types are not
supported"), so the printer hoists capture-free Python lambdas / nested functions to top-level functions and
reports capturing ones (docs-exceptions).

## Rejected constructs (and why)

"X is not supported in JavaFarmer" (valid Zig the game cannot express): struct / enum / union declarations, error
sets and `catch |err|`, `errdefer`, `defer` other than `deinit`, comptime parameters and `anytype`,
`inline` / `export` / `extern`, labeled blocks / loops, `while ... else`, pointer captures, pointers other than
map entries, sentinel arrays, bitwise / wrapping operators, `**` (array multiplication, removed in 0.17: use
`@splat`), `orelse { ... }` blocks, switch ranges and prong captures, `unreachable` as a statement, map
`getPtr`, untyped enum literals, untyped switch expressions whose type cannot be inferred
(`const x: T = switch ...`), calls of the program's own functions in container-level initializers,
`std.mem.indexOfScalar(...)` / `list.pop()` inside a larger expression (see Values).

## Deviations from real Zig

* Zig analyses lazily; JavaFarmer checks every function (the conformance hook forces the same in the real compiler).
* Overflow is not checked (the game uses Python integers); integer division by a runtime zero is a game error.
* `orelse` needs a variable, `map.get(k)`, `list.pop()`, `std.mem.indexOfScalar(...)` or a declaration on its left (the Python twin would evaluate it twice).
* If expressions must be a whole value (declaration, assignment, return, argument).
* AutoHashMap iteration follows insertion order in the game (Python dicts), hash order in real Zig.
* A top-level `if` value is a module-level if/else in the game (it charges "if" like any if); in Zig it is
  evaluated at comptime.
* Reading a top-level `var x: T = undefined;` before assigning it is the game's "never defined" error (Zig: an
  undefined value). Captures of `for (grid) |row|` alias the row (Zig copies it): changing `grid` inside that
  loop is visible through `row`.
* FormatValue shows nested strings quoted.

## Printer

ZigPrinter prints any frontend's IR (typed or inferred Python IR) as beginner Zig: `const` unless mutated,
`_ = x;` / `_ = farm.harvest();` where Zig requires it, `.empty` / `.init(farm.allocator)` collections with
`append` / `put` lines, `try` + `!void` main, `if (opt) |v|` and `orelse continue` for the optional patterns,
`while (c) : (i += 1)` for continue expressions, `@intCast` / `@floatFromInt` with result types, default
argument values filled in at calls, struct unpacking (`x, y = measure()`) through a constant. Snippet mode prints
statements bare (no imports, no main) and a lone prose expression bare (`farm.canHarvest()`). Comments survive.
Problems are reported through IPrinterDiagnostics (DocsTranslator keeps those spans in Python).

## Stub file

`ApiStubFile` → `farm.zig`: `pub const allocator`, the enums `Entity`, `Item`, `Ground`, `Unlock`, `Hat`,
`Leaderboard`, `Direction` with the known members, `Position` / `Companion` / `Drone` structs, `quickPrint` and
one `pub fn` per builtin arity (incl. registered ones) whose body only discards its parameters and refuses a comptime
call (`if (@inComptime()) @compileError("unable to resolve comptime value: farm.X runs in the game ...")`, so that
the real compiler rejects `const n = farm.getWorldSize();` at the top level like JavaFarmer). Use it with
`zig build-exe --dep farm -Mroot=main.zig -Mfarm=farm.zig`.

## Tests

tests/corpus/zig (valid incl. multi/, diff with .expected outputs, invalid with ERROR/AT markers,
docs-exceptions.txt), tests/JavaFarmer.Languages.Tests/Hooks/ZigHooks.cs (Conformance: `zig build-exe
-fno-emit-bin` with a comptime block referencing every top-level declaration; Runner: `zig build-exe ...
-femit-bin=prog` then `./prog`; the zig executable is `zig` on PATH or `$JF_ZIG`), tests/JavaFarmer.Languages.Tests/Zig/
(ZigValueSemanticsTests: the Values section; ZigContentTests.EveryCompilingBlock_IsAcceptedByTheRealZigCompiler runs
`zig ast-check` and `zig build-exe -fno-emit-bin` with the stub on every content/zig docs block, tooltip example and
unlock snippet JavaFarmer accepts, exactly as wrapped; skipped without zig). The content wrapper keeps a snippet's
const / var declarations at the top level when they are comptime-known and moves them into `main` otherwise
(unused ones get `_ = x;`).
