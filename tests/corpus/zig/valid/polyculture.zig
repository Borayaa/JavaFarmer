//! Plant each tile's preferred companion; remember the wishes in a map keyed by position.
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    var wishes = std.AutoHashMap(farm.Position, farm.Entity).init(farm.allocator);
    defer wishes.deinit();
    const n = farm.getWorldSize();
    const choices = [_]farm.Entity{ .Grass, .Bush, .Tree, .Carrot };
    for (0..n * n) |step| {
        const want = choices[step % choices.len];
        if (want == .Carrot and farm.getGroundType() != .Soil) {
            farm.till();
        }
        _ = farm.plant(want);
        if (farm.getCompanion()) |c| {
            wishes.put(.{ .x = c.x, .y = c.y }, c.entity) catch unreachable;
        }
        if (farm.getPosY() == n - 1) {
            _ = farm.move(.East);
        }
        _ = farm.move(.North);
    }
    var it = wishes.iterator();
    while (it.next()) |entry| {
        const p = entry.key_ptr.*;
        farm.quickPrint("{d} {d} {any}\n", .{ p.x, p.y, entry.value_ptr.* });
    }
    farm.quickPrint("wishes: {d} first choice: {any}\n", .{ wishes.count(), choices[0] });
}
