// ERROR: array multiplication ('**', removed in Zig 0.17; use @splat or std.math.pow) is not supported in JavaFarmer
// AT: **
const farm = @import("farm");

pub fn main() void {
    const zeros = [_]i32{0} ** 4;
    farm.quickPrint("{any}\n", .{zeros});
}
