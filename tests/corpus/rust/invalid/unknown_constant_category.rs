// A typo in the enum name.
// ERROR: failed to resolve: use of undeclared type `Entities` (the game's Entities are `Entity::Carrot` in Rust) (E0433)
// AT: Entities
use farm::*;

fn main() {
    plant(Entities::Carrot);
}
