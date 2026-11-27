# For Loop
The `for` loop in Rust works like in Python: it goes through every element of a sequence. (Called a foreach loop in some languages. Rust has no C-style for loop with three parts.)

`for i in 0..5 {
	// do something with i
}`

Similar to the `while` loop, the `for` loop also repeatedly runs a block of code. Instead of looping based on a condition, it executes the loop body once for each element in a sequence.

## Syntax
A for loop looks like this:

`for variable_name in sequence {
	// code block
}`

`variable_name` can be any name you choose. It is a variable that stores the current element in the sequence. `sequence` needs to be some value that can be iterated like a range of numbers. The code block is executed for every element with the loop variable assigned to that element.
The loop variable only exists inside the loop. If you don't need it, call it `_`: `for _ in 0..5 {`

## Ranges
Rust has no range() function like Python. A range is written with two dots:
`0..5` is `0, 1, 2, 3, 4` (the end is not included).
`1..=5` is `1, 2, 3, 4, 5` (with `..=` the end is included).

You can also count in other ways:

`// 2, 4, 6, 8
for i in (2..10).step_by(2) {
	println!("{}", i);
}

// 10, 9, 8, ..., 1
for i in (1..=10).rev() {
	println!("{}", i);
}`

## Sequences
[Ranges](functions/range)      <unlock=lists>[Vec](docs/scripting/lists.md)      </unlock><unlock=dicts>[HashMap](docs/scripting/dicts.md)      </unlock><unlock=sets>[HashSet](docs/scripting/sets.md)</unlock>

## Example
`for i in 0..5 {
	harvest();
}`

This loop executes the body a fixed number of times. It is essentially the same as writing

`let mut i = 0;
harvest();
i = 1;
harvest();
i = 2;
harvest();
i = 3;
harvest();
i = 4;
harvest();`

So it calls `harvest()` 5 times.
<unlock=lists>
Going through a list looks like this:
`let numbers = Vec::from([4, 7, 2, 5]);
let mut sum = 0;
for number in &numbers {
	sum += number;
}`
`sum` is now `18`. The `&` in `&numbers` lets the loop look at the list without using it up, so you can still use `numbers` after the loop.
</unlock>
See also [Break](docs/scripting/break.md) and [Continue](docs/scripting/continue.md)
