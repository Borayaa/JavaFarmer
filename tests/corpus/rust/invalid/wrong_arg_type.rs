// An Item where an Entity is expected.
// ERROR: mismatched types: expected `Entity`, found `Item` (E0308)
// AT: Item::Hay
use farm::*;

fn main() {
    plant(Item::Hay);
}
