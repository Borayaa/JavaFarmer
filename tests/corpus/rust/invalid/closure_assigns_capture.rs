// The game has no nonlocal: a closure cannot assign outer variables.
// ERROR: assigning to a captured variable inside a closure (game Python closures cannot assign outer variables; pass the value in and return the result) is not supported in JavaFarmer
// AT: count += 1
use farm::*;

fn main() {
    let mut count = 0;
    let mut tick = || {
        count += 1;
    };
    tick();
}
