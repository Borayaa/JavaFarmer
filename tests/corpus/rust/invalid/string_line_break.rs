// The game's strings are one line.
// ERROR: a string with a line break is not supported in JavaFarmer (the game's strings are one line)
use farm::*;

fn main() {
    quick_print!("{}", "two
lines");
}
