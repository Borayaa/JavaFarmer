// Measure every sunflower, then harvest the ones with the most petals first.
use farm::*;
use std::collections::HashMap;

fn main() {
    let n = get_world_size();
    let mut by_petals: HashMap<i32, Vec<Position>> = HashMap::new();
    for x in 0..n {
        for y in 0..n {
            if get_ground_type() != Ground::Soil {
                till();
            }
            plant(Entity::Sunflower);
            let petals: i32 = measure();
            let mut list = by_petals.get(&petals).cloned().unwrap_or(Vec::new());
            list.push((x, y));
            by_petals.insert(petals, list);
            move_to(North);
        }
        move_to(East);
    }
    for petals in (7..=15).rev() {
        if !by_petals.contains_key(&petals) {
            continue;
        }
        for &(x, y) in &by_petals[&petals] {
            quick_print!("petals {} at {} {}", petals, x, y);
        }
    }
    println!("sizes: {}", by_petals.len());
}
