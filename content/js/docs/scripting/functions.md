# Functions
Use the `function` keyword to define a new function:
`function f(arg1, arg2 = false) {
	// function code
}`

You can use the call operator `()` to call the function:
`function f(arg1, arg2 = false) {
}
f(42);`

Also see [Scopes](docs/scripting/scopes.md) to learn about local and global variables in functions.

## Introduction
You've already seen built-in functions like `harvest()`.
You can also define your own functions which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

## Function Definitions
For example, you could define a function that moves the drone several times.

`function moveNDir(n, dir) {
	for (let i = 0; i < n; i++) {
		move(dir);
	}
}`

The `function` keyword signals that this is a function definition. 
`moveNDir` is the name of the function. This can be any valid variable name and will be used to call the function.
`n` and `dir` are parameters. They are variables that hold the values that are passed into the function (these values are also called arguments). You can add as many parameters to a function definition as you want, separated by commas.
After the parentheses comes the code block in `{ }` that will run when the function is called.

With the above definition the following code then moves the drone `10` tiles `North` and `2` tiles `West`.

`moveNDir(10, North);
moveNDir(2, West);

function moveNDir(n, dir) {
	for (let i = 0; i < n; i++) {
		move(dir);
	}
}`

In JavaScript, it doesn't matter where in the file a `function` definition is written. The code above can call `moveNDir` even though the function is defined below the calls.

Real JavaScript doesn't check the number of arguments, but the game does. If you call a function with the wrong number of arguments, the editor shows an error right away: with the definition above, `moveNDir(North);` gives `moveNDir() expects 2 arguments, but got 1`.

## Return Values
Use the `return` keyword to make a function return a value. 
For example, the following function defines the exclusive or operation. The exclusive or returns `true` if one value is `true` and the other one is `false`:

`function xor(a, b) {
	return a !== b;
}

if (xor(true, false)) {
	doAFlip();
}`

`return` ends the function immediately. `return;` without a value leaves the function early. A function that ends without `return` gives back `null`.

A function can only return one value, but that value can be an array. See [Positions](docs/scripting/tuples.md) to learn how to return several values.

## Default Arguments
You can also assign default values that will be used if no arguments are passed.

`function f(a = false) {
	if (a) {
		doAFlip();
	}
}

f();

f(true);`

An argument that has a default value cannot be followed by an argument that doesn't have a default value.

## Advanced Function Usage
Functions are values just like any other value. You can store them in variables, pass them to other functions and return them from functions.
This allows doing things like this:

`function f() {
	function d() {
		doAFlip();
	}
	return d;
}

f()();`

Here `f()` calls the function `f` which defines and returns a new function `d`. The second `()` then executes the returned function and performs flip.
(Doing these sort of things is usually not a good idea because it's hard to see what's going on)

Functions that take other functions as arguments let you get really creative:

`function f(g, arg) {
	for (let i = 0; i < 10; i++) {
		g(arg);
	}
}

f(move, North);
f(useItem, Items.Fertilizer);`

This code moves the drone `North` 10 times and then uses fertilizer 10 times.

## Arrow Functions
A short function without a name can be written as an arrow function. `(x) => x * 2` is a function that takes `x` and returns `x * 2`. With a code block it looks like this:

`const flipTwice = () => {
	doAFlip();
	doAFlip();
};
flipTwice();`

Arrow functions are handy when you pass a small piece of code to another function, for example to tell a new drone what to do (see the Megafarm unlock).