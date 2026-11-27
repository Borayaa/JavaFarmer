// Assigning a `let` variable twice needs `let mut`.
// ERROR: cannot assign twice to immutable variable `count` (make it mutable: `let mut count`) (E0384)
// AT: count = count + 1
use farm::*;

fn main() {
    let count = 0;
    count = count + 1;
}
