// ERROR: bitwise operators are not supported in JavaFarmer
// AT: <<
const farm = @import("farm");

pub fn main() void {
    const big: i64 = 1 << 10;
    farm.quickPrint("{d}\n", .{big});
}
