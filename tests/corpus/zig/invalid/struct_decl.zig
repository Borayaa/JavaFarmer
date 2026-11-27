// ERROR: struct declarations are not supported in JavaFarmer
// AT: struct { x: i32, y: i32 }
const farm = @import("farm");

const Point = struct { x: i32, y: i32 };

pub fn main() void {
    const p = Point{ .x = 1, .y = 2 };
    _ = p;
    _ = farm.harvest();
}
