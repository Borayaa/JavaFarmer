// An if body needs braces.
// ERROR: expected `{`, found `harvest`
// AT: harvest
use farm::*;

fn main() {
    if can_harvest() harvest();
}
