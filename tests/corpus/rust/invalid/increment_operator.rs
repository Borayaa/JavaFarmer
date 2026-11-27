// Rust has no ++.
// ERROR: Rust has no postfix increment operator (write `x += 1` instead)
use farm::*;

fn main() {
    let mut i = 0;
    i++;
}
