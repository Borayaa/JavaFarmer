// ERROR: unable to resolve comptime value
// AT: farm.getWorldSize()
const std = @import("std");
const farm = @import("farm");

const n = farm.getWorldSize();

pub fn main() void {
    std.debug.print("{d}\n", .{n});
}
