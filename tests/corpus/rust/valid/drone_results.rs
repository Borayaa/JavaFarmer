// Drones return a value; the main drone waits for all of them.
use farm::*;

fn count_carrots() -> i32 {
    let mut found = 0;
    for _ in 0..get_world_size() {
        if get_entity_type() == Some(Entity::Carrot) {
            found += 1;
        }
        move_to(North);
    }
    found
}

fn main() {
    let mut drones = Vec::new();
    for _ in 0..max_drones() - 1 {
        if let Some(d) = spawn_drone(count_carrots) {
            drones.push(d);
        }
        move_to(East);
    }
    let mut total = 0;
    for d in drones {
        total += wait_for(d);
    }
    let doubled = |n: i32| n * 2;
    let report = move || println!("carrots: {}", total);
    report();
    quick_print!("{}", doubled(total));
}
