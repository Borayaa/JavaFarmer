// / on signed integers is ambiguous for negative numbers.
// ERROR: signed integers must use @divTrunc, @divFloor, or @divExact
// AT: /
const farm = @import("farm");

pub fn main() void {
    const total: i32 = @intCast(farm.getWorldSize());
    const half = total / 2;
    farm.quickPrint("{d}\n", .{half});
}
