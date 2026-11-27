// ERROR: cannot assign to constant
// AT: p
const farm = @import("farm");

pub fn main() void {
    const p: farm.Position = .{ .x = 1, .y = 2 };
    p.x = 3;
    farm.quickPrint("{d}\n", .{p.x});
}
