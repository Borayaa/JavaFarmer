// ERROR: 'main' is not marked 'pub'
// AT: main
const farm = @import("farm");

fn main() void {
    _ = farm.harvest();
}
