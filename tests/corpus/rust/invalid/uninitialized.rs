// Reading a variable before it has a value.
// ERROR: used binding `x` isn't initialized (E0381)
// AT: x
use farm::*;

fn main() {
    let x: i32;
    quick_print!("{}", x);
}
