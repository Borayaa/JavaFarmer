// A misspelled builtin.
// ERROR: cannot find function `harvst` in this scope (E0425)
// AT: harvst
use farm::*;

fn main() {
    harvst();
}
