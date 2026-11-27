// while let only works with v.pop().
// ERROR: this `while let` (only `while let Some(x) = v.pop()` is) is not supported in JavaFarmer
// AT: x
use farm::*;

fn main() {
    let mut x = Some(3);
    while let Some(n) = x {
        x = None;
    }
}
