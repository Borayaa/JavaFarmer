// Every {} in the format string needs a value in the tuple.
// ERROR: too few arguments
// AT: .{1}
const std = @import("std");

pub fn main() void {
    std.debug.print("{d} {d}\n", .{1});
}
