// Only std::collections is available.
// ERROR: `use std::io` is not supported in JavaFarmer
// AT: use std::io;
use farm::*;

use std::io;

fn main() {
    harvest();
}
