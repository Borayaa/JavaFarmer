// `move` is a Rust keyword: the builtin is move_to.
// ERROR: expected one of `|` or `||`, found `(` (the game's move() is called move_to() or r#move() in Rust)
// AT: (
use farm::*;

fn main() {
    move(North);
}
