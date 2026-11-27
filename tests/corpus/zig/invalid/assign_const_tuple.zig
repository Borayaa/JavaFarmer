// ERROR: cannot assign to constant
// AT: t
const std = @import("std");

pub fn main() void {
    const t: struct { i32, i32 } = .{ 1, 2 };
    t[0] = 5;
    std.debug.print("{any}\n", .{t});
}
