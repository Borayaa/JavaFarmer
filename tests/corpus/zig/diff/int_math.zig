//! Integer division and remainder with negative numbers: @divTrunc, @divFloor, @rem, @mod.
const std = @import("std");

pub fn main() void {
    const values = [_]i32{ 7, -7 };
    const divisors = [_]i32{ 2, -2, 3 };
    for (values) |a| {
        for (divisors) |b| {
            std.debug.print("{d} {d}: trunc {d} floor {d} rem {d} mod {d}\n", .{ a, b, @divTrunc(a, b), @divFloor(a, b), @rem(a, b), @mod(a, b) });
        }
    }
    const u: usize = 17;
    std.debug.print("{d} {d}\n", .{ u / 5, u % 5 });
    var x: i32 = 100;
    x -= 58;
    x *= 2;
    x = @divTrunc(x, 4);
    std.debug.print("x = {d}\n", .{x});
    const big: i64 = 1024 * 1024 * 1024 * 1024;
    std.debug.print("{d} {d}\n", .{ big, @abs(-12) });
    std.debug.print("{d} {d}\n", .{ @min(3, 9), @max(-4, -8) });
}
