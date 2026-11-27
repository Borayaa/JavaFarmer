# Name Scopes
Scopes determine which variables can be accessed from where. A scope is basically a mapping from names to values.

In Zig, every block `{ }` has its own scope: the body of a function, of an `if`, of an `else` and of a loop.
There is also the top level of the file: everything outside of all functions.

## Local variables
A variable declared inside a function is a local variable. It only exists inside the block `{ }` where it is declared, from the line of the declaration to the closing `}` of that block.

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    const x: i32 = 1;
    if (farm.canHarvest()) {
        const y: i32 = 2;
        std.debug.print("{d}\n", .{ x + y });
    }
}`

Inside the block of the `if` you can use both `x` and `y`.
If you add the line `std.debug.print("{d}\n", .{ y });` after the closing `}` of the `if`, you get the error `use of undeclared identifier 'y'`, because `y` only exists inside the block of the `if`.
If you need a value after the block, declare the variable before the block and change it inside:

`var y: i32 = 0;
if (farm.canHarvest()) {
    y = 2;
}
std.debug.print("{d}\n", .{ y });`

Every function has its own local variables. A local variable `x` in one function has nothing to do with a local variable `x` in another function. Parameters are local constants of their function.

## No shadowing
Zig doesn't let you declare a name that already exists in a scope around it. This code
`const x: i32 = 1;
if (farm.canHarvest()) {
    const z: i32 = x + 1;
    std.debug.print("{d}\n", .{ z });
}`
is fine, but if you called the inner constant `x` too, you would get the error `local constant 'x' shadows local constant from outer scope`. The same goes for names declared at the top level of the file: a local variable can't have the same name as a top-level variable or function (`local constant 'x' shadows declaration of 'x'`).
So there is never any doubt which `x` you mean. To change a variable, assign to it (`x = 2;`) instead of declaring it again.

## Top-level variables
A variable that is declared outside of all functions is a top-level variable. This is Zig's version of a global variable: every function in the file can read it, and if it is a `var`, every function can change it.

`const std = @import("std");
const farm = @import("farm");

var x: i32 = 0;

pub fn main() void {
    f();
    std.debug.print("{d}\n", .{ x });
}

fn f() void {
    x = 1;
}`

This prints `1`, because `f` changes the top-level variable `x`.

In Python a function needs the global keyword to change a global variable, otherwise it creates a new local variable with the same name. Zig doesn't have that keyword and doesn't need it: a function can't declare a local variable with the same name as a top-level variable (see above), so `x = 1;` can only mean the top-level `x`. A function can never change a top-level variable by accident just because one of its local variables happens to have the same name.
(The game runs your code as Python. JavaFarmer adds the global lines for you.)

A top-level `const` can be read everywhere but never changed. Use it for values that are fixed, like `const field_size: usize = 6;`.

Note that changing top-level variables from many functions is usually the first step towards spaghetti code, where every part of the program affects every other part of the program, so don't overuse it.

## Loops and branches
Loops and branches create their own scopes too. The loop variable of a `for` loop (the name between the `| |`) only exists inside the loop:

`for (0..3) |i| {
    std.debug.print("{d}\n", .{ i });
}`

Using `i` after the closing `}` gives the error `use of undeclared identifier 'i'`. If you need the last value after the loop, declare a variable before the loop:

`var last: usize = 0;
for (0..3) |i| {
    last = i;
}
std.debug.print("{d}\n", .{ last });`

This prints `2` because the last iteration of the `for` loop assigned `2` to `last`.
