// Lifetime parameters.
// ERROR: a generic function is not supported in JavaFarmer
// AT: <
use farm::*;

fn first<'a>(a: &'a str) -> &'a str {
    a
}

fn main() {
    quick_print!("{}", first("x"));
}
