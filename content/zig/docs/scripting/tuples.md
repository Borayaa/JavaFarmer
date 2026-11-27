# Tuples
Tuples are a great way to combine multiple values into a single value.
In Zig an anonymous tuple is written with a dot and curly braces, the values separated by commas:

`const tuple = .{ 1, 2 };`

You can also unpack them into several variables again. Inside a function, `const a, const b = .{ 1, 2 };` unpacks the tuple `.{ 1, 2 }` into two constants `a` and `b`.

Tuples can be indexed like arrays, starting at `0`. The values of a tuple can have different types, like `.{ farm.Entity.Bush, 3 }`.

`const pair = .{ 1, 2 };
std.debug.print("{d}\n", .{pair[1]});`
prints `2`

A tuple in a `const` can't be changed after creation: `pair[0] = 3;` is an error. Create a new tuple instead.

To print a whole tuple, use `{any}`: `std.debug.print("{any}\n", .{pair});` prints `.{ 1, 2 }`.

## Positions
The game uses a small struct for positions: a `farm.Position` holds an x and a y coordinate. Its values have names instead of numbers:

`const p = farm.Position{ .x = 1, .y = 2 };
std.debug.print("{d} {d}\n", .{ p.x, p.y });`

`p.x` and `p.y` are `usize`s, like `farm.getPosX()` and `farm.getPosY()`.
<unlock=mazes>For example, `farm.measure(?farm.Position)` in a maze returns the position of the treasure. `measure` can return different kinds of values, so you tell it which type you want. It is an optional because there might be no treasure:

`if (farm.measure(?farm.Position)) |treasure| {
    std.debug.print("treasure at {d}, {d}\n", .{ treasure.x, treasure.y });
}`

</unlock><unlock=polyculture>`farm.getCompanion()` returns a `?farm.Companion`. A `farm.Companion` holds the type of plant (`.entity`) and where it wants to be (`.x` and `.y`):

`if (farm.getCompanion()) |c| {
    std.debug.print("{} at {d}, {d}\n", .{ c.entity, c.x, c.y });
}`

</unlock><unlock=dicts>
Unlike a `std.ArrayList`, a position can be used as a key in a `std.AutoHashMap`. Two positions are equal if their coordinates are equal.

`var d = std.AutoHashMap(farm.Position, farm.Position).init(farm.allocator);
defer d.deinit();
try d.put(.{ .x = 1, .y = 2 }, .{ .x = 4, .y = 5 });

std.debug.print("{any}\n", .{d.get(.{ .x = 1, .y = 2 }).?});`
prints `.{ .x = 4, .y = 5 }`</unlock>

They can also be useful for returning multiple values in a function. The return type `struct { i32, i32 }` is a tuple of two `i32`s:

`const std = @import("std");

fn f() struct { i32, i32 } {
    return .{ 1, 2 };
}

pub fn main() void {
    const x, const y = f();
    std.debug.print("{d} {d}\n", .{ x, y });
}`
