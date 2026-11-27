// Follow the wall to the treasure, then read the next hint with measure().
use farm::*;

const DIRS: [Direction; 4] = [North, East, South, West];

fn turn_right(d: usize) -> usize {
    (d + 1) % 4
}

fn turn_left(d: usize) -> usize {
    (d + 3) % 4
}

fn main() {
    plant(Entity::Bush);
    while get_entity_type() == Some(Entity::Bush) {
        use_item_with(Item::Weird_Substance, get_world_size());
    }
    let mut dir = 0;
    while get_entity_type() != Some(Entity::Treasure) {
        let right = turn_right(dir);
        if can_move(DIRS[right]) {
            dir = right;
        } else if !can_move(DIRS[dir]) {
            dir = turn_left(dir);
            continue;
        }
        move_to(DIRS[dir]);
    }
    let hint: Option<Position> = measure();
    if let Some((x, y)) = hint {
        quick_print!("next treasure {} {}", x, y);
    }
    harvest();
}
