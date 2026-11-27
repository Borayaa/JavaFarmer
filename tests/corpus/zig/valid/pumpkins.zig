//! Replant dead pumpkins until the whole field is one big pumpkin.
const std = @import("std");
const farm = @import("farm");

var dead_x: std.ArrayList(usize) = .empty;
var dead_y: std.ArrayList(usize) = .empty;

fn goTo(x: usize, y: usize) void {
    while (farm.getPosX() != x) {
        _ = farm.move(.East);
    }
    while (farm.getPosY() != y) {
        _ = farm.move(.North);
    }
}

fn plantAll() void {
    const n = farm.getWorldSize();
    for (0..n) |_| {
        for (0..n) |_| {
            if (farm.getGroundType() != .Soil) {
                farm.till();
            }
            _ = farm.plant(.Pumpkin);
            dead_x.append(farm.allocator, farm.getPosX()) catch unreachable;
            dead_y.append(farm.allocator, farm.getPosY()) catch unreachable;
            _ = farm.move(.North);
        }
        _ = farm.move(.East);
    }
}

pub fn main() void {
    farm.clear();
    plantAll();
    while (dead_x.items.len > 0) {
        const x = dead_x.orderedRemove(0);
        const y = dead_y.orderedRemove(0);
        goTo(x, y);
        const here = farm.getEntityType();
        if (here != null and here.? == .Pumpkin and farm.canHarvest()) {
            continue;
        }
        _ = farm.plant(.Pumpkin);
        dead_x.append(farm.allocator, x) catch unreachable;
        dead_y.append(farm.allocator, y) catch unreachable;
    }
    _ = farm.harvest();
}
