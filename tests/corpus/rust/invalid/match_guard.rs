// match guards.
// ERROR: a `match` guard (`if` after the pattern) is not supported in JavaFarmer
// AT: x > 5
use farm::*;

fn main() {
    let n = get_world_size();
    match n {
        x if x > 5 => quick_print!("big"),
        _ => quick_print!("small"),
    }
}
