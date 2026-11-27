// ERROR: root source file struct 'farm' has no member named 'can_harvest'
// AT: can_harvest
const farm = @import("farm");

pub fn main() void {
    if (farm.can_harvest()) {
        _ = farm.harvest();
    }
}
