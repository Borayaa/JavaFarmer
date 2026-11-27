// println is a macro.
// ERROR: expected function, found macro `println` (write `println!(...)`) (E0423)
// AT: println
use farm::*;

fn main() {
    println("hello");
}
