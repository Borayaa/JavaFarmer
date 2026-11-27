//! Water dry tiles and fertilize trees, using integer division and remainder builtins.
const farm = @import("farm");

fn waterIfDry(level: f64) bool {
    if (farm.getWater() < level and farm.numItems(.Water) > 0) {
        return farm.useItem(.Water);
    }
    return false;
}

pub fn main() void {
    const n: i32 = @intCast(farm.getWorldSize());
    var watered: i32 = 0;
    var i: i32 = 0;
    while (i < n * n) : (i += 1) {
        const row = @divTrunc(i, n);
        const col = @rem(i, n);
        if (waterIfDry(0.5)) {
            watered += 1;
        }
        if (@mod(row + col, 3) == 0) {
            _ = farm.plant(.Tree);
            _ = farm.useItemN(.Fertilizer, 1);
        }
        if (col == n - 1) {
            _ = farm.move(.East);
        }
        _ = farm.move(.North);
    }
    farm.quickPrint("watered {d} of {d}\n", .{ watered, n * n });
}
