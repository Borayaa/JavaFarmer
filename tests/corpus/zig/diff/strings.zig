//! Strings ([]const u8): {s}, std.mem.eql, arrays of strings, bools and optionals in prints.
const std = @import("std");

fn plural(n: i32) []const u8 {
    if (n == 1) return "carrot";
    return "carrots";
}

pub fn main() void {
    const names = [_][]const u8{ "hay", "wood", "hay", "carrot" };
    var hay: i32 = 0;
    for (names) |n| {
        if (std.mem.eql(u8, n, "hay")) hay += 1;
    }
    std.debug.print("{d} times hay of {d}\n", .{ hay, names.len });
    var c: i32 = 0;
    while (c < 3) : (c += 1) {
        std.debug.print("{d} {s}\n", .{ c, plural(c) });
    }
    const nothing: ?i32 = null;
    const some: ?i32 = 4;
    std.debug.print("{any} {any} {} {}\n", .{ nothing, some, true, 1 > 2 });
    std.debug.print("{s}{s}!\n", .{ "many ", plural(2) });
}
