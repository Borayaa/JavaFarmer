// Vec indices are usize.
// ERROR: the type `[{integer}]` cannot be indexed by `i32` (use `[i as usize]`) (E0277)
// AT: i
use farm::*;

fn main() {
    let v = vec![1, 2, 3];
    let i: i32 = 1;
    let x = v[i];
}
