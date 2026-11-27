// String + String: the right side must be a &str.
// ERROR: mismatched types: expected `&str`, found `String` (consider borrowing here: `&`) (E0308)
// AT: b
use farm::*;

fn main() {
    let a = String::from("car");
    let b = String::from("rot");
    let c = a + b;
}
