// append can fail (out of memory): write try (in pub fn main() !void) or catch unreachable.
// ERROR: error union is ignored
// AT: list.append(farm.allocator, 1)
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    var list: std.ArrayList(i32) = .empty;
    list.append(farm.allocator, 1);
    farm.quickPrint("{d}\n", .{list.items.len});
}
