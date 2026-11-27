# Timing
If you really want to optimize your methods you need to understand how time is measured in this game. This unlock is all about that.

## New Functions
There are two useful functions to measure how long things take:

`farm.getTime()` returns the time in seconds since the start of the game.

`farm.getTickCount()` returns the number of ticks performed since the start of execution.

Both return an `f64`.

`const start = farm.getTime();
_ = farm.harvest();
const time_passed = farm.getTime() - start;
farm.quickPrint("{d}\n", .{time_passed});`

These two functions as well as `farm.quickPrint()`, are completely free. Even the call operation is free for them.

## Runtime Details

### Heads-Up
This is not how performance works in the real world. These are just rules made up for this game to have a consistent and understandable timing model.
You will probably only care about this if you want to hyper-optimize your code.

JavaFarmer translates your Zig code into the game's own language before it runs, and the rules below apply to the translated code. Most Zig statements translate one to one, so the rules apply to your Zig code as you would expect. A few things can cost a bit more than they look, for example `std.debug.print` with several values (the pieces are joined together), `orelse` and `if (optional) |value|` (a check for `null`), or casts like `@intCast`.

The basic unit of time for code execution is called a "tick". Without speed upgrades and power, the execution proceeds at a rate of `400` ticks per second.

In general, operations that combine two values such as `+, -, *, /, %, and, or, ...` (also `@divFloor`, `@mod` and friends) take one tick to run.
Single value `-` and `!` are free.
An `if` branch also takes one tick to run (in addition to the time it takes to evaluate the condition expression).
Function calls and variable reads and writes are free but function definitions (`fn`) take 1 tick.
`@import` is free.
Accessing an imported file with the `.` operator (like `utils.plantRow()`) is free.
If a function has been passed around (like `farm.spawnDrone(task)` or `const op = square;`), using it will cost 1 tick instead of 0.
`for` and `while` loops take one tick to start, but the iterations are free (not counting the time to evaluate the condition/sequence expressions).
`return`, `break` and `continue` are all free.
An empty block `{}` as the body of a loop or `if` takes one tick, so it can be used to create precise delays: `for (0..10) |_| {}` waits about 10 ticks.
Indexing into a datastructure (like `array[i]`, `list.items[i]` or `map.get(key)`) takes one tick for the index operator and, in the case of an `std.AutoHashMap`, additional ticks depending on the size of the key.

The number of ticks that builtin functions take to execute is documented in the documentation of each function on an individual basis.