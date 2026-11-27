# Functions
Define a new function by writing its return type, its name and its parameters:
`void f(int arg1, bool arg2) {
	// function code
}`

You can use the call operator `()` to call the function:
`f(42, false);`

Also see [Scopes](docs/scripting/scopes.md) to learn about local and global variables in functions.

## Introduction
You've already seen built-in functions like `harvest()`, and you have already written one function yourself: `main`.
You can also define your own functions which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

Functions are defined next to `main`, not inside it. C does not allow a function inside another function.

## Function Definitions
For example, you could define a function that moves the drone several times.

`void move_n_dir(int n, Direction dir) {
	for (int i = 0; i < n; i++) {
		move(dir);
	}
}`

`void` means that the function does not return a value (see below).
`move_n_dir` is the name of the function. This can be any valid name and will be used to call the function.
`n` and `dir` are parameters. They are variables that hold the values that are passed into the function (these values are also called arguments). Every parameter needs a type, just like a variable. You can add as many parameters to a function definition as you want, separated by commas. A function without parameters is written with `(void)`.
After the parentheses comes the code block in `{ }` that will run when the function is called.

With the above definition the following program moves the drone `10` tiles `North` and `2` tiles `West`.

`#include "farm.h"

void move_n_dir(int n, Direction dir) {
	for (int i = 0; i < n; i++) {
		move(dir);
	}
}

int main(void) {
	move_n_dir(10, North);
	move_n_dir(2, West);
}`

C reads the file from top to bottom, so a function must be known before it is called. Write it above `main`, or write only its first line followed by a semicolon (a prototype) above `main` and the full function below:

`void flip_twice(void);

void use_it(void) {
	flip_twice();
}

void flip_twice(void) {
	do_a_flip();
	do_a_flip();
}`

If you call a function that C doesn't know yet, or with the wrong number or types of arguments, the editor shows an error right away. For example `move_n_dir(North);` gives `too few arguments to function 'move_n_dir'`.

## Return Values
Use the `return` keyword to make a function return a value. Instead of `void`, write the type of the returned value in front of the function name.
For example, the following function defines the exclusive or operation. The exclusive or returns `true` if one value is `true` and the other one is `false`:

`bool xor(bool a, bool b) {
	return a != b;
}`

`if (xor(true, false)) {
	do_a_flip();
}`

`return` ends the function immediately. In a `void` function you can use `return;` without a value to leave the function early.

A function can only return one value. If you need to return several values, see [Tuples](docs/scripting/tuples.md).

## Default Arguments
C has no default arguments: every call must pass a value for every parameter. A function that only needs a fixed value can call a more general function:

`void flips(int n) {
	for (int i = 0; i < n; i++) {
		do_a_flip();
	}
}

void flip_once(void) {
	flips(1);
}`

Now `flip_once()` does one flip and `flips(3)` does three.
(C also doesn't let two functions share a name, so give each function its own name.)

## Advanced Function Usage
Functions can be passed around as values with a function pointer. The type `void (*task)(void)` reads as "`task` points to a function that takes no parameters and returns nothing". You get such a value by writing the name of a function without `()`, and you call it like a normal function:

`void harvest_north(void) {
	harvest();
	move(North);
}

void repeat(void (*task)(void), int times) {
	for (int i = 0; i < times; i++) {
		task();
	}
}`

`repeat(do_a_flip, 10);
repeat(harvest_north, 10);`

This code does 10 flips and then harvests the next 10 tiles to the north.
This is also how you tell a new drone what to do (see the Megafarm unlock): `spawn_drone(harvest_north)`.

A function pointer always has a fixed type. For a function that takes an `int` and returns an `int`, write `int (*g)(int)`.
