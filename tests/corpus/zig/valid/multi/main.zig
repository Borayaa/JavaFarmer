//! Uses helpers from two other files.
const farm = @import("farm");
const utils = @import("utils.zig");
const farming = @import("farming.zig");

pub fn main() void {
    utils.goTo(0, 0);
    const n = utils.size();
    for (0..n) |_| {
        farming.tendTile();
        utils.moves += 1;
        _ = farm.move(.North);
    }
    farm.quickPrint("moves: {d} planted: {d}\n", .{ utils.moves, farming.planted });
    utils.moves = 0;
    _ = farm.spawnDrone(farming.tendTile);
}
