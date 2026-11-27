//! Drones return a value; the main drone waits for all of them.
const std = @import("std");
const farm = @import("farm");

var total: i32 = 0;

fn countCarrots() i32 {
    var found: i32 = 0;
    for (0..farm.getWorldSize()) |_| {
        const here = farm.getEntityType();
        if (here != null and here.? == .Carrot) {
            found += 1;
        }
        _ = farm.move(.North);
    }
    return found;
}

pub fn main() void {
    var drones: std.ArrayList(farm.Drone) = .empty;
    defer drones.deinit(farm.allocator);
    for (0..farm.maxDrones() - 1) |_| {
        if (farm.spawnDrone(countCarrots)) |d| {
            drones.append(farm.allocator, d) catch unreachable;
        }
        _ = farm.move(.East);
    }
    for (drones.items) |d| {
        total += farm.waitFor(i32, d);
    }
    std.debug.print("carrots: {d}\n", .{total});
    farm.quickPrint("{d}\n", .{total * 2});
}
