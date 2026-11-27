//! A library file (no main).
const farm = @import("farm");

pub var planted: i32 = 0;

pub fn tendTile() void {
    if (farm.canHarvest()) {
        _ = farm.harvest();
    }
    if (farm.plant(.Bush)) {
        planted += 1;
    }
}
