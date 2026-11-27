// match (statement and value), loop with break, while, ranges with rev() and step_by().
use farm::*;

fn score(e: Option<Entity>) -> i32 {
    match get_ground_type() {
        Ground::Soil => 2,
        _ => {
            if e == Some(Entity::Grass) {
                1
            } else {
                0
            }
        }
    }
}

fn main() {
    let mut moves = 0;
    loop {
        move_to(East);
        moves += 1;
        if get_pos_x() == 0 {
            break;
        }
    }
    for i in 0..moves {
        if i % 2 == 1 {
            continue;
        }
        let e = get_entity_type();
        let kind = match moves {
            1 => "tiny",
            2 | 3 => "small",
            _ => {
                quick_print!("big farm");
                "big"
            }
        };
        quick_print!("{} {}", kind, score(e));
    }
    let mut count = 0;
    for i in (1..=10).rev().step_by(3) {
        count += if i > 5 { 2 } else { 1 };
    }
    for i in (0..20).step_by(5) {
        count += i;
    }
    quick_print!("{}", count);
}
