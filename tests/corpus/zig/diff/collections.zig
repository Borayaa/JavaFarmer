//! ArrayList, AutoHashMap and sets (AutoHashMap(T, void)); maps are read with get / contains, never iterated
//! (Zig hash maps have no insertion order).
const std = @import("std");
const farm = @import("farm");

pub fn main() !void {
    var list: std.ArrayList(i32) = .empty;
    defer list.deinit(farm.allocator);
    for (0..6) |i| {
        const v: i32 = @intCast(i * i);
        try list.append(farm.allocator, v);
    }
    try list.insert(farm.allocator, 0, -1);
    _ = list.orderedRemove(1);
    const last = list.pop().?;
    std.debug.print("{any} last {d} len {d}\n", .{ list.items, last, list.items.len });
    var squares = std.AutoHashMap(i32, i32).init(farm.allocator);
    defer squares.deinit();
    for (list.items) |v| {
        try squares.put(v, v * v);
    }
    try squares.put(4, 0);
    std.debug.print("{d} {d} {any}\n", .{ squares.count(), squares.get(4) orelse -1, squares.contains(5) });
    var seen = std.AutoHashMap(i32, void).init(farm.allocator);
    defer seen.deinit();
    const items = [_]i32{ 3, 1, 3, 2, 1 };
    var dupes: i32 = 0;
    for (items) |x| {
        if (seen.contains(x)) {
            dupes += 1;
        } else {
            try seen.put(x, {});
        }
    }
    _ = seen.remove(2);
    std.debug.print("dupes {d} unique {d}\n", .{ dupes, seen.count() });
}
