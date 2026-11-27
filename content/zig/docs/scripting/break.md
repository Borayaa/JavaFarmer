# Break
`break` allows stopping a loop early. When the `break;` statement is reached it will immediately exit the innermost loop and start running the code after the loop.

`for (0..10) |i| {
    if (i == 3) {
        break;
    }
    std.debug.print("{d}\n", .{i});
}`
This prints `0`, `1` and `2`. When `i` reaches `3` the `break` ends the loop.

The loop variable `i` of a `for` loop only exists inside the loop. Using it after the loop is an error (`use of undeclared identifier 'i'`).
If you need a value after the loop, declare a `var` before the loop:

`var n: i32 = 0;
while (n < 10) : (n += 1) {
    break;
}
std.debug.print("{d}\n", .{n});`
This prints `0` because `n` is `0` in the first iteration of the loop and then the break statement ends the loop. A `break` also skips the continue expression `(n += 1)`.

It also works on `while` loops without a continue expression.

`while (true) {
    if (farm.canHarvest()) {
        break;
    }
}
_ = farm.harvest();`

This code runs the `while` loop until `farm.canHarvest()` is `true` and then harvests. 
It has the same effect as

`while (!farm.canHarvest()) {}
_ = farm.harvest();`

An empty block `{}` is a loop body that does nothing.

In nested loops `break` always exits the innermost loop.

`for (0..10) |_| {
    for (0..10) |j| {
        if (j == 0) {
            break;
        }
        std.debug.print("this is never printed\n", .{});
    }
    std.debug.print("this is printed 10 times\n", .{});
}`

Zig does not allow code directly after a `break;` in the same block, because it could never run. The compiler reports this as `unreachable code`. That's why the examples put `break;` inside an `if`.
