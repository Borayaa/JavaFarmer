# Name Scopes
Scopes determine which variables can be accessed from where. A scope is basically a mapping from names to values.

There is a global scope, each function has a local scope, and in JavaScript every block `{ }` has its own scope too.
When you declare a variable with `let` or `const`, it gets added to the current scope.
Anything outside of a function definition is considered part of the global scope.

`const x = 1;`
Declares the name `x` in the global scope and gives it the value `1`.

`function f() {
	// declares y in the local scope of f
	const y = 1;
	// declares the function g in the local scope of f
	function g() {
	}
}
f();`
This `function` statement declares the function `f` in the global scope. The call `f()` retrieves the function stored in `f` from the global scope and calls it.

Writing `console.log(y);` after this code in the global scope is an error (`y is not defined`), because `y` was never declared in the global scope so we can't read it here.
It only existed in the local scope of `f`.

## Global variables
Variables declared at the top level of a file are global variables. Every function in the file can read and change them, you don't need a special keyword for that:

`let x = 0;

function f() {
	x = 1;
}
f();
console.log(x);`

This prints `1`, because `f` changes the global `x`. (Python needs the `global` keyword for this, JavaScript doesn't.)

But if a function declares its own variable with the same name, the local variable hides the global one:

`let x = 0;

function f() {
	let x = 1;
}
f();
console.log(x);`

This code prints `0` because the local `x` inside `f` is not the same variable as the global `x`, so the global `x` remains unchanged. This is important because otherwise a function call could accidentally override a global variable that just happens to have the same name as a local variable of that function.

Note that changing global variables is usually the first step towards spaghetti code, where every part of the program affects every other part of the program, so don't overuse it.

A function defined inside another function (or an arrow function) can read the local variables of the outer function. In JavaFarmer it can't assign them though (`assigning a captured variable inside a closure is not supported in JavaFarmer`). Use a global variable or return the new value instead.

## Loops and branches
Unlike in Python, loops and branches create their own scopes. A variable declared with `let` or `const` inside a block only exists inside that block, from its declaration to the closing `}`:

`if (canHarvest()) {
	const y = 2;
	console.log(y);
}`

Using `y` after the closing `}` is an error. This is also true for loops: the loop variable of `for (let i = 0; i < 3; i++)` only exists inside the loop.
If you need the value after the loop, declare the variable before the loop:

`let i = 0;
for (i = 0; i < 3; i++) {
}
console.log(i);`

This prints `3`, because the loop stops when `i < 3` is no longer `true`.

You can't declare the same name twice in the same scope: `let x = 1;` followed by `let x = 2;` gives `Identifier 'x' has already been declared`. To change the value, just assign it: `x = 2;`.

(The old `var` keyword ignores blocks: a `var` belongs to the whole function, or to the file at the top level. Prefer `let` and `const`.)