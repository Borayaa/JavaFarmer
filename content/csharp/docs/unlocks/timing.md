# Timing
If you really want to optimize your methods you need to understand how time is measured in this game. This unlock is all about that.

## New Methods
There are two useful methods to measure how long things take:

`GetTime()` returns the time in seconds since the start of the game.

`GetTickCount()` returns the number of ticks performed since the start of execution.

`double start = GetTime();
Harvest();
double timePassed = GetTime() - start;`

These two methods as well as `QuickPrint()`, are completely free. Even the call operation is free for them.

## Runtime Details

### Heads-Up
This is not how performance works in the real world. These are just rules made up for this game to have a consistent and understandable timing model.
You will probably only care about this if you want to hyper-optimize your code.

JavaFarmer translates your C# code into the game's own language before it runs, and the rules below apply to the translated code. Most C# statements translate one to one, so the rules apply to your C# code as you would expect. A few things can cost a bit more than they look, for example `/` and `%` on `int`s that might be negative, `(int)` casts, `Console.Write` or `switch`.

The basic unit of time for code execution is called a "tick". Without speed upgrades and power, the execution proceeds at a rate of `400` ticks per second.

In general, operations that combine two values such as `+, -, *, /, %, &&, ||, ...` take one tick to run.
Single value `-` and `!` are free.
An `if` branch also takes one tick to run (in addition to the time it takes to evaluate the condition expression).
Method calls and variable reads and writes are free, but every method definition takes 1 tick when the window starts or is loaded.
Using another window (like `Utils.PlantRow()`) is free.
If a method has been passed around (like an `Action` in a variable or parameter), calling it costs 1 tick instead of 0.
`for`, `foreach` and `while` loops take one tick to start, but the iterations are free (not counting the time to evaluate the condition and the update).
`return`, `break` and `continue` are all free.
Indexing into a datastructure (like `array[i]`, `list[i]` or `dict[key]`) takes one tick for the index operation and, in the case of a `Dictionary` or `HashSet`, additional ticks depending on the size of the key.

The number of ticks that builtin methods take to execute is documented in the documentation of each method on an individual basis.
