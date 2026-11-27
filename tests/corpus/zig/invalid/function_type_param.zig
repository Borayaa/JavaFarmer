// Passing your own functions as values is only supported for farm.spawnDrone.
// ERROR: function types are not supported in JavaFarmer
// AT: fn () void
const farm = @import("farm");

fn everyRow(task: fn () void) void {
    task();
}

fn row() void {
    _ = farm.harvest();
}

pub fn main() void {
    everyRow(row);
}
