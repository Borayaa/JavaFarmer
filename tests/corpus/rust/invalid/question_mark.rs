// The ? operator.
// ERROR: the `?` operator is not supported in JavaFarmer
// AT: ?
use farm::*;

fn main() {
    let e = get_entity_type()?;
}
