//! Check what we can afford before unlocking.
const std = @import("std");
const farm = @import("farm");

fn canAfford(u: farm.Unlock) bool {
    const cost = farm.getCost(u) orelse return false;
    var it = cost.iterator();
    while (it.next()) |entry| {
        if (farm.numItems(entry.key_ptr.*) < entry.value_ptr.*) {
            return false;
        }
    }
    return true;
}

pub fn main() void {
    const wanted = [_]farm.Unlock{ .Speed, .Expand, .Plant };
    for (wanted) |u| {
        if (canAfford(u) and farm.numUnlocked(u) < 5) {
            _ = farm.unlock(u);
        }
    }
    if (farm.getCompanion()) |c| {
        farm.quickPrint("{any} {d} {d}\n", .{ c.entity, c.x, c.y });
    }
    farm.changeHat(.Straw_Hat);
    farm.setExecutionSpeed(2);
    const t = farm.getTime();
    farm.quickPrint("ticks {d} time {d}\n", .{ farm.getTickCount(), t });
}
