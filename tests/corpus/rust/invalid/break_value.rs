// loop as a value.
// ERROR: `break` with a value is not supported in JavaFarmer
// AT: break 5
use farm::*;

fn main() {
    let x = loop {
        break 5;
    };
}
