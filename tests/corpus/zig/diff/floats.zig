//! f64 arithmetic, conversions and comparisons (values chosen to print the same in Zig and Python).
const std = @import("std");

fn average(values: []const f64) f64 {
    var sum: f64 = 0;
    for (values) |v| {
        sum += v;
    }
    return sum / @as(f64, @floatFromInt(values.len));
}

pub fn main() void {
    const xs = [_]f64{ 1.5, 2.5, 4.0 };
    std.debug.print("average {d}\n", .{average(&xs)});
    const half: f64 = 7.0 / 2.0;
    const whole: i32 = @intFromFloat(half);
    std.debug.print("{d} {d}\n", .{ half, whole });
    const n: i32 = 9;
    const ratio = @as(f64, @floatFromInt(n)) / 4;
    std.debug.print("ratio {d} {any}\n", .{ ratio, ratio > 2 });
    const a: f64 = 0.1;
    const b: f64 = 0.2;
    std.debug.print("{d}\n", .{a + b});
}
