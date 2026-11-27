// ERROR: use of undeclared identifier 'True'
// AT: True
const farm = @import("farm");

pub fn main() void {
    while (True) {
        _ = farm.harvest();
    }
}
