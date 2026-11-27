// Zig has no elif: write } else if (...) {. Here elif (...) { reads as a call followed by an initializer.
// ERROR: expected ',' after initializer
// AT: ;
const farm = @import("farm");

pub fn main() void {
    const x = farm.getPosX();
    if (x == 0) {
        _ = farm.harvest();
    }
    elif (x == 1) {
        farm.till();
    }
}
