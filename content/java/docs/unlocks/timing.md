# Timing
If you really want to optimize your methods you need to understand how time is measured in this game. This unlock is all about that.

## New Methods
There are two useful methods to measure how long things take:

`getTime()` returns the time in seconds since the start of the game.

`getTickCount()` returns the number of ticks performed since the start of execution.

`double start = getTime();
harvest();
double timePassed = getTime() - start;`

These two methods as well as `quickPrint()`, are completely free. Even the call operation is free for them.

## Runtime Details

### Heads-Up
This is not how performance works in the real world. These are just rules made up for this game to have a consistent and understandable timing model.
You will probably only care about this if you want to hyper-optimize your code.

JavaFarmer translates your Java code into the game's own language before it runs, and the rules below apply to the translated code. Most Java statements translate one to one, so the rules apply to your Java code as you would expect. A few things can cost a bit more than they look, for example `/` and `%` on `int`s that might be negative, or `(int)` casts.

The basic unit of time for code execution is called a "tick". Without speed upgrades and power, the execution proceeds at a rate of `400` ticks per second.

In general, operations that combine two values such as `+, -, *, /, %, &&, ||, ...` take one tick to run.
Single value `-` and `!` are free.
An `if` branch also takes one tick to run (in addition to the time it takes to evaluate the condition expression).
Method calls and variable reads and writes are free, but every method definition takes 1 tick when the file is loaded.
Using another file (like `Utils.plantRow()`) is free.
If a task has been passed around (like a `Runnable` in a variable or parameter), calling it costs 1 tick instead of 0.
`for` and `while` loops take one tick to start, but the iterations are free (not counting the time to evaluate the condition and the update).
`return`, `break` and `continue` are all free.
Indexing into a datastructure (like `array[i]`, `list.get(i)` or `map.get(key)`) takes one tick for the index operation and, in the case of a `HashMap` or `HashSet`, additional ticks depending on the size of the key.

The number of ticks that builtin methods take to execute is documented in the documentation of each method on an individual basis.
