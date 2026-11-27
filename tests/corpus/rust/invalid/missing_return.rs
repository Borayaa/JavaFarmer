// A function whose last expression is missing.
// ERROR: mismatched types: expected `i32`, found `()` (E0308)
use farm::*;

fn best() -> i32 {
    if can_harvest() {
        return 1;
    }
}

fn main() {
    best();
}
