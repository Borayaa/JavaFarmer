// Remember visited tiles in a HashSet and counts in a HashMap.
use farm::*;
use std::collections::{HashMap, HashSet};

fn main() {
    let mut visited: HashSet<Position> = HashSet::new();
    let mut counts: HashMap<Entity, i32> = HashMap::new();
    let n = get_world_size();
    for _ in 0..n {
        let pos = (get_pos_x(), get_pos_y());
        if visited.contains(&pos) {
            break;
        }
        visited.insert(pos);
        if let Some(e) = get_entity_type() {
            *counts.entry(e).or_insert(0) += 1;
        }
        move_to(North);
    }
    for (e, c) in &counts {
        quick_print!("{} {}", e, c);
    }
    let carrots = *counts.get(&Entity::Carrot).unwrap_or(&0);
    quick_print!("visited {} carrots {}", visited.len(), carrots);
    counts.remove(&Entity::Grass);
    counts.clear();
}
