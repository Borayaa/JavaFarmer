// Booleans are lowercase.
// ERROR: cannot find value `True` in this scope (Rust writes `true`) (E0425)
// AT: True
use farm::*;

fn main() {
    let done = True;
}
