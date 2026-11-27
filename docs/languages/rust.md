# Rust in JavaFarmer (id `rust`)

Rust (edition 2024 subset) for The Farmer Was Replaced. Every valid JavaFarmer Rust program is also real Rust. The
conformance tests build every valid corpus program with `rustc --edition 2024` against the stub `farm.rs` (see
"Stub file"). The differential tests run the real binary and the generated Python, and the outputs must match.
Frontend: `src/JavaFarmer.Compiler/Languages/Rust/`: RustLexer → RustParser (RustAst) → RustChecker (+ .Calls /
.Expr; types in RustTypes, rustc wording) → RustLowering (IR) → the shared IrEmitter. RustPrinter turns IR into Rust,
RustHighlighter does the highlighting, RustStub writes farm.rs, RustApi holds the naming table, and RustLanguage is
the ILanguage (class `RustLanguage`, DisplayName "Rust", FileExtension `.rs`).

A file's language comes from its extension (`main.rs`). Other windows are modules by window name without extension.

## 1. Program shape

    use farm::*;

    fn main() {
    	loop {
    		if can_harvest() {
    			harvest();
    		}
    		move_to(North);
    	}
    }

* `fn main()` is the entry point. Its header is scaffolding: it lowers to the entry function, which emits
  `def main():` and the `__name__` guard as Synthetic + NoHighlight, so it is free. `main` cannot return a value.
* `use farm::*;` (needed, like in real Rust), `use std::collections::{HashMap, HashSet};` and `mod utils;` have no
  IR node, so they cost nothing. A `mod` only costs Imports once something from the module is used (`import utils`).
* A file without `fn main` is a module that other windows use (`mod utils;` + `utils::f()`, or
  `use utils::{f, LIMIT};` / `use utils::*;`). Only `pub` items are visible (E0603 otherwise). `mod x;` for a
  missing window gives E0583 "(create a code window named "x")".
* Module level: `fn` items, `const NAME: T = value;` and `static NAME: T = value;` (free constants or globals),
  `static mut COUNTER: i32 = 0;` for module state. It works only for Copy types (numbers, bool, game enums,
  tuples of them), and every read or write must be inside `unsafe { }` (E0133, as in rustc). A `static mut` of a
  Vec / HashMap / String is rejected: Rust 2024 forbids references to it, so use a local and pass it as a parameter.

## 2. API naming (ILanguage.ApiName / FromApiName)

The names are the game's snake_case names (`GameApi` at compile time, so registered extension builtins work
without code changes, e.g. `scan_port` → `scan_port`). The exceptions:

| game (Python) | Rust | why |
|---|---|---|
| `move(North)` | `move_to(North)` (also `r#move(North)`) | `move` is a Rust keyword |
| `print(a, b)` | `println!("{} {}", a, b)` (also `print!`) | print is a macro |
| `quick_print(x)` | `quick_print!("{}", x)` | the stub defines it as a macro |
| optional parameters, e.g. `use_item(i, n)`, `measure(dir)` | `use_item(i)` / `use_item_with(i, n)`, `measure()` / `measure_with(dir)` | Rust has no default arguments |
| `min` / `max` / `abs` | `min(a, b)` / `max(a, b)` / `abs(x)` (generic) | |
| `len(x)` | `v.len()` (usize: `v.len() as i32`) | |
| `str(x)` | `x.to_string()` / `format!("{}", x)` | |

FromApiName also accepts `r#move`, `print!`, `println` and the `_with` forms (`use_item_with` → `use_item`).

Return types: `get_entity_type()` → `Option<Entity>`, `get_ground_type()` → `Ground`, `get_pos_x()` /
`get_world_size()` → `i32`, `num_items(..)` / `get_water()` / `get_time()` / `random()` → `f64`, `get_cost(u)` →
`Option<HashMap<Item, f64>>`, `get_companion()` → `Option<Companion>` (`Companion = (Entity, Position)`,
`Position = (i32, i32)`), `measure()` → generic `T: Measurement` (`f64`, `i32`, `Position` or an `Option` of them:
`let h: f64 = measure();`, `if let Some(p) = measure() { .. }`), `spawn_drone(task)` → `Option<Drone<T>>`,
`wait_for(d)` → `T` (what the task returns). `spawn_drone` takes a `fn` item or a closure (`|| ...`,
`move || ...`).

## 3. Constants

The game's plural categories become a singular enum, and the member keeps the game's name. Any member name is
accepted (IrConst(category, member)). The plugin knows the names for completion. The stub lists the known members,
and the conformance hook adds the ones the sources use.

| game | Rust |
|---|---|
| `Entities.Carrot` | `Entity::Carrot` |
| `Items.Hay` | `Item::Hay` |
| `Grounds.Soil` | `Ground::Soil` |
| `Unlocks.Speed` | `Unlock::Speed` |
| `Hats.Straw_Hat` | `Hat::Straw_Hat` |
| `Leaderboards.X` | `Leaderboard::X` |
| `North` … | `North` / `Direction::North` (`use farm::*;` re-exports the four directions) |

A game member that is a Rust keyword prints as `r#name`. A typo in the enum name (`Entities::Carrot`) gives E0433
"(the game's Entities are `Entity::Carrot` in Rust)".

## 4. Types and operators

* `i32`, `i64`, `u32`, `usize` (all of them are the game's one number type; `usize` is needed for indexing),
  `f64`, `bool`, `String`, `&str` / `&'static str`, `Vec<T>`, `[T; N]` arrays (consts / literals),
  `HashMap<K, V>`, `HashSet<T>`, tuples, `Option<T>`, `fn(A) -> R`, and the game types `Entity`, `Item`,
  `Ground`, `Unlock`, `Hat`, `Leaderboard`, `Direction`, `Position`, `Companion`, `Drone<T>`.
* No implicit conversions, as in rustc: `i32 * {float}` is E0277 "cannot multiply", and `let x: i32 = 0.5;` is
  E0308. Convert with `as` (`x as f64`, `f as i32` truncates → IrCast). Literals take their type from context
  (`let x = 5;` is `{integer}` until it is used). A method on an ambiguous literal is E0689, as in rustc
  (`2_i32.pow(3)`).
* Integer `/` truncates and `%` is the remainder (IrBinOp.IntDivTrunc / ModTrunc → `__jdiv` / `__jmod` helpers,
  Synthetic). `div_euclid` / `rem_euclid` are Python's `//` / `%`. `f64 /` is true division. Overflow is not
  emulated (deviation).
* Indexing needs `usize` (`v[i as usize]`; `v[i]` with `i: i32` is E0277 "cannot be indexed by `i32`").
* `let` is immutable (assigning again is E0384, mutating a non-mut Vec / HashMap is E0596). Shadowing
  (`let x = x + 1;`) is a new variable. A use before initialisation is E0381.
* Operators: `+ - * / %`, `+= -= *= /= %=`, comparisons, `&& || !`, unary `-`, `as`. `&`, `&mut`, `*x`,
  `.iter()`, `.clone()` of Copy values and `.copied()` are no-ops in the IR (Python has no references). `.clone()` of a
  Vec / HashMap / HashSet is a copy (`list(x)` / `dict(x)` / `set(x)`). String `+ &str` and `format!` concatenate.
  Rejected: bitwise and shift operators, `?`, `++` ("Rust has no postfix increment operator"), comparing
  `Option` with a value without `Some(..)` (E0308).

## 5. Statements and expressions

* `if / else if / else` (no truthiness: `if get_entity_type()` is E0308 "(use `.is_some()`)"), `if let Some(x) =
  e` (also `if let Some((a, (x, y))) = get_companion()` and constant patterns), `while`, `while let Some(x) =
  v.pop()`, `loop`, `break` / `continue` (without labels or values).
* `for`: `0..n`, `a..=b`, `(a..b).rev()`, `.step_by(k)` (range → `for x in range(..)`), `for x in &v` /
  `v.iter()` / `for &x in &v`, `for (i, x) in v.iter().enumerate()`, `for (k, v) in &m`, `m.keys()` /
  `m.values()`, `for x in [a, b, c]`.
* `match` on numbers, strings (`s.as_str()`), bools, enums, `Option` (`Some(x) => .., None => ..`), with or-patterns
  `1 | 2 =>` and `_`. As a statement, it lowers to the same if/elif chain as Python's `match` / if chain (Speed "if").
  A `match` or `if` as the whole value of a `let`, an assignment or a return is lifted to statements, so it costs
  only "if".
* Functions: `fn name(a: i32, v: &Vec<i32>, m: &mut HashMap<..>) -> T`, early `return`, and tail-expression returns.
  `fn` items are values (`let f: fn(i32) -> i32 = double;`, `spawn_drone(work)`). Closures: `|a: i32| a + 1`,
  `|| { .. }`, `move || ..`. Assigning a captured variable inside a closure is rejected, because the game's Python
  has no `nonlocal`.

## 6. Collections

| Rust | IR / Python |
|---|---|
| `vec![1, 2]`, `Vec::new()`, `vec![0; n]`, `vec![vec![0; h]; w]`, `Vec::from([..])` | list (`[0] * n` helpers) |
| `v.push(x)`, `v.pop()` (Option; `v.pop().unwrap()`), `v.insert(i, x)`, `v.remove(i)`, `v.swap(i, j)`, `v.contains(&x)`, `v.len()`, `v.is_empty()`, `v.clear()`, `v[i]`, `v[i] = x` | append / pop / insert / pop(i) / swap / in / len / clear / [] |
| `v.remove_value(&x)`, `v.index_of(&x)` (JavaFarmer helpers in the `FarmVec` trait of the stub) | remove / index |
| `HashMap::new()`, `HashMap::from([(k, v)])`, `m.insert(k, v)`, `m.get(&k)` (Option), `m[&k]`, `m.contains_key(&k)`, `m.remove(&k)`, `*m.entry(k).or_insert(0) += x`, `m.len()`, `m.clear()` | dict |
| `HashSet::new()` / `HashSet::from([..])`, `insert`, `remove`, `contains`, `len`, `clear` | set |
| tuples `(1, 2)`, `p.0`, `let (x, y) = p;` | tuples |

Rejected with "… is not supported in JavaFarmer": `sort`, `reverse`, iterator adapters (`map`, `filter`,
`collect` …) outside a `for` header, slicing `v[a..b]`, `chars()`, `&mut` iteration (change elements by index), the
values of `insert` / `remove` on sets, and other std collections (BTreeMap, VecDeque …).

## 7. Printing (ILanguage.FormatValue)

`println!` / `print!` / `quick_print!` / `format!` with `{}`, `{:?}`, positional `{0}` and inline `{x}` / `{x:?}`
lower to `print` / `quick_print` with a Synthetic `+` / `str()` concatenation, so they cost only "print" (Debug), and
`format!` alone costs nothing extra. `print!` prints a whole line like `println!` (deviation: the game's print always
ends the line). Precision (`{:.2}`), width and other format specs are rejected ("the game prints numbers in full").
Line breaks inside format strings are rejected (one `println!` per line).

FormatValue is Rust Debug style: `true` / `false`, `None`, numbers like Python without a trailing `.0` (`3`, `2.5`),
strings as they are (`"a"` inside collections), `[1, 2]`, `{1, 2}`, `{"k": 1}`, `(1, 2)`, enum members by name
(`Carrot`, `North`: the stub's Display / Debug), drones `Drone(3)`, functions by their Rust name (`move_to`),
closures `{closure}`. Deviation: an `Option` prints its value (`Carrot`), not `Some(Carrot)`, because the game
has no Option.

## 8. Rejected on purpose (and why)

Own types (struct, enum, impl, trait, type aliases), generics and lifetimes (the only lifetime is `&'static str`),
`?`, `unsafe` functions, macros other than the print family (`panic!`, `vec!` is fine), loop labels, `break` with a
value, match guards, tuple / range / binding-before-last patterns in `match`, `char` literals (the game has only
strings), `static mut` of non-Copy types, multi-line strings, `use std::...` other than collections, slicing,
iterator chains, `sort` / `reverse` (no game twin). Each gives "X is not supported in JavaFarmer", often with a hint.
Identifiers starting with `__j` are reserved for the emitter. Beginner mistakes get rustc's wording and code: missing
`;`, `println(..)` (E0423 "found macro"), `move(..)` "(the game's move() is called move_to() or r#move() in Rust)",
`True` (E0425 "(Rust writes `true`)"), a missing `use std::collections::HashMap;` (E0433), and `String + String`
(E0308 "consider borrowing here").

## 9. Deviations from real Rust

* Every integer type is the game's number: no overflow, and `u32` can go negative without a panic.
* `print!` ends the line. An `Option` prints as its value. HashMap / HashSet iteration order is the game's insertion
  order (Rust's is random).
* `static mut` is allowed (inside `unsafe`) as the game's module state. It is single-threaded in the game. Drones get
  their own copy of the globals, as in the game.
* Unwrapping `None` is the game's error, not a Rust panic message.

## 10. Unlock mapping (each construct costs its Python twin)

| Rust | unlock |
|---|---|
| `fn main()` header, `use`, `mod` (until used) | free (Synthetic / no IR) |
| `loop`, `while`, `while let` | Loops |
| `if`, `else`, `if let`, `match`, `if` / `match` as a value | Speed ("if") |
| `for` over a range | Expand 2 ("for") + "range"; `for` over a Vec / map | "for" |
| arithmetic, comparison, logic, `as` | Operators (the `as` cast and `__jdiv` helpers are Synthetic) |
| `let`, assignments, `const` / `static` | Variables |
| `fn` items, `return`, closures | Functions |
| `utils::f()` (another window, any language) | Imports |
| `Vec`, arrays | Lists; `HashMap` → Dictionaries; `HashSet` → Dictionaries ("sets") |
| `println!` / `quick_print!` | Debug ("print"): the str() / + parts are Synthetic |
| `min` / `max` / `abs` | Utilities |
| `.to_string()`, `format!` of one value | Debug ("str") |

A `while` never gets a "for" DependencyOverride (unit test `WhileLoop_CostsLoops_NotFor`).

## 11. Cross-language imports (docs/LANGUAGE-GUIDE.md `9)

`mod helper;` + `helper::f()` / `use helper::f;` works for windows of ANY language. When
`ForeignModules.IsForeign(options, module, "rust")` is true, the checker reads the window through
`ForeignModules.Resolve` and never parses it as Rust. Typed signatures (Java: `int`, `double`, `boolean`, the game
enums and their Option) are checked like Rust items. Strings (as parameters), collections, functions and untyped
values (Python, JS, R) become an `{error}` type that accepts anything, which is the escape hatch. Foreign items are
never private, and foreign globals can be read. A foreign file that does not compile is reported at the use site:
"cannot use `helper`: helper (Java) does not compile (line 3: …)". A Rust module with a syntax error is reported
the same way. `RustLanguage` implements `ISignatureProvider` (declarations only, bodies not checked), so Java and
the other languages call Rust windows with real types (`helper.twice(4)`).

## 12. Printer (IR → Rust)

`RustPrinter` prints IR from any frontend (typed Rust / Java IR, inferred Python docs IR) as beginner Rust: `let` /
`let mut` (mutability analysis), `for i in 0..n`, `loop`, `if let Some(x) = ..` (it recognises the Rust lowering's
pattern), `match` for switch IR, `println!` with inline `{x}`, `Vec` / `HashMap` / `HashSet` with the needed `use`
line, `&` / `&mut` parameters for collections, `.unwrap()` where an Option meets a value, `Option<f64>` for a
`measure()` the code tests for None, and `const` / `static mut` (inside `unsafe`) for module state. Comments
survive. Snippet mode (docs) prints the statements without `fn main` and keeps the declarations. In a full program,
the variables of an earlier docs block become main locals marked "// defined elsewhere". Python's
`if __name__ == "__main__":` is unwrapped. IPrinterDiagnostics lists what Rust cannot say, so the docs translator
keeps Python there: lists with mixed element types, a loop variable read after its loop (a later loop or assignment
that rebinds the name does not count), builtins with parameters as values, module-level collections shared by
functions, unknown types. `tests/corpus/rust/docs-exceptions.txt` lists the 12 EN docs blocks that cannot be
printed, each with its reason.

## 13. Stub file

`ApiStubFile` → `farm.rs`, generated from `GameApi.Builtins` (registered extension builtins included). It contains a
library crate with the enums (derive Debug/Clone/Copy/PartialEq/Eq/Hash/Ord + Display), `pub use Direction::*;`,
`Position` / `Companion` aliases, `Drone<T>`, marker traits for union parameters (`Measurement`, `Number`),
`FarmVec` (`remove_value` / `index_of`), the `quick_print!` macro, and one `pub fn` per builtin (plus `_with` forms;
bodies are `unimplemented!()`). Use it with an editor or rustc:

    rustc --edition 2024 --crate-type lib --crate-name farm farm.rs
    rustc --edition 2024 --extern farm=libfarm.rlib -L . main.rs

## 14. Tests

`tests/corpus/rust/`: `valid/` (24 programs, including the v1 Java farm scenarios rewritten as Rust and a three-file
program in `valid/multi/`), `diff/` (9 pure-computation programs: integer math with negatives, loops, match, closures
and fn values, collections, strings, recursion, algorithms, tuples), `invalid/` (47 programs with `// ERROR:` /
`// AT:` markers). Hooks: `tests/JavaFarmer.Languages.Tests/Hooks/RustHooks.cs` (conformance: stub as
`--crate-type lib --crate-name farm`, then the crate root with `--emit=metadata --extern farm=libfarm.rlib` and
`#![allow(unused)]` prepended; runner: build and run; PythonPrelude prints like `println!`). Unit tests:
`tests/JavaFarmer.Languages.Tests/Rust/` (language, fairness, printer, perf, fuzz, cross-language).
