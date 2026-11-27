// The values of std.debug.print go into a tuple: .{ x }
// ERROR: expected tuple or struct argument, found 'comptime_int'
// AT: 5
const std = @import("std");

pub fn main() void {
    std.debug.print("{d}\n", 5);
}
