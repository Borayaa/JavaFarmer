// Replant dead pumpkins until the whole field is one big pumpkin.
use farm::*;

fn go_to(x: i32, y: i32) {
    while get_pos_x() != x {
        move_to(East);
    }
    while get_pos_y() != y {
        move_to(North);
    }
}

fn plant_all(dead_x: &mut Vec<i32>, dead_y: &mut Vec<i32>) {
    let n = get_world_size();
    for _ in 0..n {
        for _ in 0..n {
            if get_ground_type() != Ground::Soil {
                till();
            }
            plant(Entity::Pumpkin);
            dead_x.push(get_pos_x());
            dead_y.push(get_pos_y());
            move_to(North);
        }
        move_to(East);
    }
}

fn main() {
    clear();
    let mut dead_x = Vec::new();
    let mut dead_y = Vec::new();
    plant_all(&mut dead_x, &mut dead_y);
    while !dead_x.is_empty() {
        let x = dead_x.remove(0);
        let y = dead_y.remove(0);
        go_to(x, y);
        if get_entity_type() == Some(Entity::Pumpkin) && can_harvest() {
            continue;
        }
        plant(Entity::Pumpkin);
        dead_x.push(x);
        dead_y.push(y);
    }
    harvest();
}
