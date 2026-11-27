// The game prints numbers in full.
// ERROR: the precision `{:.2}` (the game prints numbers in full) is not supported in JavaFarmer
// AT: {:.2}
use farm::*;

fn main() {
    println!("{:.2}", get_water());
}
