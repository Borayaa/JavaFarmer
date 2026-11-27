# Functions
Define a new function by writing its return type, its name and its parameters:
`void f(int arg1, bool arg2 = false) {
	// function code
}`

You can use the call operator `()` to call the function:
`f(42);`

Also see [Scopes](docs/scripting/scopes.md) to learn about local and global variables in functions.

## Introduction
You've already seen built-in functions like `harvest()`, and you have already written one function yourself: `main`.
You can also define your own functions which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

Functions are defined next to `main`, not inside it.

## Function Definitions
For example, you could define a function that moves the drone several times.

`void move_n_dir(int n, Direction dir) {
	for (int i = 0; i < n; i++) {
		move(dir);
	}
}`

`void` means that the function does not return a value (see below).
`move_n_dir` is the name of the function. This can be any valid name and will be used to call the function.
`n` and `dir` are parameters. They are variables that hold the values that are passed into the function (these values are also called arguments). Every parameter needs a type, just like a variable. You can add as many parameters to a function definition as you want, separated by commas.
After the parentheses comes the code block in `{ }` that will run when the function is called.

With the above definition the following code then moves the drone `10` tiles `North` and `2` tiles `West`.

`move_n_dir(10, North);
move_n_dir(2, West);`

C++ reads your file from top to bottom, and a function has to be declared before you can call it.
If `main` calls a function that is only defined further down, the editor shows the error `'func' was not declared in this scope`.
So either define your functions above `main`, or declare them first with a prototype: the first line of the function, ending with a `;` instead of the code block.

`void func();

int main() {
	func();
}

void func() {
	do_a_flip();
}`

## Return Values
Use the `return` keyword to make a function return a value. Instead of `void`, write the type of the returned value in front of the function name.
For example, the following function defines the exclusive or operation. The exclusive or returns `true` if one value is `true` and the other one is `false`:

`bool exclusive_or(bool a, bool b) {
	return a != b;
}

int main() {
	if (exclusive_or(true, false)) {
		do_a_flip();
	}
}`

`return` ends the function immediately. In a `void` function you can use `return;` without a value to leave the function early.

A function can only return one value. If you need to return several values, see [Position (Tuples)](docs/scripting/tuples.md).

## Default Arguments
You can also assign default values that will be used if no arguments are passed.

`void flips(bool really = false) {
	if (really) {
		do_a_flip();
	}
}

int main() {
	flips();
	flips(true);
}`

A parameter that has a default value cannot be followed by a parameter that doesn't have a default value.

## Advanced Function Usage
Functions are values just like any other value: you can store a function in a variable, pass it to another function or return it.
The type of a function value is `std::function` (from `#include <functional>`): `std::function<void()>` is a function without parameters that returns nothing, `std::function<int(int)>` takes an `int` and returns an `int`.
You can write the name of a function without `()` to use it as a value, or create a new function on the spot with a lambda: `[&] { do_a_flip(); }`. The `[&]` starts the lambda (it may use the variables around it), the code block follows. A function defined inside another function is always a lambda.
This allows doing things like this:

`std::function<void()> f() {
	auto d = [&] {
		do_a_flip();
	};
	return d;
}

int main() {
	f()();
}`

Here `f()` calls the function `f` which creates and returns a new function `d`. The second `()` then executes the returned function and performs a flip.
(Doing these sort of things is usually not a good idea because it's hard to see what's going on)

Functions that take other functions as arguments let you get really creative:

`void repeat(std::function<void()> task, int times) {
	for (int i = 0; i < times; i++) {
		task();
	}
}

void harvest_north() {
	harvest();
	move(North);
}

int main() {
	repeat([&] { move(North); }, 10);
	repeat(harvest_north, 10);
	repeat(do_a_flip, 10);
}`

This code moves the drone `North` 10 times, then harvests the next 10 tiles to the north and then does 10 flips.
A lambda can also take parameters in parentheses after the brackets and return a value: `auto twice = [] (int x) { return x * 2; };` makes `twice(4)` return `8`.
