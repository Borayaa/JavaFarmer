// ERROR: cannot assign to constant
// AT: moves
const farm = @import("farm");

pub fn main() void {
    const moves: i32 = 0;
    moves = 5;
    farm.quickPrint("{d}\n", .{moves});
}
