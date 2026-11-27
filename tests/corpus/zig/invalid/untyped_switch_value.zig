// ERROR: value with comptime-only type 'comptime_int' depends on runtime control flow
// AT: switch
const farm = @import("farm");

pub fn main() void {
    const points = switch (farm.getWorldSize()) {
        3 => 1,
        else => 2,
    };
    farm.quickPrint("{d}\n", .{points});
}
