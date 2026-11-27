# If
You can use `if`, `else if` and `else` to run code conditionally.

On this page, `condition`, `condition1` and `condition2` stand for any `bool` value. To try the examples, declare them first, for example like this:

`const condition = farm.canHarvest();
const condition1 = farm.getPosX() == 0;
const condition2 = farm.getEntityType() == null;`

`if (condition1) {
    farm.doAFlip();
} else if (condition2) {
    _ = farm.harvest();
} else {
    farm.doAFlip();
    _ = farm.harvest();
}`

## Syntax
`if`s allow you to run code only if some condition is `true`. They are like a `while` loop that doesn't loop.
The `if` takes a condition in parentheses just like the `while` loop and executes the code block in `{ }` if the condition evaluates to `true`:

`// do a flip if condition is true
if (condition) {
    farm.doAFlip();
}`

The condition must be a `bool`. Zig doesn't treat numbers or other values as true or false, so write `if (count > 0)`, not `if (count)`.

You can also add an `else` after the if that defines code to be executed if the condition evaluates to `false`.

Do a flip if `condition` is true, otherwise harvest.
`if (condition) {
    farm.doAFlip();
} else {
    _ = farm.harvest();
}`

You can put another `if` inside the `else` block:

`if (condition1) {
    // a
} else {
    if (condition2) {
        // b
    } else {
        // c
    }
}`

This can be shortened by writing `else if`:

`if (condition1) {
    // a
} else if (condition2) {
    // b
} else {
    // c
}`

You can add as many `else if` blocks as you want. Only the first block whose condition is `true` runs.

Zig also allows leaving out the `{ }` when there is only one statement, like `if (condition) farm.doAFlip();`, but with braces it is easier to see what belongs to the `if`.

## If as a value
An `if` with an `else` can also produce a value. Then the two parts are values instead of blocks, and there is no `;` inside:

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    const flips: i32 = if (farm.canHarvest()) 1 else 2;
    const next_plant: farm.Entity = if (farm.getPosX() == 0) .Bush else .Grass;
    std.debug.print("{d}\n", .{ flips });
    _ = farm.plant(next_plant);
}`

`flips` is `1` if the plant can be harvested and `2` otherwise.
In JavaFarmer an `if` value must be a whole value: the whole right side of a declaration or an assignment, a `return` value or a whole argument of a function call. It can't be part of a bigger expression like `1 + if (condition) 1 else 2`.
