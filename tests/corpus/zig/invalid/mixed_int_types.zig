// usize and i32 do not mix in arithmetic: cast with @intCast.
// ERROR: incompatible types: 'usize' and 'i32'
// AT: farm.getPosX() + offset
const farm = @import("farm");

pub fn main() void {
    const offset: i32 = 2;
    const target = farm.getPosX() + offset;
    farm.quickPrint("{d}\n", .{target});
}
