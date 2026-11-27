//! Wear the dinosaur hat and steer toward apples using measure().
const std = @import("std");
const farm = @import("farm");

var path: std.ArrayList(farm.Direction) = .empty;

fn step(d: farm.Direction) bool {
    if (farm.move(d)) {
        path.append(farm.allocator, d) catch unreachable;
        return true;
    }
    return false;
}

pub fn main() void {
    farm.changeHat(.Dinosaur_Hat);
    while (true) {
        const target = farm.measure(?farm.Position);
        if (target == null) {
            break;
        }
        const apple = target.?;
        var moved = false;
        if (farm.getPosX() < apple.x) {
            moved = step(.East);
        } else if (farm.getPosX() > apple.x) {
            moved = step(.West);
        } else if (farm.getPosY() < apple.y) {
            moved = step(.North);
        } else {
            moved = step(.South);
        }
        if (!moved) {
            break;
        }
    }
    farm.quickPrint("steps {d}\n", .{path.items.len});
    farm.changeHat(.Straw_Hat);
}
