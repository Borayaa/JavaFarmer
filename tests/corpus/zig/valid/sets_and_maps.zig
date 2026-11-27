//! Sets are AutoHashMap(T, void); maps count harvests per entity.
const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    var seen = std.AutoHashMap(farm.Entity, void).init(farm.allocator);
    defer seen.deinit();
    var counts = std.AutoHashMap(farm.Entity, i32).init(farm.allocator);
    defer counts.deinit();
    for (0..farm.getWorldSize()) |_| {
        if (farm.getEntityType()) |e| {
            seen.put(e, {}) catch unreachable;
            const old = counts.get(e) orelse 0;
            counts.put(e, old + 1) catch unreachable;
        }
        _ = farm.move(.North);
    }
    if (seen.contains(.Bush)) {
        _ = seen.remove(.Bush);
    }
    farm.quickPrint("kinds {d}\n", .{seen.count()});
    var keys = seen.keyIterator();
    while (keys.next()) |k| {
        farm.quickPrint("{any} {d}\n", .{ k.*, counts.get(k.*) orelse 0 });
    }
}
