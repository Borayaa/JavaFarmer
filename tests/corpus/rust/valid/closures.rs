// Closures and functions as values for drones.
use farm::*;

fn for_all(f: fn() -> bool) {
    let row = move || {
        for _ in 0..get_world_size() - 1 {
            f();
            move_to(East);
        }
        f();
    };
    for _ in 0..get_world_size() {
        if spawn_drone(row).is_none() {
            row();
        }
        move_to(North);
    }
}

fn main() {
    for_all(harvest);
    for dir in [North, East, South, West] {
        let task = move || {
            move_to(dir);
            do_a_flip();
        };
        spawn_drone(task);
    }
}
