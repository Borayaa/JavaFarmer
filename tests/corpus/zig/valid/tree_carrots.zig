//! Trees on a checkerboard, carrots in between, water when dry.
const farm = @import("farm");

const water_level = 0.75;

fn tend() void {
    const here = farm.getEntityType();
    if (here != null and farm.canHarvest()) {
        _ = farm.harvest();
    }
    if (farm.getWater() < water_level and farm.numItems(.Water) > 0) {
        _ = farm.useItem(.Water);
    }
    if ((farm.getPosX() + farm.getPosY()) % 2 == 0) {
        _ = farm.plant(.Tree);
    } else {
        if (farm.getGroundType() != .Soil) {
            farm.till();
        }
        _ = farm.plant(.Carrot);
    }
}

pub fn main() void {
    farm.clear();
    const n = farm.getWorldSize();
    while (true) {
        for (0..n * n) |_| {
            tend();
            if (farm.getPosY() == n - 1) {
                _ = farm.move(.East);
            }
            _ = farm.move(.North);
        }
    }
}
