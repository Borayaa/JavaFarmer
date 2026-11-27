# Timing
If you really want to optimize your methods you need to understand how time is measured in this game. This unlock is all about that.

## New Functions
There are two useful functions to measure how long things take:

`get_time()` returns the time in seconds since the start of the game, as an `f64`.

`get_tick_count()` returns the number of ticks performed since the start of execution.

`let start = get_time();
harvest();
let time_passed = get_time() - start;`

These two functions as well as `quick_print!()`, are completely free. Even the call operation is free for them.

## Runtime Details

### Heads-Up
This is not how performance works in the real world. These are just rules made up for this game to have a consistent and understandable timing model.
You will probably only care about this if you want to hyper-optimize your code.

JavaFarmer translates your Rust code into the game's own language before it runs, and the rules below apply to the translated code. Most Rust statements translate one to one, so the rules apply to your Rust code as you would expect. A few things can cost a bit more than they look, for example `/` and `%` on integers (JavaFarmer adds a small helper so that they work like in Rust) or `as` casts.

The basic unit of time for code execution is called a "tick". Without speed upgrades and power, the execution proceeds at a rate of `400` ticks per second.

In general, operations that combine two values such as `+, -, *, /, %, &&, ||, ...` take one tick to run.
Single value `-` and `!` are free.
An `if` branch also takes one tick to run (in addition to the time it takes to evaluate the condition expression).
Function calls and variable reads and writes are free, but every `fn` definition takes 1 tick when the file is loaded.
`use` and `mod` lines are free.
Using another code window (like `utils::plant_row()`) is free.
If a function has been passed around (like a `fn` value or a closure in a variable or parameter), calling it costs 1 tick instead of 0.
`for`, `while` and `loop` loops take one tick to start, but the iterations are free (not counting the time to evaluate the condition/sequence expressions).
`return`, `break` and `continue` are all free.
Indexing into a datastructure (like `v[i]` or `m[&key]`) takes one tick for the index operation and, in the case of a `HashMap` or `HashSet`, additional ticks depending on the size of the key.

The number of ticks that builtin functions take to execute is documented in the documentation of each function on an individual basis.
