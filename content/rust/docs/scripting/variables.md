# Variables
Variables can be thought of as named containers that can hold a value.
In Rust you declare a variable with `let`, its name, `=` and the value to store in it: `let variable_name = value;`

The left hand side is the variable name. You can give it any name you want. In Rust, variable names are written in "snake_case": lowercase words joined by `_`, like `plant_count`.
The right hand side is an expression whose resulting value will be stored in the variable.

Declare a variable named `a` and store the value `5` in it:
`let a = 5;`
Declare a variable named `b` and store the return value of `can_harvest()` in it:
`let b = can_harvest();`

## Types
Every variable has a type that says what kind of value it can hold. Rust usually figures out the type from the value, but you can also write it after the name with a colon.
These are the types you will use most:

`i32` is a whole number like `5`, `0` or `-3`.
`f64` is a number with a decimal point like `0.25` or `2.0`.
`bool` is `true` or `false`.
`&str` is a piece of text in double quotes like `"hello"`. `String` is a text that you build while the program runs.

The game has its own types too:
`Entity` for things like `Entity::Bush`, `Item` for things like `Item::Hay`, `Ground` for things like `Ground::Soil` and `Direction` for `North`, `East`, `South` and `West`.

`let count: i32 = 0;
let water: f64 = 0.25;
let ready: bool = can_harvest();
let greeting: &str = "hello";
let next_plant: Entity = Entity::Bush;
let dir: Direction = North;`

The type protects you from mistakes. If you try to put a value of the wrong type into a variable, the code editor shows an error before the program even runs: `let a: i32 = "hello";` gives the error `mismatched types`.

Rust never converts numbers by itself. Use `as` to convert between `i32` and `f64`:

`let d = 5 as f64;
let i = 2.7 as i32;`
`d` is now `5.0` and `i` is now `2`. `as i32` cuts off the decimal places.

## Assigning new values
Do not confuse the `=` operator with the `==` operator. 
The `==` operator checks whether two values are equal and returns `true` or `false`.
The `=` operator assigns the value on the right to the variable on the left.

A variable declared with `let` can't be changed. If you want to assign a new value later, declare it with `let mut` (mutable):

`let mut a = 5;
a = 7;`

Without `mut`, `a = 7;` gives the error `cannot assign twice to immutable variable`.

After a variable has been assigned you can use it in the code to retrieve the value it contains

`let a = 5;
for i in 0..a {
	do_a_flip();
}`

The above loop is executed 5 times because `a` is set to `5`.
The `i` in the `for` loop is also a variable that is automatically assigned the current value of the sequence at each iteration of the loop. (It doesn't have to be called `i`, you can give it any valid variable name.)

Variables also let you do the same thing with a while loop:

`let a = 5;
let mut i = 0;
while i < a {
	do_a_flip();
	i = i + 1;
}`

This does the same thing as the for loop above. We just have to increment `i` manually.
Note that to increment `i`, we set it to be its own value plus `1`. Changing the value of a variable based on its previous value is something very common. 
It can be abbreviated using these operators: `+=, -=, *=, /=, %=`

`i = i + 1;` is the same as `i += 1;`
`a = a / 3;` is the same as `a /= 3;`

Rust has no `++` operator, write `i += 1;` instead.

A variable can only be used inside the block (the `{ }`) where it was declared. See [Scopes](docs/scripting/scopes.md) for more.

## Constants
A value that never changes can be declared as a constant with `const`, outside of all functions. A constant always needs its type, and its name is written in capitals:
`const MAX_FLIPS: i32 = 3;`
Assigning a new value to `MAX_FLIPS` is an error.
