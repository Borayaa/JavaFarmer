// A local constant that is never used is a compile error in Zig.
// ERROR: unused local constant
// AT: size
const farm = @import("farm");

pub fn main() void {
    const size = farm.getWorldSize();
    _ = farm.harvest();
}
