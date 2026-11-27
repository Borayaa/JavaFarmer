// Loop labels.
// ERROR: a loop label is not supported in JavaFarmer
// AT: 'outer:
use farm::*;

fn main() {
    'outer: loop {
        break 'outer;
    }
}
