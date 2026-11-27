//! switch statements and expressions on integers, with lists of values and else.
const std = @import("std");

fn name(n: i32) []const u8 {
    return switch (n) {
        0 => "zero",
        1, 2, 3 => "small",
        else => "big",
    };
}

fn score(n: i32) i32 {
    var s: i32 = 0;
    switch (@mod(n, 4)) {
        0 => s = 10,
        1 => {
            s = 20;
            s += n;
        },
        else => s = -1,
    }
    return s;
}

pub fn main() void {
    var k: i32 = 0;
    while (k < 6) : (k += 1) {
        std.debug.print("{d} {s} {d}\n", .{ k, name(k), score(k) });
    }
    const grade: i32 = 87;
    const letter: []const u8 = if (grade >= 90) "A" else if (grade >= 80) "B" else "C";
    std.debug.print("grade {s}\n", .{letter});
}
