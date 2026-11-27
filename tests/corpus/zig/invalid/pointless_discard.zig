// ERROR: pointless discard of local constant
// AT: n
const farm = @import("farm");

pub fn main() void {
    const n = farm.getWorldSize();
    farm.quickPrint("{d}\n", .{n});
    _ = n;
}
