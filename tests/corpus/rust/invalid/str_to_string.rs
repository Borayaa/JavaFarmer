// A string literal is a &str, not a String.
// ERROR: mismatched types: expected `String`, found `&str` (try using a conversion method: `.to_string()`) (E0308)
// AT: "carrot"
use farm::*;

fn main() {
    let name: String = "carrot";
}
