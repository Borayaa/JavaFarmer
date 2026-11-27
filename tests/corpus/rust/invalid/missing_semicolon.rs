// A statement without its semicolon.
// ERROR: expected `;`, found `harvest`
// AT: harvest
use farm::*;

fn main() {
    let x = 5
    harvest();
}
