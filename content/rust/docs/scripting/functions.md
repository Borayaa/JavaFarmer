# Functions
Use the `fn` keyword to define a new function:
`fn f(arg1: i32, arg2: bool) {
	// function code
}`

You can use the call operator `()` to call the function:
`f(42, false);`

Also see [Scopes](docs/scripting/scopes.md) to learn about local and global variables in functions.

## Introduction
You've already seen built-in functions like `harvest()`, and you have already written one function yourself: `main`.
You can also define your own functions which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

Functions are written next to `fn main()`, not inside it.

## Function Definitions
For example, you could define a function that moves the drone several times.

`fn move_n_dir(n: i32, dir: Direction) {
	for _ in 0..n {
		move_to(dir);
	}
}`

The `fn` keyword signals that this is a function definition. 
`move_n_dir` is the name of the function. This can be any valid name and will be used to call the function. Rust function names are written in snake_case: lowercase words joined by `_`.
`n` and `dir` are parameters. They are variables that hold the values that are passed into the function (these values are also called arguments). Every parameter needs a type, written after a colon: `n: i32` is a whole number and `dir: Direction` is a direction. You can add as many parameters to a function definition as you want, separated by commas.
After the parentheses comes the code block in `{ }` that will run when the function is called.

With the above definition the following program moves the drone `10` tiles `North` and `2` tiles `West`.

`use farm::*;

fn main() {
	move_n_dir(10, North);
	move_n_dir(2, West);
}

fn move_n_dir(n: i32, dir: Direction) {
	for _ in 0..n {
		move_to(dir);
	}
}`

Unlike in Python, it doesn't matter in which order the functions are written in the file. `main` can call `move_n_dir` even though `move_n_dir` is defined below it.

If you call a function with the wrong number or types of arguments, the editor shows an error right away: `move_n_dir(North);` gives `this function takes 2 arguments but 1 argument was supplied`.

## Return Values
To make a function return a value, write `->` and the type of the value after the parentheses.
For example, the following function defines the exclusive or operation. The exclusive or returns `true` if one value is `true` and the other one is `false`:

`fn xor(a: bool, b: bool) -> bool {
	a != b
}

if xor(true, false) {
	do_a_flip();
}`

The last expression of the function, written without a `;`, is the value it returns. You can also write `return a != b;`, which does the same.
`return` ends the function immediately, so it is handy to leave a function early. In a function without `->`, write `return;` without a value.

A function with a return type must return a value on every path, otherwise you get the error `mismatched types`.

[Tuples](docs/scripting/tuples.md) allow returning multiple values.

## Default Arguments
Rust has no default arguments: a call always passes a value for every parameter.
That's why some game functions come in two versions. `use_item(Item::Fertilizer)` uses one item and `use_item_with(Item::Fertilizer, 2)` says how many. In the same way there are `measure()` and `measure_with(North)`.

You can do the same with your own functions. A function that only needs a fixed value can call a more general function:

`fn flips(n: i32) {
	for _ in 0..n {
		do_a_flip();
	}
}

fn flip_once() {
	flips(1);
}`

Now `flip_once()` does one flip and `flips(3)` does three.

## Advanced Function Usage
Functions are values just like any other value. The type `fn()` is a function without parameters and without a return value, `fn(i32) -> bool` takes an `i32` and returns a `bool`.
A closure is a function without a name, written with bars: `|| do_a_flip()` takes no arguments, `|x: i32| x + 1` adds one to its argument.
This allows doing things like this:

`fn f() -> fn() {
	|| do_a_flip()
}

f()();`

Here `f()` calls the function `f` which creates and returns a new function. The second `()` then executes the returned function and performs a flip.
(Doing these sort of things is usually not a good idea because it's hard to see what's going on)

Functions that take other functions as arguments let you get really creative:

`fn harvest_north() {
	harvest();
	move_to(North);
}

fn repeat(task: fn(), times: i32) {
	for _ in 0..times {
		task();
	}
}

repeat(do_a_flip, 10);
repeat(harvest_north, 10);
repeat(|| {
	use_item(Item::Fertilizer);
}, 10);`

This code does 10 flips, then harvests the next 10 tiles to the north and then uses fertilizer 10 times.
This is also how you tell a new drone what to do (see the Megafarm unlock): `spawn_drone(harvest_north)`.
