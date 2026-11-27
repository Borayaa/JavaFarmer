// get_entity_type returns an Option.
// ERROR: mismatched types: expected `Entity`, found `Option<Entity>` (E0308)
// AT: get_entity_type()
use farm::*;

fn main() {
    let e: Entity = get_entity_type();
}
