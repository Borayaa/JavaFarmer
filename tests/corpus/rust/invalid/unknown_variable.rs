// A misspelled variable.
// ERROR: cannot find value `totl` in this scope (E0425)
// AT: totl
use farm::*;

fn main() {
    let total = 1;
    quick_print!("{}", totl);
}
