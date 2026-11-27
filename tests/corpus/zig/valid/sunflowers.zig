//! Measure every sunflower, then harvest the ones with the most petals first.
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    var by_petals = std.AutoHashMap(i32, std.ArrayList(farm.Position)).init(farm.allocator);
    defer by_petals.deinit();
    const n = farm.getWorldSize();
    for (0..n) |x| {
        for (0..n) |y| {
            if (farm.getGroundType() != .Soil) farm.till();
            _ = farm.plant(.Sunflower);
            const petals = farm.measure(i32);
            var spots = by_petals.get(petals) orelse std.ArrayList(farm.Position).empty;
            spots.append(farm.allocator, .{ .x = x, .y = y }) catch unreachable;
            by_petals.put(petals, spots) catch unreachable;
            _ = farm.move(.North);
        }
        _ = farm.move(.East);
    }
    var petals: i32 = 15;
    while (petals >= 7) : (petals -= 1) {
        const spots = by_petals.get(petals) orelse continue;
        for (spots.items) |p| {
            farm.quickPrint("petals {d} at {d} {d}\n", .{ petals, p.x, p.y });
        }
    }
    std.debug.print("sizes: {d}\n", .{by_petals.count()});
}
