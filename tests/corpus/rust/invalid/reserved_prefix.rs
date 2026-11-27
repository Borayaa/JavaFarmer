// Names starting with __j are reserved.
// ERROR: identifiers starting with `__j` are reserved by JavaFarmer
// AT: __jcount
use farm::*;

fn main() {
    let __jcount = 1;
}
