// ERROR: expected ';' after statement
// AT: )
const farm = @import("farm");

pub fn main() void {
    farm.till()
    _ = farm.harvest();
}
