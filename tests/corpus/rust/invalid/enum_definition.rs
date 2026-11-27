// Own enums are not part of the subset.
// ERROR: defining an enum is not supported in JavaFarmer
// AT: enum
use farm::*;

enum Mode {
    Farm,
    Rest,
}

fn main() {
    harvest();
}
