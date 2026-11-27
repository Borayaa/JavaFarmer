// ERROR: defer is only supported for list.deinit(...) / map.deinit(); this defer is not supported in JavaFarmer
// AT: defer farm.clear();
const farm = @import("farm");

pub fn main() void {
    defer farm.clear();
    _ = farm.harvest();
}
