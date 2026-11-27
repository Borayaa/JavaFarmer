// % on signed integers: use @rem or @mod.
// ERROR: remainder division with 'i32' and 'comptime_int': signed integers and floats must use @rem or @mod
// AT: %
const farm = @import("farm");

pub fn main() void {
    const x: i32 = -@as(i32, @intCast(farm.getPosX()));
    farm.quickPrint("{d}\n", .{x % 3});
}
