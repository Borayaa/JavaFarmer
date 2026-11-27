// Comparing i32 with f64.
// ERROR: mismatched types: expected `i32`, found `f64` (E0308)
// AT: get_water()
use farm::*;

fn main() {
    if get_pos_x() < get_water() {
        harvest();
    }
}
