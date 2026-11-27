// Rust never converts numbers implicitly.
// ERROR: cannot multiply `i32` by `{float}` (E0277)
// AT: *
use farm::*;

fn main() {
    let n = get_world_size();
    let half = n * 0.5;
}
