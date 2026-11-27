//! Optionals: ?farm.Entity from getEntityType, if-captures, orelse and .? unwrapping.
const std = @import("std");
const farm = @import("farm");

fn entityOr(fallback: farm.Entity) farm.Entity {
    const here = farm.getEntityType();
    const e = here orelse fallback;
    return e;
}

fn findTall() ?i32 {
    const h = farm.measure(i32);
    if (h > 5) {
        return h;
    }
    return null;
}

pub fn main() void {
    if (farm.getEntityType()) |e| {
        if (e == .Grass) {
            _ = farm.harvest();
        }
    } else {
        _ = farm.plant(.Bush);
    }
    const kind = entityOr(.Grass);
    farm.quickPrint("{any}\n", .{kind});
    const tall = findTall();
    if (tall != null) {
        farm.quickPrint("tall {d}\n", .{tall.?});
    }
    const drone = farm.spawnDrone(work) orelse return;
    while (!farm.hasFinished(drone)) {
        farm.doAFlip();
    }
}

fn work() void {
    _ = farm.harvest();
}
