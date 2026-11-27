//! The first program: harvest hay forever.
const farm = @import("farm");

pub fn main() void {
    while (true) {
        if (farm.canHarvest()) {
            _ = farm.harvest();
        }
    }
}
