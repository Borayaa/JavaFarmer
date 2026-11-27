// char literals.
// ERROR: a `char` literal (use a string "x") is not supported in JavaFarmer
// AT: 'a'
use farm::*;

fn main() {
    let c = 'a';
}
