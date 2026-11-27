// A float literal for an i32 variable.
// ERROR: mismatched types: expected `i32`, found `{float}` (you can convert with `as i32`) (E0308)
// AT: 0.75
use farm::*;

fn main() {
    let water: i32 = 0.75;
}
