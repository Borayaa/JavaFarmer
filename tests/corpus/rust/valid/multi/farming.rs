// A library window without main.
use farm::*;

pub fn tend_tile() -> bool {
    if can_harvest() {
        harvest();
    }
    plant(Entity::Bush)
}
