// ERROR: list.pop() is only supported as a whole value
// AT: list.pop()
const std = @import("std");
const farm = @import("farm");

pub fn main() !void {
    var list: std.ArrayList(i32) = .empty;
    defer list.deinit(farm.allocator);
    try list.append(farm.allocator, 1);
    std.debug.print("{any}\n", .{list.pop()});
}
