// ERROR: unused function parameter
// AT: times
const farm = @import("farm");

fn flip(times: i32) void {
    farm.doAFlip();
}

pub fn main() void {
    flip(3);
}
