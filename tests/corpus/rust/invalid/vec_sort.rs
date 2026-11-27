// Vec::sort has no game Python twin.
// ERROR: the method `.sort()` on `Vec<{integer}>` is not supported in JavaFarmer
// AT: sort
use farm::*;

fn main() {
    let mut v = vec![3, 1, 2];
    v.sort();
}
