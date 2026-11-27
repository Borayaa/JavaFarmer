// Every drone farms one column. Closures capture the column number with move.
use farm::*;

fn farm_column(column: i32) {
    for _ in 0..get_world_size() {
        if can_harvest() {
            harvest();
        }
        move_to(North);
    }
    quick_print!("column done {}", column);
}

fn main() {
    clear();
    for col in 0..get_world_size() {
        let drone = spawn_drone(move || farm_column(col));
        if drone.is_none() {
            farm_column(col);
        }
        move_to(East);
    }
    while num_drones() > 1 {
        do_a_flip();
    }
}
