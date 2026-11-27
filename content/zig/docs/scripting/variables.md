# Variables
Variables can be thought of as named containers that can hold a value.
In Zig you declare a variable with `const` or `var`, its name, a `:` and its type, then `=` and the value to store in it. Like every statement, a declaration ends with `;`.

`var plant_count: i32 = 0;`

The left hand side of the `=` is the variable name and its type. You can give it any name you want. In Zig, variable names are usually written in "snake_case": lowercase words joined by `_`, like `plant_count`.
The right hand side is an expression whose resulting value will be stored in the variable.

Declare a constant named `a` and store the value `5` in it:
`const a = 5;`
Declare a constant named `b` and store the return value of `farm.canHarvest()` in it:
`const b = farm.canHarvest();`

## const and var
A `const` can't be changed after it was declared. A `var` can.
Use `const` unless you really change the value later. Zig is strict about this: a `var` that is never changed is the error `local variable is never mutated`, and a local variable that is never used is the error `unused local constant` (or `unused local variable`).

## Types
These are the types you will use most:

`i32` is a whole number like `5`, `0` or `-3`.
`usize` is a whole number that can't be negative. The game uses it for positions, sizes and counts: `farm.getPosX()`, `farm.getWorldSize()` and `farm.numDrones()` return a `usize`.
`f64` is a number with a decimal point like `0.25` or `2.0`. `farm.numItems()` and `farm.getTime()` return an `f64`.
`bool` is `true` or `false`.
`[]const u8` is a piece of text in double quotes like `"hello"`.

The game has its own types too:
`farm.Entity` for things like `farm.Entity.Bush`, `farm.Item` for things like `farm.Item.Hay`, `farm.Ground` for things like `farm.Ground.Soil` and `farm.Direction` for `.North`, `.East`, `.South` and `.West`.

`var count: i32 = 0;
const water: f64 = 0.25;
const ready: bool = farm.canHarvest();
const greeting: []const u8 = "hello";
const next_plant: farm.Entity = .Bush;
const dir = farm.Direction.North;`

When the type is written, the short form `.Bush` is enough, because Zig already knows that it must be a `farm.Entity`.
You can leave out the type if Zig can figure it out from the value: `dir` above is a `farm.Direction` and `b` is a `bool`.
This doesn't work for a `var` with a plain number: `var x = 0;` gives the error `variable of type 'comptime_int' must be const or comptime`, because Zig doesn't know which kind of number you want. Write the type: `var x: i32 = 0;`.

The type protects you from mistakes. If you try to put a value of the wrong type into a variable, the code editor shows an error before the program even runs: `const a: i32 = "hello";` gives the error `expected type 'i32', found '*const [5:0]u8'`.

Zig also never mixes different number types by itself. `farm.getPosX() + a` with an `i32` `a` gives the error `incompatible types: 'usize' and 'i32'`. Convert the value first:

`const x: i32 = @intCast(farm.getPosX());
const hay: f64 = farm.numItems(.Hay);
const whole_hay: i32 = @intFromFloat(hay);
const size: f64 = @floatFromInt(farm.getWorldSize());`

`@intCast` turns one kind of whole number into another, `@intFromFloat` turns an `f64` into a whole number (the decimal places are cut off) and `@floatFromInt` turns a whole number into an `f64`. They take the type from the declaration, so the type must be written there. A smaller whole number type fits into a bigger one without a conversion, and `@as(i64, x)` makes that explicit.

## Assigning new values
Do not confuse the `=` operator with the `==` operator. 
The `==` operator checks whether two values are equal and returns `true` or `false`.
The `=` operator assigns the value on the right to the variable on the left.

You only write `var` and the type once, when you declare the variable. After that you can assign a new value without them:

`var a: i32 = 5;
a = 7;`

After a variable has been assigned you can use it in the code to retrieve the value it contains

`const a: usize = 5;
for (0..a) |_| {
    farm.doAFlip();
}`

The above loop is executed 5 times because `a` is set to `5`.
The `_` between the `| |` means that this loop doesn't need the number of the current round. Write a name instead, for example `|i|`, and `i` is a constant that is automatically assigned the current value of the sequence at each iteration of the loop. (It doesn't have to be called `i`, you can give it any valid variable name.)

Variables also let you do the same thing with a while loop:

`const a: i32 = 5;
var i: i32 = 0;
while (i < a) {
    farm.doAFlip();
    i = i + 1;
}`

This does the same thing as the for loop above. We just have to increment `i` manually.
Note that to increment `i`, we set it to be its own value plus `1`. Changing the value of a variable based on its previous value is something very common. 
It can be abbreviated using these operators: `+=, -=, *=, /=, %=`

`i = i + 1;` is the same as `i += 1;`
`a = a / 3;` is the same as `a /= 3;`

Zig has no `++` or `--`, so write `i += 1;` and `i -= 1;`.
`/=` works on `usize` and `f64` variables and `%=` only on `usize` variables. For an `i32` you have to choose how to round, so write `a = @divTrunc(a, 3);` or `a = @mod(a, 3);` instead (and `@mod` for the remainder of an `f64`). See [Operators](docs/scripting/operators.md) for why.

A variable can only be used inside the block (the `{ }`) where it was declared. A variable declared outside of all functions is a top-level variable that every function can use. See [Scopes](docs/scripting/scopes.md) for more.
