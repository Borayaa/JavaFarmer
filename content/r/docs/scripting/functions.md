# Functions
Use the `function` keyword to define a new function and `<-` to give it a name:
`f <- function(arg1, arg2 = FALSE) {
	do_a_flip()
}`

You can use the call operator `()` to call the function:
`f(42)`

Also see [Scopes](docs/scripting/scopes.md) to learn about local and global variables in functions.

## Introduction
You've already seen built-in functions like `harvest()`.
You can also define your own functions which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

## Function Definitions
For example, you could define a function that moves the drone several times.

`move_n_dir <- function(n, dir) {
	for (i in seq_len(n)) {
		move(dir)
	}
}`

The `function` keyword signals that this is a function definition. 
`move_n_dir` is the name that the function gets bound to. This can be any valid variable name and will be used to call the function.
`n` and `dir` are parameters. They are variables that hold the values that are passed into the function (These values are also called arguments). You can add as many parameters to a function definition as you want.
Between the curly braces `{` and `}` comes the code block that will run when the function is called.

With the above definition the following code then moves the drone `10` tiles `North` and `2` tiles `West`.

`move_n_dir(10, North)
move_n_dir(2, West)`

You can also name the arguments when you call a function: `move_n_dir(dir = West, n = 2)` does the same as `move_n_dir(2, West)`.

When you see `f <- function() { ... }` you should really think of it as a variable assignment, because that's exactly what it is: a new function is created and stored in the variable `f`.
Like with all assignments, you can't use the variable before it was assigned!
The definition has to run before any function calls.
This code will throw an error:

`func()
func <- function() {
	do_a_flip()
}`

## Return Values
A function returns the value of the last expression in its body. 
For example, the following function defines the exclusive or operation. The exclusive or returns `TRUE` if one value is `TRUE` and the other one is `FALSE`:

`xor <- function(a, b) {
	a != b
}

if (xor(TRUE, FALSE)) {
	do_a_flip()
}`

Use `return(value)` to leave a function early and return a value:

`first_bush_x <- function() {
	for (x in seq_len(get_world_size()) - 1) {
		if (get_entity_type() == Entities$Bush) {
			return(x)
		}
		move(East)
	}
	NULL
}`

A function can only return one value. To return several values, return a vector like `c(x, y)` (see [Tuples](docs/scripting/tuples.md)).

## Default Arguments
You can also assign default values that will be used if no arguments are passed.

`f <- function(a = FALSE) {
	if (a) {
		do_a_flip()
	}
}

f()

f(TRUE)`

## Advanced Function Usage
Functions are values just like any other value, and `function` just creates such a value that the assignment `<-` stores under a name.
This allows doing things like this:

`f <- function() {
	d <- function() {
		do_a_flip()
	}
	d
}

f()()`

Here `f()` calls the function `f` which defines and returns a new function `d`. The second `()` then executes the returned function and performs flip.
(Doing these sort of things is usually not a good idea because it's hard to see what's going on)

Functions that take other functions as arguments let you get really creative:

`f <- function(g, arg) {
	for (x in seq_len(10)) {
		g(arg)
	}
}

f(move, North)
f(use_item, Items$Fertilizer)`

This code moves the drone `North` 10 times and then uses fertilizer 10 times.

A function without a name is written the same way: `function(x) x * 2` is a function that doubles its argument. A one-line function needs no curly braces.
