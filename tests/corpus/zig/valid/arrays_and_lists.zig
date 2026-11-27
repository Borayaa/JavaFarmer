//! Fixed arrays, slices and ArrayList operations.
const std = @import("std");
const farm = @import("farm");

fn sum(values: []const i32) i32 {
    var s: i32 = 0;
    for (values) |v| {
        s += v;
    }
    return s;
}

pub fn main() !void {
    var grid = [_]i32{ 0, 0, 0, 0 };
    for (0..grid.len) |i| {
        grid[i] = farm.measure(i32);
        _ = farm.move(.East);
    }
    var list: std.ArrayList(i32) = .empty;
    defer list.deinit(farm.allocator);
    try list.append(farm.allocator, sum(&grid));
    try list.append(farm.allocator, grid[0]);
    try list.insert(farm.allocator, 0, 7);
    const last = list.pop();
    if (last) |v| {
        farm.quickPrint("popped {d}\n", .{v});
    }
    list.items[0] = 9;
    _ = list.orderedRemove(0);
    farm.quickPrint("{d} items, first {d}, {any}\n", .{ list.items.len, list.items[0], list.items });
}
