//! Top-level constants and variables, qualified game constants and int/float conversions.
const std = @import("std");
const farm = @import("farm");

const target_hay = 1000;
const crop = farm.Entity.Carrot;
var rounds: u32 = 0;
var total: i64 = 0;

fn progress() f64 {
    return farm.numItems(farm.Item.Hay) / target_hay;
}

pub fn main() void {
    while (farm.numItems(.Hay) < target_hay) : (rounds += 1) {
        if (farm.canHarvest()) {
            _ = farm.harvest();
            total += 1;
        }
        if (farm.getGroundType() == farm.Ground.Soil) {
            _ = farm.plant(crop);
        }
    }
    const percent: i32 = @intFromFloat(progress() * 100);
    const avg = @as(f64, @floatFromInt(total)) / @as(f64, @floatFromInt(rounds));
    const size: i32 = @intCast(farm.getWorldSize());
    farm.quickPrint("{d}% after {d} rounds, {d} per round, size {d}\n", .{ percent, rounds, avg, size });
    farm.changeHat(farm.Hat.Straw_Hat);
}
