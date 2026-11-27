//! Every drone farms one column; the main drone farms the last one itself.
const farm = @import("farm");

fn farmColumn() void {
    for (0..farm.getWorldSize()) |_| {
        if (farm.canHarvest()) {
            _ = farm.harvest();
        }
        _ = farm.move(.North);
    }
}

pub fn main() void {
    farm.clear();
    for (0..farm.getWorldSize()) |_| {
        if (farm.spawnDrone(farmColumn) == null) {
            farmColumn();
        }
        _ = farm.move(.East);
    }
    while (farm.numDrones() > 1) {
        farm.doAFlip();
    }
}
