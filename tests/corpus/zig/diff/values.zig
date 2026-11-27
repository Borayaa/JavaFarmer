//! Zig value semantics the game's Python lacks: arrays are copied, std.mem.indexOfScalar and list.pop() give
//! null (not -1 / an error), tuples and positions are changed element by element, container-level declarations
//! may be in any order and are comptime-known.
const std = @import("std");
const farm = @import("farm");

const total: i32 = base * 2;
const base: i32 = 21;
const mode: i32 = if (base > 20) 1 else 2;
var counts = [_]i32{ 0, 0, 0 };
var later: i32 = undefined;

fn snapshot() [3]i32 {
    return counts;
}

fn firstAfterBump(a: [3]i32) i32 {
    counts[0] += 100;
    return a[0];
}

pub fn main() !void {
    // arrays: assignment, returns and arguments copy
    var a = [_]i32{ 1, 2, 3 };
    var b = a;
    b[0] = 9;
    a[1] = 5;
    var grid = [_][2]i32{ .{ 1, 2 }, .{ 3, 4 } };
    const row = grid[0];
    var copy = grid;
    grid[0][1] = 7;
    copy[1][0] = 8;
    var snap = snapshot();
    snap[2] = 4;
    counts[1] = 6;
    const first = firstAfterBump(counts);
    std.debug.print("{any} {any} {any} {any} {any}\n", .{ a, b, row, grid, copy });
    std.debug.print("{any} {any} {d}\n", .{ snap, counts, first });
    // std.mem.indexOfScalar: null when missing
    const xs = [_]i32{ 4, 5, 6 };
    if (std.mem.indexOfScalar(i32, &xs, 6)) |i| std.debug.print("6 at {d}\n", .{i});
    const missing = std.mem.indexOfScalar(i32, &xs, 7);
    if (missing == null) std.debug.print("7 missing\n", .{});
    const at = std.mem.indexOfScalar(i32, &xs, 9) orelse 99;
    std.debug.print("{d} {any}\n", .{ at, missing });
    // list.pop(): null on an empty list
    var list: std.ArrayList(i32) = .empty;
    defer list.deinit(farm.allocator);
    try list.append(farm.allocator, 1);
    const p1 = list.pop();
    const p2 = list.pop();
    _ = list.pop();
    const p3 = list.pop() orelse -1;
    if (list.pop() == null) std.debug.print("empty\n", .{});
    std.debug.print("{any} {any} {d}\n", .{ p1, p2, p3 });
    // tuples and positions
    var t: struct { i32, bool } = .{ 1, false };
    t[0] += 2;
    t[1] = true;
    var pos: farm.Position = .{ .x = 1, .y = 2 };
    pos.y = 5;
    std.debug.print("{any} {d} {d}\n", .{ t, pos.x, pos.y });
    later = total + mode;
    std.debug.print("{d}\n", .{later});
}
