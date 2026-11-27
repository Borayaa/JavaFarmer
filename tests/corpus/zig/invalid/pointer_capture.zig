// ERROR: pointer captures (|*x|) are not supported in JavaFarmer
// AT: *v
const std = @import("std");

pub fn main() void {
    var values = [_]i32{ 1, 2, 3 };
    for (&values) |*v| {
        v.* += 1;
    }
    std.debug.print("{any}\n", .{values});
}
