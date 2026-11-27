// Rust has no truthiness: an Option is not a bool.
// ERROR: mismatched types: expected `bool`, found `Option<Entity>` (Rust has no truthiness: use `.is_some()`) (E0308)
// AT: get_entity_type()
use farm::*;

fn main() {
    if get_entity_type() {
        harvest();
    }
}
