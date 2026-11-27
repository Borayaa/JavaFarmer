//! Sort a row of cacti with bubble sort using measure() and swap().
const std = @import("std");
const farm = @import("farm");

fn sortRow() void {
    const n = farm.getWorldSize();
    var swapped = true;
    while (swapped) {
        swapped = false;
        for (0..n - 1) |_| {
            const here = farm.measure(i32);
            const next = farm.measureDirection(i32, .East);
            if (here > next) {
                _ = farm.swap(.East);
                swapped = true;
            }
            _ = farm.move(.East);
        }
        for (0..n - 1) |_| {
            _ = farm.move(.West);
        }
    }
}

pub fn main() void {
    const size = farm.getWorldSize();
    var heights: std.ArrayList(i32) = .empty;
    defer heights.deinit(farm.allocator);
    for (0..size) |_| {
        if (farm.getGroundType() != .Soil) {
            farm.till();
        }
        _ = farm.plant(.Cactus);
        heights.append(farm.allocator, farm.measure(i32)) catch unreachable;
        _ = farm.move(.East);
    }
    var tallest: i32 = 0;
    for (heights.items) |h| {
        tallest = @max(tallest, h);
    }
    farm.quickPrint("tallest {d}\n", .{tallest});
    sortRow();
    _ = farm.harvest();
}
