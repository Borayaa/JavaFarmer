// Keep the soil wet and fertilise when there is plenty.
use farm::*;

fn water_if_dry(min_water: f64) {
    if get_water() < min_water && num_items(Item::Water) >= 1.0 {
        use_item(Item::Water);
    }
}

fn main() {
    let rounds = 3;
    for _ in 0..rounds {
        for _ in 0..get_world_size() {
            water_if_dry(0.5);
            if num_items(Item::Fertilizer) > 10.0 {
                use_item_with(Item::Fertilizer, 2);
            }
            move_to(North);
        }
    }
}
