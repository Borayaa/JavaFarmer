// ERROR: unable to resolve comptime value
// AT: g
const std = @import("std");

var g: i32 = 1;
const h = g + 1;

pub fn main() void {
    g += 1;
    std.debug.print("{d}\n", .{h});
}
