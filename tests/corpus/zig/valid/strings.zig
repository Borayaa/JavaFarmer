//! Strings are []const u8: compare with std.mem.eql, print with {s}.
const std = @import("std");
const farm = @import("farm");

fn label(n: usize) []const u8 {
    if (n < 4) {
        return "small";
    }
    return "large";
}

pub fn main() void {
    const name: []const u8 = label(farm.getWorldSize());
    if (std.mem.eql(u8, name, "small")) {
        farm.quickPrint("a {s} farm\n", .{name});
    } else {
        std.debug.print("a {s} farm of {d} tiles\n", .{ name, farm.getWorldSize() * farm.getWorldSize() });
    }
    const words = [_][]const u8{ "hay", "wood", "carrot" };
    for (words) |w| {
        std.debug.print("{s}\n", .{w});
    }
    std.debug.print("{d} words\n", .{words.len});
}
