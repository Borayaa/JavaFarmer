//! Plant a checkerboard of bushes and grass using the drone position.
const farm = @import("farm");

pub fn main() void {
    const size = farm.getWorldSize();
    while (true) {
        for (0..size) |_| {
            for (0..size) |_| {
                if (farm.canHarvest()) {
                    _ = farm.harvest();
                }
                if ((farm.getPosX() + farm.getPosY()) % 2 == 0) {
                    _ = farm.plant(.Bush);
                } else {
                    _ = farm.plant(.Grass);
                }
                _ = farm.move(.North);
            }
            _ = farm.move(.East);
        }
    }
}
