//! farm.simulate with empty maps (.{}), a start file global left undefined for the simulation to pass in.
const std = @import("std");
const farm = @import("farm");

var limit: i32 = undefined;
var grid: [2][2]i32 = undefined;

pub fn check() void {
    limit = 3;
    grid[1][1] = limit;
    farm.quickPrint("{d} {any}\n", .{ limit, grid });
}

pub fn main() void {
    var items = std.AutoHashMap(farm.Item, f64).init(farm.allocator);
    defer items.deinit();
    items.put(.Hay, 100) catch unreachable;
    const t = farm.simulate("f1", .{}, items, .{}, 0, 64);
    std.debug.print("{d}\n", .{t});
    check();
}
