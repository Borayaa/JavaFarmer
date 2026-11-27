# R in JavaFarmer (id `r`)

R 4.x script subset for The Farmer Was Replaced. Every valid JavaFarmer R program is real R: the conformance tests
run R's own parser (`parse(file = f)`, Rscript or the docker `r-base` image) on every valid corpus file plus the
stub `farm.R`, and the differential tests run the pure-computation programs in real R (with `farm.R` sourced
first) and compare the output with the game Python.

Frontend: `src/JavaFarmer.Compiler/Languages/R/` — `RlLexer` → `RlParser` (RAst.cs) → `RlLowering`
(scoping analysis, checks, IR lowering, `IrTypeInference.Infer` + R fix-ups, `IrDeclarations.Place`) → shared
IrEmitter. Printer `RlPrinter` (IR from any frontend → R). `RLanguage` (public, `ILanguage` +
`IPrinterDiagnostics`): Id `r`, DisplayName `R`, FileExtension `.R`, ApiNaming snake_case, stub file
`farm.R`.

File extension: `.R` (the R convention). The plugin chooses a window's language by its extension; it should also
recognise `.r` (compare the extension case-insensitively). `source("utils.R")` and `source("utils.r")` both name
the window `utils`.

## 1. Program form

An R file is a script: the top-level statements run from top to bottom (IrStmt items, no entry function). A new
window is empty (`NewFileTemplate` returns "" with the caret at 0).

    # Harvest the whole farm forever.
    while (TRUE) {
      for (i in seq_len(get_world_size())) {
        if (can_harvest()) harvest()
        move(North)
      }
      move(East)
    }

Statements are separated by new lines or `;`. Comments: `#` (line), roxygen `#'` lines are doc comments (IsDoc).
There are no block comments. Like real R, `else` at the top level must be on the same line as the closing `}`
(`} else {`); a top-level `else` on its own line is `Error: unexpected 'else' in "else"`.

## 2. Variables, assignment, scoping

* Assignment: `x <- 1`, `x = 1` (statement level), `1 -> x`, chained `x <- y <- 3` (becomes `y <- 3`
  then `x <- y`). An assignment used as a value elsewhere (`f(x <- 1)`, `if (x = 1)`) is rejected.
* Top-level names that a function reads or assigns become Global symbols (IrGlobal at their first plain top-level
  assignment); top-level names used only at the top level stay script locals.
* Inside a function, `<-` to a name creates a **local** (R semantics). When a function does `x <- x + 1` and `x`
  is a top-level variable, the reads of `x` before the first local assignment (textual order) are the global, and the
  local is a different symbol (the emitter renames it, e.g. `x_l`).
* `x <<- value` (also `value ->> x`) assigns the top-level variable (Python `global x`). `<<-` to a variable
  of an enclosing *function* is rejected: "assigning a variable of an enclosing function with <<- is not supported in
  JavaFarmer (only top-level variables); return the value instead".
* Reading a name that is never assigned: `Error: object 'x' not found`; at the top level a name must be assigned
  before it is used (textual order), like running the script in R.
* Names with dots are valid: `my.count` becomes `my_count` in the game Python. Two names that differ only by
  `.` / `_` are rejected ("'my.x' and 'my_x' are the same name in JavaFarmer (dots in names become
  underscores)"). Names starting with `__j` are reserved. Backtick names are accepted only when they are plain names (`` `total` ``); `` `my var` `` is "the name
  `my var` is not supported in JavaFarmer; use letters, digits, _ and . only (starting with a letter)".
* Numbers are the game's numbers (doubles). `5` and `5L` are whole-number literals (Int), `2.5`, `1e3` are
  Float; hex `0x1F` is accepted. Complex numbers (`2i`) are rejected.
* `TRUE` / `FALSE` / `NULL` (Python `None`). `T` / `F` → "T and F are not supported in JavaFarmer; write TRUE / FALSE".
  `NA` (and `NA_integer_` …) → "NA is not supported in JavaFarmer (the game has no missing values); use NULL".
  `Inf` / `NaN` → "Inf is not supported in JavaFarmer (game numbers are always finite)".
* Strings: `"…"` or `'…'` with R escapes; an unknown escape is R's
  `Error: '\q' is an unrecognized escape in character string`; line breaks inside a string are rejected (the game
  has no multi-line strings); raw strings `r"(...)"` are rejected.

## 3. Operators

| R | IR / game Python |
|---|---|
| `+ - *` , unary `-` | `+ - *` |
| `/` | TrueDiv `/` |
| `%/%` | IntDivFloor `//` (floors, like R) |
| `%%` | ModFloor `%` (sign of the divisor, like R) |
| `^` , `**` | Pow `**` |
| `== != < <= > >=` | same |
| `&& \|\| !` | `and or not` |
| `x %in% v` | Contains: `x in v` |
| `k %in% names(m)` | Contains(m, k): `k in m` |
| `is.null(x)` / `!is.null(x)` | `x == None` / `x != None` |

Rejected: vectorised `&` / `|` ("the vectorised & is not supported in JavaFarmer; use && (and || instead
of |)"), `x == NULL` ("comparing with NULL using == does not work in R (the result is empty); use is.null(x)"), the pipe
`|>`, formulas `~`, help `?`, `pkg::name`, slots `@`, other `%op%` operators, `a:b` outside a `for` sequence
("a:b (a sequence) is only supported as the sequence of a for loop in JavaFarmer: for (i in a:b)"). `text + 1` is
R's "non-numeric argument to binary operator (use paste() or paste0() to join text)".

## 4. Control flow

* `if (c) … else if (c2) … else …` (braces optional). `if` as a value with simple branches → IrConditional
  (`y <- if (x > 0) "a" else "b"`, also inside expressions). As the whole value of an assignment the branches may be
  blocks: `y <- if (c) { …; v1 } else { …; v2 }` becomes an IrIf assigning `y` in each branch (a missing else assigns
  NULL, Synthetic).
* `for (i in a:b)` → IrRangeFor Start a, End b, Inclusive. With literals `a > b` (`5:1`) it counts down (step -1,
  Synthetic). **Deviation:** with computed bounds JavaFarmer always counts up; R would count down when `b < a` (for example
  `1:0` runs twice in R and zero times in JavaFarmer). Use `seq_len(n)` to be safe.
* `for (i in seq_len(n))` → 1..n inclusive (start 1 Synthetic); `for (i in seq_len(n) - 1)` → 0..n-1 (Python
  `range(n)`); `seq_along(v)` → 1..length(v) (the `len` is Synthetic); `seq(a, b)`, `seq(a, b, by = s)` (the step must be
  a constant whole number: "seq(by = ...) needs a constant whole number step in JavaFarmer"). `seq_len` / `seq` /
  `seq_along` elsewhere are rejected.
* `for (x in v)` → IrForEach over the elements; `for (k in names(m))` → over the keys; `for (v in m)` with a named list
  → over the values.
* `while (c)`, `repeat { }` (IrLoop → `while True:`), `break`, `next` (continue). Outside a loop: R's
  "Error: no loop for break/next, jumping to top level".
* `switch(s, a = {…}, "b c" = {…}, {default})`: a text selector with named arms. Empty arms fall through to the
  next arm (`a = , b = print(1)`: two labels). As a statement → IrSwitch; as the whole value of an assignment
  (`y <- switch(s, a = 1, b = 2, 0)`) → IrSwitch with an assignment in each arm (no default → a Synthetic default
  assigning NULL, like R). The game Python is an if / elif chain. Numeric (positional) switch: "numeric switch() is not
  supported in JavaFarmer; use if / else if". Duplicate defaults: R's "Error in switch(): duplicate 'switch' defaults".

## 5. Functions

* `f <- function(a, b = 1) { … }` at the top level (assigned once) → IrFunction (`b = 1` is IrParam.Default; a default
  that uses another parameter is rejected). One-line bodies need no braces: `twice <- function(x) x * 2`.
* Anywhere else (nested, anonymous `function(x) …`, R 4.1 `\(x) …`) → IrLambda (closure, lifted by the emitter).
* Calls: positional and named arguments (`f(1, b = 2)`) for top-level functions, checked like R ("unused argument
  (c = 3)", "argument \"a\" is missing, with no default", "formal argument \"a\" matched by multiple actual arguments").
  Calls of closures / function values take positional arguments only. `...` is rejected.
* At the top level a function must be defined before it is called (as when R runs the script): "could not find
  function \"f\" (it is defined further down; define functions before you call them)". Inside function bodies any
  top-level function can be called (R looks it up when the call runs).
* `return(x)` → IrReturn (user). **Implicit return:** the last expression statement of a function body (and of each
  branch of a final if / else chain or switch) becomes an IrReturn with `Synthetic = true`, unless it is a call whose type is
  Void (print, cat, void game builtins such as `till()`, user functions that never return a value). `return()` outside a
  function: R's "Error: no function to return from, jumping to top level"; `return(a, b)`: "multi-argument returns are
  not permitted". `invisible(x)` is `x`.
* Printer: an IrReturn that is the last statement of a function prints as the bare value (idiomatic R); other returns
  print as `return(x)`.

## 6. Game API

Game builtins keep their snake_case names (ApiNaming Snake): `ApiName("can_harvest")` = `can_harvest`,
`FromApiName("do_a_flip")` = `do_a_flip`. They are looked up in `GameApi` at compile time, so builtins registered
by extensions work with no code change; argument counts and names come from the ApiBuiltin (optional parameters may
be given by name in order: `scan_port("a", port = 80)`).

| game (Python) | R |
|---|---|
| `harvest()` / `can_harvest()` | `harvest()` / `can_harvest()` |
| `plant(Entities.Carrot)` | `plant(Entities$Carrot)` |
| `move(North)` | `move(North)` |
| `get_world_size()`, `get_pos_x()` | same |
| `num_items(Items.Hay)` | `num_items(Items$Hay)` |
| `get_cost(x)` | `get_cost(x)` (a named list: `for (item in names(cost))`) |
| `measure()` (position) | `p <- measure()`; `p[[1]]`, `p[[2]]` |
| `get_companion()` | `comp <- get_companion()`; `comp[[1]]` (plant), `comp[[2]]` (position) |
| `spawn_drone(f)` | `spawn_drone(f)` (function name → IrFuncRef), `spawn_drone(function() work(3))` (IrLambda) |
| `print(x)` | `print(x)` (one value) |
| `print(a, b)` | `cat(a, b, "\n")` |
| `quick_print(a, b)` | `quick_print(a, b)` |
| `random()` | `random()` (`runif` / `sample` / `rnorm` are rejected: "use random()") |
| `min(a, b)` / `max` / `abs` | same (game builtins) |
| `len(x)` | `length(x)` (lists, maps), `nchar(s)` (text) |
| `str(x)` | `as.character(x)`, `paste0(x)` |
| `int(x)` | `as.integer(x)`, `trunc(x)` (IrCast Int, truncates) |
| `float(x)` | `as.numeric(x)` / `as.double(x)` |
| `math.floor` / `ceil` / `sqrt` | `floor(x)` / `ceiling(x)` / `sqrt(x)` |

`round()` is rejected: "round() is not supported in JavaFarmer (R rounds halves to even: round(2.5) is 2); use
floor(x + 0.5)". Converting text to a number is rejected. Unknown functions: R's `could not find function "foo"`;
common base-R functions that JavaFarmer lacks (`sum`, `sort`, `ifelse`, `stop`, `tryCatch`, `library`, `unique`,
`message` …) say "… is not supported in JavaFarmer" with a hint where there is one.

**Constants:** `Entities$Carrot`, `Items$Hay`, `Grounds$Soil`, `Unlocks$Speed`, `Hats$Straw_Hat`,
`Leaderboards$…` → IrConst(category, member) for ANY member name (also `Entities[["Carrot"]]`). Directions are bare:
`North East South West`. A bare category (`x <- Entities`) is rejected except as a call argument (the game's
`simulate(…, Unlocks, …)`). In `farm.R` the constants are text (`Items$Hay` is the string `"Items$Hay"`), so named
lists keyed by constants work in real R.

## 7. Printing

* `print(x)` → the game's `print(x)` (one value; `print(a, b)` is rejected: "print() shows one value in
  JavaFarmer; use cat(a, b, \"\\n\") to print several values").
* `cat(a, b, "\n")` → `print(a, b)` (one game print per cat call; a trailing `"\n"` argument, or a trailing `\n`
  at the end of the last text, is dropped; `cat("\n")` prints an empty line). `sep = " "` (default) and `sep = ""`
  (the values are joined, Synthetic joins) are supported; other `sep` values and other named arguments are rejected.
  Deviation: `cat` without a final `\n` still prints a whole line (the game has no partial lines).
* `paste(a, b)` → Concat with `" "` (Synthetic separator literals); `paste(a, b, sep = "-")` (a text constant);
  `paste0(a, b)` / `paste(a, b, sep = "")` → Concat. Non-text values get a Synthetic `str()`. `collapse =` is
  rejected. `paste(x)` with one value is `as.character(x)`.
* `message()` is rejected ("use print() or cat()").

**FormatValue** (how the game shows values to R players; `RlValueFormat`): `TRUE` / `FALSE` / `NULL`; numbers like R
prints them (whole numbers without decimals, others with 7 significant digits: `0.3333333`, `1.5e+20`; no `[1]`
prefix); text bare at the top level and double-quoted when nested; lists of one scalar kind as `c(1, 2, 3)`, others
(mixed, nested, empty) as `list(...)`; maps as `list(hay = 3, "two words" = TRUE, Items$Hay = 1)`; constants
`Entities$Carrot`, directions bare (`North`); positions `c(3, 4)`; companions `list(Entities$Tree, c(1, 2))`;
drones `<drone 2>`; functions by name, lambdas as `function`; sets like lists.

## 8. Collections

**Vectors / lists** (IR lists): `c(1, 2, 3)`, `list(1, "a")` → IrListLit; `c()`, `list()`, `vector()`,
`vector("list")` → empty list; `rep(x, n)` → a list of n copies (ArrayNew). There is no difference between atomic
vectors and lists in JavaFarmer (they are all game lists).

* **1-based indexing:** `v[i]` and `v[[i]]` → IrIndex with BaseOffset 1 (the emitter subtracts it; the `- 1` is
  Synthetic; literal indices are folded: `v[1]` → `v[0]`); assignments too. `v[0]` is rejected (R's "attempt to select
  less than one element (R positions start at 1…)"). Vector indices (`v[c(1, 2)]`, `v[1:3]`), logical indices,
  `v[]`, matrix indexing `x[i, j]` (use `x[[i]][[j]]`) are rejected.
* `length(v)` → Len. `x %in% v` → Contains. `match(x, v)` → ListIndexOf + 1 (the `+ 1` is Synthetic; the game
  raises an error when x is missing, R gives NA).
* Grow / shrink (statements): `v[[length(v) + 1]] <- x`, `v[length(v) + 1] <- x`, `v <- c(v, x)`, `v <- append(v, x)`
  → ListAppend; `v <- c(x, v)` → ListInsert at 0; `v <- append(v, x, after = i)` → ListInsert at i;
  `v[[i]] <- NULL` / `v <- v[-i]` → ListPop(v, i - 1); `v <- v[-length(v)]` → `v.pop()`.
* `c()` with a collection argument anywhere else is rejected: "combining vectors with c() is not supported in
  JavaFarmer; use list(...) for a list of lists" (and "combining vectors with c() / append() …; add the elements one at
  a time" for `v <- c(v, w)`). `append()` is only supported as `v <- append(v, x)`.

**Maps** (named lists, IR maps with text or constant keys): `list(a = 1, b = 2)` and `c(a = 1)` → IrMapLit;
`setNames(list(v1, v2), c(k1, k2))` for computed keys. An empty `list()` that is later used as a map (text or
constant keys in `[[ ]]`, `$`, `names()`) is an empty map.

* Read: `m[["k"]]`, `m$k`, `m[[k]]` → MapGetOrNull (R gives NULL for a missing key: `is.null(m$k)` works).
* Write: `m[[k]] <- v`, `m$k <- v` (IrIndex BaseOffset 0); remove: `m[[k]] <- NULL` → MapRemoveIfPresent.
* `names(m)` → keys, `length(m)` → Len, `k %in% names(m)` → Contains(m, k).
* List or map? For `x[[i]]`: a text / game-constant index → map; a number → list; otherwise the receiver's use in the
  file; default list. Maps with number keys or tuple keys cannot be written in R (named lists are keyed by text): use
  `paste(x, y)` as the key.

**Sets — decision:** R has no set type; JavaFarmer R has no set syntax. `unique`, `union`, `intersect`, `setdiff`
are rejected: "sets (unique()) are not supported in JavaFarmer; use a vector and %in%". Write sets as vectors that you
keep unique: `if (!(x %in% s)) s[[length(s) + 1]] <- x`. The printer prints IR sets (Python docs, Java saves) that way:
`set()` → `c()`, `s.add(x)` → the line above, `x in s` → `x %in% s`, `s.remove(x)` → `s <- s[-match(x, s)]`; it
reports set conversions (`set(list)`) as problems. Re-compiled, such code is a list in the game (same behaviour, other
Python shape).

**Tuples / positions:** R has no tuples. Positions and companions from the game are indexed 1-based (`p[[1]]`,
`p[[2]]`). Tuple unpacking and returning several values are not available (return a list: `list(x, y)` / `c(x, y)`).

## 9. Copy semantics (R values vs game references)

R vectors and lists are values; game lists and maps are shared references. JavaFarmer copies where R code relies on
value semantics (all copies are Synthetic, so they cost nothing):

* `b <- a` where the value is a plain name or an element access (`m$k`, `v[[i]]`) whose inferred type is a list /
  map → `b = list(a)` / `dict(a)` (ListCopy / MapCopy). The same for values stored into a list or map (`list(a, b)`,
  `v[[i]] <- a`, appends).
* A function that changes a parameter in place (index assignment, append / insert / remove lowering) starts with
  `p = list(p)`.
* A function that changes a top-level list with `<-` works on a local copy (R semantics): `g[[1]] <- 2` in a function
  becomes `g_l = list(g)` then `g_l[0] = 2` (use `g[[1]] <<- 2` to change the top-level list).

Remaining deviations: copies are shallow (a list of lists shares its inner lists); values returned from functions are
shared; values whose type is unknown at compile time (for example the result of
`measure()` or of a call of a function value) are not copied.

## 10. Other files (imports)

Other code windows can be written in ANY JavaFarmer language (the plugin picks each window's language by its file
extension). `source("utils.R")` (a top-level statement) brings the top-level functions and variables of the window
`utils` in: they are used by their bare names (`helper(1)`, `limit`). The argument is the window's file name with its
own extension: `source("utils.R")` for an R window, `source("Utils.java")`, `source("bot.py")`, `source("lib.rs")` …
for windows in other languages (the module name is the file name without its extension; any extension works, the
language comes from CompileOptions.OtherFileLanguages). Lowering: IrImport (ImportAll) + External symbols with the
other file's Python names (PyName); the game Python is `import utils` / `utils.helper(1)`. For R windows this is real R
(Rscript runs it when the files are next to each other); sourcing a window in another language only works in the game.

* R windows (no language entry, or "r"): parsed for their top-level declarations only (cached by text).
* Windows in another language (LANGUAGE-GUIDE §9): never parsed as R. `ForeignModules.Resolve` reads them with their
  own frontend; functions keep their parameter names (named arguments and argument-count checks work:
  `unused argument (2)`), and calls / variables get the signature's types (a Java `int twice(int)` call is Int; Python /
  JS / R files give unknown types, which stay dynamic). Privacy of the other language is not enforced. When the other
  file does not compile, the error is reported on the file name of the `source()` call: "cannot use Utils: Utils (Java)
  does not compile (line 3: …)".
* Other languages use R windows through `ForeignModules` too (`helper.twice(4)` from Java). R has no
  `ISignatureProvider`: the default (ToIr + FromIr) gives better types, because it includes the inferred return and
  variable types; a cycle between R and another language without a signature provider fails with the "imports this
  file back" message.

A missing window: R's "Error in file(filename, \"r\"): cannot open file 'x.R': No such file or directory (there is no
code window named x)". `source()` without an extension or with a directory, `source()` inside functions or as a
value, and a file sourcing itself are rejected. Without OtherFiles (ToIr for conversion) unknown names bind to the
first sourced file. The printer prints imports as `source("m.R")` (it does not know other windows' extensions; the R
frontend accepts that spelling for every language).

## 11. Rejected constructs (selection, exact messages start)

| construct | message |
|---|---|
| `T` / `F` | T and F are not supported in JavaFarmer; write TRUE / FALSE |
| `NA`, `Inf`, `NaN` | NA is not supported in JavaFarmer (the game has no missing values); use NULL / Inf is not supported in JavaFarmer (game numbers are always finite) |
| `a & b`, `a \| b` | the vectorised & is not supported in JavaFarmer; use && (and \|\| instead of \|) |
| `x \|> f()` | the pipe operator \|> is not supported in JavaFarmer; call the function directly |
| `switch(2, …)` | numeric switch() is not supported in JavaFarmer; use if / else if |
| `c(v, w)` | combining vectors with c() is not supported in JavaFarmer |
| `unique(v)` | sets (unique()) are not supported in JavaFarmer; use a vector and %in% |
| `round(x)` | round() is not supported in JavaFarmer (R rounds halves to even: round(2.5) is 2); use floor(x + 0.5) |
| `runif(1)` | runif() is not supported in JavaFarmer; use random() (the game's random number in [0, 1)) |
| `ifelse(c, a, b)` | ifelse() is not supported in JavaFarmer; use if (cond) a else b |
| `library(x)` | packages (library()) are not supported in JavaFarmer |
| `stop()`, `tryCatch` | stop() is not supported in JavaFarmer (the game has no errors you can catch) |
| `message(x)` | message() is not supported in JavaFarmer; use print() or cat() |
| `print(a, b)` | print() shows one value in JavaFarmer; use cat(a, b, "\n") to print several values |
| `if (x = 1)` | assignment inside a condition is not supported in JavaFarmer; did you mean == ? |
| `v[0]` | Error: attempt to select less than one element (R positions start at 1, so the first element is x[[1]]) |
| `x[i, j]` | matrix indexing x[i, j] is not supported in JavaFarmer; use a list of lists: x[[i]][[j]] |
| `v[c(1, 2)]` | indexing with a vector (x[c(1, 2)], x[1:3]) is not supported in JavaFarmer; use one index at a time |
| `s[1]` on text | x[i] on a string does not select a character in R (a string is one value); character access is not supported in JavaFarmer |
| `names(x) <- …` | replacement functions like names(x) <- value are not supported in JavaFarmer |
| `function(...)` | ... (a variable number of arguments) is not supported in JavaFarmer |
| `pkg::f()` | the :: operator (package::name) is not supported in JavaFarmer; call the function directly |
| `paste(v, collapse = "")` | paste(collapse = ...) is not supported in JavaFarmer; join the values in a loop |
| `list(1, a = 2)` | a list with some named and some unnamed elements is not supported in JavaFarmer |
| `f(if (c) 1)` | if without else cannot be used as a value here in JavaFarmer; add an else branch |
| `__jx` | names starting with __j are reserved for JavaFarmer |

Syntax errors imitate R: `Error: unexpected symbol in "x y"`, `Error: unexpected '}' in "}"`,
`Error: unexpected end of input` (with "the '{' here is never closed"), `Error: unexpected INCOMPLETE_STRING`,
each with the span of the offending token.

## 12. Unlock fairness (SPEC §7)

Every construct emits the IR its Python twin would produce, so it costs the twin's unlock: `for` loops → `for`
(range / for-each), `while` / `repeat` → `while`, `if` / `switch` / if-values → `if`, `function` → `def`
(functions unlock), `c()` / `list()` → lists, named lists → dictionaries, `%in%` → `in`, `paste` → `+` on text,
`as.character` → `str`. Invented scaffolding is Synthetic (free): implicit returns, the `1` start of
`seq_len` / `seq_along` and the `len` of `seq_along`, the step `-1` of `5:1`, the `- 1` of 1-based indexing, value
copies (`list(a)`, `p = list(p)`, `g_l = list(g)`), paste separators and `str()` conversions, `cat(sep = "")`
joins, the `+ 1` of `match`, the NULL defaults of `y <- switch(...)` / `y <- if (c) {...}`. The `+ 1` of an
inclusive range end is Structural (as for every language). Tests: `tests/JavaFarmer.Languages.Tests/R/RUnlockFairnessTests.cs`.

## 13. Printer (IR → R)

`RlPrinter` turns IR from any frontend (Python docs, Java saves) into idiomatic R that `RLanguage.Compile` turns
back into the same game Python shape (harness (e)/(f)):

* Scripts print their items in order; programs with an entry function print functions, globals, then the entry body at
  the top level (or `main <- function() {…}` + `main()` when that is not safe).
* 0-based IR indices print as `v[[i + 1]]` (literals folded: `v[[1]]`); 1-based R indices print as written.
* `range(n)` → `for (i in seq_len(n) - 1)` (correct for n = 0, unlike `0:(n - 1)`); `range(a, b)` → `a:(b - 1)` (real R would count down when `b - 1 < a`; the game
  counts up, see §4);
  steps → `seq(a, b - 1, by = s)`; C-style counting loops become ranges when possible, otherwise `while`.
* The last return of a function prints as the bare value; globals assigned in a function use `<<-`.
* IrSwitch with text labels → `switch()`; other labels (numbers, constants) → an if / else if chain.
* Maps: `list(a = 1)` or `setNames(list(…), c(…))`; `d.get(k, default)` uses a printed `get_or_default` helper.
* Names colliding with R keywords, base functions JavaFarmer handles (`c`, `length`, `list`, `T` …), game builtins
  or constant bags are renamed consistently (`c_`, `length_`).
* What R cannot express (tuple unpacking / tuple keys, number-keyed maps, iterating a string's characters, set
  conversions, 2-D arrays) is reported through `IPrinterDiagnostics` problems; the docs translator then keeps the Python
  block. Docs blocks that cannot be expressed are listed in `tests/corpus/r/docs-exceptions.txt` with a reason.

## 14. Stub file `farm.R`

`ApiStubFile` returns `farm.R` for external editors (RStudio, Rscript): every GameApi builtin (including
registered extension builtins, with the extension name) as an R function with roxygen `#'` docs and
`@param` / `@return` lines (optional parameters default to NULL, variadic ones are `...`); constant bags
`Entities`, `Items`, `Grounds`, `Unlocks`, `Hats`, `Leaderboards` with S3 `$` / `[[` methods so ANY member
name works and yields a string like `"Entities$Carrot"`; `North` / `East` / `South` / `West`; and game-like
`print`, `cat`, `paste`, `paste0`, `as.character` (mask base R so a differential run prints like the game),
`quick_print`, `min`, `max`, `abs`, `random`. Game actions do nothing in R (they return FALSE / 0 / "" / NULL).

## 15. Deviations from real R (summary)

* `a:b` with computed bounds always counts up.
* Copies are shallow; returned values and unknown-typed values are shared (§9).
* `cat` always prints whole lines; `print` shows one value without `[1]`.
* Numbers are the game's doubles; there are no NA / Inf / NaN, integer overflow is not emulated; `as.integer` truncates.
* Vectors and lists are the same thing (game lists); a vector can hold mixed values.
* `match(x, v)` with a missing x is a game error (R: NA). A missing map key reads as NULL (like R).
* Reading an index past the end is a game error (R gives NA / NULL).
* A function's implicit return of a void call returns nothing (R returns the call's invisible value).
* Top-level functions must be defined before top-level code calls them; a name assigned in a function before its
  global is read there is a local (textual order, as described in §2).

## 16. Content cheat sheet (for docs content written in R)

* **Program start:** no main function; the file runs from the top. An empty window is a valid program.
* **Statements:** one per line; `x <- 1`, `x <- x + 1` (no `+=`), `if (c) { … } else { … }` with `} else {` on one
  line, `while (c) { … }`, `repeat { … }` with `break`, `next` for continue, `for (i in seq_len(n))` (1..n),
  `for (i in seq_len(n) - 1)` (0..n-1, Python's `range(n)`), `for (i in 1:10)`, `for (x in v)`. Comments `#`.
* **Functions:** `f <- function(a, b = 1) {
  …
  a + b
}` (the last value is returned; `return(x)` for early returns).
  Define them before top-level code calls them. Change a top-level variable from a function with `<<-`.
* **Vectors / lists:** `v <- c(1, 2, 3)`, `v <- c()` / `list()`, first element `v[[1]]`, last `v[[length(v)]]`,
  append `v[[length(v) + 1]] <- x`, insert at the front `v <- c(x, v)`, remove `v <- v[-i]`, size `length(v)`,
  membership `x %in% v`, list of lists `list(c(1, 2), c(3, 4))` with `grid[[x]][[y]]`.
* **Maps (dictionaries):** `m <- list(hay = 1, wood = 2)`, `m <- list()`, `m[["hay"]]` / `m$hay`,
  `m[[Items$Hay]] <- 5`, `"hay" %in% names(m)`, `for (k in names(m)) print(m[[k]])`, remove `m[[k]] <- NULL`,
  `length(m)`; a missing key reads as NULL (`is.null(m$k)`).
* **Sets:** no set type; use a vector kept unique: `if (!(x %in% s)) s[[length(s) + 1]] <- x`, test with `x %in% s`,
  remove with `s <- s[-match(x, s)]`.
* **Positions / tuples:** no tuples; `p <- measure()` then `p[[1]]`, `p[[2]]`; `comp <- get_companion()` then
  `comp[[1]]` (plant) and `comp[[2]][[1]]` / `comp[[2]][[2]]` (x / y); use `paste(x, y)` as a map key for a cell;
  return several values as `c(x, y)`.
* **Imports / other files:** `source("utils.R")` at the top (another language: `source("Utils.java")`, `source("bot.py")`),
  then call `helper()` and read `limit` by their bare names.
* **spawn_drone:** `spawn_drone(worker)` (a top-level function's name) or `spawn_drone(function() worker(3))`; a function that
  makes tasks: `make_task <- function(col) function() farm_column(col)`; `is.null(drone)` when no drone was spawned.
* **Printing:** `print(x)` (one value), `cat("count:", n, "\n")` (several values), `quick_print(a, b)`, text with
  `paste("a", n)` / `paste0("a=", n)`, `as.character(n)`.
* **Constants:** `Entities$Carrot`, `Items$Hay`, `Grounds$Soil`, `Unlocks$Speed`, `Hats$Straw_Hat`, directions `North`
  … bare; game functions snake_case: `can_harvest()`, `get_world_size()`, `num_items(Items$Hay)`.
* **None:** `NULL`; test with `is.null(x)` / `!is.null(x)` (never `x == NULL`). Booleans `TRUE` / `FALSE` (not T / F).
* **Numbers:** `%/%` (floor division), `%%` (modulo), `^` (power), `floor`, `ceiling`, `sqrt`, `abs`, `min`, `max`,
  `random()`; no `round()` (use `floor(x + 0.5)`).

## 17. Performance

Compile runs on every keystroke. A ~300-line R file (RRobustnessTests.R300) compiles in about the time of the
300-line Java benchmark on the Debug test build (p25 ≈ 4.4–4.9 ms on a busy machine; the limit is 5 ms). About 60%
of the time is the shared core (IrTypeInference, IrEmitter, IrDeclarations); the R parts avoid extra walks: the
usage-kind analysis walks the tree once and repeats only the nodes that matter, builtin calls and comparisons get
their types at lowering (inference skips them), the post-inference checks visit only the nodes they check, and
globals / parameter copies are found without walking whole functions.
