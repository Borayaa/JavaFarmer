// ERROR: labeled loops are not supported in JavaFarmer
// AT: outer: while
const farm = @import("farm");

pub fn main() void {
    outer: while (true) {
        _ = farm.harvest();
        break :outer;
    }
}
