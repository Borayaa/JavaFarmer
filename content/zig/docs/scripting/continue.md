# Continue
`continue` allows stopping the current iteration of a loop and jumping to the next iteration of the innermost loop.

`for (0..10) |i| {
    if (i % 2 == 0) {
        continue;
    }
    std.debug.print("{d}\n", .{i});
}`

This runs all `10` iterations of the loop, but the `std.debug.print` statement is skipped for every even `i`. So it prints `1, 3, 5, 7, 9`.
Like with `break`, Zig does not allow code directly after a `continue;` in the same block (`unreachable code`), so `continue;` usually goes inside an `if`.

It also works on `while` loops.

`while (true) {
    if (!farm.canHarvest()) {
        continue;
    }
    _ = farm.harvest();
}`

This code only calls `farm.harvest()` when `farm.canHarvest()` is `true`. 
It has the same effect as

`while (true) {
    if (farm.canHarvest()) {
        _ = farm.harvest();
    }
}`

In a `while` loop with a continue expression, like `while (i < 10) : (i += 1)`, `continue` still runs the continue expression before the next iteration.

In nested loops `continue` always affects the innermost loop.

`for (0..10) |_| {
    for (0..10) |j| {
        std.debug.print("this is printed 100 times\n", .{});
        if (j < 10) {
            continue;
        }
        std.debug.print("this is never printed\n", .{});
    }
    std.debug.print("this is printed 10 times\n", .{});
}`
