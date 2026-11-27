// Iterator adapters outside for loops.
// ERROR: iterator methods outside a `for` loop (`.iter()`) is not supported in JavaFarmer
// AT: iter
use farm::*;

fn main() {
    let v = vec![1, 2, 3];
    let doubled: Vec<i32> = v.iter().map(|x| x * 2).collect();
}
