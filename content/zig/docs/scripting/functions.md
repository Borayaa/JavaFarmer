# Functions
Use the `fn` keyword to define a new function. Write its name, its parameters with their types and the type of the value it returns:
`fn f(arg1: i32, arg2: bool) void {
    // function code
    _ = arg1;
    _ = arg2;
}`

You can use the call operator `()` to call the function:
`f(42, false);`

Also see [Scopes](docs/scripting/scopes.md) to learn about local variables and top-level variables.

## Introduction
You've already seen built-in functions like `farm.harvest()`, and you have already written one function yourself: `main`.
You can also define your own functions which allows structuring your code in a modular way. It basically allows you to give a name to a block of code so you can call it from anywhere you want.

Functions are defined at the top level of the file, next to `main`, not inside it.

## Function Definitions
For example, you could define a function that moves the drone several times.

`fn moveNDir(n: usize, dir: farm.Direction) void {
    for (0..n) |_| {
        _ = farm.move(dir);
    }
}`

The `fn` keyword signals that this is a function definition. 
`moveNDir` is the name of the function. This can be any valid name and will be used to call the function. In Zig, function names are usually written in "camelCase": start with a lowercase letter and start every following word with an uppercase letter.
`n` and `dir` are parameters. They are constants that hold the values that are passed into the function (these values are also called arguments). Every parameter needs a type, written after a `:` just like for a variable. You can add as many parameters to a function definition as you want, separated by commas.
`void` after the parentheses means that the function does not return a value (see below).
After that comes the code block in `{ }` that will run when the function is called.

Parameters can't be changed inside the function (that gives the error `cannot assign to constant`). If you need to change the value, copy it into a `var` first. Like every local constant, a parameter must also be used somewhere in the function, otherwise you get the error `unused function parameter`. If you really don't need it, write `_ = arg1;` like in the example at the top.

With the above definition the following program moves the drone `10` tiles `North` and `2` tiles `West`.

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    moveNDir(10, .North);
    moveNDir(2, .West);
}

fn moveNDir(n: usize, dir: farm.Direction) void {
    for (0..n) |_| {
        _ = farm.move(dir);
    }
}`

Unlike variables inside a function, it doesn't matter in which order the functions are written in the file. `main` can call `moveNDir` even though `moveNDir` is defined below it.
Because the type of `dir` is known to be `farm.Direction`, you can write the short form `.North` instead of `farm.Direction.North`.

If you call a function with the wrong number or types of arguments, the editor shows an error right away: `moveNDir(.North);` gives `expected 2 argument(s), found 1`.

## Return Values
Use the `return` keyword to make a function return a value. Instead of `void`, write the type of the returned value after the parentheses.
For example, the following function defines the exclusive or operation. The exclusive or returns `true` if one value is `true` and the other one is `false`:

`fn xor(a: bool, b: bool) bool {
    return a != b;
}`

`if (xor(true, false)) {
    farm.doAFlip();
}`

A function with a return type must return a value on every path, otherwise you get the error `function with non-void return type 'bool' implicitly returns`.

`return` ends the function immediately. In a `void` function you can use `return;` without a value to leave the function early.

Zig doesn't let you forget a returned value. If you call a function that returns something and don't use the result, you get an error like `value of type 'bool' ignored`. If you really don't need the result, throw it away with `_ =`, just like with `_ = farm.harvest();`:

`_ = xor(true, false);`

A function can only return one value. [Tuples](docs/scripting/tuples.md) allow returning multiple values.

## Default Arguments
Zig has no default values for parameters: every call must pass every argument.
If you want a short version of a function that always uses the same value, write a second function that calls the general one:

`fn flips(n: usize) void {
    for (0..n) |_| {
        farm.doAFlip();
    }
}

fn flipOnce() void {
    flips(1);
}`

Now `flipOnce()` does one flip and `flips(3)` does three.
The game's own functions do the same thing: `farm.useItem(.Fertilizer)` uses one item and `farm.useItemN(.Fertilizer, 3)` uses three.
(Zig doesn't let two functions share a name, so give each function its own name.)

## Advanced Function Usage
A function name can be used as a value in two ways.

You can give a function a second name with a constant:
`fn square(x: i32) i32 {
    return x * x;
}

const op = square;`

Now `op(3)` is the same as `square(3)`.

And you can pass a function without parameters to `farm.spawnDrone()` to tell a new drone what to do (see the Megafarm unlock):

`const std = @import("std");
const farm = @import("farm");

fn harvestNorth() void {
    for (0..10) |_| {
        _ = farm.harvest();
        _ = farm.move(.North);
    }
}

pub fn main() void {
    _ = farm.spawnDrone(harvestNorth);
    harvestNorth();
}`

Here a new drone and the first drone both harvest the next 10 tiles to the north. Note that you write `harvestNorth` without `()`: the function itself is passed, it is not called.

That's all you can do with functions as values. Zig has no closures (functions that remember the local variables around them) and doesn't let you define a function inside another function. JavaFarmer also doesn't support parameters that have a function type, so `fn f(g: fn () void) void` gives the error `function types are not supported in JavaFarmer`.
Instead of passing a function, pass a value that says what to do, and call the game function inside:

`fn repeatMove(dir: farm.Direction, times: usize) void {
    for (0..times) |_| {
        _ = farm.move(dir);
    }
}

fn repeatItem(item: farm.Item, times: usize) void {
    for (0..times) |_| {
        _ = farm.useItem(item);
    }
}`

`repeatMove(.North, 10);
repeatItem(.Fertilizer, 10);`

This code moves the drone `North` 10 times and then uses fertilizer 10 times.
