# Name Scopes
Scopes determine which variables can be accessed from where.

## Local variables
A variable declared with `let` inside a function is a local variable. It only exists inside the block `{ }` where it is declared, from the line of the declaration to the closing `}` of that block.

`use farm::*;

fn main() {
	let x = 1;
	if can_harvest() {
		let y = 2;
		println!("{}", x + y);
	}
	println!("{}", y);
}`

The last line is an error (`cannot find value y in this scope`) because `y` only exists inside the block of the `if`.
This is also true for loops. The loop variable of a `for` loop only exists inside the loop, so `i` can't be used after `for i in 0..3 { }`.

You can declare a new variable with the same name as an old one. The new one hides the old one from then on (this is called shadowing):
`let x = 1;
let x = x + 1;`
`x` is now `2`. To change the value of a variable instead, declare it with `let mut` and assign it: `x = 2;`.

Every function has its own local variables, even if they have the same name. A function can't see the local variables of another function, not even those of `main`. Parameters are local variables too.

`use farm::*;

fn main() {
	let x = 0;
	f();
	println!("{}", x);
}

fn f() {
	let x = 1;
}`

This code prints `0` because the local `x` inside `f` is not the same variable as the `x` in `main`, so the `x` in `main` remains unchanged.

To give a value to a function, pass it as a parameter. To get a value back, return it:

`fn add_one(x: i32) -> i32 {
	x + 1
}

let mut x = 0;
x = add_one(x);`

## Global variables
A `const` that is declared outside of all functions can be read by every function in the file. Its type must be written, and its name is written in capitals:

`const MAX_FLIPS: i32 = 3;

fn flips() {
	for _ in 0..MAX_FLIPS {
		do_a_flip();
	}
}`

Python needs a `global` keyword to change a global variable from inside a function. Rust uses `static mut` for a global variable that can change. Every use of it must be inside an `unsafe { }` block, because Rust can't check that two parts of a program don't change it at the same time:

`static mut X: i32 = 0;

fn f() {
	unsafe {
		X = 1;
	}
}

f();
println!("{}", unsafe { X });`

This prints `1`, because `f` changes the global `X`. A `static mut` can only hold simple values like numbers, `bool`s or game values, not a `Vec` or a `HashMap`.
Note that changing global variables from many functions is usually the first step towards spaghetti code, where every part of the program affects every other part of the program, so prefer parameters and return values.

## Loops and branches
Loops and branches create their own scopes, so anything declared within them can't be used outside. If you need a value after the loop, declare the variable before the loop:

`let mut last = 0;
for i in 0..3 {
	last = i;
}
println!("{}", last);`

This will print `2` because the last iteration of the `for` loop assigned `2` to `last`.
