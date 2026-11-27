# Timing
If you really want to optimize your functions you need to understand how time is measured in this game. This unlock is all about that.

## New Functions
There are two useful functions to measure how long things take:

`get_time()` returns the time in seconds since the start of the game.

`get_tick_count()` returns the number of ticks performed since the start of execution.

`double start = get_time();
harvest();
double time_passed = get_time() - start;`

These two functions as well as `quick_print()`, are completely free. Even the call operation is free for them.

## Runtime Details

### Heads-Up
This is not how performance works in the real world. These are just rules made up for this game to have a consistent and understandable timing model.
You will probably only care about this if you want to hyper-optimize your code.

JavaFarmer translates your C++ code into the game's own language before it runs, and the rules below apply to the translated code. Most C++ statements translate one to one, so the rules apply to your C++ code as you would expect. A few things can cost a bit more than they look, for example `/` and `%` on `int`s that might be negative, `(int)` casts, or copying a vector (`std::vector<int> b = a;` and vector parameters without `&` make a copy).

The basic unit of time for code execution is called a "tick". Without speed upgrades and power, the execution proceeds at a rate of `400` ticks per second.

In general, operations that combine two values such as `+, -, *, /, %, &&, ||, ...` take one tick to run.
Single value `-` and `!` are free.
An `if` branch also takes one tick to run (in addition to the time it takes to evaluate the condition expression).
Function calls and variable reads and writes are free, but every function definition (and every lambda) takes 1 tick when it is created.
Prototypes and `extern` declarations are free, and so is using a function or global of another window.
If a function has been passed around (like a `std::function` in a variable or parameter), calling it costs 1 tick instead of 0.
`for` and `while` loops take one tick to start, but the iterations are free (not counting the time to evaluate the condition and the update).
`return`, `break` and `continue` are all free.
Indexing into a datastructure (like `v[i]` or `m[key]`) takes one tick for the index operation and, in the case of a `std::map` or `std::set`, additional ticks depending on the size of the key.

The number of ticks that builtin functions take to execute is documented in the documentation of each function on an individual basis.
