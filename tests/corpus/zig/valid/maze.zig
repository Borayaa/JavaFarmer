//! Follow the right-hand wall through a maze to the treasure.
const farm = @import("farm");

const dirs = [_]farm.Direction{ .North, .East, .South, .West };

fn turnRight(d: usize) usize {
    return (d + 1) % 4;
}

fn turnLeft(d: usize) usize {
    return (d + 3) % 4;
}

fn isTreasure() bool {
    const here = farm.getEntityType();
    return here != null and here.? == .Treasure;
}

pub fn main() void {
    _ = farm.plant(.Bush);
    while (farm.getEntityType() == null or farm.getEntityType().? == .Bush) {
        _ = farm.useItemN(.Weird_Substance, farm.getWorldSize());
    }
    var dir: usize = 0;
    while (!isTreasure()) {
        const right = turnRight(dir);
        if (farm.canMove(dirs[right])) {
            dir = right;
        } else if (!farm.canMove(dirs[dir])) {
            dir = turnLeft(dir);
            continue;
        }
        _ = farm.move(dirs[dir]);
    }
    const hint = farm.measure(?farm.Position);
    if (hint) |next| {
        farm.quickPrint("next treasure {d} {d}\n", .{ next.x, next.y });
    }
    _ = farm.harvest();
}
