// Strings are compared with std.mem.eql.
// ERROR: operator == not allowed for type '[]const u8'
// AT: name == other
const std = @import("std");
const farm = @import("farm");

fn pick(n: usize) []const u8 {
    return if (n > 3) "big" else "small";
}

pub fn main() void {
    const name = pick(farm.getWorldSize());
    const other = pick(2);
    if (name == other) {
        farm.quickPrint("same\n", .{});
    }
}
