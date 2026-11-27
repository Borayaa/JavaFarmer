const farm = @import("farm");

pub var moves: i32 = 0;
const secret = 42;

pub fn size() usize {
    return farm.getWorldSize();
}

pub fn goTo(x: usize, y: usize) void {
    while (farm.getPosX() != x) {
        _ = farm.move(.West);
        moves += 1;
    }
    while (farm.getPosY() != y) {
        _ = farm.move(.South);
        moves += 1;
    }
    if (moves > secret) {
        farm.quickPrint("long trip\n", .{});
    }
}
