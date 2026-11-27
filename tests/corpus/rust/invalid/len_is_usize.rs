// len() returns usize, not i32.
// ERROR: mismatched types: expected `i32`, found `usize` (you can convert with `as i32`) (E0308)
// AT: v.len()
use farm::*;

fn main() {
    let v = vec![1, 2, 3];
    let n: i32 = v.len();
}
