// Returning a &str from a fn that returns i32.
// ERROR: mismatched types: expected `i32`, found `&str` (E0308)
// AT: "big"
use farm::*;

fn size() -> i32 {
    "big"
}

fn main() {
    size();
}
