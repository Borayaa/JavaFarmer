// Plant a checkerboard of bushes and grass using the drone position.
use farm::*;

fn main() {
    let size = get_world_size();
    loop {
        for _x in 0..size {
            for _y in 0..size {
                if can_harvest() {
                    harvest();
                }
                if (get_pos_x() + get_pos_y()) % 2 == 0 {
                    plant(Entity::Bush);
                } else {
                    plant(Entity::Grass);
                }
                move_to(North);
            }
            move_to(East);
        }
    }
}
