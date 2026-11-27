# For Loop
The `for` loop runs its body once for every element of a sequence. (Called a foreach loop in some languages, not to be confused with the C-style for loop, which is a different thing).

`for (i in 1:3) {
	do_a_flip()
}`

Similar to the `while` loop, the `for` loop also repeatedly calls a block of code. Instead of looping based on a condition, it executes the loop body once for each element in a sequence.

## Syntax
A for loop looks like this: `for (variable_name in sequence) { ... }`, with the code block between the curly braces `{` and `}`.

`variable_name` can be any name you choose. It is a variable that stores the current element in the sequence. `sequence` needs to be some value that can be iterated like a range of numbers. The code block is executed for every element with the loop variable assigned to that element.

## Sequences
[Ranges](functions/range)      <unlock=lists>[Lists](docs/scripting/lists.md)      </unlock><unlock=functions>[Tuples](docs/scripting/tuples.md)      </unlock><unlock=dicts>[Dictionaries](docs/scripting/dicts.md)      </unlock><unlock=sets>[Sets](docs/scripting/sets.md)</unlock>

These sequences of numbers can be used in a `for` loop:
`a:b` counts from `a` to `b`, both included. `1:5` is `1, 2, 3, 4, 5`.
`seq_len(n)` counts from `1` to `n`. It is the safest way to repeat something `n` times: if `n` is `0`, the loop doesn't run at all.
`seq_len(n) - 1` counts from `0` to `n - 1`. This is useful for positions on the farm, because they start at `0`.
`seq(a, b, by = 2)` counts from `a` to `b` in steps of `2`.

Note: in JavaFarmer, `a:b` always counts up when `a` or `b` is computed (like `1:n`). In normal R, `1:0` counts down and runs twice. Use `seq_len(n)` when `n` can be `0`.

## Example
`for (i in seq_len(5) - 1) {
	harvest()
}`

This loop executes the body a fixed number of times. It is essentially the same as writing

`i <- 0
harvest()
i <- 1
harvest()
i <- 2
harvest()
i <- 3
harvest()
i <- 4
harvest()`

So it calls `harvest()` 5 times.

See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)
