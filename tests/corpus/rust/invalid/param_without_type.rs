// Function parameters need a type.
// ERROR: expected `:` (parameters need a type, e.g. `n: i32`), found `)`
// AT: )
use farm::*;

fn go(steps) {
    harvest();
}

fn main() {
    go(3);
}
