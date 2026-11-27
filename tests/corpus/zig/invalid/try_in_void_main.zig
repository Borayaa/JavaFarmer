// try needs a function that can return an error: pub fn main() !void
// ERROR: expected type 'void', found 'error{OutOfMemory}'
// AT: try
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    var list: std.ArrayList(i32) = .empty;
    try list.append(farm.allocator, 1);
    farm.quickPrint("{d}\n", .{list.items.len});
}
