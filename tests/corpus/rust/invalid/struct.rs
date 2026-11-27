// Structs are not part of the JavaFarmer subset.
// ERROR: defining a struct is not supported in JavaFarmer
// AT: struct
use farm::*;

struct Plot {
    x: i32,
}

fn main() {
    harvest();
}
