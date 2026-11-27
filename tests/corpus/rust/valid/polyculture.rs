// Plant each tile's preferred companion; remember wishes in a map keyed by position.
use farm::*;
use std::collections::HashMap;

fn main() {
    let n = get_world_size();
    let mut wishes: HashMap<Position, Entity> = HashMap::new();
    let choices = [Entity::Grass, Entity::Bush, Entity::Tree, Entity::Carrot];
    for step in 0..n * n {
        let want = choices[(step % 4) as usize];
        if want == Entity::Carrot && get_ground_type() != Ground::Soil {
            till();
        }
        plant(want);
        match get_companion() {
            Some((entity, pos)) => {
                wishes.insert(pos, entity);
            }
            None => quick_print!("no companion"),
        }
        if get_pos_y() == n - 1 {
            move_to(East);
        }
        move_to(North);
    }
    for (pos, e) in &wishes {
        quick_print!("{} {} {}", pos.0, pos.1, e);
    }
    quick_print!("wishes: {} first choice: {}", wishes.len(), choices[0].to_string());
}
