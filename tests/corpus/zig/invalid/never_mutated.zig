// var that is never changed must be const.
// ERROR: local variable is never mutated
// AT: count
const farm = @import("farm");

pub fn main() void {
    var count: i32 = 3;
    farm.quickPrint("{d}\n", .{count});
}
