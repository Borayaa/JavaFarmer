//! switch as a statement and as an expression, while with a continue expression, if expressions.
const farm = @import("farm");

fn score(e: farm.Entity) i32 {
    switch (farm.getGroundType()) {
        .Soil => return 2,
        else => return if (e == .Grass) 1 else 0,
    }
}

pub fn main() void {
    var moves: i32 = 0;
    while (true) {
        _ = farm.move(.East);
        moves += 1;
        if (farm.getPosX() == 0) break;
    }
    var i: i32 = 0;
    while (i < moves) : (i += 1) {
        if (@mod(i, 2) == 1) {
            continue;
        }
        const kind: []const u8 = switch (moves) {
            1 => "tiny",
            2, 3 => "small",
            else => "big",
        };
        if (moves > 3) {
            farm.quickPrint("big farm\n", .{});
        }
        farm.quickPrint("{s} {d}\n", .{ kind, score(.Grass) });
    }
    var count: i32 = 0;
    var j: i32 = 10;
    while (j > 0) : (j -= 3) {
        count += if (j > 5) 2 else 1;
    }
    farm.quickPrint("{d}\n", .{count});
}
