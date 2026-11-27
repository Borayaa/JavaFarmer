// Trees on a checkerboard, carrots in between, water when dry.
use farm::*;

const WATER_LEVEL: f64 = 0.75;

fn tend() {
    if let Some(_here) = get_entity_type() {
        if can_harvest() {
            harvest();
        }
    }
    if get_water() < WATER_LEVEL && num_items(Item::Water) > 0.0 {
        use_item(Item::Water);
    }
    if (get_pos_x() + get_pos_y()) % 2 == 0 {
        plant(Entity::Tree);
    } else {
        if get_ground_type() != Ground::Soil {
            till();
        }
        plant(Entity::Carrot);
    }
}

fn main() {
    clear();
    loop {
        for _ in 0..get_world_size() * get_world_size() {
            tend();
            if get_pos_y() == get_world_size() - 1 {
                move_to(East);
            }
            move_to(North);
        }
    }
}
