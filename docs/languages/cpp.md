# C++ in JavaFarmer (id `cpp`)

C++23 for The Farmer Was Replaced. Everything a valid JavaFarmer C++ program contains is real C++: the conformance tests
compile every valid corpus program with `g++ -std=c++23 -Wall -Werror -Wno-unused -Wno-sign-compare -fsyntax-only`
against the stub `farm.hpp`; the differential tests build pure-computation programs with g++, run them and compare the
output with the emitted game Python run by CPython.
Shared frontend with C: `src/JavaFarmer.Compiler/Languages/CFamily/` (dialect switches in Parser and Binder), printer
`CPrinter`, stub generator `FarmStub.CppHeader`, `CppLanguage` (`Languages/Cpp/CppLanguage.cs`). Extension `.cpp`.
Read docs/languages/c.md too: what it says about scaffolding, switch, numbers, other windows and unlock fairness also
holds for C++; this file lists the differences.

## 1. A program

    #include "farm.hpp"
    #include <vector>
    #include <print>

    int main() {
    	std::vector<Entity> crops = {Entities::Carrot, Entities::Grass};
    	for (int col = 0; col < get_world_size(); col++) {
    		if (!spawn_drone([&]() { plant(crops[col % 2]); })) {
    			harvest();
    		}
    		move(East);
    	}
    	std::println("done");
    }

* Entry point `int main()` (also `int main(void)`); final `return 0;` dropped. Template:
  `#include "farm.hpp"\n\nint main() {\n\t\n}\n`.
* Headers: `"farm.hpp"` plus `<iostream> <vector> <map> <unordered_map> <set> <unordered_set> <string> <cmath>
  <algorithm> <functional> <optional> <print> <format> <cstdio> <cstdlib> <utility> <ranges>`. A missing standard
  header is an error with g++'s hint ("did you forget '#include <vector>'?").
* `using namespace std;` makes the `std::` prefix optional; `using namespace farm;` is accepted (the stub already
  does it). Other `using` declarations, namespaces, classes, structs, templates, `new` / `delete`, pointers,
  exceptions (`try`, `throw`) are rejected with "… is not supported in JavaFarmer".

## 2. Game API (`farm.hpp`)

Same snake_case names as the game and as C (`harvest()`, `get_pos_x()` ...), declared in `namespace farm` and
available unqualified (`farm::harvest()` works too). Differences from C:

| game | C++ |
|---|---|
| `print(...)` | `std::cout << a << " " << b << std::endl;` (or `'\n'`), `std::println("{} {}", a, b)`, `std::print` |
| `quick_print(a, b)` | `quick_print("{} {}", a, b)` (std::format style) |
| `min` / `max` / `abs` | `std::min(a, b)` / `std::max` (both arguments of the same type, like the real ones) / `std::abs` |
| `random()` | `farm::random()` (plain `random()` is ambiguous with the C library's, exactly as in g++) |
| `str(x)` | `std::to_string(x)` (numbers), `std::format("{}", x)` |
| `get_entity_type()` | `Entity`, `== nullptr` when empty |
| `get_cost(x)` | `std::optional<std::map<Item, double>>`: `if (cost) for (auto& [item, n] : *cost)`, `cost->size()` |
| `get_companion()` | `std::optional<Companion>`: `c->entity`, `c->position`; `auto [kind, where] = *c;` |
| `measure()` | `farm::Value`, converts to `int`, `double` or `Position` (`int petals = measure();`, `Position next = measure();`) |
| `spawn_drone(f)` | a function name, `std::function<void()>`, or a lambda `[&]() { ... }` / `[=] { ... }` (no parameters); returns `Drone` (`nullptr` / `false` when none is free) |
| `wait_for(d)` | returns nothing |

Constants: `Entities::Carrot`, `Items::Hay`, `Grounds::Soil`, `Unlocks::Speed`, `Hats::…`, `Leaderboards::…` (any
member name), directions `North` or `Direction::North` (also `farm::North`). `Position{x, y}` with `.x` / `.y`.

## 3. Types

`int`, `long` (= int), `double`, `float` (= double), `bool`, `std::string`, `auto`, `const` / `constexpr`, game
types, `Position`, `Companion`, `std::vector<T>`, `std::map<K, V>` / `std::unordered_map`, `std::set<T>` /
`std::unordered_set`, `std::optional<T>`, `std::function<R(P...)>`, lambdas. Vectors of vectors are allowed
(`std::vector<std::vector<int>> g(w, std::vector<int>(h, 0));`). Fixed arrays `int a[5]` work as in C.
References: `T& x` parameters alias the caller's object; `for (auto& x : v)` reads (assigning the loop reference
variable is rejected: assign `v[i]` instead). **Value semantics are kept:** `std::vector<int> b = a;` and by-value
parameters copy (the emitter writes `list(a)` / `set` / `dict` copies), unlike Python. `char`, unsigned types and
pointers are rejected.

## 4. Library subset

| | supported |
|---|---|
| `std::vector` | `{...}` init, `(n, value)`, `push_back`, `emplace_back`, `pop_back`, `[i]`, `at`, `front`, `back`, `size`, `empty`, `clear`, `insert(v.begin() + i, x)`, `erase(v.begin() + i)`, `erase(std::find(v.begin(), v.end(), x))`, range-for, `std::ranges::contains(v, x)`, `std::find(v.begin(), v.end(), x) != v.end()` |
| `std::map` / `unordered_map` | `{{k, v}, ...}`, `m[k]` (read and write), `at`, `count(k)` (as a condition or compared with 0/1), `contains`, `erase`, `insert_or_assign`, `size`, `empty`, `clear`, `m.find(k) != m.end()`, `for (auto& [k, v] : m)`, `for (auto& p : m)` with `p.first` / `p.second` |
| `std::set` / `unordered_set` | `insert`, `erase`, `count`, `contains`, `size`, `empty`, `clear`, range-for |
| `std::string` | `+` (one side a `std::string`), `+=`, `==`, `size`, `length`, `empty`, `contains`, `find(t) != std::string::npos`, `c_str`, `std::to_string` |
| `std::optional` | `if (opt)`, `opt.has_value()`, `*opt`, `opt->member`, `opt.value()`, `std::nullopt` |
| `<cmath>` | `std::floor`, `ceil`, `round`, `sqrt`, `pow`, `fmod`, `fabs`, `fmin`, `fmax` (also unqualified) |
| output | `std::cout <<` chains (`std::endl`, `'\n'`, `std::boolalpha`), `std::println` / `std::print` / `std::format` with `{}` only |

Other iterator use, algorithms, `std::sort`, `std::pair` values, format specifications (`{:5}`) are rejected.
Printing a `bool` with `std::cout` without `std::boolalpha` is rejected (real C++ prints `1`, the game `true`);
`std::println("{}", b)` prints `true` in both. Collections print with `std::println("{}", v)` (`[1, 2]`, `{k: v}`).

## 5. Lambdas and spawn_drone

`[&] { ... }`, `[=] { ... }`, `[&](int x) { return x * 2; }`, captures by name. A lambda becomes a hoisted game
function. It may read the variables around it, but it **cannot assign a local of the enclosing function**: the game
has no `nonlocal` (Core reports "a closure cannot assign the outer variable"), so use a global or a collection
element. `spawn_drone` needs a lambda without parameters.

## 6. Printing and buffering

As in C: one game `print` per finished line; text without a line break (`std::cout << x;`, `std::print`) is kept
until a later output ends the line. Numbers are formatted like the game (`3.5`, `2`).

## 7. Other code windows

Prototypes and `extern` declarations, exactly like C (docs/languages/c.md §8).

## 8. Unlock fairness

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

Lambdas cost Functions (they are game functions).

## 9. Deviations from real C++ (documented)

* One number type in the game (doubles): no overflow, `long` = `int`, `float` = `double`; `size()` is an `int`.
* `std::map` / `std::set` visit their elements in sorted order in real C++, the game's dicts and sets in insertion
  order; `unordered_*` have no order. Programs whose output depends on iteration order differ.
* Uninitialised `int` / `double` / `bool` locals start at 0 / `false`.
* `wait_for` returns nothing; lambdas cannot assign captured locals (§5).
* Doubles print like the game (no `std::cout` precision settings).

## 10. Values (FormatValue)

`nullptr`, `true` / `false`, numbers like the game, vectors `[1, 2]`, sets `{1, 2}`, maps `{"a": 1}`, positions
`(3, 4)`, constants `Entities::Carrot`, directions `North`, drones `Drone 2` (the `std::format` style of farm.hpp).

## 11. Stub file and tests

`ApiStubFile` → `farm.hpp` (`FarmStub.CppHeader`: handle types with `std::hash` / `std::formatter` / `operator<<`,
constants as `inline const` objects in namespaces `Entities`, `Items` ..., the game functions, `quick_print`,
`farm::random`). Tests: `tests/corpus/cpp/{valid,diff,invalid}`, `Hooks/CppHooks.cs`, `tests/corpus/cpp/docs-exceptions.txt`,
`tests/JavaFarmer.Languages.Tests/CFamily/`.
